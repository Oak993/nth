<# Restores only. Never builds, packages, signs, installs or publishes. #>
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    function Restore-Locked([string[]]$Arguments) {
        & dotnet restore @Arguments -p:EnableWindowsTargeting=true -p:RestoreLockedMode=true --nologo
        if ($LASTEXITCODE -ne 0) { throw "Locked restore failed: $Arguments" }
    }
    Restore-Locked @('Marrow.sln')
    # NuGet solution graph traversal does not propagate ProjectReference.AdditionalProperties.
    Restore-Locked @('tests/Marrow.Shim.OfflineTests/Marrow.Shim.OfflineTests.csproj', '-p:PublishAot=false')
    # The future installer publish uses a separate trim graph; restoring it does not build the installer.
    Restore-Locked @('src/Marrow.Setup/Marrow.Setup.csproj', '-r', 'win-x64', '-p:PublishTrimmed=true', '-p:PublishSingleFile=true')
    foreach ($project in Get-ChildItem (Join-Path $root 'porting/probes') -Recurse -Filter '*.csproj' | Sort-Object FullName) {
        $arguments = @($project.FullName)
        # Historical P1 used explicit package versions before central version management.
        if ($project.Directory.Name -eq 'p1-glass') { $arguments += '-p:ManagePackageVersionsCentrally=false' }
        Restore-Locked $arguments
    }
    & dotnet tool restore
    if ($LASTEXITCODE -ne 0) { throw 'Pinned local tool restore failed.' }
    & (Join-Path $PSScriptRoot 'verify-toolchain.ps1')
} finally { Pop-Location }
