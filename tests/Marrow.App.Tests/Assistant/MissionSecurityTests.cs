using Marrow.App.Assistant;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Core.Support;
using Marrow.Services.Assistant;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.App.Tests.Assistant;

// Dextra AS-5 security review (DESIGN.md rev 5, 12.1a, 12.3, 12.4, 12.5, 12.6; ED-NEW-dextra-as5-13, -14): an agent's gate
// slot moves to its chat in one step; the owner's answer reaches the provider only while its request is still pending, so
// Stop wins a race with it; every allow is logged with the card or chat answer that gave it, and an allow or a grant that
// cannot be logged does not happen; the nth-dextra tools act only within the calling chat's own missions, pass a tainted
// chat's material as data and carry its taint, never brief the reviewer, and are bounded; each chat is classified in its
// own folder; and a mission chat's helpers are the mission's agents (Stop reaches them, the gate counts them, the review
// waits for them). Fakes only; no provider runs.
[Trait("Decision", "ED-NEW-dextra-as5-13")]
[Trait("Feature", "AS-5")]
public sealed class MissionSecurityTests : IDisposable
{
    private readonly AssistantTempRoot temp = new();
    private readonly FakeMissionAgents agents = new();
    private readonly Dictionary<Guid, ChatThread> chats = [];
    private readonly List<AuditEntry> audit = [];
    private readonly MissionProject project;
    private Func<AuditEntry, bool>? auditFails;

    public MissionSecurityTests() => project = new MissionProject(MissionRig.Project, "App", Path.Combine(temp.Folder, "owner"));

    public void Dispose() => temp.Dispose();

    private MissionRig Rig(MissionLimits? limits = null) => new(temp.Folder, limits: limits);

    private AssistantController Controller() => new(new AssistantControllerOptions
    {
        Agents = agents,
        Thread = id => chats.GetValueOrDefault(id),
        Projects = () => [project],
        Profiles = () => [new AssistantProfile("dextra", "Dextra")],
        Jobs = new AgentJobs(new FakeAgentJobFactory()),
        Workspaces = new FakeWorkspaces(Path.Combine(temp.Folder, "worktrees")),
        Audit = e =>
        {
            if (auditFails?.Invoke(e) == true) throw new IOException("The activity log's disk is full.");
            lock (audit) audit.Add(e);
        },
        Memory = new FakeMemory(FakeMemory.Plenty),
        Model = "model-a",
    });

    /// <summary>A mission started through the controller, its lead chat on record in the mission's own folder.</summary>
    private MissionRecord Mission(AssistantController controller, string title = "Fix the login timeout", Guid? parent = null)
    {
        var start = controller.Runner.Start(new MissionRequest(title, "Make it 30 s.", "dextra", project.Id, project.Path, ProviderKind.Claude) { Parent = parent });
        Assert.True(MissionRig.Within(start.Launched, 10));
        var record = controller.Runner.Find(start.Record.Id)!;
        var lead = record.LeadThread!.Value;
        chats[lead] = MissionRig.NewChat(() => new ChatThread(project.Id, ProviderKind.Claude, "model-a", null, SealedProviderConfig.CappedMode)
        {
            Id = lead, MissionID = record.Id, Title = "Lead", WorktreePath = record.Workspace!.WorkingFolder,
        });
        return record;
    }

    private static AssistantToolCall Call(AssistantChat chat, string tool, string arguments, bool dataOnly = false) =>
        new(chat, AssistantToolCatalog.Find(tool)!, JSONValue.Parse(arguments)!, new Classification(ActionClass.Agents, "the tool's class"),
            new PolicyDecision(Verdict.Allow, PolicyRule.Alone, "Allowed") { DataOnly = dataOnly }, AssistantRoute.Mcp);

    private static AssistantChat ChatOf(Guid thread, Guid? mission) =>
        new(thread, "dextra", mission, mission is null ? AssistantChatRole.Home : AssistantChatRole.Mission, ProviderKind.Claude, "model-a");

