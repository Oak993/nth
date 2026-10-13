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
        // Web surfaces W23 (ED-NEW-web-w23-3): Visus's browser data (%LOCALAPPDATA%\nth\WebView2) goes first, with its own 5 s budget.
        WebDataUninstallHook.Register(hook => app.OnBeforeUninstallFastCallback(hook), Marrow.Services.Web.WebDataRoot.Resolve);
        // nth installer PLAN.md 5 (unit U9, ED-NEW-inst-u9-1): the setup's leftover folders go with the app, unless a setup or the uninstaller is open.
        SetupLeftoversStartup.RegisterUninstall(hook => app.OnBeforeUninstallFastCallback(hook));
        // SK-13 / DL-16: the skills and tools data is unlocked and deleted before Velopack removes the app (ONDEMAND-PLAN.md 3.8).
        SkillsUninstallHook.Register(hook => app.OnBeforeUninstallFastCallback(hook), SkillsDataRoot.Resolve);
        // nth installer PLAN.md 8 and 8a (unit U5, ED-NEW-inst-u5-1): Start, the desktop shortcut, the root uninstaller
        // and the uninstall key, inside Velopack's hook budgets. The real shell and HKCU are bound here only.
        NthInstallHook.Register(
            hook => app.OnAfterInstallFastCallback(hook),
            hook => app.OnAfterUpdateFastCallback(hook),
            hook => app.OnBeforeUninstallFastCallback(hook),
            version => NthInstallHook.ContextFor(baseDirectory, version),
            () => new ShellLinks(),
            () => new RegistryUserHive(Microsoft.Win32.Registry.CurrentUser));
        app.Run();
    }

    internal static bool InstalledByVelopack(string baseDirectory) => InstalledRoot(baseDirectory) is not null;

    /// <summary>The Velopack install root this app runs from (the folder one above its own <c>current</c> folder, where Update.exe is), or null for a dev run, a portable copy or a test host.</summary>
    internal static string? InstalledRoot(string baseDirectory) =>
        Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(baseDirectory)) is { } root && File.Exists(Path.Combine(root, "Update.exe")) ? root : null;

    /// <summary>
    /// ED-002 reopens the running instance when a second nth is started; ED-NEW-inst-u8-3 makes one exception: a start by the
    /// "Open at sign-in" entry (<see cref="SignInEntry.Argument"/>) does nothing to a running nth.
    /// </summary>
    internal static bool ShouldReopenRunningInstance(IReadOnlyList<string> args) => !SignInEntry.IsAtSignInLaunch(args);

    /// <summary>
    /// nth installer PLAN.md 8 and 8a (unit U8, ED-NEW-inst-u8-1): Settings > General > "On this PC" manages nth's desktop shortcut and
    /// sign-in entry, in a Velopack install only. The real shell and the real HKCU are bound here (and in the self-heal below) only.
    /// </summary>
    internal static void BindOnThisPc(string baseDirectory)
    {
        if (!InstalledByVelopack(baseDirectory) || InstallLayout.RootOf(baseDirectory) is not { } root) return;
        OnThisPcPlatform.Slot.SetProcessDefault(new OnThisPcPlatform(root, () => new ShellLinks(), () => new RegistryUserHive(Microsoft.Win32.Registry.CurrentUser)));
    }

    [STAThread]
    public static int Main(string[] args)
    {
        // SK-17: private, headless MCP relay. It must precede Velopack, single-instance, settings and all desktop work.
        if (args.FirstOrDefault() == Marrow.Services.Port.Skills.Providers.SkillsMcpBridge.Command)
            return Marrow.Services.Port.Skills.Providers.SkillsMcpBridge.RunClient(args, Console.OpenStandardInput(), Console.OpenStandardOutput()).GetAwaiter().GetResult();
        // ED-NEW-ignisadvisor-2: the ask_advisor relay, the same kind of private headless child. ED-NEW-mainorder-2: it shares
        // SK-17's exact-prefix exception (ED-235, ED-636), so it too precedes Velopack, single-instance, settings and desktop work.
        if (args.FirstOrDefault() == Marrow.Services.Port.Advisor.AdvisorMcpBridge.Command)
            return Marrow.Services.Port.Advisor.AdvisorMcpBridge.RunClient(args, Console.OpenStandardInput(), Console.OpenStandardOutput()).GetAwaiter().GetResult();
        // ED-NEW-web-w12-1: the nth-ui relay (Tabula's artifact tools), the same kind of private headless child.
        if (args.FirstOrDefault() == Marrow.Services.Web.UiMcpBridge.Command)
            return Marrow.Services.Web.UiMcpBridge.RunClient(args, Console.OpenStandardInput(), Console.OpenStandardOutput()).GetAwaiter().GetResult();
        // Tactus (OD-0140, ED-NEW-pcctl-10): the nth-pc relay, the same kind of private headless child.
        if (args.FirstOrDefault() == Marrow.Services.Port.PcControl.PcControlBridge.Command)
            return Marrow.Services.Port.PcControl.PcControlBridge.RunClient(args, Console.OpenStandardInput(), Console.OpenStandardOutput()).GetAwaiter().GetResult();
        // Tactus's separate workspace (P18, ED-NEW-pcws-6): the agent the child session runs, and the user's own enable and
        // disable commands, headless like the relays above.
        if (Marrow.App.Port.PcControl.Workspace.WorkspaceCommands.Handles(args))
            return Marrow.App.Port.PcControl.Workspace.WorkspaceCommands.Run(args).GetAwaiter().GetResult();
        // ED-NEW-dextra-as4-3: the nth-dextra relay, the same kind of private headless child.
        if (args.FirstOrDefault() == Marrow.Services.Assistant.AssistantMcpBridge.Command)
            return Marrow.Services.Assistant.AssistantMcpBridge.RunClient(args, Console.OpenStandardInput(), Console.OpenStandardOutput()).GetAwaiter().GetResult();
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
                || root.StartsWith(Path.Combine(live, AppInfo.DataFolderName), StringComparison.OrdinalIgnoreCase) || root.StartsWith(Path.Combine(live, AppInfo.LegacyDataFolderName), StringComparison.OrdinalIgnoreCase)) return 2;
            DevDataRoot.PerformanceRoot.SetProcessDefault(root);
        }
        // ED-NEW-models-palette-11: offline, owner-local pack generator. It runs right after the Velopack hook (ED-235: nothing
        // but the SK-17 relay may precede the hook; a normal launch returns from the hook at once) and before every other
        // desktop or data binding: the single-instance lock, toast identity, settings and the app lifetime.
        if(args.FirstOrDefault()=="--colour-bake")return Marrow.Services.Port.ModelsPalette.PaletteBake.Run(args,new Marrow.UI.Port.ModelsPalette.SkiaLogoDecoder());
        if(args.FirstOrDefault()=="--colour-bake-live")return Marrow.Services.Port.ModelsPalette.PaletteLiveBake.Run(args,new Marrow.UI.Port.ModelsPalette.SkiaLogoDecoder());
