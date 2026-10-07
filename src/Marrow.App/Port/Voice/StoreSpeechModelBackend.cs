// PORT-ONLY, owner-ruled addition (OD-0092, OD-0099 Q-V-13 B; first-release voice typing, VT-14): the production backend of the Speech model list.
using Marrow.Core.Port.Voice;
using Marrow.Services.Port.Voice.Models;

namespace Marrow.App.Port.Voice;

/// <summary>
/// <see cref="ISpeechModelBackend"/> over VT-7's store and downloader. The pair is made on first use (the start-up composition builds the backend and opens no folder), and the downloader is
/// the one the store's files go through, so a finished download is recorded in the store the list reads.
/// </summary>
internal sealed class StoreSpeechModelBackend : ISpeechModelBackend, IVoiceModelSource
{
    private readonly Lazy<(ModelStore Store, ModelDownloader Downloader)> parts;

    public StoreSpeechModelBackend(Func<(ModelStore Store, ModelDownloader Downloader)> make) =>
        parts = new Lazy<(ModelStore, ModelDownloader)>(make, LazyThreadSafetyMode.ExecutionAndPublication);

    public StoreSpeechModelBackend(ModelStore store, ModelDownloader downloader) : this(() => (store, downloader))
    {
    }

    public ModelInstallState State(ModelFile file) => parts.Value.Store.State(file);

    public long InstalledBytes => parts.Value.Store.InstalledBytes;

    public ISpeechModelDownload Download(ModelFile file) => new ModelDownloadHandle(parts.Value.Downloader.Start(file));

    public ModelRemoveResult Remove(ModelFile file) => parts.Value.Store.Remove(file);

    public bool IsInstalled(ModelFile file) => State(file).Kind is ModelInstallKind.Installed or ModelInstallKind.InUse;
    public string PathFor(ModelFile file) => parts.Value.Store.PathFor(file);
    public Task<ModelVerification> VerifyAsync(ModelFile file, CancellationToken cancellationToken) => parts.Value.Store.VerifyForLoad(file, cancellationToken);
    public Task<ModelDownloadResult> InstallAsync(ModelFile file, Action<double> fraction, CancellationToken cancellationToken) =>
        new StoreModelSource(parts.Value.Store, parts.Value.Downloader).InstallAsync(file, fraction, cancellationToken);
}
