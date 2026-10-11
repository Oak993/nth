using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Xunit;

namespace Marrow.App.Tests.Views.Chat;

// ED-NEW-lean-8: budgets and the pixel guard for the Hydra decorations of Marrow.App (the charge around the Hydra button,
// a head's progress bar and its working title). Headless and deterministic: a manual clock and a manual frame source, no
// wall clock, no sleeps (R-TEST-5). Allocation figures are Debug builds (the heavy slot's build), bytes per drawn frame
// after warm-up.
[Collection("Headless")]
public sealed class HydraDecorationLeanTests(HeadlessSession headless)
{
    // Budgets: measured on the unchanged base, then on the fix (both numbers in ED-NEW-lean-8).
    private const int ChargeInvalidationsPerSecond = 78; // base 240 at 240 Hz; fixed 60
    private const long ChargeBytesPerFrame = 1092; // base 15088; fixed 840 (what is left is Skia's two gradient shaders per draw)
    private const int BarInvalidationsPerCycle = 83; // one 6 s sweep cycle; base 145; fixed 64 (the 2.6 s sweep at 24 a second)
    private const long BarBytesPerFrame = 519; // base 91812; fixed 399 (the timeline's frame and Skia's clip and band shader)
    private const int TitleLayoutsPerSecond = 0; // base 20; fixed 0
    private const long TitleBytesPerFrame = 281; // base 5930; fixed 216 (the timeline's frame and the stops' change notifications)

    // The pixel guard: SHA-256 over the frames at the 20 fixed timestamps, recorded on the unchanged base.
    private const string ChargeFrames = "C1C60C4544099BDCC3F185574F25B66222254B8A0EBF7828DEEE129007BDC64C";
    private const string BarFrames = "E121A229A1D261F89E336C72F03F06DD6F5889FC154968C4CE22BF6B07282824";
    private const string TitleFrames = "94CEFEDD9BEE686D583980CEA8F2337A26FDE325F04A6FBA9484F00EEA505C06";

    private const int Refresh = 240; // a 240 Hz display: one frame source tick per refresh