    private static ApprovalRequest Write(string id, string path) =>
        new(id, ApprovalRequest.Kind.FileChange, path, new ValueList<ApprovalRequest.Option>([
            new("allow", "Allow", ApprovalRequest.Option.Role.Approve),
            new("deny", "Deny", ApprovalRequest.Option.Role.Decline),
        ]));

    // ---- the gate ----

    [Fact]
    public void A_held_slot_moves_to_its_chat_in_one_step_and_no_waiting_agent_takes_it_in_between()
    {
        var gate = new AgentGate(() => new MissionLimits { LocalAgents = 1 }, new FakeMemory(FakeMemory.Plenty));
        var (slot, chat, waiter) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        Assert.True(gate.Enter(slot, AgentPlace.Local).IsCompletedSuccessfully);
        var waiting = gate.Enter(waiter, AgentPlace.Local);

        Assert.True(gate.Rekey(slot, chat));
        Assert.True(gate.Holds(chat));
        Assert.False(gate.Holds(slot));
        Assert.False(waiting.IsCompleted);
        Assert.Equal(1, gate.Running);
        Assert.False(gate.Rekey(slot, Guid.NewGuid()));

        gate.Leave(chat);
        Assert.True(MissionRig.Within(waiting, 5));

        // Two held ids that become one free a slot for the next in line.
        var pair = new AgentGate(() => new MissionLimits { LocalAgents = 2 }, new FakeMemory(FakeMemory.Plenty));
        var (a, b, next) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        Assert.True(pair.Enter(a, AgentPlace.Local).IsCompletedSuccessfully);
        Assert.True(pair.Enter(b, AgentPlace.Local).IsCompletedSuccessfully);
        var third = pair.Enter(next, AgentPlace.Local);
        Assert.False(third.IsCompleted);
        Assert.True(pair.Rekey(a, b));
        Assert.True(MissionRig.Within(third, 5));
        Assert.Equal(2, pair.Running);
    }

    [Fact]
    public void A_running_lead_keeps_its_slot_and_the_next_mission_waits_for_it()
    {
        var rig = Rig(new MissionLimits { LocalAgents = 1 });
        var first = rig.Started();
        var second = rig.Runner.Start(rig.Request("Second"));
        Assert.False(MissionRig.Within(second.Launched, 0.3));
        Assert.True(rig.Gate.Holds(first.LeadThread!.Value));
        Assert.Equal(1, rig.Gate.Running);
        Assert.Null(rig.Now(second.Record).LeadThread);
    }

    // ---- the owner's answers ----

    [Fact]
    public void An_answer_that_arrives_after_Stop_sends_nothing_and_the_request_is_denied_once()
    {
        var rig = Rig();
        var record = rig.Started();
        var lead = record.LeadThread!.Value;
        Assert.True(rig.Runner.OnApproval(lead, MissionRig.Approval("a1", "git push")));
        var card = rig.Queue.Of(record.Id).Single();

        rig.Agents.Defer = true;
        Assert.Null(rig.Queue.Answer(card.Id, DecisionAnswer.AllowOnce)); // on its way to the main thread
        Assert.Empty(rig.Agents.Resolutions);
        Assert.Null(rig.Runner.Stop(record.Id));
        rig.Agents.RunPosted();
        rig.Agents.Defer = false;

        Assert.Equal(new[] { (lead, "a1", false) }, rig.Agents.Resolutions);
        Assert.DoesNotContain(rig.Audit, e => (e.Result ?? "").StartsWith("allowed", StringComparison.Ordinal));
    }

