// PORT-ONLY: Dextra (DESIGN.md rev 5, 12.3 point 2, 12.4; unit AS-7; ED-NEW-dextra-as7-7 to -10). No Swift twin.
// The runner's side of taint: each agent of a mission carries what it read (MissionRecord.AgentTaint) on top of the
// mission's floor, which every agent shares once a tainted agent may have left text in the workspace. Taint follows
// text: a head carries its lead's, a lead its heads' reports, a helper its opener's and back, a child mission's report
// the child's. The engine's Allow is answered alone (labelled and logged) only while auto-answering is on, and
// instructions found in what an agent read are kept on the mission's card.
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;

namespace Marrow.App.Assistant;

public sealed partial class MissionRunner
{
    /// <summary>The longest child report the chat that asked for it is sent; the whole one stays on the child's card.</summary>
    public const int ReportCap = 12_000;

    /// <summary>What one agent of the mission carries now: the mission's floor and its own taint.</summary>
    public TaintContext TaintOf(Guid mission, Guid thread)
    {
        if (Find(mission) is not { } record) return TaintContext.Clean;
        var own = record.AgentTaint.GetValueOrDefault(thread) ?? Taint.Clean;
        return new TaintContext(TaintWiring.Capped(record.Mission.TaintContext.Taint.Union(own)));
    }

