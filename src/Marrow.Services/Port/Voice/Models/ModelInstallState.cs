// PORT-ONLY, owner-ruled addition (OD-0092, OD-0099 Q-V-9 C and Q-V-13 B; first-release voice typing, VT-7): install state per model id (voice-SPEC V-9.4).
namespace Marrow.Services.Port.Voice.Models;

/// <summary>The state of one model file; <see cref="Fraction"/> (0 to 1) is meaningful while Downloading.</summary>
public readonly record struct ModelInstallState(ModelInstallKind Kind, double Fraction = 0);
