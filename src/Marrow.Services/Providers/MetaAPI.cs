using System.Runtime.CompilerServices;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;

[assembly: InternalsVisibleTo("Marrow.Services.Tests")]

namespace Marrow.Services.Providers;

// Source: DroppyCode/Services/Providers/MetaAPI.swift (B3 52bc7926), :1-70.
//
// Minimal OpenAI-compatible HTTP client for api.meta.ai. Chat streaming itself lives in MetaSession (not part of
// this unit); this covers the lightweight calls (key validation, model catalog) the registry needs.
//
// PORT-ONLY: Validate and ListModels take an optional trailing `modelsUrl` parameter, defaulting to null. When
// null (every production call site) the real ModelsURL is used, exactly as Swift's hardcoded `modelsURL`; tests
// pass a loopback URL so the HTTP path (R-NET-1) is exercised without reaching the internet (R-TEST-10). No
// production behaviour differs: the parameter is never supplied outside tests/Marrow.Services.Tests, which the
// assembly-level InternalsVisibleTo above grants access to.
public static class MetaAPI
{
    // PORT-ONLY R-TEST-10: scoped loopback endpoint boundary for production registry proofs (the same seam
    // DeepSeekAPI.Endpoint and ZaiAPI.Endpoint already carry). Identity by default; requests, headers, response
    // parsing and timing remain the pinned Swift path.
    internal static Ambient<Func<Uri, Uri>> Endpoint { get; } = new(() => url => url);

    // :16
    public static readonly Uri BaseURL = new("https://api.meta.ai/v1");

    // :17
    public static readonly Uri ChatURL = new("https://api.meta.ai/v1/chat/completions");

    // :18
    public static readonly Uri ModelsURL = new("https://api.meta.ai/v1/models");

    /// <summary>Muse Wonder and Muse Spark context window per https://dev.meta.ai/docs/models, read 2026-10-13.</summary>
    public const int ContextWindow = 1_048_576;

    // :22
    public const int MaxOutputTokens = 131_072;

    /// <summary>True when the key lists models successfully.</summary>
    // :25-27
    public static async Task<bool> Validate(string apiKey, Uri? modelsUrl = null) =>
        // :26
        await OpenAICompatibleAPI.Validate(modelsUrl ?? Endpoint.Current(ModelsURL), apiKey);

    // :29-31
    // ED-NEW-muse-wonder-1/2: the API's chat models, with documented unavailable variants excluded.
    public static async Task<ValueList<ModelOption>> ListModels(string apiKey, Uri? modelsUrl = null)
    {
        var inputs = new Dictionary<string, bool>(StringComparer.Ordinal);
        var rows = await OpenAICompatibleAPI.ListModels(modelsUrl ?? Endpoint.Current(ModelsURL), apiKey, (id, item) =>
        {
            if (UnavailableOnAPI(id) || !Port.ChatModelCatalog.CanChat(id, item)) return null;
            if (inputs.Count < 4096 && Port.ChatModelCatalog.TakesImages(item) is { } vision)
                inputs[id] = vision;
            return Option(id) ?? new ModelOption(id, id);
        });
        // Model capabilities are bound to exact model ids. Replace the bounded snapshot on each successful list,
        // including an empty list; missing metadata retains only documented known-model facts.
        visionInputs = inputs;
        var wonder = rows.Any(row => row.Id == "muse-wonder-1.0");
        return new ValueList<ModelOption>(rows.Select(row => wonder ? row with { IsDefault = row.Id == "muse-wonder-1.0" } : row)
            .OrderBy(row => row.IsDefault ? 0 : row.Id == "muse-spark-1.3" ? 1 : 2)
            .ThenBy(row => row.Id, SwiftStringComparer.Instance));
    }

    private static volatile IReadOnlyDictionary<string, bool> visionInputs = new Dictionary<string, bool>(StringComparer.Ordinal);

    private static bool UnavailableOnAPI(string id)
    {
        var lower = SwiftString.Lowercased(id);
        return SwiftString.HasPrefix(lower, "muse-wonder-full")
            || SwiftString.HasPrefix(lower, "muse-glimmer")
            || SwiftString.HasPrefix(lower, "muse-wonder-") && SwiftString.HasSuffix(lower, "-contributor");
    }

