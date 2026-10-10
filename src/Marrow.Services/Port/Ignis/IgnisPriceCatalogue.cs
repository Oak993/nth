// PORT-ONLY: Ignis (OD-0134, ED-NEW-ignis-data). The all-model price catalogue: LiteLLM's public price file on GitHub
// (BerriAI/litellm, model_prices_and_context_window.json, about 2.7 MB and 4,000 models), fetched through the points feed's
// GitHub-only rule (never a model provider, never a model call) and trimmed to the catalogue providers the points file maps
// to nth providers (IgnisProviderRule.Catalogue). Prices there are US dollars per token; here they are per million tokens,
// converted from the number's own text in decimal (no binary rounding). Only the standard prices are read: the batch,
// flex, priority and ultrafast variants and the regional uplifts are not.
//
// ENGINEERING DECISIONS (ED-NEW-ignis-data):
// - "cache_creation_input_token_cost" is the 5-minute write for "anthropic" (with "..._above_1hr" the 1-hour write, as
//   LiteLLM documents them); for any other catalogue provider it is the write at the provider rule's ttlMainSeconds, and it
//   is left out when the rule names no lifetime. A write of 0 is no write premium.
// - A model with no cache-read price gets its input price (no cache saving claimed); a price step with no cache-read price
//   gets its own input price. A step needs its input and output prices and must lie within the context window.
// - Guardrails: every price at least 0, cache read at most input, a write at least the cache read (a write below it is
//   left out); a model that fails is set aside with a warning. For a model the curated points also list, a catalogue price
//   more than 4x (either way) from the curated one keeps the curated prices and records a warning; otherwise the catalogue's
//   prices replace the curated ones only once the curated entry is older than 30 days. Curated rules (lifetimes, keep-warm,
//   tidyAt, helperModel, context window) are never replaced.
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Ignis;
using Marrow.Core.Port.Ignis.Engine;

namespace Marrow.Services.Port.Ignis;

/// <summary>One catalogue model: the nth provider (raw value), the catalogue provider it came from, its id, window and prices.</summary>
public sealed record IgnisCatalogueModel(string Provider, string Source, string Id, long ContextWindow, IgnisPrices Prices);

/// <summary>A trimmed catalogue: when its content was fetched and last confirmed current, the ETag that confirms it, its models.</summary>
public sealed class IgnisCatalogue
{
    private readonly Dictionary<string, IgnisCatalogueModel> index = new(StringComparer.OrdinalIgnoreCase);

    public IgnisCatalogue(DateTimeOffset fetchedAt, DateTimeOffset checkedAt, string? etag, IReadOnlyList<IgnisCatalogueModel> models, IReadOnlyList<string> warnings)
    {
        (FetchedAt, CheckedAt, ETag, Models, Warnings) = (fetchedAt, checkedAt, etag, models, warnings);
        foreach (var model in models) index.TryAdd(Key(model.Provider, model.Id), model);
    }

    public DateTimeOffset FetchedAt { get; }

    public DateTimeOffset CheckedAt { get; }

    public string? ETag { get; }

    public IReadOnlyList<IgnisCatalogueModel> Models { get; }

    public IReadOnlyList<string> Warnings { get; }

    /// <summary>The day the catalogue was last confirmed current ("yyyy-MM-dd", UTC).</summary>
    public string Day => CheckedAt.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>The same catalogue confirmed current again at <paramref name="at"/>.</summary>
    public IgnisCatalogue Confirmed(DateTimeOffset at, string? etag) => new(FetchedAt, at, etag ?? ETag, Models, Warnings);

    /// <summary>The model with this id for the nth provider (ordinal, case-insensitive); null when the catalogue has none.</summary>
    public IgnisCatalogueModel? Lookup(string provider, string id) => index.GetValueOrDefault(Key(provider, id.Trim()));

    private static string Key(string provider, string id) => provider + "\u0000" + id;
}

/// <summary>A built catalogue, or why none was built.</summary>
public sealed record IgnisCatalogueBuild(IgnisCatalogue? Catalogue, string? Error);

public static partial class IgnisPriceCatalogue
{
    /// <summary>LiteLLM's public price file on GitHub.</summary>
    public const string Url = "https://raw.githubusercontent.com/BerriAI/litellm/main/model_prices_and_context_window.json";

    /// <summary>The largest body read (the file is about 2.7 MB).</summary>
    public const int MaximumBytes = 8 << 20;

