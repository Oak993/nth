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
        // ED-NEW-models-palette-lead-1 (3): the shipped pack is the audited owner-PC release bake, byte for byte (its SHA256SUMS line),
        // with every source model table still present, the 43 captured seeds and the two placeholder identities.
        using(var raw=typeof(PaletteReleasePack).Assembly.GetManifestResourceStream("Marrow.Services.ModelsPalette.Pack1.gz")!)
            Assert.Equal("53683576E50180D1A054417578AD54F5606F52249180F5D166C740A42ED5631F",Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(raw)));
        Assert.Empty(PaletteSourcePack.Default.Baseline.Where(f=>f.Eligible).Select(f=>f.Id).Distinct(StringComparer.Ordinal).Except(pack.Snapshot0.Models.Keys,StringComparer.Ordinal));
        Assert.Equal(43,pack.Seeds.Count);Assert.Equal(2,pack.PlaceholderHashes.Count);Assert.All(pack.PlaceholderHashes.Values,h=>Assert.NotEmpty(h));
        using var roots=new ModelsPaletteRoots();using var fake=new PalettePipelineFixture();
        using var engine=new PaletteEngine(roots.New(),fake.Http,fake,releasePack:pack);
        Assert.Same(pack.Snapshot0,engine.Current);Assert.Empty(fake.Http.Received);
        await engine.Prime();Assert.Same(pack.Snapshot0,engine.Current);Assert.Empty(fake.Http.Received);
        var shade=engine.Current.Resolve("openai/gpt-6-astra","max",false);Assert.NotNull(shade);Assert.Equal(1024,shade.GradientRow.Count);
    }
}
