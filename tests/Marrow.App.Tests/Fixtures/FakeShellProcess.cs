using Marrow.App.Views.Terminal.Port;
using Marrow.Core.Port;

namespace Marrow.App.Tests.Fixtures;

// PORT-ONLY test doubles for TerminalHostView.ShellFactory. A terminal session always starts a shell when it is made
// (TerminalStore.swift:17-41), and a real one is a PowerShell plus a conhost, about 75 MB between them, that lives until
// it is ended. A test of the drawer's layout, the tabs or the store's bookkeeping reads none of what a real shell does, so
// it runs on this stand-in instead; the tests that read a real shell's output, process or exit stay on the real one.

/// <summary>
/// A shell that is not a process: it is "running" from its start until it is killed or told to exit, as an idle prompt that
/// ignores the graceful step is, it prints nothing and it never exits by itself. Its input is read and thrown away, so a
/// write to it returns as a write to a real pseudoconsole does.
/// </summary>
internal sealed class FakeShellProcess : IShellProcess
{
    // Windows process ids are multiples of four, so an id that is one more than a multiple of four names no real process
    // and never meets one in the process table.
    private static int lastProcessId = 0x40000001 - 4;

    private readonly object gate = new();
    private ProcessPipe? input;
    private ProcessPipe? output;
    private Action<int>? terminated;
    private volatile bool running;

    public bool IsRunning => running;

    public int ProcessIdentifier { get; private set; }

    /// <summary>What the view started it with; null before the start.</summary>
    public ShellLaunch? Launch { get; private set; }

    /// <summary>How many times the graceful step was asked for.</summary>
    public int GracefulStops { get; private set; }

    /// <summary>How many times the shell was killed while it was running.</summary>
    public int Kills { get; private set; }

    public void Start(ShellLaunch launch, ProcessPipe input, ProcessPipe output, Action<int> terminated)
    {
        lock (gate)
        {
            Launch = launch;
            this.input = input;
            this.output = output;
            this.terminated = terminated;
            ProcessIdentifier = Interlocked.Add(ref lastProcessId, 4);
            running = true;
        }
        var drain = new Thread(() => input.ReadDataToEndOfFile()) { IsBackground = true, Name = "fake shell input" };
        drain.Start();
    }

    public void Terminate()
    {
        lock (gate) GracefulStops++;
    }

    public void KillTree() => Kill();

    public void KillProcess() => Kill();

    public void ResizePseudoConsole(PseudoConsoleSize size)
    {
    }

    /// <summary>The shell exits by itself with <paramref name="code"/>: its termination handler is called here, on the caller's thread.</summary>
    public void Exit(int code) => End(code, killed: false);

    // What a kill leaves: the exit code 9 of ProcessLauncher's forced end, reported to the termination handler as a real exit is.
    private void Kill() => End(9, killed: true);

    private void End(int code, bool killed)
    {
        Action<int>? callback;
        lock (gate)
        {
            if (!running) return;
            running = false;
            if (killed) Kills++;
            callback = terminated;
        }
        CloseEnds();
        callback?.Invoke(code);
    }

    // What the end of a real shell does to its pipes: the reader of its input and the reader of its output both reach the end.
    private void CloseEnds()
    {
        ProcessPipe? readIn;
        ProcessPipe? writeOut;
        lock (gate)
        {
            readIn = input;
            writeOut = output;
        }
        readIn?.CloseWriting();
        writeOut?.CloseWriting();
    }
}

/// <summary>The scope of one <see cref="TerminalShells.Fake"/>: it keeps the shells that its views started.</summary>
internal sealed class FakeShells : IDisposable
{
    private readonly object gate = new();
    private readonly List<FakeShellProcess> started = [];
    private readonly IDisposable scope;

    public FakeShells() => scope = TerminalHostView.ShellFactory.Override(Make);

    /// <summary>The shells made so far, in the order their views were made.</summary>
    public IReadOnlyList<FakeShellProcess> Made
    {
        get
        {
            lock (gate) return [.. started];
        }
    }

    public void Dispose() => scope.Dispose();

    private FakeShellProcess Make()
    {
        var shell = new FakeShellProcess();
        lock (gate) started.Add(shell);
        return shell;
    }
}

/// <summary>The scopes a test puts the terminal views of its own flow on.</summary>
internal static class TerminalShells
{
    /// <summary>Until the result is disposed, every terminal view made in this flow runs a <see cref="FakeShellProcess"/>.</summary>
    public static FakeShells Fake() => new();
}

/// <summary>A process delegate that keeps the exit codes it was told of.</summary>
internal sealed class RecordingProcessDelegate : ITerminalProcessDelegate
{
    public List<int?> Exits { get; } = [];

    public void SizeChanged(int newCols, int newRows)
    {
    }

    public void SetTerminalTitle(string? title)
    {
    }

    public void HostCurrentDirectoryUpdate(string? directory)
    {
    }

    public void ProcessTerminated(int? exitCode) => Exits.Add(exitCode);
}
