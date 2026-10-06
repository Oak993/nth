using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Marrow.Core.Tests.Port.Skills.Pages;

// SK-14 must compile with the repository's warnings-as-errors policy. Offline compiler only;
// no restore, vendor installer, download or provider. Outputs go to a disposable directory.
[Trait("Feature", "SK-14")]
[Trait("Spec", "DL-18")]
[Trait("Review", "SK-14")]
[Trait("Rule", "R-TEST-4")]
public sealed class REVIEW_SkillsBuildTests
{
    [Fact]
    public async Task REVIEW_Core_builds_without_relaxing_nullable_diagnostics()
    {
        var root = Repository();
        var output = Path.Combine(Path.GetTempPath(), "review-sk14-build-" + Guid.NewGuid().ToString("N"));
        var dotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        var executable = dotnetRoot is null ? "dotnet" : Path.Combine(dotnetRoot, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
        var start = new ProcessStartInfo(executable) { WorkingDirectory = root, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var argument in new[] { "build", "src/Marrow.Core/Marrow.Core.csproj", "--no-restore", "-t:Rebuild", "-p:EnableWindowsTargeting=true", "-p:TreatWarningsAsErrors=true", "-p:WarningsNotAsErrors=", "-p:OutputPath=" + Path.Combine(output, "bin") + Path.DirectorySeparatorChar, "-p:IntermediateOutputPath=" + Path.Combine(output, "obj") + Path.DirectorySeparatorChar })
            start.ArgumentList.Add(argument);
        try
        {
            using var process = Process.Start(start)!;
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException) { process.Kill(entireProcessTree: true); throw; }
            Assert.True(process.ExitCode == 0, await stdout + await stderr);
        }
        finally { if (Directory.Exists(output)) Directory.Delete(output, recursive: true); }
    }

    private static string Repository([CallerFilePath] string path = "")
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(path)!);
        while (!File.Exists(Path.Combine(directory.FullName, "Marrow.sln"))) directory = directory.Parent!;
        return directory.FullName;
    }
}
