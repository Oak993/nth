using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Marrow.App.Port.FirstRun;
using Marrow.UI.Port;
using SkiaSharp;

namespace Marrow.App.Views.Composer;

// ED-NEW-launch-fix (A): the glass programs a launch could link on the render thread just after the hand-over, made ahead on a worker's
// GPU context (GpuBootPreparation's independent context), so the program cache holds them before a window draws with them.
//
// What varied between launches was never a shader's text: every program a launch added had the text a launch before it had, but Skia
// links the glass material's one effect into another program for each way a frame draws it: whole or faded (a paint alpha below 255 wraps
// the effect in an alpha modulation), open or under the hand-over's content clip (BootStageWindow.Apply's rounded RectangleGeometry, which
// Skia applies as a coverage mask), and a lone rounded rectangle with its inside drawn apart or not. Which of these the main window's first
// frames meet depends on where its fade and its clip stand when each piece first draws, so a launch could meet one no launch before it had;
// a merged container's program takes about 3 s to link (measured: 2.7 to 3.0 s for six pieces), all of it on the render thread.
//
// Each variant is drawn through the material's own Draw (GlassProgramVariants), under the opacity and the kind of clip that make it, into an
// RGBA target on the worker's context: natively, against a cache filled by real launches, every variant drawn so was found in the cache (the
// worker linked nothing a window had not), the BGRA target found the same programs, and a RoundedRect clip (analytic, not a mask) made a
// program no window had; so neither is drawn. Not awaited by anything: Boot B, the hand-over and the first run never wait for it. Once all have
// been drawn for this build on this GPU (the root's marker), a launch does nothing.
internal static class GlassVariantPreparation
{
    /// <summary>One way a frame draws the glass: its number of merged pieces, faded or whole, under the hand-over's clip or open, and for
    /// a lone piece, a capsule or a rounded rectangle (whose inside the material draws apart).</summary>
    internal readonly record struct Variant(int Count, bool Faded, bool Clipped, bool Capsule)
    {
        public string Name => string.Create(CultureInfo.InvariantCulture,
            $"glass {Count}{(Capsule ? " capsule" : "")} {(Faded ? "faded" : "whole")} {(Clipped ? "clipped" : "open")}");
    }

    /// <summary>The root's file naming the build, the GPU's cache folder and the variants a completed pass drew there.</summary>
    internal const string Marker = "glass-variants.txt";

    /// <summary>The opacity a faded variant is drawn at (any below 1 links the same program).</summary>
    internal const double Fade = 0.5;

    /// <summary>The hand-over's content clip as BootStageWindow.Apply makes it (a rectangle with the frame's corner radius), meeting the pieces.</summary>
    internal static Geometry HandOverClip() => new RectangleGeometry(new Rect(60, 50, 190, 150), 8, 8);

    /// <summary>Every variant, by number of pieces.</summary>
    internal static IReadOnlyList<Variant> Variants { get; } =
    [
        .. Enumerable.Range(1, GlassProgramVariants.Counts).SelectMany(count => (count == 1 ? new[] { false, true } : [false])
            .SelectMany(capsule => new[] { (false, false), (true, false), (false, true), (true, true) }
                .Select(way => new Variant(count, way.Item1, way.Item2, capsule)))),
    ];

    /// <summary>The variants as programs a GPU preparation draws (never registered with GpuWarmup: a first run does not wait for them).</summary>
    internal static IReadOnlyList<GpuProgram> Programs { get; } =
        [.. Variants.Select(variant => GpuProgram.Draw(variant.Name, FirstRunSteps.Graphics, context => Draw(context, variant)))];

    /// <summary>Draws the programs on a worker's GPU context (tests substitute it).</summary>
    internal static Func<GpuProgram[], Task> Runner { get; set; } = programs => GpuBootPreparation.Run(programs, 1, CancellationToken.None);

    private static Task? pass;
    private static readonly object gate = new();

    /// <summary>The pass for this process, begun once (from the first GPU context's arrival; nothing waits for it).</summary>
    internal static Task Start()
    {
        lock (gate) return pass ??= Task.Run(Prepare);
    }

    /// <summary>Draws every variant on a worker's context unless the root's marker says a pass did for this build and this GPU's folder,
    /// then writes the marker. A failure is traced, never thrown (the window then links what it meets, as before).</summary>
    internal static async Task Prepare()
    {
        try
        {
            if (GpuProgramCache.Root() is not { } root || GpuProgramCache.Folder is not { } folder) return; // no cache: nothing kept for later
            string[] expected = [GpuProgramCache.Build, Path.GetFileName(folder), Variants.Count.ToString(CultureInfo.InvariantCulture)];
            if (Done(root, expected)) return;
            using (AuroraBootTrace.Begin("glass variants")) await Runner([.. Programs]).ConfigureAwait(false);
            File.WriteAllLines(Path.Combine(root, Marker), expected);
        }
        catch (Exception e)
        {
            using var failed = AuroraBootTrace.Begin("glass variants: " + e.GetType().Name);
        }
    }

    private static bool Done(string root, string[] expected)
    {
        try
        {
            var file = Path.Combine(root, Marker);
            return File.Exists(file) && File.ReadAllLines(file).SequenceEqual(expected, StringComparer.Ordinal);
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    /// <summary>Draws <paramref name="variant"/> into an RGBA target on the context's GPU (a CPU raster where there is none: tests), over an
    /// opaque, uneven backdrop so neither of the material's two draws can be skipped.</summary>
    internal static void Draw(GpuWarmContext context, Variant variant)
    {
        var scale = Math.Max(1, context.Scaling);
        var size = GlassProgramVariants.Size;
        var info = new SKImageInfo((int)Math.Ceiling(size.Width * scale), (int)Math.Ceiling(size.Height * scale), SKColorType.Rgba8888, SKAlphaType.Premul);
        using var target = GpuWarmup.WarmTarget.Open(context.Gpu, info, scale);
        var canvas = target.Surface.Canvas;
        canvas.Clear(new SKColor(24, 35, 58));
        using (var stripe = new SKPaint { Color = new SKColor(138, 72, 43) })
            canvas.DrawRect((float)(160 * scale), (float)(100 * scale), (float)(70 * scale), (float)(170 * scale), stripe);
        var drawing = target.Context;
        using (variant.Faded ? drawing.PushOpacity(Fade) : default)
        using (variant.Clipped ? drawing.PushGeometryClip(HandOverClip()) : default)
            GlassProgramVariants.Draw(drawing, variant.Count, variant.Capsule);
        target.Surface.Flush();
    }
}
