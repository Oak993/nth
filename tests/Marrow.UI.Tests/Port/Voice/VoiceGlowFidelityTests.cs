using Marrow.UI.Port.Voice.Glow;
using SkiaSharp;

namespace Marrow.UI.Tests.Port.Voice;

// VT-9, V-T60 (voice-SPEC V-13.2, V-13.3): the port draws what voice-glow.js draws. tests/fixtures/voice/ref-glow.js renders the reference frames in headless Chrome (the design's
// own voice-glow.js and mountVoiceGlow, the manual frame clock at 1/60 s) for 7 states x 2 composer sizes x 2 themes; each job is replayed through the port's driver and painter
// on the same card colour and compared.
//
// TOLERANCE (ENGINEERING DECISION, set from the first comparison of all 28 frames; V-13.2 says to): the port is the browser's picture re-made in Skia, layer by layer, so the two
// agree to rounding and to the way the two rasterizers blend, not bit for bit. Measured on the first run (Windows 11, Chrome 154 on ANGLE D3D11, the port on Skia's CPU raster,
// the band drawn as the canvas strokes it): the share of pixels within 8/255 was 98.3 % to 100 % (lowest: the 0.2 level, dark), within 16/255 99.97 % to 100 %, the mean over
// pixels of the largest channel difference 0.00 to 1.77 and the largest difference 45; the larger errors are the band canvas's sixteen overlapping blurred strokes (the browser's GPU
// blends them in its own rounding) and the antialiased rim at the rounded corners. The band drawn as one analytic pass (what the painter does on a GPU) measured 99.86 % to 100 %,
// 99.97 % to 100 %, mean 0.00 to 1.12 and largest 44. The test asks for the same of both: >= 98.0 % of pixels within 8/255, >= 99.9 % within 16/255, mean <= 2.0, largest <= 64.
public sealed class VoiceGlowFidelityTests
{
    public static TheoryData<string> Names() => VoiceGlowJobs.Names();

    private static void The_frame_matches(string name, VoiceGlowBandMode mode)
    {
        var job = VoiceGlowJobs.All.Single(j => j.Name == name);
        var path = Path.Combine(VoiceGlowRig.ReferenceFolder(), name + ".png");
        Assert.True(File.Exists(path), "the reference PNG is missing: " + path);
        using var reference = SKBitmap.Decode(path);
        using var painter = new VoiceGlowPainter { BandMode = mode };
        using var port = VoiceGlowRig.Render(job, painter: painter);
        Assert.Equal((reference.Width, reference.Height), (port.Width, port.Height));
        var (within8, within16, mean, max) = VoiceGlowRig.Compare(reference, port);
        Assert.True(within8 >= 0.98, $"{name} ({mode}): {within8:P2} of pixels within 8/255 (needs 98 %), mean {mean:F2}, largest {max}");
        Assert.True(within16 >= 0.999, $"{name} ({mode}): {within16:P2} of pixels within 16/255 (needs 99.9 %), mean {mean:F2}, largest {max}");
        Assert.True(mean <= 2.0, $"{name} ({mode}): mean difference {mean:F2} (needs 2.0 or less), largest {max}");
        Assert.True(max <= 64, $"{name} ({mode}): largest difference {max} (needs 64 or less)");
    }

    [RefGlowTheory]
    [Trait("Feature", "VT-9")]
    [Trait("Decision", "ED-302")]
    [Trait("Spec", "V-T60")]
    [MemberData(nameof(Names))]
    public void The_port_matches_the_reference_frame(string name) => The_frame_matches(name, VoiceGlowBandMode.Auto);

    [RefGlowTheory]
    [Trait("Feature", "VT-9")]
    [Trait("Decision", "ED-336")]
    [Trait("Spec", "V-T60")]
    [MemberData(nameof(Names))]
    public void The_port_with_the_analytic_band_matches_the_reference_frame(string name) => The_frame_matches(name, VoiceGlowBandMode.Analytic);
}
