using Marrow.Core.Models;
using Marrow.Core.Port;

// Source: DroppyCode/Services/Providers/ProviderCredits.swift (B2 1fbf81e), :1-76.

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

    // :20-21. "$12.34": the amount with whatever currency the provider reported.
    public string AmountText => Money(Total, Currency);

    // :23-30. "Topped up $10.00 · Granted $2.34", or null when the provider reports one number.
    public string? BreakdownText
    {
        get
        {
            var parts = new List<string>();
            if (Monthly is { } monthly && monthly > 0) parts.Add($"Monthly {Money(monthly, Currency)}");     // :26
            if (ToppedUp is { } toppedUp && toppedUp > 0) parts.Add($"Topped up {Money(toppedUp, Currency)}"); // :27
            if (Granted is { } granted && granted > 0) parts.Add($"Granted {Money(granted, Currency)}");       // :28
            return parts.Count == 0 ? null : string.Join(" · ", parts); // :29 U+00B7 MIDDLE DOT
        }
    }

    // :32
    public bool IsDepleted => !IsUsable || Total <= 0;

    // :34-45. Money wears the currency the provider reported, pinned to English formatting: the Mac's own locale
    // would read "US$ 0,43" where DeepSeek's dashboard, and the rest of this app, write "$0.43". An unknown or
    // missing code falls back to a plain number so the panel never shows a wrong symbol.
    public static string Money(double value, string currency)
    {
        var code = SwiftString.Uppercased(currency); // :39
        // :40. code.count == 3, code.allSatisfy(\.isLetter)
        if (SwiftString.Count(code) != 3 || !SwiftString.Characters(code).All(SwiftCharacter.IsLetter))
        {
            var number = Plain(value); // :41
            return currency.Length == 0 ? number : $"{number} {currency}"; // :42-43
        }
        return SwiftFormatters.Currency(value, code, "en_US"); // :44
    }

    // :47-49
    private static string Plain(double value) => SwiftFormatters.FractionLength(value, 2, "en_US");
}

// :52-76. A @MainActor enum of static members -> static class (R-ISO-1: callers are on main).
public static class CreditsReader
{
    // :58
    public static bool ExposesCredits(ProviderKind provider) =>
        provider == ProviderKind.Deepseek || provider == ProviderKind.Commandcode;

    // :60-66
    public static Task<ProviderCredits?> Read(ProviderKind provider, string apiKey) => provider switch
    {
        ProviderKind.Deepseek => DeepSeekAPI.Balance(apiKey),      // :62
        ProviderKind.Commandcode => CommandCodeAPI.Credits(apiKey), // :63
        _ => Task.FromResult<ProviderCredits?>(null),               // :64
    };

    // :69-75. Where the provider's dashboard tops the balance up.
    public static Uri? TopUpURL(ProviderKind provider) => provider switch
    {
        ProviderKind.Deepseek => new Uri("https://platform.deepseek.com/top_up"), // :71
        ProviderKind.Commandcode => CommandCodeAPI.BillingURL,                    // :72
        _ => null,                                                                // :73
    };
}
