// PORT-ONLY, owner-ruled addition (OD-0092, OD-0099 Q-V-13 B; first-release voice typing, VT-14): the production backend of the Speech model list.
using Marrow.Core.Port.Voice;
using Marrow.Services.Port.Voice.Models;

namespace Marrow.App.Port.Voice;

/// <summary><see cref="ISpeechModelBackend"/> over VT-7's store and downloader, which the app's start-up composition builds on the data root.</summary>
internal sealed class StoreSpeechModelBackend(ModelStore store, ModelDownloader downloader) : ISpeechModelBackend
{
    public ModelInstallState State(ModelFile file) => store.State(file);

    public long InstalledBytes => store.InstalledBytes;

    public ISpeechModelDownload Download(ModelFile file) => new ModelDownloadHandle(downloader.Start(file));

    public ModelRemoveResult Remove(ModelFile file) => store.Remove(file);
}
