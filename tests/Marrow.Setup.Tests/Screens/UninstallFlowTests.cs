using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Marrow.Setup.Flow;
using Marrow.Setup.Screens;
using Marrow.Setup.Support;
using Marrow.Setup.Tests.Fixtures;
using Marrow.Setup.Text;
using Marrow.Setup.Ui;

namespace Marrow.Setup.Tests.Screens;

// PORT-ONLY (no Swift twin): nth installer PLAN.md 3, 3b and 8 and the canvas's Uninstall, unit U6 (ED-NEW-inst-u6-1).
// Headless, on the window's own clock, with a scripted removal (nothing is removed): Uninstall passes the switch's state and
// shows Removing at once, which stays until both the removal has ended and it has been shown for its shortest time, then
// "nth was removed" with one primary Close that works again; Cancel, Enter and Esc cancel without removing (exit 0); a double
// click removes once; a failure shows Problem worded for removing with its safe or left ending, and its Retry comes back
// already removing (as Removing looks, nothing revealed on arrival) and removes again with the same choice; a removal that
// throws says files may be left; the end comes back through the dispatcher by default. Frame capture: ten fast toggles, then
// Uninstall while the last reveal is in flight, through Removing to Removed, never move anything more than 3 DIP in a frame,
// never fade anything more than 35 % in a frame, never overlap the buttons, never move them while they show, and end at rest.
[Collection("SetupHeadless")]
public sealed class UninstallFlowTests(SetupHeadlessSession headless)
{
    private static readonly UninstallReport Removed = new(UninstallOutcome.Removed, RootGone: true, Leftovers: []);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Decision", "OD-0072")]
    public void Uninstall_passes_the_switch_and_Removing_stays_until_the_removal_and_its_shortest_time_are_both_done(bool wipe)
    {
        headless.OnUiThread(() =>
        {
            var calls = new List<bool>();
            var removal = new TaskCompletionSource<UninstallReport>();
            var (rig, session, screen, flow) = Open(w => { calls.Add(w); return removal.Task; });
            if (wipe)
            {
                screen.Switch.Toggle();
                rig.Run(0.6);
            }

            screen.Remove.Invoke();
            Assert.Equal([wipe], calls);
            Assert.Equal(UninstallState.Removing, screen.State);
            Assert.True(flow.Busy);
            Assert.True(rig.Window.Guard.IsPaused);
            removal.SetResult(Removed); // a fast removal: done at once
            rig.Run(UninstallFlow.MinimumRemoving - 0.2);
            Assert.Equal(UninstallState.Removing, screen.State); // never a flash of the words
            Assert.Equal(SetupStrings.UninstallRemoving, screen.TitleBlock.Text);
            Assert.Equal(wipe ? SetupStrings.UninstallRemovingWipe : SetupStrings.UninstallRemovingKeep, screen.SubtitleBlock.Text);
            Assert.True(rig.Window.Guard.IsPaused);
            rig.Run(0.3);
            Assert.Equal(UninstallState.Removed, screen.State);
            Assert.False(flow.Busy);
            Assert.Same(Removed, flow.Report);
            rig.Run(1.2);
            Assert.Equal(SetupStrings.UninstallRemoved, screen.TitleBlock.Text);
            Assert.Equal(wipe ? SetupStrings.UninstallRemovedWipe : SetupStrings.UninstallRemovedKeep, screen.SubtitleBlock.Text);
            Assert.False(rig.Window.Guard.IsPaused);
            Assert.True(screen.Cancel.IsVisible);
            Assert.False(screen.Remove.IsVisible);
            Assert.Equal(SetupStrings.UninstallClose, screen.Cancel.Label);
            Assert.Equal(ButtonKind.Primary, screen.Cancel.Kind);

            screen.Cancel.Invoke();
            Assert.False(rig.Window.IsVisible);
            Assert.Equal(0, session.ExitCode);
            Assert.Equal(1, flow.Attempts);
        });
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void A_slow_removal_keeps_Removing_and_Close_paused_until_it_ends()
    {
        headless.OnUiThread(() =>
        {
            var removal = new TaskCompletionSource<UninstallReport>();
            var (rig, _, screen, flow) = Open(_ => removal.Task);

            screen.Remove.Invoke();
            rig.Run(4);
            Assert.Equal(UninstallState.Removing, screen.State);
            Assert.True(screen.Bar.IsVisible);
            Assert.True(rig.Window.Guard.IsPaused);
            Assert.True(screen.Hold!.IsVisible);
            Esc(rig);
            Assert.True(rig.Window.IsVisible); // nothing cancels a removal under way

            removal.SetResult(Removed);
            Assert.Equal(UninstallState.Removed, screen.State);
            Assert.False(flow.Busy);
            rig.Window.CloseNow();
        });
    }

    [Theory]
    [InlineData("enter")]
    [InlineData("escape")]
    [InlineData("cancel")]
    [Trait("Decision", "OD-0072")]
    public void Cancel_Enter_and_Esc_cancel_without_removing_anything(string how)
    {
        headless.OnUiThread(() =>
        {
            var calls = 0;
            var (rig, session, screen, flow) = Open(_ => { calls++; return Task.FromResult(Removed); });
            screen.Switch.Toggle(); // the switch on changes nothing about cancelling
            rig.Run(0.6);
            Assert.True(screen.Cancel.IsFocused);

            switch (how)
            {
                case "enter": Press(rig, Key.Enter); break;
                case "escape": Esc(rig); break;
                default: screen.Cancel.Invoke(); break;
            }

            Assert.False(rig.Window.IsVisible);
            Assert.Equal(0, calls);
            Assert.Equal(0, flow.Attempts);
            Assert.Equal(0, session.ExitCode);
        });
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void A_double_click_removes_once_and_the_switch_does_nothing_while_removing()
    {
        headless.OnUiThread(() =>
        {
            var calls = new List<bool>();
            var removal = new TaskCompletionSource<UninstallReport>();
            var (rig, _, screen, flow) = Open(w => { calls.Add(w); return removal.Task; });

            screen.Remove.Invoke();
            screen.Remove.Invoke();
            rig.Run(0.05);
            screen.Remove.Invoke();
            screen.Switch.Toggle();
            rig.Run(0.6);
            Assert.Equal([false], calls);
            Assert.Equal(1, flow.Attempts);
            Assert.False(screen.Warning.IsVisible);
            Assert.Equal(SetupStrings.UninstallRemovingKeep, screen.SubtitleBlock.Text);
            removal.SetResult(Removed);
            rig.Run(1);
            Assert.Equal([false], calls);
            rig.Window.CloseNow();
        });
    }

    [Theory]
    [InlineData(UninstallOutcome.DidNotStart, true)]
    [InlineData(UninstallOutcome.NoUninstaller, true)]
    [InlineData(UninstallOutcome.VelopackFailed, false)]
    [Trait("Decision", "OD-0072")]
    public void A_failure_shows_Problem_worded_for_removing_and_Retry_removes_again_with_the_same_choice(UninstallOutcome outcome, bool safe)
    {
        headless.OnUiThread(() =>
        {
            var calls = new List<bool>();
            var results = new Queue<UninstallReport>([new UninstallReport(outcome, VelopackExitCode: outcome == UninstallOutcome.VelopackFailed ? 1 : null), Removed]);
            var (rig, session, screen, flow) = Open(w => { calls.Add(w); return Task.FromResult(results.Dequeue()); });
            screen.Switch.Toggle();
            rig.Run(0.6);

            screen.Remove.Invoke();
            rig.Run(UninstallFlow.MinimumRemoving + 0.1);
            Assert.Equal(SetupScreen.Problem, session.Screen);
            var problem = Assert.IsType<ProblemScreen>(session.Current);
            rig.Run(1.2);
            Assert.False(rig.Window.Guard.IsPaused);
            Assert.Contains(SetupStrings.UninstallProblemTitle, Words(problem));
            Assert.DoesNotContain(SetupStrings.ProblemTitle, Words(problem));
            Assert.Equal(SetupStrings.UninstallProblemOther, problem.Reason.Text);
            Assert.Equal(safe ? SetupStrings.ProblemSafe : SetupStrings.UninstallProblemLeft, problem.Ending.Text);
            Assert.Contains("Uninstall: " + outcome, problem.Details(), StringComparison.Ordinal);
            Assert.True(problem.Retry.IsVisible);
            Assert.Equal(1, session.ExitCode); // closed here, the uninstall failed

            problem.Retry.Invoke();
            Assert.Equal(SetupScreen.Uninstall, session.Screen);
            Assert.Equal(UninstallState.Removing, screen.State);
            Assert.True(rig.Window.Guard.IsPaused);
            Assert.False(screen.Group.IsVisible);
            Assert.False(screen.Cancel.IsVisible);
            Assert.False(screen.Remove.IsVisible);
            Assert.True(screen.Bar.IsVisible);
            Assert.Equal(SetupStrings.UninstallRemoving, screen.TitleBlock.Text);
            Assert.Equal(SetupStrings.UninstallRemovingWipe, screen.SubtitleBlock.Text);
            Assert.Equal([true, true], calls);
            Assert.Equal(2, flow.Attempts);
            rig.Run(UninstallFlow.MinimumRemoving + 1.3);
            Assert.Equal(UninstallState.Removed, screen.State);
            Assert.Equal(SetupStrings.UninstallRemovedWipe, screen.SubtitleBlock.Text);
            Assert.False(rig.Window.Guard.IsPaused);
            Assert.Equal(0, session.ExitCode);
            rig.Window.CloseNow();
        });
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void Retry_comes_back_looking_exactly_as_Removing_does_after_Uninstall()
    {
        headless.OnUiThread(() =>
        {
            var first = new TaskCompletionSource<UninstallReport>();
            var second = new TaskCompletionSource<UninstallReport>();
            var removals = new Queue<Task<UninstallReport>>([first.Task, second.Task]);
            var (rig, session, screen, _) = Open(_ => removals.Dequeue());

            screen.Remove.Invoke();
            rig.Run(2); // Removing has come to rest
            Assert.False(screen.Reveal.IsBusy);
            var shown = Places(rig, screen);
            first.SetResult(new UninstallReport(UninstallOutcome.DidNotStart));
            var problem = Assert.IsType<ProblemScreen>(session.Current);
            rig.Run(1.2);
            problem.Retry.Invoke();
            Assert.False(screen.Reveal.IsBusy); // nothing reveals on arrival
            rig.Run(2);
            Assert.Equal(shown, Places(rig, screen));
            Assert.Equal(UninstallState.Removing, screen.State);
            rig.Window.CloseNow();
        });
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void A_removal_that_throws_or_faults_says_files_may_be_left()
    {
        headless.OnUiThread(() =>
        {
            foreach (var remove in new Func<bool, Task<UninstallReport>>[] { _ => throw new InvalidOperationException("boom"), _ => Task.FromException<UninstallReport>(new IOException("disk")) })
            {
                var (rig, session, screen, flow) = Open(remove);
                screen.Remove.Invoke();
                rig.Run(UninstallFlow.MinimumRemoving + 0.1);
                var problem = Assert.IsType<ProblemScreen>(session.Current);
                Assert.Equal(SetupStrings.UninstallProblemLeft, problem.Ending.Text);
                Assert.Equal(UninstallOutcome.VelopackFailed, flow.Report!.Outcome);
                Assert.False(rig.Window.Guard.IsPaused);
                rig.Window.CloseNow();
            }
        });
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void The_end_of_a_removal_on_another_thread_comes_back_through_the_dispatcher()
    {
        headless.OnUiThread(() =>
        {
            var ui = Environment.CurrentManagedThreadId;
            var ranOn = 0;
            var rig = new WindowRig();
            var session = rig.Session(SetupScreen.Uninstall, installed: "1.1.0");
            Show(rig, session);
            var flow = UninstallFlow.Attach(session, _ => Task.Run(() => { ranOn = Environment.CurrentManagedThreadId; return Removed; }));
            var screen = flow.Screen;

            screen.Remove.Invoke();
            rig.Run(UninstallFlow.MinimumRemoving + 0.1);
            for (var i = 0; i < 400 && screen.State != UninstallState.Removed; i++)
            {
                Dispatcher.UIThread.RunJobs();
                if (screen.State != UninstallState.Removed) Thread.Sleep(5);
            }
            Assert.Equal(UninstallState.Removed, screen.State);
            Assert.NotEqual(ui, ranOn);
            rig.Window.CloseNow();
        });
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void Uninstall_mode_is_wired_only_to_a_session_that_starts_on_Uninstall()
    {
        headless.OnUiThread(() =>
        {
            var rig = new WindowRig();
            var welcome = rig.Session(SetupScreen.Welcome);
            Assert.False(welcome.Uninstalling);
            Assert.Throws<InvalidOperationException>(() => UninstallFlow.Attach(welcome, _ => Task.FromResult(Removed)));
            var uninstall = new WindowRig().Session(SetupScreen.Uninstall, installed: "1.1.0");
            Assert.True(uninstall.Uninstalling);
        });
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(2.25)]
    [Trait("Decision", "OD-0072")]
    public void Ten_fast_toggles_then_Uninstall_mid_flight_never_jump_never_overlap_and_end_removed_at_rest(double textScale)
    {
        headless.OnUiThread(() =>
        {
            var rig = new WindowRig(Marrow.Setup.Platform.SystemLook.Default with { TextScale = textScale });
            var session = rig.Session(SetupScreen.Uninstall, installed: "1.1.0");
            Show(rig, session);
            var calls = new List<bool>();
            var flow = UninstallFlow.Attach(session, w => { calls.Add(w); return Task.FromResult(Removed); }, action => action());
            var screen = flow.Screen;
            var probe = new FrameProbe(rig, screen.View, [screen.MarkWrap, screen.TitleBlock, screen.SubtitleBlock, screen.Group, screen.Warning, screen.Bar, screen.Hold!.Child!], [screen.Cancel, screen.Remove]) { ButtonsFixed = false };
            var cancelAt = probe.Of(screen.Cancel).Box;
            var removeAt = probe.Of(screen.Remove).Box;

            for (var toggle = 0; toggle < 10; toggle++)
            {
                screen.Switch.Toggle();
                probe.Run(0.06, _ =>
                {
                    Assert.True(FrameProbe.Step(cancelAt, probe.Of(screen.Cancel).Box) < .01, "Cancel moved while the switch was toggled");
                    Assert.True(FrameProbe.Step(removeAt, probe.Of(screen.Remove).Box) < .01, "Uninstall moved while the switch was toggled");
                });
            }
            screen.Switch.Toggle(); // the eleventh: on, its reveal in flight as Uninstall is pressed
            probe.Run(0.05);
            Assert.True(screen.Reveal.IsBusy);
            screen.Remove.Invoke();
            probe.Run(UninstallFlow.MinimumRemoving + 2);

            Assert.Equal([true], calls);
            Assert.Equal(UninstallState.Removed, screen.State);
            Assert.False(screen.Reveal.IsBusy);
            Assert.False(screen.Warning.IsVisible);
            Assert.False(screen.Group.IsVisible);
            Assert.False(screen.Bar.IsVisible);
            Assert.Equal(1, probe.Of(screen.Cancel).Opacity, 3);
            Assert.Equal(SetupStrings.UninstallClose, screen.Cancel.Label);
            Assert.Equal(UninstallScreen.RemovedDim, screen.MarkPose.Opacity, 3);
            Assert.Equal(UninstallScreen.RemovedScale, screen.MarkPose.Scale, 3);
            Assert.InRange(probe.LargestStep, 0, FrameProbe.MaxStep);
            rig.Window.CloseNow();
        });
    }

    private static (WindowRig Rig, SetupSession Session, UninstallScreen Screen, UninstallFlow Flow) Open(Func<bool, Task<UninstallReport>> remove)
    {
        var rig = new WindowRig();
        var session = rig.Session(SetupScreen.Uninstall, installed: "1.1.0");
        Show(rig, session);
        var flow = UninstallFlow.Attach(session, remove, action => action());
        return (rig, session, flow.Screen, flow);
    }

    private static void Show(WindowRig rig, SetupSession session)
    {
        session.Start();
        rig.Window.Show();
        rig.Settle();
        rig.Run(1);
    }

    private static List<(string Name, double X, double Y, double Width, double Height, double Opacity, bool Visible)> Places(WindowRig rig, UninstallScreen screen)
    {
        var probe = new FrameProbe(rig, screen.View, [], []);
        return [.. new (string, Control)[]
        {
            ("mark", screen.MarkWrap), ("title", screen.TitleBlock), ("subtitle", screen.SubtitleBlock), ("row", screen.Group), ("warning", screen.Warning),
            ("bar", screen.Bar), ("hold", screen.Hold!), ("cancel", screen.Cancel), ("remove", screen.Remove),
        }.Select(c => (c.Item1, Math.Round(probe.Of(c.Item2).Box.X, 2), Math.Round(probe.Of(c.Item2).Box.Y, 2), Math.Round(probe.Of(c.Item2).Box.Width, 2), Math.Round(probe.Of(c.Item2).Box.Height, 2), Math.Round(probe.Of(c.Item2).Opacity, 3), c.Item2.IsEffectivelyVisible))];
    }

    private static List<string> Words(ISetupScreen screen) => [.. screen.View.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text).OfType<string>()];

    private static void Esc(WindowRig rig) => Press(rig, Key.Escape);

    private static void Press(WindowRig rig, Key key) =>
        rig.Window.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, Source = rig.Window });
}
