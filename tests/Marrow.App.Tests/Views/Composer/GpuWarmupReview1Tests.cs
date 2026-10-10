using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Avalonia.Styling;
using Avalonia.Threading;
using Marrow.App.Port.FirstRun;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Testing;
using Marrow.UI.Port;
using SkiaSharp;

namespace Marrow.App.Tests.Views.Composer;

// GR-1 review round 1, the fixes. (1) The slider liquid and the strike compile on the window's GPU context: Avalonia 12.1.2's
// RenderTargetBitmap is always a CPU raster (Avalonia.Skia 12.1.2, decompiled: PlatformRenderInterface.CreateRenderTargetBitmap
// returns RenderTargetBitmapImpl, a WriteableBitmapImpl drawn through a FramebufferRenderTarget, with no GRContext), so LiquidTrack
// (which makes its surfaces on lease.GrContext when it has one) never compiled a shader on it. They draw into a surface the
// context makes, through a DrawingContextImpl that carries the context. (3) A first run that joins an idle warm-up still runs the
// work steps. The GPU itself cannot be made here: a stand-in context object carries the wiring, the pixels are the CPU's.
[Collection("Headless")]
public sealed class GpuWarmupReview1Tests(HeadlessSession headless)
{
    private static GRContext StandIn()
    {
        var gpu = (GRContext)RuntimeHelpers.GetUninitializedObject(typeof(GRContext)); // never used by a GPU: only handed on
        GC.SuppressFinalize(gpu);
        return gpu;
    }

    // GR-1 review round 2: the stand-in is a GRContext with no native object (Handle 0). xunit formats both sides of a failed
    // Assert.Same or Assert.Equal by reading their properties, and GRContext.Backend on it is an access violation: the test host
    // crashed (0xC0000005) and every later test's result was lost, with the line still saying Passed. So no assertion is handed the
    // stand-in: identity is asserted as a boolean with its message.
    private static void IsTheStandIn(GRContext expected, GRContext? actual, string what) =>
        Assert.True(ReferenceEquals(expected, actual), what + (actual is null ? ": it was null" : ": it was another context"));

    private static bool Painted(SKSurface surface)
    {
        using var image = surface.Snapshot();
        using var bitmap = SKBitmap.FromImage(image);
        for (var y = 0; y < bitmap.Height; y++)
            for (var x = 0; x < bitmap.Width; x++)
                if (bitmap.GetPixel(x, y).Alpha > 0) return true;
        return false;
    }

    [Fact] // GR-1.programs
    [Trait("Decision", "ED-252")]
    public void The_pinned_Avalonia_has_the_types_a_target_is_made_of() =>
        Assert.True(GpuWarmup.WarmTarget.Available, "Avalonia.Skia's DrawingContextImpl.CreateInfo or Avalonia.Base's PlatformDrawingContext changed: the liquid and strike warm-up cannot make their target");

    [Fact] // GR-1.programs
    [Trait("Decision", "ED-252")]
    public void The_target_a_program_draws_into_carries_the_gpu_context_its_surface_and_the_scaling() => headless.OnUiThread(() =>
    {
        var gpu = StandIn();
        var asked = new List<(GRContext Gpu, SKImageInfo Info)>();
        var made = new List<SKSurface>();
        var factory = GpuWarmup.WarmTarget.GpuSurface;
        GpuWarmup.WarmTarget.GpuSurface = (g, info) => { asked.Add((g, info)); var s = SKSurface.Create(info); made.Add(s); return s; };
        try
        {
            var info = new SKImageInfo(320, 80, SKColorType.Rgba8888, SKAlphaType.Premul);
            GRContext? leasedGpu = null;
            SKSurface? leasedSurface = null;
            var scale = 0f;
            using (var target = GpuWarmup.WarmTarget.Open(gpu, info, 2))
            {
                target.Context.Custom(new ReadingDrawOperation(context =>
                {
                    using var lease = context.TryGetFeature<ISkiaSharpApiLeaseFeature>()!.Lease();
                    (leasedGpu, leasedSurface, scale) = (lease.GrContext, lease.SkSurface, lease.SkCanvas.TotalMatrix.ScaleX);
                }));
                Assert.Same(made[0], target.Surface);
                Assert.Same(target.Surface, leasedSurface);
            }
            Assert.True(asked.Count == 1, "the GPU context was asked for " + asked.Count + " surfaces"); // the context's own surface, the size given (not Assert.Single: it would format the stand-in)
            IsTheStandIn(gpu, asked[0].Gpu, "the surface was asked of another context");
            Assert.Equal(info, asked[0].Info);
            IsTheStandIn(gpu, leasedGpu, "the lease did not carry the GPU context");     // the lease says GPU: LiquidTrack makes its surfaces on it
            Assert.Equal(2f, scale);              // and the scaling LiquidTrack reads off the canvas
        }
        finally { GpuWarmup.WarmTarget.GpuSurface = factory; }
    });

    [Fact] // GR-1.programs
    [Trait("Decision", "ED-252")]
    public void A_gpu_context_that_makes_no_surface_fails_the_target_it_never_falls_back_to_the_cpu() => headless.OnUiThread(() =>
    {
        var factory = GpuWarmup.WarmTarget.GpuSurface;
        GpuWarmup.WarmTarget.GpuSurface = (_, _) => null;
        try
        {
            var error = Assert.Throws<InvalidOperationException>(() => GpuWarmup.WarmTarget.Open(StandIn(), new SKImageInfo(16, 16, SKColorType.Rgba8888, SKAlphaType.Premul), 1));
            Assert.Contains("GPU", error.Message, StringComparison.Ordinal);
        }
        finally { GpuWarmup.WarmTarget.GpuSurface = factory; }
    });

