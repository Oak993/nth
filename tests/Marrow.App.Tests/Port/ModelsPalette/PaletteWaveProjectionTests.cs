using Marrow.App.Port.ModelsPalette;
namespace Marrow.App.Tests.Port.ModelsPalette;
[Trait("Decision","ED-NEW-modelspalette-21")]
public sealed class PaletteWaveProjectionTests
{
    [Fact]
    public void Effort_scales_the_locked_wave_amplitude_and_keeps_the_SVG_viewbox_height_fixed()
    {
        Assert.Equal(3,PaletteWaveProjection.FastAmplitude(18,false,false));Assert.Equal(5,PaletteWaveProjection.FastAmplitude(30,false,false));
        Assert.Equal(10,PaletteWaveProjection.FastAmplitude(30,true,true));Assert.Equal(3.6,PaletteWaveProjection.FastAmplitude(18,true,false),12);
        Assert.Equal(30,PaletteWaveProjection.ViewBox(false));Assert.Equal(40,PaletteWaveProjection.ViewBox(true));
    }
}
