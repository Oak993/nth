using Marrow.App.Port.Voice;
using Marrow.Core.Port.Voice;
using Marrow.Services.Port.Voice.Probe;
using Marrow.Testing;

namespace Marrow.App.Tests.Fixtures;

/// <summary>VT-15: a voice preparation over scripted seams: a scripted machine, the model source double, an engine that times a decode by model on the manual clock, in-memory settings.</summary>
public sealed class VoicePrepRig
{
    public const string App = "1.7.0+21";
    public const string Runtime = "1.9.0";

    public VoicePrepRig(ScriptedMachine machine, IReadOnlyDictionary<string, double> seconds, string? language = null)
    {
        Clock = new ManualTimeProvider();
        Models = new VoicePrepModelSource();
        Engine = new VoicePrepEngine(Clock, seconds);
        Settings = new VoicePrepSettingsFake();
        if (language is not null) Settings.Typing = VoiceTypingSettings.Default with { Lang = language };
        Items = new VoicePrepItems();
        Preparation = new VoicePreparation(new ScriptedProbe(machine), Models, () => Engine, Settings, () => new float[160320], App, Runtime, Clock);
    }

    public ManualTimeProvider Clock { get; }

    public VoicePrepModelSource Models { get; }

    public VoicePrepEngine Engine { get; }

    public VoicePrepSettingsFake Settings { get; }

    public VoicePrepItems Items { get; }

    public VoicePreparation Preparation { get; }

    /// <summary>Probes, takes the recommendation and installs and times it (consent given first).</summary>
    public async Task<(HardwareProfile Profile, Recommendation Recommendation, VoicePreparationResult Result)> FirstRunAsync()
    {
        Preparation.GrantConsent();
        var profile = await Preparation.ProbeAsync(Items, CancellationToken.None);
        var recommendation = Preparation.Recommend(profile);
        var result = await Preparation.InstallAndTimeAsync(recommendation.ModelId, profile, Items, CancellationToken.None);
        return (profile, recommendation, result);
    }
}
