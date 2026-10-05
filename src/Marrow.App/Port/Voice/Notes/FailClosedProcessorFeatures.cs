// PORT-ONLY, owner-ruled addition (OD-0106 Notes A Plain, OD-0099 Q-V-7 A; first-release voice typing, VT-17): the fail-closed default of the processor seam (V-2.5).
using Marrow.Core.Port.Voice.Seams;

namespace Marrow.App.Port.Voice.Notes;

public sealed class FailClosedProcessorFeatures : IProcessorFeatures
{
    public bool Avx2Supported => throw new SeamNotBoundException(nameof(IProcessorFeatures), nameof(Avx2Supported));
}
