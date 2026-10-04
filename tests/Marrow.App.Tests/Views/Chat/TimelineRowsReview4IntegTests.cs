using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Marrow.App.Port;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.App.Views.Composer;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;
using Xunit;
using ChromeTokens = Marrow.UI.Chrome.Chrome;
using AvaloniaWindow = Avalonia.Controls.Window;
using static Marrow.App.Tests.Views.Chat.TimelineRowsPrSupport;
using static Marrow.App.Tests.Fixtures.TimelineRowsFinChecks;

namespace Marrow.App.Tests.Views.Chat;

// REVIEW4 (adversarial re-check of unit/integ-tlrab at 6dad8310c). Failing tests, committed as findings; no fix here.
[Collection("Headless")]
public sealed class TimelineRowsReview4IntegTests(HeadlessSession headless)
{
    private const string Hank = "{\"task\": \"Tidy the popover\", \"prompt\": \"Tidy it.\", \"name\": \"Hank\"}";
    private const string OneClosedStreaming = "[\n" + Hank + ",\n";
    private const string OneClosedShort = "[\n" + Hank + "\n]";

    private static void Run(TestRuntime main, ManualFrameSource frames, AvaloniaWindow window, double milliseconds, Action? each = null)
    {
        for (var elapsed = 10.0; elapsed <= milliseconds + 0.001; elapsed += 10)
        {
            main.AdvanceMilliseconds(10);
            frames.Advance(TimeSpan.FromMilliseconds(10));
            main.OnMain(() => { Settle(window); each?.Invoke(); });
        }
    }

    // TimelineRows.swift:694-701 the wave is the card's `.overlay`: SwiftUI sizes an overlay to the view it overlays (the padded VStack with its pill
    // background), so the dot field is laid out over the card and nothing else. 6dad8310c moved the wave into the card's frame panel, which takes the whole
    // box the card is given (900 by 700 in this host), so the field is laid out over that box and only clipped to the pill (7147e44b8: 171 by 54).
    [Fact]
    [Trait("Req", "PR-CHAT-149")]
    public void REVIEW4_the_sent_wave_is_laid_out_over_the_card_not_over_the_box_the_card_is_given() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        var frames = new ManualFrameSource();
        using var frameSeam = FrameClock.Source.Override(frames);
        using var genie = ChromeTokens.ReduceMotionPlatform.Override(new FakeGeniePlatform());
        HydraDelegationBlock card = null!;
        AvaloniaWindow window = null!;
        h.Main.OnMain(() => { card = new HydraDelegationBlock(OneClosedShort); window = Show(card); card.Update(OneClosedShort, sent: true); });
        try
        {
            h.Main.OnMain(() => Settle(window));
            frames.Advance(TimeSpan.Zero);
            Run(h.Main, frames, window, 400);
            h.Main.OnMain(() =>
            {
                var pill = Mounted<Border>(card).First(border => border.Child is StackPanel { Spacing: 6 });
                var sweep = Mounted<DotFieldSweep>(card).Single();
                var pillBox = new Rect(pill.TranslatePoint(default, card)!.Value, pill.Bounds.Size);
                var sweepBox = new Rect(sweep.TranslatePoint(default, card)!.Value, sweep.Bounds.Size);
                Assert.Equal(pillBox, sweepBox);
            });
        }
        finally { h.Main.OnMain(() => Close(window)); }
    });

    // ED-264 (3) and OD-0099 Q-RM A: a transition OD-0076 adds where the Mac snaps is stilled by reduced motion. TimelineRows.swift:703-704 animates the card only
    // when `sent` or `delegations.count` changes; a block that closes with the same count (here one head, streaming then complete) changes neither, and the
    // streamed flush is made in no animation (ThreadRuntime.swift has withAnimation only at :2947 and :2953, the sent card's leaving), so the Mac draws it at once.
    // Under reduced motion the port must cut it: the card is its new height on the first frame. 6dad8310c eases it along panelSlide's easeOut(0.18) instead.
    [Fact]
    [Trait("Req", "PR-CHAT-149")]
    public void REVIEW4_with_reduced_motion_a_block_that_closes_with_the_same_count_is_its_new_height_on_the_first_frame() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        var frames = new ManualFrameSource();
        using var frameSeam = FrameClock.Source.Override(frames);
        using var genie = ChromeTokens.ReduceMotionPlatform.Override(new FakeGeniePlatform { AccessibilityDisplayShouldReduceMotion = true });
        HydraDelegationBlock card = null!;
        AvaloniaWindow window = null!;
        Border Pill() => Mounted<Border>(card).First(border => border.Child is StackPanel { Spacing: 6 });
        var before = 0.0;
        h.Main.OnMain(() => { card = new HydraDelegationBlock(OneClosedStreaming); window = Show(card); before = Pill().Bounds.Height; card.Update(OneClosedShort, sent: false); });
        try
        {
            h.Main.OnMain(() => Settle(window));
            frames.Advance(TimeSpan.Zero);
            var first = 0.0;
            Run(h.Main, frames, window, 10, () => first = Pill().Bounds.Height);
            var after = 0.0;
            Run(h.Main, frames, window, 600, () => after = Pill().Bounds.Height);
            Assert.True(before > after + 10, $"the card went from {before:F1} to {after:F1} tall");
            Assert.Equal(after, first, 0.5);
        }
        finally { h.Main.OnMain(() => Close(window)); }
    });
}
