namespace Marrow.App.Port;

// PORT-ONLY: ED-NEW-inst-c7-6. A callback retains its retirement state even if the tombstone lookup is pruned.
internal sealed class NthInstallerQuit
{
    internal required string Id { get; init; }
    internal bool NeedsIdle { get; init; }
    internal bool Cancelled { get; set; }
    internal bool Committed { get; set; }
}
