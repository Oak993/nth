using Marrow.App.Assistant;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;
using Marrow.Services.Port.Usage;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;
using static Marrow.App.Tests.Fixtures.ReserveRig;

namespace Marrow.App.Tests.Assistant;

// Dextra AS-13 (DESIGN.md rev 5, 11.3 to 11.5; ED-NEW-dextra-as13-9): the reserve guard over the mission runner and fake chats. Before
// each turn it checks estimate + this turn's p90 + the margin against the floor; a projection that crosses pauses the mission at the
// turn boundary (a running turn is never cut) and puts the four options in the queue; the answers act. The rig's account has a rate
// of 100,000 tokens per percent and a new turn counts 1.9743 points (its p90 1.3162 times the 1.5 tail allowance); a whole-point
// reading adds a point (ED-NEW-dextra-as13-5). A spend is counted when it is after the reading it follows, so each test lets a
// second pass before the ledger speaks.
[Trait("Decision", "ED-NEW-dextra-as13-9")]
[Trait("Feature", "AS-13")]
public sealed class ReserveGuardTests : IDisposable
{
    private readonly AssistantTempRoot temp = new();

    public void Dispose() => temp.Dispose();

    private ReserveRig Rig(FakeMover? mover = null) => new(temp.Folder, mover);

    private static Guid LeadOf(ReserveRig rig, MissionRecord record) => rig.Missions.Agents.Lead(record).Id;

    private static void EndTurn(ReserveRig rig, Guid thread, string? answer)
    {
        rig.Missions.Agents.Finish(thread, answer);
        rig.Missions.Runner.OnTurnFinished(thread, TurnStatus.Completed, continues: false);
    }

    private static void Second(ReserveRig rig) => rig.Clock.Advance(TimeSpan.FromSeconds(1));

    // ---- before a turn ----

    [Fact]
    public void A_mission_with_room_starts_and_raises_no_card()
    {
        using var rig = Rig();
        rig.Always(30);
        rig.Read(60);                                              // 60 + 1.974 + 1 + 1 = 63.97, under the floor of 70
        var record = rig.Started();
        Assert.Equal(MissionState.Working, record.Mission.State);
        Assert.NotNull(record.LeadThread);
        Assert.Null(rig.Card(record));
        Assert.Empty(rig.Holds);
        Assert.Null(rig.Guard.HoldOf(record.Id));
    }

    [Fact]
    public void A_mission_without_room_waits_Paused_before_its_lead_starts_with_the_card_and_the_toast()
    {
        using var rig = Rig();
        rig.Always(30);
        rig.Read(68);                                              // 68 + 1.974 + 1 + 1 = 71.97, over the floor of 70
        var record = rig.Started();

        Assert.Equal(MissionState.Paused, record.Mission.State);
        Assert.Equal(PauseCause.Reserve, record.Mission.PausedBy);
        Assert.Equal(MissionState.Planning, record.Mission.ResumeTo);
        Assert.Null(record.LeadThread);
        Assert.Empty(rig.Missions.Agents.Chats);                   // nothing was sent
        Assert.Equal(0, rig.Missions.Gate.Running);                // its slot was given back
        Assert.Equal(BoardGroup.NeedsYou, record.Mission.Group);
        Assert.Equal("Paused before your Claude 20x reserve", record.Waiting);

        var card = Assert.IsType<DecisionItem>(rig.Card(record));
        Assert.Equal(DecisionKind.Reserve, card.Kind);
        Assert.Equal("Fix the login timeout would touch your Claude 20x reserve", card.Title);
        Assert.Equal("Standing rule: always keep 30% on Claude 20x", card.Why);
        Assert.Equal(["Account", "Window", "Used now", "Your floor", "The next step needs about", "Where it stands"], card.Inputs.Select(i => i.Name));
        Assert.Equal(["Claude 20x", "5-hour window", "68%", "70%", "2%", "Claude 20x is at 68% of its 5-hour window; your floor is 70%. The next step needs about 2% more."],
            card.Inputs.Select(i => i.Value));
        Assert.Equal(ReserveGuard.PausedNote, card.Note);
        Assert.Equal(record.Id, card.MissionId);
        Assert.Equal([DecisionAnswerKind.Pick], card.Answers);     // four options and nothing to type

        var hold = Assert.Single(rig.Holds);
        Assert.Equal("Paused before your reserve. Claude 20x is at 68%; your floor is 70%.", hold.Toast);
        Assert.Equal("Paused before your Claude 20x reserve", hold.Line);
        Assert.Equal(card.Id, hold.Card);
        Assert.Equal(record.Id, hold.Mission);
        Assert.Contains(rig.Audit, e => e.Tool == "reserve_hold" && e.Mission == record.Id.ToString("D") && e.Result!.Contains("floor 70%", StringComparison.Ordinal));
    }

    [Fact]
    public void The_card_offers_wait_use_once_and_stop_and_with_a_mover_moving_the_rest_first_in_the_canvas_words()
    {
        var reset = T0.AddHours(3);
        using (var bare = Rig())
        {
            bare.Always(30);
            bare.Read(68);
            var held = bare.Started();
            Assert.Equal([$"Wait for the reset at {Clock24(reset)}", "Use 2% of the reserve once", "Stop and give me what you have"], bare.Card(held)!.Options);
        }
        var mover = new FakeMover { Offering = new ReserveMoveOffer(CodexA, 3.2) };
        using var rig = Rig(mover);
        rig.Always(30);
        rig.Read(40, account: CodexA);                             // room on Codex A
        rig.Read(68);
        var record = rig.Started();
        Assert.Equal(["Move the rest to Codex A (≈4%)", $"Wait for the reset at {Clock24(reset)}", "Use 2% of the reserve once", "Stop and give me what you have"],
            rig.Card(record)!.Options);
        var seen = Assert.Single(Assert.Single(mover.Seen));
        Assert.Equal(CodexA, seen.Account);                        // the account with room was put to the mover
        Assert.True(seen.Room > 0);
    }

