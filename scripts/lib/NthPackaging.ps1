# nth installer PLAN.md sections 7, 9 and 11, VELOPACK_CHECK.md checks 12 and 13, unit U7 (ED-NEW-inst-u7-3 to ED-NEW-inst-u7-9).
# The functions behind scripts/package-windows.ps1 and scripts/sign-template.ps1. They are dot-sourced, so the tests (which run
# them in a fresh pwsh over fixtures in a temp folder) and the packaging script use exactly the same code. Nothing here uploads,
# publishes, runs an installer or touches the real Desktop, Start menu, HKCU or %LOCALAPPDATA%\nth (OD-0058).
$script:NthLibDir = $PSScriptRoot

# The old exe name is written in two halves on purpose: a test greps the repository for it and fails on any hit outside the decision
# ledgers (PLAN.md section 7), and this file must not be one.
$script:NthOldExeName = 'Marrow.App' + '.exe'

function Initialize-NthBuildTypes {
    foreach ($source in @('Win32VersionStamp.cs', 'BinaryTextScan.cs')) {
        $name = if ($source -eq 'Win32VersionStamp.cs') { 'Nth.Build.Win32VersionStamp' } else { 'Nth.Build.BinaryTextScan' }
        if (-not ($name -as [type])) { Add-Type -Path (Join-Path $script:NthLibDir $source) -ErrorAction Stop }
    }
}

function Invoke-NthCheck {
    param([Parameter(Mandatory)][string]$Name, [Parameter(Mandatory)][scriptblock]$Check)
    try { & $Check } catch { throw "Build check failed: $Name. $($_.Exception.Message)" }
    Write-Host "  ok  $Name"
}

# C4b: this local build-tool job owns the smoke process and its descendants, even after the parent exits.
function Start-NthUiSmokeProcess {
    param([Parameter(Mandatory)][string]$FilePath, [string[]]$ArgumentList = @())
    if (-not ('Nth.Build.SmokeProcess' -as [type])) {
        Add-Type -Path (Join-Path $script:NthLibDir 'SmokeProcess.cs') -ErrorAction Stop
    }
    [Nth.Build.SmokeProcess]::Start($FilePath, $ArgumentList)
}

function Invoke-NthUiSmoke {
    param(
        [Parameter(Mandatory)][scriptblock]$StartProcess,
        [ValidateRange(1, 2147483647)][int]$TimeoutMilliseconds = 30000,
        [ValidateRange(1, 2147483647)][int]$StopTimeoutMilliseconds = 5000
    )
    $owned = $null
    $failure = $null
    try {
        $owned = & $StartProcess
        if ($null -eq $owned) { throw 'The UI smoke process could not be started.' }
        if (-not $owned.WaitForExit($TimeoutMilliseconds)) {
            throw "nth-Setup.exe --ui-smoke timed out after $TimeoutMilliseconds ms."
        }
        if ($owned.ExitCode -ne 0) { throw "nth-Setup.exe --ui-smoke exited with code $($owned.ExitCode)." }
    } catch {
        $failure = $_
        throw
    } finally {
        if ($null -ne $owned) {
            $cleanupErrors = [Collections.Generic.List[Exception]]::new()
            try { $owned.Stop($StopTimeoutMilliseconds) } catch { $cleanupErrors.Add($_.Exception) }
            try { $owned.Dispose() } catch { $cleanupErrors.Add($_.Exception) }
            if ($cleanupErrors.Count -gt 0) {
                if ($null -ne $failure) { $cleanupErrors.Insert(0, $failure.Exception) }
                throw [AggregateException]::new('The UI smoke run or its cleanup failed.', $cleanupErrors.ToArray())
            }
        }
    }
}

# ---- PE files --------------------------------------------------------------------------------------------------------------

