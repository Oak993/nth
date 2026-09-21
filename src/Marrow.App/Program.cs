namespace Marrow.App;

/// <summary>
/// STUB ENTRY POINT, tracked as incomplete. Replaced by the M6 app-shell task that ports
/// DroppyCode/App/DroppyCodeApp.swift and AppDelegate.swift. It exists only so the solution builds
/// while the lower layers are ported. Contract section 13: temporary stubs must be visibly tracked
/// and removed before acceptance; Gate C will fail while this marker remains.
/// </summary>
public static class Program
{
    public const string StubMarker = "MARROW_STUB_ENTRY_POINT";

    [STAThread]
    public static int Main(string[] args) => 0;
}
