using System.Globalization;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Port.Usage;

// Source: DroppyCode/Services/Providers/ZaiAPI.swift (B3 52bc7926), :1-207.
//
// Minimal OpenAI-compatible HTTP client for api.z.ai (GLM Coding Plan). Chat streaming itself lives in
// ZaiSession (not part of this unit); this covers the lightweight calls (key validation, model catalog, plan
// limits) the registry needs.
//
// PORT-ONLY: Validate, ListModels, PlanLimits, ResetCredits and ConsumeResetCredit each take an optional
// trailing URL override parameter, defaulting to null. When null (every production call site) the real ZaiAPI
// URL constant is used, exactly as Swift's hardcoded URL; tests pass a loopback URL so the HTTP path (R-NET-1)
// is exercised without reaching the internet (R-TEST-10) -- the same seam MetaAPI.cs (:25, :36) already uses.
// No production behaviour differs: the parameter is never supplied outside tests/Marrow.Services.Tests.

namespace Marrow.Services.Providers;

// :6-207
public static class ZaiAPI
{
    // PORT-ONLY R-TEST-10: scoped loopback endpoint boundary for production registry/view proofs.
    // Identity by default; requests, headers, response parsing and timing remain the pinned Swift path.
    internal static Ambient<Func<Uri, Uri>> Endpoint { get; } = new(() => url => url);
    public static readonly Uri BaseURL = new("https://api.z.ai/api/coding/paas/v4");                  // :7
    public static readonly Uri ChatURL = new("https://api.z.ai/api/coding/paas/v4/chat/completions"); // :8
    public static readonly Uri ModelsURL = new("https://api.z.ai/api/coding/paas/v4/models");         // :9
    public static readonly Uri QuotaURL = new("https://api.z.ai/api/monitor/usage/quota/limit");      // :10
    public static readonly Uri ResetListURL = new("https://api.z.ai/api/biz/customer-package-reset/list?targetType=PERSONAL"); // :11
    public static readonly Uri ResetUseURL = new("https://api.z.ai/api/biz/customer-package-reset/use"); // :12

    // :14
    public static readonly ValueList<string> ZaiEfforts = new(["low", "high", "max"]);

    // :16-17. int, as OpenAICompatibleSession's configuration takes them.
    public const int ContextWindow = 1_000_000;
    public const int MaxOutputTokens = 128_000;

    /// <summary>True when the key lists models successfully.</summary>
    // :20-22
    public static async Task<bool> Validate(string apiKey, Uri? modelsUrl = null) =>
        // :21
        await OpenAICompatibleAPI.Validate(modelsUrl ?? Endpoint.Current(ModelsURL), apiKey);

    // :24-26
    public static async Task<ValueList<ModelOption>> ListModels(string apiKey, Uri? modelsUrl = null) =>
        // :25
        await OpenAICompatibleAPI.ListModels(modelsUrl ?? Endpoint.Current(ModelsURL), apiKey, Option);

    /// <summary>
    /// Only the two 5.3 Coding Plan models are offered. Older GLM ids are routed to 5.3 by Z.ai, so they are
    /// not offered.
    /// </summary>
    // :30-45
    public static ModelOption? Option(string id)
    {
        switch (id)
        {
            case "glm-5.3": // :32-33
                return new ModelOption(id: id, name: "GLM-5.3",
                    detail: "Best reasoning and coding \U000000B7 1M context", // U+00B7 MIDDLE DOT
                    efforts: ZaiEfforts, defaultEffort: "max", isDefault: true);
            case "glm-5.3-flash": // :34-35
                return new ModelOption(id: id, name: "GLM-5.3 Flash",
                    detail: "Fast, reads images \U000000B7 1M context", // U+00B7 MIDDLE DOT
                    efforts: ZaiEfforts, defaultEffort: "max");
            default:
                // :37-41. GLM-5.3-FlashX is on the account's model list but not in the Coding Plan
                // ("GLM-5.3-FlashX is not yet available on the plan", docs.z.ai/guides/vlm/glm-5.3-flash), so
                // every request for it is turned down with 1311 before a single credit is spent. It is never
                // offered, or a pair can be pointed at a model that cannot answer.
                if (!SwiftString.HasPrefix(id, "glm-5.3") ||
                    SwiftString.Lowercased(id).Contains("flashx", StringComparison.Ordinal)) // :42
                {
                    return null;
                }
                return new ModelOption(id: id, name: id, efforts: ZaiEfforts, defaultEffort: "max"); // :43
        }
    }