function Get-NthPeInfo {
    param([Parameter(Mandatory)][string]$Path)
    $full = (Resolve-Path -LiteralPath $Path).Path
    $stream = [IO.File]::Open($full, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite)
    try {
        $reader = [IO.BinaryReader]::new($stream)
        if ($stream.Length -lt 64 -or $reader.ReadUInt16() -ne 0x5A4D) { throw "$full is not a PE file (no MZ header)." }
        $stream.Position = 0x3C
        $peOffset = $reader.ReadUInt32()
        $stream.Position = $peOffset
        if ($reader.ReadUInt32() -ne 0x00004550) { throw "$full is not a PE file (no PE signature)." }
        $machine = $reader.ReadUInt16()
        $stream.Position = $peOffset + 24
        $magic = $reader.ReadUInt16()
        $stream.Position = $peOffset + 24 + 68
        $subsystem = $reader.ReadUInt16()
        [pscustomobject]@{
            Path = $full; Machine = $machine; IsX64 = ($machine -eq 0x8664); Pe32Plus = ($magic -eq 0x20B)
            Subsystem = $subsystem; IsGui = ($subsystem -eq 2); IsConsole = ($subsystem -eq 3)
        }
    } finally { $stream.Dispose() }
}

# PLAN.md section 6: the setup exe, nth.exe, Velopack's Setup.exe and Update.exe must all be GUI-subsystem exes (no console window).
function Assert-NthGuiExe {
    param([Parameter(Mandatory)][string]$Path)
    $info = Get-NthPeInfo -Path $Path
    if (-not $info.IsX64) { throw "$Path is not an x64 PE file." }
    if (-not $info.IsGui) { throw "$Path is not a GUI-subsystem exe (subsystem $($info.Subsystem)); it would open a console window." }
}

# ---- version resources -----------------------------------------------------------------------------------------------------

function Get-NthVersionInfo {
    param([Parameter(Mandatory)][string]$Path)
    $v = [Diagnostics.FileVersionInfo]::GetVersionInfo((Resolve-Path -LiteralPath $Path).Path)
    [pscustomobject]@{
        FileDescription = $v.FileDescription; ProductName = $v.ProductName; CompanyName = $v.CompanyName
        FileVersion = $v.FileVersion; ProductVersion = $v.ProductVersion; LegalCopyright = $v.LegalCopyright
        OriginalFilename = $v.OriginalFilename; InternalName = $v.InternalName; Comments = $v.Comments; Language = $v.Language
        FileVersionNumber = [version]('{0}.{1}.{2}.{3}' -f $v.FileMajorPart, $v.FileMinorPart, $v.FileBuildPart, $v.FilePrivatePart)
    }
}

# Writes the fields (a hashtable of FileDescription, ProductName, CompanyName, FileVersion, ProductVersion, LegalCopyright,
# OriginalFilename, InternalName, Comments; a field left out keeps what the file says) in English (United States).
function Set-NthVersionResource {
    param([Parameter(Mandatory)][string]$Path, [Parameter(Mandatory)][System.Collections.IDictionary]$Fields)
    Initialize-NthBuildTypes
    $change = [Nth.Build.VersionFields]::new()
    foreach ($key in $Fields.Keys) { $change.$key = [string]$Fields[$key] }
    [Nth.Build.Win32VersionStamp]::Stamp((Resolve-Path -LiteralPath $Path).Path, $change)
}

# Throws, naming every field that differs, when the file does not say what the hashtable lists.
function Assert-NthVersionResource {
    param([Parameter(Mandatory)][string]$Path, [Parameter(Mandatory)][System.Collections.IDictionary]$Expected, [switch]$CheckLanguage)
    $actual = Get-NthVersionInfo -Path $Path
    $problems = foreach ($key in $Expected.Keys) {
        if ([string]$actual.$key -cne [string]$Expected[$key]) { "$key is '$($actual.$key)', not '$($Expected[$key])'" }
    }
    if ($CheckLanguage -and "$($actual.Language)" -notlike '*English (United States)*') { $problems += "Language is '$($actual.Language)', not English (United States)" }
    if ($problems) { throw "${Path}: $($problems -join '; ')" }
}

