using System.Diagnostics;
using System.Security.Cryptography;
using Marrow.App.Window;
using Marrow.Core.Port;
using Marrow.Services.Port;
using P = Marrow.App.Tests.Fixtures.WindowManagerProcessProtocol;

namespace Marrow.App.Tests.Fixtures;

// PORT-ONLY test protocol. Existing ProcessProtocol APIs are reused unchanged. Child mode is
// generated internally per phase; normal suite callers set no restart or process-proof switches.
internal static class WindowManagerRestartProtocol
{
    internal const string PhaseVariable = "MARROW_WINDOW_RESTART_PHASE";
    private const string RunPrefix = "marrow-window-process-";
    private static readonly string[] WritableVariables =
    [
        "MARROW_PORT_DATA_DIR", "MARROW_DEV_DATA_ROOT", "TEMP", "TMP", "LOCALAPPDATA", "APPDATA",
        "USERPROFILE", "DOTNET_CLI_HOME", "NUGET_PACKAGES",
    ];

    internal static WindowManagerRestartRoot CreateRoot(string factName)
    {
        P.RequirePlatform();
        var token = Guid.NewGuid().ToString("N");
        using var parent = Process.GetCurrentProcess();
        var root = new WindowManagerRestartRoot(token, P.CreateRun(token), Environment.ProcessId,
            parent.StartTime.ToUniversalTime().Ticks, factName,
            Path.GetFullPath(typeof(WindowManagerRestartProtocol).Assembly.Location),
            typeof(WindowManager).Module.ModuleVersionId, typeof(WindowManagerRestartProtocol).Module.ModuleVersionId,
            typeof(SettingsStore).Module.ModuleVersionId, typeof(FileOps).Module.ModuleVersionId);
        Directory.CreateDirectory(P.UnderRun(root.Directory, Path.GetDirectoryName(StorePath(root))!));
        P.Write(root.Directory, "restart-root.json", root);
        return root;
    }

    internal static string StorePath(WindowManagerRestartRoot root) =>
        P.UnderRun(root.Directory, SettingsStore.ProductionPath(Path.Combine(root.Directory, "persistent", "local")));

    internal static string PhaseDirectory(WindowManagerRestartRequest request) =>
        P.UnderRun(request.Root.Directory, Path.Combine(request.Root.Directory,
            "phase-" + P.Invariant(request.Phase), RunPrefix + request.Token));

    internal static WindowManagerRestartRequest CreatePhase(WindowManagerRestartRoot root, int phase,
        int previousChildProcessId = 0, string? expectedInputHash = null)
    {
        if (phase is not (1 or 2)) throw new InvalidOperationException("Invalid restart phase.");
        var request = new WindowManagerRestartRequest(root, phase, Guid.NewGuid().ToString("N"),
            previousChildProcessId, expectedInputHash);
        var directory = PhaseDirectory(request);
        if (Path.Exists(directory)) throw new InvalidOperationException("A restart phase must own a new directory.");
        Directory.CreateDirectory(directory);
        var environment = ChildEnvironment(request);
        foreach (var variable in WritableVariables)
            Directory.CreateDirectory(P.UnderRun(directory, environment[variable]));
        Directory.CreateDirectory(P.UnderRun(directory, Path.Combine(directory, "results")));
        P.Write(directory, "request.json", BaseRequest(request));
        P.Write(directory, "restart-request.json", request);
        return request;
    }

    internal static IReadOnlyDictionary<string, string> ChildEnvironment(WindowManagerRestartRequest request)
    {
        var values = P.ChildEnvironment(PhaseDirectory(request), request.Token)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        values[PhaseVariable] = P.Invariant(request.Phase);
        return values;
    }

    private static WindowManagerProcessRequest BaseRequest(WindowManagerRestartRequest request) =>
        new(request.Token, request.Root.ParentProcessId, request.Root.TestAssemblyPath,
            request.Root.AppModuleVersion, request.Root.TestModuleVersion);

