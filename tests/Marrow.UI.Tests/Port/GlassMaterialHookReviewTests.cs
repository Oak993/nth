// Source: DroppyCode/UI/Chrome/Chrome.swift (B3 52bc792), :171-182 (the glass frosts the app content under it). Review of ED-320 18 (7d84e1448, 61e517395).
// REVIEW (round 4): the debug-events hook's compiled lambdas (GlassMaterial.Frame.ToRect, Frame.Adder) and the every-frame size read of Backdrop.Take. These pin:
// - the lambdas do what the reflection did, member for member (Avalonia 12.1.2's LtrbRect fields Left/Top/Right/Bottom, SingleDirtyRectTracker.AddRect): the
//   same rectangle read from a box (halves, negatives, a billion; an infinite rect is not compared: the old reflection threw OverflowException on it, the project being checked, the compiled lambda converts unchecked) and the same union in the tracker;
// - the one-time compile is safe when many threads reach it at once (statics reset before each round);
// - after the first frames of a window, in whatever build configuration the tests run (Debug and Release), the hook is installed AND live: the Reach holds
//   its tracker and its compiled call, knows the material, and a change under the glass goes through the compiled call (it is counted); two windows each have one;
// - a call that is refused (MemberAccessException, InvalidCastException) switches the Reach off for good, the frame going on; it is not logged anywhere;
// - the window surface's size read every frame copies nothing and allocates the same few bytes on a small and a very large surface.
using System.Diagnostics;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;
using SkiaSharp;
using Xunit;
using Xunit.Abstractions;
using Scene = Marrow.UI.Tests.Fixtures.GlassScene;

namespace Marrow.UI.Tests.Port;