    /// <summary>Only Flash accepts images; the full model returns an error for them.</summary>
    // :48-51
    public static bool IsVisionModel(string? id)
    {
        if (id is not { } value) return false; // :49
        return value.Contains("flash", StringComparison.Ordinal) || value.EndsWith("v", StringComparison.Ordinal); // :50
    }

    /// <summary>
    /// The Coding Plan's rolling windows from the monitor quota endpoint. Nil when the key is missing, rejected
    /// or the API is unreachable.
    /// </summary>
    // :55-66
    public static async Task<PlanLimits?> PlanLimits(string apiKey, Uri? quotaUrl = null, Uri? resetListUrl = null, bool reportedWindowsOnly = false)
    {
        var key = apiKey.Trimmed(); // :56
        if (key.Length == 0) return null; // :57
        var request = OpenAICompatibleAPI.BearerRequest(quotaUrl ?? Endpoint.Current(QuotaURL), key); // :58
        // :59-61
        var result = await SwiftTry.OptionalValueAsync(() => URLSession.Shared.Data(request));
        if (reportedWindowsOnly && result?.Response is HTTPURLResponse response) LimitsPollException.ThrowIfThrottled(response);
        if (result is not { } exchange || exchange.Response is not HTTPURLResponse http || http.StatusCode != 200 ||
            JSONValue.Parse(exchange.Data) is not { } json)
        {
            return null;
        }
        if (json["code"]?.JSONValueInt != 200 && json["success"]?.JSONValueBool != true) return null; // :62
        if (Limits(json, reportedWindowsOnly) is not { } limits) return null; // :63
        return limits with { PlanLimitsResetCredits = await ResetCredits(key, resetListUrl) }; // :64-65
    }

