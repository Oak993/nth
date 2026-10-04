using System.Reflection;
using Marrow.App.Views.Composer;
using Marrow.App.Port.FirstRun;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Port.FirstRun;

// GR-2: the startup hook in AppDelegate.ApplicationDidFinishLaunching. The sheet is armed before the main window is made (so it is
// in the window's first frame), runs once the window is shown, and the first-run tour (a window of its own over the main one,
// AppDelegate.swift:24-26) waits for it: it would sit over the sheet.
[Collection("Headless")]
public sealed class GettingReadyAppDelegateTests(HeadlessSession headless)
{
    [Fact] // GR-2.startup
    [Trait("Decision", "ED-288")]
    public void Launch_arms_the_sheet_after_the_model_and_before_the_boot_makes_the_main_window()
    {
        var members = CompiledLaunchOrder.Members(typeof(AppDelegate).GetMethod(nameof(AppDelegate.ApplicationDidFinishLaunching))!).ToList();
        var model = members.FindIndex(member => member is MethodBase && member.Name == "set_Model");
        var arm = members.FindIndex(member => member.DeclaringType == typeof(GettingReadyLaunch) && member.Name == "Arm");
        var boot = members.FindIndex(member => member is MethodBase && member.Name == "get_StartBoot");
        Assert.True(model >= 0 && arm > model && boot > arm, $"set_Model {model}, Arm {arm}, get_StartBoot {boot}");
    }

    [Fact] // GR-2.startup
    [Trait("Decision", "ED-288")]
    public void A_first_run_shows_the_sheet_in_the_main_window_and_the_tour_waits_for_it() =>
        LaunchSurfaceRig.WithLaunch(headless, null, launch =>
        {
            var frames = new ManualFrameSource();
            launch.Harness.Main.OnMain(() =>
            {
                using var scope = FrameClock.Source.Override(frames);
                var registry = new List<GpuProgram> { GpuProgram.Draw("k1", FirstRunSteps.Knob, _ => { }), GpuProgram.Draw("strike", FirstRunSteps.Strike, _ => { }) };
                launch.Delegate.FirstRun = new FirstRunPreparation(new FirstRunPreparation.Seams(() => false, () => registry, _ => Task.CompletedTask,
                    (_, progress, _) =>
                    {
                        progress.Report(new GpuWarmupStep(0, 2, "k1", 1, false, "knob"));
                        progress.Report(new GpuWarmupStep(1, 2, "strike", 1, false, "strike"));
                        return Task.CompletedTask;
                    }), TimeProvider.System);
                launch.Manager.MainContent = () => new RootView();
                launch.Delegate.ApplicationDidFinishLaunching();
                var root = FindRoot(launch.Manager.MainWindow!);
                Assert.NotNull(root.GettingReady);
                launch.Harness.Main.AdvanceMilliseconds(700);
                Assert.DoesNotContain("tour", launch.Events); // the sheet is up: the tour does not open over it
                for (var index = 0; index < 120; index++)
                {
                    var by = TimeSpan.FromSeconds(1 / GettingReadyRig.Hz);
                    launch.Harness.Main.Advance(by);
                    frames.Advance(by);
                    Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                }
                Assert.Null(root.GettingReady); // lifted
                Assert.DoesNotContain("tour", launch.Events);
                launch.Harness.Main.AdvanceMilliseconds(700);
                Assert.Contains("tour", launch.Events); // 700 ms after the sheet has gone (AppDelegate.swift:25, counted from when the window is clear)
            });
        });

    private static RootView FindRoot(Avalonia.Controls.Window window) =>
        Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(window).OfType<RootView>().Single();
}
