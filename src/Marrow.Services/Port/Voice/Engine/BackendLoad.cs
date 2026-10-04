// PORT-ONLY, owner-ruled addition (OD-0091, OD-0092, OD-0099 Q-V-9 C; first-release voice typing, VT-6): what the engine asks the backend to load (V-7.1).
namespace Marrow.Services.Port.Voice.Engine;

/// <summary>A model load: the verified model file, whether the GPU may be used, the runtime order, and the library root for the download folder.</summary>
public sealed record BackendLoad(string ModelPath, bool UseGpu, IReadOnlyList<RuntimeKind> RuntimeOrder, string? LibraryRoot);
