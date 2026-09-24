using System.Globalization;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;

// Source: DroppyCode/Services/Providers/ZaiAPI.swift (B2 1fbf81e), :1-224.
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

// :6-224
public static class ZaiAPI
{
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
    // :20-29
    public static async Task<bool> Validate(string apiKey, Uri? modelsUrl = null)
    {
        var key = apiKey.Trimmed(); // :21
        if (key.Length == 0) return false; // :22
        var request = new URLRequest(modelsUrl ?? ModelsURL, timeoutInterval: 15); // :23
        request = request.SetValue("Bearer " + key, forHTTPHeaderField: "Authorization"); // :24
        request = request.SetValue("application/json", forHTTPHeaderField: "Accept"); // :25
        // :26-27
        var result = await SwiftTry.OptionalValueAsync(() => URLSession.Shared.Data(request));
        if (result is not { } exchange || exchange.Response is not HTTPURLResponse http) return false;
        return http.StatusCode == 200; // :28
    }

    // :31-45
    public static async Task<ValueList<ModelOption>> ListModels(string apiKey, Uri? modelsUrl = null)
    {
        var key = apiKey.Trimmed(); // :32
        if (key.Length == 0) return ValueList<ModelOption>.Empty; // :33
        var request = new URLRequest(modelsUrl ?? ModelsURL, timeoutInterval: 15); // :34
        request = request.SetValue("Bearer " + key, forHTTPHeaderField: "Authorization"); // :35
        request = request.SetValue("application/json", forHTTPHeaderField: "Accept"); // :36
        var (data, response) = await URLSession.Shared.Data(request); // :37
        if (response is not HTTPURLResponse http || http.StatusCode != 200 || JSONValue.Parse(data) is not { } json) // :38-39
        {
            return ValueList<ModelOption>.Empty;
        }
        var ids = new List<string>(); // :40
        foreach (var item in json["data"]?.JSONValueArray ?? [])
        {
            if (item["id"]?.JSONValueString is { } id) ids.Add(id);
        }
        if (ids.Count == 0) return ValueList<ModelOption>.Empty; // :41
        var options = new List<ModelOption>(); // :42-44
        foreach (var id in ids)
        {
            if (Option(id) is { } option) options.Add(option);
        }
        return new ValueList<ModelOption>(options
            .OrderBy(o => o.IsDefault ? 0 : 1)
            .ThenBy(o => o.Id, SwiftStringComparer.Instance));
    }

    /// <summary>
    /// Only the two 5.3 Coding Plan models are offered. Older GLM ids are routed to 5.3 by Z.ai, so they are
    /// not offered.
    /// </summary>
    // :49-64
    public static ModelOption? Option(string id)
    {
        switch (id)
        {
            case "glm-5.3": // :51-52
                return new ModelOption(id: id, name: "GLM-5.3",
                    detail: "Best reasoning and coding \U000000B7 1M context", // U+00B7 MIDDLE DOT
                    efforts: ZaiEfforts, defaultEffort: "max", isDefault: true);
            case "glm-5.3-flash": // :53-54
                return new ModelOption(id: id, name: "GLM-5.3 Flash",
                    detail: "Fast, reads images \U000000B7 1M context", // U+00B7 MIDDLE DOT
                    efforts: ZaiEfforts, defaultEffort: "max");
            default:
                // :56-60. GLM-5.3-FlashX is on the account's model list but not in the Coding Plan
                // ("GLM-5.3-FlashX is not yet available on the plan", docs.z.ai/guides/vlm/glm-5.3-flash), so
                // every request for it is turned down with 1311 before a single credit is spent. It is never
                // offered, or a pair can be pointed at a model that cannot answer.
                if (!SwiftString.HasPrefix(id, "glm-5.3") ||
                    SwiftString.Lowercased(id).Contains("flashx", StringComparison.Ordinal)) // :61
                {
                    return null;
                }
                return new ModelOption(id: id, name: id, efforts: ZaiEfforts, defaultEffort: "max"); // :62
        }
    }

    /// <summary>Only Flash accepts images; the full model returns an error for them.</summary>
    // :67-70
    public static bool IsVisionModel(string? id)
    {
        if (id is not { } value) return false; // :68
        return value.Contains("flash", StringComparison.Ordinal) || value.EndsWith("v", StringComparison.Ordinal); // :69
    }

