using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.App.Views.Composer;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;
using Marrow.Testing;
using Marrow.UI.Common;
using Marrow.UI.Port;
using AvaloniaWindow = Avalonia.Controls.Window;
using ChromeTokens = Marrow.UI.Chrome.Chrome;
using static Marrow.App.Tests.Fixtures.TimelineRowsFinChecks;
using static Marrow.App.Tests.Views.Chat.TimelineRowsPrSupport;

namespace Marrow.App.Tests.Views.Chat;

// Source: DroppyCode/App/Views/Chat/TimelineRows.swift (B3 52bc792), :393-476 HydraHeadsWorkingRow and FinishedHeadPill,
// :527-596 HydraBriefRow, :648-783 HydraDelegationBlock.
[Collection("Headless")]
public sealed class TimelineRowsFinHeadRowsTests(HeadlessSession headless)
{
    private static Border PillOf(Control row) => Mounted<Border>(row).First(border => border.Padding == new Thickness(12, 8, 14, 8));

    private static double Top(Control control, Visual root) => control.TranslatePoint(default, root)!.Value.Y;

    [Fact]
    [Trait("Req", "UI-CHAT-051")] // TimelineRows.swift:537-595 the lead's brief pill
    public void The_brief_pill_is_a_trailing_tinted_pill_that_reads_starting_to_cook_with_a_shimmer_until_the_head_answers() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frameSeam = FrameClock.Source.Override(new ManualFrameSource());
        using var genie = ChromeTokens.ReduceMotionPlatform.Override(new FakeGeniePlatform());
        HydraBriefRow row = null!;
        AvaloniaWindow window = null!;
        h.Main.Loop.Run(() => h.Runtime.EnsureLoaded(), AppModelHarness.Bound);
        h.Main.OnMain(() =>
        {
            // :548 cooking needs a head that is running: an idle head with nothing back yet reads "<name>'s brief" with no shimmer.
            var idleEntry = UserEntry("Build the thing", null);
            var idle = new HydraBriefRow(((TimelineItem.Content.User)idleEntry.Item.TimelineItemContent).Value, idleEntry, h.Runtime);
            var idleWindow = Show(new StackPanel { Children = { idle } });
            try
            {
                Assert.Equal("Hank's brief", TextNamed(idle, "Hank's brief").Text);
                Assert.False(Assert.Single(Mounted<HydraGlyph>(idle)).IsRunning);
                Assert.DoesNotContain(Mounted<Border>(idle), border => border.Background is LinearGradientBrush);
            }
            finally { Close(idleWindow); }
            h.Runtime.RehearseTurn("Build the thing");
            var entry = h.Runtime.Entries.Last(candidate => candidate.TimelineEntryKind == TimelineEntry.Kind.User);
            var file = new Attachment { Name = "a.txt", Path = @"C:\no-such-folder\a.txt", MimeType = "text/plain" };
            var message = ((TimelineItem.Content.User)entry.Item.TimelineItemContent).Value with { Attachments = new ValueList<Attachment>([file]) };
            row = new HydraBriefRow(message, entry, h.Runtime);
            var host = new Border { Width = 600, Child = row };
            window = Show(new StackPanel { Children = { host } });
            try
            {
                var pill = PillOf(row);
                // :574-577 12 leading, 14 trailing, 8 vertical, radius 18, .tint at 0.1.
                Assert.Equal(new CornerRadius(18), pill.CornerRadius);
                Paints(pill.Background, ChromeTokens.Accent.Opacity(0.1), pill, "pill fill");
                // :588-589 trailing-aligned with 96 points of leading padding, like a sent message.
                Assert.Equal(new Thickness(96, 0, 0, 0), row.Padding);
                Near("nHeadRows:1", Left(host, window) + 600, Left(pill, window) + pill.Bounds.Width);
                // :548 cooking: the title is "<name> is starting to cook" and an ellipsis (U+2026); the glyph runs.
                var title = TextNamed(row, "Hank is starting to cook\u2026");
                Face(title, 12, FontWeight.Medium, ".chat(.callout, weight: .medium)");
                Paints(title.Foreground, ChromeTokens.PrimaryText.Opacity(0.9), title, "title");
                Assert.Equal(1, title.MaxLines);
                Assert.True(Assert.Single(Mounted<HydraGlyph>(row)).IsRunning);
                // :558-562 the title carries the shimmer while cooking: a gradient band over it.
                Assert.Contains(Mounted<Border>(row), border => border.Background is LinearGradientBrush);
                // :556 HStack(spacing: 8): glyph 18, title, chevron on one centre line.
                var glyph = Assert.Single(Mounted<HydraGlyph>(row));
                Assert.Equal(18, glyph.Size);
                Near("nHeadRows:2", 8, Left(title, window) - (Left(glyph, window) + 18));
                var chevron = Assert.Single(Mounted<SwiftImage>(row), image => image.SymbolName == "chevron.right");
                var font = SwiftImage.GetFont(chevron)!.Resolve();
                Assert.Equal(10, font.FontSize);
                Assert.Equal(FontWeight.SemiBold, font.FontWeight);
                Paints(chevron.Foreground, ChromeTokens.SecondaryText, chevron, "chevron");
                Near("nHeadRows:3", 8, Left(chevron, window) - (Left(title, window) + title.Bounds.Width));
                OnOneCentreLine(window, glyph, title, chevron);
                // :582 the tooltip; :581 never focusable; :586 the accessibility label is the title.
                var button = Assert.Single(Mounted<Button>(row), candidate => ToolTip.GetTip(candidate) as string == "Show the brief");
                Assert.False(button.Focusable);
                Assert.Equal("Hank is starting to cook\u2026", AutomationProperties.GetName(button));
                // :549-552 attachments, if any, sit above the pill, the stack 6 points apart.
                var strip = Assert.Single(Mounted<AttachmentStrip>(row));
                Near("nHeadRows:4", 6, Top(button, window) - (Top(strip, window) + strip.Bounds.Height));
                h.Runtime.Rehearse(new ProviderEvent.ToolStarted("tool-1", Call(ToolCall.Kind.Read, "Read a", ToolCall.Status.Running)));
            }
            catch { Close(window); throw; }
        });
        h.Main.OnMain(() =>
        {
            try
            {
                Settle(window);
                // The head has output: "<name>'s brief" with a straight apostrophe (U+0027) and no shimmer.
                var title = TextNamed(row, "Hank\u0027s brief");
                Assert.Equal("Hank's brief", title.Text);
                Assert.False(Assert.Single(Mounted<HydraGlyph>(row)).IsRunning);
                Assert.DoesNotContain(Mounted<Border>(row), border => border.Background is LinearGradientBrush);
            }
            finally { Close(window); }
        });
    });

    [Theory]
    [InlineData(HydraHeadInfo.Status.Failed, "Hank failed")]
    [InlineData(HydraHeadInfo.Status.Stopped, "Hank was stopped")]
    [InlineData(HydraHeadInfo.Status.Completed, "Hank finished working")]
    [Trait("Req", "UI-CHAT-052")] // TimelineRows.swift:438-476 the finished head pill
    public void A_finished_head_pill_names_its_outcome_beside_its_18_point_glyph_with_the_report_one_tap_away(HydraHeadInfo.Status status, string expected) => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        using var frameSeam = FrameClock.Source.Override(new ManualFrameSource());
        runtime.OnMain(() =>
        {
            var pillRow = new FinishedHeadPill(TimelineRowsFinTeam.Head(0, status));
            var window = Show(new StackPanel { Children = { pillRow } });
            try
            {
                var pill = PillOf(pillRow);
                Assert.Equal(new CornerRadius(18), pill.CornerRadius);
                Paints(pill.Background, ChromeTokens.Accent.Opacity(0.1), pill, "pill fill");
                // :454 the name in the head's own name style over Chrome.primaryText at 0.75, then the outcome, callout medium.
                var text = Assert.Single(Mounted<TextBlock>(pillRow));
                var runs = text.Inlines!.OfType<Run>().ToList();
                Assert.Equal(expected, string.Concat(runs.Select(run => run.Text)));
                Assert.Equal("Hank", runs[0].Text);
                Paints(runs[0].Foreground, ChromeTokens.PrimaryText.Opacity(0.75), text, "name");
                Assert.Equal(12, runs[1].FontSize);
                Assert.Equal(FontWeight.Medium, runs[1].FontWeight);
                Assert.Equal(1, text.MaxLines);
                Assert.Equal(AutomationProperties.GetName(pillRow), expected);
                var glyph = Assert.Single(Mounted<HydraGlyph>(pillRow));
                Assert.Equal(18, glyph.Size);
                Assert.Equal(status, glyph.Status);
                var chevron = Assert.Single(Mounted<SwiftImage>(pillRow), image => image.SymbolName == "chevron.right");
                Paints(chevron.Foreground, ChromeTokens.SecondaryText, chevron, "chevron");
                Near("nHeadRows:5", 8, Left(text, window) - (Left(glyph, window) + 18));
                OnOneCentreLine(window, glyph, text, chevron);
                Assert.Equal("Show the report", ToolTip.GetTip(Assert.Single(Mounted<Button>(pillRow))));
            }
            finally { Close(window); }
        });
    });

    [Fact]
    [Trait("Req", "UI-CHAT-052")] // TimelineRows.swift:398-418 the finished heads' pills stack 6 apart on one leading edge
    public void The_finished_heads_stack_six_points_apart_from_one_leading_edge_with_96_points_clear_and_leave_under_the_settle_glide() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        using var frameSeam = FrameClock.Source.Override(frames);
        var (store, app, runtime) = TimelineRowsFinTeam.Make(
            TimelineRowsFinTeam.Head(0, HydraHeadInfo.Status.Failed), TimelineRowsFinTeam.Head(1, HydraHeadInfo.Status.Completed));
        using (store)
        {
            HydraHeadsWorkingRow row = null!;
            AvaloniaWindow window = null!;
            store.Main.OnMain(() =>
            {
                row = new HydraHeadsWorkingRow(new ValueList<int>([1, 0, 5]), runtime);
                var host = new Border { Width = 500, Child = row };
                Marrow.App.Port.AppEnvironment.SetAppModel(host, app);
                window = Show(new StackPanel { Children = { host } });
                row.ApplyTeam(app.Children(runtime.ThreadID));
                Settle(window);
                var pills = Mounted<FinishedHeadPill>(row).ToList();
                Assert.Equal(["Walter finished working", "Hank failed"], pills.Select(pill => AutomationProperties.GetName(pill)));
                // :409 VStack(alignment: .leading, spacing: 6): every pill starts at the same left edge, 6 points apart.
                Near("nHeadRows:6", Left(pills[0], window), Left(pills[1], window));
                Near("nHeadRows:7", 6, Top(pills[1], window) - (Top(pills[0], window) + pills[0].Bounds.Height));
                // :416-417 the row takes the width less 96 points.
                Assert.Equal(new Thickness(0, 0, 96, 0), row.Padding);
                Assert.True(pills.All(pill => Left(pill, window) + pill.Bounds.Width <= 500 - 96 + 0.5 + Left(row, window)));
                // :415 .animation(.smooth(duration: 0.3)): a head that goes leaves over 0.3 s under the settle glide and the one under it follows.
                row.ApplyTeam(app.Children(runtime.ThreadID).Where(thread => thread.Hydra?.Index != 0).ToList());
            });
            frames.Advance(TimeSpan.Zero);
            store.Main.AdvanceMilliseconds(100);
            frames.Advance(TimeSpan.FromMilliseconds(100));
            store.Main.OnMain(() =>
            {
                Assert.Contains("Hank failed", Mounted<FinishedHeadPill>(row).Select(pill => AutomationProperties.GetName(pill)));
            });
            store.Main.AdvanceMilliseconds(1500);
            frames.Advance(TimeSpan.FromMilliseconds(1500));
            store.Main.OnMain(() =>
            {
                try
                {
                    Settle(window);
                    Assert.Equal(["Walter finished working"], Mounted<FinishedHeadPill>(row).Select(pill => AutomationProperties.GetName(pill)));
                    Near("nHeadRows:6b", Top(row, window), Top(Assert.Single(Mounted<FinishedHeadPill>(row)), window));
                }
                finally { Close(window); }
            });
        }
    });

    // 1 - e^(-w t) (1 + w t) with w = 2 pi / response: SwiftUI's `.smooth(duration:)`, a critically damped spring of that response (Chrome.swift:960 `.smooth(duration: 0.32)`).
    private static double Smooth(double response, double seconds) => seconds <= 0 ? 0 : 1 - Math.Exp(-2 * Math.PI / response * seconds) * (1 + 2 * Math.PI / response * seconds);

    [Fact]
    [Trait("Req", "UI-CHAT-052")] // TimelineRows.swift:410-411 `.transition(.settleGlide)`, Chrome.swift:949-955, :994-1000: a joining finished-head pill arrives softly, opacity 0 to 1 and a 4 point rise, on smooth(0.32)
    public void A_finished_head_pill_that_joins_arrives_from_nothing_with_a_four_point_rise_along_the_soft_appear_curve() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        using var frameSeam = FrameClock.Source.Override(frames);
        using var genie = ChromeTokens.ReduceMotionPlatform.Override(new FakeGeniePlatform());
        var (store, app, runtime) = TimelineRowsFinTeam.Make(
            TimelineRowsFinTeam.Head(0, HydraHeadInfo.Status.Failed), TimelineRowsFinTeam.Head(1, HydraHeadInfo.Status.Completed));
        using (store)
        {
            HydraHeadsWorkingRow row = null!;
            AvaloniaWindow window = null!;
            store.Main.OnMain(() =>
            {
                row = new HydraHeadsWorkingRow(new ValueList<int>([1, 0]), runtime);
                var host = new Border { Width = 500, Child = row };
                Marrow.App.Port.AppEnvironment.SetAppModel(host, app);
                window = Show(new StackPanel { Children = { host } });
                // One head is finished and on show; the other joins.
                row.ApplyTeam(app.Children(runtime.ThreadID).Where(thread => thread.Hydra?.Index == 1).ToList());
                Settle(window);
            });
            store.Main.AdvanceMilliseconds(1500);
            frames.Advance(TimeSpan.FromMilliseconds(1500));
            // What the pill is drawn with: its own opacity and every ancestor's up to the row, and the rise its transition and its ancestors give it.
            FinishedHeadPill Joining() => Mounted<FinishedHeadPill>(row).Single(pill => AutomationProperties.GetName(pill) == "Hank failed");
            (double Opacity, double Rise) Drawn()
            {
                var pill = Joining();
                var opacity = 1.0; var rise = 0.0;
                for (Visual? at = pill; at is not null && !ReferenceEquals(at, row); at = at.GetVisualParent())
                {
                    opacity *= at.Opacity;
                    if (at is Visual { RenderTransform: { } transform }) rise += transform.Value.M32;
                }
                return (opacity, rise);
            }
            store.Main.OnMain(() =>
            {
                row.ApplyTeam(app.Children(runtime.ThreadID).ToList());
                Settle(window);
            });
            frames.Advance(TimeSpan.Zero);
            var samples = new List<(double At, double Opacity, double Rise)>();
            try
            {
                for (var at = 10.0; at <= 400; at += 10)
                {
                    store.Main.AdvanceMilliseconds(10);
                    frames.Advance(TimeSpan.FromMilliseconds(10));
                    store.Main.OnMain(() => { Settle(window); var drawn = Drawn(); samples.Add((at, drawn.Opacity, drawn.Rise)); });
                }
                foreach (var (at, opacity, rise) in samples)
                {
                    var p = Smooth(0.32, at / 1000);
                    Assert.True(Math.Abs(opacity - p) <= 0.04, $"at {at} ms the pill is {opacity:F3} in, smooth(0.32) has {p:F3}");
                    Assert.True(Math.Abs(rise - 4 * (1 - p)) <= 0.2, $"at {at} ms the pill is {rise:F2} points low, smooth(0.32) has {4 * (1 - p):F2}");
                }
                Assert.Contains(samples, sample => sample.Opacity is > 0.05 and < 0.95); // it arrives over time, not at once
            }
            finally { store.Main.OnMain(() => Close(window)); }
        }
    });

    private const string Hank = "{\"task\": \"Tidy the popover\", \"prompt\": \"p\", \"name\": \"Walter\", \"project\": \"/Users/me/code/website\"}";

    [Fact]
    [Trait("Req", "UI-CHAT-054")] // TimelineRows.swift:652-783 the lead's delegation card
    public void The_delegation_card_is_a_tinted_pill_with_a_16_point_mark_a_title_and_one_row_per_head_on_centre_lines() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        using var frameSeam = FrameClock.Source.Override(new ManualFrameSource());
        runtime.OnMain(() =>
        {
            const string anon = "{\"task\": \"Write tests\", \"prompt\": \"p\", \"project\": \"docs\"}";
            var sent = new HydraDelegationBlock("[\n" + Hank + ",\n" + anon + "\n]", sent: true);
            var streaming = new HydraDelegationBlock("[\n" + Hank + ",\n{\"task\": \"Wri");
            var empty = new HydraDelegationBlock("[\n{\"task\": \"a");
            var panel = new StackPanel { Spacing = 30 };
            foreach (var block in new Control[] { sent, streaming, empty }) panel.Children.Add(block);
            var window = Show(panel);
            try
            {
                var pill = PillOf(sent);
                // :691-694 12 leading, 14 trailing, 8 vertical, radius 18, .tint at 0.1.
                Assert.Equal(new CornerRadius(18), pill.CornerRadius);
                Paints(pill.Background, ChromeTokens.Accent.Opacity(0.1), pill, "pill fill");
                // :658 the outer stack is 6 points apart: the header, then the head list.
                var column = Assert.IsType<StackPanel>(pill.Child);
                Assert.Equal(6, column.Spacing);
                var header = Assert.IsAssignableFrom<StackPanel>(column.Children[0]);
                var rows = Assert.IsType<StackPanel>(column.Children[1]);
                Assert.Equal(5, rows.Spacing); // :676 the head list is 5 points apart
                // :659-674 the header: a 16-point mark, 8 points before the title (callout medium), then the sent check.
                var mark = Assert.Single(Mounted<HydraMarkImage>(header));
                Assert.Equal(16, mark.Bounds.Width);
                Paints(Assert.Single(Mounted<SwiftImage>(mark)).Foreground, ChromeTokens.SecondaryText, mark, "mark");
                var title = TextNamed(header, "Summoned 2 heads");
                Face(title, 12, FontWeight.Medium, ".chat(.callout, weight: .medium)");
                Near("nHeadRows:8", 8, Left(title, window) - (Left(mark, window) + 16));
                var check = Assert.Single(Mounted<SwiftImage>(header), image => image.SymbolName == "checkmark.circle.fill");
                Assert.Equal("sent", AutomationProperties.GetName(check));
                Assert.Equal(12, SwiftImage.GetFont(check)!.Resolve().FontSize);
                Paints(check.Foreground, SwiftColor.Rgb(0x34C759), check, "sent check is green");
                OnOneCentreLine(window, mark, title, check);
                // :711-741 each head: the glyph of the named head (or the mark) in a 16-point column, then the task (caption, 0.75).
                var rowViews = rows.Children.OfType<StackPanel>().ToList();
                Assert.Equal(2, rowViews.Count);
                var walter = Assert.Single(Mounted<HydraGlyph>(rowViews[0]));
                Assert.Equal(16, walter.Size);
                var task = TextNamed(rowViews[0], "Tidy the popover");
                Face(task, 10, FontWeight.Normal, ".chat(.caption)");
                Paints(task.Foreground, ChromeTokens.PrimaryText.Opacity(0.75), task, "task");
                Near("nHeadRows:9", 8, Left(task, window) - (Left(walter, window) + 16));
                // :727-738 the other project: an arrow and the project's last component, caption2 medium, 2 points further on.
                var project = TextNamed(rowViews[0], "website");
                Face(project, 10, FontWeight.Medium, ".chat(.caption2, weight: .medium)");
                Paints(project.Foreground, ChromeTokens.SecondaryText, project, "project");
                var arrow = Assert.Single(Mounted<SwiftImage>(rowViews[0]), image => image.SymbolName == "arrow.right");
                Near("nHeadRows:10", 3, Left(project, window) - (Left(arrow, window) + arrow.Bounds.Width));
                Near("nHeadRows:11", 2 + 8, Left(arrow, window) - (Left(task, window) + task.Bounds.Width));
                Assert.Equal("in website", AutomationProperties.GetName((Control)project.Parent!));
                OnOneCentreLine(window, walter, task, arrow, project);
                // The unnamed head wears the mark in its slot.
                Assert.Single(Mounted<HydraMarkImage>(rowViews[1]));
                // :680-687 still streaming with entries listed: a final line "Writing the next brief…" under the clear column.
                var next = TextNamed(streaming, "Writing the next brief\u2026");
                Face(next, 10, FontWeight.Normal, ".chat(.caption)");
                Paints(next.Foreground, ChromeTokens.SecondaryText, next, "next brief");
                Near("nHeadRows:12", 16 + 8, Left(next, window) - Left(streaming, window) - 12);
                // :746 streaming or nothing listed: exactly Summoning heads, and nothing else.
                Assert.Equal(["Summoning heads"], Texts(empty));
            }
            finally { Close(window); }
        });
    });
}
