using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Port;

// ED-217, source TimelineRows.swift:1969-1973. The Windows wrapped-last-line mapping is UNVERIFIED under OD-0001.
[Collection("Headless")]
public sealed class MiddleTruncatingTextTests(HeadlessSession headless)
{
    private static MiddleTruncatingTextBlock Text(string full, int limit)
    {
        var control = new MiddleTruncatingTextBlock { FullText = full };
        SwiftFont.System(12, design: SwiftFont.Design.Monospaced).Apply(control);
        var property = typeof(MiddleTruncatingTextBlock).GetProperty("LineLimit");
        Assert.NotNull(property);
        property.SetValue(control, limit);
        return control;
    }

    private static double Width(TextBlock control, string text) => new FormattedText(text,
        System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
        new Typeface(control.FontFamily, control.FontStyle, control.FontWeight, control.FontStretch), control.FontSize, null).Width;

    [Theory]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("first line\nsecond line")]
    [Trait("Decision", "ED-217")] // App/Views/Chat/TimelineRows.swift:1969-1973
    public void One_or_two_fitting_lines_are_unchanged(string full) => headless.OnUiThread(() =>
    {
        var control = Text(full, 2);
        control.Measure(new Size(300, double.PositiveInfinity));
        Assert.Equal(full, control.Text);
        Assert.InRange(control.TextLayout.TextLines.Count, 1, 2);
    });

    [Fact]
    [Trait("Decision", "ED-217")] // App/Views/Chat/TimelineRows.swift:1969-1973
    public void Overflow_keeps_the_normal_first_line_and_the_strings_own_tail_on_the_second() => headless.OnUiThread(() =>
    {
        var full = string.Join(" ", Enumerable.Repeat("read a long path", 12)) + " /source/last-file.cs";
        var control = Text(full, 2);
        var ordinary = new TextBlock { Text = full, FontFamily = control.FontFamily, FontSize = control.FontSize, TextWrapping = TextWrapping.Wrap };
        ordinary.Measure(new Size(180, double.PositiveInfinity));
        var first = ordinary.TextLayout.TextLines[0];
        var firstText = full.Substring(first.FirstTextSourceIndex, first.Length - first.NewLineLength);
        control.Measure(new Size(180, double.PositiveInfinity));
        Assert.Equal(2, control.TextLayout.TextLines.Count);
        var drawn = Assert.IsType<string>(control.Text);
        Assert.StartsWith(firstText, drawn, StringComparison.Ordinal);
        Assert.EndsWith("last-file.cs", drawn, StringComparison.Ordinal);
        Assert.Equal(1, drawn.Count(character => character == '\u2026'));
        Assert.DoesNotContain('\u2026', drawn[..firstText.Length]);
        Assert.True(control.TextLayout.TextLines.All(line => line.Width <= 180));
    });

    [Fact]
    [Trait("Decision", "ED-217")] // App/Views/Chat/TimelineRows.swift:1969-1973; single-line rule is unchanged
    public void A_one_line_limit_preserves_the_existing_even_head_tail_elision() => headless.OnUiThread(() =>
    {
        var control = Text("abcdefghij", 1);
        control.Measure(new Size(Width(control, "abc\u2026hij"), double.PositiveInfinity));
        Assert.Equal("abc\u2026hij", control.Text);
        control.Measure(new Size(1000, double.PositiveInfinity));
        Assert.Equal("abcdefghij", control.Text);
    });

    [Fact]
    [Trait("Decision", "ED-217")] // App/Views/Chat/TimelineRows.swift:1969-1973; last line holds the rest, including the whole string's tail
    public void Explicit_line_breaks_in_an_overflowing_remainder_do_not_hide_the_tail() => headless.OnUiThread(() =>
    {
        var control = Text("first line\nsecond line\nlast-file.cs", 2);
        control.Measure(new Size(600, double.PositiveInfinity));
        var drawn = Assert.IsType<string>(control.Text);
        Assert.StartsWith("first line\n", drawn, StringComparison.Ordinal);
        Assert.Equal(1, drawn.Count(character => character == '\n'));
        Assert.Equal(1, drawn.Count(character => character == '\u2026'));
        Assert.EndsWith("last-file.cs", drawn, StringComparison.Ordinal);
        Assert.Equal(2, control.TextLayout.TextLines.Count);
    });
}
