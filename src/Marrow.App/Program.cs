using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Marrow.App.Port;
using Marrow.Core.Port;
using Marrow.Services.Port;
using Marrow.Services.Store;
using Marrow.UI.Markdown;
using Marrow.UI.Port;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App;

/// <summary>
/// PORT-ONLY entry point: what `@main struct DroppyCodeApp: App` (DroppyCode/App/DroppyCodeApp.swift, B3 52bc792) and
/// NSApplicationMain do on the Mac, in this order:
/// <list type="number">
/// <item>ED-002: the single-instance lock; a second launch hands the reopen to the running instance and exits.</item>
/// <item>The process-wide bindings a Swift process has from its first instruction (UserDefaults.standard, Storage's
/// roots, the main actor, the frame clock, the openURL environment default), each a trap until bound.</item>
/// <item>The Avalonia application on the classic desktop lifetime (NSApplication), set up but not yet running.</item>
/// <item>DroppyCodeApp.init()'s prologue (:7-19).</item>
/// <item>The run: the lifetime's Startup is NSApplication.finishLaunching, where the delegate's
/// applicationDidFinishLaunching(_:) shows the main window (AppDelegate.swift:9-34); then the main loop.</item>
/// </list>
/// The app quits only through AppShell (OD-0008 C1, C2: the main window's close, Alt+F4; ED-004: a session end), so the
/// lifetime's shutdown is explicit. The boot sequence of OD-0018 (ED-102, ED-103) is MARROW_DEFERRED: see
/// AppDelegate.ApplicationDidFinishLaunching.
/// </summary>
public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (!DroppyCodeApp.TryAcquireSingleInstanceLock(out var singleInstanceLock))
        {
            ReopenSignal.Send(DroppyCodeApp.ReopenSignalName); // ED-002: the running instance runs the reopen path
            return 0;
        }
        using var instanceLock = singleInstanceLock;
        using var reopen = ReopenSignal.Create(DroppyCodeApp.ReopenSignalName);

        // UserDefaults.standard (ED-079, ED-156: %LOCALAPPDATA%\Marrow Code\UserDefaults.json, OD-0008 C7), synchronized
        // on a normal exit (AppShell's afterWillTerminate) and from the unhandled-exception handler.
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var settings = new SettingsStore(SettingsStore.ProductionPath(localAppData));
        UserDefaults.Standard.SetProcessDefault(settings);
        AppDomain.CurrentDomain.UnhandledException += (_, _) => settings.Synchronize();
        // Storage.swift:19-48's roots (OD-0008 C7: %LOCALAPPDATA%\Marrow Code; OD-0012 F1: %USERPROFILE%\.marrow).
        Storage.Locations.SetProcessDefault(StorageLocations.CreateProduction());

        var lifetime = new ClassicDesktopStyleApplicationLifetime
        {
            Args = args,
            ShutdownMode = ShutdownMode.OnExplicitShutdown,
        };
        MarrowApplication.BuildAvaloniaApp().SetupWithLifetime(lifetime);
        // ED-065, R-ISO-1: the main actor is Avalonia's UI thread, bound before any window and before the hang
        // watchdog's first ping (HangWatchdog.swift:52) posts to it.
        MainActor.Bind(AvaloniaMainExecutor.Instance);
        // SwiftUI's default @Environment(\.openURL): the system opens the link (MarkdownView.swift:1110, :1148).
        InlineText.OpenUrl = url => Marrow.Services.Port.Workspace.Open.Current(url);

        DroppyCodeApp.RunPrologue(); // DroppyCodeApp.swift:7-19

        // ED-083, R-VIEW-10: frames come from TopLevel.RequestAnimationFrame, which in Avalonia 12.1.2 asks the one
        // process-wide MediaContext whatever the top level (TopLevel.RequestAnimationFrame decompiled from
        // Avalonia.Controls 12.1.2), so one top level made for the purpose serves every window. It is never shown, so it
        // is no window of the app (the lifetime lists only windows that were opened) and the prologue still runs before
        // any window exists (PR-LAUNCH-001).
        FrameClock.Source.SetProcessDefault(new AvaloniaFrameSource(new AvaloniaWindow()));

        var shell = new AppShell(new AppDelegate(), exit: () => lifetime.Shutdown(), afterWillTerminate: () => settings.Synchronize());
        lifetime.Startup += (_, _) =>
        {
            shell.FinishLaunching();
            reopen.Listen(() => shell.Reopen(lifetime.Windows));
        };
        lifetime.ShutdownRequested += (_, e) => shell.ShutdownRequested(e);
        lifetime.Exit += (_, _) => shell.WillTerminate();
        return lifetime.Start(args);
    }
}
