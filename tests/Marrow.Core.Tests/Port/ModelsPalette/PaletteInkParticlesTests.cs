using Marrow.Core.Port.ModelsPalette;
namespace Marrow.Core.Tests.Port.ModelsPalette;
[Trait("Decision","ED-NEW-modelspalette-18")]
public sealed class PaletteInkParticlesTests
{
    [Fact]
    public void Tracers_use_the_v22_spawn_life_radius_and_thin_front_visibility_formula()
    {
        var fluid=new PaletteInk(480,20);var particles=new PaletteInkParticles(false,()=>.5);
        particles.Advance(fluid,1/60d,false);
        Assert.Equal(7,particles.Count);var first=particles.At(0);
        Assert.Equal(477,first.X);Assert.Equal(11,first.Y);Assert.Equal(4-1/60d,first.Life,6);Assert.Equal(1.05,first.Radius,6);
        Assert.Equal(0,PaletteInkParticles.Visibility(0));Assert.Equal(.8,PaletteInkParticles.Visibility(-Math.Log(.8)/1.6),12);
        Assert.Equal(0,PaletteInkParticles.Visibility(-Math.Log(.05)/1.6));
        particles.Advance(fluid,1/60d,true);Assert.Equal(7,particles.Count);
        var lite=new PaletteInkParticles(true,()=>.5);lite.Advance(new PaletteInk(240,10),1/60d,false);Assert.Equal(3,lite.Count);
    }
}
