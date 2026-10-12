using System.Diagnostics;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Xml.Linq;
using Marrow.Core.Port;
using E = Marrow.App.Tests.Fixtures.WindowManagerRestartExpectations;
using P = Marrow.App.Tests.Fixtures.WindowManagerProcessProtocol;
using R = Marrow.App.Tests.Fixtures.WindowManagerRestartProtocol;

namespace Marrow.App.Tests.Fixtures;

// One exact Fact in one owned runner/testhost tree. Successful children exit naturally with a
// passing TRX. Keep the first child's process handle alive until the second phase has completed.
internal sealed class WindowManagerRestartPhase(WindowManagerRestartRequest request) : IDisposable
{
    private Process? child;
    private bool started;

    internal bool ChildHasExited => child is { HasExited: true };

    internal WindowManagerRestartCompletion Run()
    {
        if (started) throw new InvalidOperationException("A restart phase cannot be launched twice.");
        started = true;
        var directory = R.PhaseDirectory(request);
        var executable = P.ResolveDotnetHost();
        if (!File.Exists(request.Root.TestAssemblyPath) ||
            !File.Exists(Path.ChangeExtension(request.Root.TestAssemblyPath, ".runtimeconfig.json")))
            throw new FileNotFoundException("The loaded restart-test DLL or runtime configuration is unavailable.");
        var arguments = new[]
        {
            "vstest", request.Root.TestAssemblyPath,
            "/Platform:x64",
            "/TestCaseFilter:FullyQualifiedName=" + request.Root.FactName,
            "/ResultsDirectory:" + Path.Combine(directory, "results"),
            "/logger:trx;LogFileName=child.trx",
            "/logger:console;verbosity=normal",
        };
        P.Write(directory, "invocation.json", new { Executable = executable, Arguments = arguments, WorkingDirectory = directory });
        var stdout = new ProcessPipe();
        var stderr = new ProcessPipe();
        var launcher = new ProcessLauncher
        {
            ExecutableURL = executable,
            Arguments = arguments,
            CurrentDirectoryURL = directory,
            Environment = R.ChildEnvironment(request),
            StandardOutput = stdout,
            StandardError = stderr,
            UsesJobObject = true,
        };
        WindowManagerProcessOwnedJob? owned = null;
        Process? runner = null;
        Task<byte[]>? output = null;
        Task<byte[]>? errors = null;
        WindowManagerRestartReady? ready = null;
        WindowManagerRestartCompletion? completed = null;
        int? childExit = null;
        int? runnerExit = null;
        var ownershipRetained = false;
        var jobEmptyBeforeCleanup = false;
        var cleanupComplete = false;
        Exception? failure = null;
        try
        {
            owned = StartOwned(launcher);
            ownershipRetained = true;
            output = Task.Run(stdout.ReadDataToEndOfFile);
            errors = Task.Run(stderr.ReadDataToEndOfFile);
            var phaseJob = owned;
            runner = Process.GetProcessById(launcher.ProcessIdentifier);
            _ = runner.SafeHandle;
            phaseJob.RequireMember(runner);
            P.Until(() =>
            {
                if (File.Exists(Path.Combine(directory, "ready.json"))) return true;
                if (!launcher.IsRunning)
                    throw new InvalidOperationException("The restart runner exited before the selected child became ready.");
                return false;
            }, P.StartupBound, "The selected restart phase never announced its child identity.");
            ready = P.Read<WindowManagerRestartReady>(directory, "ready.json");
            Assert.Equal(request, ready.Request);
            Assert.Equal(request.Token, ready.Process.Token);
            Assert.Equal(request.Root.ParentProcessId, ready.Process.ParentProcessId);
            Assert.Equal(request.Root.AppModuleVersion, ready.Process.AppModuleVersion);
            Assert.Equal(request.Root.TestModuleVersion, ready.Process.TestModuleVersion);
            Assert.NotEqual(Environment.ProcessId, ready.Process.ChildProcessId);
            Assert.NotEqual(launcher.ProcessIdentifier, ready.Process.ChildProcessId);
            Assert.NotEqual(request.PreviousChildProcessId, ready.Process.ChildProcessId);
            child = Process.GetProcessById(ready.Process.ChildProcessId);
            _ = child.SafeHandle;
            Assert.False(child.HasExited);
            Assert.Equal(child.StartTime.ToUniversalTime().Ticks, ready.ProcessStartUtcTicks);
            phaseJob.RequireMember(child);
            Assert.False(File.Exists(Path.Combine(directory, "phase-result.json")));
            P.Write(directory, "permit.json", new WindowManagerRestartPermit(ready));

            if (!child.WaitForExit(checked((int)P.ChildBound.TotalMilliseconds)))
                throw new TimeoutException("The restart child did not finish its phase and exit naturally.");
            childExit = child.ExitCode;
            if (!runner.WaitForExit(checked((int)P.CleanupBound.TotalMilliseconds)))
                throw new TimeoutException("VSTest did not finish reporting the restart child's normal result.");
            runnerExit = runner.ExitCode;
            P.Until(() => !launcher.IsRunning && phaseJob.ActiveProcesses == 0, P.CleanupBound,
                "The restart phase left an owned process alive after normal runner exit.");
            jobEmptyBeforeCleanup = true;
            AssertPassingTrx(directory, request.Root);
            Assert.Equal(0, childExit.Value);
            Assert.Equal(0, runnerExit.Value);
            Assert.Equal(0, launcher.TerminationStatus);

            var result = P.Read<WindowManagerRestartResult>(directory, "phase-result.json");
            Assert.Equal(ready, result.Identity);
            Assert.Equal(R.StorePath(request.Root), result.StorePath);
            Assert.Equal(E.SavedMain, result.Main);
            Assert.Equal(E.SavedMain, result.Settings); // OD-0151: Settings opens as the panel in the main window, so it reads the main frame
            Assert.Equal(request.ExpectedInputHash, result.InputHash);
            var snapshot = File.ReadAllBytes(P.UnderRun(directory, Path.Combine(directory, "output-store.bin")));
            E.AssertSerializedFrame(snapshot);
            Assert.Equal(result.OutputHash, R.Hash(snapshot));
            Assert.Equal(snapshot, File.ReadAllBytes(result.StorePath));
            if (request.Phase == 2)
            {
                var incoming = File.ReadAllBytes(P.UnderRun(directory, Path.Combine(directory, "input-store.bin")));
                Assert.Equal(request.ExpectedInputHash, R.Hash(incoming));
                Assert.Equal(incoming, snapshot);
            }
            Assert.False(File.Exists(Path.Combine(directory, "failure.json")));
            Assert.Equal(E.Events(request.Phase), File.ReadAllLines(Path.Combine(directory, "child.log")));
            completed = new WindowManagerRestartCompletion(result, childExit.Value, runnerExit.Value, jobEmptyBeforeCleanup);
        }
        catch (Exception error) { failure = error; }
        finally
        {
            cleanupComplete = true;
            void Cleanup(Action action)
            {
                try { action(); }
                catch (Exception cleanupError)
                {
                    failure = failure is null ? cleanupError : new AggregateException(failure, cleanupError);
                    cleanupComplete = false;
                }
            }
            // Each cleanup is attempted even if another fails, preserving both runner streams.
            // Success already observed an empty job; forced cleanup cannot manufacture a pass.
            Cleanup(() =>
            {
                if (owned is not null) owned.Dispose();
                else if (launcher.ProcessIdentifier != 0) launcher.KillTree();
            });
            Cleanup(() =>
            {
                if (launcher.ProcessIdentifier != 0)
                {
                    P.Until(() => !launcher.IsRunning, P.CleanupBound, "The owned restart runner did not stop.");
                    if (!ownershipRetained)
                        throw new InvalidOperationException("Restart job ownership was not retained; descendant cleanup is uncertified.");
                }
            });
            Cleanup(() => SaveOutput(directory, "runner.stdout.log", stdout, output));
            Cleanup(() => SaveOutput(directory, "runner.stderr.log", stderr, errors));
            Cleanup(() => ReleaseExitedRunnerHandle(launcher));
            runner?.Dispose();
            P.Write(directory, "parent-result.json", new
            {
                Request = request, Child = ready, RunnerProcessId = launcher.ProcessIdentifier,
                KernelChildExitCode = childExit, KernelRunnerExitCode = runnerExit,
                JobEmptyBeforeCleanup = jobEmptyBeforeCleanup, CleanupComplete = cleanupComplete,
                PhaseVerified = completed is not null && failure is null && cleanupComplete,
                Error = failure?.ToString(),
            });
        }
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
        Assert.True(cleanupComplete);
        return completed ?? throw new InvalidOperationException("The restart phase produced no verified result: " + directory);
    }

