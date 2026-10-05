using Marrow.UI.Port.Voice.Glow;
using SkiaSharp;

namespace Marrow.UI.Tests.Port.Voice;

// VT-9, V-T60: the band canvas alone against Chrome. The whole-frame measures of VoiceGlowFidelityTests pass a band whose blur or colours are off, because most of a frame is
// card and the other layers dominate the rest (a change of the band's blur from 3.5 to 2.5 failed one of its 56 rows). ref-glow.js therefore also renders the band with every
// other layer hidden by the page's own CSS (`ref_band_...`: 4 states x 2 sizes x 2 themes), and this compares only the pixels the reference lights.
//
// TOLERANCE (ENGINEERING DECISION, from the first comparison): the analytic band measured, over the lit pixels of the 16 frames, 97.2 % to 99.3 % within 4/255 and a mean of the
// largest channel difference of 1.25 to 1.79; with the blur at 2.5 instead of 3.5 it measured 93.6 % to 99.4 % and 1.35 to 2.11, and 6 of the 16 frames fell under 96.5 %.
// The test asks for >= 96.5 % within 4/255 and a mean <= 2.0. The canvas strokes mode measured 58 % to 98 % within 4/255 (the browser's GPU blends the sixteen strokes in its own
// rounding), too loose to judge a blur, so it is held only to a mean of 4.5 (measured up to 3.54).
public sealed class VoiceGlowBandFidelityTests
{
    public static TheoryData<string> Names() => VoiceGlowJobs.BandNames();

    private static (double Within4, double Mean, int Lit) Measure(string name, VoiceGlowBandMode mode)
    {
        var job = VoiceGlowJobs.BandOnly.Single(j => j.Name == name);
        var path = Path.Combine(VoiceGlowRig.ReferenceFolder(), name + ".png");
        Assert.True(File.Exists(path), "the reference PNG is missing: " + path);
        using var reference = SKBitmap.Decode(path);
        using var painter = new VoiceGlowPainter { BandMode = mode, Layers = VoiceGlowLayers.Band };
        using var port = VoiceGlowRig.Render(job, painter: painter);
        return VoiceGlowRig.CompareLit(reference, port, job.Dark ? VoiceGlowRig.DarkCard : VoiceGlowRig.LightCard);
    }

    [RefGlowTheory]
    [Trait("Feature", "VT-9")]
    [Trait("Decision", "ED-NEW-f-vt-9")]
    [Trait("Spec", "V-T60")]
    [MemberData(nameof(Names))]
    public void The_analytic_band_matches_the_reference_band_on_the_pixels_it_lights(string name)
    {
        var (within4, mean, lit) = Measure(name, VoiceGlowBandMode.Analytic);
        Assert.True(lit > 100, $"{name}: the reference band lights only {lit} pixels");
        Assert.True(within4 >= 0.965, $"{name}: {within4:P2} of the {lit} lit pixels within 4/255 (needs 96.5 %), mean {mean:F2}");
        Assert.True(mean <= 2.0, $"{name}: mean difference {mean:F2} over the {lit} lit pixels (needs 2.0 or less), {within4:P2} within 4/255");
    }

    [RefGlowTheory]
    [Trait("Feature", "VT-9")]
    [Trait("Decision", "ED-NEW-f-vt-9")]
    [Trait("Spec", "V-T60")]
    [MemberData(nameof(Names))]
    public void The_stroked_band_matches_the_reference_band_on_the_pixels_it_lights(string name)
    {
        var (within4, mean, lit) = Measure(name, VoiceGlowBandMode.Strokes);
        Assert.True(mean <= 4.5, $"{name}: mean difference {mean:F2} over the {lit} lit pixels (needs 4.5 or less), {within4:P2} within 4/255");
    }
}
