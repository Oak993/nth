// PORT-ONLY, owner-ruled addition (OD-0091; first-release voice typing, VT-3): the capture seam (voice-SPEC V-2.4, V-4).
namespace Marrow.Core.Port.Voice.Seams;

/// <summary>The format the capture runs in: the default endpoint's own mix format (V-4.1), never a converted one.</summary>
public readonly record struct CaptureFormat(int SampleRate, int Channels)
{
    /// <summary>The format the recognition path wants: 16 kHz mono (V-5.2).</summary>
    public static CaptureFormat Speech16kMono { get; } = new(16000, 1);

    /// <summary>True for 16 kHz mono, which needs neither a downmix nor a resampler.</summary>
    public bool IsSpeech16kMono => SampleRate == 16000 && Channels == 1;
}

/// <summary>What happened to the device while capturing (V-4.4).</summary>
public enum CaptureDeviceEventKind
{
    /// <summary>The capture endpoint was removed.</summary>
    DeviceRemoved,

    /// <summary>The default capture endpoint changed.</summary>
    DefaultDeviceChanged,

    /// <summary>The endpoint's mix format changed.</summary>
    FormatChanged,

    /// <summary>Another app holds the device exclusively (AUDCLNT_E_DEVICE_IN_USE).</summary>
    InUse,
}

/// <summary>
/// Where a capture delivers. <see cref="OnAudio"/> gets interleaved float samples in the started format (V-4.2: conversion from
/// PCM16/24/32 to float is the capture adapter's job). A voice session calls nothing back from inside these methods, and a
/// capture delivers on the main executor when the sink is a <c>VoiceSession</c> (the production pipeline of VT-5 posts its batches).
/// </summary>
public interface IAudioCaptureSink
{
    void OnAudio(ReadOnlySpan<float> interleavedSamples);

    void OnDeviceEvent(CaptureDeviceEventKind kind);
}

/// <summary>
/// The microphone (V-2.4). Production is <c>WasapiCapture</c> (NAudio WASAPI, VT-5); tests bind <see cref="ScriptedCapture"/>.
/// <see cref="Start"/> opens the default endpoint in its mix format and returns that format; <see cref="Stop"/> closes it.
/// </summary>
public interface IAudioCapture
{
    CaptureFormat Start(IAudioCaptureSink sink);

    void Stop();
}
