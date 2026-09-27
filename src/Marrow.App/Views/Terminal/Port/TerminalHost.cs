// PORT-ONLY: DroppyCode/App/Views/Terminal/TerminalStore.swift's `view: LocalProcessTerminalView` is SwiftTerm, a
// third-party macOS terminal-emulator control with no Windows twin in this tree yet (a future UI unit hosts the
// visible ANSI grid the panel embeds). This file is only the small surface TerminalStore.swift itself touches:
// starting the real shell over a ConPTY (Marrow.Core.Port.ProcessLauncher), the process-exit delegate callback,
// and the font/colour/Option-as-Meta properties TerminalSession assigns. It carries no rendering.
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.UI.Port;

namespace Marrow.App.Views.Terminal.Port;

/// <summary>SwiftTerm's <c>LocalProcessTerminalViewDelegate</c>: the four callbacks TerminalBridge implements.</summary>
public interface ITerminalProcessDelegate
{
    void SizeChanged(int newCols, int newRows);

    void SetTerminalTitle(string? title);

    void HostCurrentDirectoryUpdate(string? directory);

    void ProcessTerminated(int? exitCode);
}

/// <summary>
/// The port's stand-in for <c>LocalProcessTerminalView</c>: hosts one real shell process over a pseudoconsole and
/// exposes the properties and one method TerminalSession.init and applyAppearance use.
/// </summary>
public sealed class TerminalHostView
{
    // ENGINEERING DECISION: an initial character-cell size for the pseudoconsole. TerminalStore.swift:21 gives the
    // macOS view a fixed POINT frame (640x240) that "the real size comes from the host container immediately
    // afterwards" replaces (PR-TERM-038); no host container exists yet in this unit, so this is a placeholder the
    // hosting panel (a future unit) resizes via ResizePseudoConsole.
    private static readonly PseudoConsoleSize InitialSize = new(80, 24);

    private readonly ProcessLauncher launcher = new();

    /// <summary>:22 <c>view.font = TerminalFont.preferred()</c>.</summary>
    public SwiftFont? Font { get; set; }

    /// <summary>:23 <c>view.optionAsMetaKey = true</c>; the port's Alt-as-Meta equivalent (windows_path).</summary>
    public bool OptionAsMetaKey { get; set; }

    /// <summary>:50-51.</summary>
    public SwiftColor? NativeBackgroundColor { get; set; }

    /// <summary>:50-51.</summary>
    public SwiftColor? NativeForegroundColor { get; set; }

    /// <summary>:55.</summary>
    public SwiftColor? CaretColor { get; set; }

    /// <summary>:56.</summary>
    public SwiftColor? SelectedTextBackgroundColor { get; set; }

    /// <summary>:26 <c>view.processDelegate = bridge</c>.</summary>
    public ITerminalProcessDelegate? ProcessDelegate { get; set; }

    /// <summary>Swift <c>process.isRunning</c>, read through the view as SwiftTerm exposes it.</summary>
    public bool IsRunning => launcher.IsRunning;

    /// <summary>The real OS process id of the launched shell; 0 before <see cref="StartProcess"/>.</summary>
    public int ProcessId => launcher.ProcessIdentifier;

    /// <summary>
    /// :40 <c>view.startProcess(executable:args:environment:execName:currentDirectory:)</c>. DEVIATION OD-0008 C5 /
    /// PR-TERM-042: <paramref name="execName"/> (upstream's hyphen-prefixed login-shell argv[0]) is accepted for
    /// citation parity but not used: Windows cannot set argv[0] independently of the executable path
    /// (porting/port_impacts.md:438 entry 215).
    /// </summary>
    public void StartProcess(
        string executable,
        IReadOnlyList<string> args,
        IReadOnlyDictionary<string, string> environment,
        string execName,
        string currentDirectory)
    {
        _ = execName; // PR-TERM-042: dropped on Windows.
        launcher.ExecutableURL = executable;
        launcher.Arguments = args;
        launcher.Environment = environment;
        launcher.CurrentDirectoryURL = currentDirectory;
        launcher.PseudoConsole = InitialSize;
        launcher.StandardInput = new ProcessPipe();
        launcher.StandardOutput = new ProcessPipe();
        launcher.UsesJobObject = true;
        launcher.TerminationHandler = host => ProcessDelegate?.ProcessTerminated(host.TerminationStatus);
        launcher.Run();
    }

    /// <summary>:61 <c>view.terminate()</c>: the graceful step only, no escalation (PR-TERM-046).</summary>
    public void Terminate() => launcher.Terminate();
}

/// <summary>
/// DEVIATION OD-0008 C5. DroppyCode/App/Views/Terminal/TerminalStore.swift:28's
/// <c>LoginEnvironment.userShell()</c> (Core/Support/Shell.swift:429-439: SHELL, then the password database, then
/// "/bin/zsh") is replaced by PowerShell 7 if installed, otherwise Windows PowerShell (OD-0008 C5, resolving
/// UNK-0005). ED-222 settles PR-TERM-039: <see cref="LoginEnvironment.Which"/> searches PATH first for pwsh.exe,
/// then for powershell.exe; if neither is found, use the absolute System32 WindowsPowerShell path.
/// </summary>
public static class WindowsShell
{
    public static string Resolve() => Resolve(LoginEnvironment.Current);

    // CreateProcessW does not search PATH for a bare lpApplicationName the way it does when only lpCommandLine is
    // given, so the fallback is resolved through the same PATH lookup as the preferred one, then (System32 always
    // has powershell.exe, but is not guaranteed to be on PATH in a stripped test environment) an absolute path
    // built from the Windows system directory.
    internal static string Resolve(IReadOnlyDictionary<string, string>? environment) =>
        LoginEnvironment.Which("pwsh.exe", environment)
        ?? LoginEnvironment.Which("powershell.exe", environment)
        ?? Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
}
