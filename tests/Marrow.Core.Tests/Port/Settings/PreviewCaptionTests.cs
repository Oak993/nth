using System.Text.Json;
using Marrow.Core.Models.ComposerLights;
using Marrow.Core.Port.Settings;
namespace Marrow.Core.Tests.Port.Settings;

public sealed class PreviewCaptionTests
{
    private static JsonDocument Fixture()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Marrow.sln"))) dir = dir.Parent;
        return JsonDocument.Parse(File.ReadAllBytes(Path.Combine(dir!.FullName, "tests/fixtures/customization/stage.json")));
    }

    private static BeamType Type(string size) => size switch
    {
        "md" => BeamType.Large, "line" => BeamType.Line, "sm" => BeamType.Small, "pulse-inner" => BeamType.PulseInner, "pulse-outside" => BeamType.PulseOutside,
        _ => throw new ArgumentOutOfRangeException(nameof(size)),
    };

    private static BeamPalette Palette(string id) => Enum.Parse<BeamPalette>(id, ignoreCase: true);

    private static AuraScheme Scheme(string id) => Enum.Parse<AuraScheme>(id, ignoreCase: true);

    // customization-SPEC CZ-4.4: the preview foot's caption for every style and shape, byte for byte what cz.js caption() builds.
    [Fact]
    [Trait("Feature", "CZ-3")]
    [Trait("Spec", "CZ-T28")]
    [Trait("Decision", "ED-251")]
    public void The_foot_caption_matches_the_designs_caption_for_every_style_shape_palette_scheme_and_speed()
    {
        using var fixture = Fixture();
        var rows = fixture.RootElement.GetProperty("captions").EnumerateArray().ToArray();
        Assert.True(rows.Length > 100);
        foreach (var row in rows)
        {
            var style = row.GetProperty("style").GetString();
            ComposerLightSettings settings;
            var maker = "Claude";
            switch (style)
            {
                case "beam":
                    settings = ComposerLightSettings.Default with { Style = ComposerLightStyle.Beam, Beam = BorderBeamOptions.Default.WithType(Type(row.GetProperty("size").GetString()!)) with { Palette = Palette(row.GetProperty("color").GetString()!) } };
                    break;
                case "aura":
                    maker = MakerLiquids.For(row.GetProperty("maker").GetString()!)["name"];
                    settings = ComposerLightSettings.Default with { Style = ComposerLightStyle.Aura, Aura = UiverseAuraOptions.Default with { Scheme = Scheme(row.GetProperty("scheme").GetString()!), Speed = row.GetProperty("speed").GetDouble() } };
                    break;
                default:
                    settings = ComposerLightSettings.Default with { Style = ComposerLightStyle.Off };
                    break;
            }
            Assert.Equal(row.GetProperty("text").GetString(), PreviewCaption.Text(settings, maker));
        }
    }

    [Fact]
    [Trait("Feature", "CZ-3")]
    [Trait("Spec", "CZ-T28")]
    [Trait("Decision", "ED-251")]
    public void The_captions_literal_parts_are_the_specs_exact_words()
    {
        var beam = ComposerLightSettings.Default;
        Assert.Equal("Border beam \u00b7 Large \u00b7 Colourful", PreviewCaption.Text(beam, "Claude"));
        Assert.Equal("Border beam \u00b7 Pulse inside \u00b7 Gold", PreviewCaption.Text(beam with { Beam = beam.Beam.WithType(BeamType.PulseInner) with { Palette = BeamPalette.Gold } }, "Claude"));
        Assert.Equal("Uiverse light \u00b7 Original \u00b7 12 s a turn", PreviewCaption.Text(beam with { Style = ComposerLightStyle.Aura }, "Claude"));
        Assert.Equal("Uiverse light \u00b7 OpenAI colours \u00b7 3.7 s a turn", PreviewCaption.Text(beam with { Style = ComposerLightStyle.Aura, Aura = UiverseAuraOptions.Default with { Scheme = AuraScheme.Maker, Speed = 3.69 } }, "OpenAI"));
        Assert.Equal("No light", PreviewCaption.Text(beam with { Style = ComposerLightStyle.Off }, "Claude"));
    }
}
