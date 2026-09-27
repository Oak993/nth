using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;

namespace Marrow.Services.Providers;

// Source: DroppyCode/Services/Providers/ZaiSession.swift (B3 52bc792), :1-106.
//
// Native Z.ai agent: talks to https://api.z.ai/api/coding/paas/v4/chat/completions directly
// (OpenAI-compatible, the GLM Coding Plan endpoint) and executes coding tools locally, so no CLI
// install is needed, just an API key in Settings -> Providers -> Z.ai.
public sealed class ZaiSession : OpenAICompatibleSession
{
    // :8-18
    private static readonly Config config = new()
    {
        Kind = ProviderKind.Zai,
        Title = "Z.ai",
        AgentIdentity = _ => "Z.ai GLM coding agent", // :11
        DefaultModel = "glm-5.3", // :12
        ChatURL = ZaiAPI.ChatURL, // :13
        ContextWindow = ZaiAPI.ContextWindow, // :14
        MaxOutputTokens = ZaiAPI.MaxOutputTokens, // :15
        ReplaysReasoning = true, // :16
        IsVisionModel = ZaiAPI.IsVisionModel, // :17
    };

    // :20-22
    public ZaiSession(SessionConfiguration configuration) : base(config, configuration)
    {
    }

    // :24-31. GLM asks for a max_tokens cap, an always-on thinking mode that keeps prior
    // thinking, and tool_stream alongside the tools.
    public override void ApplyProviderPayload(Dictionary<string, JSONValue> payload, string model, string? effort, bool withTools)
    {
        if (withTools) payload["tool_stream"] = true; // :27
        payload["thinking"] = JSONValue.FromDictionaryLiteral(("type", "enabled"), ("clear_thinking", false)); // :28
        payload["max_tokens"] = new JSONValue.Int(ZaiAPI.MaxOutputTokens); // :29
        if (ReasoningEffort(effort) is { } mapped) payload["reasoning_effort"] = mapped; // :30
    }

    // :33-41
    public static string? ReasoningEffort(string? effort)
    {
        if (effort is null || effort.Length == 0) return null; // :34
        return effort switch // :35
        {
            "low" => "low", // :36
            "medium" or "high" => "high", // :37
            "xhigh" or "extra-high" or "max" => "max", // :38
            _ => null, // :39
        };
    }

