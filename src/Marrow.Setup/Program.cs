using System.Diagnostics;
using System.Security;
using Avalonia.Threading;
using Marrow.App.Port;
using Marrow.Core.Support;
using Marrow.Setup.Flow;
using Marrow.Setup.Install;
using Marrow.Setup.Platform;
using Marrow.Setup.Port;
using Marrow.Setup.Screens;
using Marrow.Setup.Shell;
using Marrow.Setup.Smoke;
using Marrow.Setup.Support;
using Marrow.Setup.Text;
using Microsoft.Win32;

namespace Marrow.Setup;

/// <summary>
/// PORT-ONLY (OD-0072, ED-247; nth installer PLAN.md 3 and 10, unit U1, ED-NEW-inst-u1-1): the installer the owner shares.
/// <c>--silent</c> and <c>--help</c> go to the unchanged gate (<see cref="LegacyGateHost"/>): no window. <c>--ui-smoke</c>
/// runs the window's smoke checks and exits (it cannot install). Otherwise one setup window per user session: a second
/// start brings the first to the front and exits 0. The window's first screen comes from what is installed
/// (<see cref="SetupFlow"/>), and its own signature is checked while it shows.
/// </summary>
public static class Program
{
    private static readonly TimeSpan FocusPatience = TimeSpan.FromSeconds(3);

    [STAThread]
    public static int Main(string[] args)
    {
        var started = Stopwatch.GetTimestamp();
        // nth installer PLAN.md 8, unit U6 (ED-NEW-inst-u6-1): uninstall mode, with its window or --silent (QuietUninstallString)
        if (SetupFlow.IsUninstall(args) && !InstallerGate.IsHelp(args) && UiSmoke.Read(args) is null) return Uninstall(args, started);
        if (InstallerGate.IsSilent(args) || InstallerGate.IsHelp(args)) return LegacyGateHost.Run(args);

        var smoke = UiSmoke.Read(args);
        SingleInstance? instance = null;
        if (smoke is null)
        {
            instance = SingleInstance.TryAcquire();
            if (instance is null)
            {
                SingleInstance.FocusExisting(new WindowsFocuser(), FocusPatience);
                return 0;
            }
        }
        using (instance)
        {
            var (exitCode, installChosen) = RunWindow(args, instance, smoke, started);
            return installChosen ? LegacyGateHost.Run(args) : exitCode; // only a session without the in-window install (see InstallFlowFor)
        }
    }

    private static (int ExitCode, bool InstallChosen) RunWindow(string[] args, SingleInstance? instance, UiSmokeOptions? smoke, long started, string? uninstallRoot = null)
    {
        SetupApp.Build().SetupWithoutStarting();
        var settings = new SystemSettings(new WindowsSystemLookSource());
        var window = new SetupWindow(settings);
        var version = LegacyGateHost.Metadata("InstallerVersion");
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string? problem = null;
        SetupRoute route;
        try
        {
            route = SetupFlow.Route(version, LegacyGateHost.Metadata("InstallerAppId"), args, AppContext.BaseDirectory, localAppData, new RegistryUninstallKeys());
        }
        catch (Exception error) when (error is FormatException or IOException or UnauthorizedAccessException or SecurityException)
        {
            route = SetupFlow.Problem(localAppData);
            problem = SetupStrings.ProblemOther;
        }
        var session = new SetupSession(window, route, new CanvasScreens(), version, canInstall: smoke is null, arguments: args)
        {
            SizeMegabytes = long.TryParse(LegacyGateHost.Metadata("InstallerSizeMegabytes"), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var megabytes) && megabytes > 0 ? megabytes : null,
        };
        // PLAN.md 3 step 4 and 5, "Leftovers" (unit U9, ED-NEW-inst-u9-1): this run's staging folder goes on every exit path (Ready
        // or Problem showing, Close on any screen, an unhandled exception); stale folders from earlier runs are swept once the
        // window is up. A smoke run stages nothing and sweeps nothing; uninstall mode stages nothing (its removal sweeps, unit U6).
        using var staging = smoke is null && uninstallRoot is null && Path.IsPathFullyQualified(localAppData) ? StagingScope.Create(localAppData, Path.GetTempPath(), version) : null;
        staging?.Attach(session);
        if (problem is not null) session.Fail(problem);
        var engine = smoke is null && problem is null ? InstallFlowFor(session, args, localAppData, version) : null;
        if (engine is not null) window.Opened += (_, _) => _ = engine.StageAsync(); // PLAN.md 3, step 6: staged while Welcome or Update shows
        window.HandleReady += handle => instance?.Publish(handle, Environment.ProcessId);
        session.Start();
        if (uninstallRoot is not null && smoke is null && session.Uninstalling)
            UninstallFlow.Attach(session, wipe => RemoveAsync(uninstallRoot, wipe, silent: false, localAppData, Path.GetTempPath()));

        using var done = new CancellationTokenSource();
        window.Closed += (_, _) => done.Cancel();
        if (smoke is not null) window.Opened += (_, _) => _ = UiSmoke.RunAsync(session, smoke, started);
        else _ = CheckSignatureAsync(session);
        // the previews' wallpaper is read after the first frame, off the UI thread (PLAN.md 8a)
        window.Opened += (_, _) => Dispatcher.UIThread.Post(() => WallpaperLoader.Start(session), DispatcherPriority.Background);
        if (staging is not null) window.Opened += (_, _) => _ = staging.SweepStaleAsync();
        window.Show();
        Dispatcher.UIThread.MainLoop(done.Token);
        return (session.ExitCode, session.InstallChosen);
    }