    [Fact]
    public void Every_answer_is_logged_with_its_card_and_an_allow_that_cannot_be_logged_is_a_deny()
    {
        var rig = Rig();
        var record = rig.Started();
        var lead = record.LeadThread!.Value;

        Assert.True(rig.Runner.OnApproval(lead, MissionRig.Approval("a1", "git push")));
        var allowed = rig.Queue.Of(record.Id).Single();
        Assert.Null(rig.Queue.Answer(allowed.Id, DecisionAnswer.AllowOnce));
        Assert.Contains(rig.Audit, e => e.Result == "allowed by the owner: AllowOnce" && e.AllowedBy == "card:" + allowed.Id.ToString("D"));

        Assert.True(rig.Runner.OnApproval(lead, MissionRig.Approval("a2", "git push --tags")));
        var denied = rig.Queue.Of(record.Id).Single();
        Assert.Null(rig.Queue.Answer(denied.Id, DecisionAnswer.Deny("not now")));
        Assert.Contains(rig.Audit, e => e.Result == "denied by the owner: Deny" && e.AllowedBy == "card:" + denied.Id.ToString("D"));

        rig.AuditFails = e => (e.Result ?? "").StartsWith("allowed by the owner", StringComparison.Ordinal);
        Assert.True(rig.Runner.OnApproval(lead, MissionRig.Approval("a3", "git push --force")));
        var unlogged = rig.Queue.Of(record.Id).Single();
        Assert.Null(rig.Queue.Answer(unlogged.Id, DecisionAnswer.AllowOnce));
        Assert.Equal(new[] { (lead, "a1", true), (lead, "a2", false), (lead, "a3", false) }, rig.Agents.Resolutions);
    }

    [Fact]
    public void An_answer_in_the_chat_is_logged_as_the_owners_withdraws_the_card_and_is_a_decline_when_it_cannot_be_logged()
    {
        var rig = Rig();
        var record = rig.Started();
        var lead = record.LeadThread!.Value;
        var first = MissionRig.Approval("c1", "npm install");
        Assert.True(rig.Runner.OnApproval(lead, first));
        var card = rig.Queue.Of(record.Id).Single();

        var chosen = rig.Runner.OnChatAnswer(lead, first, first.Options[0]);
        Assert.Equal("allow", chosen.Id);
        Assert.Contains(rig.Audit, e => e.Result == "allowed by the owner in the chat: Allow" && e.AllowedBy == "chat:" + lead.ToString("D"));
        Assert.Empty(rig.Queue.Of(record.Id));
        Assert.NotNull(rig.Queue.Answer(card.Id, DecisionAnswer.AllowOnce)); // the card is gone; its answer goes nowhere
        Assert.Empty(rig.Agents.Resolutions);
        Assert.Same(first.Options[0], rig.Runner.OnChatAnswer(lead, first, first.Options[0])); // settled already: as it is

        rig.AuditFails = e => (e.Result ?? "").StartsWith("allowed by the owner", StringComparison.Ordinal);
        var second = MissionRig.Approval("c2", "npm install left-pad");
        Assert.True(rig.Runner.OnApproval(lead, second));
        Assert.Equal("deny", rig.Runner.OnChatAnswer(lead, second, second.Options[1]).Id); // "always" is not kept either
        Assert.Empty(rig.Queue.Of(record.Id));

        var stranger = MissionRig.Approval("c3", "ls");
        Assert.Same(stranger.Options[0], rig.Runner.OnChatAnswer(Guid.NewGuid(), stranger, stranger.Options[0]));
    }

    [Fact]
    public void A_grant_is_logged_with_its_card_and_one_that_cannot_be_logged_is_not_kept()
    {
        var controller = Controller();
        var record = Mission(controller);
        var lead = record.LeadThread!.Value;
        var elsewhere = Path.Combine(temp.Folder, "notes", "today.txt");

        Assert.True(controller.OnApproval(lead, Write("g1", elsewhere)));
        var card = controller.Queue.Of(record.Id).Single();
        Assert.Contains(DecisionAnswerKind.ForThisMission, card.Answers);
        auditFails = e => e.Tool == "grant";
        Assert.Null(controller.Queue.Answer(card.Id, DecisionAnswer.ForThisMission));
        Assert.Contains(agents.Resolutions, r => r == (lead, "g1", true));
        Assert.Empty(controller.Grants);

        auditFails = null;
        Assert.True(controller.OnApproval(lead, Write("g2", elsewhere)));
        var again = controller.Queue.Of(record.Id).Single();
        Assert.Null(controller.Queue.Answer(again.Id, DecisionAnswer.ForThisMission));
        var grant = Assert.Single(controller.Grants);
        Assert.Equal(GrantScopeKind.Mission, grant.Scope.Kind);
        Assert.Contains(audit, e => e.Tool == "grant" && e.AllowedBy == "card:" + again.Id.ToString("D"));
    }

