// PORT-ONLY: Dextra (DESIGN.md rev 5, 3.1-3.4, 4.1, 4.2, 12.3, 12.6, 12.6a, 12.6b, 13.2, 15.3, 15.4; unit AS-5;
// ED-NEW-dextra-as5-6 to -10). No Swift twin.
// Runs Dextra's missions: makes each one's workspace, starts its lead (alone or with a Hydra team) behind the agent gate,
// puts every provider approval and question of its chats in the owner's decision queue (auto-answering stays off until
// AS-7: the engine's Deny is applied, its Allow and Ask both go to a card), runs the reviewer when the work is done
// (Pass: Done and a merge card; Fail: a redirect, at most twice, then Failed), and stops, pauses, resumes and redirects
// a mission. Every change is journaled, so a crash resumes it. Confined to the app's main thread, like AppModel; work
// that resumes off it (the gate, git) comes back through IMissionAgents.Post.
using System.Globalization;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;

namespace Marrow.App.Assistant;

/// <summary>What a mission is asked to do (start_mission, a duty, a delegation, request_helpers).</summary>
public sealed record MissionRequest(string Title, string Brief, string AssistantId, Guid ProjectId, string ProjectPath, ProviderKind Provider)
{
    public string? Model { get; init; }

    public bool Team { get; init; }

    public MissionOrigin Origin { get; init; } = MissionOrigin.Owner;

    public Guid? Parent { get; init; }

    public TaintContext Taint { get; init; } = TaintContext.Clean;

    public decimal? BudgetUsd { get; init; }
}

/// <summary>A mission that was started, and the work that launches its lead (it waits behind the gate).</summary>
public sealed record MissionStart(MissionRecord Record, Task Launched);

/// <summary>What the policy engine says about one provider approval of a mission chat.</summary>
public sealed record ApprovalVerdict(Classification Classification, PolicyDecision Decision);

public sealed class MissionRunnerOptions
{
    public required IMissionAgents Agents { get; init; }

    public required DecisionQueue Queue { get; init; }

    public required AgentGate Gate { get; init; }

    public required AgentJobs Jobs { get; init; }

    public required IMissionWorkspaces Workspaces { get; init; }

    /// <summary>The policy engine's verdict on an approval (the controller classifies it and builds the context).</summary>
    public required Func<MissionRecord, Guid, ApprovalRequest, ApprovalVerdict> Policy { get; init; }

    public Func<MissionLimits> Limits { get; init; } = () => MissionLimits.Default;

    /// <summary>Journals a mission (the store's write-ahead journal). Throws when it cannot.</summary>
    public Action<MissionRecord>? Save { get; init; }

    /// <summary>Writes one activity log line.</summary>
    public Action<AuditEntry>? Audit { get; init; }

    /// <summary>A grant the owner made from a card (For this mission, Always...).</summary>
    public Action<DecisionItem, DecisionAnswer>? Granted { get; init; }

    public TimeProvider Time { get; init; } = TimeProvider.System;
}

public sealed class MissionRunner
{
    /// <summary>How often a stopping mission looks whether its agents have ended.</summary>
    public static readonly TimeSpan StopCheck = TimeSpan.FromMilliseconds(100);

    public const string Withdrawn = "Answered Deny by Stop: the turn was cut, and it is asked again when that turn runs again.";

    public const string WithdrawnQuestion = "Withdrawn by Stop: the turn that asked was cut, and it asks again when that turn runs again.";

    private readonly MissionRunnerOptions o;
    private readonly object gate = new();
    private readonly Dictionary<Guid, MissionRecord> missions = [];
    private readonly Dictionary<Guid, Guid> threadMission = [];
    private readonly Dictionary<Guid, Live> live = [];
    private ITimer? stopTimer;

    /// <summary>What the runner knows about a mission that is not journaled (it is rebuilt after a crash).</summary>
    private sealed class Live
    {
        public MissionState? NeedsYouFrom;
        public bool LeadDone;
        public bool ReviewDone;
        public readonly Dictionary<Guid, string> AgentNames = [];
        public CancellationTokenSource Launch = new();
        /// <summary>It came back from the journal: no turn of it runs, so Resume runs its cut turn again.</summary>
        public bool Restored;
    }

    public MissionRunner(MissionRunnerOptions options)
    {
        o = options;
        o.Queue.Changed += QueueChanged;
    }

    /// <summary>Raised after a mission changed (on the main thread).</summary>
    public event Action<MissionRecord>? Changed;

    /// <summary>A journal or log write failed; the mission went on and the board shows the line.</summary>
    public event Action<string>? Faulted;

    public IReadOnlyList<MissionRecord> Missions
    {
        get { lock (gate) return [.. missions.Values.OrderBy(m => m.Created)]; }
    }

    public MissionRecord? Find(Guid mission)
    {
        lock (gate) return missions.GetValueOrDefault(mission);
    }

    /// <summary>The mission a chat works for, when it is one of the runner's.</summary>
    public MissionRecord? MissionOf(Guid thread)
    {
        lock (gate) return threadMission.TryGetValue(thread, out var id) ? missions.GetValueOrDefault(id) : null;
    }

    /// <summary>The live local agents: the mission threads that hold a gate slot.</summary>
    public IReadOnlyList<Guid> AgentThreads(Guid mission)
    {
        lock (gate) return missions.TryGetValue(mission, out var r) ? Threads(r) : [];
    }

    // ---- start ----

