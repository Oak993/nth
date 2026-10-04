using Marrow.Services.Port.Downloads;
using Marrow.Testing;
using Marrow.Testing.Downloads;

namespace Marrow.Services.Tests.Fixtures;

// SK-3 test fixture: one deterministic runtime (manual clock), a scratch folder, SK-0's fake https server on 127.0.0.1 (and, when asked, a second on
// 127.0.0.2 and a plain-http one), the guarded handler that reaches only those, and a Downloader over them. Every real-time wait is bounded by
// FakeDownloadServer.Guard.
public sealed class DownloadRig : IAsyncDisposable
{
    public DownloadRig(bool second = false, bool plain = false)
    {
        Server = FakeDownloadServer.StartHttps(clock: ServerClock);
        Second = second ? FakeDownloadServer.StartHttps("127.0.0.2", ServerClock) : null;
        Plain = plain ? FakeDownloadServer.StartPlainHttp(clock: ServerClock) : null;
        var servers = new[] { Server, Second, Plain }.OfType<FakeDownloadServer>().ToArray();
        Handler = FakeDownloadTransport.CreateHandler(servers);
        Counting = new CountingHandler(Handler);
        var data = Environment.GetEnvironmentVariable(TestDataDirectory.VariableName);
        if (string.IsNullOrEmpty(data)) throw new InvalidOperationException("Set MARROW_PORT_DATA_DIR to a throwaway folder before running the tests (R-TEST-9).");
        Root = Path.Combine(OperatingSystem.IsWindows() ? TestDataDirectory.Read()! : data, "DownloadTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
        PartFolder = Path.Combine(Root, "downloads");
    }

    public TestRuntime Runtime { get; } = new();

    /// <summary>The fake servers' own clock (their trickle waits). It is separate from the downloader's, so a test moves each side on purpose.</summary>
    public ManualTimeProvider ServerClock { get; } = new();

    public FakeDownloadServer Server { get; }

    public FakeDownloadServer? Second { get; }

    public FakeDownloadServer? Plain { get; }

    public GuardedDownloadHandler Handler { get; }

    public CountingHandler Counting { get; }

    public string Root { get; }

    public string PartFolder { get; }

    public Downloader Create(DownloadOptions? options = null, params string[] allowedHosts) =>
        new(PartFolder, new DownloadPolicy(allowedHosts.Length == 0 ? ["127.0.0.1"] : allowedHosts), options, Counting);

    public string Dest(string name) => Path.Combine(Root, "out", name);

    public string PartPath(FakeArtifact artifact) => Path.Combine(PartFolder, artifact.Sha256Hex[..8] + ".part");

    public DownloadRequest Request(string path, FakeArtifact artifact, string? destName = null, string? source = null, FakeDownloadServer? on = null) =>
        new((on ?? Server).Url(path), Dest(destName ?? Path.GetFileName(path)), artifact.Length, artifact.Sha256Hex, source);

    /// <summary>Starts and waits (bounded) for the outcome.</summary>
    public async Task<DownloadOutcome> Run(DownloadRequest request, DownloadOptions? options = null, params string[] allowedHosts)
    {
        var job = Create(options, allowedHosts).Start(request);
        return await job.Completion.WaitAsync(FakeDownloadServer.Guard);
    }

    /// <summary>Polls (bounded by the hang guard) until <paramref name="condition"/> holds.</summary>
    public static async Task Until(Func<bool> condition)
    {
        using var stop = new CancellationTokenSource(FakeDownloadServer.Guard);
        while (!condition()) await Task.Delay(1, stop.Token);
    }

    public void SeedPart(FakeArtifact artifact, byte[] bytes)
    {
        Directory.CreateDirectory(PartFolder);
        File.WriteAllBytes(PartPath(artifact), bytes);
    }

    public async ValueTask DisposeAsync()
    {
        await Server.DisposeAsync();
        if (Second is not null) await Second.DisposeAsync();
        if (Plain is not null) await Plain.DisposeAsync();
        Handler.Dispose();
        Runtime.Dispose();
    }
}
