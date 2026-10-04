using SkiaSharp;

namespace Marrow.UI.Tests.Port.Voice;

// VT-8, V-T50 (V-11.7, V-11.1, V-11.2): the port draws what voice-orbs.js draws. tests/fixtures/voice/ref-orbs.js renders the reference frames in headless Chrome (WebGL2,
// ANGLE D3D11 on the lead's Windows run) at fixed times and states; each job is rendered by the port on the same card colour and compared: per-channel difference
// <= 2/255 at >= 99.5 % of pixels. While no reference PNG is committed the theory is reported as skipped (RefOrbsTheory), never passed.
public sealed class OrbFidelityTests
{
    public static TheoryData<string> Names() => OrbJobs.Names();

    [RefOrbsTheory]
    [Trait("Feature", "VT-8")]
    [Trait("Decision", "ED-267")]
    [Trait("Spec", "V-T50")]
    [MemberData(nameof(Names))]
    public void The_port_matches_the_reference_frame(string name)
    {
        var job = OrbJobs.All.Single(j => j.Name == name);
        var path = Path.Combine(OrbRig.ReferenceFolder(), name + ".png");
        Assert.True(File.Exists(path), "the reference PNG is missing: " + path);
        using var reference = SKBitmap.Decode(path);
        using var port = OrbRig.Render(job);
        Assert.Equal((reference.Width, reference.Height), (port.Width, port.Height));
        var (within, max) = OrbRig.Compare(reference, port, 2);
        Assert.True(within >= 0.995, $"{name}: {within:P2} of pixels within 2/255 (needs 99.5 %), largest difference {max}");
    }
}
