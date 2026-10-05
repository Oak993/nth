// REVIEW (WL-5 independent review): the fidelity gate against the WL-4 static engine it exists to check, and the coverage and
// tolerance the oracle commits to.
using SkiaSharp;

namespace Marrow.App.Tests.Wallpapers;

[Trait("Rule", "R-TEST-1")]
[Trait("Feature", "WL-5")]
[Trait("Review", "WL-5")]
public sealed class REVIEW_WallpaperFidelityGateTests
{
    // WallpaperFidelityTheoryAttribute turns a missing binding into xUnit Skip, so the run reports "Passed!" with zero pixels compared.
    // With WL-4 merged the bridge still finds no usable renderer constructor (WL-4's is (selection, bool, StaticWallpaperInputs?),
    // not the WL-3 WallpaperEnvironment shape), no dynamic/focus selection parameters (WL-4 keeps them in Options) and no
    // Premium/StaticPremium definition property (WL-4 exposes Static).
    [Fact]
    public void REVIEW_the_static_default_fidelity_gate_is_open_against_the_WL_4_renderer()
    {
        var reason = WallpaperRendererBridge.SkipReason("static", false);
        Assert.True(reason is null, "static pixel comparisons would be skipped, not run: " + reason);
    }

    [Fact]
    public void REVIEW_the_static_premium_fidelity_gate_is_open_against_the_WL_4_renderer()
    {
        var reason = WallpaperRendererBridge.SkipReason("static", true);
        Assert.True(reason is null, "dusk, Match accent and full Focus comparisons would be skipped, not run: " + reason);
    }

    // Every oracle case is rendered with the pointer at the centre (tilt [0,0]); the static Perspective path, which moves the
    // picture by u_tilt * u_tg, is never compared at all.
    [Fact]
    public void REVIEW_some_static_reference_is_rendered_with_a_non_zero_tilt()
    {
        Assert.Contains(StaticCases(), c => c.Inputs.GetProperty("perspective").GetBoolean()
            && c.Inputs.GetProperty("tilt").EnumerateArray().Any(t => t.GetDouble() != 0));
    }

    // Match accent is compared at one accent (#005FB8) only, so the hue-offset compression and the clamped chroma scale are never
    // exercised at a second accent.
    [Fact]
    public void REVIEW_match_accent_is_compared_at_more_than_one_accent()
    {
        var accents = StaticCases().Where(c => c.Variant == "match-accent").Select(c => c.Inputs.GetProperty("accent").GetString()).Distinct().Count();
        Assert.True(accents > 1, $"{accents} accent colour(s)");
    }

    // Dynamic is compared at 18:00 only: a key hour, where the page's smoothstep legs are flat, so interpolation between keys is untested.
    [Fact]
    public void REVIEW_dynamic_is_compared_at_an_hour_between_two_key_looks()
    {
        var hours = StaticCases().Where(c => c.Variant == "dynamic-dusk").Select(c => c.Inputs.GetProperty("clockHours").GetDouble()).Distinct().ToArray();
        Assert.Contains(hours, h => h % 6 != 0);
    }

    // Focus is compared at 0 and 1 only; the page's default Focus for Fogbow/Brume (0.1) and Lenticular/Pillar (0.05) and every
    // intermediate sigma are unchecked (ED-NEW-codex-wl-5 item 2 says so).
    [Fact]
    public void REVIEW_focus_is_compared_at_an_intermediate_value()
    {
        var focus = StaticCases().Select(c => c.Inputs.GetProperty("focus").GetDouble()).Distinct().ToArray();
        Assert.Contains(focus, f => f > 0 && f < 1);
    }

    // GUARD (passes today): ED-NEW-codex-wl-5 item 3 chose 0.010 (mean) and 0.080 (maximum) before any app comparison and calls them
    // "not experimentally calibrated". A flat 3-level palette error over the whole picture (OKLab dE about 0.0126 at mid-grey) is still
    // rejected; the review found no evidence the mean limit is too loose, only that nothing measured it.
    [Fact]
    public void REVIEW_a_uniform_three_level_colour_error_over_the_whole_picture_is_not_within_tolerance()
    {
        using var reference = Flat(128, 128, 128); using var shifted = Flat(131, 131, 131);
        var (mean, max, _, _) = WallpaperPerceptualComparison.Measure(reference, shifted);
        Assert.False(WallpaperPerceptualComparison.Within(mean, max), $"mean dE {mean:F5} (limit {WallpaperPerceptualComparison.MeanLimit}), max {max:F5} (limit {WallpaperPerceptualComparison.PixelLimit}) is accepted");
    }

    private static IEnumerable<WallpaperFidelityCase> StaticCases() => WallpaperReferences.Cases.Where(c => string.Equals(c.Collection, "static", StringComparison.Ordinal));

    private static SKBitmap Flat(byte r, byte g, byte b)
    {
        var bitmap = new SKBitmap(8, 8, SKColorType.Rgba8888, SKAlphaType.Opaque);
        bitmap.Erase(new SKColor(r, g, b)); return bitmap;
    }
}
