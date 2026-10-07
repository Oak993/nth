// PORT-ONLY, owner-ruled addition (OD-0099 Q-V-15; VT-16): cloud text arrives during Processing (voice-SPEC V-18.1).
using Marrow.Core.Port.Voice.Transcript;

namespace Marrow.Core.Port.Voice.Seams;

/// <summary>A recogniser that publishes word operations while its final request is in flight.</summary>
public interface IProcessingSpeechEngine : ISpeechEngine
{
    event Action<IReadOnlyList<WordOp>>? OpsProduced;

    /// <summary>Checks the protected key before capture starts, including when a previous model load is cached. No network request.</summary>
    void ValidateReady();
}
