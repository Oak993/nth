// PORT-ONLY, owner-ruled addition (OD-0091, OD-0092, OD-0099 Q-V-9 C; first-release voice typing, VT-6): what the engine knows about the runtimes on this machine (V-7.1).
namespace Marrow.Services.Port.Voice.Engine;

/// <summary>
/// The runtimes that are really installed, whether the graphics adapter is NVIDIA, and the library root (the data root that holds
/// <c>runtimes\cuda12\win-x64</c>; null when no download folder is in play).
/// </summary>
public sealed record RuntimeFacts(IReadOnlyList<RuntimeKind> Installed, bool NvidiaPresent, string? LibraryRoot = null);
