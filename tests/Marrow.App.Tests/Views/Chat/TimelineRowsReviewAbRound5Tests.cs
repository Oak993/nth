using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Marrow.App.Port;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;
using Xunit;
using ChromeTokens = Marrow.UI.Chrome.Chrome;
using AvaloniaWindow = Avalonia.Controls.Window;
using static Marrow.App.Tests.Views.Chat.TimelineRowsPrSupport;
using static Marrow.App.Tests.Fixtures.TimelineRowsFinChecks;

namespace Marrow.App.Tests.Views.Chat;

// The round-4 review of unit/integ-tlrab (ED-285, fifth-round closure). Source: DroppyCode/App/Views/Chat/TimelineRows.swift (B3 52bc792) :652-707.
// :703-704 animate the card only when `sent` or `delegations.count` changes; any other change of a streamed block is made in no animation on the Mac
// (ThreadRuntime.swift's withAnimation is only at :2947 and :2953), so the port eases it under OD-0076, and reduced motion stills that (ED-264 (3)).
// :706 `.accessibilityElement(children: .combine)`: the card is one element whose label is its children's, in order.
[Collection("Headless")]
public sealed class TimelineRowsReviewAbRound5Tests(HeadlessSession headless)
{
    private const string Hank = "{\"task\": \"Tidy the popover\", \"prompt\": \"Tidy it.\", \"name\": \"Hank\"}";
    private const string Elsewhere = "{\"task\": \"Fix the merge pill\", \"prompt\": \"Fix it.\", \"project\": \"/Users/me/code/website\"}";
    private const string OneClosedStreaming = "[\n" + Hank + ",\n";
    private const string OneClosedShort = "[\n" + Hank + "\n]";
    private const string Writing = "Writing the next brief…";

    private static void Run(TestRuntime main, ManualFrameSource frames, AvaloniaWindow window, double milliseconds, Action? each = null)
    {
        for (var elapsed = 10.0; elapsed <= milliseconds + 0.001; elapsed += 10)
        {
            main.AdvanceMilliseconds(10);
            frames.Advance(TimeSpan.FromMilliseconds(10));
            main.OnMain(() => { Settle(window); each?.Invoke(); });
        }
    }

    private static Border Pill(HydraDelegationBlock card) => Mounted<Border>(card).First(border => border.Child is StackPanel { Spacing: 6 });

    private static double Drawn(HydraDelegationBlock card, string text)
    {
        var line = Mounted<TextBlock>(card).Where(block => block.Text == text).ToList();
        return line.Count == 0 ? 0 : line.Max(block => block.GetSelfAndVisualAncestors().Aggregate(1.0, (product, at) => product * at.Opacity));
    }

