using System.Text.RegularExpressions;
using Marrow.UI.Port.Settings;
using Marrow.UI.Port.Settings.ColorPicker;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Port.Settings;

// Unit CZ-2, job 1 (OD-0099 Look Picks, Q-CZ-3 B): every size, gap and radius of the picker is lp-page.css's for variant B. The tests read the
// stylesheet (porting/first-release/design-src/look-picks/lp-page.css) and hold the control's numbers to its declarations.
[Collection("Headless")]
public sealed class GlassColorPickerStyleTests(HeadlessSession headless)
{
    private static string Css { get; } = Load();

    private static string Load()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Marrow.sln"))) dir = dir.Parent;
        return File.ReadAllText(Path.Combine(dir!.FullName, "porting", "first-release", "design-src", "look-picks", "lp-page.css"));
    }

    /// <summary>The declarations of the rule whose selector is exactly <paramref name="selector"/>, as "property" to "value".</summary>
    private static Dictionary<string, string> Rule(string selector)
    {
        var m = Regex.Match(Css, "(?:^|\\})" + Regex.Escape(selector) + "\\{([^}]*)\\}", RegexOptions.Multiline | RegexOptions.CultureInvariant);
        Assert.True(m.Success, "no rule for " + selector);
        return m.Groups[1].Value.Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(d => d.Trim()).Where(d => d.Contains(':', StringComparison.Ordinal))
            .ToDictionary(d => d[..d.IndexOf(':', StringComparison.Ordinal)].Trim(), d => d[(d.IndexOf(':', StringComparison.Ordinal) + 1)..].Trim(), StringComparer.Ordinal);
    }

    private static double Px(string value) => double.Parse(value.Replace("px", "", StringComparison.Ordinal), System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    [Trait("Feature", "CZ-2")]
    [Trait("Rule", "R-TEST-4")]
    public void The_card_the_picker_and_the_square_are_the_stylesheets()
    {
        var mg = Rule(".mg,.mw,.ms");
        Assert.Equal(GlassColorPicker.PickerWidth, Px(mg["width"]));
        Assert.Equal(GlassColorPicker.Padding, Px(mg["padding"]));
        Assert.Equal(GlassColorPicker.Gap, Px(mg["gap"]));
        var pk = Rule(".cz-pk");                                       // the card's 6 px; the popover is the picker plus 12
        Assert.Equal(Px(pk["top"]), Px(pk["left"]));
        Assert.Equal(12, 2 * Px(pk["left"]));
        Assert.Equal(1 + GlassColorPicker.CardInset.Left, Px(pk["left"]));   // the card's 1 pt edge plus the inset
        var sv = Rule(".mg-sv");
        Assert.Equal(PickerSurface.SquareHeight, Px(sv["height"]));
        Assert.Equal(PickerSurface.SquareRadius, Px(sv["border-radius"]));
        Assert.Contains("inset 0 0 0 .5px", sv["box-shadow"], StringComparison.Ordinal);
        Assert.Equal(0.5, PickerSurface.RimWidth);
        var th = Rule(".mg-sv .th,.mw-wheel .th");
        Assert.Equal(PickerSurface.ThumbSize, Px(th["width"]));
        Assert.Equal(PickerSurface.ThumbSize, Px(th["height"]));
        Assert.Contains("0 0 0 2px #fff", th["box-shadow"], StringComparison.Ordinal);
        Assert.Equal(2, PickerSurface.ThumbRing);
        Assert.Equal(PickerMath.SquareThumb, PickerSurface.ThumbSize);
    }

    [Fact]
    [Trait("Feature", "CZ-2")]
    [Trait("Rule", "R-TEST-4")]
    public void The_strips_and_their_thumbs_are_the_stylesheets()
    {
        var strip = Rule(".mg-strip");
        Assert.Equal(PickerSurface.StripHeight, Px(strip["height"]));
        Assert.Equal(PickerSurface.StripRadius, Px(strip["border-radius"]));
        var th = Rule(".mg-strip .th");
        Assert.Equal(PickerSurface.StripThumbTop, Px(th["top"]));
        Assert.Equal(PickerSurface.StripThumbLeft, Px(th["margin-left"]));
        Assert.Equal(PickerSurface.ThumbSize, Px(th["width"]));
        Assert.Equal(PickerSurface.ThumbSize, Px(th["height"]));
        Assert.Contains("0 1px 4px rgba(0,0,0,.4)", th["box-shadow"], StringComparison.Ordinal);
        Assert.Equal(PickerSurface.ThumbSize, Px(Rule(".mg-sv .th,.mw-wheel .th")["width"]));
        Assert.Contains("0 1px 4px rgba(0,0,0,.45)", Rule(".mg-sv .th,.mw-wheel .th")["box-shadow"], StringComparison.Ordinal);
        Assert.Matches(@"/8px 8px", Rule(".chk")["background"]);       // the checker's cell
        Assert.Equal(8, PickerSurface.CheckCell);
    }

    [Fact]
    [Trait("Feature", "CZ-2")]
    [Trait("Rule", "R-TEST-4")]
    public void The_hex_row_the_caption_and_the_swatches_are_the_stylesheets()
    {
        var row = Rule(".mg-hexrow");
        Assert.Equal(GlassColorPicker.HexRowGap, Px(row["gap"]));
        var hex = Rule(".mg-hex");
        Assert.Equal(GlassColorPicker.HexHeight, Px(hex["height"]));
        Assert.Equal(GlassColorPicker.HexRadius, Px(hex["border-radius"]));
        Assert.Equal(GlassColorPicker.HexInnerGap, Px(hex["gap"]));
        Assert.Equal("0 10px", hex["padding"]);
        Assert.Equal(GlassColorPicker.HexPadding, 10);
        Assert.Equal(GlassColorPicker.PercentWidth, Px(Rule(".mg-pct")["min-width"]));
        var dot = Rule(".mg-dot");
        Assert.Equal(GlassColorPicker.DotSize, Px(dot["width"]));
        Assert.Equal(GlassColorPicker.DotSize, Px(dot["height"]));
        var cap = Rule(".mg-cap");
        Assert.Equal(11, Px(cap["font-size"]));
        Assert.Equal(GlassColorPicker.CaptionMargin, Px(cap["margin-bottom"]));
        Assert.Equal(PickerSurface.SwatchGap, Px(Rule(".sws")["gap"]));
        Assert.Equal(PickerSurface.SwatchBox, Px(Rule(".sws")["min-height"]));
        var sw = Rule(".sw");
        Assert.Equal(PickerSurface.SwatchBox, Px(sw["width"]));
        Assert.Equal(PickerSurface.SwatchBox, Px(sw["height"]));
        Assert.Equal(PickerSurface.SwatchDot, Px(Rule(".sw i")["width"]));
        var ring = Rule(".sw::after");
        Assert.Equal(PickerSurface.RingInset, Px(ring["inset"]));
        Assert.Contains("inset 0 0 0 1.5px var(--ring)", ring["box-shadow"], StringComparison.Ordinal);
        Assert.Equal(1.5, PickerSurface.RingWidth);
    }

    [Fact]
    [Trait("Feature", "CZ-2")]
    [Trait("Rule", "R-TEST-4")]
    public void The_laid_out_picker_has_the_stylesheets_box_model() => headless.OnUiThread(() =>
    {
        var picker = new GlassColorPicker();
        picker.Measure(Avalonia.Size.Infinity);
        picker.Arrange(new Avalonia.Rect(picker.DesiredSize));
        // 6 + 150 + 10 + 14 + 10 + 14 + 10 + 28 + 10 + 14.3 + (10 - 4) + 24 + 6
        Assert.Equal(302.3, GlassColorPicker.FullHeight, 6);
        Assert.Equal(Math.Ceiling(GlassColorPicker.FullHeight), picker.DesiredSize.Height);   // layout rounding takes the desired size up to a whole pixel
        Assert.Equal(new Avalonia.Rect(6, 6, 248, 150), picker.Square.Bounds);
        Assert.Equal(new Avalonia.Rect(6, 166, 248, 14), picker.Hue.Bounds);
        Assert.Equal(new Avalonia.Rect(6, 190, 248, 14), picker.OpacityStrip.Bounds);
        Assert.Equal(new Avalonia.Rect(6, 214, 248 - 42 - 22 - 16, 28), picker.Pill.Bounds);           // flex: 1 between the row's edge and the percent
        Assert.Equal((182.0, 42.0), (picker.Percent.Bounds.X, picker.Percent.Bounds.Width));
        Assert.Equal(228, picker.Percent.Bounds.Y + picker.Percent.Bounds.Height / 2, 0.51);   // centred in the 28 px row (align-items: center)
        Assert.Equal(new Avalonia.Rect(6 + 168 + 8 + 42 + 8, 217, 22, 22), picker.Dot.Bounds);
        Assert.Equal(6 + 168 + 8 + 42 + 8 + 22, 254);                                                  // the row ends at the padding
        Assert.InRange(picker.RecentRow.Bounds.Y, 272, 272.5);                                             // the caption's top 252, its 14.3, then 10 - 4
        Assert.Equal((248.0, 24.0), (picker.RecentRow.Bounds.Width, picker.RecentRow.Bounds.Height));
        Assert.InRange(picker.RecentRow.Bounds.Bottom + 6, 302, 303);   // whole pixels after layout rounding
    });
}
