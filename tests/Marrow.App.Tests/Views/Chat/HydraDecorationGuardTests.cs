using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Testing;
using Marrow.UI.Port;
using Xunit;

namespace Marrow.App.Tests.Views.Chat;

// ED-NEW-lean-8 (review): the progress bar's pixel guards for what the sweep-only guard never reaches: staves rising (and the
// tick that ends a rise), the hover stave and its card, empty slots, the Failed and Stopped ending staves, Reduce Motion on
// and off, and theme changes on a bar that stands still. Each guard checks the picture two ways: one SHA-256 over the
// compositor's frames, recorded on the unchanged base (HydraDecorationLeanTests' guard style), and, at every timestamp, the
// bar's kept picture against a new BarDrawing given the same inputs, drawn on the same machine (no pinned output: a stale
// kept shape or brush fails it anywhere). A theme change on a still bar is also checked against a bar made in the new theme.
[Collection("Headless")]
public sealed class HydraDecorationGuardTests(HeadlessSession headless)
{
    // Recorded on the unchanged base (claude/lean-l8 at 6c67eba4a, the base plus the test hooks).
    private const string MotionFrames = "CD68ACC7811B1A7D6D232508F855BEE77F3E7E524307B322ADDE918908C86283";
    private const string EndingFrames = "2D42E52102D1CB907C75823006EA4A8CAD9BC7622AF063B743309DFDC0F8DC41";

    private static readonly SwiftColor Tint = new(0.3, 0.6, 0.9);

    [Fact]
    [Trait("Decision", "ED-NEW-lean-8")]
    public void Rising_staves_hover_slots_and_Reduce_Motion_draw_the_base_pixels_with_nothing_kept_stale() => headless.OnUiThread(() =>
    {
        var (hash, distinct) = Run(HydraHeadInfo.Status.Running, (k, scene) =>
        {
            switch (k)
            {
                case 2: scene.Add(ToolCall.Kind.Edit, 0); break; // a stave arriving now: it rises over the next captures
                case 3: scene.Add(ToolCall.Kind.Read, 0.12); break; // arriving just before the next capture
                case 5: scene.Add(ToolCall.Kind.Command, 0); scene.Hover(1); break;
                case 6: scene.Hover(4); break;
                case 8: scene.Hover(null); break;
                case 9: scene.Add(ToolCall.Kind.Edit, 0.05); break;
                case 11: scene.ReduceMotion(true); break;
                case 12: scene.Add(ToolCall.Kind.Read, 0); break; // under Reduce Motion: no rise
                case 14: scene.ReduceMotion(false); break;
                case 15: scene.Add(ToolCall.Kind.Command, 0); break;
                case 16: scene.Hover(7); break;
                case 18: scene.Hover(null); break;
            }
        });
        Assert.True(distinct >= 10, $"the bar moves between the timestamps: {distinct} distinct frames");
        Assert.True(hash == MotionFrames, $"motion frames: {hash}");
    });

    [Fact]
    [Trait("Decision", "ED-NEW-lean-8")]
    public void Failed_and_Stopped_endings_and_theme_changes_draw_the_base_pixels_with_nothing_kept_stale() => headless.OnUiThread(() =>
    {
        var (hash, distinct) = Run(HydraHeadInfo.Status.Failed, (k, scene) =>
        {
            switch (k)
            {
                case 3: scene.Theme(ThemeVariant.Dark); break; // a settled bar: nothing ticks
                case 6: scene.Theme(ThemeVariant.Light); break;
                case 8: scene.Status(HydraHeadInfo.Status.Stopped); break;
                case 10: scene.Theme(ThemeVariant.Dark); break;
                case 12: scene.Status(HydraHeadInfo.Status.Running); break;
                case 14: scene.Theme(ThemeVariant.Light); break;
                case 16: scene.Status(HydraHeadInfo.Status.Failed); break;
                case 18: scene.Theme(ThemeVariant.Dark); break;
            }
        });
        Assert.True(distinct >= 6, $"the bar changes between the timestamps: {distinct} distinct frames");
        Assert.True(hash == EndingFrames, $"ending frames: {hash}");
    });

