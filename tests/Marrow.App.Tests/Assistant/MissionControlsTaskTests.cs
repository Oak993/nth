using Marrow.App.Assistant;
using Marrow.App.Safety;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.App.Tests.Assistant;

// Dextra AS-6 (DESIGN.md rev 5, 4.2, 12.6, 12.6b; ED-NEW-dextra-as6-3 to -9): Pause, Resume, Stop, Revert and Restart for one task.
// Pause sends no interrupt and waits for the turn boundary; Resume restores the task where it was and holds to the rules of the
// cards, the stop key, a reserve, Tactus and a task whose agents were let go; Stop works in Pausing and Paused; a stopped task
// keeps its finished steps and runs the cut turn again; Revert keeps the work restorable for 7 days; Restart begins again from
// the brief on a fresh branch.
[Trait("Decision", "ED-NEW-dextra-as6-3")]
[Trait("Feature", "AS-6")]
public sealed class MissionControlsTaskTests : IDisposable
{
    private readonly AssistantTempRoot temp = new();

    public void Dispose() => temp.Dispose();

    private ControlsRig Rig(bool stopKey = true, Func<MissionRecord, DecisionItem?>? reserveCard = null) => new(temp.Folder, stopKey, reserveCard);

    private static readonly DateTimeOffset Long = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>A mission the runner loads from its journal in a state, with a workspace of the fakes' kind.</summary>
    private MissionRecord Journaled(MissionState state, int changedFiles = 3, StopCause? stoppedBy = StopCause.Owner, MissionState? resumeTo = null)
    {
        var id = Guid.NewGuid();
        var path = Path.Combine(temp.Folder, "worktrees", id.ToString("N"));
        var branch = MissionWorkspaces.BranchName(id, "Fix the login timeout");
        return new MissionRecord(new Mission(id, "Fix the login timeout", "dextra", MissionOrigin.Owner, state)
        {
            ChangedFiles = changedFiles,
            ResumeTo = resumeTo ?? (state == MissionState.Stopped ? MissionState.Working : null),
            StoppedBy = state == MissionState.Stopped ? stoppedBy : null,
            Branch = branch,
            WorktreePath = path,
        })
        {
            Brief = "Make the login wait 30 s, not 5 s, and keep the tests green.",
            ProjectId = MissionRig.Project,
            ProjectPath = Path.Combine(temp.Folder, "owner"),
            Provider = ProviderKind.Claude,
            Model = "model-a",
            Workspace = new MissionWorkspace(MissionWorkspaceKind.Worktree, Path.Combine(temp.Folder, "owner"), path)
            {
                WorkingFolder = path,
                Repository = Path.Combine(temp.Folder, "owner"),
                Branch = branch,
                BaseBranch = "main",
                BaseCommit = "0000000000000000000000000000000000000000",
            },
            Reason = state == MissionState.Failed ? "The reviewer failed it 3 times. Last: tests are red." : null,
            Created = Long,
            Ended = state == MissionState.Failed ? Long.AddHours(1) : null,
        };
    }

    // ---- Pause ----

    [Fact]
    public void Pause_sends_no_interrupt_and_a_task_mid_turn_stays_Pausing_until_its_turn_ends_then_Paused()
    {
        var rig = Rig();
        var record = rig.Started();
        var lead = record.LeadThread!.Value;
        Assert.Equal(PcHotkeyDefault, rig.Host.Registered);

        Assert.Null(rig.Controls.Pause(record.Id));

        Assert.Equal(MissionState.Pausing, rig.Now(record).Mission.State);
        Assert.Equal("Pausing…", MissionStates.Label(MissionState.Pausing));
        Assert.Equal(0, rig.Agents.Of(lead).Interrupts);
        Assert.Equal(0, rig.Rig.JobFactory.Made.Sum(j => j.Terminations));
        rig.Time.Advance(TimeSpan.FromMinutes(5)); // minutes can pass: a turn can last that long, and Pause never cuts it
        Assert.Equal(MissionState.Pausing, rig.Now(record).Mission.State);
        Assert.Equal((SlotA.Resume, SlotB.Stop), (rig.Now(record).Mission.Slots.A, rig.Now(record).Mission.Slots.B));

        rig.EndTurn(lead, "Wrote the first half.");
        var paused = rig.Now(record).Mission;
        Assert.Equal((MissionState.Paused, PauseCause.Owner, MissionState.Working), (paused.State, paused.PausedBy, paused.ResumeTo));
        Assert.Equal(0, rig.Agents.Of(lead).Interrupts);
        Assert.Equal("The task is already pausing or paused.", rig.Controls.Pause(record.Id));
        Assert.Contains(rig.Rig.Audit, e => e.Tool == "pause" && e.Result == "Pausing");
    }

