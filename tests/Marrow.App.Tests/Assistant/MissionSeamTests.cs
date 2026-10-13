using Marrow.App.Assistant;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.App.Tests.Assistant;

// Dextra AS-5 (DESIGN.md rev 5, 12.1a, 12.3 points 2 and 3, 15.4, Hydra seam 3; ED-NEW-dextra-as5-6, -11): the seams in the
// app itself. A Hydra head under a mission's lead is the mission's agent (its MissionID, the capped mode, never Full
// access, refused on a provider nth cannot seal) and waits at the agent gate; a mission chat's every turn runs in the ask
// mode; its approvals reach Dextra's queue through ThreadRuntime and the owner's answer reaches the session as the
// provider's once option; and the runner's chats are made sealed. Sessions are scripted; no provider runs.
[Trait("Decision", "ED-NEW-dextra-as5-6")]
[Trait("Feature", "AS-5")]
public sealed class MissionSeamTests
{
    private static AgentSpawn Spawn(string id) => new() { Id = id, TaskID = "task-" + id, Description = "Check the tests", Prompt = "Run the login tests." };

    private static MissionRecord Record(Guid mission, Guid lead, Guid project, string path) =>
        new(new Mission(mission, "Fix the login timeout", "dextra", MissionOrigin.Owner, MissionState.Working) { ThreadIds = new([lead]) })
        {
            Brief = "Make it 30 s.",
            ProjectId = project,
            ProjectPath = path,
            LeadThread = lead,
            Created = DateTimeOffset.UtcNow,
        };

    private static AssistantController Controller(AppModel app, FakeWorkspaces workspaces, List<AuditEntry> audit) => new(new AssistantControllerOptions
    {
        Agents = new AppMissionAgents(app),
        Thread = app.Thread,
        Projects = () => [.. app.Projects.Select(p => new MissionProject(p.Id, p.Name, p.Path))],
        Profiles = () => [new AssistantProfile("dextra", "Dextra")],
        Jobs = app.MissionJobs,
        Workspaces = workspaces,
        Audit = e =>
        {
            lock (audit) audit.Add(e);
        },
        Memory = new FakeMemory(FakeMemory.Plenty),
    });

    [Fact]
    public void A_head_of_a_missions_lead_inherits_the_mission_and_the_ask_mode_and_one_on_an_unsealable_provider_is_refused()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("Project", h.NewFolder("project"));
        var mission = Guid.NewGuid();
        var assistant = Guid.NewGuid();
        var lead = AppModelSamples.Thread(project) with { Provider = ProviderKind.Claude, RuntimeMode = RuntimeMode.FullAccess, MissionID = mission, AssistantID = assistant };
        var plain = AppModelSamples.Thread(project) with { Provider = ProviderKind.Claude, RuntimeMode = RuntimeMode.AutoAcceptEdits };
        var unsealable = AppModelSamples.Thread(project) with { Provider = ProviderKind.Cursor, MissionID = mission };
        h.WriteLibrary([project], [lead, plain, unsealable]);
        var model = h.MakeModel();
        h.PrepareBootstrap(model);

        ChatThread? head = null;
        ChatThread? plainHead = null;
        ChatThread? refused = null;
        h.Main.OnMain(() =>
        {
            head = model.SpawnNativeHead(lead.Id, Spawn("a"));
            plainHead = model.SpawnNativeHead(plain.Id, Spawn("b"));
            refused = model.SpawnNativeHead(unsealable.Id, Spawn("c"));
        });

        Assert.NotNull(head);
        Assert.Equal(mission, head!.MissionID);
        Assert.Equal(assistant, head.AssistantID);
        Assert.Equal(SealedProviderConfig.CappedMode, head.RuntimeMode);
        Assert.NotEqual(RuntimeMode.FullAccess, head.RuntimeMode);
        Assert.Equal(SealedProviderConfig.CappedMode, MissionLaunch.TurnMode(head));

