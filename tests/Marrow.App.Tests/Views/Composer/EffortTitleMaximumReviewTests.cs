using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Composer;

// REVIEW (final review of unit/effort-title-fidelity, round 5, 2026-10-03).
//
// SOURCE FACT (EffortSlider.swift:570-583, B3 52bc792): the title's HStack (the Text with .contentTransition(.numericText())
// and the chevron) carries `.animation(.smooth(duration: 0.3), value: isMaxEffort)` and `.animation(.smooth(duration: 0.3),
// value: isFast)`. When maximum is reached or left, SwiftUI animates every change under that HStack (the morph, the label's
// width, the chevron's place) on smooth(0.3), whatever the transaction around it: the slider's withAnimation(.snappy(duration:
// 0.2)) (:631) as well as no animation at all (onReset, :219-224, writes the effort and fastMode = false with none).
// SOURCE FACT, the reference runtime (2-chat-use.mov; study effort-title-2026-10-03/video_inc_r3 and video_dec_r3, the
// chevron's centre x frame by frame; work/claude-verify/resume-2026-10-03/effort-rev-r5-chevron/chevron_fit.py and
// chevron_fit.out, start free on a 1 ms grid): High -> Extra High at 34.86 s (maximum unchanged) follows snappy(0.2), rms
// 0.008 (smooth(0.3) 0.106); Extra High -> Max at 35.05 s follows smooth(0.3), rms 0.013 (snappy(0.2) 0.097); Max -> Extra
// High at 35.62 s follows smooth(0.3), rms 0.007 (snappy(0.2) 0.099). The port's card morphs every change on the transaction
// it comes in (EffortSlider.cs:573 `title.SetText(nextTitle, ... Transaction.Current)`): snappy(0.2) inside the slider's
// animation, and no morph at all outside one.
[Collection("Headless")]
public sealed class EffortTitleMaximumReviewTests(HeadlessSession headless)
{
    // The card as the effort popover shows it (a title-tap handler, so the chevron is there), its efforts with a maximum.
    private static (EffortSliderCard Card, ObservableStringBox Effort, Avalonia.Controls.Window Window, MorphingTextBlock Title, Control Chevron) Card(string effortValue)
    {
        var effort = new ObservableStringBox { Value = effortValue };
        var card = new EffortSliderCard("Sonnet", ProviderKind.Claude, new ValueList<string>(["low", "medium", "high", "xhigh", "max"]), "medium", false,
            effort.AsBinding(), Binding<bool>.Constant(false), () => { }, onTitleTap: () => { });
        var window = new Avalonia.Controls.Window { Width = 330, Height = 140, Content = card };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var title = Assert.Single(card.GetVisualDescendants().OfType<MorphingTextBlock>());
        // OD-0139: the chevron is on the model line now and no longer rides the title; what moves with the title's frame, half its width's way,
        // is the title's right edge, which is what these tests track in the chevron's place (the card has its chevron, on the line, and the title has none).
        Assert.Single(card.GetVisualDescendants().OfType<SwiftImage>(), image => image.SymbolName == "chevron.right");
        Assert.DoesNotContain(title.GetVisualAncestors(), ancestor => ancestor is Button);
        return (card, effort, window, title, title);
    }

    // The chevron's x every millisecond from the change (index 0) to <paramref name="until"/> ms, as the label's frame moves it.
    private static List<double> ChevronTrack(Avalonia.Controls.Window window, Control chevron, ManualTimeProvider clock, ManualFrameSource frames, int until)
    {
        var xs = new List<double>();
        for (var ms = 0; ms <= until; ms++)
        {
            if (ms > 0)
            {
                clock.Advance(TimeSpan.FromMilliseconds(1));
                frames.Advance(TimeSpan.FromMilliseconds(1));
            }
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            xs.Add(chevron.TranslatePoint(new Point(chevron.Bounds.Width, 0), window)!.Value.X); // the title's right edge, where the chevron rode before OD-0139
        }
        return xs;
    }

    // The best fit, over a start shifted -15 to +15 ms, of the port's chevron progress to the video's samples (ms after the
    // video's change began, share of the chevron's way): its rms, and the port's progress at the samples at that start.
    private static (double Rms, int Shift, string Seen) Fit(List<double> xs, (int Ms, double Video)[] video)
    {
        var from = xs[0];
        var to = xs[^1];
        double Progress(int ms) => ms < 0 ? 0 : ms >= xs.Count ? 1 : (xs[ms] - from) / (to - from);
        var best = (Rms: double.MaxValue, Shift: 0);
        for (var shift = -15; shift <= 15; shift++)
        {
            var rms = Math.Sqrt(video.Average(sample => Math.Pow(Progress(sample.Ms + shift) - sample.Video, 2)));
            if (rms < best.Rms) best = (rms, shift);
        }
        var seen = string.Join(" ", video.Select(sample => $"{sample.Ms}ms video {sample.Video:F2} port {Progress(sample.Ms + best.Shift):F2}"));
        return (best.Rms, best.Shift, seen);
    }

