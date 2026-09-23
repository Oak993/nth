namespace Marrow.Services.Providers;

// Source: DroppyCode/Services/Providers/MetaKeychain.swift (B2 1fbf81e), :1-16.
//
// DEVIATION OD-0008 B3: the key goes through APIKeychain to Windows Credential Manager (one generic credential under
// the one service name the port uses in place of AppInfo.name) instead of the macOS Keychain; the account string is
// kept verbatim (PR-PROVAPI-006).

/// <summary>
/// Stores the Meta Model API key (MODEL_API_KEY) in the macOS Keychain so it never sits in plaintext. UserDefaults
/// keeps a copy only where the Keychain refuses it (:3-4).
/// </summary>
// :5. Public because App (AppSettings.swift:445) reads it.
public static class MetaKeychain
{
    // :6
    private const string Account = "Meta API Key";

    /// <summary>The stored Meta key, or <paramref name="fallback"/> when nothing is stored.</summary>
    // :8-10
    public static string ApiKey(string fallback) =>
        APIKeychain.Key(@for: Account, fallback: fallback);

    /// <summary>Stores the Meta key (an empty one removes it); returns whether the Keychain took it.</summary>
    // :12-15. @discardableResult has no C# counterpart; callers may ignore the result.
    public static bool SetAPIKey(string key) =>
        APIKeychain.SetKey(key, account: Account);
}
