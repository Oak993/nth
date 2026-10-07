using Marrow.Services.Port.ModelsPalette;
using Marrow.Services.Tests.Fixtures;
namespace Marrow.Services.Tests.Port.ModelsPalette;
[Trait("Decision","ED-NEW-modelspalette-11")]
public sealed class PaletteMakerConcurrencyTests
{
    [Fact]
    public async Task Two_makers_progress_in_parallel_and_source_results_still_publish_in_priority_order()
    {
        using var roots=new ModelsPaletteRoots();using var fake=new PalettePipelineFixture();using var transport=new TwoMakerPaletteTransport(fake);
        using var engine=new PaletteEngine(roots.New(),transport,fake,fake.Time,fake.Pack);
        var check=engine.CheckNow();
        try{await transport.BothStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));Assert.Equal(2,transport.Peak);}
        finally{transport.Release.TrySetResult();}
        await check;Assert.Equal(PaletteState.Ready,engine.Current.Models["acme/nova-1"].State);Assert.Equal(PaletteState.Ready,engine.Current.Models["beta/nova-1"].State);
        Assert.Equal(engine.Current.Models["acme/nova-1"].Palette.Stops,engine.Current.Models["beta/nova-1"].Palette.Stops);
    }
}