    [Fact]
    public async Task A_tool_call_the_owner_allows_runs_only_once_his_allow_is_logged_with_its_card()
    {
        var controller = Controller();
        var record = Mission(controller);
        var lead = record.LeadThread!.Value;
        var home = ChatOf(Guid.NewGuid(), null);
        var stop = Call(home, "stop_agent", $$"""{"agent":"{{lead:D}}"}""");

        auditFails = e => (e.Result ?? "").StartsWith("allowed by the owner", StringComparison.Ordinal);
        var unlogged = Guid.Parse(await controller.Ask(stop, CancellationToken.None));
        Assert.Null(controller.Queue.Answer(unlogged, DecisionAnswer.AllowOnce));
        Assert.Equal(MissionState.Working, controller.Runner.Find(record.Id)!.Mission.State);
        Assert.DoesNotContain(audit, e => e.AllowedBy == "card:" + unlogged.ToString("D"));

        auditFails = null;
        var logged = Guid.Parse(await controller.Ask(stop with { Id = "second" }, CancellationToken.None));
        Assert.Null(controller.Queue.Answer(logged, DecisionAnswer.AllowOnce));
        Assert.NotEqual(MissionState.Working, controller.Runner.Find(record.Id)!.Mission.State);
        Assert.Equal(2, audit.Count(e => e.AllowedBy == "card:" + logged.ToString("D")));

        // A line about a refusal that cannot be written is reported, never dropped in silence.
        var faults = new List<string>();
        controller.Runner.Faulted += faults.Add;
        auditFails = _ => true;
        var denied = Guid.Parse(await controller.Ask(stop with { Id = "third" }, CancellationToken.None));
        Assert.Null(controller.Queue.Answer(denied, DecisionAnswer.Deny()));
        Assert.Contains(faults, f => f.StartsWith("The activity log could not be written: ", StringComparison.Ordinal));
    }

    // ---- the nth-dextra tools ----

    [Fact]
    [Trait("Decision", "ED-NEW-dextra-as5-14")]
    public async Task A_mission_chat_acts_only_on_its_own_mission_and_the_ones_under_it()
    {
        var controller = Controller();
        var a = Mission(controller, "A");
        var b = Mission(controller, "B");
        var child = Mission(controller, "A's helpers", parent: a.Id);
        var fromA = ChatOf(a.LeadThread!.Value, a.Id);

        Assert.Equal(AssistantController.NotInScope, (await controller.Run(Call(fromA, "launch_hydra_team", $$"""{"mission":"{{b.Id:D}}","brief":"Help."}"""), CancellationToken.None)).Text);
        Assert.Equal(AssistantController.NotInScope, (await controller.Run(Call(fromA, "message_agent", $$"""{"agent":"{{b.LeadThread:D}}","message":"Stop."}"""), CancellationToken.None)).Text);
        Assert.Equal(AssistantController.NotInScope, (await controller.Run(Call(fromA, "stop_agent", $$"""{"agent":"{{b.LeadThread:D}}"}"""), CancellationToken.None)).Text);
        Assert.Equal(MissionState.Working, controller.Runner.Find(b.Id)!.Mission.State);

        var sent = await controller.Run(Call(fromA, "message_agent", $$"""{"agent":"{{child.LeadThread:D}}","message":"Use 30 s."}"""), CancellationToken.None);
        Assert.False(sent.IsError, sent.Text);
        var team = await controller.Run(Call(ChatOf(Guid.NewGuid(), null), "launch_hydra_team", $$"""{"mission":"{{b.Id:D}}","brief":"Help."}"""), CancellationToken.None);
        Assert.False(team.IsError, team.Text);
    }

