// PORT-ONLY: Dextra (DESIGN.md rev 5, 0 points 1 and 4, 12.3, 15.1, 15.4, 15.5; unit AS-5; ED-NEW-dextra-as5-11). No
// Swift twin. Dextra's controller inside nth's process: it owns the decision queue, the mission runner, the agent gate
// and the grants; it is the host behind the nth-dextra tools (IAssistantToolHost), and the policy engine's caller for
// every provider approval of a mission chat. Auto-answering is off (16, the ordering rule): AS-7 turns it on only after
// its injection corpus passes, so until then the engine's Allow still lands on a card.
using System.Text.Json.Nodes;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Core.Support;
using Marrow.Services.Assistant;

namespace Marrow.App.Assistant;

/// <summary>A project a mission may work in.</summary>
public sealed record MissionProject(Guid Id, string Name, string Path);

public sealed class AssistantControllerOptions
{
    public required IMissionAgents Agents { get; init; }

    /// <summary>A chat by id (AppModel.Thread).</summary>
    public required Func<Guid, ChatThread?> Thread { get; init; }

    /// <summary>The projects missions may work in (the sidebar's), for start_mission's project argument.</summary>
    public required Func<IReadOnlyList<MissionProject>> Projects { get; init; }

    /// <summary>Dextra and its Asturs.</summary>
    public required Func<IReadOnlyList<AssistantProfile>> Profiles { get; init; }

    public required AgentJobs Jobs { get; init; }

    public required IMissionWorkspaces Workspaces { get; init; }

    /// <summary>Writes one activity log line through the single writer; throws when it cannot (then nothing acts).</summary>
    public required Action<AuditEntry> Audit { get; init; }

    /// <summary>Journals a mission (AssistantStore.SaveMission).</summary>
    public Action<MissionRecord>? Save { get; init; }

    public IMemoryReader Memory { get; init; } = WindowsMemoryReader.Instance;

    public Func<MissionLimits> Limits { get; init; } = () => MissionLimits.Default;

    /// <summary>The provider and model a mission's lead runs on until the router (AS-14) picks per step.</summary>
    public ProviderKind Provider { get; init; } = ProviderKind.Claude;

    public string? Model { get; init; }

    public TimeProvider Time { get; init; } = TimeProvider.System;

    /// <summary>nth's own folders (the store, the audit log, the settings): an agent's write there is a hard deny (12.1a).</summary>
    public IReadOnlyList<string> ProtectedRoots { get; init; } = [];

    /// <summary>The environment the classifier places paths with; null reads nth's own (MissionClassifier.CurrentEnvironment).</summary>
    public ValueMap<string, string>? Environment { get; init; }
}

public sealed class AssistantController : IAssistantToolHost, IDisposable
{
    private readonly AssistantControllerOptions o;
    private readonly object gate = new();
    private readonly List<Grant> grants = [];
    private readonly Dictionary<Guid, List<string>> waitingMessages = [];

    public AssistantController(AssistantControllerOptions options)
    {
        o = options;
        Queue = new DecisionQueue(o.Time);
        Gate = new AgentGate(o.Limits, o.Memory, o.Time);
        Runner = new MissionRunner(new MissionRunnerOptions
        {
            Agents = o.Agents,
            Queue = Queue,
            Gate = Gate,
            Jobs = o.Jobs,
            Workspaces = o.Workspaces,
            Policy = ApprovalPolicy,
            Limits = o.Limits,
            Save = o.Save,
            Audit = TryAudit,
            Granted = Granted,
            Time = o.Time,
        });
    }

    public DecisionQueue Queue { get; }

    public AgentGate Gate { get; }

    public MissionRunner Runner { get; }

    /// <summary>
    /// Off until AS-7's injection corpus passes (16): the engine's Allow runs no tool and answers no approval alone. Only
    /// the read-only and stop tools marked RunsBeforeAutoAnswer run on an Allow.
    /// </summary>
    public bool AutoAnswering => false;

    /// <summary>The grants the owner made from cards (For this mission, Always...).</summary>
    public IReadOnlyList<Grant> Grants
    {
        get { lock (gate) return [.. grants]; }
    }

    // ---- the seal and the app's taps ----

    /// <summary>A mission chat's seal: the owner's connected servers its assistant's profile lists (12.1a).</summary>
    public SealedLaunch? Seal(ChatThread thread)
    {
        if (thread.MissionID is not { } mission) return null;
        var assistant = Runner.Find(mission)?.Mission.AssistantId ?? thread.AssistantID?.ToString("N");
        var profile = o.Profiles().FirstOrDefault(p => p.Id == assistant);
        return new SealedLaunch(mission) { Connections = profile?.Connections ?? ValueList<string>.Empty };
    }

