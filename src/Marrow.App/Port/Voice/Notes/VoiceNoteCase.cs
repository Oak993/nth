// PORT-ONLY, owner-ruled addition (OD-0106 Notes A Plain, OD-0099 Q-V-7 A; first-release voice typing, VT-17): the twelve cases a voice note can name (the locked Microphone blocked note and the eleven of set A).
namespace Marrow.App.Port.Voice.Notes;

/// <summary>
/// Blocked is the locked note of V-14.6; the other eleven are D-VN's set A, in the page's order (V-17.6: no microphone, unplugged, busy, model still downloading,
/// failed to load, no AVX2, engine failure, cloud without a key, key refused, out of quota, no network).
/// </summary>
public enum VoiceNoteCase
{
    Blocked,
    NoMicrophone,
    MicrophoneUnplugged,
    MicrophoneBusy,
    ModelStillDownloading,
    ModelFailedToLoad,
    NoAvx2,
    EngineFailure,
    CloudNoKey,
    CloudKeyRefused,
    CloudOutOfQuota,
    CloudNoNetwork,
}
