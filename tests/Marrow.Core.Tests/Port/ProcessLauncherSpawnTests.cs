using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
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
//     void Terminate()                               the graceful step (ED-047): CTRL_BREAK_EVENT to the child's own
//                                                    process group (the child is created with
//                                                    CREATE_NEW_PROCESS_GROUP), sent from outside the test host's
//                                                    console as probe P6 did (an AttachConsole helper); never
//                                                    TerminateProcess (see the conflict note in
//                                                    ProcessLauncherLadderTests.cs)
//     void KillTree()                                TerminateJobObject
//     void KillProcess()                             TerminateProcess on the pid alone
//     void ResizePseudoConsole(PseudoConsoleSize size)
//     Only the child's own three standard handles are inherited (P6 put them in PROC_THREAD_ATTRIBUTE_HANDLE_LIST):
//     a sibling started later never holds this child's pipe ends open.
//   ProcessPipe, with a parameterless constructor
//     Action<byte[]>? ReadabilityHandler             each chunk as read, on the pool; an empty array once at the end;
//                                                    set to null, nothing more (not even the end marker) reaches the
//                                                    handler that was set before (FileHandle.readabilityHandler = nil)
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
        var seen = new TaskCompletionSource<(bool InTask, bool OnMain, int Status, bool Running, ProcessLauncher Sender)>(TaskCreationOptions.RunContinuationsAsynchronously);
        launcher.TerminationHandler = sender =>
        {
            Interlocked.Increment(ref calls);
            seen.TrySetResult((SwiftTask.IsInTask, MainActor.IsIsolated, sender.TerminationStatus, sender.IsRunning, sender));
        };
        await TaskHandle.Detached(() =>
        {
            launcher.Run(); // started from inside a task: the handler must still run outside it
            return Task.CompletedTask;
        }).Completion.WaitAsync(Bound);
        // The main loop is pumped while waiting, so a port that posts the handler to main (MainActor.Post) runs it
        // there and is caught by OnMain below, rather than never running and timing out.
        PumpMainUntil(runtime, seen.Task);
        var result = await seen.Task.WaitAsync(Bound);
        Assert.False(result.InTask);
        Assert.False(result.OnMain);
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

    // ---- Handle inheritance (R-PROC-1): each child gets its own three standard handles, nothing else -----------

    [Fact]
    [Trait("Rule", "R-PROC-1")]
    [Trait("Rule", "R-PROC-2")]
    public void A_later_sibling_does_not_inherit_a_childs_pipe_ends_so_closing_its_stdin_still_ends_it()
    {
        // Callers rely on end of file: Shell.swift:58-60 (OutputCollector closes a stream on empty data, :268-270,
        // then finished(within: 3)), StdioProcess.swift:61-64 (finishOutput on EOF) and closing stdin as the
        // shutdown trigger (StdioProcess.swift:114-121, ED-027). If B inherited A's stdin write end, A would never
        // see its input end; if B inherited A's stdout write end, A's reader would never see EOF. Both happen when
        // handles are created inheritable and CreateProcessW gets bInheritHandles=TRUE with no handle list.
        var inputA = new ProcessPipe();
        var outputA = new ProcessPipe();
        var stdoutA = new PipeRecorder(outputA);
        var a = new ProcessLauncher { ExecutableURL = SystemTools.Sort, StandardInput = inputA, StandardOutput = outputA };
        var inputB = new ProcessPipe(); // left open: B waits for the end of its input for the whole test
        var outputB = new ProcessPipe();
        var stdoutB = new PipeRecorder(outputB);
        var b = new ProcessLauncher { ExecutableURL = SystemTools.Sort, StandardInput = inputB, StandardOutput = outputB };
        a.Run();
        try
        {
            b.Run();
            inputA.Write("b\r\na\r\n"u8.ToArray());
            inputA.CloseWriting();
            WaitForExit(a);
            Assert.True(stdoutA.WaitForEnd(Bound), "A's stdout never reached end of file while B was running");
            Assert.Equal("a\r\nb\r\n"u8.ToArray(), stdoutA.Bytes);
            Assert.Equal(0, a.TerminationStatus);
            Assert.True(b.IsRunning);
            Assert.Equal(0, stdoutB.EndMarkers);
        }
        finally
        {
            if (a.IsRunning) a.KillTree();
            if (b.IsRunning) b.KillTree();
        }
    }

    // ---- Large output on both streams (R-PROC-2, PA-0008) ------------------------------------------------------

    [Fact]
    [Trait("Rule", "R-PROC-2")]
    public void A_megabyte_on_stderr_then_a_megabyte_on_stdout_arrive_whole_and_in_order_because_both_are_read_at_once()
    {
        // R-PROC-2: "Each output stream is read as bytes by its own reader on the pool" (Shell.swift:59-60 set a
        // readabilityHandler on each pipe before run). Each write is far larger than a pipe buffer, so a port that
        // reads stdout to the end before stderr, or reads only after exit, deadlocks: the child blocks on stderr.
        // The byte patterns differ (stderr starts at 1, stdout at 0) and the lengths are not multiples of 256.
        // PA-0008's bounded tail (trim to `limit` once past 2 * limit, Shell.swift:284-288) and the 3 s drain
        // (`finished(within: 3)`, Shell.swift:105 and :290-314) belong to OutputCollector, i.e. to the Shell.cs
        // port, not to this launcher: here every byte is delivered.
        const int ErrorLength = 1_048_579;
        const int OutputLength = 1_048_581;
        var output = new ProcessPipe();
        var error = new ProcessPipe();
        var stdout = new PipeRecorder(output);
        var stderr = new PipeRecorder(error);
        var launcher = new ProcessLauncher
        {
            ExecutableURL = SystemTools.PowerShell,
            Arguments = SystemTools.PowerShellScript(
                "$n = 2097152; $b = New-Object byte[] $n; for ($i = 0; $i -lt 256; $i++) { $b[$i] = $i }; " +
                "$k = 256; while ($k -lt $n) { [Array]::Copy($b, 0, $b, $k, $k); $k *= 2 }; " +
                "$e = [Console]::OpenStandardError(); $e.Write($b, 1, 1048579); $e.Flush(); " +
                "$o = [Console]::OpenStandardOutput(); $o.Write($b, 0, 1048581); $o.Flush(); exit 0"),
            StandardOutput = output,
            StandardError = error,
        };
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
        Assert.True(stderr.WaitForEnd(Bound));
        Assert.Equal(0, launcher.TerminationStatus);
        AssertPattern(stderr.Bytes, first: 1, ErrorLength);
        AssertPattern(stdout.Bytes, first: 0, OutputLength);
        Assert.Equal(0, stdout.ChunksAfterEnd + stderr.ChunksAfterEnd);
    }

    // ---- The graceful step on a real child (ED-047, probe P6 M3) ----------------------------------------------

    [Fact]
    [Trait("Rule", "R-PROC-1")]
    [Trait("Decision", "ED-047")]
    public void Terminate_alone_ends_a_pipe_child_that_handles_Ctrl_Break_and_lets_it_run_its_handler()
    {
        // ED-047 (a), the Shell shape (a job). Shell.swift:112 and MCPHub.swift:288 call process.terminate(), and
        // callers depend on it being graceful: CommandCodeSession.swift:232-234 "SIGTERM ends the run at once (exit
        // 130); the CLI has already persisted every turn it finished". A Terminate that is TerminateProcess ends the
        // child too, but with no handler run: no marker, and not the child's own exit code.
        TerminateEndsACooperatingChild(usesJobObject: true, withInput: false);
    }

    [Fact]
    [Trait("Rule", "R-PROC-1")]
    [Trait("Decision", "ED-047")]
    public void Terminate_alone_ends_a_cooperating_pipe_child_that_has_no_job_the_Stdio_shape()
    {
        // ED-047 (b): StdioProcess.swift:123 process.terminate(), "graceful step = CTRL_BREAK_EVENT to that child
        // only", with NO job object; StdioProcess.swift:18 and :42 always give the child a stdin pipe.
        TerminateEndsACooperatingChild(usesJobObject: false, withInput: true);
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
        var (launcher, grandchild, _, _) = StartChildWithGrandchild(usesJobObject: true, pseudoConsole: null);
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
        var (launcher, grandchild, _, _) = StartChildWithGrandchild(usesJobObject: false, pseudoConsole: null);
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

    [Fact]
    [Trait("Rule", "R-PROC-1")]
    [Trait("Decision", "ED-047")]
    public void Without_a_job_the_grandchild_outlives_the_child_its_termination_handler_and_the_launcher_itself()
    {
        // ED-047 (b): "TerminateProcess on that PID alone, with NO job object". TerminateProcess never reaches a
        // grandchild, so the test above also passes for a port that ignores UsesJobObject=false and puts the child
        // in a KILL_ON_JOB_CLOSE job; that job dies with its last handle, when the launcher lets go of it. So: the
        // child is killed, its termination handler has run, the launcher is unreachable and collected, and the
        // grandchild must still be alive after a bounded wait of 2,000 ms (probe P5 control B3 used the same wait).
        var grandchild = StartAndKillChildWithoutJob();
        try
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            Assert.False(grandchild.WaitForExit(2_000), "the grandchild died with the launcher: it was in a job");
        }
        finally
        {
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

    [Fact]
    [Trait("Rule", "R-PROC-2")]
    [Trait("Decision", "ED-047")]
    public void Setting_the_readability_handler_to_nil_stops_delivery_although_a_grandchild_writes_and_closes_the_pipe()
    {
        // StdioProcess.swift:176-180: "A grandchild can inherit stdout and keep it open after the provider exits",
        // so a second after exit `handle.readabilityHandler = nil` and the stream is finished by hand. Shell.swift:
        // 66-67, StdioProcess.swift:82-83 and MCPHub.swift:363-364 set it to nil to stop reading too. After the nil,
        // the grandchild (no job, so it survives KillProcess, ED-047 (b)) writes a marker and exits, which also ends
        // the pipe: neither the marker nor the end marker may reach the handler that was set before. The grandchild
        // waits for a named event, so it cannot write before the nil; its exit is the positive barrier; the
        // absence is then checked with the bounded 2,000 ms wait of P5 control B3.
        var eventName = "Marrow-ProcessLauncher-" + Guid.NewGuid().ToString("N");
        using var go = new EventWaitHandle(false, EventResetMode.ManualReset, eventName);
        var grandchildScript =
            "$null = ([Threading.EventWaitHandle]::OpenExisting('" + eventName + "')).WaitOne(60000)\n" +
            "[Console]::Out.Write('AFTER-NULL')\n[Console]::Out.Flush()\nexit 0\n";
        var (launcher, grandchild, output, recorder) = StartChildWithGrandchild(usesJobObject: false, pseudoConsole: null, grandchildScript);
        try
        {
            launcher.KillProcess();
            WaitForExit(launcher);
            output.ReadabilityHandler = null;
            var before = recorder.Bytes;
            Assert.Equal(0, recorder.EndMarkers); // the grandchild still holds the pipe open
            go.Set();
            Assert.True(grandchild.WaitForExit(Bound), "the grandchild never wrote its marker and exited");
            Assert.Equal(0, grandchild.ExitCode);
            Assert.False(recorder.WaitForText("AFTER-NULL", TimeSpan.FromMilliseconds(2_000)));
            Assert.Equal(before, recorder.Bytes);
            Assert.Equal(0, recorder.EndMarkers);
        }
        finally
        {
            Cleanup(launcher, grandchild);
        }
    }

    // ---- A .cmd or .bat target (PA-0010: npm installs provider CLIs as .cmd shims) -----------------------------

    [Fact]
    [Trait("Rule", "R-PROC-1")]
    [Trait("Decision", "ED-024")]
    public void A_cmd_shim_target_passes_its_arguments_on_literally_or_fails_explicitly_and_never_runs_them_as_commands()
    {
        // STOP F.ProcessLauncher #4: no recorded decision covers a .cmd or .bat ExecutableURL. CreateProcessW runs one
        // through cmd.exe, which re-parses the MSVCRT-quoted command line by cmd's rules: %VAR% expands, `\"`
        // flips cmd's quote state, and `&` or `|` inside the --agents JSON (ClaudeSession.swift:108) would run as
        // commands. R-PROC-1: "nothing passes through cmd /c unless a recorded decision says so". Until a decision
        // pins one behaviour, this test holds what any faithful decision keeps: on macOS Foundation passes the argv
        // to the program with no shell, so the arguments reach the program behind the shim literally (a) or the
        // launcher refuses the target with an error from Run (b); in no case does any of them run as a command.
        // The shim has npm's shape (the program, then %*); its program records its own raw command line, which is
        // parsed here by CommandLineToArgvW, as node (the program behind a real npm shim) would parse it.
        var folder = ProcessTestFolders.Create("cmd shim");
        File.WriteAllText(
            Path.Join(folder, "echo-args.ps1"),
            "[IO.File]::WriteAllText((Join-Path $PSScriptRoot 'args.txt'), [Environment]::CommandLine, (New-Object Text.UTF8Encoding $false))\r\n");
        var shim = Path.Join(folder, "tool.cmd");
        File.WriteAllText(
            shim,
            "@\"%SystemRoot%\\System32\\WindowsPowerShell\\v1.0\\powershell.exe\" -NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"%~dp0echo-args.ps1\" %*\r\n");
        string[] arguments = [
            "& echo INJECTED-A",
            "%PATH%",
            "{\"reviewer\":{\"prompt\":\"Say \\\"done\\\" & echo INJECTED-B & type nul > INJECTED-C.txt\\n\"}}",
            "caret^ pipe| (paren) !bang! <less >more",
        ];
        var output = new ProcessPipe();
        var stdout = new PipeRecorder(output);
        var launcher = new ProcessLauncher { ExecutableURL = shim, Arguments = arguments, CurrentDirectoryURL = folder, StandardOutput = output };
        Exception? refused = null;
        try
        {
            launcher.Run();
        }
        catch (Exception error) when (error is not Xunit.Sdk.XunitException)
        {
            refused = error;
        }
        var recorded = Path.Join(folder, "args.txt");
        if (refused is null)
        {
            try
            {
                WaitForExit(launcher);
            }
            finally
            {
                if (launcher.IsRunning) launcher.KillTree();
            }
            Assert.True(stdout.WaitForEnd(Bound));
            Assert.True(File.Exists(recorded), "the program behind the shim never ran");
            var argv = WindowsCommandLine.Parse(File.ReadAllText(recorded));
            var file = argv.ToList().IndexOf("-File");
            Assert.True(file >= 0);
            Assert.Equal(arguments, argv.Skip(file + 2).ToArray());
        }
        else
        {
            Assert.False(launcher.IsRunning);
            Assert.False(File.Exists(recorded));
        }
        Assert.DoesNotContain("INJECTED", stdout.Text, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Join(folder, "INJECTED-C.txt")));
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
        var (launcher, grandchild, _, _) = StartChildWithGrandchild(usesJobObject: true, pseudoConsole: new PseudoConsoleSize(120, 30));
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

    /// <summary>Runs the manual main loop in turns until <paramref name="task"/> completes or the bound passes.</summary>
    private static void PumpMainUntil(TestRuntime runtime, Task task)
    {
        for (var turn = 0; turn < 1_200 && !task.IsCompleted; turn++)
        {
            runtime.RunUntilIdle();
            task.Wait(TimeSpan.FromMilliseconds(50));
        }
    }

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
    /// powershell.exe that starts a second powershell.exe running <paramref name="grandchildScript"/> (by default a
    /// 60 s sleep; no new window, so it inherits the job and the pipes), prints "GRANDCHILD pid END", and sleeps 60 s
    /// itself. Returns once the grandchild's pid is read, with the stdout pipe and its recorder.
    /// </summary>
    private static (ProcessLauncher Launcher, Process Grandchild, ProcessPipe Output, PipeRecorder Recorder) StartChildWithGrandchild(
        bool usesJobObject, PseudoConsoleSize? pseudoConsole, string grandchildScript = "Start-Sleep 60")
    {
        var output = new ProcessPipe();
        var recorder = new PipeRecorder(output);
        var launcher = new ProcessLauncher
        {
            ExecutableURL = SystemTools.PowerShell,
            Arguments = SystemTools.PowerShellScript(
                "$p = Start-Process -FilePath powershell.exe -ArgumentList '-NoProfile','-NonInteractive','-EncodedCommand','" +
                SystemTools.Encode(grandchildScript) + "' -NoNewWindow -PassThru; " +
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
            return (launcher, grandchild, output, recorder);
        }
        catch
        {
            if (launcher.IsRunning) launcher.KillProcess();
            throw;
        }
    }

    /// <summary>
    /// Starts the Ctrl+Break child of <see cref="ProcessScripts.CtrlBreakCooperating"/>, waits until its handler is
    /// in place, calls Terminate() and nothing else, and checks the child ran its handler: its marker on stdout and
    /// its own exit code, 131. KillTree runs only in cleanup, after the assertions, if the child is still running.
    /// </summary>
    private static void TerminateEndsACooperatingChild(bool usesJobObject, bool withInput)
    {
        var output = new ProcessPipe();
        var stdout = new PipeRecorder(output);
        var launcher = new ProcessLauncher
        {
            ExecutableURL = SystemTools.PowerShell,
            Arguments = SystemTools.PowerShellEncoded(ProcessScripts.CtrlBreakCooperating()),
            StandardInput = withInput ? new ProcessPipe() : null,
            StandardOutput = output,
            UsesJobObject = usesJobObject,
        };
        var consoleBefore = HostConsole.Window;
        launcher.Run();
        try
        {
            Assert.True(stdout.WaitForText(ProcessScripts.Armed, Bound), "the child never armed its Ctrl+Break handler");
            launcher.Terminate();
            WaitForExit(launcher);
            Assert.True(stdout.WaitForText(ProcessScripts.BreakSeen, Bound));
            Assert.True(stdout.WaitForEnd(Bound));
            Assert.Equal(ProcessScripts.BreakExitCode, launcher.TerminationStatus);
            Assert.Equal(consoleBefore, HostConsole.Window); // the host kept its own console (P6 deviation 4)
        }
        finally
        {
            if (launcher.IsRunning) launcher.KillTree();
        }
    }

    /// <summary>
    /// A child with no job and no pipes starts a grandchild (a 60 s sleep), writes its pid to a file and signals a
    /// named event; the child is then killed with KillProcess and its termination handler awaited. Only the
    /// grandchild's Process is returned, so nothing the test holds keeps the launcher reachable.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Process StartAndKillChildWithoutJob()
    {
        var folder = ProcessTestFolders.Create("no job release");
        var pidFile = Path.Join(folder, "grandchild.pid");
        var eventName = "Marrow-ProcessLauncher-" + Guid.NewGuid().ToString("N");
        using var ready = new EventWaitHandle(false, EventResetMode.ManualReset, eventName);
        var exited = new ManualResetEventSlim(); // never disposed: a handler run late must not throw on the pool
        var launcher = new ProcessLauncher
        {
            ExecutableURL = SystemTools.PowerShell,
            Arguments = SystemTools.PowerShellEncoded(
                "$p = Start-Process -FilePath powershell.exe -ArgumentList '-NoProfile','-NonInteractive','-Command','Start-Sleep 60' -NoNewWindow -PassThru\n" +
                "[IO.File]::WriteAllText('" + pidFile + "', [string]$p.Id)\n" +
                "$null = ([Threading.EventWaitHandle]::OpenExisting('" + eventName + "')).Set()\n" +
                "Start-Sleep 60\n"),
            CurrentDirectoryURL = folder,
            UsesJobObject = false,
        };
        launcher.TerminationHandler = _ => exited.Set();
        launcher.Run();
        try
        {
            Assert.True(ready.WaitOne(Bound), "the child never reported its grandchild");
            var grandchild = Process.GetProcessById(int.Parse(File.ReadAllText(pidFile).Trim(), CultureInfo.InvariantCulture));
            launcher.KillProcess();
            Assert.True(exited.Wait(Bound), "the termination handler never ran");
            Assert.False(launcher.IsRunning);
            return grandchild;
        }
        catch
        {
            if (launcher.IsRunning) launcher.KillTree();
            throw;
        }
    }

    /// <summary>Asserts <paramref name="actual"/> is <paramref name="length"/> bytes of (first + i) mod 256.</summary>
    private static void AssertPattern(byte[] actual, int first, int length)
    {
        Assert.Equal(length, actual.Length);
        var mismatch = -1;
        for (var i = 0; i < length && mismatch < 0; i++)
            if (actual[i] != (byte)((first + i) % 256)) mismatch = i;
        Assert.Equal(-1, mismatch);
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
