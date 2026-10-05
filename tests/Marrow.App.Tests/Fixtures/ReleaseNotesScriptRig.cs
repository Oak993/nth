using System.Diagnostics;
using System.Text;

namespace Marrow.App.Tests.Fixtures;

// UE-6: every script runs in this disposable repository. Native commands are guards, never the real tools.
internal sealed class ReleaseNotesScriptRig : IDisposable
{
    public string Folder { get; } = Directory.CreateTempSubdirectory("ue6 notes ü ").FullName;
    public string Changelog => Path.Combine(Folder, "CHANGELOG.md");
    public string Notes => Path.Combine(Folder, "override.md");
    public string Output => Path.Combine(Folder, "extracted.md");
    public string Guard => Path.Combine(Folder, "native-command-called");

    public ReleaseNotesScriptRig()
    {
        var root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "Marrow.sln"))) root = Directory.GetParent(root)!.FullName;
        Directory.CreateDirectory(Path.Combine(Folder, "scripts"));
        foreach (var script in new[] { "package-windows.ps1", "release-notes.ps1", "extract-release-notes.ps1" })
            if (File.Exists(Path.Combine(root, "scripts", script))) File.Copy(Path.Combine(root, "scripts", script), Path.Combine(Folder, "scripts", script));
        var source = Directory.CreateDirectory(Path.Combine(Folder, "src", "Marrow.App")).FullName;
        File.WriteAllText(Path.Combine(source, "AssemblyInfo.cs"), "[assembly: AssemblyMetadata(\"CFBundleShortVersionString\", \"1.7.0\")]\n[assembly: AssemblyMetadata(\"CFBundleVersion\", \"21\")]");
        File.WriteAllText(Path.Combine(Folder, "Directory.Packages.props"), "<PackageVersion Include=\"Velopack\" Version=\"0.0.0\" />");
        File.WriteAllText(Path.Combine(Folder, "guard.ps1"), """
            param([switch]$ValidateReleaseNotesOnly, [string]$ReleaseNotesFile)
            $ErrorActionPreference = 'Stop'
            function global:vpk { Set-Content (Join-Path $PSScriptRoot 'native-command-called') 'vpk'; throw 'FORBIDDEN vpk invocation' }
            function global:dotnet { Set-Content (Join-Path $PSScriptRoot 'native-command-called') 'dotnet'; throw 'FORBIDDEN dotnet invocation' }
            function global:gh { Set-Content (Join-Path $PSScriptRoot 'native-command-called') 'gh'; throw 'FORBIDDEN gh invocation' }
            & (Join-Path $PSScriptRoot 'scripts/package-windows.ps1') @PSBoundParameters
            """, new UTF8Encoding(false));
    }

    public (int ExitCode, string Text) Run(string script, params string[] args)
    {
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("MARROW_TEST_PWSH") ?? "pwsh")
        {
            WorkingDirectory = Folder, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false,
        };
        foreach (var arg in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-File", Path.Combine(Folder, script) }.Concat(args)) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(30_000)) { process.Kill(entireProcessTree: true); throw new TimeoutException("Release notes script did not finish."); }
        return (process.ExitCode, stdout.GetAwaiter().GetResult() + stderr.GetAwaiter().GetResult());
    }

    public void AssertNoCommandsOrArtifacts()
    {
        Assert.False(File.Exists(Guard), "A native command was reached.");
        Assert.False(Directory.Exists(Path.Combine(Folder, "artifacts")));
    }

    public void Dispose() => Directory.Delete(Folder, recursive: true);
}
