using System.Reflection;
using Avalonia;
using Avalonia.OpenGL;
using Avalonia.Platform;
using Avalonia.Skia;
using Marrow.UI.Port;
using SkiaSharp;

namespace Marrow.App.Views.Composer;

// Avalonia 12.1.2's D3D11 factory creates a new EGL display/device for each context. A shared context
// holds the compositor's display lock, so it cannot be used here. No GPU objects cross these displays;
// only ANGLE's program binaries do. The usual compositor pass still proves each real program afterward.
internal static class GpuBootPreparation
{
    internal static Task Run(GpuProgram[] programs, double scaling, CancellationToken cancellation)
    {
        var locator = typeof(AvaloniaLocator).GetProperty("Current", BindingFlags.Public | BindingFlags.Static)?.GetValue(null)
            ?? throw new InvalidOperationException("Boot warm-up: Avalonia's service locator API changed.");
        var getService = locator.GetType().GetMethod("GetService", [typeof(Type)])
            ?? throw new InvalidOperationException("Boot warm-up: Avalonia's service resolver API changed.");
        var factory = getService.Invoke(locator, [typeof(IPlatformGraphicsOpenGlContextFactory)]) as IPlatformGraphicsOpenGlContextFactory;
        var renderer = getService.Invoke(locator, [typeof(IPlatformRenderInterface)]) as IPlatformRenderInterface;
        if (factory?.GetType().FullName != "Avalonia.Win32.OpenGl.Angle.D3D11AngleWin32PlatformGraphics" || renderer is null || GpuProgramCache.Root() is null)
            return Task.CompletedTask; // retain the existing GPU/software path on unsupported backends
        return Task.Factory.StartNew(() => Prepare(factory, renderer, programs, scaling, cancellation), cancellation,
            TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }

    private static void Prepare(IPlatformGraphicsOpenGlContextFactory factory, IPlatformRenderInterface renderer,
        GpuProgram[] programs, double scaling, CancellationToken cancellation)
    {
        using var work = AuroraBootTrace.Begin("Boot: independent GPU preparation");
        using var context = factory.CreateContext(null);
        using (context.MakeCurrent())
        {
            // PrivateApi is omitted from Avalonia's reference assembly. The pinned implementation constructs
            // its Skia backend with the same options as the window's backend; fail visibly if its shape changes.
            using var backend = (IDisposable)(renderer.GetType().GetMethod("CreateBackendContext")?.Invoke(renderer, [context])
                ?? throw new InvalidOperationException("Boot warm-up: Avalonia's Skia backend API changed."));
            var skiaGpu = backend.GetType().GetField("_gpu", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(backend);
            var gpu = skiaGpu?.GetType().GetProperty("GrContext")?.GetValue(skiaGpu) as GRContext
                ?? throw new InvalidOperationException("Boot warm-up: the independent Skia context is not a GPU context.");
            try
            {
                var binding = GpuProgramCache.ForContext(gpu);
                if (binding.State is { } reason) throw new InvalidOperationException("Boot warm-up: " + reason);
                var cached = binding.Folder ?? throw new InvalidOperationException("Boot warm-up: no program cache folder.");
                foreach (var program in programs.Where(p => !p.IsWork))
                {
                    cancellation.ThrowIfCancellationRequested();
                    using var step = AuroraBootTrace.Begin("worker: " + program.Name);
                    program.Drawing(new GpuWarmContext(gpu, scaling));
                    using (AuroraBootTrace.Begin("worker flush: " + program.Name)) gpu.Flush();
                }
                GpuProgramCache.Drain(cached).GetAwaiter().GetResult();
            }
            finally { JewelKnobRenderer.Retire(gpu); GpuProgramCache.Retire(gpu); }
        } // MakeCurrent's thread-affine scope and all resources end on this dedicated worker thread
    }
}
