using Marrow.Core.Models;
using Marrow.Core.Port.Ignis;
using Marrow.Core.Port.Ignis.Engine;

namespace Marrow.Core.Tests.Port.Ignis.Engine;

// Ignis U1 (OD-0134, ED-NEW-ignis-engine; work/ignis/PLAN.md): the points parser takes a valid schema v1 file whole and
// rejects a malformed one with its first reason; ids and aliases find a model, case-insensitively, per provider. Schema v2
// (ED-NEW-ignis-data) is read too, so the unsupported-schema case names 3 and 0 (IgnisPointsSchemaV2Tests covers v2).
public sealed class IgnisPointsParserTests
{
    internal const string Valid = """
        {"schemaVersion":1,"version":"2026.10.10","future":"ignored","models":[
          {"provider":"claude","ids":["claude-opus-5-5"],"aliases":["opus"],"displayName":"Opus 5.5","contextWindow":1000000,
           "prices":{"input":4,"output":20,"cacheRead":0.2,"cacheWrite5m":5,"cacheWrite1h":8,"steps":[]},
           "cache":{"ttlMainSeconds":3600,"ttlSubagentSeconds":300,"keepWarm":true},"helperModel":"claude-haiku-5-5",
           "sourceUrl":"https://platform.claude.com/docs/en/about-claude/pricing","retrievedAt":"2026-10-10"},
          {"provider":"claude","ids":["claude-haiku-5-5"],"aliases":["haiku"],"displayName":"Haiku 5.5","contextWindow":1000000,
           "prices":{"input":0.1,"output":0.5,"cacheRead":0.01,"cacheWrite5m":0.125,"cacheWrite1h":0.2,
                     "steps":[{"aboveTokens":100000,"input":0.5,"output":2.5,"cacheRead":0.05,"cacheWrite5m":0.625,"cacheWrite1h":1}]},
           "cache":{"ttlMainSeconds":3600,"ttlSubagentSeconds":300,"keepWarm":true},
           "sourceUrl":"https://platform.claude.com/docs/en/about-claude/pricing","retrievedAt":"2026-10-10"},
          {"provider":"codex","ids":["gpt-5.3-codex"],"aliases":[],"displayName":"GPT-5.3 Codex","contextWindow":400000,
           "prices":{"input":1.75,"output":14,"cacheRead":0.175},
           "cache":{"keepWarm":false},"tidyAt":300000,"tidyReason":"Set by the points",
           "sourceUrl":"https://developers.openai.com/api/docs/pricing","retrievedAt":"2026-10-10"}]}
        """;

    [Fact]
    [Trait("Feature", "IG-1")]
    [Trait("Decision", "OD-0134")]
    public void A_valid_file_parses_whole_and_unknown_members_are_ignored()
    {
        var parse = IgnisPointsParser.Parse(Valid);
        Assert.True(parse.Ok, parse.Error);
        var file = parse.File!;
        Assert.Equal(1, file.SchemaVersion);
        Assert.Equal("2026.10.10", file.Version);
        Assert.Equal(3, file.Models.Count);
        var opus = file.Models[0];
        Assert.Equal("claude", opus.Provider);
        Assert.Equal(new[] { "claude-opus-5-5" }, opus.Ids);
        Assert.Equal(new[] { "opus" }, opus.Aliases);
        Assert.Equal(1_000_000, opus.ContextWindow);
        Assert.Equal(new IgnisPrices(4m, 20m, 0.2m, 5m, 8m, new([])), opus.Prices);
        Assert.Equal(new IgnisCachePoints(3600, 300, true), opus.Cache);
        Assert.Equal("claude-haiku-5-5", opus.HelperModel);
        Assert.Equal(new IgnisPriceStep(100_000, 0.5m, 2.5m, 0.05m, 0.625m, 1m), Assert.Single(file.Models[1].Prices.Steps));
        var codex = file.Models[2];
        Assert.Null(codex.Prices.CacheWrite5m);
        Assert.Null(codex.Prices.CacheWrite1h);
        Assert.Equal(new IgnisCachePoints(null, null, false), codex.Cache);
        Assert.Equal(300_000, codex.TidyAt);
        Assert.Equal("Set by the points", codex.TidyReason);
    }

    [Fact]
    [Trait("Feature", "IG-1")]
    [Trait("Decision", "OD-0134")]
    public void Ids_and_aliases_find_the_model_case_insensitively_for_its_provider_only()
    {
        var file = IgnisPointsParser.Parse(Valid).File!;
        Assert.Equal("Opus 5.5", IgnisPointsParser.Find(file, ProviderKind.Claude, "claude-opus-5-5")?.DisplayName);
        Assert.Equal("Opus 5.5", IgnisPointsParser.Find(file, ProviderKind.Claude, "OPUS")?.DisplayName);
        Assert.Equal("Haiku 5.5", IgnisPointsParser.Find(file, ProviderKind.Claude, "haiku")?.DisplayName);
        Assert.Equal("Opus 5.5", IgnisPointsParser.Find(file, ProviderKind.Claude, "claude-opus-5-5[1m]")?.DisplayName);
        Assert.Equal("GPT-5.3 Codex", IgnisPointsParser.Find(file, ProviderKind.Codex, "gpt-5.3-codex")?.DisplayName);
        Assert.Null(IgnisPointsParser.Find(file, ProviderKind.Codex, "opus"));
        Assert.Null(IgnisPointsParser.Find(file, ProviderKind.Claude, "gpt-5.3-codex"));
        Assert.Null(IgnisPointsParser.Find(file, ProviderKind.Claude, "default"));
        Assert.Null(IgnisPointsParser.Find(file, ProviderKind.Claude, null));
        Assert.Null(IgnisPointsParser.Find(file, ProviderKind.Claude, " "));
    }

