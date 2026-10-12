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

/// <summary>What Credential Manager answered for the audit key.</summary>
internal enum AuditKeyState
{
    /// <summary>A 32-byte key is held.</summary>
    Held,

    /// <summary>No such item (ItemNotFound): the key is gone.</summary>
    Missing,

    /// <summary>The item is there but is not 32 bytes: it is no key of ours.</summary>
    Damaged,
}

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

    /// <summary>
    /// Whether a usable key is held (for the Activity view's "Key missing"). Throws
    /// <see cref="AuditKeyUnavailableException"/> when Credential Manager refuses the read: that is not "missing".
    /// </summary>
    public bool HasKey()
    {
        var key = ReadKey(out _);
        if (key is null) return false;
        CryptographicOperations.ZeroMemory(key);
        return true;
    }

    /// <summary>
    /// The key; or null with <paramref name="state"/> <see cref="AuditKeyState.Missing"/> when Credential Manager holds
    /// no such item, or <see cref="AuditKeyState.Damaged"/> when the held item is not 32 bytes (it is no key of ours).
    /// Any other answer (a refused read, ERROR_NO_SUCH_LOGON_SESSION, a store not ready yet) throws
    /// <see cref="AuditKeyUnavailableException"/>: a read that failed is never taken for a key that is gone, so a
    /// moment's failure can never replace the one key that verifies the history (12.5; ED-NEW-dextra-as3-6).
    /// </summary>
    internal byte[]? ReadKey(out AuditKeyState state)
    {
        var status = secrets.CopyMatching(Service, KeyAccount, out var data);
        if (status == SecretStoreStatus.ItemNotFound)
        {
            state = AuditKeyState.Missing;
            return null;
        }
        if (status != SecretStoreStatus.Success || data is null)
            throw new AuditKeyUnavailableException($"Credential Manager did not give the audit key (status {(int)status}).");
        if (data.Length == KeyBytes)
        {
            state = AuditKeyState.Held;
            return data;
        }
        CryptographicOperations.ZeroMemory(data);
        state = AuditKeyState.Damaged;
        return null;
    }

    /// <summary>
    /// Makes and stores a new random key and returns it. For a <see cref="AuditKeyState.Missing"/> key it only adds:
    /// an item that is there after all (DuplicateItem) is never overwritten, the call fails closed. Only a
    /// <see cref="AuditKeyState.Damaged"/> item (not 32 bytes) is replaced.
    /// </summary>
    internal byte[] CreateKey(AuditKeyState state)
    {
        if (state == AuditKeyState.Held) throw new InvalidOperationException("A key is held; it is never replaced.");
        var key = RandomNumberGenerator.GetBytes(KeyBytes);
        var status = state == AuditKeyState.Damaged ? secrets.Update(Service, KeyAccount, key) : secrets.Add(Service, KeyAccount, key);
        if (state == AuditKeyState.Damaged && status == SecretStoreStatus.ItemNotFound) status = secrets.Add(Service, KeyAccount, key);
        if (status != SecretStoreStatus.Success)
        {
            CryptographicOperations.ZeroMemory(key);
            throw new AuditKeyUnavailableException($"Credential Manager did not keep the audit key (status {(int)status}).");
        }
        return key;
    }

    /// <summary>
    /// The head anchor, or null when none is held or the held one is not a well-formed anchor. A refused read throws
    /// <see cref="AuditKeyUnavailableException"/>, so the writer never opens without the evidence of a cut log.
    /// </summary>
    public AuditAnchor? ReadAnchor()
    {
        var status = secrets.CopyMatching(Service, AnchorAccount, out var data);
        if (status == SecretStoreStatus.ItemNotFound) return null;
        if (status != SecretStoreStatus.Success || data is null)
            throw new AuditKeyUnavailableException($"Credential Manager did not give the audit anchor (status {(int)status}).");
        if (AssistantJson.ParseObject(Encoding.UTF8.GetString(data)) is not { } node || AuditLine.Long(node, "seq") is not { } sequence
            || AuditLine.Text(node, "hmac") is not { } hmac || AuditLine.Text(node, "file") is not { } file) return null;
        return new AuditAnchor(sequence, hmac, file);
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
