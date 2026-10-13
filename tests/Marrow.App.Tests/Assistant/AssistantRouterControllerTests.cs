using Marrow.App.Assistant;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Core.Support;
using Marrow.Services.Assistant;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.App.Tests.Assistant;

// Dextra AS-14 (DESIGN.md rev 5, 11.6, 15.4; ED-NEW-dextra-as14-8): the router in the controller. start_mission lets it pick the lead's
// provider, model and account; a team under a mission keeps its parent's; with no pick the mission starts on the controller's default
// and the reserve holds it if it must; a team lead's pair comes from the router. Without a router setup nothing changes.
[Trait("Decision", "ED-NEW-dextra-as14-8")]
[Trait("Feature", "AS-14")]
public sealed class AssistantRouterControllerTests : IDisposable
{
    private static readonly Guid ClaudeId = Guid.Parse("c0000000-0000-4000-8000-0000000000c1");

    private static readonly Guid CodexId = Guid.Parse("c0000000-0000-4000-8000-0000000000c2");

    private readonly AssistantTempRoot temp = new();
    private readonly FakeMissionAgents agents = new();
    private readonly List<RouterAccount> accounts;
    private readonly MissionProject project;

    public AssistantRouterControllerTests()
    {
        project = new MissionProject(MissionRig.Project, "App", Path.Combine(temp.Folder, "owner"));
        accounts =
        [
            new RouterAccount(ClaudeId, new ReserveAccountRef("claude:1", "Claude 20x", "claude"), ProviderKind.Claude, true, 0),
            new RouterAccount(CodexId, new ReserveAccountRef("codex:1", "Codex A", "codex"), ProviderKind.Codex, true, 1),
        ];
    }

    public void Dispose() => temp.Dispose();

    private AssistantController Controller(bool router = true) => new(new AssistantControllerOptions
    {
        Agents = agents,
        Thread = _ => null,
        Projects = () => [project],
        Profiles = () => [new AssistantProfile("dextra", "Dextra")],
        Jobs = new AgentJobs(new FakeAgentJobFactory()),
        Workspaces = new FakeWorkspaces(Path.Combine(temp.Folder, "worktrees")),
        Audit = _ => { },
        Memory = new FakeMemory(FakeMemory.Plenty),
        Provider = ProviderKind.Claude,
        Model = "model-a",
        Router = router ? new RouterSetup
        {
            Accounts = () => accounts,
            Models = provider => provider == ProviderKind.Claude
                ? [new RouteModel("haiku", ModelTier.Light), new RouteModel("opus", ModelTier.Strong)]
                : provider == ProviderKind.Codex ? [new RouteModel("sol", ModelTier.Standard), new RouteModel("astra", ModelTier.Strong)] : [],
        } : null,
    });

    private static AssistantToolCall Call(AssistantChat chat, string tool, string arguments) =>
        new(chat, AssistantToolCatalog.Find(tool)!, JSONValue.Parse(arguments)!, new Classification(ActionClass.Agents, "the tool's class"),
            new PolicyDecision(Verdict.Ask, PolicyRule.AlwaysConfirmed, "Asks: the owner's call"), AssistantRoute.Mcp);

    private static AssistantChat Home() => new(Guid.NewGuid(), "dextra", null, AssistantChatRole.Home, ProviderKind.Claude, "model-a");

    private async Task<MissionRecord> Start(AssistantController controller, string tool = "start_mission", string arguments = """{"title":"Invoices","brief":"Sort the invoices.","project":"App"}""")
    {
        var started = await controller.Run(Call(Home(), tool, arguments), CancellationToken.None);
        Assert.False(started.IsError, started.Text);
        var record = controller.Runner.Missions[^1];
        MissionRig.Until(() => controller.Runner.Find(record.Id)!.LeadThread is not null, "the lead to start");
        return controller.Runner.Find(record.Id)!;
    }

