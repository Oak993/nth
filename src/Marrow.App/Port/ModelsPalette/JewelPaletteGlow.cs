// PORT-ONLY: IR-3 glow_alpha/glow_blur, under the OD-0087 Jewel, same effect sample.
// Extra tail uniforms are renderer-only: the original shader block/layout is unchanged.
using SkiaSharp;
using System.Runtime.InteropServices;
namespace Marrow.App.Views.Composer;
internal sealed partial class JewelKnobRenderer
{
    private readonly SKPaint paletteGlow=new(){IsAntialias=true};private float paletteGlowBlur=-1;
    private void DrawPaletteGlow(SKCanvas canvas,float[] values,SKRect box,float opacity)
    {
        if(values.Length<=113||values[112]<=0)return;
        var blur=values[113];
        if(blur!=paletteGlowBlur){var filter=PaletteGlowNative.Blur(SKBlurStyle.Normal,blur/2,true);PaletteGlowNative.Set(paletteGlow.Handle,filter);PaletteGlowNative.Release(filter);paletteGlowBlur=blur;}
        paletteGlow.Color=new((byte)Math.Round(values[72]*255),(byte)Math.Round(values[73]*255),(byte)Math.Round(values[74]*255),(byte)Math.Round(Math.Clamp(values[112]*opacity,0,1)*255));
        canvas.DrawOval(box,paletteGlow);
    }
    private void DisposePaletteGlow(){paletteGlow.Dispose();}
}

internal static class PaletteGlowNative
{
    [DllImport("libSkiaSharp",EntryPoint="sk_maskfilter_new_blur",CallingConvention=CallingConvention.Cdecl)] internal static extern nint Blur(SKBlurStyle style,float sigma,[MarshalAs(UnmanagedType.I1)] bool respectCtm);
    [DllImport("libSkiaSharp",EntryPoint="sk_paint_set_maskfilter",CallingConvention=CallingConvention.Cdecl)] internal static extern void Set(nint paint,nint filter);
    [DllImport("libSkiaSharp",EntryPoint="sk_maskfilter_unref",CallingConvention=CallingConvention.Cdecl)] internal static extern void Release(nint filter);
}
