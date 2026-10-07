using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using Marrow.Services.Port.ModelsPalette;
using Marrow.Services.Tests.Fixtures;
namespace Marrow.Services.Tests.Port.ModelsPalette;
[Trait("Decision","ED-NEW-modelspalette-19")]
public sealed class PaletteLiveBakeTests
{
    [Fact]
    public async Task Capture_uses_the_real_pipeline_and_preserves_each_raw_response_date_url_and_digest()
    {
        using var roots=new ModelsPaletteRoots();using var fake=new PalettePipelineFixture{ImageVersion=1};using var transport=new PaletteCaptureFixture(fake);
        var folder=roots.New();var pack=await PaletteLiveBake.Capture(folder,transport,fake,fake.Time,fake.Pack);
        var seed=Assert.Single(pack.Seeds.Values);Assert.Equal("acme",seed.Maker);
        var image=Assert.Single(seed.Evidence,e=>e.Rgba64.Length>0);Assert.Equal(9,image.ByteLength);Assert.NotNull(image.FetchedAt);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(new byte[]{137,80,78,71,13,10,26,10,1})),image.Sha256);
        Assert.Equal("https://cdn-avatars.huggingface.co/v1/acme.png",image.Source);
        Assert.Equal(3,pack.PlaceholderEvidence.Count);Assert.NotNull(pack.Market);
        using var stream=File.OpenRead(Path.Combine(folder,"pack.v1.json.gz"));Assert.Equal(pack.Snapshot0.Digest,PaletteReleasePack.ReadCompressed(stream).Snapshot0.Digest);
        Assert.True(File.Exists(Path.Combine(folder,"capture.json")));Assert.True(File.Exists(Path.Combine(folder,"SHA256SUMS")));
    }
    [Fact]
    public async Task A_failed_official_catalogue_never_writes_a_release_pack()
    {
        using var roots=new ModelsPaletteRoots();using var fake=new PalettePipelineFixture{FailModels=true};using var transport=new PaletteCaptureFixture(fake);var folder=roots.New();
        await Assert.ThrowsAsync<InvalidDataException>(()=>PaletteLiveBake.Capture(folder,transport,fake,fake.Time,fake.Pack));
        Assert.False(File.Exists(Path.Combine(folder,"pack.v1.json.gz")));
    }
}
