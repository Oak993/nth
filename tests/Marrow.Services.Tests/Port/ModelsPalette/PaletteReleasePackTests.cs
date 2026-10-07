using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Marrow.Core.Port.ModelsPalette;
using Marrow.Services.Port.ModelsPalette;
using Marrow.Services.Tests.Fixtures;
namespace Marrow.Services.Tests.Port.ModelsPalette;
// PORT-ONLY: AC-3 PLAN 5.5/5.6, real pack validation over synthetic evidence only.
[Trait("Decision","ED-NEW-modelspalette-11")]
public sealed class PaletteReleasePackTests
{
    [Fact]
    public void Pack_roundtrip_verifies_every_table_and_rejects_tampering_and_future_engines()
    {
        using var fake=new PalettePipelineFixture();
        var pack=PaletteReleasePack.Bake(fake.Pack,1,[]);
        var bytes=pack.Encode();var read=PaletteReleasePack.Read(bytes);
        Assert.Equal(pack.Snapshot0.Digest,read.Snapshot0.Digest);
        Assert.Equal(pack.Snapshot0.Resolve("openai/gpt-6-astra","max",false),read.Snapshot0.Resolve("openai/gpt-6-astra","max",false));
        Assert.Equal("AC-3",read.MinimumEngine);Assert.Equal("S2",read.SeedVersion);Assert.Equal("D3",read.DeriveVersion);
        var root=JsonNode.Parse(bytes)!;root["data"]!["version"]=2;
        Assert.Throws<InvalidDataException>(()=>PaletteReleasePack.Read(System.Text.Encoding.UTF8.GetBytes(root.ToJsonString())));
        Assert.Throws<InvalidDataException>(()=>PaletteReleasePack.Bake(fake.Pack,1,[],minimumEngine:"AC-4"));
    }
    [Fact]
    public void Baked_evidence_and_epochs_are_append_only_and_post_pack_reservations_do_not_move()
    {
        using var fake=new PalettePipelineFixture();var pixels=fake.Decode([0],"image/png")!;
        var seed=DominantColour.Extract(pixels);var evidence=new PaletteSeedEvidence("https://cdn-avatars.huggingface.co/acme.png",Convert.ToHexString(SHA256.HashData([0])),seed.Rgba64.ToArray());
        var first=PaletteReleasePack.Bake(fake.Pack,1,[new PalettePackSeed("acme",seed.Seed!,[evidence])]);
        Assert.Equal(1,first.Seeds["acme"].Epoch);
        var beta=DominantColour.Extract(fake.Decode([1],"image/png")!);
        var betaEvidence=evidence with {Sha256=Convert.ToHexString(SHA256.HashData([1])),Rgba64=beta.Rgba64.ToArray()};
        var second=PaletteReleasePack.Bake(fake.Pack,2,[new PalettePackSeed("beta",beta.Seed!,[betaEvidence])],first);
        Assert.Equal(first.Seeds["acme"],second.Seeds["acme"]);Assert.Equal(2,second.Seeds["beta"].Epoch);
        Assert.Equal(first.FrozenReserved,second.FrozenReserved);
        Assert.Equal(PaletteDeriver.Derive(new(false,120,25,280),first.FrozenReserved),PaletteDeriver.Derive(new(false,120,25,280),second.FrozenReserved));
        Assert.Throws<InvalidDataException>(()=>PaletteReleasePack.Bake(fake.Pack,2,[new PalettePackSeed("bad",seed.Seed!,[evidence with {Sha256="invented"}])],first));
    }
    [Fact]
    public async Task Pack_authority_prevents_network_and_prime_restores_the_last_displayed_snapshot()
    {
        using var roots=new ModelsPaletteRoots();using var fake=new PalettePipelineFixture();
        var seed=DominantColour.Extract(fake.Decode([0],"image/png")!);
        var evidence=new PaletteSeedEvidence("https://cdn-avatars.huggingface.co/acme.png",Convert.ToHexString(SHA256.HashData([0])),seed.Rgba64.ToArray());
        var pack=PaletteReleasePack.Bake(fake.Pack,1,[new PalettePackSeed("acme",seed.Seed!,[evidence])]);var root=roots.New();string digest;
        using(var engine=new PaletteEngine(root,fake.Http,fake,fake.Time,fake.Pack,releasePack:pack))
        {
            await engine.Prime();await engine.CheckNow();digest=engine.Current.Digest;
            Assert.Equal(PaletteState.Ready,engine.Current.Models["acme/nova-1"].State);
            Assert.DoesNotContain(fake.Http.Received,r=>r.Url.Host.Contains("huggingface",StringComparison.Ordinal));
        }
        using var restarted=new PaletteEngine(root,fake.Http,fake,fake.Time,fake.Pack,releasePack:pack);
        await restarted.Prime();Assert.Equal(digest,restarted.Current.Digest);
        Assert.Equal(PaletteState.Known,restarted.Current.Models["acme/nova-1"].State);
    }
}
