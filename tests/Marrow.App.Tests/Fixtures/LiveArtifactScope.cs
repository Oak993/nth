using Marrow.Core.Models;
using Marrow.Services.Web;
using Marrow.Testing;

namespace Marrow.App.Tests.Fixtures;

/// <summary>Artifacts on for the calling flow: a store on a throwaway threads folder (R-TEST-9).</summary>
internal sealed class LiveArtifactScope : IDisposable
{
    private readonly IDisposable scope;
    private readonly string folder;

    public LiveArtifactScope(LiveAnswerHost? live = null)
    {
        var data = TestDataDirectory.Read();
        if (string.IsNullOrEmpty(data)) throw new InvalidOperationException("Set MARROW_PORT_DATA_DIR to a throwaway folder before running the tests (R-TEST-9).");
        folder = Path.Combine(data, "LiveArtifacts", Guid.NewGuid().ToString("N"), "threads");
        Directory.CreateDirectory(folder);
        Store = new ArtifactStore(() => folder);
        Host = new ArtifactHost(Store, null, live);
        scope = ArtifactHost.Shared.Override(Host);
    }

    public ArtifactStore Store { get; }

    public ArtifactHost Host { get; }

    public void Dispose()
    {
        scope.Dispose();
        try { Directory.Delete(Path.GetDirectoryName(folder)!, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
