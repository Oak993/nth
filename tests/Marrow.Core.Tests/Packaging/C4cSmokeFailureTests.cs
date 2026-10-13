using Marrow.Core.Tests.Fixtures;

namespace Marrow.Core.Tests.Packaging;

public sealed class C4cSmokeFailureTests
{
    [Fact]
    [Trait("Decision", "ED-247")]
    public void Real_failed_start_uses_the_tested_cleanup_seam_with_its_primary_failure()
    {
        var source = File.ReadAllText(Path.Combine(PackagingRig.Scripts, "lib", "SmokeProcess.cs"));
        Assert.Contains("CleanupFailedStart(startFailure,", source, StringComparison.Ordinal);
        Assert.Contains("startFailure = error;", source, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Failed_start_cleanup_retains_the_start_error_and_disposes_after_a_cleanup_error() => CheckFailedStartCleanup(false);

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Failed_start_cleanup_retains_start_stop_and_dispose_errors() => CheckFailedStartCleanup(true);

    private static void CheckFailedStartCleanup(bool disposeFails)
    {
        var result = PackagingRig.RunJson($$"""
            Add-Type -Path (Join-Path $script:NthLibDir 'SmokeProcess.cs')
            $method = [Nth.Build.SmokeProcess].GetMethod('CleanupFailedStart', [Reflection.BindingFlags]'NonPublic,Static')
            if (-not $method) { throw 'The deterministic failed-start cleanup seam was not found.' }
            # CLR callbacks make exceptions observable to the C# cleanup catch blocks.
            # The type's name sits on its own line: R-TEST-3 reads a type keyword and a name on one line as a test type (ED-NEW-inst-merge-15).
            Add-Type -TypeDefinition @'
            public static class
            CleanupFixture
            {
                public static bool Disposed;
                public static bool DisposeFails;
                public static void Stop() { throw new System.InvalidOperationException("Synthetic failed-start stop error."); }
                public static void Dispose()
                {
                    Disposed = true;
                    if (DisposeFails) throw new System.InvalidOperationException("Synthetic disposal error.");
                }
                public static string Run(System.Reflection.MethodInfo method, bool disposeFails)
                {
                    DisposeFails = disposeFails;
                    try
                    {
                        method.Invoke(null, new object[] {
                            new System.InvalidOperationException("Synthetic assignment failure."),
                            new System.Action(Stop), new System.Action(Dispose) });
                        return "";
                    }
                    catch (System.Reflection.TargetInvocationException error)
                    {
                        // Capture the complete aggregate before PowerShell projects its base exception.
                        return error.InnerException.ToString();
                    }
                }
            }
            '@
            $message = [CleanupFixture]::Run($method, ${{disposeFails.ToString().ToLowerInvariant()}})
            Write-Output '#JSON'
            [pscustomobject]@{ message = $message; disposed = [CleanupFixture]::Disposed } | ConvertTo-Json -Compress
            """);
        Assert.Contains("Synthetic assignment failure.", result.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Contains("Synthetic failed-start stop error.", result.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.True(result.GetProperty("disposed").GetBoolean());
        if (disposeFails) Assert.Contains("Synthetic disposal error.", result.GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_dispose_error_retains_the_primary_smoke_failure()
    {
        var result = PackagingRig.RunJson("""
            $fixtureOwned = [pscustomobject]@{ ExitCode = 7; stopped = $false; disposed = $false }
            $fixtureOwned | Add-Member ScriptMethod WaitForExit { param($ms) return $true }
            $fixtureOwned | Add-Member ScriptMethod Stop { param($ms) $this.stopped = $true }
            $fixtureOwned | Add-Member ScriptMethod Dispose { $this.disposed = $true; throw 'Synthetic disposal error.' }
            $message = ''
            try { Invoke-NthUiSmoke -StartProcess { $fixtureOwned } } catch { $message = $_.Exception.Message }
            Write-Output '#JSON'
            [pscustomobject]@{ message = $message; stopped = $fixtureOwned.stopped; disposed = $fixtureOwned.disposed } | ConvertTo-Json -Compress
            """);
        Assert.Contains("exited with code 7", result.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Contains("Synthetic disposal error.", result.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.True(result.GetProperty("stopped").GetBoolean());
        Assert.True(result.GetProperty("disposed").GetBoolean());
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_cleanup_error_retains_the_primary_smoke_timeout_and_still_disposes() => CheckPrimaryFailure(false, 0, "timed out after 30000 ms");

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_cleanup_error_retains_the_nonzero_smoke_exit_and_still_disposes() => CheckPrimaryFailure(true, 7, "exited with code 7");

    private static void CheckPrimaryFailure(bool exited, int exitCode, string expected)
    {
        var result = PackagingRig.RunJson($$"""
            $fixtureOwned = [pscustomobject]@{ ExitCode = {{exitCode}}; disposed = $false }
            $fixtureOwned | Add-Member ScriptMethod WaitForExit { param($ms) return ${{exited.ToString().ToLowerInvariant()}} }
            $fixtureOwned | Add-Member ScriptMethod Stop { param($ms) throw 'Synthetic cleanup timed out.' }
            $fixtureOwned | Add-Member ScriptMethod Dispose { $this.disposed = $true }
            $message = ''
            try { Invoke-NthUiSmoke -StartProcess { $fixtureOwned } } catch { $message = $_.Exception.Message }
            Write-Output '#JSON'
            [pscustomobject]@{ message = $message; disposed = $fixtureOwned.disposed } | ConvertTo-Json -Compress
            """);
        Assert.Contains(expected, result.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Contains("Synthetic cleanup timed out.", result.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.True(result.GetProperty("disposed").GetBoolean());
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_cleanup_failure_after_success_still_fails_and_disposes()
    {
        var result = PackagingRig.RunJson("""
            $fixtureOwned = [pscustomobject]@{ ExitCode = 0; disposed = $false }
            $fixtureOwned | Add-Member ScriptMethod WaitForExit { param($ms) return $true }
            $fixtureOwned | Add-Member ScriptMethod Stop { param($ms) throw 'Synthetic cleanup timed out.' }
            $fixtureOwned | Add-Member ScriptMethod Dispose { $this.disposed = $true }
            $message = ''
            try { Invoke-NthUiSmoke -StartProcess { $fixtureOwned } } catch { $message = $_.Exception.Message }
            Write-Output '#JSON'
            [pscustomobject]@{ message = $message; disposed = $fixtureOwned.disposed } | ConvertTo-Json -Compress
            """);
        Assert.Contains("Synthetic cleanup timed out.", result.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.True(result.GetProperty("disposed").GetBoolean());
    }
}