    /// <summary>How long one fetch may take in all.</summary>
    public static TimeSpan Timeout { get; } = TimeSpan.FromSeconds(60);

    /// <summary>How often an automatic check asks again.</summary>
    public static TimeSpan CheckEvery { get; } = TimeSpan.FromDays(1);

    /// <summary>A curated entry older than this takes the catalogue's prices.</summary>
    public static TimeSpan CuratedFreshFor { get; } = TimeSpan.FromDays(30);

    /// <summary>How far (either way) a catalogue price may sit from the curated one before the curated prices are kept.</summary>
    public const decimal MaximumDrift = 4m;

    private const string Anthropic = "anthropic";

    private static readonly string[] Modes = ["chat", "responses"];

    [GeneratedRegex(@"^(input_cost_per_token|output_cost_per_token|cache_read_input_token_cost|cache_creation_input_token_cost|cache_creation_input_token_cost_above_1hr)_above_(\d+)k_tokens$", RegexOptions.CultureInvariant)]
    private static partial Regex StepField();

    /// <summary>One GET of the catalogue (GitHub only); with <paramref name="etag"/>, an unchanged file answers 304.</summary>
    public static Task<IgnisFeedResult> Fetch(Uri address, string? etag, CancellationToken cancellationToken) =>
        IgnisPointsFeed.Fetch(address, MaximumBytes, Timeout, etag, "The price catalogue must be on GitHub.", cancellationToken);

