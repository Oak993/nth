// PORT-ONLY: the allocation-free Skia calls of the per-frame drawing (ED-248 review, OD-0069 zero stutter).
using System.Reflection;
using System.Runtime.InteropServices;
using SkiaSharp;

namespace Marrow.UI.Port;

// ED-248 review (OD-0069, zero stutter): the liquid makes a dozen shaders a frame (a pass each, a bubble each, the glass)
// and samples two snapshots, and the glass material (the knob's lens, the panels) redraws whenever the liquid under it
// changes. Through SkiaSharp 3.119's managed calls each one leaves garbage behind: SKRuntimeEffect.ToShader copies the
// uniforms into an SKData it never disposes (measured: 1,000 calls add 1,000 entries to SkiaSharp's handle table until a
// collection runs their finalizers), each SKShader, SKImage, SKData and SKPaint is a wrapper registered in that table, and
// an object that owns or keeps another alive makes itself a ConcurrentDictionary of them (about 1 kB on a 16-core machine).
// Between collections the table grew by whole frames of entries, so it resized in steps of 0.5, 1 and 2 MB (large-object
// heap arrays) and each collection handed the finalizer thread thousands of objects. So the frame paths call the same
// native functions SkiaSharp's own SkiaApi binds (libSkiaSharp's C API, the signatures checked against SkiaApi by a test),
// holding each shader and image only as a native reference released at once. Nothing managed is made per frame.
// (Moved from Marrow.App to Marrow.UI in review 2 so the glass material uses it too.)
public static class SkiaShaders
{
    private const string Skia = "libSkiaSharp";

    [DllImport(Skia, CallingConvention = CallingConvention.Cdecl)]
    private static extern nint sk_data_new_with_copy(nint source, nint length);

    [DllImport(Skia, CallingConvention = CallingConvention.Cdecl)]
    private static extern void sk_data_unref(nint data);

    [DllImport(Skia, CallingConvention = CallingConvention.Cdecl)]
    private static extern nint sk_runtimeeffect_make_shader(nint effect, nint uniforms, nint[]? children, nint childCount, ref SKMatrix localMatrix);

    [DllImport(Skia, CallingConvention = CallingConvention.Cdecl)]
    private static extern void sk_shader_unref(nint shader);

    [DllImport(Skia, CallingConvention = CallingConvention.Cdecl)]
    private static extern void sk_paint_set_shader(nint paint, nint shader);

    [DllImport(Skia, CallingConvention = CallingConvention.Cdecl)]
    private static extern nint sk_shader_new_linear_gradient(SKPoint[] points, uint[] colors, float[] colorPos, int colorCount, SKShaderTileMode tileMode, ref SKMatrix localMatrix);

    [DllImport(Skia, CallingConvention = CallingConvention.Cdecl)]
    private static extern nint sk_shader_new_blend(SKBlendMode mode,nint destination,nint source);

    [DllImport(Skia, CallingConvention = CallingConvention.Cdecl)]
    private static extern nint sk_shader_new_color(uint color);

    [DllImport(Skia, CallingConvention = CallingConvention.Cdecl)]
    private static extern nint sk_surface_new_image_snapshot(nint surface);

    [DllImport(Skia, CallingConvention = CallingConvention.Cdecl)]
    private static extern nint sk_image_make_shader(nint image, SKShaderTileMode tileX, SKShaderTileMode tileY, ref SKSamplingOptions sampling, ref SKMatrix localMatrix);

    [DllImport(Skia, CallingConvention = CallingConvention.Cdecl)]
    private static extern void sk_image_unref(nint image);

    [DllImport(Skia, CallingConvention = CallingConvention.Cdecl)]
    private static extern void sk_canvas_draw_image(nint canvas, nint image, float x, float y, ref SKSamplingOptions sampling, nint paint);

    /// <summary>The native functions this class binds, with the parameter types it binds them with (a test compares them with
    /// SkiaSharp's own SkiaApi).</summary>
    public static IReadOnlyList<MethodInfo> Bindings => typeof(SkiaShaders).GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
        .Where(m => m.Name.StartsWith("sk_", StringComparison.Ordinal)).ToArray();

    // SKRuntimeEffectUniforms keeps its values in a private SKData it writes in place; the shader is made from a copy of it
    private static readonly FieldInfo? UniformData = typeof(SKRuntimeEffectUniforms).GetField("data", BindingFlags.Instance | BindingFlags.NonPublic);

    /// <summary>The uniform block's own data (null if this SkiaSharp keeps it elsewhere: then <see cref="Make"/> goes the
    /// managed way).</summary>
    public static SKData? DataOf(SKRuntimeEffectUniforms uniforms) => UniformData?.GetValue(uniforms) as SKData;

