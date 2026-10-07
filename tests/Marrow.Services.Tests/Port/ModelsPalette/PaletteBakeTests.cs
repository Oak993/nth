using System.Security.Cryptography;
using System.Text.Json;
using Marrow.Services.Port.ModelsPalette;
using Marrow.Services.Tests.Fixtures;
namespace Marrow.Services.Tests.Port.ModelsPalette;
[Trait("Decision","ED-NEW-modelspalette-11")]
public sealed class PaletteBakeTests
{
    [Fact]
    public void Offline_bake_computes_source_hash_size_date_and_resample_from_the_actual_artifact()
    {
        using var roots=new ModelsPaletteRoots();using var fake=new PalettePipelineFixture();var root=roots.New();Directory.CreateDirectory(root);var logo=Path.Combine(root,"logo.png");byte[] bytes=[137,80,78,71,13,10,26,10,0];File.WriteAllBytes(logo,bytes);
        var manifest=Path.Combine(root,"manifest.json");var when=DateTimeOffset.Parse("2026-10-07T06:20:00Z",System.Globalization.CultureInfo.InvariantCulture);
        File.WriteAllText(manifest,JsonSerializer.Serialize(new {version=1,images=new[]{new {maker="acme",path=logo,url="https://cdn-avatars.huggingface.co/acme.png",fetchedAt=when,type="image/png"}},placeholders=Array.Empty<object>()}));
        var pack=PaletteBake.FromManifest(manifest,fake,fake.Pack);var evidence=Assert.Single(pack.Seeds["acme"].Evidence);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)),evidence.Sha256);Assert.Equal(bytes.Length,evidence.ByteLength);Assert.Equal(when,evidence.FetchedAt);Assert.Equal(16384,evidence.Rgba64.Length);
        Assert.Equal(pack.Seeds["acme"].Seed,Marrow.Core.Port.ModelsPalette.DominantColour.Extract(new(64,64,evidence.Rgba64)).Seed);
    }
}
