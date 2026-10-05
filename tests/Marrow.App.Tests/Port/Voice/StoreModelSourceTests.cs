using Marrow.App.Port.Voice;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Voice;
using Marrow.Services.Port.Downloads;
using Marrow.Services.Port.Voice.Models;
using Marrow.Services.Port.Voice.Probe;

namespace Marrow.App.Tests.Port.Voice;

// VT-15 (V-10.7, V-9.5; D3 with consent): the preparation over the real model store and downloader, with a transport that throws. Nothing reaches it before consent; after consent the failure
// is the download's own and nothing is stored.
public sealed class StoreModelSourceTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "marrow-vt15-" + Guid.NewGuid().ToString("N"));
    private readonly VoicePrepThrowingHandler handler = new();

    public void Dispose()
    {
        try { Directory.Delete(root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private (VoicePrepRig Rig, VoicePreparation Preparation, StoreModelSource Source) Build()
    {
        var rig = new VoicePrepRig(ScriptedMachines.OwnersDesktop, new Dictionary<string, double> { ["large-v3-turbo-q8_0"] = 0.6 });
        var store = new ModelStore(Path.Combine(root, "data"), () => null, rig.Clock);
        var downloader = new Marrow.Services.Port.Downloads.Downloader(Path.Combine(root, "part"), new DownloadPolicy(ModelDownloader.AllowedHosts), new DownloadOptions(), handler);
        var source = new StoreModelSource(store, new ModelDownloader(store, downloader));
        return (rig, new VoicePreparation(new ScriptedProbe(ScriptedMachines.OwnersDesktop), source, () => rig.Engine, rig.Settings, () => new float[10], "a", "r", rig.Clock), source);
    }

    [Fact]
    [Trait("Feature", "VT-15")]
    [Trait("Decision", "ED-289")]
    public async Task Without_consent_the_transport_is_never_reached()
    {
        var (rig, preparation, _) = Build();
        var profile = await preparation.ProbeAsync(null, CancellationToken.None);
        var result = await preparation.InstallAndTimeAsync("large-v3-turbo-q8_0", profile, null, CancellationToken.None);
        Assert.Equal(VoicePreparationStatus.AwaitingConsent, result.Status);
        Assert.Equal(0, handler.Calls);
        Assert.Equal(0, rig.Settings.RecordWrites);
    }

    [Fact]
    [Trait("Feature", "VT-15")]
    [Trait("Decision", "ED-289")]
    public async Task After_consent_a_transport_failure_fails_the_run_and_stores_nothing()
    {
        var (rig, preparation, source) = Build();
        preparation.GrantConsent();
        var profile = await preparation.ProbeAsync(null, CancellationToken.None);
        var result = await preparation.InstallAndTimeAsync("large-v3-turbo-q8_0", profile, null, CancellationToken.None);
        Assert.Equal(VoicePreparationStatus.Failed, result.Status);
        Assert.True(handler.Calls >= 1);
        Assert.Equal(0, rig.Settings.RecordWrites);
        Assert.Empty(rig.Engine.Loads);
        Assert.False(source.IsInstalled(ModelCatalog.Find("large-v3-turbo-q8_0")!));
    }

    [Fact]
    [Trait("Feature", "VT-15")]
    [Trait("Decision", "ED-289")]
    public void The_path_is_the_stores_and_a_missing_file_does_not_verify()
    {
        var (_, _, source) = Build();
        var file = ModelCatalog.Find("base.en")!;
        Assert.Equal(Path.Combine(root, "data", "models", "whisper", "ggml-base.en.bin"), source.PathFor(file));
        Assert.False(source.IsInstalled(file));
        Assert.Equal(ModelVerification.Missing, source.VerifyAsync(file, CancellationToken.None).GetAwaiter().GetResult());
    }
}
