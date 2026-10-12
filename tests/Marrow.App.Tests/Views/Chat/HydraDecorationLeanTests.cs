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
    private const long ChargeBytesPerFrame = 1019; // base 15088; fixed 840, then 784 with the breath's curve made once (review): the two gradient draws' own, as bare (ChargeOwnBytesPerFrame)
    private const int BarInvalidationsPerCycle = 83; // one 6 s sweep cycle; base 145; fixed 64 (the 2.6 s sweep at 24 a second)
    private const long BarBytesPerFrame = 519; // base 91812; fixed 399 (the timeline's frame and Skia's clip and band shader)
    private const int TitleLayoutsPerSecond = 0; // base 20; fixed 0
    private const long TitleBytesPerFrame = 281; // base 5930; fixed 216 (the timeline's frame and the stops' change notifications)

    // ED-NEW-lean-8 review: what each control allocates per frame itself, measured, not claimed: its bytes per frame less the
    // bytes of the same draw calls made bare (the same kinds of pens, brushes and geometry, made once by the test) and of a bare
    // frame clock timeline of the same interval (FrameClock.cs, L7's file). The rest is Avalonia's and Skia's.
    private const long ChargeOwnBytesPerFrame = 0; // base 14304; fixed 0 (of 784 per frame, all of it the same two draws made bare)
    private const long BarOwnBytesPerFrame = 18; // base 91427; fixed 14 (of 399 per frame: the band's clip and shader 161 and the once-a-second accessibility name 224, as bare)
    private const long TitleOwnBytesPerFrame = 3; // base 5716; fixed 2 (of 216 per frame: the stops' and opacity's change notifications 214, as bare)

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
        using var display = Marrow.UI.Common.DecorationPace.Period.Override(() => TimeSpan.FromSeconds(1.0 / Refresh)); // a 240 Hz display (ED-NEW-lean-8 review)
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
        using var display = Marrow.UI.Common.DecorationPace.Period.Override(() => TimeSpan.FromSeconds(1.0 / Refresh)); // a 240 Hz display (ED-NEW-lean-8 review)
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
        using var display = Marrow.UI.Common.DecorationPace.Period.Override(() => TimeSpan.FromSeconds(1.0 / Refresh)); // a 240 Hz display (ED-NEW-lean-8 review)
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

    [Fact]
    [Trait("Decision", "ED-NEW-lean-8")]
    public void The_charge_allocates_nothing_of_its_own_per_frame() => headless.OnUiThread(() =>
    {
        var charge = Charge();
        var (total, frame) = Split(charge, 48, 48, 1.0 / 60, context => charge.LayerView!.Render(context));
        // the charge's own glow and ring, made as its Render makes them: the same accent, size and ring colours
        var layer = charge.LayerView!;
        var glow = HydraCharge.GlowBrush((Color)typeof(HydraChargeLayerView).GetField("glowAccent", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(layer)!, new Size(24, 24));
        var closed = (Color[])typeof(HydraChargeLayerView).GetField("ringColors", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(layer)!;
        Assert.NotEmpty(closed);
        var stops = new GradientStops();
        for (var index = 0; index < closed.Length; index++) stops.Add(new GradientStop(closed[index], index / (double)(closed.Length - 1)));
        var ring = new Pen(new ConicGradientBrush { Center = new RelativePoint(0.5, 0.5, RelativeUnit.Relative), Angle = 90, GradientStops = stops }, HydraChargeLayerView.LineWidth);
        var bare = Bare(48, 48, (context, i) =>
        {
            var centre = new Point(14, 14);
            using (context.PushOpacity(0.5 + i % 7 * 0.01))
                context.DrawEllipse(glow, null, centre, 12, 12);
            var turn = Matrix.CreateTranslation(-14, -14) * Matrix.CreateRotation(i * 0.05) * Matrix.CreateTranslation(14, 14);
            using (context.PushOpacity(0.8 + i % 5 * 0.01))
            using (context.PushTransform(turn))
                context.DrawEllipse(null, ring, centre, 12, 12);
        });
        var own = total - bare;
        Assert.True(own <= ChargeOwnBytesPerFrame, $"charge own bytes per frame: {own} (per frame {total}, of it the frame callback {frame}; bare draw {bare}; limit {ChargeOwnBytesPerFrame})");
    });

    [Fact]
    [Trait("Decision", "ED-NEW-lean-8")]
    public void The_progress_bar_allocates_nothing_of_its_own_per_frame() => headless.OnUiThread(() =>
    {
        var bar = Bar();
        Control? drawing = null;
        var (total, frame) = Split(bar, 300, 90, 1.0 / 24, context => (drawing ??= BarDrawing(bar)).Render(context));
        var timeline = Timeline(1.0 / 24);
        var name = BareName(24, 144); // the accessibility name: a new elapsed second once a second
        // five staves of three kinds, the slots after them, and the band clipped to the staves: the bar's own draw calls
        static GeometryGroup Group(IEnumerable<(int Index, double Height)> rects)
        {
            var group = new GeometryGroup();
            foreach (var (index, height) in rects)
                group.Children.Add(new RectangleGeometry { Rect = new Rect(index * 6, 28 - 28 * height, 3, 28 * height), RadiusX = 1.5, RadiusY = 1.5 });
            return group;
        }
        var kinds = new[] { Group([(0, 0.45), (3, 0.45)]), Group([(1, 0.65)]), Group([(2, 0.85), (4, 0.85)]) };
        var brushes = new IBrush[] { new SolidColorBrush(Colors.SkyBlue), new SolidColorBrush(Colors.SteelBlue), new SolidColorBrush(Colors.RoyalBlue) };
        var filled = Group([(0, 0.45), (1, 0.65), (2, 0.85), (3, 0.45), (4, 0.85)]);
        var slots = Group(Enumerable.Range(5, 45).Select(index => (index, 0.45)));
        var slotBrush = new SolidColorBrush(Color.FromArgb(15, 255, 255, 255));
        var band = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
            GradientStops = { new GradientStop(Color.FromArgb(0, 255, 255, 255), 0), new GradientStop(Color.FromArgb(140, 255, 255, 255), 0.5), new GradientStop(Color.FromArgb(0, 255, 255, 255), 1) },
        };
        var bare = Bare(300, 90, (context, i) =>
        {
            for (var kind = 0; kind < kinds.Length; kind++) context.DrawGeometry(brushes[kind], null, kinds[kind]);
            context.DrawGeometry(slotBrush, null, slots);
            if (i % 144 >= 62) return; // the band is out 2.6 s of each 6 s cycle
            using (context.PushGeometryClip(filled))
                context.DrawRectangle(band, null, new Rect(-24 + i % 144 * 0.8, 0, 24, 28));
        });
        var own = total - timeline - name - bare;
        Assert.True(own <= BarOwnBytesPerFrame, $"bar own bytes per frame: {own} (per frame {total}, of it the frame callbacks {frame}; bare timeline {timeline}, bare name {name}, bare draw {bare}; limit {BarOwnBytesPerFrame})");
    });

    [Fact]
    [Trait("Decision", "ED-NEW-lean-8")]
    public void The_working_title_allocates_nothing_of_its_own_per_frame() => headless.OnUiThread(() =>
    {
        var title = Title();
        var (total, _) = Split(title, 220, 60, 1.0 / 20, _ => { }, 6.2);
        var timeline = Timeline(1.0 / 20, 6.2);
        // the paint's own writes, made bare: the label's opacity and the stops of a gradient on a shown label, moved as the
        // title moves them (the sweep 3.2 s of each 6.2 s, the breath always)
        var stops = new[] { new GradientStop(Colors.White, 0), new GradientStop(Colors.White, 0), new GradientStop(Colors.Gray, 0), new GradientStop(Colors.White, 0.3), new GradientStop(Colors.White, 1) };
        var label = new TextBlock { Text = "Refactor the sweep budget", Foreground = new LinearGradientBrush { GradientStops = { stops[0], stops[1], stops[2], stops[3], stops[4] } } };
        var window = Show(label, 220, 60);
        long stopsBytes;
        try
        {
            void Paint(int i)
            {
                var now = i / 20.0;
                var phase = Math.Min(now % 6.2 / 3.2, 1);
                var centre = -0.3 + phase * 1.6;
                label.Opacity = 0.86 + 0.14 * (0.5 + 0.5 * Math.Sin(now / 3.2 * 2 * Math.PI));
                stops[1].Offset = (float)Math.Clamp(centre - 0.3, 0, 1);
                stops[2].Offset = (float)Math.Clamp(centre, 0, 1);
                stops[3].Offset = (float)Math.Clamp(centre + 0.3, 0, 1);
            }
            for (var i = 0; i < 124; i++) Paint(i);
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 124; i++) Paint(i);
            stopsBytes = (GC.GetAllocatedBytesForCurrentThread() - before) / 124;
        }
        finally { window.Close(); Dispatcher.UIThread.RunJobs(); }
        var own = total - timeline - stopsBytes;
        Assert.True(own <= TitleOwnBytesPerFrame, $"title own bytes per frame: {own} (per frame {total}; bare timeline {timeline}, bare stops {stopsBytes}; limit {TitleOwnBytesPerFrame})");
    });

    // ---- harness ----

    /// <summary>A control's bytes per frame over one whole 6 s cycle of frames at <paramref name="step"/> after a cycle of
    /// warm-up: each frame runs the frame callbacks and draws once (Total); the callbacks alone are also given (Frame).</summary>
    private static (long Total, long Frame) Split(Control control, double width, double height, double step, Action<DrawingContext> draw, double seconds = 6)
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
            var cycle = (int)Math.Round(seconds / step);
            for (var i = 0; i < cycle; i++) { time.Step(span); frames.Fire(time.Elapsed); draw(context); }
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < cycle; i++) { time.Step(span); frames.Fire(time.Elapsed); draw(context); }
            var total = (GC.GetAllocatedBytesForCurrentThread() - before) / cycle;
            before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < cycle; i++) { time.Step(span); frames.Fire(time.Elapsed); }
            return (total, (GC.GetAllocatedBytesForCurrentThread() - before) / cycle);
        }
        finally { window.Close(); Dispatcher.UIThread.RunJobs(); }
    }

    /// <summary>Bytes per call of bare draw calls into a bitmap over <paramref name="count"/> calls, after as many of warm-up.</summary>
    private static long Bare(double width, double height, Action<DrawingContext, int> draw, int count = 144)
    {
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)width, (int)height));
        using var context = bitmap.CreateDrawingContext();
        for (var i = 0; i < count; i++) draw(context, i);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < count; i++) draw(context, i);
        return (GC.GetAllocatedBytesForCurrentThread() - before) / count;
    }

    /// <summary>Bytes per frame of setting a shown control's accessibility name to a new duration sentence once every
    /// <paramref name="every"/> frames (the bar's name: its elapsed seconds change once a second).</summary>
    private static long BareName(int every, int count)
    {
        var host = new Border { Width = 10, Height = 10 };
        var window = Show(host, 48, 48);
        try
        {
            void Name(int i)
            {
                if (i % every != 0) return;
                var parts = new List<string> { $"{(i / every).ToString(System.Globalization.CultureInfo.InvariantCulture)} edits", "1 command", "2 reads" };
                Avalonia.Automation.AutomationProperties.SetName(host, $"{string.Join(", ", parts)}, {Marrow.UI.Common.RelativeTime.Duration(TimeSpan.FromSeconds(60 + i / every))} so far");
            }
            for (var i = 0; i < count; i++) Name(i);
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = count; i < 2 * count; i++) Name(i);
            return (GC.GetAllocatedBytesForCurrentThread() - before) / count;
        }
        finally { window.Close(); Dispatcher.UIThread.RunJobs(); }
    }

    /// <summary>Bytes per frame of a bare frame clock timeline of <paramref name="interval"/> on a shown control.</summary>
    private static long Timeline(double interval, double seconds = 6)
    {
        var frames = new SlotFrames();
        var time = new StepClock();
        using var source = FrameClock.Source.Override(frames);
        using var clock = SwiftClock.Provider.Override(time);
        var dates = 0L;
        var host = new Border { Width = 10, Height = 10 }.TimelineView(() => new AnimationTimelineSchedule(interval, false), date => dates += date.Ticks & 1);
        var window = Show(host, 48, 48);
        try
        {
            var span = TimeSpan.FromSeconds(interval);
            var cycle = (int)Math.Round(seconds / interval);
            for (var i = 0; i < cycle; i++) { time.Step(span); frames.Fire(time.Elapsed); }
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < cycle; i++) { time.Step(span); frames.Fire(time.Elapsed); }
            var bytes = (GC.GetAllocatedBytesForCurrentThread() - before) / cycle;
            GC.KeepAlive(dates);
            return bytes;
        }
        finally { window.Close(); Dispatcher.UIThread.RunJobs(); }
    }

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
        using var display = Marrow.UI.Common.DecorationPace.Period.Override(() => TimeSpan.FromSeconds(1.0 / Refresh)); // a 240 Hz display (ED-NEW-lean-8 review)
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
        using var display = Marrow.UI.Common.DecorationPace.Period.Override(() => TimeSpan.FromSeconds(1.0 / Refresh)); // a 240 Hz display (ED-NEW-lean-8 review)
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
}
