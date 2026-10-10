// PORT-ONLY: the Keychain SecItem calls have no Windows twin; this is the secret store in Windows Credential Manager
// (ADR-0002 deviation 8, OD-0008 B3, R-SEC-1), encoded per ED-093 with its chunk paragraph amended by ED-120.
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Marrow.Core.Port;

namespace Marrow.Services.Port;

// Source: DroppyCode/Services/Providers/APIKeychain.swift (B2 1fbf81e), :13-65.
// Source: DroppyCode/Services/MCP/MCPKeychain.swift (B2 1fbf81e), :11-59.
// Source: DroppyCode/Core/Support/Text.swift (B2 1fbf81e), :132.
//
// The store stands in for the SecItem calls of the two Keychain helpers: SecItemAdd, SecItemUpdate,
// SecItemCopyMatching and SecItemDelete on kSecClassGenericPassword with kSecAttrService and kSecAttrAccount
// (APIKeychain.swift:29-33, :43-49, :59-63; MCPKeychain.swift:13-19, :37-41, :53-57). The service is passed in, so
// this unit needs no AppInfo. The capture-run guards (APIKeychain.swift:14, MCPKeychain.swift:12) belong to the
// callers and are always false on Windows (dead_code.md entry 44, ED-001); they are not part of the store.
//
// ED-093: one CRED_TYPE_GENERIC, CRED_PERSIST_LOCAL_MACHINE credential per item, UserName the account verbatim,
// TargetName `<service>:<account>:<16 lowercase hex digits of SHA-256 of the account's UTF-8 bytes>`; the SecItem
// status contract (DuplicateItem on add of an existing item, ItemNotFound on update, copy or delete of a missing one);
// the Win32 calls behind a backend seam.
// ED-120 (amends ED-093's chunk paragraph): a value of at most 2560 bytes is one bare primary with no attributes; a
// longer value keeps chunk 1 in the primary with exactly one attribute, keyword Marrow_Chunks, value the ASCII bytes
// of "<count>/<slot>" (decimal, no leading zeros, count >= 2, slot 1 or 2), and continuations `<target>#<slot>.<n>`
// for n = 2..count. A chunked write goes to the slot the live primary does not reference, continuations first and the
// primary last, so a reader sees the old value or the new one, as with SecItemUpdate (MCPKeychain.swift:42-44).
// OD-0008 A6: fail closed, store nothing; there is no plain-text fallback here.

/// <summary>
/// The result of a secret store call, in the shape of the SecItem OSStatus it stands in for. The three named values
/// carry the OSStatus numbers (errSecSuccess 0, errSecDuplicateItem -25299, errSecItemNotFound -25300). Any other
/// value is a refusal and is the Win32 error the backend returned, unchanged (ED-120 (3)); a Win32 error is never
/// one of the named values, so a refusal is never read as ItemNotFound (MCPKeychain.swift:44).
/// </summary>
public enum SecretStoreStatus
{
    Success = 0,
    DuplicateItem = -25299,
    ItemNotFound = -25300,
}

/// <summary>A CREDENTIAL_ATTRIBUTEW with Flags 0 (ED-120 (1)).</summary>
public sealed record CredentialAttribute(string Keyword, byte[] Value);

/// <summary>
/// The fields of a CREDENTIALW the store uses (ED-093): TargetName, UserName, CredentialBlob, Type, Persist and
/// Attributes. Comment, TargetAlias and Flags are left empty.
/// </summary>
public sealed record GenericCredential(
    string TargetName,
    string UserName,
    byte[] Blob,
    int Type,
    int Persist,
    IReadOnlyList<CredentialAttribute> Attributes);

/// <summary>
/// The backend seam of ED-093: CredWriteW, CredReadW and CredDeleteW for CRED_TYPE_GENERIC. Each returns 0 on success
/// or the Win32 error (1168 ERROR_NOT_FOUND for a missing target). Target names compare without case, as in
/// Credential Manager. ED-120 (6): the seam has no enumeration.
/// </summary>
public interface ICredentialBackend
{
    /// <summary>CredWriteW: creates the credential or replaces the one with the same target name.</summary>
    int Write(GenericCredential credential);

    /// <summary>CredReadW for CRED_TYPE_GENERIC.</summary>
    int Read(string targetName, out GenericCredential? credential);

    /// <summary>CredDeleteW for CRED_TYPE_GENERIC.</summary>
    int Delete(string targetName);
}

