using Marrow.Core.Tests.Fixtures;

namespace Marrow.Core.Tests.Packaging;

// Only disposable PowerShell exit fixtures are started; no setup, app, native fixture or packager is run.
public sealed class C4U7SmokeOutcomeTests
{
    [Fact]
    [Trait("Decision", "ED-247")]
    public void Successful_disposable_smoke_exits_without_an_error()
    {
        var result = RunExitFixture(0);
        Assert.Equal("", result.GetProperty("message").GetString());
        Assert.True(result.GetProperty("handleDisposed").GetBoolean());
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Nonzero_disposable_smoke_exit_fails_with_the_exit_code()
    {
        var result = RunExitFixture(7);
        Assert.Contains("exited with code 7", result.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.True(result.GetProperty("handleDisposed").GetBoolean());
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Failed_smoke_start_preserves_the_plain_start_error()
    {
        var result = PackagingRig.RunJson("""
            $message = ''
            try { Invoke-NthUiSmoke -StartProcess { throw 'Disposable fixture start failed.' } -TimeoutMilliseconds 100 }
            catch { $message = $_.Exception.Message }
            Write-Output '#JSON'
            [pscustomobject]@{ message = $message } | ConvertTo-Json -Compress
            """);
        Assert.Equal("Disposable fixture start failed.", result.GetProperty("message").GetString());
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Packaging_uses_the_bounded_smoke_seam_with_a_nonblocking_start()
    {
        var script = File.ReadAllText(Path.Combine(PackagingRig.Scripts, "package-windows.ps1"));
        Assert.Contains("Invoke-NthUiSmoke -StartProcess { Start-NthUiSmokeProcess -FilePath $setupFree -ArgumentList @('--ui-smoke') }", script, StringComparison.Ordinal);
        Assert.DoesNotContain("--ui-smoke' -Wait", script, StringComparison.Ordinal);
    }

    private static System.Text.Json.JsonElement RunExitFixture(int exitCode) => PackagingRig.RunJson("""
        $state = [pscustomobject]@{ owned = $null; handle = $null }
        try {
            $message = ''
            try {
                Invoke-NthUiSmoke -StartProcess {
                    $state.owned = Start-NthUiSmokeProcess -FilePath (Get-Process -Id $PID).Path -ArgumentList @('-NoLogo', '-NoProfile', '-NonInteractive', '-Command', 'exit EXIT_CODE')
                    $state.handle = $state.owned.Process.SafeHandle
                    $state.owned
                } -TimeoutMilliseconds 5000 -StopTimeoutMilliseconds 5000
            }
            catch { $message = $_.Exception.Message }
            Write-Output '#JSON'
            [pscustomobject]@{ message = $message; handleDisposed = ($state.handle -and $state.handle.IsClosed) } | ConvertTo-Json -Compress
        } finally {
            if ($state.owned) { try { $state.owned.Stop(5000) } finally { $state.owned.Dispose() } }
        }
        """.Replace("EXIT_CODE", exitCode.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal));
}