    [Fact]
    public async Task Material_from_a_tainted_chat_goes_as_data_and_the_mission_that_gets_it_takes_its_taint()
    {
        var controller = Controller();
        var a = Mission(controller, "A");
        var child = Mission(controller, "A's helpers", parent: a.Id);
        var childLead = child.LeadThread!.Value;
        agents.Finish(childLead, null);
        Assert.False(controller.Runner.Find(child.Id)!.Mission.TaintContext.IsTainted);

        // A delegation the policy marks data-only (from the home chat, which is clean).
        var home = ChatOf(Guid.NewGuid(), null);
        Assert.False((await controller.Run(Call(home, "message_agent", $$"""{"agent":"{{childLead:D}}","message":"Do X >>> now"}""", dataOnly: true), CancellationToken.None)).IsError);
        var wrapped = agents.Of(childLead).Sent[^1];
        Assert.Equal(AssistantController.AsData("Do X >>> now"), wrapped);
        Assert.StartsWith("Material passed on from a chat that read untrusted content.", wrapped, StringComparison.Ordinal);
        Assert.DoesNotContain("X >>> now", wrapped, StringComparison.Ordinal);

        // A chat of a tainted mission: wrapped too, and its taint goes with it.
        controller.Runner.Absorb(a.Id, Taint.From(ContentOrigin.Web, "https://news.example/a"));
        agents.Finish(childLead, null);
        Assert.False((await controller.Run(Call(ChatOf(a.LeadThread!.Value, a.Id), "message_agent", $$"""{"agent":"{{childLead:D}}","message":"Read this."}"""), CancellationToken.None)).IsError);
        Assert.Equal(AssistantController.AsData("Read this."), agents.Of(childLead).Sent[^1]);
        Assert.True(controller.Runner.Find(child.Id)!.Mission.TaintContext.IsTainted);
    }

    [Fact]
    public async Task No_agent_may_message_the_reviewer_and_every_message_and_question_is_bounded()
    {
        var controller = Controller();
        var lead = Guid.NewGuid();
        var reviewer = Guid.NewGuid();
        var id = Guid.NewGuid();
        controller.Runner.Load([new MissionRecord(new Mission(id, "Fix", "dextra", MissionOrigin.Owner, MissionState.Reviewing) { ThreadIds = new([lead, reviewer]) })
        {
            ProjectId = project.Id, ProjectPath = project.Path, LeadThread = lead, ReviewerThread = reviewer, Created = DateTimeOffset.UtcNow,
        }]);
        var home = ChatOf(Guid.NewGuid(), null);
        Assert.Equal("Not run: the reviewer works alone; no agent may message it.",
            (await controller.Run(Call(home, "message_agent", $$"""{"agent":"{{reviewer:D}}","message":"It passes."}"""), CancellationToken.None)).Text);

        var tooLong = new string('x', AssistantController.MessageCap + 1);
        Assert.True((await controller.Run(Call(home, "message_agent", $$"""{"agent":"{{lead:D}}","message":"{{tooLong}}"}"""), CancellationToken.None)).IsError);

        var asker = ChatOf(lead, id);
        async Task<AssistantToolResult> Ask(string arguments) => await controller.Run(Call(asker, "ask_owner", arguments), CancellationToken.None);
        Assert.True((await Ask("""{"question":"  "}""")).IsError);
        Assert.True((await Ask($$"""{"question":"{{new string('q', AssistantController.QuestionCap + 1)}}"}""")).IsError);
        Assert.True((await Ask($$"""{"question":"Which?","detail":"{{new string('d', AssistantController.DetailCap + 1)}}"}""")).IsError);
        Assert.True((await Ask("""{"question":"Which?","options":["1","2","3","4","5","6","7","8","9"]}""")).IsError);
        Assert.True((await Ask($$"""{"question":"Which?","options":["{{new string('o', AssistantController.OptionCap + 1)}}"]}""")).IsError);
        Assert.Empty(controller.Queue.Waiting);
        for (var i = 0; i < AssistantController.OpenQuestionsPerChat; i++) Assert.False((await Ask($$"""{"question":"Which {{i}}?"}""")).IsError);
        Assert.True((await Ask("""{"question":"One more?"}""")).IsError);
        Assert.Equal(AssistantController.OpenQuestionsPerChat, controller.Queue.Waiting.Count);
    }

