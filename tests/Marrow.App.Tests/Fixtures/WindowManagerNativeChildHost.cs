using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Xml.Linq;
using Marrow.Core.Port;
using Xunit.Sdk;

namespace Marrow.App.Tests.Window.Native;

// Only the existing compiled test DLL is executed. Discovery/parent collection construction
// cannot initialize Avalonia. A successful runner exit without one exact passed Fact is a failure.
internal static class WindowManagerNativeChildHost
{
    internal static void RunParent(string fact)
    {
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 8 || !Environment.UserInteractive)
            throw new InvalidOperationException("Native window tests require an interactive Windows x64 desktop.");
        var request = WindowManagerNativeChildProtocol.CreateRequest(fact);
        var run = request.RunDirectory;
        Console.WriteLine("Native Fact child artifacts: " + run);
        var executable = WindowManagerNativeChildProtocol.ResolveDotnet();
        if (!File.Exists(request.TestAssemblyPath) || !File.Exists(Path.ChangeExtension(request.TestAssemblyPath, ".runtimeconfig.json")))
            throw new FileNotFoundException("The current compiled native test DLL/runtime configuration is unavailable.");
        var arguments = new[]
        {
            "vstest", request.TestAssemblyPath,
            "/Platform:x64",
            "/TestCaseFilter:FullyQualifiedName=" + fact,
            "/ResultsDirectory:" + Path.Combine(run, "results"),
            "/logger:trx;LogFileName=native-child.trx",
            "/logger:console;verbosity=normal",
        };
        WindowManagerNativeChildProtocol.Write(run, "invocation.json", new { Executable = executable, Arguments = arguments });
        var stdout = new ProcessPipe();
        var stderr = new ProcessPipe();
        var launcher = new ProcessLauncher
        {
            ExecutableURL = executable,
            Arguments = arguments,
            CurrentDirectoryURL = Path.GetDirectoryName(request.TestAssemblyPath),
            Environment = WindowManagerNativeChildProtocol.ChildEnvironment(request, executable),
            StandardOutput = stdout,
            StandardError = stderr,
            UsesJobObject = true,
        };
        var output = Task.Run(stdout.ReadDataToEndOfFile);
        var errors = Task.Run(stderr.ReadDataToEndOfFile);
        WindowManagerNativeOwnedJob? owned = null;
        Process? child = null;
        WindowManagerNativeReady? ready = null;
        WindowManagerNativeChildResult? result = null;
        var failures = new List<Exception>();
        int? runnerExit = null;
        int? childExit = null;
        var verified = false;
        var cleanupComplete = false;
        var ownershipRetained = false;
        try
        {
            owned = new WindowManagerNativeOwnedJob(launcher); // launches and retains atomically against the watcher
            ownershipRetained = true;
            WindowManagerNativeChildProtocol.Until(() =>
            {
                if (File.Exists(Path.Combine(run, "ready.json"))) return true;
                if (!launcher.IsRunning)
                    throw new InvalidOperationException("Native runner exited before the selected Fact announced its identity.");
                return false;
            }, WindowManagerNativeChildProtocol.StartupBound, "The selected native Fact did not enter its child testhost.");
            ready = WindowManagerNativeChildProtocol.Read<WindowManagerNativeReady>(run, "ready.json");
            Assert.Equal(request, ready.Request);
            Assert.NotEqual(Environment.ProcessId, ready.ChildProcessId);
            Assert.NotEqual(launcher.ProcessIdentifier, ready.ChildProcessId);
            child = Process.GetProcessById(ready.ChildProcessId);
            _ = child.SafeHandle; // pin identity while it waits for this parent's permit
            Assert.False(child.HasExited);
            Assert.Equal(ready.ChildStartedUtcTicks, child.StartTime.ToUniversalTime().Ticks);
            owned.RequireMember(child);
            // A narrow grant to our verified child, when Windows permits this parent to grant it.
            // Failure here is recorded; the child may independently already own foreground rights.
            var foregroundGranted = AllowSetForegroundWindow((uint)ready.ChildProcessId);
            var foregroundError = foregroundGranted ? 0 : Marshal.GetLastWin32Error();
            WindowManagerNativeChildProtocol.Write(run, "permit.json", new WindowManagerNativePermit(ready, foregroundGranted, foregroundError));

            WindowManagerNativeChildProtocol.Until(() => !launcher.IsRunning,
                WindowManagerNativeChildProtocol.ScenarioBound, "Native child Fact or VSTest runner did not finish within its liveness bound.");
            runnerExit = launcher.TerminationStatus;
            if (!child.WaitForExit((int)WindowManagerNativeChildProtocol.CleanupBound.TotalMilliseconds))
                throw new TimeoutException("Native child survived its completed VSTest runner.");
            childExit = child.ExitCode;
            if (File.Exists(Path.Combine(run, "child-result.json")))
                result = WindowManagerNativeChildProtocol.Read<WindowManagerNativeChildResult>(run, "child-result.json");
            VerifyTrx(request); // surfaces the child's actual assertion message and stack, including a failed fixture
            Assert.Equal(0, runnerExit.Value);
            Assert.Equal(0, childExit.Value);
            Assert.NotNull(result);
            Assert.Equal(ready, result.Identity);
            Assert.True(result.BodyPassed, result.Error);
            Assert.True(result.NativeThreadJoined, "Native child did not finish its STA/window teardown.");
            Assert.Null(result.Error);
            WindowManagerNativeChildProtocol.Until(() => owned.ActiveProcesses == 0,
                WindowManagerNativeChildProtocol.CleanupBound, "Completed native runner retained a descendant in its job.");
            verified = true;
        }
        catch (Exception error) { failures.Add(error); }
        finally
        {
            // Each cleanup gets its own attempt, so a job/pipe failure never suppresses another cleanup.
            try
            {
                if (owned is not null) owned.Dispose();
                else if (launcher.ProcessIdentifier != 0 && launcher.IsRunning) launcher.KillTree();
                if (launcher.ProcessIdentifier != 0)
                {
                    WindowManagerNativeChildProtocol.Until(() => !launcher.IsRunning,
                        WindowManagerNativeChildProtocol.CleanupBound, "Native runner did not exit after owned-job cleanup.");
                    runnerExit = launcher.TerminationStatus;
                }
                cleanupComplete = ownershipRetained || launcher.ProcessIdentifier == 0;
            }
            catch (Exception error) { failures.Add(error); }
            try { WriteOutput(run, "runner.stdout.log", stdout, output); }
            catch (Exception error) { failures.Add(error); cleanupComplete = false; }
            try { WriteOutput(run, "runner.stderr.log", stderr, errors); }
            catch (Exception error) { failures.Add(error); cleanupComplete = false; }
            child?.Dispose();
            try { ReleaseExitedRunnerHandle(launcher); }
            catch (Exception error) { failures.Add(error); cleanupComplete = false; }
        }
        WindowManagerNativeChildProtocol.Write(run, "parent-result.json", new
        {
            Request = request,
            RunnerProcessId = launcher.ProcessIdentifier,
            Child = ready,
            ChildResult = result,
            RunnerExitCode = runnerExit,
            KernelChildExitCode = childExit,
            TestPassed = verified && failures.Count == 0,
            CleanupComplete = cleanupComplete,
            Errors = failures.Select(error => error.ToString()).ToArray(),
        });
        if (failures.Count != 0)
            throw new XunitException("Native child Fact failed: " + fact + "\nArtifacts: " + run + "\n" +
                string.Join("\n", failures.Select(error => error.ToString())));
        Assert.True(verified && cleanupComplete, "Native child result or cleanup was incomplete: " + run);
        Console.WriteLine("Native child PASS: " + fact + "; artifacts=" + run);
    }

    private static void VerifyTrx(WindowManagerNativeRequest request)
    {
        var path = Path.Combine(request.RunDirectory, "results", "native-child.trx");
        if (!File.Exists(path)) throw new InvalidOperationException("Native child runner produced no TRX result: " + path);
        var run = XDocument.Load(path).Root ?? throw new InvalidDataException("Native child TRX has no root.");
        var ns = run.Name.Namespace;
        var result = Assert.Single(run.Element(ns + "Results")?.Elements(ns + "UnitTestResult") ?? []);
        Assert.Equal(request.FactName, (string?)result.Attribute("testName"));
        var definition = Assert.Single(run.Element(ns + "TestDefinitions")?.Elements(ns + "UnitTest") ?? []);
        var method = definition.Element(ns + "TestMethod");
        Assert.NotNull(method);
        Assert.Equal(request.FactName, (string?)method.Attribute("className") + "." + (string?)method.Attribute("name"));
        Assert.True(string.Equals(request.TestAssemblyPath, (string?)method.Attribute("codeBase"), StringComparison.OrdinalIgnoreCase),
            "Native child TRX belongs to a different compiled test assembly.");
        Assert.Equal((string?)definition.Attribute("id"), (string?)result.Attribute("testId"));
        Assert.Equal((string?)definition.Element(ns + "Execution")?.Attribute("id"), (string?)result.Attribute("executionId"));
        var outcome = (string?)result.Attribute("outcome");
        if (!string.Equals(outcome, "Passed", StringComparison.Ordinal))
        {
            var error = result.Element(ns + "Output")?.Element(ns + "ErrorInfo");
            throw new XunitException("Child native result " + outcome + ": " + request.FactName + "\n" +
                (string?)error?.Element(ns + "Message") + "\n" + (string?)error?.Element(ns + "StackTrace"));
        }
        var summary = run.Element(ns + "ResultSummary");
        Assert.NotNull(summary);
        Assert.True((string?)summary.Attribute("outcome") is "Completed" or "Passed", "Native child runner summary did not complete successfully.");
        var counts = summary.Element(ns + "Counters");
        Assert.NotNull(counts);
        foreach (var name in new[] { "total", "executed", "passed" }) Assert.Equal("1", (string?)counts.Attribute(name));
        foreach (var name in new[] { "failed", "error", "timeout", "aborted", "inconclusive", "notRunnable", "notExecuted", "disconnected", "passedButRunAborted" })
            Assert.Equal("0", (string?)counts.Attribute(name));
        Assert.DoesNotContain(summary.Descendants(ns + "RunInfo"), info => (string?)info.Attribute("outcome") is "Error" or "Failed" or "Aborted");
    }

    private static void WriteOutput(string run, string name, ProcessPipe pipe, Task<byte[]> read)
    {
        pipe.CloseWriting(); // also supplies EOF if launch failed before handing the pipe to a child
        if (!read.Wait(WindowManagerNativeChildProtocol.CleanupBound))
            throw new TimeoutException("Native runner output did not reach EOF: " + name);
        try
        {
            using var stream = new FileStream(Path.Combine(run, name), FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            stream.Write(read.GetAwaiter().GetResult());
            stream.Flush(flushToDisk: true);
        }
        finally
        {
            // ProcessPipe has no public read-end disposal; close only this completed, owned pipe.
            var handle = typeof(ProcessPipe).GetProperty("ReadEnd", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(pipe) as SafeHandle
                ?? throw new MissingMemberException(nameof(ProcessPipe), "ReadEnd");
            handle.Dispose();
        }
    }

    private static void ReleaseExitedRunnerHandle(ProcessLauncher launcher)
    {
        if (launcher.ProcessIdentifier == 0 || launcher.IsRunning) return;
        var type = typeof(ProcessLauncher);
        var gate = type.GetField("gate", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(launcher)
            ?? throw new MissingFieldException(type.FullName, "gate");
        lock (gate)
        {
            var handle = type.GetField("process", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(launcher) as SafeHandle
                ?? throw new MissingFieldException(type.FullName, "process");
            handle.Dispose();
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern bool AllowSetForegroundWindow(uint processId);
}