    [Fact]
    [Trait("Decision", "ED-238")] // PR-COMPOSER-111 / UI-COMPOSER-031: the title's change into maximum on :582's smooth(0.3)
    public void REVIEW_A_change_into_maximum_moves_the_title_and_chevron_along_smooth_0_3_as_the_video_shows() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        using var source = FrameClock.Source.Override(frames);
        var clock = new ManualTimeProvider();
        using var time = SwiftClock.Provider.Override(clock);
        var (_, effort, window, title, chevron) = Card("xhigh");
        try
        {
            Transaction.WithAnimation(Animation.Snappy(duration: 0.2), () => effort.Value = "max"); // the slider's write, :631
            var xs = ChevronTrack(window, chevron, clock, frames, 500);
            Assert.Equal("Max", title.Text);
            // Video, Extra High -> Max (study video_inc_r3, smooth(0.3)'s best start 35.051 s): the chevron's share of its way.
            var (rms, shift, seen) = Fit(xs, [(6, 0.02), (27, 0.10), (49, 0.27), (71, 0.48), (92, 0.57), (114, 0.68), (136, 0.77), (156, 0.83), (177, 0.88), (199, 0.92), (221, 0.94), (242, 0.95), (264, 0.96)]);
            Assert.True(rms <= 0.04, $"the chevron does not move as the video's does into maximum (smooth(0.3), :582): rms {rms:F3} at the best start ({shift:+0;-0} ms): {seen}");
        }
        finally { window.Close(); }
    });

    [Fact]
    [Trait("Decision", "ED-238")] // PR-COMPOSER-111 / UI-COMPOSER-031: the title's change out of maximum on :582's smooth(0.3)
    public void REVIEW_A_change_out_of_maximum_moves_the_title_and_chevron_along_smooth_0_3_as_the_video_shows() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        using var source = FrameClock.Source.Override(frames);
        var clock = new ManualTimeProvider();
        using var time = SwiftClock.Provider.Override(clock);
        var (_, effort, window, title, chevron) = Card("max");
        try
        {
            Transaction.WithAnimation(Animation.Snappy(duration: 0.2), () => effort.Value = "xhigh"); // the slider's write, :631
            var xs = ChevronTrack(window, chevron, clock, frames, 500);
            Assert.Equal("Extra High", title.Text);
            // Video, Max -> Extra High (study video_dec_r3, smooth(0.3)'s best start 35.623 s; taken over at 35.753 s, so only
            // its first 120 ms): the chevron's share of its way to Extra High's place.
            var (rms, shift, seen) = Fit(xs, [(20, 0.06), (34, 0.15), (55, 0.33), (77, 0.48), (99, 0.60), (120, 0.72)]);
            Assert.True(rms <= 0.04, $"the chevron does not move as the video's does out of maximum (smooth(0.3), :582): rms {rms:F3} at the best start ({shift:+0;-0} ms): {seen}");
        }
        finally { window.Close(); }
    });

    [Fact]
    [Trait("Decision", "ED-238")] // PR-COMPOSER-111 / UI-COMPOSER-031: leaving maximum with no transaction (onReset, :219-224) still animates, :582
    public void REVIEW_Leaving_maximum_with_no_animation_around_it_still_morphs_the_title_on_smooth_0_3() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        using var source = FrameClock.Source.Override(frames);
        var clock = new ManualTimeProvider();
        using var time = SwiftClock.Provider.Override(clock);
        var (_, effort, window, title, chevron) = Card("max");
        try
        {
            effort.Value = "medium"; // as Reset writes it: no withAnimation (:219-224); isMaxEffort true -> false
            Dispatcher.UIThread.RunJobs();
            var morphing = title.IsMorphing;
            var xs = ChevronTrack(window, chevron, clock, frames, 500);
            Assert.Equal("Medium", title.Text);
            var from = xs[0];
            var to = xs[^1];
            var at90 = Math.Abs(to - from) < 0.5 ? 1 : (xs[90] - from) / (to - from);
            Assert.True(morphing, $"leaving maximum with no transaction, the title changes at once; :582 animates it on smooth(0.3) (chevron {from:F1} -> {to:F1}, {at90:F2} of its way at 90 ms)");
            Assert.InRange(at90, 0.42, 0.66); // smooth(0.3) at 90 ms: 0.56
        }
        finally { window.Close(); }
    });

    // OD-0076 (owner ruling 2026-10-02, "every change is transitioned, never cut"; named by this review's brief): where the Mac
    // changes state with no animation, the port still transitions ("content that swaps crossfades ... a label whose width
    // changes eases its container's width"). Reset from an effort that is neither maximum nor fast (:219-224, no withAnimation,
    // :582-583 not triggered) changes the Mac's title at once; the port's card cuts it too (EffortSlider.cs:571-573 "outside one
    // the text simply changes"), the text and the chevron jumping in one frame. No ED or OD records an exception for it.
    [Fact]
    [Trait("Decision", "ED-238")] // OD-0076 at PR-COMPOSER-111 / UI-COMPOSER-031's title: an effort change with no animation around it (Reset)
    public void REVIEW_An_effort_change_with_no_animation_around_it_is_transitioned_not_cut() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        using var source = FrameClock.Source.Override(frames);
        var clock = new ManualTimeProvider();
        using var time = SwiftClock.Provider.Override(clock);
        var (_, effort, window, title, chevron) = Card("high");
        try
        {
            effort.Value = "medium"; // as Reset writes it from High: no withAnimation, maximum and fast unchanged
            Dispatcher.UIThread.RunJobs();
            var morphing = title.IsMorphing;
            var xs = ChevronTrack(window, chevron, clock, frames, 600);
            Assert.Equal("Medium", title.Text);
            var from = xs[0];
            var to = xs[^1];
            var firstFrame = Math.Abs(to - from) < 0.5 ? 1 : (xs[16] - from) / (to - from);
            Assert.True(morphing && firstFrame < 0.5, $"the title is cut from High to Medium (morphing {morphing}; the chevron {from:F1} -> {to:F1}, {firstFrame:F2} of its way one 60 Hz frame in), OD-0076");
        }
        finally { window.Close(); }
    });
}
