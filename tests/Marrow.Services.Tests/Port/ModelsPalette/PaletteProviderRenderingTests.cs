using Marrow.Core.Port.ModelsPalette;
using Marrow.Services.Port.ModelsPalette;
using Marrow.Services.Tests.Fixtures;
namespace Marrow.Services.Tests.Port.ModelsPalette;
[Trait("Decision","ED-NEW-modelspalette-14")]
public sealed class PaletteProviderRenderingTests
{
    [Fact]
    public async Task Harness_choices_use_the_actual_global_member_and_prepare_every_row_off_the_UI_thread()
    {
        using var roots=new ModelsPaletteRoots();using var fake=new PalettePipelineFixture();using var engine=new PaletteEngine(roots.New(),fake.Http,fake,fake.Time,fake.Pack);await engine.CheckNow();
        var actual=engine.Current.Resolve("openai/gpt-6-astra","high",false)!;
        var composed=await engine.PrepareProviderRender([new("codex","gpt-6-astra","GPT-6 Astra","high")],[],false,(snapshot,workers,leads,composite)=>
        {
            Assert.Equal(ThreadPriority.BelowNormal,Thread.CurrentThread.Priority);Assert.Equal(new PaletteRenderSelection("openai/gpt-6-astra","high"),Assert.Single(workers));Assert.Empty(leads);
            Assert.Same(actual,snapshot.Resolve(workers[0].Id,workers[0].Effort,false));Assert.NotNull(composite);Assert.Same(actual,composite!.Shade);return composite;
        });
        Assert.Equal("openai/gpt-6-astra",Assert.Single(composed!.Arrangement.Spans).ModelId);
    }
}
