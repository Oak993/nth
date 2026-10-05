// PORT-ONLY, owner-ruled addition (OD-0092; first-release voice typing, VT-14): the hook where VT-15's self-test plugs into "Use recommended" (voice-SPEC V-10.6, V-10.3).
using Marrow.Core.Port.Voice;

namespace Marrow.App.Port.Voice;

/// <summary>
/// V-10.6: "Use recommended installs the candidate (if needed), runs the self-test, and selects the result (stepping down per V-10.3 and updating the card)". The self-test (the warm-up
/// load, the timed 10 s clip, the ladder) is VT-15's <c>VoicePreparation.InstallAndTimeAsync</c>; this is the seam the page calls it through. The candidate is installed when this is called.
/// </summary>
internal interface ISpeechSelfTest
{
    /// <summary>Times the installed candidate and decides the model to keep; null when no timing was possible (the candidate is then selected as it is).</summary>
    Task<SelfTestOutcome?> RunAsync(string candidateId, HardwareProfile profile, CancellationToken cancellationToken);
}
