using System.Threading;
using Marrow.Core.Support;

namespace Marrow.Services.Tests.Fixtures;

// The compiled stand-in for the Services.Providers.PlanLimits tests' `auth status` refresh calls. Helper types
// live here, never inside a test class (R-TEST-3). The stand-in is
// tests/fixtures/claude-auth-cli/AuthRewriteStandIn.cs (R-TEST-8), an offline program compiled with the
// system's .NET Framework csc.exe, the same pattern as ClaudeStandIns.cs; nothing reaches a network or a real
// AI CLI (R-TEST-10).
public static class AuthRewriteStandIns
{
    private static readonly Lazy<string> compiled = new(Compile, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>A fresh copy of the compiled stand-in, so argv captures never collide across tests.</summary>
    public static string CopyFor(string label)
    {
        var root = Environment.GetEnvironmentVariable("MARROW_PORT_DATA_DIR");
        if (string.IsNullOrEmpty(root))
            throw new InvalidOperationException("Set MARROW_PORT_DATA_DIR to a throwaway folder before running the tests (R-TEST-9).");
        var folder = Path.Combine(root, "auth-rewrite-stand-in", label + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "claude.exe");
        File.Copy(compiled.Value, path, overwrite: true);
        return path;
    }

    private static string Compile()
    {
        var root = Environment.GetEnvironmentVariable("MARROW_PORT_DATA_DIR");
        if (string.IsNullOrEmpty(root))
            throw new InvalidOperationException("Set MARROW_PORT_DATA_DIR to a throwaway folder before running the tests (R-TEST-9).");
        var folder = Path.Combine(root, "auth-rewrite-stand-in-build", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var source = FindSource();
        var compiler = Environment.GetFolderPath(Environment.SpecialFolder.Windows) +
            "\\Microsoft.NET\\Framework64\\v4.0.30319\\csc.exe";
        var output = Path.Combine(folder, "auth-rewrite-stand-in.exe");
        var run = Shell.Run(compiler, ["-nologo", "-target:exe", "-out:" + output, source], @in: folder);
        if (!run.Wait(TimeSpan.FromSeconds(120))) throw new TimeoutException("csc.exe did not finish.");
        if (!run.Result.Succeeded) throw new InvalidOperationException("The auth-rewrite stand-in did not compile: " + run.Result.FailureMessage);
        return output;
    }

    private static string FindSource()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = directory.FullName.TrimEnd('\\') + "\\tests\\fixtures\\claude-auth-cli\\AuthRewriteStandIn.cs";
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }
        throw new FileNotFoundException("tests\\fixtures\\claude-auth-cli\\AuthRewriteStandIn.cs was not found above the test output folder.");
    }
}
