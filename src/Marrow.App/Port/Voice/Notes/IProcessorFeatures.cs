// PORT-ONLY, owner-ruled addition (OD-0106 Notes A Plain, OD-0099 Q-V-7 A; first-release voice typing, VT-17): the processor seam (AVX2 is what the local engine needs, V-3.2).
namespace Marrow.App.Port.Voice.Notes;

public interface IProcessorFeatures
{
    bool Avx2Supported { get; }
}