    private static readonly Marrow.Core.Port.PcControl.Pill.PcHotkey PcHotkeyDefault = Marrow.Core.Port.PcControl.Pill.PcHotkey.StopDefault;

    [Fact]
    public void Resume_on_Pausing_cancels_the_pause_because_the_turn_never_stopped()
    {
        var rig = Rig();
        var record = rig.Started();
        var lead = record.LeadThread!.Value;
        Assert.Null(rig.Controls.Pause(record.Id));
        Assert.Null(rig.Controls.Resume(record.Id));
        var back = rig.Now(record).Mission;
        Assert.Equal(MissionState.Working, back.State);
        Assert.Null(back.PausedBy);
        Assert.Null(back.ResumeTo);
        Assert.True(rig.Agents.TurnRunning(lead));
        Assert.Single(rig.Agents.Of(lead).Sent); // nothing was said to the lead: its turn simply went on
        Assert.Equal("There is nothing to resume.", rig.Controls.Resume(record.Id));
    }

    [Fact]
    public void Stop_works_while_Pausing_and_while_Paused_and_a_stopped_task_keeps_where_it_paused_from()
    {
        var rig = Rig();
        var pausing = rig.Started(rig.Rig.Request("Pausing one"));
        var paused = rig.Started(rig.Rig.Request("Paused one"));
        Assert.Null(rig.Controls.Pause(pausing.Id));
        Assert.Null(rig.Controls.Pause(paused.Id));
        rig.EndTurn(paused.LeadThread!.Value, "Paused here.");
        Assert.Equal(MissionState.Pausing, rig.Now(pausing).Mission.State);
        Assert.Equal(MissionState.Paused, rig.Now(paused).Mission.State);

        Assert.Null(rig.Controls.Stop(pausing.Id));
        Assert.Null(rig.Controls.Stop(paused.Id));

        Assert.Equal(1, rig.Agents.Of(pausing.LeadThread!.Value).Interrupts); // the pausing turn is cut by Stop, not by Pause
        Assert.Equal(MissionState.Stopping, rig.Now(pausing).Mission.State);
        Assert.Equal(MissionState.Working, rig.Now(pausing).Mission.ResumeTo);
        Assert.Equal((MissionState.Stopped, MissionState.Working), (rig.Now(paused).Mission.State, rig.Now(paused).Mission.ResumeTo));
        Assert.Equal("The task is already stopping or stopped.", rig.Controls.Stop(paused.Id));
    }

    // ---- cards while paused or stopped ----

    [Fact]
    public void A_reversible_card_stays_answerable_while_paused_and_its_answer_is_kept_and_acts_on_Resume()
    {
        var rig = Rig();
        var record = rig.Started();
        var lead = record.LeadThread!.Value;
        rig.Runner.OnApproval(lead, ControlsRig.Approval("a1", "npm test"));
        var card = rig.Queue.Of(record.Id).Single();
        Assert.Null(rig.Controls.Pause(record.Id)); // a task that needs you runs no turn: it pauses at once
        Assert.Equal(MissionState.Paused, rig.Now(record).Mission.State);
        Assert.Equal(DecisionState.Open, rig.Queue.Find(card.Id)!.State);

        Assert.Null(rig.Controls.Answer(card.Id, DecisionAnswer.Deny("not now")));
        Assert.Null(rig.Controls.Answer(card.Id, DecisionAnswer.AllowOnce)); // changed his mind: the later answer is the one kept
        var kept = rig.Queue.Find(card.Id)!;
        Assert.Equal((DecisionState.Open, DecisionAnswerKind.AllowOnce), (kept.State, kept.Kept!.Kind));
        Assert.Empty(rig.Agents.Resolutions); // nothing reached the provider

        Assert.Null(rig.Controls.Resume(record.Id));
        Assert.Contains((lead, "a1", true), rig.Agents.Resolutions);
        Assert.Equal(DecisionState.Answered, rig.Queue.Find(card.Id)!.State);
        Assert.Null(rig.Queue.Find(card.Id)!.Kept);
        Assert.Equal(MissionState.Working, rig.Now(record).Mission.State);
    }

