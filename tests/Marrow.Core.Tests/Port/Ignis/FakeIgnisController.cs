using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Ignis;

namespace Marrow.Core.Tests.Port.Ignis;

// Ignis U0 (OD-0134, ED-NEW-ignis-contract): a fake that proves the contract compiles for the units that implement it.
internal sealed class FakeIgnisController(IgnisSettings settings) : IIgnisController
{
    private readonly Dictionary<Guid, IIgnisRuntimePort> ports = [];
    public event Action<Guid>? Changed;
    public IgnisSettings Settings => settings;
    public IIgnisPoints Points { get; } = new FakeIgnisPoints();
    public ISavingsLedger Ledger { get; } = new FakeIgnisLedger();
    public IgnisChatStatus Status(Guid thread)
    {
        var own = settings.ForChat(thread);
        return IgnisChatStatus.Unknown(thread, own.Mode, own.Auto);
    }
    public void SetMode(Guid thread, IgnisMode mode) { settings.SetChat(thread, settings.ForChat(thread) with { Mode = mode }); Changed?.Invoke(thread); }
    public void SetAuto(Guid thread, bool auto) { settings.SetChat(thread, settings.ForChat(thread) with { Auto = auto }); Changed?.Invoke(thread); }
    public void KeepWarmNow(Guid thread) { if (ports.TryGetValue(thread, out var port) && port.IsBetweenTasks) port.SendHiddenPing(); }
    public void TidyUpNow(Guid thread) { if (ports.TryGetValue(thread, out var port)) port.Tidy(settings.Value.KeepTurns); }
    public string Report(Guid thread) => "";
    public void Attach(IIgnisRuntimePort port) => ports[port.ThreadID] = port;
    public void Detach(Guid thread) => ports.Remove(thread);
    public void Observe(Guid thread, IgnisTurnUsage usage) { }
    public void TurnFinished(Guid thread) { }
    public void UsageLimitHit(Guid thread, DateTimeOffset? resetsAt) { }
}