    /// <summary>
    /// Starts a mission: refused at once for a provider nth cannot seal or a depth past the ceiling; else it is journaled
    /// Planning, its workspace made, and its lead started behind the agent gate (MissionStart.Launched).
    /// </summary>
    public MissionStart Start(MissionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var now = o.Time.GetUtcNow();
        MissionRecord? parent = request.Parent is { } p ? Find(p) : null;
        var level = parent is null ? MissionLimits.TopLevel : MissionGate.ChildLevel(parent.Level);
        var mission = new Mission(Guid.NewGuid(), request.Title.Trim(), request.AssistantId, request.Origin, MissionState.Planning)
        {
            ParentMissionId = request.Parent,
            TaintContext = parent is null ? request.Taint : new TaintContext(parent.Mission.TaintContext.ChildContext().Taint.Union(request.Taint.Taint)),
        };
        var record = new MissionRecord(mission)
        {
            Brief = request.Brief,
            ProjectId = request.ProjectId,
            ProjectPath = request.ProjectPath,
            Provider = request.Provider,
            Model = request.Model,
            Team = request.Team,
            Level = level,
            BudgetUsd = request.BudgetUsd,
            Created = now,
        };
        var refused = SealedProviderConfig.Refusal(request.Provider)
            ?? (MissionGate.BeyondCeiling(level) ? $"Helpers here would run {level} levels deep; nothing runs deeper than {MissionLimits.DepthCeiling}." : null);
        if (refused is not null) record = record with { Mission = mission with { State = MissionState.Blocked }, Reason = refused };
        lock (gate)
        {
            missions[mission.Id] = record;
            live[mission.Id] = new Live();
        }
        if (parent is not null) Update(parent.Id, r => r with { Mission = r.Mission with { ChildMissionIds = r.Mission.ChildMissionIds.Appending(mission.Id) } });
        Journal(record);
        Log(record, null, "start_mission", ActionClass.Agents, refused is null ? "started" : "blocked: " + refused);
        Raise(record);
        if (refused is not null) return new MissionStart(record, Task.CompletedTask);
        return new MissionStart(record, Launch(mission.Id));
    }

