using System.Security.Cryptography;
using Marrow.Core.Port.ModelsPalette;
using Marrow.Services.Port.ModelsPalette;
using Marrow.Services.Tests.Fixtures;
namespace Marrow.Services.Tests.Port.ModelsPalette;
[Trait("Decision","ED-NEW-modelspalette-11")]
public sealed class PaletteLockedStoreTests
{
    [Fact]
    public void A_locked_store_is_retried_after_unlock_and_does_not_become_permanently_read_only_or_corrupt()
    {
        using var roots=new ModelsPaletteRoots();using var fake=new PalettePipelineFixture();var store=new PaletteStore(roots.New());var colour=DominantColour.Extract(fake.Decode([0],"image/png")!);
        var seed=new StoredSeed(new string('A',64),colour.Seed!,[new("https://cdn-avatars.huggingface.co/acme.png",Convert.ToHexString(SHA256.HashData([0])),colour.Rgba64.ToArray())],20000);
        store.SaveSeeds(new(){["acme"]=seed});
        using(var locked=new FileStream(store.SeedsPath,FileMode.Open,FileAccess.Read,FileShare.None))Assert.Empty(store.LoadSeeds());
        Assert.Equal(seed.Seed,store.LoadSeeds()["acme"].Seed);Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(store.SeedsPath)!,"*.corrupt-*"));
        store.SaveSeeds(new(){["acme"]=seed with {Day=20001}});Assert.Equal(20001,new PaletteStore(Path.GetDirectoryName(Path.GetDirectoryName(store.SeedsPath))!).LoadSeeds()["acme"].Day);
    }
}
