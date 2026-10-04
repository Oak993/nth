// PORT-ONLY, owner-ruled addition (OD-0091, OD-0092, OD-0099 Q-V-9 C; first-release voice typing, VT-6): the fail-closed default of the engine's backend seam (V-2.5).
using Marrow.Core.Port.Voice.Seams;

namespace Marrow.Services.Port.Voice.Engine;

/// <summary><see cref="IWhisperBackend"/> default: loads no native library and no model file.</summary>
public sealed class FailClosedWhisperBackend : IWhisperBackend
{
    public Task<IWhisperModel> LoadAsync(BackendLoad load, CancellationToken cancellationToken) =>
        throw new SeamNotBoundException(nameof(IWhisperBackend), nameof(LoadAsync));
}
