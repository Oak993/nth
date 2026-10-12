using Marrow.App.Assistant;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Core.Support;
using Marrow.Services.Assistant;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.App.Tests.Assistant;

// Dextra AS-5 (DESIGN.md rev 5, 12.1a, 12.3, 15.4, 15.5; ED-NEW-dextra-as5-11): the controller over fake chats, with the
// real classifier and policy engine behind every provider approval of a mission chat. Auto-answering is off, so the
// engine's Allow lands on a card like its Ask; it says which chats are whose; and the nth-dextra tools it serves in this
// unit start missions, redirect and stop agents, ask the owner and report, while the rest say they are not built yet.
[Trait("Decision", "ED-NEW-dextra-as5-11")]
[Trait("Feature", "AS-5")]
public sealed class AssistantControllerTests : IDisposable
{
    private readonly AssistantTempRoot temp = new();
    private readonly FakeMissionAgents agents = new();
    private readonly Dictionary<Guid, ChatThread> chats = [];
    private readonly List<AuditEntry> audit = [];
    private readonly MissionProject project;
    private readonly FakeWorkspaces workspaces;

    public AssistantControllerTests()
    {
        project = new MissionProject(MissionRig.Project, "App", Path.Combine(temp.Folder, "owner"));
        workspaces = new FakeWorkspaces(Path.Combine(temp.Folder, "worktrees"));
    }

    public void Dispose() => temp.Dispose();

    private AssistantController Controller(ProviderKind provider = ProviderKind.Claude) => new(new AssistantControllerOptions
    {
        Agents = agents,
        Thread = id => chats.GetValueOrDefault(id),
        Projects = () => [project],
        Profiles = () => [new AssistantProfile("dextra", "Dextra") { Connections = new(["calendar"]) }],
        Jobs = new AgentJobs(new FakeAgentJobFactory()),
        Workspaces = workspaces,
        Audit = e =>
        {
            lock (audit) audit.Add(e);
        },
        Memory = new FakeMemory(FakeMemory.Plenty),
        Provider = provider,
        Model = "model-a",
    });

    /// <summary>A mission started through the runner, with its lead chat on record as the app would have it.</summary>
    private MissionRecord Mission(AssistantController controller)
    {
        var start = controller.Runner.Start(new MissionRequest("Fix the login timeout", "Make it 30 s.", "dextra", project.Id, project.Path, ProviderKind.Claude));
        Assert.True(MissionRig.Within(start.Launched, 10));
        var record = controller.Runner.Find(start.Record.Id)!;
        var lead = record.LeadThread!.Value;
        chats[lead] = MissionRig.NewChat(() => new ChatThread(project.Id, ProviderKind.Claude, "model-a", null, SealedProviderConfig.CappedMode) { Id = lead, MissionID = record.Id, Title = "Lead" });
        return record;
    }

    private static AssistantToolCall Call(AssistantChat chat, string tool, string arguments, Verdict verdict = Verdict.Ask, ActionClass actionClass = ActionClass.Agents) =>
        new(chat, AssistantToolCatalog.Find(tool)!, JSONValue.Parse(arguments)!, new Classification(actionClass, "the tool's class"),
            new PolicyDecision(verdict, PolicyRule.AlwaysConfirmed, "Asks: the owner's call"), AssistantRoute.Mcp);

    private static AssistantChat ChatOf(Guid thread, Guid? mission) =>
        new(thread, "dextra", mission, mission is null ? AssistantChatRole.Home : AssistantChatRole.Mission, ProviderKind.Claude, "model-a");

    [Fact]
    public void Auto_answering_is_off_and_the_engines_allow_still_waits_for_the_owner()
    {
        var controller = Controller();
        Assert.False(controller.AutoAnswering);
        var record = Mission(controller);
        var lead = record.LeadThread!.Value;

        Assert.True(controller.OnApproval(lead, MissionRig.Approval("s1", "git status")));
        var status = controller.Queue.Of(record.Id).Single();
        Assert.Equal(DecisionWeight.Reversible, status.Weight);
        Assert.Equal("Asks until nth's injection checks pass", status.Rule);
        Assert.Equal(ActionClass.Work, status.Class);

        Assert.True(controller.OnApproval(lead, MissionRig.Approval("p1", "git push origin main")));
        var push = controller.Queue.Of(record.Id).Single(c => c.RequestId == "p1");
        Assert.Equal(DecisionWeight.Irreversible, push.Weight);
        Assert.Equal(ActionClass.Outward, push.Class);
        Assert.Equal("Always confirmed", push.Rule);
        Assert.Empty(agents.Resolutions);

        Assert.Null(controller.Queue.Answer(push.Id, DecisionAnswer.DoIt));
        Assert.Equal([(lead, "p1", true)], agents.Resolutions);
        Assert.Contains(audit, e => e.Tool == "approval" && e.Result == "asked: AlwaysConfirmed");
        Assert.False(controller.OnApproval(Guid.NewGuid(), MissionRig.Approval("x", "git status")));
    }

