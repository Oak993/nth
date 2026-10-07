using Marrow.Services.Port.ModelsPalette;
using Marrow.Services.Tests.Fixtures;
namespace Marrow.Services.Tests.Port.ModelsPalette;
// PORT-ONLY: AC-3 3A.2, reads provider catalogues without loading or changing global membership; ED-NEW-models-palette-6.
[Trait("Decision","ED-NEW-modelspalette-6")]
public sealed class PaletteProviderCatalogueTests
{
    [Fact]
    public async Task Native_provider_ids_are_placed_by_identity_without_becoming_lineup_facts()
    {
        using var roots=new ModelsPaletteRoots();using var fake=new PalettePipelineFixture();
        using var engine=new PaletteEngine(roots.New(),fake.Http,fake,fake.Time,fake.Pack);
        await engine.CheckNow();var before=engine.Current;var requests=fake.Http.Received.Count;
        await engine.ObserveCatalogues([new("codex","gpt-6-astra","Astra"),new("cursor","unlisted-model","Unlisted model")]);
        Assert.Equal(requests,fake.Http.Received.Count);
        Assert.Equal(before.Models["openai/gpt-6-astra"].Position,engine.Current.Models["openai/gpt-6-astra"].Position);
        Assert.Equal("cursor",engine.Current.Models["cursor/unlisted-model"].Maker);
        Assert.Equal("Unlisted model",engine.Current.Models["cursor/unlisted-model"].Name);
        Assert.Equal(before.Models.Count+1,engine.Current.Models.Count);
    }
}
