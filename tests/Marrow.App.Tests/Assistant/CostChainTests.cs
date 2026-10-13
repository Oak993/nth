using Marrow.App.Assistant;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.App.Tests.Assistant;

// Dextra AS-15 (DESIGN.md rev 5, 11.5, 13.2; ED-NEW-dextra-as15-4): a mission has two things to be asked before a turn goes, the usage reserve
// and the owner's caps, and the runner has one gate, the reserve. The reserve asks the caps once it has said yes; a task a cap holds is not
// asked of the reserve again, so there is never a second card; the caps hear of every turn and of the end of a task through the reserve.
[Trait("Decision", "ED-NEW-dextra-as15-4")]
[Trait("Feature", "AS-15")]
public sealed class CostChainTests : IDisposable
{
    private readonly AssistantTempRoot temp = new();

    public void Dispose() => temp.Dispose();

    private (ReserveRig Rig, CostGuard Costs) Chain()
    {
        var rig = new ReserveRig(temp.Folder);
        var costs = new CostGuard(new CostGuardOptions
        {
            Runner = rig.Missions.Runner,
            Queue = rig.Missions.Queue,
            Settings = rig.Settings,
            Ledger = new CostLedger(rig.Clock),
            Calibrator = rig.Calibrator,
            Turns = rig.Turns,
            AccountOf = _ => ReserveRig.Claude20,
            AccountForNew = _ => ReserveRig.Claude20,
            WindowsOf = _ => [ReserveWindowKind.FiveHour],
            Time = rig.Clock,
        });
        rig.Guard.Costs = costs;
        return (rig, costs);
    }

    private static MissionRecord WithCap(ReserveRig rig, double percent, string title = "Fix the login timeout") =>
        rig.Missions.Started(rig.Missions.Request(title) with { Reserve = new ReserveStart([], null, []) { TaskCaps = [new CostCap("", CostCapScope.Task) { Percent = percent }] } });

    private static DecisionItem? CapCard(ReserveRig rig, MissionRecord record) => rig.Missions.Queue.Of(record.Id).FirstOrDefault(c => c.Kind == DecisionKind.Cap);

    [Fact]
    public void The_runner_still_has_the_one_gate_the_reserve()
    {
        var (rig, costs) = Chain();
        using var owned = rig;
        using var ownedCosts = costs;
        Assert.Same(rig.Guard, rig.Missions.Runner.Reserve);
        Assert.Same(costs, rig.Guard.Costs);
    }

    [Fact]
    public void A_task_the_reserve_holds_is_not_asked_of_the_caps_and_once_the_reserve_lets_it_go_the_cap_holds_it()
    {
        var (rig, costs) = Chain();
        using var owned = rig;
        using var ownedCosts = costs;
        rig.Always(30);
        rig.Read(68);                                                 // the reserve has no room for the first turn (68 + 1.97 + 1 + 1 > 70)
        var record = WithCap(rig, 1);                                 // and the task's own cap would not let it start either
        Assert.NotNull(rig.Card(record));
        Assert.Null(CapCard(rig, record));                            // the reserve said no first: the caps were not asked
        Assert.False(costs.IsHeld(record.Id));

        Assert.Null(rig.Pick(rig.Card(record)!, ReserveOptionKind.UseOnce));
        var now = rig.Missions.Now(record);
        Assert.Equal(MissionState.Paused, now.Mission.State);          // the reserve let it go; the cap holds it
        var cap = Assert.IsType<DecisionItem>(CapCard(rig, record));
        Assert.Equal("Fix the login timeout would pass your cap", cap.Title);
        Assert.True(costs.IsHeld(record.Id));
        Assert.Single(rig.Missions.Queue.Of(record.Id));              // one card at a time
    }

    [Fact]
    public void A_task_a_cap_holds_is_not_put_to_the_reserve_again_so_there_is_never_a_second_card()
    {
        var (rig, costs) = Chain();
        using var owned = rig;
        using var ownedCosts = costs;
        rig.Always(30);
        rig.Read(40);                                                 // the reserve has room
        var record = WithCap(rig, 1);
        Assert.NotNull(CapCard(rig, record));

        rig.Read(68);                                                 // now it would not: but the cap already holds the task
        Assert.False(rig.Guard.BeforeTurn(rig.Missions.Now(record), null, ProviderKind.Claude, ReserveRig.Model, ReserveTurns.Head));   // a head waiting its turn looks again
        Assert.Null(rig.Card(record));
        Assert.Single(rig.Missions.Queue.Of(record.Id));
    }

    [Fact]
    public void The_caps_hear_of_a_turn_that_starts_and_of_what_it_spends_through_the_reserve()
    {
        var (rig, costs) = Chain();
        using var owned = rig;
        using var ownedCosts = costs;
        var record = WithCap(rig, 5);
        Assert.Null(CapCard(rig, record));
        var lead = rig.Missions.Agents.Lead(record).Id;
        costs.OnSpend(lead, ReserveRig.Model, 600_000);                // 6 points against a cap of 5: the turn in flight was told to the caps
        var pausing = rig.Missions.Now(record);
        Assert.Equal(MissionState.Pausing, pausing.Mission.State);
        Assert.NotNull(CapCard(rig, record));
    }

    [Fact]
    public void A_turn_that_runs_on_another_account_is_asked_of_the_caps_for_that_account_through_the_reserve()
    {
        // AS-14: the reserve is given the account a reviewer's turn will run on and passes it on to the caps.
        var (rig, costs) = Chain();
        using var owned = rig;
        using var ownedCosts = costs;
        var record = WithCap(rig, 5);
        costs.OnSpend(rig.Missions.Agents.Lead(record).Id, ReserveRig.Model, 450_000);                 // 4.5 of the task's 5% on Claude 20x
        Assert.Null(CapCard(rig, record));

        Assert.True(rig.Guard.BeforeTurn(rig.Missions.Now(record), null, ProviderKind.Codex, ReserveRig.Model, ReserveTurns.Review, ReserveRig.CodexA));   // on Codex A it has used nothing
        Assert.Null(CapCard(rig, record));
        Assert.False(rig.Guard.BeforeTurn(rig.Missions.Now(record), null, ProviderKind.Claude, ReserveRig.Model, ReserveTurns.Review));                  // on its own account 4.5 + 1.3 passes 5
        Assert.NotNull(CapCard(rig, record));
    }

    [Fact]
    public void The_reserve_says_which_usage_windows_an_account_reports_so_a_percent_cap_counts_only_those()
    {
        var (rig, costs) = Chain();
        using var owned = rig;
        using var ownedCosts = costs;
        Assert.Empty(rig.Guard.WindowKinds(ReserveRig.Claude20.Key));                                    // not read yet
        rig.Read(40, weekly: 10);
        Assert.Equal([ReserveWindowKind.FiveHour, ReserveWindowKind.Weekly], rig.Guard.WindowKinds(ReserveRig.Claude20.Key));
        Assert.Empty(rig.Guard.WindowKinds("nobody:x"));
    }

    [Fact]
    public void The_end_of_a_task_reaches_the_caps_and_takes_their_card_away()
    {
        var (rig, costs) = Chain();
        using var owned = rig;
        using var ownedCosts = costs;
        var record = WithCap(rig, 1);
        var card = CapCard(rig, record)!;
        rig.Guard.MissionEnded(record.Id);
        Assert.Equal(DecisionState.Withdrawn, rig.Missions.Queue.Find(card.Id)!.State);
        Assert.False(costs.IsHeld(record.Id));
    }
}
