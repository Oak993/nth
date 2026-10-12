// PORT-ONLY: Dextra (DESIGN.md rev 5, 3.1, 3.4, 12.6a, 15.3 "Missions are journaled"; unit AS-5; ED-NEW-dextra-as5-7).
// No Swift twin. What the mission runner keeps about a mission beside its state (AS-1's Mission): the brief, where it
// works, its chats, the reviewer's verdicts and what it delivered. The journal codec writes it to the store's mission
// journal and reads it back strictly, and the interrupted rule turns a mission that was live when nth ended into a
// paused one that asks "Resume?".
using System.Globalization;
using System.Text.Json.Nodes;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;

namespace Marrow.Services.Assistant;

/// <summary>One verdict of the reviewer (3.4).</summary>
public sealed record MissionReview(bool Passed, string Reasons, string Reviewer, DateTimeOffset At);

/// <summary>
/// Instructions an agent of the mission found in untrusted content and nth ignored (12.4, AS-7): when, which agent,
/// where the text came from, a short quote and the pattern that matched. The mission card lists them.
/// </summary>
public sealed record MissionFinding(DateTimeOffset At, Guid? Agent, ContentOrigin Origin, string Detail, string Quote, string Pattern);

/// <summary>A mission as the runner keeps it.</summary>
public sealed record MissionRecord(Mission Mission)
{
    public Guid Id => Mission.Id;

    /// <summary>What the mission must deliver (the owner's words, or the duty's or the delegating assistant's).</summary>
    public string Brief { get; init; } = "";

    public Guid ProjectId { get; init; }

    /// <summary>The owner's folder the mission works on (the repository or the plain folder its workspace copies).</summary>
    public string ProjectPath { get; init; } = "";

    public ProviderKind Provider { get; init; } = ProviderKind.Claude;

    public string? Model { get; init; }

    /// <summary>A Hydra team leads it (the lead chat has Hydra on).</summary>
    public bool Team { get; init; }

    /// <summary>The depth it runs at: a top mission is 2 (its lead and the lead's heads), a child one below its parent.</summary>
    public int Level { get; init; } = MissionLimits.TopLevel;

    public MissionWorkspace? Workspace { get; init; }

    public Guid? LeadThread { get; init; }

    public Guid? ReviewerThread { get; init; }

    /// <summary>What the lead reported last (its final message or its report).</summary>
    public string? Report { get; init; }

    /// <summary>The files and results it delivered.</summary>
    public ValueList<string> Artifacts { get; init; } = ValueList<string>.Empty;

    public ValueList<MissionReview> Reviews { get; init; } = ValueList<MissionReview>.Empty;

    /// <summary>Why it is Failed or Blocked, for the card.</summary>
    public string? Reason { get; init; }

    /// <summary>Why it waits to start (the agent gate's line), while Planning.</summary>
    public string? Waiting { get; init; }

    /// <summary>It was live when nth ended: it came back Paused and asks "Resume?" (15.3).</summary>
    public bool Interrupted { get; init; }

    /// <summary>The reviewer passed and the workspace's changes wait for the owner's Merge, Rebase first or Discard.</summary>
    public bool MergePending { get; init; }

    /// <summary>The owner kept the workspace past the 7 days (12.6a).</summary>
    public bool KeepWorkspace { get; init; }

    /// <summary>A redirect that waits for the lead's turn to end.</summary>
    public string? PendingRedirect { get; init; }

    public decimal? BudgetUsd { get; init; }

    public DateTimeOffset Created { get; init; }

    public DateTimeOffset? Ended { get; init; }

    /// <summary>
    /// What each of its agents read that nth cannot vouch for (12.4, AS-7): an agent's own taint, on top of the mission's
    /// (Mission.TaintContext, the floor every agent shares once a tainted agent may have left text in the workspace).
    /// </summary>
    public ValueMap<Guid, Taint> AgentTaint { get; init; } = ValueMap<Guid, Taint>.Empty;

    /// <summary>The taint its report carries to whoever reads it (the parent's agent, Dextra's brain), kept when it ends.</summary>
    public Taint ReportTaint { get; init; } = Taint.Clean;

    /// <summary>Instructions its agents found in untrusted content, newest last, at most <see cref="MaxFindings"/>.</summary>
    public ValueList<MissionFinding> Findings { get; init; } = ValueList<MissionFinding>.Empty;

    /// <summary>A reader mission (12.4 "Reader and actor split"): its agents only read and report; no reviewer, no merge.</summary>
    public bool Readers { get; init; }