# PLAN.md section 7: what each exe says. The publisher is decision A (oak993); it is one value, passed in.
function Get-NthIdentity {
    param(
        [Parameter(Mandatory)][string]$Publisher, [Parameter(Mandatory)][string]$Version, [Parameter(Mandatory)][string]$Build,
        [Parameter(Mandatory)][string]$AppCopyright
    )
    $copyright = [char]0x00A9
    [pscustomobject]@{
        App = [ordered]@{
            FileDescription = 'nth'; ProductName = 'nth'; CompanyName = $Publisher; FileVersion = "$Version.$Build"
            ProductVersion = $Version; LegalCopyright = $AppCopyright; OriginalFilename = 'nth.exe'; InternalName = 'nth'
        }
        Setup = [ordered]@{
            FileDescription = 'nth Setup'; ProductName = 'nth'; CompanyName = $Publisher; FileVersion = "$Version.$Build"
            ProductVersion = $Version
            LegalCopyright = "$copyright 2026 $Publisher. Includes Droppy Code by Jordy Spruit (Droppy), GNU AGPL v3.0."
            OriginalFilename = 'nth-Setup.exe'; InternalName = 'nth Setup'
        }
        # Velopack's Update.exe says "Velopack" (VELOPACK_CHECK check 12). Only these four fields change; its copyright line, which
        # credits Velopack's authors, and its own file version stay.
        Updater = [ordered]@{ FileDescription = 'nth Updater'; ProductName = 'nth'; CompanyName = $Publisher; ProductVersion = $Version }
        # Velopack's Setup.exe already carries nth's values from the package (vpk writes them); it is checked, never edited.
        VelopackSetup = [ordered]@{ CompanyName = $Publisher; ProductName = 'nth' }
    }
}

# ---- icon ------------------------------------------------------------------------------------------------------------------

# PLAN.md section 7: icon.ico holds sizes 16, 20, 24, 32, 40, 48, 64, 96, 128 and 256.
$script:NthIcoSizes = @(16, 20, 24, 32, 40, 48, 64, 96, 128, 256)

function Get-NthIcoSizes {
    param([Parameter(Mandatory)][string]$Path)
    $bytes = [IO.File]::ReadAllBytes((Resolve-Path -LiteralPath $Path).Path)
    if ($bytes.Length -lt 6 -or [BitConverter]::ToUInt16($bytes, 0) -ne 0 -or [BitConverter]::ToUInt16($bytes, 2) -ne 1) { throw "$Path is not an .ico file." }
    $count = [BitConverter]::ToUInt16($bytes, 4)
    @(for ($i = 0; $i -lt $count; $i++) { $width = $bytes[6 + 16 * $i]; if ($width -eq 0) { 256 } else { [int]$width } })
}

function Assert-NthIconSizes {
    param([Parameter(Mandatory)][string]$Path)
    $sizes = @(Get-NthIcoSizes -Path $Path | Sort-Object)
    if (($sizes -join ',') -ne ($script:NthIcoSizes -join ',')) { throw "$Path holds sizes $($sizes -join ', ') instead of $($script:NthIcoSizes -join ', ')." }
}

# ---- packages --------------------------------------------------------------------------------------------------------------

# PLAN.md section 4. T_pkg is the full nupkg; T_app is what current\ will hold (lib/app without the two entries Setup.exe moves out
# of it: Squirrel.exe becomes <root>\Update.exe and <name>_ExecutionStub.exe becomes <root>\<name>.exe, VELOPACK_CHECK checks 2 and
# 5); T_disk adds those two back, so the one size shown everywhere counts every file once. VelopackNeedBytes is Velopack's own
# free-space threshold on the root's drive: all entries compressed plus uncompressed plus 50,000,000 (VELOPACK_CHECK "Also found").
function Get-NthPackageMetrics {
    param([Parameter(Mandatory)][string]$Package)
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead((Resolve-Path -LiteralPath $Package).Path)
    try {
        $files = @($archive.Entries | Where-Object { -not $_.FullName.EndsWith('/', [StringComparison]::Ordinal) })
        $app = @($files | Where-Object { $_.FullName.StartsWith('lib/app/', [StringComparison]::Ordinal) })
        $updater = @($app | Where-Object { $_.FullName -ceq 'lib/app/Squirrel.exe' })
        $stub = @($app | Where-Object { $_.FullName -match '^lib/app/[^/]+_ExecutionStub\.exe$' })
        if ($updater.Count -ne 1) { throw "$Package holds no lib/app/Squirrel.exe (Velopack's Update.exe)." }
        if ($stub.Count -ne 1) { throw "$Package holds $($stub.Count) lib/app/*_ExecutionStub.exe entries, not one." }
        $moved = @($updater[0].FullName, $stub[0].FullName)
        $appBytes = [long](($app | Where-Object { $moved -cnotcontains $_.FullName } | Measure-Object -Property Length -Sum).Sum)
        $packageBytes = (Get-Item -LiteralPath $Package).Length
        $all = [long](($files | Measure-Object -Property Length -Sum).Sum) + [long](($files | Measure-Object -Property CompressedLength -Sum).Sum)
        [pscustomobject]@{
            PackageBytes = [long]$packageBytes; AppBytes = $appBytes; UpdaterBytes = [long]$updater[0].Length; StubBytes = [long]$stub[0].Length
            DiskBytes = [long]$packageBytes + $appBytes + [long]$updater[0].Length + [long]$stub[0].Length
            VelopackNeedBytes = $all + 50000000L
            Entries = @($files | ForEach-Object { $_.FullName })
        }
    } finally { $archive.Dispose() }
}

