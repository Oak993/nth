using Avalonia.Media;
using Marrow.App.Port.ModelsPalette;
using Marrow.App.Views.Composer;
using Marrow.Services.Port.ModelsPalette;
using Marrow.Services.Tests.Fixtures;
namespace Marrow.App.Tests.Port.ModelsPalette;
[Trait("Decision","ED-NEW-modelspalette-17")]
public sealed class PaletteVisibleRendererTests
{
    [Fact]
    public async Task Every_visible_projection_comes_from_the_same_frozen_snapshot_in_both_appearances()
    {
        using var roots=new ModelsPaletteRoots();using var fake=new PalettePipelineFixture();using var engine=new PaletteEngine(roots.New(),fake.Http,fake,fake.Time,fake.Pack);await engine.CheckNow();
        foreach(var light in new[]{false,true})
        {
            var target=await engine.PrepareRender([], [new("acme/nova-1","low")],light,(s,w)=>PaletteTrackTarget.Create(s,[],[new("acme/nova-1","low")],light,w!));
            var shade=engine.Current.Resolve("acme/nova-1","low",light)!;
            Assert.Equal(Color.Parse(shade.Title),target.TitleStops.Single().Color);
            Assert.Equal(Color.Parse(shade.Glow),target.GlowStops.Single().Color);
            Assert.Equal(JewelColor.Parse(shade.Stops[1]),target.Jewel.Liquid);
            Assert.Equal(shade.GradientRow[512],target.Gradient[128].Red);
            Assert.True(ModelShades.ApplyToUi);
            var pair=await engine.PrepareRender([new("acme/nova-1","high")],[new("openai/gpt-6-astra","low")],light,(s,w)=>PaletteTrackTarget.Create(s,[new("acme/nova-1","high")],[new("openai/gpt-6-astra","low")],light,w!));
            Assert.Equal(2,pair.TitleStops.Count);Assert.Equal(2,pair.GlowStops.Count);
            Assert.Equal(5d/13,pair.TitleStops[0].End,12);Assert.Equal(1,pair.TitleStops[1].End);
            Assert.Equal(Color.Parse(engine.Current.Resolve("openai/gpt-6-astra","low",light)!.Title),pair.TitleStops[1].Color);
        }
    }
    [Fact]
    public async Task The_real_composer_light_uses_the_snapshot_heart_pair_instead_of_router_purple()
    {
        using var roots=new ModelsPaletteRoots();using var fake=new PalettePipelineFixture();using var engine=new PaletteEngine(roots.New(),fake.Http,fake,fake.Time,fake.Pack);await engine.CheckNow();
        var palette=await engine.PrepareRender([], [new("acme/nova-1","medium")],false,(s,_)=>PaletteVisibleProjection.Composer(s,[],[new("acme/nova-1","medium")],"#123456","#654321"));
        var dark=engine.Current.Resolve("acme/nova-1","medium",false)!;var light=engine.Current.Resolve("acme/nova-1","medium",true)!;
        Assert.Equal(dark.Stops[1],palette.MakerHeartDark);Assert.Equal(dark.Stops[2],palette.MakerSecondDark);
        Assert.Equal(light.Stops[0],palette.MakerHeartLight);Assert.Equal(light.Stops[1],palette.MakerSecondLight);
        Assert.Equal("#123456",palette.AccentDark);Assert.Equal("#654321",palette.AccentLight);
    }
}
