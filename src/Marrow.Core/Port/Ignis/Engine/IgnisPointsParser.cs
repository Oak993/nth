// PORT-ONLY: Ignis (OD-0134, ED-NEW-ignis-engine; work/ignis/PLAN.md unit U1; schema v2: ED-NEW-ignis-data). Reads and
// validates a model points file (schema v1 or v2, IgnisPointsSchema.cs) and finds a model in it. A file is taken whole or
// not at all: any invalid model or provider rule rejects the file with the first error found, so a half-broken feed never
// replaces good points. Unknown members are ignored (a newer writer may add fields); the v2 members (providers,
// cacheWrites) are read only from a v2 file; any other schemaVersion is rejected.
using System.Globalization;
using System.Text.Json;
using Marrow.Core.Models;

namespace Marrow.Core.Port.Ignis.Engine;

/// <summary>The result of reading a points file: the file, or the first reason it was rejected.</summary>
public sealed record IgnisPointsParse(IgnisPointsFile? File, string? Error)
{
    public bool Ok => File is not null;
}

public static class IgnisPointsParser
{
    /// <summary>Reads and validates a points file; never throws.</summary>
    public static IgnisPointsParse Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Fail("empty");
        try
        {
            using var document = JsonDocument.Parse(json);
            return Read(document.RootElement);
        }
        catch (JsonException error)
        {
            return Fail("not JSON: " + error.Message);
        }
        catch (InvalidOperationException error)
        {
            return Fail("wrong type: " + error.Message);
        }
    }

    /// <summary>Whether a version reads "yyyy.MM.dd" and names a real day (the versions order ordinally).</summary>
    public static bool IsVersion(string? version) =>
        version is { Length: 10 } && DateOnly.TryParseExact(version, "yyyy.MM.dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

    /// <summary>Whether a retrieval date reads "yyyy-MM-dd" and names a real day.</summary>
    public static bool IsDay(string? day) =>
        day is { Length: 10 } && DateOnly.TryParseExact(day, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

    /// <summary>
    /// The model whose ids or aliases match <paramref name="model"/> for the provider, ordinal and case-insensitive.
    /// ENGINEERING DECISION: a Claude Code selector suffix such as "[1m]" ("claude-fable-5-1[1m]") is dropped when the
    /// whole id does not match, so the 1M-context selector finds its model.
    /// </summary>
    public static IgnisModelPoints? Find(IgnisPointsFile file, ProviderKind provider, string? model)
    {
        if (string.IsNullOrWhiteSpace(model)) return null;
        var raw = SwiftEnum<ProviderKind>.RawValue(provider);
        var trimmed = model.Trim();
        return Match(file, raw, trimmed) ?? (StripSelector(trimmed) is { } bare ? Match(file, raw, bare) : null);
    }

    private static IgnisModelPoints? Match(IgnisPointsFile file, string provider, string model)
    {
        foreach (var points in file.Models)
        {
            if (!string.Equals(points.Provider, provider, StringComparison.Ordinal)) continue;
            if (points.Ids.Any(id => string.Equals(id, model, StringComparison.OrdinalIgnoreCase))) return points;
            if (points.Aliases.Any(alias => string.Equals(alias, model, StringComparison.OrdinalIgnoreCase))) return points;
        }
        return null;
    }

    private static string? StripSelector(string model)
    {
        var open = model.IndexOf('[', StringComparison.Ordinal);
        return open > 0 && model.EndsWith(']') ? model[..open] : null;
    }

    /// <summary>
    /// Reads one "prices" object with the schema v2 members, for a model of <paramref name="window"/> tokens (the price
    /// catalogue's stored copy uses it); never throws.
    /// </summary>
    public static (IgnisPrices? Prices, string? Error) ParsePrices(JsonElement element, long window)
    {
        if (element.ValueKind != JsonValueKind.Object) return (null, "prices: missing");
        try { return Prices(element, "prices", window, v2: true); }
        catch (InvalidOperationException error) { return (null, "prices: wrong type: " + error.Message); }
    }

    // ---- reading ----

    private static IgnisPointsParse Read(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object) return Fail("the file is not an object");
        if (Int(root, "schemaVersion") is not { } schema) return Fail("schemaVersion: missing");
        if (schema is < IgnisPointsFile.OldestSchemaVersion or > IgnisPointsFile.CurrentSchemaVersion)
            return Fail("schemaVersion: " + schema.ToString(CultureInfo.InvariantCulture) + " is not supported");
        var v2 = schema >= 2;
        var version = Text(root, "version");
        if (!IsVersion(version)) return Fail("version: not yyyy.MM.dd");
        if (!root.TryGetProperty("models", out var models) || models.ValueKind != JsonValueKind.Array) return Fail("models: missing");
        if (models.GetArrayLength() == 0) return Fail("models: empty");

        var list = new List<IgnisModelPoints>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var index = 0;
        foreach (var element in models.EnumerateArray())
        {
            var at = "models[" + index.ToString(CultureInfo.InvariantCulture) + "]";
            var (model, error) = Model(element, at, v2);
            if (model is null) return Fail(error!);
            foreach (var name in model.Ids.Concat(model.Aliases))
                if (!seen.Add(model.Provider + "\u0000" + name)) return Fail(at + ": \"" + name + "\" is already used by another " + model.Provider + " model");
            list.Add(model);
            index++;
        }
        var rules = new List<IgnisProviderRule>();
        if (v2 && root.TryGetProperty("providers", out var providers) && providers.ValueKind != JsonValueKind.Null)
        {
            if (providers.ValueKind != JsonValueKind.Array) return Fail("providers: not a list");
            var catalogueNames = new HashSet<string>(StringComparer.Ordinal);
            index = 0;
            foreach (var element in providers.EnumerateArray())
            {
                var at = "providers[" + index.ToString(CultureInfo.InvariantCulture) + "]";
                var (rule, error) = Rule(element, at);
                if (rule is null) return Fail(error!);
                if (rules.Any(other => other.Provider == rule.Provider)) return Fail(at + ": \"" + rule.Provider + "\" already has a rule");
                foreach (var name in rule.Catalogue)
                    if (!catalogueNames.Add(name)) return Fail(at + ".catalogue: \"" + name + "\" already maps to another provider");
                rules.Add(rule);
                index++;
            }
        }
        return new IgnisPointsParse(new IgnisPointsFile((int)schema, version!, new ValueList<IgnisModelPoints>(list)) { Providers = new ValueList<IgnisProviderRule>(rules) }, null);
    }

    private static (IgnisProviderRule? Rule, string? Error) Rule(JsonElement element, string at)
    {
        if (element.ValueKind != JsonValueKind.Object) return (null, at + ": not an object");
        var provider = Text(element, "provider");
        if (SwiftEnum<ProviderKind>.FromRawValue(provider) is null) return (null, at + ".provider: unknown");
        IgnisBilling? billing = Text(element, "billing") switch
        {
            "perToken" => IgnisBilling.PerToken,
            "flat" => IgnisBilling.Flat,
            "none" => IgnisBilling.None,
            _ => null,
        };
        if (billing is null) return (null, at + ".billing: not perToken, flat or none");
        var (ttlMain, mainOk) = OptionalPositiveInt(element, "ttlMainSeconds");
        var (ttlSub, subOk) = OptionalPositiveInt(element, "ttlSubagentSeconds");
        if (!mainOk) return (null, at + ".ttlMainSeconds: not a positive number of seconds");
        if (!subOk) return (null, at + ".ttlSubagentSeconds: not a positive number of seconds");
        if (Bool(element, "keepWarm") is not { } keepWarm) return (null, at + ".keepWarm: missing");
        if (keepWarm && (billing != IgnisBilling.PerToken || ttlMain is null)) return (null, at + ".keepWarm: needs per-token billing and a known lifetime");
        var catalogue = element.TryGetProperty("catalogue", out var catalogueElement) && catalogueElement.ValueKind != JsonValueKind.Null
            ? Strings(element, "catalogue") : [];
        if (catalogue is null) return (null, at + ".catalogue: not a list of names");
        var notes = Text(element, "notes");
        var (sourceUrl, sourceError) = Source(element, at);
        if (sourceError is not null) return (null, sourceError);
        var retrievedAt = Text(element, "retrievedAt");
        if (!IsDay(retrievedAt)) return (null, at + ".retrievedAt: not yyyy-MM-dd");
        return (new IgnisProviderRule(provider!, billing.Value, ttlMain, ttlSub, keepWarm, new ValueList<string>(catalogue), notes, sourceUrl!, retrievedAt!), null);
    }

    private static (string? Url, string? Error) Source(JsonElement element, string at)
    {
        var sourceUrl = Text(element, "sourceUrl");
        if (sourceUrl is null || !Uri.TryCreate(sourceUrl, UriKind.Absolute, out var source) || source.Scheme != Uri.UriSchemeHttps)
            return (null, at + ".sourceUrl: not an https address");
        return (sourceUrl, null);
    }

    private static (IgnisModelPoints? Model, string? Error) Model(JsonElement element, string at, bool v2)
    {
        if (element.ValueKind != JsonValueKind.Object) return (null, at + ": not an object");
        var provider = Text(element, "provider");
        if (SwiftEnum<ProviderKind>.FromRawValue(provider) is null) return (null, at + ".provider: unknown");
        if (Strings(element, "ids") is not { Count: > 0 } ids) return (null, at + ".ids: missing");
        var aliases = element.TryGetProperty("aliases", out var aliasElement) && aliasElement.ValueKind != JsonValueKind.Null
            ? Strings(element, "aliases") : [];
        if (aliases is null) return (null, at + ".aliases: not a list of names");
        if (Text(element, "displayName") is not { Length: > 0 } displayName) return (null, at + ".displayName: missing");
        if (Int(element, "contextWindow") is not { } window || window <= 0) return (null, at + ".contextWindow: missing");

        if (!element.TryGetProperty("prices", out var pricesElement) || pricesElement.ValueKind != JsonValueKind.Object)
            return (null, at + ".prices: missing");
        var (prices, priceError) = Prices(pricesElement, at + ".prices", window, v2);
        if (prices is null) return (null, priceError);

        if (!element.TryGetProperty("cache", out var cacheElement) || cacheElement.ValueKind != JsonValueKind.Object)
            return (null, at + ".cache: missing");
        if (Bool(cacheElement, "keepWarm") is not { } keepWarm) return (null, at + ".cache.keepWarm: missing");
        var (ttlMain, mainOk) = OptionalPositiveInt(cacheElement, "ttlMainSeconds");
        var (ttlSub, subOk) = OptionalPositiveInt(cacheElement, "ttlSubagentSeconds");
        if (!mainOk) return (null, at + ".cache.ttlMainSeconds: not a positive number of seconds");
        if (!subOk) return (null, at + ".cache.ttlSubagentSeconds: not a positive number of seconds");

        long? tidyAt = null;
        if (element.TryGetProperty("tidyAt", out var tidy) && tidy.ValueKind != JsonValueKind.Null)
        {
            if (tidy.ValueKind != JsonValueKind.Number || !tidy.TryGetInt64(out var tokens) || tokens <= 0 || tokens > window)
                return (null, at + ".tidyAt: not within the context window");
            tidyAt = tokens;
        }
        var tidyReason = Text(element, "tidyReason");
        var helperModel = Text(element, "helperModel");
        var (sourceUrl, sourceError) = Source(element, at);
        if (sourceError is not null) return (null, sourceError);
        var retrievedAt = Text(element, "retrievedAt");
        if (!IsDay(retrievedAt)) return (null, at + ".retrievedAt: not yyyy-MM-dd");

        return (new IgnisModelPoints(
            provider!, new ValueList<string>(ids), new ValueList<string>(aliases), displayName, window, prices,
            new IgnisCachePoints(ttlMain, ttlSub, keepWarm), tidyAt, tidyReason, helperModel, sourceUrl!, retrievedAt!), null);
    }

    private static (IgnisPrices? Prices, string? Error) Prices(JsonElement element, string at, long window, bool v2)
    {
        if (Price(element, "input") is not { } input) return (null, at + ".input: missing");
        if (Price(element, "output") is not { } output) return (null, at + ".output: missing");
        if (Price(element, "cacheRead") is not { } cacheRead) return (null, at + ".cacheRead: missing");
        var (write5m, ok5) = OptionalPrice(element, "cacheWrite5m");
        var (write1h, ok1) = OptionalPrice(element, "cacheWrite1h");
        if (!ok5) return (null, at + ".cacheWrite5m: not a price");
        if (!ok1) return (null, at + ".cacheWrite1h: not a price");
        var (writes, writesError) = v2 ? Writes(element, at) : ([], null);
        if (writes is null) return (null, writesError);

        var steps = new List<IgnisPriceStep>();
        if (element.TryGetProperty("steps", out var stepsElement) && stepsElement.ValueKind != JsonValueKind.Null)
        {
            if (stepsElement.ValueKind != JsonValueKind.Array) return (null, at + ".steps: not a list");
            var index = 0;
            long previous = 0;
            foreach (var step in stepsElement.EnumerateArray())
            {
                var stepAt = at + ".steps[" + index.ToString(CultureInfo.InvariantCulture) + "]";
                if (step.ValueKind != JsonValueKind.Object) return (null, stepAt + ": not an object");
                if (Int(step, "aboveTokens") is not { } above || above <= previous || above >= window)
                    return (null, stepAt + ".aboveTokens: not rising within the context window");
                if (Price(step, "input") is not { } stepInput) return (null, stepAt + ".input: missing");
                if (Price(step, "output") is not { } stepOutput) return (null, stepAt + ".output: missing");
                if (Price(step, "cacheRead") is not { } stepRead) return (null, stepAt + ".cacheRead: missing");
                var (s5, s5ok) = OptionalPrice(step, "cacheWrite5m");
                var (s1, s1ok) = OptionalPrice(step, "cacheWrite1h");
                if (!s5ok || !s1ok) return (null, stepAt + ": a cache write is not a price");
                var (stepWrites, stepWritesError) = v2 ? Writes(step, stepAt) : ([], null);
                if (stepWrites is null) return (null, stepWritesError);
                steps.Add(new IgnisPriceStep(above, stepInput, stepOutput, stepRead, s5, s1) { CacheWrites = new ValueList<IgnisCacheWrite>(stepWrites) });
                previous = above;
                index++;
            }
        }
        return (new IgnisPrices(input, output, cacheRead, write5m, write1h, new ValueList<IgnisPriceStep>(steps)) { CacheWrites = new ValueList<IgnisCacheWrite>(writes) }, null);
    }

    /// <summary>The optional "cacheWrites" list: one positive lifetime each, not 5 minutes or 1 hour (those have their own fields).</summary>
    private static (List<IgnisCacheWrite>? Writes, string? Error) Writes(JsonElement element, string at)
    {
        var list = new List<IgnisCacheWrite>();
        if (!element.TryGetProperty("cacheWrites", out var writes) || writes.ValueKind == JsonValueKind.Null) return (list, null);
        if (writes.ValueKind != JsonValueKind.Array) return (null, at + ".cacheWrites: not a list");
        var index = 0;
        foreach (var write in writes.EnumerateArray())
        {
            var writeAt = at + ".cacheWrites[" + index.ToString(CultureInfo.InvariantCulture) + "]";
            if (write.ValueKind != JsonValueKind.Object) return (null, writeAt + ": not an object");
            var (ttl, ttlOk) = OptionalPositiveInt(write, "ttlSeconds");
            if (!ttlOk || ttl is not { } seconds) return (null, writeAt + ".ttlSeconds: not a positive number of seconds");
            if (seconds is 300 or 3600) return (null, writeAt + ".ttlSeconds: 5 minutes and 1 hour have their own fields");
            if (list.Any(other => other.TtlSeconds == seconds)) return (null, writeAt + ".ttlSeconds: already listed");
            if (Price(write, "price") is not { } price) return (null, writeAt + ".price: missing");
            list.Add(new IgnisCacheWrite(seconds, price));
            index++;
        }
        return (list, null);
    }

    // ---- members ----

    private static IgnisPointsParse Fail(string error) => new(null, error);

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool? Bool(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : null;

    private static long? Int(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number) ? number : null;

    private static (int? Value, bool Ok) OptionalPositiveInt(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null) return (null, true);
        return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) && number > 0 ? (number, true) : (null, false);
    }

    private static decimal? Price(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var price) && price >= 0 ? price : null;

    private static (decimal? Value, bool Ok) OptionalPrice(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null) return (null, true);
        return Price(element, name) is { } price ? (price, true) : (null, false);
    }

    private static List<string>? Strings(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array) return null;
        var list = new List<string>();
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String || item.GetString() is not { } text || text.Trim().Length == 0) return null;
            list.Add(text.Trim());
        }
        return list;
    }
}
