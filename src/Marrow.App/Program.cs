using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Marrow.App.Port;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.App.Port.Skills;
using Marrow.Services.Port;
using Marrow.Services.Port.Skills.Host;
using Marrow.Services.Store;
using Marrow.UI.Markdown;
using Marrow.UI.Port;
using Velopack;
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
/// lifetime's shutdown is explicit. The boot sequence of OD-0018 (ED-102, ED-103) runs from
/// AppDelegate.ApplicationDidFinishLaunching (Port/BootStageWindow.cs).
/// </summary>
public static class Program
{
    /// <summary>
    /// ED-235: Velopack's hook runs only in a Velopack install (Update.exe one folder above the app's own, its
    /// <c>current</c> folder). Velopack's Run() writes %LOCALAPPDATA%\velopack\velopack.log on every call, installed or
    /// not, so a dev run or a test host never calls it.
    /// </summary>
    internal static void RunVelopackHook(string baseDirectory)
    {
        if (!InstalledByVelopack(baseDirectory)) return;
        var app = VelopackApp.Build();
        // SK-13 / DL-16: the skills and tools data is unlocked and deleted before Velopack removes the app (ONDEMAND-PLAN.md 3.8).
        SkillsUninstallHook.Register(hook => app.OnBeforeUninstallFastCallback(hook), SkillsDataRoot.Resolve);
        app.Run();
    }

