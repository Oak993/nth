using Marrow.App.Window;

namespace Marrow.App;

/// <summary>
/// PARTIAL ENTRY POINT, still tracked as incomplete. DroppyCode/App/DroppyCodeApp.swift is now ported
/// (DroppyCodeApp.cs: the prologue and the single-instance lock ED-002 adds), but DroppyCode/App/AppDelegate.swift
/// is a separate, not yet started unit, and App.Window.WindowManager (which constructs the single AppModel and
/// shows the main window) is still MARROW_STUB. Running this Main() therefore acquires the lock, runs the real
/// prologue, and then throws from WindowManager.Shared - a real, visible failure that traces to exactly which
/// unported unit is missing, rather than the silent `return 0` this replaced. Contract section 13: still not
/// Gate C clean until AppDelegate and WindowManager both land.
/// </summary>
public static class Program
{
    public const string StubMarker = "MARROW_STUB_ENTRY_POINT";

    [STAThread]
    public static int Main(string[] args)
    {
        if (!DroppyCodeApp.TryAcquireSingleInstanceLock(out var singleInstanceLock)) return 0; // ED-002
        using var _ = singleInstanceLock;

        DroppyCodeApp.RunPrologue();

        // :21-25 the scene's only real effect: the first access to WindowManager.shared constructs the single
        // AppModel and, from there, the main window. Both still MARROW_STUB pending App.Window.WindowManager.
        WindowManager.Shared.ShowMain();
        return 0;
    }
}
