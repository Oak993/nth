using Marrow.App.Assistant;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;
using Marrow.Services.Tests.Fixtures;
using static Marrow.App.Tests.Fixtures.RouterRig;

namespace Marrow.App.Tests.Assistant;

// Dextra AS-14 (DESIGN.md rev 5, 11.5 option 1; ED-NEW-dextra-as14-6): "Move the rest to ...". The router offers the best-placed of the
// accounts with room that belong to the task's own provider, with the task's model (a chat changes account, not model), and never when
// the owner pinned the model. The move sets the task's account, so every chat it starts runs there, and changes a live lead to it at
// its next safe point with nth's own summary; the task stays held until that is done, and a move that cannot be done leaves it where
// it was and asks again. The activity log says that it moved, to which account key, and nothing else.
[Trait("Decision", "ED-NEW-dextra-as14-6")]
[Trait("Feature", "AS-14")]
public sealed class AssistantRouterMoveTests : IDisposable
{
    private readonly AssistantTempRoot temp = new();

    public void Dispose() => temp.Dispose();

    private RouterRig Rig() => new(temp.Folder, claudeB: true);

    // A task on Claude 20x whose floor is 70 and which stands at 68: the gate holds it with the card. Claude B and Codex A have room.
    private static MissionRecord Held(RouterRig rig, string title = "Fix the login timeout", bool pinned = false)
    {
        rig.Reserve.Always(30);
        rig.Read(ReserveRig.Claude20, 68);
        rig.Read(ClaudeB, 30);
        rig.Read(ReserveRig.CodexA, 10);
        rig.Reserve.DefaultAccount = ReserveRig.Claude20;
        return rig.Missions.Started(rig.Missions.Request(title) with { Model = "opus", ModelPinned = pinned });
    }

    private static string? Pick(RouterRig rig, MissionRecord mission, ReserveOptionKind kind) => rig.Reserve.Pick(rig.Reserve.Card(mission)!, kind);

    // A task running on Claude 20x, whose floor is 70, that stood at 40 when it began; 30 points of the ledger's spend later its running
    // turn would cross the floor. The turn ends, the task is Paused, and its card offers the move to Claude B.
    private static (MissionRecord Mission, Guid Lead, DecisionItem Card) LiveHeld(RouterRig rig, string title = "Fix the login timeout")
    {
        rig.Reserve.Always(30);
        rig.Read(ReserveRig.Claude20, 40);
        rig.Read(ClaudeB, 30);
        var mission = rig.Missions.Started(rig.Missions.Request(title) with { Model = "opus" });
        var lead = rig.Missions.Agents.Lead(mission).Id;
        rig.Reserve.Clock.Advance(TimeSpan.FromSeconds(1));
        rig.Reserve.Guard.OnSpend(lead, "opus", 3_000_000);
        var card = rig.Reserve.Card(mission)!;
        rig.Missions.Agents.Finish(lead, "Half done.");
        rig.Missions.Runner.OnTurnFinished(lead, TurnStatus.Completed, continues: false);
        return (mission, lead, card);
    }

    [Fact]
    public void The_card_offers_the_best_placed_account_of_the_missions_own_provider_with_its_cost()
    {
        using var rig = Rig();
        var mission = Held(rig);
        var card = rig.Reserve.Card(mission)!;
        // Codex A has the most room, but a lead of a Claude mission changes account within Claude: Claude B is offered. Its rate is the
        // conservative default, so the next turn costs 131,623 / 75,000 = 1.76 points: about 2%.
        Assert.Equal("Move the rest to Claude B (≈2%)", card.Options[0]);
        Assert.Equal(MissionState.Paused, rig.Missions.Now(mission).Mission.State);
    }

    [Fact]
    public void No_move_is_offered_for_a_pinned_model_or_when_the_only_accounts_with_room_are_another_providers()
    {
        using (var rig = Rig())
        {
            var mission = Held(rig, pinned: true);
            Assert.DoesNotContain(rig.Reserve.Card(mission)!.Options, o => o.StartsWith("Move", StringComparison.Ordinal));
            Assert.Null(rig.Router.Offer(rig.Missions.Now(mission), [new ReserveCandidate(ClaudeB, 2, 30)]));
        }
        using (var rig = new RouterRig(temp.Folder))
        {
            var mission = Held(rig);
            Assert.DoesNotContain(rig.Reserve.Card(mission)!.Options, o => o.StartsWith("Move", StringComparison.Ordinal));   // only Codex A has room
        }
    }

