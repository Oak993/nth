using Avalonia;
using Avalonia.Input;
using Marrow.UI.Port.Settings;
using Marrow.UI.Port.Settings.ColorPicker;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Port.Settings;

// Unit CZ-2 (OD-0099 Look Picks, Q-CZ-3 B): Marrow's glass colour picker, drawn after lp-pickers.js variant B: the square, the hue strip, the
// opacity strip, the hex field, the recent colours; the colour handed out as #RRGGBBAA. Pointer values are the design's (PickerMathTests holds
// them to node's output); these tests drive the control. The design states no keyboard; the scheme is ED-NEW-f-cz-2.
[Collection("Headless")]
public sealed class GlassColorPickerTests(HeadlessSession headless)
{
    private static GlassColorPicker Make(string? value = null, List<(string Value, bool Commit)>? log = null)
    {
        var picker = new GlassColorPicker();
        if (value is not null) picker.Value = value;
        if (log is not null) picker.Changed += (v, c) => log.Add((v, c));
        picker.Measure(Size.Infinity);
        picker.Arrange(new Rect(picker.DesiredSize));
        return picker;
    }

    private static void Press(PickerSurface surface, Key key, KeyModifiers modifiers = KeyModifiers.None)
    {
        surface.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = modifiers });
        surface.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyUpEvent, Key = key, KeyModifiers = modifiers });
    }

    [Fact]
    [Trait("Feature", "CZ-2")]
    [Trait("Rule", "R-TEST-4")]
    public void The_picker_is_260_wide_with_the_squares_236_by_150_and_opens_on_the_designs_first_colour() => headless.OnUiThread(() =>
    {
        var picker = Make();
        Assert.Equal(260, picker.DesiredSize.Width);
        Assert.Equal(new Rect(6, 6, 248, 150), picker.Square.Bounds);
        Assert.Equal(248, picker.Hue.Bounds.Width);
        Assert.Equal(248, picker.OpacityStrip.Bounds.Width);
        Assert.Equal(14, picker.Hue.Bounds.Height);
        Assert.Equal("#8E7CFFFF", picker.Value);
        Assert.Equal("8E7CFF", picker.HexBox.Text);
        Assert.Equal("100 %", picker.Percent.Text);
        Assert.Equal(["#8E7CFF", "#38C9B4"], picker.Recent.Items);
    });

    [Fact]
    [Trait("Feature", "CZ-2")]
    [Trait("Rule", "R-TEST-4")]
    public void A_value_is_stored_with_its_alpha_and_a_six_digit_value_reads_opaque() => headless.OnUiThread(() =>
    {
        var log = new List<(string, bool)>();
        var picker = Make("#38C9B440", log);
        Assert.Equal("#38C9B440", picker.Value);
        Assert.Equal("25 %", picker.Percent.Text);
        Assert.Equal("38C9B4", picker.HexBox.Text);
        picker.Value = "#102030";
        Assert.Equal("#102030FF", picker.Value);
        picker.Value = "nonsense";
        Assert.Equal("#102030FF", picker.Value);
        Assert.Empty(log);                            // moving the picker from outside is not an edit
    });

    [Fact]
    [Trait("Feature", "CZ-2")]
    [Trait("Rule", "R-TEST-4")]
    public void The_pointer_edits_while_it_moves_and_commits_on_release_and_the_recent_list_follows_the_commits() => headless.OnUiThread(() =>
    {
        var log = new List<(string Value, bool Commit)>();
        var picker = Make("#FF0000", log);
        picker.Square.Point(new Point(124, 75), false);               // half saturation, half value: (127.5, 63.75, 63.75) rounds half up to 80 40 40
        picker.Square.Point(new Point(248, 0), true);                 // the top right corner: full saturation, full value
        picker.OpacityStrip.Point(new Point(124, 7), true);                // the middle of the strip: round(0.5 * 100) / 100
        picker.Hue.Point(new Point(124, 7), true);                    // the middle of the hue strip: 179.95 degrees, cyan
        Assert.Equal([("#804040FF", false), ("#FF0000FF", true), ("#FF000080", true), ("#00FFFF80", true)], log);
        Assert.Equal("50 %", picker.Percent.Text);
        Assert.Equal(["#00FFFF", "#FF0000", "#8E7CFF", "#38C9B4"], picker.Recent.Items);   // a release moves its colour to the front; the same colour twice stays once
    });

    [Fact]
    [Trait("Feature", "CZ-2")]
    [Trait("Rule", "R-TEST-4")]
    public void A_recent_swatch_keeps_the_current_alpha_and_moves_to_the_front() => headless.OnUiThread(() =>
    {
        var log = new List<(string Value, bool Commit)>();
        var picker = Make("#FF000080", log);
        picker.RecentRow.Point(new Point(12 + 26, 12), false);        // the second swatch (24 px boxes, 2 px apart)
        Assert.Equal([("#38C9B480", true)], log);
        Assert.Equal(["#38C9B4", "#8E7CFF"], picker.Recent.Items);
        picker.RecentRow.Point(new Point(25, 12), false);             // between two swatches: nothing
        picker.RecentRow.Point(new Point(12 + 2 * 26, 12), false);    // past the last
        Assert.Single(log);
    });

    [Fact]
    [Trait("Feature", "CZ-2")]
    [Trait("Rule", "R-TEST-4")]
    public void The_hex_field_takes_six_or_eight_digits_and_a_bad_entry_shows_the_colour_again() => headless.OnUiThread(() =>
    {
        var log = new List<(string Value, bool Commit)>();
        var picker = Make("#FF0000", log);
        picker.HexBox.Text = "#12345680";
        picker.EnterHex();
        Assert.Equal([("#12345680", true)], log);
        Assert.Equal("123456", picker.HexBox.Text);
        Assert.Equal("50 %", picker.Percent.Text);
        picker.HexBox.Text = "abcdef";                               // six digits keep the alpha
        picker.EnterHex();
        Assert.Equal("#ABCDEF80", picker.Value);
        picker.HexBox.Text = "12ab";
        picker.EnterHex();
        Assert.Equal(2, log.Count);
        Assert.Equal("ABCDEF", picker.HexBox.Text);
        Assert.Equal(["#ABCDEF", "#123456", "#8E7CFF", "#38C9B4"], picker.Recent.Items);   // the opening colour #FF0000 was never committed
    });

    [Fact]
    [Trait("Feature", "CZ-2")]
    [Trait("Rule", "R-TEST-4")]
    public void Arrow_keys_step_the_square_and_a_key_release_commits() => headless.OnUiThread(() =>
    {
        var log = new List<(string Value, bool Commit)>();
        var picker = Make("#FF0000", log);
        Press(picker.Square, Key.Left);                               // a hundredth less saturated
        Assert.Equal([("#FF0303FF", false), ("#FF0303FF", true)], log);
        Press(picker.Square, Key.Down, KeyModifiers.Shift);           // Shift: a tenth less bright
        Assert.Equal("#E60202FF", picker.Value);
        Press(picker.Square, Key.Right, KeyModifiers.Shift);          // clamped at full saturation
        Press(picker.Square, Key.Up);
        Assert.Equal("#E80000FF", picker.Value);
        Assert.Equal(1, picker.Square.Colour.S);
        Assert.Equal(0.91, picker.Square.Colour.V, 6);
        Press(picker.Square, Key.A);                                  // any other key is not the square's
        Assert.Equal(0.91, picker.Square.Colour.V, 6);
    });

    [Fact]
    [Trait("Feature", "CZ-2")]
    [Trait("Rule", "R-TEST-4")]
    public void Arrow_keys_step_the_hue_and_the_opacity_and_Home_and_End_go_to_the_ends() => headless.OnUiThread(() =>
    {
        var picker = Make("#FF0000");
        Press(picker.Hue, Key.Right);                                 // a degree
        Assert.Equal("#FF0400FF", picker.Value);
        Press(picker.Hue, Key.Right, KeyModifiers.Shift);             // ten degrees
        Assert.Equal(11, picker.Hue.Colour.H, 6);
        Assert.Equal("#FF2F00FF", picker.Value);
        Press(picker.Hue, Key.End);
        Assert.Equal(PickerMath.HueMax, picker.Hue.Colour.H, 6);
        Press(picker.Hue, Key.Home);
        Assert.Equal(0, picker.Hue.Colour.H);
        Press(picker.OpacityStrip, Key.Left, KeyModifiers.Shift);          // a tenth less opaque
        Assert.Equal("#FF0000E6", picker.Value);
        Press(picker.OpacityStrip, Key.Right);                             // a hundredth more: 0.91
        Assert.Equal("#FF0000E8", picker.Value);
        Press(picker.OpacityStrip, Key.Home);
        Assert.Equal("#FF000000", picker.Value);
        Press(picker.OpacityStrip, Key.End);
        Assert.Equal("#FF0000FF", picker.Value);
    });

    [Fact]
    [Trait("Feature", "CZ-2")]
    [Trait("Rule", "R-TEST-4")]
    public void The_recent_row_is_walked_with_the_arrows_and_Enter_picks() => headless.OnUiThread(() =>
    {
        var log = new List<(string Value, bool Commit)>();
        var picker = Make("#FF000080", log);
        Press(picker.RecentRow, Key.Right);
        Assert.Equal(1, picker.RecentRow.RecentCursor);
        Press(picker.RecentRow, Key.Right);                           // the last stays
        Assert.Equal(1, picker.RecentRow.RecentCursor);
        Assert.Empty(log);
        Press(picker.RecentRow, Key.Enter);
        Assert.Equal([("#38C9B480", true)], log);
        Assert.Equal(["#38C9B4", "#8E7CFF"], picker.Recent.Items);
        Press(picker.RecentRow, Key.Left);
        Assert.Equal(0, picker.RecentRow.RecentCursor);
    });
}