    [Fact]
    public void What_waits_for_a_busy_lead_is_bounded_and_a_refused_redirect_is_not_logged()
    {
        var rig = Rig();
        var record = rig.Started(); // its lead's turn runs
        Assert.Null(rig.Runner.Redirect(record.Id, new string('r', MissionRunner.RedirectCap)));
        Assert.Equal("Too much already waits for the lead; say it again when its turn ends.", rig.Runner.Redirect(record.Id, "and one more"));
        Assert.Equal(MissionRunner.RedirectCap, rig.Now(record).PendingRedirect!.Length);
        Assert.Single(rig.Audit, e => e.Result == "redirect");
    }

    // ---- each chat in its own folder ----

    [Fact]
    [Trait("Decision", "ED-NEW-dextra-as5-14")]
    public void Each_chat_is_classified_in_its_own_folder_and_a_chat_nth_cannot_place_gets_none()
    {
        var worktree = Path.Combine(temp.Folder, "worktree");
        var outside = Path.Combine(temp.Folder, "owner");
        Directory.CreateDirectory(Path.Combine(worktree, "src"));
        Directory.CreateDirectory(Path.Combine(outside, "src"));
        var record = new MissionRecord(new Mission(Guid.NewGuid(), "Fix", "dextra", MissionOrigin.Owner, MissionState.Working))
        {
            Workspace = new MissionWorkspace(MissionWorkspaceKind.Worktree, outside, worktree) { WorkingFolder = worktree },
        };
        var removal = new ApprovalRequest("r1", ApprovalRequest.Kind.Command, @"Remove-Item -Recurse -Force .\src", ValueList<ApprovalRequest.Option>.Empty);

        var own = MissionClassifier.ForAgent(record, [], worktree);
        Assert.Equal(worktree, own.WorkingDirectory);
        Assert.Equal(ActionClass.Work, ActionClassifier.ClassifyApproval(removal, own, ShellKind.PowerShell, ProviderKind.Claude).Class);

        var away = MissionClassifier.ForAgent(record, [], outside);
        Assert.Equal(outside, away.WorkingDirectory);
        Assert.Equal(worktree, away.MissionWorktree);
        Assert.Equal(ActionClass.Destructive, ActionClassifier.ClassifyApproval(removal, away, ShellKind.PowerShell, ProviderKind.Claude).Class);

        var nowhere = MissionClassifier.ForAgent(record, [], null);
        Assert.Null(nowhere.WorkingDirectory);
        Assert.Null(nowhere.MissionWorktree);
        Assert.Empty(nowhere.AccessFolders);
        Assert.NotEqual(ActionClass.Work, ActionClassifier.ClassifyApproval(Write("r2", Path.Combine(worktree, "src", "a.cs")), nowhere, provider: ProviderKind.Claude).Class);
    }

    [Fact]
    [Trait("Decision", "ED-NEW-dextra-as5-14")]
    public void The_controller_places_a_relative_path_where_the_asking_chat_works()
    {
        var controller = Controller();
        var record = Mission(controller);
        var lead = record.LeadThread!.Value;
        var helper = Guid.NewGuid();
        Assert.True(controller.Runner.HelperAdded(lead, helper));
        // A chat on record in the owner's own project folder (no mission folder of its own).
        chats[helper] = MissionRig.NewChat(() => new ChatThread(project.Id, ProviderKind.Claude, "model-a", null, SealedProviderConfig.CappedMode)
        {
            Id = helper, MissionID = record.Id, Title = "Helper",
        });

        Assert.True(controller.OnApproval(helper, Write("h1", @"src\login.cs")));
        Assert.Equal(ActionClass.WriteElsewhere, controller.Queue.Of(record.Id).Single(c => c.ThreadId == helper).Class);
        Assert.True(controller.OnApproval(lead, Write("l1", @"src\login.cs")));
        Assert.Equal(ActionClass.Work, controller.Queue.Of(record.Id).Single(c => c.ThreadId == lead).Class);
    }

