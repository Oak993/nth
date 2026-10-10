using Avalonia;
using Avalonia.Controls;
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
using static Marrow.App.Tests.Views.Chat.TimelineRowsPrSupport;

namespace Marrow.App.Tests.Views.Chat;

// Source: DroppyCode/App/Views/Chat/TimelineRows.swift (B3 52bc792), :1636-1853 ToolRow, :2067-2092 ToolStatusIcon.
[Collection("Headless")]
public sealed class TimelineRowsFinToolRowTests(HeadlessSession headless)
{
    private static void FontOf(SwiftImage image, double size, FontWeight weight)
    {
        var font = SwiftImage.GetFont(image)!.Resolve();
        Assert.Equal(size, font.FontSize);
        Assert.Equal(weight, font.FontWeight);
    }

    private static MiddleTruncatingTextBlock Label(Control row) => Assert.Single(Mounted<MiddleTruncatingTextBlock>(row));

    [Fact]
    [Trait("Req", "UI-CHAT-024")] // TimelineRows.swift:1657-1853 the plain row: icon column, text, stats, chevron, trailing outcome
    public void A_plain_tool_row_is_a_16_point_icon_column_then_secondary_callout_text_then_stats_and_a_chevron_on_one_centre_line() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        h.Main.OnMain(() =>
        {
            var output = ToolRowFor(h, Call(ToolCall.Kind.Command, "npm test") with { Output = "a\nb" });
            var diff = ToolRowFor(h, Call(ToolCall.Kind.Edit, "Edit a.swift") with { Edits = new ValueList<FileEdit>([new FileEdit("a.swift", "@@ -1 +1 @@\n-a\n+b\n", 3, 1)]) });
            var plain = ToolRowFor(h, Call(ToolCall.Kind.Search, "grep foo"));
            var running = ToolRowFor(h, Call(ToolCall.Kind.Command, "make", ToolCall.Status.Running));
            var failed = ToolRowFor(h, Call(ToolCall.Kind.Command, "make", ToolCall.Status.Failed) with { ExitCode = 2 });
            var declined = ToolRowFor(h, Call(ToolCall.Kind.Command, "rm -rf /", ToolCall.Status.Declined));
            var panel = new StackPanel { Spacing = 20, Width = 600 };
            foreach (var row in new Control[] { output, diff, plain, running, failed, declined }) panel.Children.Add(row);
            var window = Show(panel);
            try
            {
                // ---- a row with output: the icon column (16), the text 8 points on (callout, .secondary), a right chevron 8 after it.
                var icon = Assert.Single(Mounted<ToolStatusIcon>(output));
                // OD-0136 amendment 2, step 8: a completed call's icon is the nth status slot's tick (the kind's glyph, "terminal" here, was the done look)
                Assert.Empty(Mounted<SwiftImage>(icon));
                var tick = Assert.Single(Mounted<Marrow.UI.Port.Nth.NthStatusMark>(icon));
                var text = Label(output);
                var chevron = Assert.Single(Mounted<SwiftImage>(output), image => image.SymbolName == "chevron.right");
                Assert.Equal(Marrow.UI.Port.Nth.NthStatus.Done, tick.Status);
                Assert.True(double.IsNegativeInfinity(Assert.IsType<Marrow.UI.Port.Nth.NthStatusState>(tick.Snapshot().State).ChangedAt)); // a row made done shows it in place
                Assert.Equal(16, Mounted<Border>(icon).First().Width);
                Assert.Equal(Left(output, window), Left(icon, window), 0.5);
                Assert.Equal("Ran npm test", text.FullText);
                Assert.Equal(16 + 8, Left(text, window) - Left(output, window), 0.5);
                Face(text, 12, FontWeight.Normal, ".chat(.callout)");
                Paints(text.Foreground, SwiftColor.Secondary, text, "plain row text is .secondary (:1793)");
                Assert.Equal(1, text.LineLimit);
                FontOf(chevron, 10, FontWeight.SemiBold); // :1807 caption2 semibold
                Paints(chevron.Foreground, ChromeTokens.SecondaryText, chevron, "chevron");
                Assert.Equal(8, Left(chevron, window) - (Left(text, window) + text.Bounds.Width), 0.5);
                Assert.Equal(0, Assert.IsType<RotateTransform>(chevron.RenderTransform).Angle);
                OnOneCentreLine(window, icon, text, chevron);
                Assert.Equal("Show the output", ToolTip.GetTip(Assert.Single(Mounted<Button>(output))));
                // :1801-1804 a popover row (a diff) shows a down chevron and the stat; the same centre line.
                var down = Assert.Single(Mounted<SwiftImage>(diff), image => image.SymbolName == "chevron.down");
                var stat = Assert.Single(Mounted<DiffStatLabel>(diff));
                Assert.Equal(3, stat.Additions);
                Assert.Equal(1, stat.Deletions);
                FontOf(down, 10, FontWeight.SemiBold); // :1803 caption2 semibold
                Paints(down.Foreground, ChromeTokens.SecondaryText, down, "down chevron");
                Assert.Equal("Show this change", ToolTip.GetTip(Assert.Single(Mounted<Button>(diff))));
                OnOneCentreLine(window, Assert.Single(Mounted<ToolStatusIcon>(diff)), Label(diff), stat, down);
                Assert.Equal(8, Left(stat, window) - (Left(Label(diff), window) + Label(diff).Bounds.Width), 0.5);
                Assert.Equal(8, Left(down, window) - (Left(stat, window) + stat.Bounds.Width), 0.5);
                // :1677, :1708 nothing to open: a line of text, no control, no tooltip, no chevron.
                Assert.Empty(Mounted<Button>(plain));
                Assert.DoesNotContain(Mounted<SwiftImage>(plain), image => image.SymbolName is "chevron.down" or "chevron.right");
                // ---- :2072-2091 the status icon: a spinner, a red cross, a raised hand, the kind's glyph.
                Assert.Empty(Mounted<Avalonia.Controls.ProgressBar>(running));
                Assert.Equal(Marrow.UI.Port.Nth.NthStatus.Working, Assert.Single(Mounted<Marrow.UI.Port.Nth.NthStatusMark>(running)).Status);
                Assert.Empty(Mounted<SwiftImage>(Assert.Single(Mounted<ToolStatusIcon>(running))));
                var cross = Assert.Single(Mounted<SwiftImage>(failed), image => image.SymbolName == "xmark.circle.fill");
                Paints(cross.Foreground, ChromeTokens.Danger, cross, "failed glyph");
                var hand = Assert.Single(Mounted<SwiftImage>(declined), image => image.SymbolName == "hand.raised.slash");
                Paints(hand.Foreground, ChromeTokens.Warning, hand, "declined glyph");
                // :1833-1841 exit N / Declined at the trailing edge, 12 points in (:1844), caption type.
                var exit = TextNamed(failed, "exit 2");
                Face(exit, 10, FontWeight.Normal, "exit code");
                Paints(exit.Foreground, ChromeTokens.Danger, exit, "exit code");
                Assert.Equal(Left(failed, window) + 600 - 12, Left(exit, window) + exit.Bounds.Width, 0.5);
                var word = TextNamed(declined, "Declined");
                Paints(word.Foreground, ChromeTokens.Warning, word, "declined word");
                Assert.Equal(Left(declined, window) + 600 - 12, Left(word, window) + word.Bounds.Width, 0.5);
                OnOneCentreLine(window, Assert.Single(Mounted<ToolStatusIcon>(failed)), Label(failed), exit);
            }
            finally { Close(window); }
        });
    });

    [Fact]
    [Trait("Req", "UI-CHAT-024")] // TimelineRows.swift:1666-1669, :1784, :1806-1809 a read that looked at an image; :1844 trailing padding
    public void A_read_of_an_image_wears_the_photo_glyph_and_opens_the_image_and_a_long_title_stays_12_points_clear_of_the_edge() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        h.Main.OnMain(() =>
        {
            var path = Path.Combine(h.Project.Path, "shot.png");
            File.WriteAllBytes(path, []);
            var image = ToolRowFor(h, Call(ToolCall.Kind.Read, path));
            var longRow = ToolRowFor(h, Call(ToolCall.Kind.Command, new string('x', 300)) with { Output = "out" });
            var panel = new StackPanel { Spacing = 20, Width = 320 };
            panel.Children.Add(image);
            panel.Children.Add(longRow);
            var window = Show(panel);
            try
            {
                var glyph = Assert.Single(Mounted<SwiftImage>(Assert.Single(Mounted<ToolStatusIcon>(image))));
                Assert.Equal("photo", glyph.SymbolName);
                Assert.Equal("Show the image", ToolTip.GetTip(Assert.Single(Mounted<Button>(image))));
                Assert.Contains("chevron.down", Mounted<SwiftImage>(image).Select(item => item.SymbolName));
                // The text takes what is left, the chevron stays beside it and nothing crosses the 12-point trailing padding.
                var chevron = Assert.Single(Mounted<SwiftImage>(longRow), item => item.SymbolName == "chevron.right");
                Assert.True(Left(chevron, window) + chevron.Bounds.Width <= Left(longRow, window) + 320 - 12 + 0.5, "the chevron runs into the trailing padding");
                Assert.True(Label(longRow).Bounds.Width < 320 - 24);
                OnOneCentreLine(window, Assert.Single(Mounted<ToolStatusIcon>(longRow)), Label(longRow), chevron);
            }
            finally { Close(window); }
        });
    });

    [Fact]
    [Trait("Req", "UI-CHAT-024")] // TimelineRows.swift:1773-1795, :1812-1820 the badge variant: pill insets, 0.32 quaternary fill, callout medium
    [Trait("Req", "UI-CHAT-056")] // TimelineRows.swift:1723-1755, :1766-1793 head departure badge
    [Trait("Req", "UI-CHAT-057")] // TimelineRows.swift:2242-2254, :1772-1793 MCP tool badge
    public void A_badge_row_is_an_18_point_glyph_and_medium_callout_text_in_a_quaternary_pill_with_the_exact_words() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        h.Main.OnMain(() =>
        {
            var sending = ToolRowFor(h, Call(ToolCall.Kind.Agent, "Pip: the merge popover", ToolCall.Status.Running));
            var failed = ToolRowFor(h, Call(ToolCall.Kind.Agent, "Pip: the merge popover", ToolCall.Status.Failed));
            var sent = ToolRowFor(h, Call(ToolCall.Kind.Agent, "Subagent"));
            var sentTask = ToolRowFor(h, Call(ToolCall.Kind.Agent, "Refactor: the store"));
            var empty = ToolRowFor(h, Call(ToolCall.Kind.Agent, ""));
            var mcp = ToolRowFor(h, Call(ToolCall.Kind.Mcp, "Context7 · resolve-library-id"));
            var panel = new StackPanel { Spacing = 20, Width = 700 };
            var rows = new Control[] { sending, failed, sent, sentTask, empty, mcp };
            foreach (var row in rows) panel.Children.Add(row);
            var window = Show(panel);
            try
            {
                // :1739-1745 who is the roster name or "a head"; rest is a colon, a space and the task, or nothing for a generic title.
                Assert.Equal("Sending out Pip: the merge popover", Label(sending).FullText);
                Assert.Equal("Could not send out Pip: the merge popover", Label(failed).FullText);
                Assert.Equal("Sent out a head", Label(sent).FullText);
                Assert.Equal("Sent out a head: Refactor: the store", Label(sentTask).FullText);
                Assert.Equal("Sent out a head", Label(empty).FullText);
                // :2253 the catalog entry's name, a colon, a space and the tool name.
                Assert.Equal("Context7: resolve-library-id", Label(mcp).FullText);
                foreach (var row in rows)
                {
                    // :1812-1818 the pill: 12 leading, 14 trailing, 8 vertical, radius 18, .quaternary at 0.32.
                    var pill = Mounted<Border>(row).First(border => border.Padding == new Thickness(12, 8, 14, 8));
                    Assert.Equal(new CornerRadius(18), pill.CornerRadius);
                    Paints(pill.Background, SwiftColor.Quaternary.Opacity(0.32), pill, "badge fill");
                    var text = Label(row);
                    Face(text, 12, FontWeight.Medium, ".chat(.callout, weight: .medium)");
                    Paints(text.Foreground, ChromeTokens.PrimaryText.Opacity(0.9), text, "badge text");
                    // :1775 content spacing 8 between the 18-point glyph and the text; both on one centre line.
                    var glyph = Assert.Single(Mounted<HydraGlyph>(row).Cast<Control>().Concat(Mounted<MCPGlyph>(row)));
                    Assert.Equal(18, glyph.Bounds.Width);
                    Assert.Equal(18, glyph.Bounds.Height);
                    Assert.Equal(8, Left(text, window) - (Left(glyph, window) + glyph.Bounds.Width), 0.5);
                    OnOneCentreLine(window, glyph, text);
                    Assert.Empty(Mounted<ToolStatusIcon>(row)); // never a spinner or the kind glyph beside Delegating Subagent
                }
                Assert.Equal("Pip", Assert.Single(Mounted<HydraGlyph>(sending)).Persona.Name);
                Assert.Single(Mounted<MCPGlyph>(mcp));
            }
            finally { Close(window); }
        });
    });

    [Fact]
    [Trait("Req", "UI-CHAT-056")] // TimelineRows.swift:1766-1788 a call whose head is linked: "Sending out <name>: <title>" / "Sent out <name>: <title>"
    public void A_call_whose_head_is_linked_names_the_head_from_its_record_and_opens_its_steps() => headless.OnUiThread(() =>
    {
        using var frameSeam = FrameClock.Source.Override(new ManualFrameSource());
        var (store, app, runtime) = TimelineRowsFinTeam.Make(TimelineRowsFinTeam.Head(3, HydraHeadInfo.Status.Running));
        using (store)
        {
            store.Main.OnMain(() =>
            {
                var headID = app.Threads.Single(thread => thread.IsHydraHead).Id;
                var running = ToolEntry(Call(ToolCall.Kind.Agent, "Tidy the popover", ToolCall.Status.Running));
                var finished = ToolEntry(Call(ToolCall.Kind.Agent, "Tidy the popover"));
                runtime.RehearseHydraHead(running.Id, headID);
                runtime.RehearseHydraHead(finished.Id, headID);
                var panel = new StackPanel { Spacing = 20, Width = 600 };
                panel.Children.Add(new ToolRow(running, runtime));
                panel.Children.Add(new ToolRow(finished, runtime));
                var host = new Border { Child = panel };
                Marrow.App.Port.AppEnvironment.SetAppModel(host, app);
                var window = Show(host);
                try
                {
                    var rows = panel.Children.ToList();
                    // :1788 the head's roster name (index 3 is Otto) and the call's own title.
                    Assert.Equal("Sending out Otto: Tidy the popover", Label(rows[0]).FullText);
                    Assert.Equal("Sent out Otto: Tidy the popover", Label(rows[1]).FullText);
                    foreach (var row in rows)
                    {
                        var pill = Mounted<Border>(row).First(border => border.Padding == new Thickness(12, 8, 14, 8));
                        Paints(pill.Background, SwiftColor.Quaternary.Opacity(0.32), pill, "badge fill");
                        Face(Label(row), 12, FontWeight.Medium, "badge text");
                        var glyph = Assert.Single(Mounted<HydraGlyph>(row));
                        Assert.Equal("Otto", glyph.Persona.Name);
                        Assert.Equal(18, glyph.Bounds.Width);
                        OnOneCentreLine(window, glyph, Label(row));
                        Assert.Equal("Show the head's steps", ToolTip.GetTip(Assert.Single(Mounted<Button>(row))));
                        Assert.Contains("chevron.down", Mounted<SwiftImage>(row).Select(image => image.SymbolName));
                    }
                }
                finally { Close(window); }
            });
        }
    });

    // The row under test, mounted with the harness' runtime.
    private static ToolRow ToolRowFor(ThreadRuntimePart12Harness h, ToolCall call) => new(ToolEntry(call), h.Runtime, h.Project.Path);
}