    /// <summary>
    /// Why a mission chat may not launch its provider now: its mission is stopping or stopped (12.6), so nothing of it runs
    /// again (a retry, a relaunch, a message typed into its chat) until the owner's Resume. Null: it may.
    /// </summary>
    public string? LaunchRefusal(ChatThread thread) =>
        thread.MissionID is { } mission && Runner.Find(mission)?.Mission.State is MissionState.Stopping or MissionState.Stopped
            ? StoppedLaunch
            : null;

    public const string StoppedLaunch = "This mission is stopped. Resume it on the board to go on.";

    /// <summary>A provider approval in a chat: the runner takes it when the chat is one of its missions'.</summary>
    public bool OnApproval(Guid thread, ApprovalRequest request) => Runner.OnApproval(thread, request);

    public bool OnQuestion(Guid thread, QuestionRequest request) => Runner.OnQuestion(thread, request);

    public void OnRequestResolved(Guid thread, string requestId) => Runner.OnRequestResolved(thread, requestId);

    public void OnTurnFinished(Guid thread, TurnStatus status, bool continues)
    {
        Runner.OnTurnFinished(thread, status, continues);
        if (!continues) Flush(thread);
    }

    public void OnHeadAdded(ChatThread lead, ChatThread head) => Runner.HeadAdded(lead.Id, head.Id, head.Hydra?.DisplayName ?? head.Title ?? "A head",
        native: head.Hydra?.HydraHeadInfoKind == HydraHeadInfo.Kind.Native);

    public void OnHeadFinished(Guid head, TurnStatus status) => Runner.HeadFinished(head, status);

    // ---- the approval policy of mission chats (12.3 point 2) ----

    private ApprovalVerdict ApprovalPolicy(MissionRecord record, Guid thread, ApprovalRequest request)
    {
        var context = MissionClassifier.For(record, o.ProtectedRoots, o.Environment);
        var provider = o.Thread(thread)?.Provider ?? record.Provider;
        var classification = ActionClassifier.ClassifyApproval(request, context, provider: provider);
        var decision = PolicyEngine.Decide(classification, MissionContext(record.Mission.AssistantId, record.Id, record.Mission.TaintContext));
        return new ApprovalVerdict(classification, decision);
    }

    private PolicyContext MissionContext(string assistantId, Guid? mission, TaintContext taint)
    {
        var now = o.Time.GetUtcNow();
        return new PolicyContext(AssistantChain.Of(assistantId, o.Profiles()), now)
        {
            Grants = new ValueList<Grant>(Grants.Where(g => g.AssistantId == assistantId)),
            MissionId = mission,
            TaintContext = taint,
        };
    }

    private void Granted(DecisionItem card, DecisionAnswer answer)
    {
        Grant? grant = answer.Kind switch
        {
            DecisionAnswerKind.Always => answer.Grant,
            DecisionAnswerKind.ForThisMission when card is { Class: { } c, GrantTarget: { } target, MissionId: { } mission } =>
                new Grant(Guid.NewGuid(), card.AssistantId, c, target, GrantScope.ForMission(mission)),
            _ => null,
        };
        if (grant is null || GrantRules.Invalid(grant) is not null) return;
        lock (gate) grants.Add(grant);
    }

    // ---- IAssistantToolHost (the nth-dextra tools) ----

    public AssistantChatIdentity? Identify(Guid thread)
    {
        if (o.Thread(thread) is not { } chat) return null;
        if (chat.MissionID is { } mission)
        {
            var assistant = Runner.Find(mission)?.Mission.AssistantId ?? chat.AssistantID?.ToString("N");
            return assistant is null ? null : new AssistantChatIdentity(assistant, mission, AssistantChatRole.Mission);
        }
        if (chat.AssistantID is { } home && !chat.HiddenFromAssistant) return new AssistantChatIdentity(home.ToString("N"), null, AssistantChatRole.Home);
        return null;
    }

    public PolicyContext PolicyContext(AssistantChat chat)
    {
        var taint = chat.MissionId is { } mission && Runner.Find(mission) is { } record ? record.Mission.TaintContext : TaintContext.Clean;
        return MissionContext(chat.AssistantId, chat.MissionId, taint);
    }

    public Task<AssistantToolResult> Run(AssistantToolCall call, CancellationToken cancellationToken) =>
        Task.FromResult(RunNow(call));

