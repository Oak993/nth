// PORT-ONLY: Tactus (ED-NEW-pcctl-36). The dodge's window events, raised by the test.
using Marrow.App.Port.PcControl.Overlay;

namespace Marrow.App.Tests.Fixtures;

public sealed class PcFakeDodgeEvents : IDodgeEventSource
{
    public event Action<DodgeEventKind>? Raised;

    public bool Disposed { get; private set; }

    public void Raise(DodgeEventKind kind) => Raised?.Invoke(kind);

    public void Dispose() => Disposed = true;
}
