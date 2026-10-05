// PORT-ONLY, owner-ruled addition (OD-0092, OD-0096; first-release voice typing, VT-15, voice-SPEC V-10.7): how the install and speed test ended.
namespace Marrow.App.Port.Voice;

/// <summary>Prepared: the result is stored. AwaitingConsent: nothing was done (D3: no consent yet). Failed: a download, a verification or the engine failed and nothing was stored.</summary>
public enum VoicePreparationStatus { Prepared, AwaitingConsent, Failed }