    [Fact]
    public void A_model_the_owner_pinned_is_never_offered_a_silent_switch()
    {
        var mover = new FakeMover { Offering = new ReserveMoveOffer(CodexA, 3.2) };
        using var rig = Rig(mover);
        rig.Pinned = _ => true;
        rig.Always(30);
        rig.Read(40, account: CodexA);
        rig.Read(68);
        var record = rig.Started();
        Assert.DoesNotContain(rig.Card(record)!.Options, o => o.StartsWith("Move", StringComparison.Ordinal));
        Assert.Empty(mover.Seen);                                  // the mover is not even asked
    }

    // ---- the four answers ----

    [Fact]
    public void Using_some_of_the_reserve_once_lets_the_mission_start_and_the_card_is_answered()
    {
        using var rig = Rig();
        rig.Always(30);
        rig.Read(68);
        var record = rig.Started();
        var card = rig.Card(record)!;
        Assert.Null(rig.Pick(card, ReserveOptionKind.UseOnce));
        var now = rig.Missions.Now(record);
        Assert.Equal(MissionState.Working, now.Mission.State);
        Assert.Null(now.Mission.PausedBy);
        Assert.NotNull(now.LeadThread);
        Assert.Null(now.Waiting);
        Assert.Single(rig.Missions.Agents.Chats);
        Assert.Equal(DecisionState.Answered, rig.Missions.Queue.Find(card.Id)!.State);
        Assert.Null(rig.Guard.HoldOf(record.Id));
        Assert.Contains(rig.Audit, e => e.Tool == "reserve_answer" && e.Result == "Use 2% of the reserve once");
    }

    [Fact]
    public void Stopping_gives_the_owner_what_there_is()
    {
        using var rig = Rig();
        rig.Always(30);
        rig.Read(68);
        var record = rig.Started();
        Assert.Null(rig.Pick(rig.Card(record)!, ReserveOptionKind.Stop));
        Assert.Equal(MissionState.Stopped, rig.Missions.Now(record).Mission.State);
        Assert.Empty(rig.Missions.Agents.Chats);
        Assert.Null(rig.Guard.HoldOf(record.Id));
    }

    [Fact]
    public void Moving_the_rest_runs_the_mission_on_the_other_account_and_a_move_that_fails_asks_again_with_the_reason()
    {
        var mover = new FakeMover { Offering = new ReserveMoveOffer(CodexA, 3.2), Refusal = "Codex A is signed out" };
        using var rig = Rig(mover);
        rig.Always(30);
        rig.Read(30, account: CodexA);
        rig.Read(68);
        var record = rig.Started();
        var first = rig.Card(record)!;

        Assert.Null(rig.Pick(first, ReserveOptionKind.Move));       // the owner's tap is taken; the move fails
        Assert.Single(mover.Moved);
        Assert.DoesNotContain(rig.Audit, e => (e.Result ?? "").Contains("signed out", StringComparison.Ordinal));   // the reason is for the card, not the log
        Assert.Contains(rig.Audit, e => e.Tool == "reserve_answer" && e.Result!.EndsWith(": did not work", StringComparison.Ordinal));
        var again = rig.Missions.Queue.Of(record.Id).Single(c => c.Kind == DecisionKind.Reserve);
        Assert.NotEqual(first.Id, again.Id);
        Assert.Equal("Moving the rest did not work: Codex A is signed out. " + ReserveGuard.PausedNote, again.Note);
        Assert.Equal(MissionState.Paused, rig.Missions.Now(record).Mission.State);
        Assert.Empty(rig.Missions.Agents.Chats);

        mover.Refusal = null;
        rig.DefaultAccount = CodexA;                               // the real mover points the mission's chats at the other account
        Assert.Null(rig.Pick(again, ReserveOptionKind.Move));
        Assert.Equal(2, mover.Moved.Count);
        Assert.Equal(MissionState.Working, rig.Missions.Now(record).Mission.State);
        Assert.Single(rig.Missions.Agents.Chats);
        Assert.Null(rig.Guard.HoldOf(record.Id));
    }

    [Fact]
    public void Nothing_a_mission_s_title_or_a_mover_s_error_says_reaches_the_activity_log()
    {
        const string Sentinel = "sk-SENTINEL-0123456789abcdef";
        var mover = new FakeMover { Offering = new ReserveMoveOffer(CodexA, 3.2), Refusal = "token " + Sentinel + " was refused" };
        using var rig = Rig(mover);
        rig.Always(30);
        rig.Read(30, account: CodexA);
        rig.Read(68);
        var record = rig.Missions.Started(rig.Missions.Request("Use " + Sentinel + " to log in"));
        Assert.Null(rig.Pick(rig.Card(record)!, ReserveOptionKind.Move));
        Assert.Null(rig.Pick(rig.Missions.Queue.Of(record.Id).Single(c => c.Kind == DecisionKind.Reserve), ReserveOptionKind.Wait));
        Assert.NotEmpty(rig.Audit);
        Assert.All(rig.Audit, e => Assert.DoesNotContain(Sentinel, string.Join(" ", e.Tool, e.Target, e.Result, e.Mission, e.Assistant, string.Join(" ", e.Inputs.Select(i => i.ToString()))), StringComparison.Ordinal));
        Assert.All(rig.Audit, e => Assert.Equal(record.Id.ToString("D"), e.Mission));   // each line names its mission, by id
    }

