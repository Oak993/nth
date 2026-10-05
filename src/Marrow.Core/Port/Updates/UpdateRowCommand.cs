// PORT-ONLY: first-release feature unit UE-4 (OD-0100; the U1 failure line "... Retry, or run <command> in a terminal."). The command a person can run by hand to update an item the orchestrator lists.
namespace Marrow.Core.Port.Updates;

public static class UpdateRowCommand
{
    /// <summary>
    /// The command that updates the row's item by hand, or null when it has none. Only the provider CLIs that carry their own updater have one (ProviderRegistry.UpdatableProviders; ProviderRegistry.UpdateCLI
    /// runs "&lt;cli&gt; update"): the ids of those items are "cli:claude" and "cli:codex" (ProviderCliUpdateItem), the commands "claude update" and "codex update".
    /// </summary>
    public static string? For(UpdateRow row) =>
        row.Kind == UpdateItemKind.ProviderCli && row.Id.StartsWith("cli:", StringComparison.Ordinal) && row.Id[4..] is "claude" or "codex" ? row.Id[4..] + " update" : null;
}
