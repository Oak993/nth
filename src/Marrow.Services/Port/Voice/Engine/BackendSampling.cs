// PORT-ONLY, owner-ruled addition (OD-0091, OD-0092, OD-0099 Q-V-9 C; first-release voice typing, VT-6): the two sampling strategies of V-7.1.
namespace Marrow.Services.Port.Voice.Engine;

/// <summary>Live passes are greedy; the final pass is a beam search (V-7.1).</summary>
public enum BackendSampling { Greedy, BeamSearch }
