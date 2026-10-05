namespace Marrow.Testing;

// PORT-ONLY: a real native test folder on both Linux and Windows, below the explicitly supplied throwaway data root.
public sealed class LocalProviderFolder : IDisposable
{
    private readonly string? previousDirectory;
    public LocalProviderFolder(bool atFileSystemRoot = false)
    {
        var data = OperatingSystem.IsWindows() ? TestDataDirectory.Read() : Environment.GetEnvironmentVariable(TestDataDirectory.VariableName);
        if (string.IsNullOrWhiteSpace(data)) throw new InvalidOperationException("Set MARROW_PORT_DATA_DIR before testing Local.");
        Root = Path.Combine(Path.GetFullPath(data), "LocalProviderTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
        // The Windows path port treats a bare leading slash as relative. A non-parallel test can scope Linux's cwd
        // to / so repeated standardization still names the actual file. Production path rules stay untouched.
        if (atFileSystemRoot && !OperatingSystem.IsWindows())
        {
            previousDirectory = Environment.CurrentDirectory;
            Environment.CurrentDirectory = "/";
        }
    }
    public string Root { get; }
    public void Dispose()
    {
        if (previousDirectory is not null) Environment.CurrentDirectory = previousDirectory;
        Directory.Delete(Root, recursive: true);
    }
}
