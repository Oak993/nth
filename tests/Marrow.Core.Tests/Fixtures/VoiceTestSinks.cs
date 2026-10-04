using Marrow.Core.Port.Voice.Seams;

namespace Marrow.Core.Tests.Fixtures;

// VT-3 test fixture: a capture sink that drops everything, and one that keeps what it is given.
public sealed class NullCaptureSink : IAudioCaptureSink
{
    public void OnAudio(ReadOnlySpan<float> interleavedSamples) { }

    public void OnDeviceEvent(CaptureDeviceEventKind kind) { }
}