/// <summary>
/// The Keychain helpers' SecItem calls over Windows Credential Manager (ED-093, ED-120). Every operation runs under
/// one process-wide lock (ED-120 (6)); the app's single-instance lock (ED-002) means no other Marrow process writes
/// these credentials.
/// </summary>
public sealed class SecretStore
{
    /// <summary>CRED_MAX_CREDENTIAL_BLOB_SIZE, probed: 2560 accepted, 2561 refused with 1783 (ED-093).</summary>
    public const int ChunkBytes = 2560;

    /// <summary>ED-120 (1): the one attribute of a chunked primary. A persisted format; never changes.</summary>
    public const string ChunksKeyword = "Marrow_Chunks";

    private const int CredTypeGeneric = 1; // wincred.h CRED_TYPE_GENERIC (ED-093)
    private const int CredPersistLocalMachine = 2; // wincred.h CRED_PERSIST_LOCAL_MACHINE (ED-093)
    private const int ErrorSuccess = 0;
    private const int ErrorNotFound = 1168; // ERROR_NOT_FOUND

    // ED-120 (6): one process-wide lock for every operation. C# `lock` is reentrant on the owning thread, which the
    // helpers (delete then add, update then add) and a test reading from inside a backend hook rely on.
    private static readonly object Gate = new();

    private static readonly UTF8Encoding StrictUTF8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public SecretStore(ICredentialBackend backend) => Backend = backend;

    public ICredentialBackend Backend { get; }

    /// <summary>
    /// The store the Keychain helpers reach as a static (R-LIFE-2): the real Credential Manager, created on first use.
    /// A test replaces it for its own async flow.
    /// </summary>
    public static Ambient<SecretStore> Shared { get; } = new(() => new SecretStore(new CredentialManagerBackend()));

