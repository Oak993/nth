using System.Security.Cryptography;
using Marrow.Core.Port.ModelsPalette;
using Marrow.Services.Port.ModelsPalette;
using Marrow.Services.Tests.Fixtures;
namespace Marrow.Services.Tests.Port.ModelsPalette;
[Trait("Decision","ED-NEW-modelspalette-11")]
public sealed class PalettePackAdoptionTests
{
    [Fact]
    public async Task Signed_forward_pack_replaces_local_authority_and_reset_keeps_pack_colours()
    {
        using var fake=new PalettePipelineFixture();using var roots=new ModelsPaletteRoots();var first=PaletteReleasePack.Bake(fake.Pack,1,[]);
        using var engine=new PaletteEngine(roots.New(),fake.Http,fake,fake.Time,fake.Pack,releasePack:first);await engine.CheckNow();
        var old=engine.Current.Models["acme/nova-1"].Palette;
        var seed=DominantColour.Extract(fake.Decode([1],"image/png")!);var evidence=new PaletteSeedEvidence("https://cdn-avatars.huggingface.co/acme.png",Convert.ToHexString(SHA256.HashData([1])),seed.Rgba64.ToArray());
        var next=PaletteReleasePack.Bake(fake.Pack,2,[new("acme",seed.Seed!,[evidence])],first);
        await Assert.ThrowsAsync<InvalidDataException>(()=>engine.AdoptPack(next.Encode(),_=>false));Assert.Equal(old,engine.Current.Models["acme/nova-1"].Palette);
        await engine.AdoptPack(next.Encode(),_=>true);Assert.NotEqual(old,engine.Current.Models["acme/nova-1"].Palette);
        var expected=engine.Current.Models["acme/nova-1"].Palette;var calls=fake.Http.Received.Count;
        await engine.ResetLocalColours();Assert.Equal(expected,engine.Current.Models["acme/nova-1"].Palette);Assert.Equal(calls,fake.Http.Received.Count);
        await Assert.ThrowsAsync<InvalidDataException>(()=>engine.AdoptPack(first.Encode(),_=>true));
    }
}
