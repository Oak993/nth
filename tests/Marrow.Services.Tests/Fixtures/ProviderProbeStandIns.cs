using System.Text;
using System.Threading;
using Marrow.Core.Support;

namespace Marrow.Services.Tests.Fixtures;

// The compiled stand-in for the Services.Providers.ProviderRegistry part 2 tests' version and auth probes.
// Helper types live here, never inside a test class (R-TEST-3). The stand-in is
// tests/fixtures/provider-probe-cli/ProviderProbeStandIn.cs (R-TEST-8), an offline program compiled with the
// system's .NET Framework csc.exe, the same pattern as ClaudeStandIns.cs; nothing reaches a network or a real
// AI CLI (R-TEST-10). It reads control files next to its own executable, so it needs nothing from the
// production Environment(for:) computation to reach it.
public static class ProviderProbeStandIns
{
    private static readonly Lazy<string> compiled = new(Compile, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>A fresh copy of the compiled stand-in in its own folder.</summary>
    public static ProviderProbeScript CopyFor(string label)
    {
        var root = Environment.GetEnvironmentVariable("MARROW_PORT_DATA_DIR");
        if (string.IsNullOrEmpty(root))
            throw new InvalidOperationException("Set MARROW_PORT_DATA_DIR to a throwaway folder before running the tests (R-TEST-9).");
        var folder = Path.Combine(root, "provider-probe-stand-in", label + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "provider-probe.exe");
        File.Copy(compiled.Value, path, overwrite: true);
        return new ProviderProbeScript(path, folder);
    }

    private static string Compile()
    {
        var root = Environment.GetEnvironmentVariable("MARROW_PORT_DATA_DIR");
        if (string.IsNullOrEmpty(root))
            throw new InvalidOperationException("Set MARROW_PORT_DATA_DIR to a throwaway folder before running the tests (R-TEST-9).");
        var folder = Path.Combine(root, "provider-probe-stand-in-build", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var source = FindSource();
        var compiler = Environment.GetFolderPath(Environment.SpecialFolder.Windows) +
            "\\Microsoft.NET\\Framework64\\v4.0.30319\\csc.exe";
        var output = Path.Combine(folder, "provider-probe-stand-in.exe");
        var run = Shell.Run(compiler, ["-nologo", "-target:exe", "-out:" + output, source], @in: folder);
        if (!run.Wait(TimeSpan.FromSeconds(120))) throw new TimeoutException("csc.exe did not finish.");
        if (!run.Result.Succeeded) throw new InvalidOperationException("The provider-probe stand-in did not compile: " + run.Result.FailureMessage);
        return output;
    }

    private static string FindSource()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = directory.FullName.TrimEnd('\\') + "\\tests\\fixtures\\provider-probe-cli\\ProviderProbeStandIn.cs";
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }
        throw new FileNotFoundException("tests\\fixtures\\provider-probe-cli\\ProviderProbeStandIn.cs was not found above the test output folder.");
    }
}

/// <summary>One instance of the stand-in: its executable path and the folder its control files live in.</summary>
public sealed class ProviderProbeScript(string executablePath, string folder)
{
    public string ExecutablePath { get; } = executablePath;
    public string Folder { get; } = folder;

    public ProviderProbeScript SetStdout(string text)
    {
        File.WriteAllBytes(Path.Combine(Folder, "stdout.txt"), new UTF8Encoding(false).GetBytes(text));
        return this;
    }

    public ProviderProbeScript SetStderr(string text)
    {
        File.WriteAllBytes(Path.Combine(Folder, "stderr.txt"), new UTF8Encoding(false).GetBytes(text));
        return this;
    }

    public ProviderProbeScript SetExitCode(int code)
    {
        File.WriteAllText(Path.Combine(Folder, "exit.txt"), code.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return this;
    }

    /// <summary>Makes the run sleep sleepMs before doing anything else, and mark markerPath present while it does.</summary>
    public ProviderProbeScript SetConcurrencyProbe(string markerPath, int sleepMs)
    {
        File.WriteAllText(Path.Combine(Folder, "marker.txt"), markerPath);
        File.WriteAllText(Path.Combine(Folder, "sleep-ms.txt"), sleepMs.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return this;
    }

    /// <summary>Makes every call sleep sleepMs before doing anything else, with no concurrency marker.</summary>
    public ProviderProbeScript SetSleep(int sleepMs)
    {
        File.WriteAllText(Path.Combine(Folder, "sleep-ms.txt"), sleepMs.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return this;
    }

    /// <summary>The argv (NUL-decoded) of every call so far, in call order (0-based, ClaudeStandIn.cs's shape).</summary>
    public IReadOnlyList<IReadOnlyList<string>> RecordedCalls()
    {
        var calls = new List<IReadOnlyList<string>>();
        var call = 0;
        while (true)
        {
            var path = Path.Combine(Folder, "call-" + call + ".args");
            if (!File.Exists(path)) break;
            var bytes = File.ReadAllBytes(path);
            var parts = new List<string>();
            var start = 0;
            for (var i = 0; i < bytes.Length; i++)
            {
                if (bytes[i] != 0) continue;
                parts.Add(Encoding.UTF8.GetString(bytes, start, i - start));
                start = i + 1;
            }
            calls.Add(parts);
            call++;
        }
        return calls;
    }
}
