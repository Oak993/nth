// PORT-ONLY, owner-ruled addition (OD-0092; first-release voice typing, VT-16): the cloud errors of voice-SPEC V-17.6 and V-18.4.
namespace Marrow.Services.Port.Voice.Cloud;

/// <summary>
/// Which cloud note a failed transcription maps to (V-17.6: "cloud without a key, key refused (401), quota or rate (429), network
/// failure", and "engine failure"). The words of each note are not this unit's: they are the pick of D-VN, built by VT-17 (Q-V-7).
/// </summary>
public enum CloudNote
{
    /// <summary>No key is stored (V-17.6 "cloud without a key").</summary>
    KeyMissing,

    /// <summary>OpenAI answered 401 (V-17.6 "key refused").</summary>
    KeyRefused,

    /// <summary>OpenAI answered 429 (V-17.6 "quota or rate").</summary>
    QuotaOrRate,

    /// <summary>No answer: the connection failed or the 60 s timeout ran out (V-17.6 "network failure"; ED-311).</summary>
    NetworkFailure,

    /// <summary>Any other answer: 5xx, another status, an unreadable reply (V-17.6 "engine failure"; ED-311).</summary>
    EngineFailure,
}
