using Marrow.Core.Tests.Fixtures;

namespace Marrow.Core.Tests.Packaging;

public sealed class C4cSmokeCoverageTests
{
    [Fact]
    [Trait("Decision", "ED-247")]
    public void Real_Stop_uses_the_tested_polling_seam_on_its_owned_job()
    {
        var source = File.ReadAllText(Path.Combine(PackagingRig.Scripts, "lib", "SmokeProcess.cs"));
        Assert.Contains("TerminateJobObject(job, 1)", source, StringComparison.Ordinal);
        Assert.Contains("WaitForJobEmpty(milliseconds,", source, StringComparison.Ordinal);
        Assert.Contains("QueryInformationJobObject(job, 1, out Accounting info", source, StringComparison.Ordinal);
        Assert.True(source.IndexOf("TerminateJobObject(job, 1)", StringComparison.Ordinal) < source.IndexOf("WaitForJobEmpty(milliseconds,", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Packaging_smoke_defaults_are_exactly_30000_to_exit_and_5000_to_stop()
    {
        var result = PackagingRig.RunJson("""
            $fixtureOwned = [pscustomobject]@{ ExitCode = 0; exitWait = 0; stopWait = 0; disposed = $false }
            $fixtureOwned | Add-Member ScriptMethod WaitForExit { param($ms) $this.exitWait = $ms; return $true }
            $fixtureOwned | Add-Member ScriptMethod Stop { param($ms) $this.stopWait = $ms }
            $fixtureOwned | Add-Member ScriptMethod Dispose { $this.disposed = $true }
            Invoke-NthUiSmoke -StartProcess { $fixtureOwned }
            Write-Output '#JSON'
            $fixtureOwned | ConvertTo-Json -Compress
            """);
        Assert.Equal(30000, result.GetProperty("exitWait").GetInt32());
        Assert.Equal(5000, result.GetProperty("stopWait").GetInt32());
        Assert.True(result.GetProperty("disposed").GetBoolean());
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Dispose_closes_the_job_handle_and_the_process_handle_independently()
    {
        var result = PackagingRig.RunJson("""
            Add-Type -Path (Join-Path $script:NthLibDir 'SmokeProcess.cs')
            # A non-owning synthetic handle never closes an OS handle. The process object is a disposable observer of this fixture.
            $job = [Microsoft.Win32.SafeHandles.SafeFileHandle]::new([IntPtr]123, $false)
            $observer = [Diagnostics.Process]::GetCurrentProcess()
            $processHandle = $observer.SafeHandle
            $ctor = [Nth.Build.SmokeProcess].GetConstructors([Reflection.BindingFlags]'NonPublic,Instance')[0]
            $owned = $ctor.Invoke(@($observer, $job))
            try {
                $owned.Dispose()
                $owned.Dispose()
                Write-Output '#JSON'
                [pscustomobject]@{ jobClosed = $job.IsClosed; processClosed = $processHandle.IsClosed } | ConvertTo-Json -Compress
            } finally { $job.Dispose(); $observer.Dispose() }
            """);
        Assert.True(result.GetProperty("jobClosed").GetBoolean());
        Assert.True(result.GetProperty("processClosed").GetBoolean());
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Stop_polls_until_the_owned_job_is_empty_with_a_fake_clock() => CheckStop(false, 3, 20, 2, "");

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Stop_throws_at_the_exact_deadline_when_the_owned_job_stays_active() => CheckStop(true, 4, 25, 3, "The smoke process tree did not stop within 25 ms.");

    private static void CheckStop(bool staysActive, int polls, int elapsed, int sleeps, string expected)
    {
        var result = PackagingRig.RunJson($$"""
            Add-Type -Path (Join-Path $script:NthLibDir 'SmokeProcess.cs')
            $script:clock = 0L; $script:polls = 0; $script:sleeps = 0
            $method = [Nth.Build.SmokeProcess].GetMethod('WaitForJobEmpty', [Reflection.BindingFlags]'NonPublic,Static')
            if (-not $method) { throw 'The deterministic job cleanup seam was not found.' }
            $active = [Func[uint]] { $script:polls++; if (${{staysActive.ToString().ToLowerInvariant()}} -or $script:polls -lt 3) { return 1 } else { return 0 } }
            $elapsed = [Func[long]] { return $script:clock }
            $sleep = [Action[int]] { param($ms) $script:clock += $ms; $script:sleeps++ }
            $message = ''
            try { $method.Invoke($null, @([int]25, $active, $elapsed, $sleep)) } catch { $message = $_.Exception.GetBaseException().Message }
            Write-Output '#JSON'
            [pscustomobject]@{ message = $message; polls = $script:polls; elapsed = $script:clock; sleeps = $script:sleeps } | ConvertTo-Json -Compress
            """);
        Assert.Equal(expected, result.GetProperty("message").GetString());
        Assert.Equal(polls, result.GetProperty("polls").GetInt32());
        Assert.Equal(elapsed, result.GetProperty("elapsed").GetInt32());
        Assert.Equal(sleeps, result.GetProperty("sleeps").GetInt32());
    }
}
