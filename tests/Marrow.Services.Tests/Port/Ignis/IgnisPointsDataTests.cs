using Marrow.Core.Models;
using Marrow.Core.Port.Ignis;
using Marrow.Core.Port.Ignis.Engine;
using Marrow.Services.Port.Ignis;

namespace Marrow.Services.Tests.Port.Ignis;

// Ignis U1 (OD-0134, ED-NEW-ignis-engine): the bundled model points parse, name an official https source and a retrieval
// day for every model, cover the models Marrow offers, and keep Codex keep-warm off (PLAN lead decision).
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
}
