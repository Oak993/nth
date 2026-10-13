using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using Marrow.Testing;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Port;

// ED-NEW-perf-motion-10 and -11 on the machine's own frames (the lead's rulings 2 and 3 of 2026-10-13). The routine tests step the curves
// on exact 60 and 144 Hz frames; here the frames are the real ones: the precise wake timer the frame clock uses, armed once a frame, on
// the machine's clock, with its jitter and late frames, and the composition's own drawn frames. A real frame can come late, so a landing
// is judged by speed (each step over its own frame's time): the last frame's speed no greater than the one before. They wait on the
// machine's clock and draw real frames, so they run with the native tests (a quiet PC; a failure counts when it fails alone twice).
[Collection("Headless")]
public sealed class MotionLandingNativeTests(HeadlessSession headless)
{
    // A step under a millionth of the travel is the curve at rest (SpringLandingTests.AtRest).
    private const double AtRest = 1e-6;

    // The times of the machine's real frames at hertz for seconds, from 0: the precise wake timer armed for each next frame boundary.
    private static List<double> RealFrames(double hertz, double seconds)
    {
        var times = new List<double> { 0 };
        using var signal = new SemaphoreSlim(0);
        var clock = Stopwatch.StartNew();
        var timer = WakeTimer.Create(() => signal.Release(), job => job(), precise: true);
        Assert.NotNull(timer); // Windows 10 1803 and later
        try
        {
            var frame = 1 / hertz;
            for (var next = frame; next <= seconds + frame; next += frame)
            {
                var wait = next - clock.Elapsed.TotalSeconds;
                timer.Arm(TimeSpan.FromSeconds(Math.Max(0.0005, wait)));
                Assert.True(signal.Wait(TimeSpan.FromSeconds(2)), "the frame timer fired");
                times.Add(clock.Elapsed.TotalSeconds);
                if (times[^1] > next + frame) next = Math.Floor(times[^1] / frame) * frame; // a late frame: the next boundary after it
            }
        }
        finally
        {
            timer.Dispose();
        }
        return times;
    }

    private static void AssertLandsBySpeed(IReadOnlyList<double> times, IReadOnlyList<double> values, string what)
    {
        var last = values.Count - 1;
        var end = Math.Abs(values[last] - values[last - 1]);
        var before = Math.Abs(values[last - 1] - values[last - 2]);
        var endSpeed = end / (times[last] - times[last - 1]);
        var beforeSpeed = before / (times[last - 1] - times[last - 2]);
        Assert.True(endSpeed <= beforeSpeed || end < AtRest,
            $"{what}: the last frame moved {end:E2} in {(times[last] - times[last - 1]) * 1000:F2} ms, the one before {before:E2} in {(times[last - 1] - times[last - 2]) * 1000:F2} ms");
    }

    [Theory]
    [InlineData(0.35, 1.0, 60)]
    [InlineData(0.35, 1.0, 144)]
    [InlineData(0.21, 1.0, 60)]
    [InlineData(0.21, 1.0, 144)]
    [InlineData(0.3, 0.85, 144)]
    [InlineData(0.6, 0.86, 60)]
    [Trait("Rule", "R-VIEW-4")]
    [Trait("Decision", "ED-NEW-perfmotion-10")]
    public void Native_a_spring_on_the_machines_real_frames_ends_with_no_last_frame_faster_than_the_one_before(double response, double damping, int hertz)
    {
        if (!OperatingSystem.IsWindows()) return;
        var spring = Animation.Spring(response: response, dampingFraction: damping);
        for (var run = 0; run < 5; run++)
        {
            var times = new List<double>();
            var values = new List<double>();
            foreach (var t in RealFrames(hertz, spring.Duration))
            {
                var done = t + 0.001 >= spring.Duration; // ED-117 (2), as AnimatedScalar finishes a run
                times.Add(t);
                values.Add(done ? 1 : spring.Progress(t));
                if (done) break;
            }
            Assert.Equal(1, values[^1]);
            AssertLandsBySpeed(times, values, $"spring({response}, {damping}) at {hertz} Hz, run {run}");
        }
    }

    [Theory]
    [InlineData(60)]
    [InlineData(144)]
    [Trait("Rule", "R-VIEW-4")]
    [Trait("Decision", "ED-NEW-perfmotion-10")]
    public void Native_the_zoom_spring_on_the_machines_real_frames_lands_with_no_last_frame_faster_than_the_one_before(int hertz)
    {
        if (!OperatingSystem.IsWindows()) return;
        for (var run = 0; run < 5; run++)
        {
            // ZoomMotion.To(1.1) from 1: the rate, the rest bound of the scale and the press's push (ED-NEW-textsize-3).
            var spring = new LandingSpring(1);
            spring.Aim(1.1, 26, 1e-4 * 1.1, 26 * 0.1);
            var times = new List<double>();
            var values = new List<double>();
            var previous = 0.0;
            foreach (var t in RealFrames(hertz, 0.5))
            {
                var landed = spring.Advance(t - previous);
                previous = t;
                times.Add(t);
                values.Add(spring.Position);
                if (landed) break;
            }
            Assert.Equal(1.1, values[^1]);
            AssertLandsBySpeed(times, values, $"the zoom glide at {hertz} Hz, run {run}");
        }
    }

