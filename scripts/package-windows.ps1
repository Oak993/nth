<#
.SYNOPSIS
  Builds the Velopack per-user installer for nth, the Windows port, LOCALLY (OD-0062, ED-235).

.DESCRIPTION
  1. dotnet publish src/Marrow.App, Release, self-contained win-x64, into artifacts/publish.
  2. vpk pack that folder into artifacts/releases: Setup.exe, the full nupkg, RELEASES and releases.win.json
     (plus a delta nupkg when an earlier release is already in that folder).
  3. dotnet publish src/Marrow.Setup (OD-0072, ED-247) with that Setup.exe embedded, into
     artifacts/installer/nth-<version>-Setup.exe: THE file to share. It checks what is installed before Velopack's
     Setup.exe runs: an older version is upgraded in place, the same version says it is already up to date and runs
     nothing, a newer version refuses ("A newer version of nth (x.y.z) is already installed.") and changes nothing,
     otherwise it installs. The bare Setup.exe in artifacts/releases is only its payload: shared on its own it would
     offer Repair and Downgrade (Velopack's own dialog).

  Release folder for the feed (UE-1, OD-0100): artifacts/releases is exactly the folder `vpk upload github` takes: the full
  nupkg, the delta nupkg when an earlier release is in it, RELEASES and releases.win.json (which carries the release notes
  with mandatory exact-version markdown from CHANGELOG.md by default; the app shows them), plus Setup.exe. The feed is the owner's public
  repository https://github.com/Oak993/marrow-releases (Marrow.Core.Port.SelfUpdate.MarrowReleaseFeed). The script prints the
  upload command for the owner to run after his word for that publish; it never runs it. A delta needs the earlier release's
  files in artifacts/releases first (vpk download github would fetch them: that is a network call this script does not make).

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

  OD-0135 renames the product to nth: the installed app is called "nth" (installer title, Start menu entry, Apps list)
  and installs fresh under the package id NthApp, to %LOCALAPPDATA%\NthApp; an earlier MarrowCode install is left alone.
  The id is NthApp and not nth because Velopack deletes its whole install folder on uninstall and the data folder is
  %LOCALAPPDATA%\nth: the two must never be one folder. The shortcut is Start Menu\Programs\nth\nth.lnk (--packAuthors
  nth). The executable keeps its name Marrow.App.exe for now (AssemblyName unchanged).

  Release notes (OD-0111 / UE-6): a ## [version] entry in CHANGELOG.md with populated ### New features, ### Bug fixes or
  ### Refinements. -ReleaseNotesFile can override it with a file containing only that exact version. Missing, empty,
  mismatched or unrecognised notes fail before any commands or outputs. -ValidateReleaseNotesOnly checks this without
  packaging or writing files. scripts/extract-release-notes.ps1 extracts a standalone release notes file.

  Prerequisite: run scripts/restore-dependencies.ps1 to restore the locked dependencies and local vpk tool.
  global.json and scripts/release-toolchain.json pin the SDK and packaging toolchain.

.EXAMPLE
  pwsh scripts/package-windows.ps1
  pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/package-windows.ps1   (pinned PowerShell 7; Bypass applies to this one process only)
#>
[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$Runtime = 'win-x64',
    [string]$PackId = 'NthApp',
    [string]$PackTitle = 'nth',
    [string]$PackAuthors = 'nth',
    [string]$MainExe = 'Marrow.App.exe',
    [string]$ReleaseNotesFile = '',
    [switch]$ValidateReleaseNotesOnly
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
# OD-0100: the update feed (the same repository as Marrow.Core.Port.SelfUpdate.MarrowReleaseFeed.Url; a test holds them equal).
$feedRepoUrl = 'https://github.com/Oak993/marrow-releases'

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

# OD-0111: validate before any native command, deletion or artifact creation. An explicit file must identify this version;
# otherwise extract it from the repository CHANGELOG. The read-only mode executes the same guard for tests and release preparation.
. (Join-Path $PSScriptRoot 'release-notes.ps1')
$releaseNotes = Get-ReleaseNotes -Version $version -ChangelogFile (Join-Path $root 'CHANGELOG.md') -ReleaseNotesFile $ReleaseNotesFile
if ($ValidateReleaseNotesOnly) {
    Write-Host "Release notes validated for nth $version. No artifacts produced."
    return
}

# vpk must match the Velopack library the app was built with (Directory.Packages.props).
& (Join-Path $PSScriptRoot 'verify-toolchain.ps1') -PackagingOnly
function vpk {
    Push-Location $root
    try { & dotnet tool run vpk -- @args } finally { Pop-Location }
}
$packages = Get-Content -LiteralPath (Join-Path $root 'Directory.Packages.props') -Raw
$libraryMatch = [regex]::Match($packages, '<PackageVersion Include="Velopack" Version="([^"]+)"')
if (-not $libraryMatch.Success) { throw 'The Velopack package is not pinned in Directory.Packages.props.' }
$libraryVersion = $libraryMatch.Groups[1].Value
if (-not (Get-Command vpk -ErrorAction SilentlyContinue)) {
    throw "vpk is not installed. Run: dotnet tool restore"
}
$toolVersion = ((& vpk --help | Select-Object -First 3) -join ' ')
if ($toolVersion -notmatch [regex]::Escape($libraryVersion)) {
    throw "vpk does not report version $libraryVersion (it says: $toolVersion). Restore the pinned local tool."
}

Write-Host "nth $version (build $build) -> Velopack package $PackId, local only."

if (Test-Path -LiteralPath $publishDir) { Remove-Item -LiteralPath $publishDir -Recurse -Force }
New-Item -ItemType Directory -Force -Path $releasesDir | Out-Null
# Always pack a snapshot of the validated exact-version notes, including for an explicit override.
$ReleaseNotesFile = Join-Path $root "artifacts/release-notes-$version.md"
[System.IO.File]::WriteAllText($ReleaseNotesFile, $releaseNotes, [System.Text.UTF8Encoding]::new($false))
$notesArgs = @('--releaseNotes', $ReleaseNotesFile)

Invoke-Native 'dotnet publish' {
    dotnet publish $project -c $Configuration -r $Runtime --self-contained true -o $publishDir --nologo -v q
}
if (-not (Test-Path -LiteralPath (Join-Path $publishDir $MainExe))) { throw "$MainExe was not published to $publishDir." }

# RR-14 (ONDEMAND-PLAN.md 3.6, ED-NEW-dl-10): every addons\bin shim is a copy of one NativeAOT launcher, shipped beside the app.
$shimProject = Join-Path $root 'src/Marrow.Shim/Marrow.Shim.csproj'
$shimBuildDir = Join-Path $root 'artifacts/shim-build'
if (Test-Path -LiteralPath $shimBuildDir) { Remove-Item -LiteralPath $shimBuildDir -Recurse -Force }
# NativeAOT finds the C++ linker through vswhere and vcvarsall, which need the "(x86)" folder variables; a shell such as Git Bash drops
# variables whose names hold parentheses, so restore them for this process (and its children) only. No global setting changes.
if (-not ${env:ProgramFiles(x86)}) { ${env:ProgramFiles(x86)} = [Environment]::GetFolderPath('ProgramFilesX86') }
if (-not ${env:CommonProgramFiles(x86)}) { ${env:CommonProgramFiles(x86)} = [Environment]::GetFolderPath('CommonProgramFilesX86') }
$vsInstaller = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer'
if ((Test-Path -LiteralPath (Join-Path $vsInstaller 'vswhere.exe')) -and ($env:Path -notlike "*$vsInstaller*")) { $env:Path = "$vsInstaller;$env:Path" }
Invoke-Native 'dotnet publish (Marrow.Shim)' {
    dotnet publish $shimProject -c $Configuration -r $Runtime -o $shimBuildDir --nologo -v q
}
$shimExe = Join-Path $shimBuildDir 'Marrow.Shim.exe'
if (-not (Test-Path -LiteralPath $shimExe)) { throw "Marrow.Shim.exe was not published to $shimBuildDir." }
Copy-Item -LiteralPath $shimExe -Destination (Join-Path $publishDir 'Marrow.Shim.exe')

# No --signParams (nothing is signed), no vpk upload/publish. --shortcuts: the Start menu entry only, in the
# "nth" folder, named after --packAuthors (ED-247: never Programs\Marrow.lnk, the Electron app's).
# OD-0111: every package carries its validated markdown notes in releases.win.json.
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
        --framework vcredist143-x64 `
        --shortcuts StartMenu `
        --outputDir $releasesDir `
        @notesArgs
}

# PR-SETTINGS-249: the Licenses row reads LICENSE, THIRD_PARTY_NOTICES.md and TRADEMARK.md beside Marrow.App.exe, so the package that
# Velopack installs (and updates) from must hold the three at the app root, byte for byte what was published. A package without them
# stops here, before any installer is built from it. (tests/Marrow.App.Tests/Views/Settings/LicensesPackTests.cs checks the same package.)
$fullPackage = Get-ChildItem -LiteralPath $releasesDir -File -Filter "$PackId-*-full.nupkg" | Sort-Object LastWriteTime | Select-Object -Last 1
if (-not $fullPackage) { throw "vpk pack produced no $PackId-*-full.nupkg in $releasesDir." }
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [System.IO.Compression.ZipFile]::OpenRead($fullPackage.FullName)
try {
    if (-not $archive.GetEntry("lib/app/$MainExe")) { throw "$($fullPackage.Name) holds no lib/app/$MainExe." }
    foreach ($document in @('LICENSE', 'THIRD_PARTY_NOTICES.md', 'TRADEMARK.md')) {
        $entry = $archive.GetEntry("lib/app/$document")
        if (-not $entry) { throw "$($fullPackage.Name) does not hold $document beside $MainExe (lib/app/$document)." }
        $stream = $entry.Open()
        try {
            $memory = New-Object System.IO.MemoryStream
            $stream.CopyTo($memory)
            $packed = [System.BitConverter]::ToString([System.Security.Cryptography.SHA256]::Create().ComputeHash($memory.ToArray()))
        } finally { $stream.Dispose() }
        $published = [System.BitConverter]::ToString([System.Security.Cryptography.SHA256]::Create().ComputeHash([System.IO.File]::ReadAllBytes((Join-Path $publishDir $document))))
        if ($packed -ne $published) { throw "$document in $($fullPackage.Name) is not the published file." }
    }
} finally { $archive.Dispose() }
Write-Host "$($fullPackage.Name) holds LICENSE, THIRD_PARTY_NOTICES.md and TRADEMARK.md beside $MainExe, byte for byte as published."

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
Write-Host ''
Write-Host "The feed folder is $releasesDir. NOTHING WAS UPLOADED. When the owner says to publish this version, he runs (not this script):"
Write-Host "  vpk upload github --outputDir `"$releasesDir`" --repoUrl $feedRepoUrl --releaseName `"nth $version`" --tag v$version --publish --token <a GitHub token for $feedRepoUrl>"
