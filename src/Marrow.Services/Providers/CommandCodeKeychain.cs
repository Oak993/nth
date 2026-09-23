namespace Marrow.Services.Providers;

// Source: DroppyCode/Services/Providers/CommandCodeKeychain.swift (B2 1fbf81e), :1-18.
//
// DEVIATION OD-0008 B3: the key lives in Windows Credential Manager through APIKeychain (its header gives the
// mapping), under the same account string. DEVIATION OD-0008 A6: the doc's UserDefaults copy where the Keychain
// refuses the key (:6, AppSettings.swift:405-406) is not kept; the caller shows the failure (OD-0012 E5).

/// <summary>
/// Stores an optional Command Code Studio API key in protected storage, so it never sits in plaintext. The CLI's own
/// `cmd login` keeps its key in `~/.commandcode/auth.json`; this one is for a machine where the user would rather
/// paste a key than sign in through the browser (:3-6).
/// </summary>
// :7. Public because the tests live in a separate assembly and the caller (AppSettings.swift:399-408, :447) is in App.
public static class CommandCodeKeychain
{
    // :8
    private const string Account = "Command Code API Key";

    /// <summary>The stored key, or <paramref name="fallback"/> when nothing is stored.</summary>
    // :10-12
    public static string ApiKey(string fallback) => APIKeychain.Key(@for: Account, fallback: fallback);

    /// <summary>Stores the key, or removes it for an empty one; returns whether the store took it.</summary>
    // :14-17. @discardableResult: C# callers may ignore the result.
    public static bool SetAPIKey(string key) => APIKeychain.SetKey(key, account: Account);
}
