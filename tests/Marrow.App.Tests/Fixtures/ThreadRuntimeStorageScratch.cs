using Marrow.Services.Store;
using Marrow.Testing;

namespace Marrow.App.Tests.Fixtures;

/// <summary>
/// A throwaway root and home directory (R-TEST-9), for tests that call Storage/DiskWriter directly without a
/// full AppModelHarness (matching Marrow.Services.Tests.Fixtures.StorageScratchFolder, duplicated here since a
/// test project does not reference another test project's assembly): binds Storage.Locations for the calling
/// async flow only, so parallel test classes never share one root and nothing ever touches the real
/// %LOCALAPPDATA%\Marrow Code.
/// </summary>
public sealed class ThreadRuntimeStorageScratch : IDisposable
{
    private readonly IDisposable scope;
    private readonly string baseFolder;

    public ThreadRuntimeStorageScratch()
    {
        var dataDirectory = TestDataDirectory.Read();
        if (string.IsNullOrEmpty(dataDirectory))
            throw new InvalidOperationException("Set MARROW_PORT_DATA_DIR to a throwaway folder before running the tests (R-TEST-9).");
        baseFolder = Path.Combine(dataDirectory, "ThreadRuntimeStorageTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(baseFolder);
        var root = Path.Combine(baseFolder, "root");
        var home = Path.Combine(baseFolder, "home");
        scope = Storage.Locations.Override(StorageLocations.Create(root, home));
    }

    public void Dispose()
    {
        scope.Dispose();
        try { Directory.Delete(baseFolder, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
