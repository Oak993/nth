using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.App.Views.Diff;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Common;
using Marrow.UI.Port;
using AvaloniaWindow = Avalonia.Controls.Window;
using ChromeTokens = Marrow.UI.Chrome.Chrome;
using static Marrow.App.Tests.Fixtures.TimelineRowsFinChecks;
using static Marrow.App.Tests.Views.Chat.TimelineRowsPrSupport;

namespace Marrow.App.Tests.Views.Chat;

// Source: DroppyCode/App/Views/Chat/TimelineRows.swift (B3 52bc792), :2094-2167 ToolDetailView (the expanded tool row's
// image, detail, diffs and output) and :1710-1713 where the row hangs it.
[Collection("Headless")]
public sealed class TimelineRowsFinToolDetailTests(HeadlessSession headless)
{
    private static string Lines(int count) => string.Join("\n", Enumerable.Range(1, count).Select(i => "line " + i));

    private static readonly FontFamily Mono = new("fonts:CascadiaMono#Cascadia Mono");

    private static StackPanel Column(ToolDetailView view) => Assert.IsType<StackPanel>(view.Child);

    [Fact]
    [Trait("Req", "UI-CHAT-027")] // TimelineRows.swift:2120-2126 the detail text
    [Trait("Req", "PR-CHAT-166")] // TimelineRows.swift:2152 tool output enables text selection (and :2124 the detail)
    public void The_detail_is_selectable_secondary_monospace_text_capped_at_twelve_lines_above_the_output_and_omitted_when_empty() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        runtime.OnMain(() =>
        {
            var detailText = Lines(20);
            var view = new ToolDetailView(Call(ToolCall.Kind.Command, "cat") with { Detail = detailText, Output = "result" });
            var withoutDetail = new ToolDetailView(Call(ToolCall.Kind.Command, "cat") with { Detail = null, Output = "result" });
            var emptyDetail = new ToolDetailView(Call(ToolCall.Kind.Command, "cat") with { Detail = "", Output = "result" });
            var panel = new StackPanel { Width = 500 };
            foreach (var item in new Control[] { view, withoutDetail, emptyDetail }) panel.Children.Add(item);
            var window = Show(panel);
            try
            {
                var detail = Assert.Single(Mounted<SelectableTextBlock>(view), block => block.Text == detailText);
                // :2125 .lineLimit(12) is a display limit only: the stored detail is untouched and all of it is the text.
                Assert.Equal(12, detail.MaxLines);
                Assert.Equal(detailText, detail.Text);
                // :2122-2123 .font(.chat(.caption, design: .monospaced)) in Chrome.secondaryText; the face is the monospace one.
                Assert.Equal(10, detail.FontSize);
                Assert.Equal(Mono, detail.FontFamily);
                Assert.Equal(FontWeight.Normal, detail.FontWeight);
                Paints(detail.Foreground, ChromeTokens.SecondaryText, detail, "detail text");
                // :2116 inline above the output (the first of the column's rows, a row gap before the output).
                var rows = Column(view).Children;
                Assert.Same(detail, rows[0]);
                Assert.Equal(2, rows.Count);
                Assert.Equal(20, rows[1].TranslatePoint(default, window)!.Value.Y - (detail.TranslatePoint(default, window)!.Value.Y + detail.Bounds.Height), 0.5);
                // :2120 omitted when the detail is null or empty.
                Assert.Single(Column(withoutDetail).Children);
                Assert.Single(Column(emptyDetail).Children);
                Assert.DoesNotContain(Mounted<SelectableTextBlock>(withoutDetail), block => block.MaxLines == 12);
            }
            finally { Close(window); }
        });
    });

    [Fact]
    [Trait("Req", "UI-CHAT-025")] // TimelineRows.swift:2141-2164 the output block
    [Trait("Req", "UI-CHAT-026")] // TimelineRows.swift:2156-2162 the Show full output link
    [Trait("Req", "PR-CHAT-166")] // TimelineRows.swift:2152 tool output enables text selection
    public void The_output_is_a_trimmed_inline_selectable_plate_collapsed_to_thirty_lines_with_a_link_that_shows_the_rest() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        ToolDetailView long_ = null!, exactly = null!;
        AvaloniaWindow window = null!;
        runtime.OnMain(() =>
        {
            long_ = new ToolDetailView(Call(ToolCall.Kind.Command, "cat") with { Output = "\n\n" + Lines(142) + "\n\n" });
            exactly = new ToolDetailView(Call(ToolCall.Kind.Command, "cat") with { Output = "\n" + Lines(30) + "\n" });
            var panel = new StackPanel { Width = 500, Spacing = 40 };
            panel.Children.Add(long_);
            panel.Children.Add(exactly);
            window = Show(panel);
            try
            {
                var plate = Mounted<Border>(long_).Single(border => border.Padding == new Thickness(10));
                var text = Assert.Single(Mounted<SelectableTextBlock>(plate));
                // :2145-2148 trimmed of leading and trailing newlines, then collapsed to the first 30 lines.
                Assert.Equal(Lines(30), text.Text);
                // :2151 caption monospaced; no foreground style of its own (so the primary label colour).
                Assert.Equal(10, text.FontSize);
                Assert.Equal(Mono, text.FontFamily);
                Paints(text.Foreground, SwiftColor.Primary, text, "output text");
                // :2153-2155 padding 10, a 10-point rounded plate of .quaternary at 0.45, as wide as the detail.
                Assert.Equal(new CornerRadius(10), plate.CornerRadius);
                Paints(plate.Background, SwiftColor.Quaternary.Opacity(0.45), plate, "output plate");
                Assert.Equal(500, plate.Bounds.Width, 0.5);
                Assert.Equal(Left(long_, window), Left(plate, window), 0.5);
                // :2156-2162 the link: exactly the total line count in parentheses, caption type, never a singular.
                var link = Assert.Single(Mounted<Button>(long_));
                var title = Assert.Single(Mounted<TextBlock>(link));
                Assert.Equal("Show full output (142 lines)", title.Text);
                Face(title, 10, FontWeight.Normal, ".chat(.caption)");
                Assert.Equal(Left(long_, window), Left(link, window), 0.5);
                Assert.Equal(20, Top(link, window) - (Top(plate, window) + plate.Bounds.Height), 0.5);
                // Exactly 30 lines after the trim: the whole output and no link.
                Assert.Equal(Lines(30), Assert.Single(Mounted<SelectableTextBlock>(exactly)).Text);
                Assert.Empty(Mounted<Button>(exactly));
                Click(window, link);
            }
            catch { Close(window); throw; }
        });
        runtime.OnMain(() =>
        {
            try
            {
                Settle(window);
                // Expanded: the whole output, and the label becomes exactly Show less; the link stays.
                Assert.Equal(Lines(142), Assert.Single(Mounted<SelectableTextBlock>(long_)).Text);
                Assert.Equal("Show less", Assert.Single(Mounted<TextBlock>(Assert.Single(Mounted<Button>(long_)))).Text);
                // The expanded output is taller than the window, so the second tap is the button's own click.
                Assert.Single(Mounted<Button>(long_)).RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            }
            catch { Close(window); throw; }
        });
        runtime.OnMain(() =>
        {
            try
            {
                Settle(window);
                Assert.Equal("Show full output (142 lines)", Assert.Single(Mounted<TextBlock>(Assert.Single(Mounted<Button>(long_)))).Text);
                Assert.Equal(Lines(30), Assert.Single(Mounted<SelectableTextBlock>(long_)).Text);
            }
            finally { Close(window); }
        });
    });

    private static double Top(Control control, Visual root) => control.TranslatePoint(default, root)!.Value.Y;

    [Fact]
    [Trait("Req", "UI-CHAT-028")] // TimelineRows.swift:2127-2140 the per-file diff block
    public void Each_edit_with_a_diff_is_one_ten_point_plate_with_a_padded_path_above_numberless_diff_lines() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        runtime.OnMain(() =>
        {
            var call = Call(ToolCall.Kind.Edit, "e") with
            {
                Edits = new ValueList<FileEdit>(
                [
                    new FileEdit("src/a.swift", "@@ -1 +1 @@\n-a\n+b\n", 1, 1),
                    new FileEdit("src/empty.swift", "", 0, 0),
                    new FileEdit("src/none.swift", null, 0, 0),
                    new FileEdit("src/b.swift", "@@ -2 +2 @@\n-c\n+d\n", 1, 1),
                ]),
            };
            // Inspected without a layout pass: DiffLinesView's own font (DiffInspector's) does not resolve headless.
            var view = new ToolDetailView(call);
            // :2128 one block per edit that carries a non-empty diff.
            var plates = Column(view).Children.OfType<Border>().ToList();
            Assert.Equal(2, plates.Count);
            Assert.DoesNotContain(Mounted<TextBlock>(view), block => block.Text is "src/empty.swift" or "src/none.swift");
            foreach (var (plate, name, diff) in new[] { (plates[0], "src/a.swift", "@@ -1 +1 @@\n-a\n+b\n"), (plates[1], "src/b.swift", "@@ -2 +2 @@\n-c\n+d\n") })
            {
                var file = Assert.IsType<StackPanel>(plate.Child);
                var path = Assert.IsType<TextBlock>(file.Children[0]);
                Assert.Equal(name, path.Text);
                // :2133-2134 the path row is padded 10 horizontally and 6 vertically; :2131-2132 caption, Chrome.secondaryText.
                Assert.Equal(new Thickness(10, 6), path.Margin);
                Assert.Equal(10, path.FontSize);
                Assert.Equal(FontWeight.Normal, path.FontWeight);
                Assert.Equal(Inter, path.FontFamily);
                Paints(path.Foreground, ChromeTokens.SecondaryText, path, "path");
                Assert.Equal(0, ((StackPanel)file).Spacing);
                // :2135 the diff lines without line numbers, below the path.
                var lines = Assert.IsType<DiffLinesView>(file.Children[1]);
                var parsed = DiffDetailCache.File(diff, name);
                var numberless = LogicalQuery.SelfAndDescendants<TextBlock>(new DiffLinesView(parsed, showsLineNumbers: false)).Count();
                Assert.True(numberless < LogicalQuery.SelfAndDescendants<TextBlock>(new DiffLinesView(parsed, showsLineNumbers: true)).Count(), "line numbers are drawn either way");
                Assert.Equal(numberless, LogicalQuery.SelfAndDescendants<TextBlock>(lines).Count());
                // :2137-2138 filled .quaternary at 0.45 and clipped to a 10-point continuous rounded rectangle.
                Assert.Equal(new CornerRadius(10), plate.CornerRadius);
                Assert.True(plate.ClipToBounds);
                Paints(plate.Background, SwiftColor.Quaternary.Opacity(0.45), plate, "diff plate");
            }
        });
    });

    [Fact]
    [Trait("Req", "PR-COMPOSER-211")] // TimelineRows.swift:2117-2119; AttachmentPreview.swift:398-438
    public void An_image_a_call_names_is_shown_above_its_detail_in_a_420_point_plate_with_its_file_name_and_an_Open_link_whatever_the_kind() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        var opened = new List<string>();
        var previous = ShellWorkspace.OpenWithDefaultApp;
        ShellWorkspace.OpenWithDefaultApp = opened.Add;
        try
        {
            h.Main.OnMain(() =>
            {
                var path = Path.Combine(h.Project.Path, "screenshot.png");
                File.WriteAllBytes(path, []);
                // A read names the file whole; a command that wrote it names it among its words (PR-CHAT-103).
                var read = new ToolDetailView(Call(ToolCall.Kind.Read, path) with { Detail = "image detail" }, h.Project.Path);
                var command = new ToolDetailView(Call(ToolCall.Kind.Command, "screencapture " + path) with { Detail = "image detail" }, h.Project.Path);
                var plainCall = new ToolDetailView(Call(ToolCall.Kind.Command, "ls") with { Detail = "plain detail" }, h.Project.Path);
                var panel = new StackPanel { Width = 480, Spacing = 40 };
                foreach (var item in new Control[] { read, command, plainCall }) panel.Children.Add(item);
                var window = Show(panel);
                try
                {
                    Assert.Empty(Mounted<ToolImagePreview>(plainCall));
                    foreach (var view in new[] { read, command })
                    {
                        // :2117-2119 the preview is the first thing in the detail, above the detail text.
                        var rows = Column(view).Children;
                        var preview = Assert.IsType<ToolImagePreview>(rows[0]);
                        Assert.IsType<SelectableTextBlock>(rows[1]);
                        Assert.Equal(480, preview.Bounds.Width, 0.5); // as wide as the row
                        Assert.Equal(20, Top(rows[1], window) - (Top(preview, window) + preview.Bounds.Height), 0.5);
                        // :408-421 the box: at most 420 tall, a 10-point rounded plate of .quaternary at 0.45, clipped.
                        var box = Assert.IsType<Border>(preview.Children[0]);
                        Assert.Equal(420, box.MaxHeight);
                        Assert.Equal(new CornerRadius(10), box.CornerRadius);
                        Assert.True(box.ClipToBounds);
                        Paints(box.Background, SwiftColor.Quaternary.Opacity(0.45), box, "image plate");
                        Assert.Equal(480, box.Bounds.Width, 0.5);
                        // :416-417 a small spinner stays until the thumbnail loads (an empty file never does).
                        Assert.True(Assert.Single(Mounted<Avalonia.Controls.ProgressBar>(box)).IsVisible);
                        // :422-433 6 points below, the file's last path component (caption, secondary, one line) and an Open link.
                        Assert.Equal(6, Top(preview.Children[1], window) - (Top(box, window) + box.Bounds.Height), 0.5);
                        var name = Assert.Single(Mounted<MiddleTruncatingTextBlock>(preview));
                        Assert.Equal("screenshot.png", name.FullText);
                        Assert.Equal(10, name.FontSize);
                        Assert.Equal(1, name.LineLimit);
                        Paints(name.Foreground, ChromeTokens.SecondaryText, name, "file name");
                        var open = Assert.Single(Mounted<Button>(preview));
                        var openText = Assert.Single(Mounted<TextBlock>(open));
                        Assert.Equal("Open", openText.Text);
                        Assert.Equal(10, openText.FontSize);
                        Assert.Equal(Left(preview, window) + preview.Bounds.Width, Left(open, window) + open.Bounds.Width, 0.5);
                        Assert.True(Left(open, window) - (Left(name, window) + name.Bounds.Width) >= 8 - 0.5);
                        OnOneCentreLine(window, name, openText);
                        Click(window, open);
                    }
                    Assert.Equal([path, path], opened);
                }
                finally { Close(window); }
            });
        }
        finally { ShellWorkspace.OpenWithDefaultApp = previous; }
    });

    [Fact]
    [Trait("Req", "UI-CHAT-025")] // TimelineRows.swift:1710-1713 the whole detail is inset 12 points from the trailing edge
    public void An_expanded_row_hangs_its_detail_one_row_gap_below_the_label_and_12_points_short_of_the_trailing_edge() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        ToolRow row = null!;
        AvaloniaWindow window = null!;
        h.Main.OnMain(() =>
        {
            row = new ToolRow(ToolEntry(Call(ToolCall.Kind.Command, "ls") with { Output = "a\nb" }), h.Runtime);
            var panel = new StackPanel { Width = 500 };
            panel.Children.Add(row);
            window = Show(panel);
            try
            {
                Assert.Empty(Mounted<ToolDetailView>(row));
                Click(window, Assert.Single(Mounted<Button>(row)));
            }
            catch { Close(window); throw; }
        });
        h.Main.OnMain(() =>
        {
            try
            {
                Settle(window);
                var detail = Assert.Single(Mounted<ToolDetailView>(row));
                var button = Assert.Single(Mounted<Button>(row));
                Assert.Equal(12, detail.Margin.Right);
                Assert.Equal(Left(row, window) + 500 - 12, Left(detail, window) + detail.Bounds.Width, 0.5);
                Assert.Equal(20, Top(detail, window) - (Top(button, window) + button.Bounds.Height), 0.5);
                var plate = Mounted<Border>(detail).Single(border => border.Padding == new Thickness(10));
                Assert.Equal(detail.Bounds.Width, plate.Bounds.Width, 0.5);
            }
            finally { Close(window); }
        });
    });
}
