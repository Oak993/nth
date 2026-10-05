using Marrow.App.Port.Voice;

namespace Marrow.App.Tests.Port.Voice;

// VT-15, V-T104 first half (V-2.6): the switch that turns the feature on. The orb in every composer (V-14.3) is VT-12's and is not tested here.
[Collection("VoiceFeature")]
public sealed class VoiceFeatureTests : IDisposable
{
    public VoiceFeatureTests() => VoiceFeature.Reset();

    public void Dispose() => VoiceFeature.Reset();

    [Fact]
    [Trait("Feature", "VT-15")]
    [Trait("Spec", "V-T104")]
    [Trait("Decision", "ED-289")]
    public void The_feature_is_off_until_the_switch_is_thrown_and_stays_on()
    {
        Assert.False(VoiceFeature.Enabled);
        VoiceFeature.SwitchOn();
        Assert.True(VoiceFeature.Enabled);
        VoiceFeature.SwitchOn();
        Assert.True(VoiceFeature.Enabled);
        VoiceFeature.Reset();
        Assert.False(VoiceFeature.Enabled);
    }
}