    [Fact]
    public void A_mission_chats_seal_carries_only_its_assistants_listed_servers()
    {
        var controller = Controller();
        var record = Mission(controller);
        var seal = controller.Seal(chats[record.LeadThread!.Value])!;
        Assert.Equal(record.Id, seal.MissionId);
        Assert.Equal(["calendar"], seal.Connections);
        Assert.Null(controller.Seal(MissionRig.NewChat(() => new ChatThread(project.Id, ProviderKind.Claude, null, null, RuntimeMode.FullAccess))));
    }

    [Fact]
    public void It_knows_a_mission_chat_a_home_chat_and_a_chat_that_is_no_assistants()
    {
        var controller = Controller();
        var record = Mission(controller);
        var lead = record.LeadThread!.Value;
        Assert.Equal(new AssistantChatIdentity("dextra", record.Id, AssistantChatRole.Mission), controller.Identify(lead));

        var assistant = Guid.NewGuid();
        var home = MissionRig.NewChat(() => new ChatThread(project.Id, ProviderKind.Claude, null, null, RuntimeMode.Supervised) { AssistantID = assistant });
        var hidden = home with { Id = Guid.NewGuid(), HiddenFromAssistant = true };
        var plain = MissionRig.NewChat(() => new ChatThread(project.Id, ProviderKind.Claude, null, null, RuntimeMode.Supervised));
        foreach (var chat in new[] { home, hidden, plain }) chats[chat.Id] = chat;
        Assert.Equal(new AssistantChatIdentity(assistant.ToString("N"), null, AssistantChatRole.Home), controller.Identify(home.Id));
        Assert.Null(controller.Identify(hidden.Id));
        Assert.Null(controller.Identify(plain.Id));
        Assert.Null(controller.Identify(Guid.NewGuid()));
    }

    [Fact]
    public async Task Start_mission_starts_a_sealed_mission_and_refuses_a_provider_nth_cannot_seal()
    {
        var controller = Controller();
        var home = Guid.NewGuid();
        var started = await controller.Run(Call(ChatOf(home, null), "start_mission", """{"title":"Invoices","brief":"Sort the invoices.","project":"App"}"""), CancellationToken.None);
        Assert.False(started.IsError, started.Text);
        var mission = Assert.Single(controller.Runner.Missions);
        Assert.Equal("Invoices", mission.Mission.Title);
        Assert.Equal(MissionOrigin.Owner, mission.Mission.Origin);
        Assert.Contains(mission.Id.ToString("D"), started.Text, StringComparison.Ordinal);

        var noProject = await controller.Run(Call(ChatOf(home, null), "start_mission", """{"title":"X","brief":"Y","project":"Elsewhere"}"""), CancellationToken.None);
        Assert.True(noProject.IsError);
        Assert.Equal("Not run: name the project the mission works in (its id or its name).", noProject.Text);

        var cursor = Controller(ProviderKind.Cursor);
        var refused = await cursor.Run(Call(ChatOf(home, null), "start_mission", """{"title":"X","brief":"Y"}"""), CancellationToken.None);
        Assert.True(refused.IsError);
        Assert.Equal("Not started: " + SealedProviderConfig.Refusal(ProviderKind.Cursor), refused.Text);
        Assert.Equal(MissionState.Blocked, cursor.Runner.Missions.Single().Mission.State);
        Assert.DoesNotContain(agents.Chats, c => c.Launch.Provider == ProviderKind.Cursor);
    }

    [Fact]
    [Trait("Decision", "ED-NEW-dextra-as5-12")]
    public void A_stopped_missions_chats_may_not_launch_again_until_it_resumes()
    {
        var controller = Controller();
        var record = Mission(controller);
        var lead = record.LeadThread!.Value;
        var chat = chats[lead];
        Assert.Null(controller.LaunchRefusal(chat));

        Assert.Null(controller.Runner.Stop(record.Id));
        Assert.Equal(AssistantController.StoppedLaunch, controller.LaunchRefusal(chat));
        agents.Finish(lead, null);
        controller.OnTurnFinished(lead, TurnStatus.Interrupted, continues: false);
        MissionRig.Until(() => controller.Runner.Find(record.Id)!.Mission.State == MissionState.Stopped, "the mission to stop");
        Assert.Equal(AssistantController.StoppedLaunch, controller.LaunchRefusal(chat));

        Assert.Null(controller.Runner.Resume(record.Id));
        Assert.Null(controller.LaunchRefusal(chat));
        Assert.Null(controller.LaunchRefusal(MissionRig.NewChat(() => new ChatThread(project.Id, ProviderKind.Claude, null, null, RuntimeMode.FullAccess))));
    }

