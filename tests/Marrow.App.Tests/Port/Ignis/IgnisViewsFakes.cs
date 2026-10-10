using Marrow.App.Runtime;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Ignis;

namespace Marrow.App.Tests.Port.Ignis;

// The fakes IgnisViewsTests drives the Kindle with (moved out of the test class unchanged but for their names: a test class holds no nested type and its file names no other type, R-TEST-2/R-TEST-3, ED-NEW-handoverfix-3).
internal sealed class KindleFakeLedger : ISavingsLedger
{
    public event Action? Changed { add { } remove { } }
    public List<IgnisEvent> All { get; } = [];
    public void Record(IgnisLedgerEntry entry) { }
    public void Record(IgnisEvent @event) => All.Add(@event);
    public IgnisDayTotals Day(DateOnly day) => IgnisDayTotals.Empty(day);
    public IgnisDayTotals ChatDay(Guid thread, DateOnly day) => IgnisDayTotals.Empty(day) with { Reuse = 1.5m };
    public IReadOnlyList<IgnisDayTotals> Days(DateOnly from, DateOnly to) => [];
    public decimal AllTimeSaved => 1478m;
    public DateOnly? Since => null;
    public IReadOnlyList<IgnisEvent> Events(Guid thread, DateOnly day) => All.Where(e => e.ThreadID == thread).ToList();
    public IgnisEvent? Event(string id) => All.FirstOrDefault(e => e.Id == id);
}

internal sealed class KindleFakePoints : IIgnisPoints
{
    public event Action? Changed { add { } remove { } }
    public IgnisPointsFile Current { get; } = new(IgnisPointsFile.CurrentSchemaVersion, "2026.09.20", new ValueList<IgnisModelPoints>([]));
    public IgnisPointsOrigin Origin => IgnisPointsOrigin.Bundled;
    public DateTimeOffset? CheckedAt => null;
    public IgnisModelPoints? Find(ProviderKind provider, string? model) => null;
    public Task<IgnisPointsCheck> CheckAsync(bool install, CancellationToken cancellationToken) => Task.FromResult(new IgnisPointsCheck(Current.Version, null, false));
}

internal sealed class KindleFakeController(IgnisSettings settings) : IIgnisController
{
    internal static readonly Guid Chat = Guid.Parse("6f1c0e2a-1111-4c2b-9a51-0a9a3c1e7d01");
    public event Action<Guid>? Changed;
    public IgnisSettings Settings => settings;
    public IIgnisPoints Points { get; } = new KindleFakePoints();
    public KindleFakeLedger FakeLedger { get; } = new();
    public ISavingsLedger Ledger => FakeLedger;
    public IgnisChatStatus Current { get; set; } = IgnisChatStatus.Unknown(Chat, IgnisMode.WarmAndTidy, true) with { Provider = ProviderKind.Claude };
    public List<string> Calls { get; } = [];
    public void Set(IgnisChatStatus status) { Current = status; Changed?.Invoke(status.ThreadID); }
    public IgnisChatStatus Status(Guid thread) => Current with { ThreadID = thread };
    public void SetMode(Guid thread, IgnisMode mode) { Calls.Add("mode " + mode); Set(Current with { Mode = mode }); }
    public void SetAuto(Guid thread, bool auto) { Calls.Add("auto " + auto); Set(Current with { Auto = auto }); }
    public void KeepWarmNow(Guid thread) => Calls.Add("warm");
    public void TidyUpNow(Guid thread) => Calls.Add("tidy");
    public string Report(Guid thread) => "Ignis report · this chat";
    public void Attach(IIgnisRuntimePort port) { }
    public void Detach(Guid thread) { }
    public void Observe(Guid thread, IgnisTurnUsage usage) { }
    public void TurnFinished(Guid thread) { }
    public void UsageLimitHit(Guid thread, DateTimeOffset? resetsAt) { }
}
