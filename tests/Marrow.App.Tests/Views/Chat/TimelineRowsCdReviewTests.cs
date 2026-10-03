using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;
using static Marrow.App.Tests.Fixtures.TimelineRowsFinChecks;
using static Marrow.App.Tests.Views.Chat.TimelineRowsPrSupport;

namespace Marrow.App.Tests.Views.Chat;

// REVIEW (adversarial review of unit blitz-timelinerows-cd, 2026-10-04). Source: DroppyCode/App/Views/Chat/TimelineRows.swift (B3 52bc792).
[Collection("Headless")]
public sealed class TimelineRowsCdReviewTests(HeadlessSession headless)
{
    // :1934-1946 header(_:): HStack(alignment: .center, spacing: TimelineMetrics.iconSpacing) { HydraGlyph(size: 22); Text(name);
    // Spacer(minLength: 12); Text(statusLine).lineLimit(1) }. The stack's 8-point spacing stands on both sides of the Spacer (the
    // reading this unit's own tests take for :1774 and :2833-2838: "two 8-point stack gaps, 24 points in all"), so the status line
    // never comes nearer the name than 8 + 12 + 8 = 28 points: when the row is short of room the texts give way, not the gap.
    // The port's DockPanel keeps only 12 + 8 = 20 (status.Margin, TimelineRows.cs HydraHeadStepsPopover.Header).
    [Fact]
    [Trait("Req", "UI-CHAT-055")] // TimelineRows.swift:1934-1946 the minimum 12-point spacer before the status line, between the stack's two 8-point gaps
    public void REVIEW_the_head_headers_status_line_keeps_the_spacer_and_both_stack_gaps_from_the_name_when_room_is_short() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        using var locale = SwiftFormatters.CurrentLocale.Override("en_US");
        using var frameSeam = FrameClock.Source.Override(new ManualFrameSource());
        runtime.OnMain(() =>
        {
            var start = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);
            var head = TimelineRowsFinTeam.Head(0, HydraHeadInfo.Status.Stopped) with { Task = "Tidy the popover", StartedAt = start, FinishedAt = start.AddSeconds(3599), ToolCalls = 12 };
            Control Header() => Assert.IsAssignableFrom<Control>(typeof(HydraHeadStepsPopover).GetMethod("Header", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [head]));
            // The header's own width with all its room: then four points less than the Swift stack needs (glyph, 8, name, 8, 12, 8, status).
            var probe = Header();
            var probeWindow = Show(new StackPanel { Children = { new Border { Child = probe, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left } } });
            var natural = probe.Bounds.Width;
            Close(probeWindow);
            var header = Header();
            var window = Show(new StackPanel { Children = { new Border { Width = natural + 4, Child = header, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left } } });
            try
            {
                var name = TextNamed(header, "Hank");
                var status = TextNamed(header, "Stopped · 12 tools · 59m 59s");
                var gap = Left(status, window) - (Left(name, window) + name.Bounds.Width);
                Assert.True(gap >= 28 - 0.5, $"the status line stands {gap} points from the name; the Swift stack keeps 8 + 12 + 8 = 28");
            }
            finally { Close(window); }
        });
    });

    // :1934-1946 again, the other side of "minimum": Spacer(minLength: 12) shrinks to 12 and no further, so the stack's ideal
    // width is exactly glyph 22 + 8 + name + 8 + 12 + 8 + status, and the status line stands exactly 28 points from the name
    // there. A wider fixed margin (a 36-point one passes the >= 28 checks) keeps the texts apart by more than the Swift ever does.
    [Fact]
    [Trait("Req", "UI-CHAT-055")] // TimelineRows.swift:1934-1946 the minimum 12-point spacer before the status line, between the stack's two 8-point gaps
    public void REVIEW_the_head_headers_ideal_width_is_the_swift_stacks_with_the_spacer_at_its_12_point_minimum() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        using var locale = SwiftFormatters.CurrentLocale.Override("en_US");
        using var frameSeam = FrameClock.Source.Override(new ManualFrameSource());
        runtime.OnMain(() =>
        {
            var start = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);
            var head = TimelineRowsFinTeam.Head(0, HydraHeadInfo.Status.Stopped) with { Task = "Tidy the popover", StartedAt = start, FinishedAt = start.AddSeconds(3599), ToolCalls = 12 };
            var header = Assert.IsAssignableFrom<Control>(typeof(HydraHeadStepsPopover).GetMethod("Header", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [head]));
            var window = Show(new StackPanel { Children = { new Border { Child = header, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left } } });
            try
            {
                var name = TextNamed(header, "Hank");
                var status = TextNamed(header, "Stopped · 12 tools · 59m 59s");
                var swiftIdeal = 22 + 8 + name.Bounds.Width + 8 + 12 + 8 + status.Bounds.Width;
                Assert.True(Math.Abs(header.Bounds.Width - swiftIdeal) <= 0.5, $"the header's ideal width is {header.Bounds.Width}; the Swift stack's is {swiftIdeal}");
                var gap = Left(status, window) - (Left(name, window) + name.Bounds.Width);
                Assert.True(Math.Abs(gap - 28) <= 0.5, $"at its ideal width the status line stands {gap} points from the name; the Swift stack keeps exactly 8 + 12 + 8 = 28");
            }
            finally { Close(window); }
        });
    });
}
