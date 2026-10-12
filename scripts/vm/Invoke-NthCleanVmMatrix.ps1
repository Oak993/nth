<#
.SYNOPSIS
  Web surfaces W23 (DESIGN 14): the clean-VM install matrix for nth's packaging. Written, not run: nothing in the repository starts it,
  and an agent never does. The lead runs it in a quiet window once the owner has built the VMs, with the installer the owner built.

.DESCRIPTION
  Four cells, each a clean Hyper-V checkpoint (taken once, before nth was ever installed):

      x64   with the WebView2 runtime      checkpoint nth-clean-x64-runtime     (a stock Windows 11 x64)
      x64   without the runtime            checkpoint nth-clean-x64-noruntime   (an image that never had the runtime; Windows 10 22H2)
      arm64 with the WebView2 runtime      checkpoint nth-clean-arm64-runtime   (needs an ARM64 Hyper-V host)
      arm64 without the runtime            checkpoint nth-clean-arm64-noruntime (needs an ARM64 Hyper-V host)

  For each cell it restores the checkpoint, starts the VM, and over PowerShell Direct (no network, no remoting setup):
      1. copies the installer and scripts/vm/Test-NthWebGuest.ps1 with scripts/lib/NthWebView2.ps1 into C:\nth-w23;
      2. PreInstall check (the runtime is as the cell says, nth is not there);
      3. runs the installer with -InstallArguments (default --silent);
      4. Installed check (WebView2Loader.dll of the right architecture beside the app, the managed Core assembly, runtime unchanged);
      5. SeedWebData (stand-in for what Visus writes), then runs nth's uninstall (Velopack's Update.exe --uninstall at the install root);
      6. Uninstalled check (app gone, %LOCALAPPDATA%\nth\WebView2 gone, the rest of nth's data kept, runtime untouched).
  It collects every phase's JSON under -OutDir, prints a table, and exits with the number of failed checks. It then turns the VM off
  and leaves the checkpoint as it was.

  What it does NOT cover, because a script cannot press the buttons: the runtime flow itself (open Visus in the "without runtime" cell,
  see "Visus needs the WebView2 runtime.", press Install, watch "Installing the WebView2 runtime.", see the page). That is a hand check
  for W21 once Visus (W8a) is in the build, using the same checkpoints; DESIGN 13.4 lists it (crash and failure, uninstall and privacy).

  UNVERIFIED until first use (nothing here has run): the silent switch of nth's new setup (U1/U7 ship --silent per their plan, check
  with -h on the built installer), the uninstall command (Velopack's own Update.exe --uninstall at the root), and the checkpoint names.
  All three are parameters. This script touches only the VMs named in -VmName; it never runs the installer on the host.

.EXAMPLE
  pwsh -File scripts/vm/Invoke-NthCleanVmMatrix.ps1 -Installer E:\out\nth-Setup-1.7.0.exe -VmName @{ x64 = 'nth-win11-x64'; arm64 = 'nth-win11-arm64' } -Credential (Get-Credential) -Cells x64-runtime,x64-noruntime
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Installer,
    # The VM to use per architecture.
    [Parameter(Mandatory)][hashtable]$VmName,
    # A local account in the guests (PowerShell Direct logs in with it; its profile is where nth installs).
    [Parameter(Mandatory)][pscredential]$Credential,
    [ValidateSet('x64-runtime', 'x64-noruntime', 'arm64-runtime', 'arm64-noruntime')][string[]]$Cells = @('x64-runtime', 'x64-noruntime', 'arm64-runtime', 'arm64-noruntime'),
    [string]$CheckpointPrefix = 'nth-clean',
    [string[]]$InstallArguments = @('--silent'),
    # The uninstall command, run in the guest with %LOCALAPPDATA%\Programs\nth (else %LOCALAPPDATA%\NthApp) as the root: Update.exe and its arguments.
    [string[]]$UninstallArguments = @('--uninstall'),
    [string]$OutDir = (Join-Path ([System.IO.Path]::GetTempPath()) ('nth-clean-vm-' + (Get-Date -Format 'yyyyMMdd-HHmmss')))
)

$ErrorActionPreference = 'Stop'
if (-not (Get-Command Restore-VMCheckpoint -ErrorAction SilentlyContinue)) { throw 'The Hyper-V PowerShell module is not available on this machine.' }
if (-not (Test-Path -LiteralPath $Installer)) { throw "Installer not found: $Installer" }
$guestScripts = @(
    (Join-Path $PSScriptRoot 'Test-NthWebGuest.ps1'),
    (Join-Path (Split-Path -Parent $PSScriptRoot) 'lib/NthWebView2.ps1')
)
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