    /// <summary>ED-093: `<service>:<account>:<h>`, h the first 16 lowercase hex digits of SHA-256(UTF-8 account).</summary>
    public static string TargetName(string service, string account)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(account));
        return service + ":" + account + ":" + Convert.ToHexStringLower(digest)[..16];
    }

    // ---- The helper forms (R-SEC-1) ----

    /// <summary>
    /// APIKeychain.setKey: trims; an empty value deletes the item and reports true; otherwise deletes, then adds, and
    /// reports whether the add succeeded (so after a refused add the account holds nothing).
    /// </summary>
    public bool SetReplacing(string service, string account, string value)
    {
        lock (Gate)
        {
            // APIKeychain.swift:23-27
            var trimmed = Trimmed(value);
            if (trimmed.Length == 0)
            {
                Delete(service, account);
                return true;
            }
            // :28
            var data = Encoding.UTF8.GetBytes(trimmed);
            // :34
            Delete(service, account);
            // :39
            return Add(service, account, data) == SecretStoreStatus.Success;
        }
    }

    /// <summary>
    /// MCPKeychain.set: trims; an empty value deletes the item and reports true; otherwise updates, reports true on
    /// success and false on any status but ItemNotFound, and adds on ItemNotFound.
    /// </summary>
    public bool SetUpdating(string service, string account, string value)
    {
        lock (Gate)
        {
            // MCPKeychain.swift:31-35
            var trimmed = Trimmed(value);
            if (trimmed.Length == 0)
            {
                Delete(service, account);
                return true;
            }
            // :36
            var data = Encoding.UTF8.GetBytes(trimmed);
            // :42-44
            var updated = Update(service, account, data);
            if (updated == SecretStoreStatus.Success) return true;
            if (updated != SecretStoreStatus.ItemNotFound) return false;
            // :45-49
            return Add(service, account, data) == SecretStoreStatus.Success;
        }
    }

    /// <summary>
    /// APIKeychain.read and MCPKeychain.value: the stored bytes decoded as UTF-8, trimmed; null when nothing is stored,
    /// the bytes are not UTF-8, or the value trims to empty.
    /// </summary>
    public string? Read(string service, string account) => ReadCarryingOver(service, null, account);

    /// <summary>
    /// <see cref="Read"/> over <see cref="CopyMatchingCarryingOver"/>: an item still kept under the product's old service
    /// name is carried over once (OD-0135).
    /// </summary>
    public string? ReadCarryingOver(string service, string? legacyService, string account)
    {
        lock (Gate)
        {
            // APIKeychain.swift:51-55; MCPKeychain.swift:21-25
            if (CopyMatchingCarryingOver(service, legacyService, account, out var data) != SecretStoreStatus.Success || data is null) return null;
            string key;
            try
            {
                // String(data:encoding: .utf8): nil for invalid bytes; a leading BOM is kept (R-STR-8).
                key = StrictUTF8.GetString(data);
            }
            catch (DecoderFallbackException)
            {
                return null;
            }
            var trimmed = Trimmed(key);
            return trimmed.Length == 0 ? null : trimmed;
        }
    }

    // ---- OD-0135: items kept under the product's old name ----

    /// <summary>The suffix of the marker that records an item carried over from its old service (OD-0135).</summary>
    public const string CarriedMarkerSuffix = "#carried";

    /// <summary>
    /// OD-0135: <see cref="CopyMatching"/> under <paramref name="service"/>. When that item is missing and the same account
    /// has an item under <paramref name="legacyService"/> (the service name before the product was named nth) that was never
    /// carried over, its bytes are added under the new service, a marker (the new item's target name plus
    /// <see cref="CarriedMarkerSuffix"/>, no value) records that, and the bytes are returned. The old item is never changed
    /// or removed: an older copy of the app may still read it. The marker keeps an item the user later removes under the new
    /// service from coming back. A refused add still returns the old bytes and leaves no marker, so a later read tries
    /// again. A null legacy service, or one equal to the service, is a plain <see cref="CopyMatching"/>.
    /// </summary>
    public SecretStoreStatus CopyMatchingCarryingOver(string service, string? legacyService, string account, out byte[]? data)
    {
        lock (Gate)
        {
            var status = CopyMatching(service, account, out data);
            if (status != SecretStoreStatus.ItemNotFound || legacyService is null
                || string.Equals(service, legacyService, StringComparison.OrdinalIgnoreCase)) return status;
            var marker = TargetName(service, account) + CarriedMarkerSuffix;
            // Carried before, or a marker that cannot be read: never carried again.
            if (Backend.Read(marker, out _) != ErrorNotFound) return status;
            if (CopyMatching(legacyService, account, out var old) != SecretStoreStatus.Success || old is null) return status;
            if (Add(service, account, old) == SecretStoreStatus.Success)
                Backend.Write(Credential(marker, account, [], []));
            data = old;
            return SecretStoreStatus.Success;
        }
    }

    /// <summary>
    /// OD-0135: called before a helper stores or removes an item under <paramref name="service"/>. When the same account
    /// still has an item under <paramref name="legacyService"/>, the carried marker is written, so a value the user set
    /// or removed under the new name is never replaced by the old one at a later read. The old item is not touched.
    /// </summary>
    public void SupersedeLegacy(string service, string? legacyService, string account)
    {
        if (legacyService is null || string.Equals(service, legacyService, StringComparison.OrdinalIgnoreCase)) return;
        lock (Gate)
        {
            var marker = TargetName(service, account) + CarriedMarkerSuffix;
            if (Backend.Read(marker, out _) != ErrorNotFound) return;
            if (CopyMatching(legacyService, account, out _) != SecretStoreStatus.Success) return;
            Backend.Write(Credential(marker, account, [], []));
        }
    }

    // ---- The SecItem calls ----

    /// <summary>SecItemAdd (APIKeychain.swift:39, MCPKeychain.swift:49): DuplicateItem when the item exists.</summary>
    public SecretStoreStatus Add(string service, string account, byte[] data)
    {
        lock (Gate)
        {
            // ED-120 (2) step 1: read the primary first; a present one is DuplicateItem and nothing is written.
            var target = TargetName(service, account);
            var read = Backend.Read(target, out _);
            if (read == ErrorSuccess) return SecretStoreStatus.DuplicateItem;
            if (read != ErrorNotFound) return (SecretStoreStatus)read;
            return WriteValue(target, account, data, oldSlot: 0);
        }
    }

    /// <summary>SecItemUpdate of kSecValueData (MCPKeychain.swift:42): ItemNotFound when the item is missing.</summary>
    public SecretStoreStatus Update(string service, string account, byte[] data)
    {
        lock (Gate)
        {
            // ED-120 (2) step 1: read the primary first; an absent one is ItemNotFound and nothing is written.
            var target = TargetName(service, account);
            var read = Backend.Read(target, out var primary);
            if (read == ErrorNotFound) return SecretStoreStatus.ItemNotFound;
            if (read != ErrorSuccess || primary is null) return (SecretStoreStatus)read;
            // Step 2: the old slot is 0 for a bare or malformed primary.
            var oldSlot = ParseChunks(primary) is { } chunks ? chunks.Slot : 0;
            return WriteValue(target, account, data, oldSlot);
        }
    }

    /// <summary>
    /// SecItemCopyMatching with kSecReturnData (APIKeychain.swift:51, MCPKeychain.swift:21): the stored bytes, or
    /// ItemNotFound with null.
    /// </summary>
    public SecretStoreStatus CopyMatching(string service, string account, out byte[]? data)
    {
        lock (Gate)
        {
            data = null;
            var target = TargetName(service, account);
            var read = Backend.Read(target, out var primary);
            if (read == ErrorNotFound) return SecretStoreStatus.ItemNotFound;
            if (read != ErrorSuccess || primary is null) return (SecretStoreStatus)read;
            // ED-120 (4): a bare primary is its blob.
            if (primary.Attributes.Count == 0)
            {
                data = primary.Blob;
                return SecretStoreStatus.Success;
            }
            // A malformed attribute is ItemNotFound.
            if (ParseChunks(primary) is not { } chunks) return SecretStoreStatus.ItemNotFound;
            // Join the chunks of the slot in the attribute as bytes; the caller decodes UTF-8 after the join.
            var joined = new List<byte>(primary.Blob);
            for (var n = 2; n <= chunks.Count; n++)
            {
                var part = Backend.Read(ContinuationName(target, chunks.Slot, n), out var continuation);
                // A missing continuation is ItemNotFound (ED-093, ED-120 (4)).
                if (part == ErrorNotFound) return SecretStoreStatus.ItemNotFound;
                if (part != ErrorSuccess || continuation is null) return (SecretStoreStatus)part;
                joined.AddRange(continuation.Blob);
            }
            data = joined.ToArray();
            return SecretStoreStatus.Success;
        }
    }

    /// <summary>
    /// SecItemDelete (APIKeychain.swift:58-65, :34; MCPKeychain.swift:52-59): Success, or ItemNotFound when there was
    /// no item.
    /// </summary>
    public SecretStoreStatus Delete(string service, string account)
    {
        lock (Gate)
        {
            // ED-120 (5): read the primary and delete it first, so every reader gets ItemNotFound at once.
            var target = TargetName(service, account);
            Backend.Read(target, out _);
            var deleted = Backend.Delete(target);
            var status = deleted switch
            {
                ErrorSuccess => SecretStoreStatus.Success,
                ErrorNotFound => SecretStoreStatus.ItemNotFound,
                _ => (SecretStoreStatus)deleted,
            };
            // Then both slots' continuations from .2 upward until the first not-found, even when the primary was
            // absent or malformed. A continuation delete that fails with anything but not-found is returned instead,
            // because stored values remain (OD-0008 A12).
            int? failure = null;
            foreach (var slot in (int[])[1, 2])
            {
                var result = Sweep(target, slot, from: 2);
                if (result != ErrorNotFound) failure ??= result;
            }
            return failure is { } error ? (SecretStoreStatus)error : status;
        }
    }

    // ---- ED-120 encoding ----

    private readonly record struct Chunks(int Count, int Slot);

    private static string Trimmed(string text) => FoundationText.Trim(text, includeNewlines: true); // Text.swift:132

    private static string ContinuationName(string target, int slot, int n) =>
        target + "#" + slot.ToString(System.Globalization.CultureInfo.InvariantCulture)
        + "." + n.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static GenericCredential Credential(string target, string account, byte[] blob, IReadOnlyList<CredentialAttribute> attributes) =>
        new(target, account, blob, CredTypeGeneric, CredPersistLocalMachine, attributes);

    /// <summary>
    /// ED-120 (1): the count and slot of a chunked primary, or null for a bare primary or a malformed attribute
    /// (anything but exactly one Marrow_Chunks attribute whose value is ASCII "count/slot", decimal, no leading zeros,
    /// count >= 2, slot 1 or 2).
    /// </summary>
    private static Chunks? ParseChunks(GenericCredential primary)
    {
        if (primary.Attributes.Count != 1) return null;
        var attribute = primary.Attributes[0];
        if (!string.Equals(attribute.Keyword, ChunksKeyword, StringComparison.Ordinal)) return null;
        var value = attribute.Value;
        var slash = Array.IndexOf(value, (byte)'/');
        if (slash < 0) return null;
        if (ParseDecimal(value.AsSpan(0, slash)) is not { } count) return null;
        if (ParseDecimal(value.AsSpan(slash + 1)) is not { } slot) return null;
        if (count < 2 || slot is not (1 or 2)) return null;
        return new Chunks(count, slot);
    }

    /// <summary>ASCII decimal digits with no sign and no leading zero; null otherwise or past int.MaxValue.</summary>
    private static int? ParseDecimal(ReadOnlySpan<byte> digits)
    {
        if (digits.Length is 0 or > 10) return null;
        if (digits[0] == (byte)'0') return null;
        long result = 0;
        foreach (var digit in digits)
        {
            if (digit is < (byte)'0' or > (byte)'9') return null;
            result = result * 10 + (digit - (byte)'0');
        }
        return result > int.MaxValue ? null : (int)result;
    }

    private static byte[] Encode(int count, int slot) =>
        Encoding.ASCII.GetBytes(count.ToString(System.Globalization.CultureInfo.InvariantCulture) + "/"
            + slot.ToString(System.Globalization.CultureInfo.InvariantCulture));

    /// <summary>ED-120 (2) steps 3-5 and (3): the write of Add and Update once the primary has been read.</summary>
    private SecretStoreStatus WriteValue(string target, string account, byte[] data, int oldSlot)
    {
        // Step 3: a value that fits one chunk is a bare primary.
        if (data.Length <= ChunkBytes)
        {
            var written = Backend.Write(Credential(target, account, data, []));
            if (written != ErrorSuccess) return (SecretStoreStatus)written;
            // Step 5: for a bare value, sweep both slots from .2; a failed sweep delete does not change Success.
            Sweep(target, 1, from: 2);
            Sweep(target, 2, from: 2);
            return SecretStoreStatus.Success;
        }

        // Step 4: the new slot is 2 if the old slot is 1, and 1 in every other case.
        var count = (data.Length + ChunkBytes - 1) / ChunkBytes;
        var slot = oldSlot == 1 ? 2 : 1;
        for (var n = 2; n <= count; n++)
        {
            var start = (n - 1) * ChunkBytes;
            var chunk = data[start..Math.Min(start + ChunkBytes, data.Length)];
            var written = Backend.Write(Credential(ContinuationName(target, slot, n), account, chunk, []));
            if (written != ErrorSuccess)
            {
                // (3): never write the primary; delete the new slot's continuations already written.
                for (var m = 2; m < n; m++) Backend.Delete(ContinuationName(target, slot, m));
                return (SecretStoreStatus)written;
            }
        }
        // The primary last, with chunk 1 and the one attribute, in one CredWrite.
        var primary = Credential(target, account, data[..ChunkBytes], [new CredentialAttribute(ChunksKeyword, Encode(count, slot))]);
        var primaryWritten = Backend.Write(primary);
        if (primaryWritten != ErrorSuccess)
        {
            for (var m = 2; m <= count; m++) Backend.Delete(ContinuationName(target, slot, m));
            return (SecretStoreStatus)primaryWritten;
        }
        // Step 5: a stale tail of the new slot from count + 1, then the other slot from .2.
        Sweep(target, slot, from: count + 1);
        Sweep(target, slot == 1 ? 2 : 1, from: 2);
        return SecretStoreStatus.Success;
    }

    /// <summary>
    /// Deletes `<target>#<slot>.<n>` from n = from upward until a delete does not succeed, and returns that delete's
    /// status: ERROR_NOT_FOUND at the end of the run, or the failure that stopped it (which Delete reports).
    /// </summary>
    private int Sweep(string target, int slot, int from)
    {
        for (var n = from; ; n++)
        {
            var deleted = Backend.Delete(ContinuationName(target, slot, n));
            if (deleted != ErrorSuccess) return deleted;
        }
    }
}

