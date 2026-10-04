// PORT-ONLY, owner-ruled addition (OD-0091, OD-0092, OD-0099 Q-V-9 C; first-release voice typing, VT-6): the tiny VAD load test behind the hardware probe's "GPU backend" row (V-10.1).
namespace Marrow.Services.Port.Voice.Engine;

/// <summary>Loads the VAD model on the given runtime order and reports which runtime loaded (null when none did). Production: <see cref="WhisperNetVadLoadTest"/>.</summary>
public interface IVadLoadTest
{
    RuntimeKind? Run(IReadOnlyList<RuntimeKind> order, string? libraryRoot);
}
