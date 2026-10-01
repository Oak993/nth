using System.Globalization;
using System.Reflection;
using System.Text;
using System.Threading;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Providers;

namespace Marrow.Services.Tests.Fixtures;

// The compiled stand-in for an ACP agent (`opencode acp`), for the ProviderRegistry tests of the OpenCode catalog
// probe. Helper types live here, never inside a test class (R-TEST-3). The stand-in is
// tests/fixtures/acp-cli/AcpStandIn.cs (R-TEST-8), an offline program compiled with the system's .NET Framework csc.exe;
// nothing reaches a network or a real AI CLI (R-TEST-10). It reads control files next to its own executable.
public static class AcpStandIns
{
    private static readonly Lazy<string> compiled = new(Compile, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>A fresh copy of the compiled stand-in in its own folder, with its control files.</summary>
    public static AcpScript CopyFor(string label)
    {
        var root = Environment.GetEnvironmentVariable("MARROW_PORT_DATA_DIR");
        if (string.IsNullOrEmpty(root))
            throw new InvalidOperationException("Set MARROW_PORT_DATA_DIR to a throwaway folder before running the tests (R-TEST-9).");
        var folder = Path.Combine(root, "acp-stand-in", label + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "acp-agent.exe");
        File.Copy(compiled.Value, path, overwrite: true);
        return new AcpScript(path, folder);
    }

    /// <summary>
    /// Forgets the process-wide launch spacing of ACPSession (a static gate that waits on the flow's clock, which a
    /// deterministic clock never advances on its own): every launch in a test then starts at once. Only for test classes
    /// that run alone (no other class launches an ACP agent at the same time).
    /// </summary>
    public static void ResetLaunchGate()
    {
        var gate = typeof(ACPSession).GetField("LaunchGate", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        gate.GetType().GetField("nextSlot", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(gate, SwiftDate.DistantPast);
    }

    /// <summary>The session/new result of an agent offering the given (model id, name) rows, the first one current.</summary>
    public static string Models(params (string Id, string Name)[] rows)
    {
        var entries = string.Join(",", rows.Select(row => "{\"modelId\":\"" + row.Id + "\",\"name\":\"" + row.Name + "\"}"));
        return "{\"sessionId\":\"s1\",\"models\":{\"currentModelId\":\"" + rows[0].Id + "\",\"availableModels\":[" + entries + "]}}";
    }

    private static string Compile()
    {
        var root = Environment.GetEnvironmentVariable("MARROW_PORT_DATA_DIR");
        if (string.IsNullOrEmpty(root))
            throw new InvalidOperationException("Set MARROW_PORT_DATA_DIR to a throwaway folder before running the tests (R-TEST-9).");
        var folder = Path.Combine(root, "acp-stand-in-build", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var source = FindSource();
        var compiler = Environment.GetFolderPath(Environment.SpecialFolder.Windows) +
            "\\Microsoft.NET\\Framework64\\v4.0.30319\\csc.exe";
        var output = Path.Combine(folder, "acp-stand-in.exe");
        var run = Shell.Run(compiler, ["-nologo", "-target:exe", "-out:" + output, source], @in: folder);
        if (!run.Wait(TimeSpan.FromSeconds(120))) throw new TimeoutException("csc.exe did not finish.");
        if (!run.Result.Succeeded) throw new InvalidOperationException("The ACP stand-in did not compile: " + run.Result.FailureMessage);
        return output;
    }

    private static string FindSource()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = directory.FullName.TrimEnd('\\') + "\\tests\\fixtures\\acp-cli\\AcpStandIn.cs";
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }
        throw new FileNotFoundException("tests\\fixtures\\acp-cli\\AcpStandIn.cs was not found above the test output folder.");
    }
}

/// <summary>One instance of the ACP stand-in: its executable path and the folder its control files live in.</summary>
public sealed class AcpScript(string executablePath, string folder)
{
    public string ExecutablePath { get; } = executablePath;
    public string Folder { get; } = folder;

    /// <summary>The session/new result for every launch without a file of its own (or for launch <paramref name="launch"/> only).</summary>
    public AcpScript SetModels(string resultJson, int? launch = null)
    {
        File.WriteAllText(Path.Combine(Folder, launch is { } n ? "models-" + n.ToString(CultureInfo.InvariantCulture) + ".json" : "models.json"), resultJson, new UTF8Encoding(false));
        return this;
    }

    /// <summary>Makes session/new fail with this message, for every launch (or for launch <paramref name="launch"/> only).</summary>
    public AcpScript SetError(string message, int? launch = null)
    {
        File.WriteAllText(Path.Combine(Folder, launch is { } n ? "error-" + n.ToString(CultureInfo.InvariantCulture) + ".txt" : "error.txt"), message, new UTF8Encoding(false));
        return this;
    }

    /// <summary>Makes the agent wait this long (real time) before answering session/new.</summary>
    public AcpScript SetDelay(int milliseconds)
    {
        File.WriteAllText(Path.Combine(Folder, "delay-ms.txt"), milliseconds.ToString(CultureInfo.InvariantCulture));
        return this;
    }

    /// <summary>How many times the agent has been launched so far.</summary>
    public int Launches
    {
        get
        {
            var count = 0;
            while (File.Exists(Path.Combine(Folder, "call-" + count.ToString(CultureInfo.InvariantCulture) + ".args"))) count++;
            return count;
        }
    }

    /// <summary>The argument vector (NUL-decoded) of launch <paramref name="launch"/>.</summary>
    public IReadOnlyList<string> Arguments(int launch)
    {
        var bytes = File.ReadAllBytes(Path.Combine(Folder, "call-" + launch.ToString(CultureInfo.InvariantCulture) + ".args"));
        var parts = new List<string>();
        var start = 0;
        for (var i = 0; i < bytes.Length; i++)
        {
            if (bytes[i] != 0) continue;
            parts.Add(Encoding.UTF8.GetString(bytes, start, i - start));
            start = i + 1;
        }
        return parts;
    }
}