    private static Avalonia.Controls.Window Show(Control content, double width, double height)
    {
        // the title's primary text is dark in the light appearance: it is drawn over grey so every shade of it shows
        var window = new Avalonia.Controls.Window { Width = width, Height = height, Content = content, Background = content is HydraWorkingTitle ? Brushes.Gray : Brushes.Black };
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

    private static HydraChargeMotion Charge() => new() { Width = 28, Height = 28 };

    private static HydraProgressBar Bar() => new(EmptyRuntime(), DateTimeOffset.UnixEpoch, finishedAt: null, HydraHeadInfo.Status.Running,
        new SwiftColor(0.3, 0.6, 0.9),
        [
            ToolEntry(ToolCall.Kind.Read, "Read a file"),
            ToolEntry(ToolCall.Kind.Command, "Run a command"),
            ToolEntry(ToolCall.Kind.Edit, "Edit a file"),
            ToolEntry(ToolCall.Kind.Read, "Read another file"),
            ToolEntry(ToolCall.Kind.Edit, "Edit another file"),
        ]);

    private static HydraWorkingTitle Title() => new("Refactor the sweep budget", isRunning: true);

    [Fact]
    [Trait("Decision", "ED-NEW-lean-8")]
    public void The_charge_repaints_at_most_sixty_times_a_second_on_a_240_Hz_display() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        var time = new ManualTimeProvider();
        using var source = FrameClock.Source.Override(frames);
        using var clock = SwiftClock.Provider.Override(time);
        var charge = Charge();
        var window = Show(charge, 48, 48);
        try
        {
            for (var i = 0; i < 60; i++) Tick(time, frames, 1.0 / Refresh);
            var before = charge.LayerView.Invalidations;
            for (var i = 0; i < Refresh; i++) Tick(time, frames, 1.0 / Refresh);
            var count = charge.LayerView.Invalidations - before;
            Assert.True(count <= ChargeInvalidationsPerSecond, $"charge invalidations per simulated second: {count} (limit {ChargeInvalidationsPerSecond})");
            Assert.True(count >= 59, $"the charge still turns at 60 a second: {count}");
        }
        finally { window.Close(); Dispatcher.UIThread.RunJobs(); }
    });

    [Fact]
    [Trait("Decision", "ED-NEW-lean-8")]
    public void The_progress_bar_repaints_only_while_its_picture_moves() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        var time = new ManualTimeProvider();
        using var source = FrameClock.Source.Override(frames);
        using var clock = SwiftClock.Provider.Override(time);
        var bar = Bar();
        var window = Show(bar, 300, 90);
        try
        {
            var drawing = BarDrawing(bar);
            for (var i = 0; i < 60; i++) Tick(time, frames, 1.0 / Refresh);
            var before = Invalidations(drawing);
            for (var i = 0; i < 6 * Refresh; i++) Tick(time, frames, 1.0 / Refresh); // one whole sweep cycle (2.6 s + 3.4 s rest)
            var count = Invalidations(drawing) - before;
            Assert.True(count <= BarInvalidationsPerCycle, $"bar invalidations per 6 s sweep cycle: {count} (limit {BarInvalidationsPerCycle})");
            Assert.True(count >= 2.6 * 24 - 2, $"the sweep still moves at 24 a second: {count}");
        }
        finally { window.Close(); Dispatcher.UIThread.RunJobs(); }
    });

    [Fact]
    [Trait("Decision", "ED-NEW-lean-8")]
    public void The_working_title_keeps_its_text_layout_while_it_shimmers() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        var time = new ManualTimeProvider();
        using var source = FrameClock.Source.Override(frames);
        using var clock = SwiftClock.Provider.Override(time);
        var title = Title();
        var window = Show(title, 220, 60);
        try
        {
            var label = title.GetVisualDescendants().OfType<TextBlock>().Single();
            for (var i = 0; i < 60; i++) Tick(time, frames, 1.0 / Refresh);
            var layout = label.TextLayout;
            var layouts = 0;
            var opacities = new HashSet<double>();
            for (var i = 0; i < Refresh; i++)
            {
                Tick(time, frames, 1.0 / Refresh);
                if (!ReferenceEquals(layout, label.TextLayout)) { layouts++; layout = label.TextLayout; }
                opacities.Add(label.Opacity);
            }
            Assert.True(layouts <= TitleLayoutsPerSecond, $"title text layouts per simulated second: {layouts} (limit {TitleLayoutsPerSecond})");
            Assert.True(opacities.Count >= 15, $"the title still breathes at 20 a second: {opacities.Count}");
        }
        finally { window.Close(); Dispatcher.UIThread.RunJobs(); }
    });

    [Fact]
    [Trait("Decision", "ED-NEW-lean-8")]
    public void The_charge_allocates_within_budget_per_drawn_frame_once_warm() => headless.OnUiThread(() =>
    {
        var charge = Charge();
        var bytes = MeasureFrames(charge, 48, 48, 1.0 / 60, 120, 240, context => charge.LayerView.Render(context));
        Assert.True(bytes <= ChargeBytesPerFrame, $"charge bytes per drawn frame: {bytes} (limit {ChargeBytesPerFrame})");
    });

    [Fact]
    [Trait("Decision", "ED-NEW-lean-8")]
    public void The_progress_bar_allocates_within_budget_per_drawn_frame_once_warm() => headless.OnUiThread(() =>
    {
        var bar = Bar();
        Control? drawing = null;
        // one whole sweep cycle at the bar's 24 a second, so the sweep and the rest both count
        var bytes = MeasureFrames(bar, 300, 90, 1.0 / 24, 144, 144, context => (drawing ??= BarDrawing(bar)).Render(context));
        Assert.True(bytes <= BarBytesPerFrame, $"bar bytes per drawn frame: {bytes} (limit {BarBytesPerFrame})");
    });

    [Fact]
    [Trait("Decision", "ED-NEW-lean-8")]
    public void The_working_title_allocates_within_budget_per_drawn_frame_once_warm() => headless.OnUiThread(() =>
    {
        var title = Title();
        TextBlock? label = null;
        var bytes = MeasureFrames(title, 220, 60, 1.0 / 20, 124, 124, _ => GC.KeepAlive((label ??= title.GetVisualDescendants().OfType<TextBlock>().Single()).TextLayout));
        Assert.True(bytes <= TitleBytesPerFrame, $"title bytes per drawn frame: {bytes} (limit {TitleBytesPerFrame})");
    });

    [Fact]
    [Trait("Decision", "ED-NEW-lean-8")]
    public void The_charge_draws_the_same_pixels_at_twenty_fixed_timestamps() => headless.OnUiThread(() =>
    {
        var (hash, distinct) = Frames(Charge, 48, 48);
        Assert.True(distinct >= 10, $"the charge moves between the timestamps: {distinct} distinct frames");
        Assert.True(hash == ChargeFrames, $"charge frames: {hash}");
    });

    [Fact]
    [Trait("Decision", "ED-NEW-lean-8")]
    public void The_progress_bar_draws_the_same_pixels_at_twenty_fixed_timestamps() => headless.OnUiThread(() =>
    {
        var (hash, distinct) = Frames(Bar, 300, 90);
        Assert.True(distinct >= 5, $"the sweep moves between the timestamps: {distinct} distinct frames");
        Assert.True(hash == BarFrames, $"bar frames: {hash}");
    });

    [Fact]
    [Trait("Decision", "ED-NEW-lean-8")]
    public void The_working_title_draws_the_same_pixels_at_twenty_fixed_timestamps() => headless.OnUiThread(() =>
    {
        var (hash, distinct) = Frames(Title, 220, 60);
        Assert.True(distinct >= 10, $"the title shimmers between the timestamps: {distinct} distinct frames");
        Assert.True(hash == TitleFrames, $"title frames: {hash}");
    });

    // ---- harness ----

    private static Control BarDrawing(HydraProgressBar bar) =>
        bar.GetVisualDescendants().OfType<Control>().Single(control => control.GetType().Name == "BarDrawing");

    private static int Invalidations(Control drawing) =>
        (int)drawing.GetType().GetProperty("Invalidations", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(drawing)!;

    /// <summary>
    /// Bytes per drawn frame: each frame advances the clock by <paramref name="step"/> (so every frame is a due one), runs the
    /// frame callbacks and draws once. The frame source and the clock allocate nothing themselves.
    /// </summary>
    private static long MeasureFrames(Control control, double width, double height, double step, int warm, int measured, Action<DrawingContext> draw)
    {
        var frames = new SlotFrames();
        var time = new StepClock();
        using var source = FrameClock.Source.Override(frames);
        using var clock = SwiftClock.Provider.Override(time);
        var window = Show(control, width, height);
        try
        {
            using var bitmap = new RenderTargetBitmap(new PixelSize((int)width, (int)height));
            using var context = bitmap.CreateDrawingContext();
            var span = TimeSpan.FromSeconds(step);
            for (var i = 0; i < warm; i++) { time.Step(span); frames.Fire(time.Elapsed); draw(context); }
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < measured; i++) { time.Step(span); frames.Fire(time.Elapsed); draw(context); }
            return (GC.GetAllocatedBytesForCurrentThread() - before) / measured;
        }
        finally { window.Close(); Dispatcher.UIThread.RunJobs(); }
    }

    /// <summary>The control rendered by the compositor at 20 fixed timestamps (1 s, then every 0.173 s): one SHA-256 over
    /// every frame's pixels, and how many of the frames differ.</summary>
    private static (string Hash, int Distinct) Frames(Func<Control> make, double width, double height)
    {
        var frames = new ManualFrameSource();
        var time = new ManualTimeProvider();
        using var source = FrameClock.Source.Override(frames);
        using var clock = SwiftClock.Provider.Override(time);
        var window = Show(make(), width, height);
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

    private static TimelineEntry ToolEntry(ToolCall.Kind kind, string title) =>
        new(new TimelineItem(
            turnID: null,
            content: new TimelineItem.Content.Tool(new ToolCall
            {
                ToolCallKind = kind,
                Title = title,
                StartedAt = DateTimeOffset.UnixEpoch,
            }),
            date: DateTimeOffset.UnixEpoch));

    private static ThreadRuntime EmptyRuntime()
    {
        var runtime = (ThreadRuntime)RuntimeHelpers.GetUninitializedObject(typeof(ThreadRuntime));
        typeof(ThreadRuntime)
            .GetField("entries", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(runtime, ValueList<TimelineEntry>.Empty);
        return runtime;
    }

    /// <summary>A frame source that allocates nothing per frame: requests go into a fixed array.</summary>
    private sealed class SlotFrames : IFrameSource
    {
        private readonly Action<TimeSpan>?[] waiting = new Action<TimeSpan>?[16];
        private readonly Action<TimeSpan>?[] running = new Action<TimeSpan>?[16];
        private int count;

        public void RequestFrame(Action<TimeSpan> frame) => waiting[count++] = frame;

        public void CancelFrame(Action<TimeSpan> frame)
        {
            for (var i = 0; i < count; i++)
            {
                if (!ReferenceEquals(waiting[i], frame)) continue;
                Array.Copy(waiting, i + 1, waiting, i, count - i - 1);
                waiting[--count] = null;
                return;
            }
        }

        public int Pending => count;

        public void Fire(TimeSpan now)
        {
            var n = count;
            Array.Copy(waiting, running, n);
            Array.Clear(waiting, 0, n);
            count = 0;
            for (var i = 0; i < n; i++) { running[i]!(now); running[i] = null; }
        }
    }

    /// <summary>A clock that steps by hand and allocates nothing (ManualTimeProvider's Advance queries its timers with LINQ);
    /// its timers never fire.</summary>
    private sealed class StepClock : TimeProvider
    {
        private static readonly DateTimeOffset Origin = new(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);
        private long ticks;

        public TimeSpan Elapsed => TimeSpan.FromTicks(ticks);

        public void Step(TimeSpan by) => ticks += by.Ticks;

        public override long GetTimestamp() => ticks;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override DateTimeOffset GetUtcNow() => Origin + TimeSpan.FromTicks(ticks);

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) => new Idle();

        private sealed class Idle : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;
            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
