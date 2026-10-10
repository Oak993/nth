using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Ignis;
using Marrow.Core.Port.Ignis.Engine;

namespace Marrow.Core.Tests.Port.Ignis.Engine;

// Ignis points schema v2 (OD-0134, ED-NEW-ignis-data): write prices at other lifetimes (OpenAI's 30-minute writes) and the
// provider rules are read from a v2 file, validated like the rest (one bad entry rejects the file), and ignored in a v1 file.
// WriteFor picks the price for an exact lifetime.
public sealed class IgnisPointsSchemaV2Tests
{
    internal const string Valid = """
        {"schemaVersion":2,"version":"2026.10.10","providers":[
          {"provider":"claude","billing":"perToken","ttlMainSeconds":3600,"ttlSubagentSeconds":300,"keepWarm":true,"catalogue":["anthropic"],
           "notes":"Two lifetimes","sourceUrl":"https://platform.claude.com/docs/en/build-with-claude/prompt-caching","retrievedAt":"2026-10-10"},
          {"provider":"codex","billing":"perToken","ttlMainSeconds":1800,"keepWarm":false,"catalogue":["openai"],
           "sourceUrl":"https://developers.openai.com/api/docs/guides/prompt-caching","retrievedAt":"2026-10-10"},
          {"provider":"copilot","billing":"flat","keepWarm":false,"sourceUrl":"https://github.com/features/copilot/plans","retrievedAt":"2026-10-10"},
          {"provider":"local","billing":"none","keepWarm":false,"catalogue":null,"sourceUrl":"https://example.org/local","retrievedAt":"2026-10-10"}],
         "models":[
          {"provider":"codex","ids":["gpt-6-sol"],"aliases":[],"displayName":"GPT-6 Sol","contextWindow":1050000,
           "prices":{"input":2,"output":10,"cacheRead":0.2,"cacheWrites":[{"ttlSeconds":1800,"price":2.5}],
                     "steps":[{"aboveTokens":272000,"input":4,"output":15,"cacheRead":0.4,"cacheWrites":[{"ttlSeconds":1800,"price":5}]}]},
           "cache":{"ttlMainSeconds":1800,"keepWarm":false},
           "sourceUrl":"https://developers.openai.com/api/docs/pricing","retrievedAt":"2026-10-10"},
          {"provider":"claude","ids":["claude-haiku-5-5"],"aliases":["haiku"],"displayName":"Haiku 5.5","contextWindow":1000000,
           "prices":{"input":0.1,"output":0.5,"cacheRead":0.01,"cacheWrite5m":0.125,"cacheWrite1h":0.2,
                     "steps":[{"aboveTokens":100000,"input":0.5,"output":2.5,"cacheRead":0.05,"cacheWrite5m":0.625}]},
           "cache":{"ttlMainSeconds":3600,"ttlSubagentSeconds":300,"keepWarm":true},
           "sourceUrl":"https://platform.claude.com/docs/en/about-claude/pricing","retrievedAt":"2026-10-10"}]}
        """;

    private static IgnisPointsFile File()
    {
        var parse = IgnisPointsParser.Parse(Valid);
        Assert.True(parse.Ok, parse.Error);
        return parse.File!;
    }

    [Fact]
    [Trait("Feature", "IG-1")]
    [Trait("Decision", "OD-0134")]
    public void A_v2_file_reads_its_provider_rules_and_write_prices_at_other_lifetimes()
    {
        var file = File();
        Assert.Equal(2, file.SchemaVersion);
        Assert.Equal(2, IgnisPointsFile.CurrentSchemaVersion);
        Assert.Equal(1, IgnisPointsFile.OldestSchemaVersion);
        Assert.Equal(
            new IgnisProviderRule("claude", IgnisBilling.PerToken, 3600, 300, true, new(["anthropic"]), "Two lifetimes",
                "https://platform.claude.com/docs/en/build-with-claude/prompt-caching", "2026-10-10"),
            file.Rule("claude"));
        Assert.Equal(new IgnisCachePoints(1800, null, false), file.Rule("codex")!.Cache);
        Assert.Equal((IgnisBilling.Flat, 0, (string?)null), (file.Rule("copilot")!.Billing, file.Rule("copilot")!.Catalogue.Count, file.Rule("copilot")!.Notes));
        Assert.Equal(IgnisBilling.None, file.Rule("local")!.Billing);
        Assert.Empty(file.Rule("local")!.Catalogue);
        Assert.Null(file.Rule("grok"));

        var sol = IgnisPointsParser.Find(file, ProviderKind.Codex, "gpt-6-sol")!;
        Assert.Equal(new IgnisCacheWrite(1800, 2.5m), Assert.Single(sol.Prices.CacheWrites));
        Assert.Equal(new IgnisCacheWrite(1800, 5m), Assert.Single(Assert.Single(sol.Prices.Steps).CacheWrites));
        Assert.Equal(
            new IgnisPrices(2m, 10m, 0.2m, null, null, new([new IgnisPriceStep(272_000, 4m, 15m, 0.4m, null, null) { CacheWrites = new([new IgnisCacheWrite(1800, 5m)]) }]))
            { CacheWrites = new([new IgnisCacheWrite(1800, 2.5m)]) },
            sol.Prices);
        // The write list is part of the value: the same prices without it are not equal.
        Assert.NotEqual(sol.Prices with { CacheWrites = ValueList<IgnisCacheWrite>.Empty }, sol.Prices);
    }

