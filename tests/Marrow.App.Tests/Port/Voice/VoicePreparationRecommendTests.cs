using Marrow.App.Port.Voice;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Voice;
using Marrow.Services.Port.Voice.Probe;

namespace Marrow.App.Tests.Port.Voice;

// VT-15 (V-10.2, V-7.14): Recommend gives the recommender's pick for the probe's profile and the resolved language of the settings.
public sealed class VoicePreparationRecommendTests
{
    private static readonly Dictionary<string, double> None = [];

    public static TheoryData<string, string?, string, RecommendationBasis> Cases => new()
    {
        { "desktop", null, "large-v3-turbo-q8_0", RecommendationBasis.Gpu },
        { "twogb", null, "large-v3-turbo-q5_0", RecommendationBasis.Gpu },
        { "laptop", null, "large-v3-turbo-q5_0", RecommendationBasis.Cpu },
        { "laptop", "en", "small.en-q8_0", RecommendationBasis.Cpu },
        { "fourcore", null, "small-q5_1", RecommendationBasis.Cpu },
        { "fourcore", "en", "base.en", RecommendationBasis.Cpu },
        { "fourcore", "hi", "small-q5_1", RecommendationBasis.Cpu },
    };

    private static ScriptedMachine Machine(string name) => name switch
    {
        "desktop" => ScriptedMachines.OwnersDesktop,
        "twogb" => ScriptedMachines.TwoGbVramCard,
        "laptop" => ScriptedMachines.CpuOnlyLaptop,
        _ => ScriptedMachines.FourCoreMachine,
    };

    [Theory]
    [MemberData(nameof(Cases))]
    [Trait("Feature", "VT-15")]
    [Trait("Decision", "ED-289")]
    public async Task Recommend_follows_the_profile_and_the_language(string machine, string? language, string model, RecommendationBasis basis)
    {
        var rig = new VoicePrepRig(Machine(machine), None, language);
        var profile = await rig.Preparation.ProbeAsync(null, CancellationToken.None);
        Assert.Equal(new Recommendation(model, basis), rig.Preparation.Recommend(profile));
    }

    [Fact]
    [Trait("Feature", "VT-15")]
    [Trait("Decision", "ED-289")]
    public async Task With_no_language_picked_the_windows_default_of_the_customization_row_decides()
    {
        var rig = new VoicePrepRig(ScriptedMachines.CpuOnlyLaptop, None);
        rig.Settings.Orb = VoiceOrbSettings.Default with { LangMode = VoiceLangMode.Windows };
        rig.Settings.UiLanguage = "en-US";
        var profile = await rig.Preparation.ProbeAsync(null, CancellationToken.None);
        Assert.Equal("small.en-q8_0", rig.Preparation.Recommend(profile).ModelId);
    }
}