    [Fact]
    public void Waiting_for_the_reset_resumes_the_mission_once_a_reading_shows_the_new_window()
    {
        using var rig = Rig();
        rig.Always(30);
        rig.Read(68);
        var record = rig.Started();
        var reset = T0.AddHours(3);
        Assert.Null(rig.Pick(rig.Card(record)!, ReserveOptionKind.Wait));
        Assert.Equal($"Waiting for the Claude 20x reset at {Clock24(reset)}", rig.Missions.Now(record).Waiting);
        Assert.Equal(MissionState.Paused, rig.Missions.Now(record).Mission.State);
        Assert.Equal(1, rig.Clock.ActiveTimers);

        // At the reset the timer has nothing newer than the old reading: it asks for a fresh one, and tries again in 30 s.
        rig.Clock.Advance(TimeSpan.FromHours(3) + TimeSpan.FromSeconds(59));
        Assert.Empty(rig.Refreshed);                               // AutoContinue's margin of 60 s after the reset
        rig.Clock.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal([Claude20], rig.Refreshed);
        Assert.Equal(MissionState.Paused, rig.Missions.Now(record).Mission.State);
        rig.Clock.Advance(ReserveGuard.WaitRecheck);
        Assert.Equal(2, rig.Refreshed.Count);

        // The poll after the reset shows the new window: the mission goes on.
        rig.Read(2, resets: T0.AddHours(8));
        var now = rig.Missions.Now(record);
        Assert.Equal(MissionState.Working, now.Mission.State);
        Assert.NotNull(now.LeadThread);
        Assert.Null(now.Waiting);
        Assert.Equal(0, rig.Clock.ActiveTimers);
        Assert.Contains(rig.Replans, r => r.Line.EndsWith("reset · re-planned · resumed", StringComparison.Ordinal));
        Assert.Contains(rig.Audit, e => e.Tool == "reserve_wait" && e.Result == "window reset, resumed");
    }

    [Fact]
    public void After_the_reset_a_floor_that_still_holds_asks_again()
    {
        using var rig = Rig();
        rig.Always(30);
        rig.Read(68, weekly: 40);
        var record = rig.Started();
        Assert.Null(rig.Pick(rig.Card(record)!, ReserveOptionKind.Wait));
        rig.Clock.Advance(TimeSpan.FromHours(3) + TimeSpan.FromSeconds(61));
        rig.Read(2, weekly: 69, resets: T0.AddHours(8));            // the 5-hour window reset; the weekly one is at 69 against its floor of 70
        var asked = rig.Missions.Queue.Of(record.Id).Single(c => c.Kind == DecisionKind.Reserve);
        Assert.StartsWith("Claude 20x's 5-hour window reset, but your floor still holds.", asked.Note, StringComparison.Ordinal);
        Assert.Equal("weekly window", asked.Inputs.Single(i => i.Name == "Window").Value);
        Assert.Equal(MissionState.Paused, rig.Missions.Now(record).Mission.State);
        Assert.Contains(rig.Audit, e => e.Tool == "reserve_wait" && e.Result == "window reset, floor still holds");
    }

    [Fact]
    public void A_wait_for_a_reading_that_never_comes_gives_up_after_40_tries_and_the_mission_goes_on_once_the_reset_has_passed()
    {
        using var rig = Rig();
        rig.Always(30);
        rig.Read(68);
        var record = rig.Started();
        Assert.Null(rig.Pick(rig.Card(record)!, ReserveOptionKind.Wait));
        rig.Clock.Advance(TimeSpan.FromHours(3) + TimeSpan.FromSeconds(61));
        for (var i = 0; i < ReserveGuard.WaitTries - 1; i++) rig.Clock.Advance(ReserveGuard.WaitRecheck);
        Assert.Equal(MissionState.Paused, rig.Missions.Now(record).Mission.State);     // still trying
        rig.Clock.Advance(ReserveGuard.WaitRecheck);                                    // the 41st look: it stops asking
        // The window's reset time has passed, so its standing floor has eased to none: the owner's choice stands and the mission goes on.
        Assert.Equal(MissionState.Working, rig.Missions.Now(record).Mission.State);
        Assert.Contains(rig.Audit, e => e.Tool == "reserve_wait" && e.Result == "no reading after the reset");
        Assert.Equal(0, rig.Clock.ActiveTimers);
        Assert.Equal(ReserveGuard.WaitTries, rig.Refreshed.Count);            // it asked for a fresh reading each time it waited
    }

    // ---- never mid-turn ----

    [Fact]
    public void A_running_turn_is_never_cut_the_mission_pauses_at_its_boundary_and_sends_nothing_more()
    {
        using var rig = Rig();
        rig.Always(30);
        rig.Read(55);
        var record = rig.Started();
        var lead = LeadOf(rig, record);
        Second(rig);

        // Spend moves the estimate between polls: 55 + 9 = 64, + 4 = 68 (at the floor with the margin and the quantum), + 1 = 69.
        rig.Guard.OnSpend(lead, Model, 900_000);
        rig.Guard.OnSpend(lead, Model, 400_000);
        Assert.Equal(MissionState.Working, rig.Missions.Now(record).Mission.State);
        Assert.Null(rig.Card(record));
        rig.Guard.OnSpend(lead, Model, 100_000);

        var pausing = rig.Missions.Now(record);
        Assert.Equal(MissionState.Pausing, pausing.Mission.State);
        Assert.Equal(PauseCause.Reserve, pausing.Mission.PausedBy);
        var chat = rig.Missions.Agents.Of(lead);
        Assert.True(chat.Running);                                  // the turn goes on
        Assert.Equal(0, chat.Interrupts);                           // nothing was interrupted
        var card = Assert.IsType<DecisionItem>(rig.Card(record));
        Assert.Contains("Use 1% of the reserve once", card.Options);
        Assert.Single(chat.Sent);                                   // nothing new was sent to it

        // Its turn ends: now it is Paused, in Needs you, and still nothing is sent.
        EndTurn(rig, lead, "Half done.");
        var paused = rig.Missions.Now(record);
        Assert.Equal(MissionState.Paused, paused.Mission.State);
        Assert.Equal(BoardGroup.NeedsYou, paused.Mission.Group);
        Assert.Single(chat.Sent);
        Assert.Equal(0, chat.Interrupts);
        Assert.Empty(rig.Missions.Agents.Refused);

        // The poll shows room again, and the owner uses a point of the reserve: the mission goes on to its review.
        Second(rig);
        rig.Read(50);
        Assert.Null(rig.Pick(card, ReserveOptionKind.UseOnce));
        Assert.Equal(MissionState.Reviewing, rig.Missions.Now(record).Mission.State);
        Assert.NotNull(rig.Missions.Now(record).ReviewerThread);
    }

