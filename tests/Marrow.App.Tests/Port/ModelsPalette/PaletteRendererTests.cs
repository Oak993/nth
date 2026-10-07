using Marrow.App.Port.ModelsPalette;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.ModelsPalette;
using Marrow.Services.Port.ModelsPalette;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;
namespace Marrow.App.Tests.Port.ModelsPalette;
[Trait("Decision","ED-NEW-modelspalette-11")]
public sealed class PaletteRendererTests
{
    [Fact]
    public async Task Retained_renderer_reads_one_effect_clock_and_every_v22_weight_without_frame_allocations()
    {
        using var roots=new ModelsPaletteRoots();using var fake=new PalettePipelineFixture();
        using var engine=new PaletteEngine(roots.New(),fake.Http,fake,fake.Time,fake.Pack);await engine.CheckNow();
        var renderer=new PaletteRenderer();var snapshot=engine.Current;
        var selected=new[]{new PaletteRenderSelection("openai/gpt-6-astra","max"),new PaletteRenderSelection("acme/nova-1","low")};
        renderer.Retarget(snapshot,selected.Take(1).ToArray(),[],false,0);renderer.Retarget(snapshot,selected,selected.Take(1).ToArray(),false,1);
        var row=new byte[1024];var motion=new double[renderer.MotionCount];
        var halfway=renderer.Sample(1.6,row,motion);Assert.InRange(halfway.Progress,.499999,.500001);
        Assert.NotEqual(0u,halfway.Title);Assert.NotEqual(0u,halfway.Glow);
        var displayed=row.ToArray();renderer.Retarget(snapshot,selected.Reverse().ToArray(),[],false,1.6);renderer.Sample(1.6,row,motion);Assert.Equal(displayed,row);
        renderer.Sample(2.8,row,motion);Assert.All(Enumerable.Range(0,256),x=>Assert.Equal(255,row[x*4+3]));
        var before=GC.GetAllocatedBytesForCurrentThread();for(var i=0;i<10000;i++)renderer.Sample(3+i/60d,row,motion);Assert.Equal(before,GC.GetAllocatedBytesForCurrentThread());
        renderer.Retarget(snapshot,selected,[],false,4,reduceMotion:true);Assert.Equal(1,renderer.Sample(4,row,motion).Progress);
    }
}
