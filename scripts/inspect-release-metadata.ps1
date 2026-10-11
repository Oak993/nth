<# Read-only PE inspection. Never loads application code, runs an EXE or builds an installer. #>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$AppDirectory,
    [string]$SetupExe = '',
    [string]$SetupAssembly = ''
)
$ErrorActionPreference = 'Stop'
if (-not [OperatingSystem]::IsWindows()) { throw 'Native EXE version resources require Windows for this PowerShell inspection. Use a PE resource parser for Linux application checks.' }
$root = Split-Path -Parent $PSScriptRoot
$source = Get-Content (Join-Path $root 'src/Marrow.App/AssemblyInfo.cs') -Raw
$version = [regex]::Match($source, 'AssemblyMetadata\("CFBundleShortVersionString",\s*"([^"]+)"\)').Groups[1].Value
$build = [regex]::Match($source, 'AssemblyMetadata\("CFBundleVersion",\s*"([^"]+)"\)').Groups[1].Value
$copyright = [regex]::Match($source, 'AssemblyCopyright\("([^"]+)"\)').Groups[1].Value
# nth installer PLAN.md 2 (override 7) and 7, unit U7: the publisher is one value (decision A, open) and the product version reads like the version.
$publisher = [regex]::Match((Get-Content (Join-Path $root 'Directory.Release.props') -Raw), '<NthPublisher>([^<]+)</NthPublisher>').Groups[1].Value
if (-not $publisher) { throw 'NthPublisher is not set in Directory.Release.props.' }
function Artifact([string]$Path) {
    $relative = [IO.Path]::GetRelativePath($root, (Resolve-Path $Path).Path).Replace('\', '/')
    if ($relative.StartsWith('../') -or [IO.Path]::IsPathRooted($relative)) { throw 'Inspected binaries must be retained inside the repository artifact tree.' }
    return @{ path=$relative; sha256=(Get-FileHash $Path -Algorithm SHA256).Hash.ToLowerInvariant() }
}
function Read-Identity([string]$Exe, [string]$Assembly, [string]$Title) {
    $bytes = [IO.File]::ReadAllBytes((Resolve-Path $Exe).Path)
    if ($bytes.Length -lt 64 -or [Text.Encoding]::ASCII.GetString($bytes,0,2) -ne 'MZ') { throw "$Exe is not a PE EXE." }
    $peOffset = [BitConverter]::ToUInt32($bytes,0x3c)
    if ($peOffset + 6 -ge $bytes.Length -or [Text.Encoding]::ASCII.GetString($bytes,$peOffset,4) -ne "PE`0`0" -or [BitConverter]::ToUInt16($bytes,$peOffset+4) -ne 0x8664) { throw "$Exe is not x64 PE." }
    $resource = [Diagnostics.FileVersionInfo]::GetVersionInfo((Resolve-Path $Exe).Path)
    $assemblyVersion = [Reflection.AssemblyName]::GetAssemblyName((Resolve-Path $Assembly).Path).Version.ToString()
    $fields = [ordered]@{
        assembly_version = $assemblyVersion; file_version = $resource.FileVersion
        product_version = $resource.ProductVersion; product = $resource.ProductName
        company = $resource.CompanyName; title = $resource.FileDescription
    }
    $expected = @{ assembly_version="$version.0"; file_version="$version.$build"; product_version=$version; product='nth'; company=$publisher; title=$Title }
    foreach ($name in $expected.Keys) {
        if ($fields[$name] -ne $expected[$name]) { throw "$Exe PE $name does not equal $($expected[$name])." }
    }
    $fields.exe_sha256 = (Get-FileHash $Exe -Algorithm SHA256).Hash.ToLowerInvariant()
    $fields.assembly_sha256 = (Get-FileHash $Assembly -Algorithm SHA256).Hash.ToLowerInvariant()
    $fields.legal_copyright = $resource.LegalCopyright
    if ($Title -eq 'nth' -and $fields.legal_copyright -cne $copyright) { throw 'Application legal copyright differs from the unchanged canonical source.' }
    $fields.architecture = 'x64'
    $fields.exe_artifact = Artifact $Exe
    $fields.assembly_artifact = Artifact $Assembly
    return $fields
}
# A published folder holds nth.exe (the packaging script renames the apphost, PLAN.md override 8); a build folder holds the apphost the SDK
# names after the assembly, which is also the name of the .dll the metadata is read from.
$appAssembly = 'Marrow.App'
$appExe = if (Test-Path (Join-Path $AppDirectory 'nth.exe')) { 'nth.exe' } else { "$appAssembly.exe" }
$result = [ordered]@{ pe_metadata=[ordered]@{ app=(Read-Identity (Join-Path $AppDirectory $appExe) (Join-Path $AppDirectory "$appAssembly.dll") 'nth') }; complete=$false }
if ($SetupExe -or $SetupAssembly) {
    if (-not $SetupExe -or -not $SetupAssembly) { throw 'Supply both final SetupExe and its matching build SetupAssembly for inspection.' }
    $result.pe_metadata.setup = Read-Identity $SetupExe $SetupAssembly 'nth Setup'
    $result.complete = $true
}
$result | ConvertTo-Json -Depth 5
