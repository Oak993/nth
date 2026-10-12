<#
.SYNOPSIS
  Builds the Velopack per-user installer for nth, the Windows port, LOCALLY (OD-0062, ED-235).

.DESCRIPTION
  1. dotnet publish src/Marrow.App, Release, self-contained win-x64, into artifacts/publish. The packaging step renames the apphost
     to nth.exe (PLAN.md override 8), stamps its version resource, and copies the four Visual C++ runtime DLLs the voice natives
     import next to it (override 4, instead of Velopack's vcredist143-x64 prerequisite).
  2. dotnet publish src/Marrow.Setup with Native AOT and no payload, into the publish folder as nth-Setup.exe: the uninstaller the
     package carries (<root>\current\nth-Setup.exe, PLAN.md section 9 item 3).
  3. vpk pack that folder into artifacts/releases: Setup.exe, the full nupkg, RELEASES and releases.win.json (plus a delta nupkg
     when an earlier release is already in that folder). Start shows Programs\nth.lnk (--shortcuts StartMenuRoot, override 3), the
     shortcut's AppUserModelID is the one nth sets on its own process (--aumid), and Velopack's Update.exe is stamped as nth's
     (VELOPACK_CHECK check 12) through vpk's sign template, which is also where signing would run.
  4. Measure T_pkg, T_app and T_disk from the full nupkg (PLAN.md section 4).
  5. dotnet publish src/Marrow.Setup (OD-0072, ED-247) with Native AOT and that Setup.exe embedded (stored, not compressed), with the
     measured sizes, the default root %LOCALAPPDATA%\Programs\nth, the publisher and the release notes as metadata, into
     artifacts/installer/nth-Setup-<version>-UNSIGNED.exe (nth-Setup-<version>.exe once signed): THE file to share. It checks what
     is installed before Velopack's Setup.exe runs: an older version is upgraded in place (an install at %LOCALAPPDATA%\NthApp is
     updated where it is, never copied to the new default), the same version says it is already up to date and runs nothing, a
     newer version refuses and changes nothing, otherwise it installs to %LOCALAPPDATA%\Programs\nth through --installto. The bare
     Setup.exe in artifacts/releases is only its payload: shared on its own it would offer Repair and Downgrade (Velopack's dialog).
  6. Build checks (PLAN.md section 9 item 8), then the last lines say UNSIGNED when nothing was signed.

  Release folder for the feed (UE-1, OD-0100): artifacts/releases is exactly the folder `vpk upload github` takes: the full
  nupkg, the delta nupkg when an earlier release is in it, RELEASES and releases.win.json (which carries the release notes
  with mandatory exact-version markdown from CHANGELOG.md by default; the app shows them), plus Setup.exe. The feed is the owner's public
  repository https://github.com/Oak993/marrow-releases (Marrow.Core.Port.SelfUpdate.MarrowReleaseFeed). The script prints the
  upload command for the owner to run after his word for that publish; it never runs it. A delta needs the earlier release's
  files in artifacts/releases first (vpk download github would fetch them: that is a network call this script does not make).

  LOCAL ONLY (OD-0058, OD-0062). This script never uploads (no vpk upload), never creates a release, never pushes anything and
  never runs the installer it builds: running Setup.exe installs the app onto this PC. It signs only when -Sign is given (decision
  B, the signing route, is the owner's and open; -Sign is his word for that build, with the certificate settings in his
  environment, see scripts/sign-template.ps1). The artifacts/ folder is gitignored; -ArtifactsDir builds somewhere else.

  The version is the port's own version source: the CFBundleShortVersionString assembly metadata in
  src/Marrow.App/AssemblyInfo.cs (CFBundleVersion is the build number, which becomes the fourth part of the file version).

  Identity (OD-0135): the installed app is called "nth" (installer title, Start menu entry, Apps list) and the package id is
  NthApp, not nth: Velopack deletes its whole install folder on uninstall and the data folder is %LOCALAPPDATA%\nth, so the two
  must never be one folder. A fresh install goes to %LOCALAPPDATA%\Programs\nth (--installto, chosen by the setup); an earlier
  install at %LOCALAPPDATA%\NthApp is updated where it is. The shortcut is Start Menu\Programs\nth.lnk. The publisher
  (-PackAuthors, decision A: oak993) is one value: vpk's --packAuthors, the Company of every exe and the setup's copyright.

  Release notes (OD-0111 / UE-6): a ## [version] entry in CHANGELOG.md with populated ### New features, ### Bug fixes or
  ### Refinements. -ReleaseNotesFile can override it with a file containing only that exact version. Missing, empty,
  mismatched or unrecognised notes fail before any commands or outputs. -ValidateReleaseNotesOnly checks this without
  packaging or writing files. scripts/extract-release-notes.ps1 extracts a standalone release notes file.

  -SkipUiSmoke leaves out the one build check that RUNS the payload-free setup (nth-Setup.exe --ui-smoke, PLAN.md section 9 item
  8): it shows the window and runs the real shell-link round trip and the wallpaper read under Native AOT. Use it only where the
  setup must not be started; the build then says so in its last lines.

  Prerequisite: run scripts/restore-dependencies.ps1 to restore the locked dependencies and local vpk tool.
  global.json and scripts/release-toolchain.json pin the SDK, the packaging toolchain and the Visual C++ runtime copies.

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
    [string]$PackAuthors = 'oak993',
    [string]$MainExe = 'nth.exe',
    [string]$ReleaseNotesFile = '',
    [string]$ArtifactsDir = '',
    [ValidateSet('None', 'SignTool', 'Azure')][string]$Sign = 'None',
    [switch]$SkipUiSmoke,
    [switch]$ValidateReleaseNotesOnly
)

