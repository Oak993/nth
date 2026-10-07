using Marrow.Core.Port.Voice.Seams;

namespace Marrow.Services.Tests.Fixtures;

// VT-11 test fixture: an IAudioCaptureSink that also takes the processing thread's block tap (OnAudioBlock) and remembers which thread each call came on, the format it was given and the
// samples. The main executor's delivery (OnAudio) is counted apart.
public sealed class TappedSink : IAudioCaptureSink
{
    private readonly object gate = new();

    public List<(int Thread, CaptureFormat Format, float[] Samples)> Blocks { get; } = [];

    public List<int> DeliveryThreads { get; } = [];

    public List<float> Delivered { get; } = [];

    public void OnAudioBlock(CaptureFormat format, ReadOnlySpan<float> interleavedSamples)
    {
        lock (gate) Blocks.Add((Environment.CurrentManagedThreadId, format, interleavedSamples.ToArray()));
    }

    public void OnAudio(ReadOnlySpan<float> interleavedSamples)
    {
        lock (gate)
        {
            DeliveryThreads.Add(Environment.CurrentManagedThreadId);
            Delivered.AddRange(interleavedSamples);
        }
    }

    public void OnDeviceEvent(CaptureDeviceEventKind kind)
    {
    }
}
