// PORT-ONLY, owner-ruled addition (OD-0091; first-release voice typing, VT-5): the single-producer single-consumer ring of voice-SPEC V-2.2 item 1.
namespace Marrow.Services.Port.Voice.Capture;

/// <summary>
/// A lock-free ring of float samples for one writer thread and one reader thread. A write that does not fit is refused whole. Positions are monotonic counts of
/// samples (never reset), so <see cref="TotalWritten"/> orders a device event against the audio written before it.
/// </summary>
public sealed class SampleRing
{
    private readonly float[] data;
    private long head; // next sample to read; moved only by the reader
    private long tail; // next sample to write; moved only by the writer

    public SampleRing(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(capacity, 0);
        data = new float[capacity];
    }

    public int Capacity => data.Length;

    /// <summary>Samples written and not yet read.</summary>
    public int Count => (int)(Volatile.Read(ref tail) - Volatile.Read(ref head));

    /// <summary>Samples ever written (accepted writes only).</summary>
    public long TotalWritten => Volatile.Read(ref tail);

    /// <summary>Writer thread: appends all of <paramref name="samples"/>, or none (false) when they do not fit. No allocation.</summary>
    public bool TryWrite(ReadOnlySpan<float> samples)
    {
        var write = Volatile.Read(ref tail);
        var read = Volatile.Read(ref head);
        if (data.Length - (write - read) < samples.Length) return false;
        var index = (int)(write % data.Length);
        var first = Math.Min(samples.Length, data.Length - index);
        samples[..first].CopyTo(data.AsSpan(index));
        samples[first..].CopyTo(data);
        Volatile.Write(ref tail, write + samples.Length);
        return true;
    }

    /// <summary>Reader thread: moves up to <paramref name="destination"/>.Length samples out, oldest first; returns how many.</summary>
    public int Read(Span<float> destination)
    {
        var read = Volatile.Read(ref head);
        var available = Volatile.Read(ref tail) - read;
        var count = (int)Math.Min(destination.Length, available);
        if (count == 0) return 0;
        var index = (int)(read % data.Length);
        var first = Math.Min(count, data.Length - index);
        data.AsSpan(index, first).CopyTo(destination);
        data.AsSpan(0, count - first).CopyTo(destination[first..]);
        Volatile.Write(ref head, read + count);
        return count;
    }

    /// <summary>Zeroes the storage and empties the ring (V-4.7). Only when neither thread is using it.</summary>
    public void Clear()
    {
        Array.Clear(data);
        Volatile.Write(ref head, Volatile.Read(ref tail));
    }
}
