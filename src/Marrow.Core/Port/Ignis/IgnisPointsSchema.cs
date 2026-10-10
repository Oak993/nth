// PORT-ONLY: Ignis (OD-0134, ED-NEW-ignis-contract; PLAN unit U0; schema v2: ED-NEW-ignis-data). The model points file
// as records only: the parser and validator are IgnisPointsParser, the bundled data IgnisPointsData. The JSON shape
// (camelCase names, "?" = optional, "v2" = read only from a schemaVersion 2 file):
//
// {"schemaVersion":2, "version":"yyyy.MM.dd",
//  "providers"(v2)?:[{"provider":"claude", "billing":"perToken"|"flat"|"none", "ttlMainSeconds"?:3600, "ttlSubagentSeconds"?:300,
//     "keepWarm":true, "catalogue"?:["anthropic"], "notes"?:"...", "sourceUrl":"https://...", "retrievedAt":"yyyy-MM-dd"}],
//  "models":[{
//    "provider":"claude", "ids":["..."], "aliases":["..."], "displayName":"Opus 5.5", "contextWindow":1000000,
//    "prices":{"input":5, "output":25, "cacheRead":0.5, "cacheWrite5m"?:6.25, "cacheWrite1h"?:10,
//              "cacheWrites"(v2)?:[{"ttlSeconds":1800, "price":2.5}],
//              "steps":[{"aboveTokens":200000, "input":..., "output":..., "cacheRead":..., "cacheWrite5m"?:..., "cacheWrite1h"?:...,
//                        "cacheWrites"(v2)?:[...]}]},
//    "cache":{"ttlMainSeconds"?:3600, "ttlSubagentSeconds"?:300, "keepWarm":true},
//    "tidyAt"?:400000, "tidyReason"?:"...", "helperModel"?:"...",
//    "sourceUrl":"https://...", "retrievedAt":"yyyy-MM-dd"}]}
//
// Prices are US dollars per million tokens, from the provider's official pricing page named by sourceUrl on retrievedAt
// (never invented). "provider" is the ProviderKind raw value ("claude", "codex", "deepseek", ...). A step applies when
// the prompt is above aboveTokens and replaces every price it names. tidyAt overrides the computed tidy-up point.
// cacheWrites holds write prices at lifetimes other than 5 minutes and 1 hour (OpenAI's 30-minute writes). A provider
// rule covers that provider's models the file does not list one by one (models found only in the price catalogue,
// IgnisPriceCatalogue) and names the catalogue providers whose prices map to it.
namespace Marrow.Core.Port.Ignis;

/// <summary>The whole points file. <see cref="Version"/> is "yyyy.MM.dd" and orders ordinally.</summary>
public sealed record IgnisPointsFile(int SchemaVersion, string Version, ValueList<IgnisModelPoints> Models)
{
    /// <summary>The schema this build writes (the bundled data and the feed).</summary>
    public const int CurrentSchemaVersion = 2;

    /// <summary>The oldest schema this build still reads.</summary>
    public const int OldestSchemaVersion = 1;

    /// <summary>The provider rules (schema v2); empty in a v1 file.</summary>
    public ValueList<IgnisProviderRule> Providers { get; init; } = ValueList<IgnisProviderRule>.Empty;

