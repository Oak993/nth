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
            if (instance is null) return SecondStart(new WindowsFocuser(), FocusPatience);
        }
        using (instance)
        {
            var (exitCode, installChosen) = RunWindow(args, instance, smoke, started);
            return installChosen ? LegacyGateHost.Run(args) : exitCode; // only a session without the in-window install (see InstallFlowFor)
        }
    }

    /// <summary>
    /// A second start (PLAN.md 3, step 1; tested by SingleInstanceTests, unit U10): the first window is brought to the front
    /// and this process ends at once with 0, whether or not that window could be found.
    /// </summary>
    internal static int SecondStart(IWindowFocuser focuser, TimeSpan patience, string name = SingleInstance.SetupLockName)
    {
        _ = SingleInstance.FocusExisting(focuser, patience, name);
        return 0;
    }

    private static (int ExitCode, bool InstallChosen) RunWindow(string[] args, SingleInstance? instance, UiSmokeOptions? smoke, long started, string? uninstallRoot = null)
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        // PLAN.md 5 (ED-NEW-inst-fix-5): a Native AOT setup carries Skia, HarfBuzz and ANGLE inside; they are unpacked and loaded first
        try { SetupNatives.Prepare(localAppData, Path.GetTempPath(), Environment.ProcessPath); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or DllNotFoundException or BadImageFormatException or ArgumentException)
        {
            // no window can be drawn without them: the reason goes in a message box, as the windowless gate reports its errors
            if (smoke is null) new LegacyGateHost().ShowMessage(InstallerGate.Caption(LegacyGateHost.Metadata("InstallerTitle")), error.Message, warning: true);
            return (1, false);
        }
        SetupApp.Build().SetupWithoutStarting();
        var settings = new SystemSettings(new WindowsSystemLookSource());
        var window = new SetupWindow(settings);
        var version = LegacyGateHost.Metadata("InstallerVersion");
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
            SizeMegabytes = StampedSizes().DiskMegabytes, // T_disk, the one size shown everywhere (PLAN.md 4); the plan's 420 MB for a build the script did not measure
            ReleaseNotes = SetupReleaseNotes.Embedded(), // PLAN.md 9 item 7: the validated notes the script embedded, for Update's "What's new"
        };
        // PLAN.md 3 step 4 and 5, "Leftovers" (unit U9, ED-NEW-inst-u9-1): this run's staging folder goes on every exit path (Ready
        // or Problem showing, Close on any screen, an unhandled exception); stale folders from earlier runs are swept once the
        // window is up. A smoke run stages nothing and sweeps nothing; uninstall mode stages nothing (its removal sweeps, unit U6).
        using var staging = smoke is null && uninstallRoot is null && Path.IsPathFullyQualified(localAppData) ? StagingScope.Create(localAppData, Path.GetTempPath(), version) : null;
        staging?.Attach(session);
        if (problem is not null) session.Fail(problem);
        var (engine, check) = smoke is null && problem is null ? InstallFlowFor(session, localAppData, version) : (null, null);
        if (engine is not null) window.Opened += (_, _) => _ = engine.StageAsync(); // PLAN.md 3, step 6: staged while Welcome or Update shows
        window.HandleReady += handle => instance?.Publish(handle, Environment.ProcessId);
        session.Start();
        if (uninstallRoot is not null && smoke is null && session.Uninstalling)
            UninstallFlow.Attach(session, wipe => RemoveAsync(uninstallRoot, wipe, silent: false, localAppData, Path.GetTempPath()));

        using var done = new CancellationTokenSource();
        window.Closed += (_, _) => done.Cancel();
        if (smoke is not null) window.Opened += (_, _) => _ = UiSmoke.RunAsync(session, smoke, started, () => new ShellLinks());
        else _ = CheckSignatureAsync(session, done.Token);
        // the previews' wallpaper is read after the first frame, off the UI thread (PLAN.md 8a)
        window.Opened += (_, _) => Dispatcher.UIThread.Post(() => WallpaperLoader.Start(session), DispatcherPriority.Background);
        if (staging is not null) window.Opened += (_, _) => _ = staging.SweepStaleAsync();
        window.Show();
        Dispatcher.UIThread.MainLoop(done.Token);
        CallOffNth(check);
        WindowsSetupActions.RemoveLicenseCopy(Path.GetTempPath()); // View license's copy leaves with the window (ED-NEW-inst-fix-3)
        return (session.ExitCode, session.InstallChosen);
    }

    /// <summary>
    /// The window has closed while a running nth may still have a request from it (Busy, Waiting, the not-responding question):
    /// the request is withdrawn before the process ends. Main is synchronous and the UI loop is over, so this waits here, at
    /// most 2 s (the handshake bounds the withdraw itself to 1 s); a withdraw that does not get through leaves nth as it was.
    /// </summary>
    private static void CallOffNth(NthCheck? check)
    {
        if (check is null) return;
        try { check.CloseAsync().Wait(TimeSpan.FromSeconds(2)); }
        catch (AggregateException) { } // best effort, as the handshake's own withdraw
    }

    /// <summary>
    /// The in-window install (unit U3, ED-NEW-inst-u3-1): Install and Update start an <see cref="InstallFlow"/> whose runs
    /// stage the engine in <c>%LOCALAPPDATA%\nth Setup\&lt;version&gt;</c>, check free space against the stamped sizes, and
    /// start Velopack's Setup.exe through <see cref="ProcessVelopackRunner"/>, the only thing that starts it. Before the
    /// progress shows, <see cref="NthCheck"/> takes the install hold and asks a running nth (PLAN.md 3 step 7 and 3a; unit U4,
    /// bound by ED-NEW-inst-fix-1). Returns the staging and the check, or nulls when this route installs nothing.
    /// </summary>
    private static (EngineStaging? Staging, NthCheck? Check) InstallFlowFor(SetupSession session, string localAppData, string version)
    {
        var route = session.Route;
        if (route.Action is not (InstallerAction.Install or InstallerAction.Upgrade) || route.Screen is not (SetupScreen.Welcome or SetupScreen.Update)) return (null, null);
        string folder;
        try { folder = InstallOptions.StagingFolder(localAppData, version); }
        catch (ArgumentException) { return (null, null); }
        // PLAN.md 5, "Leftovers" (unit U9): the engine is staged only after the run's folder is claimed, so no sweep takes it and Ready or Problem can delete it.
        var staging = new EngineStaging(folder, EngineStaging.EmbeddedPayload, session.Staging is { } scope ? scope.Ensure : null);
        var sizes = StampedSizes();
        var update = route.Action == InstallerAction.Upgrade;
        Action<Action> post = action => Dispatcher.UIThread.Post(action);
        var check = new NthCheck(session, ProductionRunningNth.Create, root => InstallHold.TryAcquire(root), post);
        if (update && session.Get(SetupScreen.Update) is UpdateScreen updateScreen) check.AttachUpdate(updateScreen, route.TargetRoot);
        // Unit U5 (integrated): the real shell links and HKCU after Setup.exe exits 0 (ProductionAfterSetupExit).
        var flow = new InstallFlow(
            session,
            () => new InstallRun(
                new InstallRequest(update, RunRoot(route, session.InstallRoot), route.Installed.Version, InstallOptions.ForAction(route.Action, session.DesktopShortcut), folder, sizes),
                new ProcessVelopackRunner(),
                new WindowsDiskSpace(),
                new ProductionAfterSetupExit(),
                releaseStaging: session.Staging is { } claimed ? claimed.Release : null), // U9's guarded cleanup (ED-NEW-inst-merge-13)
            staging,
            new BackgroundPollLoop(),
            post,
            gate: check);
        session.InstallRequested = flow.Begin;
        return (staging, check);
    }

    /// <summary>
    /// Where a run installs (PLAN.md 3 step 7): an update stays in the existing root; a fresh install goes to the folder chosen
    /// in Options (<see cref="SetupSession.InstallRoot"/>, the route's default until Change… picks another).
    /// </summary>
    internal static string RunRoot(SetupRoute route, string chosenRoot)
    {
        ArgumentNullException.ThrowIfNull(route);
        return route.Action == InstallerAction.Upgrade || string.IsNullOrWhiteSpace(chosenRoot) ? route.TargetRoot : chosenRoot;
    }

    /// <summary>The sizes the packaging script stamped into this setup (unit U7), or the plan's figures.</summary>
    private static InstallSizes StampedSizes() => InstallSizes.FromMetadata(key => LegacyGateHost.Metadata(key) is { Length: > 0 } value ? value : null);

    /// <summary>The self-signature check hashes the whole file, so it runs off the UI thread after the window shows.</summary>
    private static Task CheckSignatureAsync(SetupSession session, CancellationToken closed) =>
        DispatchSignatureAsync(session, SignatureCheck.VerifySelfAsync(), TimeProvider.System,
            action => Dispatcher.UIThread.Post(action), closed);

    private static async Task DispatchSignatureAsync(SetupSession session, Task<SignatureResult> check,
        TimeProvider clock, Action<Action> post, CancellationToken closed)
    {
        try
        {
            var result = await SignatureCheck.WaitForAnswerAsync(check, clock, closed).ConfigureAwait(false);
            if (closed.IsCancellationRequested) return;
            post(() =>
            {
                if (closed.IsCancellationRequested) return;
                if (result is null) session.SignatureCheckUnavailable();
                else session.SignatureChecked(result);
            });
        }
        catch (OperationCanceledException) when (closed.IsCancellationRequested) { }
    }

    /// <summary>
    /// Uninstall mode (unit U6, ED-NEW-inst-u6-1; PLAN.md 8). A run from inside the root hands off to a temporary copy before
    /// any window (UninstallLaunch) and exits; the copy, or a run from elsewhere, holds the setup lock for the whole uninstall
    /// (so nth's start sweep and before-uninstall hook stand aside, unit U9) and shows the window, or with --silent removes
    /// nth with no window: busy, refusing or silent nth exits 3, another setup on the root 4, a Velopack failure 1. The copy
    /// hands its own folder to one short-lived helper as it ends (<see cref="UninstallCopyCleanup"/>).
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
                return SecondStart(new WindowsFocuser(), FocusPatience);
            }
            if (UninstallLaunch.NeedsCopy(args, process, root))
            {
                if (silent && SilentRefusal(root) is { } refused) return refused;
                // the copy waits for this process to let go of the lock; if no copy can be made, this one goes on in place
                if (UninstallLaunch.HandOff(process!, root, temp, silent, StartCopy) is not null) return 0;
            }
            if (!silent) return RunWindow(args, instance, null, started, root).ExitCode;
            var report = RemoveAsync(root, wipe: false, silent: true, localAppData, temp).GetAwaiter().GetResult();
            return UninstallExitCode(report.Outcome);
        }
        finally
        {
            // ED-NEW-inst-picks-1 (the pick of ED-NEW-inst-fix-2): one short-lived helper removes exactly this copy's folder once this
            // process has ended; when it cannot start, the restart removal and the sweeps are left as before
            if (copy && !UninstallCopyCleanup.Begin(process, temp, Environment.ProcessId, Environment.SystemDirectory, StartCleanupHelper))
                UninstallLaunch.ScheduleCopyRemoval(process, temp, RestartRemoval.DeleteAtRestart);
        }
    }

    /// <summary>
    /// The exit code of a silent uninstall (PLAN.md 3a and 8; tested by UninstallExitCodeTests, unit U10): 0 removed, 3 nth busy or
    /// refusing, 4 another setup on the root, and 1 for everything that failed (no uninstaller, it did not start, Velopack failed).
    /// </summary>
    internal static int UninstallExitCode(UninstallOutcome outcome) => outcome switch
    {
        UninstallOutcome.Removed => 0,
        UninstallOutcome.InProgress => InstallerGate.InProgressExitCode,
        UninstallOutcome.NthNotClosed => InstallerGate.NotClosedExitCode,
        _ => 1,
    };

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
    /// <summary>Starts the cleanup helper: Windows' own shell, hidden, in the system folder (never in the folder it removes).</summary>
    private static bool StartCleanupHelper(string shell, string arguments, string workingDirectory)
    {
        var start = new ProcessStartInfo(shell) { Arguments = arguments, UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden, WorkingDirectory = workingDirectory };
        using var process = Process.Start(start);
        return process is not null;
    }

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
    /// here only (ShellLinksBindingTests). Returns what was done, or null when the shell or HKCU could not be reached at all.
    /// The silent gate takes it as best effort (Setup.exe succeeded, and nth's start-time self-heal repeats these); the
    /// window's install does not certify 100 without the required steps (ProductionAfterSetupExit, ED-NEW-inst-merge-12).
    /// </summary>
    internal static AfterSetupExitReport? FinishAfterSetupExit(string root, string? resultPath = null)
    {
        try
        {
            using var shell = new ShellLinks();
            return AfterSetupExit.Run(shell, new RegistryUserHive(Registry.CurrentUser), root, resultPath);
        }
        catch (Exception)
        {
            // No shell or no HKCU: nothing was done; the caller decides what that means.
            return null;
        }
    }
}
