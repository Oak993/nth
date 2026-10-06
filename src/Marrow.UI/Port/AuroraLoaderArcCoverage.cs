using SkiaSharp;

namespace Marrow.UI.Port;

// Blink's single-arc Canvas fast path -> Ganesh CircleGeometryProcessor (GrOvalOpFactory.cpp).
// The two nearby round-cap contributions add outside the arc planes, as in that processor.
internal sealed class AuroraLoaderArcCoverage : IDisposable
{
    private readonly SKRuntimeEffect effect = Compile();
    private static SKRuntimeEffect Compile() => SKRuntimeEffect.CreateShader("""
        uniform float4 ring;
        uniform float4 endpoints;
        uniform float4 parameters;
        uniform float4 color;
        half4 main(float2 p) {
            float2 q = (p - ring.xy) * parameters.x;
            float R = ring.z * parameters.x;
            float W = ring.w * parameters.x;
            float distance = length(q);
            float coverage = clamp(R + W + .5 - distance, 0, 1) * clamp(distance - (R - W - .5), 0, 1);
            if (parameters.y > 0) {
                float clip = clamp(dot(q, float2(-endpoints.y, endpoints.x)) + .5, 0, 1)
                    * clamp(dot(q, float2(endpoints.w, -endpoints.z)) + .5, 0, 1);
                float caps = max(W + .5 - length(q - R * endpoints.xy), 0)
                    + max(W + .5 - length(q - R * endpoints.zw), 0);
                coverage = min(coverage * clip + (1 - clip) * caps, 1);
            }
            return half4(color * coverage);
        }
        """, out var error) ?? throw new InvalidOperationException(error);
    private readonly float[] uniforms = new float[16];
    private readonly nint[] children = [];

    internal void Draw(SKCanvas canvas, SKPaint paint, float cx, float cy, float radius, float width, double start, double end, bool caps, SKColor color, double alpha)
    {
        var matrix = canvas.TotalMatrix;
        var scale = Math.Sqrt(matrix.ScaleX * matrix.ScaleX + matrix.SkewY * matrix.SkewY);
        uniforms[0] = cx; uniforms[1] = cy; uniforms[2] = radius; uniforms[3] = width / 2;
        uniforms[4] = (float)Math.Cos(start); uniforms[5] = (float)Math.Sin(start);
        uniforms[6] = (float)Math.Cos(end); uniforms[7] = (float)Math.Sin(end);
        uniforms[8] = (float)scale; uniforms[9] = caps ? 1 : 0;
        uniforms[12] = (float)(color.Red / 255.0 * alpha); uniforms[13] = (float)(color.Green / 255.0 * alpha);
        uniforms[14] = (float)(color.Blue / 255.0 * alpha); uniforms[15] = (float)alpha;
        var data = AuroraLoaderSkia.sk_data_new_with_copy(uniforms, effect.UniformSize);
        try
        {
            var shader = AuroraLoaderSkia.sk_runtimeeffect_make_shader(effect.Handle, data, children, 0, 0);
            if (shader == 0) throw new InvalidOperationException("The arc coverage shader was not created.");
            AuroraLoaderSkia.sk_paint_set_shader(paint.Handle, shader);
            AuroraLoaderSkia.sk_shader_unref(shader);
        }
        finally { AuroraLoaderSkia.sk_data_unref(data); }
        paint.Style = SKPaintStyle.Fill; paint.IsAntialias = false; paint.Color = SKColors.White;
        var edge = radius + width / 2 + (float)(1 / scale);
        canvas.DrawRect(new SKRect(cx - edge, cy - edge, cx + edge, cy + edge), paint);
        paint.Shader = null;
    }
    public void Dispose() => effect.Dispose();
}
