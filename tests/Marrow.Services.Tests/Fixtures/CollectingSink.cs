using Marrow.Core.Port.Voice.Seams;

namespace Marrow.Services.Tests.Fixtures;

// VT-5 test fixture: an IAudioCaptureSink that remembers what it was given and in which order. Calls are expected on the main loop (a session's sink is main-actor only).
public sealed class CollectingSink : IAudioCaptureSink
{
    public List<float> Samples { get; } = [];

    /// <summary>One entry per call: "audio:N" (N samples) or "event:Kind".</summary>
    public List<string> Journal { get; } = [];

    public List<CaptureDeviceEventKind> Events { get; } = [];

    public int AudioCalls { get; private set; }

    /// <summary>Runs inside every OnDeviceEvent, like a session that stops its capture when the device goes.</summary>
    public Action<CaptureDeviceEventKind>? OnEvent { get; set; }

    public void OnAudio(ReadOnlySpan<float> interleavedSamples)
    {
        AudioCalls += 1;
        Samples.AddRange(interleavedSamples);
        Journal.Add("audio:" + interleavedSamples.Length);
    }

    public void OnDeviceEvent(CaptureDeviceEventKind kind)
    {
        Events.Add(kind);
        Journal.Add("event:" + kind);
        OnEvent?.Invoke(kind);
    }
}