    /// <summary>The role an agent of the mission decides with (12.4): the reviewer and a reader mission's agents only read.</summary>
    public static AgentRole RoleOf(MissionRecord record, Guid thread)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (thread == record.ReviewerThread) return AgentRole.Reviewer;
        return record.Readers ? AgentRole.Reader : AgentRole.Actor;
    }

    /// <summary>
    /// One of the runner's agents read something untrusted (a tool call, an allowed read, a report): it carries that from
    /// now on. When the call may write files (<paramref name="writesFiles"/>) and the agent is tainted, the mission's floor
    /// takes its taint too, so no agent of the mission runs alone from text a tainted agent left in the workspace. An
    /// ended mission takes nothing (its taint ended with it).
    /// </summary>
    public void AbsorbAgent(Guid thread, Taint taint, bool writesFiles)
    {
        if (!taint.IsTainted && !writesFiles) return;
        if (MissionOf(thread) is not { } record) return;
        Update(record.Id, r =>
        {
            if (r.Mission.State is MissionState.Done or MissionState.Failed) return r;
            var own = r.AgentTaint.GetValueOrDefault(thread) ?? Taint.Clean;
            var next = TaintWiring.Capped(own.Union(taint));
            var floor = r.Mission.TaintContext.Taint;
            var nextFloor = writesFiles && next.IsTainted ? TaintWiring.Capped(floor.Union(next)) : floor;
            if (next == own && nextFloor == floor) return r;
            return r with
            {
                AgentTaint = next == own ? r.AgentTaint : r.AgentTaint.Mutating(map => map[thread] = next),
                Mission = nextFloor == floor ? r.Mission : r.Mission with { TaintContext = new TaintContext(nextFloor) },
            };
        });
    }

    /// <summary>
    /// Instructions an agent found in what it read (12.4 "Instructions found in content"): never followed, kept on the
    /// mission's card (the newest <see cref="MissionRecord.MaxFindings"/>) and logged.
    /// </summary>
    public void Found(Guid thread, InstructionsFound found)
    {
        ArgumentNullException.ThrowIfNull(found);
        if (MissionOf(thread) is not { } record) return;
        var finding = new MissionFinding(o.Time.GetUtcNow(), thread, found.Source.Origin, found.Source.Detail, found.Finding.Quote, found.Finding.Pattern);
        Update(record.Id, r => r with { Findings = new ValueList<MissionFinding>(r.Findings.Append(finding).TakeLast(MissionRecord.MaxFindings)) });
        var read = found.Source.Origin is ContentOrigin.OutsideFile or ContentOrigin.Pdf ? ActionClass.ReadLocal : ActionClass.ReadWeb;
        Log(record, thread, "instructions", read, $"found in {TaintGates.OriginWord(found.Source.Origin)} ({found.Finding.Pattern}); ignored");
    }

    /// <summary>
    /// A chat of the mission sent the helper it opened a new prompt (AppModel.SpawnSubagent reusing it): the parent
    /// wrote it, so the helper carries the parent's taint as it is now, not only as it was when the helper opened
    /// (12.4 "Taint follows text"; ED-NEW-dextra-as7-15).
    /// </summary>
    public void HelperPrompted(Guid parent, Guid helper)
    {
        if (MissionOf(helper) is not { } record || MissionOf(parent)?.Id != record.Id) return;
        Inherit(record.Id, parent, helper);
    }

    private Taint TaintOfAgent(Guid mission, Guid thread) =>
        Find(mission)?.AgentTaint.GetValueOrDefault(thread) ?? Taint.Clean;

    /// <summary>Text one agent wrote reached another (a task, a report): the other carries the writer's taint.</summary>
    private void Inherit(Guid mission, Guid from, Guid to)
    {
        var taint = TaintOfAgent(mission, from);
        if (taint.IsTainted) AbsorbAgent(to, taint, writesFiles: false);
    }

    private void AbsorbFloor(Guid mission, Taint taint) => Absorb(mission, taint);

    private bool AutoAnswering()
    {
        try { return o.AutoAnswering(); }
        catch (Exception error) when (error is not OperationCanceledException) { return false; }
    }

    /// <summary>
    /// The engine allowed it and auto-answering is on (12.3 point 2): logged first with the rule or the grants that
    /// allowed it (none written: it is refused), then answered yes, and labelled in the chat. What it reads taints its
    /// agent before the provider runs it, and the grants it used are spent.
    /// </summary>
    private void AnswerAlone(MissionRecord record, Guid thread, ApprovalRequest request, Classification c, PolicyDecision decision)
    {
        var by = decision.GrantsUsed.Count > 0 ? string.Join(",", decision.GrantsUsed.Select(g => g.ToString("D"))) : "rule:" + decision.Rule;
        if (!Log(record, thread, "approval", c.Class, "allowed: " + decision.Rule, request.Title, by))
        {
            o.Agents.Resolve(thread, request, approve: false);
            return;
        }
        AbsorbAgent(thread, TaintWiring.Untrusted(c, request.Title), writesFiles: false);
        o.Agents.Resolve(thread, request, approve: true);
        o.Agents.Note(thread, $"{AssistantNames.Assistant} allowed this for you: {RuleLine(decision.Rule)}. {decision.Reason}.");
        if (decision.GrantsUsed.Count > 0) o.GrantsUsed?.Invoke(decision.GrantsUsed, "approval:" + request.Id);
    }

    /// <summary>
    /// A child mission ended: its report goes to the chat of the parent that asked for it (the parent's lead when that
    /// chat is gone), as fenced data when it is tainted, and that chat carries its taint (12.4 "a delegated report carries
    /// its taint up"). A parent that ended or is stopping gets nothing.
    /// </summary>
    private void ReportToParent(MissionRecord child)
    {
        if (child.Mission.ParentMissionId is not { } parentId || Find(parentId) is not { } parent) return;
        if (parent.Mission.State is MissionState.Done or MissionState.Failed or MissionState.Stopping or MissionState.Stopped) return;
        var to = child.RequestedBy is { } asked && Threads(parent).Contains(asked) ? asked : parent.LeadThread;
        if (to is not { } thread) return;
        var report = child.Report ?? child.Reason ?? "It left no report.";
        if (report.Length > ReportCap) report = report[..ReportCap] + "\n(cut here; the whole report is on its card)";
        var ended = child.Mission.State == MissionState.Done ? "finished" : "ended without finishing";
        var text = $"The helpers' mission “{child.Mission.Title}” {ended}. Their report:\n" + TaintGates.AsData(new TaintedText(report, child.ReportTaint));
        AbsorbAgent(thread, child.ReportTaint, writesFiles: false);
        if (thread != parent.LeadThread) o.Deliver?.Invoke(thread, text);
        else if (Tell(parent.Id, text, "child report", child.Id.ToString("D")) is { } refused)
            Log(parent, thread, "report", ActionClass.Agents, "child report not delivered: " + refused, child.Id.ToString("D"));
    }
}
