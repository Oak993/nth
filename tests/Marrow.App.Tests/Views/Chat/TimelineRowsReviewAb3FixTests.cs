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

// The fix of REVIEW3's delegation-card finding (unit blitz-timelinerows-ab, ED-285 (3)). Source: DroppyCode/App/Views/Chat/TimelineRows.swift (B3 52bc792)
// :652-704: the pill is the VStack's background and `.animation(Chrome.panelSlide, value: delegations.count)` animates its frame in both directions, height as
// well as width, while the VStack's own contents are laid out at their new size (SwiftUI animates the frame, not the text's layout). OD-0076: a card that changes
// size eases to it.
[Collection("Headless")]
public sealed class TimelineRowsReviewAb3FixTests(HeadlessSession headless)
{
    private static void Run(TestRuntime main, ManualFrameSource frames, AvaloniaWindow window, double milliseconds, Action? each = null)
    {
        for (var elapsed = 10.0; elapsed <= milliseconds + 0.001; elapsed += 10)
        {
            main.AdvanceMilliseconds(10);
            frames.Advance(TimeSpan.FromMilliseconds(10));
            main.OnMain(() => { Settle(window); each?.Invoke(); });
        }
    }

    private const string Hank = "{\"task\": \"Tidy the popover\", \"prompt\": \"Tidy it.\", \"name\": \"Hank\"}";
    private const string WalterLong = "{\"task\": \"Write the tests for every silent path of the merge coordinator, one by one\", \"prompt\": \"Write them.\", \"name\": \"Walter\"}";
    private const string TwoClosedLong = "[\n" + Hank + ",\n" + WalterLong + "\n]";
    private const string OneClosedShort = "[\n" + Hank + "\n]";
    private const string OneClosedStreaming = "[\n" + Hank + ",\n";

    private static Border Pill(HydraDelegationBlock card) => Mounted<Border>(card).First(border => border.Child is StackPanel { Spacing: 6 });

    private static TextBlock Words(HydraDelegationBlock card, string text) => Mounted<TextBlock>(card).Single(block => block.Text == text);

    // Reads the pill's drawn size before the change, 50 ms into the panel slide and once it has settled.
    private (Size Before, Size Mid, Size After) Ease(string from, string to, Action<HydraDelegationBlock>? atMid = null, List<Size>? sizes = null)
    {
        var result = (default(Size), default(Size), default(Size));
        var drawn = sizes;
        headless.OnUiThread(() =>
        {
            using var h = Harness();
            var frames = new ManualFrameSource();
            using var frameSeam = FrameClock.Source.Override(frames);
            using var genie = ChromeTokens.ReduceMotionPlatform.Override(new FakeGeniePlatform());
            HydraDelegationBlock card = null!;
            AvaloniaWindow window = null!;
            h.Main.OnMain(() =>
            {
                card = new HydraDelegationBlock(from);
                window = Show(card);
                result.Item1 = Pill(card).Bounds.Size;
                card.Update(to, sent: false);
            });
            try
            {
                h.Main.OnMain(() => Settle(window));
                frames.Advance(TimeSpan.Zero);
                Run(h.Main, frames, window, 50, () => drawn?.Add(Pill(card).Bounds.Size));
                h.Main.OnMain(() => { result.Item2 = Pill(card).Bounds.Size; atMid?.Invoke(card); });
                Run(h.Main, frames, window, 1000, () => drawn?.Add(Pill(card).Bounds.Size));
                h.Main.OnMain(() => result.Item3 = Pill(card).Bounds.Size);
            }
            finally { h.Main.OnMain(() => Close(window)); }
        });
        return result;
    }

