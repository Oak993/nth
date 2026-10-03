using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Port;

// OD-0094 (owner ruling 2026-10-03, a deviation from EffortSlider.swift:543-555's static LinearGradient): wherever the
// effort title is a gradient, its colours drift slowly along the letters at rest, a seamless mirrored loop, continuous
// with the colours passing along the letters during a change (one phase), still under reduce motion, on the frame
// clock and only while the title is shown. A single-colour title stays as it is.
[Collection("Headless")]
public sealed class MorphingTextBlockDriftTests(HeadlessSession headless)
{
    private const double Period = 7;

    private static LinearGradientBrush RedToBlue() => new()
    {
        StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
        EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
        GradientStops = { new GradientStop(Colors.Red, 0), new GradientStop(Colors.Blue, 1) },
    };

    private static void Run(Action<MorphingTextBlock, Window, Action<int>> body, string text = "HHHH", IBrush? brush = null, Action<MorphingTextBlock>? setup = null)
    {
        var frames = new ManualFrameSource();
        using var source = FrameClock.Source.Override(frames);
        var clock = new ManualTimeProvider();
        using var time = SwiftClock.Provider.Override(clock);
        var label = new MorphingTextBlock { Text = text, FontSize = 30, Foreground = brush ?? RedToBlue(), DriftPeriod = Period, Margin = new Thickness(10) };
        setup?.Invoke(label);
        var window = new Window { Width = 400, Height = 80, Background = Brushes.Black, Content = new Border { Child = label, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top } };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        void Step(int milliseconds)
        {
            for (var ms = 0; ms < milliseconds; ms += 20)
            {
                var step = Math.Min(20, milliseconds - ms);
                clock.Advance(TimeSpan.FromMilliseconds(step));
                frames.Advance(TimeSpan.FromMilliseconds(step));
                Dispatcher.UIThread.RunJobs();
            }
            window.UpdateLayout();
        }
        try { body(label, window, Step); }
        finally { window.Close(); }
    }

