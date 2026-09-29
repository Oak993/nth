using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Marrow.App.Port;
using Marrow.Testing;

namespace Marrow.App.Tests.Fixtures;

// PORT-ONLY test infrastructure. These files are a private parent/child protocol, never app data.
// All bounds measure test-process liveness, not a source-defined product delay (R-TEST-5).
internal static class WindowManagerProcessProtocol
{
    internal const string ChildVariable = "MARROW_SHELL_PROOF_CHILD";
    internal const string DirectoryVariable = "MARROW_SHELL_PROOF_DIRECTORY";
    internal const int SuccessExitCode = 73;
    internal const int FailureExitCode = 74;
    internal const int WatchdogExitCode = 75;
    internal static readonly TimeSpan StartupBound = TimeSpan.FromSeconds(45);
    internal static readonly TimeSpan ChildBound = TimeSpan.FromSeconds(45);
    internal static readonly TimeSpan CleanupBound = TimeSpan.FromSeconds(10);
    private static readonly object LogGate = new();
    private const string RunPrefix = "marrow-window-process-";
    private static readonly (string Variable, string Directory)[] ChildDirectories =
    [
        ("MARROW_PORT_DATA_DIR", "data"),
        ("MARROW_DEV_DATA_ROOT", "dev"),
        ("TEMP", "temp"),
        ("TMP", "temp"),
        ("LOCALAPPDATA", "local"),
        ("APPDATA", "roaming"),
        ("USERPROFILE", "profile"),
        ("DOTNET_CLI_HOME", "dotnet-home"),
        ("NUGET_PACKAGES", "nuget"),
    ];

