using Marrow.Core.Models;
using Marrow.Core.Port.Accounts;
using Marrow.Core.Support;
using Marrow.Services.Port;

namespace Marrow.Services.Providers;

// Source: DroppyCode/Services/Providers/APIKeychain.swift (B3 52bc792), :1-84.
// Line 12 (`guard !CaptureRun.isEnabled else { return "" }`) is not ported: CaptureRun is not ported (ED-001) and the
// guard is dead-code register entry 44 (porting/dead_code.md), excluded by ED-061; in every reachable launch it is
// false, so the live path below is the whole behaviour.
//
// DEVIATION OD-0008 B3: the SecItem calls on kSecClassGenericPassword (:45-49, :60-67, :77-81) go to Windows
// Credential Manager through the secret store (src/Marrow.Services/Port/SecretStore.cs, ED-093, ED-120), one generic
// credential per account string under the one service name the port uses in place of AppInfo.name (AppInfo.Current
// .Name). kSecAttrAccessibleWhenUnlocked (:55) has no Windows analogue (port_impacts.md entry 262).
// OD-0008 A6: SetKey stores through SecretStore.SetUpdating (update in place, add only on ItemNotFound; PR-PROVAPI-
// 003), not SetReplacing (delete then add); a refused write keeps the previously stored key and nothing else is
// written anywhere (AppSettings.cs's StoreAPIKeyChange keeps only the clearing half of the legacy fallback).
//
// NEW at B3 (impact R3-48 APIKeychain #1, PR-PROVAPI-192): the per-provider DeepSeekKeychain, MetaKeychain,
// ZaiKeychain and CommandCodeKeychain wrapper files are deleted; their account strings move verbatim into the
// private Account(ProviderKind) table below, and Key/SetKey now take a ProviderKind instead of a caller-supplied
// account string.

/// <summary>
/// One Keychain implementation for every native provider's API key (:4-5): the accounts live in one service
/// (AppInfo.name), one item per provider.
/// </summary>
// Public because the tests live in a separate assembly; the caller is App.AppSettings (AppSettings.cs).
public static class APIKeychain
{
    /// <summary>
    /// The stored key for a provider, or <paramref name="fallback"/> when the provider has no account (PR-PROVAPI-
    /// 192) or nothing is stored for it.
    /// </summary>
    // :7-15
    public static string Key(ProviderKind @for, string fallback)
    {
        // :11
        if (Account(@for) is not { } account) return fallback;
        // :12 not ported (dead_code.md entry 44, ED-001, ED-061).
        // :13
        if (Read(account) is { } keychain && keychain.Length != 0) return keychain;
        // :14
        return fallback;
    }

    /// <summary>
    /// Stores the key for a provider, or removes it for an empty one. Returns false without writing, deleting or
    /// trimming anything when the provider has no account (PR-PROVAPI-192).
    /// </summary>
    // :17-23. Swift: @discardableResult, which has no C# counterpart; callers may ignore the result.
    public static bool SetKey(string key, ProviderKind @for)
    {
        // :21
        if (Account(@for) is not { } account) return false;
        // :22
        return Set(key, account);
    }

    // PORT-ONLY: OD-0114 / ED-404. Migrated identities keep the old protected target in place.
    public static string Key(Marrow.Core.Port.Accounts.Account account, string fallback) =>
        CredentialAccount(account) is { } target ? Read(target) ?? fallback : fallback;

    public static bool SetKey(string key, Marrow.Core.Port.Accounts.Account account) =>
        CredentialAccount(account) is { } target && Set(key, target);

    private static string? CredentialAccount(Marrow.Core.Port.Accounts.Account account) => Account(account.Service) is { } legacy
        ? account.UsesLegacyCredentials ? legacy : legacy + " / " + account.Id.ToString("N")
        : null;

    /// <summary>PORT-ONLY (MA-6): the account a provider's one key is stored under, for the extra keys' account names. Null for a provider with no account.</summary>
    internal static string? PrimaryAccount(ProviderKind provider) => Account(provider);

    /// <summary>The accounts older builds stored keys under; changing one loses that key.</summary>
    // :25-34
    private static string? Account(ProviderKind provider) => provider switch
    {
        ProviderKind.Deepseek => "DeepSeek API Key",        // :28
        ProviderKind.Meta => "Meta API Key",                // :29
        ProviderKind.Zai => "Z.ai API Key",                 // :30
        ProviderKind.Commandcode => "Command Code API Key", // :31
        ProviderKind.OpenRouter => "OpenRouter API Key",     // PORT-ONLY (ED-234): the one account of a provider the source lacks
        _ => null,                                          // :32
    };

    /// <summary>Stores the value, or removes it for an empty one.</summary>
    // :36-58. SetUpdating is the transcription of this body over the secret store: trim (Text.swift:132); an empty
    // trimmed value deletes (:39-43) and returns true; otherwise the UTF-8 bytes (:44) update the item in place
    // (:50-52), and only when no item exists (errSecItemNotFound, :52) is one added (:53-57).
    public static bool Set(string key, string account, string? service = null)
    {
        if (service is null) SecretStore.Shared.Current.SupersedeLegacy(Service, LegacyService, account); // OD-0135
        return SecretStore.Shared.Current.SetUpdating(service ?? Service, account, key);
    }

    /// <summary>
    /// SecItemCopyMatching with kSecReturnData and kSecMatchLimitOne; nil unless the status is success, the bytes
    /// decode as UTF-8 and the trimmed value is not empty; otherwise the trimmed value.
    /// </summary>
    // :60-74
    public static string? Read(string account, string? service = null) =>
        service is null
            ? SecretStore.Shared.Current.ReadCarryingOver(Service, LegacyService, account) // OD-0135: a key kept under "Marrow" is carried over once
            : SecretStore.Shared.Current.Read(service, account);

    /// <summary>SecItemDelete; the status is ignored, as upstream ignores it.</summary>
    // :76-83
    public static void Delete(string account, string? service = null)
    {
        if (service is null) SecretStore.Shared.Current.SupersedeLegacy(Service, LegacyService, account); // OD-0135
        SecretStore.Shared.Current.Delete(service ?? Service, account);
    }

    // The service is AppInfo.name ("nth" / "nth Dev" under OD-0135); LegacyService is the name before it ("Marrow" /
    // "Marrow Dev"), read only to carry a key over once (SecretStore.CopyMatchingCarryingOver). ED-212 applies to both.
    private static string Service => DevDataRoot.CredentialService(AppInfo.Current.Name);
    private static string LegacyService => DevDataRoot.CredentialService(AppInfo.LegacyNameFor(AppInfo.Current.IsDevelopment));
}