    private AssistantToolResult RunNow(AssistantToolCall call)
    {
        var args = call.Arguments;
        string? Text(string name) => args[name]?.JSONValueString;
        long? Whole(string name) => args[name] is { } v ? v.JSONValueInt ?? (long?)v.JSONValueDouble : null;
        IReadOnlyList<string> List(string name) => args[name]?.JSONValueArray is { } items ? [.. items.Select(i => i.JSONValueString).OfType<string>()] : [];
        var chat = call.Chat;
        switch (call.Tool.Name)
        {
            case "start_mission":
            case "launch_hydra_team":
            {
                var team = call.Tool.Name == "launch_hydra_team";
                MissionRecord? parent = team && Text("mission") is { } given && Guid.TryParse(given, out var pid) ? Runner.Find(pid) : null;
                if (team && parent is null) return AssistantToolResult.Failed("Not run: there is no such mission.");
                var project = parent is not null
                    ? new MissionProject(parent.ProjectId, "", parent.ProjectPath)
                    : ResolveProject(Text("project"));
                if (project is null) return AssistantToolResult.Failed("Not run: name the project the mission works in (its id or its name).");
                var title = team ? "Team: " + parent!.Mission.Title : Text("title") ?? "Mission";
                var started = Runner.Start(new MissionRequest(title, Text("brief") ?? "", chat.AssistantId, project.Id, project.Path, parent?.Provider ?? o.Provider)
                {
                    Model = parent?.Model ?? o.Model,
                    Team = team,
                    Origin = chat.MissionId is null ? MissionOrigin.Owner : MissionOrigin.Delegation,
                    Parent = parent?.Id ?? chat.MissionId,
                    Taint = PolicyContext(chat).TaintContext,
                    BudgetUsd = args["budget_usd"]?.JSONValueDouble is { } budget ? (decimal)budget : null,
                });
                var r = started.Record;
                return r.Mission.State == MissionState.Blocked
                    ? AssistantToolResult.Failed($"Not started: {r.Reason}")
                    : AssistantToolResult.Done($"Mission {r.Id:D} “{r.Mission.Title}” is planning; its lead starts when an agent slot is free.");
            }
            case "message_agent":
            {
                if (!Guid.TryParse(Text("agent"), out var agent)) return AssistantToolResult.Failed("Not run: the agent's id is not one nth knows.");
                if (!Owns(chat, agent)) return AssistantToolResult.Failed("Not run: that agent is not this assistant's.");
                var record = Runner.MissionOf(agent);
                if (record is not null && agent == record.LeadThread)
                    return Runner.Redirect(record.Id, Text("message") ?? "") is { } refused ? AssistantToolResult.Failed("Not sent: " + refused) : AssistantToolResult.Done("Sent to the lead.");
                Deliver(agent, Text("message") ?? "");
                return AssistantToolResult.Done("Sent.");
            }
            case "stop_agent":
            {
                if (!Guid.TryParse(Text("agent"), out var agent)) return AssistantToolResult.Failed("Not run: the agent's id is not one nth knows.");
                if (!Owns(chat, agent)) return AssistantToolResult.Failed("Not run: that agent is not this assistant's.");
                var record = Runner.MissionOf(agent);
                if (record is not null && agent == record.LeadThread)
                    return Runner.Stop(record.Id) is { } refused ? AssistantToolResult.Failed("Not stopped: " + refused) : AssistantToolResult.Done("The mission is stopping; its work is kept.");
                o.Agents.StopHead(agent);
                o.Jobs.Terminate(agent);
                return AssistantToolResult.Done("The head is stopping; its work is kept.");
            }
            case "request_helpers":
            {
                if (chat.MissionId is not { } mission) return AssistantToolResult.Failed("Not run: only a mission's chat can ask for helpers.");
                var started = Runner.RequestHelpers(mission, Text("task") ?? "", (int)(Whole("helpers") ?? 1), Text("reason") ?? "", out var refused);
                return started is null
                    ? AssistantToolResult.Done(refused ?? "Not started.")
                    : AssistantToolResult.Done($"Helpers started as mission {started.Record.Id:D}; their report comes back to this mission.");
            }
            case "ask_owner":
            {
                var options = List("options");
                var card = new DecisionItem(Guid.NewGuid(), DecisionKind.Question, DecisionWeight.Question, Text("question") ?? "")
                {
                    Inputs = Text("detail") is { } detail ? new ValueList<DecisionInput>([new("Detail", detail)]) : ValueList<DecisionInput>.Empty,
                    Why = "An agent asks you.",
                    Rule = "Questions are yours to answer",
                    AssistantId = chat.AssistantId,
                    MissionId = chat.MissionId,
                    ThreadId = chat.Thread,
                    Agent = o.Thread(chat.Thread)?.Title,
                    Options = new ValueList<string>(options),
                };
                var raised = Queue.Raise(card, (item, answer) => Deliver(chat.Thread,
                    $"The owner answered your question “{item.Title}”: {answer.Text}"));
                return AssistantToolResult.Done($"Waiting for the owner (card {raised.Id:D}). Carry on with what does not depend on the answer; it comes back to this chat.")
                    with { DecisionId = raised.Id.ToString("D") };
            }
            case "report":
            {
                var refused = Runner.Report(chat.Thread, Text("summary") ?? "", Text("status") ?? "done", List("artifacts"));
                return refused is null ? AssistantToolResult.Done("Reported.") : AssistantToolResult.Failed("Not reported: " + refused);
            }
            default:
                // Wired by the units that own them: duties and watches (AS-10), memory (AS-12), usage and plans (AS-13),
                // Asturs (AS-16), suggestions (AS-11), sessions (AS-2's reader), deliver and merge_results (AS-8).
                return AssistantToolResult.Failed($"Not run: {call.Tool.Name} is not part of this build of {AssistantNames.Assistant} yet.");
        }
    }

