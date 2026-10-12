namespace Marrow.App.Port;

// PORT-ONLY: ED-NEW-inst-c7-5. One admission reservation and its delayed commit.
internal sealed class NthInstallerQuit
{
    internal bool NeedsIdle { get; init; } = true;
    internal bool Cancelled { get; set; }
    internal bool Committed { get; set; }
}
