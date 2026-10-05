using Marrow.Core.Port.Voice;
using Marrow.Core.Port.Voice.Light;
using Marrow.Core.Port.Voice.Session;

namespace Marrow.Core.Tests.Port.Voice;

// VT-9, V-T61 (V-13.4, OD-0097 B1 to B3): "for away, dim, stay the target is 0 / 0.35 / 1 during Listening, Processing and Landing and 1 otherwise; default 1 changes nothing".
public sealed class VoiceLightTargetTests
{
    public static TheoryData<VoiceLightVoice, double> Levels() => new()
    {
        { VoiceLightVoice.Away, 0 },
        { VoiceLightVoice.Dim, 0.35 },
        { VoiceLightVoice.Stay, 1 },
    };

    [Theory]
    [MemberData(nameof(Levels))]
    [Trait("Feature", "VT-9")]
    [Trait("Spec", "V-T61")]
    [Trait("Decision", "ED-302")]
    public void A_setting_asks_for_its_level_while_the_voice_is_on(VoiceLightVoice setting, double level)
    {
        Assert.Equal(level, VoiceLightTarget.Level(setting));
        foreach (var state in new[] { VoiceSessionState.Listening, VoiceSessionState.Processing, VoiceSessionState.Landing })
            Assert.Equal(level, VoiceLightTarget.For(setting, state));
    }

    [Theory]
    [MemberData(nameof(Levels))]
    [Trait("Feature", "VT-9")]
    [Trait("Spec", "V-T61")]
    [Trait("Decision", "ED-302")]
    public void Every_other_state_asks_for_the_light_at_full_strength(VoiceLightVoice setting, double _)
    {
        foreach (var state in new[] { VoiceSessionState.Rest, VoiceSessionState.Hover, VoiceSessionState.Asking, VoiceSessionState.Blocked })
        {
            Assert.Equal(1, VoiceLightTarget.For(setting, state));
            Assert.False(VoiceLightTarget.VoiceIsOn(state));
        }
    }

    [Fact]
    [Trait("Feature", "VT-9")]
    [Trait("Spec", "V-T61")]
    [Trait("Decision", "ED-302")]
    public void The_default_setting_steps_aside_and_the_voice_states_are_exactly_three()
    {
        Assert.Equal(VoiceLightVoice.Away, VoiceOrbSettings.Default.LightVoice);
        var on = Enum.GetValues<VoiceSessionState>().Where(VoiceLightTarget.VoiceIsOn).ToArray();
        Assert.Equal([VoiceSessionState.Listening, VoiceSessionState.Processing, VoiceSessionState.Landing], on);
    }
}