    private static int MaxDifference(RenderedPixels a, RenderedPixels b)
    {
        var worst = 0;
        for (var y = 0; y < a.Height; y++)
            for (var x = 0; x < a.Width; x++)
            {
                var (r1, g1, b1) = a.At(x, y);
                var (r2, g2, b2) = b.At(x, y);
                worst = Math.Max(worst, Math.Max(Math.Abs(r1 - r2), Math.Max(Math.Abs(g1 - g2), Math.Abs(b1 - b2))));
            }
        return worst;
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-111")] // the title (EffortSlider.swift:573-583) as ruled by OD-0094
    public void At_rest_a_gradient_titles_colours_drift_slowly_along_the_letters() => headless.OnUiThread(() => Run((label, window, step) =>
    {
        Assert.Equal(0, label.DriftPhase);
        var start = RenderedPixels.Capture(window);
        step(1000);
        Assert.InRange(label.DriftPhase, 1 / Period - 0.01, 1 / Period + 0.01); // a period of seven seconds
        Assert.True(MaxDifference(start, RenderedPixels.Capture(window)) > 20, "the colours have moved");
    }));

    [Fact]
    [Trait("Req", "PR-COMPOSER-111")] // the title (EffortSlider.swift:573-583) as ruled by OD-0094
    public void The_drift_loops_without_a_seam() => headless.OnUiThread(() => Run((label, window, step) =>
    {
        step(1500);
        var before = RenderedPixels.Capture(window);
        step((int)(Period * 1000) / 2);
        Assert.True(MaxDifference(before, RenderedPixels.Capture(window)) > 20, "half a loop on, the colours differ");
        step((int)(Period * 1000) - (int)(Period * 1000) / 2);
        Assert.True(MaxDifference(before, RenderedPixels.Capture(window)) <= 1, "a whole loop on, the same frame");
        // Across the loop's end the colours go on moving by a frame's worth, never jumping back to the start.
        step((int)(Period * 1000) - 1500 - 20);
        Assert.True(label.DriftPhase > 0.99, $"phase {label.DriftPhase:F4}");
        var ending = RenderedPixels.Capture(window);
        step(60);
        Assert.True(label.DriftPhase < 0.01, $"phase {label.DriftPhase:F4}");
        Assert.True(MaxDifference(ending, RenderedPixels.Capture(window)) <= 8, "no seam where the loop starts again");
    }));

    [Fact]
    [Trait("Req", "PR-COMPOSER-111")] // the title (EffortSlider.swift:573-583) as ruled by OD-0094
    public void Under_reduce_motion_the_gradient_holds_still() => headless.OnUiThread(() => Run((label, window, step) =>
    {
        EnvironmentValues.SetAccessibilityReduceMotion(window, true);
        Dispatcher.UIThread.RunJobs();
        var start = RenderedPixels.Capture(window);
        step(2000);
        Assert.Equal(0, label.DriftPhase);
        Assert.True(MaxDifference(start, RenderedPixels.Capture(window)) <= 1);
    }));

    [Fact]
    [Trait("Req", "PR-COMPOSER-111")] // the title (EffortSlider.swift:573-583) as ruled by OD-0094
    public void A_change_takes_the_drift_over_and_hands_it_back_without_a_jump() => headless.OnUiThread(() => Run((label, window, step) =>
    {
        step(1500); // well into the loop, so a hand-off that dropped the drift would show
        var rest = RenderedPixels.Capture(window);
        var phase = label.DriftPhase;
        label.SetText("2025", Animation.Snappy(0.2)); // same width: only the last figure rolls; "202" is kept in place
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        var taken = RenderedPixels.Capture(window);
        Assert.Equal(phase, label.DriftPhase, 6); // one phase, not restarted by the change
        Assert.True(MaxDifference(Left(rest), Left(taken)) <= 2, "the kept figures keep their colours as the change takes over");
        var lastMorphing = taken;
        var last = label.DriftPhase;
        while (label.IsMorphing)
        {
            lastMorphing = RenderedPixels.Capture(window);
            last = label.DriftPhase;
            step(20);
        }
        Assert.InRange(label.DriftPhase - last, 0.001, 0.01); // the drift goes on from where the change left it
        Assert.True(MaxDifference(lastMorphing, RenderedPixels.Capture(window)) <= 3, "no jump as the rest takes the drift back");
    }, text: "2024"));

    // The same hand-over where the line does not start at the label's edge (aligned right in a wider label): the letters
    // at rest are drawn as the line's glyph runs, each where the line places it, and their colours still come from the
    // box the change pins its letters' colours to, not from the run's own place.
    [Fact]
    [Trait("Req", "PR-COMPOSER-111")] // the title (EffortSlider.swift:573-583) as ruled by OD-0094
    public void A_line_set_in_from_the_labels_edge_keeps_its_colours_as_a_change_takes_the_drift_over() => headless.OnUiThread(() => Run((label, window, step) =>
    {
        step(1500);
        var rest = RenderedPixels.Capture(window);
        label.SetText("2025", Animation.Snappy(0.2));
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        var taken = RenderedPixels.Capture(window);
        var kept = new Func<RenderedPixels, RenderedPixels>(pixels => pixels.Crop((int)(pixels.Width * 0.3), 0, (int)(pixels.Width * 0.22), pixels.Height));
        static int Ink(RenderedPixels pixels)
        {
            var most = 0;
            for (var y = 0; y < pixels.Height; y++)
                for (var x = 0; x < pixels.Width; x++) most = Math.Max(most, pixels.Level(x, y));
            return most;
        }
        Assert.True(Ink(kept(rest)) > 120, $"the kept figures are drawn at rest ({Ink(kept(rest))})");
        Assert.True(MaxDifference(kept(rest), kept(taken)) <= 2, $"the kept figures keep their colours as the change takes over ({MaxDifference(kept(rest), kept(taken))} levels; ink {Ink(kept(rest))} at rest, {Ink(kept(taken))} taken)");
    }, text: "2024", setup: label => { label.Width = 220; label.TextAlignment = TextAlignment.Right; }));

    // The window's left 15 %: the kept "202" of the label at its left.
    private static RenderedPixels Left(RenderedPixels pixels) => pixels.Crop(0, 0, (int)(pixels.Width * 0.15), pixels.Height);

    [Fact]
    [Trait("Req", "PR-COMPOSER-111")] // the title (EffortSlider.swift:573-583) as ruled by OD-0094
    public void The_drift_asks_for_frames_only_while_the_title_is_shown() => headless.OnUiThread(() => Run((label, window, step) =>
    {
        step(100);
        Assert.True(label.DriftPhase > 0);
        Assert.True(((ManualFrameSource)FrameClock.Source.Current).Pending > 0);
        var host = (Border)label.Parent!;
        host.IsVisible = false;
        step(100);
        Assert.Equal(0, ((ManualFrameSource)FrameClock.Source.Current).Pending); // hidden: no frames at all
        var held = label.DriftPhase;
        step(500);
        Assert.Equal(held, label.DriftPhase);
        host.IsVisible = true;
        Dispatcher.UIThread.RunJobs();
        step(100);
        Assert.True(label.DriftPhase > held); // shown again: it goes on
        host.Child = null; // gone with its popover
        step(100);
        Assert.Equal(0, ((ManualFrameSource)FrameClock.Source.Current).Pending);
    }));

    [Fact]
    [Trait("Req", "PR-COMPOSER-111")] // the title (EffortSlider.swift:573-583) as ruled by OD-0094
    public void A_single_colour_title_stays_as_it_is() => headless.OnUiThread(() => Run((label, window, step) =>
    {
        var start = RenderedPixels.Capture(window);
        step(1000);
        Assert.Equal(0, label.DriftPhase);
        Assert.Equal(0, ((ManualFrameSource)FrameClock.Source.Current).Pending);
        Assert.True(MaxDifference(start, RenderedPixels.Capture(window)) <= 1);
    }, brush: Brushes.White));
}
