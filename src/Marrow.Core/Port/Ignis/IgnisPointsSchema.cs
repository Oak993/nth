// PORT-ONLY: Ignis (OD-0134, ED-NEW-ignis-contract; PLAN unit U0). The model points file, schema v1, as records only:
// the parser, the validator and the bundled data are unit U1's. The JSON shape (camelCase names, "?" = optional):
//
// {"schemaVersion":1, "version":"yyyy.MM.dd", "models":[{
//    "provider":"claude", "ids":["..."], "aliases":["..."], "displayName":"Opus 5.5", "contextWindow":1000000,
//    "prices":{"input":5, "output":25, "cacheRead":0.5, "cacheWrite5m"?:6.25, "cacheWrite1h"?:10,
//              "steps":[{"aboveTokens":200000, "input":..., "output":..., "cacheRead":..., "cacheWrite5m"?:..., "cacheWrite1h"?:...}]},
//    "cache":{"ttlMainSeconds"?:3600, "ttlSubagentSeconds"?:300, "keepWarm":true},
//    "tidyAt"?:400000, "tidyReason"?:"...", "helperModel"?:"...",
//    "sourceUrl":"https://...", "retrievedAt":"yyyy-MM-dd"}]}
//
// Prices are US dollars per million tokens, from the provider's official pricing page named by sourceUrl on retrievedAt
// (never invented). "provider" is the ProviderKind raw value ("claude", "codex", "deepseek", ...). A step applies when
// the prompt is above aboveTokens and replaces every price it names. tidyAt overrides the computed tidy-up point.
namespace Marrow.Core.Port.Ignis;

/// <summary>The whole points file. <see cref="Version"/> is "yyyy.MM.dd" and orders ordinally.</summary>
public sealed record IgnisPointsFile(int SchemaVersion, string Version, ValueList<IgnisModelPoints> Models)
{
    /// <summary>The only schema this build reads.</summary>
    public const int CurrentSchemaVersion = 1;
}

/// <summary>One model's points.</summary>
public sealed record IgnisModelPoints(
    string Provider,
    ValueList<string> Ids,
    ValueList<string> Aliases,
    string DisplayName,
    long ContextWindow,
    IgnisPrices Prices,
    IgnisCachePoints Cache,
    long? TidyAt,
    string? TidyReason,
    string? HelperModel,
    string SourceUrl,
    string RetrievedAt);

/// <summary>Base prices (USD per million tokens) and the steps above a prompt size. A null write price: no write premium.</summary>
public sealed record IgnisPrices(
    decimal Input,
    decimal Output,
    decimal CacheRead,
    decimal? CacheWrite5m,
    decimal? CacheWrite1h,
    ValueList<IgnisPriceStep> Steps);

/// <summary>The prices that apply once the prompt is above <see cref="AboveTokens"/>.</summary>
public sealed record IgnisPriceStep(
    long AboveTokens,
    decimal Input,
    decimal Output,
    decimal CacheRead,
    decimal? CacheWrite5m,
    decimal? CacheWrite1h);

/// <summary>Cache lifetimes (seconds) for the main chat and for sub-agents, and whether keep-warm is on for the model.</summary>
public sealed record IgnisCachePoints(int? TtlMainSeconds, int? TtlSubagentSeconds, bool KeepWarm);
