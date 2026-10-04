using System.Collections.Concurrent;

namespace Marrow.App.Tests.Fixtures;

/// <summary>
/// The system clock, counting which managed threads read it. Installed as SwiftClock's provider, every read of
/// SwiftClock.Now names its thread: a render-server sample of the composer's dot field (DotFieldCompositionFrame.Sample
/// reads the clock its field captured) shows on the render thread, and any UI-thread work of the field would show on the
/// UI thread (ComposerWorkingDotsNativeTests).
/// </summary>
public sealed class ThreadRecordingTimeProvider : TimeProvider
{
    private ConcurrentDictionary<int, int> reads = new();

    public override long GetTimestamp()
    {
        reads.AddOrUpdate(Environment.CurrentManagedThreadId, 1, (_, count) => count + 1);
        return TimeProvider.System.GetTimestamp();
    }

    public override long TimestampFrequency => TimeProvider.System.TimestampFrequency;

    /// <summary>Forgets every read so far.</summary>
    public void Reset() => Interlocked.Exchange(ref reads, new ConcurrentDictionary<int, int>());

    /// <summary>The reads since the last reset, by managed thread id.</summary>
    public IReadOnlyDictionary<int, int> Reads => new Dictionary<int, int>(Volatile.Read(ref reads));
}
