using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Common;
using Marrow.UI.Port;
using AvaloniaWindow = Avalonia.Controls.Window;
using ChromeTokens = Marrow.UI.Chrome.Chrome;
using static Marrow.App.Tests.Fixtures.TimelineRowsFinChecks;
using static Marrow.App.Tests.Fixtures.TimelineRowsFinRig;
using static Marrow.App.Tests.Views.Chat.TimelineRowsPrSupport;

namespace Marrow.App.Tests.Views.Chat;

// Source: DroppyCode/App/Views/Chat/TimelineRows.swift (B3 52bc792), :2829-2858 the turn file card's file list popover.
[Collection("Headless")]
public sealed class TimelineRowsFinFilesPopoverTests(HeadlessSession headless)
{
    private static Control FilesPopover(IEnumerable<FileChangeSummary.File> files)
    {
        var card = new TurnFileCard(new FileChangeSummary(new ValueList<FileChangeSummary.File>(files), 0, 0), false, () => { }, _ => { });
        return Assert.IsAssignableFrom<Control>(typeof(TurnFileCard).GetMethod("FilesPopover", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(card, null));
    }

    private static IEnumerable<FileChangeSummary.File> Many(int count) =>
        Enumerable.Range(1, count).Select(i => new FileChangeSummary.File("src/file" + i + ".swift", i, 1, false));

    private static double Top(Control control, Visual root) => control.TranslatePoint(default, root)!.Value.Y;

    [Fact]
    [Trait("Req", "UI-CHAT-041")] // TimelineRows.swift:2829-2858 the file row
    [Trait("Req", "UI-GIT-045")] // TimelineRows.swift:2829-2858 the file list popover
    public void A_file_row_is_a_middle_truncated_callout_path_a_spacer_and_the_stat_or_Binary_on_one_centre_line_padded_12_and_7() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        runtime.OnMain(() =>
        {
            var files = new FileChangeSummary.File[]
            {
                new("src/" + new string('a', 60) + "/Deep/" + new string('b', 60) + "/final-name.swift", 3, 2, false),
                new("logo.png", 0, 0, true),
                new("short.swift", 5, 4, false),
            };
            var popover = FilesPopover(files);
            var host = new Border { Width = 300, Child = popover };
            var window = Show(new StackPanel { Children = { host } });
            try
            {
                // :2831 a leading VStack with spacing 0.
                var column = Assert.IsType<StackPanel>(popover);
                Assert.Equal(0, column.Spacing);
                var rows = column.Children.OfType<DockPanel>().ToList();
                Assert.Equal(3, rows.Count);
                // :2847-2848 each row padded 12 horizontally and 7 vertically.
                Assert.All(rows, row => Assert.Equal(new Thickness(12, 7), row.Margin));
                // :2834-2837 the path: callout, one line, middle truncation so the file name stays readable.
                var long_ = Mounted<MiddleTruncatingTextBlock>(rows[0]).Single();
                Face(long_, 12, FontWeight.Normal, ".chat(.callout)");
                Assert.Equal(1, long_.LineLimit);
                Assert.Contains("…", long_.Text, StringComparison.Ordinal);
                Assert.StartsWith("src/", long_.Text, StringComparison.Ordinal);
                Assert.EndsWith("final-name.swift", long_.Text, StringComparison.Ordinal);
                // :2838 a spacer of at least 8 before the trailing value: the path never runs into the stat.
                var stat = Assert.Single(Mounted<DiffStatLabel>(rows[0]));
                Assert.True(Left(stat, window) - (Left(long_, window) + long_.Bounds.Width) >= 8 - 0.5);
                Assert.Equal(Left(rows[0], window) + rows[0].Bounds.Width - 12, Left(stat, window) + stat.Bounds.Width + 0, 0.5 + 12);
                // :2839-2845 a binary file shows the literal Binary (caption, Chrome.secondaryText) in place of the stat.
                var binary = TextNamed(rows[1], "Binary");
                Face(binary, 10, FontWeight.Normal, ".chat(.caption)");
                Paints(binary.Foreground, ChromeTokens.SecondaryText, binary, "Binary");
                Assert.Empty(Mounted<DiffStatLabel>(rows[1]));
                Assert.Single(Mounted<DiffStatLabel>(rows[2]));
                // :2833 HStack(spacing: 8): the path and its trailing value share one centre line, whatever their heights.
                foreach (var (row, trailing) in new (DockPanel, Control)[] { (rows[0], stat), (rows[1], binary), (rows[2], Mounted<DiffStatLabel>(rows[2]).Single()) })
                {
                    var path = Mounted<MiddleTruncatingTextBlock>(row).Single();
                    OnOneCentreLine(window, path, trailing);
                }
            }
            finally { Close(window); }
        });
    });

    [Fact]
    [Trait("Req", "UI-CHAT-041")] // TimelineRows.swift:2832, :2850-2856 at most twelve rows and the overflow line
    [Trait("Req", "UI-GIT-045")] // TimelineRows.swift:2850-2856 and 1 more file / and N more files
    public void The_list_shows_twelve_rows_then_exactly_and_1_more_file_or_and_N_more_files_in_secondary_callout() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        runtime.OnMain(() =>
        {
            var twelve = FilesPopover(Many(12));
            var thirteen = FilesPopover(Many(13));
            var twenty = FilesPopover(Many(20));
            var panel = new StackPanel { Spacing = 20, Width = 400 };
            foreach (var popover in new Control[] { twelve, thirteen, twenty }) panel.Children.Add(popover);
            var window = Show(panel);
            try
            {
                Assert.Equal(12, Mounted<MiddleTruncatingTextBlock>(twelve).Count());
                Assert.DoesNotContain(Texts(twelve), text => text.StartsWith("and ", StringComparison.Ordinal));
                Assert.Equal(12, Mounted<MiddleTruncatingTextBlock>(thirteen).Count());
                Assert.DoesNotContain("src/file13.swift", Texts(thirteen));
                var one = TextNamed(thirteen, "and 1 more file");
                var eight = TextNamed(twenty, "and 8 more files");
                foreach (var line in new[] { one, eight })
                {
                    // :2852-2855 callout in Chrome.secondaryText, padded 12 and 7, after the twelve rows.
                    Face(line, 12, FontWeight.Normal, ".chat(.callout)");
                    Paints(line.Foreground, ChromeTokens.SecondaryText, line, "overflow line");
                    Assert.Equal(new Thickness(12, 7), line.Margin);
                    Assert.Same(line, ((StackPanel)line.Parent!).Children.Last());
                }
            }
            finally { Close(window); }
        });
    });
}
