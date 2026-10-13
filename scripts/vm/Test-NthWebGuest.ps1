<#
.SYNOPSIS
  Web surfaces W23 (DESIGN 14, "installs on clean x64 and arm64 VMs with and without the runtime; uninstall removes the web data
  folder"): the checks that run INSIDE a clean VM around an nth install. Written, not run: the lead runs the matrix in a quiet window
  (scripts/vm/Invoke-NthCleanVmMatrix.ps1 copies this file in and calls it).

.DESCRIPTION
  It changes nothing about the machine except in -Phase SeedWebData (which makes a few files under %LOCALAPPDATA%\nth, the stand-in
  for what Visus writes when it has been used) and it never installs or uninstalls anything itself. One phase per call:

    PreInstall   the runtime is as the cell says (Present: a WebView2 runtime is registered; Absent: none is) and nth is not installed.
    Installed    after nth's installer ran: the app folder holds WebView2Loader.dll of the machine's architecture beside the managed Core
                 assembly, no WPF or WinForms assembly, and the runtime state has not changed.
    SeedWebData  makes %LOCALAPPDATA%\nth\WebView2 with a read-only file, a nested folder, and %LOCALAPPDATA%\nth\keep-w23.txt beside it.
    Uninstalled  after nth's uninstall: the app folder is gone, %LOCALAPPDATA%\nth\WebView2 is gone (the Velopack before-uninstall hook
                 removed it), keep-w23.txt is still there (the hook removes the web data and nothing else of nth's), no WebView2 browser
                 process still holds nth's data folder, and the shared runtime was not touched.

  Runtime detection is Microsoft's documented registry check (pv under the WebView2 client GUID, machine and user hives).
  The exit code is the number of failed checks; the results are also written as JSON to -OutFile.

.EXAMPLE
  pwsh -File Test-NthWebGuest.ps1 -Phase Installed -Architecture x64 -Runtime Present -OutFile C:\nth-w23\installed.json
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('PreInstall', 'Installed', 'SeedWebData', 'Uninstalled')][string]$Phase,
    [Parameter(Mandatory)][ValidateSet('x64', 'arm64')][string]$Architecture,
    [Parameter(Mandatory)][ValidateSet('Present', 'Absent')][string]$Runtime,
    # Where nth is installed. Empty: the new default (%LOCALAPPDATA%\Programs\nth), else the earlier root (%LOCALAPPDATA%\NthApp).
    [string]$InstallRoot = '',
    [string]$OutFile = ''
)

$ErrorActionPreference = 'Stop'
$sibling = Join-Path $PSScriptRoot 'NthWebView2.ps1'
if (-not (Test-Path -LiteralPath $sibling)) { throw "NthWebView2.ps1 must sit beside this script (the matrix copies it in): $sibling" }
. $sibling

$checks = New-Object System.Collections.Generic.List[object]
function Add-Check([string]$Name, [bool]$Ok, [string]$Detail = '') {
    $checks.Add([pscustomobject]@{ phase = $Phase; name = $Name; ok = $Ok; detail = $Detail })
    $mark = if ($Ok) { 'PASS' } else { 'FAIL' }
    Write-Host ("[{0}] {1}{2}" -f $mark, $Name, $(if ($Detail) { " - $Detail" } else { '' }))
}

$dataRoot = Join-Path $env:LOCALAPPDATA 'nth'
$webData = Join-Path $dataRoot 'WebView2'
$keep = Join-Path $dataRoot 'keep-w23.txt'
$clientGuid = '{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}'

function Get-RuntimeVersions {
    # Microsoft: "Inspect the pv (REG_SZ) regkey at both locations" (per-machine under WOW6432Node on 64-bit Windows, per-user under HKCU).
    $paths = @("HKLM:\SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\$clientGuid", "HKCU:\Software\Microsoft\EdgeUpdate\Clients\$clientGuid")
    foreach ($path in $paths) {
        if (Test-Path -LiteralPath $path) {
            $pv = (Get-ItemProperty -LiteralPath $path -Name pv -ErrorAction SilentlyContinue).pv
            if ($pv -and $pv -ne '0.0.0.0') { [pscustomobject]@{ Hive = $path; Version = $pv } }
        }
    }
}

function Find-Root {
    if ($InstallRoot) { return $InstallRoot }
    foreach ($candidate in @((Join-Path $env:LOCALAPPDATA 'Programs\nth'), (Join-Path $env:LOCALAPPDATA 'NthApp'))) {
        if (Test-Path -LiteralPath $candidate) { return $candidate }
    }
    return (Join-Path $env:LOCALAPPDATA 'Programs\nth')
}

