using Marrow.Core.Models.ComposerLights;
using Marrow.UI.Port.ComposerLights;
using Marrow.UI.Tests.Fixtures;
using SkiaSharp;

namespace Marrow.UI.Tests.Port.ComposerLights;

// DEVIATION OD-0089 (owner-ruled addition; ED-251): the composer light's renderer, drawn on the playground's reference card
// (beam-fix/pill.js framing). Expectations follow the playground: Border beam lives inside the composer (its slot clips to
// the composer's corner radius), the Uiverse light's halo outside it, Off draws nothing (logic.js panes: "Nothing is drawn").
public sealed class ComposerLightRendererTests
{
    [Fact]
    [Trait("Decision", "ED-251")] // the board's port notes: one SkSL shader per light; both compile
    public void Both_shaders_compile()
    {
        Assert.NotNull(ComposerLightShaders.Beam);
        Assert.NotNull(ComposerLightShaders.Aura);
    }

    [Fact]
    [Trait("Decision", "ED-251")] // evidence: the frames of a reference job list (MARROW_COMPOSER_LIGHT_JOBS), written to MARROW_COMPOSER_LIGHT_EVIDENCE
    public void Evidence_frames_of_the_reference_jobs()
    {
        var jobs = Environment.GetEnvironmentVariable("MARROW_COMPOSER_LIGHT_JOBS");
        if (string.IsNullOrEmpty(jobs)) return;
        foreach (var job in ComposerLightJob.Load(jobs))
        {
            var dark = job.Th == "dark";
            var light = ComposerLightRig.Light(job.Settings(), dark, job.Phase);
            light.Still(job.T, job.TH ?? job.T, job.AuraP ?? 0, job.AuraBp ?? 0, 0);
            using var frame = ComposerLightRig.Render(light, dark);
            ComposerLightRig.Evidence(frame, job.Name.Replace("ref_", "port_", StringComparison.Ordinal));
        }
    }
}
