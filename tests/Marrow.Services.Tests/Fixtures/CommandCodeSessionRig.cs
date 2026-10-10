using System.Reflection;
using System.Text;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Providers;
using Marrow.Testing;

namespace Marrow.Services.Tests.Fixtures;

// PORT-ONLY test rig (R-TEST-3, R-TEST-9, R-TEST-10): one CommandCodeSession whose executable is the compiled Command Code
// stand-in (tests/fixtures/command-code-cli/CommandCodeStandIn.cs: offline, never a real `cmd` CLI), with the data root
// redirected to a throwaway folder through DevDataRoot's MARROW_DEV_DATA_ROOT (ED-212, Debug builds only), so the approval
// mod, the approvals directories and the MCP bridge Send installs are written under MARROW_PORT_DATA_DIR and never under the
// real %LOCALAPPDATA%\Marrow Code. The redirect is a process-wide environment variable, so a test class using this rig must
// join the "Dev data root env" collection (DisableParallelization), which serializes it against every other user of that variable.

/// <summary>A Command Code session over the stand-in, with a bound main loop, scratch storage and a redirected data root.</summary>
public sealed class CommandCodeSessionRig : IDisposable
{
    private readonly string? previousRoot;
    private readonly StorageScratchFolder storage;
    private bool disposed;

    public CommandCodeSessionRig(
        string label,
        bool launchable = true,
        string? home = null,
        string? resumeID = null,
        string? model = null,
        string? effort = null,
        RuntimeMode runtimeMode = RuntimeMode.Supervised,
        InteractionMode interactionMode = InteractionMode.Build)
    {
        var dataDirectory = TestDataDirectory.Read();
        if (string.IsNullOrEmpty(dataDirectory))
            throw new InvalidOperationException("Set MARROW_PORT_DATA_DIR to a throwaway folder before running the tests (R-TEST-9).");
        DataRoot = Path.Combine(dataDirectory, "CommandCodeSessionRig", label + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(DataRoot);
        previousRoot = Environment.GetEnvironmentVariable(DevDataRoot.VariableName);
        Environment.SetEnvironmentVariable(DevDataRoot.VariableName, DataRoot);
        storage = new StorageScratchFolder();
        Runtime = new TestRuntime();
        Script = new CommandCodeScript(label);
        WorkingDirectory = Path.Combine(DataRoot, "work");
        Directory.CreateDirectory(WorkingDirectory);
        var entries = new Dictionary<string, string> { [Script.EnvironmentEntry.Key] = Script.EnvironmentEntry.Value };
        if (home is not null) entries["HOME"] = home;
        Configuration = new SessionConfiguration
        {
            Provider = ProviderKind.Commandcode,
            Executable = launchable ? Script.Executable : NativeUrl(Path.Combine(DataRoot, "does-not-exist", "cmd.exe")),
            WorkingDirectory = NativeUrl(WorkingDirectory),
            Environment = new ValueMap<string, string>(entries),
            ResumeID = resumeID,
            Model = model,
            Effort = effort,
            RuntimeMode = runtimeMode,
            InteractionMode = interactionMode,
        };
        Session = new CommandCodeSession(Configuration) { OnEvent = Events.Add };
    }

    public TestRuntime Runtime { get; }

    public CommandCodeScript Script { get; }

    public SessionConfiguration Configuration { get; }

    public CommandCodeSession Session { get; }

    public List<ProviderEvent> Events { get; } = [];

    /// <summary>The folder MARROW_DEV_DATA_ROOT names while the rig lives: the stand-in for %LOCALAPPDATA%.</summary>
    public string DataRoot { get; }

    public string WorkingDirectory { get; }

    public string SupportDirectory => Path.Combine(DataRoot, "nth", "CommandCode");

    public string ApprovalsRoot => Path.Combine(SupportDirectory, "approvals");

    public string ModPath => Path.Combine(SupportDirectory, "marrow-approvals.ts");

    /// <summary>A SwiftURL whose Path is the Windows path unchanged (a "file:///C:/x" URL gives "/C:/x", which CreateProcessW rejects).</summary>
    public static SwiftURL NativeUrl(string windowsPath) => SwiftURL.Create("marrow-native-path:" + windowsPath.Replace('\\', '/'))!;

    public static TurnInput Turn(
        string text = "hello",
        string? model = null,
        string? effort = null,
        RuntimeMode runtimeMode = RuntimeMode.Supervised,
        InteractionMode interactionMode = InteractionMode.Build,
        bool isFinalReport = false) => new()
    {
        Text = text,
        Images = ValueList<Attachment>.Empty,
        Model = model,
        Effort = effort,
        RuntimeMode = runtimeMode,
        InteractionMode = interactionMode,
        IsFinalReport = isFinalReport,
    };

    /// <summary>Pumps the manual main loop (where the session's read task and file watcher run) until the condition holds; fails the test after the bound.</summary>
    public void WaitFor(Func<bool> condition, string what, int seconds = 30)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (true)
        {
            Runtime.RunUntilIdle();
            if (condition()) return;
            if (DateTime.UtcNow > deadline) throw new Xunit.Sdk.XunitException("Timed out waiting for " + what + ".");
            Thread.Sleep(10);
        }
    }

