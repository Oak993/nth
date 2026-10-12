using Marrow.Services.Assistant;
using Marrow.Services.Port;
using Marrow.Testing;

namespace Marrow.Services.Tests.Fixtures;

/// <summary>
/// PORT-ONLY (AS-3): a throwaway pair of assistant roots under MARROW_PORT_DATA_DIR (or the temp folder), never the
/// owner's %LOCALAPPDATA% (16.1, R-TEST-9), plus an in-memory Credential Manager for the audit key and anchor.
/// </summary>
public sealed class AssistantTempRoot : IDisposable
{
    public AssistantTempRoot()
    {
        var parent = TestDataDirectory.Read() ?? Path.GetTempPath();
        Folder = Path.Combine(parent, "as3-" + Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(Folder);
        Roots = new FixedAssistantStorageRoots(Path.Combine(Folder, "storage", "Assistant"), Path.Combine(Folder, "local", "assistant", "audit"));
        Keys = new AuditKeyStore(new SecretStore(new ReadGate(this)), "nth.dextra.audit.test");
    }

    /// <summary>
    /// Runs before each Credential Manager read with the target name. A nonzero result refuses the read with that Win32
    /// error (ERROR_NO_SUCH_LOGON_SESSION 1312, access denied 5), the item left as it is. A test uses it for a read that
    /// fails for a moment.
    /// </summary>
    public Func<string, int>? RefuseRead { get; set; }

    public string Folder { get; }

    public IAssistantStorageRoots Roots { get; }

    public InMemoryCredentialBackend Credentials { get; } = new();

    public AuditKeyStore Keys { get; }

    public string AuditDirectory => Roots.AuditDirectory;

    public string Store => Roots.AssistantRoot;

    /// <summary>The in-memory Credential Manager with <see cref="RefuseRead"/> in front of its reads.</summary>
    private sealed class ReadGate(AssistantTempRoot root) : ICredentialBackend
    {
        public int Write(GenericCredential credential) => root.Credentials.Write(credential);

        public int Read(string targetName, out GenericCredential? credential)
        {
            if (root.RefuseRead?.Invoke(targetName) is { } refused and not 0)
            {
                credential = null;
                return refused;
            }
            return root.Credentials.Read(targetName, out credential);
        }

        public int Delete(string targetName) => root.Credentials.Delete(targetName);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Folder, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