function Wait-Guest([string]$Vm, [pscredential]$Cred, [int]$Minutes = 10) {
    $deadline = (Get-Date).AddMinutes($Minutes)
    while ((Get-Date) -lt $deadline) {
        try {
            $session = New-PSSession -VMName $Vm -Credential $Cred -ErrorAction Stop
            return $session
        } catch { Start-Sleep -Seconds 5 }
    }
    throw "$Vm did not accept a PowerShell Direct session in $Minutes minutes."
}

function Invoke-GuestPhase($Session, [string]$Phase, [string]$Architecture, [string]$Runtime, [string]$Cell) {
    $remoteJson = "C:\nth-w23\$Phase.json"
    $exit = Invoke-Command -Session $Session -ScriptBlock {
        param($Phase, $Architecture, $Runtime, $Out)
        & pwsh -NoProfile -File 'C:\nth-w23\Test-NthWebGuest.ps1' -Phase $Phase -Architecture $Architecture -Runtime $Runtime -OutFile $Out
        $LASTEXITCODE
    } -ArgumentList $Phase, $Architecture, $Runtime, $remoteJson
    $local = Join-Path $OutDir "$Cell-$Phase.json"
    Copy-Item -FromSession $Session -Path $remoteJson -Destination $local -ErrorAction SilentlyContinue
    return @{ Exit = [int]($exit | Select-Object -Last 1); File = $local }
}

$results = New-Object System.Collections.Generic.List[object]
foreach ($cell in $Cells) {
    $architecture, $runtimeWord = $cell -split '-'
    $runtime = if ($runtimeWord -eq 'runtime') { 'Present' } else { 'Absent' }
    $vm = $VmName[$architecture]
    if (-not $vm) { throw "-VmName has no entry for '$architecture'." }
    $checkpoint = "$CheckpointPrefix-$cell"
    Write-Host "=== $cell : $vm @ $checkpoint ==="
    Restore-VMCheckpoint -VMName $vm -Name $checkpoint -Confirm:$false
    Start-VM -Name $vm
    $session = Wait-Guest -Vm $vm -Cred $Credential
    try {
        Invoke-Command -Session $session -ScriptBlock { New-Item -ItemType Directory -Force -Path 'C:\nth-w23' | Out-Null }
        foreach ($file in $guestScripts) { Copy-Item -ToSession $session -Path $file -Destination 'C:\nth-w23\' }
        Copy-Item -ToSession $session -Path $Installer -Destination 'C:\nth-w23\nth-Setup.exe'

        $results.Add((Invoke-GuestPhase $session 'PreInstall' $architecture $runtime $cell) + @{ Cell = $cell; Phase = 'PreInstall' })

        Invoke-Command -Session $session -ScriptBlock {
            param($InstallArguments)
            $p = Start-Process -FilePath 'C:\nth-w23\nth-Setup.exe' -ArgumentList $InstallArguments -PassThru
            if (-not $p.WaitForExit(15 * 60 * 1000)) { throw 'The installer did not finish in 15 minutes.' }
            Write-Host "installer exit code $($p.ExitCode)"
        } -ArgumentList (, $InstallArguments)
        $results.Add((Invoke-GuestPhase $session 'Installed' $architecture $runtime $cell) + @{ Cell = $cell; Phase = 'Installed' })

        $results.Add((Invoke-GuestPhase $session 'SeedWebData' $architecture $runtime $cell) + @{ Cell = $cell; Phase = 'SeedWebData' })
        Invoke-Command -Session $session -ScriptBlock {
            param($UninstallArguments)
            $root = @((Join-Path $env:LOCALAPPDATA 'Programs\nth'), (Join-Path $env:LOCALAPPDATA 'NthApp')) | Where-Object { Test-Path (Join-Path $_ 'Update.exe') } | Select-Object -First 1
            if (-not $root) { throw 'No Update.exe at either install root; nothing to uninstall.' }
            $p = Start-Process -FilePath (Join-Path $root 'Update.exe') -ArgumentList $UninstallArguments -PassThru
            if (-not $p.WaitForExit(10 * 60 * 1000)) { throw 'The uninstall did not finish in 10 minutes.' }
            Write-Host "uninstall exit code $($p.ExitCode)"
        } -ArgumentList (, $UninstallArguments)
        $results.Add((Invoke-GuestPhase $session 'Uninstalled' $architecture $runtime $cell) + @{ Cell = $cell; Phase = 'Uninstalled' })
    } finally {
        Remove-PSSession $session -ErrorAction SilentlyContinue
        Stop-VM -Name $vm -TurnOff -Force -ErrorAction SilentlyContinue
    }
}

Write-Host ''
Write-Host 'Phase results (exit code = failed checks):'
$results | ForEach-Object { '{0,-16} {1,-12} {2,3}  {3}' -f $_.Cell, $_.Phase, $_.Exit, $_.File }
$failed = ($results | Measure-Object -Property Exit -Sum).Sum
Write-Host "Total failed checks: $failed. Results under $OutDir"
exit ([int]$failed)