    [Fact]
    public async Task Start_mission_lets_the_router_pick_the_leads_provider_model_and_account()
    {
        using var controller = Controller();
        var record = await Start(controller);
        Assert.Equal((ProviderKind.Claude, "opus", ClaudeId), (record.Provider, record.Model, record.AccountId));
        Assert.False(record.ModelPinned);
        var launch = agents.Lead(record).Launch;
        Assert.Equal((ProviderKind.Claude, "opus", ClaudeId), (launch.Provider, launch.Model, launch.AccountId));
        Assert.NotNull(controller.Router);

        // The owner's pill order puts Codex A first: the lead goes there, on its strong model.
        accounts[0] = accounts[0] with { Rank = 5 };
        var second = await Start(controller, arguments: """{"title":"More invoices","brief":"Sort them too.","project":"App"}""");
        Assert.Equal((ProviderKind.Codex, "astra", CodexId), (second.Provider, second.Model, second.AccountId));
        Assert.Equal(CodexId, agents.Lead(second).Launch.AccountId);
    }

    [Fact]
    public async Task With_no_pick_the_mission_starts_on_the_controllers_default_and_without_a_router_setup_nothing_changes()
    {
        using var controller = Controller();
        for (var i = 0; i < accounts.Count; i++) accounts[i] = accounts[i] with { Usable = false };
        var record = await Start(controller);
        Assert.Equal((ProviderKind.Claude, "model-a", (Guid?)null), (record.Provider, record.Model, record.AccountId));

        using var plain = Controller(router: false);
        Assert.Null(plain.Router);
        var unrouted = await Start(plain);
        Assert.Equal((ProviderKind.Claude, "model-a", (Guid?)null), (unrouted.Provider, unrouted.Model, unrouted.AccountId));
    }

    [Fact]
    public async Task A_team_under_a_mission_keeps_the_provider_model_and_account_of_its_parent_even_if_the_router_would_choose_otherwise()
    {
        using var controller = Controller();
        var parent = await Start(controller);
        accounts[0] = accounts[0] with { Rank = 5 };                 // the router would now choose Codex A
        var team = await Start(controller, "launch_hydra_team", $$"""{"mission":"{{parent.Id:D}}","brief":"Split it."}""");
        Assert.Equal(parent.Id, team.Mission.ParentMissionId);
        Assert.Equal((parent.Provider, parent.Model, parent.AccountId), (team.Provider, team.Model, team.AccountId));
        Assert.True(team.Team);
    }

    [Fact]
    public async Task A_team_leads_pair_comes_from_the_router_and_no_other_chat_gets_one()
    {
        using var controller = Controller();
        var parent = await Start(controller);
        var team = await Start(controller, "launch_hydra_team", $$"""{"mission":"{{parent.Id:D}}","brief":"Split it."}""");
        var leadChat = MissionRig.NewChat(() => new ChatThread(project.Id, ProviderKind.Claude, "opus", null, SealedProviderConfig.CappedMode) { Id = team.LeadThread!.Value, MissionID = team.Id });
        var pair = controller.TeamPair(leadChat)!;
        Assert.Equal(ProviderKind.Claude, pair.Provider);
        Assert.Equal("opus", pair.WorkerModel);
        Assert.Equal("Reading", Assert.Single(pair.HeadProfiles).Name);

        // The first mission runs no team; a head (it has a parent), a chat on another provider, or no mission's, gets nothing.
        var soloChat = MissionRig.NewChat(() => new ChatThread(project.Id, ProviderKind.Claude, "opus", null, SealedProviderConfig.CappedMode) { Id = parent.LeadThread!.Value, MissionID = parent.Id });
        Assert.Null(controller.TeamPair(soloChat));
        Assert.Null(controller.TeamPair(leadChat with { ParentThreadID = Guid.NewGuid() }));
        Assert.Null(controller.TeamPair(leadChat with { Provider = ProviderKind.Codex }));
        Assert.Null(controller.TeamPair(leadChat with { MissionID = null }));
        using var plain = Controller(router: false);
        Assert.Null(plain.TeamPair(leadChat));
    }
}
