using Marrow.App.Port.Voice;

namespace Marrow.App.Tests.Port.Voice;

// VT-10, V-T87 (V-2.6): the flag is off until phase V7 switches voice typing on; a test turns it on for its own flow.
public sealed class VoiceFeatureTests
{
    [Fact] // V-T87
    [Trait("Decision", "ED-292")]
    public void The_feature_is_off_by_default()
    {
        Assert.False(VoiceFeature.Enabled);
    }

    [Fact] // V-T87
    [Trait("Decision", "ED-292")]
    public void An_override_turns_it_on_for_its_own_flow_only()
    {
        using (VoiceFeature.Switch.Override(new VoiceFeatureSwitch(true)))
            Assert.True(VoiceFeature.Enabled);
        Assert.False(VoiceFeature.Enabled);
    }
}
