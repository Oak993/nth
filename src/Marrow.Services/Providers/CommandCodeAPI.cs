using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;

// Source: DroppyCode/Services/Providers/CommandCodeAPI.swift (B2 1fbf81e), :1-458. Skeleton: the URLs (:13-14) and
// the StoredLogin shape (:26-29) are complete; the private planNames table and fetch (:132-141, :168-175), the seed
// catalog (:349-359), the approval mod text (:372-437) and every member with behaviour are left to the
// implementing unit.

namespace Marrow.Services.Providers;

// :12-458
public static class CommandCodeAPI
{
    public static readonly Uri BaseURL = new("https://api.commandcode.ai");      // :13
    public static readonly Uri BillingURL = new("https://commandcode.ai/billing"); // :14

    // :19-24
    public static string? ApiKey(IReadOnlyDictionary<string, string> environment) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/CommandCodeAPI.swift:19");

    // :26-29
    public sealed record StoredLogin(string ApiKey, string? UserName = null);

    // :33-39. Swift: storedLogin(environment:) (R-NAME-3: named like the nested type StoredLogin).
    public static StoredLogin? CommandCodeAPIStoredLogin(IReadOnlyDictionary<string, string> environment) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/CommandCodeAPI.swift:33");

    // :43-48
    public static JSONValue? UserConfig(IReadOnlyDictionary<string, string> environment) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/CommandCodeAPI.swift:43");

    // :52-69
    public static Task<ProviderStatus.Auth> AuthStatus(SwiftURL executable, IReadOnlyDictionary<string, string> environment) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/CommandCodeAPI.swift:52");

    // :78-101
    public static Task<PlanLimits?> PlanLimits(IReadOnlyDictionary<string, string> environment) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/CommandCodeAPI.swift:78");

    // :106-109. Swift: credits(environment:).
    public static Task<ProviderCredits?> Credits(IReadOnlyDictionary<string, string> environment) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/CommandCodeAPI.swift:106");

    // :111-125. Swift: credits(apiKey:).
    public static Task<ProviderCredits?> Credits(string apiKey) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/CommandCodeAPI.swift:111");

    // :147-166
    public static string? PlanName(string? planID) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/CommandCodeAPI.swift:147");

    // :183-192
    public static Task<ValueList<ModelOption>> ListModels(SwiftURL executable, IReadOnlyDictionary<string, string> environment) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/CommandCodeAPI.swift:183");

    // :194-234
    public static ValueList<ModelOption> ParseModels(string text, string? configuredModel, JSONValue? configuredEfforts) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/CommandCodeAPI.swift:194");

    // :237-244
    public static bool Matches(string id, string query) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/CommandCodeAPI.swift:237");

    // :249-279
    public static string DisplayName(string id) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/CommandCodeAPI.swift:249");

    // :283-310
    public static ValueList<string> Efforts(string id) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/CommandCodeAPI.swift:283");

    // :313-345
    public static long? ContextWindow(string? id) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/CommandCodeAPI.swift:313");

    // :349-359. A static let built with efforts(for:).
    public static ValueList<ModelOption> Seed =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/CommandCodeAPI.swift:349");

    // :372-437. A static let string; the byte-for-byte text is the implementing unit's (R-STR-9).
    public static string ApprovalMod =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/CommandCodeAPI.swift:372");

    // :440-444
    public static SwiftURL SupportDirectory =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/CommandCodeAPI.swift:440");

    // :448-457
    public static SwiftURL InstallMod() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/CommandCodeAPI.swift:448");
}
