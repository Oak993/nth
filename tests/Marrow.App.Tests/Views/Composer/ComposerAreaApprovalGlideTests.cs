using Avalonia;
using Avalonia.Controls;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Services.Providers;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Xunit;
using AvaloniaWindow = Avalonia.Controls.Window;
using static Marrow.App.Tests.Views.Composer.ComposerViewTests;

namespace Marrow.App.Tests.Views.Composer;

// PR-COMPOSER-003, STOP App.Views.Composer.ComposerView #2: DroppyCode/App/Views/Composer/ComposerView.swift:23-25, :52-56 `.animation(Chrome.panelSlide, value:
// runtime.approvals.map(\.id))` on the whole area. The area is pinned to the bottom of the chat, so when the cards change the views that stay move in the window:
// a card above a new one rises along the spring (Chrome.panelSlide: spring 0.32 / 0.9; 0.18 s ease-out under Reduce Motion) and so does the area's top edge, the
// pill does not move, and a card that is resolved leaves the layout at once (the views below and above it take their places along the same curve) while it fades
// where it stood. Sampled frame by frame against the curve.
[Collection("Headless")]
public sealed class ComposerAreaApprovalGlideTests(HeadlessSession headless)
{
    private static double Top(Visual control, AvaloniaWindow window) => control.TranslatePoint(default, window)!.Value.Y;

