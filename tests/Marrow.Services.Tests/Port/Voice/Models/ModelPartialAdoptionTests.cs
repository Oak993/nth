using Marrow.Core.Port;
using Marrow.Core.Port.Voice;
using Marrow.Services.Port.Voice.Models;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing.Downloads;

namespace Marrow.Services.Tests.Port.Voice.Models;

// VT-7 / ED-NEW-dlresume-1 (voice-SPEC V-9.5 items 1 and 3, V-T43): a "<file>.partial" beside the model, the spec's name for a partial, is taken into the
// shared downloader's own partial slot (ED-305 (2)) and resumed with Range; whatever resumes is verified whole before it is installed. The seeded files
// are small synthetic ones; no step touches the network.
[Trait("Spec", "V-T43")]
public sealed class ModelPartialAdoptionTests
{
    private static readonly FakeArtifact Tiny = FakeArtifact.Pattern(120_000, seed: 81);

    private static (ModelStore Store, ModelDownloader Downloader, ModelFile Model) Make(DownloadRig rig, string id = "tiny-q5_1")
    {
        var model = VoiceModelFixtures.Model(Tiny, id);
        rig.Server.Serve("/" + model.FileName, Tiny);
        var store = new ModelStore(Path.Combine(rig.Root, "data"), () => null, rig.Runtime.Time);
        Directory.CreateDirectory(store.Folder);
        var downloader = new ModelDownloader(store, ModelDownloader.CreateDownloader(rig.PartFolder, ["127.0.0.1"], rig.Counting), f => rig.Server.Url("/" + f.FileName));
        return (store, downloader, model);
    }

    private static string Beside(ModelStore store, ModelFile model) => Path.Combine(store.Folder, model.FileName + ".partial");

    private static string Own(DownloadRig rig) => rig.PartPath(Tiny);

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "VT-7")]
    public async Task A_partial_beside_the_model_with_the_wrong_bytes_is_verified_away_and_the_file_is_fetched_whole()
    {
        await using var rig = new DownloadRig();
        var (store, downloader, model) = Make(rig);
        await File.WriteAllBytesAsync(Beside(store, model), Enumerable.Repeat((byte)0xEE, 40_000).ToArray());

        var result = await downloader.Start(model).Completion.WaitAsync(FakeDownloadServer.Guard);

        Assert.Equal(ModelDownloadStatus.Installed, result.Status);
        var requests = await rig.Server.WaitForRequests(2);
        Assert.Equal([(40_000L, 206), ((long?)null, 200)], requests.Select(r => (r.RangeStart, r.Status)).ToArray()); // the resume, then the one silent restart
        Assert.Equal(Tiny.Bytes, await File.ReadAllBytesAsync(store.PathFor(model)));
        Assert.Equal(model.Id, Assert.Single(store.Records).Id);
        Assert.False(File.Exists(Beside(store, model)));
        Assert.Empty(Directory.GetFiles(rig.PartFolder));
    }

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "VT-7")]
    public async Task A_partial_beside_the_model_longer_than_the_pinned_size_cannot_be_a_prefix_and_the_file_is_fetched_whole()
    {
        await using var rig = new DownloadRig();
        var (store, downloader, model) = Make(rig);
        await File.WriteAllBytesAsync(Beside(store, model), FakeArtifact.Pattern(130_000, seed: 81).Bytes);

        var result = await downloader.Start(model).Completion.WaitAsync(FakeDownloadServer.Guard);

        Assert.Equal(ModelDownloadStatus.Installed, result.Status);
        var request = Assert.Single(await rig.Server.WaitForRequests(1));
        Assert.Equal(((long?)null, 200, 120_000L), (request.RangeStart, request.Status, request.BodyBytesSent));
        Assert.Equal(Tiny.Bytes, await File.ReadAllBytesAsync(store.PathFor(model)));
    }

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "VT-7")]
    public async Task The_downloaders_own_partial_is_never_replaced_by_one_beside_the_model()
    {
        await using var rig = new DownloadRig();
        var (store, downloader, model) = Make(rig);
        Directory.CreateDirectory(rig.PartFolder);
        await File.WriteAllBytesAsync(Own(rig), Tiny.Bytes[..30_000]);
        await File.WriteAllBytesAsync(Beside(store, model), Tiny.Bytes[..50_000]);

        var result = await downloader.Start(model).Completion.WaitAsync(FakeDownloadServer.Guard);

        Assert.Equal(ModelDownloadStatus.Installed, result.Status);
        var request = Assert.Single(await rig.Server.WaitForRequests(1));
        Assert.Equal((30_000L, 206, 90_000L), (request.RangeStart, request.Status, request.BodyBytesSent));
        Assert.Equal(50_000, new FileInfo(Beside(store, model)).Length); // bytes this download did not write are neither taken over nor deleted
        Assert.Equal(Tiny.Bytes, await File.ReadAllBytesAsync(store.PathFor(model)));
    }

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "VT-7")]
    public async Task An_empty_partial_of_the_downloader_does_not_shadow_one_beside_the_model()
    {
        await using var rig = new DownloadRig();
        var (store, downloader, model) = Make(rig);
        Directory.CreateDirectory(rig.PartFolder);
        await File.WriteAllBytesAsync(Own(rig), []);
        await File.WriteAllBytesAsync(Beside(store, model), Tiny.Bytes[..40_000]);

        var result = await downloader.Start(model).Completion.WaitAsync(FakeDownloadServer.Guard);

        Assert.Equal(ModelDownloadStatus.Installed, result.Status);
        var request = Assert.Single(await rig.Server.WaitForRequests(1));
        Assert.Equal((40_000L, 206, 80_000L), (request.RangeStart, request.Status, request.BodyBytesSent));
        Assert.Equal(Tiny.Bytes, await File.ReadAllBytesAsync(store.PathFor(model)));
        Assert.False(File.Exists(Beside(store, model)));
    }

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "VT-7")]
    public async Task A_partial_beside_the_model_that_cannot_be_moved_is_left_alone_logged_without_its_name_and_the_download_starts_over()
    {
        if (!OperatingSystem.IsWindows()) return; // a rename of an open file succeeds elsewhere; the refusal this pins is Windows' sharing violation
        const string secret = "SENTINEL-SECRET";
        await using var rig = new DownloadRig();
        var (store, downloader, model) = Make(rig, "tiny-" + secret);
        var lines = new List<string>();
        using var scope = Logger.Sink.Override((message, _) => { lock (lines) lines.Add(message); });
        ModelDownloadResult result;
        await using (var held = new FileStream(Beside(store, model), FileMode.Create, FileAccess.ReadWrite, FileShare.None))
        {
            await held.WriteAsync(Tiny.Bytes.AsMemory(0, 40_000)); // a scanner or another process holding the file: Windows refuses to move it
            await held.FlushAsync();
            result = await downloader.Start(model).Completion.WaitAsync(FakeDownloadServer.Guard);
        }

        Assert.Equal(ModelDownloadStatus.Installed, result.Status);
        var request = Assert.Single(await rig.Server.WaitForRequests(1));
        Assert.Equal(((long?)null, 200), (request.RangeStart, request.Status));
        Assert.True(File.Exists(Beside(store, model)));
        lock (lines)
        {
            Assert.Contains(lines, l => l.StartsWith("voice model partial not adopted", StringComparison.Ordinal));
            Assert.DoesNotContain(lines, l => l.Contains(secret, StringComparison.Ordinal) || l.Contains(rig.Root, StringComparison.OrdinalIgnoreCase));
        }
    }
}
