using Marrow.Core.Port.Voice;

namespace Marrow.Core.Tests.Port.Voice;

// VT-12 (voice-SPEC V-12.2, V-12.5, V-12.6): the numbers of the pill, the cross beside the orb and the text field's right edge.
public sealed class VoicePillGeometryTests
{
    [Fact]
    [Trait("Feature", "VT-12")]
    [Trait("Spec", "V-T72")]
    [Trait("Decision", "ED-292")]
    public void Heights_fonts_and_label_places_are_the_designs()
    {
        Assert.Equal(40, VoicePillGeometry.Height(VoiceListeningStyle.Pill));
        Assert.Equal(44, VoicePillGeometry.Height(VoiceListeningStyle.Float));
        Assert.Equal(13.5, VoicePillGeometry.FontSize(VoiceListeningStyle.Pill));
        Assert.Equal(15, VoicePillGeometry.FontSize(VoiceListeningStyle.Float));
        Assert.Equal(40, VoicePillGeometry.LabelLeft(VoiceListeningStyle.Pill));
        Assert.Equal(52, VoicePillGeometry.LabelLeft(VoiceListeningStyle.Float));
        Assert.Equal(-56, VoicePillGeometry.FloatLift); // -52 + 44/2 - 26
    }

    [Theory]
    [Trait("Feature", "VT-12")]
    [Trait("Spec", "V-T72")]
    [Trait("Decision", "ED-292")]
    [InlineData(VoiceListeningStyle.Pill, 77, 0, 40 + 8 + 77 + 8 + 24 + 8)]
    [InlineData(VoiceListeningStyle.Float, 90, 0, 44 + 8 + 90 + 8 + 24 + 8)]
    [InlineData(VoiceListeningStyle.Pill, 77, 51, 40 + 8 + 77 + 51 + 8 + 24 + 8)]
    public void The_open_width_is_H_plus_8_plus_label_plus_name_plus_8_plus_24_plus_8(VoiceListeningStyle style, double label, double name, double expected) =>
        Assert.Equal(expected, VoicePillGeometry.OpenWidth(style, label, name));

    [Theory]
    [Trait("Feature", "VT-12")]
    [Trait("Spec", "V-T72")]
    [Trait("Decision", "ED-292")]
    [InlineData(VoiceListeningStyle.Pill, 300, 300 + 20 - 150)] // the right end stays at cx + H/2
    [InlineData(VoiceListeningStyle.Float, 300, 300 - 22)] // the left end stays at cx - H/2
    public void One_end_of_the_pill_stays_at_the_orb(VoiceListeningStyle style, double cx, double left) =>
        Assert.Equal(left, VoicePillGeometry.Left(style, cx, 150));

    [Theory]
    [Trait("Feature", "VT-12")]
    [Trait("Spec", "V-T74")]
    [Trait("Decision", "ED-292")]
    [InlineData(32, -25.56)]
    [InlineData(26, -22.08)]
    public void The_cross_beside_the_orb_slides_to_minus_D_times_1_16_over_2_plus_7(double d, double xo) =>
        Assert.Equal(xo, VoicePillGeometry.CrossBesideOffset(d), 1e-9);

    [Theory]
    [Trait("Feature", "VT-12")]
    [Trait("Spec", "V-T75")]
    [Trait("Decision", "ED-292")]
    [InlineData(VoiceListeningStyle.Pill, true, 32, 173, 173 - 16 - 20 + 2)] // W - D/2 - H/2 + 2: 10 before the pill's left end, from the edge 8 before the controls
    [InlineData(VoiceListeningStyle.Pill, false, 32, 173, 0)]
    [InlineData(VoiceListeningStyle.Float, true, 32, 173, 0)]
    [InlineData(VoiceListeningStyle.Cross, true, 32, 173, 26 - 16 + 25.56)] // 26 - D/2 - xo
    [InlineData(VoiceListeningStyle.Cross, false, 32, 173, 0)]
    public void The_text_field_keeps_its_right_edge_10_before_the_pill_or_the_cross(VoiceListeningStyle style, bool live, double d, double width, double inset) =>
        Assert.Equal(inset, VoicePillGeometry.TextRightInset(style, live, d, width), 1e-9);
}
