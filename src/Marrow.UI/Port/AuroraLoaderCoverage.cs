using SkiaSharp;

namespace Marrow.UI.Port;

// Raster adaptation of Ganesh QuadPerEdgeAA/CircularRRectEffect: geometry stays in logical units;
// half-pixel coverage ramps stay in device units on both the CPU and the compositor.
// Local shader coordinates retain the CTM's fractional pixel phase. No reference pixels or masks.
internal sealed class AuroraLoaderCoverage : IDisposable
{
    private readonly SKRuntimeEffect effect = Compile();
    private static SKRuntimeEffect Compile() => SKRuntimeEffect.CreateShader("""
        uniform shader color;
        uniform float4 bounds;
        uniform float4 deviceInverse;
        uniform float4 parameters;
        half4 main(float2 p) {
            float2 mid = (bounds.xy + bounds.zw) * .5;
            float2 halfSize = (bounds.zw - bounds.xy) * .5;
            float2 distance = halfSize - abs(p - mid);
            float2 pixel = float2(abs(deviceInverse.x) + abs(deviceInverse.y), abs(deviceInverse.z) + abs(deviceInverse.w));
            float coverage = clamp(min(distance.x / pixel.x, distance.y / pixel.y) + .5, 0, 1);
            float radius = parameters.x;
            float2 d = max(abs(p - mid) - (halfSize - radius), 0);
            if (radius > 0 && min(d.x, d.y) > 0) {
                float fn = length(d) - radius;
                float2 grad = d / max(length(d), .00001) * sign(p - mid);
                float fnWidth = length(float2(dot(grad, deviceInverse.xz), dot(grad, deviceInverse.yw)));
                coverage = min(coverage, clamp(.5 - fn / max(fnWidth, .00001), 0, 1));
            }
            return color.eval(p) * coverage;
        }
        """, out var error) ?? throw new InvalidOperationException(error);
    private readonly float[] uniforms = new float[12];
    private readonly nint[] children = new nint[1];

    internal void Bind(SKCanvas canvas, SKPaint paint, SKRect rect, float radius, nint color)
    {
        var inverse = canvas.TotalMatrix.Invert();
        uniforms[0] = rect.Left; uniforms[1] = rect.Top; uniforms[2] = rect.Right; uniforms[3] = rect.Bottom;
        uniforms[4] = inverse.ScaleX; uniforms[5] = inverse.SkewX; uniforms[6] = inverse.SkewY; uniforms[7] = inverse.ScaleY;
        uniforms[8] = radius;
        children[0] = color;
        var data = AuroraLoaderSkia.sk_data_new_with_copy(uniforms, effect.UniformSize);
        try
        {
            var shader = AuroraLoaderSkia.sk_runtimeeffect_make_shader(effect.Handle, data, children, 1, 0);
            if (shader == 0) throw new InvalidOperationException("The loader coverage shader was not created.");
            AuroraLoaderSkia.sk_paint_set_shader(paint.Handle, shader);
            AuroraLoaderSkia.sk_shader_unref(shader);
        }
        finally { AuroraLoaderSkia.sk_data_unref(data); }
        paint.IsAntialias = false;
    }

    internal static void Fill(SKCanvas canvas, SKPaint paint, SKRect rect)
    {
        var inverse = canvas.TotalMatrix.Invert();
        rect.Inflate(Math.Abs(inverse.ScaleX) + Math.Abs(inverse.SkewX), Math.Abs(inverse.ScaleY) + Math.Abs(inverse.SkewY));
        canvas.DrawRect(rect, paint);
    }

    public void Dispose() => effect.Dispose();
}
