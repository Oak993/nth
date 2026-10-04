using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;
using Marrow.App.Port;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;
using Xunit;
using ChromeTokens = Marrow.UI.Chrome.Chrome;
using AvaloniaWindow = Avalonia.Controls.Window;
using static Marrow.App.Tests.Views.Chat.TimelineRowsPrSupport;
using static Marrow.App.Tests.Fixtures.TimelineRowsFinChecks;

namespace Marrow.App.Tests.Views.Chat;

// The adversarial review of unit/integ-tlrab (the merge of blitz-timelinerows-ab, ED-285, fourth-round closure). Source: DroppyCode/App/Views/Chat/TimelineRows.swift
// (B3 52bc792) :652-707: a view that leaves the delegation card is drawn inside the card's VStack while its removal transition runs (so whatever its parent is still
// doing to it, a list still fading in, goes on applying), nothing clips the card, and a view whose own arrival is still running leaves from where it has got to.
// OD-0076: nothing on screen changes by a cut. Each test reads every 10 ms frame (the reviewer's sequences).
[Collection("Headless")]
public sealed class TimelineRowsReviewAbIntegTests(HeadlessSession headless)
{
    private const string Hank = "{\"task\": \"Tidy the popover\", \"prompt\": \"Tidy it.\", \"name\": \"Hank\"}";
    private const string Empty = "[\n";
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

    // The opacity a line of the card is drawn with: its own and every ancestor's, the card's included; 0 once it is gone.
    private static double Drawn(HydraDelegationBlock card, string text)
    {
        var line = Mounted<TextBlock>(card).Where(block => block.Text == text).ToList();
        return line.Count == 0 ? 0 : line[0].GetSelfAndVisualAncestors().Aggregate(1.0, (product, at) => product * at.Opacity);
    }

    private static void NeverJumpsUp(List<double> drawn, string what)
    {
        for (var i = 1; i < drawn.Count; i++)
            Assert.True(drawn[i] - drawn[i - 1] <= 0.1, $"{what} jumped from {drawn[i - 1]:F3} to {drawn[i]:F3} at sample {i} ({string.Join(", ", drawn.Select(value => value.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)))})");
    }

