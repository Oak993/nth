// PORT-ONLY: Ignis (OD-0134, ED-NEW-ignis-engine; work/ignis/PLAN.md unit U1). Reads and validates a model points file
// (schema v1, IgnisPointsSchema.cs) and finds a model in it. A file is taken whole or not at all: any invalid model
// rejects the file with the first error found, so a half-broken feed never replaces good points. Unknown members are
// ignored (a newer writer may add fields); a different schemaVersion is rejected.
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

    // ---- reading ----

    private static IgnisPointsParse Read(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object) return Fail("the file is not an object");
        if (Int(root, "schemaVersion") is not { } schema) return Fail("schemaVersion: missing");
        if (schema != IgnisPointsFile.CurrentSchemaVersion) return Fail("schemaVersion: " + schema.ToString(CultureInfo.InvariantCulture) + " is not supported");
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
            var (model, error) = Model(element, at);
            if (model is null) return Fail(error!);
            foreach (var name in model.Ids.Concat(model.Aliases))
                if (!seen.Add(model.Provider + "\u0000" + name)) return Fail(at + ": \"" + name + "\" is already used by another " + model.Provider + " model");
            list.Add(model);
            index++;
        }
        return new IgnisPointsParse(new IgnisPointsFile(schema is var s ? (int)s : 0, version!, new ValueList<IgnisModelPoints>(list)), null);
    }

    private static (IgnisModelPoints? Model, string? Error) Model(JsonElement element, string at)
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
        var (prices, priceError) = Prices(pricesElement, at + ".prices", window);
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
        var sourceUrl = Text(element, "sourceUrl");
        if (sourceUrl is null || !Uri.TryCreate(sourceUrl, UriKind.Absolute, out var source) || source.Scheme != Uri.UriSchemeHttps)
            return (null, at + ".sourceUrl: not an https address");
        var retrievedAt = Text(element, "retrievedAt");
        if (!IsDay(retrievedAt)) return (null, at + ".retrievedAt: not yyyy-MM-dd");

        return (new IgnisModelPoints(
            provider!, new ValueList<string>(ids), new ValueList<string>(aliases), displayName, window, prices,
            new IgnisCachePoints(ttlMain, ttlSub, keepWarm), tidyAt, tidyReason, helperModel, sourceUrl, retrievedAt!), null);
    }

    private static (IgnisPrices? Prices, string? Error) Prices(JsonElement element, string at, long window)
    {
        if (Price(element, "input") is not { } input) return (null, at + ".input: missing");
        if (Price(element, "output") is not { } output) return (null, at + ".output: missing");
        if (Price(element, "cacheRead") is not { } cacheRead) return (null, at + ".cacheRead: missing");
        var (write5m, ok5) = OptionalPrice(element, "cacheWrite5m");
        var (write1h, ok1) = OptionalPrice(element, "cacheWrite1h");
        if (!ok5) return (null, at + ".cacheWrite5m: not a price");
        if (!ok1) return (null, at + ".cacheWrite1h: not a price");

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
                steps.Add(new IgnisPriceStep(above, stepInput, stepOutput, stepRead, s5, s1));
                previous = above;
                index++;
            }
        }
        return (new IgnisPrices(input, output, cacheRead, write5m, write1h, new ValueList<IgnisPriceStep>(steps)), null);
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