#if DEBUG
        // ED-NEW-pcctl-22: Tactus's overlay spike (P25a, debug builds only): NP-15 and NP-16, run by hand, never by a test.
        if (args.FirstOrDefault() == Marrow.App.Port.PcControl.Overlay.OverlaySpike.Command) return Marrow.App.Port.PcControl.Overlay.OverlaySpike.Run(args);
#endif
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
            // AS-10 (ED-NEW-dextra-as10-13): a sign-in start (--hidden) or a Task Scheduler wake (--duty id) that finds nth running opens nothing:
            // the running one holds the duties, and its own timer runs them. Everything else is the owner starting nth again.
            if (Marrow.Core.Port.Assistant.StartupArguments.Parse(args).Hidden) return 0;
            // nth installer PLAN.md 8 (unit U8, ED-NEW-inst-u8-3): the sign-in entry starts nth with --at-sign-in, and a running nth must not
            // be pulled to the front by it. A launch by the user (Start, the desktop shortcut, a double click) still reopens as ED-002 says.
            if (ShouldReopenRunningInstance(args)) ReopenSignal.Send(DroppyCodeApp.ReopenSignalName); // ED-002: the running instance runs the reopen path
            return 0;
        }
        using var instanceLock = singleInstanceLock;
        // nth installer PLAN.md 3a (U4, ED-NEW-inst-u4-3): while nth Setup runs Velopack on this install root, Velopack stops every
        // process in it, so an nth opened now would show a window and be killed. It waits here, with no window, at most ten minutes.
        DroppyCodeApp.WaitForInstallerToFinish(AppContext.BaseDirectory);
        BindOnThisPc(AppContext.BaseDirectory);
        // OD-0135: the product is named nth. Once, before anything reads settings, logs, the library or the GPU program cache, the
        // old "Marrow Code" data folder and .marrow state folder are COPIED to their new names (never moved; the old app may still run).
        LegacyDataCopy.CopyProductFolders(DevDataRoot.LocalApplicationData(), DevDataRoot.IsRedirected ? DevDataRoot.LocalApplicationData() : LoginEnvironment.HomeDirectory, AppInfo.Current.IsDevelopment);
        DesktopPerformanceProbe.BeginStartup();
        StartupPrimes.PrimeSkillsCatalogue(StartupPrimes.Pool); // ED-NEW-perf-startup-structure-3: the 1.2 MB catalogue is parsed on the pool while the desktop is bound; the skills host below takes the one parse
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
        Marrow.App.Port.Settings.ProviderSwitchRepair.RunOnce(settings); // ED-NEW-providersmerge-switch-repair-1: once, before AppSettings reads it, the providers the dead switch of older builds wrote off are on again
        // ED-NEW-perf-startup-structure-5: the store (made on first use from UserDefaults.Standard, which is bound above) checks a saved wallpaper against the whole catalogue; that catalogue is read on the pool,
        // so the first window's build finds it made, and with no wallpaper saved nothing is read. The explicit SetProcessDefault(new(settings)) that made the store here, on the UI thread before any window, is gone.
        StartupPrimes.PrimeWallpapers(settings, StartupPrimes.Pool);
        AppDomain.CurrentDomain.UnhandledException += (_, _) => settings.Synchronize();
        // Storage.swift:19-48's roots (OD-0008 C7: %LOCALAPPDATA%\Marrow Code; OD-0012 F1: %USERPROFILE%\.marrow)
        // and StoragePaths.swift's roots (ED-195: split out of Storage.swift at B3), the sibling Ambient
        // Marrow.UI.Theme.Wallpaper reads directly (Wallpaper.cs:154, :187, :191) and that traps
        // (SwiftTrapException, "StoragePaths.Locations is not bound...") on first access if this is missed.
        Storage.Locations.SetProcessDefault(StorageLocations.CreateProduction());
        StoragePaths.Locations.SetProcessDefault(StoragePathsLocations.CreateProduction());
        Marrow.App.Views.Composer.GpuProgramCache.PrepareAtLaunch(); // ED-NEW-launch-fix (1): the kept GPU programs read on the pool before Boot B's first frame
        Marrow.UI.Port.AuroraBootGpu.ContextReady = gpu => { Marrow.App.Views.Composer.GpuProgramCache.ForContext(gpu); _ = Marrow.App.Views.Composer.GlassVariantPreparation.Start(); }; // installed before anything links; ED-NEW-launch-fix (A): the glass variants made on a worker, awaited by nothing
        _ = LoginEnvironment.Load(); // OD-0133: the provider refresh's environment read on the pool now (single-flight), not when bootstrap reaches it
        // SK-13 / ED-326 (6)(a): ONE skills pipeline per data root, SK-9's generations bound to it. A host that cannot open leaves the seam unbound and the app starts.
        var skills = SkillsStartup.Bind(SkillsDataRoot.Resolve());
        Marrow.App.Port.MultiAccount.Settings.MultiAccountSettingsSeams.Bind(); // T11 step 2: Settings > Models (accounts) and Settings > Pills read the running AppModel
        Marrow.App.Port.MultiAccount.Capsule.ComposerCapsuleSeams.Bind(); // step 3a: the composer shows the account capsule
        Marrow.App.Port.Ignis.Runtime.IgnisRuntimeStartup.Bind(); // Ignis (ED-NEW-ignis-runtime): the controller, made on first use once the U1 parts are bound
        Marrow.App.Port.Advisor.AdvisorRuntimeStartup.Bind(); // ED-NEW-ignisadvisor-2: a Droppy Hydra head asks its lead's model (ask_advisor)
        Marrow.App.Port.Tabula.ArtifactRuntimeStartup.Bind(); // ED-NEW-web-w12-1: Tabula's artifacts, on disk per thread, for every model
        Marrow.App.Views.Diff.DiffPane.Register(); Marrow.App.Views.Terminal.TerminalPane.Register(); Marrow.App.Views.Browser.VisusPane.Register(); // ED-NEW-web-w4-1, ED-NEW-web-w8a-1: the Changes, Terminal and Visus panes, with the other panes and not in the prologue (PR-LAUNCH-001: it carries only HangWatchdog.Start)
        Marrow.App.Views.Panes.BackgroundTasksPane.Register(); Marrow.App.Views.Panes.TranscriptPane.Register(); // ED-NEW-web-w5-1: the Background tasks and Transcript panes draw in the pane workspace
        Marrow.App.Port.Web.AppWebSurface.Enable(); Marrow.App.Views.Artifacts.TabulaPane.Register(); // ED-NEW-web-w14-1: the Tabula pane, its pages on the process's one web surface
        Marrow.App.Port.AskUser.AskUserStartup.Bind(); // ED-NEW-web-w25-1: ask_user, a blocking question card in the thread, for every model
        Marrow.App.Views.Panes.SideChatPane.Register(); // ED-NEW-web-w6-1: Ctrl+; opens Side chat, a second conversation about the thread
        Marrow.App.Port.PcControl.PcControlStartup.Bind(); // Tactus (OD-0140, ED-NEW-pcctl-9): the capture seams, Windows.Graphics.Capture and the Skia JPEG encoder
        Marrow.App.Views.Browser.VisusAgentStartup.Bind(); // ED-NEW-web-w11-1: the web_* tools drive Visus, with its cards, the turn, Stop and Background tasks
        if (skills is not null) SkillsUpdateRows.Attach(skills); // SK-16: the installed items are the Updates page's "Skills and tools" rows (ED-343's hook)

        // AS-10: a hidden start with no duties to run would leave nth with no window and no tray, so it shows the window like any start; a
        // wake for one duty with no duties at all has nothing to do and ends.
        var mode = Marrow.Core.Port.Assistant.StartupArguments.Parse(args);
        if (mode.Hidden && !Marrow.App.Assistant.BackgroundStart.HasDuties(Marrow.Services.Assistant.AssistantStorage.Roots.Current))
        {
            if (mode.ForOneDuty) return 0;
            mode = Marrow.Core.Port.Assistant.StartupMode.Normal;
        }
        var lifetime = new ClassicDesktopStyleApplicationLifetime
        {
            Args = args,
            ShutdownMode = ShutdownMode.OnExplicitShutdown,
        };
        bindingWork.Dispose();
        AuroraBootWarmup.Register(); // OD-0113: the imported renderer joins the same awaited preparation
        NthMotionWarmup.Register(); // OD-0136 amendment 2 (ED-NEW-nth-motion): the nth motion pack's programs, same step
        Marrow.App.Port.Voice.VoiceWarmup.Register(); // ED-NEW-perf-startup-structure-2: the two voice orbs' programs ("Voice orbs", step 55) are compiled in the Getting ready pass, not on the render thread when the mic orb first shows
        Marrow.App.Port.Voice.VoiceGlowWarmup.Register(); // ED-NEW-perf-startup-structure-2: the voice glow's programs ("Voice glow", step 56), same pass
        Marrow.App.Port.MultiAccount.Handshake.HandshakeGpuProgram.EnsureRegistered(); // MA-4-2: prime the filament gradient and bloom before a live Handshake.
        using (AuroraBootTrace.Begin("Startup: Avalonia setup"))
            MarrowApplication.BuildAvaloniaApp().SetupWithLifetime(lifetime);
        // ED-065, R-ISO-1: the main actor is Avalonia's UI thread, bound before any window and before the hang
        // watchdog's first ping (HangWatchdog.swift:52) posts to it.
        MainActor.Bind(AvaloniaMainExecutor.Instance);
        StartupPrimes.PrimeSymbols(StartupPrimes.Pool); // ED-NEW-perf-startup-structure-5: the SF-symbol mapping (needs Avalonia's asset loader, set up above) is read before the cloaked window's first icon asks for it
        DesktopPerformanceProbe.Start();
        // SwiftUI's default @Environment(\.openURL): the system opens the link (MarkdownView.swift:1110, :1148). PORT-ONLY (web surfaces W10, DESIGN 5.11,
        // ED-NEW-web-w10-2): a link a chat or one of its cards opens follows Settings > Visus > "Open links from chats in" (Ask the first time, Visus or the
        // default browser); sign-ins (MCP OAuth, account sign-in), update and store links call Workspace.Open themselves and never come through here.
        InlineText.OpenUrl = url => Marrow.App.Port.Web.ChatLinks.OpenFromAnywhere(url);
        // PORT-ONLY (W19, ED-NEW-web-w19-3): MCP Apps over the hub, the App pane, the list of app tools (after the main actor and the link setting).
        Marrow.App.Port.Web.McpApps.McpAppsStartup.Bind();
        // PORT-ONLY (W10, ED-NEW-web-w10-3): Settings > Visus and Settings > Tabula meet the seams the web units left; the settings attach once the app model exists (FinishStartup).
        _ = Marrow.App.Port.Web.WebSettingsBinding.Bind(process: true);

        var shell = new AppShell(new AppDelegate(), exit: () => lifetime.Shutdown(), afterWillTerminate: () => settings.Synchronize());
        shell.Delegate.StartHidden = mode.Hidden;
        shell.Delegate.AfterBootstrap = model => StartDuties(model, mode, shell);
        var launchReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var notifications = DroppyCodeApp.ListenForNotificationArguments(shell.Delegate, launchReady.Task);
        // nth installer PLAN.md 3a (U4, ED-NEW-inst-u4-1): nth Setup asks over this pipe whether nth may quit before it updates the install.
        using var installerControl = DroppyCodeApp.ListenForInstallerControl(shell, launchReady.Task);

        DroppyCodeApp.RunPrologue(); // DroppyCodeApp.swift:7-19

        // ED-083, R-VIEW-10: frames come from TopLevel.RequestAnimationFrame, which in Avalonia 12.1.2 asks the one
        // process-wide MediaContext whatever the top level (TopLevel.RequestAnimationFrame decompiled from
        // Avalonia.Controls 12.1.2), so one top level made for the purpose serves every window. It is never shown, so it
        // is no window of the app (the lifetime lists only windows that were opened) and the prologue still runs before
        // any window exists (PR-LAUNCH-001).
        FrameClock.Source.SetProcessDefault(new AvaloniaFrameSource(new AvaloniaWindow()));

        lifetime.Startup += (_, _) => FinishStartup(shell, launchReady, skills, args, reopen, lifetime, mode);
        lifetime.ShutdownRequested += (_, e) => shell.ShutdownRequested(e);
        lifetime.Exit += (_, _) => {shell.Delegate.Model?.StopPaletteChecks();shell.WillTerminate();};
        return lifetime.Start(args);
    }

    // AS-10 (ED-NEW-dextra-as10-13): Dextra's duties start once the model is bootstrapped, whether the window is up or not. With "Keep working when the
    // window is closed" on, a close of the window hides it and the duties go on in the tray; the tray's Open nth brings it back.
    private static void StartDuties(AppModel model, Marrow.Core.Port.Assistant.StartupMode mode, AppShell shell)
    {
        var windows = Marrow.App.Window.WindowManager.Shared;
        var host = Marrow.App.Assistant.BackgroundStart.Run(model, mode,
            windowHidden: () => windows.MainWindow is not { IsVisible: true },
            open: () => MainActor.Post(windows.ShowMain),
            quit: () => MainActor.Post(shell.Terminate),
            out var problem);
        // There is no log in nth yet to write to; the Assistant's own feed is where Dextra's problems are told.
        if (problem is not null)
            model.AssistantEvents.Publish(Marrow.Core.Port.Assistant.FeedDraft.About(Marrow.Core.Port.Assistant.FeedEventKind.WatcherChanged, Marrow.Core.Port.Assistant.FeedSeverity.Error, TimeProvider.System.GetUtcNow(), problem));
        windows.KeepWorkingOnClose = () => model.Duties?.Switches.KeepWorkingWhenClosed == true && model.Tray is not null;
    }

    private static async void FinishStartup(AppShell shell, TaskCompletionSource launchReady, SkillsHost? skills, string[] args, ReopenSignal reopen, IClassicDesktopStyleApplicationLifetime lifetime, Marrow.Core.Port.Assistant.StartupMode mode)
    {
        if (mode.Hidden) shell.PrepareHidden();
        else shell.PrepareBoot();
        // Even a cached pack must let the first stage layout/paint run before model/content work.
        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { }, Avalonia.Threading.DispatcherPriority.Background);
        await Marrow.App.Port.ModelsPalette.PaletteStartup.PrepareAsync(); // ED-622: verified authority before any model/content.
        if (shell.HasTerminated) return;
        Marrow.App.Port.MultiAccount.Thread.ThreadPortSeams.Bind(Marrow.App.Window.WindowManager.Shared.Model);
        _ = Marrow.App.Port.Web.WebSettingsBinding.Attach(Marrow.App.Window.WindowManager.Shared.Model.Settings.Web); // W10: every web seam reads the reader's choices from here on
        shell.FinishLaunching();
        launchReady.TrySetResult();
        // nth installer PLAN.md 5 (unit U9, ED-NEW-inst-u9-1): nth Setup's leftover folders are removed on the pool once the first window is up, when no setup runs.
        _ = SetupLeftoversStartup.SweepAtStart(AppContext.BaseDirectory);
        skills?.CollectInBackground(); // SK-13: the startup GC runs on the pool once the main window is up, never on the launch path (single-instance lock is already held)
        // nth installer PLAN.md 8 and 8a (unit U5): the start-time self-heal of Start, nth's desktop shortcuts and the
        // uninstall key, on the pool after the first window; nothing outside a Velopack install.
        _ = NthInstallHook.StartSelfHeal(AppContext.BaseDirectory, InstalledByVelopack, () => new ShellLinks(), () => new RegistryUserHive(Microsoft.Win32.Registry.CurrentUser));
        DroppyCodeApp.ReceiveNotificationArguments(shell.Delegate, args); // ED-220: cold protocol activation
        reopen.Listen(() => shell.Reopen(lifetime.Windows));
    }

}
