using Marrow.Services.Store;

namespace Marrow.Services.Tests.Fixtures;

/// <summary>
/// A throwaway root and home directory for Storage.cs tests (R-TEST-9): binds <see cref="Storage.Locations"/>
/// to <see cref="StorageLocations.Create"/> for the calling async flow only, so parallel test classes never
/// share one root and nothing ever touches the real %LOCALAPPDATA%\Marrow Code or %USERPROFILE%\.marrow.
/// </summary>
public sealed class StorageScratchFolder : IDisposable
{
    private readonly IDisposable scope;

    public StorageScratchFolder(bool isDevelopment = false)
    {
        var dataDirectory = Environment.GetEnvironmentVariable("MARROW_PORT_DATA_DIR");
        if (string.IsNullOrEmpty(dataDirectory))
            throw new InvalidOperationException("Set MARROW_PORT_DATA_DIR to a throwaway folder before running the tests (R-TEST-9).");
        var baseFolder = Path.Combine(dataDirectory, "StorageTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(baseFolder);
        Root = Path.Combine(baseFolder, "root");
        Home = Path.Combine(baseFolder, "home");
        Locations = StorageLocations.Create(Root, Home, isDevelopment);
        scope = Storage.Locations.Override(Locations);
    }

    public string Root { get; }
    public string Home { get; }
    public IStorageLocations Locations { get; }

    public void Dispose()
    {
        scope.Dispose();
        var baseFolder = Path.GetDirectoryName(Root)!;
        try { Directory.Delete(baseFolder, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
