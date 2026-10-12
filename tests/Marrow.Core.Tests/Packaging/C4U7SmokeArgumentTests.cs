using Marrow.Core.Tests.Fixtures;

namespace Marrow.Core.Tests.Packaging;

public sealed class C4U7SmokeArgumentTests
{
    [Fact]
    [Trait("Decision", "ED-247")]
    public void Spaces_quotes_empty_arguments_and_trailing_slashes_reach_the_disposable_child_unchanged()
    {
        var result = PackagingRig.RunJson("""
            $folder = Join-Path ([IO.Path]::GetTempPath()) ('nth argv fixture ' + [Guid]::NewGuid().ToString('N'))
            $null = [IO.Directory]::CreateDirectory($folder)
            $scriptFile = Join-Path $folder 'copy arguments.ps1'
            $outputFile = Join-Path $folder 'copied arguments.json'
            $state = [pscustomobject]@{ owned = $null }
            try {
                [IO.File]::WriteAllText($scriptFile, 'param($OutputFile); [IO.File]::WriteAllText($OutputFile, (ConvertTo-Json -InputObject @($args) -Compress))')
                $message = ''
                try {
                    Invoke-NthUiSmoke -StartProcess {
                        $state.owned = Start-NthUiSmokeProcess -FilePath (Get-Process -Id $PID).Path -ArgumentList @('-NoLogo', '-NoProfile', '-NonInteractive', '-File', $scriptFile, $outputFile, 'space name', 'a"b', 'C:\ends with space\', '')
                        $state.owned
                    } -TimeoutMilliseconds 5000 -StopTimeoutMilliseconds 5000
                } catch { $message = $_.Exception.Message }
                $arguments = @()
                if ([IO.File]::Exists($outputFile)) { $arguments = @([IO.File]::ReadAllText($outputFile) | ConvertFrom-Json) }
                Write-Output '#JSON'
                [pscustomobject]@{ message = $message; arguments = $arguments } | ConvertTo-Json -Compress
            } finally {
                if ($state.owned) { try { $state.owned.Stop(5000) } finally { $state.owned.Dispose() } }
                [IO.Directory]::Delete($folder, $true)
            }
            """);
        Assert.Equal("", result.GetProperty("message").GetString());
        Assert.Equal(["space name", "a\"b", "C:\\ends with space\\", ""], result.GetProperty("arguments").EnumerateArray().Select(argument => argument.GetString()!).ToArray());
    }
}
