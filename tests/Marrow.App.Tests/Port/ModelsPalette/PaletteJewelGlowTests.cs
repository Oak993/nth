using Marrow.App.Views.Composer;
using SkiaSharp;
namespace Marrow.App.Tests.Port.ModelsPalette;
[Trait("Decision","ED-NEW-modelspalette-18")]
public sealed class PaletteJewelGlowTests
{
    [Fact]
    public void The_palette_glow_draws_its_real_alpha_and_blur_without_managed_objects_per_frame()
    {
        using var bitmap=new SKBitmap(96,96);using var canvas=new SKCanvas(bitmap);var renderer=JewelKnobRenderer.For(null,64);var values=new JewelUniforms().Values;
        values[72]=.2f;values[73]=.7f;values[74]=.4f;values[112]=.6f;values[113]=8;
        var box=new SKRect(24,24,72,72);renderer.Shadows(canvas,values,box,1);
        Assert.NotEqual(SKColors.Transparent,bitmap.GetPixel(48,48));
        var before=GC.GetAllocatedBytesForCurrentThread();
        for(var i=0;i<1000;i++){values[113]=4+i%100/25f;renderer.Shadows(canvas,values,box,1);}
        Assert.Equal(before,GC.GetAllocatedBytesForCurrentThread());Assert.Equal(27*16,JewelUniforms.ShaderBytes);
    }
}
