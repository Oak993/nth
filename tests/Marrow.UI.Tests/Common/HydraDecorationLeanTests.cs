using System.Security.Cryptography;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Common;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Common;

// ED-NEW-lean-8: budgets and the pixel guard for the Hydra decorations of Marrow.UI (the working spinner and the breathing
// ring). Headless and deterministic: a manual clock and a manual frame source, no wall clock, no sleeps (R-TEST-5).
// Allocation figures are Debug builds (the heavy slot's build), bytes per drawn frame after warm-up.
[Collection("Headless")]
public sealed class HydraDecorationLeanTests(HeadlessSession headless)
{
    // Budgets: measured on the unchanged base, then on the fix (both numbers in ED-NEW-lean-8).
    private const int SpinnerInvalidationsPerSecond = 78; // base 240 at 240 Hz; fixed 60
    private const long SpinnerBytesPerFrame = 0; // base 3920; fixed 0
    private const int RingInvalidationsPerSecond = 78; // base 240 at 240 Hz; fixed 60
    private const int RingReduceMotionRequestsPerSecond = 0; // base 240 at 240 Hz; fixed 0
    private const long RingBytesPerFrame = 0; // base 1768; fixed 56, then 0 with the swell's curve made once (review: the 56 were a timing curve per frame, not the stroke)

    // ED-NEW-lean-8 review: what the ring allocates per frame itself, measured, not claimed: its frame callback's bytes plus its
    // draw's, less the bytes of the same draw calls made bare (a pen of the same width, made once by the test).
    private const long RingOwnBytesPerFrame = 0; // base 1768; fixed 0

    // The pixel guard: SHA-256 over the frames at the 20 fixed timestamps, recorded on the unchanged base.
    private const string SpinnerFrames = "50405F9D8B66FFD0E489D896D3C34F2FF6D0466F8C6378E1EA2F7DB2AC802875";
    private const string RingFrames = "C0F7817A296EEB0A3AB53332F7B6F2718459C7D8AEC009645E5096E047AA70DB";

    private const int Refresh = 240; // a 240 Hz display: one frame source tick per refresh