# The package's nuspec as XML.
function Get-NthNuspec {
    param([Parameter(Mandatory)][string]$Package)
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead((Resolve-Path -LiteralPath $Package).Path)
    try {
        $entry = @($archive.Entries | Where-Object { $_.FullName -notlike '*/*' -and $_.FullName.EndsWith('.nuspec', [StringComparison]::Ordinal) })
        if ($entry.Count -ne 1) { throw "$Package holds $($entry.Count) root .nuspec files, not one." }
        $reader = [IO.StreamReader]::new($entry[0].Open(), [Text.Encoding]::UTF8)
        try { [xml]$reader.ReadToEnd() } finally { $reader.Dispose() }
    } finally { $archive.Dispose() }
}

# Extracts the named entries into a folder (entry names as paths below it) and returns the folder.
function Expand-NthPackageEntries {
    param([Parameter(Mandatory)][string]$Package, [Parameter(Mandatory)][string]$Destination, [Parameter(Mandatory)][string[]]$Names)
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead((Resolve-Path -LiteralPath $Package).Path)
    try {
        foreach ($name in $Names) {
            $entry = $archive.GetEntry($name)
            if (-not $entry) { throw "$Package holds no $name." }
            $target = Join-Path $Destination $name
            New-Item -ItemType Directory -Force -Path (Split-Path -Parent $target) | Out-Null
            [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $target, $true)
        }
    } finally { $archive.Dispose() }
    $Destination
}

# ---- the Visual C++ runtime, app-local (PLAN.md override 4 and section 9 item 2) -------------------------------------------

function Get-NthVcRuntimePin {
    param([Parameter(Mandatory)][string]$ToolchainPath)
    $toolchain = Get-Content -LiteralPath $ToolchainPath -Raw | ConvertFrom-Json
    if (-not $toolchain.vcRuntime) { throw "$ToolchainPath has no vcRuntime pin." }
    $toolchain.vcRuntime
}

# The folders that hold Visual Studio's redistributable copies (VC\Redist\MSVC), newest install first. -Roots wins, then
# $env:NTH_VC_REDIST_ROOT (a build agent or a test), then every Visual Studio vswhere lists, then $env:VCINSTALLDIR.
function Find-NthVcRedistRoots {
    param([string[]]$Roots)
    if ($Roots) { return @($Roots | Where-Object { Test-Path -LiteralPath $_ -PathType Container }) }
    if ($env:NTH_VC_REDIST_ROOT) { return @($env:NTH_VC_REDIST_ROOT | Where-Object { Test-Path -LiteralPath $_ -PathType Container }) }
    $found = [Collections.Generic.List[string]]::new()
    $x86 = ${env:ProgramFiles(x86)}
    if ($x86) {
        $vswhere = Join-Path $x86 'Microsoft Visual Studio\Installer\vswhere.exe'
        if (Test-Path -LiteralPath $vswhere) {
            foreach ($install in @(& $vswhere -all -products '*' -property installationPath)) {
                $candidate = Join-Path $install 'VC\Redist\MSVC'
                if (Test-Path -LiteralPath $candidate -PathType Container) { $found.Add($candidate) }
            }
        }
    }
    if ($env:VCINSTALLDIR) {
        $candidate = Join-Path $env:VCINSTALLDIR 'Redist\MSVC'
        if (Test-Path -LiteralPath $candidate -PathType Container) { $found.Add($candidate) }
    }
    @($found | Select-Object -Unique)
}