$ErrorActionPreference = 'Stop'
# C4b owner decision: package only the Release identity, before paths, commands or outputs are created.
if ($Configuration -cne 'Release') { throw 'Only Release builds can be packaged. Use -Configuration Release.' }
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src/Marrow.App/Marrow.App.csproj'
$icon = Join-Path $root 'src/Marrow.App/Assets/icon.ico'
$artifacts = if ($ArtifactsDir) { [System.IO.Path]::GetFullPath($ArtifactsDir) } else { Join-Path $root 'artifacts' }
$publishDir = Join-Path $artifacts 'publish'
$releasesDir = Join-Path $artifacts 'releases'
$setupProject = Join-Path $root 'src/Marrow.Setup/Marrow.Setup.csproj'
$setupFreeDir = Join-Path $artifacts 'installer-build-free'
$installerBuildDir = Join-Path $artifacts 'installer-build'
$installerDir = Join-Path $artifacts 'installer'
$checkDir = Join-Path $artifacts 'installer-check'
# OD-0100: the update feed (the same repository as Marrow.Core.Port.SelfUpdate.MarrowReleaseFeed.Url; a test holds them equal).
$feedRepoUrl = 'https://github.com/Oak993/marrow-releases'
# Override 3 and VELOPACK_CHECK check 3: Velopack writes this AppUserModelID on Start and on every shortcut into the root at every
# install and update; without --aumid it writes velopack.NthApp. It is the ID nth sets on its own process (ToastBuildIdentity
# .ForBuild(false, false).AppId, ShellLinkKeys.NthAppUserModelId); a test holds them equal.
$aumid = 'nth.App'
# Override 10: where a fresh install goes (the setup passes it to Velopack's Setup.exe as --installto). %25 is MSBuild's escape for %.
$defaultRootText = '%LOCALAPPDATA%\Programs\nth'
$defaultRootMetadata = '%25LOCALAPPDATA%25\Programs\nth'
# PLAN.md section 5: the setup's own code and UI add at most this much to the Velopack Setup.exe it carries.
$setupBudgetBytes = 30MB

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

. (Join-Path $PSScriptRoot 'lib/NthPackaging.ps1')
$copyrightMatch = [regex]::Match($assemblyInfo, 'AssemblyCopyright\("([^"]+)"\)')
if (-not $copyrightMatch.Success) { throw 'AssemblyCopyright not found in src/Marrow.App/AssemblyInfo.cs.' }
$appCopyright = $copyrightMatch.Groups[1].Value
$identity = Get-NthIdentity -Publisher $PackAuthors -Version $version -Build $build -AppCopyright $appCopyright

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

