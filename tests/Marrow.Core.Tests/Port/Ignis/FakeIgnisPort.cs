using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Ignis;

namespace Marrow.Core.Tests.Port.Ignis;

// Ignis U0 (OD-0134, ED-NEW-ignis-contract): a fake that proves the contract compiles for the units that implement it.
internal sealed class FakeIgnisPort : IIgnisRuntimePort
{
    public int Pings;
    public Guid ThreadID { get; } = Guid.NewGuid();
    public ProviderKind Provider => ProviderKind.Claude;
    public string? Model => "opus";
    public bool IsBetweenTasks => true;
    public DateTimeOffset? LastRequestAt => null;
    public bool CanKeepWarmSilently => false;
    public bool SendHiddenPing() { Pings++; return true; }
    public bool KeepWarmSilently() => false;
    public bool Tidy(int keepTurns) => true;
}