    [Fact]
    public void An_irreversible_card_is_held_by_Pause_and_an_approval_that_had_not_run_needs_a_new_tap()
    {
        var rig = Rig();
        var record = rig.Started();
        var lead = record.LeadThread!.Value;
        rig.Runner.OnApproval(lead, ControlsRig.Approval("a2", "git push origin main"));
        var card = rig.Queue.Of(record.Id).Single();
        Assert.Equal(DecisionWeight.Irreversible, card.Weight);

        // He taps Do it, but the app's main thread has not yet carried the answer to the provider when the pause lands.
        rig.Agents.Defer = true;
        Assert.Null(rig.Queue.Answer(card.Id, DecisionAnswer.DoIt));
        Assert.Null(rig.Controls.Pause(record.Id));
        rig.Agents.Defer = false;
        rig.Agents.RunPosted();

        var held = rig.Queue.Find(card.Id)!;
        Assert.Equal((DecisionState.Held, DecisionHold.Pause), (held.State, held.HeldBy));
        Assert.Equal(MissionRunner.HeldAfterApproval, held.Note);
        Assert.Empty(rig.Agents.Resolutions); // the provider was not told yes
        Assert.Equal("Held by Pause.", rig.Queue.Answer(card.Id, DecisionAnswer.DoIt));
        Assert.Contains(rig.Rig.Audit, e => e.Tool == "approval" && e.Result!.StartsWith("held by Pause before it ran", StringComparison.Ordinal));

        // Resume does not answer it: the task is back at Needs you and the card waits for a new tap.
        Assert.Null(rig.Controls.Resume(record.Id));
        Assert.Equal(MissionState.NeedsYou, rig.Now(record).Mission.State);
        Assert.Equal(DecisionState.Open, rig.Queue.Find(card.Id)!.State);
        Assert.Empty(rig.Agents.Resolutions);
        Assert.Null(rig.Queue.Answer(card.Id, DecisionAnswer.DoIt));
        Assert.Contains((lead, "a2", true), rig.Agents.Resolutions);
    }

    [Fact]
    public void A_live_provider_approval_is_answered_Deny_on_Stop_and_asked_again_when_the_cut_turn_runs_again()
    {
        var rig = Rig();
        var record = rig.Started();
        var lead = record.LeadThread!.Value;
        rig.Runner.OnApproval(lead, ControlsRig.Approval("a1", "npm install left-pad"));
        var first = rig.Queue.Of(record.Id).Single();

        Assert.Null(rig.Controls.Stop(record.Id));
        Assert.Contains((lead, "a1", false), rig.Agents.Resolutions);
        Assert.Equal(DecisionState.Withdrawn, rig.Queue.Find(first.Id)!.State);
        rig.EndTurn(lead, null, TurnStatus.Interrupted);
        rig.EndProcesses();
        rig.Runner.CheckStopping();
        Assert.Equal(MissionState.Stopped, rig.Now(record).Mission.State);

        Assert.Null(rig.Controls.Resume(record.Id));
        MissionRig.Until(() => rig.Agents.Of(lead).Sent.Count == 2, "the cut turn to run again");
        // The cut turn asks for the same thing again, and the owner is asked again.
        rig.Runner.OnApproval(lead, ControlsRig.Approval("a1", "npm install left-pad"));
        var again = rig.Queue.Of(record.Id).Single();
        Assert.NotEqual(first.Id, again.Id);
        Assert.Equal(DecisionState.Open, again.State);
        Assert.Equal("npm install left-pad", again.Inputs.First().Value);
    }

    [Fact]
    public void A_stopped_task_keeps_its_finished_steps_and_runs_only_the_cut_turn_again()
    {
        var rig = Rig();
        var (record, lead, done) = rig.WithHead("Docs");
        var cut = rig.AddHead(record, "Tests");
        rig.StartHead(done);
        rig.StartHead(cut);
        rig.Agents.Finish(done, "Docs done.");
        rig.Runner.HeadFinished(done, TurnStatus.Completed);
        Assert.Equal(HeadState.Done, rig.Now(record).Mission.Heads.Single(h => h.Id == done.ToString("D")).State);

        Assert.Null(rig.Controls.Stop(record.Id));
        rig.EndTurn(lead, null, TurnStatus.Interrupted);
        rig.Agents.Finish(cut, null);
        rig.Runner.HeadFinished(cut, TurnStatus.Interrupted);
        rig.EndProcesses();
        rig.Runner.CheckStopping();
        var stopped = rig.Now(record).Mission;
        Assert.Equal(MissionState.Stopped, stopped.State);
        Assert.Equal([HeadState.Done, HeadState.Stopped], stopped.Heads.Select(h => h.State));

        Assert.Null(rig.Controls.Resume(record.Id));
        var back = rig.Now(record).Mission;
        Assert.Equal(HeadState.Done, back.Heads.Single(h => h.Id == done.ToString("D")).State); // the finished step stays finished
        Assert.Equal(HeadState.Working, back.Heads.Single(h => h.Id == cut.ToString("D")).State);
        Assert.Empty(rig.Agents.HeldFinished);
        MissionRig.Until(() => rig.Agents.HeadsRestarted.Count == 1, "the cut head to start again");
        Assert.Equal(cut, rig.Agents.HeadsRestarted.Single().Head);
        Assert.Equal(MissionRunner.HeadContinueText, rig.Agents.HeadsRestarted.Single().Text);
        MissionRig.Until(() => rig.Agents.Of(lead).Sent.Count == 2, "the lead's cut turn to run again");
    }

