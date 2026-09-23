using Marrow.Services.Port;

namespace Marrow.Services.Tests.Fixtures;

/// <summary>
/// PORT-ONLY test double for the MCPKeychain tests: a credential backend that passes every call to an
/// <see cref="InMemoryCredentialBackend"/> except the ones a test tells it to refuse. It stands for a locked or
/// unavailable store (PR-MCP-096 failure case) and for a store that refuses a deletion (PR-MCP-099 and PR-MCP-228,
/// OD-0008 A12). A refusal is ERROR_ACCESS_DENIED (5), a Win32 error that is neither success nor
/// ERROR_NOT_FOUND (1168), so the secret store reports it as a status other than Success and ItemNotFound (ED-093:
/// the SecItem status contract is kept). Targets compare without case, as Credential Manager compares them.
/// </summary>
public sealed class RefusingCredentialBackend(InMemoryCredentialBackend inner) : ICredentialBackend
{
    public InMemoryCredentialBackend Inner { get; } = inner;

    /// <summary>Target names whose deletion is refused with ERROR_ACCESS_DENIED; nothing is removed.</summary>
    public HashSet<string> RefusedDeleteTargets { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>When true every read is refused with ERROR_ACCESS_DENIED, as a locked store would refuse it.</summary>
    public bool RefuseReads { get; set; }

    /// <summary>Target names whose write is refused with ERROR_ACCESS_DENIED; nothing is stored.</summary>
    public HashSet<string> RefuseWriteTargets { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Every refused deletion, in order, with the target name as passed.</summary>
    public List<string> RefusedDeletes { get; } = [];

    public int Write(GenericCredential credential) =>
        RefuseWriteTargets.Contains(credential.TargetName) ? InMemoryCredentialBackend.ErrorAccessDenied : Inner.Write(credential);

    public int Read(string targetName, out GenericCredential? credential)
    {
        if (RefuseReads)
        {
            credential = null;
            return InMemoryCredentialBackend.ErrorAccessDenied;
        }
        return Inner.Read(targetName, out credential);
    }

    public int Delete(string targetName)
    {
        if (RefusedDeleteTargets.Contains(targetName))
        {
            RefusedDeletes.Add(targetName);
            return InMemoryCredentialBackend.ErrorAccessDenied;
        }
        return Inner.Delete(targetName);
    }
}