    /// <summary>
    /// Where a uniform of <paramref name="length"/> floats lives in the block (bytes), found by writing a marker through
    /// SkiaSharp and looking for it; -1 if it cannot be found. SkiaSharp allocates on every uniform it sets (48 B, measured,
    /// for a float or an array alike), so the frame path writes its uniforms in place (<see cref="Write"/>), as SkiaSharp
    /// itself does: the floats one after another from the uniform's offset.
    /// </summary>
    public static int OffsetOf(SKRuntimeEffectUniforms uniforms, SKData? data, string name, int length)
    {
        ArgumentNullException.ThrowIfNull(uniforms);
        if (data is null || !uniforms.Contains(name)) return -1;
        const float marker = -98765.4321f;
        var bits = BitConverter.SingleToInt32Bits(marker);
        var values = new float[length];
        values[0] = marker;
        if (length == 1) uniforms[name] = marker; else uniforms[name] = values;
        var found = -1;
        for (var at = 0; at + 4 <= data.Size; at += 4)
            if (Marshal.ReadInt32(data.Data, at) == bits) { found = at; break; }
        if (length == 1) uniforms[name] = 0f; else uniforms[name] = new float[length];
        return found;
    }

    /// <summary>Writes floats into the uniform block from a byte offset.</summary>
    public static void Write(SKData data, int offset, ReadOnlySpan<float> values)
    {
        ArgumentNullException.ThrowIfNull(data);
        for (var i = 0; i < values.Length; i++) Marshal.WriteInt32(data.Data, offset + i * 4, BitConverter.SingleToInt32Bits(values[i]));
    }

    /// <summary>
    /// The effect's shader for these uniforms (copied, as SkiaSharp's ToShader copies them) and children (native handles, in
    /// the effect's declaration order): a native reference the caller releases with <see cref="Release"/>. 0 if Skia refused.
    /// </summary>
    public static nint Make(SKRuntimeEffect effect, SKRuntimeEffectUniforms uniforms, SKData? data, nint[]? children)
    {
        ArgumentNullException.ThrowIfNull(effect);
        ArgumentNullException.ThrowIfNull(uniforms);
        var identity = SKMatrix.Identity;
        if (data is null)
        {
            // the managed way (each call leaves an SKData for the finalizer): only if SkiaSharp's layout ever changes
            using var copy = uniforms.ToData();
            return sk_runtimeeffect_make_shader(effect.Handle, copy.Handle, children, children?.Length ?? 0, ref identity);
        }
        var block = sk_data_new_with_copy(data.Data, (nint)data.Size);
        try { return sk_runtimeeffect_make_shader(effect.Handle, block, children, children?.Length ?? 0, ref identity); }
        finally { sk_data_unref(block); }
    }

    /// <summary>A snapshot of the surface (a native reference: release it with <see cref="ReleaseImage"/>).</summary>
    public static nint Snapshot(SKSurface surface)
    {
        ArgumentNullException.ThrowIfNull(surface);
        return sk_surface_new_image_snapshot(surface.Handle);
    }

    /// <summary>The image's shader, clamped to transparent outside it, sampled linearly through <paramref name="matrix"/>.</summary>
    public static nint ImageShader(nint image, SKMatrix matrix) => ImageShader(image, SKShaderTileMode.Decal, matrix);

    /// <summary>The image's shader with <paramref name="tile"/> both ways, sampled linearly through <paramref name="matrix"/>
    /// (SKImage.ToShader(tile, tile, linear, matrix)).</summary>
    public static nint ImageShader(nint image, SKShaderTileMode tile, SKMatrix matrix)
    {
        var sampling = new SKSamplingOptions(SKFilterMode.Linear);
        return sk_image_make_shader(image, tile, tile, ref sampling, ref matrix);
    }

    /// <summary>
    /// <c>SKCanvas.DrawImage(image, x, y, paint)</c> for a native image: the paint's own sampling (a default paint's filter
    /// quality, none: nearest, no mipmaps), as SkiaSharp passes it.
    /// </summary>
    public static void DrawImage(SKCanvas canvas, nint image, float x, float y, SKPaint? paint)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        if (image == 0) return;
#pragma warning disable CS0618 // SkiaSharp's own DrawImage(image, x, y, paint) reads the obsolete FilterQuality the same way
        var sampling = (paint?.FilterQuality ?? SKFilterQuality.None).ToSamplingOptions();
#pragma warning restore CS0618
        sk_canvas_draw_image(canvas.Handle, image, x, y, ref sampling, paint?.Handle ?? 0);
    }

    /// <summary>Sets a native shader on a paint (the paint takes its own reference; 0 clears it).</summary>
    public static void SetShader(SKPaint paint, nint shader)
    {
        ArgumentNullException.ThrowIfNull(paint);
        sk_paint_set_shader(paint.Handle, shader);
    }

    /// <summary>Skia's sRGB linear gradient with retained input arrays and a native reference, released by the caller.</summary>
    public static nint LinearGradient(SKPoint[] points, uint[] colors, float[] stops)
    {
        var identity = SKMatrix.Identity;
        return sk_shader_new_linear_gradient(points, colors, stops, colors.Length, SKShaderTileMode.Clamp, ref identity);
    }

    /// <summary>Native blend and solid nodes retain their inputs; release the returned reference.</summary>
    public static nint Blend(SKBlendMode mode,nint destination,nint source)=>sk_shader_new_blend(mode,destination,source);
    public static nint Solid(SKColor color)=>sk_shader_new_color((uint)color);

    public static void Release(nint shader) { if (shader != 0) sk_shader_unref(shader); }

    public static void ReleaseImage(nint image) { if (image != 0) sk_image_unref(image); }
}