    // :68-130
    public static PlanLimits? Limits(JSONValue json, bool reportedWindowsOnly = false)
    {
        // :69
        var planName = PlanLimitsReader.PlanName(json["data"]?["level"]?.JSONValueString) is { } level
            ? "Coding Plan " + level
            : null;
        var entries = json["data"]?["limits"]?.JSONValueArray ?? []; // :70
        var windows = new List<PlanLimits.Window>(); // :71
        var creditIndex = 0; // :72
        var toolsIndex = 0; // :73
        foreach (var entry in entries) // :74
        {
            var type = entry["type"]?.JSONValueString ?? ""; // :75
            var usage = entry["usage"]?.JSONValueDouble ?? 0; // :76
            var current = entry["currentValue"]?.JSONValueDouble ?? 0; // :77
            double percent; // :78-85
            if (entry["percentage"]?.JSONValueDouble is { } reported)
            {
                percent = reported;
            }
            else if (usage > 0)
            {
                percent = current / usage * 100;
            }
            else
            {
                percent = 0;
            }
            percent = Math.Min(Math.Max(percent, 0), 100); // :86
            var resetsAt = entry["nextResetTime"]?.JSONValueDouble is { } ms // :87
                ? SwiftDate.From1970Seconds(ms / 1_000)
                : (DateTimeOffset?)null;
            switch (type) // :88
            {
                case "CREDIT_LIMIT": // :89
                case "TOKENS_LIMIT":
                {
                    var unit = entry["unit"]?.JSONValueInt; // :90
                    var number = entry["number"]?.JSONValueDouble ?? 0; // :91
                    long? minutes = unit switch // :92-98
                    {
                        3 => number > 0 ? SwiftNumber.TruncatingToInt(number * 60) : null,
                        4 => number > 0 ? SwiftNumber.TruncatingToInt(number * 1_440) : null,
                        6 => number > 0 ? SwiftNumber.TruncatingToInt(number * 10_080) : null,
                        5 => number > 0 ? SwiftNumber.TruncatingToInt(number * 43_200) : null,
                        _ => null,
                    };
                    string title; // :99-106
                    if (minutes is { } minutesValue)
                    {
                        title = PlanLimitsReader.WindowTitle(minutesValue);
                    }
                    else if (reportedWindowsOnly)
                    {
                        title = PlanLimitsReader.WindowTitle(null);
                    }
                    else if (creditIndex == 0)
                    {
                        title = "5-hour limit";
                    }
                    else
                    {
                        title = "Weekly";
                    }
                    windows.Add(new PlanLimits.Window // :107-113
                    {
                        Id = "credit-" + creditIndex.ToString(CultureInfo.InvariantCulture),
                        Title = title,
                        DurationMinutes = minutes, // OD-0114 / ED-406
                        Percent = percent,
                        ResetsAt = resetsAt,
                        Detail = FormatCount(current) + " of " + FormatCount(usage) + " credits",
                    });
                    creditIndex += 1; // :114
                    break;
                }
                case "TIME_LIMIT": // :115
                    windows.Add(new PlanLimits.Window // :116-122
                    {
                        Id = "tools-" + toolsIndex.ToString(CultureInfo.InvariantCulture),
                        Title = "Monthly \U000000B7 MCP tools", // U+00B7 MIDDLE DOT
                        Percent = percent,
                        ResetsAt = resetsAt,
                        Detail = FormatCount(current) + " of " + FormatCount(usage) + " calls",
                    });
                    toolsIndex += 1; // :123
                    break;
                default: // :124-125
                    continue;
            }
        }
        if (windows.Count == 0) return null; // :128
        return new PlanLimits { PlanName = planName, Windows = new ValueList<PlanLimits.Window>(windows) }; // :129
    }

    /// <summary>
    /// The reset cards the account holds, one row per card: the 5-hour ones first, then the weekly ones. Nil
    /// when the list cannot be read, so the section stays hidden.
    /// </summary>
    // :134-155
    public static async Task<PlanLimits.ResetCredits?> ResetCredits(string apiKey, Uri? resetListUrl = null)
    {
        var request = new URLRequest(resetListUrl ?? Endpoint.Current(ResetListURL), timeoutInterval: 15); // :135
        request = request.SetValue("Bearer " + apiKey, forHTTPHeaderField: "Authorization"); // :136
        request = request.SetValue("application/json", forHTTPHeaderField: "Accept"); // :137
        // :138-141
        var result = await SwiftTry.OptionalValueAsync(() => URLSession.Shared.Data(request));
        if (result is not { } exchange || exchange.Response is not HTTPURLResponse http || http.StatusCode != 200 ||
            JSONValue.Parse(exchange.Data) is not { } json || json["code"]?.JSONValueInt != 200 ||
            json["data"] is not { } payload)
        {
            return null;
        }
        var credits = new List<PlanLimits.ResetCredit>(); // :142
        // :143-153
        foreach (var (key, type, title) in new[]
                 {
                     ("fiveHourResets", "FIVE_HOUR", "Resets the 5-hour limit"),
                     ("weekResets", "WEEK", "Resets the weekly limit"),
                 })
        {
            foreach (var card in payload[key]?.JSONValueArray ?? []) // :144
            {
                // :145
                if (card["available"]?.JSONValueBool == false || card["recordId"]?.JSONValueInt is not { } record)
                {
                    continue;
                }
                credits.Add(new PlanLimits.ResetCredit // :146-151
                {
                    Id = type + ":" + record.ToString(CultureInfo.InvariantCulture),
                    Title = title,
                    Detail = null,
                    ExpiresAt = card["expireTime"]?.JSONValueString is { } expiry ? ResetDate(expiry) : null,
                });
            }
        }
        return new PlanLimits.ResetCredits { AvailableCount = credits.Count, Credits = new ValueList<PlanLimits.ResetCredit>(credits) }; // :154
    }

