// PORT-ONLY: ED-NEW-mission-pc-1; DESIGN.md 12.3-12.6. Local Tactus/Visus approvals use the exact DextraApproval path.
// Its resolution completes only this waiting adapter; provider approval requests continue to use IMissionAgents.Resolve.
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;

namespace Marrow.App.Assistant;

public sealed partial class MissionRunner
{
    private readonly Dictionary<(Guid Thread, string Request), TaskCompletionSource<bool>> pcApprovals = [];
    private const int PcApprovalCap = 128;

    internal bool PcThreadActive(Guid mission, Guid thread)
    {
        lock (gate)
        {
            if (MissionOf(thread) is not { } record || record.Id != mission || endedAgents.Contains(thread)
                || record.Mission.State is MissionState.Done or MissionState.Failed or MissionState.Stopping or MissionState.Stopped) return false;
            var head = record.Mission.Heads.FirstOrDefault(h => h.Id == HeadId(thread));
            return head is null || head.State is not (HeadState.Done or HeadState.Stopping or HeadState.Stopped);
        }
    }

    internal CancellationToken PcStopToken(Guid mission)
    {
        lock (gate) return live.TryGetValue(mission, out var running) ? running.Launch.Token : new CancellationToken(true);
    }

    internal async Task<bool> ApproveLocal(Guid mission, Guid thread, ApprovalRequest request, CancellationToken cancellationToken)
    {
        var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        OnMain(() =>
        {
            if (cancellationToken.IsCancellationRequested || !PcThreadActive(mission, thread)) { done.TrySetResult(false); return; }
            lock (gate)
            {
                if (pcApprovals.Count >= PcApprovalCap || !pcApprovals.TryAdd((thread, request.Id), done)) { done.TrySetResult(false); return; }
            }
            try { if (!OnApproval(thread, request)) ResolveApproval(thread, request, false); }
            catch (Exception error) when (error is not (OperationCanceledException or OutOfMemoryException)) { ResolveApproval(thread, request, false); }
        });
        try { return await done.Task.WaitAsync(cancellationToken).ConfigureAwait(false); }
        finally
        {
            OnMain(() =>
            {
                lock (gate) pcApprovals.Remove((thread, request.Id));
                OnRequestResolved(thread, request.Id); // cancel/Stop withdraws a card; a late Yes finds no pending request
            });
        }
    }

    private void ResolveApproval(Guid thread, ApprovalRequest request, bool approve)
    {
        TaskCompletionSource<bool>? local;
        lock (gate) pcApprovals.TryGetValue((thread, request.Id), out local);
        if (local is not null) local.TrySetResult(approve);
        else o.Agents.Resolve(thread, request, approve);
    }
}
