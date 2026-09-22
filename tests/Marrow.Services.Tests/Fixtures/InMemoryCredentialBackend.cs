using Marrow.Services.Port;

namespace Marrow.Services.Tests.Fixtures;

/// <summary>
/// PORT-ONLY test double for the secret store's backend seam (ED-093): an in-memory Credential Manager holding
/// CRED_TYPE_GENERIC credentials. It models the two Credential Manager rules the store's encoding exists for,
/// so a test against it proves the encoding, not the fake:
/// target names compare without case (a write to a target that differs only in case replaces that credential),
/// and a blob over CRED_MAX_CREDENTIAL_BLOB_SIZE (2560 bytes) is refused with error 1783 (ED-093, probed:
/// 2560 accepted, 2561 refused with 1783). The other checks are the wincred.h limits: CRED_MAX_USERNAME_LENGTH
/// 513, CRED_MAX_ATTRIBUTES 64, CRED_MAX_STRING_LENGTH 256 for a keyword, CRED_MAX_VALUE_SIZE 256 for a value,
/// CRED_PERSIST_SESSION 1 to CRED_PERSIST_ENTERPRISE 3; a violation is ERROR_INVALID_PARAMETER (87).
/// Every credential is stored and returned as a copy, so a caller can never alias the stored bytes.
/// </summary>
public sealed class InMemoryCredentialBackend : ICredentialBackend
{
    public const int ErrorSuccess = 0;
    public const int ErrorAccessDenied = 5;
    public const int ErrorInvalidParameter = 87;
    public const int ErrorNotFound = 1168;
    public const int ErrorBlobTooLarge = 1783;
    public const int CredTypeGeneric = 1;
    public const int CredPersistLocalMachine = 2;
    public const int MaxBlobBytes = 2560;

    private readonly Dictionary<string, GenericCredential> credentials = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Every call, in order: "write T", "read T", "delete T" with the target name as passed.</summary>
    public List<string> Operations { get; } = [];

    /// <summary>
    /// Runs before a write that passed the limit checks is applied. A nonzero result refuses the write with that
    /// Win32 error and stores nothing. A test uses it to refuse a write or to read the store mid-write.
    /// </summary>
    public Func<GenericCredential, int>? BeforeWrite { get; set; }

    /// <summary>The target names held now, in the case they were last written with, ordinal order.</summary>
    public IReadOnlyList<string> TargetNames =>
        credentials.Values.Select(c => c.TargetName).Order(StringComparer.Ordinal).ToList();

    /// <summary>The held credential for a target (compared without case), or null. Does not log an operation.</summary>
    public GenericCredential? Peek(string targetName) =>
        credentials.TryGetValue(targetName, out var held) ? Copy(held) : null;

    /// <summary>Removes a credential behind the store's back (a test that loses a chunk). Does not log.</summary>
    public bool Remove(string targetName) => credentials.Remove(targetName);

    public int Write(GenericCredential credential)
    {
        Operations.Add("write " + credential.TargetName);
        if (credential.Blob.Length > MaxBlobBytes) return ErrorBlobTooLarge;
        if (string.IsNullOrEmpty(credential.TargetName)) return ErrorInvalidParameter;
        if (credential.Type != CredTypeGeneric) return ErrorInvalidParameter;
        if (credential.Persist is < 1 or > 3) return ErrorInvalidParameter;
        if (credential.UserName.Length > 513) return ErrorInvalidParameter;
        if (credential.Attributes.Count > 64) return ErrorInvalidParameter;
        if (credential.Attributes.Any(a => a.Keyword.Length is 0 or > 256 || a.Value.Length > 256)) return ErrorInvalidParameter;
        var refused = BeforeWrite?.Invoke(Copy(credential)) ?? ErrorSuccess;
        if (refused != ErrorSuccess) return refused;
        credentials.Remove(credential.TargetName);
        credentials[credential.TargetName] = Copy(credential);
        return ErrorSuccess;
    }

    public int Read(string targetName, out GenericCredential? credential)
    {
        Operations.Add("read " + targetName);
        if (credentials.TryGetValue(targetName, out var held))
        {
            credential = Copy(held);
            return ErrorSuccess;
        }
        credential = null;
        return ErrorNotFound;
    }

    public int Delete(string targetName)
    {
        Operations.Add("delete " + targetName);
        return credentials.Remove(targetName) ? ErrorSuccess : ErrorNotFound;
    }

    private static GenericCredential Copy(GenericCredential c) =>
        new(c.TargetName, c.UserName, c.Blob.ToArray(), c.Type, c.Persist,
            c.Attributes.Select(a => new CredentialAttribute(a.Keyword, a.Value.ToArray())).ToList());
}
