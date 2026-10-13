using Marrow.App.Assistant;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.App.Tests.Assistant;

// Dextra AS-14 (DESIGN.md rev 5, 15.4, Hydra seams; ED-NEW-dextra-as14-7): the seams in the app itself. The launch of a mission lead's
// Hydra team takes the pair the router filled in place of the owner's pairs (and an ordinary chat's launch is as before); a head of a
// mission on the lead's provider runs on the lead's account, which is the one the router chose and the reserve follows.
[Trait("Decision", "ED-NEW-dextra-as14-7")]
[Trait("Feature", "AS-14")]
public sealed class AssistantRouterSeamTests
{
    private static AgentSpawn Spawn(string id) => new() { Id = id, TaskID = "task-" + id, Description = "Check the tests", Prompt = "Run the login tests." };

    private static AssistantController Controller(AppModel app, string worktrees) => new(new AssistantControllerOptions
    {
        Agents = new AppMissionAgents(app),
        Thread = app.Thread,
        Projects = () => [.. app.Projects.Select(p => new MissionProject(p.Id, p.Name, p.Path))],
        Profiles = () => [new AssistantProfile("dextra", "Dextra")],
        Jobs = app.MissionJobs,
        Workspaces = new FakeWorkspaces(worktrees),
        Audit = _ => { },
        Memory = new FakeMemory(FakeMemory.Plenty),
        Router = new RouterSetup
        {
            Accounts = () => [new RouterAccount(Guid.Parse("c0000000-0000-4000-8000-0000000000c1"), new ReserveAccountRef("claude:1", "Claude 20x", "claude"), ProviderKind.Claude, true, 0)],
            Models = _ => [new RouteModel("haiku", ModelTier.Light), new RouteModel("opus", ModelTier.Strong)],
        },
    });

    [Fact]
    public void The_launch_of_a_mission_leads_team_takes_the_pair_the_router_filled_and_any_other_chat_keeps_its_own()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("Project", h.NewFolder("project"));
        var mission = Guid.NewGuid();
        var lead = AppModelSamples.Thread(project) with { Provider = ProviderKind.Claude, Model = "opus", RuntimeMode = SealedProviderConfig.CappedMode, MissionID = mission, HydraEnabled = true };
        var plain = AppModelSamples.Thread(project) with { Provider = ProviderKind.Claude, Model = "opus", HydraEnabled = true };
        h.WriteLibrary([project], [lead, plain]);
        var model = h.MakeModel();
        h.PrepareBootstrap(model);
        var controller = h.Main.OnMain(() =>
        {
            model.Settings.HydraEnabled = true;
            model.MissionJobs = new AgentJobs(new FakeAgentJobFactory());
            var made = Controller(model, h.NewFolder("worktrees"));
            var record = new MissionRecord(new Mission(mission, "Fix the login timeout", "dextra", MissionOrigin.Owner, MissionState.Working) { ThreadIds = new([lead.Id]) })
            {
                Brief = "Make it 30 s.",
                ProjectId = project.Id,
                ProjectPath = project.Path,
                Provider = ProviderKind.Claude,
                Model = "opus",
                Team = true,
                LeadThread = lead.Id,
                Created = DateTimeOffset.UtcNow,
            };
            made.Runner.Load([record]);
            model.AttachDextra(made);
            return made;
        });
        try
        {
            var launch = h.Main.OnMain(() => model.HydraLaunch(model.Thread(lead.Id)!))!;
            Assert.Equal(ProviderKind.Claude, launch.HeadsProvider);
            Assert.Equal("opus", launch.WorkerModel);
            var reading = Assert.Single(launch.HeadProfiles);
            Assert.Equal(("Reading", "haiku"), (reading.Name, reading.Model));

            var ordinary = h.Main.OnMain(() => model.HydraLaunch(model.Thread(plain.Id)!))!;
            Assert.Empty(ordinary.HeadProfiles);
            Assert.Null(ordinary.WorkerModel);
        }
        finally
        {
            h.Main.OnMain(() => model.AttachDextra(null));
            controller.Dispose();
        }
    }

    [Fact]
    public void A_head_of_a_mission_on_the_leads_provider_runs_on_the_leads_account_and_a_head_of_any_other_chat_is_as_before()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("Project", h.NewFolder("project"));
        var account = Guid.NewGuid();
        var lead = AppModelSamples.Thread(project) with { Provider = ProviderKind.Claude, RuntimeMode = SealedProviderConfig.CappedMode, MissionID = Guid.NewGuid(), ProviderAccountID = account };
        var plain = AppModelSamples.Thread(project) with { Provider = ProviderKind.Claude, ProviderAccountID = account };
        h.WriteLibrary([project], [lead, plain]);
        var model = h.MakeModel();
        h.PrepareBootstrap(model);
        ChatThread? head = null;
        ChatThread? plainHead = null;
        h.Main.OnMain(() =>
        {
            head = model.SpawnNativeHead(lead.Id, Spawn("a"));
            plainHead = model.SpawnNativeHead(plain.Id, Spawn("b"));
        });
        Assert.Equal(account, head!.ProviderAccountID);
        Assert.Null(plainHead!.ProviderAccountID);
    }

    [Fact]
    public void A_head_a_profile_sends_to_another_provider_does_not_borrow_the_leads_account()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("Project", h.NewFolder("project"));
        var lead = AppModelSamples.Thread(project) with { Provider = ProviderKind.Claude, Model = "lead-model", MissionID = Guid.NewGuid(), ProviderAccountID = Guid.NewGuid() };
        h.WriteLibrary([project], [lead]);
        var model = h.MakeModel();
        h.Main.OnMain(() => model.Settings.HydraIsolateHeads = false);
        h.PrepareBootstrap(model);
        var pair = new HydraPair(ProviderKind.Claude)
        {
            HeadProfiles = new ValueList<HydraHeadProfile>([new HydraHeadProfile("Other") { Provider = ProviderKind.Codex, Model = "other-model" }]),
        };
        ChatThread? head = null;
        h.Main.OnMain(() =>
        {
            model.Settings.AddHydraPair(pair);
            model.UpdateThread(lead.Id, thread => thread with { HydraPairID = pair.Id });
            head = model.SpawnDroppyHead(lead.Id, "Write the tests", HydraHeadInfo.Origin.Delegated, [], null, null, null, "other", static (_, _) => "brief");
        });
        Assert.NotNull(head);
        Assert.Equal(ProviderKind.Codex, head!.Provider);
        Assert.Null(head.ProviderAccountID);
    }
}
