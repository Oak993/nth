using Marrow.App.Tests.Fixtures;
using Marrow.UI.Port;
using Xunit;
using static Marrow.App.Tests.Fixtures.VoicePillProbe;

namespace Marrow.App.Tests.Views.Composer;

// VT-12, OD-0123: the voice pill's glass draws no contact line just outside its ends (the approved page's pill has none), and it is the only glass that asks for that. Its rim light,
// inner shade and the page's rims are kept (OD-0121 C); that the shared material really leaves the pixels outside the edge alone for such a glass, and still draws the line for
// every other glass, is GlassMaterialLiquidLookTests.
[Collection("Headless")]
public sealed class ComposerVoiceOuterLineTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Decision", "ED-570")]
    public void The_voice_pills_glass_asks_for_no_contact_line_and_keeps_its_no_refraction() => headless.OnUiThread(() =>
    {
        using var rig = new VoiceComposerRig();
        var (pill, window) = Standalone(rig);
        rig.Main.OnMain(() =>
        {
            var effect = SwiftGlass.GetEffect(pill.Body)!;
            Assert.True(effect.NoContactLine);
            Assert.True(effect.NoRefraction);
            Assert.False(GlassEffect.Regular(GlassShape.Capsule(RoundedCornerStyle.Circular)).NoContactLine); // every other glass keeps it
        });
        rig.Close(window);
    });

    [Fact]
    [Trait("Decision", "ED-NEW-glasslook-4")] // the page's backdrop-filter blurs the pixels in the pill's own box (GlassOwnBackdropTests shows the material keeps to it)
    public void The_voice_pills_frost_reads_only_its_own_box() => headless.OnUiThread(() =>
    {
        using var rig = new VoiceComposerRig();
        var (pill, window) = Standalone(rig);
        rig.Main.OnMain(() =>
        {
            Assert.True(SwiftGlass.GetEffect(pill.Body)!.OwnBackdrop);
            Assert.False(GlassEffect.Regular(GlassShape.Capsule(RoundedCornerStyle.Circular)).OwnBackdrop); // every other glass reads round its shape
        });
        rig.Close(window);
    });
}
