using Marrow.Core.Port.ModelsPalette;
using Marrow.Services.Port.ModelsPalette;
using Marrow.Services.Tests.Fixtures;
namespace Marrow.Services.Tests.Port.ModelsPalette;
[Trait("Decision","ED-NEW-modelspalette-11")]
public sealed class PaletteEmbeddedPackTests
{
    [Fact]
    public async Task The_shipped_pack_loads_before_disk_or_network_and_contains_every_source_model_table()
    {
        var pack=PaletteReleasePack.ReadEmbedded();
        Assert.Equal(PaletteSourcePack.Default.Baseline.Where(f=>f.Eligible).Select(f=>f.Id).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal),pack.Snapshot0.Models.Keys.Order(StringComparer.Ordinal));
        Assert.Empty(pack.Seeds);Assert.Empty(pack.PlaceholderHashes);
        using var roots=new ModelsPaletteRoots();using var fake=new PalettePipelineFixture();
        using var engine=new PaletteEngine(roots.New(),fake.Http,fake,releasePack:pack);
        Assert.Same(pack.Snapshot0,engine.Current);Assert.Empty(fake.Http.Received);
        await engine.Prime();Assert.Same(pack.Snapshot0,engine.Current);Assert.Empty(fake.Http.Received);
        var shade=engine.Current.Resolve("openai/gpt-6-astra","max",false);Assert.NotNull(shade);Assert.Equal(1024,shade.GradientRow.Count);
    }
}
