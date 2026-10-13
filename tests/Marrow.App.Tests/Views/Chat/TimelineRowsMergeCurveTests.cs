using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;
using AvaloniaWindow = Avalonia.Controls.Window;
using ChromeTokens = Marrow.UI.Chrome.Chrome;
using static Marrow.App.Tests.Fixtures.TimelineRowsFinChecks;
using static Marrow.App.Tests.Views.Chat.TimelineRowsPrSupport;

namespace Marrow.App.Tests.Views.Chat;

// Source: DroppyCode/App/Views/Chat/TimelineRows.swift (B3 52bc792), :935 `private static let change = Animation.smooth(duration: 0.3)`, :954-958 and :972-1010 the pill's
// slot, :1056-1079 the stage words. OD-0098: where no reference frame shows a state the Swift is the proof. The curve of the merging pill's two changes (a new stage, the
// outcome arriving) is read frame by frame against the closed form of `.smooth(duration: 0.3)`, SwiftUI's `.spring(duration: 0.3, bounce: 0)`: a critically damped spring of
// response 0.3, written out here so a duration or a curve of the port is compared with the source's own numbers, never with the port's own Animation type.
[Collection("Headless")]
public sealed class TimelineRowsMergeCurveTests(HeadlessSession headless)
{
    // 1 - e^(-w t) (1 + w t) with w = 2 pi / response: the unit-step response of a critically damped oscillator of that response, over the
    // first half of its run; over the second half the run-out it still had at its settling time is folded in so it lands with no last step
    // (ED-NEW-perf-motion-10: ED-333's UNRESOLVED run-out, the lead's ruling 2026-10-13), the port's curve there, which SpringLandingTests holds
    // to the free one's first half and to a landing with no speed.
    private static double Smooth(double response, double seconds)
    {
        if (seconds <= 0) return 0;
        var omega = 2 * Math.PI / response;
        var curve = Animation.Smooth(response);
        if (seconds > curve.Duration / 2) return curve.Progress(seconds);
        return 1 - Math.Exp(-omega * seconds) * (1 + omega * seconds);
    }

    private static void Run(TestRuntime main, ManualFrameSource frames, double milliseconds, Action<double> each, double step = 10)
    {
        for (var elapsed = step; elapsed <= milliseconds + 0.001; elapsed += step)
        {
            main.AdvanceMilliseconds(step);
            frames.Advance(TimeSpan.FromMilliseconds(step));
            each(elapsed);
        }
    }

    private static Border PillOf(Control row) => Mounted<Border>(row).First(border => border.Padding == new Thickness(12, 8, 14, 8));

    // The most visible copy of a text in the row (the still copy that sizes the slot is invisible, a fading word has one drawn copy): its opacity as drawn, its place.
    private static (double Opacity, double Y) Drawn(Control row, AvaloniaWindow window, string text) =>
        Mounted<TextBlock>(row).Where(block => block.Text == text)
            .Select(block => (Opacity: block.GetSelfAndVisualAncestors().Aggregate(1.0, (product, visual) => product * visual.Opacity), Y: block.TranslatePoint(default, window)!.Value.Y))
            .OrderByDescending(copy => copy.Opacity).FirstOrDefault();

    private static IEnumerable<Border> Bands(Control row) => Mounted<Border>(row).Where(border => border.Background is LinearGradientBrush);

