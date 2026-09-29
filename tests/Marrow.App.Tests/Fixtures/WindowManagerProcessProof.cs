using System.Diagnostics;
using System.Runtime.ExceptionServices;
using Marrow.App.Port;
using Marrow.Core.Port;

namespace Marrow.App.Tests.Fixtures;

// The recursively selected Fact is the child entrypoint. A DLL load or test discovery never
// runs the scenario, and the child cannot recursively launch another runner.
internal static class WindowManagerProcessProof
{
    internal static void Execute(string factName)
    {
        if (Environment.GetEnvironmentVariable(WindowManagerProcessProtocol.ChildVariable) is { Length: > 0 } token)
        {
            WindowManagerProcessChild.Run(token);
            throw new InvalidOperationException("The authorized child returned instead of exiting its own process.");
        }
        RunParent(factName);
    }

    private static void RunParent(string factName)
    {
        WindowManagerProcessProtocol.RequirePlatform();
        var token = Guid.NewGuid().ToString("N");
        var run = WindowManagerProcessProtocol.CreateRun(token);
        var assembly = Path.GetFullPath(typeof(WindowManagerProcessProof).Assembly.Location);
        var executable = WindowManagerProcessProtocol.ResolveDotnetHost();
        var request = new WindowManagerProcessRequest(token, Environment.ProcessId, assembly,
            typeof(AppShell).Module.ModuleVersionId, typeof(WindowManagerProcessProof).Module.ModuleVersionId);
        WindowManagerProcessProtocol.Write(run, "request.json", request);

        var arguments = new[]
        {
            "vstest", assembly,
            "/Platform:x64",
            "/TestCaseFilter:FullyQualifiedName=" + factName,
            "/ResultsDirectory:" + Path.Combine(run, "results"),
            "/logger:trx;LogFileName=child-runner.trx",
            "/logger:console;verbosity=normal",
        };
        WindowManagerProcessProtocol.Write(run, "invocation.json", new { Executable = executable, Arguments = arguments });
        var stdout = new ProcessPipe();
        var stderr = new ProcessPipe();
        var launcher = new ProcessLauncher
        {
            ExecutableURL = executable,
            Arguments = arguments,
            CurrentDirectoryURL = run,
            Environment = WindowManagerProcessProtocol.ChildEnvironment(run, token),
            StandardOutput = stdout,
            StandardError = stderr,
            UsesJobObject = true,
        };

        WindowManagerProcessOwnedJob? owned = null;
        Process? child = null;
        Task<byte[]>? output = null;
        Task<byte[]>? errors = null;
        WindowManagerProcessReady? ready = null;
        int? childExit = null;
        int? runnerExit = null;
        var verified = false;
        var cleanupComplete = false;
        var ownershipRetained = false;
        Exception? failure = null;
        try
        {
            launcher.Run(); // existing CreateProcessW implementation: hidden console and job from first instruction
            output = Task.Run(stdout.ReadDataToEndOfFile);
            errors = Task.Run(stderr.ReadDataToEndOfFile);
            owned = new WindowManagerProcessOwnedJob(launcher);
            ownershipRetained = true;
            WindowManagerProcessProtocol.Until(() =>
            {
                if (File.Exists(Path.Combine(run, "ready.json"))) return true;
                if (!launcher.IsRunning)
                    throw new InvalidOperationException("Runner exited before the selected child Fact announced its identity.");
                return false;
            }, WindowManagerProcessProtocol.StartupBound, "The selected child Fact did not become ready.");

            ready = WindowManagerProcessProtocol.Read<WindowManagerProcessReady>(run, "ready.json");
            Assert.Equal(token, ready.Token);
            Assert.Equal(request.ParentProcessId, ready.ParentProcessId);
            Assert.NotEqual(Environment.ProcessId, ready.ChildProcessId);
            Assert.NotEqual(launcher.ProcessIdentifier, ready.ChildProcessId);
            Assert.Equal(request.AppModuleVersion, ready.AppModuleVersion);
            Assert.Equal(request.TestModuleVersion, ready.TestModuleVersion);
            child = Process.GetProcessById(ready.ChildProcessId);
            _ = child.SafeHandle; // pin the OS identity while the child is waiting for permit.json
            Assert.False(child.HasExited);
            owned.RequireMember(child);
            WindowManagerProcessProtocol.Write(run, "permit.json", new WindowManagerProcessPermit(token, ready.ChildProcessId));

            if (!child.WaitForExit((int)WindowManagerProcessProtocol.ChildBound.TotalMilliseconds + 5000))
                throw new TimeoutException("The main-window close did not end the owned child process.");
            childExit = child.ExitCode; // independent kernel observation, not a log string or runner result
            Assert.Equal(WindowManagerProcessProtocol.SuccessExitCode, childExit.Value);
            var proof = WindowManagerProcessProtocol.Read<WindowManagerProcessExit>(run, "exit-path.json");
            Assert.Equal(ready, proof.Identity);
            Assert.Equal(WindowManagerProcessProtocol.SuccessExitCode, proof.ExitCode);
            Assert.True(proof.MainVisibleAtExit);
            Assert.True(proof.MainCloseReturned);
            Assert.False(proof.MainClosedEventRaised);
            Assert.Equal(1, proof.MainBuildCount);
            Assert.Equal(1, proof.SettingsBuildCount);
            Assert.Equal(0, proof.SettingsClosedCount);
            Assert.Equal(WindowManagerProcessChild.ExpectedEvents, proof.Events);
            Assert.False(File.Exists(Path.Combine(run, "failure.json")));
            Assert.True(File.Exists(Path.Combine(run, "child.log")));
            var log = File.ReadAllLines(Path.Combine(run, "child.log"));
            Assert.Equal(WindowManagerProcessChild.ExpectedEvents, log);
            verified = true;
        }
        catch (Exception error) { failure = error; }
        finally
        {
            try
            {
                // Let VSTest observe the intentional testhost exit; a nonzero runner status alone
                // is expected here and is NEVER accepted as proof of a mutation kill or a passed test.
                if (launcher.ProcessIdentifier != 0)
                {
                    if (launcher.IsRunning && !WaitRunner(launcher, WindowManagerProcessProtocol.CleanupBound))
                        launcher.KillTree();
                    owned?.Dispose(); // retained job still reaches descendants after dotnet.exe exits
                    owned = null;
                    if (!WaitRunner(launcher, WindowManagerProcessProtocol.CleanupBound))
                        throw new TimeoutException("Owned VSTest runner did not exit during cleanup.");
                    runnerExit = launcher.TerminationStatus;
                    WriteOutput(run, "runner.stdout.log", output);
                    WriteOutput(run, "runner.stderr.log", errors);
                    if (!ownershipRetained)
                        throw new InvalidOperationException("Job ownership was not retained; descendant cleanup cannot be certified.");
                }
                cleanupComplete = true;
            }
            catch (Exception cleanupError)
            {
                failure = failure is null ? cleanupError : new AggregateException(failure, cleanupError);
            }
            finally
            {
                try { owned?.Dispose(); }
                catch (Exception cleanupError)
                {
                    failure = failure is null ? cleanupError : new AggregateException(failure, cleanupError);
                    cleanupComplete = false;
                }
                child?.Dispose();
                WindowManagerProcessProtocol.Write(run, "parent-result.json", new
                {
                    Token = token,
                    ParentProcessId = Environment.ProcessId,
                    RunnerProcessId = launcher.ProcessIdentifier,
                    Child = ready,
                    KernelChildExitCode = childExit,
                    RunnerExitCode = runnerExit,
                    ExitProofVerified = verified && failure is null,
                    CleanupComplete = cleanupComplete,
                    Error = failure?.ToString(),
                });
            }
        }
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
        Assert.True(verified && cleanupComplete, "The child exit proof or its cleanup was incomplete: " + run);
    }

    private static bool WaitRunner(ProcessLauncher launcher, TimeSpan bound)
    {
        try
        {
            WindowManagerProcessProtocol.Until(() => !launcher.IsRunning, bound, "runner still running");
            return true;
        }
        catch (TimeoutException) { return false; }
    }

    private static void WriteOutput(string run, string name, Task<byte[]>? read)
    {
        if (read is null) return;
        if (!read.Wait(WindowManagerProcessProtocol.CleanupBound))
            throw new TimeoutException("Owned runner output pipe did not reach EOF.");
        var path = WindowManagerProcessProtocol.UnderRun(run, Path.Combine(run, name));
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        stream.Write(read.GetAwaiter().GetResult());
        stream.Flush(flushToDisk: true);
    }
}
