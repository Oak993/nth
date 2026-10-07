using Avalonia;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Voice;
using Xunit;

namespace Marrow.App.Tests.Views.Composer;

// V-T132/V-T133: the cap clip must follow the actual arranged material, including Avalonia's physical-pixel layout rounding.
[Collection("Headless")]
public sealed class ComposerVoicePillClipTests(HeadlessSession headless)
{
    [Theory]
    [InlineData(VoiceListeningStyle.Pill, 26, "quiet", 1.5)]
    [InlineData(VoiceListeningStyle.Float, 32, "quiet", 1.5)]
    [InlineData(VoiceListeningStyle.Pill, 26, "pop", 1.25)]
    [InlineData(VoiceListeningStyle.Pill, 26, "pop", 1.5)]
    [InlineData(VoiceListeningStyle.Float, 26, "pop", 1.5)]
    [InlineData(VoiceListeningStyle.Float, 32, "pop", 2.0)]
    [InlineData(VoiceListeningStyle.Pill, 32, "resize", 1.25)]
    [InlineData(VoiceListeningStyle.Float, 26, "resize", 1.5)]
    [Trait("Decision", "ED-292")]
    public void The_capsule_clip_has_the_same_extent_as_the_arranged_material(VoiceListeningStyle style, int diameter, string state, double scale)
    {
        headless.OnUiThread(() =>
        {
            using var motion = new VoicePillMotion();
            using var scene = ComposerVoiceCleanPillTests.Open(style, diameter, true, state, scale);
            scene.Rig.Main.OnMain(() =>
            {
                var pill = VoicePillProbe.Pill(scene.View);
                Assert.Equal(pill.Bounds.Width, pill.Body.Bounds.Width, 1e-6);
                // Skia's geometry bounds are float32; layout extents remain double precision.
                Assert.Equal((double)(float)pill.Bounds.Width, pill.ContentClip.Bounds.Width, 1e-6);
                Assert.Equal((double)(float)pill.Bounds.Height, pill.ContentClip.Bounds.Height, 1e-6);
                // The animated width is independently checked against the design's CSS curve in ComposerVoicePillLabelTests.
                Assert.Equal(pill.ShownWidth, pill.Bounds.Width, 1e-6);
            });
        });
    }

    [Theory]
    [InlineData(26, 1.25)]
    [InlineData(26, 1.5)]
    [InlineData(32, 1.25)]
    [InlineData(32, 1.5)]
    [Trait("Decision", "ED-292")]
    public void The_rendered_orb_is_centred_on_the_slots_source_anchor(int diameter, double scale)
    {
        headless.OnUiThread(() =>
        {
            using var motion = new VoicePillMotion();
            using var scene = ComposerVoiceCleanPillTests.Open(VoiceListeningStyle.Pill, diameter, true, "quiet", scale);
            scene.Rig.Main.OnMain(() =>
            {
                var slot = VoicePillProbe.Slot(scene.View);
                var sourceAnchor = slot.TranslatePoint(new Avalonia.Point(slot.Diameter / 2, 0), scene.Window)!.Value;
                var paintedCentre = slot.Orb.TranslatePoint(new Avalonia.Point(36, 36), scene.Window)!.Value;
                Assert.Equal(sourceAnchor.X, paintedCentre.X, 1e-6);
            });
        });
    }
}
