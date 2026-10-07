using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Port.Usage;

// Source: DroppyCode/Services/Providers/PiPlanLimits.swift (B3 52bc792), :1-115.
// ED-168: URLSession, URLSessionConfiguration, URLRequest, URLResponse, HTTPURLResponse, URLError and URLCache
// moved from Marrow.Services.Port to Marrow.Core.Port; used here via "using Marrow.Core.Port;".

namespace Marrow.Services.Providers;

/// <summary>The subscription windows Pi itself reports for the providers it is signed into.
/// Pi keeps those providers' OAuth tokens in `&lt;config&gt;/auth.json`, so the usage panel can show the same
/// windows Pi's own TUI shows without owning the logins.</summary>
// :3-6. A caseless enum -> static class.
public static class PiPlanLimits
{
    // :7-8
    private static readonly Uri AnthropicUsageURL = new("https://api.anthropic.com/api/oauth/usage");
    private static readonly Uri CodexUsageURL = new("https://chatgpt.com/backend-api/wham/usage");

    /// <summary>Every OAuth login Pi holds that still has a live token, in sorted provider order.</summary>
    // :11-41
    public static Task<PlanLimits?> Read(IReadOnlyDictionary<string, string> environment, bool reportThrottling = false) =>
        Read(environment, null, null, reportThrottling);

    /// <summary>PORT-ONLY test seam: <see cref="Read(IReadOnlyDictionary{string, string})"/> with the two
    /// endpoint URLs overridable so a test can point them at a loopback server (R-TEST-10); production callers
    /// always take the single-argument overload above, which passes null for both.</summary>
    internal static async Task<PlanLimits?> Read(IReadOnlyDictionary<string, string> environment,
        Uri? anthropicUsageUrl, Uri? codexUsageUrl, bool reportThrottling = false)
    {
        var file = SwiftPath.AppendingPathComponent(PiCLI.ConfigDirectoryPath(environment), "auth.json"); // :12
        if (SwiftTry.Optional(() => File.ReadAllBytes(file)) is not { } data // :13
            || JSONValue.Parse(data) is not { } json // :14
            || json.JSONValueObject is not { } @object) // :15
        {
            return null;
        }
        var windows = new List<PlanLimits.Window>(); // :16
        var names = new List<string>(); // :17
        foreach (var key in @object.Keys.OrderBy(k => k, SwiftStringComparer.Instance)) // :18. Ordinal (windows_path).
        {
            var entry = @object[key];
            if (entry["type"]?.JSONValueString != "oauth" || // :20
                entry["access"]?.JSONValueString is not { Length: > 0 } token) // :21
            {
                continue;
            }
            // An expired token buys nothing from the usage endpoint, so it is skipped.
            if (entry["expires"]?.JSONValueDouble is { } expires && // :23
                SwiftDate.From1970Seconds(expires / 1_000) < SwiftDate.Now) // :24
            {
                continue; // :25
            }
            PlanLimits? limits; // :27
            if (key == "anthropic") // :28
            {
                limits = await ReadAnthropic(token, anthropicUsageUrl, reportThrottling); // :29
            }
            else if (key == "openai-codex" || key == "openai") // :30
            {
                limits = await ReadCodex(token, codexUsageUrl, reportThrottling); // :31
            }
            else
            {
                continue; // :33
            }
            if (limits is null) continue; // :35
            windows.AddRange(limits.Windows); // :36
            if (limits.PlanName is { } name) names.Add(name); // :37
        }
        if (windows.Count == 0) return null; // :39
        return new PlanLimits // :40
        {
            PlanName = names.Count == 0 ? null : string.Join(" \U000000B7 ", names), // U+00B7 MIDDLE DOT
            Windows = new ValueList<PlanLimits.Window>(windows),
        };
    }

    /// <summary>Anthropic's OAuth usage endpoint answers the same shape Claude Code reads itself.</summary>
    // :43-57
    private static async Task<PlanLimits?> ReadAnthropic(string token, Uri? usageUrl, bool reportThrottling)
    {
        var request = new URLRequest(usageUrl ?? AnthropicUsageURL, timeoutInterval: 15); // :45
        request = request.SetValue("Bearer " + token, forHTTPHeaderField: "Authorization"); // :46
        request = request.SetValue("oauth-2025-04-20", forHTTPHeaderField: "anthropic-beta"); // :47
        request = request.SetValue("application/json", forHTTPHeaderField: "Accept"); // :48
        var result = await SwiftTry.OptionalValueAsync(() => URLSession.Shared.Data(request));
        if (reportThrottling && result?.Response is HTTPURLResponse response) LimitsPollException.ThrowIfThrottled(response);
        if (result is not { } exchange || // :49
            exchange.Response is not HTTPURLResponse http || http.StatusCode != 200 || // :50
            JSONValue.Parse(exchange.Data) is not { } json || // :51
            PlanLimitsReader.ParseClaudeUsage(json, json["limits"]?.JSONValueArray ?? [], planName: null, titlePrefix: "Claude") is not { } limits) // :52
        {
            return null; // :53
        }
        var mapped = json["subscription_type"]?.JSONValueString is { } subscriptionType // :55
            ? PlanLimitsReader.PlanName(subscriptionType)
            : null;
        return limits with { PlanName = mapped is { } name ? "Claude " + name : "Claude" }; // :56
    }

