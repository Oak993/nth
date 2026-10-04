using Marrow.Services.Port;

namespace Marrow.Services.Tests.Fixtures;

/// <summary>
/// The real Windows Credential Manager (<see cref="CredentialManagerBackend"/>) with the service part of every target name swapped for a unique test-only one, so a test drives the
/// real Win32 calls without ever reading, writing or deleting the user's own "Marrow Code" credentials. A target outside the swapped service is refused, never forwarded.
/// </summary>
internal sealed class RenamedTargetCredentialBackend(string realService, string testService) : ICredentialBackend
{
    private readonly CredentialManagerBackend real = new();

    /// <summary>The target names that reached the real Credential Manager.</summary>
    public List<string> Touched { get; } = [];

    public string Map(string target) =>
        target.StartsWith(realService + ":", StringComparison.Ordinal)
            ? testService + target[realService.Length..]
            : throw new InvalidOperationException("A target outside " + realService + " reached the real Credential Manager: " + target);

    public int Write(GenericCredential credential)
    {
        var mapped = Map(credential.TargetName);
        Touched.Add(mapped);
        return real.Write(credential with { TargetName = mapped });
    }

    public int Read(string targetName, out GenericCredential? credential)
    {
        var mapped = Map(targetName);
        Touched.Add(mapped);
        return real.Read(mapped, out credential);
    }

    public int Delete(string targetName)
    {
        var mapped = Map(targetName);
        Touched.Add(mapped);
        return real.Delete(mapped);
    }
}
