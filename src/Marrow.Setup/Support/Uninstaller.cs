using Marrow.Core.Support;

namespace Marrow.Setup.Support;

/// <summary>How the uninstall ended.</summary>
public enum UninstallOutcome
{
    /// <summary>Velopack's uninstaller finished and nth Setup's own clean-up ran ("nth was removed").</summary>
    Removed,

    /// <summary>Another setup is installing or updating this root: nothing was asked, run or changed.</summary>
    InProgress,

    /// <summary>A silent uninstall found nth busy, not answering or refusing: nothing was run or changed (a window uninstall stops nth's work instead).</summary>
    NthNotClosed,

    /// <summary>The root has no <c>Update.exe</c>: there is nothing Velopack can remove, and nothing was changed.</summary>
    NoUninstaller,

    /// <summary><c>Update.exe</c> could not be started: nothing was changed.</summary>
    DidNotStart,

    /// <summary><c>Update.exe</c> ended with an exit code other than 0: some of nth's files may be left.</summary>
    VelopackFailed,
}

/// <summary>What the uninstall found and did, step by step (a step that did not run is null).</summary>
/// <param name="Outcome">How it ended.</param>
/// <param name="Nth">Where the running-nth handshake settled (null when nothing was asked).</param>
/// <param name="VelopackExitCode"><c>Update.exe</c>'s exit code, when it ran.</param>
/// <param name="Data">The data folder's removal (the switch on), else null.</param>
/// <param name="Secrets">nth's secrets in Credential Manager removed and kept (the switch on), else null.</param>
/// <param name="Shortcuts">Every desktop shortcut into the root removed, marked or not, renamed ones included.</param>
/// <param name="SignInRemoved">The Run value <c>nth</c> pointed into the root and was removed.</param>
/// <param name="RootGone">The install root no longer exists at the end.</param>
/// <param name="Sweep">nth Setup's own folders swept (<c>%LOCALAPPDATA%\nth Setup</c>, <c>%TEMP%\nth Uninstall</c>).</param>
/// <param name="Leftovers">What stayed that nth no longer needs (the leftover check), each a path or a registry key.</param>
public sealed record UninstallReport(
    UninstallOutcome Outcome,
    RunningNthStatus? Nth = null,
    int? VelopackExitCode = null,
    FolderRemovalResult? Data = null,
    (int Removed, int Kept)? Secrets = null,
    ShortcutChangeResult? Shortcuts = null,
    bool SignInRemoved = false,
    bool RootGone = false,
    SweepResult? Sweep = null,
    IReadOnlyList<string>? Leftovers = null)
{
    /// <summary>Nothing on the PC was changed (Problem's "Nothing was changed on your PC.").</summary>
    public bool NothingChanged => Outcome is UninstallOutcome.InProgress or UninstallOutcome.NthNotClosed or UninstallOutcome.NoUninstaller or UninstallOutcome.DidNotStart;

    /// <summary>The lines Problem's details show and Copy details copies.</summary>
    public string Details()
    {
        var lines = new List<string> { "Uninstall: " + Outcome };
        if (Nth is { } nth) lines.Add("nth: " + nth.State + (nth.Reason is { } reason ? " (" + reason + ")" : ""));
        if (VelopackExitCode is { } code) lines.Add("Update.exe exit code: " + code.ToString(System.Globalization.CultureInfo.InvariantCulture));
        foreach (var left in Leftovers ?? []) lines.Add("Left: " + left);
        return string.Join('\n', lines);
    }
}

