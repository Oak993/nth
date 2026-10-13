using Marrow.App.Assistant;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;

namespace Marrow.App.Tests.Assistant;

[Trait("Decision", "ED-NEW-finalint-4")]
public sealed class FinalIntMissionSideChatTests
{
    [Fact]
    public void A_mission_side_chat_inherits_the_seal_and_joins_stop_and_recovery_bookkeeping()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("Project", h.NewFolder("project"));
        var mission = Guid.NewGuid();
        var assistant = Guid.NewGuid();
        var lead = AppModelSamples.Thread(project) with { MissionID = mission, AssistantID = assistant, RuntimeMode = RuntimeMode.FullAccess };
        h.WriteLibrary([project], [lead]);
        var model = h.MakeModel();
        h.Main.OnMain(() => model.MissionJobs = new AgentJobs(new FakeAgentJobFactory()));
        var audit = new List<AuditEntry>();
        using var controller = new AssistantController(new AssistantControllerOptions
        {
            Agents = new AppMissionAgents(model), Thread = model.Thread,
            Projects = () => [new MissionProject(project.Id, project.Name, project.Path)],
            Profiles = () => [new AssistantProfile("dextra", "Dextra")],
            Jobs = model.MissionJobs, Workspaces = new FakeWorkspaces(h.NewFolder("worktrees")),
            Audit = e => { lock (audit) audit.Add(e); },
            Memory = new FakeMemory(FakeMemory.Plenty),
        });
        h.Main.OnMain(() =>
        {
            controller.Runner.Load([new MissionRecord(new Mission(mission, "Mission", "dextra", MissionOrigin.Owner, MissionState.Working)
                { ThreadIds = new([lead.Id]) }) { LeadThread = lead.Id, ProjectId = project.Id, ProjectPath = project.Path }]);
            model.AttachDextra(controller);
        });
        try
        {
            h.Main.OnMain(() =>
            {
                var side = Assert.IsType<ChatThread>(model.EnsureSideChat(lead.Id));
                Assert.True(side.IsSideChat);
                Assert.Equal(mission, side.MissionID);
                Assert.Equal(assistant, side.AssistantID);
                Assert.Equal(mission, controller.Runner.MissionOf(side.Id)!.Id);
                Assert.Contains(side.Id, controller.Runner.Find(mission)!.Mission.ThreadIds);
                var restored = new MissionRig(h.NewFolder("restored-runner"));
                restored.Runner.Load([controller.Runner.Find(mission)!]);
                Assert.Equal(mission, restored.Runner.MissionOf(side.Id)!.Id);
                model.SetSideChatCanAct(lead.Id, true);
                Assert.Equal(RuntimeMode.Supervised, model.Thread(side.Id)!.RuntimeMode);
                Assert.Equal(InteractionMode.Build, model.Thread(side.Id)!.InteractionMode);
                var sideJob = Assert.IsType<FakeAgentJob>(model.MissionJobs.For(side.Id));
                controller.Runner.Stop(mission);
                Assert.Equal(1, sideJob.Terminations);
                Assert.Equal(0, sideJob.LiveProcesses);
                Assert.False(model.SendSideChat(lead.Id, "Do not revive a stopped mission"));
                Assert.Empty(model.Runtime(side.Id).Turns);
                Assert.NotNull(model.SideChat(lead.Id)); // The saved conversation is still available.
            });
        }
        finally { h.Main.OnMain(() => model.AttachDextra(null)); }
    }

    [Fact]
    public void A_mission_without_its_runner_never_opens_an_unowned_side_chat()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("Project", h.NewFolder("project"));
        var lead = AppModelSamples.Thread(project) with { MissionID = Guid.NewGuid() };
        h.WriteLibrary([project], [lead]);
        var model = h.MakeModel();
        h.Main.OnMain(() =>
        {
            Assert.Null(model.EnsureSideChat(lead.Id));
            Assert.Single(model.Threads);
        });
    }
    [Theory]
    [InlineData("stop")]
    [InlineData("clear")]
    [InlineData("disarm")]
    [InlineData("close")]
    public void A_waiting_mission_side_chat_refuses_a_second_send_and_its_controls_cancel_admission(string action)
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("Project", h.NewFolder("project"));
        var mission = Guid.NewGuid();
        var lead = AppModelSamples.Thread(project) with { MissionID = mission };
        h.WriteLibrary([project], [lead]);
        var model = h.MakeModel();
        h.PrepareBootstrap(model);
        h.Main.OnMain(() => model.MissionJobs = new AgentJobs(new FakeAgentJobFactory()));
        var memory = new FakeMemory(new MemoryReading(32UL << 30, 1UL << 30));
        var audit = new List<AuditEntry>();
        using var controller = Controller(h, model, project, memory, audit);
        ChatThread side = null!;
        ThreadRuntime runtime = null!;
        h.Main.OnMain(() =>
        {
            controller.Runner.Load([new MissionRecord(new Mission(mission, "Mission", "dextra", MissionOrigin.Owner, MissionState.Working)
                { ThreadIds = new([lead.Id]) }) { LeadThread = lead.Id, ProjectId = project.Id, ProjectPath = project.Path }]);
            model.AttachDextra(controller);
            side = model.EnsureSideChat(lead.Id)!;
            model.SetSideChatCanAct(lead.Id, true);
            runtime = model.Runtime(side.Id);
        });
        h.Main.Loop.Run(() => runtime.EnsureLoaded(), AppModelHarness.Bound);
        try
        {
            h.Main.OnMain(() => Assert.True(model.SendSideChat(lead.Id, "Wait for a slot")));
            h.Main.Loop.RunUntil(() => controller.Gate.Waiting(side.Id) is not null || runtime.Turns.Count > 0, AppModelHarness.Bound);
            h.Main.OnMain(() =>
            {
                Assert.NotNull(controller.Gate.Waiting(side.Id));
                runtime.Draft = new ComposerDraft { Text = "Keep the second message" };
                Assert.False(model.SendSideChat(lead.Id, "Second message"));
                Assert.Equal("Keep the second message", runtime.Draft.Text);
                switch (action)
                {
                    case "stop": model.StopSideChat(lead.Id); break;
                    case "clear": model.ClearSideChat(lead.Id); break;
                    case "disarm": model.SetSideChatCanAct(lead.Id, false); break;
                    case "close": model.SideChatClosed(lead.Id); break;
                }
            });
            h.Main.Loop.RunUntil(() => controller.Gate.Waiting(side.Id) is null && !model.PendingInstallerTurns.Any(), AppModelHarness.Bound);
            h.Main.OnMain(() => { memory.Reading = FakeMemory.Plenty; controller.Gate.Pump(); });
            h.Main.RunUntilIdle();
            h.Main.OnMain(() => { Assert.Empty(runtime.Turns); Assert.False(controller.Gate.Holds(side.Id)); });
        }
        finally { h.Main.OnMain(() => { controller.Gate.Leave(side.Id); model.AttachDextra(null); }); }
    }

    [Fact]
    public void A_side_turn_rechecks_mission_membership_after_its_history_load()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("Project", h.NewFolder("project"));
        var mission = Guid.NewGuid();
        var lead = AppModelSamples.Thread(project) with { MissionID = mission };
        var side = AppModelSamples.Thread(project) with { MissionID = mission, ParentThreadID = lead.Id, IsSideChat = true, IsInPanel = true };
        h.WriteLibrary([project], [lead, side]);
        var model = h.MakeModel();
        h.PrepareBootstrap(model);
        h.Main.OnMain(() => model.MissionJobs = new AgentJobs(new FakeAgentJobFactory()));
        var audit = new List<AuditEntry>();
        using var controller = Controller(h, model, project, new FakeMemory(FakeMemory.Plenty), audit);
        var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task turn = null!;
        var runtime = h.Main.OnMain(() => model.Runtime(side.Id));
        h.Main.OnMain(() =>
        {
            controller.Runner.Load([Record(lead.Id, true)]);
            model.AttachDextra(controller);
            ThreadRuntimeReflection.Set(runtime, "isLoaded", false);
            ThreadRuntimeReflection.Set(runtime, "loadTask", loaded.Task);
            turn = ThreadRuntimeReflection.Call<Task>(runtime, "StartTurn", "Do not revive the old side", Array.Empty<Attachment>(), null, false)!;
            Assert.False(turn.IsCompleted);
            var failed = Record(lead.Id, true);
            controller.Runner.Load([failed with { Mission = failed.Mission with { State = MissionState.Failed } }]);
            controller.Hold.HoldForStop(); // Keep the fresh attempt paused; no launcher or provider is needed.
            Assert.Null(controller.Runner.Restart(mission));
            Assert.Null(controller.Runner.MissionOf(side.Id));
            loaded.SetResult();
        });
        try { h.Main.Loop.Run(() => turn, AppModelHarness.Bound); h.Main.OnMain(() => Assert.Empty(runtime.Turns)); }
        finally { h.Main.OnMain(() => model.AttachDextra(null)); }

        MissionRecord Record(Guid newLead, bool includeSide) => new(new Mission(mission, "Mission", "dextra", MissionOrigin.Owner, MissionState.Working)
            { ThreadIds = includeSide ? new([newLead, side.Id]) : new([newLead]) })
            { LeadThread = newLead, ProjectId = project.Id, ProjectPath = project.Path };
    }

    private static AssistantController Controller(AppModelHarness h, AppModel model, Project project, FakeMemory memory, List<AuditEntry> audit) => new(new AssistantControllerOptions
    {
        Agents = new AppMissionAgents(model), Thread = model.Thread,
        Projects = () => [new MissionProject(project.Id, project.Name, project.Path)],
        Profiles = () => [new AssistantProfile("dextra", "Dextra")],
        Jobs = model.MissionJobs, Workspaces = new FakeWorkspaces(h.NewFolder("worktrees")),
        Audit = e => { lock (audit) audit.Add(e); }, Memory = memory, Time = h.Main.Time,
    });

    [Theory]
    [InlineData("stop")]
    [InlineData("clear")]
    public void Stopping_a_side_turn_during_main_history_loading_never_sends_its_prompt(string action)
    {
        using var rig = new SideChatRig();
        rig.MakeSide();
        var side = rig.SideRuntime;
        rig.H.Main.Loop.Run(() => side.EnsureLoaded(), AppModelHarness.Bound);
        var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        // ED-NEW-finalint-10: a fail-closed, test-only await witness on the pinned runtime, without timing guesses.
        var continuation = typeof(Task).GetField("m_continuationObject", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(continuation);
        Task turn = null!;
        rig.H.Main.OnMain(() =>
        {
            ThreadRuntimeReflection.Set(rig.Main.Runtime, "isLoaded", false);
            ThreadRuntimeReflection.Set(rig.Main.Runtime, "loadTask", loaded.Task);
            Assert.Null(continuation!.GetValue(loaded.Task));
            turn = ThreadRuntimeReflection.Call<Task>(side, "StartTurn", "Never send this canceled question", Array.Empty<Attachment>(), null, false)!;
        });
        try
        {
            rig.Until(() => continuation!.GetValue(loaded.Task) is not null);
            ThreadRuntimeScriptedSession session = null!;
            rig.H.Main.OnMain(() =>
            {
                session = Assert.Single(rig.Launches).Session;
                Assert.Equal(RuntimePhase.Starting, side.Phase);
                Assert.Empty(session.Inputs);
                if (action == "clear") rig.App.ClearSideChat(rig.ParentId);
                else rig.App.StopSideChat(rig.ParentId);
                if (action == "clear") Assert.Null(rig.App.ExistingRuntime(side.ThreadID));
            });
            rig.Until(() => side.Phase == RuntimePhase.Idle && session.StopCalls > 0);
            rig.H.Main.OnMain(() =>
            {
                ThreadRuntimeReflection.Set(rig.Main.Runtime, "isLoaded", true);
                loaded.SetResult();
            });
            rig.H.Main.Loop.Run(() => turn, AppModelHarness.Bound);
            rig.H.Main.OnMain(() =>
            {
                Assert.Empty(session.Inputs);
                Assert.Equal(RuntimePhase.Idle, side.Phase);
                Assert.Equal(TurnStatus.Interrupted, Assert.Single(side.Turns).Status);
                Assert.Empty(rig.Main.Session.Inputs);
                if (action == "clear")
                {
                    Assert.Null(rig.App.Thread(side.ThreadID));
                    Assert.Null(rig.App.ExistingRuntime(side.ThreadID));
                }
            });
        }
        finally
        {
            rig.H.Main.OnMain(() => { ThreadRuntimeReflection.Set(rig.Main.Runtime, "isLoaded", true); loaded.TrySetResult(); });
            rig.H.Main.Loop.Run(() => turn, AppModelHarness.Bound);
        }
    }

}
