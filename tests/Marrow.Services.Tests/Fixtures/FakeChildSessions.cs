using Marrow.Services.Port.PcControl.Workspace;

namespace Marrow.Services.Tests.Fixtures;

// PORT-ONLY: Windows' child-session switch for tests (unit P18, ED-NEW-pcws-2). Reads answer Enabled; every write is
// recorded, so a test proves nth wrote nothing unless the user's own command ran elevated. Never touches Windows.
public sealed class FakeChildSessions : IChildSessionReadiness, IChildSessionSwitch
{
    public bool? On { get; set; }

    public int Reads { get; private set; }

    public List<bool> Writes { get; } = [];

    /// <summary>The Windows error a write fails with; 0: writes succeed.</summary>
    public int FailWith { get; set; }

    public bool? Enabled()
    {
        Reads++;
        return On;
    }

    public bool Set(bool enabled, out int error)
    {
        Writes.Add(enabled);
        error = FailWith;
        if (FailWith != 0) return false;
        On = enabled;
        return true;
    }
}