    // ---- the stop key ----

    [Fact]
    public void The_stop_chord_is_registered_whenever_work_runs_and_let_go_when_a_task_is_paused_or_over()
    {
        var rig = Rig();
        rig.Pc.Streams = 1; // the lead goes on after Resume (it is not sent to its review)
        Assert.Null(rig.Host.Registered);
        var record = rig.Started();
        Assert.Equal(PcHotkeyDefault, rig.Host.Registered);
        Assert.Contains("mission:" + record.Id.ToString("N"), rig.Key.Scopes);

        Assert.Null(rig.Controls.Pause(record.Id));
        Assert.Equal(PcHotkeyDefault, rig.Host.Registered); // Pausing is still work in flight
        rig.EndTurn(record.LeadThread!.Value, "Paused here.");
        Assert.Null(rig.Host.Registered); // nothing runs now, so other apps get the chord
        Assert.Empty(rig.Key.Scopes);

        Assert.Null(rig.Controls.Resume(record.Id));
        Assert.Equal(PcHotkeyDefault, rig.Host.Registered);
        MissionRig.Until(() => rig.Agents.Of(record.LeadThread!.Value).Sent.Count == 2, "the lead to go on");
        Assert.Null(rig.Controls.Stop(record.Id));
        Assert.Equal(PcHotkeyDefault, rig.Host.Registered); // Stopping still holds it
        rig.EndTurn(record.LeadThread!.Value, null, TurnStatus.Interrupted);
        rig.EndProcesses();
        rig.Runner.CheckStopping();
        Assert.Equal(MissionState.Stopped, rig.Now(record).Mission.State);
        Assert.Null(rig.Host.Registered);
    }

    [Fact]
    public void A_start_waits_with_the_line_while_the_chord_is_taken_and_goes_on_by_itself_when_it_is_free()
    {
        var rig = Rig();
        rig.Host.Taken.Add(PcHotkeyDefault);
        var start = rig.Runner.Start(rig.Rig.Request());
        Assert.False(start.Launched.IsCompleted);
        var waiting = rig.Now(start.Record);
        Assert.Equal(MissionState.Planning, waiting.Mission.State);
        Assert.Equal("Stop key unavailable: Ctrl+Alt+Esc is taken. Change it in Settings", waiting.Waiting);
        Assert.Empty(rig.Agents.Chats);
        Assert.Empty(rig.Rig.Workspaces.Calls); // nothing was made, nothing started

        rig.Host.Taken.Clear();
        rig.Time.Advance(GlobalStop.RetryEvery);
        Assert.True(MissionRig.Within(start.Launched, 10));
        var started = rig.Now(start.Record);
        Assert.Equal(MissionState.Working, started.Mission.State);
        Assert.Null(started.Waiting);
        Assert.Single(rig.Agents.Chats);
    }

    [Fact]
    public void A_Resume_that_cannot_take_the_chord_changes_nothing_and_says_so()
    {
        var rig = Rig();
        var record = rig.Started();
        Assert.Null(rig.Controls.Pause(record.Id));
        rig.EndTurn(record.LeadThread!.Value, "Paused here.");
        rig.Host.Taken.Add(PcHotkeyDefault);

        var refused = rig.Controls.Resume(record.Id);
        Assert.Equal("Stop key unavailable: Ctrl+Alt+Esc is taken. Change it in Settings, then resume.", refused);
        Assert.Equal(MissionState.Paused, rig.Now(record).Mission.State);
        Assert.Empty(rig.Key.Scopes);

        rig.Host.Taken.Clear();
        Assert.Null(rig.Controls.Resume(record.Id));
        Assert.NotEqual(MissionState.Paused, rig.Now(record).Mission.State);
    }

    // ---- a reserve, Tactus, and agents let go ----

