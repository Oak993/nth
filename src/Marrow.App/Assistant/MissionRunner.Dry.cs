// PORT-ONLY: Dextra (DESIGN.md rev 5, 7.3 "Dry run"; unit AS-10; ED-NEW-dextra-as10-8). No Swift twin.
// The dry run of a duty is a real mission whose approvals are all answered here: a read is done (answered yes), every other action
// is refused to the agent (answered no) and kept as a "would" step, and when the lead reports it ends Done with the step list as its
// report, with no review and no merge, and its workspace discarded. Nothing but reads ran, so nothing was written, sent or clicked.
using Marrow.Core.Models;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;

namespace Marrow.App.Assistant;

public sealed partial class MissionRunner
{
    private readonly Dictionary<Guid, DryRunLog> dryLogs = [];

    /// <summary>The steps of a dry run so far (made on first use).</summary>
    public DryRunLog DryLogOf(Guid mission)
    {
        lock (gate)
        {
            if (!dryLogs.TryGetValue(mission, out var log)) dryLogs[mission] = log = new DryRunLog();
            return log;
        }
    }

    /// <summary>The report the owner reads for a dry run: the banner, then the steps.</summary>
    public string DryReport(Guid mission) => DryLogOf(mission).Report();

    // Under no lock: a provider approval of a dry run's chat. Reads run; the rest is listed, not done.
    private bool DryApproval(MissionRecord record, Guid thread, ApprovalRequest request, Classification c, PolicyDecision decision)
    {
        var outcome = DryLogOf(record.Id).Intercept(c.Class, decision.Verdict, DryRunWords.Would(request.RequestKind, request.Title), request.Title);
        o.Agents.Resolve(thread, request, outcome.Perform);
        Log(record, thread, "approval", c.Class, outcome.Perform ? "dry run: a real read" : "dry run: not done, listed", request.Title);
        return true;
    }

    private void FinishDryRun(Guid id)
    {
        var steps = DryReport(id);
        // Working reaches Done through Reviewing (12.6b has no direct move); a dry run has no reviewer, so it passes through in the one update.
        Update(id, r => Moved(Moved(r with { Report = steps + (string.IsNullOrWhiteSpace(r.Report) ? "" : "\n\n" + r.Report), Ended = o.Time.GetUtcNow() }, MissionState.Reviewing), MissionState.Done));
        Finish(id);
        if (Find(id) is { Workspace: { } workspace }) _ = DiscardDryWorkspace(id, workspace);
    }

    private async Task DiscardDryWorkspace(Guid id, MissionWorkspace workspace)
    {
        try { await o.Workspaces.Discard(workspace).ConfigureAwait(false); }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            ReportFault("A dry run's workspace could not be removed: " + error.Message);
            return;
        }
        OnMain(() => Update(id, r => r with { Workspace = null, Mission = r.Mission with { WorktreePath = null, ChangedFiles = 0 } }));
    }
}
