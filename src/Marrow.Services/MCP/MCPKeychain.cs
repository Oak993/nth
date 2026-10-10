using System.Text;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Port;

// Source: DroppyCode/Services/MCP/MCPKeychain.swift (B2 1fbf81e), :1-68.
//
// DEVIATION OD-0008 B3: the SecItem calls on the macOS Keychain (:13-19, :37-49, :53-58) go to Windows Credential
// Manager through the secret store (R-SEC-1, ED-093, ED-120), SecretStore.Shared.Current, with the same service
// (AppInfo.name, :15/:39/:55) and the same account strings (:7-9). kSecClassGenericPassword is the store's generic
// credential; kSecAttrAccessibleWhenUnlocked (:47) has no Credential Manager counterpart and is not carried
// (PR-MCP-098 windows_path; upstream defect 21 has no Windows analogue).
//
// Not ported: the capture-run guard of :12 (guard !CaptureRun.isEnabled else { return nil }). CaptureRun is not
// ported (ED-001) and the guard is always false in a user launch (dead_code.md entry 44), so Value runs the
// non-capture branch only.

namespace Marrow.Services.MCP;

/// <summary>
/// Stores MCP secret field values in Windows Credential Manager so they never sit in plaintext. mcp.json keeps only
/// the non-secret values. (:4-6)
/// </summary>
public static class MCPKeychain
{
    // :7-9. No escaping: a server id or field key holding a space gives an ambiguous account (PR-MCP-095).
    private static string Account(string server, string field) => "MCP " + server + " " + field;

    // :15, :39, :55 kSecAttrService as String: AppInfo.name
    private static string Service => DevDataRoot.CredentialService(AppInfo.Current.Name); // ED-212

    // OD-0135: the service before the product was named Nth ("Marrow" / "Marrow Dev"), read only to carry a value over once.
    private static string LegacyService => DevDataRoot.CredentialService(AppInfo.LegacyNameFor(AppInfo.Current.IsDevelopment));

    // :11-26
    public static string? Value(string server, string field)
    {
        // :12 not ported: CaptureRun.isEnabled is always false (ED-001, dead_code.md entry 44).
        // :13-24 SecItemCopyMatching with kSecReturnData and kSecMatchLimitOne; String(data:encoding: .utf8).
        if (SecretStore.Shared.Current.CopyMatchingCarryingOver(Service, LegacyService, Account(server, field), out var data) // OD-0135
                != SecretStoreStatus.Success
            || data is null
            || SwiftText.DecodeUtf8OrNil(data) is not { } value
            || value.Trimmed().Length == 0)
        {
            return null;
        }
        // :25
        return value.Trimmed();
    }

    /// <summary>Stores the value, or removes it for an empty one. (:28)</summary>
    // :29-50. Swift: @discardableResult.
    public static bool Set(string value, string server, string field)
    {
        var store = SecretStore.Shared.Current;
        store.SupersedeLegacy(Service, LegacyService, Account(server, field)); // OD-0135
        // :31-35
        var trimmed = value.Trimmed();
        if (trimmed.Length == 0)
        {
            Delete(server: server, field: field);
            return true;
        }
        // :36
        var data = Encoding.UTF8.GetBytes(trimmed);
        // :37-44 SecItemUpdate of kSecValueData only.
        var updated = store.Update(Service, Account(server, field), data);
        if (updated == SecretStoreStatus.Success) return true;
        if (updated != SecretStoreStatus.ItemNotFound) return false;
        // :45-49 SecItemAdd with the data. kSecAttrAccessibleWhenUnlocked (:47) has no Credential Manager
        // counterpart (OD-0008 B3, PR-MCP-098).
        return store.Add(Service, Account(server, field), data) == SecretStoreStatus.Success;
    }

    // :52-59
    // DEVIATION OD-0008 A12 (PR-MCP-099, PR-MCP-228): upstream discards the SecItemDelete status (:58); the port
    // checks it and returns false when the store refused the deletion. An item that did not exist is not a failure
    // (ENGINEERING DECISION in the records' deviation), so Success and ItemNotFound both return true.
    public static bool Delete(string server, string field)
    {
        SecretStore.Shared.Current.SupersedeLegacy(Service, LegacyService, Account(server, field)); // OD-0135
        var status = SecretStore.Shared.Current.Delete(Service, Account(server, field));
        return status == SecretStoreStatus.Success || status == SecretStoreStatus.ItemNotFound;
    }

    /// <summary>
    /// Forgets every secret the entry declares. The entry, not its id: a custom server is not in the catalog, so
    /// looking it up there would silently delete nothing. (:61-62)
    /// </summary>
    // :63-67
    // DEVIATION OD-0008 A12 (PR-MCP-099, PR-MCP-228): returns false when any deletion was refused. Every declared
    // field is still attempted, in field order, because the Swift loop has no early exit (:64-66).
    public static bool DeleteAll(MCPCatalogEntry server)
    {
        var entry = server;
        var allRemoved = true;
        foreach (var field in entry.Fields)
        {
            if (!Delete(server: entry.Id, field: field.Key)) allRemoved = false;
        }
        return allRemoved;
    }
}
