using System.Text.Json;
using Marrow.Core.Port.Settings;

namespace Marrow.Core.Tests.Port.Settings;

// CZ-1: expected values are emitted by node from the locked board and the spec's tables, not this port.
public sealed class CzTextTests
{
    private static JsonDocument Fixture()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Marrow.sln"))) dir = dir.Parent;
        return JsonDocument.Parse(File.ReadAllBytes(Path.Combine(dir!.FullName, "tests/fixtures/customization/expected.json")));
    }

    [Fact]
    [Trait("Feature", "CZ-1")]
    [Trait("Spec", "CZ-T6")]
    [Trait("Decision", "ED-251")]
    public void Every_label_detail_and_caption_is_byte_equal_to_the_locked_tables()
    {
        using var f = Fixture();
        Assert.Equal(f.RootElement.GetProperty("texts").EnumerateArray().Select(x => x.GetString()), CzText.TableText);
        foreach (var text in f.RootElement.GetProperty("designTexts").EnumerateArray()) Assert.Contains(text.GetString()!, CzText.TableText);
        foreach (var pair in f.RootElement.GetProperty("constants").GetProperty("style").EnumerateObject()) Assert.Equal(pair.Value.GetString(), CzText.StyleTitles[pair.Name]);
        foreach (var pair in f.RootElement.GetProperty("constants").GetProperty("description").EnumerateObject()) Assert.Equal(pair.Value.GetString(), CzText.StyleDescriptions[pair.Name]);
        foreach (var pair in f.RootElement.GetProperty("constants").GetProperty("palettes").EnumerateObject()) Assert.Equal(pair.Value.GetString(), CzText.PaletteNames[pair.Name]);
        foreach (var pair in f.RootElement.GetProperty("constants").GetProperty("shape").EnumerateObject()) Assert.Equal(pair.Value.GetString(), CzText.ShapeDetails[pair.Name]);
        Assert.Equal("The model in use: Claude", CzText.AuraColours("maker", "Claude", "Blue"));
        Assert.Equal("Your accent: Blue", CzText.AuraColours("accent", "Claude", "Blue"));
    }

    [Fact]
    [Trait("Feature", "CZ-1")]
    [Trait("Spec", "CZ-T7")]
    [Trait("Decision", "ED-251")]
    public void Fmt_matches_node_for_halves_negatives_zero_and_exponent_boundaries()
    {
        using var f = Fixture();
        foreach (var r in f.RootElement.GetProperty("fmt").EnumerateArray())
            Assert.Equal(r.GetProperty("text").GetString(), CzText.Fmt(r.GetProperty("v").GetDouble(), r.GetProperty("n").GetInt32()));
    }

    [Fact]
    [Trait("Feature", "CZ-1")]
    [Trait("Spec", "CZ-T8")]
    [Trait("Decision", "ED-251")]
    public void Every_slider_range_snaps_clamped_values_and_halves_like_node()
    {
        using var f = Fixture();
        foreach (var r in f.RootElement.GetProperty("snap").EnumerateArray())
            Assert.Equal(r.GetProperty("result").GetDouble(), CzText.Snap(r.GetProperty("v").GetDouble(), r.GetProperty("min").GetDouble(), r.GetProperty("max").GetDouble(), r.GetProperty("step").GetDouble()));
    }

    [Fact]
    [Trait("Feature", "CZ-1")]
    [Trait("Spec", "CZ-T30")]
    [Trait("Decision", "ED-251")]
    public void Tone_matches_the_actual_design_function_for_legibility_fallback_and_channel_rounding()
    {
        // All thirty makers, both themes, four stops, plus fallback/threshold boundary fixtures.
        using var f = Fixture();
        foreach (var r in f.RootElement.GetProperty("tone").EnumerateArray())
            Assert.Equal(r.GetProperty("text").GetString(), CzText.Tone(r.GetProperty("hex").GetString()!, r.GetProperty("dark").GetBoolean(), r.GetProperty("title").GetString()!));
    }
}
