// PORT-ONLY: UE-3; exact process exit and both captured streams from trial H.
namespace Marrow.Services.Port.Updates;

public sealed record DevToolCommandResult(int? ExitCode, string Stdout, string Stderr);