# Every redistributable under the roots that has the CRT and OpenMP folders for the architecture. The toolset in the folder names
# (Microsoft.VC143.CRT in Visual Studio 2022, Microsoft.VC145.CRT in 2026) is read, never assumed: the DLL file names do not change.
function Get-NthVcRuntimeCandidates {
    param([string[]]$Roots, [string]$Architecture = 'x64')
    foreach ($root in $Roots) {
        foreach ($versionFolder in @(Get-ChildItem -LiteralPath $root -Directory -ErrorAction SilentlyContinue | Where-Object { $_.Name -match '^\d+\.\d+\.\d+$' })) {
            $architectureFolder = Join-Path $versionFolder.FullName $Architecture
            $crt = @(Get-ChildItem -LiteralPath $architectureFolder -Directory -Filter 'Microsoft.VC*.CRT' -ErrorAction SilentlyContinue) | Select-Object -First 1
            $openMp = @(Get-ChildItem -LiteralPath $architectureFolder -Directory -Filter 'Microsoft.VC*.OpenMP' -ErrorAction SilentlyContinue) | Select-Object -First 1
            if (-not $crt -or -not $openMp) { continue }
            $runtime = Join-Path $crt.FullName 'vcruntime140.dll'
            if (-not (Test-Path -LiteralPath $runtime)) { continue }
            [pscustomobject]@{
                Version = $versionFolder.Name; Root = $root; CrtFolder = $crt.FullName; OpenMpFolder = $openMp.FullName
                FileVersion = (Get-NthVersionInfo -Path $runtime).FileVersionNumber
            }
        }
    }
}

# Finds the pinned redistributable and proves each of the four files is byte for byte the pinned one (SHA-256).
function Resolve-NthVcRuntime {
    param([Parameter(Mandatory)]$Pin, [string[]]$Roots)
    $candidates = @(Get-NthVcRuntimeCandidates -Roots $Roots -Architecture $Pin.architecture)
    $match = $candidates | Where-Object { $_.Version -eq $Pin.redistVersion } | Select-Object -First 1
    if (-not $match) {
        throw "Visual C++ redistributable $($Pin.redistVersion) (the pin in scripts/release-toolchain.json) was not found under: $(@($Roots) -join '; ')."
    }
    $files = foreach ($file in $Pin.files) {
        $folder = if ($file.group -eq 'OpenMP') { $match.OpenMpFolder } else { $match.CrtFolder }
        $source = Join-Path $folder $file.name
        if (-not (Test-Path -LiteralPath $source)) { throw "$($file.name) is missing from $folder." }
        $hash = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
        if ($hash -ne $file.sha256) { throw "$($file.name) in $folder is not the pinned file (SHA-256 $hash, pinned $($file.sha256))." }
        [pscustomobject]@{ Name = $file.name; Source = $source; Sha256 = $hash }
    }
    [pscustomobject]@{ Version = $match.Version; FileVersion = $match.FileVersion; Files = @($files) }
}

# The refresh policy (PLAN.md section 9 item 2): these copies get no Windows servicing, so every release build compares the pin with
# the newest redistributable installed, and BelowSecurityFloor (the pin is older than the newest Microsoft security update noted in
# release-toolchain.json) is the one result that stops the build.
function Test-NthVcRuntimeRefresh {
    param([Parameter(Mandatory)]$Pin, [string[]]$Roots)
    $pinned = [version]$Pin.fileVersion
    $floor = [version]$Pin.securityFloor.fileVersion
    $newest = @(Get-NthVcRuntimeCandidates -Roots $Roots -Architecture $Pin.architecture | Sort-Object FileVersion -Descending) | Select-Object -First 1
    [pscustomobject]@{
        Pinned = $pinned; SecurityFloor = $floor; BelowSecurityFloor = ($pinned -lt $floor)
        Newest = if ($newest) { $newest.FileVersion } else { $null }
        NewestFolder = if ($newest) { $newest.Version } else { $null }
        NewerInstalled = [bool]($newest -and $newest.FileVersion -gt $pinned)
    }
}