/// <summary>
/// The Win32 Credential Manager behind the seam (ED-093): CredWriteW, CredReadW, CredDeleteW and CredFree from
/// advapi32 for CRED_TYPE_GENERIC. Creating one touches no credential.
/// </summary>
public sealed class CredentialManagerBackend : ICredentialBackend
{
    private const int CredTypeGeneric = 1;

    public int Write(GenericCredential credential)
    {
        var allocations = new List<IntPtr>();
        var blob = IntPtr.Zero;
        try
        {
            IntPtr Unicode(string text)
            {
                var pointer = Marshal.StringToHGlobalUni(text);
                allocations.Add(pointer);
                return pointer;
            }

            if (credential.Blob.Length > 0)
            {
                blob = Marshal.AllocHGlobal(credential.Blob.Length);
                Marshal.Copy(credential.Blob, 0, blob, credential.Blob.Length);
            }

            var attributes = IntPtr.Zero;
            if (credential.Attributes.Count > 0)
            {
                var size = Marshal.SizeOf<NativeAttribute>();
                attributes = Marshal.AllocHGlobal(size * credential.Attributes.Count);
                allocations.Add(attributes);
                for (var index = 0; index < credential.Attributes.Count; index++)
                {
                    var attribute = credential.Attributes[index];
                    var value = IntPtr.Zero;
                    if (attribute.Value.Length > 0)
                    {
                        value = Marshal.AllocHGlobal(attribute.Value.Length);
                        allocations.Add(value);
                        Marshal.Copy(attribute.Value, 0, value, attribute.Value.Length);
                    }
                    var native = new NativeAttribute
                    {
                        Keyword = Unicode(attribute.Keyword),
                        Flags = 0,
                        ValueSize = (uint)attribute.Value.Length,
                        Value = value,
                    };
                    Marshal.StructureToPtr(native, attributes + index * size, fDeleteOld: false);
                }
            }

            var nativeCredential = new NativeCredential
            {
                Flags = 0,
                Type = (uint)credential.Type,
                TargetName = Unicode(credential.TargetName),
                CredentialBlobSize = (uint)credential.Blob.Length,
                CredentialBlob = blob,
                Persist = (uint)credential.Persist,
                AttributeCount = (uint)credential.Attributes.Count,
                Attributes = attributes,
                UserName = Unicode(credential.UserName),
            };
            return CredWriteW(ref nativeCredential, 0) ? 0 : Marshal.GetLastPInvokeError();
        }
        finally
        {
            if (blob != IntPtr.Zero)
            {
                // The secret does not outlive the call in memory this process owns.
                Marshal.Copy(new byte[credential.Blob.Length], 0, blob, credential.Blob.Length);
                Marshal.FreeHGlobal(blob);
            }
            foreach (var pointer in allocations) Marshal.FreeHGlobal(pointer);
        }
    }