    /// <summary>The usage payload behind ChatGPT's quota display, with its primary and secondary windows.</summary>
    // :59-79
    private static async Task<PlanLimits?> ReadCodex(string token, Uri? usageUrl, bool reportThrottling)
    {
        var request = new URLRequest(usageUrl ?? CodexUsageURL, timeoutInterval: 15); // :61
        request = request.SetValue("Bearer " + token, forHTTPHeaderField: "Authorization"); // :62
        request = request.SetValue("application/json", forHTTPHeaderField: "Accept"); // :63
        // The endpoint scopes quota to the account, which only the token's own claim names.
        if (CodexAccountID(token) is { Length: > 0 } accountID) // :65
        {
            request = request.SetValue(accountID, forHTTPHeaderField: "ChatGPT-Account-Id"); // :66
        }
        var result = await SwiftTry.OptionalValueAsync(() => URLSession.Shared.Data(request));
        if (reportThrottling && result?.Response is HTTPURLResponse response) LimitsPollException.ThrowIfThrottled(response);
        if (result is not { } exchange || // :68
            exchange.Response is not HTTPURLResponse http || http.StatusCode != 200 || // :69
            JSONValue.Parse(exchange.Data) is not { } json) // :70
        {
            return null;
        }
        var rateLimit = json["rate_limit"] ?? json; // :71
        var windows = new List<PlanLimits.Window>(); // :72-75
        if (CodexWindow("codex-primary", rateLimit["primary_window"]) is { } primary) windows.Add(primary);
        if (CodexWindow("codex-secondary", rateLimit["secondary_window"]) is { } secondary) windows.Add(secondary);
        if (windows.Count == 0) return null; // :76
        var mapped = json["plan_type"]?.JSONValueString is { } planType // :77
            ? PlanLimitsReader.CodexPlanName(planType)
            : null;
        return new PlanLimits // :78
        {
            PlanName = mapped is { } name ? "Codex " + name : "Codex",
            Windows = new ValueList<PlanLimits.Window>(windows),
        };
    }

    // :81-100
    internal static PlanLimits.Window? CodexWindow(string id, JSONValue? raw)
    {
        if (raw is not { } value || // :82
            value["used_percent"]?.JSONValueDouble is not { } percent || // :83
            value["limit_window_seconds"]?.JSONValueInt is not { } windowSeconds) // :84
        {
            return null;
        }
        DateTimeOffset? resetsAt; // :85-93
        if (value["reset_at"]?.JSONValueDouble is { } resetAt)
        {
            resetsAt = SwiftDate.From1970Seconds(resetAt); // :86-87
        }
        else if (value["reset_after_seconds"]?.JSONValueInt is { } after)
        {
            resetsAt = SwiftDate.AddingTimeInterval(SwiftDate.Now, after); // :89-90
        }
        else
        {
            resetsAt = null; // :92
        }
        return new PlanLimits.Window // :94-99
        {
            Id = id,
            DurationMinutes = windowSeconds / 60, // OD-0114 / ED-406
            Title = "Codex \U000000B7 " + PlanLimitsReader.WindowTitle(windowSeconds / 60), // U+00B7 MIDDLE DOT; integer division (:96)
            Percent = percent,
            ResetsAt = resetsAt,
        };
    }

    /// <summary>The account id lives in the token payload's OpenAI auth claim, so no extra call is needed to find it.</summary>
    // :102-114
    internal static string? CodexAccountID(string token)
    {
        var parts = token.Split('.', StringSplitOptions.RemoveEmptyEntries); // :104. Swift split(separator:) omits empty subsequences.
        if (parts.Length < 2) return null; // :105
        var base64 = parts[1].Replace("-", "+", StringComparison.Ordinal).Replace("_", "/", StringComparison.Ordinal); // :106
        var remainder = base64.Length % 4; // :107
        if (remainder != 0) // :108
        {
            base64 += new string('=', 4 - remainder); // :109
        }
        // :111 Data(base64Encoded:) uses the default strict decoder. Convert.FromBase64String ignores
        // ASCII whitespace, so reject it here rather than accepting a payload Foundation would reject.
        if (base64.Any(c => c is ' ' or '\t' or '\r' or '\n')) return null;
        byte[]? data; // :111
        try { data = Convert.FromBase64String(base64); }
        catch (FormatException) { data = null; }
        if (data is null || JSONValue.Parse(data) is not { } json) return null; // :111-112
        return json["https://api.openai.com/auth"]?["chatgpt_account_id"]?.JSONValueString; // :113
    }
}
