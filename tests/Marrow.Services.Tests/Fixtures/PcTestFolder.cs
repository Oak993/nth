using Marrow.Testing;

namespace Marrow.Services.Tests.Fixtures;

// PORT-ONLY: PC control (ED-NEW-pcctl-1). A fresh folder under MARROW_PORT_DATA_DIR (R-TEST-9), removed on dispose.
public sealed class PcTestFolder : IDisposable
{
    public string Path { get; }

    public PcTestFolder(string prefix)
    {
        var root = TestDataDirectory.Read() ?? throw new InvalidOperationException("Set MARROW_PORT_DATA_DIR to a throwaway folder before running the tests (R-TEST-9).");
        Path = System.IO.Path.Combine(root, prefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