# Override 4: the Visual C++ runtime copies are pinned in scripts/release-toolchain.json. Checked before anything is built: the pinned
# redistributable must be installed and byte for byte the pinned files, and the pin must not be older than the newest Microsoft
# security update noted there. A newer redistributable installed is reported, not an error.
$vcPin = Get-NthVcRuntimePin -ToolchainPath (Join-Path $PSScriptRoot 'release-toolchain.json')
$vcRoots = @(Find-NthVcRedistRoots)
$vcRuntime = Resolve-NthVcRuntime -Pin $vcPin -Roots $vcRoots
$vcRefresh = Test-NthVcRuntimeRefresh -Pin $vcPin -Roots $vcRoots
if ($vcRefresh.BelowSecurityFloor) {
    throw "The pinned Visual C++ runtime $($vcRefresh.Pinned) is older than the newest security update noted in scripts/release-toolchain.json ($($vcRefresh.SecurityFloor)). Refresh the pin (redistVersion, fileVersion and the four hashes) from the newest Visual Studio redistributable."
}
if ($vcRefresh.NewerInstalled) {
    Write-Warning "A newer Visual C++ runtime ($($vcRefresh.Newest), Visual Studio redistributable $($vcRefresh.NewestFolder)) is installed than the pinned $($vcRefresh.Pinned). Windows does not service the app-local copies: refresh the pin in scripts/release-toolchain.json before a release."
}

Write-Host "nth $version (build $build) -> Velopack package $PackId, local only."

if (Test-Path -LiteralPath $publishDir) { Remove-Item -LiteralPath $publishDir -Recurse -Force }
foreach ($stale in @($setupFreeDir, $installerBuildDir, $checkDir)) {
    if (Test-Path -LiteralPath $stale) { Remove-Item -LiteralPath $stale -Recurse -Force }
}
New-Item -ItemType Directory -Force -Path $releasesDir | Out-Null
# Always pack a snapshot of the validated exact-version notes, including for an explicit override.
$ReleaseNotesFile = Join-Path $artifacts "release-notes-$version.md"
[System.IO.File]::WriteAllText($ReleaseNotesFile, $releaseNotes, [System.Text.UTF8Encoding]::new($false))
$notesArgs = @('--releaseNotes', $ReleaseNotesFile)

# 1. The app. The SDK names the apphost after the assembly; the package carries it as nth.exe (the apphost finds its .dll by the name
# embedded in it, so the rename is safe).
Invoke-Native 'dotnet publish' {
    dotnet publish $project -c $Configuration -r $Runtime --self-contained true -o $publishDir --nologo -v q "-p:NthPublisher=$PackAuthors"
}
$builtAppHost = [System.IO.Path]::GetFileNameWithoutExtension($project) + '.exe'
if (-not (Test-Path -LiteralPath (Join-Path $publishDir $builtAppHost))) { throw "$builtAppHost was not published to $publishDir." }
if ($builtAppHost -ne $MainExe) { Move-Item -LiteralPath (Join-Path $publishDir $builtAppHost) -Destination (Join-Path $publishDir $MainExe) -Force }
if (-not (Test-Path -LiteralPath (Join-Path $publishDir $MainExe))) { throw "$MainExe was not published to $publishDir." }
Set-NthVersionResource -Path (Join-Path $publishDir $MainExe) -Fields $identity.App
Copy-NthVcRuntime -Resolved $vcRuntime -Destination $publishDir
Write-Host "$MainExe stamped; the Visual C++ runtime $($vcRuntime.FileVersion) (4 DLLs) sits next to it."

# 2. The payload-free setup: the uninstaller the package carries. Native AOT, so no .NET runtime unpacks when it starts.
# The Native AOT linker lookup runs Visual Studio's vcvarsall.bat, which needs vswhere.exe on PATH; a PowerShell that was not started from a
# developer prompt lacks it, and the failure shows up as a broken link command. Only this process's PATH is changed.
$vsInstaller = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer'
if ((Test-Path -LiteralPath (Join-Path $vsInstaller 'vswhere.exe')) -and ($env:PATH -notlike "*$vsInstaller*")) { $env:PATH = "$vsInstaller;$env:PATH" }
Invoke-Native 'dotnet publish (nth-Setup, no payload)' {
    dotnet publish $setupProject -c $Configuration -r $Runtime -o $setupFreeDir --nologo -v q -p:PublishAot=true "-p:NthPublisher=$PackAuthors"
}
$setupFree = Join-Path $setupFreeDir 'nth-Setup.exe'
if (-not (Test-Path -LiteralPath $setupFree)) { throw "nth-Setup.exe was not published to $setupFreeDir." }
Set-NthVersionResource -Path $setupFree -Fields $identity.Setup
Copy-Item -LiteralPath $setupFree -Destination (Join-Path $publishDir 'nth-Setup.exe') -Force