    /// <summary>
    /// Official reasoning docs, read 2026-10-13: Wonder 1.0 and Spark 1.3 support "max".
    /// The other documented Spark ids retain their five-level scale; unknown scales are not inferred.
    /// </summary>
    // :36
    public static readonly ValueList<string> MaxEfforts =
        new(new[] { "minimal", "low", "medium", "high", "xhigh", "max" });

    // :37
    public static readonly ValueList<string> StandardEfforts =
        new(new[] { "minimal", "low", "medium", "high", "xhigh" });

    // :39-41
    public static ValueList<string> Efforts(string id) =>
        id is "muse-wonder-1.0" or "muse-spark-1.3" ? MaxEfforts
            : id is "muse-spark-1.3-contributor" or "muse-spark-1.2" or "muse-spark-1.2-contributor" or "muse-spark-1.1"
                ? StandardEfforts : ValueList<string>.Empty;

    // :43-63
    public static ModelOption? Option(string id)
    {
        if (UnavailableOnAPI(id)) return null;
        var efforts = Efforts(id);
        // ED-NEW-muse-wonder-1: https://dev.meta.ai/docs/models, read 2026-10-13.
        if (id == "muse-wonder-1.0")
            return new ModelOption(id, "Wonder 1.0",
                detail: "Most capable Muse model \U000000B7 complex coding and agents \U000000B7 Standard tier \U000000B7 1M context",
                efforts: efforts, defaultEffort: "high", isDefault: true);
        if (SwiftString.HasPrefix(SwiftString.Lowercased(id), "muse-wonder-"))
            return new ModelOption(id, id);
        // :45
        if (SwiftString.Equal(id, "muse-spark-1.3"))
        {
            // :46-47
            return new ModelOption(id: id, name: "Spark 1.3",
                detail: "Latest Muse Spark · best agentic coding · 1M context", // U+00B7 MIDDLE DOT
                efforts: efforts, defaultEffort: "high", isDefault: true);
        }
        if (SwiftString.Equal(id, "muse-spark-1.3-contributor"))
        {
            // :48-49
            return new ModelOption(id: id, name: "Spark 1.3 Contributor",
                detail: "Cheaper 1.3 · Meta may train on your chats · no max effort", // U+00B7 MIDDLE DOT
                efforts: efforts, defaultEffort: "high");
        }
        if (SwiftString.Equal(id, "muse-spark-1.2"))
        {
            // :50-51
            return new ModelOption(id: id, name: "Spark 1.2",
                detail: "Previous checkpoint · Standard tier · 1M context", // U+00B7 MIDDLE DOT
                efforts: efforts, defaultEffort: "high");
        }
        if (SwiftString.Equal(id, "muse-spark-1.2-contributor"))
        {
            // :52-53
            return new ModelOption(id: id, name: "Spark 1.2 Contributor",
                detail: "Cheaper 1.2 · Meta may train on your chats", // U+00B7 MIDDLE DOT
                efforts: efforts, defaultEffort: "high");
        }
        if (SwiftString.Equal(id, "muse-spark-1.1"))
        {
            // :54-55
            return new ModelOption(id: id, name: "Spark 1.1",
                detail: "Original checkpoint · Standard tier · 1M context", // U+00B7 MIDDLE DOT
                efforts: efforts, defaultEffort: "high");
        }
        // :56-60. This formats the source's Spark family. ListModels handles every other eligible chat id
        // independently, with its raw name and no guessed reasoning scale (ED-NEW-muse-wonder-2).
        if (!SwiftString.HasPrefix(SwiftString.Lowercased(id), "muse-spark")) return null;
        // :61
        return new ModelOption(id: id, name: id);
    }

    /// <summary>Documented Wonder and Spark input: text, image, video, audio, PDF; unknown models use list metadata.</summary>
    // :66-69
    public static bool IsVisionModel(string? id)
    {
        // :67
        if (id is not { } value || value.Length == 0) return true;
        // :68
        if (UnavailableOnAPI(value)) return false;
        if (visionInputs.TryGetValue(value, out var vision)) return vision;
        return SwiftString.HasPrefix(SwiftString.Lowercased(value), "muse-spark")
            || value == "muse-wonder-1.0";
    }
}
