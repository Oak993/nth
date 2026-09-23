namespace Marrow.Services.Providers;

// Source: DroppyCode/Services/Providers/DeepSeekKeychain.swift (B2 1fbf81e), :1-16.
//
// DEVIATION OD-0008 B3: the key goes to Windows Credential Manager through APIKeychain (APIKeychain.cs), one generic
// credential under the one service name the port uses in place of AppInfo.name, with the same account string.
// OD-0008 A6: no UserDefaults copy is written where the store refuses (the doc comment at :4 describes upstream).

/// <summary>
/// Stores the DeepSeek API key in the macOS Keychain so it never sits in plaintext. UserDefaults keeps a copy only
/// where the Keychain refuses it (:3-4).
/// </summary>
// :5. Public because the tests live in a separate assembly.
public static class DeepSeekKeychain
{
    // :6
    private const string Account = "DeepSeek API Key";

    /// <summary>The stored DeepSeek key, or <paramref name="fallback"/> when nothing is stored.</summary>
    // :8-10
    public static string ApiKey(string fallback) =>
        APIKeychain.Key(@for: Account, fallback: fallback);

    /// <summary>Stores the DeepSeek key, or removes it for an empty one; returns whether the store took it.</summary>
    // :12-15. @discardableResult has no C# counterpart; callers may ignore the Boolean.
    public static bool SetAPIKey(string key) =>
        APIKeychain.SetKey(key, account: Account);
}
