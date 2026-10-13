using Marrow.Core.Support;

namespace Marrow.Setup.Support;

/// <summary>Which Velopack program a command starts.</summary>
public enum VelopackCommandKind
{
    /// <summary>Velopack's <c>Setup.exe</c>, staged as <c>nth Setup Engine.exe</c>: a fresh install or an update.</summary>
    Install,

    /// <summary>The install root's <c>Update.exe</c>, uninstalling.</summary>
    Uninstall,
}

/// <summary>
/// One run of a Velopack program, fully decided before it starts: the executable, its arguments in order, its working
/// folder and the environment variables added to the setup's own. Build it with <see cref="Install"/> or
/// <see cref="Uninstall"/>; nothing else decides Velopack's command lines.
/// </summary>
public sealed record VelopackCommand(
    VelopackCommandKind Kind,
    string Executable,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory,
    IReadOnlyDictionary<string, string> Environment,
    string InstallRoot,
    string? LogPath)
{
    /// <summary>
    /// <c>Setup.exe --silent --log &lt;log&gt; --installto &lt;root&gt;</c> (PLAN.md 3, step 7) with <c>NTH_SETUP_OPTIONS</c> set to
    /// the options file, started in the engine's own folder. Always silent: Velopack's splash and dialogs never show, and
    /// silent does not start nth. SOURCE FACT (Velopack 1.2.161 tag source, VELOPACK_CHECK.md checks 4 and 10): the app is
    /// started only when not silent (install.rs:252-255), and the install hook inherits Setup.exe's environment
    /// (process_win.rs:135-156), so the variable reaches it; the hook still falls back to the fixed path.
    /// </summary>
    public static VelopackCommand Install(string setupEngine, string installRoot, string logPath, string optionsPath)
    {
        RequireFull(setupEngine, nameof(setupEngine));
        RequireFull(installRoot, nameof(installRoot));
        RequireFull(logPath, nameof(logPath));
        RequireFull(optionsPath, nameof(optionsPath));
        return new VelopackCommand(
            VelopackCommandKind.Install,
            setupEngine,
            ["--silent", "--log", logPath, "--installto", installRoot],
            Path.GetDirectoryName(setupEngine)!,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [InstallOptions.EnvironmentVariable] = optionsPath },
            installRoot,
            logPath);
    }

    /// <summary>
    /// <c>&lt;root&gt;\Update.exe --uninstall --silent</c>, started in the root. SOURCE FACT (Velopack 1.2.161,
    /// VELOPACK_CHECK.md check 7): <c>--uninstall</c> is an alias of <c>uninstall</c> (update.rs:58-61), <c>-s/--silent</c> is
    /// global (update.rs:44), and this is Velopack's own <c>QuietUninstallString</c> (registry.rs:34-36; the same strings are
    /// in <c>vpk/1.2.161/vendor/update.exe</c>). Velopack stops every process in the root first, so the uninstaller runs
    /// from a temporary copy (unit U6).
    /// </summary>
    public static VelopackCommand Uninstall(string installRoot)
    {
        RequireFull(installRoot, nameof(installRoot));
        return new VelopackCommand(
            VelopackCommandKind.Uninstall,
            Path.Combine(installRoot, "Update.exe"),
            ["--uninstall", "--silent"],
            installRoot,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            installRoot,
            LogPath: null);
    }

    private static void RequireFull(string path, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path, name);
        if (!Path.IsPathFullyQualified(path)) throw new ArgumentException($"'{path}' is not a full path.", name);
    }
}

/// <summary>A started Velopack program: its process id and its exit code once it ends. It is never killed.</summary>
public sealed record VelopackRun(int ProcessId, Task<int> Exited);

/// <summary>
/// PORT-ONLY (nth installer PLAN.md 10, unit U0, ED-NEW-inst-u0-1). The only thing that starts Velopack's
/// <c>Setup.exe</c> or <c>Update.exe</c>. Production: <c>Port/ProcessVelopackRunner.cs</c> (a GUI-subsystem child with no
/// window). Tests: <c>FakeVelopackRunner</c> (unit U3) acts out a recorded timeline in a temporary folder. There is no
/// cancel: Velopack rolls back only if its program finishes, so a run is always waited for (PLAN.md 3, Close).
/// </summary>
public interface IVelopackRunner
{
    /// <summary>Starts the command and returns once the process exists; throws if it cannot be started.</summary>
    VelopackRun Start(VelopackCommand command);
}