    public int Read(string targetName, out GenericCredential? credential)
    {
        credential = null;
        if (!CredReadW(targetName, CredTypeGeneric, 0, out var pointer)) return Marshal.GetLastPInvokeError();
        try
        {
            var native = Marshal.PtrToStructure<NativeCredential>(pointer);
            var blob = new byte[native.CredentialBlobSize];
            if (blob.Length > 0) Marshal.Copy(native.CredentialBlob, blob, 0, blob.Length);
            var attributes = new List<CredentialAttribute>((int)native.AttributeCount);
            var size = Marshal.SizeOf<NativeAttribute>();
            for (var index = 0; index < (int)native.AttributeCount; index++)
            {
                var attribute = Marshal.PtrToStructure<NativeAttribute>(native.Attributes + index * size);
                var value = new byte[attribute.ValueSize];
                if (value.Length > 0) Marshal.Copy(attribute.Value, value, 0, value.Length);
                attributes.Add(new CredentialAttribute(Marshal.PtrToStringUni(attribute.Keyword) ?? "", value));
            }
            credential = new GenericCredential(
                Marshal.PtrToStringUni(native.TargetName) ?? "",
                Marshal.PtrToStringUni(native.UserName) ?? "",
                blob,
                (int)native.Type,
                (int)native.Persist,
                attributes);
            return 0;
        }
        finally
        {
            CredFree(pointer);
        }
    }

    public int Delete(string targetName) =>
        CredDeleteW(targetName, CredTypeGeneric, 0) ? 0 : Marshal.GetLastPInvokeError();

    // wincred.h CREDENTIALW.
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeCredential
    {
        public uint Flags;
        public uint Type;
        public IntPtr TargetName;
        public IntPtr Comment;
        public uint LastWrittenLow;
        public uint LastWrittenHigh;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public IntPtr TargetAlias;
        public IntPtr UserName;
    }

    // wincred.h CREDENTIAL_ATTRIBUTEW.
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeAttribute
    {
        public IntPtr Keyword;
        public uint Flags;
        public uint ValueSize;
        public IntPtr Value;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWriteW(ref NativeCredential credential, uint flags);

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredReadW(string targetName, uint type, uint flags, out IntPtr credential);

    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredDeleteW(string targetName, uint type, uint flags);

    [DllImport("advapi32.dll", EntryPoint = "CredFree")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern void CredFree(IntPtr buffer);
}
