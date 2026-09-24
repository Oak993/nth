using Marrow.Core.Models;

// Source: DroppyCode/Services/Providers/ProviderCredits.swift (B2 1fbf81e), :1-76. Skeleton: the stored properties
// (:7-18) are complete; the private helper plain(_:) (:47-49) and every member with behaviour are left to the
// implementing unit.

namespace Marrow.Services.Providers;

// :6-50. A Swift struct -> immutable record (R-VAL-1).
public sealed record ProviderCredits
{
    public required double Total { get; init; }     // :8
    public required string Currency { get; init; }  // :10
    public double? ToppedUp { get; init; }          // :12
    public double? Granted { get; init; }           // :14
    public double? Monthly { get; init; }           // :16
    public required bool IsUsable { get; init; }    // :18

    // :21
    public string AmountText =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/ProviderCredits.swift:21");

    // :24-30
    public string? BreakdownText =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/ProviderCredits.swift:24");

    // :32
    public bool IsDepleted =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/ProviderCredits.swift:32");

    // :38-45
    public static string Money(double value, string currency) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/ProviderCredits.swift:38");
}

// :52-76. A @MainActor enum of static members -> static class (R-ISO-1: callers are on main).
public static class CreditsReader
{
    // :58
    public static bool ExposesCredits(ProviderKind provider) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/ProviderCredits.swift:58");

    // :60-66
    public static Task<ProviderCredits?> Read(ProviderKind provider, string apiKey) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/ProviderCredits.swift:60");

    // :69-75
    public static Uri? TopUpURL(ProviderKind provider) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/ProviderCredits.swift:69");
}
