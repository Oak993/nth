using System.Diagnostics;
using System.Globalization;
using Marrow.Services.Port.Skills.Shims;

namespace Marrow.Services.Tests.Port.Skills.Shims;

// PORT-ONLY: SK-6 native Windows-only acceptance, never a real tool target.
public sealed class ShimWindowsTests
{
    [WindowsShimFact, Trait("Feature", "SK-6")]
    public async Task Cmd_invocation_preserves_arguments_streams_and_exit_code() => await Shell("cmd.exe");

    [WindowsShimFact, Trait("Feature", "SK-6")]
    public async Task PowerShell_invocation_preserves_arguments_streams_and_exit_code() => await Shell("powershell.exe");

    [WindowsShimFact, Trait("Feature", "SK-6")]
    public async Task Git_Bash_invocation_preserves_arguments_streams_and_exit_code() => await Shell("bash.exe");

    private static async Task Shell(string shell)
    {
        using var fixture = new ShimFixtures();
        var shim = fixture.Install("qpdf", new ShimDefinition { Target = fixture.Target });
        // Shell-specific escaping; argv includes spaces, literal quote and Unicode.
        string[] expected = ["two words", "a\"quote", "مرحبا 日本語"];
        string Command(bool echo) => shell switch
        {
            "cmd.exe" => "\"\"" + shim + "\" " + (echo ? "echo" : "args \"two words\" \"a\\\"quote\" \"مرحبا 日本語\"") + "\"",
            // Start-Process -NoNewWindow inherits native handles without PowerShell's pipeline transcoding.
            // ArgumentList here is the raw Windows command line, whose quoted argv is tested below.
            "powershell.exe" => "$p=Start-Process -FilePath '" + shim + "' -ArgumentList '" +
                (echo ? "echo" : "args \"two words\" \"a\\\"quote\" \"مرحبا 日本語\"") +
                "' -NoNewWindow -Wait -PassThru; exit $p.ExitCode",
            "bash.exe" => "exec '" + shim.Replace('\\', '/') + "' " + (echo ? "echo" : "args 'two words' 'a\"quote' 'مرحبا 日本語'"),
            _ => throw new InvalidOperationException()
        };
        var command = Command(false);
        string[] prefix = shell switch
        {
            "cmd.exe" => ["/d", "/s", "/c"],
            "powershell.exe" => ["-NoProfile", "-NonInteractive", "-Command"],
            "bash.exe" => ["--noprofile", "--norc", "-c"],
            _ => throw new InvalidOperationException()
        };
        var executable = shell == "bash.exe"
            ? Environment.GetEnvironmentVariable("MARROW_SK6_GIT_BASH") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Git", "bin", "bash.exe")
            : shell;
        Assert.True(shell != "bash.exe" || File.Exists(executable), "WINDOWS-ONLY prerequisite: Git for Windows Bash (or MARROW_SK6_GIT_BASH).");
        using (var child = fixture.Start(executable, [.. prefix, command]))
        {
            child.StandardInput.Close();
            var output = await child.StandardOutput.ReadToEndAsync();
            var error = await child.StandardError.ReadToEndAsync();
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
            Assert.Equal("", error);
            Assert.Equal(0, child.ExitCode);
            Assert.Equal(expected, output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => ShimFixtures.Decode(l.TrimEnd('\r'))));
        }
        // Native command invoked through each shell inherits byte streams; no PowerShell pipeline transcoding.
        command = Command(true);
        using var echo = fixture.Start(executable, [.. prefix, command]);
        using var stdout = new MemoryStream();
        using var stderr = new MemoryStream();
        var outTask = echo.StandardOutput.BaseStream.CopyToAsync(stdout);
        var errTask = echo.StandardError.BaseStream.CopyToAsync(stderr);
        byte[] input = [0, 255, 128, 13, 10];
        await echo.StandardInput.BaseStream.WriteAsync(input);
        echo.StandardInput.Close();
        await echo.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
        await Task.WhenAll(outTask, errTask);
        Assert.Equal(input, stdout.ToArray());
        Assert.Equal(new byte[] { 0, 255, 10, 13, 128 }, stderr.ToArray());
        Assert.Equal(37, echo.ExitCode);
    }
    [WindowsShimFact, Trait("Feature", "SK-6")]
    public async Task Killing_the_shim_kills_its_child_and_grandchild_by_Job_Object()
    {
        using var fixture = new ShimFixtures();
        var shim = fixture.Install("qpdf", new ShimDefinition { Target = fixture.Target });
        var signal = Path.Combine(fixture.Root, "grandchild.pid");
        using var child = fixture.Start(shim, "tree", signal);
        Process? grandchild = null;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            while (!File.Exists(signal)) await Task.Delay(20, timeout.Token);
            grandchild = Process.GetProcessById(int.Parse(File.ReadAllText(signal), CultureInfo.InvariantCulture));
            child.Kill(); // pid only; the launcher must own tree cleanup.
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
            await grandchild.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
            Assert.True(grandchild.HasExited);
            Assert.Equal("", await child.StandardError.ReadToEndAsync()); // EOF also proves the direct target exited.
        }
        finally
        {
            if (!child.HasExited) child.Kill(true);
            if (grandchild != null) { if (!grandchild.HasExited) grandchild.Kill(true); grandchild.Dispose(); }
        }
    }
}
