namespace Marrow.Services.Providers;

// Source: DroppyCode/Services/Providers/ZaiKeychain.swift (B2 1fbf81e), :1-16.
//
// DEVIATION OD-0008 B3: the key goes through APIKeychain to Windows Credential Manager (one generic credential under
// the one service name the port uses in place of AppInfo.name) instead of the macOS Keychain; the account string is
// kept verbatim (PR-PROVAPI-007).

/// <summary>
/// Stores the Z.ai API key in the macOS Keychain so it never sits in plaintext. UserDefaults keeps a copy only where
/// the Keychain refuses it (:3-4).
/// </summary>
// :5. Public because App (AppSettings.swift:394, :446) uses it.
public static class ZaiKeychain
{
    // :6
    private const string Account = "Z.ai API Key";

    /// <summary>The stored Z.ai key, or <paramref name="fallback"/> when nothing is stored.</summary>
    // :8-10
    public static string ApiKey(string fallback) =>
        APIKeychain.Key(@for: Account, fallback: fallback);

    /// <summary>Stores the Z.ai key (an empty one removes it); returns whether the Keychain took it.</summary>
    // :12-15. @discardableResult has no C# counterpart; callers may ignore the result.
    public static bool SetAPIKey(string key) =>
        APIKeychain.SetKey(key, account: Account);
}