[Collection("Headless")]
public sealed class GlassMaterialHookReviewTests(HeadlessSession headless, ITestOutputHelper output)
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly Assembly Base = typeof(Avalonia.Rendering.Composition.CompositionCustomVisualHandler).Assembly;
    private static readonly Type Ltrb = Base.GetType("Avalonia.Platform.LtrbRect", true)!;
    private static readonly Type Tracker = Base.GetType("Avalonia.Rendering.Composition.Server.SingleDirtyRectTracker", true)!;
    private static readonly Type Frame = typeof(GlassMaterial).GetNestedType("Frame", BindingFlags.NonPublic)!;

    private static object Box(double left, double top, double right, double bottom)
    {
        var box = Activator.CreateInstance(Ltrb)!;
        Ltrb.GetField("Left")!.SetValue(box, left);
        Ltrb.GetField("Top")!.SetValue(box, top);
        Ltrb.GetField("Right")!.SetValue(box, right);
        Ltrb.GetField("Bottom")!.SetValue(box, bottom);
        return box;
    }

    private static (double L, double T, double R, double B) Fields(object box) =>
        ((double)Ltrb.GetField("Left")!.GetValue(box)!, (double)Ltrb.GetField("Top")!.GetValue(box)!, (double)Ltrb.GetField("Right")!.GetValue(box)!, (double)Ltrb.GetField("Bottom")!.GetValue(box)!);

    // The reflection the lambdas replaced (61e517395's removed code), kept here as the oracle.
    private static SKRectI OldToRect(object rect)
    {
        var type = rect.GetType();
        FieldInfo l = type.GetField("Left")!, t = type.GetField("Top")!, r = type.GetField("Right")!, b = type.GetField("Bottom")!;
        return new SKRectI((int)Math.Round((double)l.GetValue(rect)!), (int)Math.Round((double)t.GetValue(rect)!),
            (int)Math.Round((double)r.GetValue(rect)!), (int)Math.Round((double)b.GetValue(rect)!));
    }

    private static object OldToLtrb(Type type, SKRectI rect)
    {
        var boxed = Activator.CreateInstance(type)!;
        type.GetField("Left")!.SetValue(boxed, (double)rect.Left);
        type.GetField("Top")!.SetValue(boxed, (double)rect.Top);
        type.GetField("Right")!.SetValue(boxed, (double)rect.Right);
        type.GetField("Bottom")!.SetValue(boxed, (double)rect.Bottom);
        return boxed;
    }

    private static SKRectI ToRect(object box) => (SKRectI)Frame.GetMethod("ToRect", Members)!.Invoke(null, [box])!;

    private static Action<object, SKRectI> Adder(object tracker) =>
        (Action<object, SKRectI>)Frame.GetMethod("Adder", Members)!.Invoke(null, [tracker.GetType(), tracker.GetType().GetMethod("AddRect", Members)!])!;

    private static void ResetReader()
    {
        Frame.GetField("reader", Members)!.SetValue(null, null);
        Frame.GetField("readerType", Members)!.SetValue(null, null);
    }

    public static TheoryData<double, double, double, double> Rects() => new()
    {
        { 0, 0, 10, 10 }, { 0.5, 1.5, 2.5, 3.5 }, { -0.5, -1.5, -2.5, -3.5 }, { -5, 3, 7, 9 }, { 99.49, 199.51, 300.5, 400.4999 },
        { 0.1, 0.9, 1e6, 1e7 }, { -2e9, -1e9, 1e9, 2e9 }, { 3, 4, 3, 4 },
    };

    [Fact]
    [Trait("Req", "PR-WIN-180")]
    public void REVIEW_The_lambdas_target_the_members_the_reflection_did()
    {
        // The members by name and type: the four public double fields, no others, in the struct the tracker's AddRect takes; the debug events' RectInvalidated takes it too.
        var fields = Ltrb.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.Equal(["Bottom", "Left", "Right", "Top"], fields.Select(f => f.Name).Order());
        Assert.All(fields, f => Assert.Equal(typeof(double), f.FieldType));
        Assert.True(Ltrb.IsValueType);
        var add = Tracker.GetMethods(Members).Where(m => m.Name == "AddRect").ToArray();
        var one = Assert.Single(add);
        Assert.Equal(Ltrb, Assert.Single(one.GetParameters()).ParameterType);
        var events = Base.GetType("Avalonia.Rendering.Composition.ICompositionTargetDebugEvents", true)!;
        Assert.Equal(Ltrb, Assert.Single(events.GetMethod("RectInvalidated")!.GetParameters()).ParameterType);
        Assert.Equal("SingleDirtyRectTracker", Tracker.Name);
    }

    [Theory]
    [MemberData(nameof(Rects))]
    [Trait("Req", "PR-WIN-180")]
    public void REVIEW_The_compiled_reader_reads_what_the_reflection_read(double l, double t, double r, double b)
    {
        var box = Box(l, t, r, b);
        Assert.Equal(OldToRect(box), ToRect(box));
        Assert.Equal((l, t, r, b), Fields(box)); // reading changes nothing
    }

    [Fact]
    [Trait("Req", "PR-WIN-180")]
    public void REVIEW_The_compiled_adder_adds_what_the_reflection_added()
    {
        var union = Tracker.GetField("_rect", Members)!;
        var reflected = Activator.CreateInstance(Tracker)!;
        var compiled = Activator.CreateInstance(Tracker)!;
        var add = Adder(compiled);
        var addRect = Tracker.GetMethod("AddRect", Members)!;
        SKRectI[] rects = [new(0, 0, 10, 10), new(-5, 3, 7, 9), new(100, 200, 300, 400), new(2, 2, 2, 2), new(-1000000, -3, 5, 1000000)];
        foreach (var rect in rects)
        {
            addRect.Invoke(reflected, [OldToLtrb(Ltrb, rect)]);
            add(compiled, rect);
            var (a, b) = (union.GetValue(reflected), union.GetValue(compiled));
            Assert.NotNull(a);
            Assert.NotNull(b);
            Assert.Equal(Fields(a!), Fields(b!));
        }
        // And it is one call on one object: a tracker of another type is refused, not silently ignored.
        Assert.Throws<InvalidCastException>(() => add(new object(), rects[0]));
    }

    [Fact]
    [Trait("Req", "PR-WIN-180")]
    public void REVIEW_The_one_time_compile_is_safe_from_many_threads_at_once()
    {
        var rects = Enumerable.Range(0, 64).Select(i => Box(i * 1.5, i * 2.5 + 0.5, i * 3.5 + 100.5, i * 4.5 + 200)).ToArray();
        var expected = rects.Select(OldToRect).ToArray();
        var failures = new System.Collections.Concurrent.ConcurrentBag<string>();
        for (var round = 0; round < 150; round++)
        {
            ResetReader();
            var trackers = Enumerable.Range(0, 16).Select(_ => Activator.CreateInstance(Tracker)!).ToArray();
            using var gate = new Barrier(16);
            var threads = Enumerable.Range(0, 16).Select(n => new Thread(() =>
            {
                try
                {
                    gate.SignalAndWait();
                    for (var i = 0; i < rects.Length; i++)
                    {
                        var got = ToRect(rects[(i + n) % rects.Length]);
                        if (got != expected[(i + n) % rects.Length]) failures.Add($"round {round}: thread {n} read {got}, not {expected[(i + n) % rects.Length]}");
                    }
                    Adder(trackers[n])(trackers[n], new SKRectI(1, 2, 3, 4));
                }
                catch (Exception e) { failures.Add($"round {round}: thread {n}: {e.GetType().Name}: {e.Message}"); }
            })).ToArray();
            foreach (var t in threads) t.Start();
            foreach (var t in threads) t.Join();
        }
        Assert.True(failures.IsEmpty, string.Join("\n", failures.Take(5)));
    }

    private static object Server(Window window)
    {
        var renderer = typeof(TopLevel).GetProperty("Renderer", Members)!.GetValue(window)!;
        var target = renderer.GetType().GetProperty("CompositionTarget", Members)!.GetValue(renderer)!;
        return target.GetType().GetProperties(Members).First(p => p.Name == "Server" && p.DeclaringType == target.GetType()).GetValue(target)!;
    }

    private static GlassMaterial.Reach ReachOf(Window window) =>
        Assert.IsAssignableFrom<GlassMaterial.Reach>(Server(window).GetType().GetProperty("DebugEvents", Members)!.GetValue(Server(window)));

    private static T Private<T>(object owner, string name) => (T)typeof(GlassMaterial.Reach).GetField(name, Members)!.GetValue(owner)!;

    private static Panel Backdrop(Border block) => new() { Background = Brushes.Gray, Children = { block } };

    private static Border Piece()
    {
        var piece = new Border { Background = Brushes.Transparent, Width = 160, Height = 120, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        SwiftGlass.SetEffect(piece, GlassEffect.Regular(GlassShape.RoundedRectangle(16, RoundedCornerStyle.Continuous)));
        return piece;
    }

    private static Border Block(double x, double y) => new() { Width = 20, Height = 16, Background = Brushes.Black, HorizontalAlignment = HorizontalAlignment.Left,
        VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(x, y, 0, 0) };

    // The compiled call is wrapped by a counter (the Reach's own field), so the test sees every rect the hook adds to the frame.
    private static int[] Count(GlassMaterial.Reach reach)
    {
        var calls = new[] { 0 };
        var inner = Private<Action<object, SKRectI>>(reach, "addRect");
        typeof(GlassMaterial.Reach).GetField("addRect", Members)!.SetValue(reach, new Action<object, SKRectI>((t, r) => { calls[0]++; inner(t, r); }));
        return calls;
    }

    [Fact]
    [Trait("Req", "PR-WIN-180")]
    public void REVIEW_After_the_first_frames_the_hook_is_installed_and_live_in_this_build() => headless.OnUiThread(() =>
    {
        var block = Block(95, 100);
        using var scene = new Scene(Backdrop(block), Piece(), 400, 300);
        _ = scene.Pixels();
        var reach = ReachOf(scene.Window);
        Assert.NotNull(Private<object>(reach, "tracker"));
        Assert.NotNull(Private<Action<object, SKRectI>>(reach, "addRect"));
        Assert.True(Private<System.Collections.IDictionary>(reach, "materials").Count >= 1, "the material is not known to the Reach after its first draws");
        var calls = Count(reach);
        block.Background = Brushes.White; // a change near the glass's edge, drawn by itself
        _ = scene.Pixels();
        Assert.True(calls[0] > 0, "a change under the glass did not go through the compiled call");
        Assert.NotNull(Private<object>(reach, "tracker")); // and it did not switch itself off
        output.WriteLine($"{(Debugger.IsAttached ? "attached" : "")} {(typeof(GlassMaterial).Assembly.GetCustomAttribute<DebuggableAttribute>()?.IsJITOptimizerDisabled == true ? "Debug" : "Release")} build: {calls[0]} rect(s) added");
    });

    [Fact]
    [Trait("Req", "PR-WIN-180")]
    public void REVIEW_Two_windows_each_have_a_live_hook_of_their_own() => headless.OnUiThread(() =>
    {
        var (one, two) = (Block(95, 100), Block(90, 105));
        using var a = new Scene(Backdrop(one), Piece(), 400, 300);
        using var b = new Scene(Backdrop(two), Piece(), 400, 300);
        _ = a.Pixels();
        _ = b.Pixels();
        var (ra, rb) = (ReachOf(a.Window), ReachOf(b.Window));
        Assert.NotSame(ra, rb);
        var (ca, cb) = (Count(ra), Count(rb));
        one.Background = Brushes.White;
        two.Background = Brushes.White;
        _ = a.Pixels();
        _ = b.Pixels();
        Assert.True(ca[0] > 0 && cb[0] > 0, $"rects added: window A {ca[0]}, window B {cb[0]}");
        Assert.NotNull(Private<object>(ra, "tracker"));
        Assert.NotNull(Private<object>(rb, "tracker"));
    });

    // Drives a Reach on its own, as ReachOf makes it, with a call that is refused.
    private static (GlassMaterial.Reach Reach, MethodInfo Invalidate, object Material) Standalone(Action<object, SKRectI> add)
    {
        var events = Base.GetType("Avalonia.Rendering.Composition.ICompositionTargetDebugEvents", true)!;
        var reach = (GlassMaterial.Reach)DispatchProxy.Create(events, typeof(GlassMaterial.Reach));
        reach.Bind(new object(), add);
        var material = new object();
        reach.Set(material, new GlassMaterial.Spread(new SKRectI(0, 0, 500, 500), 40, default, 36, 28));
        return (reach, events.GetMethod("RectInvalidated")!, material);
    }

    [Theory]
    [InlineData(typeof(MemberAccessException))]
    [InlineData(typeof(InvalidCastException))]
    [Trait("Req", "PR-WIN-180")]
    public void REVIEW_A_refused_call_switches_the_reach_off_and_the_frame_goes_on(Type refusal)
    {
        var calls = 0;
        var (reach, invalidate, _) = Standalone((_, _) => { calls++; throw (Exception)Activator.CreateInstance(refusal)!; });
        invalidate.Invoke(reach, [Box(100, 100, 110, 110)]); // must not throw into the render thread
        Assert.Equal(1, calls);
        Assert.Null(Private<object>(reach, "tracker"));
        invalidate.Invoke(reach, [Box(100, 100, 110, 110)]);
        Assert.Equal(1, calls); // off for good: no second attempt, no second failure
    }

    // The catch around Grow names the two refusals of a bad call (MemberAccessException, InvalidCastException). The reader is compiled at the first rect, inside
    // that same Grow: if it cannot be built (a struct without the four fields: ArgumentException from Expression.Field), the exception is not caught there and
    // goes through the DispatchProxy into Avalonia's update on the render thread, instead of the Reach switching itself off. FAILS at ff3db9d09.
    [Fact]
    [Trait("Req", "PR-WIN-180")]
    public void REVIEW_A_rect_the_reader_cannot_be_compiled_for_switches_the_reach_off_too()
    {
        var (reach, invalidate, _) = Standalone((_, _) => { });
        try
        {
            var thrown = Record.Exception(() => typeof(GlassMaterial.Reach).GetMethod("Invoke", Members)!.Invoke(reach, [invalidate, new object[] { new SKRectI(1, 2, 3, 4) }])); // a boxed struct without the four fields
            Assert.True(thrown is null, $"{thrown?.GetType().Name} / {thrown?.InnerException?.GetType().Name} reached the caller: {thrown?.InnerException?.Message ?? thrown?.Message}");
            Assert.Null(Private<object>(reach, "tracker"));
        }
        finally { ResetReader(); }
    }

    // A live reach: a change's rect is grown by the material's reach (Blur + Bend, 64) inside the material's output, once.
    [Fact]
    [Trait("Req", "PR-WIN-180")]
    public void REVIEW_A_live_reach_adds_the_materials_reach_once_per_changed_rect()
    {
        var added = new List<SKRectI>();
        var (reach, invalidate, _) = Standalone((_, r) => added.Add(r));
        invalidate.Invoke(reach, [Box(100, 100, 110, 110)]);
        var one = Assert.Single(added);
        Assert.True(new SKRectI(100 - 64, 100 - 64, 110 + 64, 110 + 64) == one, $"added {one}");
        Assert.NotNull(Private<object>(reach, "tracker"));
    }

    // Take measures the surface every frame (7d84e1448). Skia copies the pixels of a surface when a snapshot of it is still alive as the surface is drawn
    // to; the probe is released at once, so a frame's drawing copies nothing. Measured on a small and on a very large software surface, in steady state: one
    // frame is a draw of a 40 x 16 wash into the surface and Take with that rect dirty. The bytes managed code allocates per frame must not depend on the
    // surface's size, and the time per frame must not either (a copy of the 4096 x 2304 surface would be 37 MB).
    [Fact]
    [Trait("Req", "PR-WIN-180")]
    public void REVIEW_The_every_frame_size_read_copies_nothing_and_allocates_the_same_on_any_size() => headless.OnUiThread(() =>
    {
        var type = typeof(GlassMaterial).GetNestedType("Backdrop", BindingFlags.NonPublic)!;
        var take = type.GetMethod("Take", Members)!;
        var results = new List<(int W, int H, double Bytes, double Micro, double Probe, double ProbeMicro)>();
        foreach (var (w, h) in new[] { (600, 400), (1200, 800), (4096, 2304), (7680, 4320) })
        {
            var backdrop = Activator.CreateInstance(type, nonPublic: true)!;
            using var window = SKSurface.Create(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Premul));
            var lease = new GlassFakeLease(window);
            var crop = new SKRectI(50, 50, w - 50, h - 50);
            using var wash = new SKPaint { Color = new SKColor(255, 255, 255, 40) };
            var dirty = (SKRectI?)new SKRectI(200, 200, 240, 216);
            void Frame()
            {
                window.Canvas.DrawRect(200, 200, 40, 16, wash);
                Assert.NotNull(take.Invoke(backdrop, [lease, crop, dirty]));
            }
            Frame(); // the whole take
            Frame();
            for (var i = 0; i < 30; i++) Frame();
            GC.Collect();
            const int frames = 200;
            var args = new object?[] { lease, crop, dirty };
            long before = GC.GetAllocatedBytesForCurrentThread();
            var clock = Stopwatch.StartNew();
            for (var i = 0; i < frames; i++) { window.Canvas.DrawRect(200, 200, 40, 16, wash); take.Invoke(backdrop, args); }
            clock.Stop();
            var bytes = (GC.GetAllocatedBytesForCurrentThread() - before) / (double)frames;
            // The probe alone (what 7d84e1448 added to every frame), then a draw: the draw would copy the surface if the probe were alive.
            before = GC.GetAllocatedBytesForCurrentThread();
            clock.Restart();
            for (var i = 0; i < frames; i++) { using (var probe = window.Snapshot()) _ = probe.Width; window.Canvas.DrawRect(200, 200, 40, 16, wash); }
            clock.Stop();
            results.Add((w, h, bytes, 0, (GC.GetAllocatedBytesForCurrentThread() - before) / (double)frames, clock.Elapsed.TotalMilliseconds * 1000 / frames));
            // And the whole take with timing alone, apart from the allocation count (the reflection invoke boxes its arguments: the figure includes that).
            clock.Restart();
            for (var i = 0; i < frames; i++) { window.Canvas.DrawRect(200, 200, 40, 16, wash); take.Invoke(backdrop, args); }
            clock.Stop();
            results[^1] = results[^1] with { Micro = clock.Elapsed.TotalMilliseconds * 1000 / frames };
            type.GetMethod("Release")!.Invoke(backdrop, null);
            type.GetMethod("DisposeReleased")!.Invoke(null, null);
        }
        foreach (var r in results) output.WriteLine($"{r.W} x {r.H}: Take {r.Bytes:F0} B and {r.Micro:F0} us per frame; probe + draw alone {r.Probe:F0} B and {r.ProbeMicro:F0} us");
        var small = results[0];
        foreach (var r in results)
        {
            Assert.True(Math.Abs(r.Bytes - small.Bytes) <= 64, $"{r.W} x {r.H}: {r.Bytes:F0} B per frame against {small.Bytes:F0} B at {small.W} x {small.H}: the allocation depends on the surface's size");
            Assert.True(Math.Abs(r.Probe - small.Probe) <= 16, $"{r.W} x {r.H}: the probe allocates {r.Probe:F0} B against {small.Probe:F0}");
            Assert.True(r.ProbeMicro < 500, $"{r.W} x {r.H}: probe and a wash took {r.ProbeMicro:F0} us a frame: the surface is copied");
        }
    });
}