    [Theory]
    [Trait("Feature", "IG-1")]
    [Trait("Decision", "OD-0134")]
    [InlineData("not json", "not JSON")]
    [InlineData("[]", "the file is not an object")]
    [InlineData("{\"schemaVersion\":3,\"version\":\"2026.10.10\",\"models\":[]}", "schemaVersion: 3 is not supported")]
    [InlineData("{\"schemaVersion\":0,\"version\":\"2026.10.10\",\"models\":[]}", "schemaVersion: 0 is not supported")]
    [InlineData("{\"version\":\"2026.10.10\",\"models\":[]}", "schemaVersion: missing")]
    [InlineData("{\"schemaVersion\":1,\"version\":\"2026-10-10\",\"models\":[]}", "version: not yyyy.MM.dd")]
    [InlineData("{\"schemaVersion\":1,\"version\":\"2026.02.30\",\"models\":[]}", "version: not yyyy.MM.dd")]
    [InlineData("{\"schemaVersion\":1,\"version\":\"2026.10.10\",\"models\":[]}", "models: empty")]
    [InlineData("{\"schemaVersion\":1,\"version\":\"2026.10.10\"}", "models: missing")]
    public void A_malformed_file_is_rejected_with_its_reason(string json, string reason)
    {
        var parse = IgnisPointsParser.Parse(json);
        Assert.False(parse.Ok);
        Assert.Null(parse.File);
        Assert.StartsWith(reason, parse.Error, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Feature", "IG-1")]
    [Trait("Decision", "OD-0134")]
    [InlineData("\"provider\":\"claude\"", "\"provider\":\"anthropic\"", "models[0].provider: unknown")]
    [InlineData("\"ids\":[\"claude-opus-5-5\"]", "\"ids\":[]", "models[0].ids: missing")]
    [InlineData("\"ids\":[\"claude-opus-5-5\"]", "\"ids\":[\" \"]", "models[0].ids: missing")]
    [InlineData("\"displayName\":\"Opus 5.5\"", "\"displayName\":\"\"", "models[0].displayName: missing")]
    [InlineData("\"contextWindow\":400000", "\"contextWindow\":0", "models[2].contextWindow: missing")]
    [InlineData("\"input\":4,", "\"input\":-4,", "models[0].prices.input: missing")]
    [InlineData("\"output\":20,", "\"output\":\"20\",", "models[0].prices.output: missing")]
    [InlineData("\"cacheWrite1h\":8,", "\"cacheWrite1h\":\"x\",", "models[0].prices.cacheWrite1h: not a price")]
    [InlineData("\"aboveTokens\":100000", "\"aboveTokens\":1000000", "models[1].prices.steps[0].aboveTokens: not rising within the context window")]
    [InlineData("\"aboveTokens\":100000", "\"aboveTokens\":0", "models[1].prices.steps[0].aboveTokens: not rising within the context window")]
    [InlineData("\"cache\":{\"keepWarm\":false}", "\"cache\":{}", "models[2].cache.keepWarm: missing")]
    [InlineData("\"cache\":{\"keepWarm\":false}", "\"cache\":{\"ttlMainSeconds\":0,\"keepWarm\":false}", "models[2].cache.ttlMainSeconds: not a positive number of seconds")]
    [InlineData("\"tidyAt\":300000", "\"tidyAt\":500000", "models[2].tidyAt: not within the context window")]
    [InlineData("\"sourceUrl\":\"https://developers.openai.com/api/docs/pricing\"", "\"sourceUrl\":\"http://developers.openai.com/api/docs/pricing\"", "models[2].sourceUrl: not an https address")]
    [InlineData("\"sourceUrl\":\"https://developers.openai.com/api/docs/pricing\",\"retrievedAt\":\"2026-10-10\"", "\"sourceUrl\":\"https://developers.openai.com/api/docs/pricing\",\"retrievedAt\":\"10/10/2026\"", "models[2].retrievedAt: not yyyy-MM-dd")]
    [InlineData("\"aliases\":[\"haiku\"]", "\"aliases\":[\"opus\"]", "models[1]: \"opus\" is already used by another claude model")]
    public void One_bad_model_rejects_the_whole_file(string from, string to, string reason)
    {
        Assert.Contains(from, Valid, StringComparison.Ordinal);
        var parse = IgnisPointsParser.Parse(ReplaceFirst(Valid, from, to));
        Assert.False(parse.Ok);
        Assert.Equal(reason, parse.Error);
    }

    [Fact]
    [Trait("Feature", "IG-1")]
    [Trait("Decision", "OD-0134")]
    public void The_same_id_under_two_providers_is_allowed_and_versions_must_be_real_days()
    {
        var json = ReplaceFirst(Valid, "\"ids\":[\"gpt-5.3-codex\"]", "\"ids\":[\"claude-opus-5-5\"]");
        Assert.True(IgnisPointsParser.Parse(json).Ok);
        Assert.True(IgnisPointsParser.IsVersion("2026.10.10"));
        Assert.False(IgnisPointsParser.IsVersion("2026.1.10"));
        Assert.False(IgnisPointsParser.IsVersion("2026.13.01"));
        Assert.True(IgnisPointsParser.IsDay("2026-10-10"));
        Assert.False(IgnisPointsParser.IsDay("2026.10.10"));
        Assert.False(IgnisPointsParser.Parse("").Ok);
        Assert.False(IgnisPointsParser.Parse(null).Ok);
    }

    internal static string ReplaceFirst(string text, string from, string to)
    {
        var at = text.IndexOf(from, StringComparison.Ordinal);
        return text[..at] + to + text[(at + from.Length)..];
    }
}