    /// <summary>
    /// The Coding Plan's rolling windows from the monitor quota endpoint. Nil when the key is missing, rejected
    /// or the API is unreachable.
    /// </summary>
    // :74-87
    public static async Task<PlanLimits?> PlanLimits(string apiKey, Uri? quotaUrl = null, Uri? resetListUrl = null)
    {
        var key = apiKey.Trimmed(); // :75
        if (key.Length == 0) return null; // :76
        var request = new URLRequest(quotaUrl ?? QuotaURL, timeoutInterval: 15); // :77
        request = request.SetValue("Bearer " + key, forHTTPHeaderField: "Authorization"); // :78
        request = request.SetValue("application/json", forHTTPHeaderField: "Accept"); // :79
        // :80-82
        var result = await SwiftTry.OptionalValueAsync(() => URLSession.Shared.Data(request));
        if (result is not { } exchange || exchange.Response is not HTTPURLResponse http || http.StatusCode != 200 ||
            JSONValue.Parse(exchange.Data) is not { } json)
        {
            return null;
        }
        if (json["code"]?.JSONValueInt != 200 && json["success"]?.JSONValueBool != true) return null; // :83
        if (Limits(json) is not { } limits) return null; // :84
        return limits with { PlanLimitsResetCredits = await ResetCredits(key, resetListUrl) }; // :85-86
    }

    // :89-151
    public static PlanLimits? Limits(JSONValue json)
    {
        // :90
        var planName = PlanLimitsReader.PlanName(json["data"]?["level"]?.JSONValueString) is { } level
            ? "Coding Plan " + level
            : null;
        var entries = json["data"]?["limits"]?.JSONValueArray ?? []; // :91
        var windows = new List<PlanLimits.Window>(); // :92
        var creditIndex = 0; // :93
        var toolsIndex = 0; // :94
        foreach (var entry in entries) // :95
        {
            var type = entry["type"]?.JSONValueString ?? ""; // :96
            var usage = entry["usage"]?.JSONValueDouble ?? 0; // :97
            var current = entry["currentValue"]?.JSONValueDouble ?? 0; // :98
            double percent; // :99-106
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
            percent = Math.Min(Math.Max(percent, 0), 100); // :107
            var resetsAt = entry["nextResetTime"]?.JSONValueDouble is { } ms // :108
                ? SwiftDate.From1970Seconds(ms / 1_000)
                : (DateTimeOffset?)null;
            switch (type) // :109
            {
                case "CREDIT_LIMIT": // :110
                case "TOKENS_LIMIT":
                {
                    var unit = entry["unit"]?.JSONValueInt; // :111
                    var number = entry["number"]?.JSONValueDouble ?? 0; // :112
                    long? minutes = unit switch // :113-119
                    {
                        3 => number > 0 ? SwiftNumber.TruncatingToInt(number * 60) : null,
                        4 => number > 0 ? SwiftNumber.TruncatingToInt(number * 1_440) : null,
                        6 => number > 0 ? SwiftNumber.TruncatingToInt(number * 10_080) : null,
                        5 => number > 0 ? SwiftNumber.TruncatingToInt(number * 43_200) : null,
                        _ => null,
                    };
                    string title; // :120-127
                    if (minutes is { } minutesValue)
                    {
                        title = PlanLimitsReader.WindowTitle(minutesValue);
                    }
                    else if (creditIndex == 0)
                    {
                        title = "5-hour limit";
                    }
                    else
                    {
                        title = "Weekly";
                    }
                    windows.Add(new PlanLimits.Window // :128-134
                    {
                        Id = "credit-" + creditIndex.ToString(CultureInfo.InvariantCulture),
                        Title = title,
                        Percent = percent,
                        ResetsAt = resetsAt,
                        Detail = FormatCount(current) + " of " + FormatCount(usage) + " credits",
                    });
                    creditIndex += 1; // :135
                    break;
                }
                case "TIME_LIMIT": // :136
                    windows.Add(new PlanLimits.Window // :137-143
                    {
                        Id = "tools-" + toolsIndex.ToString(CultureInfo.InvariantCulture),
                        Title = "Monthly \U000000B7 MCP tools", // U+00B7 MIDDLE DOT
                        Percent = percent,
                        ResetsAt = resetsAt,
                        Detail = FormatCount(current) + " of " + FormatCount(usage) + " calls",
                    });
                    toolsIndex += 1; // :144
                    break;
                default: // :145-146
                    continue;
            }
        }
        if (windows.Count == 0) return null; // :149
        return new PlanLimits { PlanName = planName, Windows = new ValueList<PlanLimits.Window>(windows) }; // :150
    }

