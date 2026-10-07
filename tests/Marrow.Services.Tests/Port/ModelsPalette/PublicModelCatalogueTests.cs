using System.Globalization;
using Marrow.Core.Port;
using Marrow.Services.Port.ModelsPalette;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Services.Tests.Port.ModelsPalette;

// PORT-ONLY: global public facts required by OD-0065 as amended by OD-0066.
// ED-613. Every HTTPS exchange is answered in process; no real network.
[Collection(OpenAICompatibleSessionCollection.Name)]
[Trait("Decision", "ED-NEW-modelspalette-2")]
public sealed class PublicModelCatalogueTests
{
    private const string Catalogue = """
        {"data":[
          {"id":"moonshotai/kimi-k2","name":"MoonshotAI: Kimi K2","created":1750000000,
           "pricing":{"prompt":"0.0000006","completion":"0.0000025"},"context_length":128000},
          {"id":"anthropic/claude-sonnet-4.5","name":"Anthropic: Claude Sonnet 4.5",
           "supported_parameters":["tools","reasoning"]}
        ]}
        """;

    [Fact]
    public async Task Global_facts_are_fetched_without_a_key_and_keep_the_published_strength_signals()
    {
        using var runtime = new TestRuntime();
        using var handler = new InProcessHttpHandler { Answer = _ => InProcessHttpHandler.Reply(200, Catalogue) };
        using var transport = URLSession.Transport.Override(handler);
        var snapshot = Assert.IsType<PublicModelCatalogueSnapshot>(await PublicModelCatalogue.Fetch());
        var request = Assert.Single(handler.Received);
        Assert.Equal(OpenRouterAPI.ModelsURL, request.Url);
        Assert.Equal("GET", request.Method);
        Assert.Null(request.Header("Authorization"));
        Assert.Equal("application/json", request.Header("Accept"));
        Assert.Empty(request.Body);
        Assert.Equal(["anthropic/claude-sonnet-4.5", "moonshotai/kimi-k2"], snapshot.Models.Select(model => model.Id));
        var kimi = snapshot.Models[1];
        Assert.Equal("moonshotai", kimi.Maker);
        var raw = Marrow.Core.Support.JSONValue.Parse(System.Text.Encoding.UTF8.GetBytes(kimi.PublicFacts))!;
        Assert.Equal(1750000000L, raw["created"]!.JSONValueInt);
        Assert.Equal("0.0000006", raw["pricing"]!["prompt"]!.JSONValueString);
        Assert.Equal(128000L, raw["context_length"]!.JSONValueInt);
    }

    [Fact]
    public void Ten_independent_snapshots_converge_despite_catalogue_and_property_order_and_culture()
    {
        var otherOrder = """
            {"data":[
              {"supported_parameters":["tools","reasoning"],"name":"Anthropic: Claude Sonnet 4.5","id":"anthropic/claude-sonnet-4.5"},
              {"context_length":128000,"pricing":{"completion":"0.0000025","prompt":"0.0000006"},
               "created":1750000000,"name":"MoonshotAI: Kimi K2","id":"moonshotai/kimi-k2"}
            ]}
            """;
        var expected = Assert.IsType<PublicModelCatalogueSnapshot>(PublicModelCatalogue.Parse(Catalogue));
        var old = CultureInfo.CurrentCulture;
        try
        {
            for (var i = 0; i < 10; i++)
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(i % 2 == 0 ? "tr-TR" : "en-US");
                var actual = Assert.IsType<PublicModelCatalogueSnapshot>(PublicModelCatalogue.Parse(i % 2 == 0 ? Catalogue : otherOrder));
                Assert.Equal(expected.Version, actual.Version);
                Assert.Equal(expected.Fingerprint, actual.Fingerprint);
                Assert.Equal(expected.Models, actual.Models);
            }
        }
        finally { CultureInfo.CurrentCulture = old; }
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"data\":[]}")]
    [InlineData("{\"data\":[{\"id\":\"maker/model\",\"name\":\"A\"},{\"id\":\"maker/model\",\"name\":\"B\"}]}")]
    [InlineData("{\"data\":[{\"name\":\"no identity\"}]}")]
    [InlineData("not json")]
    public void Unusable_or_conflicting_catalogues_cannot_become_a_lineup(string body) =>
        Assert.Null(PublicModelCatalogue.Parse(body));

    [Fact]
    public void Identical_duplicate_rows_are_coalesced_and_bad_rows_follow_the_existing_parser_policy()
    {
        var snapshot = Assert.IsType<PublicModelCatalogueSnapshot>(PublicModelCatalogue.Parse("""
            {"data":[{"id":"maker/model","name":"Model"},{"id":"maker/model","name":"Model"},{"name":"no id"}]}
            """));
        Assert.Equal("maker/model", Assert.Single(snapshot.Models).Id);
    }

    [Theory]
    [InlineData(401)]
    [InlineData(503)]
    public async Task A_failed_source_yields_no_new_snapshot_instead_of_a_different_lineup(int status)
    {
        using var runtime = new TestRuntime();
        using var handler = new InProcessHttpHandler { Answer = _ => InProcessHttpHandler.Reply(status, Catalogue) };
        using var transport = URLSession.Transport.Override(handler);
        Assert.Null(await PublicModelCatalogue.Fetch());
    }
}
