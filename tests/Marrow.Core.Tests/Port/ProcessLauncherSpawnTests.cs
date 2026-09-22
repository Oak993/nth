using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using Marrow.Core.Port;
using Marrow.Core.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Core.Tests.Port;

// F.ProcessLauncher, part 1: real children through CreateProcessW (ADR-0002, R-PROC-1, R-PROC-2; probes P5 and P6).
// The children are Windows system binaries only (where.exe, sort.exe, powershell.exe with a one-line script):
// never an AI CLI (R-TEST-10). Waits are bounded waits on events, never sleeps.
//
// The surface these tests assume, in namespace Marrow.Core.Port, standing in for Foundation Process and Pipe as
// Shell.swift:45-60, StdioProcess.swift:17-45, MCPHub.swift:332-343, MCPProbe.swift:122-133 and
// HangWatchdog.swift:95-101 use them:
//   ProcessLauncher : IProcessControl, with a parameterless constructor
//     string? ExecutableURL                          the executable's path (Swift executableURL)
//     IReadOnlyList<string> Arguments                (default empty)
//     string? CurrentDirectoryURL                    null: the parent's
//     IReadOnlyDictionary<string, string>? Environment   null: inherit; set: replaces the whole environment
//     ProcessPipe? StandardInput, StandardOutput, StandardError   null: the null device (R-PROC-1)
//     bool UsesJobObject                             default true (Shell, MCPHub, MCPProbe); false for StdioProcess (ED-047 (b))
//     PseudoConsoleSize? PseudoConsole               null: none; set: the child runs on a ConPTY (P5), whose
//                                                    output arrives on StandardOutput and whose input is StandardInput
//     Action<ProcessLauncher>? TerminationHandler    called once after exit, off the main actor, outside any task
//     void Run()                                     throws when the process cannot be started
//     bool IsRunning; int ProcessIdentifier; int TerminationStatus; void WaitUntilExit()
//     void Terminate()                               the graceful step (ED-047)
//     void KillTree()                                TerminateJobObject
//     void KillProcess()                             TerminateProcess on the pid alone
//     void ResizePseudoConsole(PseudoConsoleSize size)
//   ProcessPipe, with a parameterless constructor
//     Action<byte[]>? ReadabilityHandler             each chunk as read, on the pool; an empty array once at the end
//     void Write(byte[] data)                        throws IOException once the reading side is gone
//     void CloseWriting()
//     byte[] ReadDataToEndOfFile()
//   PseudoConsoleSize(int Columns, int Rows), a readonly record struct
public sealed class ProcessLauncherSpawnTests
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(60);

    // ---- Pipes and exit status (R-PROC-2, PA-0008) -------------------------------------------------------------

    [Fact]
    [Trait("Rule", "R-PROC-1")]
    [Trait("Rule", "R-PROC-2")]
    public void Each_stream_arrives_as_raw_bytes_on_its_own_pipe_ends_once_and_the_exit_status_is_the_childs()
    {
        // A lone CR and an invalid UTF-8 byte must come through untouched: bytes, never text lines (R-PROC-2).
        var output = new ProcessPipe();
        var error = new ProcessPipe();
        var stdout = new PipeRecorder(output);
        var stderr = new PipeRecorder(error);
        var launcher = new ProcessLauncher
        {
            ExecutableURL = SystemTools.PowerShell,
            Arguments = SystemTools.PowerShellScript(
                "$o=[Console]::OpenStandardOutput(); $o.Write([byte[]](97,13,98,10,255),0,5); $o.Flush(); " +
                "$e=[Console]::OpenStandardError(); $e.Write([byte[]](101,114,114),0,3); $e.Flush(); exit 7"),
            StandardOutput = output,
            StandardError = error,
        };
        launcher.Run();
        WaitForExit(launcher);
        Assert.True(stdout.WaitForEnd(Bound));
        Assert.True(stderr.WaitForEnd(Bound));
        Assert.Equal(new byte[] { 97, 13, 98, 10, 255 }, stdout.Bytes);
        Assert.Equal(new byte[] { 101, 114, 114 }, stderr.Bytes);
        Assert.Equal(1, stdout.EndMarkers);
        Assert.Equal(1, stderr.EndMarkers);
        Assert.Equal(0, stdout.ChunksAfterEnd + stderr.ChunksAfterEnd);
        Assert.Equal(7, launcher.TerminationStatus);
        Assert.False(launcher.IsRunning);
    }

    [Fact]
    [Trait("Rule", "R-PROC-2")]
    public async Task One_pipe_can_take_both_streams_and_be_read_to_the_end()
    {
        // HangWatchdog.swift:98-101: standardOutput and standardError are the same Pipe, read to end of file.
        var pipe = new ProcessPipe();
        var launcher = new ProcessLauncher
        {
            ExecutableURL = SystemTools.PowerShell,
            Arguments = SystemTools.PowerShellScript(
                "$o=[Console]::OpenStandardOutput(); $o.Write([byte[]](111,117,116),0,3); $o.Flush(); " +
                "$e=[Console]::OpenStandardError(); $e.Write([byte[]](101,114,114),0,3); $e.Flush(); exit 0"),
            StandardOutput = pipe,
            StandardError = pipe,
        };
        launcher.Run();
        var data = await Task.Run(pipe.ReadDataToEndOfFile).WaitAsync(Bound);
        WaitForExit(launcher);
        Assert.Equal("outerr"u8.ToArray(), data);
        Assert.Equal(0, launcher.TerminationStatus);
    }

    [Fact]
    [Trait("Rule", "R-PROC-1")]
    [Trait("Rule", "R-PROC-2")]
    public void A_failing_child_reports_its_status_with_stdout_empty_and_its_message_on_stderr()
    {
        var folder = ProcessTestFolders.Create("status");
        var output = new ProcessPipe();
        var error = new ProcessPipe();
        var stdout = new PipeRecorder(output);
        var stderr = new PipeRecorder(error);
        var launcher = new ProcessLauncher
        {
            ExecutableURL = SystemTools.Where,
            Arguments = ["no-such-file-" + Guid.NewGuid().ToString("N")],
            CurrentDirectoryURL = folder,
            StandardOutput = output,
            StandardError = error,
        };
        launcher.Run();
        WaitForExit(launcher);
        Assert.True(stdout.WaitForEnd(Bound));
        Assert.True(stderr.WaitForEnd(Bound));
        Assert.Equal(1, launcher.TerminationStatus); // where.exe: 1 when nothing matches
        Assert.Empty(stdout.Bytes);
        Assert.NotEmpty(stderr.Bytes);
    }

    [Fact]
    [Trait("Rule", "R-PROC-1")]
    public async Task The_termination_handler_runs_once_after_exit_outside_any_task_with_the_final_status()
    {
        // Shell.swift:73-75 and StdioProcess.swift:76-78 resume or finish from terminationHandler, a Foundation
        // callback on a background thread: outside any Swift task (R-TASK-2), off the main actor (R-ISO-4).
        using var runtime = new TestRuntime();
        var folder = ProcessTestFolders.Create("handler");
        var launcher = new ProcessLauncher { ExecutableURL = SystemTools.Where, Arguments = ["/Q", "nothing-here"], CurrentDirectoryURL = folder };
        var calls = 0;
        var seen = new TaskCompletionSource<(bool InTask, int Status, bool Running, ProcessLauncher Sender)>(TaskCreationOptions.RunContinuationsAsynchronously);
        launcher.TerminationHandler = sender =>
        {
            Interlocked.Increment(ref calls);
            seen.TrySetResult((SwiftTask.IsInTask, sender.TerminationStatus, sender.IsRunning, sender));
        };
        await TaskHandle.Detached(() =>
        {
            launcher.Run(); // started from inside a task: the handler must still run outside it
            return Task.CompletedTask;
        }).Completion.WaitAsync(Bound);
        var result = await seen.Task.WaitAsync(Bound);
        Assert.False(result.InTask);
        Assert.Equal(1, result.Status);
        Assert.False(result.Running);
        Assert.Same(launcher, result.Sender);
        Assert.Equal(1, Volatile.Read(ref calls));
    }

    [Fact]
    [Trait("Rule", "R-PROC-1")]
    public void A_missing_executable_throws_from_run_and_never_calls_the_termination_handler()
    {
        // StdioProcess.swift:79-86 and MCPHub.swift:360-366 catch the throw from process.run(). The error text is
        // Foundation's NSError text, not settled for Windows, so only the throw is asserted.
        var folder = ProcessTestFolders.Create("missing");
        var launcher = new ProcessLauncher { ExecutableURL = Path.Join(folder, "absent.exe") };
        var calls = 0;
        launcher.TerminationHandler = _ => Interlocked.Increment(ref calls);
        Assert.ThrowsAny<Exception>(launcher.Run);
        Assert.False(launcher.IsRunning);
        Assert.Equal(0, Volatile.Read(ref calls));
    }

    // ---- Standard input (R-PROC-1: no input means the null device; P6: a dead reader is an IOException) --------

    [Fact]
    [Trait("Rule", "R-PROC-1")]
    public void With_no_input_stdin_is_the_null_device_so_a_reader_sees_end_of_file_at_once()
    {
        // Shell.swift:56 standardInput = FileHandle.nullDevice. sort.exe reads stdin to the end: it would wait
        // forever on an inherited or unclosed stdin.
        var output = new ProcessPipe();
        var stdout = new PipeRecorder(output);
        var launcher = new ProcessLauncher { ExecutableURL = SystemTools.Sort, StandardOutput = output };
        launcher.Run();
        try
        {
            WaitForExit(launcher);
        }
        finally
        {
            if (launcher.IsRunning) launcher.KillTree();
        }
        Assert.True(stdout.WaitForEnd(Bound));
        Assert.Equal(0, launcher.TerminationStatus);
        Assert.Empty(stdout.Bytes);
    }

    [Fact]
    [Trait("Rule", "R-PROC-1")]
    [Trait("Rule", "R-PROC-2")]
    public void Input_written_to_the_stdin_pipe_then_closed_reaches_the_child()
    {
        // Shell.swift:90-95: write the input, then close the write end.
        var input = new ProcessPipe();
        var output = new ProcessPipe();
        var stdout = new PipeRecorder(output);
        var launcher = new ProcessLauncher { ExecutableURL = SystemTools.Sort, StandardInput = input, StandardOutput = output };
        launcher.Run();
        try
        {
            input.Write("b\r\na\r\n"u8.ToArray());
            input.CloseWriting();
            WaitForExit(launcher);
        }
        finally
        {
            if (launcher.IsRunning) launcher.KillTree();
        }
        Assert.True(stdout.WaitForEnd(Bound));
        Assert.Equal("a\r\nb\r\n"u8.ToArray(), stdout.Bytes);
        Assert.Equal(0, launcher.TerminationStatus);
    }

    [Fact]
    [Trait("Rule", "R-PROC-2")]
    public void Writing_to_the_stdin_of_a_child_that_exited_throws_an_IOException_the_caller_can_ignore()
    {
        // StdioProcess.swift:101 `try? handle.write(contentsOf:)`. P6 measured ERROR_NO_DATA (232) on every such
        // write; ERROR_BROKEN_PIPE (109) is the other code the probe accepts. where.exe never reads stdin.
        var folder = ProcessTestFolders.Create("dead stdin");
        var input = new ProcessPipe();
        var launcher = new ProcessLauncher { ExecutableURL = SystemTools.Where, Arguments = ["/Q", "nothing-here"], CurrentDirectoryURL = folder, StandardInput = input };
        launcher.Run();
        WaitForExit(launcher);
        var error = Assert.ThrowsAny<IOException>(() => input.Write("{}\n"u8.ToArray()));
        Assert.Contains(error.HResult & 0xFFFF, new[] { 232, 109 });
    }

    // ---- Environment, working directory, executable path (R-PROC-1, PA-0009) ----------------------------------

    [Fact]
    [Trait("Rule", "R-PROC-1")]
    public void A_set_environment_replaces_the_whole_environment_and_a_nil_one_inherits()
    {
        // `process.environment = env` (Shell.swift:49): the child sees exactly env. where.exe $VAR:pattern searches
        // the folders listed in VAR, so it finds itself through PATH only when PATH reached it.
        var inherited = RunWhere(["/Q", "$PATH:where.exe"], environment: null, directory: null);
        Assert.Equal(0, inherited);
        var replaced = RunWhere(["/Q", "$PATH:where.exe"], environment: SystemRootOnly(), directory: null);
        Assert.Equal(1, replaced);
    }

    [Fact]
    [Trait("Rule", "R-PROC-1")]
    public void An_environment_value_with_spaces_and_non_ASCII_text_reaches_the_child_intact()
    {
        var folder = ProcessTestFolders.Create("env caf\U000000E9 value"); // U+00E9 LATIN SMALL LETTER E WITH ACUTE
        ProcessTestFolders.Touch(folder, "probe.txt");
        var environment = SystemRootOnly();
        environment["MARROW_PROBE_DIR"] = folder;
        Assert.Equal(0, RunWhere(["/Q", "$MARROW_PROBE_DIR:probe.txt"], environment, directory: null));
        environment["MARROW_PROBE_DIR"] = folder + "-not-there";
        Assert.Equal(1, RunWhere(["/Q", "$MARROW_PROBE_DIR:probe.txt"], environment, directory: null));
    }

    [Fact]
    [Trait("Rule", "R-PROC-1")]
    public void The_working_directory_is_honoured_with_spaces_and_non_ASCII_text_in_its_path()
    {
        // Shell.swift:48 currentDirectoryURL; where.exe looks in the current directory first.
        var folder = ProcessTestFolders.Create("cwd caf\U000000E9 dir"); // U+00E9 LATIN SMALL LETTER E WITH ACUTE
        var other = ProcessTestFolders.Create("cwd other");
        var name = "probe-" + Guid.NewGuid().ToString("N") + ".txt";
        ProcessTestFolders.Touch(folder, name);
        Assert.Equal(0, RunWhere(["/Q", name], environment: null, directory: folder));
        Assert.Equal(1, RunWhere(["/Q", name], environment: null, directory: other));
    }

    [Fact]
    [Trait("Rule", "R-PROC-1")]
    public void An_executable_whose_path_has_spaces_and_non_ASCII_text_starts()
    {
        var folder = ProcessTestFolders.Create("exe caf\U000000E9 dir"); // U+00E9 LATIN SMALL LETTER E WITH ACUTE
        var copy = Path.Join(folder, "where.exe");
        File.Copy(SystemTools.Where, copy);
        var cwd = ProcessTestFolders.Create("exe cwd");
        ProcessTestFolders.Touch(cwd, "probe.txt");
        var launcher = new ProcessLauncher { ExecutableURL = copy, Arguments = ["/Q", "probe.txt"], CurrentDirectoryURL = cwd };
        launcher.Run();
        WaitForExit(launcher);
        Assert.Equal(0, launcher.TerminationStatus);
    }

    // ---- Process identity and the job (ADR-0002, P5, PA-0007, ED-047) ------------------------------------------

    [Fact]
    [Trait("Rule", "R-PROC-1")]
    [Trait("Decision", "ED-047")]
    public void The_process_identifier_is_the_childs_pid_and_KillTree_ends_it()
    {
        var input = new ProcessPipe(); // left open: sort.exe waits for the end of its input
        var launcher = new ProcessLauncher { ExecutableURL = SystemTools.Sort, StandardInput = input };
        launcher.Run();
        try
        {
            Assert.True(launcher.IsRunning);
            Assert.True(launcher.ProcessIdentifier > 0);
            using var child = Process.GetProcessById(launcher.ProcessIdentifier);
            Assert.Equal("sort", child.ProcessName, ignoreCase: true);
            launcher.KillTree();
            WaitForExit(launcher);
            Assert.False(launcher.IsRunning);
            Assert.True(child.WaitForExit(Bound));
        }
        finally
        {
            if (launcher.IsRunning) launcher.KillProcess();
        }
    }

    [Fact]
    [Trait("Rule", "R-PROC-1")]
    [Trait("Decision", "ED-047")]
    public void KillTree_ends_the_child_and_the_grandchild_it_started()
    {
        // ED-047 (a): Shell's kill is the group kill (Shell.swift:116, -pid), on Windows TerminateJobObject. P5 A5.
        var (launcher, grandchild) = StartChildWithGrandchild(usesJobObject: true, pseudoConsole: null);
        try
        {
            launcher.KillTree();
            WaitForExit(launcher);
            Assert.True(grandchild.WaitForExit(Bound));
        }
        finally
        {
            Cleanup(launcher, grandchild);
        }
    }

    [Fact]
    [Trait("Rule", "R-PROC-1")]
    [Trait("Decision", "ED-047")]
    public void Without_a_job_KillProcess_ends_the_child_alone_and_the_grandchild_survives()
    {
        // ED-047 (b): StdioProcess.swift:126 kill(pid, SIGKILL) takes the pid alone; on Windows TerminateProcess
        // with no job object, "so grandchildren survive exactly as they do on macOS".
        var (launcher, grandchild) = StartChildWithGrandchild(usesJobObject: false, pseudoConsole: null);
        try
        {
            launcher.KillProcess();
            WaitForExit(launcher);
            grandchild.Refresh();
            Assert.False(grandchild.HasExited);
        }
        finally
        {
            Cleanup(launcher, grandchild);
        }
    }

    // ---- ConPTY and the job in one attribute list (ADR-0002, probe P5) ----------------------------------------

    [Fact]
    [Trait("Rule", "R-PROC-1")]
    public void A_pseudoconsole_child_sees_a_console_of_the_given_size_and_follows_a_resize()
    {
        // P5 A1, A2, A4: both of the child's streams arrive on the pty output; the child is not redirected; its
        // column count follows ResizePseudoConsole.
        var input = new ProcessPipe();
        var output = new ProcessPipe();
        var pty = new PipeRecorder(output);
        var launcher = new ProcessLauncher
        {
            ExecutableURL = SystemTools.PowerShell,
            Arguments = SystemTools.PowerShellScript(
                "'W=' + [Console]::WindowWidth + '=W'; 'R=' + [Console]::IsOutputRedirected + '=R'; " +
                "[Console]::Error.WriteLine('E=err=E'); $null = [Console]::ReadLine(); 'W=' + [Console]::WindowWidth + '=W'"),
            StandardInput = input,
            StandardOutput = output,
            PseudoConsole = new PseudoConsoleSize(73, 25),
        };
        launcher.Run();
        try
        {
            Assert.True(pty.WaitForText("W=73=W", Bound));
            Assert.True(pty.WaitForText("R=False=R", Bound));
            Assert.True(pty.WaitForText("E=err=E", Bound));
            launcher.ResizePseudoConsole(new PseudoConsoleSize(150, 40));
            input.Write("go\r"u8.ToArray());
            Assert.True(pty.WaitForText("W=150=W", Bound));
            WaitForExit(launcher);
        }
        finally
        {
            if (launcher.IsRunning) launcher.KillTree();
        }
    }

    [Fact]
    [Trait("Rule", "R-PROC-1")]
    [Trait("Decision", "ED-047")]
    public void A_pseudoconsole_child_is_in_the_job_so_KillTree_ends_it_and_its_grandchild()
    {
        // ADR-0002: PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE and PROC_THREAD_ATTRIBUTE_JOB_LIST in ONE attribute list, so
        // the child is in the job from its first instruction (P5 A3, A5).
        var (launcher, grandchild) = StartChildWithGrandchild(usesJobObject: true, pseudoConsole: new PseudoConsoleSize(120, 30));
        try
        {
            launcher.KillTree();
            WaitForExit(launcher);
            Assert.True(grandchild.WaitForExit(Bound));
        }
        finally
        {
            Cleanup(launcher, grandchild);
        }
    }

    private static void WaitForExit(ProcessLauncher launcher) =>
        Assert.True(Task.Run(launcher.WaitUntilExit).Wait(Bound), "the child did not exit in time");

    private static Dictionary<string, string> SystemRootOnly() =>
        new(StringComparer.Ordinal) { ["SystemRoot"] = Environment.GetFolderPath(Environment.SpecialFolder.Windows) };

    private static int RunWhere(IReadOnlyList<string> arguments, IReadOnlyDictionary<string, string>? environment, string? directory)
    {
        var launcher = new ProcessLauncher
        {
            ExecutableURL = SystemTools.Where,
            Arguments = arguments,
            Environment = environment,
            CurrentDirectoryURL = directory ?? ProcessTestFolders.Create("where cwd"),
        };
        launcher.Run();
        WaitForExit(launcher);
        return launcher.TerminationStatus;
    }

    /// <summary>
    /// powershell.exe that starts a second powershell.exe (sleeping 60 s, no new window, so it inherits the job and
    /// the pipes), prints "GRANDCHILD pid END", and sleeps 60 s itself. Returns once the grandchild's pid is read.
    /// </summary>
    private static (ProcessLauncher Launcher, Process Grandchild) StartChildWithGrandchild(bool usesJobObject, PseudoConsoleSize? pseudoConsole)
    {
        var output = new ProcessPipe();
        var recorder = new PipeRecorder(output);
        var launcher = new ProcessLauncher
        {
            ExecutableURL = SystemTools.PowerShell,
            Arguments = SystemTools.PowerShellScript(
                "$p = Start-Process -FilePath powershell.exe -ArgumentList '-NoProfile','-NonInteractive','-Command','Start-Sleep 60' -NoNewWindow -PassThru; " +
                "[Console]::Out.WriteLine('GRANDCHILD ' + $p.Id + ' END'); [Console]::Out.Flush(); Start-Sleep 60"),
            StandardInput = pseudoConsole is null ? null : new ProcessPipe(),
            StandardOutput = output,
            UsesJobObject = usesJobObject,
            PseudoConsole = pseudoConsole,
        };
        launcher.Run();
        try
        {
            Assert.True(recorder.WaitForText(" END", Bound), "the child never reported its grandchild");
            var match = Regex.Match(recorder.Text, "GRANDCHILD ([0-9]+) END");
            Assert.True(match.Success);
            var grandchild = Process.GetProcessById(int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture));
            Assert.False(grandchild.HasExited);
            return (launcher, grandchild);
        }
        catch
        {
            if (launcher.IsRunning) launcher.KillProcess();
            throw;
        }
    }

    private static void Cleanup(ProcessLauncher launcher, Process grandchild)
    {
        if (launcher.IsRunning) launcher.KillProcess();
        try
        {
            if (!grandchild.HasExited) grandchild.Kill();
        }
        catch (InvalidOperationException)
        {
            // already gone
        }
        grandchild.Dispose();
    }
}
