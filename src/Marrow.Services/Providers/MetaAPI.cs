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
    // :25-27
    public static async Task<bool> Validate(string apiKey, Uri? modelsUrl = null) =>
        // :26
        await OpenAICompatibleAPI.Validate(modelsUrl ?? ModelsURL, apiKey);

    // :29-31
    public static async Task<ValueList<ModelOption>> ListModels(string apiKey, Uri? modelsUrl = null) =>
        // :30
        await OpenAICompatibleAPI.ListModels(modelsUrl ?? ModelsURL, apiKey, Option);

    /// <summary>
    /// Probed live 2026-09-13: only `muse-spark-1.3` accepts "max". Every other checkpoint, including
    /// `muse-spark-1.3-contributor`, rejects it with a bare "The request contains invalid parameters" 400 (:33-35).
    /// </summary>
    // :36
    public static readonly ValueList<string> MaxEfforts =
        new(new[] { "minimal", "low", "medium", "high", "xhigh", "max" });

    // :37
    public static readonly ValueList<string> StandardEfforts =
        new(new[] { "minimal", "low", "medium", "high", "xhigh" });

    // :39-41
    public static ValueList<string> Efforts(string id) =>
        SwiftString.Equal(id, "muse-spark-1.3") ? MaxEfforts : StandardEfforts;

    // :43-63
    public static ModelOption? Option(string id)
    {
        var efforts = Efforts(id);
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
        // :56-60. Future Muse Spark checkpoints still show up. The endpoint also lists non-chat models
        // (muse-image-1.0, muse-voice-transcribe-1.0) that fail on chat/completions, so only Spark ids are
        // accepted.
        if (!SwiftString.HasPrefix(SwiftString.Lowercased(id), "muse-spark")) return null;
        // :61
        return new ModelOption(id: id, name: id, efforts: efforts, defaultEffort: "high");
    }

    /// <summary>All Muse Spark models are multimodal (text, image, video, audio, PDF in).</summary>
    // :66-69
    public static bool IsVisionModel(string? id)
    {
        // :67
        if (id is not { } value || value.Length == 0) return true;
        // :68
        return SwiftString.HasPrefix(SwiftString.Lowercased(value), "muse-spark");
    }
}
