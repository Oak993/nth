namespace Marrow.Services.Tests.Fixtures;

// PORT-ONLY: Tactus, PC control (ED-NEW-pcctl-18). A step's wait mark, as a test sees it: the reasons it was marked with,
// and how many marks were let go.
public sealed class PcWaitMarks : IDisposable
{
    private int released;

    public List<string> Reasons { get; } = [];

    public int Released => Volatile.Read(ref released);

    public IDisposable Mark(string reason)
    {
        lock (Reasons) Reasons.Add(reason);
        return this;
    }

    public void Dispose() => Interlocked.Increment(ref released);
}
