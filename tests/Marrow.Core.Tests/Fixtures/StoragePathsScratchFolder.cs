using Marrow.Core.Support;
using Marrow.Testing;

namespace Marrow.Core.Tests.Fixtures;

/// <summary>
/// A throwaway root for StoragePaths.cs tests (R-TEST-9): binds <see cref="StoragePaths.Locations"/> to
/// <see cref="StoragePathsLocations.Create(string)"/> for the calling async flow only, so parallel test
/// classes never share one root and nothing ever touches the real %LOCALAPPDATA%\Marrow Code.
/// </summary>
public sealed class StoragePathsScratchFolder : IDisposable
{
    private readonly IDisposable scope;

    public StoragePathsScratchFolder()
    {
        var dataDirectory = TestDataDirectory.Read();
        if (string.IsNullOrEmpty(dataDirectory))
            throw new InvalidOperationException("Set MARROW_PORT_DATA_DIR to a throwaway folder before running the tests (R-TEST-9).");
        var baseFolder = Path.Combine(dataDirectory, "StoragePathsTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(baseFolder);
        Root = Path.Combine(baseFolder, "root");
        scope = StoragePaths.Locations.Override(StoragePathsLocations.Create(Root));
    }

    public string Root { get; }

    public void Dispose()
    {
        scope.Dispose();
        var baseFolder = Path.GetDirectoryName(Root)!;
        try { Directory.Delete(baseFolder, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