    private static void Step(ThreadRuntimePart12Harness h, ManualFrameSource frames, AvaloniaWindow window, double seconds)
    {
        h.Main.Advance(TimeSpan.FromSeconds(seconds));
        h.Main.OnMain(() => { frames.Advance(TimeSpan.FromSeconds(seconds)); window.UpdateLayout(); });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Req", "PR-COMPOSER-003")] // ComposerView.swift:23-25, :52-56
    public void A_card_above_a_new_one_rises_along_the_panel_slide_and_the_pill_stays_where_it_is(bool reduceMotion) => headless.OnUiThread(() =>
    {
        using var h = RuntimeHarness();
        var frames = new ManualFrameSource();
        using var frameScope = FrameClock.Source.Override(frames);
        using var motion = Chrome.ReduceMotionPlatform.Override(new FakeGeniePlatform { AccessibilityDisplayShouldReduceMotion = reduceMotion });
        var curve = reduceMotion ? Animation.EaseOut(duration: 0.18) : Animation.Spring(response: 0.32, dampingFraction: 0.9);
        var runtime = Unresolved(h);
        var area = h.Main.OnMain(() => new ComposerArea(runtime, null, showsChanges: false));
        var window = h.Main.OnMain(() => Show(area, h.App, 500));
        h.Main.OnMain(() => runtime.Rehearse(new ProviderEvent.Approval(Approval("first"))));
        Pump(h, window); Step(h, frames, window, 3); Pump(h, window);
        var (first, pill) = h.Main.OnMain(() => ((Control)One<ApprovalCard>(area).Parent!, (Control)One<ComposerView>(area)));
        var (firstFrom, pillAt) = h.Main.OnMain(() => (Top(first, window), Top(pill, window)));

        h.Main.OnMain(() => runtime.Rehearse(new ProviderEvent.Approval(Approval("second")))); // under the first, over the pill
        Pump(h, window);
        h.Main.OnMain(() => frames.Advance(TimeSpan.Zero));
        // where the first card ends: its place in the new layout, read once the glide is over
        var samples = new List<(double T, double Y, double Pill)>();
        var elapsed = 0.0;
        foreach (var step in new[] { 0.02, 0.03, 0.03, 0.04, 0.08, 0.1 })
        {
            Step(h, frames, window, step); elapsed += step;
            samples.Add((elapsed, h.Main.OnMain(() => Top(first, window)), h.Main.OnMain(() => Top(pill, window))));
        }
        Step(h, frames, window, 3); Pump(h, window);
        var firstTo = h.Main.OnMain(() => Top(first, window));
        Assert.True(firstFrom - firstTo > 40, $"the first card rose only {firstFrom - firstTo:F1} points for a card under it");
        foreach (var (t, y, pillY) in samples)
        {
            var expected = firstFrom + (firstTo - firstFrom) * curve.Progress(t);
            if (t >= curve.Duration) expected = firstTo;
            Assert.True(Math.Abs(y - expected) <= 1.5, $"at {t * 1000:F0} ms the first card is at {y:F1}, the curve has it at {expected:F1} (from {firstFrom:F1} to {firstTo:F1})");
            Assert.Equal(pillAt, pillY, 1); // the pill, at the bottom of the pinned area, does not move
        }
        Assert.Contains(samples, sample => sample.Y < firstFrom - 1 && sample.Y > firstTo + 1); // part way: a glide, not a cut
        Close(h, window);
    });

    [Fact]
    [Trait("Req", "PR-COMPOSER-003")] // ComposerView.swift:23-25, :52-56
    public void A_resolved_card_leaves_the_layout_at_once_fading_where_it_stood_while_the_card_above_it_glides_down() => headless.OnUiThread(() =>
    {
        using var h = RuntimeHarness();
        var frames = new ManualFrameSource();
        using var frameScope = FrameClock.Source.Override(frames);
        using var motion = Chrome.ReduceMotionPlatform.Override(new FakeGeniePlatform { AccessibilityDisplayShouldReduceMotion = false });
        var curve = Animation.Spring(response: 0.32, dampingFraction: 0.9);
        var runtime = Unresolved(h);
        var area = h.Main.OnMain(() => new ComposerArea(runtime, null, showsChanges: false));
        var window = h.Main.OnMain(() => Show(area, h.App, 500));
        h.Main.OnMain(() => { runtime.Rehearse(new ProviderEvent.Approval(Approval("top"))); runtime.Rehearse(new ProviderEvent.Approval(Approval("bottom"))); });
        Pump(h, window); Step(h, frames, window, 3); Pump(h, window);
        var cards = h.Main.OnMain(() => LogicalQuery.SelfAndDescendants<ApprovalCard>(area).Select(card => (Control)card.Parent!).ToArray());
        var (top, bottom) = (cards[0], cards[1]);
        var (topFrom, bottomAt, stackBefore) = h.Main.OnMain(() => (Top(top, window), Top(bottom, window), Stack(area).Bounds.Height));

        h.Main.OnMain(() => runtime.Rehearse(new ProviderEvent.RequestResolved("bottom")));
        Pump(h, window);
        h.Main.OnMain(() => frames.Advance(TimeSpan.Zero));
        // out of the layout at once: the stack is its final height in the change's own frame, and the card is no longer one of its views
        var (stackNow, inStack) = h.Main.OnMain(() => (Stack(area).Bounds.Height, Stack(area).Children.Contains(SwiftTransition.Container(bottom))));
        Assert.False(inStack, "the resolved card still holds its place in the stack");
        Assert.True(stackBefore - stackNow > 40, $"the stack is {stackNow:F1} high, {stackBefore:F1} before: the card's place lingers");
        var samples = new List<(double T, double Top, double Bottom, double Fade)>();
        var elapsed = 0.0;
        foreach (var step in new[] { 0.02, 0.03, 0.03, 0.04, 0.08 })
        {
            Step(h, frames, window, step); elapsed += step;
            samples.Add((elapsed, h.Main.OnMain(() => Top(top, window)), h.Main.OnMain(() => TopLevel.GetTopLevel(bottom) is null ? double.NaN : Top(bottom, window)), h.Main.OnMain(() => SwiftTransition.Container(bottom).Opacity)));
        }
        Step(h, frames, window, 3); Pump(h, window);
        var topTo = h.Main.OnMain(() => Top(top, window));
        Assert.True(topTo - topFrom > 40, $"the top card came down only {topTo - topFrom:F1} points");
        foreach (var (t, y, bottomY, fade) in samples)
        {
            var expected = topFrom + (topTo - topFrom) * curve.Progress(t);
            Assert.True(Math.Abs(y - expected) <= 1.5, $"at {t * 1000:F0} ms the top card is at {y:F1}, the curve has it at {expected:F1}");
            Assert.True(double.IsNaN(bottomY) || Math.Abs(bottomY - bottomAt) <= 0.5, $"at {t * 1000:F0} ms the leaving card moved to {bottomY:F1} from {bottomAt:F1}");
            Assert.Equal(1 - curve.Progress(t), fade, 2); // it fades where it stood, along the same curve
        }
        h.Main.OnMain(() => Assert.Single(LogicalQuery.SelfAndDescendants<ApprovalCard>(area)));
        Close(h, window);
    });
}
