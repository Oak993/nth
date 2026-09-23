using Marrow.Core.Port;
using Marrow.Services.Store;
using Marrow.Testing;

namespace Marrow.App.Tests.Fixtures;

/// <summary>
/// A throwaway UserDefaults + Storage.Locations scope for TokenActivitySectionTests (R-TEST-9): TokenLedger's
/// private constructor (Services/Store/TokenLedger.cs:105-110) reads both through LiveSpendStateStore.Load, so
/// both must be bound before the first TokenLedger.Shared.Current access in the calling async flow. Kept in its
/// own Fixtures file, not in the test file, because R-TEST-2/3 (Marrow.Testing.Traceability) forbids a second
/// class/record declaration inside a file that holds a test class.
/// </summary>
public sealed class TokenActivityTestScope : IDisposable
{
    private readonly IDisposable defaultsScope;
    private readonly IDisposable storageScope;
    private readonly string baseFolder;

    private TokenActivityTestScope(IDisposable defaultsScope, IDisposable storageScope, string baseFolder)
    {
        this.defaultsScope = defaultsScope;
        this.storageScope = storageScope;
        this.baseFolder = baseFolder;
    }

    public static TokenActivityTestScope Create()
    {
        var defaultsScope = UserDefaults.Standard.Override(new InMemorySettingsStore());
        var dataDirectory = Environment.GetEnvironmentVariable("MARROW_PORT_DATA_DIR");
        if (string.IsNullOrEmpty(dataDirectory)) dataDirectory = Path.GetTempPath();
        var baseFolder = Path.Combine(dataDirectory, "TokenActivitySectionTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(baseFolder);
        var locations = StorageLocations.Create(Path.Combine(baseFolder, "root"), Path.Combine(baseFolder, "home"));
        var storageScope = Storage.Locations.Override(locations);
        return new TokenActivityTestScope(defaultsScope, storageScope, baseFolder);
    }

    public void Dispose()
    {
        defaultsScope.Dispose();
        storageScope.Dispose();
        try { Directory.Delete(baseFolder, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