    [Fact]
    public void Moving_a_task_whose_lead_has_not_started_points_the_task_at_the_other_account_and_the_lead_starts_there()
    {
        using var rig = Rig();
        var mission = Held(rig);
        Assert.Empty(rig.Missions.Agents.Chats);
        Assert.Null(Pick(rig, mission, ReserveOptionKind.Move));
        MissionRig.Until(() => rig.Missions.Now(mission).LeadThread is not null, "the lead to start");
        var moved = rig.Missions.Now(mission);
        Assert.Equal(RouterRig.ClaudeBId, moved.AccountId);
        Assert.Equal(MissionState.Working, moved.Mission.State);
        Assert.Equal(RouterRig.ClaudeBId, rig.Missions.Agents.Lead(moved).Launch.AccountId);
        Assert.Equal("opus", rig.Missions.Agents.Lead(moved).Launch.Model);          // the model stays
        Assert.Empty(rig.Missions.Agents.AccountMoves);                              // there was no chat to change
        Assert.Null(rig.Reserve.Guard.HoldOf(mission.Id));
        Assert.Contains(rig.Missions.Audit, e => e.Tool == "move_account" && e.Result == "moved to claude:b" && e.Mission == mission.Id.ToString("D"));
        Assert.Contains(rig.Reserve.Audit, e => e.Tool == "reserve_answer" && e.Result == "Move the rest to Claude B (≈2%)");
    }

    [Fact]
    public void Moving_a_live_lead_holds_the_task_until_its_chat_has_changed_account_then_it_goes_on()
    {
        using var rig = Rig();
        var (mission, lead, card) = LiveHeld(rig);
        Assert.Equal(MissionState.Paused, rig.Missions.Now(mission).Mission.State);

        var changing = new TaskCompletionSource<bool>();
        rig.Missions.Agents.MoveAnswer = (_, _) => changing.Task;
        Assert.Null(rig.Reserve.Pick(card, ReserveOptionKind.Move));
        // The chat is asked to change; until it has, the task is still held with its hold in place, and nothing is sent.
        Assert.Equal([(lead, RouterRig.ClaudeBId)], rig.Missions.Agents.AccountMoves);
        Assert.Equal(MissionState.Paused, rig.Missions.Now(mission).Mission.State);
        Assert.NotNull(rig.Reserve.Guard.HoldOf(mission.Id));
        Assert.Equal(RouterRig.ClaudeBId, rig.Missions.Now(mission).AccountId);

        changing.SetResult(true);
        Assert.Null(rig.Reserve.Guard.HoldOf(mission.Id));
        Assert.Equal(MissionState.Reviewing, rig.Missions.Now(mission).Mission.State);   // the lead's work was done: its review begins
        Assert.Equal(RouterRig.ClaudeBId, rig.Missions.Now(mission).AccountId);
    }

    [Fact]
    public void A_chat_that_cannot_change_account_leaves_the_task_where_it_was_and_the_owner_is_asked_again_with_the_reason()
    {
        const string Sentinel = "sk-SENTINEL-0123456789abcdef";
        using var rig = Rig();
        var (mission, _, first) = LiveHeld(rig, "Use " + Sentinel + " to log in");

        rig.Missions.Agents.MoveAnswer = (_, _) => throw new InvalidOperationException("token " + Sentinel + " was refused");
        Assert.Null(rig.Reserve.Pick(first, ReserveOptionKind.Move));
        var again = rig.Missions.Queue.Of(mission.Id).Single(c => c.Kind == DecisionKind.Reserve);
        Assert.NotEqual(first.Id, again.Id);
        Assert.StartsWith("Moving the rest did not work: its chat could not change to Claude B.", again.Note, StringComparison.Ordinal);
        Assert.Null(rig.Missions.Now(mission).AccountId);                              // back where it was
        Assert.Equal(MissionState.Paused, rig.Missions.Now(mission).Mission.State);
        Assert.NotNull(rig.Reserve.Guard.HoldOf(mission.Id));

        // What was thrown is for the fault line, never the card or the activity log; the log says moved and moved back.
        Assert.DoesNotContain(Sentinel, again.Note, StringComparison.Ordinal);
        Assert.All(rig.Reserve.Audit.Concat(rig.Missions.Audit.Where(e => e.Tool == "move_account")),
            e => Assert.DoesNotContain(Sentinel, string.Join(" ", e.Tool, e.Result, e.Target, e.Mission, string.Join(" ", e.Inputs.Select(i => i.ToString()))), StringComparison.Ordinal));
        Assert.Contains(rig.Missions.Audit, e => e.Tool == "move_account" && e.Result == "moved to claude:b");
        Assert.Contains(rig.Missions.Audit, e => e.Tool == "move_account" && e.Result == "back to the provider's own account");
        Assert.Contains(rig.Reserve.Faults, f => f.StartsWith("The chat could not be moved to Claude B (InvalidOperationException)", StringComparison.Ordinal));

        // A chat that answers false is a move that did not work too.
        rig.Missions.Agents.MoveAnswer = (_, _) => Task.FromResult(false);
        Assert.Null(rig.Reserve.Pick(again, ReserveOptionKind.Move));
        Assert.Null(rig.Missions.Now(mission).AccountId);
        Assert.Equal(2, rig.Missions.Agents.AccountMoves.Count);
    }