    private static Window Show(Control content, double width = 48, double height = 48)
    {
        var window = new Window { Width = width, Height = height, Content = content, Background = Brushes.Black };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static void Tick(ManualTimeProvider time, ManualFrameSource frames, double seconds)
    {
        time.Advance(TimeSpan.FromSeconds(seconds));
        frames.Advance(TimeSpan.FromSeconds(seconds));
        Dispatcher.UIThread.RunJobs();
    }

    [Fact]
    [Trait("Decision", "ED-NEW-lean-8")]
    public void The_spinner_repaints_at_most_sixty_times_a_second_on_a_240_Hz_display() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        var time = new ManualTimeProvider();
        using var source = FrameClock.Source.Override(frames);
        using var clock = SwiftClock.Provider.Override(time);
        using var display = DecorationPace.Period.Override(() => TimeSpan.FromSeconds(1.0 / Refresh)); // a 240 Hz display (ED-NEW-lean-8 review)
        var spinner = new SpinnerLayerView(MiniSpinner.Cells, 2, 2.4);
        var window = Show(spinner);
        try
        {
            for (var i = 0; i < 60; i++) Tick(time, frames, 1.0 / Refresh);
            var before = spinner.Invalidations;
            for (var i = 0; i < Refresh; i++) Tick(time, frames, 1.0 / Refresh);
            var count = spinner.Invalidations - before;
            Assert.True(count <= SpinnerInvalidationsPerSecond, $"spinner invalidations per simulated second: {count} (limit {SpinnerInvalidationsPerSecond})");
            Assert.True(count >= 59, $"the spinner still moves at 60 a second: {count}");
        }
        finally { window.Close(); Dispatcher.UIThread.RunJobs(); }
    });

    [Fact]
    [Trait("Decision", "ED-NEW-lean-8")]
    public void The_breathing_ring_repaints_at_most_sixty_times_a_second_on_a_240_Hz_display() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        var time = new ManualTimeProvider();
        using var source = FrameClock.Source.Override(frames);
        using var clock = SwiftClock.Provider.Override(time);
        using var display = DecorationPace.Period.Override(() => TimeSpan.FromSeconds(1.0 / Refresh)); // a 240 Hz display (ED-NEW-lean-8 review)
        var ring = new HydraBreathRingView { Width = 20, Height = 20, StrokeColor = new SwiftColor(0.3, 0.6, 0.9, 0.55) };
        var window = Show(ring);
        try
        {
            for (var i = 0; i < 60; i++) Tick(time, frames, 1.0 / Refresh);
            var before = ring.Invalidations;
            for (var i = 0; i < Refresh; i++) Tick(time, frames, 1.0 / Refresh);
            var count = ring.Invalidations - before;
            Assert.True(count <= RingInvalidationsPerSecond, $"ring invalidations per simulated second: {count} (limit {RingInvalidationsPerSecond})");
            Assert.True(count >= 59, $"the ring still breathes at 60 a second: {count}");
        }
        finally { window.Close(); Dispatcher.UIThread.RunJobs(); }
    });

    [Fact]
    [Trait("Decision", "ED-NEW-lean-8")]
    public void Under_Reduce_Motion_the_breathing_ring_asks_for_no_frames() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        var time = new ManualTimeProvider();
        using var source = FrameClock.Source.Override(frames);
        using var clock = SwiftClock.Provider.Override(time);
        using var display = DecorationPace.Period.Override(() => TimeSpan.FromSeconds(1.0 / Refresh)); // a 240 Hz display (ED-NEW-lean-8 review)
        var ring = new HydraBreathRingView { Width = 20, Height = 20 };
        var host = new Border { Child = ring };
        EnvironmentValues.SetAccessibilityReduceMotion(host, true);
        var window = Show(host);
        try
        {
            for (var i = 0; i < 10; i++) Tick(time, frames, 1.0 / Refresh);
            var requests = 0;
            for (var i = 0; i < Refresh; i++)
            {
                requests += frames.Pending;
                Tick(time, frames, 1.0 / Refresh);
            }
            Assert.True(requests <= RingReduceMotionRequestsPerSecond, $"ring frame requests per simulated second under Reduce Motion: {requests} (limit {RingReduceMotionRequestsPerSecond})");
            Assert.Equal(HydraBreathRingView.RestingScale, ring.CurrentScale);
            Assert.Equal(HydraBreathRingView.RestingOpacity, ring.CurrentOpacity);

            // Reduce Motion turned off again: the breath comes back.
            EnvironmentValues.SetAccessibilityReduceMotion(host, false);
            Dispatcher.UIThread.RunJobs();
            Tick(time, frames, 0.4);
            Tick(time, frames, 0.4);
            Assert.NotEqual(HydraBreathRingView.RestingScale, ring.CurrentScale);
        }
        finally { window.Close(); Dispatcher.UIThread.RunJobs(); }
    });

    [Fact]
    [Trait("Decision", "ED-NEW-lean-8")]
    public void The_spinner_allocates_within_budget_per_drawn_frame_once_warm() => headless.OnUiThread(() =>
    {
        var bytes = MeasureFrames(new SpinnerLayerView(MiniSpinner.Cells, 2, 6), static (control, context) => control.Render(context));
        Assert.True(bytes <= SpinnerBytesPerFrame, $"spinner bytes per drawn frame: {bytes} (limit {SpinnerBytesPerFrame})");
    });

    [Fact]
    [Trait("Decision", "ED-NEW-lean-8")]
    public void The_breathing_ring_allocates_within_budget_per_drawn_frame_once_warm() => headless.OnUiThread(() =>
    {
        var ring = new HydraBreathRingView { Width = 20, Height = 20, StrokeColor = new SwiftColor(0.3, 0.6, 0.9, 0.55) };
        var bytes = MeasureFrames(ring, static (control, context) => control.Render(context));
        Assert.True(bytes <= RingBytesPerFrame, $"ring bytes per drawn frame: {bytes} (limit {RingBytesPerFrame})");
    });

    [Fact]
    [Trait("Decision", "ED-NEW-lean-8")]
    public void The_spinner_draws_the_same_pixels_at_twenty_fixed_timestamps() => headless.OnUiThread(() =>
    {
        var (hash, distinct) = Frames(() => new MiniSpinner(cellSize: 6));
        Assert.True(distinct >= 10, $"the spinner moves between the timestamps: {distinct} distinct frames");
        Assert.True(hash == SpinnerFrames, $"spinner frames: {hash}");
    });

    [Fact]
    [Trait("Decision", "ED-NEW-lean-8")]
    public void The_breathing_ring_draws_the_same_pixels_at_twenty_fixed_timestamps() => headless.OnUiThread(() =>
    {
        var (hash, distinct) = Frames(() => new HydraBreathRingView { Width = 20, Height = 20, StrokeColor = new SwiftColor(0.3, 0.6, 0.9, 0.55) });
        Assert.True(distinct >= 10, $"the ring moves between the timestamps: {distinct} distinct frames");
        Assert.True(hash == RingFrames, $"ring frames: {hash}");
    });

    [Fact]
    [Trait("Decision", "ED-NEW-lean-8")]
    public void The_breathing_ring_allocates_nothing_of_its_own_per_frame() => headless.OnUiThread(() =>
    {
        var ring = new HydraBreathRingView { Width = 20, Height = 20, StrokeColor = new SwiftColor(0.3, 0.6, 0.9, 0.55) };
        var frames = new SlotFrames();
        var time = new StepClock();
        long frame, draw, bare;
        using (FrameClock.Source.Override(frames))
        using (SwiftClock.Provider.Override(time))
        {
            var window = Show(ring);
            try
            {
                using var bitmap = new RenderTargetBitmap(new PixelSize(48, 48));
                using var context = bitmap.CreateDrawingContext();
                var step = TimeSpan.FromSeconds(1.0 / 60);
                for (var i = 0; i < 120; i++) { time.Step(step); frames.Fire(time.Elapsed); ring.Render(context); }
                var before = GC.GetAllocatedBytesForCurrentThread();
                for (var i = 0; i < 240; i++) { time.Step(step); frames.Fire(time.Elapsed); }
                frame = (GC.GetAllocatedBytesForCurrentThread() - before) / 240;
                before = GC.GetAllocatedBytesForCurrentThread();
                for (var i = 0; i < 240; i++) ring.Render(context);
                draw = (GC.GetAllocatedBytesForCurrentThread() - before) / 240;

                var pen = new Pen(new SolidColorBrush(Color.FromArgb(140, 77, 153, 230)), 1.5);
                void Bare(int i)
                {
                    var scale = 1.05 + i % 40 * 0.01;
                    using (context.PushOpacity(0.9 - i % 40 * 0.02))
                    using (context.PushTransform(Matrix.CreateTranslation(-10, -10) * Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(10, 10)))
                        context.DrawEllipse(null, pen, new Point(10, 10), 9.25, 9.25);
                }
                for (var i = 0; i < 120; i++) Bare(i);
                before = GC.GetAllocatedBytesForCurrentThread();
                for (var i = 0; i < 240; i++) Bare(i);
                bare = (GC.GetAllocatedBytesForCurrentThread() - before) / 240;
            }
            finally { window.Close(); Dispatcher.UIThread.RunJobs(); }
        }
        var own = frame + draw - bare;
        Assert.True(own <= RingOwnBytesPerFrame, $"ring own bytes per frame: {own} (frame {frame}, draw {draw}, bare draw {bare}; limit {RingOwnBytesPerFrame})");
    });

    // ---- harness ----

    /// <summary>
    /// Bytes per drawn frame: each frame advances the clock by 1/60 s (so every frame is a due one), runs the control's frame
    /// callback and draws it once into a bitmap. The frame source and the clock allocate nothing themselves.
    /// </summary>
    private static long MeasureFrames(Control control, Action<Control, DrawingContext> draw)
    {
        var frames = new SlotFrames();
        var time = new StepClock();
        using var source = FrameClock.Source.Override(frames);
        using var clock = SwiftClock.Provider.Override(time);
        using var display = DecorationPace.Period.Override(() => TimeSpan.FromSeconds(1.0 / Refresh)); // a 240 Hz display (ED-NEW-lean-8 review)
        var window = Show(control);
        try
        {
            using var bitmap = new RenderTargetBitmap(new PixelSize(48, 48));
            using var context = bitmap.CreateDrawingContext();
            var step = TimeSpan.FromSeconds(1.0 / 60);
            for (var i = 0; i < 120; i++) { time.Step(step); frames.Fire(time.Elapsed); draw(control, context); }
            const int measured = 240;
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < measured; i++) { time.Step(step); frames.Fire(time.Elapsed); draw(control, context); }
            return (GC.GetAllocatedBytesForCurrentThread() - before) / measured;
        }
        finally { window.Close(); Dispatcher.UIThread.RunJobs(); }
    }

    /// <summary>The control rendered by the compositor at 20 fixed timestamps (1 s, then every 0.173 s): one SHA-256 over
    /// every frame's pixels, and how many of the frames differ.</summary>
    private static (string Hash, int Distinct) Frames(Func<Control> make)
    {
        var frames = new ManualFrameSource();
        var time = new ManualTimeProvider();
        using var source = FrameClock.Source.Override(frames);
        using var clock = SwiftClock.Provider.Override(time);
        using var display = DecorationPace.Period.Override(() => TimeSpan.FromSeconds(1.0 / Refresh)); // a 240 Hz display (ED-NEW-lean-8 review)
        var window = Show(make());
        try
        {
            using var all = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var seen = new HashSet<string>();
            Tick(time, frames, 1.0);
            for (var k = 0; k < 20; k++)
            {
                if (k > 0) Tick(time, frames, 0.173);
                Dispatcher.UIThread.RunJobs();
                using var frame = window.CaptureRenderedFrame() ?? throw new Xunit.Sdk.XunitException("no rendered frame");
                using var buffer = frame.Lock();
                var bytes = new byte[buffer.RowBytes * buffer.Size.Height];
                System.Runtime.InteropServices.Marshal.Copy(buffer.Address, bytes, 0, bytes.Length);
                all.AppendData(bytes);
                seen.Add(Convert.ToHexString(SHA256.HashData(bytes)));
            }
            return (Convert.ToHexString(all.GetHashAndReset()), seen.Count);
        }
        finally { window.Close(); Dispatcher.UIThread.RunJobs(); }
    }
}
