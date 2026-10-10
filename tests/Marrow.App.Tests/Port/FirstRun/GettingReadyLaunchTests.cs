using Avalonia.Controls;
using Avalonia.VisualTree;
using Marrow.App.Port;
using Marrow.App.Port.FirstRun;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views;
using Marrow.App.Views.Composer;
using Marrow.App.Window;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Common;
using Marrow.UI.Port;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Port.FirstRun;

// GR-2: the startup hook. The sheet is shown only on the first run or when the prepared state is invalid (OD-0096: "Later launches
// skip it"), is in place from the first frame of the window (the window's own reveal is its entrance: nothing cuts), is driven by
// GR-1's FirstRunPreparation and by nothing else, and never holds the app longer than the work: the run waits for the sheet,
// and the sheet waits for nothing but the work, then a 0.15 s hold and the 1.1 s lift.
[Collection("Headless")]
public sealed class GettingReadyLaunchTests(HeadlessSession headless)
{
    private static GettingReadyScripted Preparation(bool prepared, Func<IProgress<GpuWarmupStep>, Task>? warm = null, bool[]? flip = null)
    {
        var log = new List<string>();
        var registry = new List<GpuProgram>
        {
            GpuProgram.Draw("k1", FirstRunSteps.Knob, _ => { }), GpuProgram.Draw("l1", FirstRunSteps.Lights, _ => { }),
            GpuProgram.Draw("liquid", FirstRunSteps.Liquid, _ => { }), GpuProgram.Draw("strike", FirstRunSteps.Strike, _ => { }),
        };
        var seams = new FirstRunPreparation.Seams(() => { log.Add("asked"); return flip is { } f ? f[0] : prepared; }, () => registry, _ => Task.CompletedTask,
            (window, progress, _) =>
            {
                log.Add("warm");
                if (warm is not null) return warm(progress);
                foreach (var (name, index) in new[] { "k1", "l1", "liquid", "strike" }.Select((name, index) => (name, index)))
                    progress.Report(new GpuWarmupStep(index, 4, name, 1, false, "step"));
                return Task.CompletedTask;
            });
        return new GettingReadyScripted(new FirstRunPreparation(seams, TimeProvider.System), log, () => prepared);
    }

    private static (AvaloniaWindow Window, RootView Root) Host(AppModel model, RootView root)
    {
        var host = new Border();
        AppEnvironment.SetAppModel(host, model);
        host.Child = root;
        var window = new AvaloniaWindow { Width = 960, Height = 680, Content = host };
        window.Show();
        for (var pass = 0; pass < 3; pass++)
        {
            window.UpdateLayout();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        }
        return (window, root);
    }

    private static void Frames(ManualFrameSource frames, ManualTimeProvider time, double seconds)
    {
        for (var index = 0; index < (int)Math.Round(seconds * GettingReadyRig.Hz); index++)
        {
            var by = TimeSpan.FromSeconds(1 / GettingReadyRig.Hz);
            time.Advance(by);
            frames.Advance(by);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        }
    }

    [Fact] // GR-2.launch
    [Trait("Decision", "ED-288")]
    public void When_the_machine_is_not_prepared_the_root_view_is_built_with_the_sheet_over_it_listing_the_plan() => headless.OnUiThread(() =>
    {
        var scripted = Preparation(prepared: false);
        var launch = new GettingReadyLaunch(scripted.Preparation);
        var content = launch.Wrap(() => new RootView())();
        var root = Assert.IsType<RootView>(content);
        Assert.NotNull(launch.Sheet);
        Assert.Same(launch.Sheet, root.GettingReady);
        Assert.Equal(scripted.Preparation.Plan().Select(step => step.Step.Id), launch.Sheet!.Rows.Select(row => row.Id));
        Assert.False(launch.Done.IsCompleted);
    });

    [Fact] // GR-2.launch
    [Trait("Decision", "ED-288")]
    public void When_the_machine_is_prepared_no_sheet_is_made_and_nothing_runs() => headless.OnUiThread(() =>
    {
        var scripted = Preparation(prepared: true);
        var launch = new GettingReadyLaunch(scripted.Preparation);
        var content = launch.Wrap(() => new RootView())();
        var root = Assert.IsType<RootView>(content);
        Assert.Null(launch.Sheet);
        Assert.Null(root.GettingReady);
        Assert.Null(root.DetailPane.Effect);
        Assert.Equal(1, root.DetailPane.Opacity);
        Assert.True(launch.Done.IsCompletedSuccessfully);
        var run = launch.RunAsync(null);
        Assert.True(run.IsCompletedSuccessfully);
        Assert.DoesNotContain("warm", scripted.Log);
    });

    [Fact] // GR-2.launch
    [Trait("Decision", "ED-288")]
    public void Content_that_is_not_the_root_view_is_left_alone_and_the_prepared_state_is_not_even_read() => headless.OnUiThread(() =>
    {
        var scripted = Preparation(prepared: false);
        var launch = new GettingReadyLaunch(scripted.Preparation);
        var stand = new Border();
        Assert.Same(stand, launch.Wrap(() => stand)());
        Assert.Null(launch.Sheet);
        Assert.True(launch.Done.IsCompletedSuccessfully);
        Assert.Empty(scripted.Log);
    });

