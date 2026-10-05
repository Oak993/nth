// PORT-ONLY, owner-ruled addition (OD-0092, OD-0096; first-release voice typing, VT-15, voice-SPEC V-10.7): where the preparation gets a model file.
using Marrow.Core.Port.Voice;
using Marrow.Services.Port.Voice.Models;

namespace Marrow.App.Port.Voice;

/// <summary>
/// The model store and downloader as the preparation uses them. Production: <see cref="StoreModelSource"/>. A test's double throws on any transfer, so no test reaches the
/// network (D3 with consent: nothing downloads before the owner's yes).
/// </summary>
public interface IVoiceModelSource
{
    bool IsInstalled(ModelFile file);

    string PathFor(ModelFile file);

    /// <summary>Downloads the file; <paramref name="fraction"/> gets the share received as it grows.</summary>
    Task<ModelDownloadResult> InstallAsync(ModelFile file, Action<double> fraction, CancellationToken cancellationToken);

    Task<ModelVerification> VerifyAsync(ModelFile file, CancellationToken cancellationToken);
}
