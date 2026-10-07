using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Marrow.Services.Port.ModelsPalette;
using Marrow.Services.Tests.Fixtures;
namespace Marrow.Services.Tests.Port.ModelsPalette;
[Trait("Decision","ED-NEW-modelspalette-11")]
public sealed class PaletteSeedMigrationTests
{
    [Fact]
    public async Task Extractor_only_version_change_reuses_current_input_resamples_without_network_or_old_seed_values()
    {
        using var roots=new ModelsPaletteRoots();using var fake=new PalettePipelineFixture();var root=roots.New();string path;string[] expected;
        using(var engine=new PaletteEngine(root,fake.Http,fake,fake.Time,fake.Pack))
        {await engine.CheckNow();path=engine.SeedsPath;expected=engine.Current.Models["acme/nova-1"].Palette.Stops.ToArray();}
        var document=JsonNode.Parse(File.ReadAllBytes(path))!;var data=document["data"]!;var row=data["acme"]!;
        Assert.NotNull(row["InputDigest"]);row["SeedVersion"]="S1";row["Key"]=new string('0',64);row["Seed"]!["Hue"]=0;
        var raw=data.ToJsonString();document["hash"]=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));File.WriteAllText(path,document.ToJsonString());
        fake.FailHf=true;var calls=fake.Http.Received.Count;
        using var restarted=new PaletteEngine(root,fake.Http,fake,fake.Time,fake.Pack);await restarted.CheckNow();
        Assert.Equal(expected,restarted.Current.Models["acme/nova-1"].Palette.Stops);
        Assert.DoesNotContain(fake.Http.Received.Skip(calls),r=>r.Url.Host.Contains("huggingface",StringComparison.Ordinal));
    }
}