/// <summary>Everything the uninstaller reaches outside its own window, so every step runs on fakes in a test.</summary>
/// <param name="Runner">Starts <c>&lt;root&gt;\Update.exe --uninstall --silent</c> (the only process it starts).</param>
/// <param name="Shell">The desktop's links (the real <c>ShellLinks</c> only in Program.cs).</param>
/// <param name="Registry">The user's hive (the Run value, the uninstall entry; the real HKCU only in Program.cs).</param>
/// <param name="Credentials">Credential Manager, for nth's secrets when the switch is on.</param>
/// <param name="LocalAppData"><c>%LOCALAPPDATA%</c>: the data folder <c>nth</c> and nth Setup's folder are under it.</param>
/// <param name="TempPath"><c>%TEMP%</c>: the uninstaller's copy is in <c>nth Uninstall</c> under it.</param>
/// <param name="RunningNth">The running-nth handshake for a root (unit U4); null asks nothing.</param>
/// <param name="TakeHold">The install hold for a root (unit U4's <c>InstallHold</c>); null when another setup holds it.</param>
/// <param name="Time">The clock for the root's wait.</param>
/// <param name="RootPatience">How long the root is given to go after Update.exe exits (default <see cref="Uninstaller.RootPatience"/>).</param>
public sealed record UninstallPorts(
    IVelopackRunner Runner,
    IShellLinks Shell,
    IUserRegistry Registry,
    ICredentialStore Credentials,
    string LocalAppData,
    string TempPath,
    Func<string, IRunningNth>? RunningNth = null,
    Func<string, IDisposable?>? TakeHold = null,
    TimeProvider? Time = null,
    TimeSpan? RootPatience = null);

/// <summary>
/// PORT-ONLY (no Swift twin; nth installer PLAN.md 8, "Uninstall flow", 3a and 10; VELOPACK_CHECK.md checks 1 and 7; unit U6,
/// ED-NEW-inst-u6-1). Removes nth once the person has said Uninstall, in this order:
/// <list type="number">
/// <item>The install hold on the root (unit U4), so an nth started now waits instead of being killed with a window up, and a
/// second setup for the same root stands aside.</item>
/// <item>A running nth is asked to quit (PLAN.md 3a): busy, it stops its work the clean way (the person's Uninstall is the
/// consent; there is no busy state on this screen) and saves and quits; one that does not answer or will not quit is left to
/// Velopack, which stops every process in the root. A silent uninstall never stops anyone's work: busy, refused or silent
/// ends it with nothing changed (exit 3), as a silent setup does.</item>
/// <item><c>&lt;root&gt;\Update.exe --uninstall --silent</c> through the runner, waited for, never killed. SOURCE FACT (Velopack
/// 1.2.161, VELOPACK_CHECK.md check 7; uninstall.rs): it stops the root's processes, runs nth's before-uninstall hook (60 s),
/// deletes the shortcuts into the root, the root's contents, <c>%TEMP%\velopack_NthApp</c> and the uninstall key, schedules
/// its own delayed removal of the root, and returns 0 even when a step failed.</item>
/// <item>Only with the switch on: the data folder <c>%LOCALAPPDATA%\nth</c> (never through a link) and nth's secrets in
/// Credential Manager, so "Everything nth kept on this PC is gone" holds.</item>
/// <item>Every desktop shortcut into the root, marked or not, renamed ones included; the sign-in Run value when it starts a
/// program in the root (both again, in case Velopack's hook did not run).</item>
/// <item>The root's own removal, which Velopack schedules about 3 s after it exits, is waited for up to 5 s; a root left with
/// nothing but <c>Update.exe</c> or empty folders is removed here (that is all Velopack's delayed removal would do).</item>
/// <item>nth Setup's own folders (<c>%LOCALAPPDATA%\nth Setup</c>, <c>%TEMP%\nth Uninstall</c>) are swept (unit U9); the running
/// copy keeps its own folder, which it schedules for removal as it exits.</item>
/// </list>
/// Nothing here throws for a file, shell or registry failure: each step reports, and the leftover check lists what stayed.
/// </summary>
public sealed class Uninstaller
{
    /// <summary>nth's data folder under <c>%LOCALAPPDATA%</c>. SOURCE FACT: AppInfo.DataFolderName (src/Marrow.Core/Support/Text.cs, OD-0135).</summary>
    public const string DataFolderName = "nth";

    /// <summary>How long the root is given to go after Update.exe exits (its own delayed removal waits 3 s; VELOPACK_CHECK.md check 7).</summary>
    public static readonly TimeSpan RootPatience = TimeSpan.FromSeconds(5);

    /// <summary>How often the root is looked at while waiting.</summary>
    public static readonly TimeSpan RootPoll = TimeSpan.FromMilliseconds(100);

    private readonly UninstallPorts ports;
    private readonly TimeProvider time;