    /// <summary>
    /// The in-window install (unit U3, ED-NEW-inst-u3-1): Install and Update start an <see cref="InstallFlow"/> whose runs
    /// stage the engine in <c>%LOCALAPPDATA%\nth Setup\&lt;version&gt;</c>, check free space against the stamped sizes, and
    /// start Velopack's Setup.exe through <see cref="ProcessVelopackRunner"/>, the only thing that starts it. Returns the
    /// staging, or null when this route installs nothing.
    /// </summary>
    private static EngineStaging? InstallFlowFor(SetupSession session, string[] args, string localAppData, string version)
    {
        var route = session.Route;
        if (route.Action is not (InstallerAction.Install or InstallerAction.Upgrade) || route.Screen is not (SetupScreen.Welcome or SetupScreen.Update)) return null;
        string folder;
        try { folder = InstallOptions.StagingFolder(localAppData, version); }
        catch (ArgumentException) { return null; }
        session.DesktopShortcut = InstallOptions.DesktopShortcutChoice(args);
        var staging = new EngineStaging(folder, EngineStaging.EmbeddedPayload);
        var sizes = InstallSizes.FromMetadata(key => LegacyGateHost.Metadata(key) is { Length: > 0 } value ? value : null);
        var update = route.Action == InstallerAction.Upgrade;
        // Unit U5 (integrated): the real shell links and HKCU after Setup.exe exits 0 (ProductionAfterSetupExit).
        var flow = new InstallFlow(
            session,
            () => new InstallRun(
                new InstallRequest(update, route.TargetRoot, route.Installed.Version, InstallOptions.ForAction(route.Action, session.DesktopShortcut), folder, sizes),
                new ProcessVelopackRunner(),
                new WindowsDiskSpace(),
                new ProductionAfterSetupExit()),
            staging,
            new BackgroundPollLoop(),
            action => Dispatcher.UIThread.Post(action));
        session.InstallRequested = flow.Begin;
        return staging;
    }

    /// <summary>The self-signature check hashes the whole file, so it runs off the UI thread after the window shows.</summary>
    private static async Task CheckSignatureAsync(SetupSession session)
    {
        var result = await SignatureCheck.VerifySelfAsync().ConfigureAwait(false);
        Dispatcher.UIThread.Post(() => session.SignatureChecked(result));
    }

