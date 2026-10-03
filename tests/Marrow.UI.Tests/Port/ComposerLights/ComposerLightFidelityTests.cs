using Marrow.UI.Tests.Fixtures;
using SkiaSharp;

namespace Marrow.UI.Tests.Port.ComposerLights;

// DEVIATION OD-0089 (owner-ruled addition; ED-251): the port draws what the approved playground draws. The references in
// tests/fixtures/composer-lights are the playground board itself (ComposerLights.dc.html in headless Chrome, frozen at the
// job's time by tests/fixtures/composer-lights/ref.js, 2026-10-03); each job is rendered by the port on the same card and
// compared pixel against pixel. The tolerances are per job in jobs.json: the stroke's corner antialiasing differs by a
// few pixels, and Pulse Outside in the light theme widens its blurred blobs rather than blurring them (a known gap, 7.5 %).
public sealed class ComposerLightFidelityTests
{
    public static TheoryData<string> Names()
    {
        var data = new TheoryData<string>();
        foreach (var (job, _, _) in ComposerLightFixtures.Jobs()) data.Add(job.Name);
        return data;
    }

    [Theory]
    [Trait("Decision", "ED-251")] // the playground board's own frames (tests/fixtures/composer-lights/*.png)
    [MemberData(nameof(Names))]
    public void The_port_matches_the_playground(string name)
    {
        var (job, maxDelta, maxOff) = ComposerLightFixtures.Jobs().Single(j => j.Job.Name == name);
        var dark = job.Th == "dark";
        var light = ComposerLightRig.Light(job.Settings(), dark, job.Phase);
        light.Still(job.T, job.TH ?? job.T, job.AuraP ?? 0, job.AuraBp ?? 0, 0);
        using var port = ComposerLightRig.Render(light, dark);
        using var reference = SKBitmap.Decode(ComposerLightFixtures.Path(name + ".png"));
        Assert.Equal((reference.Width, reference.Height), (port.Width, port.Height));
        var (max, off) = ComposerLightFixtures.Compare(reference, port);
        Assert.True(max <= maxDelta && off <= maxOff, $"{name}: largest difference {max} (allowed {maxDelta}), {off:F3} % of pixels off by more than 6 (allowed {maxOff} %)");
    }
}