    internal static bool InstalledByVelopack(string baseDirectory) =>
        Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(baseDirectory)) is { } root
        && File.Exists(Path.Combine(root, "Update.exe"));

    [STAThread]
    public static int Main(string[] args)
    {
        // SK-17: private, headless MCP relay. It must precede Velopack, single-instance, settings and all desktop work.
        if (args.FirstOrDefault() == Marrow.Services.Port.Skills.Providers.SkillsMcpBridge.Command)
            return Marrow.Services.Port.Skills.Providers.SkillsMcpBridge.RunClient(args, Console.OpenStandardInput(), Console.OpenStandardOutput()).GetAwaiter().GetResult();
        // OD-0062, ED-235: Velopack's startup hook, before anything else (before the single-instance lock, the toast
        // identity, every binding). When Update.exe runs the app to install, update or uninstall it (--veloapp-*
        // arguments) this handles the event and exits the process; started normally it returns at once.
        RunVelopackHook(AppContext.BaseDirectory);
        // ED-NEW-T44: an explicitly marked copied profile, including Release, never the owner's live roots.
        if (args.Contains("--performance-profile", StringComparer.Ordinal))
        {
            var profile = Environment.GetEnvironmentVariable("MARROW_PORT_DATA_DIR");
            if (string.IsNullOrWhiteSpace(profile) || !Path.IsPathFullyQualified(profile)
                || !File.Exists(Path.Combine(profile, ".marrow-performance-profile"))) return 2;
            var root = Path.GetFullPath(profile);
            var live = Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
            if (string.Equals(root.TrimEnd(Path.DirectorySeparatorChar), live.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)
                || root.StartsWith(Path.Combine(live, "Marrow Code"), StringComparison.OrdinalIgnoreCase)) return 2;
            DevDataRoot.PerformanceRoot.SetProcessDefault(root);
        }
        // ED-NEW-models-palette-11: offline, owner-local pack generator. It runs right after the Velopack hook (ED-235: nothing
        // but the SK-17 relay may precede the hook; a normal launch returns from the hook at once) and before every other
        // desktop or data binding: the single-instance lock, toast identity, settings and the app lifetime.
        if(args.FirstOrDefault()=="--colour-bake")return Marrow.Services.Port.ModelsPalette.PaletteBake.Run(args,new Marrow.UI.Port.ModelsPalette.SkiaLogoDecoder());
        if(args.FirstOrDefault()=="--colour-bake-live")return Marrow.Services.Port.ModelsPalette.PaletteLiveBake.Run(args,new Marrow.UI.Port.ModelsPalette.SkiaLogoDecoder());
        WindowsToastStartup.RestoreDevRoot(args); // ED-212/ED-220: retain protocol launch's instance identity
#if DEBUG
        // ED-212, review fix: a mistyped or since-deleted MARROW_DEV_DATA_ROOT must not silently fall back to the
        // developer's own real %LOCALAPPDATA%\Marrow Code (DevDataRoot.LocalApplicationData's contract for every
        // other caller). Checked once, before the single-instance lock, so a misconfigured launch never touches
        // real data or contends with a real running instance either.
        if (DevDataRoot.IsMisconfigured)
        {
            Console.Error.WriteLine(
                $"{DevDataRoot.VariableName} is set to \"{Environment.GetEnvironmentVariable(DevDataRoot.VariableName)}\", " +
                "which is not an existing directory. Fix the path or unset the variable before launching.");
            return 1;
        }
#endif

        if (!DroppyCodeApp.TryAcquireSingleInstanceLock(out var singleInstanceLock))
        {
            // Bind only transport in the short-lived second process: no registration, data store or toast posting.
            ToastLaunchTransport.Current.SetProcessDefault(new WindowsToastLaunchTransport());
            if (DroppyCodeApp.ForwardNotificationArguments(args)) return 0;
            // A recognized toast whose transport failed must not reopen the main window (especially an update).
            if (args.Any(argument => ToastActivation.Decode(WindowsToastStartup.Identity, argument) is not null)) return 1;
            ReopenSignal.Send(DroppyCodeApp.ReopenSignalName); // ED-002: the running instance runs the reopen path
            return 0;
        }
        using var instanceLock = singleInstanceLock;
        DesktopPerformanceProbe.BeginStartup();
        var bindingWork = AuroraBootTrace.Begin("Startup: desktop bindings");
        Marrow.App.Port.ModelsPalette.PaletteStartup.Begin(); // ED-622: overlap verified pack loading with desktop bindings.
        using var reopen = ReopenSignal.Create(DroppyCodeApp.ReopenSignalName);
        WindowsToastStartup.Bind(); // ED-220: process AUMID/our per-user scheme before any window exists

        // UserDefaults.standard (ED-079, ED-156: %LOCALAPPDATA%\Marrow Code\UserDefaults.json, OD-0008 C7), synchronized
        // on a normal exit (AppShell's afterWillTerminate) and from the unhandled-exception handler.
        // ED-212: a Debug launch honours MARROW_DEV_DATA_ROOT when it names an existing directory; Release and an
        // unset/absent variable resolve exactly the special folder above.
        var localAppData = DevDataRoot.LocalApplicationData();
        var settings = new SettingsStore(SettingsStore.ProductionPath(localAppData));
        UserDefaults.Standard.SetProcessDefault(settings);
        Marrow.UI.Theme.Wallpapers.WallpaperLibraryStore.Shared.SetProcessDefault(new(settings));
        AppDomain.CurrentDomain.UnhandledException += (_, _) => settings.Synchronize();
        // Storage.swift:19-48's roots (OD-0008 C7: %LOCALAPPDATA%\Marrow Code; OD-0012 F1: %USERPROFILE%\.marrow)
        // and StoragePaths.swift's roots (ED-195: split out of Storage.swift at B3), the sibling Ambient
        // Marrow.UI.Theme.Wallpaper reads directly (Wallpaper.cs:154, :187, :191) and that traps
        // (SwiftTrapException, "StoragePaths.Locations is not bound...") on first access if this is missed.
        Storage.Locations.SetProcessDefault(StorageLocations.CreateProduction());
        StoragePaths.Locations.SetProcessDefault(StoragePathsLocations.CreateProduction());
        Marrow.App.Views.Composer.GpuProgramCache.PrepareAtLaunch(); // ED-NEW-launch-fix (1): the kept GPU programs read on the pool before Boot B's first frame
        Marrow.UI.Port.AuroraBootGpu.ContextReady = gpu => Marrow.App.Views.Composer.GpuProgramCache.ForContext(gpu); // installed before anything links
        _ = LoginEnvironment.Load(); // OD-0133: the provider refresh's environment read on the pool now (single-flight), not when bootstrap reaches it
        // SK-13 / ED-326 (6)(a): ONE skills pipeline per data root, SK-9's generations bound to it. A host that cannot open leaves the seam unbound and the app starts.
        var skills = SkillsStartup.Bind(SkillsDataRoot.Resolve());
        Marrow.App.Port.MultiAccount.Settings.MultiAccountSettingsSeams.Bind(); // T11 step 2: Settings > Models (accounts) and Settings > Pills read the running AppModel
        Marrow.App.Port.MultiAccount.Capsule.ComposerCapsuleSeams.Bind(); // step 3a: the composer shows the account capsule
        if (skills is not null) SkillsUpdateRows.Attach(skills); // SK-16: the installed items are the Updates page's "Skills and tools" rows (ED-343's hook)

        var lifetime = new ClassicDesktopStyleApplicationLifetime
        {
            Args = args,
            ShutdownMode = ShutdownMode.OnExplicitShutdown,
        };
        bindingWork.Dispose();
        AuroraBootWarmup.Register(); // OD-0113: the imported renderer joins the same awaited preparation
        AuroraLoaderWarmup.Register(); // OD-0113 / OD-0108: before the first preparation plan or GPU warm-up
        Marrow.App.Port.MultiAccount.Handshake.HandshakeGpuProgram.EnsureRegistered(); // MA-4-2: prime the filament gradient and bloom before a live Handshake.
        using (AuroraBootTrace.Begin("Startup: Avalonia setup"))
            MarrowApplication.BuildAvaloniaApp().SetupWithLifetime(lifetime);
        // ED-065, R-ISO-1: the main actor is Avalonia's UI thread, bound before any window and before the hang
        // watchdog's first ping (HangWatchdog.swift:52) posts to it.
        MainActor.Bind(AvaloniaMainExecutor.Instance);
        DesktopPerformanceProbe.Start();
        // SwiftUI's default @Environment(\.openURL): the system opens the link (MarkdownView.swift:1110, :1148).
        InlineText.OpenUrl = url => Marrow.Services.Port.Workspace.Open.Current(url);

        var shell = new AppShell(new AppDelegate(), exit: () => lifetime.Shutdown(), afterWillTerminate: () => settings.Synchronize());
        var launchReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var notifications = DroppyCodeApp.ListenForNotificationArguments(shell.Delegate, launchReady.Task);

        DroppyCodeApp.RunPrologue(); // DroppyCodeApp.swift:7-19

        // ED-083, R-VIEW-10: frames come from TopLevel.RequestAnimationFrame, which in Avalonia 12.1.2 asks the one
        // process-wide MediaContext whatever the top level (TopLevel.RequestAnimationFrame decompiled from
        // Avalonia.Controls 12.1.2), so one top level made for the purpose serves every window. It is never shown, so it
        // is no window of the app (the lifetime lists only windows that were opened) and the prologue still runs before
        // any window exists (PR-LAUNCH-001).
        FrameClock.Source.SetProcessDefault(new AvaloniaFrameSource(new AvaloniaWindow()));

        lifetime.Startup += (_, _) => FinishStartup(shell, launchReady, skills, args, reopen, lifetime);
        lifetime.ShutdownRequested += (_, e) => shell.ShutdownRequested(e);
        lifetime.Exit += (_, _) => {shell.Delegate.Model?.StopPaletteChecks();shell.WillTerminate();};
        return lifetime.Start(args);
    }

    private static async void FinishStartup(AppShell shell, TaskCompletionSource launchReady, SkillsHost? skills, string[] args, ReopenSignal reopen, IClassicDesktopStyleApplicationLifetime lifetime)
    {
        shell.PrepareBoot();
        // Even a cached pack must let the first stage layout/paint run before model/content work.
        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { }, Avalonia.Threading.DispatcherPriority.Background);
        await Marrow.App.Port.ModelsPalette.PaletteStartup.PrepareAsync(); // ED-622: verified authority before any model/content.
        if (shell.HasTerminated) return;
        Marrow.App.Port.MultiAccount.Thread.ThreadPortSeams.Bind(Marrow.App.Window.WindowManager.Shared.Model);
        shell.FinishLaunching();
        launchReady.TrySetResult();
        skills?.CollectInBackground(); // SK-13: the startup GC runs on the pool once the main window is up, never on the launch path (single-instance lock is already held)
        DroppyCodeApp.ReceiveNotificationArguments(shell.Delegate, args); // ED-220: cold protocol activation
        reopen.Listen(() => shell.Reopen(lifetime.Windows));
    }

}
