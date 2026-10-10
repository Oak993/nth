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
    public void Embedded_pack_does_not_leave_multi_megabyte_json_buffers_in_the_shared_pool()
    {
        using var buffers = new PaletteJsonPoolListener();
        var pack = PaletteReleasePack.ReadEmbedded();
        Assert.NotEmpty(pack.Snapshot0.Models);
        Assert.InRange(buffers.LargestRental, 1, 4 * 1024 * 1024 - 1);
    }

    [Fact]
    public void Persisting_and_restoring_snapshot_does_not_pool_multi_megabyte_json_buffers()
    {
        var pack = PaletteReleasePack.ReadEmbedded();
        using var roots = new ModelsPaletteRoots();
        var store = new PaletteStore(roots.New());
        using var buffers = new PaletteJsonPoolListener();
        store.SaveSnapshot(pack.Snapshot0);
        var restored = Assert.IsType<PaletteSnapshot>(store.LoadSnapshot());
        Assert.Equal(pack.Snapshot0.Digest, restored.Digest);
        Assert.Equal(pack.Snapshot0.Shades.Count, restored.Shades.Count);
        Assert.InRange(buffers.LargestRental, 1, 4 * 1024 * 1024 - 1);
    }

    [Fact]
    public void Checksum_uses_exact_raw_data_bytes_including_whitespace_and_unicode_escapes()
    {
        using var fake = new PalettePipelineFixture();
        var pack = PaletteReleasePack.Bake(fake.Pack, 1, []);
        var root = JsonNode.Parse(pack.Encode())!;
        root["data"]!["ignoredUnicode"] = "café 雪";
        var body = root["data"]!.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        var hash = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(body)));
        var envelope = System.Text.Encoding.UTF8.GetBytes("{\"ignored\": [1,{}], \"data\":" + body + ", \"hash\":\"" + hash + "\",\"schema\":1}");
        Assert.Equal(pack.Snapshot0.Digest, PaletteReleasePack.Read(envelope).Snapshot0.Digest);
        var changed = System.Text.Encoding.UTF8.GetBytes(System.Text.Encoding.UTF8.GetString(envelope).Replace("\"data\":{", "\"data\":{ ", StringComparison.Ordinal));
        Assert.Throws<InvalidDataException>(() => PaletteReleasePack.Read(changed));
    }

    [Fact]
    public void Duplicate_and_escaped_envelope_properties_use_the_last_value()
    {
        using var fake = new PalettePipelineFixture();
        var pack = PaletteReleasePack.Bake(fake.Pack, 1, []);
        var root = JsonNode.Parse(pack.Encode())!;
        var data = root["data"]!.ToJsonString();
        var hash = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(data)));
        var bytes = System.Text.Encoding.UTF8.GetBytes("{\"schema\":99,\"hash\":\"wrong\",\"data\":null,\"sch\\u0065ma\":1,\"h\\u0061sh\":"
            + System.Text.Json.JsonSerializer.Serialize(hash) + ",\"d\\u0061ta\":" + data + "}");
        Assert.Equal(pack.Snapshot0.Digest, PaletteReleasePack.Read(bytes).Snapshot0.Digest);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("{\"schema\":1")]
    [InlineData("{}{}")]
    [InlineData("{\"ignored\":[1,]}")]
    public void Invalid_envelopes_are_rejected(string json)
    {
        Assert.Throws<InvalidDataException>(() => PaletteReleasePack.Read(System.Text.Encoding.UTF8.GetBytes(json)));
    }

    [Fact]
    public void A_future_snapshot_is_preserved_byte_for_byte_when_loading_and_saving()
    {
        using var fake = new PalettePipelineFixture();
        using var roots = new ModelsPaletteRoots();
        var pack = PaletteReleasePack.Bake(fake.Pack, 1, []);
        var store = new PaletteStore(roots.New());
        store.SaveSnapshot(pack.Snapshot0);
        var path = Path.Combine(Path.GetDirectoryName(store.SeedsPath)!, "snapshot-v1.json");
        var root = JsonNode.Parse(File.ReadAllBytes(path))!;
        root["schema"] = 2;
        var future = System.Text.Encoding.UTF8.GetBytes(root.ToJsonString());
        File.WriteAllBytes(path, future);
        Assert.Null(store.LoadSnapshot());
        store.SaveSnapshot(pack.Snapshot0);
        store.RetryPendingWrites();
        Assert.Equal(future, File.ReadAllBytes(path));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!, "*.corrupt-*"));
    }

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