function Copy-NthVcRuntime {
    param([Parameter(Mandatory)]$Resolved, [Parameter(Mandatory)][string]$Destination)
    foreach ($file in $Resolved.Files) {
        $target = Join-Path $Destination $file.Name
        Copy-Item -LiteralPath $file.Source -Destination $target -Force
        if ((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -ne $file.Sha256) { throw "$($file.Name) was not copied intact to $Destination." }
    }
}

# The four DLLs must sit next to nth.exe (the application folder is where Windows resolves a loaded DLL's imports from).
function Assert-NthVcRuntimeBeside {
    param([Parameter(Mandatory)]$Pin, [Parameter(Mandatory)][string]$Folder)
    foreach ($file in $Pin.files) {
        $path = Join-Path $Folder $file.name
        if (-not (Test-Path -LiteralPath $path)) { throw "$($file.name) is not next to the app in $Folder." }
        if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $file.sha256) { throw "$path is not the pinned $($file.name)." }
    }
}

# ---- names -----------------------------------------------------------------------------------------------------------------

# PLAN.md section 9 items 7 and 10: the shared file is nth-Setup-<version>.exe, or nth-Setup-<version>-UNSIGNED.exe when it is not
# signed, so an unsigned test build cannot be mistaken for a release.
function Get-NthSetupFileName {
    param([Parameter(Mandatory)][string]$Version, [Parameter(Mandatory)][bool]$Signed)
    if ($Signed) { "nth-Setup-$Version.exe" } else { "nth-Setup-$Version-UNSIGNED.exe" }
}

function Get-NthUnsignedMessage { 'UNSIGNED, not for sharing' }

# ---- the old exe name and foreign names ------------------------------------------------------------------------------------

