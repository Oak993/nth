[CmdletBinding()]
param([switch]$PackagingOnly)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$pins = Get-Content (Join-Path $root 'scripts/release-toolchain.json') -Raw | ConvertFrom-Json
Push-Location $root
try {
    $sdk = (& dotnet --version).Trim()
    if ($LASTEXITCODE -ne 0 -or $sdk -ne $pins.dotnetSdk) { throw "SDK must be $($pins.dotnetSdk), got $sdk." }
    if ($PSVersionTable.PSVersion.ToString() -ne $pins.powerShell) { throw "PowerShell must be $($pins.powerShell)." }
    if (-not $PackagingOnly) {
        $nodeVersion = (& node --version).Trim().TrimStart('v')
        if ($LASTEXITCODE -ne 0 -or $nodeVersion -ne $pins.node) { throw "Node must be $($pins.node)." }
    }
    $manifest = Get-Content (Join-Path $root '.config/dotnet-tools.json') -Raw | ConvertFrom-Json
    if ($manifest.tools.vpk.version -ne $pins.vpk) { throw 'Local vpk manifest differs from release toolchain.' }
    $help = (& dotnet tool run vpk -- --help | Select-Object -First 3) -join ' '
    if ($LASTEXITCODE -ne 0 -or $help -notmatch [regex]::Escape($pins.vpk)) { throw 'Restore the pinned local tool with dotnet tool restore.' }
    $cache = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $HOME '.nuget/packages' }
    $toolPackage = Join-Path $cache "vpk/$($pins.vpk)/vpk.$($pins.vpk).nupkg"
    $digest = [Convert]::ToBase64String([Security.Cryptography.SHA512]::HashData([IO.File]::ReadAllBytes($toolPackage)))
    if ($digest -ne $pins.vpkNugetSha512) { throw 'Pinned vpk package content hash mismatch.' }
    Write-Host "Pinned toolchain verified: SDK $sdk, PowerShell $($pins.powerShell), local vpk $($pins.vpk)."
} finally { Pop-Location }
