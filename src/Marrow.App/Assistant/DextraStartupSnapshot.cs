// PORT-ONLY: DESIGN.md 15.1, 15.3; ED-NEW-dextra-open-6.
// MainActor captures app facts; persisted services and the controller are built on the worker.
using Marrow.Services.Assistant;
namespace Marrow.App.Assistant;
public sealed record DextraStartupSnapshot(IReadOnlyList<Guid> HomeChats, IReadOnlyList<MemoryImportSource> ImportSources);
