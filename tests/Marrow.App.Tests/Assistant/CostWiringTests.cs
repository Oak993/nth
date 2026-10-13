using Marrow.App.Assistant;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.App.Tests.Assistant;

// Dextra AS-15 (DESIGN.md rev 5, 13.2; ED-NEW-dextra-as15-4, -5, -6): the cost controls as the controller holds them. The caps are made only
// with the reserve (they read its accounts and its calibration) and hang off it; the duty watch is made when it has a book to keep; the cheap
// triage model and the strong one are picked from what the app offers. Over fake chats; no provider runs.
[Trait("Decision", "ED-NEW-dextra-as15-4")]
[Trait("Feature", "AS-15")]
public sealed class CostWiringTests : IDisposable
{
    private readonly AssistantTempRoot temp = new();
    private readonly FakeMissionAgents agents = new();
    private readonly List<AuditEntry> audit = [];

    public void Dispose() => temp.Dispose();

    private static ReserveSetup Reserve(CostSetup? costs) => new()
    {
        Settings = new ReserveSettings(),
        Calibrator = new UsageCalibrator(),
        Turns = new TurnCostHistory(),
        AccountOf = _ => ReserveRig.Claude20,
        AccountForNew = _ => ReserveRig.Claude20,
        AccountOfSnapshot = _ => ReserveRig.Claude20,
        Costs = costs,
    };

    private AssistantController Controller(ReserveSetup? reserve = null, DutyHealthBook? duties = null, TriageSetup? triage = null) => new(new AssistantControllerOptions
    {
        Agents = agents,
        Thread = _ => null,
        Projects = () => [new MissionProject(MissionRig.Project, "App", Path.Combine(temp.Folder, "owner"))],
        Profiles = () => [new AssistantProfile("dextra", "Dextra")],
        Jobs = new AgentJobs(new FakeAgentJobFactory()),
        Workspaces = new FakeWorkspaces(Path.Combine(temp.Folder, "worktrees")),
        Audit = e =>
        {
            lock (audit) audit.Add(e);
        },
        Memory = new FakeMemory(FakeMemory.Plenty),
        Model = "model-a",
        Reserve = reserve,
        DutyHealth = duties,
        Triage = triage,
    });

    private static CostSetup Costs() => new() { Ledger = new CostLedger(), AssistantName = id => id == "dextra" ? "Dextra" : null };

    private MissionRequest Request(string title = "Fix the login timeout") =>
        new(title, "Make it 30 s.", "dextra", MissionRig.Project, Path.Combine(temp.Folder, "owner"), ProviderKind.Claude) { Model = "model-a" };

    [Fact]
    public void The_caps_are_made_only_with_a_reserve_and_hang_off_it_so_the_runner_still_has_one_gate()
    {
        using var none = Controller();
        Assert.Null(none.Costs);
        Assert.Null(none.Duties);

        using var reserveOnly = Controller(Reserve(null));
        Assert.NotNull(reserveOnly.Reserve);
        Assert.Null(reserveOnly.Costs);
        Assert.Null(reserveOnly.Reserve!.Costs);

        using var both = Controller(Reserve(Costs()));
        Assert.NotNull(both.Costs);
        Assert.Same(both.Costs, both.Reserve!.Costs);
        Assert.Same(both.Reserve, both.Runner.Reserve);
    }

    [Fact]
    public void A_standing_cap_holds_a_mission_the_controller_starts_with_a_card_in_its_queue()
    {
        var reserve = Reserve(Costs());
        using var controller = Controller(reserve);
        Assert.Null(reserve.Settings.AddCap(new CostCap("", CostCapScope.AssistantDay) { Percent = 1, AssistantId = "dextra" }));   // nothing is calibrated yet: a new turn costs 1.6 points
        var start = controller.Runner.Start(Request());
        Assert.True(MissionRig.Within(start.Launched, 10));
        var record = controller.Runner.Find(start.Record.Id)!;
        Assert.Equal(MissionState.Paused, record.Mission.State);
        Assert.Equal(PauseCause.Reserve, record.Mission.PausedBy);
        var card = Assert.Single(controller.Queue.Of(record.Id));
        Assert.Equal(DecisionKind.Cap, card.Kind);
        Assert.Equal("Standing cap for Dextra: at most 1% of any account a day", card.Why);
        Assert.Empty(agents.Chats);

        var raised = new List<CapHold>();
        controller.Costs!.Held += raised.Add;
        Assert.Null(reserve.Settings.AddCap(new CostCap("", CostCapScope.ApiDay) { Dollars = 1m }));                               // an API-key cap does not touch a subscription mission
        Assert.Empty(raised);
    }

    [Fact]
    public void A_mission_under_its_caps_starts_as_it_always_did()
    {
        var reserve = Reserve(Costs());
        using var controller = Controller(reserve);
        Assert.Null(reserve.Settings.AddCap(new CostCap("", CostCapScope.AssistantDay) { Percent = 50, AssistantId = "dextra" }));
        var start = controller.Runner.Start(Request());
        Assert.True(MissionRig.Within(start.Launched, 10));
        Assert.Equal(MissionState.Working, controller.Runner.Find(start.Record.Id)!.Mission.State);
        Assert.Empty(controller.Queue.Waiting);
    }

    [Fact]
    public void The_duty_watch_is_made_when_there_is_a_book_to_keep_and_its_log_lines_go_where_the_controllers_do()
    {
        using var controller = Controller(duties: new DutyHealthBook());
        var watch = Assert.IsType<DutyWatch>(controller.Duties);
        for (var i = 0; i < 3; i++) watch.RunEnded(new DutyRun("digest", "Daily digest", "dextra", false, "the site is down"));
        Assert.True(watch.IsPaused("digest"));
        Assert.Contains(controller.Queue.Waiting, c => c.Kind == DecisionKind.Duty);
        Assert.Contains(audit, e => e.Tool == "duty_paused" && e.Assistant == "dextra");
    }

    // ---- the cheap model and the strong one ----

    private static BrainModel Model(string name, decimal? input, decimal? output, bool local = false) =>
        new("claude", name, "claude:20x", "Claude 20x") { InputPerMillion = input, OutputPerMillion = output, RunsLocally = local, ContextWindow = 200_000 };

    [Fact]
    public void Triage_goes_to_the_cheapest_capable_model_and_planning_to_the_brains_own()
    {
        var brain = Model("big", 15m, 75m);
        var cheap = Model("small", 1m, 5m);
        var local = Model("llama", null, null, local: true);
        using var controller = Controller(triage: new TriageSetup { Options = () => [brain, cheap, local], Brain = () => brain });

        Assert.Same(cheap, controller.TriageModel().Model);
        Assert.Same(local, controller.TriageModel(requireLocal: true).Model);                // text that may not leave the PC goes to the one on it
        var strong = controller.PlanningModel();
        Assert.Same(brain, strong.Model);
        Assert.True(strong.IsBrain);
        Assert.Null(controller.PlanningModel(requireLocal: true).Model);                       // and the strong model is a cloud model here: it may not read it
    }

    [Fact]
    public void With_nothing_set_up_there_is_no_model_to_pick_and_the_pick_says_so()
    {
        using var controller = Controller();
        var triage = controller.TriageModel();
        Assert.Null(triage.Model);
        Assert.Equal("No model is available to read it.", triage.Reason);
        Assert.Equal("No model is chosen for the brain.", controller.PlanningModel().Reason);
    }
}
