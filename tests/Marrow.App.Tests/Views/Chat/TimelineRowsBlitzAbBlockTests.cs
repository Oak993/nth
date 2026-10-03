using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Common;
using Marrow.UI.Markdown;
using Marrow.UI.Port;
using AvaloniaWindow = Avalonia.Controls.Window;
using ChromeTokens = Marrow.UI.Chrome.Chrome;
using static Marrow.App.Tests.Fixtures.TimelineRowsFinChecks;
using static Marrow.App.Tests.Views.Chat.TimelineRowsPrSupport;

namespace Marrow.App.Tests.Views.Chat;

// Source: DroppyCode/App/Views/Chat/TimelineRows.swift (B3 52bc792): the timeline's shared geometry (:8-28 and every row that reads it), the work
// steps (:1446-1559), the expanded tool row's detail (:2094-2167), the finished turn (:2480-2716) and the heads' report pill (:236-388).
// Where a record's capture comparison asks for a frame the reference videos do not contain, the Swift source is the proof (OD-0098).
[Collection("Headless")]
public sealed class TimelineRowsBlitzAbBlockTests(HeadlessSession headless)
{
    private static readonly (ThemeVariant Theme, Color Backdrop)[] Themes =
    [
        (ThemeVariant.Dark, Color.FromRgb(24, 24, 27)),
        (ThemeVariant.Light, Color.FromRgb(244, 244, 246)),
    ];

    private static double Top(Control control, Visual root) => control.TranslatePoint(default, root)!.Value.Y;

    private static string Lines(int count) => string.Join("\n", Enumerable.Range(1, count).Select(i => "line " + i));

