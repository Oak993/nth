// PORT-ONLY, owner-ruled addition (OD-0091, OD-0092, OD-0099 Q-V-9 C; first-release voice typing, VT-6): the fail-closed default of the VAD native seam (V-2.5).
using Marrow.Core.Port.Voice.Seams;

namespace Marrow.Services.Port.Voice.Engine;

/// <summary><see cref="IVadNative"/> default: calls no native function.</summary>
public sealed class FailClosedVadNative : IVadNative
{
    public float[] Detect(ReadOnlySpan<float> samples) => throw new SeamNotBoundException(nameof(IVadNative), nameof(Detect));

    public void Dispose()
    {
    }
}
