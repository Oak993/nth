using System.Runtime.CompilerServices;
using System.Text.Json;
using Marrow.UI.Port.Voice.Glow;

namespace Marrow.UI.Tests.Port.Voice;

// VT-9, V-T60 (V-13.2, V-13.3): the fixture script and the test render the same matrix. ref-glow.js writes its jobs next to the PNGs (manifest.json); they must be the port test's
// matrix, with V-13.3's configuration, and the script must load the design's voice-glow.js as it is (one global appended, no line of the package touched).
public sealed class VoiceGlowReferenceScriptTests
{
    private static string Here([CallerFilePath] string here = "") => here;

    private static string Script() =>
        File.ReadAllText(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Here())!, "..", "..", "..", "fixtures", "voice", "ref-glow.js")));

    private static JsonDocument Manifest() => JsonDocument.Parse(File.ReadAllBytes(Path.Combine(VoiceGlowRig.ReferenceFolder(), "manifest.json")));

    [Fact]
    [Trait("Feature", "VT-9")]
    [Trait("Decision", "ED-302")]
    [Trait("Spec", "V-T60")]
    public void The_manifest_the_script_wrote_is_the_matrix_of_the_test()
    {
        using var doc = Manifest();
        var jobs = doc.RootElement.GetProperty("jobs").EnumerateArray().ToList();
        Assert.Equal(28, VoiceGlowJobs.All.Count);
        Assert.Equal(28, VoiceGlowJobs.All.Select(j => j.Name).Distinct().Count());
        Assert.Equal(VoiceGlowJobs.All.Select(j => j.Name), jobs.Select(j => j.GetProperty("name").GetString()));
        for (var i = 0; i < jobs.Count; i++)
        {
            var expected = VoiceGlowJobs.All[i];
            var job = jobs[i];
            Assert.Equal((expected.Width, expected.Height, expected.Theme, expected.State), (job.GetProperty("width").GetInt32(), job.GetProperty("height").GetInt32(), job.GetProperty("theme").GetString(), job.GetProperty("state").GetString()));
            var script = job.GetProperty("script").EnumerateArray().Select(s => new VoiceGlowSegment(s.GetProperty("frames").GetInt32(), s.GetProperty("level").GetDouble(), s.GetProperty("processing").GetBoolean())).ToList();
            Assert.Equal(expected.Script, script);
        }
        Assert.Equal(60, doc.RootElement.GetProperty("fps").GetInt32());
        Assert.Equal("#1c1c1c", doc.RootElement.GetProperty("card").GetProperty("dark").GetString());
        Assert.Equal("#f4f4f4", doc.RootElement.GetProperty("card").GetProperty("light").GetString());
    }

    [Fact]
    [Trait("Feature", "VT-9")]
    [Trait("Decision", "ED-302")]
    [Trait("Spec", "V-T60")]
    public void The_reference_page_is_given_V_13_3s_configuration()
    {
        using var doc = Manifest();
        var props = doc.RootElement.GetProperty("props");
        var mine = VoiceGlowComposerProps.For(dark: true, processing: false, reduceMotion: false);
        Assert.Equal("default", props.GetProperty("type").GetString());
        Assert.Equal(mine.Scale, props.GetProperty("scale").GetDouble());
        Assert.Equal(mine.GlowWidth, props.GetProperty("glowWidth").GetDouble());
        Assert.Equal(mine.LobeSpacing, props.GetProperty("lobeSpacing").GetDouble());
        Assert.Equal(mine.RangeWidth, props.GetProperty("rangeWidth").GetDouble());
        Assert.Equal(mine.ProcessingTravel, props.GetProperty("processingTravel").GetDouble());
        Assert.Equal(mine.BorderRadius, props.GetProperty("borderRadius").GetDouble());
        Assert.False(props.GetProperty("reducedMotion").GetBoolean());
        Assert.Equal(VoiceGlowInput.ComposerRadius, mine.BorderRadius);
    }

    [Fact]
    [Trait("Feature", "VT-9")]
    [Trait("Decision", "ED-302")]
    [Trait("Spec", "V-T60")]
    public void The_script_loads_the_design_source_unchanged_but_for_one_appended_global()
    {
        var script = Script();
        Assert.Contains("porting', 'first-release', 'design-src', 'customization', 'voice-glow.js'", script, StringComparison.Ordinal);
        Assert.Contains(@"fs.readFileSync(glowPath, 'utf8') + '\nwindow.__voiceGlow = { mountVoiceGlow };\n'", script, StringComparison.Ordinal);
        Assert.DoesNotContain(".replace(", script, StringComparison.Ordinal);   // no line of the package is rewritten
        Assert.Contains("deviceScaleFactor: 1", script, StringComparison.Ordinal);
        Assert.Contains("const FPS_MS = 1000 / 60;", script, StringComparison.Ordinal);
        Assert.Contains("window.requestAnimationFrame = (cb)", script, StringComparison.Ordinal);       // the manual frame clock
        Assert.Contains("a.finish()", script, StringComparison.Ordinal);                              // the CSS fade is finished at once
    }

    [Fact]
    [Trait("Feature", "VT-9")]
    [Trait("Decision", "ED-302")]
    [Trait("Spec", "V-T60")]
    public void The_design_source_still_exports_the_mount_and_its_driver_constants()
    {
        var design = File.ReadAllText(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Here())!, "..", "..", "..", "..", "porting", "first-release", "design-src", "customization", "voice-glow.js")));
        Assert.Contains("export function mountVoiceGlow(el, props = {})", design, StringComparison.Ordinal);
        Assert.Contains("const Za = 1e3 / 60 - 2", design, StringComparison.Ordinal);   // the page's 60 fps cap, which the reference clock steps at exactly 1000 / 60
        Assert.Contains("vb-fade-in-${e} 0.6s ease forwards", design, StringComparison.Ordinal);
        Assert.Contains("vb-fade-out-${e} 0.5s ease forwards", design, StringComparison.Ordinal);
    }
}
