// PORT-ONLY, owner-ruled addition (OD-0091; first-release voice typing, VT-5): the capture side of voice-SPEC V-2.2 (capture thread -> ring -> processing thread -> main executor).
using System.Buffers;
using System.Collections.Concurrent;
using Marrow.Core.Port;
using Marrow.Core.Port.Voice.Seams;

namespace Marrow.Services.Port.Voice.Capture;

/// <summary>
/// Moves audio from a device to a session's sink in three steps (V-2.2, V-2.3). The capture thread calls <see cref="OnBuffer"/>: it converts the buffer to float and writes
/// it into a lock-free ring of 2 s, nothing else (no allocation, no lock). The processing thread (or a test, by hand) calls <see cref="Pump"/> every 10 ms: it reads the ring
/// into a batch, puts the device events between the batches they belong to, and posts one delivery to the main executor. The delivery hands each batch to
/// <see cref="IAudioCaptureSink.OnAudio"/> and each event to <see cref="IAudioCaptureSink.OnDeviceEvent"/>, in the order they happened. <see cref="Flush"/> does the whole
/// chain at once on the calling (main) thread, so the last audio reaches a sink that is still listening when the capture stops (V-7.6). <see cref="Dispose"/> discards
/// whatever has not been delivered.
/// </summary>
public sealed class CapturePipeline : IDisposable
{
    /// <summary>V-2.2 item 1: "2 s at the device rate".</summary>
    public const int RingSeconds = 2;

    /// <summary>V-2.2 item 2: the processing thread drains the ring every 10 ms.</summary>
    public static readonly TimeSpan PumpInterval = TimeSpan.FromMilliseconds(10);

    private readonly DeviceFormat format;
    private readonly CaptureFormat captureFormat;
    private readonly IAudioCaptureSink sink;
    private readonly SampleRing ring;
    private readonly float[] scratch;
    private readonly ConcurrentQueue<(CaptureDeviceEventKind Kind, long Position)> events = new();
    private readonly ConcurrentQueue<Batch> outbox = new();
    private readonly Lock pumpGate = new();
    private readonly ManualResetEventSlim wake = new(false);
    private readonly Thread? thread;
    private long consumed;
    private long dropped;
    private int scheduled;
    private volatile bool disposed;

    public CapturePipeline(DeviceFormat format, IAudioCaptureSink sink, bool runThread = true)
    {
        this.format = format;
        captureFormat = format.ToCaptureFormat();
        this.sink = sink;
        ring = new SampleRing(format.SampleRate * format.Channels * RingSeconds);
        scratch = new float[Math.Max(1, format.SampleRate / 2) * format.Channels]; // half a second of whole frames: a device buffer is 100 ms or less
        if (!runThread) return;
        thread = new Thread(Run) { IsBackground = true, Name = "Nth voice processing" };
        thread.Start();
    }

    /// <summary>Samples refused because the ring was full (the processing thread fell 2 s behind).</summary>
    public long DroppedSamples => Interlocked.Read(ref dropped);

    public bool ThreadRunning => thread is { IsAlive: true };

    /// <summary>Capture thread. Whole frames only: a trailing partial frame is ignored.</summary>
    public void OnBuffer(ReadOnlySpan<byte> buffer, bool silent)
    {
        if (disposed) return;
        var frameBytes = format.BytesPerFrame;
        var usable = buffer.Length - buffer.Length % frameBytes;
        var chunkBytes = scratch.Length / format.Channels * frameBytes;
        for (var at = 0; at < usable; at += chunkBytes)
        {
            var piece = buffer.Slice(at, Math.Min(chunkBytes, usable - at));
            var count = SampleConverter.ToFloat(piece, format.Encoding, silent, scratch);
            if (!ring.TryWrite(scratch.AsSpan(0, count))) Interlocked.Add(ref dropped, count);
        }
    }

    /// <summary>Any thread. The event is ordered after the audio written so far.</summary>
    public void RaiseEvent(CaptureDeviceEventKind kind)
    {
        if (disposed) return;
        events.Enqueue((kind, ring.TotalWritten));
        wake.Set();
    }

    /// <summary>The processing step (the processing thread every 10 ms, or a test): ring and events into the outbox, and one delivery posted to the main executor.</summary>
    public void Pump()
    {
        lock (pumpGate)
        {
            if (disposed) return;
            while (events.TryPeek(out var next))
            {
                Take(next.Position);
                if (events.TryDequeue(out var taken)) outbox.Enqueue(new Batch(null, 0, taken.Kind));
            }
            Take(ring.TotalWritten);
        }
        if (outbox.IsEmpty || Interlocked.CompareExchange(ref scheduled, 1, 0) != 0) return;
        MainActor.Post(Deliver);
    }

    /// <summary>Main thread: everything captured so far and every event, delivered now, in order.</summary>
    public void Flush()
    {
        Pump();
        DeliverAll();
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        wake.Set();
        if (thread is not null && thread != Thread.CurrentThread) thread.Join();
        lock (pumpGate)
        {
            while (outbox.TryDequeue(out var batch)) Release(batch);
            ring.Clear();
        }
    }

    private void Take(long upTo)
    {
        var count = (int)(upTo - consumed);
        if (count <= 0) return;
        var samples = ArrayPool<float>.Shared.Rent(count);
        var read = ring.Read(samples.AsSpan(0, count));
        consumed += read;
        if (read == 0)
        {
            ArrayPool<float>.Shared.Return(samples);
            return;
        }
        Tap(samples.AsSpan(0, read));
        outbox.Enqueue(new Batch(samples, read, null));
    }

    // V-2.2 items 2 and 4 (VT-11): the sink's tap gets the batch here, on the thread that drained the ring (the processing thread, or the caller of Flush), before the batch is posted
    // to the main executor. A tap that throws loses nothing: the batch still goes on to the sink.
    private void Tap(ReadOnlySpan<float> samples)
    {
        try
        {
            sink.OnAudioBlock(captureFormat, samples);
        }
        catch (Exception)
        {
            // The level is only for the orb; the audio itself is unaffected.
        }
    }

    private void Deliver()
    {
        Interlocked.Exchange(ref scheduled, 0);
        DeliverAll();
    }

    private void DeliverAll()
    {
        while (outbox.TryDequeue(out var batch))
        {
            if (disposed)
            {
                Release(batch);
                continue;
            }
            try
            {
                if (batch.Samples is { } samples) sink.OnAudio(samples.AsSpan(0, batch.Count));
                else if (batch.Event is { } kind) sink.OnDeviceEvent(kind);
            }
            finally
            {
                Release(batch);
            }
        }
    }

    private static void Release(Batch batch)
    {
        if (batch.Samples is { } samples) ArrayPool<float>.Shared.Return(samples, clearArray: true); // V-4.7: the audio is zeroed when it is returned
    }

    private void Run()
    {
        while (!disposed)
        {
            wake.Wait(PumpInterval);
            wake.Reset();
            try
            {
                Pump();
            }
            catch (Exception)
            {
                // The processing thread never takes the app down: a failed step loses that step's batch (a stopped main executor at shutdown is the usual cause).
            }
        }
    }

    private readonly record struct Batch(float[]? Samples, int Count, CaptureDeviceEventKind? Event);
}