    [Fact]
    public async Task Message_and_stop_agent_reach_only_this_assistants_mission_chats()
    {
        var controller = Controller();
        var record = Mission(controller);
        var lead = record.LeadThread!.Value;
        var home = ChatOf(Guid.NewGuid(), null);

        var redirect = await controller.Run(Call(home, "message_agent", $$"""{"agent":"{{lead:D}}","message":"Use 30 s."}"""), CancellationToken.None);
        Assert.Equal("Sent to the lead.", redirect.Text);
        Assert.Equal("Use 30 s.", controller.Runner.Find(record.Id)!.PendingRedirect);

        var stranger = await controller.Run(Call(home, "message_agent", $$"""{"agent":"{{Guid.NewGuid():D}}","message":"hi"}"""), CancellationToken.None);
        Assert.Equal("Not run: that agent is not this assistant's.", stranger.Text);
        var other = home with { AssistantId = "astur-1" };
        Assert.Equal("Not run: that agent is not this assistant's.",
            (await controller.Run(Call(other, "stop_agent", $$"""{"agent":"{{lead:D}}"}"""), CancellationToken.None)).Text);

        var stopped = await controller.Run(Call(home, "stop_agent", $$"""{"agent":"{{lead:D}}"}"""), CancellationToken.None);
        Assert.Equal("The mission is stopping; its work is kept.", stopped.Text);
        Assert.Equal(MissionState.Stopping, controller.Runner.Find(record.Id)!.Mission.State);
        Assert.Equal(1, agents.Of(lead).Interrupts);
    }

    [Fact]
    public async Task Ask_owner_raises_a_question_and_the_answer_comes_back_to_the_chat_when_its_turn_ends()
    {
        var controller = Controller();
        var record = Mission(controller);
        var lead = record.LeadThread!.Value;
        var asked = await controller.Run(Call(ChatOf(lead, record.Id), "ask_owner", """{"question":"Which colour?","options":["Blue","Green"]}"""), CancellationToken.None);
        Assert.False(asked.IsError);
        var card = controller.Queue.Find(Guid.Parse(asked.DecisionId!))!;
        Assert.Equal(DecisionKind.Question, card.Kind);
        Assert.Equal(["Blue", "Green"], card.Options);
        Assert.Equal(record.Id, card.MissionId);

        Assert.Null(controller.Queue.Answer(card.Id, DecisionAnswer.Pick("Blue")));
        Assert.Single(agents.Of(lead).Sent); // its turn runs: the answer waits
        agents.Finish(lead, "Waiting for the colour.");
        controller.OnTurnFinished(lead, TurnStatus.Completed, continues: false);
        Assert.Contains("The owner answered your question “Which colour?”: Blue", agents.Of(lead).Sent);
    }

    [Fact]
    public async Task A_tool_call_the_policy_asks_about_runs_only_on_the_owners_allow_and_its_result_goes_to_the_chat()
    {
        var controller = Controller();
        var homeThread = Guid.NewGuid();
        agents.Restore(homeThread, new AgentLaunch(Guid.Empty, "dextra", "Home", "", project.Id, project.Path, ProviderKind.Claude));
        var call = Call(ChatOf(homeThread, null), "start_mission", """{"title":"Invoices","brief":"Sort them.","project":"App"}""");
        var id = Guid.Parse(await controller.Ask(call, CancellationToken.None));
        var card = controller.Queue.Find(id)!;
        Assert.Equal(DecisionKind.ToolCall, card.Kind);
        Assert.Contains(card.Inputs, i => i.Name == "title" && i.Value == "Invoices");
        Assert.Empty(controller.Runner.Missions);

        Assert.Null(controller.Queue.Answer(id, DecisionAnswer.AllowOnce));
        Assert.Single(controller.Runner.Missions);
        Assert.StartsWith("nth-dextra start_mission (the owner allowed it): Mission ", agents.Of(homeThread).Sent.Single(), StringComparison.Ordinal);

        var denied = Guid.Parse(await controller.Ask(call with { Id = "second" }, CancellationToken.None));
        agents.Finish(homeThread, "ok");
        Assert.Null(controller.Queue.Answer(denied, DecisionAnswer.Deny("Not now.")));
        Assert.Equal("The owner did not allow nth-dextra start_mission. Not now.", agents.Of(homeThread).Sent[^1]);
        Assert.Single(controller.Runner.Missions);
        Assert.Contains(audit, e => e.Result == "denied by the owner");
    }

    [Fact]
    public async Task The_tools_of_later_units_say_they_are_not_built_yet()
    {
        var controller = Controller();
        var result = await controller.Run(Call(ChatOf(Guid.NewGuid(), null), "remember", """{"text":"x"}"""), CancellationToken.None);
        Assert.True(result.IsError);
        Assert.Equal("Not run: remember is not part of this build of Dextra yet.", result.Text);
    }
}