    [Fact]
    public void A_poll_that_shows_the_owner_s_own_use_pauses_a_running_mission_the_same_way()
    {
        using var rig = Rig();
        rig.Always(30);
        rig.Read(50);
        var record = rig.Started();
        rig.Clock.Advance(TimeSpan.FromSeconds(60));
        rig.Read(66);                                              // 16 points the ledger never saw: outside nth
        Assert.Null(rig.Card(record));                              // 66 + 1.316 (what its turn has left) + 1 = 68.3
        rig.Clock.Advance(TimeSpan.FromSeconds(60));
        rig.Read(69);                                              // 3 more: 69 + 1.316 + 1 = 71.3
        var card = Assert.IsType<DecisionItem>(rig.Card(record));
        Assert.Equal(MissionState.Pausing, rig.Missions.Now(record).Mission.State);
        var facts = card.Inputs.Single(i => i.Name == "Where it stands").Value;
        Assert.Equal($"Claude 20x is at 69% of its 5-hour window; your floor is 70%. It was 50% at {Clock24(T0)}: this task used 0%, and 19% was used outside nth. The next step needs about 2% more.", facts);
        Assert.Equal(19, rig.Guard.Bars().Single().OutsidePoints, 6);
    }

    // ---- a new agent's first turn ----

    [Fact]
    public void A_head_whose_account_has_no_room_waits_while_its_mission_is_paused_and_starts_when_the_owner_lets_it_go_on()
    {
        using var rig = Rig();
        rig.Always(30);
        rig.Read(55);
        var record = rig.Started();
        var lead = LeadOf(rig, record);
        Second(rig);
        rig.Read(66);                                               // between turns the mission fits (66 + 1.316 + 1 + 1 = 69.3) ...
        Assert.Null(rig.Card(record));
        var head = Guid.NewGuid();
        rig.Missions.Runner.HeadAdded(lead, head, "Tests");

        var admit = rig.Missions.Runner.AdmitHead(head, ProviderKind.Claude);   // ... but a new agent's turn does not (70.29)
        Assert.False(admit.IsCompleted);
        var card = Assert.IsType<DecisionItem>(rig.Card(record));
        var held = rig.Missions.Now(record);
        Assert.Equal(MissionState.Paused, held.Mission.State);       // (a task with heads pauses by its heads; the lead's own turn goes on)
        Assert.Equal(PauseCause.Reserve, held.Mission.PausedBy);
        Assert.Equal(0, rig.Missions.Agents.Of(lead).Interrupts);
        Assert.NotEqual(HeadState.Working, held.Mission.Heads.Single().State);
        Assert.False(rig.Missions.Gate.Holds(head));                 // it took no slot and started no chat

        Assert.Null(rig.Pick(card, ReserveOptionKind.UseOnce));      // the owner uses a point of the reserve
        Assert.True(MissionRig.Within(admit, 10));
        MissionRig.Until(() => rig.Missions.Now(record).Mission.Heads.Single().State == HeadState.Working, "the head to start");
        Assert.True(rig.Missions.Gate.Holds(head));
        Assert.Equal(MissionState.Working, rig.Missions.Now(record).Mission.State);
    }

    [Fact]
    public void A_head_that_waits_for_the_reserve_stops_waiting_when_its_mission_is_stopped()
    {
        using var rig = Rig();
        rig.Always(30);
        rig.Read(55);
        var record = rig.Started();
        var lead = LeadOf(rig, record);
        Second(rig);
        rig.Read(66);
        var head = Guid.NewGuid();
        rig.Missions.Runner.HeadAdded(lead, head, "Tests");
        var admit = rig.Missions.Runner.AdmitHead(head, ProviderKind.Claude);
        Assert.False(admit.IsCompleted);

        Assert.Null(rig.Missions.Runner.Stop(record.Id));
        Assert.True(MissionRig.Within(admit, 10));                    // it no longer waits
        Assert.NotEqual(HeadState.Working, rig.Missions.Now(record).Mission.Heads.Single().State);
        Assert.False(rig.Missions.Gate.Holds(head));                  // and it took no slot on its way out
    }

    // ---- the margin, the ledger, the windows ----

    [Fact]
    public void Turns_already_in_flight_count_in_the_margin_of_the_next_mission()
    {
        using var rig = Rig();
        rig.Always(30);
        rig.Read(66);
        var one = rig.Started("one");                               // 66 + 1.974 + 1 + 1 = 69.97: at the floor, not over it
        Assert.Null(rig.Card(one));
        var two = rig.Started("two");                               // now one turn is in flight: margin 1.316, so 70.29
        Assert.Equal(MissionState.Paused, two.Mission.State);
        Assert.NotNull(rig.Card(two));
        Assert.Equal(1, rig.Missions.Gate.Running);
        // The first mission's turn ends and its review starts; the owner uses a point of the reserve for the second.
        EndTurn(rig, LeadOf(rig, one), "Done.");
        Assert.Null(rig.Pick(rig.Card(two)!, ReserveOptionKind.UseOnce));
        Assert.Equal(MissionState.Working, rig.Missions.Now(two).Mission.State);
    }

