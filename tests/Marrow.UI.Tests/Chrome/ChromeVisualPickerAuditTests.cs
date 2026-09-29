using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Chrome;

// Audit fixes for UI/Chrome/ChromeVisualPicker.swift (B3 52bc792): option equality :40, the preview closure :44-46,
// the caption :76-84, the tile's default width :56. Expected values are read off the Swift, not the C#.
[Collection("Headless")]
public sealed class ChromeVisualPickerAuditTests(HeadlessSession headless)
{
    private static Window Show(Control content)
    {
        content.HorizontalAlignment = HorizontalAlignment.Left;
        content.VerticalAlignment = VerticalAlignment.Top;
        var window = new Window { Width = 400, Height = 300, Content = content };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    [Fact]
    [Trait("Req", "PR-THEME-182")] // ChromeVisualPicker.swift:40: isSelected is option.value == selection, and Swift's Double == says NaN != NaN (R-VAL-4)
    public void A_NaN_option_is_not_selected_by_a_NaN_selection() => headless.OnUiThread(() =>
    {
        using var frameSource = FrameClock.Source.Override(new ManualFrameSource());
        var cell = new CellSample<double>(double.NaN);
        var picker = new ChromeVisualPicker<double, TextBlock>(
            new[] { (double.NaN, "Not a number"), (1.0, "One") },
            new Binding<double>(() => cell.Value, v => cell.Value = v),
            value => new TextBlock { Text = "x" });
        var window = Show(picker);
        Assert.All(VisualQuery.Descendants<ChromeVisualTile>(picker), tile => Assert.False(tile.IsSelected));
        window.Close();
        Dispatcher.UIThread.RunJobs();
    });

    [Fact]
    [Trait("Req", "PR-THEME-183")] // ChromeVisualPicker.swift:76-84: lineLimit(2), and SwiftUI's default truncation is the tail ellipsis
    public void A_caption_longer_than_two_lines_ends_with_a_tail_ellipsis() => headless.OnUiThread(() =>
    {
        using var frameSource = FrameClock.Source.Override(new ManualFrameSource());
        const string title = "A very long option name that needs three lines";
        var cell = new CellSample<int>(1);
        var picker = new ChromeVisualPicker<int, TextBlock>(
            new[] { (1, title), (2, "Two") },
            new Binding<int>(() => cell.Value, v => cell.Value = v),
            value => new TextBlock { Text = "x" });
        var window = Show(picker);
        var caption = Assert.Single(VisualQuery.Descendants<TextBlock>(picker), t => t.Text == title);
        Assert.Equal(2, caption.MaxLines);
        Assert.Equal(TextTrimming.CharacterEllipsis, caption.TextTrimming);
        window.Close();
        Dispatcher.UIThread.RunJobs();
    });
}
