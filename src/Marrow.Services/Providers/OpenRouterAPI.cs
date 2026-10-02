using System.Globalization;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;

namespace Marrow.Services.Providers;

// PORT-ONLY, no Swift twin: OD-0049 and OD-0060 add OpenRouter as an API-key provider, "in the manner of the source's
// OpenAI-compatible providers (DeepSeek, Meta, Z.ai: OpenAICompatibleAPI/Session)", ED-234. The shape follows
// DeepSeekAPI.swift / MetaAPI.swift / ZaiAPI.swift: endpoint constants, Validate, ListModels, Option, IsVisionModel,
// the bearer request from OpenAICompatibleAPI.BearerRequest, and a model list the registry caches the way it caches
// every API provider's list (ProviderRegistry.LoadAPICatalog, :567-591).
//
// What differs from those three, and why:
//  - The models endpoint answers 200 without a key, so a models call cannot prove a key; Validate reads GET /key, which
//    answers 401 for a key OpenRouter does not know. The endpoint is OpenRouter's documented key endpoint
//    (https://openrouter.ai/docs/api-reference/limits, read 2026-10-02: GET https://openrouter.ai/api/v1/key); never
//    called from here (nothing in this port's tests may reach the network), so a first real key confirms the 401.
//  - OpenRouter lists several hundred models, each with its own context length, price and abilities, so the list is
//    parsed from the whole item (OpenAICompatibleAPI.ListModels only looks at ids) and the per-model context length and
//    image ability the session needs are kept beside the persisted ModelOption rows (ModelOption has no field for them).
//  - There is no URL boundary like DeepSeekAPI.Endpoint: the tests answer https://openrouter.ai itself through the HTTP
//    client's test seam (URLSession.Transport, OD-0057).
public static class OpenRouterAPI
{
    // https://openrouter.ai/api/v1 is OpenAI-compatible Chat Completions with tools.
    public static readonly Uri BaseURL = new("https://openrouter.ai/api/v1");
    public static readonly Uri ChatURL = new("https://openrouter.ai/api/v1/chat/completions");
    public static readonly Uri ModelsURL = new("https://openrouter.ai/api/v1/models");
    public static readonly Uri KeyURL = new("https://openrouter.ai/api/v1/key");

    /// <summary>OpenRouter's own router: it picks a model per request. Listed by the models endpoint and the seed row.</summary>
    public const string AutoModelId = "openrouter/auto";

    /// <summary>The window the usage ring assumes for a model whose length is not known yet (the list has not loaded).</summary>
    public const int DefaultContextWindow = 128_000;

    /// <summary>The scale a reasoning model offers; OpenRouter's unified <c>reasoning.effort</c> takes these words.</summary>
    public static readonly ValueList<string> ReasoningEfforts = new(["low", "medium", "high"]);

    private const string MiddleDot = "\U000000B7";

    /// <summary>One entry of the models endpoint, as far as the port reads it.</summary>
    public sealed record ModelEntry
    {
        public required string Id { get; init; }
        public required string Name { get; init; }
        public int? ContextLength { get; init; }

        /// <summary>USD per million prompt tokens (the endpoint prices a single token); null when absent or negative.</summary>
        public double? PromptPerMillion { get; init; }

        public double? CompletionPerMillion { get; init; }
        public bool SupportsReasoning { get; init; }
        public bool SupportsImages { get; init; }
    }

    /// <summary>What the session needs of a model the list has described.</summary>
    private sealed record ModelFacts(int? ContextLength, bool SupportsImages);

    // The facts of the last list fetched, replaced as a whole when a fetch delivers rows.
    private static volatile IReadOnlyDictionary<string, ModelFacts> facts = new Dictionary<string, ModelFacts>(StringComparer.Ordinal);

    /// <summary>
    /// True when OpenRouter accepts the key: GET /key answers 200 (an unknown key is 401). An empty key is false with no
    /// request, a failed request is false.
    /// </summary>
    public static async Task<bool> Validate(string apiKey)
    {
        var key = apiKey.Trimmed();
        if (key.Length == 0) return false;
        var request = OpenAICompatibleAPI.BearerRequest(KeyURL, key);
        var result = await SwiftTry.OptionalValueAsync(() => URLSession.Shared.Data(request));
        if (result is not { } exchange || exchange.Response is not HTTPURLResponse http) return false;
        return http.StatusCode == 200;
    }

    /// <summary>
    /// GET /models with the key. Empty for an empty key, a non-200 answer or a body without rows; a transport failure
    /// throws (the registry swallows it as the source does, LoadAPICatalog :582-586). Rows without an id are skipped, the
    /// default row comes first and the rest follow in id order, as OpenAICompatibleAPI.ListModels orders them.
    /// </summary>
    public static async Task<ValueList<ModelOption>> ListModels(string apiKey)
    {
        var key = apiKey.Trimmed();
        if (key.Length == 0) return ValueList<ModelOption>.Empty;
        var request = OpenAICompatibleAPI.BearerRequest(ModelsURL, key);
        var (data, response) = await URLSession.Shared.Data(request);
        if (response is not HTTPURLResponse http || http.StatusCode != 200 || JSONValue.Parse(data) is not { } json)
            return ValueList<ModelOption>.Empty;
        var entries = new List<ModelEntry>();
        foreach (var item in json["data"]?.JSONValueArray ?? [])
        {
            if (Parse(item) is { } entry) entries.Add(entry);
        }
        if (entries.Count == 0) return ValueList<ModelOption>.Empty;
        facts = entries
            .GroupBy(entry => entry.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => new ModelFacts(group.First().ContextLength, group.First().SupportsImages), StringComparer.Ordinal);
        return new ValueList<ModelOption>(entries
            .Select(Option)
            .OrderBy(option => option.IsDefault ? 0 : 1)
            .ThenBy(option => option.Id, SwiftStringComparer.Instance));
    }