    /// <summary>The parent mission's chat that asked for it (request_helpers, start_mission from a mission): its report goes there.</summary>
    public Guid? RequestedBy { get; init; }

    public const int MaxFindings = 20;

    /// <summary>The model that reviewed it last ("Reviewed by ...").</summary>
    public string? ReviewedBy => Reviews.Count > 0 ? Reviews[^1].Reviewer : null;
}

/// <summary>The mission journal's codec (15.3), and the rule for a mission nth was running when it ended.</summary>
public static class MissionJournal
{
    public const int Version = 1;

    /// <summary>The store id of a mission: its id in 32 lowercase hex digits.</summary>
    public static string StoreId(Guid mission) => mission.ToString("N");

    public static JsonObject ToJson(MissionRecord record)
    {
        var m = record.Mission;
        var body = new JsonObject
        {
            ["v"] = Version,
            ["id"] = m.Id.ToString("D"),
            ["title"] = m.Title,
            ["assistant"] = m.AssistantId,
            ["origin"] = m.Origin.ToString(),
            ["state"] = m.State.ToString(),
            ["reverted"] = m.Reverted,
            ["changedFiles"] = m.ChangedFiles,
            ["reviewRounds"] = m.ReviewRounds,
            ["heads"] = new JsonArray([.. m.Heads.Select(HeadJson)]),
            ["children"] = new JsonArray([.. m.ChildMissionIds.Select(c => (JsonNode)c.ToString("D"))]),
            ["threads"] = new JsonArray([.. m.ThreadIds.Select(t => (JsonNode)t.ToString("D"))]),
            ["taint"] = TaintJson(m.TaintContext.Taint),
            ["brief"] = record.Brief,
            ["project"] = record.ProjectId.ToString("D"),
            ["projectPath"] = record.ProjectPath,
            ["provider"] = record.Provider.Id,
            ["team"] = record.Team,
            ["level"] = record.Level,
            ["artifacts"] = new JsonArray([.. record.Artifacts.Select(a => (JsonNode)a)]),
            ["reviews"] = new JsonArray([.. record.Reviews.Select(r => (JsonNode)new JsonObject
            {
                ["passed"] = r.Passed, ["reasons"] = r.Reasons, ["reviewer"] = r.Reviewer, ["at"] = Time(r.At),
            })]),
            ["interrupted"] = record.Interrupted,
            ["mergePending"] = record.MergePending,
            ["keepWorkspace"] = record.KeepWorkspace,
            ["created"] = Time(record.Created),
        };
        void Put(string name, string? value) { if (value is not null) body[name] = value; }
        Put("resumeTo", m.ResumeTo?.ToString());
        Put("pausedBy", m.PausedBy?.ToString());
        Put("stoppedBy", m.StoppedBy?.ToString());
        Put("parent", m.ParentMissionId?.ToString("D"));
        Put("worktreePath", m.WorktreePath);
        Put("branch", m.Branch);
        Put("model", record.Model);
        Put("lead", record.LeadThread?.ToString("D"));
        Put("reviewer", record.ReviewerThread?.ToString("D"));
        Put("report", record.Report);
        Put("reason", record.Reason);
        Put("waiting", record.Waiting);
        Put("pendingRedirect", record.PendingRedirect);
        Put("ended", record.Ended is { } ended ? Time(ended) : null);
        if (record.BudgetUsd is { } budget) body["budgetUsd"] = budget;
        if (record.Workspace is { } workspace) body["workspace"] = MissionWorkspaces.ToJson(workspace);
        // AS-7: optional keys of version 1 (absent in a journal written before them: clean, no findings, an actor mission).
        if (record.AgentTaint.Count > 0)
        {
            var agents = new JsonObject();
            foreach (var (agent, taint) in record.AgentTaint.OrderBy(p => p.Key)) agents[agent.ToString("D")] = TaintJson(taint);
            body["agentTaint"] = agents;
        }
        if (record.ReportTaint.IsTainted) body["reportTaint"] = TaintJson(record.ReportTaint);
        if (record.Findings.Count > 0)
            body["findings"] = new JsonArray([.. record.Findings.Select(f =>
            {
                var o = new JsonObject
                {
                    ["at"] = Time(f.At), ["origin"] = f.Origin.ToString(), ["detail"] = f.Detail, ["quote"] = f.Quote, ["pattern"] = f.Pattern,
                };
                if (f.Agent is { } agent) o["agent"] = agent.ToString("D");
                return (JsonNode)o;
            })]);
        if (record.Readers) body["readers"] = true;
        Put("requestedBy", record.RequestedBy?.ToString("D"));
        return body;
    }

