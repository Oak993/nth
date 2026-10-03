using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Common;
using Marrow.UI.Port;
using AvaloniaWindow = Avalonia.Controls.Window;
using ChromeTokens = Marrow.UI.Chrome.Chrome;
using static Marrow.App.Tests.Fixtures.TimelineRowsFinChecks;
using static Marrow.App.Tests.Views.Chat.TimelineRowsPrSupport;

namespace Marrow.App.Tests.Views.Chat;

// Source: DroppyCode/App/Views/Chat/TimelineRows.swift (B3 52bc792), :1877-2064 HydraHeadStepsPopover and :1757-1853 where a
// tool row opens it.
[Collection("Headless")]
public sealed class TimelineRowsFinHeadStepsTests(HeadlessSession headless)
{
    private static object? InvokeStatic(string name, params object?[] arguments) =>
        typeof(HydraHeadStepsPopover).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, arguments);

    private static Control Body(HydraHeadInfo? head, IReadOnlyList<TimelineEntry> steps) =>
        Assert.IsAssignableFrom<Control>(InvokeStatic("Body", head, steps));

    private static HydraHeadInfo Finished(string summary, HydraHeadInfo.Status status = HydraHeadInfo.Status.Completed)
    {
        var start = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);
        return TimelineRowsFinTeam.Head(0, status) with { Task = "Tidy the popover", Summary = summary, StartedAt = start, FinishedAt = start.AddSeconds(64), ToolCalls = 2 };
    }

    private static double Top(Control control, Visual root) => control.TranslatePoint(default, root)!.Value.Y;

    [Fact]
    [Trait("Req", "UI-CHAT-055")] // TimelineRows.swift:1877-2064
    [Trait("Req", "PR-CHAT-154")] // TimelineRows.swift:1877-1931 the panel, :1934-1952 the header, :2008-2025 the status line
    [Trait("Req", "PR-CHAT-166")] // TimelineRows.swift:1900-1901 the task, :1922 the report, :1989 and :2000 later briefs and notices enable selection
    public void The_head_panel_is_440_wide_with_a_22_point_header_the_task_the_steps_at_the_row_gap_and_a_report_once_finished() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        using var locale = SwiftFormatters.CurrentLocale.Override("en_US");
        using var frameSeam = FrameClock.Source.Override(new ManualFrameSource());
        runtime.OnMain(() =>
        {
            var steps = new TimelineEntry[]
            {
                ToolEntry(Call(ToolCall.Kind.Read, "Read the popover")),
                Reply("what the head said"),
                UserEntry("steer it on", null),
                NoticeEntry(Notice.Level.Error, "it broke"),
            };
            var body = Body(Finished("the report words"), steps);
            var window = Show(new StackPanel { Children = { body } });
            try
            {
                // :1881-1882, :1930 a 440-point panel; :1892 scrolling past 520 points.
                Assert.Equal(440, Mounted<Border>(body).First(border => border.Padding == new Thickness(16)).Width);
                var scroll = Assert.IsType<PopoverScroll>(body);
                Assert.True(Assert.IsType<ScrollViewer>(scroll.Child).Height <= 520);
                // :1927-1928 padded 16; :1893 the sections are 16 points apart in a leading stack.
                var column = Assert.IsType<StackPanel>(Mounted<Border>(body).First(border => border.Padding == new Thickness(16)).Child);
                Assert.Equal(16, column.Spacing);
                Assert.Equal(HorizontalAlignment.Stretch, column.HorizontalAlignment);
                // :1935-1945 the header: a 22-point glyph, the name (headline, primary) and the status line on the trailing side.
                var glyph = Assert.Single(Mounted<HydraGlyph>(body));
                Assert.Equal(22, glyph.Size);
                var name = TextNamed(body, "Hank");
                Face(name, 13, FontWeight.SemiBold, ".chat(.headline)");
                Paints(name.Foreground, ChromeTokens.PrimaryText, name, "name");
                Near("nHeadSteps:1", 8, Left(name, window) - (Left(glyph, window) + 22));
                // :2010-2025 the status line: the outcome, the tool count, the duration, joined by a middle dot (U+00B7).
                var status = TextNamed(body, "Done · 2 tools · 1m 4s");
                Face(status, 10, FontWeight.Normal, ".chat(.caption)");
                Paints(status.Foreground, ChromeTokens.SecondaryText, status, "status line");
                Assert.Equal(1, status.MaxLines);
                OnOneCentreLine(window, glyph, name, status);
                Assert.True(Left(status, window) - (Left(name, window) + name.Bounds.Width) >= 12 - 0.5, "less than the 12-point spacer before the status line");
                // :1896-1902 the head's task: callout, primary, selectable.
                var task = Assert.Single(Mounted<SelectableTextBlock>(body), text => text.Text == "Tidy the popover");
                Face(task, 12, FontWeight.Normal, ".chat(.callout)");
                Paints(task.Foreground, ChromeTokens.PrimaryText, task, "task");
                // :2050 the section titles: caption semibold in Chrome.secondaryText, exactly Steps and Report.
                foreach (var titleText in new[] { "Steps", "Report" })
                {
                    var title = TextNamed(body, titleText);
                    Face(title, 10, FontWeight.SemiBold, ".chat(.caption, weight: .semibold)");
                    Paints(title.Foreground, ChromeTokens.SecondaryText, title, titleText);
                }
                // :1904 the steps list: row spacing 20 under its title.
                var stepsTitle = TextNamed(body, "Steps");
                var list = Assert.IsType<StackPanel>(stepsTitle.Parent);
                Assert.Equal(20, list.Spacing);
                // :1960-1976 a tool step: the icon column (16), then the label at callout in Chrome.secondaryText, up to two lines.
                var tool = TextNamed(body, "Read the popover");
                Face(tool, 12, FontWeight.Normal, ".chat(.callout)");
                Paints(tool.Foreground, ChromeTokens.SecondaryText, tool, "tool step");
                Assert.Equal(2, tool.MaxLines);
                // :1978-1983 a reply is primary selectable text; :1984-1990 a later brief is secondary selectable text.
                var reply = Assert.Single(Mounted<SelectableTextBlock>(body), text => text.Text == "what the head said");
                Paints(reply.Foreground, ChromeTokens.PrimaryText, reply, "reply");
                var brief = Assert.Single(Mounted<SelectableTextBlock>(body), text => text.Text == "steer it on");
                Paints(brief.Foreground, ChromeTokens.SecondaryText, brief, "later brief");
                // :1991-2002 a notice: its level glyph (caption, in the level's colour) and message (callout, selectable).
                var notice = Assert.Single(Mounted<SelectableTextBlock>(body), text => text.Text == "it broke");
                Paints(notice.Foreground, ChromeTokens.Danger, notice, "notice");
                var noticeGlyph = Assert.Single(Mounted<SwiftImage>(body), image => image.SymbolName == "exclamationmark.octagon");
                Paints(noticeGlyph.Foreground, ChromeTokens.Danger, noticeGlyph, "notice glyph");
                Near("notice text after its 16-point glyph column", 16 + 8, Left(notice, window) - Left(noticeGlyph, window));
                // :1917-1924 a finished head with a report: a Report section, 8 points under its title, the text primary and selectable.
                var report = Assert.Single(Mounted<SelectableTextBlock>(body), text => text.Text == "the report words");
                Face(report, 12, FontWeight.Normal, ".chat(.callout)");
                Paints(report.Foreground, ChromeTokens.PrimaryText, report, "report");
                Near("nHeadSteps:2", 8, Top(report, window) - (Top(TextNamed(body, "Report"), window) + TextNamed(body, "Report").Bounds.Height));
                // :1900-1901, :1922, :1978-1983, :1984-1990, :1999-2000 .textSelection(.enabled): the pointer is an I-beam over each of the
                // five selectable lines (PR-CHAT-166 windows_path; a SelectableTextBlock has no cursor of its own with no control theme).
                // A selection in each of them is drawn, in the system's selected-text colour (a SelectableTextBlock has no SelectionBrush with
                // no control theme loaded, so a drag would select text that is never painted; ED-256 (5)).
                var system = SwiftColor.SelectedTextBackgroundColor.Resolve(window.ActualThemeVariant);
                Assert.NotEqual(0, system.A);
                foreach (var selectable in new[] { task, reply, brief, notice, report })
                {
                    Hover(window, selectable);
                    Assert.Equal("Ibeam", selectable.Cursor?.ToString());
                    selectable.SelectAll();
                    Assert.Equal(selectable.Text, selectable.SelectedText);
                    Assert.True((selectable.SelectionBrush as ISolidColorBrush)?.Color == system,
                        $"\"{selectable.Text}\": the selection is drawn with {selectable.SelectionBrush?.ToString() ?? "no brush (nothing is drawn)"}");
                }
            }
            finally { Close(window); }
        });
    });

    // Review round 4 (mutant M7): the five selectable lines are Texts with .fixedSize(horizontal: false, vertical: true) (:1901,
    // :1923, :1983, :1990, :2001), which wrap at the width the 440-point panel offers and grow downward; none runs on in one line.
    [Fact]
    [Trait("Rule", "R-TEST-4")] // Partial proof UI-CHAT-055 (the panel's lines wrap at its width): TimelineRows.swift:1896-1902, :1917-1924, :1978-1990, :1991-2002, :1927-1930
    public void A_long_task_reply_brief_notice_and_report_each_wrap_inside_the_440_point_panel() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        using var frameSeam = FrameClock.Source.Override(new ManualFrameSource());
        runtime.OnMain(() =>
        {
            string Long(string word) => string.Join(" ", Enumerable.Repeat(word, 60));
            var steps = new TimelineEntry[] { Reply(Long("reply")), UserEntry(Long("brief"), null), NoticeEntry(Notice.Level.Warning, Long("notice")) };
            var start = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);
            var head = TimelineRowsFinTeam.Head(0, HydraHeadInfo.Status.Completed) with
            {
                Task = Long("task"), Summary = Long("report"), StartedAt = start, FinishedAt = start.AddSeconds(64), ToolCalls = 2,
            };
            var body = Body(head, steps);
            var window = Show(new StackPanel { Children = { body } });
            try
            {
                var panel = Mounted<Border>(body).First(border => border.Padding == new Thickness(16));
                var right = Left(panel, window) + 440 - 16; // :1927-1930 the 440-point frame, padded 16
                var lines = Mounted<SelectableTextBlock>(body).ToArray();
                Assert.Equal(5, lines.Length);
                foreach (var line in lines)
                {
                    Assert.True(line.TextLayout.TextLines.Count > 1, $"\"{line.Text![..8]}...\" runs on in one line {line.TextLayout.Width:0} points wide");
                    Assert.True(Left(line, window) + line.TextLayout.Width <= right + 0.5, $"\"{line.Text![..8]}...\" runs past the panel's padding");
                }
            }
            finally { Close(window); }
        });
    });

    [Fact]
    [Trait("Req", "UI-CHAT-055")] // TimelineRows.swift:1906-1909 the empty state, :1916 no report until the head is finished
    [Trait("Req", "PR-CHAT-154")] // TimelineRows.swift:1906-1909 No steps yet.
    public void With_no_steps_the_panel_reads_No_steps_yet_and_a_running_head_has_no_report_section() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        using var frameSeam = FrameClock.Source.Override(new ManualFrameSource());
        runtime.OnMain(() =>
        {
            var running = TimelineRowsFinTeam.Head(0, HydraHeadInfo.Status.Running) with { Task = "", ToolCalls = 1, Summary = "not yet" };
            var body = Body(running, []);
            var window = Show(new StackPanel { Children = { body } });
            try
            {
                var empty = TextNamed(body, "No steps yet.");
                Face(empty, 12, FontWeight.Normal, ".chat(.callout)");
                Paints(empty.Foreground, ChromeTokens.SecondaryText, empty, "empty state");
                Assert.DoesNotContain("Report", Texts(body));
                Assert.DoesNotContain("not yet", Texts(body));
                Assert.Contains("Working… · 1 tool", Texts(body));
                // :1896 an empty task is not shown.
                Assert.DoesNotContain(Mounted<SelectableTextBlock>(body), text => text.Text == "");
            }
            finally { Close(window); }
        });
    });

    [Fact]
    [Trait("Req", "UI-CHAT-055")] // TimelineRows.swift:1892 the steps scroll once the panel passes 520 points
    public void A_head_with_many_steps_scrolls_at_520_points() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        using var frameSeam = FrameClock.Source.Override(new ManualFrameSource());
        runtime.OnMain(() =>
        {
            var steps = Enumerable.Range(1, 60).Select(index => ToolEntry(Call(ToolCall.Kind.Read, "Read file " + index))).ToArray();
            var body = Body(Finished("done"), steps);
            var window = Show(new StackPanel { Children = { body } });
            try
            {
                Assert.Equal(520, Assert.IsType<ScrollViewer>(Assert.IsType<PopoverScroll>(body).Child).Height);
            }
            finally { Close(window); }
        });
    });
}