    // A launch driven by hand: the body runs on the main actor with the frame clock and clock the test moves; the run's own
    // continuations (the task that completes when the sheet has lifted) are posted to the main actor and run when the body returns.
    private static void Driven(HeadlessSession headless, GettingReadyScripted scripted, Action<GettingReadyLaunch, RootView, ManualFrameSource, ManualTimeProvider> during,
        Action<GettingReadyLaunch, RootView> after)
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            var launch = new GettingReadyLaunch(scripted.Preparation);
            AvaloniaWindow window = null!;
            RootView root = null!;
            try
            {
                harness.Main.OnMain(() =>
                {
                    var frames = new ManualFrameSource();
                    var time = new ManualTimeProvider();
                    using var frameScope = FrameClock.Source.Override(frames);
                    using var clockScope = SwiftClock.Provider.Override(time);
                    (window, root) = Host(model, (RootView)launch.Wrap(() => new RootView())());
                    during(launch, root, frames, time);
                });
                harness.Main.OnMain(() => { });
                after(launch, root);
            }
            finally { harness.Main.OnMain(() => window?.Close()); }
        });
    }

    [Fact] // GR-2.launch
    [Trait("Decision", "ED-288")]
    public void The_run_happens_behind_the_sheet_and_the_launch_is_done_when_the_sheet_has_lifted()
    {
        var scripted = Preparation(prepared: false);
        Task<FirstRunOutcome> run = null!;
        Driven(headless, scripted, (launch, root, frames, time) =>
        {
            run = launch.RunAsync(TopLevel.GetTopLevel(root));
            Assert.Contains("warm", scripted.Log);
            Assert.False(run.IsCompleted);
            Assert.False(launch.Done.IsCompleted); // the sheet is still up: the tour and the rest of the launch wait
            Frames(frames, time, 0.1);
            Assert.Equal(GettingReadyPhase.Ready, launch.Sheet!.Phase); // the hold
            Assert.All(launch.Sheet.Rows, row => Assert.Equal(FirstRunStepState.Done, row.State));
            Frames(frames, time, 1.4);
            Assert.Null(root.GettingReady);
            Assert.Null(root.DetailPane.Effect);
        }, (launch, _) =>
        {
            Assert.True(run.IsCompletedSuccessfully);
            Assert.Equal(FirstRunOutcomeKind.Prepared, run.Result.Kind);
            Assert.True(launch.Done.IsCompletedSuccessfully);
        });
    }

    [Fact] // GR-2.launch
    [Trait("Decision", "ED-288")]
    public void A_run_that_fails_still_lifts_the_sheet_without_saying_Ready()
    {
        var scripted = Preparation(prepared: false, warm: _ => throw new InvalidOperationException("a program did not draw"));
        Task<FirstRunOutcome> run = null!;
        Driven(headless, scripted, (launch, root, frames, time) =>
        {
            run = launch.RunAsync(TopLevel.GetTopLevel(root));
            Frames(frames, time, 0.3);
            Assert.Equal(GettingReadyFixture.Title, launch.Sheet!.TitleWord);
            Frames(frames, time, 1.2);
            Assert.Null(root.GettingReady);
            Assert.Equal(1, root.DetailPane.Opacity);
        }, (launch, _) =>
        {
            Assert.True(run.IsCompletedSuccessfully);
            Assert.Equal(FirstRunOutcomeKind.Failed, run.Result.Kind);
            Assert.True(launch.Done.IsCompletedSuccessfully);
        });
    }

    [Fact] // GR-2.launch
    [Trait("Decision", "ED-288")]
    public void A_machine_found_prepared_after_the_sheet_was_made_just_lifts_it_and_runs_nothing()
    {
        var flip = new[] { false };
        var scripted = Preparation(prepared: false, flip: flip);
        Task<FirstRunOutcome> run = null!;
        Driven(headless, scripted, (launch, root, frames, time) =>
        {
            flip[0] = true; // another launch's idle fallback wrote the marker in between
            run = launch.RunAsync(TopLevel.GetTopLevel(root));
            Frames(frames, time, 1.3);
            Assert.Null(root.GettingReady);
        }, (launch, _) =>
        {
            Assert.True(run.IsCompletedSuccessfully);
            Assert.DoesNotContain("warm", scripted.Log);
            Assert.True(launch.Done.IsCompletedSuccessfully);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Decision", "ED-288")]
    public void A_boot_shell_accepts_the_real_root_with_the_preparation_sheet_already_wrapped(bool prepared) => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        harness.Main.OnMain(() =>
        {
            var manager = new WindowManager();
            var shell = manager.PrepareMainShell();
            try
            {
                Assert.Null(shell.Content);
                Assert.False(manager.HasCreatedModel);
                manager.MainContent = () => new RootView();
                var launch = GettingReadyLaunch.Arm(manager, Preparation(prepared).Preparation);
                Assert.Same(shell, manager.PrepareMain());
                var root = LogicalQuery.SelfAndDescendants<RootView>((Control)shell.Content!).Single();
                Assert.Same(launch.Sheet, root.GettingReady);
                Assert.Equal(prepared, root.GettingReady is null);
                Assert.Same(manager.Model, AppEnvironment.GetAppModel(root));
            }
            finally { shell.Close(); manager.Model.StopPaletteChecks(); }
        });
    });

    [Fact] // GR-2.launch
    [Trait("Decision", "ED-288")]
    public void Arm_puts_the_hook_in_front_of_the_window_managers_main_content() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var manager = harness.Main.OnMain(() => new WindowManager());
        var scripted = Preparation(prepared: false);
        manager.MainContent = () => new RootView();
        var launch = GettingReadyLaunch.Arm(manager, scripted.Preparation);
        var content = Assert.IsType<RootView>(manager.MainContent());
        Assert.Same(launch.Sheet, content.GettingReady);
        Assert.NotNull(launch.Sheet);
    });
}
