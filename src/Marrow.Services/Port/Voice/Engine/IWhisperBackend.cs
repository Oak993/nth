// PORT-ONLY, owner-ruled addition (OD-0091, OD-0092, OD-0099 Q-V-9 C; first-release voice typing, VT-6): the seam under WhisperNetEngine (V-2.4, V-7.1).
namespace Marrow.Services.Port.Voice.Engine;

/// <summary>
/// The Whisper.net side of the engine. Production: <see cref="WhisperNetBackend"/>. Tests: a scripted backend. The engine's own logic (words, decode settings, one model at
/// a time, residency, cancellation) sits above this seam so it is tested without a native library.
/// </summary>
public interface IWhisperBackend
{
    Task<IWhisperModel> LoadAsync(BackendLoad load, CancellationToken cancellationToken);
}
