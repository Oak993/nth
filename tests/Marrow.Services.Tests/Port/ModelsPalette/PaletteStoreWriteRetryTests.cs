using System.Security.Cryptography;
using Marrow.Core.Port.ModelsPalette;
using Marrow.Services.Port.ModelsPalette;
using Marrow.Services.Tests.Fixtures;
namespace Marrow.Services.Tests.Port.ModelsPalette;
[Trait("Decision","ED-NEW-modelspalette-11")]
public sealed class PaletteStoreWriteRetryTests
{
    [Fact]
    public void A_locked_write_keeps_the_current_payload_and_retries_atomically_after_unlock()
    {
        using var roots=new ModelsPaletteRoots();using var fake=new PalettePipelineFixture();var store=new PaletteStore(roots.New());var colour=DominantColour.Extract(fake.Decode([0],"image/png")!);
        var seed=new StoredSeed(new string('A',64),colour.Seed!,[new("https://cdn-avatars.huggingface.co/acme.png",Convert.ToHexString(SHA256.HashData([0])),colour.Rgba64.ToArray())],20000);
        store.SaveSeeds(new(){["acme"]=seed});
        using(var locked=new FileStream(store.SeedsPath,FileMode.Open,FileAccess.Read,FileShare.None))Assert.Throws<IOException>(()=>store.SaveSeeds(new(){["acme"]=seed with {Day=20001}}));
        store.RetryPendingWrites();Assert.Equal(20001,store.LoadSeeds()["acme"].Day);Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(store.SeedsPath)!,"*.corrupt-*"));
    }
}
