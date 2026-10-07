using Marrow.Services.Port.Voice.Models;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing.Downloads;

namespace Marrow.Services.Tests.Port.Voice.Models;

// GAP-2, V-T43 / V-9.5: the voice spec explicitly names <file>.partial, independently of the shared skills downloader's .part convention.
public sealed class GapModelDownloadAcceptanceTests
{
    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "VT-7")]
    [Trait("Spec", "V-T43")]
    public async Task GAP_A_voice_model_partial_is_resumed_with_Range_and_refetches_no_byte()
    {
        await using var rig = new DownloadRig();
        var artifact = FakeArtifact.Pattern(120_000, seed: 81);
        var model = VoiceModelFixtures.Model(artifact);
        rig.Server.Serve("/" + model.FileName, artifact);
        var store = new ModelStore(Path.Combine(rig.Root, "data"), () => null, rig.Runtime.Time);
        Directory.CreateDirectory(store.Folder);
        await File.WriteAllBytesAsync(Path.Combine(store.Folder, model.FileName + ".partial"), artifact.Bytes[..40_000]);
        var downloader = new ModelDownloader(store, ModelDownloader.CreateDownloader(rig.PartFolder, ["127.0.0.1"], rig.Counting),
            f => rig.Server.Url("/" + f.FileName));
        var result = await downloader.Start(model).Completion.WaitAsync(FakeDownloadServer.Guard);
        Assert.Equal(ModelDownloadStatus.Installed, result.Status);
        var request = Assert.Single(await rig.Server.WaitForRequests(1));
        Assert.Equal((40_000L, 206, 80_000L), (request.RangeStart, request.Status, request.BodyBytesSent));
        Assert.Equal(artifact.Bytes, await File.ReadAllBytesAsync(store.PathFor(model)));
        Assert.False(File.Exists(Path.Combine(store.Folder, model.FileName + ".partial")));
    }
}
