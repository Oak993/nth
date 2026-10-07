// PORT-ONLY: VT-15, V-10.6. The settings page's explicit Use recommended action consents to installing and timing the selected candidate.
using Marrow.Core.Port.Voice;

namespace Marrow.App.Port.Voice;

internal sealed class PreparationSelfTest(VoicePreparation preparation) : ISpeechSelfTest
{
    public async Task<SelfTestOutcome?> RunAsync(string candidateId, HardwareProfile profile, CancellationToken cancellationToken)
    {
        preparation.GrantConsent();
        var result = await preparation.InstallAndTimeAsync(candidateId, profile, null, cancellationToken);
        return result.Record is { } record ? new SelfTestOutcome(record.Model, record.Rtf, record.LiveWords) : null;
    }
}
