<#
.SYNOPSIS
  Builds the Velopack per-user installer for the Marrow Windows port, LOCALLY (OD-0062, ED-235).

.DESCRIPTION
  1. dotnet publish src/Marrow.App, Release, self-contained win-x64, into artifacts/publish.
  2. vpk pack that folder into artifacts/releases: Setup.exe, the full nupkg, RELEASES and releases.win.json
     (plus a delta nupkg when an earlier release is already in that folder).

  LOCAL ONLY (OD-0058, OD-0062). This script never signs (no --signParams), never uploads (no vpk upload), never
  creates a release and never pushes anything. It also never runs the installer it builds: running Setup.exe installs the
  app onto this PC. The artifacts/ folder is gitignored.

  The version is the port's own version source: the CFBundleShortVersionString assembly metadata in
  src/Marrow.App/AssemblyInfo.cs (1.7.0; CFBundleVersion is the build number, 21, which Velopack has no field for).

  Identity (OD-0008 C7): the Velopack app id is MarrowCode, so the app installs to %LOCALAPPDATA%\MarrowCode and its
  Start menu shortcut is "Marrow Code", the same name as the data folder %LOCALAPPDATA%\Marrow Code. The owner's separate
  Electron app "Marrow" is never touched.

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
    [string]$PackTitle = 'Marrow Code',
    [string]$PackAuthors = 'Marrow',
    [string]$MainExe = 'Marrow.App.exe'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src/Marrow.App/Marrow.App.csproj'
$icon = Join-Path $root 'src/Marrow.App/Assets/icon.ico'
$publishDir = Join-Path $root 'artifacts/publish'
$releasesDir = Join-Path $root 'artifacts/releases'

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

# No --signParams (nothing is signed), no vpk upload/publish. --shortcuts: the Start menu entry only (OD-0008 C7).
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
        --shortcuts StartMenuRoot `
        --outputDir $releasesDir
}

Write-Host ''
Write-Host "Built (not signed, not published, not installed) in $releasesDir :"
Get-ChildItem -LiteralPath $releasesDir -File | ForEach-Object { '{0,14:N0}  {1}' -f $_.Length, $_.Name }