    // ---- helpers of a mission chat ----

    [Fact]
    [Trait("Decision", "ED-NEW-dextra-as5-14")]
    public void A_helper_joins_only_a_running_mission_and_Stop_reaches_it()
    {
        var rig = Rig();
        Assert.False(rig.Runner.HelperAdded(Guid.NewGuid(), Guid.NewGuid()));
        var record = rig.Started();
        var lead = record.LeadThread!.Value;
        var helper = Guid.NewGuid();
        Assert.True(rig.Runner.HelperAdded(lead, helper));
        Assert.Equal(record.Id, rig.Runner.MissionOf(helper)!.Id);
        Assert.Contains(helper, rig.Now(record).Mission.ThreadIds);
        Assert.Contains(rig.Audit, e => e.Result == "helper opened" && e.Agent == helper.ToString("D"));

        rig.Agents.Restore(helper, rig.Agents.Lead(record).Launch).Running = true;
        rig.Agents.Finish(lead, null);
        Assert.Null(rig.Runner.Stop(record.Id));
        Assert.Equal(1, rig.Agents.Of(helper).Interrupts);
        rig.Runner.CheckStopping();
        Assert.Equal(MissionState.Stopping, rig.Now(record).Mission.State); // the helper's turn still runs
        rig.Agents.Finish(helper, null);
        rig.Runner.CheckStopping();
        Assert.Equal(MissionState.Stopped, rig.Now(record).Mission.State);
        Assert.False(rig.Runner.HelperAdded(lead, Guid.NewGuid()));
    }

    [Fact]
    [Trait("Decision", "ED-NEW-dextra-as5-14")]
    public void A_helper_waits_at_the_gate_frees_its_slot_after_each_turn_and_the_review_waits_for_it()
    {
        var rig = Rig(new MissionLimits { LocalAgents = 2 });
        var record = rig.Started();
        var lead = record.LeadThread!.Value;
        var helper = Guid.NewGuid();
        Assert.True(rig.Runner.HelperAdded(lead, helper));
        Assert.True(MissionRig.Within(rig.Runner.AdmitHelper(helper, ProviderKind.Claude), 5));
        Assert.True(rig.Gate.Holds(helper));
        rig.Agents.Restore(helper, rig.Agents.Lead(record).Launch).Running = true;

        rig.Agents.Finish(lead, "The login now waits 30 s.");
        rig.Runner.OnTurnFinished(lead, TurnStatus.Completed, continues: false);
        Assert.Null(rig.Now(record).ReviewerThread); // the helper still changes files
        Assert.Equal(MissionState.Working, rig.Now(record).Mission.State);

        rig.Agents.Finish(helper, "Checked the tests.");
        rig.Runner.OnTurnFinished(helper, TurnStatus.Completed, continues: false);
        Assert.False(rig.Gate.Holds(helper));
        MissionRig.Until(() => rig.Now(record).ReviewerThread is not null, "the reviewer to start");

        // The mission ends: every chat on record, the helper too, is ended once.
        var reviewer = rig.Now(record).ReviewerThread!.Value;
        rig.Agents.Finish(reviewer, "I read the change and ran the tests.\nVERDICT: PASS");
        rig.Runner.OnTurnFinished(reviewer, TurnStatus.Completed, continues: false);
        MissionRig.Until(() => rig.Now(record).Mission.State == MissionState.Done, "the mission to end");
        Assert.Equal(1, rig.Agents.Of(helper).SessionStops);
        Assert.Equal(1, rig.Agents.Of(reviewer).SessionStops);
        Assert.Equal(1, rig.Agents.Of(lead).SessionStops);
    }
}