    private static string SourceText(params string[] path)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Marrow.sln"))) dir = dir.Parent;
        return File.ReadAllText(Path.Combine([dir?.FullName ?? throw new InvalidOperationException("Marrow.sln not found"), .. path]));
    }

    private static int Occurrences(string text, string needle)
    {
        var count = 0;
        for (var at = text.IndexOf(needle, StringComparison.Ordinal); at >= 0; at = text.IndexOf(needle, at + needle.Length, StringComparison.Ordinal)) count++;
        return count;
    }

    // ---- PR-CHAT-020: the eight constants and everything that derives from them ----

    [Fact]
    [Trait("Req", "PR-CHAT-020")] // TimelineRows.swift:8-28 and its sites (:313-316, :462-465, :574-577, :691-694, :1812-1817, :2364, :2398-2402, :2660, :2818-2822; ChatBadge.swift:46-49)
    public void Every_badge_pill_notice_and_card_of_the_conversation_is_the_one_pill_of_the_metrics_or_the_card_the_source_derives_from_its_radius() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var genie = ChromeTokens.ReduceMotionPlatform.Override(new FakeGeniePlatform());
        h.Main.Loop.Run(() => h.Runtime.EnsureLoaded(), AppModelHarness.Bound);
        h.Main.OnMain(() =>
        {
            var pill = new Thickness(TimelineMetrics.PillLeading, TimelineMetrics.PillVertical, TimelineMetrics.PillTrailing, TimelineMetrics.PillVertical);
            var radius = new CornerRadius(TimelineMetrics.PillRadius);
            Assert.Equal(new Thickness(12, 8, 14, 8), pill);
            // The one pill: leading 12, trailing 14, vertical 8, radius 18 ...
            var briefEntry = UserEntry("Build the thing", null);
            var heads = new (string Name, Control Row)[]
            {
                ("plan badge", new PlanCard(PlanEntry(ProposedPlan.State.Proposed), h.Runtime)),
                ("heads' report", new HydraReportRow(new UserMessage { Text = "## Hank: a\nbody", HydraHeads = new ValueList<long>([0]) })),
                ("lead's brief", new HydraBriefRow(((TimelineItem.Content.User)briefEntry.Item.TimelineItemContent).Value, briefEntry, h.Runtime)),
                ("finished head", new FinishedHeadPill(TimelineRowsFinTeam.Head(0, HydraHeadInfo.Status.Completed))),
                ("delegation card", new HydraDelegationBlock("")),
                ("departing head badge", new ToolRow(ToolEntry(Call(ToolCall.Kind.Agent, "Pip: the popover", ToolCall.Status.Running)), h.Runtime)),
                ("warning notice", new NoticeRow(NoticeEntry(Notice.Level.Warning, "careful"))),
                ("error notice", new NoticeRow(NoticeEntry(Notice.Level.Error, "it broke"))),
            };
            var panel = new StackPanel { Spacing = 12, Width = 700 };
            foreach (var (_, row) in heads) panel.Children.Add(row);
            // ... and the cards that take the radius with their own insets: the checklist (14), the steps card (14), the file card (12, 6, 6, 6).
            var todo = new TodoListRow(new TimelineEntry(new TimelineItem(null, Steps(("one", TodoStep.Status.Pending)))));
            var file = new TurnFileCard(new FileChangeSummary(new ValueList<FileChangeSummary.File>([new("a.swift", 1, 1, false)]), 1, 1), false, () => { }, _ => { });
            var turn = Guid.NewGuid();
            var finished = new TurnFinishedBlock(h.Runtime, turn, Summary(turn), ValueList<TimelineEntry>.Empty, new ValueList<TimelineEntry>([Tool(ToolCall.Kind.Command), Reply("an answer")]), null, false, true);
            foreach (var row in new Control[] { todo, file, finished }) panel.Children.Add(row);
            var window = Show(new ScrollViewerlessHost(panel));
            try
            {
                foreach (var (name, row) in heads)
                {
                    Assert.True(Mounted<Border>(row).Any(border => border.Padding == pill && border.CornerRadius == radius), $"{name} is not the metrics' pill");
                    Assert.True(RoundedCornerStyle.Continuous == SwiftShapes.GetCornerStyle(Mounted<Border>(row).First(border => border.Padding == pill && border.CornerRadius == radius)), $"{name} is not drawn in the continuous style");
                }
                Assert.Contains(Mounted<Border>(todo), border => border.Padding == new Thickness(14) && border.CornerRadius == radius);
                Assert.Contains(Mounted<Border>(file), border => border.Padding == new Thickness(TimelineMetrics.PillLeading, 6, 6, 6) && border.CornerRadius == radius);
                Assert.Contains(Mounted<Border>(finished), border => border.Padding == new Thickness(14) && border.CornerRadius == radius);
            }
            finally { Close(window); }
        });
    });

    private sealed class ScrollViewerlessHost : Decorator
    {
        public ScrollViewerlessHost(Control child) => Child = child;
    }

    [Fact]
    [Trait("Req", "PR-CHAT-020")] // TimelineRows.swift:164, :326, :417, :589, :1017, :2369; ChatBadge.swift:77: the 96-point side insets of the rows, laid out
    public void The_rows_that_keep_96_points_clear_of_a_side_do_so_in_a_laid_out_column() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var genie = ChromeTokens.ReduceMotionPlatform.Override(new FakeGeniePlatform());
        h.Main.Loop.Run(() => h.Runtime.EnsureLoaded(), AppModelHarness.Bound);
        h.Main.OnMain(() =>
        {
            var briefEntry = UserEntry(string.Join(" ", Enumerable.Repeat("a long line of words", 40)), null);
            // Words longer than the column, so the bubble takes every point the row offers it.
            var user = new UserMessageRow(UserEntry(string.Join(" ", Enumerable.Repeat(new string('w', 400), 3)), Guid.NewGuid()), h.Runtime, false);
            var report = new HydraReportRow(new UserMessage { Text = "## Hank: a\n" + string.Join(" ", Enumerable.Repeat("x", 100)), HydraHeads = new ValueList<long>([0]) });
            var todo = new TodoListRow(new TimelineEntry(new TimelineItem(null, Steps((string.Join(" ", Enumerable.Repeat("a very long step", 40)), TodoStep.Status.Active)))));
            var badge = new PlanCard(PlanEntry(ProposedPlan.State.Proposed), h.Runtime);
            var rows = new Control[] { user, report, todo, badge };
            var panel = new StackPanel { Width = 700, Spacing = 12 };
            foreach (var row in rows) panel.Children.Add(row);
            var window = Show(panel);
            try
            {
                var left = Left(panel, window);
                Assert.InRange(Left(Bubble(user), window) - left, 96 - 0.5, 96 + 12); // :164 the bubble fills the room: 96 clear of the leading edge
                Assert.Equal(left + 700, Left(Bubble(user), window) + Bubble(user).Bounds.Width, 0.5);
                foreach (var (name, row) in new[] { ("report pill", (Control)report), ("checklist", todo), ("plan badge", badge) })
                {
                    var widest = Mounted<Border>(row).Where(border => border.CornerRadius.TopLeft > 0).Max(border => Left(border, window) + border.Bounds.Width);
                    Assert.True(widest <= left + 700 - 96 + 0.5, $"{name} runs to {widest - left}, closer than 96 to the trailing edge"); // :326, :2369, ChatBadge :77
                }
            }
            finally { Close(window); }
        });
    });

    private static Border Bubble(Control row) => Mounted<Border>(row).First(border => border.Padding == new Thickness(14, 9, 14 + 4, 9));

    [Fact]
    [Trait("Req", "PR-CHAT-020")] // verification_method: a static check that no row hard-codes these numbers; TimelineRows.swift:8-28 and its sites
    public void No_row_spells_the_pill_the_row_gap_the_hover_room_or_the_radius_as_a_number_where_the_source_reads_the_constant() => headless.OnUiThread(() =>
    {
        var rows = SourceText("src", "Marrow.App", "Views", "Chat", "TimelineRows.cs");
        var badge = SourceText("src", "Marrow.App", "Views", "Chat", "ChatBadge.cs");
        // The pill as one set of four numbers, the radius as 18, the row gap as 20 and the hover room as 22 are spelled only in the constants.
        foreach (var literal in new[] { "new Thickness(12, 8, 14, 8)", "RoundedRectangle(18", "new CornerRadius(18)", " Spacing = 20", "Height = 22 }", "-(22 +", "-(20 +" })
        {
            Assert.Equal(0, Occurrences(rows, literal));
            Assert.Equal(0, Occurrences(badge, literal));
        }
        // The seven sites where the source writes 96 (:164, :326, :417, :589, :1017, :2369 and ChatBadge.swift:77) are the only ones here.
        Assert.Equal(6, Occurrences(rows, "new Thickness(96, 0, 0, 0)") + Occurrences(rows, "new Thickness(0, 0, 96, 0)"));
        Assert.Equal(1, Occurrences(badge, "new Thickness(0, 0, 96, 0)"));
        // The constants themselves: the eight values, once (:8-28).
        foreach (var constant in new[] { "RowSpacing = 20;", "HoverLineHeight = 22;", "IconWidth = 16;", "IconSpacing = 8;", "PillLeading = 12;", "PillTrailing = 14;", "PillVertical = 8;", "PillRadius = 18;" })
            Assert.Equal(1, Occurrences(rows, constant));
        // Every site the source reads a constant is read from the constants here.
        Assert.True(Occurrences(rows, "TimelineMetrics.PillLeading") >= 3); // the pill helper, the badge row, the file card
        Assert.True(Occurrences(rows, "TimelineMetrics.PillRadius") >= 5);
        Assert.True(Occurrences(rows, "TimelineRows.Pill(") >= 8); // the pills built by the one helper
        Assert.True(Occurrences(rows, "TimelineMetrics.RowSpacing") >= 14);
        Assert.True(Occurrences(rows, "TimelineMetrics.IconWidth") >= 7);
        Assert.True(Occurrences(rows, "TimelineMetrics.HoverLineHeight") >= 4);
        Assert.True(Occurrences(badge, "TimelineMetrics.PillRadius") >= 2);
    });

    // ---- PR-CHAT-092: the work steps, laid out in a real group ----

    [Fact]
    [Trait("Req", "PR-CHAT-092")] // TimelineRows.swift:1463-1509, :1522-1557; OD-0098: the earlier-steps line and the narration laid out in a real group and drawn in both themes
    public void In_a_real_group_the_earlier_steps_line_and_what_the_agent_said_between_steps_line_up_with_the_tool_rows_text_and_are_secondary_ink_in_both_themes() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        var manual = new ManualFrameSource();
        using var frameSeam = FrameClock.Source.Override(manual);
        h.Main.OnMain(() =>
        {
            foreach (var (theme, backdrop) in Themes)
            {
                var entries = new ValueList<TimelineEntry>([
                    ToolEntry(Call(ToolCall.Kind.Read, "Read a")), Reply("looking at the config"), ToolEntry(Call(ToolCall.Kind.Read, "Read b")),
                    ToolEntry(Call(ToolCall.Kind.Command, "make")), ToolEntry(Call(ToolCall.Kind.Edit, "Edit c"))]);
                var group = new WorkGroup(entries, h.Runtime);
                var host = new Border { Padding = new Thickness(24), Child = group };
                BlitzAbRender.Render(host, theme, backdrop, 560, 360, (window, frame) =>
                {
                    var steps = Assert.Single(Mounted<WorkSteps>(group));
                    // Five steps: the last two show under one line reading 3 earlier steps (:1523, :1536).
                    var line = Assert.Single(Mounted<Button>(steps));
                    var words = TextNamed(line, "3 earlier steps");
                    var ellipsis = Assert.Single(Mounted<SwiftImage>(line), image => image.SymbolName == "ellipsis");
                    Assert.Equal(Left(steps, window), Left(ellipsis, window), 0.5);
                    Assert.Equal(16 + 8, Left(words, window) - Left(steps, window), 0.5); // the icon column and its gap
                    var rows = Mounted<ToolRow>(steps).ToList();
                    Assert.Equal(2, rows.Count);
                    Assert.Equal(20, Top(rows[0], window) - (Top(line, window) + line.Bounds.Height), 0.5); // :1522 a row gap under the line
                    foreach (var row in rows) Assert.Equal(16 + 8, Left(Mounted<MiddleTruncatingTextBlock>(row).Single(), window) - Left(steps, window), 0.5);
                    BlitzAbRender.TextIsColour(frame, BlitzAbRender.BoxIn(words, window), backdrop, ChromeTokens.SecondaryText.Resolve(theme), $"the earlier-steps words in {theme}", closest: 130);
                    BlitzAbRender.InkIsColour(frame, BlitzAbRender.BoxIn(ellipsis, window), backdrop, ChromeTokens.SecondaryText.Resolve(theme), $"the ellipsis in {theme}", most: 1.6);
                });
            }
            // Opened, the narration sits at the same x as the tool rows' text (:1553), dimmed secondary markdown at 12 points.
            var all = new WorkSteps(new ValueList<TimelineEntry>([ToolEntry(Call(ToolCall.Kind.Read, "Read a")), Reply("looking at the config"), ToolEntry(Call(ToolCall.Kind.Read, "Read b"))]),
                h.Runtime, new Binding<bool>(() => true, _ => { }));
            var window2 = Show(all);
            try
            {
                var narration = Assert.Single(Mounted<MarkdownView>(all));
                var first = Mounted<MiddleTruncatingTextBlock>(Mounted<ToolRow>(all).First()).Single();
                Assert.Equal(Left(first, window2), Left(narration, window2), 0.5);
                Assert.Equal(Left(all, window2) + TimelineMetrics.IconWidth + TimelineMetrics.IconSpacing, Left(narration, window2), 0.5);
                Assert.Equal(12, Marrow.UI.Markdown.MarkdownView.GetMarkdownPointSize(narration));
                Assert.True(Marrow.UI.Markdown.MarkdownView.GetMarkdownDimmed(narration));
            }
            finally { Close(window2); }
        });
    });

    // ---- PR-CHAT-101: the expanded output at 29, 30 and 31 lines ----

    [Theory]
    [Trait("Req", "PR-CHAT-101")] // TimelineRows.swift:2103, :2141-2162: the 30-line fold at the edge, rendered
    [InlineData(1, 1, false)]
    [InlineData(29, 29, false)]
    [InlineData(30, 30, false)]
    [InlineData(31, 30, true)]
    [InlineData(60, 30, true)]
    public void Output_folds_to_its_first_thirty_lines_only_past_thirty_and_the_link_counts_every_line(int total, int shown, bool link) => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        h.Main.OnMain(() =>
        {
            foreach (var (theme, backdrop) in Themes)
            {
                var view = new ToolDetailView(Call(ToolCall.Kind.Command, "cat") with { Output = "\n\n" + Lines(total) + "\n" });
                var host = new Border { Padding = new Thickness(24), Child = view, Width = 560 };
                BlitzAbRender.Render(host, theme, backdrop, 560, 40 + 14 * (shown + 4), (window, frame) =>
                {
                    var plate = Mounted<Border>(view).Single(border => border.Padding == new Thickness(10));
                    var text = Assert.Single(Mounted<SelectableTextBlock>(plate));
                    Assert.Equal(Lines(shown), text.Text);
                    Assert.Equal(new CornerRadius(10), plate.CornerRadius);
                    var plateBox = BlitzAbRender.BoxIn(plate, window);
                    var fill = BlitzAbFrame.Over(SwiftColor.Quaternary.Opacity(0.45).Resolve(theme), backdrop);
                    Assert.True(BlitzAbFrame.Distance(frame.At((int)plateBox.Left + 4, (int)(plateBox.Top + 4)), fill) <= 6, $"{theme}: the plate's fill");
                    BlitzAbRender.TextIsColour(frame, new Rect(plateBox.Left + 10, plateBox.Top + 10, 200, 14), fill, SwiftColor.Primary.Resolve(theme), $"the output text in {theme}", closest: 130);
                    var links = Mounted<Button>(view).ToList();
                    if (!link) { Assert.Empty(links); return; }
                    var title = TextNamed(links[0], "Show full output (" + total + " lines)");
                    Assert.Equal(10, title.FontSize);
                    Assert.Equal(20, Top(links[0], window) - (plateBox.Bottom), 0.5); // :2149 a row gap under the plate
                    BlitzAbRender.TextIsColour(frame, BlitzAbRender.BoxIn(title, window), backdrop, ChromeTokens.Accent.Resolve(theme), $"the link in {theme}", closest: 140);
                });
            }
        });
    });

    [Fact]
    [Trait("Req", "PR-CHAT-101")] // TimelineRows.swift:2105-2113 head(of:lines:): the first lines up to the nth newline, without splitting the rest
    public void The_head_function_cuts_at_the_nth_newline_and_returns_a_shorter_text_whole() => headless.OnUiThread(() =>
    {
        string Head(string text, int lines) => (string)typeof(ToolDetailView).GetMethod("Head", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!.Invoke(null, [text, lines])!;
        Assert.Equal("l1\nl2", Head("l1\nl2\nl3\nl4", 2));
        Assert.Equal("l1", Head("l1\nl2", 1));
        Assert.Equal("l1\nl2\nl3", Head("l1\nl2\nl3", 3)); // only two newlines: the whole text
        Assert.Equal("l1\nl2\nl3", Head("l1\nl2\nl3", 30));
        Assert.Equal("", Head("", 5));
        Assert.Equal("\n\nx", Head("\n\nx\ny", 3)); // blank lines count
        Assert.Equal("a\r\nb\r", Head("a\r\nb\r\nc", 2)); // a CRLF is cut after its CR, as the source's byte scan for 0x0A does
        Assert.Equal("é\nü", Head("é\nü\nz", 2)); // a multi-byte character is no newline
        Assert.Equal(Lines(30), Head(Lines(40), 30));
    });

    // ---- PR-CHAT-104: the finished turn folded and opened, drawn ----

    [Fact]
    [Trait("Req", "PR-CHAT-104")] // TimelineRows.swift:2589-2680; OD-0098 (the opened state is in no frame): folded and open, laid out and drawn in both themes
    public void A_finished_turn_is_drawn_folded_then_open_with_the_header_a_row_gap_from_its_rule_and_body_and_the_steps_in_a_quaternary_card() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        var manual = new ManualFrameSource();
        using var frameSeam = FrameClock.Source.Override(manual);
        using var locale = SwiftFormatters.CurrentLocale.Override("en_US");
        foreach (var (theme, backdrop) in Themes)
        {
            TurnFinishedBlock block = null!;
            AvaloniaWindow window = null!;
            h.Main.OnMain(() =>
            {
                var turn = Guid.NewGuid();
                var content = new[] { ToolEntry(Call(ToolCall.Kind.Command, "make"), turn), Reply("the final answer") };
                block = new TurnFinishedBlock(h.Runtime, turn, Summary(turn), ValueList<TimelineEntry>.Empty, new ValueList<TimelineEntry>(content), null, false, false);
                window = BlitzAbRender.Open(new Border { Padding = new Thickness(24), Child = block }, theme, backdrop, 560, 360);
                var frame = BlitzAbRender.Capture(window);
                var header = Assert.Single(Mounted<Button>(block), button => ToolTip.GetTip(button) as string == "Show this turn's steps");
                var label = TextNamed(header, "Worked for 5s");
                Assert.Equal(12, label.FontSize); // :2611 callout
                BlitzAbRender.TextIsColour(frame, BlitzAbRender.BoxIn(label, window), backdrop, ChromeTokens.SecondaryText.Resolve(theme), $"the header in {theme}", closest: 130);
                var chevron = Assert.Single(Mounted<SwiftImage>(header));
                Assert.Equal(6, Left(chevron, window) - (Left(label, window) + label.Bounds.Width), 0.5); // :2609 HStack(spacing: 6)
                var rule = Mounted<Border>(block).Single(border => border.Height == 1 && border.Opacity == 0.6);
                // :2629 half a row gap on each side: the header and the answer sit one row gap apart with the rule between.
                Assert.Equal(10, Top(rule, window) - (Top(header, window) + header.Bounds.Height), 0.5);
                var answer = Assert.Single(Mounted<FoldedAnswerView>(block));
                Assert.Equal(10, Top(answer, window) - (Top(rule, window) + 1), 0.5);
                var ruleBox = BlitzAbRender.BoxIn(rule, window);
                var ink = BlitzAbFrame.Over(ChromeTokens.PrimaryText.Opacity(0.22 * 0.6).Resolve(theme), backdrop);
                Assert.True(BlitzAbFrame.Distance(frame.At((int)ruleBox.Left + 50, (int)ruleBox.Top), ink) <= 6, $"{theme}: the rule's ink");
                Assert.Equal(ruleBox.Width, Mounted<Border>(block).Max(border => border.Bounds.Width), 0.5); // it runs the block's full width
                Click(window, header);
            });
            for (var i = 0; i < 30; i++) { h.Main.Advance(TimeSpan.FromMilliseconds(16)); manual.Advance(TimeSpan.FromMilliseconds(16)); }
            h.Main.OnMain(() =>
            {
                try
                {
                    var frame = BlitzAbRender.Capture(window);
                    var header = Assert.Single(Mounted<Button>(block), button => ToolTip.GetTip(button) as string == "Hide this turn's steps");
                    var card = Mounted<Border>(block).Single(border => border.Padding == new Thickness(14));
                    var box = BlitzAbRender.BoxIn(card, window);
                    var fill = BlitzAbFrame.Over(SwiftColor.Quaternary.Opacity(0.32).Resolve(theme), backdrop);
                    Assert.True(BlitzAbFrame.Distance(frame.At((int)box.Left + 5, (int)(box.Top + box.Height / 2)), fill) <= 6, $"{theme}: the steps card's fill");
                    Assert.Equal(Left(block, window), box.Left, 0.5);
                    var rule = Mounted<Border>(block).Single(border => border.Height == 1 && border.Opacity == 0.6);
                    Assert.Equal(10, Top(card, window) - (Top(rule, window) + 1), 0.5);
                    Assert.Equal(90, Assert.IsType<RotateTransform>(Assert.Single(Mounted<SwiftImage>(header)).RenderTransform).Angle);
                    var row = Assert.Single(Mounted<ToolRow>(card));
                    Assert.Equal(box.Left + 14, Left(Mounted<ToolStatusIcon>(row).Single(), window), 0.5); // the steps keep the card's 14-point inset
                }
                finally { BlitzAbRender.Shut(window); }
            });
        }
    });

    // ---- PR-CHAT-143: the heads' report pill, drawn ----

    [Fact]
    [Trait("Req", "PR-CHAT-143")] // TimelineRows.swift:236-329; OD-0098 (every value but the pill's continuous corner, ED-181): the pill laid out and drawn in both themes
    public void The_report_pill_is_an_accent_tinted_pill_of_overlapped_glyphs_and_a_primary_title_96_points_clear_of_the_trailing_edge_in_both_themes() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        h.Main.OnMain(() =>
        {
            foreach (var (theme, backdrop) in Themes)
            {
                var row = new HydraReportRow(new UserMessage { Text = "## Hank: a\nbody\n## Walter: b\nbody", HydraHeads = new ValueList<long>([0, 1]) });
                var host = new Border { Padding = new Thickness(24), Child = row };
                BlitzAbRender.Render(host, theme, backdrop, 640, 100, (window, frame) =>
                {
                    var pill = Mounted<Border>(row).First(border => border.Padding == new Thickness(12, 8, 14, 8));
                    var box = BlitzAbRender.BoxIn(pill, window);
                    var fill = BlitzAbFrame.Over(ChromeTokens.Accent.Opacity(0.1).Resolve(theme), backdrop);
                    Assert.True(BlitzAbFrame.Distance(frame.At((int)box.Left + 5, (int)(box.Top + box.Height / 2)), fill) <= 6, $"{theme}: the pill's fill");
                    var glyphs = Mounted<HydraGlyph>(row).ToList();
                    Assert.Equal(-4, Left(glyphs[1], window) - (Left(glyphs[0], window) + 18)); // :271 HStack(spacing: -4)
                    Assert.Equal(box.Left + 12, Left(glyphs[0], window), 0.5);
                    var title = TextNamed(row, "Hank and Walter are done");
                    Assert.Equal(8, Left(title, window) - (Left(glyphs[1], window) + 18), 0.5); // :263 spacing 8
                    BlitzAbRender.TextIsColour(frame, BlitzAbRender.BoxIn(title, window), fill, ChromeTokens.PrimaryText.Opacity(0.9).Resolve(theme), $"the title in {theme}", closest: 130);
                    var chevron = Assert.Single(Mounted<SwiftImage>(row), image => image.SymbolName == "chevron.right");
                    BlitzAbRender.InkIsColour(frame, BlitzAbRender.BoxIn(chevron, window), fill, ChromeTokens.SecondaryText.Resolve(theme), $"the chevron in {theme}", most: 1.6);
                    Assert.Equal(box.Right, Left(chevron, window) + chevron.Bounds.Width + 14, 0.5); // the trailing inset
                    Assert.Equal(new Thickness(0, 0, 96, 0), row.Padding);
                });
            }
        });
    });
}