    [Theory]
    [Trait("Decision", "ED-NEW-lean-8")]
    [InlineData(HydraHeadInfo.Status.Failed)]
    [InlineData(HydraHeadInfo.Status.Stopped)]
    [InlineData(HydraHeadInfo.Status.Completed)]
    public void A_bar_standing_still_takes_a_theme_change_at_once(HydraHeadInfo.Status status) => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        var time = new ManualTimeProvider();
        using var source = FrameClock.Source.Override(frames);
        using var clock = SwiftClock.Provider.Override(time);
        var entries = Entries(SwiftDate.Now);
        var finished = SwiftDate.Now;
        var started = finished.AddSeconds(-30);
        var live = Show(Bar(status, entries, finished, started), ThemeVariant.Light);
        try
        {
            Tick(time, frames, 1.0); // risen and settled: the bar's clock is paused
            var before = Capture(live.Window);
            live.Window.RequestedThemeVariant = ThemeVariant.Dark;
            Dispatcher.UIThread.RunJobs();
            var after = Capture(live.Window);
            var made = Show(Bar(status, entries, finished, started), ThemeVariant.Dark);
            try
            {
                Tick(time, frames, 1.0);
                Assert.False(before.SequenceEqual(after), "the theme change shows on the bar");
                Assert.True(Capture(made.Window).SequenceEqual(Capture(live.Window)), "a still bar after a theme change is the bar made in that theme");
            }
            finally { made.Window.Close(); Dispatcher.UIThread.RunJobs(); }
        }
        finally { live.Window.Close(); Dispatcher.UIThread.RunJobs(); }
    });

    // ---- harness ----

    private sealed class Scene(HydraProgressBar bar, Avalonia.Controls.Window window, List<TimelineEntry> entries, DateTimeOffset startedAt, DateTimeOffset? finishedAt)
    {
        public void Add(ToolCall.Kind kind, double inSeconds)
        {
            entries.Add(ToolEntry(kind, SwiftDate.Now.AddSeconds(inSeconds)));
            bar.UpdateEntries([.. entries]);
        }

        public void Hover(int? stave)
        {
            window.MouseMove(stave is { } index ? new Point(index * 6 + 1, Top + 14) : new Point(299, 1));
            Dispatcher.UIThread.RunJobs();
        }

        public void ReduceMotion(bool on)
        {
            EnvironmentValues.SetAccessibilityReduceMotion(bar, on);
            Dispatcher.UIThread.RunJobs();
        }

        public void Theme(ThemeVariant theme)
        {
            window.RequestedThemeVariant = theme;
            Dispatcher.UIThread.RunJobs();
        }

        public void Status(HydraHeadInfo.Status status)
        {
            bar.UpdateTimes(startedAt, status == HydraHeadInfo.Status.Running ? null : finishedAt, status);
            Dispatcher.UIThread.RunJobs();
        }
    }

    private const double Top = 50; // room above the bar for its hover card

    /// <summary>
    /// A bar of five old staves and one arriving at the start, its card room above it, in a window of the light appearance:
    /// 1 s, then 19 steps of 0.173 s, with <paramref name="step"/> run before each; one SHA-256 over the compositor's frames,
    /// how many differ, and at each timestamp the kept picture checked against a new BarDrawing of the same inputs.
    /// </summary>
    private static (string Hash, int Distinct) Run(HydraHeadInfo.Status status, Action<int, Scene> step)
    {
        var frames = new ManualFrameSource();
        var time = new ManualTimeProvider();
        using var source = FrameClock.Source.Override(frames);
        using var clock = SwiftClock.Provider.Override(time);
        var entries = Entries(SwiftDate.Now).ToList();
        var startedAt = SwiftDate.Now.AddSeconds(-30);
        var finishedAt = SwiftDate.Now;
        var bar = Bar(status, [.. entries], status == HydraHeadInfo.Status.Running ? null : finishedAt, startedAt);
        var shown = Show(bar, ThemeVariant.Light);
        var scene = new Scene(bar, shown.Window, entries, startedAt, finishedAt);
        try
        {
            using var all = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var seen = new HashSet<string>();
            Tick(time, frames, 1.0);
            for (var k = 0; k < 20; k++)
            {
                step(k, scene);
                if (k > 0) Tick(time, frames, 0.173);
                Dispatcher.UIThread.RunJobs();
                var bytes = Capture(shown.Window);
                all.AppendData(bytes);
                seen.Add(Convert.ToHexString(SHA256.HashData(bytes)));
                KeptMatchesFresh(BarDrawing(bar), k);
            }
            return (Convert.ToHexString(all.GetHashAndReset()), seen.Count);
        }
        finally { shown.Window.Close(); Dispatcher.UIThread.RunJobs(); }
    }

    /// <summary>The bar's drawing as it is now against a new one handed the same inputs, drawn into bitmaps of its size.</summary>
    private static void KeptMatchesFresh(Control drawing, int k)
    {
        var type = drawing.GetType();
        object? Field(string name) => type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(drawing);
        var size = drawing.Bounds.Size;
        var fresh = (Control)Activator.CreateInstance(type, nonPublic: true)!;
        var scope = new ThemeVariantScope { RequestedThemeVariant = drawing.ActualThemeVariant, Child = fresh };
        scope.Measure(size);
        scope.Arrange(new Rect(size));
        type.GetMethod("Update")!.Invoke(fresh,
            [Field("staves"), Field("capacity"), Field("date"), Field("status"), Field("tint"), Field("hovered"), Field("rising"), Field("sweeping")]);
        Assert.Equal(size, fresh.Bounds.Size);
        Assert.True(Draw(drawing, size).SequenceEqual(Draw(fresh, size)), $"the kept picture is a new one's at timestamp {k}");
    }

    private static byte[] Draw(Control control, Size size)
    {
        var pixels = new PixelSize((int)Math.Ceiling(size.Width), (int)Math.Ceiling(size.Height));
        using var bitmap = new RenderTargetBitmap(pixels);
        using (var context = bitmap.CreateDrawingContext()) control.Render(context);
        var bytes = new byte[pixels.Width * pixels.Height * 4];
        var handle = System.Runtime.InteropServices.GCHandle.Alloc(bytes, System.Runtime.InteropServices.GCHandleType.Pinned);
        try { bitmap.CopyPixels(new PixelRect(pixels), handle.AddrOfPinnedObject(), bytes.Length, pixels.Width * 4); }
        finally { handle.Free(); }
        return bytes;
    }

    private static byte[] Capture(Avalonia.Controls.Window window)
    {
        using var frame = window.CaptureRenderedFrame() ?? throw new Xunit.Sdk.XunitException("no rendered frame");
        using var buffer = frame.Lock();
        var bytes = new byte[buffer.RowBytes * buffer.Size.Height];
        System.Runtime.InteropServices.Marshal.Copy(buffer.Address, bytes, 0, bytes.Length);
        return bytes;
    }

    private static (Avalonia.Controls.Window Window, HydraProgressBar Bar) Show(HydraProgressBar bar, ThemeVariant theme)
    {
        var window = new Avalonia.Controls.Window
        {
            Width = 300,
            Height = 130,
            RequestedThemeVariant = theme,
            Background = Brushes.Black,
            Content = new Border { Padding = new Thickness(0, Top, 0, 0), Child = bar },
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, bar);
    }

    private static void Tick(ManualTimeProvider time, ManualFrameSource frames, double seconds)
    {
        time.Advance(TimeSpan.FromSeconds(seconds));
        frames.Advance(TimeSpan.FromSeconds(seconds));
        Dispatcher.UIThread.RunJobs();
    }

    private static Control BarDrawing(HydraProgressBar bar) =>
        bar.GetVisualDescendants().OfType<Control>().Single(control => control.GetType().Name == "BarDrawing");

    private static TimelineEntry[] Entries(DateTimeOffset now) =>
    [
        ToolEntry(ToolCall.Kind.Read, now.AddSeconds(-20)),
        ToolEntry(ToolCall.Kind.Command, now.AddSeconds(-18)),
        ToolEntry(ToolCall.Kind.Edit, now.AddSeconds(-15)),
        ToolEntry(ToolCall.Kind.Read, now.AddSeconds(-9)),
        ToolEntry(ToolCall.Kind.Edit, now.AddSeconds(-4)),
        ToolEntry(ToolCall.Kind.Command, now),
    ];

    private static HydraProgressBar Bar(HydraHeadInfo.Status status, IReadOnlyList<TimelineEntry> entries, DateTimeOffset? finishedAt, DateTimeOffset? startedAt = null) =>
        new(EmptyRuntime(), startedAt ?? SwiftDate.Now.AddSeconds(-30), finishedAt, status, Tint, entries);

    private static TimelineEntry ToolEntry(ToolCall.Kind kind, DateTimeOffset date) =>
        new(new TimelineItem(
            turnID: null,
            content: new TimelineItem.Content.Tool(new ToolCall
            {
                ToolCallKind = kind,
                Title = $"{kind} at {date:HH:mm:ss.fff}",
                StartedAt = date,
            }),
            date: date));

    private static ThreadRuntime EmptyRuntime()
    {
        var runtime = (ThreadRuntime)RuntimeHelpers.GetUninitializedObject(typeof(ThreadRuntime));
        typeof(ThreadRuntime)
            .GetField("entries", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(runtime, ValueList<TimelineEntry>.Empty);
        return runtime;
    }
}
