using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;

namespace Marrow.Services.Providers;

// Source: DroppyCode/Services/Providers/MetaSession.swift (B2 1fbf81e), :1-77.
//
// Native Meta agent (Muse Wonder and Muse Spark): talks to https://api.meta.ai/v1/chat/completions directly
// (OpenAI-compatible Chat Completions) and executes coding tools locally, so no CLI install is
// needed, just a MODEL_API_KEY in Settings -> Providers -> Meta.
//
// API reference: https://dev.meta.ai/docs/api-reference/
// Auth is `Authorization: Bearer $MODEL_API_KEY`. Wonder 1.0 is the flagship default; Spark
// and future endpoint-listed chat models stay available. Wonder has a 1,048,576-token context
// window. Chat Completions does not replay reasoning across turns (that needs the Responses
// API's encrypted reasoning items); each turn still reasons fresh, which is fine for the local
// tool loop here.
public sealed class MetaSession : OpenAICompatibleSession
{
    // :15-25
    private static readonly Config config = new()
    {
        Kind = ProviderKind.Meta,
        Title = "Meta",
        AgentIdentity = model => model.StartsWith("muse-wonder-", StringComparison.Ordinal)
            ? $"Meta coding agent (Muse Wonder {model})"
            : model.StartsWith("muse-spark", StringComparison.Ordinal) ? $"Meta coding agent (Muse Spark {model})" : $"Meta coding agent ({model})",
        DefaultModel = "muse-wonder-1.0", // ED-NEW-muse-wonder-1: owner flagship default, 2026-10-13
        ChatURL = MetaAPI.ChatURL, // :20
        ContextWindow = MetaAPI.ContextWindow, // :21
        MaxOutputTokens = null, // :22
        ReplaysReasoning = false, // :23
        IsVisionModel = MetaAPI.IsVisionModel, // :24
    };

    // :27-29
    public MetaSession(SessionConfiguration configuration) : base(config, configuration)
    {
    }

    // :31-36. Meta Chat Completions takes top-level reasoning_effort, mapped per model, and
    // streams the usage block only when asked for.
    public override void ApplyProviderPayload(Dictionary<string, JSONValue> payload, string model, string? effort, bool withTools)
    {
        payload["stream_options"] = JSONValue.FromDictionaryLiteral(("include_usage", true)); // :34
        if (ReasoningEffort(effort, model) is { } mapped) payload["reasoning_effort"] = mapped; // :35
    }

    // :38-49. Swift exposes only the static mapping (no instance override); ApplyProviderPayload
    // above calls it directly, so the base class's instance-virtual ReasoningEffort (port-only
    // scaffolding OpenAICompatibleSession itself never invokes once ApplyProviderPayload is
    // overridden) is left at its base default.
    public static new string? ReasoningEffort(string? effort, string? model = null)
    {
        if (effort is null || effort.Length == 0) return null; // :39
        if (MetaAPI.Efforts(model ?? "muse-wonder-1.0").Count == 0) return null; // ED-NEW-muse-wonder-2: unknown scale is omitted
        switch (effort)
        {
            case "minimal":
            case "low":
            case "medium":
            case "high":
            case "xhigh":
                return effort; // :41
            case "max":
                // Only muse-spark-1.3 itself accepts "max"; a thread saved at max that moves to
                // any other model must not send it. :42-45
                return MetaAPI.Efforts(model ?? "muse-wonder-1.0").Contains("max") ? "max" : "xhigh";
            case "extra-high":
                return "xhigh"; // :46
            default:
                return null; // :47
        }
    }

    // :51-75
    public override string FriendlyError(string message, int statusCode)
    {
        var lower = SwiftString.Lowercased(message); // :52
        if (statusCode == 401 || (lower.Contains("invalid", StringComparison.Ordinal) && lower.Contains("key", StringComparison.Ordinal)) || lower.Contains("invalid_api_key", StringComparison.Ordinal) || lower.Contains("authentication_error", StringComparison.Ordinal)) // :53
        {
            return "Meta rejected the API key. Check it in Settings \U00002192 Providers \U00002192 Meta (MODEL_API_KEY from dev.meta.ai)."; // :54 U+2192 RIGHTWARDS ARROW
        }
        if (statusCode == 404 || lower.Contains("model_not_found", StringComparison.Ordinal) || (lower.Contains("model", StringComparison.Ordinal) && lower.Contains("not exist", StringComparison.Ordinal))) // :56
        {
            return "Meta no longer offers that model. Pick Muse Wonder 1.0 in the model picker."; // :57
        }
        if (statusCode == 429 || lower.Contains("rate limit", StringComparison.Ordinal) || lower.Contains("rate_limit_exceeded", StringComparison.Ordinal)) // :59
        {
            return "Meta is rate-limited right now. Wait a moment and try again."; // :60
        }
        if (statusCode == 504 || lower.Contains("gateway_timeout", StringComparison.Ordinal)) // :62
        {
            return "Meta timed out before finishing. Try again; long generations already stream."; // :63
        }
        if (lower.Contains("reasoning_effort", StringComparison.Ordinal) && lower.Contains("none", StringComparison.Ordinal)) // :65
        {
            return "Muse Wonder and Muse Spark always reason; \"none\" is rejected. Pick Minimal or higher in the model picker."; // :66
        }
        if (lower.Contains("balance", StringComparison.Ordinal) || lower.Contains("insufficient", StringComparison.Ordinal) || lower.Contains("quota", StringComparison.Ordinal) || lower.Contains("billing", StringComparison.Ordinal)) // :68
        {
            return "Meta reported a billing/quota problem. Check your Model API balance at dev.meta.ai, then try again."; // :69
        }
        if (lower.Contains("context", StringComparison.Ordinal) && (lower.Contains("length", StringComparison.Ordinal) || lower.Contains("window", StringComparison.Ordinal) || lower.Contains("token", StringComparison.Ordinal))) // :71
        {
            return "The conversation no longer fits Meta's 1M-token context window. Start a new thread or compact first."; // :72
        }
        return message.Length == 0 ? "Meta stopped before finishing." : message; // :74
    }
}
