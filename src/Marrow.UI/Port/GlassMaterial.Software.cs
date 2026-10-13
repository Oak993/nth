using Avalonia.Skia;
using SkiaSharp;

namespace Marrow.UI.Port;

internal static partial class GlassMaterial
{
    // ED-NEW-composer-stack-2: software SkSL is expensive even when only a sibling changes. Reuse the incoming mask and colour,
    // not composed pixels: the destination still receives DstOut then Plus with its original rounding. F32 keeps shader precision.
    // Capture the whole material, so changing the destination clip cannot discard work. Four entries share a 64 MiB retained cap.
    // Each key includes the material, transform, opacity, surface geometry and every kept backdrop byte. Release disposes its entries.
    // GPU draws and small or merged materials keep their original path.
    private const long SoftwareFrameLimit = 64L * 1024 * 1024;
    private static readonly object softwareFrameGate = new();
    private static readonly List<SoftwareFrame> softwareFrames = [];
    private static long softwareFrameBytes;
    internal static int SoftwareFrameHitsForTests;

    private sealed class SoftwareFrame(Backdrop owner, State state, SKMatrix matrix, SKRectI area, SKRectI output, SKSizeI size, byte opacity,
        long bytes, SKBitmap under, SKImage mask, SKImage colour) : IDisposable
    {
        public Backdrop Owner { get; } = owner;
        public State State { get; } = state;
        public SKMatrix Matrix { get; } = matrix;
        public SKRectI Area { get; } = area;
        public SKRectI Output { get; } = output;
        public SKSizeI Size { get; } = size;
        public byte Opacity { get; } = opacity;
        public long Bytes { get; } = bytes;
        public SKBitmap Under { get; } = under;
        public SKImage Mask { get; } = mask;
        public SKImage Colour { get; } = colour;
        public void Dispose() { Under.Dispose(); Mask.Dispose(); Colour.Dispose(); }
    }

    private static void RemoveSoftwareFrame(int index)
    {
        var frame = softwareFrames[index];
        softwareFrames.RemoveAt(index);
        softwareFrameBytes -= frame.Bytes;
        frame.Dispose();
    }

    private static void ClearSoftwareFrame(Backdrop owner)
    {
        lock (softwareFrameGate)
            for (var i = softwareFrames.Count - 1; i >= 0; i--)
                if (softwareFrames[i].Owner == owner) RemoveSoftwareFrame(i);
    }

    private static bool SoftwareFrameEligible(ISkiaSharpApiLease lease, State state, Backdrop owner, SKRectI output, SKImage input)
    {
        if (lease.GrContext is not null || state.Regions is not [{ Size.Width: >= 600, Drawn.Canvas: not null }]) return false;
        // The cold draw uses full-coordinate F32 surfaces too: bound that temporary pair as well as the retained entries.
        if ((long)owner.Whole.Width * owner.Whole.Height * 32 > SoftwareFrameLimit) return false;
        var matrix = lease.SkCanvas.TotalMatrix;
        if (matrix.Persp0 != 0 || matrix.Persp1 != 0 || matrix.Persp2 != 1) return false;
        var bounds = SKRectI.Intersect(output, new SKRectI(0, 0, owner.Whole.Width, owner.Whole.Height));
        return !bounds.IsEmpty && (long)bounds.Width * bounds.Height * 32 + (long)input.Width * input.Height * 4 <= SoftwareFrameLimit;
    }

    // Preserve the original device-coordinate arithmetic while drawing, then retain only owned pixels in the material's area.
    // A subset image can retain its whole parent allocation; the bitmap readback ensures the retained cap counts actual cropped storage.
    private static SKImage? CopySoftwareLayer(SKSurface surface, SKRectI bounds)
    {
        using var image = surface.Snapshot(bounds);
        var info = new SKImageInfo(bounds.Width, bounds.Height, SKColorType.RgbaF32, SKAlphaType.Premul, image.ColorSpace);
        using var bitmap = new SKBitmap();
        if (!bitmap.TryAllocPixels(info) || !image.ReadPixels(info, bitmap.GetPixels(), bitmap.RowBytes, 0, 0)) return null;
        bitmap.SetImmutable();
        return SKImage.FromBitmap(bitmap);
    }

