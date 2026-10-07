using Marrow.Core.Port.ModelsPalette;
using Marrow.Services.Port.ModelsPalette;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;
namespace Marrow.Services.Tests.Port.ModelsPalette;
[Trait("Decision","ED-NEW-modelspalette-11")]
public sealed class PalettePackMarketAuthorityTests
{
    [Fact]
    public async Task A_newer_pack_ignores_the_older_market_lineup_instead_of_resurrecting_its_facts()
    {
        using var roots=new ModelsPaletteRoots();using var fake=new PalettePipelineFixture();var root=roots.New();var answer=fake.Http.Answer;
        fake.Http.Answer=r=>r.Url.Host=="openrouter.ai"&&r.Url.AbsolutePath.EndsWith("/models",StringComparison.Ordinal)
            ?InProcessHttpHandler.Reply(200,"{\"data\":[{\"id\":\"openai/gpt-7\",\"created\":1791300000,\"context_length\":1000000,\"pricing\":{\"completion\":\"0.000020\"}}]}","application/json",("Date",fake.Date)):answer!(r);
        using(var old=new PaletteEngine(root,fake.Http,fake,fake.Time,fake.Pack))await old.CheckNow();
        var date=DateTimeOffset.Parse("2026-10-08T06:20:00Z",System.Globalization.CultureInfo.InvariantCulture);
        var pack=PaletteReleasePack.Bake(fake.Pack,2,[],catalogueDate:date);
        using var upgraded=new PaletteEngine(root,fake.Http,fake,fake.Time,fake.Pack,releasePack:pack);await upgraded.Prime();await upgraded.Observe(["openai/gpt-6-astra"]);
        Assert.Equal(date.ToUnixTimeSeconds()/21600,upgraded.Current.Boundary);
        Assert.Equal(pack.Snapshot0.Models["openai/gpt-6-astra"].Position,upgraded.Current.Models["openai/gpt-6-astra"].Position);
        Assert.DoesNotContain("openai/gpt-7",upgraded.Current.Models.Keys);
    }
}
