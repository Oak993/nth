// PORT-ONLY, owner-ruled addition (OD-0092, OD-0096; first-release voice typing, VT-15): the preparation's model source on VT-7's store and downloader.
using Marrow.Core.Port.Voice;
using Marrow.Services.Port.Voice.Models;

namespace Marrow.App.Port.Voice;

/// <summary><see cref="IVoiceModelSource"/> over <see cref="ModelStore"/> and <see cref="ModelDownloader"/>.</summary>
public sealed class StoreModelSource(ModelStore store, ModelDownloader downloader) : IVoiceModelSource
{
    public bool IsInstalled(ModelFile file) => store.State(file).Kind is ModelInstallKind.Installed or ModelInstallKind.InUse;

    public string PathFor(ModelFile file) => store.PathFor(file);

    public async Task<ModelDownloadResult> InstallAsync(ModelFile file, Action<double> fraction, CancellationToken cancellationToken)
    {
        var download = downloader.Start(file);
        download.FractionChanged += fraction;
        using var cancel = cancellationToken.Register(download.Cancel);
        try { return await download.Completion; }
        finally { download.FractionChanged -= fraction; }
    }

    public Task<ModelVerification> VerifyAsync(ModelFile file, CancellationToken cancellationToken) => store.VerifyForLoad(file, cancellationToken);
}
