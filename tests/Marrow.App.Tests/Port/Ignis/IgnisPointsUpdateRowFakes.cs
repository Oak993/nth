using Marrow.App.Port.Updates;
using Marrow.Core.Port.Ignis;
using Marrow.Core.Port.Updates;

namespace Marrow.App.Tests.Port.Ignis;

// The fakes IgnisPointsUpdateRowTests uses (moved out of the test class unchanged but for their names: a test class holds no nested type and its file names no other type, R-TEST-2/R-TEST-3, ED-NEW-handoverfix-3).
internal sealed class RecordingUpdateItem(IUpdateItem inner, List<string> order) : IUpdateItem
{
    public string Id => inner.Id;
    public string Title => inner.Title;
    public UpdateItemKind Kind => inner.Kind;
    public Task<string?> InstalledVersion(CancellationToken cancellation) => inner.InstalledVersion(cancellation);
    public Task<string?> LatestVersion(CancellationToken cancellation) => inner.LatestVersion(cancellation);
    public Task<UpdateOutcome> Update(IProgress<double?> progress, CancellationToken cancellation) { order.Add(Id); return inner.Update(progress, cancellation); }
}

internal sealed class PointsRowFakeController(IgnisSettings settings, IIgnisPoints points) : IIgnisController
{
    public event Action<Guid>? Changed { add { } remove { } }
    public IgnisSettings Settings => settings;
    public IIgnisPoints Points => points;
    public ISavingsLedger Ledger { get; } = new FakeSettingsLedger([]);
    public IgnisChatStatus Status(Guid thread) => IgnisChatStatus.Unknown(thread, IgnisMode.WarmAndTidy, true);
    public void SetMode(Guid thread, IgnisMode mode) { }
    public void SetAuto(Guid thread, bool auto) { }
    public void KeepWarmNow(Guid thread) { }
    public void TidyUpNow(Guid thread) { }
    public string Report(Guid thread) => "";
    public void Attach(IIgnisRuntimePort port) { }
    public void Detach(Guid thread) { }
    public void Observe(Guid thread, IgnisTurnUsage usage) { }
    public void TurnFinished(Guid thread) { }
    public void UsageLimitHit(Guid thread, DateTimeOffset? resetsAt) { }
}
