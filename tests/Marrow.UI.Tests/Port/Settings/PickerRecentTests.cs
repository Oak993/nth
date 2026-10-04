using System.Text.Json;
using Marrow.UI.Port.Settings.ColorPicker;

namespace Marrow.UI.Tests.Port.Settings;

// Unit CZ-2 (OD-0099 Look Picks, Q-CZ-3 B). SOURCE FACT (lp-pickers.js, set()): on a commit the colour's #RRGGBB goes to the front, a
// duplicate leaves, eight stay; the list opens with AURA_DEFAULTS' two colours. The sequences are what node computes from the design's own
// expression (tests/fixtures/customization/picker/picker.json).
public sealed class PickerRecentTests
{
    private static JsonElement Recent()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Marrow.sln"))) dir = dir.Parent;
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir!.FullName, "tests", "fixtures", "customization", "picker", "picker.json")));
        return json.RootElement.GetProperty("recent").Clone();
    }

    private static string[] Strings(JsonElement e) => e.EnumerateArray().Select(x => x.GetString()!).ToArray();

    [Fact]
    [Trait("Feature", "CZ-2")]
    [Trait("Rule", "R-TEST-4")]
    public void The_list_follows_the_designs_order_step_by_step()
    {
        var want = Recent();
        var recent = new PickerRecent();
        Assert.Equal(Strings(want.GetProperty("initial")), recent.Items);
        Assert.Equal(["#8E7CFF", "#38C9B4"], recent.Items);
        foreach (var step in want.GetProperty("steps").EnumerateArray())
        {
            recent.Commit(step.GetProperty("hx").GetString()!);
            Assert.Equal(Strings(step.GetProperty("after")), recent.Items);
        }
        Assert.Equal(8, recent.Items.Count);
        Assert.Equal("#445566", recent.Items[0]);
    }

    [Fact]
    [Trait("Feature", "CZ-2")]
    [Trait("Rule", "R-TEST-4")]
    public void A_colour_already_in_the_list_moves_to_the_front_and_the_ninth_drops_off()
    {
        var recent = new PickerRecent(["#000001", "#000002", "#000003", "#000004", "#000005", "#000006", "#000007", "#000008"]);
        recent.Commit("#000005");
        Assert.Equal(["#000005", "#000001", "#000002", "#000003", "#000004", "#000006", "#000007", "#000008"], recent.Items);
        recent.Commit("#0000FF");
        Assert.Equal(["#0000FF", "#000005", "#000001", "#000002", "#000003", "#000004", "#000006", "#000007"], recent.Items);
    }
}
