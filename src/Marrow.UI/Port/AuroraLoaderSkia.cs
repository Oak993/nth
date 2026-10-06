using System.Runtime.InteropServices;
using SkiaSharp;

namespace Marrow.UI.Port;

// Same Skia C ABI as AuroraMark: fixed managed buffers, changing Canvas gradients without managed wrappers.
internal static class AuroraLoaderSkia
{
    [DllImport("libSkiaSharp", CallingConvention = CallingConvention.Cdecl)]
    internal static extern nint sk_shader_new_linear_gradient_color4f([In] SKPoint[] points, [In] SKColorF[] colors, nint colorSpace, [In] float[] positions, int count, SKShaderTileMode mode, nint matrix);
    [DllImport("libSkiaSharp", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void sk_paint_set_shader(nint paint, nint shader);
    [DllImport("libSkiaSharp", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void sk_shader_unref(nint shader);
    [DllImport("libSkiaSharp", CallingConvention = CallingConvention.Cdecl)]
    internal static extern nint sk_data_new_with_copy([In] float[] values, nint bytes);
    [DllImport("libSkiaSharp", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void sk_data_unref(nint data);
    [DllImport("libSkiaSharp", CallingConvention = CallingConvention.Cdecl)]
    internal static extern nint sk_runtimeeffect_make_shader(nint effect, nint data, [In] nint[] children, nint count, nint matrix);
    [DllImport("libSkiaSharp", CallingConvention = CallingConvention.Cdecl)]
    internal static extern nint sk_shader_new_color4f(in SKColorF color, nint colorSpace);
    [DllImport("libSkiaSharp", CallingConvention = CallingConvention.Cdecl)]
    internal static extern nint sk_surface_new_image_snapshot(nint surface);
    [DllImport("libSkiaSharp", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void sk_image_unref(nint image);
    [DllImport("libSkiaSharp", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void sk_canvas_draw_image_rect(nint canvas, nint image, in SKRect source, in SKRect dest, in SKSamplingOptions sampling, nint paint);
}