# 3. vpk. Nothing is uploaded or published. --shortcuts StartMenuRoot (Programs\nth.lnk, override 3), no --framework (override 4), --aumid
# (VELOPACK_CHECK check 3). Velopack's Update.exe is stamped as nth's by the sign template, which vpk runs on the pack folder after
# it has put Update.exe there; with signing off, --signExclude keeps vpk from starting it for any other file.
# OD-0111: every package carries its validated markdown notes in releases.win.json.
$signConfigFile = Join-Path $artifacts 'sign-config.json'
[ordered]@{
    stamps = [ordered]@{ 'Squirrel.exe' = $identity.Updater }
    sign = [ordered]@{ mode = $Sign; description = $PackTitle }
} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $signConfigFile -Encoding utf8
$pwshExe = (Get-Process -Id $PID).Path
$signTemplate = "`"$pwshExe`" -NoLogo -NoProfile -NonInteractive -File `"$(Join-Path $PSScriptRoot 'sign-template.ps1')`" -Config `"$signConfigFile`" {{file...}}"
$signArgs = @('--signTemplate', $signTemplate)
if ($Sign -eq 'None') { $signArgs += @('--signExclude', '^(?!.*[\\/]Squirrel\.exe$)') }
if ($Sign -ne 'None') { Write-Host "SIGNING IS ON ($Sign): this is the owner's word for this build (OD-0058)." }
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
        --aumid $aumid `
        --outputDir $releasesDir `
        @signArgs `
        @notesArgs
}

# PR-SETTINGS-249: the Licenses row reads LICENSE, THIRD_PARTY_NOTICES.md and TRADEMARK.md beside nth.exe, so the package that
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

# 4. PLAN.md section 4: the sizes the setup shows everywhere, measured from the package it installs.
$metrics = Get-NthPackageMetrics -Package $fullPackage.FullName
Write-Host ('Sizes: T_pkg {0:N0}  T_app {1:N0}  T_disk {2:N0} bytes ({3:N0} MB, {4:N0} MiB on disk); Velopack needs {5:N0} free.' -f $metrics.PackageBytes, $metrics.AppBytes, $metrics.DiskBytes, [math]::Round($metrics.DiskBytes / 1000000), [math]::Round($metrics.DiskBytes / 1MB), $metrics.VelopackNeedBytes)

# 5. OD-0072, ED-247: the installer to share is the version gate around Velopack's Setup.exe, given the same title, id and
# version as vpk pack, so its check and the package it installs cannot disagree.
$velopackSetup = Get-ChildItem -LiteralPath $releasesDir -File -Filter "$PackId-*Setup.exe" | Sort-Object LastWriteTime | Select-Object -Last 1
if (-not $velopackSetup) { throw "vpk pack produced no $PackId-*Setup.exe in $releasesDir." }
New-Item -ItemType Directory -Force -Path $installerDir | Out-Null
Invoke-Native 'dotnet publish (Marrow.Setup)' {
    dotnet publish $setupProject -c $Configuration -r $Runtime -o $installerBuildDir --nologo -v q -p:PublishAot=true `
        "-p:InstallerTitle=$PackTitle" "-p:InstallerAppId=$PackId" "-p:InstallerVersion=$version" `
        "-p:NthPublisher=$PackAuthors" "-p:InstallerMainExe=$MainExe" "-p:InstallerAumid=$aumid" "-p:InstallerDefaultRoot=$defaultRootMetadata" `
        "-p:InstallerPackageBytes=$($metrics.PackageBytes)" "-p:InstallerAppBytes=$($metrics.AppBytes)" "-p:InstallerDiskBytes=$($metrics.DiskBytes)" `
        "-p:InstallerVelopackNeedBytes=$($metrics.VelopackNeedBytes)" "-p:InstallerReleaseNotesFile=$ReleaseNotesFile" `
        "-p:VelopackSetupExe=$($velopackSetup.FullName)"
}
$setupShared = Join-Path $installerBuildDir 'nth-Setup.exe'
if (-not (Test-Path -LiteralPath $setupShared)) { throw "nth-Setup.exe was not published to $installerBuildDir." }
Set-NthVersionResource -Path $setupShared -Fields $identity.Setup
if ($Sign -ne 'None') {
    Invoke-Native 'sign the shared setup' { & $pwshExe -NoLogo -NoProfile -NonInteractive -File (Join-Path $PSScriptRoot 'sign-template.ps1') -Config $signConfigFile $setupShared }
}
$signed = $false
if ($Sign -ne 'None') { $signed = (Get-NthSignatureState -Path $setupShared).Signed }
$installer = Join-Path $installerDir (Get-NthSetupFileName -Version $version -Signed $signed)
Copy-Item -LiteralPath $setupShared -Destination $installer -Force