    /// <summary>Spends one card. The id is the <c>type:recordId</c> pair <see cref="ResetCredits"/> built.</summary>
    // :158-181
    public static async Task<PlanLimits.ResetOutcome> ConsumeResetCredit(string apiKey, string creditID, Uri? resetUseUrl = null)
    {
        var parts = SwiftString.Split(creditID, ":", maxSplits: 1); // :159 omits empty subsequences: ":1" is ["1"]
        if (parts.Count != 2 || SwiftNumber.ParseInt(parts[1]) is not { } record) // :160
        {
            throw new ResetCreditError(new ResetCreditError.Case.UnknownOutcome());
        }
        var request = new URLRequest(resetUseUrl ?? Endpoint.Current(ResetUseURL), timeoutInterval: 20) { HttpMethod = "POST" }; // :161-162
        request = request.SetValue("Bearer " + apiKey, forHTTPHeaderField: "Authorization"); // :163
        request = request.SetValue("application/json", forHTTPHeaderField: "Content-Type"); // :164
        request = request.SetValue("application/json", forHTTPHeaderField: "Accept"); // :165
        var body = JSONValue.FromDictionaryLiteral( // :166-171
            ("targetType", "PERSONAL"),
            ("resetType", parts[0]),
            ("recordId", record),
            ("requestId", SwiftString.Lowercased(SwiftUUID.UuidString(Guid.NewGuid()))));
        request = request with { HttpBody = body.Data() }; // :172
        var (data, _) = await URLSession.Shared.Data(request); // :173
        if (JSONValue.Parse(data) is not { } json) throw new ResetCreditError(new ResetCreditError.Case.UnknownOutcome()); // :174
        if (json["code"]?.JSONValueInt != 200 || json["success"]?.JSONValueBool != true) // :175
        {
            // :176
            throw new ResetCreditError(new ResetCreditError.Case.Rejected(json["msg"]?.JSONValueString ?? "Z.ai declined the reset."));
        }
        // :178-179. The meters lag the reset by a moment; the dashboard waits the same two seconds.
        _ = await SwiftTask.TrySleep(SwiftTask.Seconds(2));
        // Marrow.Services.Providers.PlanLimits (the type): "PlanLimits" alone here would resolve to the
        // PlanLimits method above (CS0119), since member lookup inside this class prefers the method group.
        return Marrow.Services.Providers.PlanLimits.ResetOutcome.Reset; // :180
    }

    /// <summary>Z.ai writes its card expiries as naive <c>yyyy-MM-dd HH:mm:ss</c> in the operator's zone.</summary>
    // :183-194. B3 moves the DateFormatter construction into a cached static (:188-194) so it is built once, not
    // on every call; the C# port never built a formatter object per call (TryParseExact and TimeZoneInfo need no
    // such cache), so this restructuring has no C# counterpart and no observable difference (A8.md 2.A: "Zai
    // caches resetDateFormatter. Nothing observable changes.").
    private static DateTimeOffset? ResetDate(string text)
    {
        // :190-192 (locale/time-zone/format, folded into the call below at :185/:193)
        if (!DateTime.TryParseExact(text, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            return null;
        }
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai");
        var utc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(parsed, DateTimeKind.Unspecified), zone);
        return new DateTimeOffset(utc, TimeSpan.Zero); // :185, :193
    }

    // :196-206. NumberFormatter with locale en_US, decimal style and maximumFractionDigits 0 (R-FMT-1).
    private static string FormatCount(double value) =>
        SwiftFormat.DecimalNoFraction(value, CultureInfo.GetCultureInfo("en-US")); // :197-205
}
