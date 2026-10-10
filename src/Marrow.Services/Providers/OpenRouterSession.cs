using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;

namespace Marrow.Services.Providers;

// PORT-ONLY, no Swift twin (OD-0049, OD-0060, ED-234). Follows MetaSession.swift / ZaiSession.swift:
//
// Native OpenRouter agent: talks to https://openrouter.ai/api/v1/chat/completions directly (OpenAI-compatible Chat
// Completions with tools) and executes coding tools locally, so no CLI install is needed, just an API key in
// Settings -> Providers -> OpenRouter (or OPENROUTER_API_KEY). The model is any id OpenRouter lists, so the context
// window and the image ability are read per model from the last list (OpenRouterAPI), not fixed per provider.
public sealed class OpenRouterSession : OpenAICompatibleSession
{
    private static readonly Config config = new()
    {
        Kind = ProviderKind.OpenRouter,
        Title = "OpenRouter",
        AgentIdentity = model => $"OpenRouter coding agent ({model})",
        DefaultModel = OpenRouterAPI.AutoModelId,
        ChatURL = OpenRouterAPI.ChatURL,
        ContextWindow = OpenRouterAPI.DefaultContextWindow,
        MaxOutputTokens = null,
        // Reasoning comes back as delta.reasoning (DrainSSE reads it) but is not sent back: the model behind a request
        // is whichever one OpenRouter routes to, and not every one takes a reasoning field in its history.
        ReplaysReasoning = false,
        IsVisionModel = OpenRouterAPI.IsVisionModel,
    };

    public OpenRouterSession(SessionConfiguration configuration) : base(config, configuration)
    {
    }

    protected override int ContextWindowFor(string model) => OpenRouterAPI.ContextWindow(model);

    // Chat Completions streams the usage block only when asked for (as DeepSeek and Meta do), and OpenRouter's unified
    // reasoning object carries the effort.
    public override void ApplyProviderPayload(Dictionary<string, JSONValue> payload, string model, string? effort, bool withTools)
    {
        payload["stream_options"] = JSONValue.FromDictionaryLiteral(("include_usage", true));
        if (ReasoningEffort(effort) is { } mapped) payload["reasoning"] = JSONValue.FromDictionaryLiteral(("effort", mapped));
    }

    // The scale of OpenRouterAPI.ReasoningEfforts; the words the other providers' sliders add map to the nearest of them.
    public static string? ReasoningEffort(string? effort)
    {
        if (effort is null || effort.Length == 0) return null;
        return effort switch
        {
            "low" => "low",
            "medium" => "medium",
            "high" or "xhigh" or "extra-high" or "max" => "high",
            _ => null,
        };
    }

    // OpenRouter answers its refusals with the HTTP status and an error.message the body carries (402 for a spent balance,
    // 404 "No endpoints found ..." for a model that is gone or cannot do what was asked, 429 for a rate limit).
    public override string FriendlyError(string message, int statusCode)
    {
        var lower = SwiftString.Lowercased(message);
        if (statusCode == 401 || lower.Contains("no auth credentials", StringComparison.Ordinal) || lower.Contains("invalid api key", StringComparison.Ordinal) || lower.Contains("user not found", StringComparison.Ordinal))
        {
            return "OpenRouter rejected the API key. Check it in Settings \U00002192 Providers \U00002192 OpenRouter (keys are made at openrouter.ai/keys).";
        }
        if (statusCode == 402 || lower.Contains("insufficient credits", StringComparison.Ordinal) || lower.Contains("more credits", StringComparison.Ordinal))
        {
            return "OpenRouter has no credits left for this request. Add credits at openrouter.ai/credits, or pick a free model, then try again.";
        }
        if (lower.Contains("support tool use", StringComparison.Ordinal))
        {
            return "That OpenRouter model cannot use tools, which nth needs to work on files. Pick another model in the model picker.";
        }
        if (statusCode == 404 || lower.Contains("no endpoints found", StringComparison.Ordinal) || lower.Contains("model not found", StringComparison.Ordinal) || lower.Contains("not a valid model", StringComparison.Ordinal))
        {
            return "OpenRouter has no provider serving that model right now. Pick another model in the model picker.";
        }
        if (statusCode == 429 || lower.Contains("rate limit", StringComparison.Ordinal) || lower.Contains("rate-limited", StringComparison.Ordinal))
        {
            return "OpenRouter is rate-limited right now. Wait a moment and try again.";
        }
        if (lower.Contains("context", StringComparison.Ordinal) && (lower.Contains("length", StringComparison.Ordinal) || lower.Contains("window", StringComparison.Ordinal) || lower.Contains("token", StringComparison.Ordinal)))
        {
            return "The conversation no longer fits this model's context window. Start a new thread or compact first.";
        }
        if (statusCode == 408 || statusCode == 504 || lower.Contains("timed out", StringComparison.Ordinal))
        {
            return "OpenRouter timed out before finishing. Try again.";
        }
        return message.Length == 0 ? "OpenRouter stopped before finishing." : message;
    }
}
