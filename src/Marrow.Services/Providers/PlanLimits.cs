using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;

// Source: DroppyCode/Services/Providers/PlanLimits.swift (B2 1fbf81e), :1-336. Skeleton: the record shapes (:4-35)
// and the error cases (:49-52) are complete; PlanLimitsReader's private members readClaude, ClaudeLogin, usageURL,
// keychainService, readClaudeUsageEndpoint, claudeLogin, parseLogin, hexDecoded, refreshClaudeLogin,
// fetchClaudeUsage and readClaudeFromCLI (:134-290) and every member with behaviour are left to the implementing
// unit.

namespace Marrow.Services.Providers;

// :4-46. A Swift struct -> immutable record (R-VAL-1).
public sealed record PlanLimits
{
    // :5-11
    public sealed record Window
    {
        public required string Id { get; init; }        // :6
        public required string Title { get; init; }     // :7
        public required double Percent { get; init; }   // :8
        public DateTimeOffset? ResetsAt { get; init; }   // :9
        public string? Detail { get; init; }             // :10
    }

    public string? PlanName { get; init; }                   // :13
    public required ValueList<Window> Windows { get; init; } // :14
    public ResetCredits? PlanLimitsResetCredits { get; init; } // :15 Swift: resetCredits (R-NAME-3)
    public string? Problem { get; init; }                    // :17

    // :20-25
    public sealed record ResetCredit
    {
        public required string Id { get; init; }         // :21
        public string? Title { get; init; }               // :22
        public string? Detail { get; init; }              // :23
        public DateTimeOffset? ExpiresAt { get; init; }   // :24
    }

    // :28-31
    public sealed record ResetCredits
    {
        public required long AvailableCount { get; init; }                // :29
        public required ValueList<ResetCredit> Credits { get; init; }     // :30
    }

    // :34-35 (R-ENUM-1)
    public enum ResetOutcome
    {
        [RawValue("reset")] Reset,
        [RawValue("nothingToReset")] NothingToReset,
        [RawValue("noCredit")] NoCredit,
        [RawValue("alreadyRedeemed")] AlreadyRedeemed,
    }
}

// :37-44. Members declared on a nested enum -> a static extension class (R-NAME-4).
public static class PlanLimitsResetOutcomeExtensions
{
    extension(PlanLimits.ResetOutcome outcome)
    {
        // :37-44
        public string Message =>
            throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/PlanLimits.swift:37");
    }
}

// :49-61. An error enum keeps its cases as a nested record union (R-ERR-3); the unlabeled String is named after
// the binding errorDescription gives it (`let message`).
public sealed class ResetCreditError(ResetCreditError.Case errorCase) : SwiftError
{
    public abstract record Case
    {
        private Case() { }
        public sealed record CodexUnavailable : Case;          // :50
        public sealed record UnknownOutcome : Case;            // :51
        public sealed record Rejected(string Message) : Case;  // :52
    }

    public Case ErrorCase { get; } = errorCase;

    // :54-60
    public override string? ErrorDescription =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/PlanLimits.swift:54");
}

// :63-336. A @MainActor enum of static members -> static class; its nonisolated members touch no main state.
public static class PlanLimitsReader
{
    // :68-70
    public static bool ExposesLimits(ProviderKind provider) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/PlanLimits.swift:68");

    // :72-82
    public static Task<PlanLimits?> Read(ProviderKind provider, SwiftURL executable, IReadOnlyDictionary<string, string> environment) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/PlanLimits.swift:72");

    // :85-90
    public static Task<PlanLimits?> Read(ProviderKind provider, string apiKey) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/PlanLimits.swift:85");

    // :92-102
    public static string WindowTitle(long? minutes) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/PlanLimits.swift:92");

    // :104-107
    public static string? PlanName(string? raw) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/PlanLimits.swift:104");

    // :113-128
    public static string? CodexPlanName(string? raw) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/PlanLimits.swift:113");

    // :294-329. `limits: [JSONValue]` takes the list JSONValue.JSONValueArray gives.
    public static PlanLimits? ParseClaudeUsage(JSONValue root, IReadOnlyList<JSONValue> limits, string? planName, string? titlePrefix = null) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/PlanLimits.swift:294");

    // :332-335
    public static DateTimeOffset? ParseDate(string text) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/PlanLimits.swift:332");
}
