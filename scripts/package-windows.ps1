<#
.SYNOPSIS
  Builds the Velopack per-user installer for the Marrow Windows port, LOCALLY (OD-0062, ED-235).

.DESCRIPTION
  1. dotnet publish src/Marrow.App, Release, self-contained win-x64, into artifacts/publish.
  2. vpk pack that folder into artifacts/releases: Setup.exe, the full nupkg, RELEASES and releases.win.json
     (plus a delta nupkg when an earlier release is already in that folder).
  3. dotnet publish src/Marrow.Setup (OD-0072, ED-247) with that Setup.exe embedded, into
     artifacts/installer/Marrow-<version>-Setup.exe: THE file to share. It checks what is installed before Velopack's
     Setup.exe runs: an older version is upgraded in place, the same version says it is already up to date and runs
     nothing, a newer version refuses ("A newer version of Marrow (x.y.z) is already installed.") and changes nothing,
     otherwise it installs. The bare Setup.exe in artifacts/releases is only its payload: shared on its own it would
     offer Repair and Downgrade (Velopack's own dialog).

  LOCAL ONLY (OD-0058, OD-0062). This script never signs (no --signParams), never uploads (no vpk upload), never
  creates a release and never pushes anything. It also never runs the installer it builds: running Setup.exe installs the
  app onto this PC. The artifacts/ folder is gitignored.

  The version is the port's own version source: the CFBundleShortVersionString assembly metadata in
  src/Marrow.App/AssemblyInfo.cs (1.7.0; CFBundleVersion is the build number, 21, which Velopack has no field for).

  Identity (OD-0072, ED-247; amends ED-235's title): the installed app is called "Marrow" (installer title, Start menu
  entry, Apps list). The install identity stays MarrowCode: the app installs to %LOCALAPPDATA%\MarrowCode, beside the data
  folder %LOCALAPPDATA%\Marrow Code, and registers as MarrowCode. Its shortcut is Start Menu\Programs\Marrow Code\Marrow.lnk
  (--shortcuts StartMenu puts it in a folder named after --packAuthors): the owner's separate Electron app "Marrow" owns
  Start Menu\Programs\Marrow.lnk, which StartMenuRoot with the title "Marrow" would overwrite and later delete. Neither app
  can replace the other.

  Prerequisite: the vpk tool at the same version as the Velopack package in Directory.Packages.props:
    dotnet tool install -g vpk --version <that version>

.EXAMPLE
  pwsh scripts/package-windows.ps1
  powershell -NoProfile -ExecutionPolicy Bypass -File scripts/package-windows.ps1   (this PC blocks unsigned scripts by policy; Bypass applies to this one process only)
#>
[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$Runtime = 'win-x64',
    [string]$PackId = 'MarrowCode',
    [string]$PackTitle = 'Marrow',
    [string]$PackAuthors = 'Marrow Code',
    [string]$MainExe = 'Marrow.App.exe'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src/Marrow.App/Marrow.App.csproj'
$icon = Join-Path $root 'src/Marrow.App/Assets/icon.ico'
$publishDir = Join-Path $root 'artifacts/publish'
$releasesDir = Join-Path $root 'artifacts/releases'
$setupProject = Join-Path $root 'src/Marrow.Setup/Marrow.Setup.csproj'
$installerBuildDir = Join-Path $root 'artifacts/installer-build'
$installerDir = Join-Path $root 'artifacts/installer'

function Invoke-Native([string]$Name, [scriptblock]$Command) {
    & $Command
    if ($LASTEXITCODE -ne 0) { throw "$Name failed with exit code $LASTEXITCODE." }
}

# The version source: the assembly metadata the app itself reads for AppInfo.Version (Marrow.Core.Support.AppInfo).
$assemblyInfo = Get-Content -LiteralPath (Join-Path $root 'src/Marrow.App/AssemblyInfo.cs') -Raw
$versionMatch = [regex]::Match($assemblyInfo, 'AssemblyMetadata\("CFBundleShortVersionString",\s*"([^"]+)"\)')
if (-not $versionMatch.Success) { throw 'CFBundleShortVersionString not found in src/Marrow.App/AssemblyInfo.cs.' }
$version = $versionMatch.Groups[1].Value
$buildMatch = [regex]::Match($assemblyInfo, 'AssemblyMetadata\("CFBundleVersion",\s*"([^"]+)"\)')
$build = if ($buildMatch.Success) { $buildMatch.Groups[1].Value } else { '?' }

# vpk must match the Velopack library the app was built with (Directory.Packages.props).
$packages = Get-Content -LiteralPath (Join-Path $root 'Directory.Packages.props') -Raw
$libraryMatch = [regex]::Match($packages, '<PackageVersion Include="Velopack" Version="([^"]+)"')
if (-not $libraryMatch.Success) { throw 'The Velopack package is not pinned in Directory.Packages.props.' }
$libraryVersion = $libraryMatch.Groups[1].Value
if (-not (Get-Command vpk -ErrorAction SilentlyContinue)) {
    throw "vpk is not installed. Run: dotnet tool install -g vpk --version $libraryVersion"
}
$toolVersion = ((& vpk --help | Select-Object -First 3) -join ' ')
if ($toolVersion -notmatch [regex]::Escape($libraryVersion)) {
    throw "vpk does not report version $libraryVersion (it says: $toolVersion). Run: dotnet tool update -g vpk --version $libraryVersion"
}

Write-Host "Marrow $version (build $build) -> Velopack package $PackId, local only."

if (Test-Path -LiteralPath $publishDir) { Remove-Item -LiteralPath $publishDir -Recurse -Force }
New-Item -ItemType Directory -Force -Path $releasesDir | Out-Null

Invoke-Native 'dotnet publish' {
    dotnet publish $project -c $Configuration -r $Runtime --self-contained true -o $publishDir --nologo -v q
}
if (-not (Test-Path -LiteralPath (Join-Path $publishDir $MainExe))) { throw "$MainExe was not published to $publishDir." }

# No --signParams (nothing is signed), no vpk upload/publish. --shortcuts: the Start menu entry only, in the
# "Marrow Code" folder (ED-247: never Programs\Marrow.lnk, the Electron app's).
Invoke-Native 'vpk pack' {
    vpk pack `
        --packId $PackId `
        --packVersion $version `
        --packDir $publishDir `
        --mainExe $MainExe `
        --packTitle $PackTitle `
        --packAuthors $PackAuthors `
        --icon $icon `
        --runtime $Runtime `
        --shortcuts StartMenu `
        --outputDir $releasesDir
}

# OD-0072, ED-247: the installer to share is the version gate around Velopack's Setup.exe, given the same title, id and
# version as vpk pack, so its check and the package it installs cannot disagree.
$velopackSetup = Get-ChildItem -LiteralPath $releasesDir -File -Filter "$PackId-*Setup.exe" | Sort-Object LastWriteTime | Select-Object -Last 1
if (-not $velopackSetup) { throw "vpk pack produced no $PackId-*Setup.exe in $releasesDir." }
if (Test-Path -LiteralPath $installerBuildDir) { Remove-Item -LiteralPath $installerBuildDir -Recurse -Force }
New-Item -ItemType Directory -Force -Path $installerDir | Out-Null
Invoke-Native 'dotnet publish (Marrow.Setup)' {
    dotnet publish $setupProject -c $Configuration -r $Runtime --self-contained true -o $installerBuildDir --nologo -v q `
        -p:PublishSingleFile=true -p:PublishTrimmed=true -p:EnableCompressionInSingleFile=true `
        "-p:InstallerTitle=$PackTitle" "-p:InstallerAppId=$PackId" "-p:InstallerVersion=$version" `
        "-p:VelopackSetupExe=$($velopackSetup.FullName)"
}
$installer = Join-Path $installerDir "$PackTitle-$version-Setup.exe"
Copy-Item -LiteralPath (Join-Path $installerBuildDir 'Marrow.Setup.exe') -Destination $installer -Force

Write-Host ''
Write-Host "Built (not signed, not published, not installed) in $releasesDir :"
Get-ChildItem -LiteralPath $releasesDir -File | ForEach-Object { '{0,14:N0}  {1}' -f $_.Length, $_.Name }
Write-Host ''
Write-Host "The installer to share (checks the installed version first; never run it on this PC):"
Get-ChildItem -LiteralPath $installer | ForEach-Object { '{0,14:N0}  {1}' -f $_.Length, $_.FullName }