    [Fact]
    public void Between_polls_the_ledger_moves_the_estimate_for_every_chat_on_the_account_even_one_that_is_not_a_mission_s()
    {
        using var rig = Rig();
        rig.Always(30);
        rig.Read(60);
        Second(rig);
        var other = Guid.NewGuid();                                  // the owner's own chat on the same account
        rig.Guard.OnSpend(other, Model, 600_000);                    // 60 + 6 = 66: 66 + 1.974 + 1 + 1 = 69.97, still room
        Assert.Null(rig.Card(rig.Started("fits")));
        rig.Guard.OnSpend(other, Model, 100_000);                    // 67: 70.97 now crosses
        Assert.NotNull(rig.Card(rig.Started("does not")));
        // The next poll is the truth and replaces the estimate.
        rig.Clock.Advance(TimeSpan.FromSeconds(60));
        rig.Read(61);
        Assert.Equal(61, rig.Guard.Bars().Single().Windows.Single().Used, 6);
        Assert.Null(rig.Card(rig.Started("fits again")));
    }

    [Fact]
    public void Both_windows_are_checked_and_the_tighter_one_binds()
    {
        using var rig = Rig();
        rig.Always(30);
        rig.Read(50, weekly: 69);                                  // 69 + 1.974 + 1 + 1 = 72.97 against 70
        var record = rig.Started();
        var card = Assert.IsType<DecisionItem>(rig.Card(record));
        Assert.Equal("weekly window", card.Inputs.Single(i => i.Name == "Window").Value);
        Assert.Equal("69%", card.Inputs.Single(i => i.Name == "Used now").Value);
        Assert.Equal(ReserveWindowKind.Weekly, rig.Holds.Single().Kind);
        // The weekly window has no calibration here, so its conservative default prices the turn: 69 + 0.33 + 1 + 1 = 71.3.
        Assert.Equal("Use 2% of the reserve once", card.Options.Single(o => o.StartsWith("Use", StringComparison.Ordinal)));
    }

    [Fact]
    public void A_standing_floor_eases_over_the_last_45_minutes_unless_the_owner_holds_reserves()
    {
        using var rig = Rig();
        rig.Always(30);
        rig.Read(72, resets: T0.AddMinutes(10));                   // the floor is 70 and eased to 93.3 with 10 of 45 minutes left
        var easing = rig.Guard.Bars().Single().Windows.Single();
        Assert.True(easing.Eased);
        Assert.Equal(70.0, easing.Unadjusted!.Value, 6);
        Assert.InRange(easing.Floor!.Value, 93.2, 93.4);
        Assert.Null(rig.Card(rig.Started("eased")));                // 72 + 1.974 + 2 = 75.97, under the eased floor

        Assert.True(rig.Settings.SetHoldReserves(true));            // "Hold reserves until the reset"
        var held = rig.Started("held");
        Assert.NotNull(rig.Card(held));
        Assert.Equal(70.0, rig.Guard.Bars().Single().Windows.Single().Floor!.Value, 6);
    }

    [Fact]
    public void A_task_rule_is_exact_near_a_reset_and_binds_only_its_own_task()
    {
        using var rig = Rig();
        rig.Read(60, resets: T0.AddMinutes(10));
        var taskRule = new ReserveRule("t", ReserveScope.Every, ReserveLifetime.Task) { KeepPercent = 30 };   // 60 + 40 x 0.7 = 88, never eased
        var request = rig.Missions.Request("with a task rule") with { Reserve = new ReserveStart([taskRule], null, []) };
        var withRule = rig.Missions.Started(request);
        Assert.Null(rig.Card(withRule));                            // 60 + 1.974 + 2 = 63.97
        rig.Clock.Advance(TimeSpan.FromSeconds(1));
        rig.Read(86, resets: T0.AddMinutes(10));                    // 86 + 1.316 + 1 = 88.3 > 88: held, though the reset is near
        var card = Assert.IsType<DecisionItem>(rig.Card(withRule));
        Assert.Equal(MissionState.Pausing, rig.Missions.Now(withRule).Mission.State);
        Assert.Equal(88.0, rig.Holds.Single().Floor, 6);
        Assert.Equal("Your rule for this task: keep 30% of what's left", card.Why);

        // Another task has no task rule and no standing rule: nothing binds it.
        Assert.Null(rig.Card(rig.Started("free")));
    }

    [Fact]
    public void A_rule_added_while_missions_run_pauses_the_ones_it_would_cross()
    {
        using var rig = Rig();
        rig.Read(68);
        var record = rig.Started();                                 // no rule yet
        Assert.Equal(MissionState.Working, record.Mission.State);
        rig.Always(30);                                             // 68 + 1.316 + 1 + 1 = 71.3 > 70
        Assert.Equal(MissionState.Pausing, rig.Missions.Now(record).Mission.State);
        Assert.NotNull(rig.Card(record));
    }

