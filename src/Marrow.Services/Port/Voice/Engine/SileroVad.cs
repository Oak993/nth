// PORT-ONLY, owner-ruled addition (OD-0091, OD-0092, OD-0099 Q-V-9 C; first-release voice typing, VT-6): Silero VAD through whisper.cpp (voice-SPEC V-6.1, V-6.2).
using Marrow.Core.Port.Voice.Seams;

namespace Marrow.Services.Port.Voice.Engine;

/// <summary>
/// <see cref="IVoiceActivityDetector"/> over whisper.cpp's VAD (<see cref="IVadNative"/>). Whisper.net 1.9.1 hands out speech segments only, so the per-frame probability comes
/// from the five native exports (V-6.1). <c>whisper_vad_detect_speech</c> starts Silero's state afresh on every call (UNVERIFIED until the voice-evidence run on Windows), so a
/// frame is answered from one call over the last <c>windowFrames</c> frames (32 ms each) and the probability of the last of them is the frame's. The window is a rolling
/// ENGINEERING DECISION; <see cref="Reset"/> empties it for a new session.
/// </summary>
public sealed class SileroVad : IVoiceActivityDetector, IDisposable
{
    /// <summary>The window in frames: eight frames, 256 ms, enough for the model to settle and cheap to repeat every 32 ms (ENGINEERING DECISION).</summary>
    public const int DefaultWindowFrames = 8;

    private const int Frame = 512;

    private readonly IVadNative native;
    private readonly float[] window;
    private int frames;
    private bool disposed;

    public SileroVad(IVadNative native, int windowFrames = DefaultWindowFrames)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(windowFrames, 1);
        this.native = native;
        window = new float[windowFrames * Frame];
    }

    public int FrameSamples => Frame;

    /// <summary>Opens whisper.cpp's VAD on <paramref name="modelPath"/> through the whisper.dll at <paramref name="whisperLibraryPath"/> (Windows).</summary>
    public static SileroVad Open(string modelPath, string whisperLibraryPath, bool useGpu, int threads, int windowFrames = DefaultWindowFrames) =>
        new(WhisperVadNative.Open(modelPath, whisperLibraryPath, useGpu, threads), windowFrames);

    public float Probability(ReadOnlySpan<float> frame)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (frame.Length != Frame) throw new ArgumentException("A VAD frame is " + Frame + " samples (V-6.1).", nameof(frame));
        var capacity = window.Length / Frame;
        if (frames == capacity)
        {
            Array.Copy(window, Frame, window, 0, (capacity - 1) * Frame);
            frames -= 1;
        }
        frame.CopyTo(window.AsSpan(frames * Frame, Frame));
        frames += 1;
        var probabilities = native.Detect(window.AsSpan(0, frames * Frame));
        if (probabilities.Length == 0) throw new InvalidOperationException("The VAD returned no probabilities.");
        return probabilities[^1];
    }

    public void Reset() => frames = 0;

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        native.Dispose();
    }
}
