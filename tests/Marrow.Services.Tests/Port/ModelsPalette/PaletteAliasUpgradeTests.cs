using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Marrow.Services.Port.ModelsPalette;
using Marrow.Services.Tests.Fixtures;
namespace Marrow.Services.Tests.Port.ModelsPalette;
[Trait("Decision","ED-NEW-modelspalette-11")]
public sealed class PaletteAliasUpgradeTests
{
    [Fact]
    public async Task Alias_merge_discards_both_local_keys_and_never_selects_a_history_dependent_winner()
    {
        using var roots=new ModelsPaletteRoots();using var fake=new PalettePipelineFixture();var root=roots.New();var first=PaletteReleasePack.Bake(fake.Pack,1,[]);string path;
        using(var engine=new PaletteEngine(root,fake.Http,fake,fake.Time,fake.Pack,releasePack:first)){await engine.CheckNow();path=engine.SeedsPath;}
        var file=JsonNode.Parse(File.ReadAllBytes(path))!;file["data"]!["acme-labs"]=file["data"]!["acme"]!.DeepClone();
        file["hash"]=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(file["data"]!.ToJsonString())));File.WriteAllText(path,file.ToJsonString());
        using var restarted=new PaletteEngine(root,fake.Http,fake,fake.Time,fake.Pack,releasePack:first);await restarted.Prime();
        var next=PaletteReleasePack.Bake(fake.Pack,2,[],first,aliases:new(){["acme-labs"]="acme"});
        await restarted.AdoptPack(next.Encode(),_=>true);Assert.Equal(PaletteState.Pending,restarted.Current.Models["acme/nova-1"].State);
        await restarted.CheckNow();Assert.Equal(PaletteState.Ready,restarted.Current.Models["acme/nova-1"].State);
        var stored=JsonNode.Parse(File.ReadAllBytes(path))!;Assert.Null(stored["data"]!["acme-labs"]);Assert.NotNull(stored["data"]!["acme"]);
    }
}
