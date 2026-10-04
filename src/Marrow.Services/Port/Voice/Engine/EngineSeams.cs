// PORT-ONLY, owner-ruled addition (OD-0091, OD-0092, OD-0099 Q-V-9 C; first-release voice typing, VT-6): where the engine's seams are bound (V-2.4, V-2.5).
using Marrow.Core.Port;

namespace Marrow.Services.Port.Voice.Engine;

/// <summary>The ambient seams of the Whisper.net engine and the VAD. Every default fails closed; the app's start-up composition binds the production ones.</summary>
public static class EngineSeams
{
    public static Ambient<IWhisperBackend> Backend { get; } = new(new FailClosedWhisperBackend());

    public static Ambient<IVadNative> VadNative { get; } = new(new FailClosedVadNative());
}
