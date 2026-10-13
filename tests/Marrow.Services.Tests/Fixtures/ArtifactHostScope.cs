using Marrow.Core.Models;
using Marrow.Services.Web;
using Marrow.Testing;

namespace Marrow.Services.Tests.Fixtures;

/// <summary>
/// PORT-ONLY (web surfaces W12, ED-NEW-web-w12-1): artifacts on, for the calling async flow only. A throwaway threads
/// folder under MARROW_PORT_DATA_DIR (R-TEST-9), a store on it with a clock that moves a second per version, and a turn
/// seam that answers <see cref="Turn"/> for every chat, so tools record a known turn and prompt.
/// </summary>
public sealed class ArtifactHostScope : IDisposable
{
    private readonly IDisposable scope;
    private DateTimeOffset now = new(2026, 10, 11, 9, 0, 0, TimeSpan.Zero);

    public ArtifactHostScope(ArtifactTurn? turn = null, LiveAnswerHost? live = null)
    {
        var dataDirectory = TestDataDirectory.Read();
        if (string.IsNullOrEmpty(dataDirectory))
            throw new InvalidOperationException("Set MARROW_PORT_DATA_DIR to a throwaway folder before running the tests (R-TEST-9).");
        Threads = Path.Combine(dataDirectory, "ArtifactHost", Guid.NewGuid().ToString("N"), "threads");
        Directory.CreateDirectory(Threads);
        Turn = turn;
        Store = new ArtifactStore(() => Threads, () => now = now.AddSeconds(1));
        Host = new ArtifactHost(Store, _ => Task.FromResult(Turn), live);
        scope = ArtifactHost.Shared.Override(Host);
    }

    public string Threads { get; }

    public ArtifactTurn? Turn { get; set; }

    public ArtifactStore Store { get; }

    public ArtifactHost Host { get; }

    public void Dispose()
    {
        scope.Dispose();
        try { Directory.Delete(Path.GetDirectoryName(Threads)!, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
