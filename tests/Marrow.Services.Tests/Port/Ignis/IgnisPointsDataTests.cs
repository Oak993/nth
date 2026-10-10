using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Ignis;
using Marrow.Core.Port.Ignis.Engine;
using Marrow.Services.Port.Ignis;

namespace Marrow.Services.Tests.Port.Ignis;

// Ignis U1 (OD-0134, ED-NEW-ignis-engine): the bundled model points parse, name an official https source and a retrieval
// day for every model, cover the models Marrow offers, and keep Codex keep-warm off (PLAN lead decision). Schema v2 and the
// 2026-10-10 refresh (ED-NEW-ignis-data): a sourced rule for every nth provider, the new Claude models, OpenAI's 30-minute writes.
public sealed class IgnisPointsDataTests
{
    private static IgnisPointsFile Bundled()
    {
        var parse = IgnisPointsParser.Parse(IgnisPointsData.Json);
        Assert.True(parse.Ok, parse.Error);
        return parse.File!;
    }

    [Fact]
    [Trait("Feature", "IG-1")]
    [Trait("Decision", "OD-0134")]
    public void The_bundled_points_parse_and_every_model_names_an_official_source()
    {
        var file = Bundled();
        Assert.Equal("2026.10.10", file.Version);
        string[] official = ["platform.claude.com", "developers.openai.com"];
        Assert.All(file.Models, model =>
        {
            Assert.Contains(new Uri(model.SourceUrl).Host, official);
            Assert.Equal("2026-10-10", model.RetrievedAt);
            Assert.True(model.Prices.Input > 0 && model.Prices.Output > 0 && model.Prices.CacheRead > 0, model.DisplayName);
        });
    }

    [Theory]
    [Trait("Feature", "IG-1")]
    [Trait("Decision", "OD-0134")]
    [InlineData(ProviderKind.Claude, "claude-opus-5-5", "Opus 5.5")]
    [InlineData(ProviderKind.Claude, "opus", "Opus 5.5")]
    [InlineData(ProviderKind.Claude, "claude-sonnet-5-5", "Sonnet 5.5")]
    [InlineData(ProviderKind.Claude, "sonnet", "Sonnet 5.5")]
    [InlineData(ProviderKind.Claude, "claude-haiku-5-5", "Haiku 5.5")]
    [InlineData(ProviderKind.Claude, "haiku", "Haiku 5.5")]
    [InlineData(ProviderKind.Claude, "claude-fable-5-1[1m]", "Fable 5.1")]
    [InlineData(ProviderKind.Claude, "claude-opus-5", "Opus 5")]
    [InlineData(ProviderKind.Claude, "claude-sonnet-5", "Sonnet 5")]
    [InlineData(ProviderKind.Claude, "claude-haiku-4-5", "Haiku 4.5")]
    [InlineData(ProviderKind.Codex, "gpt-6-astra", "GPT-6 Astra")]
    [InlineData(ProviderKind.Codex, "gpt-6.1-sol", "GPT-6.1 Sol")]
    [InlineData(ProviderKind.Codex, "gpt-6-sol", "GPT-6 Sol")]
    [InlineData(ProviderKind.Codex, "gpt-6-luna", "GPT-6 Luna")]
    [InlineData(ProviderKind.Codex, "gpt-5.6-sol", "GPT-5.6 Sol")]
    [InlineData(ProviderKind.Codex, "gpt-5.6-terra", "GPT-5.6 Terra")]
    [InlineData(ProviderKind.Codex, "gpt-5.6-luna", "GPT-5.6 Luna")]
    [InlineData(ProviderKind.Codex, "gpt-5.5", "GPT-5.5")]
    [InlineData(ProviderKind.Codex, "gpt-5.4", "GPT-5.4")]
    [InlineData(ProviderKind.Codex, "gpt-5.3-codex", "GPT-5.3 Codex")]
    [InlineData(ProviderKind.Codex, "gpt-5.4-mini", "GPT-5.4 mini")]
    public void The_models_Marrow_offers_are_in_the_bundled_points(ProviderKind provider, string model, string displayName)
    {
        Assert.Equal(displayName, IgnisPointsParser.Find(Bundled(), provider, model)?.DisplayName);
    }

    [Fact]
    [Trait("Feature", "IG-1")]
    [Trait("Decision", "OD-0134")]
    public void Claude_keeps_warm_with_a_1_hour_main_and_5_minute_helper_cache_and_Codex_does_not_keep_warm()
    {
        var file = Bundled();
        Assert.All(file.Models.Where(m => m.Provider == "claude"), m => Assert.Equal(new IgnisCachePoints(3600, 300, true), m.Cache));
        Assert.All(file.Models.Where(m => m.Provider == "codex"), m =>
        {
            Assert.False(m.Cache.KeepWarm);
            Assert.Null(m.Prices.CacheWrite5m);
            Assert.Null(m.Prices.CacheWrite1h);
        });
        // Opus 5.5 as printed on the Claude pricing page (USD per million tokens).
        var opus = IgnisPointsParser.Find(file, ProviderKind.Claude, "opus")!;
        Assert.Equal(new IgnisPrices(4m, 20m, 0.2m, 5m, 8m, new([])), opus.Prices);
        Assert.Equal(1_000_000, opus.ContextWindow);
        // Haiku 5.5's price step at 100K puts its tidy-up point at 95K.
        Assert.Equal(95_000, IgnisTidyPoints.For(IgnisPointsParser.Find(file, ProviderKind.Claude, "haiku")!).Tokens);
    }