    internal static WindowManagerRestartReady ValidateChild(string token, string factName, out string directory)
    {
        // Existing checks cover Windows x64, testhost entry identity, directories established before
        // assembly load, a live parent, the loaded app/test MVIDs, and secret stripping in this child.
        var basic = P.ValidateChild(token, out directory);
        var request = P.Read<WindowManagerRestartRequest>(directory, "restart-request.json");
        Assert.Equal(basic, BaseRequest(request));
        Assert.Equal(token, request.Token);
        Assert.True(Guid.TryParseExact(request.Root.Token, "N", out _) && request.Root.Token.Length == 32);
        Assert.NotEqual(request.Root.Token, token);
        Assert.True(request.Phase is 1 or 2);
        Assert.Equal(P.Invariant(request.Phase), Environment.GetEnvironmentVariable(PhaseVariable));
        Assert.Equal(factName, request.Root.FactName);
        Assert.Equal(Path.GetFullPath(request.Root.Directory), request.Root.Directory);
        Assert.Equal(RunPrefix + request.Root.Token, Path.GetFileName(request.Root.Directory));
        Assert.Equal(PhaseDirectory(request), directory);
        Assert.Equal(request.Root, P.Read<WindowManagerRestartRoot>(request.Root.Directory, "restart-root.json"));
        using var parent = Process.GetProcessById(request.Root.ParentProcessId);
        _ = parent.SafeHandle;
        Assert.False(parent.HasExited);
        Assert.Equal(request.Root.ParentStartUtcTicks, parent.StartTime.ToUniversalTime().Ticks);
        Assert.Equal(typeof(SettingsStore).Module.ModuleVersionId, request.Root.ServicesModuleVersion);
        Assert.Equal(typeof(FileOps).Module.ModuleVersionId, request.Root.CoreModuleVersion);
        _ = StorePath(request.Root); // validate the shared test-owned store path before opening it
        if (request.Phase == 1)
        {
            Assert.Equal(0, request.PreviousChildProcessId);
            Assert.Null(request.ExpectedInputHash);
            Assert.False(File.Exists(Path.Combine(request.Root.Directory, "phase-1-complete.json")));
        }
        else
        {
            // The parent writes this only after a normal first-child exit, its exact passing TRX,
            // and an empty owned job. A sibling cannot seed phase two while phase one is still active.
            var previous = P.Read<WindowManagerRestartCompletion>(request.Root.Directory, "phase-1-complete.json");
            Assert.Equal(request.Root, previous.Result.Identity.Request.Root);
            Assert.Equal(1, previous.Result.Identity.Request.Phase);
            Assert.NotEqual(token, previous.Result.Identity.Request.Token);
            Assert.Equal(previous.Result.Identity.Process.ChildProcessId, request.PreviousChildProcessId);
            Assert.True(request.PreviousChildProcessId > 0);
            Assert.NotEqual(Environment.ProcessId, request.PreviousChildProcessId);
            Assert.Equal(0, previous.KernelChildExitCode);
            Assert.Equal(0, previous.KernelRunnerExitCode);
            Assert.True(previous.JobEmptyBeforeCleanup);
            Assert.Equal(previous.Result.OutputHash, request.ExpectedInputHash);
        }
        using var self = Process.GetCurrentProcess();
        return new WindowManagerRestartReady(request,
            new WindowManagerProcessReady(token, basic.ParentProcessId, Environment.ProcessId,
                basic.AppModuleVersion, basic.TestModuleVersion), self.StartTime.ToUniversalTime().Ticks);
    }

    internal static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    internal static void WriteBytes(string directory, string name, byte[] bytes)
    {
        var path = P.UnderRun(directory, Path.Combine(directory, name));
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        stream.Write(bytes);
        stream.Flush(flushToDisk: true);
    }
}

internal sealed record WindowManagerRestartRoot(string Token, string Directory, int ParentProcessId,
    long ParentStartUtcTicks, string FactName, string TestAssemblyPath, Guid AppModuleVersion, Guid TestModuleVersion,
    Guid ServicesModuleVersion, Guid CoreModuleVersion);

internal sealed record WindowManagerRestartRequest(WindowManagerRestartRoot Root, int Phase, string Token,
    int PreviousChildProcessId, string? ExpectedInputHash);

internal sealed record WindowManagerRestartReady(WindowManagerRestartRequest Request,
    WindowManagerProcessReady Process, long ProcessStartUtcTicks);

internal sealed record WindowManagerRestartPermit(WindowManagerRestartReady Identity);

internal sealed record WindowManagerRestartResult(WindowManagerRestartReady Identity, string StorePath,
    WindowManagerRestartFrame Main, WindowManagerRestartFrame Settings, string? InputHash, string OutputHash);

internal sealed record WindowManagerRestartCompletion(WindowManagerRestartResult Result,
    int KernelChildExitCode, int KernelRunnerExitCode, bool JobEmptyBeforeCleanup);
