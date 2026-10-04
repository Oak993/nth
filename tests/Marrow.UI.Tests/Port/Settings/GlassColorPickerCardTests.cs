using System.Text.Json;
using Avalonia;
using Marrow.UI.Port;
using Marrow.UI.Port.Settings;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Port.Settings;

// Unit CZ-2 (OD-0099 Look Picks, Q-CZ-3 B; OD-0095): the picker lives in the glass popover with the Mac's arrow tip. SOURCE FACT: lp-pickers.js
// draws that tip with the header's numbers (card 13 pt from the anchor, tip 12 pt high, 12 pt half base, 3 pt point radius, 4 pt shoulders) and
// the path "M-16 12 Q-12 12 -9 9 L-2.1 2.1 Q0 0 2.1 2.1 L9 9 Q12 12 16 12"; gen-picker-fixture.mjs reads both out of the design into picker.json.
[Collection("Headless")]
public sealed class GlassColorPickerCardTests(HeadlessSession headless)
{
    private static JsonElement Design()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Marrow.sln"))) dir = dir.Parent;
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir!.FullName, "tests", "fixtures", "customization", "picker", "picker.json")));
        return json.RootElement.Clone();
    }

    [Fact]
    [Trait("Feature", "CZ-2")]
    [Trait("Rule", "R-TEST-4")]
    public void The_cards_tip_is_the_designs_tip() => headless.OnUiThread(() =>
    {
        var design = Design();
        var pop = design.GetProperty("popover");
        Assert.Equal(PopoverCard.TipHeight, pop.GetProperty("tipHeight").GetDouble());
        Assert.Equal(PopoverCard.TipHalfWidth, pop.GetProperty("tipHalfBase").GetDouble());
        Assert.Equal(PopoverCard.TipApexRadius, pop.GetProperty("apexRadius").GetDouble());
        Assert.Equal(PopoverCard.TipShoulderRadius, pop.GetProperty("shoulderRadius").GetDouble());
        Assert.Equal(PopoverCard.TipHeight + PopoverCard.TipGap, pop.GetProperty("cardGap").GetDouble());   // the card's edge 13 pt from the anchor
        var tip = design.GetProperty("tip");
        Assert.Equal(0, tip.GetProperty("apexY").GetDouble());
        Assert.Equal(PopoverCard.TipHeight, tip.GetProperty("baseY").GetDouble());       // the sharp tip is as high as the constant
        var start = tip.GetProperty("sideStart").EnumerateArray().Select(x => x.GetDouble()).ToArray();
        var end = tip.GetProperty("sideEnd").EnumerateArray().Select(x => x.GetDouble()).ToArray();
        Assert.Equal(end[0] - start[0], start[1] - end[1], 9);                              // the sides run at 45 degrees
        Assert.Equal(PopoverCard.TipHalfWidth / PopoverCard.TipHeight, 1);
    });

    [Fact]
    [Trait("Feature", "CZ-2")]
    [Trait("Rule", "R-TEST-4")]
    public void The_picker_hangs_in_a_card_whose_tip_stands_off_the_edge_facing_the_well() => headless.OnUiThread(() =>
    {
        var picker = new GlassColorPicker();
        var card = new PopoverCard { Child = new Avalonia.Controls.Border { Padding = GlassColorPicker.CardInset, Child = picker }, TipEdge = PopoverTipEdge.Top, TipOffset = 100 };
        card.Measure(Size.Infinity);
        card.Arrange(new Rect(card.DesiredSize));
        Assert.Equal(new Size(GlassColorPicker.PickerWidth + 12, Math.Ceiling(GlassColorPicker.FullHeight) + 12 + PopoverCard.TipHeight + PopoverCard.TipGap), card.DesiredSize);   // the popover is the picker plus 12 (lp-pickers.js place())
        Assert.Equal(100, card.TipPoint.X, 1);                                              // the tip is under the well, wherever the card sits
        Assert.Equal(PopoverCard.TipGap, card.TipPoint.Y, 1);
        Assert.InRange(card.Outline.Bounds.Top, PopoverCard.TipGap, PopoverCard.TipHeight + PopoverCard.TipGap - 9.5);   // the point, a rounding short of the sharp height
        Assert.Equal(new Point(6, 6 + PopoverCard.TipHeight + PopoverCard.TipGap), picker.TranslatePoint(new Point(0, 0), card));   // 6 pt in from the card's edge (the 1 pt edge and the inset)
    });
}