    // A block that closes with the same count (one head, streaming then complete) changes neither value :703-704 keys, so the Mac draws the new card at once;
    // OD-0076 eases it (the card's height, the title's words crossfading, the next-brief line fading out), and not along panelSlide, which only a count or `sent`
    // change brings: ENGINEERING DECISION, the Mac's motion language for a size change, .easeOut(duration: 0.3) (ED-285, fifth round (2)).
    [Fact]
    [Trait("Req", "PR-CHAT-149")] // :703-704 the keyed animation; OD-0076 for what the Mac snaps
    public void A_block_that_closes_with_the_same_count_eases_on_the_od0076_curve_not_the_panel_slide() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        var frames = new ManualFrameSource();
        using var frameSeam = FrameClock.Source.Override(frames);
        using var genie = ChromeTokens.ReduceMotionPlatform.Override(new FakeGeniePlatform());
        HydraDelegationBlock card = null!;
        AvaloniaWindow window = null!;
        var before = 0.0;
        h.Main.OnMain(() => { card = new HydraDelegationBlock(OneClosedStreaming); window = Show(card); before = Pill(card).Bounds.Height; card.Update(OneClosedShort, sent: false); });
        try
        {
            h.Main.OnMain(() => Settle(window));
            frames.Advance(TimeSpan.Zero);
            var heights = new List<double>();
            var line = new List<double>();
            var title = new List<double>();
            Run(h.Main, frames, window, 600, () => { heights.Add(Pill(card).Bounds.Height); line.Add(Drawn(card, Writing)); title.Add(Drawn(card, "Summoning Hank")); });
            var after = heights[^1];
            Assert.True(before > after + 10, $"the card went from {before:F1} to {after:F1} tall");
            for (var i = 0; i < heights.Count; i++)
            {
                var at = (i + 1) * 10.0;
                var expected = before + (after - before) * EaseOut(at / 300);
                Assert.True(Math.Abs(heights[i] - expected) <= 0.06 * (before - after), $"at {at} ms the card is {heights[i]:F1} tall; .easeOut(duration: 0.3) has {expected:F1}");
            }
            Assert.InRange(line[4], 0.05, 0.95); // 50 ms in, the line is on its way out
            Assert.InRange(title[4], 0.05, 0.95); // and the new words on their way in
            Assert.Equal(0, line[^1], 0.001);
            Assert.Equal(1, title[^1], 0.001);
        }
        finally { h.Main.OnMain(() => Close(window)); }
    });

    // ED-264 (3): what OD-0076 adds where the Mac snaps is stilled by reduced motion. On the first frame the title is only its new words and the next-brief
    // line is gone (the card's height is REVIEW4's test).
    [Fact]
    [Trait("Req", "PR-CHAT-149")] // :703-704 the keyed animation; OD-0076 and ED-264 (3)
    public void With_reduced_motion_a_block_that_closes_with_the_same_count_swaps_its_title_and_drops_its_line_on_the_first_frame() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        var frames = new ManualFrameSource();
        using var frameSeam = FrameClock.Source.Override(frames);
        using var genie = ChromeTokens.ReduceMotionPlatform.Override(new FakeGeniePlatform { AccessibilityDisplayShouldReduceMotion = true });
        HydraDelegationBlock card = null!;
        AvaloniaWindow window = null!;
        h.Main.OnMain(() => { card = new HydraDelegationBlock(OneClosedStreaming); window = Show(card); card.Update(OneClosedShort, sent: false); });
        try
        {
            h.Main.OnMain(() => Settle(window));
            frames.Advance(TimeSpan.Zero);
            Run(h.Main, frames, window, 10, () =>
            {
                Assert.Equal(0, Drawn(card, Writing), 0.001);
                Assert.Equal(1, Drawn(card, "Summoning Hank"), 0.001);
                Assert.Equal(0, Drawn(card, "Summoning heads"), 0.001);
            });
        }
        finally { h.Main.OnMain(() => Close(window)); }
    });

    // A count change keeps panelSlide, the spring, with reduced motion off: the card is part way 50 ms in, along spring(response 0.32, damping 0.9).
    [Fact]
    [Trait("Req", "PR-CHAT-149")] // :704 `.animation(Chrome.panelSlide, value: delegations.count)`
    public void A_count_change_eases_the_card_along_the_panel_slide_spring() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        var frames = new ManualFrameSource();
        using var frameSeam = FrameClock.Source.Override(frames);
        using var genie = ChromeTokens.ReduceMotionPlatform.Override(new FakeGeniePlatform());
        HydraDelegationBlock card = null!;
        AvaloniaWindow window = null!;
        var before = 0.0;
        h.Main.OnMain(() => { card = new HydraDelegationBlock(OneClosedShort); window = Show(card); before = Pill(card).Bounds.Height; card.Update("[\n" + Hank + ",\n" + Elsewhere + "\n]", sent: false); });
        try
        {
            h.Main.OnMain(() => Settle(window));
            frames.Advance(TimeSpan.Zero);
            var heights = new List<double>();
            Run(h.Main, frames, window, 1000, () => heights.Add(Pill(card).Bounds.Height));
            var after = heights[^1];
            Assert.True(after > before + 10, $"the card went from {before:F1} to {after:F1} tall");
            foreach (var at in new[] { 50, 100, 200 })
            {
                var expected = before + (after - before) * Spring(0.32, 0.9, at / 1000.0);
                Assert.True(Math.Abs(heights[at / 10 - 1] - expected) <= 0.06 * (after - before), $"at {at} ms the card is {heights[at / 10 - 1]:F1} tall; panelSlide has {expected:F1}");
            }
        }
        finally { h.Main.OnMain(() => Close(window)); }
    });

    // :706 `.accessibilityElement(children: .combine)`: one element, its children hidden, its label the children's in order, joined as the port's other
    // combined labels are (UsageViews.swift:237 "Banked resets, 2 available"): the title, "sent" (:672), then each head's glyph (its persona's name, :716),
    // task and "in <project>" (:739), then the next-brief line (:681-686). The marks and the arrow are hidden (:664, :719, :731).
    [Fact]
    [Trait("Rule", "R-TEST-4")] // UI-CHAT-054's combined element (UI-CHAT-054 stays open on its corner, ED-275 (2) / OD-0102)
    public void The_card_is_one_accessibility_element_labelled_by_its_children_in_order() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frameSeam = FrameClock.Source.Override(new ManualFrameSource());
        HydraDelegationBlock card = null!;
        AvaloniaWindow window = null!;
        h.Main.OnMain(() => { card = new HydraDelegationBlock("[\n" + Hank + ",\n" + Elsewhere + ",\n"); window = Show(card); });
        try
        {
            h.Main.OnMain(() =>
            {
                var peer = ControlAutomationPeer.CreatePeerForElement(card);
                Assert.Empty(peer.GetChildren());
                Assert.Equal("Summoning heads, Hank, Tidy the popover, Fix the merge pill, in website, " + Writing, peer.GetName());
                card.Update("[\n" + Hank + ",\n" + Elsewhere + "\n]", sent: true);
                Settle(window);
                Assert.Equal("Summoned 2 heads, sent, Hank, Tidy the popover, Fix the merge pill, in website", ControlAutomationPeer.CreatePeerForElement(card).GetName());
            });
        }
        finally { h.Main.OnMain(() => Close(window)); }
    });

    private static double Spring(double response, double damping, double seconds)
    {
        if (seconds <= 0) return 0;
        var omega = 2 * Math.PI / response;
        var damped = omega * Math.Sqrt(1 - damping * damping);
        return 1 - Math.Exp(-damping * omega * seconds) * (Math.Cos(damped * seconds) + damping * omega / damped * Math.Sin(damped * seconds));
    }

    // SwiftUI's `.easeOut(duration:)`: the cubic Bezier (0, 0) (0.58, 1) at a fraction of its duration.
    private static double EaseOut(double fraction)
    {
        if (fraction <= 0) return 0;
        if (fraction >= 1) return 1;
        double low = 0, high = 1;
        for (var i = 0; i < 60; i++)
        {
            var mid = (low + high) / 2;
            if (3 * mid * mid * (1 - mid) * 0.58 + mid * mid * mid < fraction) low = mid; else high = mid;
        }
        var s = (low + high) / 2;
        return 3 * s * s * (1 - s) + s * s * s;
    }
}
