using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;

namespace Marrow.Services.Providers;

// Source: DroppyCode/Services/Providers/DeepSeekSession.swift (B2 1fbf81e), :1-58.
//
// Native DeepSeek agent: talks to https://api.deepseek.com/chat/completions directly (OpenAI-compatible) and
// executes coding tools locally, so no CLI install is needed, just an API key in Settings -> Providers -> DeepSeek.
// (:3-5)
// :6-7. @MainActor final class -> sealed class used only on main (R-ISO-1).
public sealed class DeepSeekSession : OpenAICompatibleSession
{
    // :8-18
    private static readonly Config config = new()
    {
        Kind = ProviderKind.Deepseek,
        Title = "DeepSeek",
        AgentIdentity = _ => "DeepSeek coding agent", // :11
        DefaultModel = "deepseek-v4-pro", // :12
        ChatURL = DeepSeekAPI.ChatURL, // :13
        ContextWindow = DeepSeekAPI.ContextWindow, // :14
        MaxOutputTokens = null, // :15
        ReplaysReasoning = true, // :16
        IsVisionModel = DeepSeekAPI.IsVisionModel, // :17
    };

    // :20-22
    public DeepSeekSession(SessionConfiguration configuration) : base(config, configuration)
    {
    }

    // :24-31. Thinking mode needs the effort mapping on top of the shared payload, and the usage block only streams
    // when asked for: without it the ledger and the context window would never hear from DeepSeek.
    public override void ApplyProviderPayload(Dictionary<string, JSONValue> payload, string model, string? effort, bool withTools)
    {
        payload["stream_options"] = JSONValue.FromDictionaryLiteral(("include_usage", true)); // :28
        payload["thinking"] = JSONValue.FromDictionaryLiteral(("type", "enabled")); // :29
        if (ReasoningEffort(effort) is { } mapped) payload["reasoning_effort"] = new JSONValue.String(mapped); // :30
    }

    // :33-40. Swift exposes only this static mapping (no instance override); ApplyProviderPayload above calls it
    // directly, so the base class's instance-virtual ReasoningEffort is left at its base default.
    public static string? ReasoningEffort(string? effort)
    {
        if (effort is null || effort.Length == 0) return null; // :34
        switch (effort)
        {
            case "low":
            case "high":
            case "max":
                return effort; // :36
            case "medium":
            case "xhigh":
            case "extra-high":
                return "high"; // :37
            default:
                return null; // :38
        }
    }

    // :42-57
    public override string FriendlyError(string message, int statusCode)
    {
        var lower = SwiftString.Lowercased(message); // :43
        if (lower.Contains("balance", StringComparison.Ordinal) || lower.Contains("insufficient", StringComparison.Ordinal)) // :44
        {
            return "DeepSeek has no balance left. Top up at platform.deepseek.com, then try again."; // :45
        }
        if (statusCode == 429 || lower.Contains("rate limit", StringComparison.Ordinal)) // :47
        {
            return "DeepSeek is rate-limited right now. Wait a moment and try again."; // :48
        }
        if (lower.Contains("context", StringComparison.Ordinal) && (lower.Contains("length", StringComparison.Ordinal) || lower.Contains("window", StringComparison.Ordinal) || lower.Contains("token", StringComparison.Ordinal))) // :50
        {
            return "The conversation no longer fits DeepSeek's context window. Start a new thread or compact first."; // :51
        }
        if (lower.Contains("model", StringComparison.Ordinal) && lower.Contains("not exist", StringComparison.Ordinal)) // :53
        {
            return "DeepSeek no longer offers that model. Pick V4 Pro or V4.1 Flash in the model picker."; // :54
        }
        return message.Length == 0 ? "DeepSeek stopped before finishing." : message; // :56
    }
}
