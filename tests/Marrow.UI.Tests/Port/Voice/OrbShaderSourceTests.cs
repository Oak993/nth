using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Marrow.UI.Port.Voice.Orb;

namespace Marrow.UI.Tests.Port.Voice;

// VT-8, V-11.1: "the fragment shader (voice-orbs.js FRAG ...) translated to SkSL function by function with the same names". The SkSL is held to the design's text: after the
// mechanical changes (vecN to floatN, `u.name` to its place in `u[]`) every function of FRAG, from the first constant to main, is in the SkSL character for character.
public sealed partial class OrbShaderSourceTests
{
    [GeneratedRegex(@"\bvec([234])\b")]
    private static partial Regex Vec();

    [GeneratedRegex(@"\bu\.(\w+)")]
    private static partial Regex Member();

    [GeneratedRegex(@"\bmrw\b")]
    private static partial Regex Mrw();

    private static string DesignFragment([CallerFilePath] string here = "")
    {
        var source = File.ReadAllText(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", "..", "..", "..", "porting", "first-release", "design-src", "customization", "voice-orbs.js")));
        var start = source.IndexOf("const FRAG = `", StringComparison.Ordinal) + "const FRAG = `".Length;
        return source[start..source.IndexOf("`;", start, StringComparison.Ordinal)];
    }

    // the mechanical changes: vecN to floatN, a member of the std140 block to its place in u[], mrw to u[34]
    private static string Mechanical(string glsl) =>
        Mrw().Replace(Member().Replace(Vec().Replace(glsl, "float$1"), m => OrbUniformLayout.Members.Single(x => x.Name == m.Groups[1].Value).Access), "u[34]");

    [Fact]
    [Trait("Feature", "VT-8")]
    [Trait("Decision", "ED-267")]
    [Trait("Spec", "V-T50")]
    public void The_program_compiles()
    {
        Assert.NotNull(OrbShaders.Effect);
    }

    [Fact]
    [Trait("Feature", "VT-8")]
    [Trait("Decision", "ED-267")]
    [Trait("Spec", "V-T50")]
    public void Every_function_of_the_design_shader_is_in_the_SkSL_unchanged_but_for_the_mechanical_changes()
    {
        var fragment = DesignFragment();
        var from = fragment.IndexOf("const float GL_CLEAR_EA", StringComparison.Ordinal);
        var to = fragment.IndexOf("void main()", StringComparison.Ordinal);
        var carried = Mechanical(fragment[from..to]).TrimEnd();
        Assert.Contains(carried, OrbShaders.Source, StringComparison.Ordinal);
        // and the functions are all there by name
        var names = Regex.Matches(fragment[from..to], @"^(?:float|vec[234]) (\w+)\(", RegexOptions.Multiline).Select(m => m.Groups[1].Value).ToArray();
        Assert.Equal(17, names.Length);   // mfEdgeD ... orbGlassLiquidAnim: the design's shader has 17 functions before main
        foreach (var name in names) Assert.Contains(name + "(", OrbShaders.Source, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Feature", "VT-8")]
    [Trait("Decision", "ED-267")]
    [Trait("Spec", "V-T50")]
    public void Main_is_the_designs_main_with_the_y_flip_and_the_return_taken_for_Skia()
    {
        var fragment = DesignFragment();
        var body = Mechanical(fragment[fragment.IndexOf("  // shader-source.ts fs_main", StringComparison.Ordinal)..fragment.LastIndexOf('}')]);
        var carried = body.Replace("fragColor = o;", "return o;", StringComparison.Ordinal);
        Assert.Contains(carried.TrimEnd(), OrbShaders.Source, StringComparison.Ordinal);
        Assert.Contains("float2 fragCoord = float2(coords.x, u[0].xy.y - coords.y);", OrbShaders.Source, StringComparison.Ordinal);
        Assert.DoesNotContain("gl_FragCoord.", OrbShaders.Source, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Feature", "VT-8")]
    [Trait("Decision", "ED-267")]
    [Trait("Spec", "V-T50")]
    public void The_two_presets_branches_are_the_ones_carried()
    {
        Assert.Contains("if (style == 9) { return glsSiriFluid(p, t); }", OrbShaders.Source, StringComparison.Ordinal);
        Assert.Contains("if (style == 19) { return glsVoiceWaveFluid(p, t); }", OrbShaders.Source, StringComparison.Ordinal);
    }
}
