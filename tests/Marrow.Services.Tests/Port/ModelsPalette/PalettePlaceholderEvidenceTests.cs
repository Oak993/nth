using System.Security.Cryptography;
using System.Text.Json;
using Marrow.Services.Port.ModelsPalette;
using Marrow.Services.Tests.Fixtures;
namespace Marrow.Services.Tests.Port.ModelsPalette;
[Trait("Decision","ED-NEW-modelspalette-11")]
public sealed class PalettePlaceholderEvidenceTests
{
    [Fact]
    public void Placeholder_bake_retains_actual_hash_size_url_and_fetch_date_through_the_pack()
    {
        using var roots=new ModelsPaletteRoots();using var fake=new PalettePipelineFixture();var root=roots.New();Directory.CreateDirectory(root);
        var artifact=Path.Combine(root,"placeholder.png");byte[] bytes=[137,80,78,71,13,10,26,10,0];File.WriteAllBytes(artifact,bytes);
        var manifest=Path.Combine(root,"manifest.json");var when=DateTimeOffset.Parse("2026-10-07T06:20:00Z",System.Globalization.CultureInfo.InvariantCulture);const string url="https://cdn-avatars.huggingface.co/placeholder.png";
        File.WriteAllText(manifest,JsonSerializer.Serialize(new {version=1,images=Array.Empty<object>(),placeholders=new[]{new {kind="hf",path=artifact,url,fetchedAt=when}}}));
        var baked=PaletteBake.FromManifest(manifest,fake,fake.Pack);var read=PaletteReleasePack.Read(baked.Encode());
        var evidence=Assert.Single(read.PlaceholderEvidence);Assert.Equal("hf",evidence.Kind);Assert.Equal(url,evidence.Source);Assert.Equal(when,evidence.FetchedAt);
        Assert.Equal(bytes.LongLength,evidence.ByteLength);Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)),evidence.Sha256);
        Assert.Equal(evidence.Sha256,Assert.Single(read.PlaceholderHashes["hf"]));
    }
}