    // :676-692 the list arrives (its rows and the next-brief line in it, fading in with the list); a brief closes; 60 ms later the block closes, so the next-brief
    // line goes (:681-686) while the list is still fading in. The leaving line is drawn in the list, so it goes on taking the list's fade-in as its own fade-out runs:
    // it never jumps back up to full opacity.
    [Fact]
    [Trait("Req", "PR-CHAT-149")] // the card's changes animate with Chrome.panelSlide (:703-704); OD-0076
    public void The_next_brief_line_leaving_while_its_list_still_fades_in_keeps_the_lists_opacity_and_never_jumps_up() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        var frames = new ManualFrameSource();
        using var frameSeam = FrameClock.Source.Override(frames);
        using var genie = ChromeTokens.ReduceMotionPlatform.Override(new FakeGeniePlatform());
        HydraDelegationBlock card = null!;
        AvaloniaWindow window = null!;
        var drawn = new List<double>();
        h.Main.OnMain(() => { card = new HydraDelegationBlock(Empty); window = Show(card); card.Update(OneClosedStreaming, sent: false); });
        try
        {
            h.Main.OnMain(() => Settle(window));
            frames.Advance(TimeSpan.Zero);
            Run(h.Main, frames, window, 60, () => drawn.Add(Drawn(card, Writing)));
            var before = drawn[^1];
            h.Main.OnMain(() => { card.Update(OneClosedShort, sent: false); Settle(window); drawn.Add(Drawn(card, Writing)); });
            frames.Advance(TimeSpan.Zero);
            Run(h.Main, frames, window, 600, () => drawn.Add(Drawn(card, Writing)));
            Assert.InRange(before, 0.05, 0.8); // the list is still part way in when the block closes
            NeverJumpsUp(drawn, "the next-brief line");
            Assert.Equal(0, drawn[^1], 0.001);
        }
        finally { h.Main.OnMain(() => Close(window)); }
    });

    // :676 the whole list goes when a block reads no head any more, here while the list is still fading in: it leaves from where its arrival has got to.
    [Fact]
    [Trait("Req", "PR-CHAT-149")] // the card's changes animate with Chrome.panelSlide (:703-704); OD-0076
    public void The_heads_list_emptied_while_it_still_fades_in_leaves_from_where_it_got_to() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        var frames = new ManualFrameSource();
        using var frameSeam = FrameClock.Source.Override(frames);
        using var genie = ChromeTokens.ReduceMotionPlatform.Override(new FakeGeniePlatform());
        HydraDelegationBlock card = null!;
        AvaloniaWindow window = null!;
        var drawn = new List<double>();
        h.Main.OnMain(() => { card = new HydraDelegationBlock(Empty); window = Show(card); card.Update(OneClosedStreaming, sent: false); });
        try
        {
            h.Main.OnMain(() => Settle(window));
            frames.Advance(TimeSpan.Zero);
            Run(h.Main, frames, window, 60, () => drawn.Add(Drawn(card, "Tidy the popover")));
            var before = drawn[^1];
            h.Main.OnMain(() => { card.Update(Empty, sent: false); Settle(window); drawn.Add(Drawn(card, "Tidy the popover")); });
            frames.Advance(TimeSpan.Zero);
            Run(h.Main, frames, window, 600, () => drawn.Add(Drawn(card, "Tidy the popover")));
            Assert.InRange(before, 0.05, 0.8);
            NeverJumpsUp(drawn, "the head row of the emptied list");
            Assert.Equal(0, drawn[^1], 0.001);
        }
        finally { h.Main.OnMain(() => Close(window)); }
    });

    // :681-686 the next-brief line fades out where it was while the card eases to its shorter height (:703-704): nothing in the card clips it, so no part of it is cut
    // away by the card's edge moving up over it while it is still drawn.
    [Fact]
    [Trait("Req", "PR-CHAT-149")] // the card's changes animate with Chrome.panelSlide (:703-704); OD-0076
    public void The_leaving_next_brief_line_fades_where_it_was_without_the_cards_easing_edge_clipping_it() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        var frames = new ManualFrameSource();
        using var frameSeam = FrameClock.Source.Override(frames);
        using var genie = ChromeTokens.ReduceMotionPlatform.Override(new FakeGeniePlatform());
        HydraDelegationBlock card = null!;
        AvaloniaWindow window = null!;
        Rect place = default;
        var checkedFrames = 0;
        void Check()
        {
            var line = Mounted<TextBlock>(card).Where(block => block.Text == Writing).ToList();
            if (line.Count == 0 || Drawn(card, Writing) < 0.02) return;
            var at = line[0].TranslatePoint(default, card)!.Value;
            Assert.Equal(place, new Rect(at, line[0].Bounds.Size)); // where it was
            foreach (var ancestor in line[0].GetVisualAncestors())
            {
                if (ReferenceEquals(ancestor, window)) break;
                if (!ancestor.ClipToBounds && ancestor.Clip is null) continue;
                var corner = line[0].TranslatePoint(default, ancestor)!.Value;
                var box = new Rect(corner, line[0].Bounds.Size);
                var inside = ancestor.Clip?.Bounds ?? new Rect(ancestor.Bounds.Size);
                Assert.True(inside.Contains(box), $"{ancestor.GetType().Name} clips the leaving line ({box} outside {inside}) while it is drawn at {Drawn(card, Writing):F2}");
            }
            checkedFrames++;
        }
        h.Main.OnMain(() => { card = new HydraDelegationBlock(OneClosedStreaming); window = Show(card); });
        try
        {
            h.Main.OnMain(() =>
            {
                Settle(window);
                var line = Mounted<TextBlock>(card).Single(block => block.Text == Writing);
                place = new Rect(line.TranslatePoint(default, card)!.Value, line.Bounds.Size);
                card.Update(OneClosedShort, sent: false);
            });
            h.Main.OnMain(() => Settle(window));
            frames.Advance(TimeSpan.Zero);
            Run(h.Main, frames, window, 400, Check);
            Assert.True(checkedFrames >= 5, $"the leaving line was drawn on {checkedFrames} frames only");
        }
        finally { h.Main.OnMain(() => Close(window)); }
    });
}
