// PORT-ONLY: Dextra, nth's assistant (DESIGN.md revision 5, section 12.5; unit AS-3). No Swift counterpart.
// ED-NEW-dextra-as3-3: the audit key and the head anchor live in Windows Credential Manager.
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Marrow.Services.Port;

namespace Marrow.Services.Assistant;

/// <summary>The chain's head as last copied into Credential Manager: the line number, its HMAC and its day file.</summary>
public sealed record AuditAnchor(long Sequence, string Hmac, string File);

/// <summary>Credential Manager refused to give or keep the audit key: the log cannot be written (fail closed).</summary>
public sealed class AuditKeyUnavailableException(string message) : InvalidOperationException(message);

/// <summary>
/// The audit log's two Credential Manager items (12.5), through the app's <see cref="SecretStore"/> (ED-093): the
/// 256-bit HMAC key (CRED_TYPE_GENERIC, encrypted per user by Windows' DPAPI; never written to disk in the clear) and
/// the head anchor (the latest line's number and HMAC, copied after each write batch). The key has no public getter:
/// only <see cref="AuditLog"/> and <see cref="AuditVerifier"/> in this assembly read it, so no model and no view can be
/// handed it.
/// </summary>
public sealed class AuditKeyStore(SecretStore secrets, string service = AuditKeyStore.DefaultService)
{
    public const string DefaultService = "nth.dextra.audit";
    public const int KeyBytes = 32;
    private const string KeyAccount = "hmac-key";
    private const string AnchorAccount = "head";

    public string Service { get; } = service;

    /// <summary>The production store: the real Credential Manager.</summary>
    public static AuditKeyStore Production() => new(SecretStore.Shared.Current);

    /// <summary>Whether a usable key is held (for the Activity view's "Key missing").</summary>
    public bool HasKey()
    {
        var key = ReadKey();
        if (key is null) return false;
        CryptographicOperations.ZeroMemory(key);
        return true;
    }

    /// <summary>The key, or null when none is held or the held one is not 32 bytes.</summary>
    internal byte[]? ReadKey()
    {
        if (secrets.CopyMatching(Service, KeyAccount, out var data) != SecretStoreStatus.Success || data is null) return null;
        if (data.Length == KeyBytes) return data;
        CryptographicOperations.ZeroMemory(data);
        return null;
    }

    /// <summary>Makes and stores a new random key, replacing any held one, and returns it.</summary>
    internal byte[] CreateKey()
    {
        var key = RandomNumberGenerator.GetBytes(KeyBytes);
        var status = secrets.Update(Service, KeyAccount, key);
        if (status == SecretStoreStatus.ItemNotFound) status = secrets.Add(Service, KeyAccount, key);
        if (status != SecretStoreStatus.Success)
        {
            CryptographicOperations.ZeroMemory(key);
            throw new AuditKeyUnavailableException($"Credential Manager did not keep the audit key (status {(int)status}).");
        }
        return key;
    }

    public AuditAnchor? ReadAnchor()
    {
        if (secrets.CopyMatching(Service, AnchorAccount, out var data) != SecretStoreStatus.Success || data is null) return null;
        try
        {
            var node = JsonNode.Parse(Encoding.UTF8.GetString(data)) as JsonObject;
            if (node?["seq"]?.GetValue<long>() is not { } sequence || node["hmac"]?.GetValue<string>() is not { } hmac
                || node["file"]?.GetValue<string>() is not { } file) return null;
            return new AuditAnchor(sequence, hmac, file);
        }
        catch (Exception error) when (error is System.Text.Json.JsonException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    internal void WriteAnchor(AuditAnchor anchor)
    {
        var text = new JsonObject
        {
            ["seq"] = anchor.Sequence,
            ["hmac"] = anchor.Hmac,
            ["file"] = anchor.File,
        }.ToJsonString();
        var bytes = Encoding.UTF8.GetBytes(text);
        var status = secrets.Update(Service, AnchorAccount, bytes);
        if (status == SecretStoreStatus.ItemNotFound) status = secrets.Add(Service, AnchorAccount, bytes);
        if (status != SecretStoreStatus.Success)
            throw new AuditKeyUnavailableException(
                $"Credential Manager did not keep the audit anchor for line {anchor.Sequence.ToString(CultureInfo.InvariantCulture)} (status {(int)status}).");
    }

    /// <summary>Removes both items (a test's clean-up, and the Native gate's delete check).</summary>
    internal void DeleteAll()
    {
        secrets.Delete(Service, KeyAccount);
        secrets.Delete(Service, AnchorAccount);
    }
}
