// PORT-ONLY, owner-ruled addition (OD-0091, OD-0092, OD-0099 Q-V-9 C; first-release voice typing, VT-6): a loaded model (V-7.1).
namespace Marrow.Services.Port.Voice.Engine;

/// <summary>A model loaded by a backend. Disposing it frees the native model.</summary>
public interface IWhisperModel : IDisposable
{
    /// <summary>The runtime that loaded it.</summary>
    RuntimeKind Runtime { get; }

    Task<IReadOnlyList<BackendSegment>> DecodeAsync(ReadOnlyMemory<float> audio, BackendDecode decode, CancellationToken cancellationToken);
}