    private static object LauncherGate(ProcessLauncher launcher) =>
        typeof(ProcessLauncher).GetField("gate", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(launcher)
        ?? throw new MissingFieldException(nameof(ProcessLauncher), "gate");

    private static WindowManagerProcessOwnedJob StartOwned(ProcessLauncher launcher)
    {
        // The existing launcher's monitor is reentrant. Holding it over Run and duplication prevents
        // its watcher from closing the fresh job first, as NativeOwnedJob's current launch pattern does.
        lock (LauncherGate(launcher))
        {
            launcher.Run();
            try { return new WindowManagerProcessOwnedJob(launcher); }
            catch
            {
                // This exact launcher's job is still held while the watcher is excluded.
                launcher.KillTree();
                throw;
            }
        }
    }

    private static void ReleaseExitedRunnerHandle(ProcessLauncher launcher)
    {
        if (launcher.ProcessIdentifier == 0 || launcher.IsRunning) return;
        lock (LauncherGate(launcher))
        {
            var handle = typeof(ProcessLauncher).GetField("process", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(launcher) as SafeHandle
                ?? throw new MissingFieldException(nameof(ProcessLauncher), "process");
            handle.Dispose();
        }
    }

    private static void SaveOutput(string directory, string name, ProcessPipe pipe, Task<byte[]>? read)
    {
        pipe.CloseWriting();
        try
        {
            if (read is not null && !read.Wait(P.CleanupBound))
                throw new TimeoutException("An owned restart output pipe did not reach EOF.");
            if (read is not null) R.WriteBytes(directory, name, read.GetAwaiter().GetResult());
        }
        finally
        {
            if (read is null || read.IsCompleted)
            {
                var handle = typeof(ProcessPipe).GetProperty("ReadEnd", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(pipe) as SafeHandle
                    ?? throw new MissingMemberException(nameof(ProcessPipe), "ReadEnd");
                handle.Dispose();
            }
        }
    }

    private static void AssertPassingTrx(string directory, WindowManagerRestartRoot expected)
    {
        var path = P.UnderRun(directory, Path.Combine(directory, "results", "child.trx"));
        var document = XDocument.Load(path);
        XNamespace ns = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";
        var root = document.Root ?? throw new InvalidDataException("Empty child TRX.");
        Assert.Equal(ns + "TestRun", root.Name);
        var results = Assert.Single(root.Elements(ns + "Results"));
        var result = Assert.Single(results.Elements(ns + "UnitTestResult"));
        Assert.Equal(expected.FactName, Attribute(result, "testName"));
        var definitions = Assert.Single(root.Elements(ns + "TestDefinitions"));
        var definition = Assert.Single(definitions.Elements(ns + "UnitTest"));
        Assert.Equal(Attribute(result, "testId"), Attribute(definition, "id"));
        Assert.Equal(Attribute(result, "executionId"), Attribute(Assert.Single(definition.Elements(ns + "Execution")), "id"));
        var method = Assert.Single(definition.Elements(ns + "TestMethod"));
        Assert.Equal(expected.FactName, Attribute(method, "className") + "." + Attribute(method, "name"));
        Assert.Equal(expected.TestAssemblyPath, Attribute(method, "codeBase"), StringComparer.OrdinalIgnoreCase);
        if (Attribute(result, "outcome") != "Passed")
        {
            var error = result.Element(ns + "Output")?.Element(ns + "ErrorInfo");
            throw new InvalidOperationException("Restart child Fact " + Attribute(result, "outcome") + ": " + expected.FactName + "\n" +
                (string?)error?.Element(ns + "Message") + "\n" + (string?)error?.Element(ns + "StackTrace"));
        }
        var summary = Assert.Single(root.Elements(ns + "ResultSummary"));
        Assert.True(Attribute(summary, "outcome") is "Completed" or "Passed");
        var counters = Assert.Single(summary.Elements(ns + "Counters"));
        foreach (var name in new[] { "total", "executed", "passed" }) Assert.Equal("1", Attribute(counters, name));
        foreach (var name in new[] { "failed", "error", "timeout", "aborted", "inconclusive", "notRunnable", "notExecuted", "disconnected", "passedButRunAborted" })
            Assert.Equal("0", Attribute(counters, name));
        Assert.DoesNotContain(summary.Descendants(ns + "RunInfo"), info => (string?)info.Attribute("outcome") is "Error" or "Failed" or "Aborted");
    }

    private static string Attribute(XElement element, string name) => element.Attribute(name)?.Value
        ?? throw new InvalidDataException("Child TRX omitted attribute " + name + ".");

    public void Dispose() => child?.Dispose();
}