    [Fact]
    [Trait("Feature", "IG-1")]
    [Trait("Decision", "OD-0134")]
    public void WriteFor_picks_the_price_for_the_exact_lifetime_and_the_step_in_force()
    {
        var file = File();
        var sol = IgnisPointsParser.Find(file, ProviderKind.Codex, "gpt-6-sol")!.Prices;
        Assert.Equal(2.5m, sol.WriteFor(TimeSpan.FromMinutes(30)));
        Assert.Null(sol.WriteFor(TimeSpan.FromMinutes(5)));
        Assert.Null(sol.WriteFor(TimeSpan.FromHours(1)));
        Assert.Null(sol.WriteFor(TimeSpan.FromMinutes(31)));
        Assert.Equal(2.5m, sol.WriteFor(TimeSpan.FromMinutes(30), 272_000));
        Assert.Equal(5m, sol.WriteFor(TimeSpan.FromMinutes(30), 272_001));
        Assert.Equal(5m, sol.Steps[0].WriteFor(TimeSpan.FromSeconds(1800)));

        var haiku = IgnisPointsParser.Find(file, ProviderKind.Claude, "haiku")!.Prices;
        Assert.Equal(0.125m, haiku.WriteFor(TimeSpan.FromMinutes(5)));
        Assert.Equal(0.2m, haiku.WriteFor(TimeSpan.FromHours(1)));
        Assert.Null(haiku.WriteFor(TimeSpan.FromMinutes(30)));
        // Above 100K the step names a 5-minute write; it leaves the 1-hour one out, so the base's stays (as IgnisMoney.Tier does).
        Assert.Equal(0.625m, haiku.WriteFor(TimeSpan.FromMinutes(5), 150_000));
        Assert.Equal(0.2m, haiku.WriteFor(TimeSpan.FromHours(1), 150_000));
        Assert.Equal(1m, (haiku.Steps[0] with { CacheWrite1h = 1m }).WriteFor(TimeSpan.FromHours(1)));
        Assert.Equal(7m, new IgnisPriceStep(1, 1m, 1m, 0.1m, null, null) { CacheWrites = new([new IgnisCacheWrite(300 * 2, 7m)]) }.WriteFor(TimeSpan.FromMinutes(10)));
    }

    [Fact]
    [Trait("Feature", "IG-1")]
    [Trait("Decision", "OD-0134")]
    public void A_v1_file_ignores_the_v2_members()
    {
        var v1 = Valid.Replace("\"schemaVersion\":2", "\"schemaVersion\":1", StringComparison.Ordinal)
            .Replace("\"provider\":\"local\",\"billing\":\"none\"", "\"provider\":\"nowhere\",\"billing\":\"none\"", StringComparison.Ordinal);
        var parse = IgnisPointsParser.Parse(v1);
        Assert.True(parse.Ok, parse.Error);
        Assert.Equal(1, parse.File!.SchemaVersion);
        Assert.Empty(parse.File.Providers);
        Assert.Null(parse.File.Rule("claude"));
        Assert.Empty(IgnisPointsParser.Find(parse.File, ProviderKind.Codex, "gpt-6-sol")!.Prices.CacheWrites);
        Assert.Empty(IgnisPointsParser.Find(parse.File, ProviderKind.Codex, "gpt-6-sol")!.Prices.Steps[0].CacheWrites);
        // The same file as v1 has no write price at 30 minutes; as v2 it has one.
        Assert.Null(IgnisPointsParser.Find(parse.File, ProviderKind.Codex, "gpt-6-sol")!.Prices.WriteFor(TimeSpan.FromMinutes(30)));
    }

