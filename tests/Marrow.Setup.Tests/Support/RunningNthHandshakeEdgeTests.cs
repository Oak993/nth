using Marrow.Core.Support;
using Marrow.Setup.Support;
using Marrow.Setup.Tests.Fakes;

namespace Marrow.Setup.Tests.Support;

// PORT-ONLY (no Swift twin): nth installer PLAN.md 3a, unit U4 (ED-NEW-inst-u4-1; amends ED-247). The corners of the running-nth handshake
// that the six main cases leave: an nth with no pipe or a hung one, one that is still starting, one that answers from outside the root, one
// that is finishing a team merge, the person giving up at each stage (the quit nth was told is called off), Windows refusing to end it, the
// steps used out of order, and a window's handler failing. Fake clock throughout.
public sealed class RunningNthHandshakeEdgeTests
{
    [Fact]
    [Trait("Decision", "ED-247")]
    public async Task An_nth_with_no_control_pipe_gets_the_same_question_after_the_start_up_patience_and_is_ended_only_on_yes()
    {
        var rig = new HandshakeRig();
        rig.Nth.Pipe = FakePipe.Missing; // an older build, or a hung one that never made it

        var status = await rig.AskAsync();

        Assert.Equal(RunningNthState.NotResponding, status.State);
        Assert.Equal("no-answer", status.Reason);
        Assert.InRange(rig.Elapsed, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5.6));
        Assert.Empty(rig.Nth.Ended);
        var closed = await rig.Time.RunAsync(rig.Handshake.EndItAsync());
        Assert.Equal(RunningNthState.Closed, closed.State);
        Assert.Equal([4242], rig.Nth.Ended);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public async Task A_silent_pipe_is_a_hung_nth_and_a_no_asks_it_again_from_the_start()
    {
        var rig = new HandshakeRig();
        rig.Nth.Pipe = FakePipe.Silent;
        Assert.Equal("no-answer", (await rig.AskAsync()).Reason);
        rig.Nth.Pipe = FakePipe.Answers; // it woke up

        var status = await rig.Time.RunAsync(rig.Handshake.KeepWaitingAsync());

        Assert.Equal(RunningNthState.Closed, status.State);
        Assert.Empty(rig.Nth.Ended);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public async Task An_nth_that_is_still_starting_is_waited_for_not_questioned()
    {
        var rig = new HandshakeRig();
        rig.Nth.PipeAppearsAfter = TimeSpan.FromSeconds(1.2);

        var status = await rig.AskAsync();

        Assert.Equal(RunningNthState.Closed, status.State);
        Assert.Contains(RunningNthState.Asking, rig.States);
        Assert.DoesNotContain(RunningNthState.NotResponding, rig.States);
        Assert.Empty(rig.Nth.Ended);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public async Task An_nth_that_leaves_by_itself_while_it_is_being_asked_is_closed()
    {
        var rig = new HandshakeRig();
        rig.Nth.Pipe = FakePipe.Missing;
        rig.Nth.CloseAfter(TimeSpan.FromSeconds(1));

        var status = await rig.AskAsync();

        Assert.Equal(RunningNthState.Closed, status.State);
        Assert.Empty(rig.Nth.Ended);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public async Task An_outside_answer_is_left_alone_and_the_discovered_in_root_process_is_still_guarded()
    {
        // ED-NEW-inst-c7-3 retires the former Clear assertion: the owner requires the discovered process's exit or consent.
        var rig = new HandshakeRig();
        rig.Nth.AnswerPid = 9001;
        rig.Nth.AnswerExe = @"E:\Droppy\Installer\portable\nth.exe";

        var status = await rig.AskAsync();

        Assert.Equal(RunningNthState.NotResponding, status.State);
        Assert.False(status.CanProceed);
        Assert.True(rig.Nth.Running);
        Assert.Equal(["status"], rig.Nth.Sent);
        Assert.Empty(rig.Nth.Ended);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public async Task An_elevated_nth_cannot_be_asked_or_ended_and_the_setup_says_so_instead_of_trying()
    {
        var rig = new HandshakeRig();
        rig.Nth.Pipe = FakePipe.NotAllowed;

        var status = await rig.AskAsync();

        Assert.Equal(RunningNthState.Refused, status.State);
        Assert.Equal("not-allowed", status.Reason);
        Assert.Empty(rig.Nth.Ended);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public async Task A_yes_that_windows_refuses_is_refused_not_closed()
    {
        var rig = new HandshakeRig();
        rig.Nth.ExitDelay = null;
        rig.Nth.CanBeEnded = false;
        Assert.Equal(RunningNthState.NotResponding, (await rig.AskAsync()).State);

        var denied = await rig.Time.RunAsync(rig.Handshake.EndItAsync());

        Assert.Equal(RunningNthState.Refused, denied.State);
        Assert.Equal("not-allowed", denied.Reason);
        Assert.True(rig.Nth.Running);

        var rig2 = new HandshakeRig();
        rig2.Nth.ExitDelay = null;
        rig2.Nth.SurvivesEnd = true;
        Assert.Equal(RunningNthState.NotResponding, (await rig2.AskAsync()).State);
        var stuck = await rig2.Time.RunAsync(rig2.Handshake.EndItAsync());
        Assert.Equal(RunningNthState.Refused, stuck.State);
        Assert.Equal("could-not-end", stuck.Reason);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public async Task A_yes_ends_every_nth_program_inside_the_root_not_only_the_one_that_answered()
    {
        var rig = new HandshakeRig();
        rig.Nth.ExitDelay = null;
        rig.Nth.InRoot = [rig.Nth.Main, new RunningNthProcess(5150, Path.Combine(HandshakeRig.Root, "current", "nth.exe"), 2000)];
        Assert.Equal(RunningNthState.NotResponding, (await rig.AskAsync()).State);

        var status = await rig.Time.RunAsync(rig.Handshake.EndItAsync());

        Assert.Equal(RunningNthState.Closed, status.State);
        Assert.Equal(new[] { 4242, 5150 }, rig.Nth.Ended.Order().ToArray());
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public async Task An_nth_finishing_a_team_merge_is_not_asked_about_for_as_long_as_the_merge_goes_on()
    {
        var rig = new HandshakeRig();
        rig.Nth.MergeUntil = TimeSpan.FromSeconds(40);
        var busy = await rig.AskAsync();
        Assert.Equal(RunningNthState.Busy, busy.State); // a merge going out is work
        Assert.Equal(NthWorkKind.Merge, Assert.Single(busy.Work).Kind);

        var status = await rig.Time.RunAsync(rig.Handshake.StopWorkAndQuitAsync());

        Assert.Equal(RunningNthState.Closed, status.State);
        Assert.DoesNotContain(RunningNthState.NotResponding, rig.States); // forty seconds passed, and the 15 did not run out under the merge
        Assert.Contains(rig.Seen, item => item.State == RunningNthState.Closing && item.FinishingMerge);
        Assert.Empty(rig.Nth.Ended);
        Assert.True(rig.Elapsed >= TimeSpan.FromSeconds(40));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public async Task A_merge_that_ends_but_leaves_nth_hung_starts_the_fifteen_seconds_from_the_last_time_it_was_seen_merging()
    {
        var rig = new HandshakeRig();
        rig.Nth.MergeUntil = TimeSpan.FromSeconds(20);
        rig.Nth.ExitDelay = null;
        Assert.Equal(RunningNthState.Busy, (await rig.AskAsync()).State);

        var status = await rig.Time.RunAsync(rig.Handshake.StopWorkAndQuitAsync());

        Assert.Equal(RunningNthState.NotResponding, status.State);
        Assert.InRange(rig.Elapsed, TimeSpan.FromSeconds(34), TimeSpan.FromSeconds(36));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public async Task Giving_up_while_nth_waits_on_a_merge_calls_the_quit_off_and_nth_stays_open()
    {
        var rig = new HandshakeRig();
        rig.Nth.MergeUntil = TimeSpan.FromSeconds(40);
        Assert.Equal(RunningNthState.Busy, (await rig.AskAsync()).State);
        var stopping = rig.Handshake.StopWorkAndQuitAsync();
        await rig.Time.AdvanceUntilAsync(() => rig.Handshake.Status.State == RunningNthState.Closing, TimeSpan.FromSeconds(1));
        await rig.Time.AdvanceUntilAsync(() => false, TimeSpan.FromSeconds(5));

        await rig.Time.RunAsync(rig.Handshake.CancelAsync());
        var status = await rig.Time.RunAsync(stopping);

        Assert.Equal(RunningNthState.Cancelled, status.State);
        Assert.Equal(RunningNthState.Cancelled, rig.Handshake.Status.State);
        Assert.Equal(1, rig.Nth.Count("withdraw"));
        Assert.False(rig.Nth.Quitting);
        Assert.True(rig.Nth.Running);
        await rig.Time.AdvanceUntilAsync(() => false, TimeSpan.FromMinutes(1));
        Assert.True(rig.Nth.Running); // and it stays open
        Assert.Empty(rig.Nth.Ended);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public async Task Stop_waiting_calls_off_the_wait_nothing_was_started_and_nth_is_untouched()
    {
        var rig = new HandshakeRig();
        rig.Nth.Work = _ => HandshakeRig.TwoThreads;
        Assert.Equal(RunningNthState.Busy, (await rig.AskAsync()).State);
        var waiting = rig.Handshake.WaitForWorkToFinishAsync();
        await rig.Time.AdvanceUntilAsync(() => false, TimeSpan.FromSeconds(7));

        await rig.Time.RunAsync(rig.Handshake.CancelAsync());
        var status = await rig.Time.RunAsync(waiting);

        Assert.Equal(RunningNthState.Cancelled, status.State);
        Assert.True(rig.Nth.Running);
        Assert.False(rig.Nth.Quitting);
        Assert.Empty(rig.Nth.Ended);
        Assert.Equal(0, rig.Nth.Count("quit:stop"));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public async Task A_cancelled_token_given_to_a_step_ends_it_the_same_way_as_Cancel()
    {
        var rig = new HandshakeRig();
        rig.Nth.Work = _ => HandshakeRig.TwoThreads;
        Assert.Equal(RunningNthState.Busy, (await rig.AskAsync()).State);
        using var source = new CancellationTokenSource();
        var waiting = rig.Handshake.WaitForWorkToFinishAsync(source.Token);
        await rig.Time.AdvanceUntilAsync(() => false, TimeSpan.FromSeconds(5));

        source.Cancel();
        var status = await rig.Time.RunAsync(waiting);

        Assert.Equal(RunningNthState.Cancelled, status.State);
        Assert.True(rig.Nth.Running);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public async Task Giving_up_at_the_question_leaves_everything_as_it_is()
    {
        var rig = new HandshakeRig();
        rig.Nth.ExitDelay = null;
        Assert.Equal(RunningNthState.NotResponding, (await rig.AskAsync()).State);

        await rig.Time.RunAsync(rig.Handshake.CancelAsync());

        Assert.Equal(RunningNthState.Cancelled, rig.Handshake.Status.State);
        Assert.Empty(rig.Nth.Ended);
        Assert.True(rig.Nth.Running);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public async Task The_person_closing_nth_themselves_while_the_setup_waits_ends_the_wait_with_nothing_asked_of_nth()
    {
        var rig = new HandshakeRig();
        rig.Nth.Work = _ => HandshakeRig.TwoThreads;
        Assert.Equal(RunningNthState.Busy, (await rig.AskAsync()).State);
        rig.Nth.CloseAfter(rig.Time.Elapsed + TimeSpan.FromSeconds(3));

        var status = await rig.Time.RunAsync(rig.Handshake.WaitForWorkToFinishAsync());

        Assert.Equal(RunningNthState.Closed, status.State);
        Assert.Equal(0, rig.Nth.Count("quit:stop"));
        Assert.Empty(rig.Nth.Ended);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public async Task While_waiting_one_or_two_unanswered_asks_are_borne_and_the_third_in_a_row_is_the_question()
    {
        var rig = new HandshakeRig();
        rig.Nth.Work = _ => HandshakeRig.TwoThreads;
        Assert.Equal(RunningNthState.Busy, (await rig.AskAsync()).State);
        var waiting = rig.Handshake.WaitForWorkToFinishAsync();
        await rig.Time.AdvanceUntilAsync(() => false, TimeSpan.FromSeconds(3)); // one ask answered busy, one on its way
        rig.Nth.Pipe = FakePipe.Silent;
        await rig.Time.AdvanceUntilAsync(() => false, TimeSpan.FromSeconds(4)); // two unanswered
        Assert.False(waiting.IsCompleted);
        rig.Nth.Pipe = FakePipe.Answers; // it answers again: the count starts over
        await rig.Time.AdvanceUntilAsync(() => false, TimeSpan.FromSeconds(4));
        Assert.False(waiting.IsCompleted);
        rig.Nth.Pipe = FakePipe.Silent;

        var status = await rig.Time.RunAsync(waiting);

        Assert.Equal(RunningNthState.NotResponding, status.State);
        Assert.Equal("no-answer", status.Reason);
        Assert.Empty(rig.Nth.Ended);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public async Task Steps_used_out_of_order_or_at_once_are_refused_loudly()
    {
        var rig = new HandshakeRig();

        await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Handshake.EndItAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Handshake.WaitForWorkToFinishAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Handshake.StopWorkAndQuitAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Handshake.KeepWaitingAsync());

        rig.Nth.ExitDelay = null;
        var first = rig.Handshake.AskAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Handshake.AskAsync());
        await rig.Time.RunAsync(first);
        Assert.Equal(RunningNthState.NotResponding, rig.Handshake.Status.State);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public async Task A_window_handler_that_throws_does_not_break_the_handshake()
    {
        var rig = new HandshakeRig();
        rig.Handshake.StatusChanged += _ => throw new InvalidOperationException("the window is gone");

        var status = await rig.AskAsync();

        Assert.Equal(RunningNthState.Closed, status.State);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public async Task Cancel_before_anything_was_asked_changes_nothing()
    {
        var rig = new HandshakeRig();

        await rig.Handshake.CancelAsync();

        Assert.Equal(RunningNthState.NotAsked, rig.Handshake.Status.State);
        Assert.Empty(rig.Nth.Sent);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void The_defaults_are_the_plans_numbers()
    {
        var timings = RunningNthTimings.Default;

        Assert.Equal(TimeSpan.FromSeconds(15), timings.ExitGrace);   // PLAN.md 3a, step 3
        Assert.Equal(TimeSpan.FromSeconds(2), timings.WorkPoll);
        Assert.Equal(TimeSpan.FromSeconds(5), timings.StartupPatience);
        Assert.Equal(3, timings.WorkFailureLimit);
        Assert.Equal(RunningNthState.NotAsked, RunningNthStatus.Initial.State);
    }
}