    [Fact]
    public void Resume_on_a_task_a_reserve_holds_brings_the_reserves_card_back_and_never_crosses_a_floor()
    {
        ControlsRig? holder = null;
        var cards = 0;
        var rig = Rig(reserveCard: record =>
        {
            cards++;
            var card = new DecisionItem(Guid.NewGuid(), DecisionKind.Limit, DecisionWeight.Reversible, "Would touch your Claude 20x reserve")
            {
                AssistantId = "dextra",
                MissionId = record.Id,
                Agent = "Reserve",
            };
            return holder!.Queue.Raise(card, (_, _) => { });
        });
        holder = rig;
        var record = rig.Started();
        var lead = record.LeadThread!.Value;
        Assert.Null(rig.Runner.Pause(record.Id, PauseCause.Reserve));
        rig.EndTurn(lead, "Reached the floor.");
        Assert.Equal((MissionState.Paused, PauseCause.Reserve), (rig.Now(record).Mission.State, rig.Now(record).Mission.PausedBy));
        Assert.Equal(BoardGroup.NeedsYou, rig.Now(record).Mission.Group); // a reserve's pause shows at Needs you

        Assert.Null(rig.Controls.Resume(record.Id));

        Assert.Equal(1, cards);
        Assert.Equal(MissionState.NeedsYou, rig.Now(record).Mission.State);
        var card = rig.Queue.Of(record.Id).Single();
        Assert.Equal("Would touch your Claude 20x reserve", card.Title);
        Assert.Single(rig.Agents.Of(lead).Sent); // the lead was told nothing: Resume did not cross the floor
        Assert.Equal(DecisionState.Open, card.State);
        // Answering the card is what resumes it: the mission goes back to work and on to its review.
        Assert.Null(rig.Queue.Answer(card.Id, DecisionAnswer.AllowOnce));
        MissionRig.Until(() => rig.Now(record).ReviewerThread is not null, "the task to go on after the card");
    }

    [Fact]
    public void A_reserve_task_without_a_card_to_raise_stays_paused()
    {
        var rig = Rig();
        var record = rig.Started();
        Assert.Null(rig.Runner.Pause(record.Id, PauseCause.Reserve));
        rig.EndTurn(record.LeadThread!.Value, "Reached the floor.");
        Assert.Equal("Your reserve holds this task; answer its card to resume it.", rig.Controls.Resume(record.Id));
        Assert.Equal(MissionState.Paused, rig.Now(record).Mission.State);
    }

    [Fact]
    public void A_pause_holds_the_Tactus_streams_and_its_Resume_lets_them_go_and_tells_the_lead_to_go_on()
    {
        var rig = Rig();
        rig.Pc.Streams = 1;
        var record = rig.Started();
        var lead = record.LeadThread!.Value;

        Assert.Null(rig.Controls.Pause(record.Id));
        Assert.Equal([record.Id], rig.Pc.Held);
        // The model can no longer act on the PC, so it ends its turn, though it is not done.
        rig.EndTurn(lead, "Waiting for you: paused.");
        Assert.Equal(MissionState.Paused, rig.Now(record).Mission.State);
        Assert.Empty(rig.Pc.Released);

        Assert.Null(rig.Controls.Resume(record.Id));
        Assert.Equal([record.Id], rig.Pc.Released);
        MissionRig.Until(() => rig.Agents.Of(lead).Sent.Count == 2, "the continue message");
        Assert.Equal(MissionRunner.ContinueText, rig.Agents.Of(lead).Sent[1]);
        // It is not sent to review as if it had finished.
        Assert.Equal(MissionState.Working, rig.Now(record).Mission.State);
        Assert.Null(rig.Now(record).ReviewerThread);
    }

    [Fact]
    public void A_pause_that_held_no_stream_resumes_without_a_continue_message()
    {
        var rig = Rig();
        var record = rig.Started();
        var lead = record.LeadThread!.Value;
        Assert.Null(rig.Controls.Pause(record.Id));
        rig.EndTurn(lead, "Finished the work.");
        Assert.Empty(rig.Pc.Released);
        Assert.Null(rig.Controls.Resume(record.Id));
        Assert.Empty(rig.Pc.Released);
        // The lead had finished its turn before the pause took: the mission moves on to its review.
        MissionRig.Until(() => rig.Now(record).ReviewerThread is not null, "the review");
        Assert.Single(rig.Agents.Of(lead).Sent);
    }

