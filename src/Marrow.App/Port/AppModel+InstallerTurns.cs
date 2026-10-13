using Marrow.Core.Port;

namespace Marrow.App;

// PORT-ONLY: ED-NEW-inst-c7-5. In-memory admission; exact typed payloads remain in their existing captured body.
public sealed partial class AppModel
{
    private readonly HashSet<object> installerQuitHolds = [];
    private readonly HashSet<InstallerTurnLease> installerAdmissions = [];
    private readonly Dictionary<Guid, Action> installerDeferredRetries = [];
    private bool installerHoldsNewTurns;
    public bool InstallerHoldsNewTurns
    {
        get => Read(installerHoldsNewTurns);
        private set => Set(ref installerHoldsNewTurns, value);
    }
    public string? InstallerClosingMessage => InstallerHoldsNewTurns ? "nth is closing to update" : null;
    internal long InstallerQuitGeneration { get; private set; }
    internal IEnumerable<(Guid Thread, DateTimeOffset Started)> PendingInstallerTurns =>
        installerAdmissions.GroupBy(lease => lease.Thread).Select(group => (group.Key, group.Min(lease => lease.Started)));
    internal bool HasUndurableInstallerTurns => installerAdmissions.Any(lease => lease.RequiresDurability && !lease.IsDurable);
    internal void HoldInstallerTurns(object quit)
    {
        MainActor.AssumeIsolated();
        if (!installerQuitHolds.Add(quit)) return;
        InstallerQuitGeneration = checked(InstallerQuitGeneration + 1);
        InstallerHoldsNewTurns = true;
    }
    internal void ReleaseInstallerTurns(object quit)
    {
        MainActor.AssumeIsolated();
        if (!installerQuitHolds.Remove(quit) || installerQuitHolds.Count != 0) return;
        InstallerHoldsNewTurns = false;
        var retries = installerDeferredRetries.Values.ToArray();
        installerDeferredRetries.Clear();
        foreach (var retry in retries) retry();
        foreach (var runtime in LiveRuntimes.ToArray()) runtime.ResumeAfterInstallerQuit();
        AccountRouting.ResumeAfterInstallerQuit();
    }
    internal InstallerTurnLease? AdmitInstallerTurn(Guid thread, bool requiresDurability = true)
    {
        MainActor.AssumeIsolated();
        if (InstallerHoldsNewTurns) return null;
        var lease = new InstallerTurnLease(this, thread, requiresDurability);
        installerAdmissions.Add(lease);
        return lease;
    }
    internal InstallerTurnLease AdmitInstallerConversationWork(Guid thread) =>
        AdmitInstallerTurn(thread, requiresDurability: false) ?? throw new InvalidOperationException("nth is closing to update");
    internal void ScheduleInstallerRetry(Guid thread, Action body)
    {
        MainActor.AssumeIsolated();
        if (InstallerHoldsNewTurns) installerDeferredRetries[thread] = body;
        else body();
    }
    internal void ReleaseInstallerTurnAdmission(InstallerTurnLease lease)
    {
        MainActor.AssumeIsolated();
        installerAdmissions.Remove(lease);
    }
    internal void CancelInstallerPrivateWork()
    {
        MainActor.AssumeIsolated();
        foreach (var lease in installerAdmissions.ToArray())
            if (lease.IsLive && !lease.RequiresDurability)
                try { lease.CancelPrivateWork(); }
                catch (Exception) { /* One provider cancellation callback must not prevent the remaining shutdowns. */ }
    }
}
