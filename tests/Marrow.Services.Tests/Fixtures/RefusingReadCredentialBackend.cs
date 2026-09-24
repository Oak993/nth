using Marrow.Services.Port;

namespace Marrow.Services.Tests.Fixtures;

/// <summary>
/// PORT-ONLY test double for the secret store's backend seam (ED-093): wraps an <see cref="InMemoryCredentialBackend"/>
/// and refuses every CredReadW with ERROR_ACCESS_DENIED (5), standing in for a SecItemCopyMatching that returns a
/// status other than errSecSuccess (APIKeychain.swift:51). Writes and deletes pass through to the wrapped backend.
/// </summary>
public sealed class RefusingReadCredentialBackend(InMemoryCredentialBackend inner) : ICredentialBackend
{
    public InMemoryCredentialBackend Inner { get; } = inner;

    public int Write(GenericCredential credential) => Inner.Write(credential);

    public int Read(string targetName, out GenericCredential? credential)
    {
        credential = null;
        return InMemoryCredentialBackend.ErrorAccessDenied;
    }

    public int Delete(string targetName) => Inner.Delete(targetName);
}