    [Fact]
    public void A_paused_tasks_agents_are_let_go_after_ten_minutes_and_Resume_starts_them_again_behind_the_memory_gate()
    {
        var rig = Rig();
        rig.Pc.Streams = 1; // the lead's turn ended because Tactus was held: it has more to do
        var record = rig.Started();
        var lead = record.LeadThread!.Value;
        rig.Agents.Context[lead] = 60_400;
        var job = (FakeAgentJob)rig.Rig.Jobs.For(lead);
        Assert.Null(rig.Controls.Pause(record.Id));
        rig.EndTurn(lead, "Waiting for you: paused.");
        Assert.True(rig.Rig.Gate.Holds(lead));

        rig.Time.Advance(TimeSpan.FromMinutes(9) + TimeSpan.FromSeconds(59));
        Assert.False(rig.Now(record).AgentsReleased);
        Assert.True(rig.Rig.Gate.Holds(lead)); // for its first 10 minutes it keeps its agents, so Resume is instant
        Assert.Equal(0, job.Terminations);
        rig.Time.Advance(TimeSpan.FromSeconds(1));

        Assert.True(rig.Now(record).AgentsReleased);
        Assert.Equal(1, rig.Agents.Of(lead).SessionStops);
        Assert.True(job.Disposed);
        Assert.Equal(1, job.Terminations);
        Assert.Null(rig.Rig.Jobs.Existing(lead));
        Assert.False(rig.Rig.Gate.Holds(lead)); // its slot and RAM are free
        Assert.Equal(MissionState.Paused, rig.Now(record).Mission.State);
        Assert.Contains(rig.Rig.Audit, e => e.Tool == "agents_released");

        // Memory is short when he resumes: the task waits with the gate's line, and the cold start is named.
        rig.Rig.Memory.Reading = new MemoryReading(32UL << 30, 2UL << 30);
        Assert.Null(rig.Controls.Resume(record.Id));
        var waiting = rig.Now(record);
        Assert.Equal(MissionState.Working, waiting.Mission.State);
        Assert.Equal("Free memory is under 15% (6% free); this agent starts when more is free.", waiting.Waiting);
        Assert.Equal(60_400, waiting.ColdStartTokens); // read again at the uncached price: it counts in the task's spend
        Assert.Single(rig.Agents.Of(lead).Sent);
        Assert.False(rig.Rig.Gate.Holds(lead));

        rig.Rig.Memory.Reading = FakeMemory.Plenty;
        rig.Rig.Gate.Pump();
        MissionRig.Until(() => rig.Agents.Of(lead).Sent.Count == 2, "the lead to start again");
        Assert.Equal(MissionRunner.ContinueText, rig.Agents.Of(lead).Sent[1]);
        MissionRig.Until(() => !rig.Now(record).AgentsReleased && rig.Now(record).Waiting is null, "the cold start to end");
        Assert.True(rig.Rig.Gate.Holds(lead));
    }

    [Fact]
    public void A_task_paused_for_less_than_ten_minutes_keeps_its_agents_and_a_turn_waiting_on_a_card_is_never_cut()
    {
        var rig = Rig();
        var record = rig.Started();
        var lead = record.LeadThread!.Value;
        rig.Runner.OnApproval(lead, ControlsRig.Approval("a1", "npm test"));
        Assert.Null(rig.Controls.Pause(record.Id)); // Needs you pauses at once, though the provider's turn waits on the card
        Assert.Equal(MissionState.Paused, rig.Now(record).Mission.State);

        rig.Time.Advance(TimeSpan.FromMinutes(10));
        Assert.False(rig.Now(record).AgentsReleased);
        Assert.Equal(0, rig.Agents.Of(lead).SessionStops);
        // It looks again every minute, and lets go once the turn is over.
        rig.Agents.Finish(lead, null);
        rig.Time.Advance(TimeSpan.FromMinutes(1));
        Assert.True(rig.Now(record).AgentsReleased);
        Assert.Equal(1, rig.Agents.Of(lead).SessionStops);
    }

    // ---- Failed, Revert, Restart ----

    [Fact]
    public void A_failed_task_offers_Restart_and_Stop_and_Restart_begins_from_the_brief_on_a_fresh_branch_while_the_failed_one_stays_for_seven_days()
    {
        var rig = Rig();
        var failed = Journaled(MissionState.Failed);
        rig.Runner.Load([failed]);
        var slots = rig.Runner.Find(failed.Id)!.Mission.Slots;
        Assert.Equal((SlotA.Restart, true, SlotB.Stop, true), (slots.A, slots.AEnabled, slots.B, slots.BEnabled));
        Assert.Equal("Failed", MissionStates.Label(MissionState.Failed));

        Assert.Null(rig.Controls.Restart(failed.Id));

        MissionRig.Until(() => rig.Runner.Find(failed.Id)!.LeadThread is not null, "the restarted lead");
        var again = rig.Runner.Find(failed.Id)!;
        Assert.Equal(2, again.Attempt);
        Assert.Equal(MissionState.Working, again.Mission.State);
        Assert.EndsWith("-r2", again.Mission.Branch, StringComparison.Ordinal);
        Assert.NotEqual(failed.Workspace!.Path, again.Workspace!.Path);
        Assert.Contains(failed.Brief, rig.Agents.Of(again.LeadThread!.Value).Launch.Brief, StringComparison.Ordinal);
        Assert.Null(again.Reason);
        Assert.Equal(0, again.Mission.ReviewRounds);
        var previous = Assert.Single(again.Previous);
        Assert.Equal(failed.Workspace.Branch, previous.Workspace.Branch);
        Assert.Equal(["create " + failed.ProjectPath], rig.Rig.Workspaces.Calls);

        // The failed branch is swept 7 days after it was set aside, not before.
        rig.Time.Advance(TimeSpan.FromDays(6.9));
        Assert.True(MissionRig.Within(rig.Runner.CleanUpWorkspaces(), 10));
        Assert.Single(rig.Runner.Find(failed.Id)!.Previous);
        rig.Time.Advance(TimeSpan.FromDays(0.2));
        Assert.True(MissionRig.Within(rig.Runner.CleanUpWorkspaces(), 10));
        Assert.Empty(rig.Runner.Find(failed.Id)!.Previous);
        Assert.Contains("discard", rig.Rig.Workspaces.Calls);
    }

