namespace Marrow.App.Views.Composer;

// Result bound to the managed context's lifetime; diagnostics never substitute for this result.
internal sealed record GpuProgramCacheInstall(string? State, string? Folder);
