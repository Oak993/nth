using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Ignis;
using Marrow.Core.Port.Ignis.Engine;
using Marrow.Services.Port.Ignis;

namespace Marrow.Services.Tests.Port.Ignis;

// Ignis price catalogue (OD-0134, ED-NEW-ignis-data): LiteLLM's price file trimmed to the providers the points map, priced
// per million tokens in exact decimals, behind the guardrails; the curated points stay authoritative while fresh, their rules
// always; a model only the catalogue knows takes its provider's rule. Pure functions over the trimmed fixture: no network.
public sealed class IgnisPriceCatalogueTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 10, 9, 30, 0, TimeSpan.Zero);

    private static IgnisPointsFile Points()
    {
        var parse = IgnisPointsParser.Parse(IgnisCatalogueFixture.Points);
        Assert.True(parse.Ok, parse.Error);
        return parse.File!;
    }

    private static IgnisCatalogue Built()
    {
        var build = IgnisPriceCatalogue.Build(IgnisCatalogueFixture.LiteLlm, Points(), Now, "\"abc\"");
        Assert.True(build.Catalogue is not null, build.Error);
        return build.Catalogue!;
    }

    private static IgnisPrices Prices(decimal input, decimal output, decimal read, decimal? write5m = null, decimal? write1h = null, IgnisPriceStep[]? steps = null, IgnisCacheWrite[]? writes = null) =>
        new(input, output, read, write5m, write1h, new ValueList<IgnisPriceStep>(steps ?? [])) { CacheWrites = new ValueList<IgnisCacheWrite>(writes ?? []) };

    [Fact]
    [Trait("Feature", "IG-1")]
    [Trait("Decision", "OD-0134")]
    public void The_mapped_providers_models_are_read_per_million_tokens_in_exact_decimals()
    {
        var catalogue = Built();
        Assert.Equal(Now, catalogue.FetchedAt);
        Assert.Equal(Now, catalogue.CheckedAt);
        Assert.Equal("\"abc\"", catalogue.ETag);
        Assert.Equal("2026-10-10", catalogue.Day);
        Assert.Equal(
            ["claude-opus-5-5", "claude-haiku-5-5", "claude-mythos-5", "gpt-6-sol", "gpt-5.5", "deepseek-v4-pro", "glm-5.3", "muse-spark-1.3", "gpt-test-cheap-write", "gpt-test-no-cache"],
            catalogue.Models.Select(model => model.Id));

        // Anthropic: the plain write is the 5-minute one, "_above_1hr" the 1-hour one; the batch prices are not read.
        var opus = catalogue.Lookup("claude", "claude-opus-5-5")!;
        Assert.Equal(("claude", "anthropic", 1_000_000L), (opus.Provider, opus.Source, opus.ContextWindow));
        Assert.Equal(Prices(4m, 20m, 0.2m, 5m, 8m), opus.Prices);
        // A price step above 100K names every price, the 1-hour write included.
        Assert.Equal(Prices(0.1m, 0.5m, 0.01m, 0.125m, 0.2m, [new IgnisPriceStep(100_000, 0.5m, 2.5m, 0.05m, 0.625m, 1m)]),
            catalogue.Lookup("claude", "CLAUDE-HAIKU-5-5")!.Prices);

        // OpenAI: the write is at the codex rule's 30-minute lifetime; the priority and batch prices are not read.
        var sol = catalogue.Lookup("codex", "gpt-6-sol")!;
        Assert.Equal(("codex", "openai", 922_000L), (sol.Provider, sol.Source, sol.ContextWindow));
        Assert.Equal(Prices(2m, 10m, 0.2m, steps: [new IgnisPriceStep(272_000, 4m, 15m, 0.4m, null, null) { CacheWrites = new([new IgnisCacheWrite(1800, 5m)]) }],
            writes: [new IgnisCacheWrite(1800, 2.5m)]), sol.Prices);
        Assert.Equal(2.5m, sol.Prices.WriteFor(TimeSpan.FromMinutes(30)));
        Assert.Equal(5m, sol.Prices.WriteFor(TimeSpan.FromMinutes(30), 300_000));
        Assert.Equal(Prices(5m, 30m, 0.5m, steps: [new IgnisPriceStep(272_000, 10m, 45m, 1m, null, null)]), catalogue.Lookup("codex", "gpt-5.5")!.Prices);

        // "deepseek/deepseek-v4-pro" and "deepseek-v4-pro" are one model; a write of 0 is no write premium.
        Assert.Single(catalogue.Models, model => model.Id == "deepseek-v4-pro");
        Assert.Equal(Prices(1.32m, 3.96m, 0.044m), catalogue.Lookup("deepseek", "deepseek-v4-pro")!.Prices);
        // The catalogue provider's prefix is dropped from the id; a provider may map several catalogue providers.
        Assert.Equal(Prices(1.4m, 4.4m, 0.26m), catalogue.Lookup("zai", "glm-5.3")!.Prices);
        Assert.Equal((1_048_576L, "meta"), (catalogue.Lookup("meta", "muse-spark-1.3")!.ContextWindow, catalogue.Lookup("meta", "muse-spark-1.3")!.Source));
        // A cost written as 3.0000000000000004e-07 reads 0.3, and no cache-read price means no cache saving.
        Assert.Equal(Prices(0.3m, 1.2m, 0.3m), catalogue.Lookup("codex", "gpt-test-no-cache")!.Prices);

        // Not read: an unmapped provider ("chatgpt", "bedrock"), a non-chat mode, a model with no context window, the spec entry.
        foreach (var absent in new[] { "chatgpt/gpt-6-sol", "gpt-image-1.5", "gpt-test-no-window", "sample_spec" })
            Assert.DoesNotContain(catalogue.Models, model => model.Id == absent);
        Assert.Null(catalogue.Lookup("codex", "gpt-image-1.5"));
        Assert.Null(catalogue.Lookup("claude", "gpt-6-sol"));
    }

    [Fact]
    [Trait("Feature", "IG-1")]
    [Trait("Decision", "OD-0134")]
    public void The_guardrails_set_aside_unsound_prices_and_name_a_curated_price_far_off()
    {
        var catalogue = Built();
        Assert.Null(catalogue.Lookup("claude", "claude-test-negative"));
        Assert.Null(catalogue.Lookup("claude", "claude-test-dear-cache"));
        // A write below the cache read is left out (no sound premium); the model stays.
        Assert.Equal(Prices(1m, 4m, 0.1m), catalogue.Lookup("codex", "gpt-test-cheap-write")!.Prices);
        Assert.Equal(
        [
            "claude-test-negative: a price is below 0; set aside.",
            "claude-test-dear-cache: its cache read costs more than its input; set aside.",
            "gpt-6-sol: the catalogue's input price 2 is more than 4x from the curated 20; the curated prices stay.",
        ], catalogue.Warnings);

        Assert.Null(IgnisPriceCatalogue.Drift(Prices(4m, 20m, 0.2m), Prices(16m, 5m, 0.05m)));
        Assert.Equal("the catalogue's output price 4.99 is more than 4x from the curated 20", IgnisPriceCatalogue.Drift(Prices(4m, 20m, 0.2m), Prices(4m, 4.99m, 0.2m)));
        Assert.Equal("the catalogue's cache read price 0 is more than 4x from the curated 0.2", IgnisPriceCatalogue.Drift(Prices(4m, 20m, 0.2m), Prices(4m, 20m, 0m)));
        Assert.Equal("the catalogue's 1-hour write price 40 is more than 4x from the curated 8", IgnisPriceCatalogue.Drift(Prices(4m, 20m, 0.2m, 5m, 8m), Prices(4m, 20m, 0.2m, 5m, 40m)));
        Assert.Null(IgnisPriceCatalogue.Drift(Prices(0m, 0m, 0m), Prices(0m, 0m, 0m)));
    }

    [Fact]
    [Trait("Feature", "IG-1")]
    [Trait("Decision", "OD-0134")]
    public void The_bundled_rules_map_the_catalogue_and_agree_with_the_bundled_prices()
    {
        var bundled = IgnisPointsParser.Parse(IgnisPointsData.Json).File!;
        var catalogue = IgnisPriceCatalogue.Build(IgnisCatalogueFixture.LiteLlm, bundled, Now, null).Catalogue!;
        Assert.Equal(["claude", "codex", "deepseek", "zai", "meta"], catalogue.Models.Select(model => model.Provider).Distinct());
        // The bundled prices agree with the catalogue's for the models both list: no drift warning, only the two test entries.
        Assert.Equal(2, catalogue.Warnings.Count);
        Assert.All(catalogue.Warnings, warning => Assert.Contains("-test-", warning, StringComparison.Ordinal));
        Assert.Equal(bundled.Models.Single(model => model.Ids.Contains("gpt-6-sol")).Prices, catalogue.Lookup("codex", "gpt-6-sol")!.Prices);
        Assert.Equal(bundled.Models.Single(model => model.Ids.Contains("claude-haiku-5-5")).Prices, catalogue.Lookup("claude", "claude-haiku-5-5")!.Prices);
    }

    [Theory]
    [Trait("Feature", "IG-1")]
    [Trait("Decision", "OD-0134")]
    [InlineData("", "The price catalogue is empty.")]
    [InlineData("not json", "The price catalogue is not JSON")]
    [InlineData("[]", "The price catalogue is not an object.")]
    [InlineData("{\"x\":{\"litellm_provider\":\"bedrock\",\"mode\":\"chat\"}}", "The price catalogue has no model for the mapped providers.")]
    public void A_catalogue_that_cannot_be_read_builds_nothing(string json, string error)
    {
        var build = IgnisPriceCatalogue.Build(json, Points(), Now, null);
        Assert.Null(build.Catalogue);
        Assert.StartsWith(error, build.Error, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Feature", "IG-1")]
    [Trait("Decision", "OD-0134")]
    public void Curated_prices_stay_while_fresh_and_curated_rules_always_stay()
    {
        var catalogue = Built();
        var points = Points();
        var opus = points.Models[0];
        var haiku = points.Models[1];
        var sol = points.Models[2];

        // Fresh (retrieved 20 days before): the curated prices stay even though the catalogue's differ.
        var dearer = opus with { Prices = Prices(5m, 25m, 0.5m) };
        Assert.Equal(dearer, IgnisPriceCatalogue.Merge(dearer, catalogue, new DateOnly(2026, 10, 10)));
        // Exactly 30 days old is still fresh; 31 days old takes the catalogue's prices.
        Assert.Equal(haiku, IgnisPriceCatalogue.Merge(haiku, catalogue, new DateOnly(2026, 8, 31)));
        var merged = IgnisPriceCatalogue.Merge(haiku, catalogue, new DateOnly(2026, 9, 1));
        Assert.Equal(catalogue.Lookup("claude", "claude-haiku-5-5")!.Prices, merged.Prices);
        Assert.Equal(haiku with { Prices = merged.Prices }, merged); // window, cache, tidyAt, helper, source and day are the curated ones
        Assert.Equal((900_000L, "Set by the points", 1_000_000L), (merged.TidyAt, merged.TidyReason, merged.ContextWindow));
        Assert.Equal(new IgnisCachePoints(3600, 300, true), merged.Cache);

        // Old but more than 4x apart: the curated prices stay.
        Assert.Equal(sol, IgnisPriceCatalogue.Merge(sol, catalogue, new DateOnly(2026, 10, 10)));
        // No catalogue, or a model it does not know: the curated points.
        Assert.Equal(haiku, IgnisPriceCatalogue.Merge(haiku, null, new DateOnly(2026, 10, 10)));
        Assert.Equal(haiku with { Ids = new(["claude-haiku-9"]) }, IgnisPriceCatalogue.Merge(haiku with { Ids = new(["claude-haiku-9"]) }, catalogue, new DateOnly(2026, 10, 10)));
    }

    [Fact]
    [Trait("Feature", "IG-1")]
    [Trait("Decision", "OD-0134")]
    public void A_model_only_the_catalogue_knows_takes_its_providers_rule()
    {
        var catalogue = Built();
        var points = Points();
        var mythos = IgnisPriceCatalogue.Find(catalogue, points, ProviderKind.Claude, "claude-mythos-5[1m]")!;
        Assert.Equal(("claude", "claude-mythos-5", "claude-mythos-5", 1_000_000L), (mythos.Provider, Assert.Single(mythos.Ids), mythos.DisplayName, mythos.ContextWindow));
        Assert.Equal(Prices(10m, 50m, 1m, 12.5m, 20m), mythos.Prices);
        Assert.Equal(new IgnisCachePoints(3600, 300, true), mythos.Cache);
        Assert.Equal((null, null, null), (mythos.TidyAt, mythos.TidyReason, mythos.HelperModel));
        Assert.Equal((IgnisPriceCatalogue.Url, "2026-10-10"), (mythos.SourceUrl, mythos.RetrievedAt));
        Assert.Empty(mythos.Aliases);

        var sol = IgnisPriceCatalogue.Find(catalogue, points, ProviderKind.Codex, " gpt-6-sol ")!;
        Assert.Equal(new IgnisCachePoints(1800, null, false), sol.Cache);
        Assert.Equal(new IgnisCachePoints(null, null, false), IgnisPriceCatalogue.Find(catalogue, points, ProviderKind.Deepseek, "deepseek-v4-pro")!.Cache);

        // Unknown: a model no one lists, another provider's model, a provider with no rule, no model, no catalogue.
        Assert.Null(IgnisPriceCatalogue.Find(catalogue, points, ProviderKind.Claude, "claude-unknown-9"));
        Assert.Null(IgnisPriceCatalogue.Find(catalogue, points, ProviderKind.Claude, "gpt-6-sol"));
        Assert.Null(IgnisPriceCatalogue.Find(catalogue, points with { Providers = ValueList<IgnisProviderRule>.Empty }, ProviderKind.Claude, "claude-mythos-5"));
        Assert.Null(IgnisPriceCatalogue.Find(catalogue, points, ProviderKind.Claude, " "));
        Assert.Null(IgnisPriceCatalogue.Find(null, points, ProviderKind.Claude, "claude-mythos-5"));
    }

    [Fact]
    [Trait("Feature", "IG-1")]
    [Trait("Decision", "OD-0134")]
    public void The_stored_copy_reads_back_whole_and_a_damaged_one_reads_as_none()
    {
        var catalogue = Built();
        var json = IgnisPriceCatalogue.Write(catalogue);
        Assert.Contains("\"source\":\"" + IgnisPriceCatalogue.Url + "\"", json, StringComparison.Ordinal);
        Assert.Contains("\"input\":0.125", IgnisPriceCatalogue.Write(new IgnisCatalogue(Now, Now, null, [new IgnisCatalogueModel("claude", "anthropic", "x", 1000, Prices(0.125000m, 1m, 0.1m))], [])), StringComparison.Ordinal);

        var read = IgnisPriceCatalogue.Read(json)!;
        Assert.Equal((catalogue.FetchedAt, catalogue.CheckedAt, catalogue.ETag, catalogue.Day), (read.FetchedAt, read.CheckedAt, read.ETag, read.Day));
        Assert.Equal(catalogue.Models, read.Models);
        Assert.Equal(catalogue.Warnings, read.Warnings);

        var later = catalogue.Confirmed(Now.AddDays(2), null);
        Assert.Equal((Now, Now.AddDays(2), "\"abc\"", "2026-10-12"), (later.FetchedAt, later.CheckedAt, later.ETag, later.Day));
        Assert.Equal(catalogue.Models, later.Models);

        Assert.Null(IgnisPriceCatalogue.Read(null));
        Assert.Null(IgnisPriceCatalogue.Read("{ broken"));
        Assert.Null(IgnisPriceCatalogue.Read(json.Replace("\"schemaVersion\":1", "\"schemaVersion\":2", StringComparison.Ordinal)));
        Assert.Null(IgnisPriceCatalogue.Read(json.Replace("\"provider\":\"claude\"", "\"provider\":\"anthropic\"", StringComparison.Ordinal)));
        Assert.Null(IgnisPriceCatalogue.Read(json.Replace("\"input\":4,", "\"input\":-4,", StringComparison.Ordinal)));
    }
}
