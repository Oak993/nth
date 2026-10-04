using Marrow.Core.Models.ComposerLights;

namespace Marrow.Core.Tests.Models.ComposerLights;

// Unit CZ-2 (OD-0099 Look Picks, Q-CZ-3 B): the Uiverse light's Custom colours are stored as #RRGGBBAA; a stored #RRGGBB still reads as
// opaque; the light draws the alpha. SOURCE FACT (lp-pickers.js, refresh()): --au-c1 / --au-c2 are rgba(colour, alpha); --au-k1/k2/n1/n2 are
// mixed from the opaque hex (hsvHex), so the alpha belongs to the halo's two colours only. The eighth-digit pair is read as a byte / 255
// as the design's own components hex field does (pickerA, parseInt(v.slice(6), 16) / 255).
public sealed class UiverseAuraAlphaTests
{
    private static ComposerLightSettings Custom(string c1, string c2 = "#38C9B4") =>
        ComposerLightSettings.Default with { Style = ComposerLightStyle.Aura, Aura = UiverseAuraOptions.Default with { Scheme = AuraScheme.Custom, Color1 = c1, Color2 = c2 } };

    [Fact]
    [Trait("Feature", "CZ-2")]
    [Trait("Rule", "R-TEST-4")]
    public void An_eight_digit_colour_is_kept_in_upper_case_and_a_six_digit_one_is_opaque()
    {
        Assert.Equal("#8E7CFF80", UiverseAuraOptions.NormalizedHex("#8e7cff80"));
        Assert.Equal("#8E7CFF", UiverseAuraOptions.NormalizedHex("#8e7cff"));
        Assert.Equal(1, UiverseAuraOptions.AlphaOf("#8E7CFF"));
        Assert.Equal(1, UiverseAuraOptions.AlphaOf("#8E7CFFFF"));
        Assert.Equal(0, UiverseAuraOptions.AlphaOf("#8E7CFF00"));
        Assert.Equal(128 / 255.0, UiverseAuraOptions.AlphaOf("#8E7CFF80"), 12);
        Assert.Equal("#8E7CFF", UiverseAuraOptions.OpaqueHex("#8E7CFF80"));
        Assert.Equal("#8E7CFF", UiverseAuraOptions.OpaqueHex("#8E7CFF"));
    }

    [Theory]
    [Trait("Feature", "CZ-2")]
    [Trait("Rule", "R-TEST-4")]
    [InlineData("#8E7CFF8")]
    [InlineData("#8E7CFF800")]
    [InlineData("8E7CFF80")]
    [InlineData("#8E7CGG80")]
    [InlineData("#8E7C")]
    public void A_colour_that_is_neither_six_nor_eight_hex_digits_falls_back_to_the_default(string text)
    {
        Assert.Null(UiverseAuraOptions.NormalizedHex(text));
        Assert.Equal(UiverseAuraOptions.Default.Color1, (UiverseAuraOptions.Default with { Color1 = text }).Clamped().Color1);
    }

    [Fact]
    [Trait("Feature", "CZ-2")]
    [Trait("Rule", "R-TEST-4")]
    public void The_alpha_round_trips_through_the_settings_json()
    {
        var settings = Custom("#8e7cff80", "#38C9B400");
        var json = settings.Clamped().ToJson();   // ToJson writes what it is given; the settings are clamped on the way in and out
        Assert.Contains("\"c1\":\"#8E7CFF80\"", System.Text.Encoding.UTF8.GetString(json), StringComparison.Ordinal);
        var back = ComposerLightSettings.FromJson(json);
        Assert.Equal("#8E7CFF80", back.Aura.Color1);
        Assert.Equal("#38C9B400", back.Aura.Color2);
        Assert.Equal(settings.Clamped(), back);
    }

    [Fact]
    [Trait("Feature", "CZ-2")]
    [Trait("Rule", "R-TEST-4")]
    public void A_stored_six_digit_colour_still_reads_and_stays_six_digits()
    {
        var back = ComposerLightSettings.FromJson(Custom("#102030").ToJson());
        Assert.Equal("#102030", back.Aura.Color1);
        Assert.Equal(1, UiverseAuraOptions.AlphaOf(back.Aura.Color1));
    }

    [Fact]
    [Trait("Feature", "CZ-2")]
    [Trait("Rule", "R-TEST-4")]
    public void Every_alpha_the_picker_can_make_comes_back_as_the_same_hundredth()
    {
        // the opacity strip steps by a hundredth (round(u * 100) / 100); the stored byte must read back as that hundredth
        for (var hundredth = 0; hundredth <= 100; hundredth++)
        {
            var a = hundredth / 100.0;
            var stored = "#112233" + ((int)Math.Round(a * 255, MidpointRounding.AwayFromZero)).ToString("X2", System.Globalization.CultureInfo.InvariantCulture);
            Assert.Equal(hundredth, Math.Round(UiverseAuraOptions.AlphaOf(stored) * 100, MidpointRounding.AwayFromZero));
        }
    }

    [Fact]
    [Trait("Feature", "CZ-2")]
    [Trait("Rule", "R-TEST-4")]
    public void The_light_takes_the_alpha_for_the_two_halo_colours_only_and_a_six_digit_colour_is_opaque()
    {
        var light = new ComposerLightAnimator(Custom("#FF000080", "#0000FF"), ComposerLightPhase.Rest, dark: true, 820, 52, reduceMotion: true);
        light.Still(0.35, 0.35, 0, 0, 0);
        var a = light.Aura;
        Assert.Equal(128 / 255.0, a.C1.A, 6);
        Assert.Equal(1, a.C2.A, 6);
        Assert.Equal((255.0, 0.0, 0.0), (a.C1.R, a.C1.G, a.C1.B));   // the colour itself is not premultiplied here
        foreach (var opaque in new[] { a.K1, a.K2, a.N1, a.N2 }) Assert.Equal(1, opaque.A, 6);
        Assert.Equal(255, a.K1.R, 6);                                 // mixed from the opaque #FF0000: 35 % towards white
        Assert.Equal(89, a.K1.G, 3);                                  // round(255 * 0.35), a whole channel as the mix returns hex
    }

    [Fact]
    [Trait("Feature", "CZ-2")]
    [Trait("Rule", "R-TEST-4")]
    public void The_other_schemes_stay_opaque()
    {
        foreach (var scheme in new[] { AuraScheme.Original, AuraScheme.Maker, AuraScheme.Accent })
        {
            var s = Custom("#FF000080") with { Aura = UiverseAuraOptions.Default with { Scheme = scheme, Color1 = "#FF000080" } };
            var light = new ComposerLightAnimator(s, ComposerLightPhase.Rest, dark: true, 820, 52, reduceMotion: true);
            light.Still(0.35, 0.35, 0, 0, 0);
            Assert.Equal(1, light.Aura.C1.A, 6);
            Assert.Equal(1, light.Aura.C2.A, 6);
        }
    }
}
