// PORT-ONLY, owner-ruled addition (OD-0092, OD-0096; first-release voice typing, VT-15): the preparation's model source on VT-7's store and downloader.
using Marrow.Core.Port.Voice;
using Marrow.Services.Port.Voice.Models;

namespace Marrow.App.Port.Voice;

/// <summary><see cref="IVoiceModelSource"/> over <see cref="ModelStore"/> and <see cref="ModelDownloader"/>.</summary>
public sealed class StoreModelSource(ModelStore store, ModelDownloader downloader) : IVoiceModelSource
{
    public bool IsInstalled(ModelFile file) => throw new NotImplementedException();

    public string PathFor(ModelFile file) => throw new NotImplementedException();

    public Task<ModelDownloadResult> InstallAsync(ModelFile file, Action<double> fraction, CancellationToken cancellationToken) => throw new NotImplementedException();

    public Task<ModelVerification> VerifyAsync(ModelFile file, CancellationToken cancellationToken) => throw new NotImplementedException();
}