    /// <summary>
    /// Builds the trimmed catalogue from LiteLLM's file: the models of every catalogue provider <paramref name="points"/>
    /// maps, after the guardrails; warnings name what was set aside. Never throws.
    /// </summary>
    public static IgnisCatalogueBuild Build(string? json, IgnisPointsFile points, DateTimeOffset now, string? etag)
    {
        if (string.IsNullOrWhiteSpace(json)) return new(null, "The price catalogue is empty.");
        var map = new Dictionary<string, IgnisProviderRule>(StringComparer.Ordinal);
        foreach (var rule in points.Providers)
            foreach (var name in rule.Catalogue)
                map[name] = rule;
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return new(null, "The price catalogue is not an object.");
            var models = new List<IgnisCatalogueModel>();
            var byKey = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var warnings = new List<string>();
            foreach (var property in document.RootElement.EnumerateObject())
            {
                var entry = property.Value;
                if (entry.ValueKind != JsonValueKind.Object) continue;
                if (Text(entry, "litellm_provider") is not { } source || !map.TryGetValue(source, out var rule)) continue;
                if (!Modes.Contains(Text(entry, "mode"))) continue;
                var prefixed = property.Name.StartsWith(source + "/", StringComparison.Ordinal);
                var id = prefixed ? property.Name[(source.Length + 1)..] : property.Name;
                if (id.Length == 0 || id.Contains('/', StringComparison.Ordinal) || id.Trim() != id) continue;
                var (model, warning) = Model(entry, rule, source, id);
                if (warning is not null) warnings.Add(id + ": " + warning);
                if (model is null) continue;
                var key = rule.Provider + "\u0000" + id;
                if (byKey.TryGetValue(key, out var at))
                {
                    // The same model under "provider/id" and "id": the bare key wins.
                    if (!prefixed) models[at] = model;
                    continue;
                }
                byKey[key] = models.Count;
                models.Add(model);
            }
            foreach (var curated in points.Models)
                foreach (var id in curated.Ids)
                    if (models.FirstOrDefault(model => model.Provider == curated.Provider && string.Equals(model.Id, id, StringComparison.OrdinalIgnoreCase)) is { } found
                        && Drift(curated.Prices, found.Prices) is { } drift)
                        warnings.Add(id + ": " + drift + "; the curated prices stay.");
            if (models.Count == 0) return new(null, "The price catalogue has no model for the mapped providers.");
            return new(new IgnisCatalogue(now, now, etag, models, warnings), null);
        }
        catch (JsonException error)
        {
            return new(null, "The price catalogue is not JSON: " + error.Message);
        }
    }

    /// <summary>
    /// The points for <paramref name="curated"/> with the catalogue's prices when they may replace the curated ones (the
    /// curated entry is older than 30 days on <paramref name="today"/> and no price drifts more than 4x); else the curated points.
    /// </summary>
    public static IgnisModelPoints Merge(IgnisModelPoints curated, IgnisCatalogue? catalogue, DateOnly today)
    {
        if (catalogue is null) return curated;
        if (!DateOnly.TryParseExact(curated.RetrievedAt, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var retrieved)) return curated;
        if (retrieved.AddDays((int)CuratedFreshFor.TotalDays) >= today) return curated;
        foreach (var id in curated.Ids)
        {
            if (catalogue.Lookup(curated.Provider, id) is not { } entry) continue;
            if (Drift(curated.Prices, entry.Prices) is not null) return curated;
            return curated with { Prices = entry.Prices };
        }
        return curated;
    }

    /// <summary>
    /// A model only the catalogue knows, as points with its provider's rule (lifetimes, keep-warm) and its id as its name;
    /// null when the catalogue has no such model or the provider has no rule.
    /// </summary>
    public static IgnisModelPoints? Find(IgnisCatalogue? catalogue, IgnisPointsFile points, ProviderKind provider, string? model)
    {
        if (catalogue is null || string.IsNullOrWhiteSpace(model)) return null;
        var raw = SwiftEnum<ProviderKind>.RawValue(provider);
        if (points.Rule(raw) is not { } rule) return null;
        var trimmed = model.Trim();
        var entry = catalogue.Lookup(raw, trimmed);
        if (entry is null && trimmed.IndexOf('[', StringComparison.Ordinal) is > 0 and var open && trimmed.EndsWith(']'))
            entry = catalogue.Lookup(raw, trimmed[..open]);
        if (entry is null) return null;
        return new IgnisModelPoints(raw, new ValueList<string>([entry.Id]), ValueList<string>.Empty, entry.Id, entry.ContextWindow, entry.Prices,
            rule.Cache, null, null, null, Url, catalogue.Day);
    }

    /// <summary>Why the catalogue's prices are too far from the curated ones (more than 4x either way); null when they are not.</summary>
    public static string? Drift(IgnisPrices curated, IgnisPrices catalogue)
    {
        foreach (var (name, mine, theirs) in new (string, decimal?, decimal?)[]
                 {
                     ("input", curated.Input, catalogue.Input),
                     ("output", curated.Output, catalogue.Output),
                     ("cache read", curated.CacheRead, catalogue.CacheRead),
                     ("5-minute write", curated.CacheWrite5m, catalogue.CacheWrite5m),
                     ("1-hour write", curated.CacheWrite1h, catalogue.CacheWrite1h),
                 })
        {
            if (mine is not { } a || theirs is not { } b || a == b) continue;
            if (a == 0 || b == 0 || b > a * MaximumDrift || a > b * MaximumDrift)
                return "the catalogue's " + name + " price " + Text(b) + " is more than " + Text(MaximumDrift) + "x from the curated " + Text(a);
        }
        return null;
    }

    // ---- the stored copy (Storage.Root/ignis/prices.json) ----

    /// <summary>The trimmed catalogue as JSON (the stored copy).</summary>
    public static string Write(IgnisCatalogue catalogue)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = false }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", 1);
            writer.WriteString("source", Url);
            writer.WriteString("fetchedAt", catalogue.FetchedAt.UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
            writer.WriteString("checkedAt", catalogue.CheckedAt.UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
            if (catalogue.ETag is { } etag) writer.WriteString("etag", etag);
            writer.WriteStartArray("models");
            foreach (var model in catalogue.Models)
            {
                writer.WriteStartObject();
                writer.WriteString("provider", model.Provider);
                writer.WriteString("source", model.Source);
                writer.WriteString("id", model.Id);
                writer.WriteNumber("contextWindow", model.ContextWindow);
                writer.WritePropertyName("prices");
                WritePrices(writer, model.Prices);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteStartArray("warnings");
            foreach (var warning in catalogue.Warnings) writer.WriteStringValue(warning);
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <summary>Reads a stored copy; null when it is missing a part or any model fails the points file's price rules. Never throws.</summary>
    public static IgnisCatalogue? Read(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            if (!root.TryGetProperty("schemaVersion", out var schema) || schema.ValueKind != JsonValueKind.Number || schema.GetInt32() != 1) return null;
            if (!Instant(root, "fetchedAt", out var fetchedAt) || !Instant(root, "checkedAt", out var checkedAt)) return null;
            if (!root.TryGetProperty("models", out var models) || models.ValueKind != JsonValueKind.Array) return null;
            var list = new List<IgnisCatalogueModel>();
            foreach (var model in models.EnumerateArray())
            {
                if (model.ValueKind != JsonValueKind.Object) return null;
                if (Text(model, "provider") is not { } provider || SwiftEnum<ProviderKind>.FromRawValue(provider) is null) return null;
                if (Text(model, "source") is not { Length: > 0 } source || Text(model, "id") is not { Length: > 0 } id) return null;
                if (!model.TryGetProperty("contextWindow", out var window) || window.ValueKind != JsonValueKind.Number || !window.TryGetInt64(out var tokens) || tokens <= 0) return null;
                if (!model.TryGetProperty("prices", out var pricesElement)) return null;
                if (IgnisPointsParser.ParsePrices(pricesElement, tokens).Prices is not { } prices) return null;
                list.Add(new IgnisCatalogueModel(provider, source, id, tokens, prices));
            }
            var warnings = new List<string>();
            if (root.TryGetProperty("warnings", out var stored) && stored.ValueKind == JsonValueKind.Array)
                foreach (var warning in stored.EnumerateArray())
                    if (warning.ValueKind == JsonValueKind.String) warnings.Add(warning.GetString()!);
            return new IgnisCatalogue(fetchedAt, checkedAt, Text(root, "etag"), list, warnings);
        }
        catch (JsonException) { return null; }
        catch (InvalidOperationException) { return null; }
        catch (FormatException) { return null; }
    }

    // ---- reading LiteLLM's entries ----

    private static (IgnisCatalogueModel? Model, string? Warning) Model(JsonElement entry, IgnisProviderRule rule, string source, string id)
    {
        if (!entry.TryGetProperty("max_input_tokens", out var max) || max.ValueKind != JsonValueKind.Number || !max.TryGetInt64(out var window) || window <= 0)
            return (null, null); // no context window: not a model Ignis can plan for
        if (Cost(entry, "input_cost_per_token") is not { } input || Cost(entry, "output_cost_per_token") is not { } output) return (null, null);
        var anthropic = source == Anthropic;
        var (read, write5m, write1h, writes) = CacheParts(entry, "", input, rule, anthropic);

        var steps = new List<IgnisPriceStep>();
        foreach (var above in StepSizes(entry))
        {
            var suffix = "_above_" + above.ToString(CultureInfo.InvariantCulture) + "k_tokens";
            var tokens = above * 1000L;
            if (tokens >= window) continue;
            if (Cost(entry, "input_cost_per_token" + suffix) is not { } stepInput || Cost(entry, "output_cost_per_token" + suffix) is not { } stepOutput) continue;
            var (stepRead, s5, s1, stepWrites) = CacheParts(entry, suffix, stepInput, rule, anthropic);
            steps.Add(new IgnisPriceStep(tokens, stepInput, stepOutput, stepRead, s5, s1) { CacheWrites = stepWrites });
        }
        var prices = new IgnisPrices(input, output, read, write5m, write1h, new ValueList<IgnisPriceStep>(steps)) { CacheWrites = writes };
        return Guard(prices) is { } problem ? (null, problem + "; set aside.") : (new IgnisCatalogueModel(rule.Provider, source, id, window, Clean(prices)), null);
    }

    private static (decimal Read, decimal? Write5m, decimal? Write1h, ValueList<IgnisCacheWrite> Writes) CacheParts(
        JsonElement entry, string suffix, decimal input, IgnisProviderRule rule, bool anthropic)
    {
        var read = Cost(entry, "cache_read_input_token_cost" + suffix) ?? input;
        var write = NonZero(Cost(entry, "cache_creation_input_token_cost" + suffix));
        if (anthropic)
            return (read, write, NonZero(Cost(entry, "cache_creation_input_token_cost_above_1hr" + suffix)), ValueList<IgnisCacheWrite>.Empty);
        return (rule.TtlMainSeconds, write) switch
        {
            (_, null) or (null, _) => (read, null, null, ValueList<IgnisCacheWrite>.Empty),
            (300, { } five) => (read, five, null, ValueList<IgnisCacheWrite>.Empty),
            (3600, { } hour) => (read, null, hour, ValueList<IgnisCacheWrite>.Empty),
            ({ } ttl, { } other) => (read, null, null, new ValueList<IgnisCacheWrite>([new IgnisCacheWrite(ttl, other)])),
        };
    }

    private static IEnumerable<int> StepSizes(JsonElement entry)
    {
        var sizes = new SortedSet<int>();
        foreach (var property in entry.EnumerateObject())
            if (StepField().Match(property.Name) is { Success: true } match && int.TryParse(match.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var size) && size > 0)
                sizes.Add(size);
        return sizes;
    }

    /// <summary>What breaks a guardrail; null when the prices are sound. A write below the cache read is dropped first (Clean).</summary>
    private static string? Guard(IgnisPrices prices)
    {
        if (prices.Input < 0 || prices.Output < 0 || prices.CacheRead < 0) return "a price is below 0";
        if (prices.CacheRead > prices.Input) return "its cache read costs more than its input";
        foreach (var step in prices.Steps)
        {
            if (step.Input < 0 || step.Output < 0 || step.CacheRead < 0) return "a price step is below 0";
            if (step.CacheRead > step.Input) return "a price step's cache read costs more than its input";
        }
        return null;
    }

    /// <summary>Leaves out a write priced below the cache read (no sound write premium).</summary>
    private static IgnisPrices Clean(IgnisPrices prices)
    {
        decimal? Keep(decimal? write, decimal read) => write is { } price && price >= read ? price : null;
        ValueList<IgnisCacheWrite> KeepAll(ValueList<IgnisCacheWrite> writes, decimal read) => new(writes.Where(write => write.Price >= read));
        return prices with
        {
            CacheWrite5m = Keep(prices.CacheWrite5m, prices.CacheRead),
            CacheWrite1h = Keep(prices.CacheWrite1h, prices.CacheRead),
            CacheWrites = KeepAll(prices.CacheWrites, prices.CacheRead),
            Steps = new ValueList<IgnisPriceStep>(prices.Steps.Select(step => step with
            {
                CacheWrite5m = Keep(step.CacheWrite5m, step.CacheRead),
                CacheWrite1h = Keep(step.CacheWrite1h, step.CacheRead),
                CacheWrites = KeepAll(step.CacheWrites, step.CacheRead),
            })),
        };
    }

    private static decimal? NonZero(decimal? price) => price is { } value && value != 0 ? value : null;

    /// <summary>A per-token cost as US dollars per million tokens, from the number's text (exact decimal; 9 places at most).</summary>
    private static decimal? Cost(JsonElement entry, string name)
    {
        if (!entry.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Number) return null;
        if (!decimal.TryParse(value.GetRawText(), NumberStyles.Float, CultureInfo.InvariantCulture, out var perToken)) return null;
        return Normal(Math.Round(perToken * 1_000_000m, 9, MidpointRounding.ToEven));
    }

    /// <summary>The same value without trailing zeros (0.200000 as 0.2).</summary>
    private static decimal Normal(decimal value) => value / 1.000000000000000000000000000000000m;

    private static string Text(decimal value) => Normal(value).ToString(CultureInfo.InvariantCulture);

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool Instant(JsonElement element, string name, out DateTimeOffset value)
    {
        value = default;
        return Text(element, name) is { } text
            && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out value);
    }

    private static void WritePrices(Utf8JsonWriter writer, IgnisPrices prices)
    {
        writer.WriteStartObject();
        WriteTier(writer, prices.Input, prices.Output, prices.CacheRead, prices.CacheWrite5m, prices.CacheWrite1h, prices.CacheWrites);
        writer.WriteStartArray("steps");
        foreach (var step in prices.Steps)
        {
            writer.WriteStartObject();
            writer.WriteNumber("aboveTokens", step.AboveTokens);
            WriteTier(writer, step.Input, step.Output, step.CacheRead, step.CacheWrite5m, step.CacheWrite1h, step.CacheWrites);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteTier(Utf8JsonWriter writer, decimal input, decimal output, decimal read, decimal? write5m, decimal? write1h, ValueList<IgnisCacheWrite> writes)
    {
        writer.WriteNumber("input", Normal(input));
        writer.WriteNumber("output", Normal(output));
        writer.WriteNumber("cacheRead", Normal(read));
        if (write5m is { } five) writer.WriteNumber("cacheWrite5m", Normal(five));
        if (write1h is { } hour) writer.WriteNumber("cacheWrite1h", Normal(hour));
        if (writes.Count == 0) return;
        writer.WriteStartArray("cacheWrites");
        foreach (var write in writes)
        {
            writer.WriteStartObject();
            writer.WriteNumber("ttlSeconds", write.TtlSeconds);
            writer.WriteNumber("price", Normal(write.Price));
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
    }
}
