using Marrow.Services.Port.Skills.Import;

namespace Marrow.Services.Tests.Port.Skills.Import;

/// <summary>A folder watch that listens to nothing: it records what it was asked to listen to and lets a test say "something changed" (SK-18).</summary>
public sealed class RecordingWatch : IFolderWatch
{
    private int disposed;

    public List<IReadOnlyList<WatchTarget>> Started { get; } = [];
    public int Disposed => Volatile.Read(ref disposed);
    public Action? Changed { get; private set; }

    public IDisposable Watch(IReadOnlyList<WatchTarget> targets, Action changed)
    {
        lock (Started) Started.Add(targets);
        Changed = changed;
        return new EndingDisposable(() => Interlocked.Increment(ref disposed));
    }
}
