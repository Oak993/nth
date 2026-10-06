// PORT-ONLY: UE-3, OD-0100; the argv recorded by supervised trial H, never shell text.
namespace Marrow.Services.Port.Updates;

public sealed record DevToolCommand(string Executable, IReadOnlyList<string> Arguments);
