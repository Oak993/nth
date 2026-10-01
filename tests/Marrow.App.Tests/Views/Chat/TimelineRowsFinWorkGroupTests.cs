using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Markdown;
using Marrow.UI.Port;
using AvaloniaWindow = Avalonia.Controls.Window;
using ChromeTokens = Marrow.UI.Chrome.Chrome;
using static Marrow.App.Tests.Fixtures.TimelineRowsFinChecks;
using static Marrow.App.Tests.Views.Chat.TimelineRowsPrSupport;

namespace Marrow.App.Tests.Views.Chat;

// Source: DroppyCode/App/Views/Chat/TimelineRows.swift (B3 52bc792), :1446-1559 WorkGroup and WorkSteps.
[Collection("Headless")]
public sealed class TimelineRowsFinWorkGroupTests(HeadlessSession headless)
{
    private static void Frames(TestRuntime main, ManualFrameSource frames, double milliseconds)
    {
        frames.Advance(TimeSpan.Zero);
        main.AdvanceMilliseconds(milliseconds);
        frames.Advance(TimeSpan.FromMilliseconds(milliseconds));
    }

    private static double Angle(Control chevron) => Assert.IsType<RotateTransform>(chevron.RenderTransform).Angle;

    [Fact]
    [Trait("Req", "UI-CHAT-022")] // TimelineRows.swift:1463-1509
    public void The_work_group_header_is_an_icon_column_a_summary_and_a_turning_chevron_that_folds_the_steps() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        var frames = new ManualFrameSource();
        using var frameSeam = FrameClock.Source.Override(frames);
        var reveals = 0;
        WorkGroup group = null!;
        AvaloniaWindow window = null!;
        h.Main.OnMain(() =>
        {
            group = new WorkGroup(new ValueList<TimelineEntry>([Tool(ToolCall.Kind.Edit), Tool(ToolCall.Kind.Command)]), h.Runtime, startsCollapsed: true);
            var host = new Border { Child = group };
            ThreadTimeline.SetRevealTimelineEnd(host, new TimelineRevealBox { Action = () => reveals++ });
            window = Show(host);
            var header = Assert.Single(Mounted<Button>(group));
            var summary = TextNamed(header, "Edited files, ran commands");
            var glyph = Assert.Single(Mounted<SwiftImage>(header), image => image.SymbolName == "pencil");
            var chevron = Assert.Single(Mounted<SwiftImage>(header), image => image.SymbolName == "chevron.right");
            // :1480-1485 the icon column is 16 wide and the text starts 8 points after it, on the row's own x.
            Assert.Equal(16, glyph.Width);
            Assert.Equal(Left(group, window), Left(glyph, window), 0.5);
            Assert.Equal(16 + 8, Left(summary, window) - Left(group, window), 0.5);
            // :1487-1490 the chevron follows the text 8 points on; :1493 12 points of trailing padding make the button.
            Assert.Equal(8, Left(chevron, window) - (Left(summary, window) + summary.Bounds.Width), 0.5);
            Assert.Equal(12, Left(header, window) + header.Bounds.Width - (Left(chevron, window) + chevron.Bounds.Width), 0.5);
            // :1482-1492 type: summary callout (12), glyph caption (10), chevron caption2 semibold (10); all Chrome.secondaryText.
            Face(summary, 12, FontWeight.Normal, ".chat(.callout)");
            Paints(summary.Foreground, ChromeTokens.SecondaryText, summary, "summary");
            Assert.Equal(10, SwiftImage.GetFont(glyph)!.Resolve().FontSize);
            Paints(glyph.Foreground, ChromeTokens.SecondaryText, glyph, "glyph");
            var chevronFont = SwiftImage.GetFont(chevron)!.Resolve();
            Assert.Equal(10, chevronFont.FontSize);
            Assert.Equal(FontWeight.SemiBold, chevronFont.FontWeight);
            Paints(chevron.Foreground, ChromeTokens.SecondaryText, chevron, "chevron");
            // :1480 HStack: the three items share one centre line, and the text is not stretched to a taller slot.
            OnOneCentreLine(window, glyph, summary, chevron);
            // :1497-1498 the tooltip and the accessibility label.
            Assert.Equal("Show these steps", ToolTip.GetTip(header));
            Assert.Equal("Show these steps", AutomationProperties.GetName(header));
            // :1490 collapsed: the chevron at 0 degrees and no steps drawn.
            Assert.Equal(0, Angle(chevron));
            Assert.Empty(Mounted<WorkSteps>(group));
            Click(window, header);
            Assert.Equal(1, reveals); // :1473 opening asks the timeline to reveal the end
        });
        // :1470 withAnimation(.snappy(duration: 0.24)): the chevron is part way at 100 ms and at 90 degrees once settled.
        Frames(h.Main, frames, 100);
        h.Main.OnMain(() =>
        {
            var chevron = Assert.Single(Mounted<SwiftImage>(group), image => image.SymbolName == "chevron.right");
            Assert.InRange(Angle(chevron), 0.01, 89.99);
        });
        Frames(h.Main, frames, 2000);
        h.Main.OnMain(() =>
        {
            try
            {
            Settle(window);
            var header = Mounted<Button>(group).First();
            var chevron = Assert.Single(Mounted<SwiftImage>(header), image => image.SymbolName == "chevron.right");
            Assert.Equal(90, Angle(chevron));
            Assert.Equal("Hide these steps", ToolTip.GetTip(header));
            Assert.Equal("Hide these steps", AutomationProperties.GetName(header));
            // :1467 the steps sit one row gap (20) under the header.
            var steps = Assert.Single(Mounted<WorkSteps>(group));
            Assert.Equal(20, steps.TranslatePoint(default, window)!.Value.Y - (header.TranslatePoint(default, window)!.Value.Y + header.Bounds.Height), 0.5);
            }
            finally { Close(window); }
        });
    });

    [Fact]
    [Trait("Req", "UI-CHAT-022")] // TimelineRows.swift:1455-1461, :1464-1466, :1503-1507, :1603
    public void A_group_folds_once_when_reply_text_arrives_never_re_folds_a_reopened_one_and_a_lone_call_has_no_header() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        var frames = new ManualFrameSource();
        using var frameSeam = FrameClock.Source.Override(frames);
        var entries = new ValueList<TimelineEntry>([Tool(ToolCall.Kind.Read), Tool(ToolCall.Kind.Read, ToolCall.Status.Running)]);
        WorkGroup group = null!;
        AvaloniaWindow window = null!;
        string Help() => (string)ToolTip.GetTip(Mounted<Button>(group).First())!;
        h.Main.OnMain(() =>
        {
            group = new WorkGroup(entries, h.Runtime);
            var lone = new WorkGroup(new ValueList<TimelineEntry>([Tool(ToolCall.Kind.Command)]), h.Runtime);
            var noTools = new WorkGroup(new ValueList<TimelineEntry>([Reply("one"), Reply("two")]), h.Runtime);
            var panel = new StackPanel { Spacing = 30 };
            foreach (var row in new Control[] { group, lone, noTools }) panel.Children.Add(row);
            window = Show(panel);
            // The live group starts open; a group with a running read reads in the present tense.
            Assert.Equal("Hide these steps", Help());
            Assert.Contains("Reading files", Texts(group));
            // :1464-1466 a lone tool call is its row alone: no header, no steps.
            Assert.Single(Mounted<ToolRow>(lone));
            Assert.Empty(Mounted<WorkSteps>(lone));
            Assert.DoesNotContain("Show these steps", Mounted<Button>(lone).Select(button => ToolTip.GetTip(button) as string));
            // :1603 a group with no tool entries reads exactly Worked, under the wrench glyph.
            Assert.Contains("Worked", Texts(noTools));
            Assert.Contains("wrench.and.screwdriver", Mounted<SwiftImage>(noTools).Select(image => image.SymbolName));
            group.Update(entries, startsCollapsed: true); // reply text arrived after the group
        });
        h.Main.OnMain(() =>
        {
            Settle(window);
            Assert.Equal("Show these steps", Help());
            group.Update(entries, startsCollapsed: false); // :1506 the flag going back down opens nothing
        });
        h.Main.OnMain(() =>
        {
            Settle(window);
            Assert.Equal("Show these steps", Help());
            Click(window, Mounted<Button>(group).First()); // the reader opens it
        });
        h.Main.OnMain(() =>
        {
            Assert.Equal("Hide these steps", Help());
            group.Update(entries, startsCollapsed: false);
        });
        h.Main.OnMain(() =>
        {
            Settle(window);
            Assert.Equal("Hide these steps", Help()); // a group the reader opened never snaps shut on its own
            Close(window);
        });
    });

    [Fact]
    [Trait("Req", "UI-CHAT-023")] // TimelineRows.swift:1522-1544 the earlier steps line
    public void Only_the_two_latest_steps_show_under_an_earlier_steps_line_that_opens_them_all_and_disappears() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        var frames = new ManualFrameSource();
        using var frameSeam = FrameClock.Source.Override(frames);
        var reveals = 0;
        var showsAll = new ObservableBoolBox();
        AvaloniaWindow window = null!;
        WorkSteps five = null!, three = null!, two = null!;
        ValueList<TimelineEntry> Rows(int count) => new(Enumerable.Range(1, count).Select(i => ToolEntry(Call(ToolCall.Kind.Read, "Read file" + i))));
        h.Main.OnMain(() =>
        {
            five = new WorkSteps(Rows(5), h.Runtime, showsAll.AsBinding());
            three = new WorkSteps(Rows(3), h.Runtime, Binding<bool>.Constant(false));
            two = new WorkSteps(Rows(2), h.Runtime, Binding<bool>.Constant(false));
            var panel = new StackPanel { Spacing = 40 };
            foreach (var steps in new Control[] { five, three, two }) panel.Children.Add(steps);
            var host = new Border { Child = panel };
            ThreadTimeline.SetRevealTimelineEnd(host, new TimelineRevealBox { Action = () => reveals++ });
            window = Show(host);
            // :1523, :1536 the singular is exactly "1 earlier step"; with two steps or fewer there is no line at all.
            Assert.Contains("3 earlier steps", Texts(five));
            Assert.Contains("1 earlier step", Texts(three));
            Assert.DoesNotContain(Texts(two), text => text.Contains("earlier", StringComparison.Ordinal));
            Assert.Equal(["Read file4", "Read file5"], Mounted<ToolRow>(five).SelectMany(row => Texts(row)).Where(text => text.StartsWith("Read ", StringComparison.Ordinal)));
            var line = Assert.Single(Mounted<Button>(five), button => Texts(button).Contains("3 earlier steps"));
            var glyph = Assert.Single(Mounted<SwiftImage>(line));
            var words = TextNamed(line, "3 earlier steps");
            // :1531-1540 the ellipsis in the 16-point icon column, the words 8 points on, 12 points of trailing padding.
            Assert.Equal("ellipsis", glyph.SymbolName);
            Assert.Equal(16, glyph.Width);
            Assert.Equal(Left(five, window), Left(glyph, window), 0.5);
            Assert.Equal(16 + 8, Left(words, window) - Left(five, window), 0.5);
            Assert.Equal(12, Left(line, window) + line.Bounds.Width - (Left(words, window) + words.Bounds.Width), 0.5);
            Face(words, 12, FontWeight.Normal, ".chat(.callout)");
            Paints(words.Foreground, ChromeTokens.SecondaryText, words, "words");
            Assert.Equal(10, SwiftImage.GetFont(glyph)!.Resolve().FontSize);
            Paints(glyph.Foreground, ChromeTokens.SecondaryText, glyph, "glyph");
            OnOneCentreLine(window, glyph, words);
            // :1525-1529 a tap shows every step and asks the timeline to reveal the end.
            Click(window, line);
            Assert.True(showsAll.Value);
            Assert.Equal(1, reveals);
        });
        Frames(h.Main, frames, 600);
        h.Main.OnMain(() =>
        {
            Settle(window);
            Assert.DoesNotContain(Texts(five), text => text.Contains("earlier", StringComparison.Ordinal)); // the line disappears
            Assert.Equal(5, Mounted<ToolRow>(five).Count());
            Close(window);
        });
    });

    [Fact]
    [Trait("Req", "UI-CHAT-061")] // TimelineRows.swift:1545-1553 what the agent said between two steps
    public void Narration_between_steps_is_dimmed_twelve_point_markdown_in_line_with_the_tool_rows() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        h.Main.OnMain(() =>
        {
            var steps = new WorkSteps(new ValueList<TimelineEntry>([
                ToolEntry(Call(ToolCall.Kind.Read, "Read a")),
                Reply("now the build"),
                ToolEntry(Call(ToolCall.Kind.Command, "make"))]), h.Runtime, Binding<bool>.Constant(true));
            var window = Show(steps);
            try
            {
                var narration = Assert.Single(Mounted<MarkdownView>(steps));
                Assert.Equal("now the build", narration.Text);
                // :1551-1552 markdown at point size 12, dimmed; :1550 in Chrome.secondaryText.
                Assert.Equal(12, MarkdownView.GetMarkdownPointSize(narration));
                Assert.True(MarkdownView.GetMarkdownDimmed(narration));
                Paints(Avalonia.Controls.Documents.TextElement.GetForeground(narration), ChromeTokens.SecondaryText, narration, "narration");
                // :1553 indented by iconWidth + iconSpacing = 24 points, in line with the tool rows' own text.
                Assert.Equal(24, narration.Margin.Left);
                var rows = Mounted<ToolRow>(steps).ToList();
                var label = TextNamed(rows[0], "Read a");
                Assert.Equal(Left(label, window), Left(narration, window), 0.5);
                Assert.Equal(Left(steps, window) + 24, Left(narration, window), 0.5);
                // The narration is one of the work rows: it sits between the two tool rows at the row gap.
                double Top(Control c) => c.TranslatePoint(default, window)!.Value.Y;
                Assert.Equal(20, Top(narration) - (Top(rows[0]) + rows[0].Bounds.Height), 0.5);
                Assert.Equal(20, Top(rows[1]) - (Top(narration) + narration.Bounds.Height), 0.5);
            }
            finally { Close(window); }
        });
    });
}
