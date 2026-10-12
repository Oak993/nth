<#
.SYNOPSIS
  Web surfaces W23 (DESIGN 2.1 and 14): the WebView2 parts of nth's package. Dot-sourced by scripts/package-windows.ps1; nothing here runs on its own.

.DESCRIPTION
  WebView2Loader.dll is a native, architecture-specific binary. The package for a runtime (win-x64, win-arm64) holds one at the root of
  the app folder, of the machine type that runtime runs as, and any other copy in the package (the skills pipeline worker's) is the same
  type. The managed Microsoft.Web.WebView2.Core.dll sits beside it, and the SDK's WPF and WinForms assemblies are not in the package.
  Marrow.Services.csproj picks the loader by RuntimeIdentifier; these functions prove the result in the publish folder and in the
  full package vpk made, so a package with the wrong or a missing loader stops the build before any installer is made from it.

  Nothing here signs, uploads or runs anything built.
#>

# IMAGE_FILE_HEADER.Machine values.
$script:NthWebMachines = @{ 'win-x64' = 0x8664; 'win-arm64' = 0xAA64; 'win-x86' = 0x014C }

function Get-NthPeMachine {
    <# The Machine field of a PE file's header, or $null when the bytes are not a PE image. #>
    param([Parameter(Mandatory)][byte[]]$Bytes)
    if ($Bytes.Length -lt 0x40 -or $Bytes[0] -ne 0x4D -or $Bytes[1] -ne 0x5A) { return $null }
    $peOffset = [BitConverter]::ToInt32($Bytes, 0x3C)
    if ($peOffset -lt 0 -or ($peOffset + 6) -gt $Bytes.Length) { return $null }
    if ($Bytes[$peOffset] -ne 0x50 -or $Bytes[$peOffset + 1] -ne 0x45 -or $Bytes[$peOffset + 2] -ne 0 -or $Bytes[$peOffset + 3] -ne 0) { return $null }
    return [int][BitConverter]::ToUInt16($Bytes, $peOffset + 4)
}

function Get-NthExpectedLoaderMachine {
    <# The machine type WebView2Loader.dll must have for a runtime identifier. Throws for a runtime nth does not package. #>
    param([Parameter(Mandatory)][string]$Runtime)
    if (-not $script:NthWebMachines.ContainsKey($Runtime)) {
        throw "Runtime '$Runtime' has no WebView2Loader.dll flavour nth packages (win-x64 or win-arm64)."
    }
    if ($Runtime -eq 'win-x86') { throw "nth does not package win-x86 (WebView2Loader.dll x64 and arm64 only)." }
    return [int]$script:NthWebMachines[$Runtime]
}

function Test-NthLoaderBytes {
    <# True when the bytes are a WebView2Loader.dll for the runtime. #>
    param([Parameter(Mandatory)][byte[]]$Bytes, [Parameter(Mandatory)][string]$Runtime)
    $machine = Get-NthPeMachine -Bytes $Bytes
    return ($null -ne $machine) -and ($machine -eq (Get-NthExpectedLoaderMachine -Runtime $Runtime))
}

function Assert-NthRuntimeRestorable {
    <#
      Packaging for win-arm64 needs the whole restore graph to include it: Directory.Build.props lists the shipping runtime identifiers
      (RR-35, RestoreLockedMode) and every project's packages.lock.json carries a target per identifier. Without them dotnet publish
      stops with NU1004, far from the cause. This says it plainly, before anything is built. win-x64 always passes.
    #>
    param([Parameter(Mandatory)][string]$Root, [Parameter(Mandatory)][string]$Runtime)
    [void](Get-NthExpectedLoaderMachine -Runtime $Runtime)
    if ($Runtime -eq 'win-x64') { return }
    $props = Get-Content -LiteralPath (Join-Path $Root 'Directory.Build.props') -Raw
    $listed = [regex]::Match($props, '<RuntimeIdentifiers>([^<]*)</RuntimeIdentifiers>')
    if (-not $listed.Success -or (($listed.Groups[1].Value -split ';') -notcontains $Runtime)) {
        throw "Packaging $Runtime needs '$Runtime' in <RuntimeIdentifiers> in Directory.Build.props (the orchestrator owns that file) and every packages.lock.json regenerated with it (scripts/restore-dependencies.ps1). It is not there yet: the restore is locked to the shipping identifiers (RR-35). Other x64-only parts also need an $Runtime flavour first: the voice natives (Whisper.net.Runtime) and the pinned Visual C++ runtime copies."
    }
    $lock = Get-Content -LiteralPath (Join-Path $Root 'src/Marrow.App/packages.lock.json') -Raw
    if ($lock -notmatch ('/' + [regex]::Escape($Runtime) + '"')) {
        throw "src/Marrow.App/packages.lock.json has no $Runtime target: regenerate the lock files with scripts/restore-dependencies.ps1 after adding '$Runtime' to Directory.Build.props."
    }
}

function Get-NthPackageLoaders {
    <# Every WebView2Loader.dll in a vpk full package (a zip), as path + bytes, paths relative to lib/app. #>
    param([Parameter(Mandatory)][string]$Package)
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [System.IO.Compression.ZipFile]::OpenRead($Package)
    try {
        foreach ($entry in $archive.Entries) {
            if ($entry.FullName -notlike 'lib/app/*') { continue }
            if (-not [string]::Equals([System.IO.Path]::GetFileName($entry.FullName), 'WebView2Loader.dll', [StringComparison]::OrdinalIgnoreCase)) { continue }
            $stream = $entry.Open()
            try {
                $memory = New-Object System.IO.MemoryStream
                $stream.CopyTo($memory)
                [pscustomobject]@{ Path = $entry.FullName.Substring('lib/app/'.Length); Bytes = $memory.ToArray() }
            } finally { $stream.Dispose() }
        }
    } finally { $archive.Dispose() }
}

function Get-NthPackageNames {
    <# Every entry name under lib/app in a vpk full package. #>
    param([Parameter(Mandatory)][string]$Package)
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [System.IO.Compression.ZipFile]::OpenRead($Package)
    try { $archive.Entries | Where-Object { $_.FullName -like 'lib/app/*' } | ForEach-Object { $_.FullName.Substring('lib/app/'.Length) } }
    finally { $archive.Dispose() }
}

function Assert-NthWebView2Package {
    <#
      Proves the WebView2 parts of the full package (and of the publish folder it was made from): a WebView2Loader.dll of the runtime's
      machine type at the app root, byte for byte the published one; every other loader in the package of the same type; the managed
      Core assembly beside it; no WPF or WinForms assembly. Throws on the first problem; returns a one-line summary otherwise.
    #>
    param(
        [Parameter(Mandatory)][string]$Package,
        [Parameter(Mandatory)][string]$Runtime,
        [Parameter(Mandatory)][string]$PublishDir
    )
    $expected = Get-NthExpectedLoaderMachine -Runtime $Runtime
    $name = [System.IO.Path]::GetFileName($Package)
    $rootLoader = Join-Path $PublishDir 'WebView2Loader.dll'
    if (-not (Test-Path -LiteralPath $rootLoader)) { throw "WebView2Loader.dll was not published to $PublishDir (Marrow.Services.csproj copies it beside the app)." }
    if (-not (Test-NthLoaderBytes -Bytes ([System.IO.File]::ReadAllBytes($rootLoader)) -Runtime $Runtime)) {
        throw ("The published WebView2Loader.dll is not a $Runtime image (machine 0x{0:X4} expected)." -f $expected)
    }

    $loaders = @(Get-NthPackageLoaders -Package $Package)
    $atRoot = @($loaders | Where-Object { $_.Path -eq 'WebView2Loader.dll' })
    if ($atRoot.Count -ne 1) { throw "$name must hold exactly one WebView2Loader.dll at the app root (lib/app/WebView2Loader.dll); it holds $($atRoot.Count)." }
    foreach ($loader in $loaders) {
        if (-not (Test-NthLoaderBytes -Bytes $loader.Bytes -Runtime $Runtime)) { throw ("$name holds lib/app/$($loader.Path), which is not a $Runtime image (machine 0x{0:X4} expected)." -f $expected) }
    }
    $packed = [System.BitConverter]::ToString([System.Security.Cryptography.SHA256]::Create().ComputeHash($atRoot[0].Bytes))
    $published = [System.BitConverter]::ToString([System.Security.Cryptography.SHA256]::Create().ComputeHash([System.IO.File]::ReadAllBytes($rootLoader)))
    if ($packed -ne $published) { throw "WebView2Loader.dll in $name is not the published file." }

    $names = @(Get-NthPackageNames -Package $Package)
    if ($names -notcontains 'Microsoft.Web.WebView2.Core.dll') { throw "$name does not hold Microsoft.Web.WebView2.Core.dll beside the loader." }
    foreach ($forbidden in @('Microsoft.Web.WebView2.Wpf.dll', 'Microsoft.Web.WebView2.WinForms.dll')) {
        if ($names -contains $forbidden) { throw "$name holds $forbidden; the SDK's WPF and WinForms assemblies are never packaged (ED-NEW-web-w7-1)." }
    }
    return "$name holds WebView2Loader.dll for $Runtime ($($loaders.Count) copy/copies, all of machine 0x$('{0:X4}' -f $expected)) and Microsoft.Web.WebView2.Core.dll, byte for byte as published."
}
