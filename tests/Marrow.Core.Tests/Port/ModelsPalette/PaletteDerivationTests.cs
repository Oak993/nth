using Marrow.Core.Port.ModelsPalette;

namespace Marrow.Core.Tests.Port.ModelsPalette;

// PORT-ONLY: automatic extraction, nearest-free search and locked v22 blend; ED-NEW-models-palette-6.
[Trait("Decision", "ED-NEW-modelspalette-6")]
public sealed class PaletteDerivationTests
{
    [Fact]
    public void Logo_seed_uses_integer_grid_and_recomputes_from_its_64_by_64_evidence()
    {
        var rgba=Enumerable.Range(0,96*48).SelectMany(_=>new byte[]{204,107,71,255}).ToArray();
        var result=DominantColour.Extract(new LogoPixels(96,48,rgba));
        Assert.Equal(64*64*4,result.Rgba64.Count);
        Assert.Equal(new ColourSeed(false,127,27,41),result.Seed);
        Assert.Equal(result.Seed,DominantColour.Extract(new LogoPixels(64,64,result.Rgba64.ToArray())).Seed);
        Assert.True(result.Decisive);
    }
    [Fact]
    public void Transparent_and_tiny_images_are_misses_and_white_tiles_are_mono()
    {
        Assert.False(DominantColour.Extract(new LogoPixels(16,16,new byte[16*16*4])).Decisive);
        Assert.False(DominantColour.Extract(new LogoPixels(64,64,new byte[64*64*4])).Decisive);
        var white=Enumerable.Repeat((byte)255,64*64*4).ToArray();
        Assert.True(DominantColour.Extract(new LogoPixels(64,64,white)).Seed!.Mono);
    }
    [Fact]
    public void Collision_search_is_order_free_and_does_not_see_other_post_pack_makers()
    {
        var locked=PaletteSourcePack.Default.Palettes.Values.ToArray();
        var seed=new ColourSeed(false,125,23,43);
        var a=PaletteDeriver.Derive(seed,locked);var b=PaletteDeriver.Derive(seed,locked.Reverse().ToArray());
        Assert.Equal(a.Palette,b.Palette); Assert.False(a.Crowded);
        foreach(var r in locked)Assert.True(PaletteMath.Distance(PaletteMath.Lab(a.Palette.Stops[1]),PaletteMath.Lab(r.Stops[1]))>=.060);
        Assert.InRange(PaletteMath.Lab(a.Palette.Stops[1]).L,0,.80);
    }
    [Fact]
    public void The_v22_blend_reverses_workers_and_leads_and_holds_each_shaded_heart_over_its_middle_third()
    {
        var a=PaletteSourcePack.Default.Palettes["Claude"];var b=PaletteSourcePack.Default.Palettes["Silver"];
        var blend=PaletteArrangement.Create([new("lead",true,a.Stops),new("worker-a",false,b.Stops),new("worker-b",false,a.Stops)]);
        Assert.Equal(["worker-b","worker-a","lead"],blend.Spans.Select(s=>s.ModelId));
        Assert.Equal(1/3.6,blend.Spans[0].End,12);
        Assert.Equal(a.Stops[1],blend.Stops[0].Color);
        Assert.Equal(Math.Round(100*(1/3.6)/3,1)/100,blend.Stops[1].Position);
        Assert.Equal(1,blend.Stops[^1].Position);
        Assert.NotEqual(b.Stops[1],blend.Sample(.50));
    }
}