        Assert.NotNull(plainHead);
        Assert.Null(plainHead!.MissionID);
        Assert.Equal(RuntimeMode.AutoAcceptEdits, plainHead.RuntimeMode);

        Assert.Null(refused);
        Assert.DoesNotContain(model.Threads, t => t.ParentThreadID == unsealable.Id);
        h.Main.Loop.RunUntil(() => model.ExistingRuntime(unsealable.Id)?.Entries.Select(e => e.Item.TimelineItemContent)
            .OfType<TimelineItem.Content.User>().Any(u => u.Value.Text == "Hydra: " + SealedProviderConfig.Refusal(ProviderKind.Cursor)) == true, AppModelHarness.Bound);
    }

    [Fact]
    public void A_head_a_profile_routes_to_an_unsealable_provider_runs_on_the_leads_provider()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("Project", h.NewFolder("project"));
        var lead = AppModelSamples.Thread(project) with { Model = "lead-model", MissionID = Guid.NewGuid() }; // Codex
        h.WriteLibrary([project], [lead]);
        var model = h.MakeModel();
        h.Main.OnMain(() => model.Settings.HydraIsolateHeads = false);
        h.PrepareBootstrap(model);
        var pair = new HydraPair(ProviderKind.Codex)
        {
            HeadProfiles = new ValueList<HydraHeadProfile>([new HydraHeadProfile("Fast") { Provider = ProviderKind.Cursor, Model = "cursor-model" }]),
        };
        ChatThread? head = null;
        h.Main.OnMain(() =>
        {
            model.Settings.AddHydraPair(pair);
            model.UpdateThread(lead.Id, thread => thread with { HydraPairID = pair.Id });
            head = model.SpawnDroppyHead(lead.Id, "Write the tests", HydraHeadInfo.Origin.Delegated, [], null, null, null, "fast", static (_, _) => "brief");
        });
        Assert.NotNull(head);
        Assert.Equal(ProviderKind.Codex, head!.Provider);
        Assert.Equal("lead-model", head.Model);
        Assert.Equal(lead.MissionID, head.MissionID);
        Assert.Equal(SealedProviderConfig.CappedMode, head.RuntimeMode);
        h.Main.Loop.RunUntil(() => model.ExistingRuntime(lead.Id)?.Entries.Select(e => e.Item.TimelineItemContent)
            .OfType<TimelineItem.Content.User>().Any(u => u.Value.Text == $"Hydra: Cursor cannot run mission work yet, so {head.Hydra!.Persona.Name} runs on Codex.") == true,
            AppModelHarness.Bound);
    }

    [Fact]
    public void A_droppy_head_of_a_mission_joins_the_mission_and_waits_at_the_gate_while_one_outside_missions_keeps_full_access()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("Project", h.NewFolder("project"));
        var mission = Guid.NewGuid();
        var lead = AppModelSamples.Thread(project) with { Provider = ProviderKind.Claude, RuntimeMode = SealedProviderConfig.CappedMode, MissionID = mission };
        var plain = AppModelSamples.Thread(project) with { Provider = ProviderKind.Claude };
        h.WriteLibrary([project], [lead, plain]);
        var model = h.MakeModel();
        h.Main.OnMain(() => model.Settings.HydraIsolateHeads = false);
        h.PrepareBootstrap(model);
        var audit = new List<AuditEntry>();
        var controller = h.Main.OnMain(() =>
        {
            model.MissionJobs = new AgentJobs(new FakeAgentJobFactory());
            var made = Controller(model, new FakeWorkspaces(h.NewFolder("worktrees")), audit);
            made.Runner.Load([Record(mission, lead.Id, project.Id, project.Path)]);
            model.AttachDextra(made);
            return made;
        });
        var sawWorking = false;
        var heldWhenWorking = false;
        controller.Runner.Changed += r =>
        {
            if (r.Id != mission || sawWorking || r.Mission.Heads.All(x => x.State != HeadState.Working)) return;
            heldWhenWorking = r.Mission.Heads.Where(x => x.State == HeadState.Working).All(x => controller.Gate.Holds(Guid.Parse(x.Id)));
            sawWorking = true;
        };
        try
        {
            ChatThread? head = null;
            ChatThread? plainHead = null;
            h.Main.OnMain(() =>
            {
                head = model.SpawnDroppyHead(lead.Id, "Write the tests", HydraHeadInfo.Origin.Delegated, [], null, null, null, null, static (_, _) => "brief");
                plainHead = model.SpawnDroppyHead(plain.Id, "Write the docs", HydraHeadInfo.Origin.Delegated, [], null, null, null, null, static (_, _) => "brief");
            });
            Assert.NotNull(head);
            Assert.Equal(mission, head!.MissionID);
            Assert.Equal(SealedProviderConfig.CappedMode, head.RuntimeMode);
            Assert.Equal(RuntimeMode.FullAccess, plainHead!.RuntimeMode);
            Assert.Null(plainHead.MissionID);

            Assert.Equal(mission, controller.Runner.MissionOf(head.Id)!.Id);
            Assert.Equal(head.Id.ToString("D"), controller.Runner.Find(mission)!.Mission.Heads.Single().Id);
            h.Main.Loop.RunUntil(() => sawWorking, AppModelHarness.Bound);
            Assert.True(heldWhenWorking); // it started only once the gate let it in
            Assert.Null(controller.Runner.MissionOf(plainHead.Id));
            Assert.False(controller.Gate.Holds(plainHead.Id));
        }
        finally
        {
            h.Main.OnMain(() => model.AttachDextra(null));
        }
    }

    [Fact]
    [Trait("Decision", "ED-NEW-dextra-as5-14")]
    public void A_subagent_a_mission_chat_opens_is_the_missions_agent_in_its_folder_only_while_dextra_runs_it_and_one_of_any_other_chat_is_as_before()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("Project", h.NewFolder("project"));
        var mission = Guid.NewGuid();
        var assistant = Guid.NewGuid();
        var worktree = h.NewFolder("worktree");
        var lead = AppModelSamples.Thread(project) with
        {
            Provider = ProviderKind.Claude, RuntimeMode = SealedProviderConfig.CappedMode, MissionID = mission, AssistantID = assistant,
            WorktreePath = worktree, Branch = "nth/mission-0f3c9a2e-fix",
        };
        var home = AppModelSamples.Thread(project) with { Provider = ProviderKind.Claude, RuntimeMode = RuntimeMode.FullAccess, AssistantID = assistant };
        h.WriteLibrary([project], [lead, home]);
        var model = h.MakeModel();
        h.PrepareBootstrap(model);
        // No Dextra runs the mission: its chat's helper does not open (no chat outside Dextra's sight).
        var (refused, count) = h.Main.OnMain(() =>
        {
            model.MissionJobs = new AgentJobs(new FakeAgentJobFactory());
            return (model.SpawnSubagent(lead.Id, "Check", "Check the login tests."), model.Threads.Count);
        });
        Assert.Null(refused);
        Assert.Equal(2, count);

        var audit = new List<AuditEntry>();
        var controller = h.Main.OnMain(() =>
        {
            var made = Controller(model, new FakeWorkspaces(h.NewFolder("worktrees")), audit);
            made.Runner.Load([Record(mission, lead.Id, project.Id, project.Path) with { Provider = ProviderKind.Claude }]);
            model.AttachDextra(made);
            return made;
        });
        try
        {
            var (fromMission, fromHome) = h.Main.OnMain(() =>
                (model.SpawnSubagent(lead.Id, "Check", "Check the login tests."), model.SpawnSubagent(home.Id, "Notes", "Sum up the notes.")));
            Assert.Equal(mission, fromMission!.MissionID);
            Assert.Equal(assistant, fromMission.AssistantID);
            Assert.Equal(SealedProviderConfig.CappedMode, fromMission.RuntimeMode);
            Assert.Equal(worktree, fromMission.WorktreePath);
            Assert.Equal("nth/mission-0f3c9a2e-fix", fromMission.Branch);
            Assert.True(MissionLaunch.IsMissionChat(fromMission));
            Assert.Equal(SealedProviderConfig.CappedMode, MissionLaunch.TurnMode(fromMission with { RuntimeMode = RuntimeMode.FullAccess }));
            Assert.Equal(mission, controller.Runner.MissionOf(fromMission.Id)!.Id);
            Assert.Contains(fromMission.Id, controller.Runner.Find(mission)!.Mission.ThreadIds);
            Assert.Contains(audit, e => e.Result == "helper opened" && e.Agent == fromMission.Id.ToString("D"));

            Assert.Null(fromHome!.MissionID);
            Assert.Null(fromHome.AssistantID);
            Assert.Null(fromHome.WorktreePath);
            Assert.Equal(RuntimeMode.FullAccess, fromHome.RuntimeMode);
            Assert.Null(controller.Runner.MissionOf(fromHome.Id));
        }
        finally
        {
            h.Main.OnMain(() => model.AttachDextra(null));
        }
    }

    [Fact]
    [Trait("Decision", "ED-NEW-dextra-as5-14")]
    public void A_missions_lead_sends_heads_only_into_its_own_project_and_no_other_repository_joins_the_sidebar()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("Project", h.NewFolder("project"));
        var other = h.NewFolder("elsewhere");
        var mission = Guid.NewGuid();
        var lead = AppModelSamples.Thread(project) with { Provider = ProviderKind.Claude, RuntimeMode = SealedProviderConfig.CappedMode, MissionID = mission };
        h.WriteLibrary([project], [lead]);
        var model = h.MakeModel();
        h.Main.OnMain(() => model.Settings.HydraIsolateHeads = false);
        h.PrepareBootstrap(model);
        var audit = new List<AuditEntry>();
        h.Main.OnMain(() =>
        {
            model.MissionJobs = new AgentJobs(new FakeAgentJobFactory());
            var made = Controller(model, new FakeWorkspaces(h.NewFolder("worktrees")), audit);
            made.Runner.Load([Record(mission, lead.Id, project.Id, project.Path)]);
            model.AttachDextra(made);
        });
        try
        {
            var (away, projects, threads) = h.Main.OnMain(() =>
                (model.SpawnDroppyHead(lead.Id, "Fix it there", HydraHeadInfo.Origin.Delegated, [], null, null, other, null, static (_, _) => "brief"),
                    model.Projects.Count, model.Threads.Count));
            Assert.Null(away);
            Assert.Equal(1, projects);
            Assert.Equal(1, threads);

            var home = h.Main.OnMain(() =>
                model.SpawnDroppyHead(lead.Id, "Write the tests", HydraHeadInfo.Origin.Delegated, [], null, null, "Project", null, static (_, _) => "brief"));
            Assert.NotNull(home);
            Assert.Equal(mission, home!.MissionID);
            Assert.Equal(project.Id, home.ProjectID);
            Assert.Equal(1, h.Main.OnMain(() => model.Projects.Count));
        }
        finally
        {
            h.Main.OnMain(() => model.AttachDextra(null));
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_mission_chats_every_turn_runs_in_the_ask_mode_and_any_other_chat_in_its_own(bool missionChat)
    {
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Claude, thread => thread with
        {
            RuntimeMode = RuntimeMode.FullAccess,
            MissionID = missionChat ? Guid.NewGuid() : null,
        });
        rig.Send("Go on.");
        var input = Assert.Single(rig.Session.Inputs);
        Assert.Equal(missionChat ? SealedProviderConfig.CappedMode : RuntimeMode.FullAccess, input.RuntimeMode);
    }

    [Fact]
    public void A_mission_chats_approval_goes_to_the_queue_and_the_owners_answer_reaches_the_session_as_the_once_option()
    {
        var mission = Guid.NewGuid();
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Claude, thread => thread with { MissionID = mission, RuntimeMode = SealedProviderConfig.CappedMode });
        var audit = new List<AuditEntry>();
        var lead = rig.InitialThread.Id;
        var controller = rig.H.Main.OnMain(() =>
        {
            var made = Controller(rig.App, new FakeWorkspaces(rig.H.NewFolder("worktrees")), audit);
            made.Runner.Load([Record(mission, lead, rig.Project.Id, rig.Project.Path)]);
            rig.App.AttachDextra(made);
            return made;
        });
        try
        {
            rig.Finish(rig.StartTurn("Fix it."));
            var push = MissionRig.Approval("p1", "git push origin main");
            var status = MissionRig.Approval("p2", "git status");
            var install = MissionRig.Approval("p3", "npm install left-pad");
            rig.H.Main.OnMain(() =>
            {
                rig.Session.Emit(new ProviderEvent.Approval(push));
                rig.Session.Emit(new ProviderEvent.Approval(status));
                rig.Session.Emit(new ProviderEvent.Approval(install));
            });
            rig.H.Main.Loop.RunUntil(() => controller.Queue.Of(mission).Count == 3, AppModelHarness.Bound);
            Assert.Empty(rig.Session.ApprovalAnswers);
            Assert.Equal(MissionState.NeedsYou, controller.Runner.Find(mission)!.Mission.State);
            var pushCard = controller.Queue.Of(mission).Single(c => c.RequestId == "p1");
            Assert.Equal(DecisionWeight.Irreversible, pushCard.Weight);

            Assert.Null(controller.Queue.Answer(pushCard.Id, DecisionAnswer.DoIt));
            rig.H.Main.Loop.RunUntil(() => rig.Session.ApprovalAnswers.Count == 1, AppModelHarness.Bound);
            Assert.Equal(("p1", "allow", (string?)null), rig.Session.ApprovalAnswers[0]);

            // The owner answers one in the chat itself, and the provider cancels the other: both cards go.
            rig.H.Main.OnMain(() =>
            {
                rig.Runtime.Resolve(status, status.Options[0]);
                rig.Session.Emit(new ProviderEvent.RequestResolved("p3"));
            });
            rig.H.Main.Loop.RunUntil(() => controller.Queue.Of(mission).Count == 0, AppModelHarness.Bound);
            Assert.All(controller.Queue.Items.Where(c => c.RequestId is "p2" or "p3"), c =>
            {
                Assert.Equal(DecisionState.Withdrawn, c.State);
                Assert.Equal(MissionRunner.RequestSettled, c.Note);
            });
            rig.H.Main.Loop.RunUntil(() => controller.Runner.Find(mission)!.Mission.State == MissionState.Working, AppModelHarness.Bound);
        }
        finally
        {
            rig.H.Main.OnMain(() => rig.App.AttachDextra(null));
        }
    }

    [Fact]
    public void The_runners_chats_are_made_sealed_in_the_missions_own_folder()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("Project", h.NewFolder("project"));
        h.WriteLibrary([project], []);
        var model = h.MakeModel();
        h.PrepareBootstrap(model);
        var worktree = h.NewFolder("worktree");
        var mission = Guid.NewGuid();
        var agents = new AppMissionAgents(model);
        var (lead, reviewer) = h.Main.OnMain(() =>
        {
            model.MissionJobs = new AgentJobs(new FakeAgentJobFactory());
            var leadId = agents.Start(new AgentLaunch(mission, "dextra", "Fix the login timeout", "The brief.", project.Id, worktree, ProviderKind.Claude)
            {
                Team = true,
                Branch = "nth/fix-the-login-timeout",
                Model = "model-a",
            });
            var reviewerId = agents.Start(new AgentLaunch(mission, "dextra", "Review: Fix the login timeout", "Review it.", project.Id, project.Path, ProviderKind.Claude)
            {
                Reviewer = true,
                Model = "model-b",
            });
            return (model.Thread(leadId)!, model.Thread(reviewerId)!);
        });

        Assert.Equal(mission, lead.MissionID);
        Assert.Equal(SealedProviderConfig.CappedMode, lead.RuntimeMode);
        Assert.Equal(worktree, lead.WorktreePath);
        Assert.Equal("nth/fix-the-login-timeout", lead.Branch);
        Assert.True(lead.HydraEnabled);
        Assert.Equal(InteractionMode.Build, lead.InteractionMode);
        Assert.Equal("model-a", lead.Model);
        Assert.Equal("Fix the login timeout", lead.Title);

        Assert.Equal(mission, reviewer.MissionID);
        Assert.Equal(SealedProviderConfig.CappedMode, reviewer.RuntimeMode);
        Assert.Null(reviewer.WorktreePath);
        Assert.Equal(InteractionMode.Plan, reviewer.InteractionMode);
        Assert.False(reviewer.HydraEnabled);
        Assert.False(h.Main.OnMain(() => agents.Send(Guid.NewGuid(), "nobody")));
    }

    [Fact]
    public void Start_dextra_brings_back_the_journaled_missions_journals_new_ones_and_binds_the_controller()
    {
        using var h = new AppModelHarness();
        using var temp = new AssistantTempRoot();
        var project = AppModelSamples.Project("Project", h.NewFolder("project"));
        h.WriteLibrary([project], []);
        var model = h.MakeModel();
        h.PrepareBootstrap(model);
        var options = new AssistantStoreOptions { InterruptedMission = MissionJournal.Interrupted };
        var lead = Guid.NewGuid();
        var working = Record(Guid.NewGuid(), lead, project.Id, project.Path);
        var crashing = AssistantStore.Open(temp.Roots, options);
        crashing.SaveMission(MissionJournal.StoreId(working.Id), MissionJournal.ToJson(working));
        crashing.AbandonForTests();

        using var store = AssistantStore.Open(temp.Roots, options);
        var audit = new List<AuditEntry>();
        var controller = h.Main.OnMain(() => model.StartDextra(store, e =>
        {
            lock (audit) audit.Add(e);
        }, () => [new AssistantProfile("dextra", "Dextra")]));
        try
        {
            Assert.Same(controller, model.Dextra);
            var back = controller.Runner.Find(working.Id)!;
            Assert.Equal(MissionState.Paused, back.Mission.State);
            Assert.Equal(MissionState.Working, back.Mission.ResumeTo);
            Assert.True(back.Interrupted);
            Assert.Equal(working.Id, controller.Runner.MissionOf(lead)!.Id);

            // A provider nth cannot seal blocks the mission before any chat or workspace exists; it is journaled all the same.
            var blocked = h.Main.OnMain(() => controller.Runner.Start(new MissionRequest("Tidy the docs", "Tidy them.", "dextra", project.Id, project.Path, ProviderKind.Cursor)));
            var stored = MissionJournal.FromJson(store.Mission(MissionJournal.StoreId(blocked.Record.Id))!.Body)!;
            Assert.Equal(MissionState.Blocked, stored.Mission.State);
            Assert.Equal(SealedProviderConfig.Refusal(ProviderKind.Cursor), stored.Reason);
            Assert.False(Directory.Exists(store.MissionWorktreeFolder(MissionJournal.StoreId(blocked.Record.Id))));
            Assert.DoesNotContain(model.Threads, t => t.MissionID == blocked.Record.Id);
            lock (audit) Assert.Contains(audit, e => e.Mission == blocked.Record.Id.ToString("D") && e.Tool == "start_mission");
        }
        finally
        {
            h.Main.OnMain(model.UnbindDextra);
            controller.Dispose();
        }
        Assert.Null(model.Dextra);
    }
}
