using Marrow.App.Port.Voice.Cuda;
using Marrow.Services.Port.Downloads;
using Marrow.Services.Port.Voice.Models;
using Marrow.Services.Port.Voice.Probe;
using Marrow.Testing;
using Marrow.Testing.Downloads;

namespace Marrow.App.Tests.Fixtures;

// VT-17 test fixture: SK-0's fake https server on 127.0.0.1 and the guarded handler that reaches only it (no real network), the shared downloader over them, VT-7's pack installer
// under a throwaway data root, a scripted hardware probe, a scripted companions seam, and the CUDA offer over all of it.
public sealed class CudaOfferRig : IAsyncDisposable
{
    public CudaOfferRig(ScriptedMachine? machine = null, ICudaCompanions? companions = null, bool wrongSha512 = false, IHardwareProbe? probe = null, string? packBody = "serve")
    {
        var data = OperatingSystem.IsWindows() ? TestDataDirectory.Read() : Environment.GetEnvironmentVariable(TestDataDirectory.VariableName);
        if (string.IsNullOrEmpty(data)) throw new InvalidOperationException("Set MARROW_PORT_DATA_DIR to a throwaway folder before running the tests (R-TEST-9).");
        Root = Path.Combine(data, "CudaOfferTests", Guid.NewGuid().ToString("N"));
        DataRoot = Path.Combine(Root, "data");
        Directory.CreateDirectory(Root);
        Server = FakeDownloadServer.StartHttps(clock: ServerClock);
        Handler = FakeDownloadTransport.CreateHandler(Server);
        (Nupkg, Pin) = CudaPackFixture.Pack(wrongSha512);
        if (packBody == "serve") Server.Serve("/pack.nupkg", Nupkg);
        PartFolder = Path.Combine(Root, "downloads");
        Installer = new CudaPackInstaller(DataRoot, new Marrow.Services.Port.Downloads.Downloader(PartFolder, new DownloadPolicy(["127.0.0.1"]), handler: Handler), _ => Server.Url("/pack.nupkg"));
        Companions = companions as ScriptedCudaCompanions ?? new ScriptedCudaCompanions();
        Offer = new CudaOffer(probe ?? new ScriptedProbe(machine ?? ScriptedMachines.OwnersDesktop), Installer, Pin, companions ?? Companions);
        Offer.Changed += row => { lock (Seen) Seen.Add(row); };
    }

    public TestRuntime Runtime { get; } = new();

    public ManualTimeProvider ServerClock { get; } = new();

    public FakeDownloadServer Server { get; }

    public GuardedDownloadHandler Handler { get; }

    public FakeArtifact Nupkg { get; private set; }

    public CudaRuntimePack Pin { get; }

    public string Root { get; }

    public string DataRoot { get; }

    public string PartFolder { get; }

    public CudaPackInstaller Installer { get; }

    public ScriptedCudaCompanions Companions { get; }

    public CudaOffer Offer { get; }

    /// <summary>Every row the offer announced, in order.</summary>
    public List<CudaOfferRow> Seen { get; } = [];

    public IReadOnlyList<CudaOfferRow> Rows
    {
        get
        {
            lock (Seen) return [.. Seen];
        }
    }

    public async Task Until(Func<bool> condition)
    {
        using var stop = new CancellationTokenSource(FakeDownloadServer.Guard);
        while (!condition()) await Task.Delay(1, stop.Token);
    }

    public async ValueTask DisposeAsync()
    {
        Offer.Dispose();
        await Server.DisposeAsync();
        Handler.Dispose();
        Runtime.Dispose();
    }
}
