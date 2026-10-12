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
        Keys = new AuditKeyStore(new SecretStore(Credentials), "nth.dextra.audit.test");
    }

    public string Folder { get; }

    public IAssistantStorageRoots Roots { get; }

    public InMemoryCredentialBackend Credentials { get; } = new();

    public AuditKeyStore Keys { get; }

    public string AuditDirectory => Roots.AuditDirectory;

    public string Store => Roots.AssistantRoot;

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
