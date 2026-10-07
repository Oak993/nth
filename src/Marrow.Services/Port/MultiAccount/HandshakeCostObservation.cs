namespace Marrow.Services.Port.MultiAccount;

// PORT-ONLY: OD-0114; completion is explicit; an abandoned/failed turn never supplies a cost sample.
public sealed class HandshakeCostObservation(Action<bool> finish) : IDisposable
{
    private int ended;
    public void Complete() { if (Interlocked.Exchange(ref ended, 1) == 0) finish(true); }
    public void Dispose() { if (Interlocked.Exchange(ref ended, 1) == 0) finish(false); }
}