    [Fact]
    [Trait("Req", "PR-HYDRAMERGE-101")] // TimelineRows.swift:935, :1056-1079: a new stage changes under smooth(0.3): the old words rise 5 and fade out as the new rise from 5 below and fade in, each with its own shimmer, the shimmer's frames not caught in the change
    public void A_new_stage_changes_along_the_smooth_0_3_second_curve_each_words_with_its_own_shimmer_that_keeps_its_own_thirty_frames_a_second() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        var frames = new ManualFrameSource();
        using var frameSeam = FrameClock.Source.Override(frames);
        using var genie = ChromeTokens.ReduceMotionPlatform.Override(new FakeGeniePlatform());
        HydraMergeRow row = null!;
        AvaloniaWindow window = null!;
        const string oldWords = "Merging…", newWords = "Bringing the checkout up to date…";
        h.Main.OnMain(() =>
        {
            h.Runtime.IsHydraMerging = true;
            h.Runtime.HydraMergeStep = HydraMergeStage.Merging;
            h.Runtime.HydraMergeStage = HydraMergeStage.Merging.Words;
            row = new HydraMergeRow(h.Runtime, new HydraMergePhase.Merging());
            window = Show(new Border { Padding = new Thickness(24), Child = row });
        });
        try
        {
            frames.Advance(TimeSpan.Zero);
            Run(h.Main, frames, 800, _ => { });
            var restOld = 0.0;
            h.Main.OnMain(() =>
            {
                Settle(window);
                restOld = Drawn(row, window, oldWords).Y;
                Assert.Single(Bands(row)); // one shimmer, the visible words'
                h.Runtime.HydraMergeStep = HydraMergeStage.Syncing;
                h.Runtime.HydraMergeStage = HydraMergeStage.Syncing.Words;
            });
            h.Main.OnMain(() => Settle(window));
            frames.Advance(TimeSpan.Zero);
            var samples = new List<(double At, (double Opacity, double Y) Old, (double Opacity, double Y) New, int Bands)>();
            Run(h.Main, frames, 330, elapsed =>
            {
                h.Main.OnMain(() => Settle(window));
                samples.Add((elapsed, Drawn(row, window, oldWords), Drawn(row, window, newWords), Bands(row).Count()));
            });
            Run(h.Main, frames, 800, _ => { });
            var restNew = 0.0;
            h.Main.OnMain(() => { Settle(window); restNew = Drawn(row, window, newWords).Y; });
            foreach (var (at, old, @new, bands) in samples)
            {
                var p = Smooth(0.3, at / 1000);
                // :1071-1076 the old words move up by 5 as they fade out; the new ones come from 5 below as they fade in; both on the one curve.
                Assert.True(Math.Abs(@new.Opacity - p) <= 0.03, $"at {at} ms the new words are {@new.Opacity:F3} in, smooth(0.3) has {p:F3}");
                Assert.True(Math.Abs(@new.Y - (restNew + 5 * (1 - p))) <= 0.35, $"at {at} ms the new words are {@new.Y - restNew:F2} below their line, smooth(0.3) has {5 * (1 - p):F2}");
                if (p < 0.985)
                {
                    Assert.True(Math.Abs(old.Opacity - (1 - p)) <= 0.03, $"at {at} ms the old words are {old.Opacity:F3} in, smooth(0.3) has {1 - p:F3}");
                    Assert.True(Math.Abs(old.Y - (restOld - 5 * p)) <= 0.35, $"at {at} ms the old words are {restOld - old.Y:F2} above their line, smooth(0.3) has {5 * p:F2}");
                }
                // Each words carries its own shimmer: while both are on show there are two bands, then the old one has gone with its words.
                if (old.Opacity > 0.02 && @new.Opacity > 0.02) Assert.Equal(2, bands);
            }
            Assert.Contains(samples, sample => sample.Old.Opacity is > 0.05 and < 0.95 && sample.New.Opacity is > 0.05 and < 0.95);
            Assert.Equal(1, samples[^1].Bands);
            // The shimmer is not caught in the change's transaction: the new words' band moves on its own thirty-frames-a-second cadence (a redraw every 33 ms), never per frame
            // along the change's spring. Read at the display's 5 ms beat for two seconds after the change, as the standalone shimmer is (PR-CHAT-148).
            var last = double.NaN;
            var redraws = new List<double>();
            double Offset() => Assert.IsType<TranslateTransform>(Bands(row).Single().RenderTransform).X;
            h.Main.OnMain(() => last = Offset());
            Run(h.Main, frames, 1000, elapsed =>
            {
                var now = 0.0;
                h.Main.OnMain(() => now = Offset());
                if (now != last) { redraws.Add(elapsed); last = now; }
            }, step: 5);
            var gaps = redraws.Zip(redraws.Skip(1), (a, b) => b - a).ToList();
            Assert.InRange(redraws.Count, 28, 32);
            Assert.True(gaps.All(gap => gap is >= 30 and <= 35), $"the band's redraws are {string.Join(", ", gaps.Distinct().OrderBy(gap => gap))} ms apart");
        }
        finally { h.Main.OnMain(() => Close(window)); }
    });

    [Fact]
    [Trait("Req", "PR-HYDRAMERGE-107")] // TimelineRows.swift:935, :972-1010: the pill's whole change, merging to outcome, is smooth(0.3) scoped to the slot: the slot's width and the two contents' opacities
    public void The_pill_changes_from_the_merge_to_its_note_along_the_smooth_0_3_second_curve_the_width_and_both_contents_by_the_same_fraction() => headless.OnUiThread(() =>
    {
        using var noNetwork = new NoLinkNetwork("gitlab.com"); // R-TEST-10: the note holds a link
        using var h = Harness();
        var frames = new ManualFrameSource();
        using var frameSeam = FrameClock.Source.Override(frames);
        using var genie = ChromeTokens.ReduceMotionPlatform.Override(new FakeGeniePlatform());
        using var browser = Marrow.Services.Port.Workspace.Open.Override(_ => true);
        HydraMergeRow row = null!;
        AvaloniaWindow window = null!;
        const string stageWords = "Opening the merge request…", title = "Hydra merged the team's work";
        h.Main.OnMain(() =>
        {
            h.Runtime.IsHydraMerging = true;
            h.Runtime.HydraMergeStep = HydraMergeStage.Opening;
            h.Runtime.HydraMergeStage = HydraMergeStage.Opening.Words;
            row = new HydraMergeRow(h.Runtime, new HydraMergePhase.Merging());
            window = Show(new Border { Padding = new Thickness(24), Child = row });
        });
        try
        {
            frames.Advance(TimeSpan.Zero);
            Run(h.Main, frames, 800, _ => { });
            var before = 0.0;
            h.Main.OnMain(() =>
            {
                Settle(window);
                before = PillOf(row).DesiredSize.Width;
                h.Runtime.IsHydraMerging = false;
                h.Runtime.HydraMergeStep = null;
                row.Update(new HydraMergePhase.Outcome(new UserMessage { Text = "Hydra merged the team's work.\nhttps://gitlab.com/g/p/-/merge_requests/7\nThree heads landed." }));
            });
            h.Main.OnMain(() => Settle(window));
            frames.Advance(TimeSpan.Zero);
            var samples = new List<(double At, double Width, (double Opacity, double Y) Old, (double Opacity, double Y) New)>();
            Run(h.Main, frames, 330, elapsed =>
            {
                h.Main.OnMain(() => Settle(window));
                samples.Add((elapsed, PillOf(row).DesiredSize.Width, Drawn(row, window, stageWords), Drawn(row, window, title)));
            });
            Run(h.Main, frames, 800, _ => { });
            var after = 0.0;
            h.Main.OnMain(() => { Settle(window); after = PillOf(row).DesiredSize.Width; });
            Assert.True(Math.Abs(after - before) > 20, $"the pill went from {before:F1} to {after:F1}");
            foreach (var (at, width, old, @new) in samples)
            {
                var p = Smooth(0.3, at / 1000);
                // The slot's width, and so the capsule's, along the curve (the slot holds only the still copy's width, never held wide for the fade). Layout rounds each width up to the
                // pixel, so the sample, the start and the end are each off by up to one: 2 points of 64 here; a curve of 0.2 s or of 0.6 s is 13 and 21 points off at 100 ms.
                var expected = before + (after - before) * p;
                Assert.True(Math.Abs(width - expected) <= 2.0, $"at {at} ms the pill is {width:F1} wide, smooth(0.3) has {expected:F1}, from {before:F1} to {after:F1}");
                // The note comes in by an opacity and the words go out by one (and rise 5), on the same fraction.
                Assert.True(Math.Abs(@new.Opacity - p) <= 0.03, $"at {at} ms the note is {@new.Opacity:F3} in, smooth(0.3) has {p:F3}");
                if (p < 0.985) Assert.True(Math.Abs(old.Opacity - (1 - p)) <= 0.03, $"at {at} ms the stage words are {old.Opacity:F3} in, smooth(0.3) has {1 - p:F3}");
            }
        }
        finally { h.Main.OnMain(() => Close(window)); }
    });
}