    private static bool TrySoftwareFrame(ISkiaSharpApiLease lease, State state, Backdrop owner, SKRectI area, SKRectI output, SKImage input, byte opacity,
        Action<SKCanvas>? maskLayer, Action<SKCanvas>? colourLayer)
    {
        if (!SoftwareFrameEligible(lease, state, owner, output, input)) return false;
        var size = owner.Whole;
        var bounds = SKRectI.Intersect(output, new SKRectI(0, 0, size.Width, size.Height));
        var required = (long)bounds.Width * bounds.Height * 32 + (long)input.Width * input.Height * 4;
        var inputInfo = new SKImageInfo(input.Width, input.Height, input.ColorType, input.AlphaType, input.ColorSpace);
        using var pixels = new SKBitmap();
        if (!pixels.TryAllocPixels(inputInfo) || !input.ReadPixels(inputInfo, pixels.GetPixels(), pixels.RowBytes, 0, 0)) return false;
        var canvas = lease.SkCanvas;
        var matrix = canvas.TotalMatrix;
        lock (softwareFrameGate)
        {
            SoftwareFrame? found = null;
            for (var i = 0; i < softwareFrames.Count; i++)
            {
                var old = softwareFrames[i];
                if (old.Owner != owner || old.Size != size || old.Area != area || old.Output != bounds || !old.Matrix.Equals(matrix) ||
                    old.Opacity != opacity || old.State.Spacing != state.Spacing || !old.State.Regions.SequenceEqual(state.Regions) ||
                    !old.Under.Info.Equals(pixels.Info) || !old.Under.GetPixelSpan().SequenceEqual(pixels.GetPixelSpan())) continue;
                found = old;
                softwareFrames.RemoveAt(i);
                softwareFrames.Add(old);
                SoftwareFrameHitsForTests++;
                break;
            }
            if (found is null)
            {
                if (maskLayer is null || colourLayer is null) return false;
                while (softwareFrames.Count >= 4 || softwareFrameBytes + required > SoftwareFrameLimit) RemoveSoftwareFrame(0);
                var info = new SKImageInfo(size.Width, size.Height, SKColorType.RgbaF32, SKAlphaType.Premul);
                using var removal = SKSurface.Create(info);
                using var addition = SKSurface.Create(info);
                if (removal is null || addition is null) return false;
                foreach (var target in new[] { removal.Canvas, addition.Canvas })
                {
                    target.Clear(SKColors.Transparent);
                    target.SetMatrix(matrix);
                }
                maskLayer(removal.Canvas);
                colourLayer(addition.Canvas);
                var maskCopy = CopySoftwareLayer(removal, bounds);
                var colourCopy = CopySoftwareLayer(addition, bounds);
                var inputCopy = pixels.Copy();
                if (maskCopy is null || colourCopy is null || inputCopy is null)
                {
                    maskCopy?.Dispose();
                    colourCopy?.Dispose();
                    inputCopy?.Dispose();
                    return false;
                }
                found = new SoftwareFrame(owner, state, matrix, area, bounds, size, opacity, required, inputCopy, maskCopy, colourCopy);
                softwareFrames.Add(found);
                softwareFrameBytes += required;
            }
            canvas.Save();
            canvas.ResetMatrix();
            using var maskPaint = new SKPaint { BlendMode = SKBlendMode.DstOut };
            using var colourPaint = new SKPaint { BlendMode = SKBlendMode.Plus };
            canvas.DrawImage(found.Mask, found.Output.Left, found.Output.Top, maskPaint);
            canvas.DrawImage(found.Colour, found.Output.Left, found.Output.Top, colourPaint);
            canvas.Restore();
            return true;
        }
    }
}