    [Fact]
    public void Nothing_is_held_for_an_account_the_guard_cannot_read_or_cannot_place_or_no_rule_covers()
    {
        using (var unread = Rig())
        {
            unread.Always(30);
            Assert.Equal(MissionState.Working, unread.Started("no reading yet").Mission.State);   // no poll has been read
        }
        using (var unplaced = Rig())
        {
            unplaced.Always(30);
            unplaced.Read(99);
            unplaced.DefaultAccount = null;                         // the chat's account is not known
            Assert.Equal(MissionState.Working, unplaced.Started("no account").Mission.State);
            Assert.DoesNotContain(unplaced.Missions.Queue.Waiting, c => c.Kind == DecisionKind.Reserve);
        }
        using var uncovered = Rig();
        uncovered.Always(30);                                       // Claude 20x only
        uncovered.DefaultAccount = CodexA;
        uncovered.Read(99, account: CodexA);
        Assert.Equal(MissionState.Working, uncovered.Started("not covered").Mission.State);
        Assert.DoesNotContain(uncovered.Missions.Queue.Waiting, c => c.Kind == DecisionKind.Reserve);
    }

    // ---- the owner's hold stands, one card each, the log ----

    [Fact]
    public void The_owner_s_own_pause_stands_and_a_held_mission_is_not_asked_about_twice()
    {
        using var rig = Rig();
        rig.Always(30);
        rig.Read(55);
        var owner = rig.Started("owner paused");
        Assert.Null(rig.Missions.Runner.Pause(owner.Id));             // the owner's pause (the lead's turn runs: Pausing)
        Second(rig);
        rig.Guard.OnSpend(LeadOf(rig, owner), Model, 2_000_000);     // a spend that would hold it
        Assert.Null(rig.Card(owner));                                // his pause is the stronger: no card
        Assert.Equal(PauseCause.Owner, rig.Missions.Now(owner).Mission.PausedBy);

        var held = rig.Started("held");
        Assert.NotNull(rig.Card(held));
        var record = rig.Missions.Now(held);
        Assert.False(rig.Guard.BeforeTurn(record, null, ProviderKind.Claude, Model, ReserveTurns.Start));   // asked again: still no
        Assert.Single(rig.Missions.Queue.Of(held.Id), c => c.Kind == DecisionKind.Reserve);
    }

    [Fact]
    public void A_log_that_cannot_be_written_is_reported_and_the_hold_still_stands()
    {
        using var rig = Rig();
        rig.AuditFails = true;
        rig.Always(30);
        rig.Read(68);
        var record = rig.Started();
        Assert.NotNull(rig.Card(record));
        Assert.Contains(rig.Faults, f => f.StartsWith("The activity log could not be written", StringComparison.Ordinal));
        Assert.Equal(MissionState.Paused, rig.Missions.Now(record).Mission.State);
    }

    [Fact]
    public void A_stopped_task_s_card_is_held_and_its_wait_is_cancelled()
    {
        using var rig = Rig();
        rig.Always(30);
        rig.Read(68);
        var stopped = rig.Started("stopped");
        Assert.Null(rig.Missions.Runner.Stop(stopped.Id));
        Assert.Equal(DecisionState.Held, rig.Card(stopped)!.State);
        Assert.NotNull(rig.Pick(rig.Card(stopped)!, ReserveOptionKind.UseOnce));   // Held by Stop: not answerable

        var waiting = rig.Started("waiting");
        Assert.Null(rig.Pick(rig.Card(waiting)!, ReserveOptionKind.Wait));
        Assert.Equal(1, rig.Clock.ActiveTimers);
        Assert.Null(rig.Missions.Runner.Stop(waiting.Id));
        Assert.Equal(0, rig.Clock.ActiveTimers);                    // a stopped task waits for nothing
    }

    [Fact]
    public void The_cut_turn_of_a_stopped_task_asks_the_reserve_before_it_runs_again()
    {
        using var rig = Rig();
        rig.Always(30);
        rig.Read(55);
        var record = rig.Started();
        var lead = LeadOf(rig, record);
        Assert.Null(rig.Missions.Runner.Stop(record.Id));
        rig.Missions.Agents.Finish(lead, null);
        rig.Missions.Runner.CheckStopping();
        Assert.Equal(MissionState.Stopped, rig.Missions.Now(record).Mission.State);
        Second(rig);
        rig.Read(68);                                              // while it was stopped the account filled up
        Assert.Null(rig.Missions.Runner.Resume(record.Id));
        MissionRig.Until(() => rig.Card(record) is not null, "the reserve's card for the cut turn");
        var resumed = rig.Missions.Now(record);
        Assert.Equal(PauseCause.Reserve, resumed.Mission.PausedBy);
        Assert.Contains("cut", resumed.PendingRedirect ?? "", StringComparison.Ordinal);   // the text of the cut turn waits for the lead
        Assert.Single(rig.Missions.Agents.Of(lead).Sent);
    }

    // ---- the snapshot feed, the Reserves view ----

    [Fact]
    public void A_plan_limit_snapshot_becomes_readings_by_the_windows_own_length_and_the_view_shows_floors()
    {
        using var rig = Rig();
        rig.Always(30);
        var limits = new PlanLimits
        {
            Windows = new ValueList<PlanLimits.Window>([
                new PlanLimits.Window { Id = "five_hour", Title = "5-hour limit", Percent = 60, DurationMinutes = 300, ResetsAt = T0.AddHours(3) },
                new PlanLimits.Window { Id = "seven_day", Title = "Weekly · all models", Percent = 41, DurationMinutes = 10_080, ResetsAt = T0.AddDays(3) },
                new PlanLimits.Window { Id = "odd", Title = "Something else", Percent = 5, DurationMinutes = 77, ResetsAt = T0.AddDays(1) },
            ]),
        };
        rig.Guard.OnSnapshot(new AccountPlanLimitsSnapshot(new UsageAccountKey(ProviderKind.Claude, "20x"), limits, T0));
        rig.Guard.OnSnapshot(new AccountPlanLimitsSnapshot(new UsageAccountKey(ProviderKind.Claude, "20x"), null, T0.AddSeconds(60)));   // no limits shared: ignored

        var bar = Assert.Single(rig.Guard.Bars());
        Assert.Equal("Claude 20x", bar.Name);
        Assert.Equal([ReserveWindowKind.FiveHour, ReserveWindowKind.Weekly, ReserveWindowKind.Other], bar.Windows.Select(w => w.Kind));
        Assert.Equal(["5-hour limit", "Weekly · all models", "Something else"], bar.Windows.Select(w => w.Title));
        Assert.Equal([60.0, 41.0, 5.0], bar.Windows.Select(w => w.Used));
        Assert.Equal(new double?[] { 70.0, 70.0, null }, bar.Windows.Select(w => w.Floor));    // an unknown window is not protected
        Assert.False(bar.Rough);                                    // the rig calibrated this account
    }

