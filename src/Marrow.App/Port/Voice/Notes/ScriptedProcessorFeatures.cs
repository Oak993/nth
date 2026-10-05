// PORT-ONLY, owner-ruled addition (OD-0106 Notes A Plain, OD-0099 Q-V-7 A; first-release voice typing, VT-17): the processor double.
namespace Marrow.App.Port.Voice.Notes;

public sealed class ScriptedProcessorFeatures(bool avx2) : IProcessorFeatures
{
    public bool Avx2Supported { get; set; } = avx2;
}