    public Uninstaller(UninstallPorts ports)
    {
        this.ports = ports ?? throw new ArgumentNullException(nameof(ports));
        ArgumentNullException.ThrowIfNull(ports.Runner);
        ArgumentNullException.ThrowIfNull(ports.Shell);
        ArgumentNullException.ThrowIfNull(ports.Registry);
        ArgumentNullException.ThrowIfNull(ports.Credentials);
        RequireFull(ports.LocalAppData, nameof(ports.LocalAppData));
        RequireFull(ports.TempPath, nameof(ports.TempPath));
        time = ports.Time ?? TimeProvider.System;
    }

    /// <summary><c>%LOCALAPPDATA%\nth</c>.</summary>
    public string DataFolder => Path.Combine(ports.LocalAppData, DataFolderName);

    /// <summary>Raised as each step starts (on the thread that runs it), for the log and the tests.</summary>
    public event Action<string>? StepStarted;

    /// <summary>Removes nth from <paramref name="root"/>; <paramref name="wipeData"/> is the switch.</summary>
    public async Task<UninstallReport> RunAsync(string root, bool wipeData, bool silent = false, CancellationToken cancellationToken = default)
    {
        RequireFull(root, nameof(root));
        root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));

        Step("hold");
        using var hold = (ports.TakeHold ?? (r => InstallHold.TryAcquire(r)))(root);
        if (hold is null) return new UninstallReport(UninstallOutcome.InProgress);

        RunningNthStatus? nth = null;
        if (ports.RunningNth is { } factory)
        {
            Step("nth");
            var handshake = factory(root);
            nth = await handshake.AskAsync(cancellationToken).ConfigureAwait(false);
            if (silent)
            {
                if (!nth.CanProceed) return new UninstallReport(UninstallOutcome.NthNotClosed, nth);
            }
            else if (nth.State == RunningNthState.Busy)
            {
                nth = await handshake.StopWorkAndQuitAsync(cancellationToken).ConfigureAwait(false);
            }
            // a window uninstall goes on in every other case: Velopack stops what is left in the root
        }

        var update = Path.Combine(root, InstallLayout.UpdateExe);
        if (!File.Exists(update)) return new UninstallReport(UninstallOutcome.NoUninstaller, nth);

        Step("velopack");
        int exitCode;
        try
        {
            var run = ports.Runner.Start(VelopackCommand.Uninstall(root));
            exitCode = await run.Exited.ConfigureAwait(false);
        }
        catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception or IOException or UnauthorizedAccessException)
        {
            return new UninstallReport(UninstallOutcome.DidNotStart, nth);
        }
        hold.Dispose(); // Velopack is done with the root: an nth waiting at its start may carry on (it finds nothing to run)
        if (exitCode != 0) return Finish(new UninstallReport(UninstallOutcome.VelopackFailed, nth, exitCode), root);

        FolderRemovalResult? data = null;
        (int, int)? secrets = null;
        if (wipeData)
        {
            Step("data");
            data = RemoveData(root);
            Step("secrets");
            secrets = Safe(() => NthSecrets.Remove(ports.Credentials), (0, 0));
        }

        Step("desktop");
        var shortcuts = Safe(() => new DesktopShortcut(ports.Shell, root, time).RemoveIntoRoot(ShortcutScan.Full), null);
        Step("sign-in");
        var signIn = Safe(() => UninstallEntry.RemoveSignInEntry(ports.Registry, root), false);

        Step("root");
        var rootGone = await WaitForRootAsync(root, cancellationToken).ConfigureAwait(false);

        var report = new UninstallReport(UninstallOutcome.Removed, nth, exitCode, data, secrets, shortcuts, signIn, rootGone);
        return Finish(report, root);
    }

    /// <summary>The sweep of nth Setup's own folders and the leftover check, after Velopack ran (whatever it returned).</summary>
    private UninstallReport Finish(UninstallReport report, string root)
    {
        Step("sweep");
        var sweep = Safe(() => SetupSweeper.Sweep(ports.LocalAppData, ports.TempPath), null);
        return report with { Sweep = sweep, Leftovers = Leftovers(root, report.Data, sweep) };
    }

    private FolderRemovalResult RemoveData(string root)
    {
        var data = Path.TrimEndingDirectorySeparator(Path.GetFullPath(DataFolder));
        // a root that is the data folder itself was emptied by Velopack already; nothing above the data folder is ever removed
        if (string.Equals(data, root, StringComparison.OrdinalIgnoreCase)) return new FolderRemovalResult(Directory.Exists(data), 0, Directory.Exists(data) ? [data] : []);
        return FolderRemoval.Remove(data);
    }

    private async Task<bool> WaitForRootAsync(string root, CancellationToken cancellationToken)
    {
        var started = time.GetTimestamp();
        while (Directory.Exists(root))
        {
            if (time.GetElapsedTime(started) >= (ports.RootPatience ?? RootPatience)) break;
            try { await Task.Delay(RootPoll, time, cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
        }
        if (!Directory.Exists(root)) return true;
        if (!OnlyVelopackRemains(root)) return false;
        return FolderRemoval.Remove(root).Gone;
    }

    /// <summary>The root holds nothing but <c>Update.exe</c> and empty folders: what Velopack's own delayed removal deletes.</summary>
    internal static bool OnlyVelopackRemains(string root)
    {
        try
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(root, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = 0, IgnoreInaccessible = false }))
            {
                if (Directory.Exists(entry) && (File.GetAttributes(entry) & FileAttributes.ReparsePoint) == 0) continue;
                var relative = Path.GetRelativePath(root, entry);
                if (!string.Equals(relative, InstallLayout.UpdateExe, StringComparison.OrdinalIgnoreCase)) return false;
            }
            return true;
        }
        catch (Exception error) when (FolderRemoval.IsFileSystemFailure(error))
        {
            return false;
        }
    }

    /// <summary>
    /// The leftover check (PLAN.md 12, gate 2 step 7, for what nth Setup can see): the root, the uninstall key for this root,
    /// a desktop link into the root, the Run value, nth Setup's folder, and the data folder when the switch was on. The running
    /// copy's own folder is expected (it goes as the copy exits) and is not listed.
    /// </summary>
    private List<string> Leftovers(string root, FolderRemovalResult? data, SweepResult? sweep)
    {
        var left = new List<string>();
        if (Directory.Exists(root)) left.Add(root);
        if (Safe(() => ports.Registry.KeyExists(UninstallEntry.KeyPath) && ports.Registry.GetValue(UninstallEntry.KeyPath, "InstallLocation") is string location
                && InstallLayout.IsInside(root, location.Trim().Trim('"')), false))
            left.Add(@"HKCU\" + UninstallEntry.KeyPath);
        if (Safe(() => ports.Registry.GetValue(UninstallEntry.RunKeyPath, UninstallEntry.SignInValueName) is string command
                && UninstallEntry.CommandExecutable(command) is { } program && InstallLayout.IsInside(root, Environment.ExpandEnvironmentVariables(program)), false))
            left.Add(@"HKCU\" + UninstallEntry.RunKeyPath + @"\" + UninstallEntry.SignInValueName);
        left.AddRange(Safe<IReadOnlyList<string>>(() => [.. new DesktopShortcut(ports.Shell, root, time).FindIntoRoot().Links.Select(link => link.Path)], []));
        var copyFolder = SetupFolders.UninstallTemp(ports.TempPath);
        foreach (var kept in sweep?.Kept ?? [])
        {
            if (InstallLayout.IsInside(copyFolder, kept.Path)) continue; // the running copy's own folder goes as it exits
            left.Add(kept.Path);
        }
        if (data is not null) left.AddRange(data.Kept);
        return left;
    }

    private void Step(string name) => StepStarted?.Invoke(name);

    private static T Safe<T>(Func<T> step, T fallback)
    {
        try { return step(); }
        catch (Exception error) when (DesktopShortcut.IsShellFailure(error) || FolderRemoval.IsFileSystemFailure(error) || error is InvalidOperationException)
        {
            return fallback;
        }
    }

    private static void RequireFull(string path, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path, name);
        if (!Path.IsPathFullyQualified(path)) throw new ArgumentException($"'{path}' is not a full path.", name);
    }
}