    /// <summary>One item of the endpoint's <c>data</c> array; null without a string id.</summary>
    public static ModelEntry? Parse(JSONValue item)
    {
        if (item["id"]?.JSONValueString is not { Length: > 0 } id) return null;
        var parameters = item["supported_parameters"]?.JSONValueArray?.Select(value => value.JSONValueString).ToList() ?? [];
        var modalities = item["architecture"]?["input_modalities"]?.JSONValueArray?.Select(value => value.JSONValueString).ToList() ?? [];
        return new ModelEntry
        {
            Id = id,
            Name = item["name"]?.JSONValueString is { Length: > 0 } name ? name : id,
            ContextLength = item["context_length"]?.JSONValueInt is { } length and > 0 and <= int.MaxValue ? (int)length : null,
            PromptPerMillion = PerMillion(item["pricing"]?["prompt"]),
            CompletionPerMillion = PerMillion(item["pricing"]?["completion"]),
            SupportsReasoning = parameters.Contains("reasoning") || parameters.Contains("include_reasoning"),
            SupportsImages = modalities.Contains("image"),
        };
    }

    // "0.000003" (a string, USD per token) -> 3 (USD per million). "-1" is OpenRouter's "priced by what it routes to".
    private static double? PerMillion(JSONValue? price)
    {
        var perToken = price?.JSONValueString is { } text ? SwiftNumber.ParseDouble(text) : price?.JSONValueDouble;
        return perToken is { } value and >= 0 && double.IsFinite(value) ? value * 1_000_000 : null;
    }

    /// <summary>
    /// The picker row for an entry: the name without its "Vendor: " lead (the id says the vendor, and the composer's chip
    /// name would cut a name at that colon), a detail of vendor, context and price, and the reasoning scale when the model
    /// reasons. The auto router is the default row.
    /// </summary>
    public static ModelOption Option(ModelEntry entry)
    {
        var parts = new List<string>();
        var slash = entry.Id.IndexOf('/', StringComparison.Ordinal);
        if (slash > 0) parts.Add(entry.Id[..slash]);
        if (entry.ContextLength is { } length) parts.Add(Context(length) + " context");
        if (Price(entry) is { } price) parts.Add(price);
        return new ModelOption(
            entry.Id,
            DisplayName(entry),
            detail: parts.Count == 0 ? null : string.Join(" " + MiddleDot + " ", parts),
            efforts: entry.SupportsReasoning ? ReasoningEfforts : ValueList<string>.Empty,
            defaultEffort: entry.SupportsReasoning ? "medium" : null,
            isDefault: SwiftString.Equal(entry.Id, AutoModelId));
    }

    /// <summary>The offline row (the source's seeds, :127-130): only the router, so the provider is usable before any list loads.</summary>
    public static ModelOption SeedOption => Option(new ModelEntry { Id = AutoModelId, Name = "Auto Router", ContextLength = 2_000_000 });

    private static string DisplayName(ModelEntry entry)
    {
        var colon = entry.Name.IndexOf(": ", StringComparison.Ordinal);
        return colon > 0 && colon + 2 < entry.Name.Length ? entry.Name[(colon + 2)..] : entry.Name;
    }

    // 1_000_000 -> "1M", 1_048_576 -> "1M", 200_000 -> "200K", 8_192 -> "8K", 512 -> "512".
    private static string Context(int tokens)
    {
        if (tokens >= 1_000_000) return Number(Math.Round(tokens / 1_000_000.0, 1)) + "M";
        if (tokens >= 1_000) return Number(Math.Round(tokens / 1_000.0)) + "K";
        return tokens.ToString(CultureInfo.InvariantCulture);
    }

    private static string? Price(ModelEntry entry)
    {
        if (entry.PromptPerMillion is not { } prompt || entry.CompletionPerMillion is not { } completion) return null;
        if (prompt == 0 && completion == 0) return "free";
        return "$" + Number(Math.Round(prompt, 3)) + " / $" + Number(Math.Round(completion, 3)) + " per 1M";
    }

    private static string Number(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>The model's context window from the last list fetched, or <see cref="DefaultContextWindow"/> when it is not known.</summary>
    public static int ContextWindow(string? model) =>
        model is not null && facts.TryGetValue(model, out var known) && known.ContextLength is { } length ? length : DefaultContextWindow;

    /// <summary>True when the last list fetched says the model takes images; false when it says no or has not been fetched.</summary>
    public static bool IsVisionModel(string? id) =>
        id is not null && facts.TryGetValue(id, out var known) && known.SupportsImages;

    // Test support: forgets the last list, so a test starts from the state before any fetch.
    internal static void ForgetModelsForTests() => facts = new Dictionary<string, ModelFacts>(StringComparer.Ordinal);
}