    /// <summary>
    /// Uninstall mode (unit U6, ED-NEW-inst-u6-1; PLAN.md 8). A run from inside the root hands off to a temporary copy before
    /// any window (UninstallLaunch) and exits; the copy, or a run from elsewhere, holds the setup lock for the whole uninstall
    /// (so nth's start sweep and before-uninstall hook stand aside, unit U9) and shows the window, or with --silent removes
    /// nth with no window: busy, refusing or silent nth exits 3, another setup on the root 4, a Velopack failure 1. The copy
    /// gives its own file to Windows for removal as it ends.
    /// </summary>
    private static int Uninstall(string[] args, long started)
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var temp = Path.GetTempPath();
        var process = Environment.ProcessPath;
        var silent = InstallerGate.IsSilent(args);
        var copy = UninstallLaunch.IsCopy(args);
        string root;
        try
        {
            root = UninstallLaunch.FindRoot(args, process, new RegistryUninstallKeys(), localAppData, AppContext.BaseDirectory);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or SecurityException or ArgumentException or NotSupportedException)
        {
            root = Path.Combine(localAppData, InstallLayout.AppId);
        }
        try
        {
            using var instance = copy ? SingleInstance.TryAcquire(UninstallLaunch.HandOffPatience) : SingleInstance.TryAcquire();
            if (instance is null)
            {
                if (silent) return InstallerGate.InProgressExitCode;
                SingleInstance.FocusExisting(new WindowsFocuser(), FocusPatience);
                return 0;
            }
            if (UninstallLaunch.NeedsCopy(args, process, root))
            {
                if (silent && SilentRefusal(root) is { } refused) return refused;
                // the copy waits for this process to let go of the lock; if no copy can be made, this one goes on in place
                if (UninstallLaunch.HandOff(process!, root, temp, silent, StartCopy) is not null) return 0;
            }
            if (!silent) return RunWindow(args, instance, null, started, root).ExitCode;
            var report = RemoveAsync(root, wipe: false, silent: true, localAppData, temp).GetAwaiter().GetResult();
            return report.Outcome switch
            {
                UninstallOutcome.Removed => 0,
                UninstallOutcome.InProgress => InstallerGate.InProgressExitCode,
                UninstallOutcome.NthNotClosed => InstallerGate.NotClosedExitCode,
                _ => 1,
            };
        }
        finally
        {
            if (copy) UninstallLaunch.ScheduleCopyRemoval(process, temp, RestartRemoval.DeleteAtRestart);
        }
    }

    /// <summary>A silent uninstall from the root asks nth before it hands off, so the caller still gets 3 or 4 (the copy's own code is not seen).</summary>
    private static int? SilentRefusal(string root)
    {
        using var preflight = SilentPreflight.RunAsync(root, ProductionRunningNth.Create(root)).GetAwaiter().GetResult();
        return preflight.ExitCode;
    }

    private static bool StartCopy(string path, IReadOnlyList<string> arguments, string folder)
    {
        var start = new ProcessStartInfo(path) { UseShellExecute = false, WorkingDirectory = folder };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start);
        return process is not null;
    }

    /// <summary>The removal itself, off the UI thread; the real shell, HKCU and Credential Manager are bound here only.</summary>
    private static Task<UninstallReport> RemoveAsync(string root, bool wipe, bool silent, string localAppData, string temp) => Task.Run(async () =>
    {
        using var shell = new ShellLinks();
        var uninstaller = new Uninstaller(new UninstallPorts(
            new ProcessVelopackRunner(),
            shell,
            new RegistryUserHive(Registry.CurrentUser),
            new WindowsCredentialStore(),
            localAppData,
            temp,
            RunningNth: ProductionRunningNth.Create));
        return await uninstaller.RunAsync(root, wipe, silent).ConfigureAwait(false);
    });

    /// <summary>
    /// Unit U5 (PLAN.md 8, item 1; 8a): after Velopack's Setup.exe exits 0, Start's AppUserModelID, the desktop shortcut's
    /// alignment and the uninstall entry, which Velopack writes after the install hook. The real shell and HKCU are bound
    /// here only (ShellLinksBindingTests). Best effort: Setup.exe succeeded, and nth's start-time self-heal repeats these.
    /// </summary>
    internal static void FinishAfterSetupExit(string root, string? resultPath = null)
    {
        try
        {
            using var shell = new ShellLinks();
            AfterSetupExit.Run(shell, new RegistryUserHive(Registry.CurrentUser), root, resultPath);
        }
        catch (Exception)
        {
            // Best effort: Setup.exe succeeded, and nth's start-time self-heal repeats these steps.
        }
    }
}