    [Fact]
    [Trait("Feature", "IG-1")]
    [Trait("Decision", "OD-0134")]
    public void The_bundled_points_are_schema_v2_and_every_nth_provider_has_a_sourced_rule()
    {
        var file = Bundled();
        Assert.Equal(IgnisPointsFile.CurrentSchemaVersion, file.SchemaVersion);
        Assert.Equal(Enum.GetValues<ProviderKind>().Length, file.Providers.Count);
        foreach (var kind in Enum.GetValues<ProviderKind>())
        {
            var raw = SwiftEnum<ProviderKind>.RawValue(kind);
            var rule = file.Rule(raw);
            Assert.NotNull(rule);
            Assert.Equal("2026-10-10", rule.RetrievedAt);
            Assert.Equal(Uri.UriSchemeHttps, new Uri(rule.SourceUrl).Scheme);
            Assert.False(string.IsNullOrWhiteSpace(rule.Notes), raw);
            // Keep-warm pays only with per-token billing, a known lifetime and a write premium (ED-NEW-ignis-data); only Claude has all three.
            Assert.Equal(raw == "claude", rule.KeepWarm);
        }
        Assert.Equal(new IgnisCachePoints(3600, 300, true), file.Rule("claude")!.Cache);
        Assert.Equal(new IgnisCachePoints(1800, null, false), file.Rule("codex")!.Cache);
        Assert.Equal(IgnisBilling.None, file.Rule("local")!.Billing);
        Assert.Equal(new[] { "anthropic" }, file.Rule("claude")!.Catalogue);
        Assert.Equal(new[] { "openai" }, file.Rule("codex")!.Catalogue);
        Assert.Equal(new[] { "xai" }, file.Rule("grok")!.Catalogue);
        Assert.Equal(new[] { "gemini" }, file.Rule("antigravity")!.Catalogue);
        Assert.Equal(new[] { "meta", "meta_llama" }, file.Rule("meta")!.Catalogue);
    }

    [Theory]
    [Trait("Feature", "IG-1")]
    [Trait("Decision", "OD-0134")]
    [InlineData("claude-fable-5", "Fable 5", 1_000_000, 10, 1.0)]
    [InlineData("claude-opus-4-8", "Opus 4.8", 1_000_000, 5, 0.5)]
    [InlineData("claude-sonnet-4-6", "Sonnet 4.6", 200_000, 3, 0.3)]
    [InlineData("claude-sonnet-4-6[1m]", "Sonnet 4.6", 1_000_000, 3, 0.3)]
    public void The_2026_10_10_refresh_adds_the_Claude_models_Claude_Code_can_pin(string id, string name, long window, double input, double read)
    {
        var model = IgnisPointsParser.Find(Bundled(), ProviderKind.Claude, id)!;
        Assert.Equal((name, window, (decimal)input, (decimal)read), (model.DisplayName, model.ContextWindow, model.Prices.Input, model.Prices.CacheRead));
        Assert.Equal((decimal?)(model.Prices.Input * 1.25m), model.Prices.CacheWrite5m);
        Assert.Equal((decimal?)(model.Prices.Input * 2m), model.Prices.CacheWrite1h);
    }

    [Fact]
    [Trait("Feature", "IG-1")]
    [Trait("Decision", "OD-0134")]
    public void GPT_5_6_and_later_write_at_1_25x_for_30_minutes_and_earlier_models_name_no_write_or_lifetime()
    {
        var file = Bundled();
        string[] thirtyMinutes = ["gpt-6-astra", "gpt-6.1-sol", "gpt-6-sol", "gpt-6-luna", "gpt-5.6-sol", "gpt-5.6-terra", "gpt-5.6-luna"];
        foreach (var id in thirtyMinutes)
        {
            var model = IgnisPointsParser.Find(file, ProviderKind.Codex, id)!;
            Assert.Equal(1800, model.Cache.TtlMainSeconds);
            Assert.Equal((decimal?)(model.Prices.Input * 1.25m), model.Prices.WriteFor(TimeSpan.FromMinutes(30)));
            var step = Assert.Single(model.Prices.Steps);
            Assert.Equal((272_000L, model.Prices.Input * 2, model.Prices.Output * 1.5m, model.Prices.CacheRead * 2), (step.AboveTokens, step.Input, step.Output, step.CacheRead));
            Assert.Equal((decimal?)(model.Prices.Input * 2.5m), model.Prices.WriteFor(TimeSpan.FromMinutes(30), 300_000));
        }
        foreach (var id in new[] { "gpt-5.5", "gpt-5.4", "gpt-5.3-codex", "gpt-5.4-mini" })
        {
            var model = IgnisPointsParser.Find(file, ProviderKind.Codex, id)!;
            Assert.Null(model.Cache.TtlMainSeconds);
            Assert.Empty(model.Prices.CacheWrites);
        }
        Assert.Equal(0.1m, IgnisPointsParser.Find(file, ProviderKind.Codex, "gpt-6.1-sol")!.Prices.CacheRead);
        Assert.Equal("GPT-5.6 Sol", IgnisPointsParser.Find(file, ProviderKind.Codex, "gpt-5.6")?.DisplayName);
        Assert.Equal("https://developers.openai.com/api/docs/models/gpt-6.1-sol", IgnisPointsParser.Find(file, ProviderKind.Codex, "gpt-6.1-sol")!.SourceUrl);
    }
}