    /// <summary>Pumps until at least <paramref name="count"/> turns have completed.</summary>
    public void WaitForTurns(int count) =>
        WaitFor(() => Events.OfType<ProviderEvent.TurnCompleted>().Count() >= count, count + " completed turn(s)");

    /// <summary>The raw bytes the stand-in's launch <paramref name="call"/> read from stdin once the client closed it (needs capture-stdin.txt).</summary>
    public byte[] CapturedStdin(int call, int seconds = 15)
    {
        var path = Path.Combine(Script.ControlDirectory, "stdin-" + call + ".bin");
        WaitFor(() => File.Exists(path), "the stand-in to see its stdin close", seconds);
        return File.ReadAllBytes(path);
    }

    public void EnableStdinCapture() => File.WriteAllText(Path.Combine(Script.ControlDirectory, "capture-stdin.txt"), "1", new UTF8Encoding(false));

    /// <summary>The stand-in keeps each launch running after its output until <see cref="Release"/> (or Stop).</summary>
    public void Hold() => File.WriteAllText(Path.Combine(Script.ControlDirectory, "hold.txt"), "1", new UTF8Encoding(false));

    public void Release() => File.WriteAllText(Path.Combine(Script.ControlDirectory, "release.txt"), "1", new UTF8Encoding(false));

    /// <summary>The working directory launch <paramref name="call"/> (0-based) ran in.</summary>
    public string LaunchDirectory(int call, int seconds = 15)
    {
        var path = Path.Combine(Script.ControlDirectory, "cwd-" + call + ".txt");
        WaitFor(() => File.Exists(path), "the stand-in to record its working directory", seconds);
        return File.ReadAllText(path);
    }

    /// <summary>The argument vector of launch <paramref name="call"/>, once it has been recorded.</summary>
    public IReadOnlyList<string> LaunchArguments(int call, int seconds = 15)
    {
        WaitFor(() => Script.Calls().Count > call, "launch " + call + " to record its arguments", seconds);
        return Script.Calls()[call].Arguments;
    }

    public object? Field(string name) =>
        typeof(CommandCodeSession).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Session);

    public void SetField(string name, object? value) =>
        typeof(CommandCodeSession).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(Session, value);

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        try
        {
            Release();
            Session.Stop();
            Runtime.RunUntilIdle();
        }
        catch
        {
            // best effort: the rig is going away
        }
        Runtime.Dispose();
        storage.Dispose();
        Environment.SetEnvironmentVariable(DevDataRoot.VariableName, previousRoot);
    }
}