    // :43-98. Z.ai answers every one of these refusals with HTTP 429, so the status alone says
    // nothing and the body has to be read: a rate limit, an overloaded service, a model the plan
    // does not carry and a spent window are all 429s (the business codes and their messages are
    // docs.z.ai/api-reference/api-code). A 429 that is not a spent window is never reported as
    // one: an account with its windows untouched must not be told its limit is reached.
    public override string FriendlyError(string message, int statusCode)
    {
        var lower = SwiftString.Lowercased(message); // :50
        // :51-52 a context-length message also says "limit", so it is read before the plan window.
        if ((lower.Contains("context", StringComparison.Ordinal)
                && (lower.Contains("length", StringComparison.Ordinal)
                    || lower.Contains("window", StringComparison.Ordinal)
                    || lower.Contains("token", StringComparison.Ordinal)))
            || lower.Contains("prompt too long", StringComparison.Ordinal)) // :52
        {
            return "The conversation no longer fits GLM-5.3's context window. Start a new thread or compact first."; // :53
        }
        if (statusCode == 401
            || lower.Contains("token expired", StringComparison.Ordinal)
            || lower.Contains("incorrect", StringComparison.Ordinal)
            || lower.Contains("authentication", StringComparison.Ordinal)) // :55
        {
            return "Z.ai rejected the API key. Check it in Settings \U00002192 Providers \U00002192 Z.ai."; // :56 U+2192 RIGHTWARDS ARROW
        }
        // 1311: the plan does not carry the model asked for. Nothing was spent, no window is
        // involved, and the model is named back so the picker can be set right. :58-60
        if (lower.Contains("does not yet include access", StringComparison.Ordinal)) // :60
        {
            return $"Your Z.ai Coding Plan does not include {PlanRefusedModel(message)}. Pick GLM-5.3 or GLM-5.3 Flash for the model, or move up a plan tier."; // :61
        }
        // 1302 and 1305: the account's request rate, or Z.ai's own load. Both pass on their own. :63
        if (lower.Contains("rate limit reached", StringComparison.Ordinal)
            || lower.Contains("too many requests", StringComparison.Ordinal)) // :64
        {
            // ED-030: the em dash (U+2014) is a reproduced upstream user-facing string, kept
            // byte-identical; it carries a checked-in lint carve-out rather than a reworded string.
            return "Z.ai is throttling requests right now \U00002014 its plans cap how many run at once. Wait a moment and send again; fewer at a time get through."; // :65 U+2014 EM DASH
        }
        if (lower.Contains("temporarily overloaded", StringComparison.Ordinal)) // :67
        {
            return "Z.ai is overloaded right now, which says nothing about your plan. Send the message again in a moment."; // :68
        }
        // 1313: the Fair Usage Policy, which lifts on its own like a throttle. :70
        if (lower.Contains("fair usage", StringComparison.Ordinal)) // :71
        {
            return "Z.ai has paused this account's requests under its Fair Usage Policy. It lifts on its own; details at z.ai/devpack/usage-policy."; // :72
        }
        // 1113: no package on this key at all. :74
        if (lower.Contains("insufficient balance", StringComparison.Ordinal)
            || lower.Contains("no resource package", StringComparison.Ordinal)
            || lower.Contains("1113", StringComparison.Ordinal)) // :75
        {
            return "This Z.ai key has no active Coding Plan. Subscribe at z.ai/subscribe, then try again."; // :76
        }
        // 1309: the subscription lapsed. :78
        if (lower.Contains("package has expired", StringComparison.Ordinal)) // :79
        {
            return "Your GLM Coding Plan has expired. Renew it at z.ai/subscribe, then try again."; // :80
        }
        // 1314 and 1315: enterprise packages and their keys. :82
        if (lower.Contains("enterprise", StringComparison.Ordinal)) // :83
        {
            return "This Z.ai key belongs to an enterprise coding package. Use the personal Coding Plan key from z.ai/manage-apikey/subscription."; // :84
        }
        // A spent window (1308, 1310, 1316-1321): "usage limit" is what auto-continue listens
        // for; it then reads the reset time from Z.ai's quota API. :86-87
        if (lower.Contains("usage limit", StringComparison.Ordinal)
            || lower.Contains("limit exhausted", StringComparison.Ordinal)
            || lower.Contains("extra usage", StringComparison.Ordinal)) // :88
        {
            return "Your GLM Coding Plan usage limit is reached for this window. It refreshes on its own; the usage ring shows when. Details at z.ai/manage-apikey/subscription."; // :89
        }
        // 1211, and any other wording for a model Z.ai does not have. :91
        if (lower.Contains("unknown model", StringComparison.Ordinal)
            || (lower.Contains("model", StringComparison.Ordinal) && lower.Contains("not exist", StringComparison.Ordinal))) // :92
        {
            return "Z.ai does not offer that model. Pick GLM-5.3 or GLM-5.3 Flash in the model picker."; // :93
        }
        // Anything else is Z.ai saying no for a reason this build does not know, and it is
        // passed on as Z.ai worded it rather than guessed at. :95-96
        return message.Length == 0 ? "Z.ai stopped before finishing." : message; // :97
    }

    // The model named in a 1311 message, quoted back so it can be changed. :100-105
    private static string PlanRefusedModel(string message)
    {
        const string needle = "access to ";
        var index = -1;
        // :102 compare windows of original text; ICU lowercasing can expand preceding scalars and invalidate indices.
        for (var start = 0; start + needle.Length <= message.Length; start++)
        {
            if (SwiftString.CaseInsensitiveCompare(message.Substring(start, needle.Length), needle) != 0) continue;
            index = start;
            break;
        }
        if (index < 0) return "that model";
        var named = message[(index + needle.Length)..];
        static bool IsTrim(char c) => FoundationText.IsWhitespace(c) || FoundationText.IsNewline(c) || c is '.' or '`' or '"' or '\'';
        var first = 0;
        var last = named.Length;
        while (first < last && IsTrim(named[first])) first++;
        while (last > first && IsTrim(named[last - 1])) last--;
        named = named[first..last]; // :103 trim one union including interleaved whitespace and punctuation
        return named.Length == 0 ? "that model" : named; // :104
    }
}