    // ---- the plan, the re-plan, the after-report ----

    [Fact]
    public void The_plan_line_for_three_accounts_is_the_canvas_s_and_the_p90_is_checked_against_every_floor()
    {
        var b = new ReserveAccountRef("claude:b", "Claude B", "claude");
        using var rig = Rig();
        rig.Calibrate(b);
        rig.Read(22, account: CodexA);
        rig.Read(41, account: b);
        rig.Read(60);
        Assert.Null(rig.Settings.Add(new ReserveRule("", ReserveScope.Named("Claude 20x"), ReserveLifetime.Standing) { KeepPercent = 30 }));
        var task = new ReserveRule("t", ReserveScope.Every, ReserveLifetime.Task) { KeepPercent = 30 };
        PlanStep Step(ReserveAccountRef account, double p50, double p90) => new(account, Model, p50 * 100_000, p90 * 100_000);
        var steps = new[] { Step(CodexA, 12, 14), Step(b, 8, 10), Step(Claude20, 4, 6) };

        var plan = rig.Guard.Plan(null, steps, [task], agents: 4, duration: TimeSpan.FromMinutes(25), team: "Hydra team of 4");
        Assert.Equal("About 12% of Codex A, 8% of Claude B and up to 6% of Claude 20x. Your reserves stay.", plan.Line);
        Assert.Equal("Keeping 30% of what's left on each account + your standing 30% on Claude 20x · Hydra team of 4 · about 25 min · start needs you (over 5% of a window; a floor is close)", plan.Details);
        Assert.Equal([76.6, 82.3, 70.0], plan.Accounts.Select(a => Math.Round(a.Floor!.Value, 1)));
        Assert.Equal([40.6, 31.3, 4.0], plan.Accounts.Select(a => Math.Round(a.Room!.Value, 1)));
        Assert.False(plan.CrossesAFloor);
        Assert.True(plan.NeedsStartTap);

        // With only the standing rule, the others are free to use and the line names the one reserve.
        var standingOnly = rig.Guard.Plan(null, steps, null, agents: 4, duration: TimeSpan.FromMinutes(25));
        Assert.Equal("About 12% of Codex A, 8% of Claude B and up to 6% of Claude 20x. Your 30% reserve on Claude 20x stays.", standingOnly.Line);
        Assert.Equal("Keeping your standing 30% on Claude 20x · about 25 min · start needs you (over 5% of a window; a floor is close)", standingOnly.Details);
    }

    [Fact]
    public void An_account_nobody_has_read_or_calibrated_is_marked_estimate_rough_and_a_plan_that_reaches_a_floor_says_so()
    {
        using var rig = Rig();
        rig.Always(30);
        rig.Read(60);
        var fresh = new ReserveAccountRef("claude:new", "Claude C", "claude");
        var plan = rig.Guard.Plan(null, [new PlanStep(fresh, "x", 300_000, 600_000), new PlanStep(Claude20, Model, 800_000, 1_200_000)]);
        Assert.Equal("About 2% of Claude C (estimate rough) and up to 12% of Claude 20x. Your 30% reserve on Claude 20x stays.", plan.Line);
        Assert.True(plan.CrossesAFloor);                            // 60 + 12 = 72 is over the floor of 70
        Assert.True(plan.Accounts.Single(a => a.Name == "Claude C").Rough);
        Assert.False(plan.Accounts.Single(a => a.Name == "Claude 20x").Rough);
        Assert.Null(plan.Accounts.Single(a => a.Name == "Claude C").Floor);
        Assert.Equal("Nothing is planned to use.", rig.Guard.Plan(null, []).Line);
        rig.Settings.SetAlwaysAskBeforeStarting(true);
        Assert.Contains("you asked to be asked", rig.Guard.Plan(null, []).StartNeeds);
    }

    [Fact]
    public void Spend_more_than_a_quarter_over_the_estimate_re_plans_and_a_plan_the_task_started_with_is_what_it_is_measured_against()
    {
        using var rig = Rig();
        rig.Always(30);
        rig.Read(60);
        var steps = new[] { new PlanStep(Claude20, Model, 200_000, 300_000) };
        var plan = rig.Guard.Plan(null, steps);
        var request = rig.Missions.Request("planned") with { Reserve = new ReserveStart([], plan, steps) };
        var record = rig.Missions.Started(request);
        var lead = LeadOf(rig, record);

        rig.Guard.OnSpend(lead, Model, 250_000);                    // exactly 25% over: not more than
        Assert.Empty(rig.Replans);
        rig.Guard.OnSpend(lead, Model, 1);
        var line = Assert.Single(rig.Replans);
        Assert.Equal(record.Id, line.Mission);
        Assert.Equal("Claude 20x is running 25% over its estimate · re-planned", line.Line);
        rig.Guard.OnSpend(lead, Model, 10_000);                     // the estimate is now what was spent: no second line yet
        Assert.Single(rig.Replans);
        rig.Guard.OnSpend(lead, Model, 400_000);                    // 660,001 on an estimate of 250,001
        Assert.Equal(2, rig.Replans.Count);
    }