    /// <summary>The record, or null when the body is damaged or of another version (the store reports it, the runner skips it).</summary>
    public static MissionRecord? FromJson(JsonObject? body)
    {
        if (body is null) return null;
        try
        {
            if (Int(body, "v") != Version) return null;
            var mission = new Mission(
                Guid.Parse(Text(body, "id")!), Text(body, "title") ?? "", Text(body, "assistant") ?? "",
                Enum.Parse<MissionOrigin>(Text(body, "origin")!), Enum.Parse<MissionState>(Text(body, "state")!))
            {
                ResumeTo = Text(body, "resumeTo") is { } r ? Enum.Parse<MissionState>(r) : null,
                PausedBy = Text(body, "pausedBy") is { } p ? Enum.Parse<PauseCause>(p) : null,
                StoppedBy = Text(body, "stoppedBy") is { } s ? Enum.Parse<StopCause>(s) : null,
                Reverted = Bool(body, "reverted"),
                ChangedFiles = Int(body, "changedFiles"),
                ReviewRounds = Int(body, "reviewRounds"),
                Heads = new ValueList<MissionHead>(Array(body, "heads").Select(h => Head((JsonObject)h!))),
                ParentMissionId = Text(body, "parent") is { } parent ? Guid.Parse(parent) : null,
                ChildMissionIds = new ValueList<Guid>(Array(body, "children").Select(c => Guid.Parse(c!.GetValue<string>()))),
                ThreadIds = new ValueList<Guid>(Array(body, "threads").Select(t => Guid.Parse(t!.GetValue<string>()))),
                TaintContext = new TaintContext(TaintOf(body, "taint")),
                WorktreePath = Text(body, "worktreePath"),
                Branch = Text(body, "branch"),
            };
            return new MissionRecord(mission)
            {
                Brief = Text(body, "brief") ?? "",
                ProjectId = Guid.Parse(Text(body, "project")!),
                ProjectPath = Text(body, "projectPath") ?? "",
                Provider = SwiftEnum<ProviderKind>.FromRawValue(Text(body, "provider")) ?? throw new FormatException("unknown provider"),
                Model = Text(body, "model"),
                Team = Bool(body, "team"),
                Level = Int(body, "level"),
                Workspace = body["workspace"] is { } workspace ? MissionWorkspaces.FromJson(workspace) ?? throw new FormatException("workspace") : null,
                LeadThread = Text(body, "lead") is { } lead ? Guid.Parse(lead) : null,
                ReviewerThread = Text(body, "reviewer") is { } reviewer ? Guid.Parse(reviewer) : null,
                Report = Text(body, "report"),
                Artifacts = new ValueList<string>(Array(body, "artifacts").Select(a => a!.GetValue<string>())),
                Reviews = new ValueList<MissionReview>(Array(body, "reviews").Select(r =>
                {
                    var o = (JsonObject)r!;
                    return new MissionReview(Bool(o, "passed"), Text(o, "reasons") ?? "", Text(o, "reviewer") ?? "", ParseTime(Text(o, "at")!));
                })),
                Reason = Text(body, "reason"),
                Waiting = Text(body, "waiting"),
                Interrupted = Bool(body, "interrupted"),
                MergePending = Bool(body, "mergePending"),
                KeepWorkspace = Bool(body, "keepWorkspace"),
                PendingRedirect = Text(body, "pendingRedirect"),
                BudgetUsd = body["budgetUsd"] is { } budget ? budget.GetValue<decimal>() : null,
                Created = ParseTime(Text(body, "created")!),
                Ended = Text(body, "ended") is { } ended ? ParseTime(ended) : null,
                AgentTaint = body["agentTaint"] is JsonObject agents
                    ? new ValueMap<Guid, Taint>(agents.ToDictionary(p => Guid.Parse(p.Key), p => TaintOf(agents, p.Key)))
                    : ValueMap<Guid, Taint>.Empty,
                ReportTaint = TaintOf(body, "reportTaint"),
                Findings = new ValueList<MissionFinding>(Array(body, "findings").Take(MissionRecord.MaxFindings).Select(f =>
                {
                    var o = (JsonObject)f!;
                    return new MissionFinding(ParseTime(Text(o, "at")!), Text(o, "agent") is { } agent ? Guid.Parse(agent) : null,
                        Enum.Parse<ContentOrigin>(Text(o, "origin")!), Text(o, "detail") ?? "", Text(o, "quote") ?? "", Text(o, "pattern") ?? "");
                })),
                Readers = Bool(body, "readers"),
                RequestedBy = Text(body, "requestedBy") is { } requester ? Guid.Parse(requester) : null,
            };
        }
        catch (Exception error) when (error is FormatException or InvalidOperationException or ArgumentException or InvalidCastException
            or NullReferenceException or OverflowException or System.Text.Json.JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// The store's InterruptedMission rule (15.3): a mission that was running, planning, reviewing, pausing or waiting on a
    /// decision when nth ended comes back Paused (from the state it was in, and a cut turn runs again on Resume), marked
    /// Interrupted so the board asks "Resume?". One that was stopping is Stopped: nth's end closed every agent's job, and
    /// kill-on-close ended its processes. Paused, Stopped, Blocked, Done and Failed missions stay as they were. Returns
    /// null when nothing changes, and for a damaged body (the store keeps it as it is).
    /// </summary>
    public static JsonObject? Interrupted(JsonObject body)
    {
        if (FromJson(body) is not { } record) return null;
        var m = record.Mission;
        var heads = new ValueList<MissionHead>(m.Heads.Select(h => h.State switch
        {
            HeadState.Working or HeadState.Pausing => h with { State = HeadState.Paused, ResumeTo = HeadState.Working },
            HeadState.Stopping => h with { State = HeadState.Stopped },
            _ => h,
        }));
        Mission? next = m.State switch
        {
            MissionState.Planning or MissionState.Working or MissionState.Reviewing =>
                m with { State = MissionState.Paused, ResumeTo = m.State, PausedBy = PauseCause.Owner, Heads = heads },
            // A card was open: the decision queue is not kept, so the cut turn runs again and asks again on Resume.
            MissionState.NeedsYou => m with { State = MissionState.Paused, ResumeTo = MissionState.Working, PausedBy = PauseCause.Owner, Heads = heads },
            MissionState.Pausing => m with { State = MissionState.Paused, Heads = heads },
            MissionState.Stopping => m with { State = MissionState.Stopped, Heads = heads },
            _ => null,
        };
        if (next is null) return null;
        return ToJson(record with { Mission = next, Interrupted = next.State == MissionState.Paused || record.Interrupted, Waiting = null });
    }

    internal static JsonArray TaintJson(Taint taint) =>
        new([.. taint.Sources.Select(s => (JsonNode)new JsonObject { ["origin"] = s.Origin.ToString(), ["detail"] = s.Detail })]);

    // A taint as the journal keeps it: its sources in order. An absent key is clean; a damaged one fails the whole record.
    private static Taint TaintOf(JsonObject o, string name) =>
        new(new ValueList<TaintSource>(Array(o, name).Select(t =>
            new TaintSource(Enum.Parse<ContentOrigin>(Text((JsonObject)t!, "origin")!), Text((JsonObject)t!, "detail") ?? ""))));

    private static JsonNode HeadJson(MissionHead head)
    {
        var o = new JsonObject
        {
            ["id"] = head.Id,
            ["name"] = head.Name,
            ["state"] = head.State.ToString(),
            ["stoppedByHand"] = head.StoppedByHand,
            ["heldByReserve"] = head.HeldByReserve,
            ["dependsOn"] = new JsonArray([.. head.DependsOn.Select(d => (JsonNode)d)]),
        };
        if (head.ResumeTo is { } resume) o["resumeTo"] = resume.ToString();
        return o;
    }

    private static MissionHead Head(JsonObject o) => new(Text(o, "id")!, Text(o, "name") ?? "", Enum.Parse<HeadState>(Text(o, "state")!))
    {
        ResumeTo = Text(o, "resumeTo") is { } r ? Enum.Parse<HeadState>(r) : null,
        StoppedByHand = Bool(o, "stoppedByHand"),
        HeldByReserve = Bool(o, "heldByReserve"),
        DependsOn = new ValueList<string>(Array(o, "dependsOn").Select(d => d!.GetValue<string>())),
    };

    private static string Time(DateTimeOffset at) => at.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseTime(string text) => DateTimeOffset.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private static string? Text(JsonObject o, string name) => o[name] is { } node ? node.GetValue<string>() : null;

    private static int Int(JsonObject o, string name) => o[name] is { } node ? node.GetValue<int>() : 0;

    private static bool Bool(JsonObject o, string name) => o[name] is { } node && node.GetValue<bool>();

    private static IEnumerable<JsonNode?> Array(JsonObject o, string name) => o[name] is JsonArray array ? array : [];
}
