using Marrow.Services.Port.PcControl;

namespace Marrow.Services.Tests.Fixtures;

// PORT-ONLY: Tactus, PC control (ED-NEW-pcctl-18). The hook's hold, played by a test: it records swallowing on and off
// and lets a test move the counts the borrow's re-verify and catch-up read.
public sealed class PcFakeInputHold : IPcInputHold
{
    private long delivered;
    private long swallowed;

    public bool Swallowing { get; private set; }

    public bool KeysOnly { get; private set; }

    public bool AbortRequested { get; set; }

    public long Delivered => Interlocked.Read(ref delivered);

    public long SwallowedCount => Interlocked.Read(ref swallowed);

    public List<string> Log { get; } = [];

    /// <summary>Runs right after swallowing goes on (a test's user acts in the gap between the gate and the hold).</summary>
    public Action? OnBegin { get; set; }

    public void BeginSwallow(bool keysOnly = false)
    {
        lock (Log) Log.Add(keysOnly ? "keys" : "begin");
        Swallowing = true;
        KeysOnly = keysOnly;
        OnBegin?.Invoke();
    }

    public void EndSwallow()
    {
        lock (Log) Log.Add("end");
        Swallowing = false;
        KeysOnly = false;
        AbortRequested = false;
    }

    public void Deliver() => Interlocked.Increment(ref delivered);

    public void CountSwallowed() => Interlocked.Increment(ref swallowed);
}