    /// <summary>The rule for a ProviderKind raw value; null when the file has none.</summary>
    public IgnisProviderRule? Rule(string provider) => Providers.FirstOrDefault(rule => string.Equals(rule.Provider, provider, StringComparison.Ordinal));
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

/// <summary>A cache write price (USD per million tokens) for a lifetime the 5-minute and 1-hour fields do not name.</summary>
public sealed record IgnisCacheWrite(int TtlSeconds, decimal Price);

/// <summary>Base prices (USD per million tokens) and the steps above a prompt size. A null write price: no write premium.</summary>
public sealed record IgnisPrices(
    decimal Input,
    decimal Output,
    decimal CacheRead,
    decimal? CacheWrite5m,
    decimal? CacheWrite1h,
    ValueList<IgnisPriceStep> Steps)
{
    /// <summary>Write prices at other lifetimes (schema v2), one per lifetime.</summary>
    public ValueList<IgnisCacheWrite> CacheWrites { get; init; } = ValueList<IgnisCacheWrite>.Empty;

    /// <summary>The write price for exactly this lifetime (the 5-minute or 1-hour field, else <see cref="CacheWrites"/>); null when none is known.</summary>
    public decimal? WriteFor(TimeSpan ttl) => IgnisCacheWrites.For(ttl, CacheWrite5m, CacheWrite1h, CacheWrites);

    /// <summary>
    /// The write price for this lifetime in force for a prompt of <paramref name="promptTokens"/>: the highest step the
    /// prompt is above names it, else the base (a step that leaves a write price out keeps the base's, as IgnisMoney.Tier does).
    /// </summary>
    public decimal? WriteFor(TimeSpan ttl, long promptTokens)
    {
        var price = WriteFor(ttl);
        foreach (var step in Steps.OrderBy(step => step.AboveTokens))
        {
            if (promptTokens <= step.AboveTokens) break;
            price = step.WriteFor(ttl) ?? price;
        }
        return price;
    }
}

/// <summary>The prices that apply once the prompt is above <see cref="AboveTokens"/>.</summary>
public sealed record IgnisPriceStep(
    long AboveTokens,
    decimal Input,
    decimal Output,
    decimal CacheRead,
    decimal? CacheWrite5m,
    decimal? CacheWrite1h)
{
    /// <summary>Write prices at other lifetimes (schema v2), one per lifetime.</summary>
    public ValueList<IgnisCacheWrite> CacheWrites { get; init; } = ValueList<IgnisCacheWrite>.Empty;

    /// <summary>The write price for exactly this lifetime; null when the step does not name one.</summary>
    public decimal? WriteFor(TimeSpan ttl) => IgnisCacheWrites.For(ttl, CacheWrite5m, CacheWrite1h, CacheWrites);
}

/// <summary>Cache lifetimes (seconds) for the main chat and for sub-agents, and whether keep-warm is on for the model.</summary>
public sealed record IgnisCachePoints(int? TtlMainSeconds, int? TtlSubagentSeconds, bool KeepWarm);

/// <summary>How a provider bills: per token (API key), a flat plan or subscription, or nothing (a local server).</summary>
public enum IgnisBilling
{
    PerToken,
    Flat,
    None,
}

/// <summary>
/// One provider's rule (schema v2): how it bills, its cache lifetimes, whether keep-warm pays, and the price catalogue
/// providers (LiteLLM's litellm_provider names) whose models belong to it. It covers the provider's models the file does
/// not list one by one; a listed model's own cache points always win.
/// </summary>
public sealed record IgnisProviderRule(
    string Provider,
    IgnisBilling Billing,
    int? TtlMainSeconds,
    int? TtlSubagentSeconds,
    bool KeepWarm,
    ValueList<string> Catalogue,
    string? Notes,
    string SourceUrl,
    string RetrievedAt)
{
    /// <summary>The cache points a model the file does not list gets from this rule.</summary>
    public IgnisCachePoints Cache => new(TtlMainSeconds, TtlSubagentSeconds, KeepWarm);
}

/// <summary>Picks the write price for an exact lifetime.</summary>
public static class IgnisCacheWrites
{
    public static decimal? For(TimeSpan ttl, decimal? write5m, decimal? write1h, IReadOnlyList<IgnisCacheWrite> writes)
    {
        if (ttl == TimeSpan.FromMinutes(5) && write5m is { } five) return five;
        if (ttl == TimeSpan.FromHours(1) && write1h is { } hour) return hour;
        foreach (var write in writes)
            if (TimeSpan.FromSeconds(write.TtlSeconds) == ttl) return write.Price;
        return null;
    }
}
