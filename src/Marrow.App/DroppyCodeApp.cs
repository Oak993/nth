using System;
using System.Threading;
using Marrow.App.Window;
using Marrow.Core.Support;

// Source: DroppyCode/App/DroppyCodeApp.swift (B2 1fbf81e), :1-27.
// Three of the six init() steps are PLATFORM BOUNDARY, not ported (per this unit's own packet, windows_path):
// DCInstallNotificationGuard() (:10, an AppKit NSNotificationCenter exception net, dead_code.md entry 41),
// signal(SIGPIPE, SIG_IGN) (:12, Windows reports a broken pipe on the write call itself, PA-0004), and
// WebsiteCaptures.prepare() (:14, the capture subsystem is not ported, ED-001). LegacyMigration.run() (:15) is
// NOT_APPLICABLE_WITH_EVIDENCE under OD-0008 A5 (BLK-0005). SwitchLatency.start() (:18) is inert outside a
// development build (ED-005) and is not ported. HangWatchdog.start() (:17) is the one step actually called here.
// The scene body (:21-25) has no port target: PR-WIN-001/PR-WIN-002's own windows_path says the Windows shell owns
// no scene graph and builds no menu bar (OD-0008 C3); the single AppModel this file's body would construct on
// first access to WindowManager.shared.model is constructed by WindowManager itself (App/Window/WindowManager.cs).
// :5's @NSApplicationDelegateAdaptor is Program.cs's `new AppDelegate()` handed to the shell (Port/AppShell.cs); the
// reopen-on-second-launch path ED-002 requires is AppDelegate.swift:36-39, reached through Port/ReopenSignal.cs.

namespace Marrow.App;

/// <summary>:3-26 <c>@main struct DroppyCodeApp: App</c>: the launch prologue and the (menus-only) scene.</summary>
public static class DroppyCodeApp
{
    // :7-19 init(): the ordered prologue, minus the three platform-boundary steps and the two inert/not-ported ones.
    public static void RunPrologue() =>
        HangWatchdog.Start(); // :17

    // ED-002 [LAUNCH]: macOS gets single-instance behavior from Launch Services for free; the source contributes
    // no lock of its own, so doing nothing here would be a real divergence (two processes sharing one
    // unsynchronised store root, Storage.swift:20, with last-writer-wins on the library). The port adds a named
    // Mutex, one per build identity so a Dev build and a Release build never contend with each other, matching
    // AppInfo.isDevelopment's own "iordv.droppycode" vs "iordv.droppycode.dev" split (project.yml, AssemblyInfo.cs).
#if DEBUG
    private const string SingleInstanceLockName = "Local\\Marrow.SingleInstance.Dev";
    internal const string ReopenSignalName = "Local\\Marrow.Reopen.Dev";
#else
    private const string SingleInstanceLockName = "Local\\Marrow.SingleInstance.Release";
    internal const string ReopenSignalName = "Local\\Marrow.Reopen.Release";
#endif

    /// <summary>
    /// True and holding <paramref name="handle"/> when this is the only running instance; false when another
    /// instance already holds the lock, in which case the caller must dispose nothing (there is nothing to
    /// dispose) and exit. The reopen path ED-002 specifies for that case - bring the existing main window forward
    /// and activate the app, AppDelegate.swift:36-39's applicationShouldHandleReopen - is Program.cs's: the second
    /// launch sets the running instance's <see cref="ReopenSignalName"/> signal (Port/ReopenSignal.cs) before it exits.
    /// </summary>
    public static bool TryAcquireSingleInstanceLock(out Mutex? handle) =>
        TryAcquireSingleInstanceLock(SingleInstanceLockName, out handle);

    /// <summary>The <paramref name="lockName"/> overload exists so a test can use a throwaway name instead of the
    /// real app-wide one.</summary>
    internal static bool TryAcquireSingleInstanceLock(string lockName, out Mutex? handle)
    {
        var mutex = new Mutex(initiallyOwned: true, name: lockName, createdNew: out var createdNew);
        if (createdNew)
        {
            handle = mutex;
            return true;
        }
        mutex.Dispose();
        handle = null;
        return false;
    }
}
