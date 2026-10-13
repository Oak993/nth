using System.Text.Json;

namespace Marrow.Core.Tests.Fixtures;

// This fixture owns only disposable PowerShell sleepers and a temp folder; no setup/app or global process enumeration.
internal static class C4SmokeTreeFixture
{
    public static JsonElement Run(string parentMode) => PackagingRig.RunJson("""
        $folder = Join-Path ([IO.Path]::GetTempPath()) ('nth smoke fixture ' + [Guid]::NewGuid().ToString('N'))
        $null = [IO.Directory]::CreateDirectory($folder)
        $sleeper = Join-Path $folder 'child sleeper.ps1'
        $starter = Join-Path $folder 'parent starter.ps1'
        $pidFile = Join-Path $folder 'child identifier.pid'
        $state = [pscustomobject]@{ owned = $null; observer = $null; descendant = $null; handle = $null; parentExitedInStart = $false; parentExitCode = -1 }
        try {
            [IO.File]::WriteAllText($sleeper, '[Threading.Thread]::Sleep(60000)')
            [IO.File]::WriteAllText($starter, @'
        param($Sleeper, $PidFile, $Mode)
        $start = [Diagnostics.ProcessStartInfo]::new((Get-Process -Id $PID).Path)
        $start.UseShellExecute = $false; $start.CreateNoWindow = $true
        foreach ($value in @('-NoLogo', '-NoProfile', '-NonInteractive', '-File', $Sleeper)) { $start.ArgumentList.Add($value) }
        $child = [Diagnostics.Process]::Start($start)
        [IO.File]::WriteAllText($PidFile, [string]$child.Id)
        if ($Mode -eq 'sleep') { [Threading.Thread]::Sleep(60000) } else { exit ([int]$Mode) }
        '@)
            $message = ''
            $elapsed = [Diagnostics.Stopwatch]::StartNew()
            try {
                Invoke-NthUiSmoke -StartProcess {
                    $state.owned = Start-NthUiSmokeProcess -FilePath (Get-Process -Id $PID).Path -ArgumentList @('-NoLogo', '-NoProfile', '-NonInteractive', '-File', $starter, $sleeper, $pidFile, 'PARENT_MODE')
                    $state.handle = $state.owned.Process.SafeHandle
                    if ('PARENT_MODE' -eq 'sleep') {
                        $state.observer = [Diagnostics.Process]::GetProcessById($state.owned.Process.Id)
                        $null = $state.observer.SafeHandle
                    }
                    $ready = [Diagnostics.Stopwatch]::StartNew()
                    $descendantId = 0
                    while ($descendantId -le 0 -and $ready.ElapsedMilliseconds -lt 5000) {
                        if ([IO.File]::Exists($pidFile)) {
                            try { $null = [int]::TryParse([IO.File]::ReadAllText($pidFile), [ref]$descendantId) } catch [IO.IOException] { }
                        }
                        if ($descendantId -le 0) { [Threading.Thread]::Sleep(10) }
                    }
                    if ($descendantId -le 0) { throw 'Disposable fixture did not start its child within five seconds.' }
                    $state.descendant = [Diagnostics.Process]::GetProcessById($descendantId)
                    $null = $state.descendant.SafeHandle
                    if ('PARENT_MODE' -ne 'sleep') {
                        $state.parentExitedInStart = $state.owned.WaitForExit(5000)
                        if (-not $state.parentExitedInStart) { throw 'Disposable fixture parent did not exit within five seconds.' }
                        $state.parentExitCode = $state.owned.ExitCode
                    }
                    $state.owned
                } -TimeoutMilliseconds 150 -StopTimeoutMilliseconds 5000
            } catch { $message = $_.Exception.Message }
            $elapsed.Stop()
            $parentAlive = $false; $descendantAlive = $false
            if ($state.observer) { $state.observer.Refresh(); $parentAlive = -not $state.observer.HasExited }
            if ($state.descendant) { $state.descendant.Refresh(); $descendantAlive = -not $state.descendant.HasExited }
            Write-Output '#JSON'
            [pscustomobject]@{ message = $message; parentAlive = $parentAlive; descendantAlive = $descendantAlive; parentExitedInStart = $state.parentExitedInStart; parentExitCode = $state.parentExitCode; handleDisposed = ($state.handle -and $state.handle.IsClosed); elapsedMs = $elapsed.ElapsedMilliseconds } | ConvertTo-Json -Compress
        } finally {
            try {
                if ($state.owned) { try { $state.owned.Stop(5000) } finally { $state.owned.Dispose() } }
            } finally {
                try {
                    foreach ($owned in @($state.descendant, $state.observer)) {
                        if ($owned) {
                            try { if (-not $owned.HasExited) { $owned.Kill($true); $null = $owned.WaitForExit(5000) } }
                            catch [InvalidOperationException] { }
                            finally { $owned.Dispose() }
                        }
                    }
                } finally { [IO.Directory]::Delete($folder, $true) }
            }
        }
        """.Replace("PARENT_MODE", parentMode, StringComparison.Ordinal));
}
