using Marrow.Core.Port.ModelsPalette;
using Marrow.Services.Port.ModelsPalette;
using Marrow.Services.Tests.Fixtures;
namespace Marrow.Services.Tests.Port.ModelsPalette;
[Trait("Decision","ED-NEW-modelspalette-11")]
public sealed class PaletteCompositionTests
{
    [Fact]
    public async Task Every_worker_and_lead_is_computed_on_the_actor_in_the_locked_v22_order()
    {
        using var roots=new ModelsPaletteRoots();using var fake=new PalettePipelineFixture();using var engine=new PaletteEngine(roots.New(),fake.Http,fake,fake.Time,fake.Pack);await engine.CheckNow();
        var composite=await engine.Compose([new("openai/gpt-6-astra","max"),new("acme/nova-1","low")],[new("openai/gpt-6-astra","high")],false);
        Assert.NotNull(composite);Assert.Equal(new[]{"acme/nova-1","openai/gpt-6-astra","openai/gpt-6-astra"},composite.Arrangement.Spans.Select(s=>s.ModelId));
        Assert.Equal(5/18d,composite.Arrangement.Spans[0].End,12);Assert.Equal(10/18d,composite.Arrangement.Spans[1].End,12);
        Assert.Equal(1024,composite.Shade.GradientRow.Count);
        for(var x=0;x<256;x++)Assert.Equal(Convert.FromHexString(composite.Arrangement.Sample((x+.5)/256)[1..]),composite.Shade.GradientRow.Skip(x*4).Take(3));
        var max=engine.Current.Resolve("openai/gpt-6-astra","max",false)!;var low=engine.Current.Resolve("acme/nova-1","low",false)!;var high=engine.Current.Resolve("openai/gpt-6-astra","high",false)!;
        foreach(var key in max.Motion.Keys)Assert.Equal((max.Motion[key]*5+low.Motion.GetValueOrDefault(key)*5+high.Motion[key]*8)/18,composite.Shade.Motion[key],12);
    }
}
