using Marrow.App.Port.Voice;
using Marrow.Core.Port.Voice;
using Marrow.Services.Port.Voice.Models;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing.Downloads;

namespace Marrow.App.Tests.Port.Voice;

// VT-14 review (voice-SPEC V-9.5, V-T95): the production backend of the Speech model list through VT-7's store and downloader, against the loopback fake server (no real network, no real model). The file is a small synthetic
// one with a manifest-style row, as ModelDownloaderTests uses.
public sealed class StoreSpeechModelBackendTests
{
    private static readonly FakeArtifact Artifact = FakeArtifact.Pattern(90_000, seed: 91);

    private static ModelFile Row() => new("tiny-q5_1", "Tiny", ModelVersion.Compact, false, Artifact.Length, Artifact.Sha256Hex);

    [Fact]
    [Trait("Spec", "V-T95")]
    [Trait("Decision", "ED-267")] // V-9.5 / V-16.3.3: a download through the backend installs the verified file, reports its fraction, and the state follows
    public async Task A_download_through_the_backend_installs_the_file_and_the_state_follows()
    {
        await using var rig = new DownloadRig();
        var file = Row();
        rig.Server.Serve("/" + file.FileName, Artifact);
        var store = new ModelStore(Path.Combine(rig.Root, "data"), () => null, rig.Runtime.Time);
        var downloader = new ModelDownloader(store, ModelDownloader.CreateDownloader(rig.PartFolder, ["127.0.0.1"], rig.Counting), f => rig.Server.Url("/" + f.FileName));
        ISpeechModelBackend backend = new StoreSpeechModelBackend(store, downloader);
        Assert.Equal(ModelInstallKind.NotInstalled, backend.State(file).Kind);

        var download = backend.Download(file);
        var fractions = new List<double>();
        download.FractionChanged += f => { lock (fractions) fractions.Add(f); };
        var status = await download.Completion.WaitAsync(FakeDownloadServer.Guard);

        Assert.Equal(ModelDownloadStatus.Installed, status);
        Assert.Equal(1.0, download.Fraction);
        Assert.Equal(ModelInstallKind.Installed, backend.State(file).Kind);
        Assert.Equal(file.Bytes, backend.InstalledBytes);
        Assert.Equal(file.Bytes, new FileInfo(store.PathFor(file)).Length);
        double[] seen;
        lock (fractions) seen = [.. fractions];
        Assert.Equal(1.0, seen[^1]);
    }

    [Fact]
    [Trait("Spec", "V-T95")]
    [Trait("Decision", "ED-267")] // V-9.5 item 3: a wrong byte is Corrupt and nothing is installed; item 4: the cross cancels
    public async Task A_corrupt_file_is_reported_and_a_cancelled_download_installs_nothing()
    {
        await using var rig = new DownloadRig();
        var file = Row();
        rig.Server.Serve("/" + file.FileName, Artifact, new FakeBehavior { WrongByteAt = 5 });
        var store = new ModelStore(Path.Combine(rig.Root, "data"), () => null, rig.Runtime.Time);
        var downloader = new ModelDownloader(store, ModelDownloader.CreateDownloader(rig.PartFolder, ["127.0.0.1"], rig.Counting), f => rig.Server.Url("/" + f.FileName));
        var backend = new StoreSpeechModelBackend(store, downloader);

        Assert.Equal(ModelDownloadStatus.Corrupt, await backend.Download(file).Completion.WaitAsync(FakeDownloadServer.Guard));
        Assert.Equal(ModelInstallKind.NotInstalled, backend.State(file).Kind);
        Assert.Equal(0, backend.InstalledBytes);

        var hold = rig.Server.HoldRequests();
        rig.Server.Behave("/" + file.FileName, new FakeBehavior());
        var download = backend.Download(file);
        await hold.WaitForArrivals(1);
        download.Cancel();
        hold.Release();
        Assert.Equal(ModelDownloadStatus.Cancelled, await download.Completion.WaitAsync(FakeDownloadServer.Guard));
        Assert.Equal(ModelInstallKind.NotInstalled, backend.State(file).Kind);
    }
}