    [Fact]
    public void When_a_window_resets_a_running_task_is_re_planned_and_what_is_left_is_measured_from_the_new_window()
    {
        using var rig = Rig();
        rig.Read(90, resets: T0.AddMinutes(20));
        var rule = new ReserveRule("t", ReserveScope.Every, ReserveLifetime.Task) { KeepPercent = 30 };   // 90 + 10 x 0.7 = 97 at the start
        var record = rig.Missions.Started(rig.Missions.Request("across a reset") with { Reserve = new ReserveStart([rule], null, []) });
        Assert.Equal(MissionState.Working, record.Mission.State);
        rig.Clock.Advance(TimeSpan.FromMinutes(21));
        rig.Read(5, resets: T0.AddHours(6));                       // the window reset
        Assert.Contains(rig.Replans, r => r.Mission == record.Id && r.Line == "Claude 20x's 5-hour window reset · re-planned: the freed budget is back in the plan");
        // The floor is now 5 + 95 x 0.7 = 71.5: a reading of 70 holds a turn that 97 would have let through.
        rig.Clock.Advance(TimeSpan.FromSeconds(60));
        rig.Read(70, resets: T0.AddHours(6));
        Assert.NotNull(rig.Card(record));
        Assert.Equal(71.5, rig.Holds.Single().Floor, 6);
    }

    [Fact]
    public void An_ended_task_gets_its_after_report_what_each_account_used_the_outside_use_apart_and_every_reserve_intact()
    {
        using var rig = Rig();
        rig.Always(30);
        rig.Read(55);
        var steps = new[] { new PlanStep(Claude20, Model, 300_000, 400_000) };
        var request = rig.Missions.Request("reported") with { Reserve = new ReserveStart([], rig.Guard.Plan(null, steps), steps) };
        var record = rig.Missions.Started(request);
        var lead = LeadOf(rig, record);
        Second(rig);
        rig.Guard.OnSpend(lead, Model, 250_000);                    // 2.5 points: the estimate is 58
        rig.Clock.Advance(TimeSpan.FromSeconds(60));
        rig.Read(60);                                               // 2 over the estimate is not outside use ...
        var between = rig.Clock.GetUtcNow();
        rig.Clock.Advance(TimeSpan.FromSeconds(60));
        rig.Read(64);                                               // ... but 64 against an estimate of 60 is 4 points of the owner's own

        EndTurn(rig, lead, "All done.");
        var reviewer = rig.Missions.Now(record).ReviewerThread!.Value;
        EndTurn(rig, reviewer, "I read it.\nVERDICT: PASS");
        MissionRig.Until(() => rig.Reports.Count == 1, "the after-report");

        var report = rig.Reports.Single();
        Assert.Same(report, rig.Guard.ReportOf(record.Id));
        Assert.Equal("reported", report.MissionTitle);
        var row = Assert.Single(report.Rows);
        Assert.Equal("Claude 20x", row.AccountName);
        Assert.Equal(3.0, row.PlannedPercent, 6);                   // 300,000 tokens at 100,000 per point
        Assert.Equal(2.5, row.UsedPercent, 6);
        Assert.Equal("5-hour window", row.WindowLabel);
        var outside = Assert.Single(report.Outside);
        Assert.Equal(4.0, outside.Points, 6);
        Assert.Equal("Claude 20x", outside.AccountName);
        Assert.Equal("Every reserve intact.", report.ReserveLine);
        Assert.Equal(["4% of Claude 20x's 5-hour window was used outside nth since " + Clock24(between) + "."], report.OutsideLines);
        Assert.Null(rig.Guard.HoldOf(record.Id));
    }

    [Fact]
    public void The_report_says_how_far_a_reserve_dipped_and_that_the_owner_agreed_to_it()
    {
        using var rig = Rig();
        rig.Always(30);
        rig.Read(68);
        var record = rig.Started();
        Assert.Null(rig.Pick(rig.Card(record)!, ReserveOptionKind.UseOnce));   // 2% of the reserve once: the floor is 72 for this task
        var lead = LeadOf(rig, rig.Missions.Now(record));
        Second(rig);
        rig.Guard.OnSpend(lead, Model, 450_000);                    // 68 + 4.5 = 73: over the 70 the standing rule keeps
        rig.Guard.MissionEnded(record.Id);
        var report = Assert.Single(rig.Reports);
        var reserve = Assert.Single(report.Reserves, x => x.AccountName == "Claude 20x" && !x.Intact);
        Assert.True(reserve.UsedOnce);
        Assert.Equal(3.0, reserve.DippedBy, 6);
        Assert.Equal("Reserve on Claude 20x dipped by 3% this once, as you said.", report.ReserveLine);
        Assert.Equal(DecisionState.Withdrawn, rig.Missions.Queue.Items.Where(c => c.Kind == DecisionKind.Reserve).Last().State);   // the open card of a task that ended goes
    }

    [Fact]
    public void Disposing_the_guard_cancels_what_it_waits_for_and_a_disposed_guard_holds_nothing()
    {
        var rig = Rig();
        rig.Always(30);
        rig.Read(68);
        var record = rig.Started();
        Assert.Null(rig.Pick(rig.Card(record)!, ReserveOptionKind.Wait));
        Assert.Equal(1, rig.Clock.ActiveTimers);
        rig.Dispose();
        Assert.Equal(0, rig.Clock.ActiveTimers);
        rig.Guard.OnReadings(Claude20, [rig.Window(10)]);
        Assert.True(rig.Guard.BeforeTurn(rig.Missions.Now(record), null, ProviderKind.Claude, Model, ReserveTurns.Start));
    }
}