# 6. PLAN.md section 9 item 8: every check stops the build, and nothing prints "Built" before they pass.
Write-Host ''
Write-Host 'Build checks:'
if ($SkipUiSmoke) {
    Write-Host '  --  the UI smoke test of the payload-free setup was SKIPPED (-SkipUiSmoke)'
} else {
    Invoke-NthCheck 'the payload-free setup passes --ui-smoke (window, real shell-link round trip, wallpaper read, under Native AOT)' {
        Invoke-NthUiSmoke -StartProcess { Start-NthUiSmokeProcess -FilePath $setupFree -ArgumentList @('--ui-smoke') }
    }
}
$packaged = Expand-NthPackageEntries -Package $fullPackage.FullName -Destination $checkDir -Names @(
    "lib/app/$MainExe", 'lib/app/nth-Setup.exe', 'lib/app/Squirrel.exe')
$stubName = @($metrics.Entries | Where-Object { $_ -match '^lib/app/[^/]+_ExecutionStub\.exe$' })[0]
$null = Expand-NthPackageEntries -Package $fullPackage.FullName -Destination $checkDir -Names @($stubName)
Invoke-NthCheck 'every exe is a GUI-subsystem exe (no console window)' {
    foreach ($exe in @((Join-Path $publishDir $MainExe), $setupFree, $setupShared, $velopackSetup.FullName,
            (Join-Path $checkDir 'lib/app/Squirrel.exe'), (Join-Path $checkDir $stubName), (Join-Path $checkDir 'lib/app/nth-Setup.exe'))) {
        Assert-NthGuiExe -Path $exe
    }
}
Invoke-NthCheck 'version resources match PLAN.md section 7 (nth.exe, execution stub, nth-Setup, nth Updater, Velopack Setup)' {
    Assert-NthVersionResource -Path (Join-Path $publishDir $MainExe) -Expected $identity.App -CheckLanguage
    Assert-NthVersionResource -Path (Join-Path $checkDir "lib/app/$MainExe") -Expected $identity.App -CheckLanguage
    Assert-NthVersionResource -Path (Join-Path $checkDir $stubName) -Expected $identity.App -CheckLanguage
    Assert-NthVersionResource -Path $setupFree -Expected $identity.Setup -CheckLanguage
    Assert-NthVersionResource -Path $setupShared -Expected $identity.Setup -CheckLanguage
    Assert-NthVersionResource -Path (Join-Path $checkDir 'lib/app/nth-Setup.exe') -Expected $identity.Setup -CheckLanguage
    Assert-NthVersionResource -Path (Join-Path $checkDir 'lib/app/Squirrel.exe') -Expected $identity.Updater -CheckLanguage
    Assert-NthVersionResource -Path $velopackSetup.FullName -Expected $identity.VelopackSetup
    foreach ($exe in @((Join-Path $checkDir 'lib/app/Squirrel.exe'), $velopackSetup.FullName)) {
        $text = (Get-NthVersionInfo -Path $exe)
        if (($text.FileDescription, $text.ProductName, $text.CompanyName -join ' ') -match 'Velopack') { throw "$exe still says Velopack in its description, product or company." }
    }
}
Invoke-NthCheck 'the icon holds all ten sizes' { Assert-NthIconSizes -Path $icon }
Invoke-NthCheck 'no user-visible "Marrow" in the setup exes' {
    # What a user can see is UTF-16: string literals, window titles, the version resource. Native AOT's type metadata is UTF-8 and keeps every
    # namespace segment (the Marrow of Marrow.Core) as a name of its own, which no one sees, so it is not read.
    foreach ($exe in @($setupFree, $setupShared)) {
        $found = @(Find-NthWordInBinary -Path $exe -Word 'Marrow' -Encoding Utf16)
        if ($found) { throw "$exe holds the word: $($found | Select-Object -First 5 | ForEach-Object { "[$_]" })" }
    }
}
Invoke-NthCheck "no reference to the old exe name is left (repository, publish folder, package)" {
    $hits = @(Find-NthOldExeNameReferences -Root $root)
    if ($hits) { throw "the repository still names the old exe in: $($hits -join ', ')" }
    if ($builtAppHost -ne $MainExe) {
        if (Test-Path -LiteralPath (Join-Path $publishDir $builtAppHost)) { throw "$builtAppHost is still in the publish folder." }
        if ($metrics.Entries -contains "lib/app/$builtAppHost") { throw "$($fullPackage.Name) holds lib/app/$builtAppHost." }
    }
}
Invoke-NthCheck 'the Visual C++ runtime sits next to nth.exe, in the publish folder and in the package, as pinned' {
    Assert-NthVcRuntimeBeside -Pin $vcPin -Folder $publishDir
    foreach ($file in $vcPin.files) {
        if ($metrics.Entries -cnotcontains "lib/app/$($file.name)") { throw "$($fullPackage.Name) holds no lib/app/$($file.name)." }
    }
}
Invoke-NthCheck 'the package asks for no framework prerequisite, starts Start at Programs\nth.lnk and sets the app AppUserModelID' {
    $nuspec = (Get-NthNuspec -Package $fullPackage.FullName).package.metadata
    if ("$($nuspec.runtimeDependencies)".Trim() -ne '') { throw "runtimeDependencies is '$($nuspec.runtimeDependencies)', not empty (a framework prerequisite would download and run an installer)." }
    if ("$($nuspec.shortcutLocations)" -ne 'StartMenuRoot') { throw "shortcutLocations is '$($nuspec.shortcutLocations)', not StartMenuRoot." }
    if ("$($nuspec.shortcutAumid)" -ne $aumid) { throw "shortcutAumid is '$($nuspec.shortcutAumid)', not $aumid." }
    if ("$($nuspec.mainExe)" -ne $MainExe) { throw "mainExe is '$($nuspec.mainExe)', not $MainExe." }
    if ("$($nuspec.authors)" -ne $PackAuthors) { throw "authors is '$($nuspec.authors)', not $PackAuthors." }
}
Invoke-NthCheck 'the shared file adds at most 30 MiB to the Velopack Setup.exe it carries' {
    $extra = (Get-Item -LiteralPath $installer).Length - $velopackSetup.Length
    if ($extra -gt $setupBudgetBytes) { throw ('the setup adds {0:N1} MiB, over the {1} MiB budget.' -f ($extra / 1MB), ($setupBudgetBytes / 1MB)) }
    if ($extra -lt 0) { throw 'the shared file is smaller than the Setup.exe it should carry.' }
    Write-Host ('      the setup adds {0:N1} MiB to the {1:N1} MiB Velopack Setup.exe' -f ($extra / 1MB), ($velopackSetup.Length / 1MB))
}
if ($Sign -ne 'None') {
    Invoke-NthCheck 'every exe is signed with a valid, timestamped signature' {
        foreach ($exe in @($setupShared, (Join-Path $checkDir "lib/app/$MainExe"), (Join-Path $checkDir 'lib/app/nth-Setup.exe'), (Join-Path $checkDir 'lib/app/Squirrel.exe'),
                (Join-Path $checkDir $stubName), $velopackSetup.FullName)) {
            $state = Get-NthSignatureState -Path $exe
            if (-not $state.Signed -or -not $state.Timestamped) { throw "$exe is $($state.Status)$(if (-not $state.Timestamped) { ', not timestamped' })." }
        }
    }
}

Write-Host ''
Write-Host "Built (not published, not installed) in $releasesDir :"
Get-ChildItem -LiteralPath $releasesDir -File | ForEach-Object { '{0,14:N0}  {1}' -f $_.Length, $_.Name }
Write-Host ''
Write-Host "The installer to share (checks the installed version first; a fresh install goes to $defaultRootText; never run it on this PC):"
Get-ChildItem -LiteralPath $installer | ForEach-Object { '{0,14:N0}  {1}' -f $_.Length, $_.FullName }
Write-Host ''
Write-Host "The feed folder is $releasesDir. NOTHING WAS UPLOADED. When the owner says to publish this version, he runs (not this script):"
Write-Host "  vpk upload github --outputDir `"$releasesDir`" --repoUrl $feedRepoUrl --releaseName `"nth $version`" --tag v$version --publish --token <a GitHub token for $feedRepoUrl>"
Write-Host ''
if ($SkipUiSmoke) { Write-Host 'NOT FULLY VERIFIED: the payload-free setup was not started, so its UI smoke test did not run.' }
if ($Sign -eq 'None') {
    Write-Host (Get-NthUnsignedMessage)
} else {
    Write-Host "Signed ($Sign) and verified: every exe has a valid, timestamped signature."
}