    [Theory]
    [Trait("Feature", "IG-1")]
    [Trait("Decision", "OD-0134")]
    [InlineData("\"provider\":\"claude\",\"billing\"", "\"provider\":\"anthropic\",\"billing\"", "providers[0].provider: unknown")]
    [InlineData("\"billing\":\"flat\"", "\"billing\":\"monthly\"", "providers[2].billing: not perToken, flat or none")]
    [InlineData("\"billing\":\"flat\",\"keepWarm\":false", "\"billing\":\"flat\",\"keepWarm\":true", "providers[2].keepWarm: needs per-token billing and a known lifetime")]
    [InlineData("\"ttlMainSeconds\":1800,\"keepWarm\":false,\"catalogue\"", "\"keepWarm\":true,\"catalogue\"", "providers[1].keepWarm: needs per-token billing and a known lifetime")]
    [InlineData("\"billing\":\"flat\",\"keepWarm\":false", "\"billing\":\"flat\"", "providers[2].keepWarm: missing")]
    [InlineData("\"ttlMainSeconds\":1800,\"keepWarm\":false,\"catalogue\"", "\"ttlMainSeconds\":-1,\"keepWarm\":false,\"catalogue\"", "providers[1].ttlMainSeconds: not a positive number of seconds")]
    [InlineData("\"catalogue\":[\"openai\"]", "\"catalogue\":[\"anthropic\"]", "providers[1].catalogue: \"anthropic\" already maps to another provider")]
    [InlineData("\"catalogue\":[\"openai\"]", "\"catalogue\":\"openai\"", "providers[1].catalogue: not a list of names")]
    [InlineData("{\"provider\":\"copilot\"", "{\"provider\":\"codex\"", "providers[2]: \"codex\" already has a rule")]
    [InlineData("\"sourceUrl\":\"https://github.com/features/copilot/plans\"", "\"sourceUrl\":\"github.com/features\"", "providers[2].sourceUrl: not an https address")]
    [InlineData("plans\",\"retrievedAt\":\"2026-10-10\"", "plans\",\"retrievedAt\":\"2026.10.10\"", "providers[2].retrievedAt: not yyyy-MM-dd")]
    [InlineData("\"providers\":[", "\"providers\":7,\"unused\":[", "providers: not a list")]
    [InlineData("\"cacheWrites\":[{\"ttlSeconds\":1800,\"price\":2.5}]", "\"cacheWrites\":[{\"ttlSeconds\":300,\"price\":2.5}]", "models[0].prices.cacheWrites[0].ttlSeconds: 5 minutes and 1 hour have their own fields")]
    [InlineData("\"cacheWrites\":[{\"ttlSeconds\":1800,\"price\":2.5}]", "\"cacheWrites\":[{\"ttlSeconds\":1800,\"price\":2.5},{\"ttlSeconds\":1800,\"price\":3}]", "models[0].prices.cacheWrites[1].ttlSeconds: already listed")]
    [InlineData("\"cacheWrites\":[{\"ttlSeconds\":1800,\"price\":2.5}]", "\"cacheWrites\":[{\"ttlSeconds\":0,\"price\":2.5}]", "models[0].prices.cacheWrites[0].ttlSeconds: not a positive number of seconds")]
    [InlineData("\"cacheWrites\":[{\"ttlSeconds\":1800,\"price\":2.5}]", "\"cacheWrites\":[{\"ttlSeconds\":1800,\"price\":-2.5}]", "models[0].prices.cacheWrites[0].price: missing")]
    [InlineData("\"cacheWrites\":[{\"ttlSeconds\":1800,\"price\":2.5}]", "\"cacheWrites\":{\"ttlSeconds\":1800}", "models[0].prices.cacheWrites: not a list")]
    [InlineData("\"cacheWrites\":[{\"ttlSeconds\":1800,\"price\":5}]", "\"cacheWrites\":[7]", "models[0].prices.steps[0].cacheWrites[0]: not an object")]
    public void One_bad_rule_or_write_price_rejects_the_whole_v2_file(string from, string to, string reason)
    {
        Assert.Contains(from, Valid, StringComparison.Ordinal);
        var parse = IgnisPointsParser.Parse(IgnisPointsParserTests.ReplaceFirst(Valid, from, to));
        Assert.False(parse.Ok);
        Assert.Equal(reason, parse.Error);
    }

    [Fact]
    [Trait("Feature", "IG-1")]
    [Trait("Decision", "OD-0134")]
    public void The_prices_reader_takes_the_v2_members_and_never_throws()
    {
        using var good = System.Text.Json.JsonDocument.Parse("{\"input\":1,\"output\":2,\"cacheRead\":0.1,\"cacheWrites\":[{\"ttlSeconds\":1800,\"price\":1.25}]}");
        var (prices, error) = IgnisPointsParser.ParsePrices(good.RootElement, 1000);
        Assert.Null(error);
        Assert.Equal(1.25m, prices!.WriteFor(TimeSpan.FromMinutes(30)));
        using var bad = System.Text.Json.JsonDocument.Parse("[1]");
        Assert.Equal((null, "prices: missing"), IgnisPointsParser.ParsePrices(bad.RootElement, 1000));
        using var steps = System.Text.Json.JsonDocument.Parse("{\"input\":1,\"output\":2,\"cacheRead\":0.1,\"steps\":[{\"aboveTokens\":2000,\"input\":1,\"output\":2,\"cacheRead\":0.1}]}");
        Assert.Equal("prices.steps[0].aboveTokens: not rising within the context window", IgnisPointsParser.ParsePrices(steps.RootElement, 1000).Error);
    }
}
