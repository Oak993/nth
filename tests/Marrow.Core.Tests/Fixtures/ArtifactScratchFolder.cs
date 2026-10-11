using Marrow.Core.Models;
using Marrow.Testing;

namespace Marrow.Core.Tests.Fixtures;

/// <summary>
/// PORT-ONLY (web surfaces W12, ED-NEW-web-w12-1): a throwaway threads folder under MARROW_PORT_DATA_DIR (R-TEST-9) for
/// artifact store tests, and a clock that moves one second on every read, so versions have distinct, ordered times.
/// </summary>
public sealed class ArtifactScratchFolder : IDisposable
{
    private DateTimeOffset now = new(2026, 10, 11, 9, 0, 0, TimeSpan.Zero);

    public ArtifactScratchFolder()
    {
        var dataDirectory = TestDataDirectory.Read();
        if (string.IsNullOrEmpty(dataDirectory))
            throw new InvalidOperationException("Set MARROW_PORT_DATA_DIR to a throwaway folder before running the tests (R-TEST-9).");
        Threads = Path.Combine(dataDirectory, "ArtifactTests", Guid.NewGuid().ToString("N"), "threads");
        Directory.CreateDirectory(Threads);
    }

    public string Threads { get; }

    public DateTimeOffset Tick() => now = now.AddSeconds(1);

    public ArtifactStore Store() => new(() => Threads, Tick);

    public void Dispose()
    {
        try { Directory.Delete(Path.GetDirectoryName(Threads)!, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