    /// <summary>
    /// The reset cards the account holds, one row per card: the 5-hour ones first, then the weekly ones. Nil
    /// when the list cannot be read, so the section stays hidden.
    /// </summary>
    // :155-176
    public static async Task<PlanLimits.ResetCredits?> ResetCredits(string apiKey, Uri? resetListUrl = null)
    {
        var request = new URLRequest(resetListUrl ?? ResetListURL, timeoutInterval: 15); // :156
        request = request.SetValue("Bearer " + apiKey, forHTTPHeaderField: "Authorization"); // :157
        request = request.SetValue("application/json", forHTTPHeaderField: "Accept"); // :158
        // :159-162
        var result = await SwiftTry.OptionalValueAsync(() => URLSession.Shared.Data(request));
        if (result is not { } exchange || exchange.Response is not HTTPURLResponse http || http.StatusCode != 200 ||
            JSONValue.Parse(exchange.Data) is not { } json || json["code"]?.JSONValueInt != 200 ||
            json["data"] is not { } payload)
        {
            return null;
        }
        var credits = new List<PlanLimits.ResetCredit>(); // :163
        // :164-174
        foreach (var (key, type, title) in new[]
                 {
                     ("fiveHourResets", "FIVE_HOUR", "Resets the 5-hour limit"),
                     ("weekResets", "WEEK", "Resets the weekly limit"),
                 })
        {
            foreach (var card in payload[key]?.JSONValueArray ?? []) // :165
            {
                // :166
                if (card["available"]?.JSONValueBool == false || card["recordId"]?.JSONValueInt is not { } record)
                {
                    continue;
                }
                credits.Add(new PlanLimits.ResetCredit // :167-172
                {
                    Id = type + ":" + record.ToString(CultureInfo.InvariantCulture),
                    Title = title,
                    Detail = null,
                    ExpiresAt = card["expireTime"]?.JSONValueString is { } expiry ? ResetDate(expiry) : null,
                });
            }
        }
        return new PlanLimits.ResetCredits { AvailableCount = credits.Count, Credits = new ValueList<PlanLimits.ResetCredit>(credits) }; // :175
    }

    /// <summary>Spends one card. The id is the <c>type:recordId</c> pair <see cref="ResetCredits"/> built.</summary>
    // :179-202
    public static async Task<PlanLimits.ResetOutcome> ConsumeResetCredit(string apiKey, string creditID, Uri? resetUseUrl = null)
    {
        var parts = creditID.Split(':', 2); // :180
        if (parts.Length != 2 || SwiftNumber.ParseInt(parts[1]) is not { } record) // :181
        {
            throw new ResetCreditError(new ResetCreditError.Case.UnknownOutcome());
        }
        var request = new URLRequest(resetUseUrl ?? ResetUseURL, timeoutInterval: 20) { HttpMethod = "POST" }; // :182-183
        request = request.SetValue("Bearer " + apiKey, forHTTPHeaderField: "Authorization"); // :184
        request = request.SetValue("application/json", forHTTPHeaderField: "Content-Type"); // :185
        request = request.SetValue("application/json", forHTTPHeaderField: "Accept"); // :186
        var body = JSONValue.FromDictionaryLiteral( // :187-192
            ("targetType", "PERSONAL"),
            ("resetType", parts[0]),
            ("recordId", record),
            ("requestId", SwiftString.Lowercased(SwiftUUID.UuidString(Guid.NewGuid()))));
        request = request with { HttpBody = body.Data() }; // :193
        var (data, _) = await URLSession.Shared.Data(request); // :194
        if (JSONValue.Parse(data) is not { } json) throw new ResetCreditError(new ResetCreditError.Case.UnknownOutcome()); // :195
        if (json["code"]?.JSONValueInt != 200 || json["success"]?.JSONValueBool != true) // :196
        {
            // :197
            throw new ResetCreditError(new ResetCreditError.Case.Rejected(json["msg"]?.JSONValueString ?? "Z.ai declined the reset."));
        }
        // :199-200. The meters lag the reset by a moment; the dashboard waits the same two seconds.
        _ = await SwiftTask.TrySleep(SwiftTask.Seconds(2));
        // Marrow.Services.Providers.PlanLimits (the type): "PlanLimits" alone here would resolve to the
        // PlanLimits method above (CS0119), since member lookup inside this class prefers the method group.
        return Marrow.Services.Providers.PlanLimits.ResetOutcome.Reset; // :201
    }

    /// <summary>Z.ai writes its card expiries as naive <c>yyyy-MM-dd HH:mm:ss</c> in the operator's zone.</summary>
    // :204-211
    private static DateTimeOffset? ResetDate(string text)
    {
        // :206-209
        if (!DateTime.TryParseExact(text, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            return null;
        }
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai");
        var utc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(parsed, DateTimeKind.Unspecified), zone);
        return new DateTimeOffset(utc, TimeSpan.Zero); // :210
    }

    // :213-223. NumberFormatter with locale en_US, decimal style and maximumFractionDigits 0 (R-FMT-1).
    private static string FormatCount(double value) =>
        SwiftFormat.DecimalNoFraction(value, CultureInfo.GetCultureInfo("en-US")); // :214-222
}