    [Fact]
    public void Stop_on_a_failed_task_keeps_its_branch_and_leaves_Restart_not_Resume()
    {
        var rig = Rig();
        var failed = Journaled(MissionState.Failed);
        rig.Runner.Load([failed]);

        Assert.Null(rig.Controls.Stop(failed.Id));
        var stopped = rig.Runner.Find(failed.Id)!;
        Assert.Equal(MissionState.Stopped, stopped.Mission.State);
        Assert.True(stopped.Mission.StoppedAfterFailure);
        Assert.NotNull(stopped.Workspace);
        Assert.Equal((SlotA.Restart, SlotB.RevertChanges), (stopped.Mission.Slots.A, stopped.Mission.Slots.B));
        Assert.Equal("It failed: Restart begins again from the brief.", rig.Controls.Resume(failed.Id));
        Assert.Null(rig.Controls.Restart(failed.Id));
        MissionRig.Until(() => rig.Runner.Find(failed.Id)!.LeadThread is not null, "the restarted lead");
    }

    [Fact]
    public async Task Revert_takes_the_work_out_keeps_it_restorable_and_withdraws_the_open_cards_then_Restart_begins_again()
    {
        var rig = Rig();
        var stopped = Journaled(MissionState.Stopped);
        rig.Runner.Load([stopped]);
        var question = rig.Queue.Raise(new DecisionItem(Guid.NewGuid(), DecisionKind.Question, DecisionWeight.Question, "Which port?")
        {
            AssistantId = "dextra",
            MissionId = stopped.Id,
        }, (_, _) => { });

        Assert.Null(await rig.Controls.Revert(stopped.Id));

        var reverted = rig.Runner.Find(stopped.Id)!;
        Assert.Equal(["revert"], rig.Rig.Workspaces.Calls);
        Assert.Equal(stopped.Workspace, rig.Rig.Workspaces.Reverted.Single());
        Assert.True(reverted.Mission.Reverted);
        Assert.Equal(0, reverted.Mission.ChangedFiles);
        Assert.Null(reverted.Workspace);
        Assert.StartsWith("refs/nth/reverted/" + stopped.Id.ToString("N"), reverted.RevertedWork!.Ref, StringComparison.Ordinal);
        Assert.Equal(3, reverted.RevertedWork.ChangedFiles);
        Assert.Equal(DecisionState.Withdrawn, rig.Queue.Find(question.Id)!.State);
        Assert.Equal("Withdrawn by Revert changes.", rig.Queue.Find(question.Id)!.Note);
        Assert.Equal((SlotA.Restart, true, SlotB.RevertChanges, false), (reverted.Mission.Slots.A, reverted.Mission.Slots.AEnabled, reverted.Mission.Slots.B, reverted.Mission.Slots.BEnabled));
        Assert.Equal("Its changes were reverted: Restart begins again from the brief.", rig.Controls.Resume(stopped.Id));
        Assert.Contains(rig.Rig.Audit, e => e.Tool == "revert" && e.Result!.StartsWith("reverted 3 changed files; kept until 2026-", StringComparison.Ordinal));
        Assert.Equal("Revert changes is only on a stopped task that changed files.", await rig.Controls.Revert(stopped.Id));

        // The Activity log restores it within 7 days.
        Assert.Null(await rig.Controls.RestoreReverted(stopped.Id));
        var back = rig.Runner.Find(stopped.Id)!;
        Assert.False(back.Mission.Reverted);
        Assert.Equal(3, back.Mission.ChangedFiles);
        Assert.Equal(stopped.Workspace, back.Workspace);
        Assert.Null(back.RevertedWork);
        Assert.Equal("Only a stopped task whose changes were reverted can have them restored.", await rig.Controls.RestoreReverted(stopped.Id));

        // Reverted again, then restarted from the brief: the work stays kept until the sweep drops it.
        Assert.Null(await rig.Controls.Revert(stopped.Id));
        Assert.Null(rig.Controls.Restart(stopped.Id));
        MissionRig.Until(() => rig.Runner.Find(stopped.Id)!.LeadThread is not null, "the restarted lead");
        var restarted = rig.Runner.Find(stopped.Id)!;
        Assert.Equal(2, restarted.Attempt);
        Assert.NotNull(restarted.RevertedWork);
        Assert.Empty(restarted.Previous);
        Assert.Equal("Only a stopped task whose changes were reverted can have them restored.", await rig.Controls.RestoreReverted(stopped.Id));
        rig.Time.Advance(TimeSpan.FromDays(7));
        await rig.Runner.CleanUpWorkspaces();
        Assert.Null(rig.Runner.Find(stopped.Id)!.RevertedWork);
        Assert.Contains("drop", rig.Rig.Workspaces.Calls);
    }

