using System;
using System.Threading;
using Marrow.App.Window;
using Marrow.Core.Support;
using Marrow.App.Port;
using Marrow.Core.Port;
using System.Security.Cryptography;
using System.Text;

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
    public static void RunPrologue()
    {
        HangWatchdog.Start(); // :17
        // PORT-ONLY (web surfaces): the workspace panes this build can draw (DESIGN 3.1); each pane unit registers its own.
        Marrow.App.Views.Diff.DiffPane.Register(); // W4
        Marrow.App.Views.Terminal.TerminalPane.Register(); // W4
        Marrow.App.Views.Browser.VisusPane.Register(); // W8a
    }

    // ED-002 [LAUNCH]: macOS gets single-instance behavior from Launch Services for free; the source contributes
    // no lock of its own, so doing nothing here would be a real divergence (two processes sharing one
    // unsynchronised store root, Storage.swift:20, with last-writer-wins on the library). The port adds a named
    // Mutex, one per build identity so a Dev build and a Release build never contend with each other, matching
    // AppInfo.isDevelopment's own "iordv.droppycode" vs "iordv.droppycode.dev" split (project.yml, AssemblyInfo.cs).
#if DEBUG
    private const string BaseSingleInstanceLockName = "Local\\Marrow.SingleInstance.Dev";
    private const string BaseReopenSignalName = "Local\\Marrow.Reopen.Dev";
#else
    private const string BaseSingleInstanceLockName = "Local\\Marrow.SingleInstance.Release";
    private const string BaseReopenSignalName = "Local\\Marrow.Reopen.Release";
#endif

    // ED-212: a Debug launch under MARROW_DEV_DATA_ROOT gets its own lock and reopen names, tagged with a hash of
    // its dev root, so two dev launches on different roots never contend with each other's single-instance lock or
    // signal each other's reopen. Release, and a Debug launch with no active redirect, keep the literal above
    // unchanged (DevDataRoot.KernelObjectName is the identity when not redirected).
    private static string SingleInstanceLockName => DevDataRoot.KernelObjectName(BaseSingleInstanceLockName);
    internal static string ReopenSignalName => DevDataRoot.KernelObjectName(BaseReopenSignalName);

    // AppDelegate.swift (B3 52bc792):77-92, ED-002 / ED-220 (6). Reuse the existing build/root instance identity;
    // payloads have their own channel so an update click never also runs the ordinary reopen/show-main path.
    internal static string ToastPipeName => "Marrow.Toast." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ReopenSignalName)));
    internal static bool ForwardNotificationArguments(IReadOnlyList<string> arguments) =>
        ToastSecondLaunch.Forward(ToastLaunchTransport.Current.Current, ToastPipeName, WindowsToastStartup.Identity, arguments);
    internal static IDisposable ListenForNotificationArguments(AppDelegate appDelegate, Task? launchReady = null) =>
        ToastLaunchTransport.Current.Current.Listen(ToastPipeName, payload =>
            MainActor.Spawn(async () =>
            {
                if (launchReady is not null) await launchReady;
                await ToastSecondLaunch.Route(appDelegate, WindowsToastStartup.Identity, payload);
            }));
    internal static void ReceiveNotificationArguments(AppDelegate appDelegate, IReadOnlyList<string> arguments)
    {
        foreach (var argument in arguments)
            if (ToastActivation.Decode(WindowsToastStartup.Identity, argument) is not null)
            {
                MainActor.Spawn(async () => await ToastSecondLaunch.Route(appDelegate, WindowsToastStartup.Identity, argument));
                break;
            }
    }

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