    internal static string UnderRun(string run, string path)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(run));
        var full = Path.GetFullPath(path);
        var prefix = root + Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(full, root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The proof path is outside its owned run directory.");
        // Reject redirection inside the generated directory. Its ambient parent may be an ordinary
        // suite data folder or the system temp folder, with no repository-location requirement.
        for (string? current = full; current is not null; current = Path.GetDirectoryName(current))
        {
            if ((File.Exists(current) || Directory.Exists(current)) &&
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("The proof refuses a reparse point inside its owned run directory.");
            if (string.Equals(current, root, StringComparison.OrdinalIgnoreCase)) break;
        }
        return full;
    }

    internal static string CreateRun(string token)
    {
        RequireNonce(token);
        var ambient = TestDataDirectory.Read();
        var root = string.IsNullOrWhiteSpace(ambient) ? Path.GetTempPath() : ambient;
        var run = Path.Combine(Path.GetFullPath(root), RunPrefix + token);
        if (Path.Exists(run)) throw new InvalidOperationException("A process-proof run must be new.");
        Directory.CreateDirectory(run);
        foreach (var name in ChildDirectories.Select(item => item.Directory).Append("results").Distinct())
            Directory.CreateDirectory(UnderRun(run, Path.Combine(run, name)));
        return run;
    }

    internal static void RequirePlatform()
    {
        if (!OperatingSystem.IsWindows() || !Environment.Is64BitProcess)
            throw new InvalidOperationException("This proof requires the existing Windows x64 testhost.");
    }

    internal static string ResolveDotnetHost()
    {
        // Read the already installed host locations; never install a host or execute a candidate
        // to identify it. A runtime-only installation cannot run the existing VSTest command.
        var candidates = new List<string?>
        {
            Environment.GetEnvironmentVariable("DOTNET_HOST_PATH"),
            Environment.ProcessPath,
        };
        for (var directory = new DirectoryInfo(RuntimeEnvironment.GetRuntimeDirectory()); directory is not null; directory = directory.Parent)
            candidates.Add(Path.Combine(directory.FullName, "dotnet.exe"));
        foreach (var name in new[] { "DOTNET_ROOT_X64", "DOTNET_ROOT" })
            if (Environment.GetEnvironmentVariable(name) is { Length: > 0 } directory)
                candidates.Add(Path.Combine(directory, "dotnet.exe"));
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            candidates.Add(Path.Combine(directory.Trim().Trim('"'), "dotnet.exe"));
        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate) || !Path.IsPathFullyQualified(candidate) ||
                !string.Equals(Path.GetFileName(candidate), "dotnet.exe", StringComparison.OrdinalIgnoreCase) ||
                !File.Exists(candidate) || !Directory.Exists(Path.Combine(Path.GetDirectoryName(candidate)!, "sdk"))) continue;
            return Path.GetFullPath(candidate);
        }
        throw new InvalidOperationException("No existing dotnet SDK host was found in the current host, runtime installation or PATH.");
    }

    private static void RequireNonce(string token)
    {
        if (!Guid.TryParseExact(token, "N", out _) || token.Length != 32)
            throw new InvalidOperationException("Invalid child proof nonce.");
    }

    private static bool LooksSensitive(string name) =>
        new[] { "API_KEY", "APIKEY", "TOKEN", "SECRET", "PASSWORD", "CREDENTIAL", "AUTH", "COOKIE" }
            .Any(part => name.Contains(part, StringComparison.OrdinalIgnoreCase));

    internal static IReadOnlyDictionary<string, string> ChildEnvironment(string run, string token)
    {
        // Build the entire child environment here, so callers need no secret-removal flags or
        // process-wide environment edits. The child test module still installs ED-211.
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in new[] { "SystemRoot", "WINDIR", "ComSpec", "PATH", "PATHEXT", "SystemDrive",
                     "NUMBER_OF_PROCESSORS", "PROCESSOR_ARCHITECTURE", "DOTNET_ROOT", "DOTNET_ROOT_X64",
                     "DOTNET_ROOT(x86)" })
        {
            if (Environment.GetEnvironmentVariable(name) is { Length: > 0 } value) values[name] = value;
        }
        values[ChildVariable] = token;
        values[DirectoryVariable] = run;
        foreach (var (variable, directory) in ChildDirectories)
            values[variable] = UnderRun(run, Path.Combine(run, directory));
        // SDK first-use must not create certificates or alter the user's tools PATH.
        values["DOTNET_GENERATE_ASPNET_CERTIFICATE"] = "false";
        values["DOTNET_ADD_GLOBAL_TOOLS_TO_PATH"] = "false";
        values["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        values["DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE"] = "true";
        values["DOTNET_NOLOGO"] = "1";
        values["MSBUILDDISABLENODEREUSE"] = "1";
        return values;
    }

    internal static WindowManagerProcessRequest ValidateChild(string token, out string run)
    {
        RequirePlatform();
        RequireNonce(token);
        run = Path.GetFullPath(Environment.GetEnvironmentVariable(DirectoryVariable)
            ?? throw new InvalidOperationException("Missing child proof directory."));
        run = UnderRun(run, run);
        if (!string.Equals(Path.GetFileName(run), RunPrefix + token, StringComparison.Ordinal))
            throw new InvalidOperationException("Child proof directory does not match its nonce.");
        if (!string.Equals(TestDataDirectory.Read(), Path.Combine(run, "data"), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Child data was not isolated before loading the test assembly.");
        foreach (var (variable, directory) in ChildDirectories)
        {
            var expected = UnderRun(run, Path.Combine(run, directory));
            if (!string.Equals(Environment.GetEnvironmentVariable(variable), expected, StringComparison.OrdinalIgnoreCase) ||
                !Directory.Exists(expected))
                throw new InvalidOperationException("Child writable directories differ from the generated run.");
        }
        // The parent already excluded credentials from the launch block. Strip any sensitive
        // additions made by the test runner inside this disposable child as well, without logging values.
        foreach (var name in Environment.GetEnvironmentVariables().Keys.Cast<string>().ToArray())
            if (LooksSensitive(name)) Environment.SetEnvironmentVariable(name, null);
        if (!string.Equals(Assembly.GetEntryAssembly()?.GetName().Name, "testhost", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Only the selected VSTest testhost Fact may enter child mode.");
        var request = Read<WindowManagerProcessRequest>(run, "request.json");
        if (request.Token != token || request.ParentProcessId <= 0 || request.ParentProcessId == Environment.ProcessId ||
            request.AppModuleVersion != typeof(AppShell).Module.ModuleVersionId ||
            request.TestModuleVersion != typeof(WindowManagerProcessProtocol).Module.ModuleVersionId ||
            !string.Equals(Path.GetFullPath(request.TestAssemblyPath),
                Path.GetFullPath(typeof(WindowManagerProcessProtocol).Assembly.Location), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Child request identity or loaded module identity differs from its parent.");
        using var parent = Process.GetProcessById(request.ParentProcessId);
        if (parent.HasExited) throw new InvalidOperationException("The proof parent already exited.");
        return request;
    }

    internal static void Write<T>(string run, string name, T value)
    {
        var path = UnderRun(run, Path.Combine(run, name));
        var pending = UnderRun(run, path + ".writing");
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value);
        using (var stream = new FileStream(pending, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
        }
        File.Move(pending, path); // no overwrite, so a stale success/second child cannot replace evidence
    }

    internal static T Read<T>(string run, string name) =>
        JsonSerializer.Deserialize<T>(File.ReadAllBytes(UnderRun(run, Path.Combine(run, name))))
        ?? throw new InvalidOperationException("Empty process-proof document: " + name);

    internal static void Log(string run, string entry)
    {
        var bytes = Encoding.UTF8.GetBytes(entry + "\n");
        lock (LogGate)
        {
            using var stream = new FileStream(UnderRun(run, Path.Combine(run, "child.log")),
                FileMode.Append, FileAccess.Write, FileShare.Read);
            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
        }
    }

    internal static void Until(Func<bool> complete, TimeSpan bound, string failure)
    {
        var elapsed = Stopwatch.StartNew();
        using var pause = new ManualResetEventSlim(false);
        while (!complete())
        {
            if (elapsed.Elapsed >= bound) throw new TimeoutException(failure);
            pause.Wait(TimeSpan.FromMilliseconds(20));
        }
    }

    internal static string Invariant(int value) => value.ToString(CultureInfo.InvariantCulture);
}

internal sealed record WindowManagerProcessRequest(string Token, int ParentProcessId,
    string TestAssemblyPath, Guid AppModuleVersion, Guid TestModuleVersion);

internal sealed record WindowManagerProcessReady(string Token, int ParentProcessId, int ChildProcessId,
    Guid AppModuleVersion, Guid TestModuleVersion);

internal sealed record WindowManagerProcessPermit(string Token, int ChildProcessId);

internal sealed record WindowManagerProcessExit(WindowManagerProcessReady Identity, int ExitCode,
    bool MainVisibleAtExit, bool MainCloseReturned, bool MainClosedEventRaised,
    int MainBuildCount, int SettingsBuildCount, int SettingsClosedCount, string[] Events);
