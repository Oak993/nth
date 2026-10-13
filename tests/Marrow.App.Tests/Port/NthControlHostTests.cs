using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Support;
using Xunit;

namespace Marrow.App.Tests.Port;

// PORT-ONLY (no Swift twin): nth installer PLAN.md 3a, unit U4 (ED-NEW-inst-u4-1; amends ED-247). The app's side of the handshake: what nth answers
// when the setup asks over the control pipe, on a real AppModel with threads whose runtimes run. status changes nothing; quit if-idle answers
// "busy" with the work listed or "ok" and then quits the way closing the main window does (AppDelegate.swift:44-54: a team merge is waited for);
// quit stop quits now; withdraw calls off a quit the installer started and a merge still holds, never the user's own close. The pipe transport
// is Marrow.Core.Tests' NthControlServerTests; the setup's client is Marrow.Setup.Tests.
[Collection("MCPHub processes")]
public sealed class NthControlHostTests
{
    [Fact]
    [Trait("Decision", "ED-247")]
    public void Status_lists_what_runs_longest_first_names_it_as_the_sidebar_does_and_changes_nothing()
    {
        using var rig = new NthControlRig("Write the release notes", "Refactor the sign-in module", "Idle thread", "");
        rig.SetRunning(0, TimeSpan.FromSeconds(61));
        rig.SetRunning(1, TimeSpan.FromSeconds(240));
        rig.SetRunning(3, TimeSpan.FromSeconds(5));

        var answer = rig.Ask(NthControlRequest.Status);

        Assert.Equal(NthControlReply.Status, answer.Response.Reply);
        Assert.Equal(4242, answer.Response.Pid);
        Assert.EndsWith("nth.exe", answer.Response.Exe, StringComparison.Ordinal);
        Assert.Equal(3, answer.Response.WorkCount);
        Assert.Equal(["Refactor the sign-in module", "Write the release notes", "New thread"], answer.Response.Work.Select(item => item.Name));
        Assert.Equal([240, 61, 5], answer.Response.Work.Select(item => item.Seconds));
        Assert.All(answer.Response.Work, item => Assert.Equal(NthWorkKind.Thread, item.Kind));
        Assert.Null(answer.AfterReply);
        Assert.Empty(rig.Recorder.Calls);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Nothing_running_is_an_empty_status_and_a_name_with_odd_characters_is_cleaned_before_it_leaves_nth()
    {
        using var rig = new NthControlRig("Fix a‮bug\u0007  now");

        Assert.Empty(rig.Ask(NthControlRequest.Status).Response.Work);

        rig.SetRunning(0, TimeSpan.FromSeconds(1));
        Assert.Equal("Fix abug now", Assert.Single(rig.Ask(NthControlRequest.Status).Response.Work).Name);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Quit_if_idle_while_work_runs_answers_busy_with_the_work_and_nothing_quits()
    {
        using var rig = new NthControlRig("Refactor the sign-in module", "Write the release notes");
        rig.SetRunning(0, TimeSpan.FromMinutes(4));
        rig.SetRunning(1, TimeSpan.FromMinutes(1));

        var answer = rig.Ask(NthControlRequest.QuitIfIdle);
        rig.Finish(answer);

        Assert.Equal(NthControlReply.Busy, answer.Response.Reply);
        Assert.Equal(2, answer.Response.WorkCount);
        Assert.Equal(["Refactor the sign-in module", "Write the release notes"], answer.Response.Work.Select(item => item.Name));
        Assert.Null(answer.AfterReply);
        Assert.Empty(rig.Recorder.Calls); // nth was not asked to quit, so it did not
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Quit_if_idle_when_nothing_runs_says_ok_and_quits_only_after_the_answer_was_sent()
    {
        using var rig = new NthControlRig("Idle thread");

        var answer = rig.Ask(NthControlRequest.QuitIfIdle);

        Assert.Equal(NthControlReply.Ok, answer.Response.Reply);
        Assert.Null(answer.Response.Deferred);
        Assert.Empty(rig.Recorder.Calls); // not yet: the setup must read "ok" first
        rig.Finish(answer);
        Assert.Equal(["exit"], rig.Recorder.Calls); // the same exit the main window's close control takes
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Quit_stop_with_work_running_quits_now_the_way_closing_the_window_does()
    {
        using var rig = new NthControlRig("Refactor the sign-in module");
        rig.SetRunning(0, TimeSpan.FromMinutes(4));

        var answer = rig.Ask(NthControlRequest.QuitAndStop);
        rig.Finish(answer);

        Assert.Equal(NthControlReply.Ok, answer.Response.Reply);
        Assert.Null(answer.Response.Deferred);
        Assert.Equal(["exit"], rig.Recorder.Calls);

        // Closing the window by hand with the same thread running ends the same way.
        using var byHand = new NthControlRig("Refactor the sign-in module");
        byHand.SetRunning(0, TimeSpan.FromMinutes(4));
        byHand.H.Main.OnMain(byHand.Shell.Terminate);
        Assert.Equal(byHand.Recorder.Calls, rig.Recorder.Calls);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_team_merge_going_out_is_work_and_a_stop_waits_for_it_as_closing_the_window_does()
    {
        using var rig = new NthControlRig("Ana");
        rig.SetMerging(0, true, TimeSpan.FromSeconds(30));

        var busy = rig.Ask(NthControlRequest.QuitIfIdle);
        Assert.Equal(NthControlReply.Busy, busy.Response.Reply);
        var merge = Assert.Single(busy.Response.Work);
        Assert.Equal(NthWorkKind.Merge, merge.Kind);
        Assert.Equal(30, merge.Seconds);

        var stop = rig.Ask(NthControlRequest.QuitAndStop);
        Assert.Equal(NthControlReply.Ok, stop.Response.Reply);
        Assert.Equal(NthWorkKind.Merge, stop.Response.Deferred); // the setup learns that nth will first finish the merge
        rig.Finish(stop);
        Assert.Empty(rig.Recorder.Calls);
        rig.H.Main.Advance(TimeSpan.FromSeconds(10));
        Assert.Empty(rig.Recorder.Calls); // still merging: still here
        rig.SetMerging(0, false);
        rig.H.Main.AdvanceMilliseconds(250);
        Assert.Equal(["exit"], rig.Recorder.Calls); // the merge ended and nth went
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Withdraw_calls_off_a_quit_that_the_installer_started_and_a_merge_still_holds()
    {
        using var rig = new NthControlRig("Ana");
        rig.SetMerging(0, true);
        rig.Finish(rig.Ask(NthControlRequest.QuitAndStop));
        rig.H.Main.Advance(TimeSpan.FromSeconds(5));
        Assert.Empty(rig.Recorder.Calls);

        var answer = rig.Ask(NthControlRequest.Withdraw);
        rig.SetMerging(0, false);
        rig.H.Main.Advance(TimeSpan.FromMinutes(5));

        Assert.Equal(NthControlReply.Ok, answer.Response.Reply);
        Assert.Empty(rig.Recorder.Calls); // the merge ended and nth stayed open: the update was given up
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Withdraw_never_calls_off_the_users_own_close_and_does_nothing_when_nothing_was_asked()
    {
        using var rig = new NthControlRig("Ana");
        rig.Finish(rig.Ask(NthControlRequest.Withdraw)); // nothing to withdraw
        Assert.Empty(rig.Recorder.Calls);

        rig.SetMerging(0, true);
        rig.H.Main.OnMain(rig.Shell.Terminate); // the user closes nth; a merge holds the quit
        rig.Finish(rig.Ask(NthControlRequest.Withdraw)); // the installer gives up: not its quit to call off
        rig.SetMerging(0, false);
        rig.H.Main.AdvanceMilliseconds(250);

        Assert.Equal(["exit"], rig.Recorder.Calls);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_second_quit_while_one_waits_changes_nothing_and_a_quit_after_the_app_ended_is_still_ok()
    {
        using var rig = new NthControlRig("Ana");
        rig.SetMerging(0, true);
        rig.Finish(rig.Ask(NthControlRequest.QuitAndStop));
        rig.Finish(rig.Ask(NthControlRequest.QuitAndStop));
        rig.SetMerging(0, false);
        rig.H.Main.AdvanceMilliseconds(250);
        Assert.Equal(["exit"], rig.Recorder.Calls); // one exit, not two

        rig.H.Main.OnMain(rig.Shell.WillTerminate);
        var late = rig.Ask(NthControlRequest.QuitIfIdle);
        rig.Finish(late);
        Assert.Equal(NthControlReply.Ok, late.Response.Reply);
        Assert.Equal(1, rig.Recorder.Exits);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public async Task A_request_that_arrives_before_the_launch_has_finished_waits_for_it()
    {
        using var rig = new NthControlRig("Ana");
        var launched = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var host = new NthControlHost(rig.Shell, launched.Task, processId: 7, executable: "nth.exe");

        var pending = Task.Run(() => host.HandleAsync(NthControlRequest.Status, CancellationToken.None));
        await Task.Delay(150);
        Assert.False(pending.IsCompleted);

        launched.SetResult();
        rig.H.Main.Loop.RunUntil(() => pending.IsCompleted, AppModelHarness.Bound);

        var answer = await pending;
        Assert.Equal(NthControlReply.Status, answer.Response.Reply);
        Assert.Equal(7, answer.Response.Pid);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public async Task A_request_whose_connection_ended_while_it_waited_is_dropped()
    {
        using var rig = new NthControlRig("Ana");
        var never = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var host = new NthControlHost(rig.Shell, never.Task);
        using var gone = new CancellationTokenSource();

        var pending = Task.Run(() => host.HandleAsync(NthControlRequest.QuitAndStop, gone.Token));
        await Task.Delay(100);
        gone.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Empty(rig.Recorder.Calls);
    }
}