    /// <summary>A call the policy asks about (or would allow while auto-answering is off): a card with its exact inputs.</summary>
    public Task<string> Ask(AssistantToolCall call, CancellationToken cancellationToken) => Task.FromResult(Card(call, offered: false).Id.ToString("D"));

    /// <summary>A call refused as "needs an untainted request": a card the owner may raise himself (12.4).</summary>
    public void Offer(AssistantToolCall call) => Card(call, offered: true);

    private DecisionItem Card(AssistantToolCall call, bool offered)
    {
        var inputs = call.Arguments.JSONValueObject is { } fields
            ? fields.Select(f => new DecisionInput(f.Key, f.Value.JSONValueString ?? f.Value.ToString()))
            : [];
        var c = call.Classification;
        var card = new DecisionItem(Guid.NewGuid(), DecisionKind.ToolCall, DecisionRules.Weigh(c.Class), call.Tool.Title)
        {
            Inputs = new ValueList<DecisionInput>(inputs),
            Why = offered
                ? "This chat read untrusted content, so it cannot ask for this itself. Raise it yourself only if you want it."
                : call.Decision.Reason,
            Rule = call.Decision.Rule.ToString(),
            AssistantId = call.Chat.AssistantId,
            MissionId = call.Chat.MissionId,
            ThreadId = call.Chat.Thread,
            Agent = o.Thread(call.Chat.Thread)?.Title,
            Class = c.Class,
            GrantTarget = c.GrantTarget,
            RequestId = call.Id,
        };
        return Queue.Raise(card, (item, answer) =>
        {
            if (answer.Approves)
            {
                var result = RunNow(call);
                TryAudit(AssistantToolRunner.Entry(call, (result.IsError ? "failed" : "done") + " on the owner's answer"));
                Deliver(call.Chat.Thread, $"{AssistantNames.Server} {call.Tool.Name} (the owner allowed it): {result.Text}");
            }
            else
            {
                TryAudit(AssistantToolRunner.Entry(call, "denied by the owner"));
                Deliver(call.Chat.Thread, $"The owner did not allow {AssistantNames.Server} {call.Tool.Name}." + (answer.Text is { } why ? " " + why : ""));
            }
            if (answer.Kind is DecisionAnswerKind.ForThisMission or DecisionAnswerKind.Always) Granted(item, answer);
        });
    }

    public void Audit(AuditEntry entry) => o.Audit(entry);

    private void TryAudit(AuditEntry entry)
    {
        try { o.Audit(entry); }
        catch (Exception error) when (error is not OperationCanceledException) { }
    }

    // ---- helpers ----

    private MissionProject? ResolveProject(string? reference)
    {
        var projects = o.Projects();
        if (string.IsNullOrWhiteSpace(reference)) return projects.Count == 1 ? projects[0] : null;
        return Guid.TryParse(reference, out var id)
            ? projects.FirstOrDefault(p => p.Id == id)
            : projects.FirstOrDefault(p => string.Equals(p.Name, reference.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Whether the agent works for the asking chat's assistant (its missions' chats only).</summary>
    private bool Owns(AssistantChat chat, Guid agent) =>
        Runner.MissionOf(agent) is { } record && record.Mission.AssistantId == chat.AssistantId;

    /// <summary>A message for a chat: now when it is idle, else when its turn ends.</summary>
    private void Deliver(Guid thread, string text) => o.Agents.Post(() =>
    {
        if (o.Agents.TurnRunning(thread) || !o.Agents.Send(thread, text))
        {
            lock (gate)
            {
                if (!waitingMessages.TryGetValue(thread, out var list)) waitingMessages[thread] = list = [];
                list.Add(text);
            }
        }
    });

    private void Flush(Guid thread)
    {
        List<string>? list;
        lock (gate)
        {
            if (!waitingMessages.Remove(thread, out list)) return;
        }
        if (!o.Agents.Send(thread, string.Join("\n\n", list)))
            lock (gate) waitingMessages[thread] = list;
    }

    public void Dispose()
    {
        foreach (var thread in o.Jobs.Threads) o.Jobs.End(thread);
    }
}