    [Fact]
    public void A_revert_nth_ended_in_is_completed_at_the_next_start_from_what_it_left_and_never_run_again_blind()
    {
        var rig = Rig();
        var begun = Journaled(MissionState.Stopped) with { RevertIntent = true };
        rig.Rig.Workspaces.FoundReverted = new RevertedWork(begun.Workspace!, "refs/nth/reverted/" + begun.Id.ToString("N") + "/20260922T120000000Z", "c0ffee", Long, 0);

        rig.Runner.Load([begun]);

        MissionRig.Until(() => rig.Runner.Find(begun.Id)!.RevertedWork is not null, "the interrupted revert to be completed");
        var done = rig.Runner.Find(begun.Id)!;
        Assert.True(done.Mission.Reverted);
        Assert.Equal(0, done.Mission.ChangedFiles);
        Assert.Null(done.Workspace);
        Assert.False(done.RevertIntent);
        Assert.Equal(3, done.RevertedWork!.ChangedFiles); // the record knows how many files; the ref does not
        Assert.Equal(["find reverted"], rig.Rig.Workspaces.Calls); // it was looked for, not done again
        Assert.Contains(rig.Rig.Audit, e => e.Tool == "revert");
    }

    [Fact]
    public void A_revert_that_had_done_nothing_when_nth_ended_clears_its_intent_and_leaves_the_task_as_it_was()
    {
        var rig = Rig();
        var begun = Journaled(MissionState.Stopped) with { RevertIntent = true };
        rig.Rig.Workspaces.FoundReverted = null;

        rig.Runner.Load([begun]);

        MissionRig.Until(() => !rig.Runner.Find(begun.Id)!.RevertIntent, "the intent to be cleared");
        var same = rig.Runner.Find(begun.Id)!;
        Assert.False(same.Mission.Reverted);
        Assert.Equal(3, same.Mission.ChangedFiles);
        Assert.NotNull(same.Workspace);
        Assert.Null(same.RevertedWork);
        Assert.Equal(["find reverted"], rig.Rig.Workspaces.Calls);
    }

    [Fact]
    public async Task Revert_that_fails_changes_nothing_and_is_hidden_on_every_task_that_is_not_stopped_with_changes()
    {
        var rig = Rig();
        var stopped = Journaled(MissionState.Stopped);
        var untouched = Journaled(MissionState.Stopped, changedFiles: 0);
        var working = rig.Started();
        rig.Runner.Load([stopped, untouched]);
        rig.Rig.Workspaces.RevertFails = new MissionWorkspaceError("The repository is locked.");

        Assert.Equal("Not reverted: The repository is locked.", await rig.Controls.Revert(stopped.Id));
        var same = rig.Runner.Find(stopped.Id)!;
        Assert.False(same.Mission.Reverted);
        Assert.Equal(3, same.Mission.ChangedFiles);
        Assert.NotNull(same.Workspace);
        Assert.Null(same.RevertedWork);
        rig.Rig.Workspaces.RevertFails = null;

        // Hidden on a working task, and on a stopped one that changed nothing: the control's ask cannot open (r4 B1).
        Assert.Equal("Revert changes is only on a stopped task that changed files.", await rig.Controls.Revert(working.Id));
        Assert.Equal("Revert changes is only on a stopped task that changed files.", await rig.Controls.Revert(untouched.Id));
        Assert.False(rig.Runner.Find(untouched.Id)!.Mission.Slots.BEnabled);
        Assert.Single(rig.Rig.Workspaces.Calls, c => c == "revert"); // only the failing try ever reached the workspaces
        Assert.Equal("There is no such mission.", await rig.Controls.Revert(Guid.NewGuid()));
    }
}