    private async Task Launch(Guid id)
    {
        if (Find(id) is not { } record) return;
        var token = LiveOf(id).Launch.Token;
        if (record.Workspace is null)
        {
            MissionWorkspace workspace;
            try { workspace = await o.Workspaces.Create(id, record.Mission.Title, record.ProjectPath).ConfigureAwait(false); }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                OnMain(() => Block(id, error is MissionWorkspaceError ? error.Message : "Its workspace could not be made: " + error.Message));
                return;
            }
            OnMain(() => Update(id, r => r with
            {
                Workspace = workspace,
                Mission = r.Mission with { WorktreePath = workspace.WorkingFolder, Branch = workspace.Branch },
            }));
        }
        var lead = Guid.NewGuid();
        await Admit(id, lead, record.Provider, token).ConfigureAwait(false);
        if (token.IsCancellationRequested) return;
        OnMain(() => StartLead(id, lead));
    }

    /// <summary>Waits at the gate for an agent of the mission; the mission shows why while it waits.</summary>
    private async Task Admit(Guid mission, Guid agent, ProviderKind provider, CancellationToken token)
    {
        var entering = o.Gate.Enter(agent, Place(provider), token);
        if (!entering.IsCompleted)
        {
            var line = o.Gate.Waiting(agent);
            OnMain(() => Update(mission, r => r with { Waiting = line ?? "Waiting for a free agent slot." }));
        }
        try { await entering.ConfigureAwait(false); }
        catch (OperationCanceledException) { return; }
        OnMain(() => Update(mission, r => r.Waiting is null ? r : r with { Waiting = null }));
    }

    private void StartLead(Guid id, Guid slot)
    {
        if (Find(id) is not { } record || record.Mission.State != MissionState.Planning)
        {
            o.Gate.Leave(slot);
            return;
        }
        var folder = record.Workspace?.WorkingFolder ?? record.ProjectPath;
        Guid thread;
        try
        {
            thread = o.Agents.Start(new AgentLaunch(id, record.Mission.AssistantId, record.Mission.Title, LeadBrief(record, folder),
                record.ProjectId, folder, record.Provider)
            {
                Model = record.Model,
                Team = record.Team,
                Branch = record.Workspace?.Branch,
            });
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            o.Gate.Leave(slot);
            Block(id, "Its lead chat could not start: " + error.Message);
            return;
        }
        Rekey(slot, thread);
        lock (gate)
        {
            threadMission[thread] = id;
            LiveOf(id).AgentNames[thread] = "Lead";
        }
        Update(id, r => Moved(r with { LeadThread = thread, Mission = r.Mission with { ThreadIds = r.Mission.ThreadIds.Appending(thread) } }, MissionState.Working));
        Log(Find(id)!, thread, "spawn_chat", ActionClass.Agents, "lead started");
    }

    /// <summary>The gate slot was taken under a placeholder id before the chat existed: it moves to the chat.</summary>
    private void Rekey(Guid slot, Guid thread)
    {
        if (slot == thread || !o.Gate.Holds(slot)) return;
        o.Gate.Leave(slot);
        _ = o.Gate.Enter(thread, AgentPlace.Local);
    }

    private static string LeadBrief(MissionRecord record, string folder)
    {
        var lines = new List<string>
        {
            $"You lead the mission \"{record.Mission.Title}\" for the owner. Work in {folder}; it is the mission's own copy, so the owner's files stay as they are.",
        };
        if (record.Team) lines.Add("You run a Hydra team: split the work among your heads, then merge their reports.");
        lines.Add("Risky steps (sending, paying, deleting outside git, installing) always wait for the owner; carry on with what does not depend on them.");
        lines.Add("When the work is done, end with a short report of what you delivered and where it is. A reviewer checks it before it counts as done.");
        lines.Add("");
        lines.Add("The brief:");
        lines.Add(record.Brief);
        return string.Join("\n", lines);
    }

    // ---- heads (Hydra seam 3) ----

    /// <summary>
    /// A Hydra head was added under one of the runner's leads: it is the mission's agent too. A native head (the lead
    /// provider's own subagent) runs inside the lead's process, so in the lead's job and slot, and works at once.
    /// </summary>
    public void HeadAdded(Guid lead, Guid head, string name, bool native = false)
    {
        if (MissionOf(lead) is not { } record) return;
        lock (gate)
        {
            threadMission[head] = record.Id;
            LiveOf(record.Id).AgentNames[head] = name;
        }
        Update(record.Id, r => r with
        {
            Mission = r.Mission with
            {
                ThreadIds = r.Mission.ThreadIds.Contains(head) ? r.Mission.ThreadIds : r.Mission.ThreadIds.Appending(head),
                Heads = r.Mission.Heads.Any(h => h.Id == HeadId(head))
                    ? r.Mission.Heads
                    : r.Mission.Heads.Appending(new MissionHead(HeadId(head), name, native ? HeadState.Working : HeadState.Queued)),
            },
        });
    }

    /// <summary>A head waits at the gate before its chat starts (the same 16-agent cap and memory floor as every agent).</summary>
    public Task AdmitHead(Guid head, ProviderKind provider, CancellationToken token = default)
    {
        if (MissionOf(head) is not { } record) return Task.CompletedTask;
        return Admit(record.Id, head, provider, token).ContinueWith(_ => OnMain(() => HeadStarted(head)), TaskScheduler.Default);
    }

    private void HeadStarted(Guid head)
    {
        if (MissionOf(head) is not { } record) return;
        var move = MissionMachine.StartHead(record.Mission, HeadId(head));
        if (move.Done) Update(record.Id, r => r with { Mission = move.Mission });
    }

    /// <summary>A head finished its last turn (HydraHeadTurnFinished): its slot is free and it counts as done.</summary>
    public void HeadFinished(Guid head, TurnStatus status)
    {
        if (MissionOf(head) is not { } record) return;
        o.Gate.Leave(head);
        if (record.Mission.State == MissionState.Stopping)
        {
            CheckStopping();
            return;
        }
        var id = HeadId(head);
        if (record.Mission.Heads.FirstOrDefault(h => h.Id == id) is not { } h) return;
        if (h.State == HeadState.Pausing)
        {
            Update(record.Id, r => r with { Mission = MissionMachine.TurnBoundary(r.Mission, id).Mission });
            return;
        }
        var move = h.State == HeadState.Working ? MissionMachine.FinishHead(record.Mission, id) : null;
        if (move is { Done: true }) Update(record.Id, r => r with { Mission = move.Mission });
        o.Jobs.End(head);
    }

    private static string HeadId(Guid thread) => thread.ToString("D");

    /// <summary>Heads Hydra reports finished count as done in the mission too (native heads finish inside the lead).</summary>
    private void SyncHeads(Guid id, Guid lead)
    {
        var finished = o.Agents.Heads(lead).Where(h => h.Finished).Select(h => HeadId(h.Thread)).ToHashSet(StringComparer.Ordinal);
        if (finished.Count == 0) return;
        Update(id, r =>
        {
            var m = r.Mission;
            foreach (var head in m.Heads.Where(h => h.State == HeadState.Working && finished.Contains(h.Id)).ToList())
                m = MissionMachine.FinishHead(m, head.Id).Mission;
            return ReferenceEquals(m, r.Mission) ? r : r with { Mission = m };
        });
    }

    // ---- approvals and questions ----

    /// <summary>
    /// A provider approval in one of the runner's chats (12.3 point 2). The engine's Deny is answered no at once; every
    /// other verdict goes to the queue, Allow included, until AS-7 turns auto-answering on. Returns whether the runner took it.
    /// </summary>
    public bool OnApproval(Guid thread, ApprovalRequest request)
    {
        if (MissionOf(thread) is not { } record) return false;
        if (record.Mission.State is MissionState.Stopping or MissionState.Stopped)
        {
            // Stop is cutting this turn: whatever it still asks for is answered no.
            o.Agents.Resolve(thread, request, approve: false);
            Log(record, thread, "approval", ActionClass.Unknown, "refused: stopping", request.Title);
            return true;
        }
        ApprovalVerdict verdict;
        try { verdict = o.Policy(record, thread, request); }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            verdict = new ApprovalVerdict(new Classification(ActionClass.Unknown, "nth could not read its rules"),
                new PolicyDecision(Verdict.Ask, PolicyRule.Unknown, "Asks: nth could not check its rules for this"));
        }
        var c = verdict.Classification;
        var decision = verdict.Decision;
        if (thread == record.ReviewerThread && !Reviewer.Reads(c.Class))
            decision = new PolicyDecision(Verdict.Deny, PolicyRule.OwnerNever, "The reviewer only reads");
        if (decision.Verdict == Verdict.Deny)
        {
            o.Agents.Resolve(thread, request, approve: false);
            Log(record, thread, "approval", c.Class, "refused: " + decision.Rule, request.Title);
            return true;
        }
        var why = decision.Verdict == Verdict.Allow
            ? "nth does not answer for you until its injection checks pass (it would have allowed this)."
            : decision.Reason;
        var rule = decision.Verdict == Verdict.Allow ? RuleLine(PolicyRule.AutoAnswerOff) : RuleLine(decision.Rule);
        var inputs = new List<DecisionInput> { new(request.RequestKind == ApprovalRequest.Kind.Command ? "Command" : "Request", request.Title) };
        if (!string.IsNullOrWhiteSpace(request.Detail)) inputs.Add(new("Detail", request.Detail!));
        if (!string.IsNullOrWhiteSpace(request.Reason)) inputs.Add(new("The agent's reason", request.Reason!));
        var card = new DecisionItem(Guid.NewGuid(), DecisionKind.Approval, DecisionRules.Weigh(c.Class), request.Headline)
        {
            Inputs = new ValueList<DecisionInput>(inputs),
            Why = why,
            Rule = rule,
            AssistantId = record.Mission.AssistantId,
            MissionId = record.Id,
            ThreadId = thread,
            Agent = AgentName(record.Id, thread),
            Class = c.Class,
            GrantTarget = c.GrantTarget,
            RequestId = request.Id,
        };
        Log(record, thread, "approval", c.Class, "asked: " + (decision.Verdict == Verdict.Allow ? PolicyRule.AutoAnswerOff : decision.Rule), request.Title);
        lock (gate) pendingRequests[(thread, request.Id)] = request;
        var raised = o.Queue.Raise(card, (item, answer) => OnMain(() => Approved(item, answer, request)));
        // Raised while the mission pauses: Pause holds it as it holds the cards that were open (irreversible ones only).
        if (Find(record.Id)?.Mission.State is MissionState.Pausing or MissionState.Paused) o.Queue.Hold(c => c.Id == raised.Id, DecisionHold.Pause);
        NeedsYou(record.Id);
        return true;
    }

    private void Approved(DecisionItem card, DecisionAnswer answer, ApprovalRequest request)
    {
        if (card.ThreadId is not { } thread || card.MissionId is not { } id || Find(id) is not { } record) return;
        lock (gate) pendingRequests.Remove((thread, request.Id));
        o.Agents.Resolve(thread, request, answer.Approves);
        if (answer.Kind is DecisionAnswerKind.ForThisMission or DecisionAnswerKind.Always) o.Granted?.Invoke(card, answer);
        Log(record, thread, "approval", card.Class ?? ActionClass.Unknown, (answer.Approves ? "allowed by the owner: " : "denied by the owner: ") + answer.Kind, request.Title);
    }

    /// <summary>A question from one of the runner's chats: always a card (taste and design picks are the owner's).</summary>
    public bool OnQuestion(Guid thread, QuestionRequest request)
    {
        if (MissionOf(thread) is not { } record) return false;
        var secret = request.Questions.Any(q => q.IsSecret);
        var single = request.Questions.Count == 1 ? request.Questions[0] : null;
        var card = new DecisionItem(Guid.NewGuid(), DecisionKind.Question, DecisionWeight.Question,
            single?.Prompt ?? string.Join(" ", request.Questions.Select(q => q.Prompt)))
        {
            Inputs = new ValueList<DecisionInput>(request.Questions.Select(q => new DecisionInput(q.Header.Length > 0 ? q.Header : "Question", q.Prompt))),
            Why = "An agent of the mission asks you.",
            Rule = "Questions are yours to answer",
            AssistantId = record.Mission.AssistantId,
            MissionId = record.Id,
            ThreadId = thread,
            Agent = AgentName(record.Id, thread),
            RequestId = request.Id,
            Options = secret || single is null ? ValueList<string>.Empty : new ValueList<string>(single.Choices.Select(ch => ch.Label)),
            Note = secret ? "It asks for a secret, so answer it in the chat; nth never keeps a secret on a card." : null,
        };
        o.Queue.Raise(card, (item, answer) => OnMain(() => Answered(item, answer, request)),
            secret ? (_, _) => "It asks for a secret: answer it in the chat." : null);
        NeedsYou(record.Id);
        return true;
    }

    private void Answered(DecisionItem card, DecisionAnswer answer, QuestionRequest request)
    {
        if (card.ThreadId is not { } thread) return;
        var text = answer.Text ?? "";
        var answers = request.Questions.ToDictionary(q => q.Id, _ => new ValueList<string>([text]));
        o.Agents.Answer(thread, request, new ValueMap<string, ValueList<string>>(answers));
    }

    /// <summary>
    /// The request was settled without the card (4.2 "Expiry"): the owner answered it in the chat, or the provider resolved
    /// or cancelled it itself. Its card says so and takes no answer.
    /// </summary>
    public void OnRequestResolved(Guid thread, string requestId)
    {
        if (MissionOf(thread) is null) return;
        lock (gate) pendingRequests.Remove((thread, requestId));
        o.Queue.WithdrawRequest(thread, requestId, RequestSettled);
    }

    public const string RequestSettled = "Answered in the chat, or cancelled by the agent.";

    private void NeedsYou(Guid id)
    {
        if (Find(id) is not { } record) return;
        var state = record.Mission.State;
        if (state is not (MissionState.Working or MissionState.Reviewing or MissionState.Planning)) return;
        LiveOf(id).NeedsYouFrom = state;
        Update(id, r => Moved(r, MissionState.NeedsYou));
    }

    private void QueueChanged() => OnMain(() =>
    {
        foreach (var record in Missions.Where(r => r.Mission.State == MissionState.NeedsYou))
        {
            if (o.Queue.Of(record.Id).Any(c => c.State == DecisionState.Open)) continue;
            if (o.Queue.Of(record.Id).Any(c => c.State == DecisionState.Held)) continue;
            var back = LiveOf(record.Id).NeedsYouFrom ?? MissionState.Working;
            LiveOf(record.Id).NeedsYouFrom = null;
            Update(record.Id, r => Moved(r, back));
            Advance(record.Id);
        }
    });

    // ---- turns ----

    /// <summary>A turn of one of the runner's chats ended (AppModel.TurnFinished).</summary>
    public void OnTurnFinished(Guid thread, TurnStatus status, bool continues)
    {
        if (MissionOf(thread) is not { } record || continues) return;
        var id = record.Id;
        switch (record.Mission.State)
        {
            case MissionState.Stopping:
                CheckStopping();
                return;
            case MissionState.Pausing:
                if (thread == record.LeadThread) LiveOf(id).LeadDone = status == TurnStatus.Completed;
                if (thread == record.ReviewerThread) LiveOf(id).ReviewDone = status == TurnStatus.Completed;
                Update(id, r => r with { Mission = MissionMachine.TurnBoundary(r.Mission, r.Mission.Heads.Any(h => h.Id == HeadId(thread)) ? HeadId(thread) : null).Mission });
                return;
        }
        if (thread == record.LeadThread)
        {
            if (status == TurnStatus.Failed)
            {
                Block(id, "Its lead chat ended with an error.");
                return;
            }
            LiveOf(id).LeadDone = status == TurnStatus.Completed;
        }
        else if (thread == record.ReviewerThread)
        {
            LiveOf(id).ReviewDone = status == TurnStatus.Completed;
            if (status == TurnStatus.Failed) LiveOf(id).ReviewDone = true; // no verdict: it fails (fail closed)
        }
        Advance(id);
    }

    /// <summary>Moves a mission on when nothing holds it: a waiting redirect, the review, the verdict.</summary>
    private void Advance(Guid id)
    {
        if (Find(id) is not { } record) return;
        var state = record.Mission.State;
        var l = LiveOf(id);
        if (record.LeadThread is { } leadThread) SyncHeads(id, leadThread);
        if (state == MissionState.Working && record.LeadThread is { } lead && !o.Agents.TurnRunning(lead))
        {
            if (record.PendingRedirect is { } redirect && o.Agents.Send(lead, redirect))
            {
                l.LeadDone = false;
                Update(id, r => r with { PendingRedirect = null });
                return;
            }
            if (!l.LeadDone || o.Queue.Of(id).Count > 0) return;
            if (o.Agents.Heads(lead).Any(h => !h.Finished)) return;
            l.LeadDone = false;
            Update(id, r => r with { Report = o.Agents.FinalMessage(lead) ?? r.Report });
            _ = StartReview(id);
        }
        else if (state == MissionState.Reviewing && l.ReviewDone && record.ReviewerThread is { } reviewer && !o.Agents.TurnRunning(reviewer))
        {
            l.ReviewDone = false;
            ApplyVerdict(id, ReviewVerdict.Parse(o.Agents.FinalMessage(reviewer)));
        }
    }

    // ---- review ----

    private async Task StartReview(Guid id)
    {
        if (Find(id) is not { } record) return;
        Update(id, r => Moved(r, MissionState.Reviewing));
        MergeReview? changes = null;
        if (record.Workspace is { } workspace)
        {
            try { changes = await o.Workspaces.Review(workspace).ConfigureAwait(false); }
            catch (Exception error) when (error is not OperationCanceledException) { changes = null; }
        }
        var slot = Guid.NewGuid();
        var token = LiveOf(id).Launch.Token;
        await Admit(id, slot, record.Provider, token).ConfigureAwait(false);
        if (token.IsCancellationRequested) return;
        OnMain(() =>
        {
            if (Find(id) is not { } now || now.Mission.State != MissionState.Reviewing)
            {
                o.Gate.Leave(slot);
                return;
            }
            var model = Reviewer.PickModel(now.Model, o.Agents.Models(now.Provider));
            Guid thread;
            try
            {
                thread = o.Agents.Start(new AgentLaunch(id, now.Mission.AssistantId, "Review: " + now.Mission.Title, Reviewer.Brief(now, changes),
                    now.ProjectId, now.Workspace?.WorkingFolder ?? now.ProjectPath, now.Provider)
                {
                    Model = model,
                    Reviewer = true,
                    Branch = now.Workspace?.Branch,
                });
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                o.Gate.Leave(slot);
                Block(id, "Its reviewer could not start: " + error.Message);
                return;
            }
            Rekey(slot, thread);
            lock (gate)
            {
                threadMission[thread] = id;
                LiveOf(id).AgentNames[thread] = "Reviewer";
            }
            Update(id, r => r with
            {
                ReviewerThread = thread,
                Model = r.Model,
                Mission = r.Mission with { ThreadIds = r.Mission.ThreadIds.Appending(thread) },
                Reviews = r.Reviews,
            });
            reviewerModels[thread] = model ?? now.Provider.DisplayName;
            Log(Find(id)!, thread, "spawn_chat", ActionClass.Agents, "reviewer started");
        });
    }

    private readonly Dictionary<Guid, string> reviewerModels = [];

    private void ApplyVerdict(Guid id, ReviewVerdict verdict)
    {
        if (Find(id) is not { } record || record.ReviewerThread is not { } reviewer) return;
        EndAgent(reviewer);
        var review = new MissionReview(verdict.Passed, verdict.Reasons, reviewerModels.GetValueOrDefault(reviewer) ?? record.Provider.DisplayName, o.Time.GetUtcNow());
        Log(record, reviewer, "review", ActionClass.ReadLocal, verdict.Passed ? "passed" : "failed");
        if (verdict.Passed)
        {
            Update(id, r => Moved(r with { Reviews = r.Reviews.Appending(review), ReviewerThread = null, Ended = o.Time.GetUtcNow() }, MissionState.Done));
            Finish(id);
            _ = OfferMerge(id, null);
            return;
        }
        var fails = record.Mission.ReviewRounds;
        if (ReviewVerdict.FailsMission(fails))
        {
            Update(id, r => Moved(r with
            {
                Reviews = r.Reviews.Appending(review),
                ReviewerThread = null,
                Reason = "The reviewer failed it " + (fails + 1).ToString(CultureInfo.InvariantCulture) + " times. Last: " + verdict.Reasons,
                Ended = o.Time.GetUtcNow(),
            }, MissionState.Failed));
            Finish(id);
            return;
        }
        Update(id, r => Moved(r with
        {
            Reviews = r.Reviews.Appending(review),
            ReviewerThread = null,
            Mission = r.Mission with { ReviewRounds = fails + 1 },
        }, MissionState.Working));
        if (record.LeadThread is { } lead)
        {
            var text = Reviewer.Redirect(verdict, fails + 1);
            if (!o.Agents.Send(lead, text)) Update(id, r => r with { PendingRedirect = text });
        }
    }

    /// <summary>The mission ended (Done or Failed): its agents stop and their jobs close (kill-on-close ends what is left).</summary>
    private void Finish(Guid id)
    {
        if (Find(id) is not { } record) return;
        foreach (var thread in Threads(record)) EndAgent(thread);
        lock (gate) LiveOf(id).Launch.Cancel();
        Update(id, r => r with { Mission = r.Mission with { TaintContext = TaintContext.MissionEnded() } });
    }

    private void EndAgent(Guid thread)
    {
        o.Agents.StopSession(thread);
        o.Jobs.End(thread);
        o.Gate.Leave(thread);
    }

    // ---- merge (12.6a) ----

    private async Task OfferMerge(Guid id, string? note)
    {
        if (Find(id) is not { Workspace: { } workspace } record) return;
        MergeReview changes;
        try { changes = await o.Workspaces.Review(workspace).ConfigureAwait(false); }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            OnMain(() => Update(id, r => r with { Reason = "Its changes could not be read: " + error.Message }));
            return;
        }
        OnMain(() =>
        {
            if (changes.Changes.Count == 0)
            {
                Update(id, r => r with { MergePending = false, Mission = r.Mission with { ChangedFiles = 0 } });
                return;
            }
            Update(id, r => r with { MergePending = true, Mission = r.Mission with { ChangedFiles = changes.Changes.Count } });
            var lines = changes.Insertions + changes.Deletions;
            var title = workspace.Kind == MissionWorkspaceKind.Worktree
                ? $"Merge “{record.Mission.Title}” ({lines.ToString(CultureInfo.InvariantCulture)} lines) into {workspace.BaseBranch ?? "your branch"}?"
                : $"Copy the changes of “{record.Mission.Title}” back to {workspace.Source}?";
            var inputs = new List<DecisionInput> { new("Changes", changes.Summary) };
            if (workspace.Branch is { } branch) inputs.Add(new("Branch", branch));
            if (workspace.BaseBranch is { } baseBranch) inputs.Add(new("Into", baseBranch));
            inputs.AddRange(changes.Changes.Take(50).Select(c => new DecisionInput(c.Kind.ToString(), c.Path)));
            var card = new DecisionItem(Guid.NewGuid(), DecisionKind.Merge, DecisionWeight.Irreversible, title)
            {
                Inputs = new ValueList<DecisionInput>(inputs),
                Why = changes.BaseMoved ? "The mission passed review. Your branch moved since it started: Rebase first replays it on top." : "The mission passed review.",
                Rule = "Merging is always yours",
                AssistantId = record.Mission.AssistantId,
                MissionId = id,
                Agent = "Reviewer",
                Note = note,
            };
            o.Queue.Raise(card, (item, answer) => _ = MergeAnswered(id, item, answer));
        });
    }

    private async Task MergeAnswered(Guid id, DecisionItem card, DecisionAnswer answer)
    {
        if (Find(id) is not { Workspace: { } workspace } record) return;
        switch (answer.Kind)
        {
            case DecisionAnswerKind.Merge:
            {
                var result = await o.Workspaces.Merge(workspace).ConfigureAwait(false);
                OnMain(() =>
                {
                    Log(record, null, "merge", ActionClass.Work, result.Outcome.ToString());
                    if (result.Outcome is MergeOutcome.Merged or MergeOutcome.NothingToMerge)
                        Update(id, r => r with { MergePending = false });
                    else
                        _ = OfferMerge(id, result.Outcome == MergeOutcome.Conflict
                            ? "Git found conflicts in " + string.Join(", ", result.Conflicts) + ", so nothing was merged. Rebase first, or Discard."
                            : result.Refused);
                });
                return;
            }
            case DecisionAnswerKind.RebaseFirst:
            {
                var result = await o.Workspaces.RebaseFirst(workspace).ConfigureAwait(false);
                OnMain(() =>
                {
                    Log(record, null, "rebase", ActionClass.Work, result.Outcome.ToString());
                    _ = OfferMerge(id, result.Outcome == MergeOutcome.Conflict
                        ? "Rebasing found conflicts in " + string.Join(", ", result.Conflicts) + "; the branch is as it was."
                        : result.Refused ?? "Rebased onto your branch.");
                });
                return;
            }
            case DecisionAnswerKind.Discard:
                await o.Workspaces.Discard(workspace).ConfigureAwait(false);
                OnMain(() =>
                {
                    Log(record, null, "discard", ActionClass.Work, "discarded");
                    Update(id, r => r with { MergePending = false, Workspace = null, Mission = r.Mission with { ChangedFiles = 0, WorktreePath = null } });
                });
                return;
        }
    }

    /// <summary>Workspaces of missions that ended more than 7 days ago go, unless the owner keeps them (12.6a).</summary>
    public async Task CleanUpWorkspaces()
    {
        var now = o.Time.GetUtcNow();
        foreach (var record in Missions.Where(r => r.Workspace is not null && !r.MergePending && r.Ended is { } ended
                     && MissionWorkspaces.Due(ended, r.KeepWorkspace, now)))
        {
            try { await o.Workspaces.Discard(record.Workspace!).ConfigureAwait(false); }
            catch (Exception error) when (error is not OperationCanceledException) { continue; }
            OnMain(() => Update(record.Id, r => r with { Workspace = null }));
        }
    }

    // ---- the owner's controls ----

    /// <summary>
    /// Stop (12.6): the cancel flags and the queued steps first; pending provider approvals answered Deny and their cards
    /// withdrawn; every other card of the mission held; each agent's turn interrupted, its heads stopped and its job
    /// terminated. The mission stays Stopping until every agent has no live process and no turn, then Stopped.
    /// </summary>
    public string? Stop(Guid id, StopCause cause = StopCause.Owner)
    {
        if (Find(id) is not { } record) return "There is no such mission.";
        var threads = Threads(record);
        var open = threads.Any(t => o.Agents.TurnRunning(t) || (o.Jobs.Existing(t)?.LiveProcesses ?? 0) > 0);
        var move = MissionMachine.Stop(record.Mission, open, cause);
        if (!move.Done) return move.Refused;
        lock (gate) LiveOf(id).Launch.Cancel();
        var stopping = move.Mission;
        // Its approval cards are answered Deny below, so on Resume it goes back to what it was doing, not to Needs you.
        if (stopping.ResumeTo == MissionState.NeedsYou) stopping = stopping with { ResumeTo = NeedsYouFrom(record) };
        Update(id, r => r with { Mission = stopping, Waiting = null });
        // The provider's own requests die with the cut turn: approvals are answered Deny, questions withdrawn.
        foreach (var card in o.Queue.Of(id).Where(c => c.Kind is DecisionKind.Approval or DecisionKind.Question && c.RequestId is not null))
        {
            if (card.Kind == DecisionKind.Approval && card.ThreadId is { } t) DenyPending(t, card.RequestId!);
            o.Queue.Withdraw(card.Id, card.Kind == DecisionKind.Approval ? Withdrawn : WithdrawnQuestion);
        }
        o.Queue.Hold(c => c.MissionId == id, DecisionHold.Stop);
        foreach (var thread in threads) o.Agents.Interrupt(thread);
        if (record.LeadThread is { } lead) o.Agents.StopHeads(lead);
        foreach (var thread in threads)
        {
            o.Jobs.Terminate(thread);
            o.Gate.Leave(thread);
        }
        Log(record, null, "stop", ActionClass.Agents, "stop sent");
        CheckStopping();
        if (Find(id)?.Mission.State == MissionState.Stopping) stopTimer ??= o.Time.CreateTimer(_ => OnMain(CheckStopping), null, StopCheck, StopCheck);
        return null;
    }

    private readonly Dictionary<(Guid, string), ApprovalRequest> pendingRequests = [];

    private void DenyPending(Guid thread, string requestId)
    {
        ApprovalRequest? request;
        lock (gate) pendingRequests.Remove((thread, requestId), out request);
        if (request is not null) o.Agents.Resolve(thread, request, approve: false);
    }

    /// <summary>Confirms each stopping agent once it has no live process and no turn (12.6 "Afterwards").</summary>
    public void CheckStopping()
    {
        foreach (var record in Missions.Where(r => r.Mission.State == MissionState.Stopping))
        {
            bool Settled(Guid t) => !o.Agents.TurnRunning(t) && o.Jobs.Settled(t);
            var leadSettled = (record.LeadThread is not { } lead || Settled(lead)) && (record.ReviewerThread is not { } rv || Settled(rv));
            var m = record.Mission;
            if (m.Heads.Count == 0)
            {
                if (leadSettled) m = MissionMachine.ConfirmStopped(m).Mission;
            }
            else
            {
                foreach (var head in m.Heads.Where(h => h.State == HeadState.Stopping).ToList())
                {
                    if (!Guid.TryParse(head.Id, out var thread) || !Settled(thread)) continue;
                    var last = m.Heads.Count(h => h.State == HeadState.Stopping) == 1;
                    if (last && !leadSettled) continue;
                    m = MissionMachine.ConfirmStopped(m, head.Id).Mission;
                }
                if (m.State == MissionState.Stopping && m.Heads.All(h => h.State != HeadState.Stopping) && leadSettled)
                    m = MissionMachine.ConfirmStopped(m).Mission;
            }
            if (!ReferenceEquals(m, record.Mission)) Update(record.Id, r => r with { Mission = m });
        }
        if (!Missions.Any(r => r.Mission.State == MissionState.Stopping))
        {
            stopTimer?.Dispose();
            stopTimer = null;
        }
    }

    /// <summary>Pause (12.6b): no interrupt; the turn in flight finishes first. Pause holds only irreversible and merge cards.</summary>
    public string? Pause(Guid id, PauseCause cause = PauseCause.Owner)
    {
        if (Find(id) is not { } record) return "There is no such mission.";
        var running = Threads(record).Any(o.Agents.TurnRunning);
        var move = MissionMachine.Pause(record.Mission, running, cause);
        if (!move.Done) return move.Refused;
        Update(id, r => r with { Mission = move.Mission });
        o.Queue.Hold(c => c.MissionId == id, DecisionHold.Pause);
        Log(record, null, "pause", ActionClass.Agents, move.Mission.State.ToString());
        return null;
    }

    /// <summary>
    /// Resume (12.6b): held cards come back bound to their inputs; a mission that was stopped, or interrupted by nth's
    /// end, runs its cut turn again; one that never started its lead launches it.
    /// </summary>
    public string? Resume(Guid id)
    {
        if (Find(id) is not { } record) return "There is no such mission.";
        var heldIrreversible = o.Queue.Of(id).Any(c => c.State == DecisionState.Held && DecisionRules.HeldByPause(c));
        var restored = LiveOf(id).Restored;
        var wasStopped = record.Mission.State == MissionState.Stopped || record.Interrupted || restored;
        var move = MissionMachine.Resume(record.Mission, heldIrreversible);
        if (!move.Done) return move.Refused;
        var mission = move.Mission;
        // It waited on cards it no longer has (lost with nth's end): it goes back to what it was doing and asks again.
        if (mission.State == MissionState.NeedsYou && o.Queue.Of(id).Count == 0) mission = mission with { State = NeedsYouFrom(record) };
        lock (gate)
        {
            var l = LiveOf(id);
            if (l.Launch.IsCancellationRequested) l.Launch = new CancellationTokenSource();
            l.Restored = false;
        }
        Update(id, r => r with { Mission = mission, Interrupted = false });
        o.Queue.Release(c => c.MissionId == id);
        Log(record, null, "resume", ActionClass.Agents, mission.State.ToString());
        var state = mission.State;
        if (state == MissionState.Planning && record.LeadThread is null)
        {
            _ = Launch(id);
            return null;
        }
        if (!wasStopped)
        {
            Advance(id);
            return null;
        }
        // The cut turn runs again: each agent that was working goes on behind the gate.
        var again = state == MissionState.Reviewing ? record.ReviewerThread : record.LeadThread;
        if (again is { } thread)
        {
            var token = LiveOf(id).Launch.Token;
            _ = Admit(id, thread, record.Provider, token).ContinueWith(_ => OnMain(() =>
            {
                if (Find(id) is not { } now || !MissionStates.IsRunning(now.Mission.State)) return;
                var text = state == MissionState.Reviewing
                    ? "Your review was cut off. Do it again from the start. " + ReviewVerdict.Instruction
                    : now.PendingRedirect ?? "The mission was stopped part way. Go on from where it stopped; do the last step again, since it may have been cut.";
                if (o.Agents.Send(thread, text) && now.PendingRedirect is not null && state != MissionState.Reviewing)
                    Update(id, r => r with { PendingRedirect = null });
            }), TaskScheduler.Default);
        }
        return null;
    }

    /// <summary>Redirect (3.3): a short instruction to the lead, now when it is idle, else at the end of its turn.</summary>
    public string? Redirect(Guid id, string text)
    {
        if (Find(id) is not { } record) return "There is no such mission.";
        if (string.IsNullOrWhiteSpace(text)) return "Say what to tell the lead.";
        if (record.Mission.State is MissionState.Done or MissionState.Failed or MissionState.Stopping) return "The mission is not running.";
        Log(record, record.LeadThread, "message_agent", ActionClass.Agents, "redirect", text);
        var running = MissionStates.IsRunning(record.Mission.State) || record.Mission.State == MissionState.NeedsYou;
        if (running && record.LeadThread is { } lead && !o.Agents.TurnRunning(lead) && record.Mission.State != MissionState.Reviewing
            && o.Agents.Send(lead, text))
        {
            LiveOf(id).LeadDone = false;
            return null;
        }
        Update(id, r => r with { PendingRedirect = r.PendingRedirect is { } earlier ? earlier + "\n\n" + text : text });
        return null;
    }

    /// <summary>
    /// request_helpers from a head (15.4): a child mission one level down. Past the owner's depth it raises a Limit card
    /// and starts only on his Allow; past the ceiling of 4 it is refused.
    /// </summary>
    public MissionStart? RequestHelpers(Guid parentMission, string task, int helpers, string reason, out string? refused)
    {
        refused = null;
        if (Find(parentMission) is not { } parent)
        {
            refused = "There is no such mission.";
            return null;
        }
        var level = MissionGate.ChildLevel(parent.Level);
        if (MissionGate.BeyondCeiling(level))
        {
            refused = $"Helpers here would run {level} levels deep; nothing runs deeper than {MissionLimits.DepthCeiling}.";
            return null;
        }
        var request = new MissionRequest("Helpers: " + TextLine(task), task + "\n\nWhy: " + reason, parent.Mission.AssistantId,
            parent.ProjectId, parent.ProjectPath, parent.Provider)
        {
            Model = parent.Model,
            Team = helpers > 1,
            Origin = MissionOrigin.Delegation,
            Parent = parentMission,
            Taint = parent.Mission.TaintContext,
        };
        if (MissionGate.OverDepth(level, o.Limits()) is { } over)
        {
            var card = new DecisionItem(Guid.NewGuid(), DecisionKind.Limit, DecisionWeight.Reversible, "Start helpers past your depth limit?")
            {
                Inputs = new ValueList<DecisionInput>([new("Task", task), new("Helpers", helpers.ToString(CultureInfo.InvariantCulture)), new("Why", reason)]),
                Why = over,
                Rule = "Depth limit (Settings)",
                AssistantId = parent.Mission.AssistantId,
                MissionId = parentMission,
                Agent = "A head",
            };
            o.Queue.Raise(card, (_, answer) => OnMain(() =>
            {
                if (answer.Approves) Start(request);
            }));
            refused = "Waiting for the owner: " + over;
            return null;
        }
        return Start(request);
    }

    /// <summary>
    /// The report tool from one of the mission's chats (15.4 "Merging"): the lead's report becomes the mission's result
    /// and its artifacts the deliverables; a lead that reports itself blocked or failed leaves the mission Blocked, with
    /// its words as the reason (the reviewer runs only on work the lead calls done).
    /// </summary>
    public string? Report(Guid thread, string summary, string status, IReadOnlyList<string> artifacts)
    {
        if (MissionOf(thread) is not { } record) return "This chat works for no mission.";
        var lead = thread == record.LeadThread;
        Update(record.Id, r => r with
        {
            Report = lead ? summary : r.Report,
            Artifacts = new ValueList<string>(r.Artifacts.Concat(artifacts).Distinct(StringComparer.Ordinal).Take(64)),
        });
        if (lead && status is "blocked" or "failed") Block(record.Id, "The lead reported it " + status + ": " + TextLine(summary));
        return null;
    }

    private static string TextLine(string text)
    {
        var line = text.ReplaceLineEndings(" ").Trim();
        return line.Length <= 60 ? line : line[..60] + "…";
    }

    // ---- crash resume (15.3) ----

    /// <summary>
    /// The missions the store held at open (after the interrupted rule): each comes back as it was journaled. A mission
    /// whose merge was pending raises its merge card again; an interrupted one waits for the owner's Resume.
    /// </summary>
    public void Load(IEnumerable<MissionRecord> records)
    {
        var merges = new List<Guid>();
        lock (gate)
        {
            foreach (var record in records)
            {
                missions[record.Id] = record;
                live[record.Id] = new Live { Restored = true };
                foreach (var thread in record.Mission.ThreadIds) threadMission[thread] = record.Id;
                if (record.LeadThread is { } lead) live[record.Id].AgentNames[lead] = "Lead";
                foreach (var head in record.Mission.Heads) if (Guid.TryParse(head.Id, out var t)) live[record.Id].AgentNames[t] = head.Name;
                if (record.MergePending) merges.Add(record.Id);
            }
        }
        foreach (var id in merges) _ = OfferMerge(id, "Raised again after nth restarted.");
    }

    // ---- helpers ----

    /// <summary>What a mission that needs the owner was doing before (Working, or Reviewing while its reviewer runs).</summary>
    private MissionState NeedsYouFrom(MissionRecord record) =>
        LiveOf(record.Id).NeedsYouFrom ?? (record.ReviewerThread is null ? MissionState.Working : MissionState.Reviewing);

    private Live LiveOf(Guid id)
    {
        lock (gate)
        {
            if (!live.TryGetValue(id, out var l)) live[id] = l = new Live();
            return l;
        }
    }

    private string AgentName(Guid mission, Guid thread)
    {
        lock (gate) return LiveOf(mission).AgentNames.GetValueOrDefault(thread) ?? "An agent";
    }

    private List<Guid> Threads(MissionRecord record)
    {
        var threads = new List<Guid>();
        if (record.LeadThread is { } lead) threads.Add(lead);
        foreach (var head in record.Mission.Heads) if (Guid.TryParse(head.Id, out var t)) threads.Add(t);
        if (record.ReviewerThread is { } reviewer) threads.Add(reviewer);
        return threads;
    }

    private static AgentPlace Place(ProviderKind provider) =>
        provider.IsAPIKeyBased || provider == ProviderKind.Local ? AgentPlace.Api : AgentPlace.Local;

    private void Block(Guid id, string reason)
    {
        Update(id, r => Moved(r with { Reason = reason, Waiting = null }, MissionState.Blocked));
        if (Find(id) is { } record) Log(record, null, "mission", ActionClass.Agents, "blocked: " + reason);
    }

    /// <summary>The record in a new state, when the move is legal; as it was otherwise.</summary>
    private static MissionRecord Moved(MissionRecord record, MissionState to) =>
        record.Mission.State == to || !MissionStates.IsLegal(record.Mission.State, to) ? record : record with { Mission = record.Mission with { State = to } };

    private void Update(Guid id, Func<MissionRecord, MissionRecord> change)
    {
        MissionRecord next;
        lock (gate)
        {
            if (!missions.TryGetValue(id, out var current)) return;
            next = change(current);
            if (ReferenceEquals(next, current)) return;
            missions[id] = next;
        }
        Journal(next);
        Raise(next);
    }

    private void Journal(MissionRecord record)
    {
        try { o.Save?.Invoke(record); }
        catch (Exception error) when (error is not OperationCanceledException) { Faulted?.Invoke("The mission journal could not be written: " + error.Message); }
    }

    private void Raise(MissionRecord record) => Changed?.Invoke(record);

    private void OnMain(Action work) => o.Agents.Post(work);

    private void Log(MissionRecord record, Guid? thread, string tool, ActionClass actionClass, string result, string? input = null)
    {
        if (o.Audit is not { } audit) return;
        try
        {
            audit(new AuditEntry
            {
                Assistant = record.Mission.AssistantId,
                Mission = record.Id.ToString("D"),
                Agent = thread?.ToString("D"),
                Tool = tool,
                ActionClass = actionClass.ToString(),
                Inputs = input is null ? [] : [AuditInput.Plain("request", input)],
                Result = result,
            });
        }
        catch (Exception error) when (error is not OperationCanceledException) { Faulted?.Invoke("The activity log could not be written: " + error.Message); }
    }

    private static string RuleLine(PolicyRule rule) => rule switch
    {
        PolicyRule.AlwaysConfirmed => "Always confirmed",
        PolicyRule.Unknown => "Not recognised: asks",
        PolicyRule.AutoAnswerOff => "Asks until nth's injection checks pass",
        PolicyRule.Tainted => "Read untrusted content: asks",
        PolicyRule.NeedsGrant => "Needs a grant",
        PolicyRule.AskFirst => "Your ask-first rule",
        PolicyRule.OwnerAsks => "You asked to be asked",
        PolicyRule.ConfigurationCard => "Standing configuration: always a card",
        PolicyRule.OutsideDuty => "Outside the duty",
        _ => rule.ToString(),
    };
}