    [Fact]
    public void A_task_stopped_while_its_chat_changes_account_stays_stopped_when_the_move_finishes()
    {
        using var rig = Rig();
        var (mission, _, card) = LiveHeld(rig);

        var changing = new TaskCompletionSource<bool>();
        rig.Missions.Agents.MoveAnswer = (_, _) => changing.Task;
        Assert.Null(rig.Reserve.Pick(card, ReserveOptionKind.Move));
        Assert.Null(rig.Missions.Runner.Stop(mission.Id));
        changing.SetResult(true);
        Assert.NotEqual(MissionState.Working, rig.Missions.Now(mission).Mission.State);
        Assert.NotEqual(MissionState.Reviewing, rig.Missions.Now(mission).Mission.State);
        Assert.Null(rig.Missions.Now(mission).ReviewerThread);
    }

    [Fact]
    public void A_crash_while_the_chat_changes_account_leaves_the_new_account_in_the_journal_and_the_reserve_still_follows_the_account_the_chat_is_on()
    {
        using var rig = Rig();
        var (mission, lead, card) = LiveHeld(rig);
        rig.Missions.Agents.MoveAnswer = (_, _) => new TaskCompletionSource<bool>().Task;           // the chat never finishes changing: nth ends here
        Assert.Null(rig.Reserve.Pick(card, ReserveOptionKind.Move));

        // The intent was journaled before the effect, and the journal reads back whole; nth cannot know whether the chat got there.
        var journaled = rig.Missions.Saved.Last(saved => saved.Id == mission.Id);
        Assert.Equal(RouterRig.ClaudeBId, journaled.AccountId);
        Assert.Equal(RouterRig.ClaudeBId, MissionJournal.FromJson(MissionJournal.ToJson(journaled))!.AccountId);

        // A turn for the chat itself is asked about the account the chat is really on (Claude 20x, at its floor); only a chat yet to be made
        // goes by the task's account (Claude B, with room). A cut turn run again after a restart is never sent on a guess.
        var other = journaled with { Mission = journaled.Mission with { Id = Guid.NewGuid() } };
        Assert.False(rig.Reserve.Guard.BeforeTurn(other, lead, ProviderKind.Claude, "opus", ReserveTurns.Resume));
        Assert.True(rig.Reserve.Guard.BeforeTurn(other, null, ProviderKind.Claude, "opus", ReserveTurns.Start));
    }

    [Fact]
    public async Task A_move_to_an_account_that_cannot_be_used_or_is_of_another_provider_or_for_a_pinned_model_is_refused_and_changes_nothing()
    {
        using var rig = Rig();
        var mission = rig.Missions.Started(rig.Missions.Request() with { Model = "opus" });
        var offer = new ReserveMoveOffer(ClaudeB, 2);
        rig.Set(RouterRig.ClaudeBId, a => a with { Usable = false });
        Assert.Equal("that account is not available now", await rig.Router.MoveAsync(mission, offer));
        rig.Set(RouterRig.ClaudeBId, a => a with { Usable = true });
        Assert.Equal("that account is not available now", await rig.Router.MoveAsync(mission, new ReserveMoveOffer(ReserveRig.CodexA, 2)));
        Assert.Equal("that account is not available now", await rig.Router.MoveAsync(mission, new ReserveMoveOffer(new ReserveAccountRef("claude:gone", "Gone", "claude"), 2)));
        Assert.StartsWith("its model was chosen", await rig.Router.MoveAsync(mission with { ModelPinned = true }, offer), StringComparison.Ordinal);
        Assert.Null(rig.Missions.Now(mission).AccountId);
        Assert.Empty(rig.Missions.Agents.AccountMoves);
        Assert.DoesNotContain(rig.Missions.Audit, e => e.Tool == "move_account");
    }
}
