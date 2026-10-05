// PORT-ONLY, owner-ruled addition (OD-0092, OD-0096, OD-0098; first-release voice typing, VT-15, voice-SPEC V-10.3, V-10.7): the voice first-run preparation service.
using Marrow.App.Views.Composer;
using Marrow.Core.Port.Voice;
using Marrow.Core.Port.Voice.Seams;
using Marrow.Services.Port.Voice.Probe;

namespace Marrow.App.Port.Voice;

/// <summary>The service the Getting ready screen, Welcome's voice question and the orb call (V-10.7).</summary>
public sealed class VoicePreparation
{
    public const string ProbeId = "voice.probe";
    public const string DownloadId = "voice.download";
    public const string VerifyId = "voice.verify";
    public const string SelfTestId = "voice.selftest";

    public VoicePreparation(IHardwareProbe probe, IVoiceModelSource models, Func<ISpeechEngine> engine, IVoicePreparationSettings settings,
        Func<ReadOnlyMemory<float>> clip, string app, string runtimeVersion, TimeProvider? clock = null, Func<string?>? currentKey = null)
    {
    }

    public bool HasConsent => throw new NotImplementedException();

    public void GrantConsent() => throw new NotImplementedException();

    public Task<HardwareProfile> ProbeAsync(IProgress<VoicePrepItem>? progress, CancellationToken cancellationToken) => throw new NotImplementedException();

    public Recommendation Recommend(HardwareProfile profile) => throw new NotImplementedException();

    public Task<VoicePreparationResult> InstallAndTimeAsync(string id, HardwareProfile profile, IProgress<VoicePrepItem>? progress, CancellationToken cancellationToken) =>
        throw new NotImplementedException();

    public Task<VoicePreparationResult> PrepareAsync(IProgress<VoicePrepItem>? progress, CancellationToken cancellationToken) => throw new NotImplementedException();

    public bool IsPrepared(out VoiceHardwareRecord? record) => throw new NotImplementedException();

    public static bool LiveWordsOf(VoiceHardwareRecord? record) => throw new NotImplementedException();

    public IDisposable RegisterStep() => throw new NotImplementedException();
}