    // :681-686 the "Writing the next brief…" line goes once the brief closes (no longer streaming); the VStack loses a row, so its background's height shrinks
    // along Chrome.panelSlide (:703-704) rather than cutting up while the slot is still tall.
    [Fact]
    [Trait("Req", "PR-CHAT-149")] // the card's changes animate with Chrome.panelSlide; OD-0076
    public void The_delegation_card_shortens_along_the_panel_slide_when_its_next_brief_line_goes()
    {
        var drawn = new List<Size>();
        var fading = -1.0;
        var (before, mid, after) = Ease(OneClosedStreaming, OneClosedShort, card =>
        {
            // its removal transition (:681-686, the default .opacity) still draws it part way, where it was
            var line = Words(card, "Writing the next brief…");
            fading = line.GetSelfAndVisualAncestors().Aggregate(1.0, (product, at) => product * at.Opacity);
        }, drawn);
        Assert.InRange(fading, 0.05, 0.95);
        Assert.True(before.Height > after.Height + 10, $"the card went from {before.Height:F1} to {after.Height:F1} tall");
        // never a cut: no 10 ms frame takes more than a third of the way (the line fading out must not hold the card tall and then let it drop)
        var steps = drawn.Prepend(before).Zip(drawn, (from, to) => from.Height - to.Height).ToList();
        Assert.True(steps.Max() <= (before.Height - after.Height) / 3, $"the card's height dropped {steps.Max():F1} of {before.Height - after.Height:F1} in one frame, {(steps.IndexOf(steps.Max()) + 1) * 10} ms in");
        // 50 ms into spring(0.32, 0.9) the curve is about a quarter of its way.
        Assert.True(mid.Height >= before.Height - 0.75 * (before.Height - after.Height),
            $"the card's height cut: {mid.Height:F1} tall 50 ms into the panel slide, from {before.Height:F1} to {after.Height:F1}");
        Assert.True(mid.Height < before.Height - 0.05 * (before.Height - after.Height), $"the card has not started to shorten 50 ms in: {mid.Height:F1}, from {before.Height:F1}");
    }

    // :677-692 a block that now reads one head instead of two loses a head row: the card's height follows the spring down as its width does.
    [Fact]
    [Trait("Req", "PR-CHAT-149")] // the card's changes animate with Chrome.panelSlide; OD-0076
    public void The_delegation_card_shortens_along_the_panel_slide_when_a_head_row_goes()
    {
        var (before, mid, after) = Ease(TwoClosedLong, OneClosedShort);
        Assert.True(before.Height > after.Height + 10, $"the card went from {before.Height:F1} to {after.Height:F1} tall");
        Assert.True(mid.Height >= before.Height - 0.75 * (before.Height - after.Height),
            $"the card's height cut: {mid.Height:F1} tall 50 ms into the panel slide, from {before.Height:F1} to {after.Height:F1}");
    }

    // The pill's edge moves along the curve while its words keep the layout they take at the card's new size (SwiftUI animates the background's frame; the
    // texts are laid out once, at the new size): growing, a long head arriving is not trimmed to the card's narrower in-between width.
    [Fact]
    [Trait("Req", "PR-CHAT-149")] // the card's changes animate with Chrome.panelSlide; OD-0076
    public void A_head_arriving_in_a_widening_card_keeps_its_whole_words_while_the_card_eases_out_to_them()
    {
        const string task = "Write the tests for every silent path of the merge coordinator, one by one";
        double arranged = 0, natural = 0;
        var (before, mid, after) = Ease(OneClosedShort, TwoClosedLong, card =>
        {
            var words = Words(card, task);
            arranged = words.Bounds.Width; natural = words.DesiredSize.Width;
        });
        Assert.True(after.Width > before.Width + 60, $"the card went from {before.Width:F1} to {after.Width:F1} wide");
        Assert.True(mid.Width < after.Width - 20, $"the card is not part way 50 ms into the panel slide: {mid.Width:F1}, from {before.Width:F1} to {after.Width:F1}");
        Assert.True(arranged >= natural - 0.5, $"the head's words were laid out at {arranged:F1} of their {natural:F1} while the card eased out (re-trimmed)");
    }

    // Shrinking, the words that stay are laid out at the card's new size, not stretched to the wider in-between width.
    [Fact]
    [Trait("Req", "PR-CHAT-149")] // the card's changes animate with Chrome.panelSlide; OD-0076
    public void The_words_that_stay_in_a_narrowing_card_keep_their_new_layout_while_the_card_eases_in_to_them()
    {
        double arranged = 0, natural = 0;
        var (before, mid, after) = Ease(TwoClosedLong, OneClosedShort, card =>
        {
            var words = Words(card, "Tidy the popover");
            arranged = words.Bounds.Width; natural = words.DesiredSize.Width;
        });
        Assert.True(mid.Width > after.Width + 20, $"the card is not part way 50 ms into the panel slide: {mid.Width:F1}, from {before.Width:F1} to {after.Width:F1}");
        Assert.Equal(natural, arranged, 0.5);
    }
}
