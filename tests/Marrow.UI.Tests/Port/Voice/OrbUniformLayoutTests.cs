using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Marrow.Core.Port.Voice.Orb;
using Marrow.UI.Port.Voice.Orb;
using SkiaSharp;

namespace Marrow.UI.Tests.Port.Voice;

// VT-8, V-11.3: "one 136-float uniform block per frame (SkSL `uniform float4 u[34]` or named members: the builder's choice, with a layout test)". The port names every member
// of the export's WGSL `Uniforms` struct in its order; this is the layout test.
public sealed partial class OrbUniformLayoutTests
{
    [GeneratedRegex(@"layout\(std140\) uniform Uniforms \{(.*?)\} u;", RegexOptions.Singleline)]
    private static partial Regex Block();

    private static string DesignSource([CallerFilePath] string here = "") =>
        File.ReadAllText(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", "..", "..", "..", "porting", "first-release", "design-src", "customization", "voice-orbs.js")));

    [Fact]
    [Trait("Feature", "VT-8")]
    [Trait("Decision", "ED-267")]
    [Trait("Spec", "V-T51")]
    public void The_members_are_the_export_struct_member_for_member_136_floats()
    {
        var members = new List<(string Name, int Width)>();
        foreach (var declaration in Block().Match(DesignSource()).Groups[1].Value.Split(';'))
        {
            var tokens = declaration.Split([' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0) continue;
            var width = tokens[0] switch { "float" => 1, "vec2" => 2, "vec4" => 4, var other => throw new InvalidOperationException(other) };
            foreach (var name in string.Join(' ', tokens.Skip(1)).Split(',', StringSplitOptions.TrimEntries)) members.Add((name, width));
        }
        Assert.Equal(members, OrbUniformLayout.Members.Select(m => (m.Name, m.Width)));
        Assert.Equal(OrbUniformLayout.Floats, OrbUniformLayout.Members.Sum(m => m.Width));
        Assert.Equal(136, OrbSeeds.Length);
        var offset = 0;
        foreach (var m in OrbUniformLayout.Members)
        {
            Assert.Equal(offset, m.Offset);
            offset += m.Width;
        }
    }

    [Fact]
    [Trait("Feature", "VT-8")]
    [Trait("Decision", "ED-267")]
    [Trait("Spec", "V-T51")]
    public void The_indexes_the_export_runtime_writes_land_on_the_members_it_means()
    {
        int Of(string name) => OrbUniformLayout.Members.Single(m => m.Name == name).Offset;
        // the audio rules (3 speed, 6 warp, 21 contourDeform, 10 sheen, 14 exposure), the style (15) and the framing and glow (4 radius, 16 edgeSoftness, 17 edgeGlow)
        Assert.Equal([3, 6, 21, 10, 14], OrbExportRuntime.AudioRules.Select(r => r.Index));
        Assert.Equal(3, Of("speed"));
        Assert.Equal(6, Of("warp"));
        Assert.Equal(21, Of("contourDeform"));
        Assert.Equal(10, Of("sheen"));
        Assert.Equal(14, Of("exposure"));
        Assert.Equal(15, Of("style"));
        Assert.Equal(4, Of("radius"));
        Assert.Equal(16, Of("edgeSoftness"));
        Assert.Equal(17, Of("edgeGlow"));
        Assert.Equal((0, 2), (Of("size"), OrbUniformLayout.Members.Single(m => m.Name == "size").Width));
        Assert.Equal(2, Of("time"));
        // colours start at 40 (index >= 40 and (index - 40) % 4 < 3 are the colour components of sampleTransition)
        Assert.Equal(40, Of("colorA"));
        Assert.Equal(84, Of("glowColor"));
        Assert.Equal(132, Of("paletteStop11"));
    }

    [Fact]
    [Trait("Feature", "VT-8")]
    [Trait("Decision", "ED-267")]
    [Trait("Spec", "V-T51")]
    public void The_compiled_program_declares_one_array_of_35_float4_the_block_and_the_marrow_term()
    {
        Assert.Equal(["u"], OrbShaders.Effect.Uniforms.ToArray());
        Assert.Equal(35 * 4 * sizeof(float), OrbShaders.Effect.UniformSize);
        Assert.Equal(OrbUniformLayout.FrameFloats, 35 * 4);
        Assert.Contains("uniform float4 u[35];", OrbShaders.Source, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("size", "u[0].xy")]
    [InlineData("time", "u[0].z")]
    [InlineData("speed", "u[0].w")]
    [InlineData("radius", "u[1].x")]
    [InlineData("edgeGlow", "u[4].y")]
    [InlineData("colorA", "u[10]")]
    [InlineData("glowColor", "u[21]")]
    [InlineData("paletteStop11", "u[33]")]
    [Trait("Feature", "VT-8")]
    [Trait("Decision", "ED-267")]
    [Trait("Spec", "V-T51")]
    public void A_member_is_read_from_its_float4_and_lane(string name, string access)
    {
        Assert.Equal(access, OrbUniformLayout.Members.Single(m => m.Name == name).Access);
    }

    [Fact]
    [Trait("Feature", "VT-8")]
    [Trait("Decision", "ED-267")]
    [Trait("Spec", "V-T51")]
    public void Writing_a_frame_sets_the_one_uniform_with_the_block_then_the_marrow_term()
    {
        using var uniforms = new SKRuntimeEffectUniforms(OrbShaders.Effect);
        var values = new float[OrbUniformLayout.Floats];
        for (var i = 0; i < values.Length; i++) values[i] = i + 0.25f;
        var frame = new float[OrbUniformLayout.FrameFloats];
        OrbUniformLayout.Write(uniforms, frame, values, 0.5f, 0.25f, 1f);
        Assert.Equal(values, frame[..OrbUniformLayout.Floats]);
        Assert.Equal([0.5f, 0.25f, 1f, 0f], frame[OrbUniformLayout.Floats..]);
        Assert.Equal(1, uniforms.Count);
    }
}
