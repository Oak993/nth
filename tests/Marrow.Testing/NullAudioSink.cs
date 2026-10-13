using Marrow.Core.Port.Voice.Seams;

namespace Marrow.Testing;

/// <summary>An audio sink that takes every block and every device event and does nothing with them (a capture under test that only has to open).</summary>
public sealed class NullAudioSink : IAudioCaptureSink
{
    public void OnAudio(ReadOnlySpan<float> interleavedSamples)
    {
    }

    public void OnDeviceEvent(CaptureDeviceEventKind kind)
    {
    }
}