function Assert-RuntimeState([string]$When) {
    $found = @(Get-RuntimeVersions)
    if ($Runtime -eq 'Present') {
        Add-Check "${When}: a WebView2 runtime is registered" ($found.Count -gt 0) (($found | ForEach-Object { "$($_.Hive) = $($_.Version)" }) -join '; ')
    } else {
        Add-Check "${When}: no WebView2 runtime is registered" ($found.Count -eq 0) (($found | ForEach-Object { "$($_.Hive) = $($_.Version)" }) -join '; ')
    }
}

$expectedArchitecture = if ($Architecture -eq 'x64') { 'X64' } else { 'Arm64' }
Add-Check "the machine is $Architecture" ([System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString() -eq $expectedArchitecture) ([System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString())
$rid = if ($Architecture -eq 'x64') { 'win-x64' } else { 'win-arm64' }

switch ($Phase) {
    'PreInstall' {
        Assert-RuntimeState 'before the install'
        $root = Find-Root
        Add-Check 'nth is not installed yet' (-not (Test-Path -LiteralPath (Join-Path $root 'current'))) $root
        Add-Check 'no nth web data yet' (-not (Test-Path -LiteralPath $webData)) $webData
    }
    'Installed' {
        $root = Find-Root
        $app = Join-Path $root 'current'
        Add-Check 'the app folder exists' (Test-Path -LiteralPath $app) $app
        $loader = Join-Path $app 'WebView2Loader.dll'
        if (Test-Path -LiteralPath $loader) {
            $ok = Test-NthLoaderBytes -Bytes ([System.IO.File]::ReadAllBytes($loader)) -Runtime $rid
            Add-Check "WebView2Loader.dll is a $rid image" $ok $loader
        } else {
            Add-Check "WebView2Loader.dll is beside the app" $false $loader
        }
        foreach ($other in @(Get-ChildItem -LiteralPath $app -Filter 'WebView2Loader.dll' -Recurse -ErrorAction SilentlyContinue | Where-Object { $_.FullName -ne $loader })) {
            Add-Check "another loader copy is a $rid image" (Test-NthLoaderBytes -Bytes ([System.IO.File]::ReadAllBytes($other.FullName)) -Runtime $rid) $other.FullName
        }
        Add-Check 'Microsoft.Web.WebView2.Core.dll is beside the app' (Test-Path -LiteralPath (Join-Path $app 'Microsoft.Web.WebView2.Core.dll')) $app
        Add-Check 'no WPF or WinForms WebView2 assembly' (-not ((Test-Path -LiteralPath (Join-Path $app 'Microsoft.Web.WebView2.Wpf.dll')) -or (Test-Path -LiteralPath (Join-Path $app 'Microsoft.Web.WebView2.WinForms.dll')))) $app
        $exe = Join-Path $app 'nth.exe'
        Add-Check 'the app executable is there' (Test-Path -LiteralPath $exe) $exe
        Assert-RuntimeState 'after the install'
    }
    'SeedWebData' {
        New-Item -ItemType Directory -Force -Path (Join-Path $webData 'EBWebView\browse\Cache') | Out-Null
        Set-Content -LiteralPath (Join-Path $webData 'EBWebView\browse\Cookies') -Value 'cookie'
        $held = Join-Path $webData 'EBWebView\browse\Cache\data_0'
        Set-Content -LiteralPath $held -Value 'cache'
        Set-ItemProperty -LiteralPath $held -Name IsReadOnly -Value $true
        Set-Content -LiteralPath $keep -Value 'the uninstall hook removes the web data and nothing else of nth''s'
        Add-Check 'web data seeded (with a read-only file)' (Test-Path -LiteralPath $held) $webData
        Add-Check 'the keep file is beside it' (Test-Path -LiteralPath $keep) $keep
    }
    'Uninstalled' {
        $root = Find-Root
        Add-Check 'the app folder is gone' (-not (Test-Path -LiteralPath (Join-Path $root 'current'))) $root
        Add-Check 'the web data folder is gone' (-not (Test-Path -LiteralPath $webData)) $webData
        Add-Check 'the rest of nth''s data folder was left alone' (Test-Path -LiteralPath $keep) $keep
        $holders = @(Get-CimInstance Win32_Process -Filter "Name = 'msedgewebview2.exe'" -ErrorAction SilentlyContinue | Where-Object { $_.CommandLine -and $_.CommandLine -like '*nth\WebView2*' })
        Add-Check 'no WebView2 process still holds nth''s data folder' ($holders.Count -eq 0) "$($holders.Count) process(es)"
        Assert-RuntimeState 'after the uninstall'
    }
}

$failed = @($checks | Where-Object { -not $_.ok }).Count
if ($OutFile) {
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $OutFile) | Out-Null
    $checks | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $OutFile -Encoding utf8
}
Write-Host "${Phase} on ${Architecture}, runtime ${Runtime}: $($checks.Count - $failed) passed, $failed failed."
exit $failed
