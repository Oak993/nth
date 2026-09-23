using System.Runtime.CompilerServices;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;

[assembly: InternalsVisibleTo("Marrow.Services.Tests")]

namespace Marrow.Services.Providers;

// Source: DroppyCode/Services/Providers/MetaAPI.swift (B2 1fbf81e), :1-90.
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
    // :16
    public static readonly Uri BaseURL = new("https://api.meta.ai/v1");

    // :17
    public static readonly Uri ChatURL = new("https://api.meta.ai/v1/chat/completions");

    // :18
    public static readonly Uri ModelsURL = new("https://api.meta.ai/v1/models");

    /// <summary>Muse Spark context window per https://dev.meta.ai/docs/models (:21).</summary>
    public const int ContextWindow = 1_048_576;

    // :22
    public const int MaxOutputTokens = 131_072;

    /// <summary>True when the key lists models successfully.</summary>
    // :25-34
    public static async Task<bool> Validate(string apiKey, Uri? modelsUrl = null)
    {
        // :26
        var key = apiKey.Trimmed();
        // :27
        if (key.Length == 0) return false;
        // :28
        var request = new URLRequest(modelsUrl ?? ModelsURL, timeoutInterval: 15);
        // :29
        request = request.SetValue("Bearer " + key, forHTTPHeaderField: "Authorization");
        // :30
        request = request.SetValue("application/json", forHTTPHeaderField: "Accept");
        // :31-32
        var result = await SwiftTry.OptionalValueAsync(() => URLSession.Shared.Data(request));
        if (result is not { } exchange || exchange.Response is not HTTPURLResponse http) return false;
        // :33
        return http.StatusCode == 200;
    }

    // :36-50
    public static async Task<ValueList<ModelOption>> ListModels(string apiKey, Uri? modelsUrl = null)
    {
        // :37
        var key = apiKey.Trimmed();
        // :38
        if (key.Length == 0) return ValueList<ModelOption>.Empty;
        // :39
        var request = new URLRequest(modelsUrl ?? ModelsURL, timeoutInterval: 15);
        // :40
        request = request.SetValue("Bearer " + key, forHTTPHeaderField: "Authorization");
        // :41
        request = request.SetValue("application/json", forHTTPHeaderField: "Accept");
        // :42
        var (data, response) = await URLSession.Shared.Data(request);
        // :43-44
        if (response is not HTTPURLResponse http || http.StatusCode != 200) return ValueList<ModelOption>.Empty;
        var json = JSONValue.Parse(data);
        if (json is null) return ValueList<ModelOption>.Empty;
        // :45
        var ids = new List<string>();
        foreach (var item in json["data"]?.JSONValueArray ?? [])
        {
            if (item[SwiftJsonIdKey]?.JSONValueString is { } id) ids.Add(id);
        }
        // :46
        if (ids.Count == 0) return ValueList<ModelOption>.Empty;
        // :47-49
        var options = new List<ModelOption>();
        foreach (var id in ids)
        {
            if (Option(id) is { } option) options.Add(option);
        }
        return new ValueList<ModelOption>(options
            .OrderBy(o => o.IsDefault ? 0 : 1)
            .ThenBy(o => o.Id, SwiftStringComparer.Instance));
    }

    // PORT-ONLY: the literal JSON key "id" used at :45; kept as a constant only to avoid repeating the string
    // literal, not a behaviour of its own.
    private const string SwiftJsonIdKey = "id";

    /// <summary>
    /// Probed live 2026-09-13: only `muse-spark-1.3` accepts "max". Every other checkpoint, including
    /// `muse-spark-1.3-contributor`, rejects it with a bare "The request contains invalid parameters" 400 (:52-54).
    /// </summary>
    // :55
    public static readonly ValueList<string> MaxEfforts =
        new(new[] { "minimal", "low", "medium", "high", "xhigh", "max" });

    // :56
    public static readonly ValueList<string> StandardEfforts =
        new(new[] { "minimal", "low", "medium", "high", "xhigh" });

    // :58-60
    public static ValueList<string> Efforts(string id) =>
        SwiftString.Equal(id, "muse-spark-1.3") ? MaxEfforts : StandardEfforts;

    // :62-82
    public static ModelOption? Option(string id)
    {
        var efforts = Efforts(id);
        // :64
        if (SwiftString.Equal(id, "muse-spark-1.3"))
        {
            // :65-66
            return new ModelOption(id: id, name: "Spark 1.3",
                detail: "Latest Muse Spark · best agentic coding · 1M context", // U+00B7 MIDDLE DOT
                efforts: efforts, defaultEffort: "high", isDefault: true);
        }
        if (SwiftString.Equal(id, "muse-spark-1.3-contributor"))
        {
            // :67-68
            return new ModelOption(id: id, name: "Spark 1.3 Contributor",
                detail: "Cheaper 1.3 · Meta may train on your chats · no max effort", // U+00B7 MIDDLE DOT
                efforts: efforts, defaultEffort: "high");
        }
        if (SwiftString.Equal(id, "muse-spark-1.2"))
        {
            // :69-70
            return new ModelOption(id: id, name: "Spark 1.2",
                detail: "Previous checkpoint · Standard tier · 1M context", // U+00B7 MIDDLE DOT
                efforts: efforts, defaultEffort: "high");
        }
        if (SwiftString.Equal(id, "muse-spark-1.2-contributor"))
        {
            // :71-72
            return new ModelOption(id: id, name: "Spark 1.2 Contributor",
                detail: "Cheaper 1.2 · Meta may train on your chats", // U+00B7 MIDDLE DOT
                efforts: efforts, defaultEffort: "high");
        }
        if (SwiftString.Equal(id, "muse-spark-1.1"))
        {
            // :73-74
            return new ModelOption(id: id, name: "Spark 1.1",
                detail: "Original checkpoint · Standard tier · 1M context", // U+00B7 MIDDLE DOT
                efforts: efforts, defaultEffort: "high");
        }
        // :75-80. Future Muse Spark checkpoints still show up. The endpoint also lists non-chat models
        // (muse-image-1.0, muse-voice-transcribe-1.0) that fail on chat/completions, so only Spark ids are
        // accepted.
        if (!SwiftString.HasPrefix(SwiftString.Lowercased(id), "muse-spark")) return null;
        // :81
        return new ModelOption(id: id, name: id, efforts: efforts, defaultEffort: "high");
    }

    /// <summary>All Muse Spark models are multimodal (text, image, video, audio, PDF in).</summary>
    // :85-88
    public static bool IsVisionModel(string? id)
    {
        // :86
        if (id is not { } value || value.Length == 0) return true;
        // :87
        return SwiftString.HasPrefix(SwiftString.Lowercased(value), "muse-spark");
    }
}
