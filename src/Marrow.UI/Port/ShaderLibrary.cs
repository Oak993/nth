// PORT-ONLY: SwiftUI's ShaderLibrary over the app's one Metal file, DroppyCode/UI/Common/Genie.metal (B2 1fbf81e), :1-54, as Skia SkSL.
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using SkiaSharp;

namespace Marrow.UI.Port;

/// <summary>
/// <c>ShaderLibrary.genie</c> (Genie.swift:158) and the <c>.distortionEffect</c> it drives (Genie.swift:157-164). The
/// Metal function has no cross-platform form (PR-THEME-175 windows_path), so it is transcribed line for line into
/// SkSL and run through Skia's runtime effects, the path the glass probes proved on Avalonia 12.1.2
/// (porting/probes/p10). Same three inputs, same order, same progress semantics.
/// </summary>
public static class ShaderLibrary
{
    // Genie.metal:16-54, verbatim in SkSL (Metal and SkSL share clamp, smoothstep, mix, min and max). `position` is a
    // destination point in the layer's coordinates and the result is the point of the layer to sample there;
    // `nothing` lies far outside the row, so it samples transparency, as in Metal.
    internal const string GenieSource = """
        uniform shader row;
        uniform float2 rowPixels;
        uniform float progress;
        uniform float4 source;
        uniform float2 target;
        uniform float layerOpacity;

        float2 genie(float2 position, float progress, float4 source, float2 target) {
            const float2 nothing = float2(-100000.0, -100000.0);
            float t = clamp(progress, 0.0, 1.0);
            float pinch = smoothstep(0.0, 0.45, t);
            float slide = smoothstep(0.2, 1.0, t);

            float width = source.z;
            float height = source.w;
            float flip = target.y >= source.y + height * 0.5 ? 1.0 : -1.0;
            float x0 = source.x;
            float y0 = flip > 0.0 ? source.y : -(source.y + height);
            float targetY = flip * target.y;
            float positionY = flip * position.y;
            float travel = max(targetY - y0, height);

            if (positionY > targetY) {
                return nothing;
            }

            float depth = clamp((positionY - y0) / travel, 0.0, 1.0);
            float bend = pinch * smoothstep(0.0, 1.0, depth);
            float neck = min(24.0, width);
            float left = mix(x0, target.x - neck * 0.5, bend);
            float right = mix(x0 + width, target.x + neck * 0.5, bend);
            float u = (position.x - left) / max(right - left, 0.0001);
            if (u < 0.0 || u > 1.0) {
                return nothing;
            }

            float sourceY = positionY - slide * (targetY - y0);
            if (sourceY < y0 || sourceY > y0 + height) {
                return nothing;
            }
            return float2(x0 + u * width, flip * sourceY);
        }

        half4 main(float2 position) {
            float2 at = genie(position, progress, source, target);
            return row.eval((at - source.xy) / source.zw * rowPixels) * layerOpacity;
        }
        """;

    private static readonly Lazy<SKRuntimeEffect> genie = new(() =>
        SKRuntimeEffect.CreateShader(GenieSource, out var errors)
        ?? throw new InvalidOperationException("The genie shader did not compile: " + errors));

    /// <summary>The compiled genie effect, built once per process like a Metal library.</summary>
    public static SKRuntimeEffect Genie => genie.Value;

    /// <summary>
    /// The flat image a flight carries, as pixels Skia can sample. A rendered ghost is already a bitmap and is copied
    /// once; any other image is first drawn into one at its own size.
    /// </summary>
    public static SKImage RasterImage(IImage image)
    {
        var bitmap = image as Bitmap;
        if (bitmap is null || (bitmap.Format != PixelFormats.Bgra8888 && bitmap.Format != PixelFormats.Rgba8888))
            bitmap = Rasterize(image);
        var size = bitmap.PixelSize;
        var stride = size.Width * 4;
        var buffer = new byte[stride * size.Height];
        var handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        try { bitmap.CopyPixels(new PixelRect(size), handle.AddrOfPinnedObject(), buffer.Length, stride); }
        finally { handle.Free(); }
        var colorType = bitmap.Format == PixelFormats.Rgba8888 ? SKColorType.Rgba8888 : SKColorType.Bgra8888;
        var alphaType = bitmap.AlphaFormat switch
        {
            AlphaFormat.Unpremul => SKAlphaType.Unpremul,
            AlphaFormat.Opaque => SKAlphaType.Opaque,
            _ => SKAlphaType.Premul,
        };
        return SKImage.FromPixelCopy(new SKImageInfo(size.Width, size.Height, colorType, alphaType), buffer, stride);
    }

    private static RenderTargetBitmap Rasterize(IImage image)
    {
        var size = image.Size;
        var pixels = new PixelSize(Math.Max(1, (int)Math.Ceiling(size.Width)), Math.Max(1, (int)Math.Ceiling(size.Height)));
        var bitmap = new RenderTargetBitmap(pixels);
        using (var context = bitmap.CreateDrawingContext())
            context.DrawImage(image, new Rect(size));
        return bitmap;
    }
}

/// <summary>
/// One frame of <c>.distortionEffect(ShaderLibrary.genie(.float(progress), .float4(frame), .float2(target)),
/// maxSampleOffset: reach)</c> over a row image drawn at <c>frame</c> (Genie.swift:150-164). Each destination pixel
/// inside <paramref name="bounds"/> shows the row pixel the shader maps it to. The values are captured when the frame
/// is recorded, because Avalonia draws it on its render thread.
/// </summary>
internal sealed class GenieDistortionOperation(Rect bounds, SKImage row, double progress, Rect frame, Point target) : ICustomDrawOperation
{
    public Rect Bounds { get; } = bounds;

    public bool HitTest(Point p) => false;

    public bool Equals(ICustomDrawOperation? other) => false;

    public void Dispose() { }

    public void Render(ImmediateDrawingContext context)
    {
        var feature = context.TryGetFeature<ISkiaSharpApiLeaseFeature>()
            ?? throw new InvalidOperationException("The genie distortion needs the Skia renderer (ISkiaSharpApiLeaseFeature).");
        using var lease = feature.Lease();
        var effect = ShaderLibrary.Genie;
        // Genie.swift:158-161: the three arguments, in this order, as float, float4 and float2.
        var uniforms = new SKRuntimeEffectUniforms(effect)
        {
            ["rowPixels"] = new[] { (float)row.Width, (float)row.Height },
            ["progress"] = (float)progress,
            ["source"] = new[] { (float)frame.X, (float)frame.Y, (float)frame.Width, (float)frame.Height },
            ["target"] = new[] { (float)target.X, (float)target.Y },
            // The opacity Avalonia has pushed for this control (:165 and every ancestor): a lease draws around
            // Avalonia's own paint opacity, so the effect applies it itself.
            ["layerOpacity"] = (float)lease.CurrentOpacity,
        };
        // ENGINEERING DECISION: the layer outside the row is transparent, so sampling past the image's edge gives
        // transparency (decal), and a row pixel is read with linear filtering as a layer is resampled.
        using var rowShader = row.ToShader(SKShaderTileMode.Decal, SKShaderTileMode.Decal, new SKSamplingOptions(SKFilterMode.Linear));
        var children = new SKRuntimeEffectChildren(effect) { ["row"] = rowShader };
        using var shader = effect.ToShader(uniforms, children);
        using var paint = new SKPaint { Shader = shader };
        lease.SkCanvas.DrawRect(new SKRect((float)Bounds.Left, (float)Bounds.Top, (float)Bounds.Right, (float)Bounds.Bottom), paint);
    }
}
