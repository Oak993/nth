// PORT-ONLY, owner-ruled addition (OD-0092, OD-0099 Q-V-13 B; first-release voice typing, VT-14): what the Voice typing page asks of the model store and the downloader (voice-SPEC V-9.4, V-9.5, V-2.4).
using Marrow.Core.Port.Voice;
using Marrow.Services.Port.Voice.Models;

namespace Marrow.App.Port.Voice;

/// <summary>
/// The Speech model list's view of the store and the downloads (VT-7's <see cref="ModelStore"/> and <see cref="ModelDownloader"/> in production, <see cref="StoreSpeechModelBackend"/>;
/// a scripted double in a test). It is a seam so the page can be built, and tested, with no folder, file or transfer: the ambient default (<see cref="SpeechModelSeams.Backend"/>)
/// fails closed.
/// </summary>
internal interface ISpeechModelBackend
{
    /// <summary>V-9.4: a stat of the file and the transfers in flight; nothing is read or hashed.</summary>
    ModelInstallState State(ModelFile file);

    /// <summary>V-9.7 and V-16.3.4: the bytes of the installed models, for the list's footer.</summary>
    long InstalledBytes { get; }

    /// <summary>V-9.5: starts the download, or joins the one already running for this file.</summary>
    ISpeechModelDownload Download(ModelFile file);

    /// <summary>Q-V-13 B, store side: removes an installed model; the one in use is refused.</summary>
    ModelRemoveResult Remove(ModelFile file);
}