    [Theory]
    [InlineData(1.0)]
    [InlineData(2.0)]
    [Trait("Decision", "ED-252")]
    public void The_liquid_draws_through_its_own_entry_point_into_the_target_at_the_scaling(double scale) => headless.OnUiThread(() =>
    {
        var frame = GpuWarmup.LiquidFrame(strike: true, scale);
        var info = new SKImageInfo((int)Math.Ceiling(frame.Size.Width * scale), (int)Math.Ceiling(frame.Size.Height * scale), SKColorType.Rgba8888, SKAlphaType.Premul);
        using var target = GpuWarmup.WarmTarget.Open(null, info, scale);
        LiquidTrack.Draw(target.Context, frame.Size, frame.Track, frame.Time, frame.Spec, frame.Gradient, frame.Bubbles, false, ThemeVariant.Dark,
            frame.Flow, frame.Strike, frame.BubbleInk);
        Assert.True(Painted(target.Surface), "nothing reached the target");
        using var image = target.Surface.Snapshot();
        using var bitmap = SKBitmap.FromImage(image);
        // the tube fills the box at the scaling: a pixel near the far end of the scaled width is painted (at 1x it is the box's own end)
        Assert.True(bitmap.GetPixel(info.Width * 3 / 4, info.Height / 2).Alpha > 0);
    });

    [Theory]
    [InlineData("liquid")]
    [InlineData("strike")]
    [Trait("Decision", "ED-252")]
    public void The_liquid_and_strike_programs_draw_into_a_surface_of_the_gpu_context_not_a_bitmap(string name) => headless.OnUiThread(() =>
    {
        var gpu = StandIn();
        var asked = new List<SKImageInfo>();
        var painted = new List<bool>();
        var factory = GpuWarmup.WarmTarget.GpuSurface;
        var inspect = GpuWarmup.WarmTarget.Inspect;
        var sameContext = true;
        GpuWarmup.WarmTarget.GpuSurface = (g, info) => { sameContext &= ReferenceEquals(gpu, g); asked.Add(info); return SKSurface.Create(info); };
        GpuWarmup.WarmTarget.Inspect = surface => painted.Add(Painted(surface));
        try
        {
            // the family's parts drawn through LiquidTrack.Draw (ED-NEW-warmupsplit-1: one GPU draw each; the strike's legacy frame)
            var parts = GpuWarmup.Registered.Where(p => p.Family == name && (name != "strike" || p.Name.StartsWith("strike[legacy ", StringComparison.Ordinal))).ToArray();
            Assert.Equal(name == "strike" ? StrikeWarmup.LegacySites : GpuWarmup.LiquidSites, parts.Length);
            foreach (var program in parts) program.Drawing(new GpuWarmContext(gpu, 1));
            Assert.Equal(parts.Length, asked.Count);                  // the context made the surface each part drew into
            Assert.True(sameContext, "the surface was asked of another context");
            Assert.All(asked, info => Assert.Equal((160, (int)EffortSlider.TrackHeight), (info.Width, info.Height)));
            Assert.Equal(parts.Length, painted.Count);
            Assert.Contains(true, painted);                           // and the frame was drawn into them
            asked.Clear(); painted.Clear();
            GpuWarmup.DrawLiquid(new GpuWarmContext(gpu, 1), strike: name == "strike"); // the frame whole, in one part
            Assert.Single(asked);
            Assert.Equal([true], painted);                            // drew into the surface the context made
        }
        finally { GpuWarmup.WarmTarget.GpuSurface = factory; GpuWarmup.WarmTarget.Inspect = inspect; }
    });

    [Fact] // GR-1.registry
    [Trait("Decision", "ED-252")]
    public void A_first_run_joining_an_idle_warm_up_still_runs_the_work_step_and_reports_every_step() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        using var bound = FrameClock.Source.Override(frames);
        var cap = GpuWarmup.IdleCap;
        GpuWarmup.IdleCap = TimeSpan.FromSeconds(60);
        GpuWarmup.PretendGpu = true;
        var worked = 0;
        using var work = GpuWarmup.Register(GpuProgram.Work("voice-hardware", FirstRunSteps.Voice, _ => { worked++; return Task.CompletedTask; }));
        var window = new Avalonia.Controls.Window { Width = 200, Height = 100 };
        window.Show();
        try
        {
            frames.RequestFrame(_ => { }); // animating throughout: the idle fallback waits
            GpuWarmup.WhenIdle(window);    // the window's own idle run, begun and waiting
            FramesFor(TimeSpan.FromMilliseconds(500), () => false);
            Assert.Equal(0, worked);
            var reports = new List<GpuWarmupStep>();
            var task = GpuWarmup.WarmAsync(window, new SyncProgress<GpuWarmupStep>(reports.Add));
            FramesFor(TimeSpan.FromSeconds(30), () => task.IsCompleted);
            Assert.True(task.IsCompletedSuccessfully, task.Exception?.InnerException?.Message);
            Assert.Equal(1, worked);                                                         // the idle run never runs it: the first run does
            Assert.Equal(GpuWarmup.Registered.Select(p => p.Name), reports.Select(r => r.Program)); // every step, in its place
        }
        finally { GpuWarmup.IdleCap = cap; GpuWarmup.PretendGpu = false; window.Close(); }
    });

    private static void FramesFor(TimeSpan span, Func<bool> done)
    {
        var since = DateTime.UtcNow;
        while (!done() && DateTime.UtcNow - since < span)
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(10);
            Dispatcher.UIThread.RunJobs();
        }
    }
}