# Files in the repository that still say the old exe name, minus the decision ledgers, the named evidence records and the legacy files
# (code and tests that handle an install made before the rename on purpose) in scripts/lib/old-exe-name-allowlist.json. A path is relative with forward slashes.
function Find-NthOldExeNameReferences {
    param([Parameter(Mandatory)][string]$Root, [string]$AllowListPath = (Join-Path $script:NthLibDir 'old-exe-name-allowlist.json'))
    $allow = Get-Content -LiteralPath $AllowListPath -Raw | ConvertFrom-Json
    $allowed = @($allow.ledgers) + @($allow.evidence | ForEach-Object { $_.path }) + @($allow.legacy | ForEach-Object { $_.path })
    $hits = $null
    & git -C $Root rev-parse --is-inside-work-tree 2>&1 | Out-Null
    if ($LASTEXITCODE -eq 0) {
        $hits = @(& git -C $Root grep --untracked -l -I -F -e $script:NthOldExeName)
        if ($LASTEXITCODE -gt 1) { throw "git grep failed in $Root." }
    } else {
        $skip = '[\\/](\.git|bin|obj|artifacts|node_modules)[\\/]'
        $hits = @(Get-ChildItem -LiteralPath $Root -Recurse -File -ErrorAction SilentlyContinue | Where-Object { $_.FullName -notmatch $skip } |
            Where-Object { Select-String -LiteralPath $_.FullName -SimpleMatch -Pattern $script:NthOldExeName -Quiet } |
            ForEach-Object { [IO.Path]::GetRelativePath($Root, $_.FullName).Replace('\', '/') })
    }
    @($hits | Where-Object { $_ } | ForEach-Object { $_.Replace('\', '/') } | Where-Object { $allowed -cnotcontains $_ } | Sort-Object)
}

# Places where a word stands as text in a binary, not counting the first part of a dotted name (a namespace). -Encoding Utf16 is what a
# user can see in a native-AOT exe (string literals and the version resource); Both also reads the UTF-8 metadata, where every namespace
# segment is a name of its own.
function Find-NthWordInBinary {
    param([Parameter(Mandatory)][string]$Path, [Parameter(Mandatory)][string]$Word, [ValidateSet('Both', 'Utf16', 'Ascii')][string]$Encoding = 'Both')
    Initialize-NthBuildTypes
    @([Nth.Build.BinaryTextScan]::FindWord((Resolve-Path -LiteralPath $Path).Path, $Word, ($Encoding -ne 'Utf16'), ($Encoding -ne 'Ascii')))
}

# ---- signing (decision B is open: this is a hook, off unless the owner says so for a build, OD-0058) ------------------------

function Get-NthSignatureState {
    param([Parameter(Mandatory)][string]$Path)
    $signature = Get-AuthenticodeSignature -LiteralPath $Path
    [pscustomobject]@{
        Status = $signature.Status.ToString(); Signed = ($signature.Status -eq 'Valid')
        Subject = if ($signature.SignerCertificate) { $signature.SignerCertificate.Subject } else { $null }
        Timestamped = [bool]$signature.TimeStamperCertificate
    }
}

# What the owner provides on his own PC (keys and settings never go in the repository): the environment variables below.
function Get-NthSignSettings {
    @{
        SignToolPath = $env:NTH_SIGN_SIGNTOOL; CertificateArguments = $env:NTH_SIGN_SIGNTOOL_ARGS; TimestampUrl = $env:NTH_SIGN_TIMESTAMP_URL
        AzureDlib = $env:NTH_SIGN_AZURE_DLIB; AzureMetadata = $env:NTH_SIGN_AZURE_METADATA
    }
}

# signtool.exe's arguments, without the file. SignTool: the owner's certificate arguments (for example "/sha1 <thumbprint>").
# Azure: the Azure signing dlib and its metadata file (VELOPACK_CHECK check 12: vpk takes only one of signTemplate, signParams and
# azureTrustedSignFile, so Azure signing goes through the template too). Always SHA-256 with an RFC 3161 timestamp.
function Get-NthSignToolArguments {
    param([Parameter(Mandatory)][ValidateSet('SignTool', 'Azure')][string]$Mode, [Parameter(Mandatory)][hashtable]$Settings, [string]$Description = 'nth')
    $timestamp = if ($Settings.TimestampUrl) { $Settings.TimestampUrl } elseif ($Mode -eq 'Azure') { 'http://timestamp.acs.microsoft.com' } else { 'http://timestamp.digicert.com' }
    $common = @('sign', '/d', $Description, '/fd', 'SHA256', '/tr', $timestamp, '/td', 'SHA256')
    if ($Mode -eq 'Azure') {
        if (-not $Settings.AzureDlib -or -not $Settings.AzureMetadata) {
            throw 'Signing with Azure is on, but NTH_SIGN_AZURE_DLIB and NTH_SIGN_AZURE_METADATA are not both set.'
        }
        return $common + @('/dlib', $Settings.AzureDlib, '/dmdf', $Settings.AzureMetadata)
    }
    if (-not $Settings.CertificateArguments) { throw 'Signing with signtool is on, but NTH_SIGN_SIGNTOOL_ARGS (the certificate arguments, for example "/sha1 <thumbprint>") is not set.' }
    $certificate = @([regex]::Matches($Settings.CertificateArguments, '"[^"]*"|\S+') | ForEach-Object { $_.Value.Trim('"') })
    $common + $certificate
}

function Resolve-NthSignTool {
    param([hashtable]$Settings)
    if ($Settings -and $Settings.SignToolPath) {
        if (-not (Test-Path -LiteralPath $Settings.SignToolPath)) { throw "NTH_SIGN_SIGNTOOL points at $($Settings.SignToolPath), which does not exist." }
        return $Settings.SignToolPath
    }
    $command = Get-Command signtool.exe -ErrorAction SilentlyContinue
    if ($command) { return $command.Source }
    $kits = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
    $found = if (Test-Path -LiteralPath $kits) { Get-ChildItem -LiteralPath $kits -Recurse -Filter signtool.exe -ErrorAction SilentlyContinue | Where-Object { $_.FullName -match '\\x64\\' } | Sort-Object FullName -Descending | Select-Object -First 1 }
    if (-not $found) { throw 'signtool.exe was not found (set NTH_SIGN_SIGNTOOL or install the Windows SDK).' }
    $found.FullName
}

function Invoke-NthSign {
    param([Parameter(Mandatory)][ValidateSet('SignTool', 'Azure')][string]$Mode, [Parameter(Mandatory)][string]$Path, [string]$Description = 'nth')
    $settings = Get-NthSignSettings
    $arguments = Get-NthSignToolArguments -Mode $Mode -Settings $settings -Description $Description
    $signtool = Resolve-NthSignTool -Settings $settings
    & $signtool @arguments $Path
    if ($LASTEXITCODE -ne 0) { throw "signtool failed with exit code $LASTEXITCODE for $Path." }
}
