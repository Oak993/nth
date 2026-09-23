using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;

// Source: DroppyCode/Services/Providers/ZaiAPI.swift (B2 1fbf81e), :1-224. Skeleton: the URLs and constants
// (:7-17) are complete; the private helpers resetDate, countFormatter and formatCount (:205-223) and every member
// with behaviour are left to the implementing unit. ChatURL and ModelsURL are baseURL.appendingPathComponent
// ("chat/completions") and ("models") (:8-9), written out.

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
    public static readonly ValueList<string> ZaiEfforts = new(new[] { "low", "high", "max" });

    // :16-17. int, as OpenAICompatibleSession's configuration takes them.
    public const int ContextWindow = 1_000_000;
    public const int MaxOutputTokens = 128_000;

    // :20-29
    public static Task<bool> Validate(string apiKey) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/ZaiAPI.swift:20");

    // :31-45
    public static Task<ValueList<ModelOption>> ListModels(string apiKey) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/ZaiAPI.swift:31");

    // :49-64
    public static ModelOption? Option(string id) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/ZaiAPI.swift:49");

    // :67-70
    public static bool IsVisionModel(string? id) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/ZaiAPI.swift:67");

    // :74-87
    public static Task<PlanLimits?> PlanLimits(string apiKey) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/ZaiAPI.swift:74");

    // :89-151
    public static PlanLimits? Limits(JSONValue json) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/ZaiAPI.swift:89");

    // :155-176
    public static Task<PlanLimits.ResetCredits?> ResetCredits(string apiKey) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/ZaiAPI.swift:155");

    // :179-202
    public static Task<PlanLimits.ResetOutcome> ConsumeResetCredit(string apiKey, string creditID) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/ZaiAPI.swift:179");
}
