using Marrow.Core.Support;
using Marrow.Services.Port;

namespace Marrow.Services.Providers;

// Source: DroppyCode/Services/Providers/APIKeychain.swift (B2 1fbf81e), :1-66.
// Line 14 (`guard !CaptureRun.isEnabled else { return "" }`) is not ported: CaptureRun is not ported (ED-001) and the
// guard is dead-code register entry 44 (porting/dead_code.md), excluded by ED-061; in every reachable launch it is
// false, so the live path below is the whole behaviour.
//
// DEVIATION OD-0008 B3: the SecItem calls on kSecClassGenericPassword (:29-33, :43-49, :59-63) go to Windows
// Credential Manager through the secret store (src/Marrow.Services/Port/SecretStore.cs, ED-093, ED-120), one generic
// credential per account string under the one service name the port uses in place of AppInfo.name (AppInfo.Current
// .Name). kSecAttrAccessibleWhenUnlocked (:37) has no Windows analogue (port_impacts.md entry 262).
// OD-0008 A6: a refused add returns false and nothing else is written here.

/// <summary>
/// One Keychain implementation for every native provider's API key (:4-9): the accounts live in one service
/// (AppInfo.name), one item per provider.
/// </summary>
// Public because the tests live in a separate assembly; upstream callers are the four provider helpers in Services
// (DeepSeekKeychain.swift:9, :14; MetaKeychain.swift:9, :14; ZaiKeychain.swift:9, :14; CommandCodeKeychain.swift:11, :16).
public static class APIKeychain
{
    /// <summary>The stored key for an account, or <paramref name="fallback"/> when nothing is stored.</summary>
    // :13-17
    public static string Key(string @for, string fallback)
    {
        // :14 not ported (dead_code.md entry 44, ED-001, ED-061).
        // :15
        if (Read(@for) is { } keychain && keychain.Length != 0) return keychain;
        // :16
        return fallback;
    }

    /// <summary>
    /// Stores the key, or removes it for an empty one. Returns whether the Keychain took it, so the caller knows
    /// whether a fallback copy is still needed.
    /// </summary>
    // :21-40. SetReplacing is the transcription of this body over the secret store: trim (:23, Text.swift:132); an
    // empty trimmed value deletes (:24-27, :58-65) and returns true; otherwise the UTF-8 bytes (:28) replace the item
    // by delete (:34) then add (:35-38), returning whether the add succeeded (:39).
    public static bool SetKey(string key, string account) =>
        SecretStore.Shared.Current.SetReplacing(AppInfo.Current.Name, account, key);

    // :42-56. SecItemCopyMatching with kSecReturnData and kSecMatchLimitOne (:43-51); nil unless the status is
    // success, the bytes decode as UTF-8 (:52-53) and the trimmed value is not empty (:54); the trimmed value (:55).
    private static string? Read(string account) =>
        SecretStore.Shared.Current.Read(AppInfo.Current.Name, account);
}
