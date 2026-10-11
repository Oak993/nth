// PORT-ONLY, owner-ruled addition (OD-0139 Composer Light v2; ED-NEW-lightv2-3): the shader lights' GPU warm-up. A shader light
// is one SkSL program; on ANGLE a program compiles on its first draw, which would hitch the frame the light first shows in. The
// lights are not in the warm-up's registry (that would warm all six at every first run and change the cache's family count):
// only the light that is used is warmed, when it is used. At launch that is the selected light; later, the one picked or shown
// in the Customization preview. ShaderLightGate holds a light view on what it showed until the light it is asked to show is warmed
// (GpuWarmup.WarmWhenIdle: idle-gated, the 300 ms input hold and IdleCap intact, compiled on the worker's own context first), so
// the first frame of a new light finds its program made. With no GPU (the CPU renderer, headless tests) there is nothing to
// compile and nothing is held.
using Avalonia.Controls;
using Avalonia.Threading;
using Marrow.App.Port.FirstRun;
using Marrow.Core.Models.ComposerLights;
using Marrow.UI.Port.ComposerLights;
using SkiaSharp;

namespace Marrow.App.Views.Composer;

/// <summary>The shader lights' warm-up programs (not registered: warmed on demand).</summary>
internal static class ShaderLightWarmup
{
    private static readonly Dictionary<string, GpuProgram> programs = [];
    private static readonly HashSet<string> warmed = [];

    /// <summary>The warm-up program of light <paramref name="id"/>: one frame of it, offscreen, through its own renderer.</summary>
    internal static GpuProgram Program(string id)
    {
        lock (programs)
        {
            if (!programs.TryGetValue(id, out var program))
                programs[id] = program = GpuProgram.Draw("light-shader-" + id, FirstRunSteps.Lights, context => Draw(context, id));
            return program;
        }
    }

    /// <summary>A frame of light <paramref name="id"/> at rest into a small surface on the context (its program compiles there).</summary>
    internal static void Draw(GpuWarmContext context, string id)
    {
        var settings = ComposerLightSettings.Default with { Style = ComposerLightStyle.Shader, ShaderId = id };
        var light = new ComposerLightAnimator(settings, ComposerLightPhase.Rest, dark: true, 472, 46, reduceMotion: true);
        using var renderer = new ShaderLightRenderer();
        var info = new SKImageInfo(16, 16, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = (context.Gpu is { } gpu ? SKSurface.Create(gpu, false, info) : null) ?? SKSurface.Create(info);
        // both of its programs: drawn with Screen (no surface to read), and composing with the window's pixels (Text and light)
        for (var i = 0; i < light.Shaders.DrawnCount; i++)
        {
            renderer.Draw(surface.Canvas, light.Shaders.Drawn[i], (float)Math.Max(1, context.Scaling));
            renderer.Draw(surface.Canvas, light.Shaders.Drawn[i], (float)Math.Max(1, context.Scaling), 1, surface);
        }
    }

    /// <summary>True when the platform draws on a GPU, so a new program would compile on its first draw.</summary>
    internal static bool Needed => GpuWarmup.PlatformDrawsOnGpu() == true || GpuWarmup.PretendGpu;

    /// <summary>True once light <paramref name="id"/> has been warmed in this process (or needs no warming).</summary>
    internal static bool IsWarm(string id) { lock (warmed) return warmed.Contains(id) || !Needed; }

    /// <summary>Warms light <paramref name="id"/> in <paramref name="host"/>'s window when idle; completes when it is warm (or was given up on: it then draws as it is).</summary>
    internal static async Task Warm(Control host, string id)
    {
        if (IsWarm(id)) return;
        try { await GpuWarmup.WarmWhenIdle(host, [Program(id)]); }
        catch (Exception) { } // not drawn in time (minimised, hidden): the light shows as it is, its program compiles when it is drawn
        lock (warmed) warmed.Add(id);
    }

    /// <summary>Tests: forget what was warmed.</summary>
    internal static void Reset() { lock (warmed) warmed.Clear(); }
}

/// <summary>
/// Hands a light view its settings, holding back a shader light that is not warmed yet (the view keeps what it showed) until its
/// warm-up is done (ED-NEW-lightv2-3). Its host is a 1 px control of its own beside the light (the warm-up's visual is its child).
/// </summary>
internal sealed class ShaderLightGate
{
    private readonly ComposerLightView light;
    private ComposerLightSettings wanted = ComposerLightSettings.Default;

    /// <summary>The warm-up's host: put it in the tree beside the light.</summary>
    internal Control Host { get; } = new() { Width = 1, Height = 1, IsHitTestVisible = false, Focusable = false, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top };

    internal ShaderLightGate(ComposerLightView light)
    {
        this.light = light;
        Host.AttachedToVisualTree += (_, _) => Apply(wanted);
    }

    /// <summary>Shows <paramref name="settings"/> now, or once its shader light is warm.</summary>
    internal void Apply(ComposerLightSettings settings)
    {
        wanted = settings;
        if (settings.ActiveShader is not { } def || ShaderLightWarmup.IsWarm(def.Id)) { light.Settings = settings; return; }
        if (TopLevel.GetTopLevel(Host) is null) return; // once in a window
        _ = Warmed(def.Id);
    }

    /// <summary>Warms light <paramref name="id"/> ahead (the Customization preview: a light shown or about to be).</summary>
    internal void Prepare(string id)
    {
        if (!ShaderLightWarmup.IsWarm(id) && TopLevel.GetTopLevel(Host) is not null) _ = ShaderLightWarmup.Warm(Host, id);
    }

    private async Task Warmed(string id)
    {
        await ShaderLightWarmup.Warm(Host, id);
        Dispatcher.UIThread.Post(() => { if (wanted.ActiveShader?.Id == id) light.Settings = wanted; });
    }
}
