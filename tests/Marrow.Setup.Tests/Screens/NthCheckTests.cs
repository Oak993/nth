using Avalonia.Automation;
using Avalonia.Threading;
using Marrow.Setup.Flow;
using Marrow.Setup.Screens;
using Marrow.Setup.Support;
using Marrow.Setup.Tests.Fakes;
using Marrow.Setup.Tests.Fixtures;
using Marrow.Setup.Text;
using Marrow.Setup.Ui;
using Marrow.Testing;

namespace Marrow.Setup.Tests.Screens;

// PORT-ONLY (no Swift twin): nth installer PLAN.md 3 step 7 and 3a; VELOPACK_CHECK.md findings 1 and 13 (ED-NEW-inst-fix-1). The
// window's install and update took no install hold and never asked a running nth to quit: InstallFlow had no gate and nothing
// answered Update's Busy, Waiting and not-responding buttons. These drive the real InstallFlow and NthCheck on a headless window
// with a hand-answered nth and a counted hold; Setup.exe is the fake runner, every folder a temp folder.
[Collection("SetupHeadless")]
public sealed class NthCheckTests(SetupHeadlessSession headless)
{
    [Fact]
    [Trait("Decision", "ED-247")]
    public void Install_takes_the_hold_and_asks_nth_before_Setup_starts_and_lets_the_hold_go_at_Ready()
    {
        headless.OnUiThread(() =>
        {
            var nth = new GateNth { Open = false };
            var holds = new CountedHolds();
            using var rig = Rig(nth, holds);

            rig.Session.ChooseInstall();
            Assert.Equal(["ask"], nth.Calls);
            Assert.Equal(1, holds.Taken);
            Assert.Equal(SetupScreen.Installing, rig.Session.Screen); // a fresh install shows its progress at once
            rig.Staged();
            for (var i = 0; i < 5; i++) rig.Step();
            Assert.Equal(0, rig.Runner.Starts); // nth has not answered: Setup.exe waits

            nth.Answer(RunningNthState.Clear);
            rig.Step();
            rig.Step();
            Assert.Equal(1, rig.Runner.Starts);
            Assert.Equal(0, holds.Last!.Disposals); // held while Setup.exe runs
            rig.Until(() => !rig.Poll.Running);
            rig.Window.Run(2);

            Assert.Equal(SetupScreen.Ready, rig.Session.Screen);
            Assert.Equal(1, holds.Last.Disposals);
        });
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Another_setup_on_the_root_is_Problem_and_nth_is_never_asked()
    {
        headless.OnUiThread(() =>
        {
            var nth = new GateNth();
            var holds = new CountedHolds { Deny = true };
            using var rig = Rig(nth, holds);

            rig.Session.ChooseInstall();
            rig.Window.Run(1);

            Assert.Equal(SetupScreen.Problem, rig.Session.Screen);
            Assert.Empty(nth.Calls);
            Assert.Equal(0, rig.Runner.Starts);
            Assert.False(rig.Flow.Busy);
            Assert.Contains("Another nth Setup is installing to this folder.", rig.Runs[0].Failure!.Log);
        });
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_fresh_install_with_a_busy_nth_goes_to_Problem_calls_off_the_ask_and_lets_the_hold_go()
    {
        headless.OnUiThread(() =>
        {
            var nth = new GateNth();
            var holds = new CountedHolds();
            using var rig = Rig(nth, holds);
            rig.Session.ChooseInstall();
            rig.Staged();

            nth.Answer(RunningNthState.Busy, threads: 2);
            rig.Step();
            rig.Window.Run(1);

            Assert.Equal(SetupScreen.Problem, rig.Session.Screen);
            Assert.Equal(SetupStrings.ProblemInUse, rig.Session.ProblemReason);
            Assert.Equal(["ask", "cancel"], nth.Calls);
            Assert.Equal(0, rig.Runner.Starts);
            Assert.Equal(1, holds.Last!.Disposals);
        });
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Nth_saving_and_closing_reads_as_the_first_step_and_Setup_starts_once_it_is_gone()
    {
        headless.OnUiThread(() =>
        {
            var nth = new GateNth();
            using var rig = Rig(nth, new CountedHolds());
            rig.Session.ChooseInstall();
            rig.Staged();

            nth.Report(RunningNthState.Closing);
            rig.Step();
            rig.Window.Run(0.5);
            Assert.Equal(SetupStrings.StepClosing, rig.Panel.Steps.Rows[0].Label);
            Assert.Equal(0, rig.Runner.Starts);

            nth.Answer(RunningNthState.Closed);
            rig.Step();
            rig.Step();
            Assert.Equal(1, rig.Runner.Starts);
        });
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Update_keeps_Ask_while_nth_answers_and_a_busy_nth_shows_its_threads_with_nothing_started()
    {
        headless.OnUiThread(() =>
        {
            var nth = new GateNth();
            var holds = new CountedHolds();
            using var rig = Rig(nth, holds, update: true);
            var update = UpdateOf(rig);

            rig.Session.ChooseInstall();
            Assert.Equal(UpdateState.Ask, update.State); // within the 0.4 s nth has to answer
            Assert.True(rig.Flow.Busy);

            nth.Answer(RunningNthState.Busy, threads: 2);
            Pump(rig, () => !rig.Flow.Busy);

            Assert.Equal(SetupScreen.Update, rig.Session.Screen);
            Assert.Equal(UpdateState.Busy, update.State);
            Assert.Equal(2, update.Threads);
            Assert.Equal(0, rig.Runner.Starts);
            Assert.Equal(1, holds.Last!.Disposals); // the question holds nothing
        });
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Update_shows_its_progress_when_nth_takes_longer_than_the_patience_and_a_later_busy_answer_comes_back_as_the_question()
    {
        headless.OnUiThread(() =>
        {
            var nth = new GateNth();
            var time = new ManualTimeProvider();
            using var rig = Rig(nth, new CountedHolds(), update: true, time: time);
            var update = UpdateOf(rig);
            rig.Session.ChooseInstall();

            time.Advance(NthCheck.AskPatience - TimeSpan.FromMilliseconds(1));
            Pump(rig, () => false, rounds: 20);
            Assert.Equal(UpdateState.Ask, update.State);
            time.Advance(TimeSpan.FromMilliseconds(1));
            Pump(rig, () => update.State == UpdateState.Updating);
            Assert.Equal(UpdateState.Updating, update.State);

            nth.Answer(RunningNthState.Busy, threads: 1);
            rig.Staged();
            rig.Step();
            rig.Window.Run(1);

            Assert.Equal(UpdateState.Busy, update.State);
            Assert.False(rig.Flow.Busy);
            Assert.Equal(0, rig.Runner.Starts);
        });
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Update_when_they_finish_waits_on_nth_and_updates_by_itself_when_it_has_closed()
    {
        headless.OnUiThread(() =>
        {
            var nth = new GateNth();
            var holds = new CountedHolds();
            using var rig = Rig(nth, holds, update: true);
            var update = UpdateOf(rig);
            rig.Session.ChooseInstall();
            nth.Answer(RunningNthState.Busy, threads: 2);
            Pump(rig, () => update.State == UpdateState.Busy);

            update.Go.Invoke(); // Busy: "Update when they finish"
            Assert.Equal(["ask", "wait"], nth.Calls);
            Assert.Equal(UpdateState.Waiting, update.State);
            Assert.Equal(2, holds.Taken);
            rig.Window.Run(1.5); // the Waiting screen is seen while nth's threads finish
            Assert.True(update.Steps.IsVisible);
            nth.Report(RunningNthState.Waiting, threads: 1);
            Assert.Equal(1, update.Threads);
            Assert.Equal(SetupStrings.StepWaiting(1), update.Steps.Rows[0].Label);
            Assert.Equal(SetupStrings.UpdateWaitSubtitle(1), AutomationProperties.GetName(update.SubtitleBlock));
            rig.Staged();
            rig.Step();
            Assert.Equal(0, rig.Runner.Starts);

            nth.Answer(RunningNthState.Closed);
            Pump(rig, () => update.State == UpdateState.Updating);
            rig.Step();
            rig.Step();
            Assert.Equal(1, rig.Runner.Starts);
            // the wait's row stays above the run's, done; nothing is rebuilt (CanvasProgress.Lead)
            Assert.Equal([SetupStrings.StepWaiting(1), SetupStrings.StepClosing, SetupStrings.StepCopyingNew, SetupStrings.StepFinishing], update.Steps.Rows.Select(row => row.Label));
            Assert.Equal(StepState.Done, update.Steps.Rows[0].State);
        });
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Stop_them_and_update_stops_nths_threads_first()
    {
        headless.OnUiThread(() =>
        {
            var nth = new GateNth();
            using var rig = Rig(nth, new CountedHolds(), update: true);
            var update = UpdateOf(rig);
            rig.Session.ChooseInstall();
            nth.Answer(RunningNthState.Busy, threads: 2);
            Pump(rig, () => update.State == UpdateState.Busy);

            update.Later.Invoke(); // Busy: "Stop them and update"
            rig.Window.Run(1);

            Assert.Equal(["ask", "stop"], nth.Calls);
            Assert.Equal(UpdateState.Updating, update.State);
            Assert.Equal(SetupStrings.StepStopping(2), update.Steps.Rows[0].Label);
        });
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Stop_waiting_calls_off_the_wait_lets_the_hold_go_and_goes_back_to_Ask()
    {
        headless.OnUiThread(() =>
        {
            var nth = new GateNth();
            var holds = new CountedHolds();
            using var rig = Rig(nth, holds, update: true);
            var update = UpdateOf(rig);
            rig.Session.ChooseInstall();
            nth.Answer(RunningNthState.Busy, threads: 2);
            Pump(rig, () => update.State == UpdateState.Busy);
            update.Go.Invoke();

            update.Later.Invoke(); // Waiting: "Stop waiting"
            Pump(rig, () => !rig.Flow.Busy);

            Assert.Equal(["ask", "wait", "cancel"], nth.Calls);
            Assert.Equal(UpdateState.Ask, update.State);
            Assert.Equal(1, holds.Last!.Disposals);
            Assert.Equal(0, rig.Runner.Starts);
        });
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_not_responding_nth_asks_to_close_it_anyway_and_Update_ends_it_then_updates()
    {
        headless.OnUiThread(() =>
        {
            var nth = new GateNth();
            using var rig = Rig(nth, new CountedHolds(), update: true);
            var update = UpdateOf(rig);
            rig.Session.ChooseInstall();
            nth.Answer(RunningNthState.NotResponding);
            Pump(rig, () => update.State == UpdateState.NotResponding);
            Assert.Equal(SetupStrings.UpdateNotResponding, AutomationProperties.GetName(update.SubtitleBlock));
            Assert.Equal(SetupStrings.UpdateTitle, update.TitleBlock.Text);
            Assert.Equal(SetupStrings.UpdateBusyNote, update.Footnote.Text);
            Assert.Equal((SetupStrings.UpdateLater, SetupStrings.UpdateGo), (update.Later.Label, update.Go.Label));
            Assert.False(update.Steps.IsVisible);
            Assert.False(update.Busy.IsVisible);

            update.Go.Invoke(); // "Update": close it anyway
            Assert.Equal(["ask", "end"], nth.Calls);
            nth.Answer(RunningNthState.Closed);
            rig.Staged();
            rig.Step();
            rig.Step();

            Assert.Equal(1, rig.Runner.Starts);
        });
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Not_now_on_the_not_responding_question_forces_nothing_and_goes_back_to_Ask()
    {
        headless.OnUiThread(() =>
        {
            var nth = new GateNth();
            using var rig = Rig(nth, new CountedHolds(), update: true);
            var update = UpdateOf(rig);
            rig.Session.ChooseInstall();
            nth.Answer(RunningNthState.NotResponding);
            Pump(rig, () => update.State == UpdateState.NotResponding);

            update.Later.Invoke();
            rig.Window.Run(1);

            Assert.Equal(["ask", "cancel"], nth.Calls);
            Assert.Equal(UpdateState.Ask, update.State);
            Assert.Equal(0, rig.Runner.Starts);
        });
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void The_Update_footnote_says_nth_saves_and_closes_only_when_nth_is_open()
    {
        headless.OnUiThread(() =>
        {
            using var open = Rig(new GateNth { Open = true }, new CountedHolds(), update: true);
            var update = UpdateOf(open);
            Pump(open, () => update.NthOpen);
            open.Window.Run(1);
            Assert.Equal(SetupStrings.UpdateKeepOpen, update.Footnote.Text);
        });
        headless.OnUiThread(() =>
        {
            using var closed = Rig(new GateNth { Open = false }, new CountedHolds(), update: true);
            var update = UpdateOf(closed);
            Pump(closed, () => false, rounds: 20);
            closed.Window.Run(1);
            Assert.False(update.NthOpen);
            Assert.Equal(SetupStrings.UpdateKeep, update.Footnote.Text);
        });
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Closing_the_window_calls_off_whatever_nth_was_told_and_lets_the_hold_go()
    {
        headless.OnUiThread(() =>
        {
            var nth = new GateNth();
            var holds = new CountedHolds();
            using var rig = Rig(nth, holds, update: true);
            rig.Session.ChooseInstall();

            Assert.True(((NthCheck)rig.Gate!).CloseAsync().IsCompleted);

            Assert.Equal(["ask", "cancel"], nth.Calls);
            Assert.Equal(1, holds.Last!.Disposals);
        });
    }

    private static InstallFlowRig Rig(GateNth nth, CountedHolds holds, bool update = false, ManualTimeProvider? time = null) =>
        new(VelopackScenarios.Build("fast"), update: update, gate: session =>
        {
            var check = new NthCheck(session, _ => nth, holds.Take, Post, time ?? new ManualTimeProvider());
            if (update) check.AttachUpdate((UpdateScreen)session.Get(SetupScreen.Update), session.Route.TargetRoot);
            return check;
        });

    private static UpdateScreen UpdateOf(InstallFlowRig rig) => (UpdateScreen)rig.Session.Get(SetupScreen.Update);

    // On the window's thread at once; from elsewhere (the footnote's background look, a timer) through the dispatcher.
    private static void Post(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess()) action();
        else Dispatcher.UIThread.Post(action);
    }

    // An answer that completes on the thread pool reaches the window through the dispatcher: run its jobs until done (or 4 s).
    private static void Pump(InstallFlowRig rig, Func<bool> done, int rounds = 800)
    {
        for (var i = 0; i < rounds && !done(); i++)
        {
            Thread.Sleep(5);
            Dispatcher.UIThread.RunJobs();
        }
        rig.Window.Run(0.5);
    }
}
