// PORT-ONLY: the seam between TerminalHostView and the operating-system process that runs its shell. SwiftTerm's
// LocalProcessTerminalView owns its pty and child itself; here the child is a ProcessLauncher on a ConPTY (ADR-0002,
// probe P5), and a test that needs no real PowerShell supplies a stand-in through TerminalHostView.ShellFactory.
using Marrow.Core.Port;

namespace Marrow.App.Views.Terminal.Port;

/// <summary>What one shell is started with: <c>startProcess(executable:args:environment:execName:currentDirectory:)</c> and the grid.</summary>
internal sealed record ShellLaunch(
    string Executable,
    IReadOnlyList<string> Arguments,
    IReadOnlyDictionary<string, string> Environment,
    string CurrentDirectory,
    PseudoConsoleSize Size);

/// <summary>
/// The process behind a terminal view: started once on a pseudoconsole fed by <c>input</c> and read on <c>output</c>.
/// <see cref="IProcessControl.Terminate"/> is the graceful step; <see cref="IProcessControl.KillTree"/> ends the shell and
/// everything it started.
/// </summary>
internal interface IShellProcess : IProcessControl
{
    /// <summary>The OS process id of the shell; 0 before <see cref="Start"/>.</summary>
    int ProcessIdentifier { get; }

    /// <summary>
    /// Starts the shell. <paramref name="terminated"/> is called once, off the main actor, with the exit code after the
    /// shell exits by itself or is killed.
    /// </summary>
    void Start(ShellLaunch launch, ProcessPipe input, ProcessPipe output, Action<int> terminated);

    /// <summary>ResizePseudoConsole; nothing for a shell that is not running on one.</summary>
    void ResizePseudoConsole(PseudoConsoleSize size);
}

/// <summary>The real shell: a <see cref="ProcessLauncher"/> child on a ConPTY, in its own job.</summary>
internal sealed class ConPtyShell : IShellProcess
{
    private readonly ProcessLauncher launcher = new();

    public bool IsRunning => launcher.IsRunning;

    public int ProcessIdentifier => launcher.ProcessIdentifier;

    public void Start(ShellLaunch launch, ProcessPipe input, ProcessPipe output, Action<int> terminated)
    {
        launcher.ExecutableURL = launch.Executable;
        launcher.Arguments = launch.Arguments;
        launcher.Environment = launch.Environment;
        launcher.CurrentDirectoryURL = launch.CurrentDirectory;
        launcher.PseudoConsole = launch.Size;
        launcher.StandardInput = input;
        launcher.StandardOutput = output;
        launcher.UsesJobObject = true;
        launcher.TerminationHandler = host => terminated(host.TerminationStatus);
        launcher.Run();
    }

    public void Terminate() => launcher.Terminate();

    public void KillTree() => launcher.KillTree();

    public void KillProcess() => launcher.KillProcess();

    public void ResizePseudoConsole(PseudoConsoleSize size) => launcher.ResizePseudoConsole(size);
}
