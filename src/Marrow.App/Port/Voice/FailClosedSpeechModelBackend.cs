// PORT-ONLY, owner-ruled addition (OD-0092; first-release voice typing, VT-14): the fail-closed default of the Speech model list's backend (voice-SPEC V-2.5).
using Marrow.Core.Port.Voice;
using Marrow.Core.Port.Voice.Seams;
using Marrow.Services.Port.Voice.Models;

namespace Marrow.App.Port.Voice;

/// <summary>Nothing is installed and nothing can be fetched or removed: a download throws <see cref="SeamNotBoundException"/>, and no folder, file or connection is touched.</summary>
internal sealed class FailClosedSpeechModelBackend : ISpeechModelBackend
{
    public ModelInstallState State(ModelFile file) => new(ModelInstallKind.NotInstalled);

    public long InstalledBytes => 0;

    public ISpeechModelDownload Download(ModelFile file) => throw new SeamNotBoundException(nameof(ISpeechModelBackend), nameof(Download));

    public ModelRemoveResult Remove(ModelFile file) => ModelRemoveResult.NotInstalled;
}
