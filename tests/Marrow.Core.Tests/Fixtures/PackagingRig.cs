using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Marrow.Core.Tests.Fixtures;

// nth installer PLAN.md 10 ("Metadata and build"), unit U7. Runs the packaging library (scripts/lib/NthPackaging.ps1) in a fresh
// PowerShell 7 over files in a temp folder, so the tests prove the same code the packaging script runs. Nothing here starts a build,
// vpk, an installer or the real packaging script, and nothing touches the real Desktop, Start menu, HKCU or %LOCALAPPDATA%\nth.
internal static class PackagingRig
{
    public static string Repository { get; } = FindRepository();

    public static string Scripts => Path.Combine(Repository, "scripts");

    private static string FindRepository()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Marrow.sln"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Marrow.sln was not found above the test output folder.");
    }

    public static DirectoryInfo TempFolder(string name) => Directory.CreateTempSubdirectory("nth-pkg-" + name + "-");

    // Runs a PowerShell script text (written to a .ps1 in its own temp folder) after dot-sourcing the library; returns what it printed.
    public static (int ExitCode, string Output) Run(string body, bool library = true)
    {
        var folder = TempFolder("run");
        try
        {
            var script = Path.Combine(folder.FullName, "case.ps1");
            var text = new StringBuilder("$ErrorActionPreference = 'Stop'\n[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)\n");
            if (library) text.Append(". '").Append(Path.Combine(Scripts, "lib", "NthPackaging.ps1").Replace("'", "''", StringComparison.Ordinal)).Append("'\n");
            text.Append(body);
            File.WriteAllText(script, text.ToString(), new UTF8Encoding(true));
            return Pwsh("-File", script, []);
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    // Runs a script of the repository's scripts folder with arguments.
    public static (int ExitCode, string Output) RunScript(string scriptName, params string[] arguments) =>
        Pwsh("-File", Path.Combine(Scripts, scriptName), arguments);

    private static (int ExitCode, string Output) Pwsh(string fileFlag, string file, string[] arguments)
    {
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("MARROW_TEST_PWSH") ?? "pwsh")
        {
            WorkingDirectory = Path.GetTempPath(), RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
        };
        // the owner's signing settings and a build agent's redistributable folder never reach a test
        foreach (var name in start.Environment.Keys.Where(k => k.StartsWith("NTH_SIGN_", StringComparison.OrdinalIgnoreCase) || k.Equals("NTH_VC_REDIST_ROOT", StringComparison.OrdinalIgnoreCase)).ToArray())
            start.Environment.Remove(name);
        foreach (var argument in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", fileFlag, file }.Concat(arguments)) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(120_000))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("The packaging script test did not finish in 2 minutes.");
        }
        return (process.ExitCode, output.GetAwaiter().GetResult() + error.GetAwaiter().GetResult());
    }

    // Runs the body, which must print one JSON document last (ConvertTo-Json), and returns it.
    public static JsonElement RunJson(string body)
    {
        var (exit, output) = Run(body);
        Assert.True(exit == 0, "pwsh failed: " + output);
        var marker = output.LastIndexOf("#JSON", StringComparison.Ordinal);
        Assert.True(marker >= 0, "the script printed no #JSON marker: " + output);
        var json = output[(marker + 5)..].Trim();
        return JsonDocument.Parse(json).RootElement.Clone();
    }
}
