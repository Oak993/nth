using Avalonia;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using SkiaSharp;

namespace Marrow.UI.Port;

// ED-NEW-launch-fix (A): the GPU programs a window's glass is linked into are not one per shape count. Skia links the material's runtime
// effect into a different program when the piece is drawn faded (the paint's alpha below 255 wraps the effect in an alpha modulation), when
// a clip the draw meets needs a coverage mask, and, for a lone rounded rectangle, when its inside is drawn apart (Inside: a difference
// clip). Which of these a launch meets depends on the frame its glass first draws in (the hand-over's fade and clip), so a launch could link one
// the launches before it never had: up to 3 s on the render thread for a merged container. VariantDraw draws a piece through the material's own
// Draw (the same effect, uniforms, blur child, stages and inside), into whatever DrawingContext the caller opened and pushed (its opacity and
// clips), so a worker's context links the program a window's draw will look up in the program cache.
internal static partial class GlassMaterial
{
    internal enum VariantShape { RoundedRectangle, Capsule }

    internal static void DrawVariant(DrawingContext context, int count, VariantShape shape) => context.Custom(new VariantDraw(count, shape));

    private sealed class VariantDraw(int count, VariantShape shape) : ICustomDrawOperation
    {
        public Rect Bounds => new(0, 0, VariantSize.Width, VariantSize.Height);
        public bool HitTest(Point p) => false;
        public bool Equals(ICustomDrawOperation? other) => false;
        public void Dispose() { }

        public void Render(ImmediateDrawingContext context)
        {
            var feature = context.TryGetFeature<ISkiaSharpApiLeaseFeature>() ?? throw new InvalidOperationException("Glass variant: no Skia lease.");
            using var lease = feature.Lease();
            if (lease.SkSurface is null) throw new InvalidOperationException("Glass variant: no surface to draw on.");
            var pieceShape = shape == VariantShape.Capsule ? GlassShape.Capsule(RoundedCornerStyle.Continuous) : GlassShape.RoundedRectangle(16, RoundedCornerStyle.Continuous);
            var size = shape == VariantShape.Capsule ? new Size(120, 32) : new Size(260, 200);
            var regions = Enumerable.Range(0, count).Select(i => new Region(size, pieceShape, new CornerRadius(16), Colors.Transparent, false, 1,
                false, false, default, Matrix.CreateTranslation(40 + i * 10, 30 + i * 6), 1)).ToArray();
            var backdrop = new Backdrop();
            try { _ = Draw(lease, new State(regions, 8), backdrop, null) ?? throw new InvalidOperationException("Glass variant: nothing drawn."); }
            finally { backdrop.DisposeOwned(); }
        }
    }

    internal static readonly Size VariantSize = new(360, 280);

    private sealed partial class Backdrop
    {
        // A variant's surfaces are its own and made on its own (a worker's) context: disposed here, never left to the compositor's queue.
        public void DisposeOwned()
        {
            kept?.Dispose();
            blurred?.Dispose();
            (kept, blurred, valid) = (null, null, false);
        }
    }
}

/// <summary>ED-NEW-launch-fix (A): the glass material's program variants, drawn for the GPU program cache (GlassMaterial.Variants.cs).</summary>
public static class GlassProgramVariants
{
    /// <summary>The largest number of pieces one program merges that a variant is drawn for.</summary>
    public const int Counts = GlassMaterial.WarmShapes;

    /// <summary>The size a variant draws in (logical units; the caller's target holds it).</summary>
    public static Size Size => GlassMaterial.VariantSize;

    /// <summary>Draws <paramref name="count"/> merged pieces (a lone rounded rectangle, or capsules) into <paramref name="context"/>, as its
    /// opacity and clips stand: a GPU target only.</summary>
    public static void Draw(DrawingContext context, int count, bool capsule = false)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(count, Counts);
        GlassMaterial.DrawVariant(context, count, capsule ? GlassMaterial.VariantShape.Capsule : GlassMaterial.VariantShape.RoundedRectangle);
    }
}
