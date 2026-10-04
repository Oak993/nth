using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace Marrow.UI.Tests.Port.Voice;

// VT-8, V-T50 (V-11.7): the fixture script and the test render the same matrix. ref-orbs.js declares its matrix as four arrays; they must be the port test's lists, and the
// script must load the design's voice-orbs.js as it is (only its two export lines become a global).
public sealed partial class OrbReferenceScriptTests
{
    [GeneratedRegex(@"const (ORBS|STATE_NAMES|BALLS|THEMES) = \[(.*?)\];", RegexOptions.Singleline)]
    private static partial Regex Arrays();

    private static string Script([CallerFilePath] string here = "") =>
        File.ReadAllText(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", "..", "..", "fixtures", "voice", "ref-orbs.js")));

    private static string[] Items(string list) => [.. list.Split(',').Select(s => s.Trim().Trim('\'')).Where(s => s.Length > 0)];

    [Fact]
    [Trait("Feature", "VT-8")]
    [Trait("Decision", "ED-267")]
    [Trait("Spec", "V-T50")]
    public void The_script_renders_the_matrix_of_the_test()
    {
        var found = Arrays().Matches(Script()).ToDictionary(m => m.Groups[1].Value, m => Items(m.Groups[2].Value));
        Assert.Equal(OrbJobs.Orbs, found["ORBS"]);
        Assert.Equal(OrbJobs.States, found["STATE_NAMES"]);
        Assert.Equal(OrbJobs.Balls.Select(b => b.ToString(System.Globalization.CultureInfo.InvariantCulture)), found["BALLS"]);
        Assert.Equal(OrbJobs.Themes, found["THEMES"]);
        Assert.Equal(72, OrbJobs.All.Count);
        Assert.Equal(72, OrbJobs.All.Select(j => j.Name).Distinct().Count());
    }

    [Fact]
    [Trait("Feature", "VT-8")]
    [Trait("Decision", "ED-267")]
    [Trait("Spec", "V-T50")]
    public void The_script_loads_the_design_source_unchanged_but_for_its_two_export_lines()
    {
        var script = Script();
        Assert.Contains("['export const orbs =', 'window.__orbs =']", script, StringComparison.Ordinal);
        Assert.Contains("['export default orbs;', '']", script, StringComparison.Ordinal);
        Assert.Contains("porting', 'first-release', 'design-src', 'customization', 'voice-orbs.js'", script, StringComparison.Ordinal);
        Assert.Contains("context: 'own'", script, StringComparison.Ordinal);
        Assert.Contains("deviceScaleFactor: 1", script, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Feature", "VT-8")]
    [Trait("Decision", "ED-267")]
    [Trait("Spec", "V-T50")]
    public void The_script_keeps_the_constants_of_the_states_V_11_4_names()
    {
        var script = Script();
        foreach (var constant in new[] { "hoverSpeed: 2.0", "voiceSpeed: 2.2", "procSpeed: 4.6", "errSpeed: 0.6", "hoverAct: 0.3", "hoverScale: 1.04", "listenScale: 1.16", "voiceScale: 0.04",
                     "procScale: 1.1", "hoverGlow: 0.55", "listenGlow: 0.45", "voiceGlow: 0.4", "procGlow: 0.75" })
            Assert.Contains(constant, script, StringComparison.Ordinal);
    }
}
