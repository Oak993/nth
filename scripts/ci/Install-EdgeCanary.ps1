<#
.SYNOPSIS
  Web surfaces W23 (DESIGN 8.1 and 14): installs Microsoft Edge Canary, the WebView2 preview channel, on a CI machine for the weekly
  Canary job (.github/workflows/web-canary.yml). Written, not run: the lead runs the job once from the Actions page when the owner says.

.DESCRIPTION
  Microsoft's documentation (Switch to a preview channel, "Option 1: Manually install preview channels") gives one enterprise link for
  Canary. The file it returns is checked for Microsoft's Authenticode signature before anything is run, then installed silently (an
  .exe with /silent /install, an .msi with msiexec /quiet, told apart by the file's first bytes, because the documentation calls the
  link both). The install is proven by finding Edge Canary's folder, and its version is printed and returned in the job output.

  This script is for a disposable CI machine only: it installs a browser. It is never part of the app, the installer or a build.
#>
[CmdletBinding()]
param(
    # The documented Canary download link.
    [string]$Url = 'https://go.microsoft.com/fwlink/?linkid=2084649&Channel=Canary&language=en',
    [int]$TimeoutMinutes = 15
)

$ErrorActionPreference = 'Stop'
$folder = Join-Path ([System.IO.Path]::GetTempPath()) ('edge-canary-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $folder | Out-Null
$file = Join-Path $folder 'EdgeCanarySetup'

Write-Host "Downloading Edge Canary from $Url"
Invoke-WebRequest -Uri $Url -OutFile $file -MaximumRedirection 5 -UseBasicParsing
$bytes = [System.IO.File]::ReadAllBytes($file)
if ($bytes.Length -lt 100KB) { throw "The Canary download is only $($bytes.Length) bytes; that is not an installer." }

$isExe = $bytes[0] -eq 0x4D -and $bytes[1] -eq 0x5A
$isMsi = $bytes[0] -eq 0xD0 -and $bytes[1] -eq 0xCF -and $bytes[2] -eq 0x11 -and $bytes[3] -eq 0xE0
if (-not ($isExe -or $isMsi)) { throw 'The Canary download is neither an executable nor an MSI package.' }
$named = Join-Path $folder ($(if ($isExe) { 'MicrosoftEdgeSetupCanary.exe' } else { 'MicrosoftEdgeCanary.msi' }))
Move-Item -LiteralPath $file -Destination $named

$signature = Get-AuthenticodeSignature -LiteralPath $named
if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch '(^|, )CN=Microsoft Corporation(,|$)') {
    throw "The Canary download is not signed by Microsoft Corporation (status $($signature.Status), signer $($signature.SignerCertificate.Subject)); not running it."
}

Write-Host "Installing $([System.IO.Path]::GetFileName($named)) (signed by Microsoft Corporation)"
$process = if ($isExe) {
    Start-Process -FilePath $named -ArgumentList '/silent', '/install' -PassThru
} else {
    Start-Process -FilePath 'msiexec.exe' -ArgumentList '/i', "`"$named`"", '/quiet', '/norestart' -PassThru
}
if (-not $process.WaitForExit($TimeoutMinutes * 60 * 1000)) { throw "The Canary installer did not finish in $TimeoutMinutes minutes." }
Write-Host "Installer exit code $($process.ExitCode)"

# Canary installs side by side ("Edge SxS"), per user or per machine.
$roots = @(
    (Join-Path $env:LOCALAPPDATA 'Microsoft\Edge SxS\Application'),
    (Join-Path ${env:ProgramFiles(x86)} 'Microsoft\Edge SxS\Application'),
    (Join-Path $env:ProgramFiles 'Microsoft\Edge SxS\Application')
)
$deadline = (Get-Date).AddMinutes(3)
$edge = $null
while (-not $edge -and (Get-Date) -lt $deadline) {
    $edge = $roots | Where-Object { Test-Path -LiteralPath (Join-Path $_ 'msedge.exe') } | Select-Object -First 1
    if (-not $edge) { Start-Sleep -Seconds 5 }
}
if (-not $edge) { throw "Edge Canary was not found under any of: $($roots -join '; ')" }
$version = (Get-Item -LiteralPath (Join-Path $edge 'msedge.exe')).VersionInfo.ProductVersion
Write-Host "Edge Canary $version is installed at $edge"
if ($env:GITHUB_OUTPUT) { "canary-version=$version" | Out-File -FilePath $env:GITHUB_OUTPUT -Append -Encoding utf8 }
Remove-Item -LiteralPath $folder -Recurse -Force -ErrorAction SilentlyContinue