    [Theory]
    [InlineData(60)]
    [InlineData(144)]
    [Trait("Rule", "R-VIEW-4")]
    [Trait("Decision", "ED-NEW-perfmotion-11")]
    public void Native_a_spring_retargeted_on_the_machines_real_frames_keeps_its_speed_across_the_new_target(int hertz)
    {
        if (!OperatingSystem.IsWindows()) return;
        // An AnimatedScalar-style run of smooth(0.35) from 0 to 100, sent back to 40 after 80 ms with keepSpeed: the new run sets out
        // from the value reached at the speed it had (Animation.WithStartSpeed), so the speed over the frames either side of the new
        // target differs by no more than the spring's own change of speed over a frame.
        var curve = Animation.Smooth(0.35);
        var frames = RealFrames(hertz, 0.6);
        const double retargetAt = 0.08;
        var at = frames.FindIndex(t => t >= retargetAt);
        Assert.InRange(at, 2, frames.Count - 3);
        var start = frames[at];
        var reached = 100 * curve.Progress(start);
        var speed = 100 * curve.Speed(start);
        var carried = curve.WithStartSpeed(speed / (40 - reached));
        double Value(double t) => t < start ? 100 * curve.Progress(t) : reached + (40 - reached) * carried.Progress(t - start);
        var before = (Value(frames[at]) - Value(frames[at - 1])) / (frames[at] - frames[at - 1]);
        var after = (Value(frames[at + 1]) - Value(frames[at])) / (frames[at + 1] - frames[at]);
        var frame = frames[at + 1] - frames[at];
        // The spring's pull towards 40 over one frame bounds the change of speed: omega^2 times the distance, times the frame.
        var omega = 2 * Math.PI / 0.35;
        var bound = omega * omega * Math.Abs(40 - reached) * frame + omega * Math.Abs(speed) * frame * 2;
        Assert.True(Math.Abs(after - before) <= bound, $"the speed went from {before:F1} to {after:F1} a second across the new target (bound {bound:F1})");
        Assert.True(after > 0, $"it carries on outwards for a moment at {after:F1} a second, as a spring with speed does");
    }

    [Fact]
    [Trait("Rule", "R-VIEW-4")]
    [Trait("Decision", "ED-NEW-perfmotion-11")]
    public void Native_a_popover_opened_again_while_it_shrinks_draws_no_turn_back_on_the_composed_frames() => headless.OnUiThread(() =>
    {
        // The drawn frames of the real composition (the window's rendered frame), not the transform's numbers: the card's drawn width,
        // measured in pixels, keeps shrinking for a frame after the press and then grows back to its full width, never stepping up and
        // down again.
        using var clocks = new AnimationClocks();
        var card = new Border { Width = 200, Height = 120, Background = Brushes.Red };
        var window = new Window { Width = 400, Height = 300, Background = Brushes.White, Content = card };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            PopoverAppear.Play(card, PlacementMode.Bottom);
            for (var i = 0; i < 30; i++) clocks.Frame(1 / 60.0);
            // A row just under the card's top edge: a popover below its anchor grows from its top (PopoverAppear's origin), so the row
            // stays on the card at every scale it is drawn at.
            var row = (int)Math.Round((card.Bounds.Top + 2) * window.RenderScaling);
            PopoverAppear.Dismiss(card, PlacementMode.Bottom, () => { });
            var widths = new List<int>();
            clocks.Frame(1 / 60.0);
            widths.Add(DrawnWidth(window, row));
            clocks.Frame(1 / 60.0);
            widths.Add(DrawnWidth(window, row));
            PopoverAppear.Play(card, PlacementMode.Bottom);
            for (var i = 0; i < 40; i++) { clocks.Frame(1 / 60.0); widths.Add(DrawnWidth(window, row)); }
            Assert.True(widths[2] <= widths[1], $"the card keeps shrinking for a frame after the press ({widths[1]} then {widths[2]} px)");
            var lowest = widths.IndexOf(widths.Min());
            for (var i = lowest + 1; i < widths.Count; i++)
                Assert.True(widths[i] >= widths[i - 1], $"the card grows back without stepping down: {string.Join(", ", widths)}");
            Assert.Equal(200, widths[^1]);
        }
        finally
        {
            window.Close();
            Dispatcher.UIThread.RunJobs();
        }
    });

    // The width in pixels of the red card along a row of the window, in the frame the composition drew.
    private static int DrawnWidth(Window window, int row)
    {
        window.UpdateLayout();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
        using var frame = window.CaptureRenderedFrame() ?? throw new Xunit.Sdk.XunitException("no rendered frame");
        using var buffer = frame.Lock();
        var pixels = new byte[buffer.RowBytes];
        System.Runtime.InteropServices.Marshal.Copy(buffer.Address + row * buffer.RowBytes, pixels, 0, pixels.Length);
        var red = 0;
        for (var x = 0; x < buffer.Size.Width; x++)
        {
            var b = pixels[x * 4];
            var g = pixels[x * 4 + 1];
            var r = pixels[x * 4 + 2];
            if (r > 200 && g < 245 && b < 245) red++; // the card over white, at any of its fade's opacities
        }
        return red;
    }
}
