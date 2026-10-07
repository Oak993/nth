# Owner-local authentic bake. No supplied colours, palette edits, commits or network credentials.
[CmdletBinding()]
param(
    [string]$OutputDirectory,
    [string]$PreviousPack
)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $repo ('porting/evidence/models-palette/bake-' + [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ')) }
if (Test-Path $OutputDirectory) { throw 'Use a new output directory; a capture must not mix generations.' }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
Push-Location $repo
try {
    & dotnet build src/Marrow.App/Marrow.App.csproj -c Release -p:EnableWindowsTargeting=true -m:1
    if ($LASTEXITCODE -ne 0) { throw 'Build failed; no embedded pack changed.' }
    $cli = Join-Path $repo 'src/Marrow.App/bin/Release/net10.0-windows10.0.19041.0/Marrow.App.dll'
    $captureArgs = @($cli, '--colour-bake-live', $OutputDirectory)
    if ($PreviousPack) { $captureArgs += (Resolve-Path $PreviousPack).Path }
    & dotnet @captureArgs
    if ($LASTEXITCODE -ne 0) { throw 'Official capture failed; no embedded pack changed.' }
    $packs = @(Get-ChildItem $OutputDirectory -Filter 'pack.v*.json.gz')
    if ($packs.Count -ne 1) { throw 'Expected exactly one validated release pack.' }
    Copy-Item $packs[0].FullName (Join-Path $repo 'src/Marrow.Services/Port/ModelsPalette/pack.v1.json.gz')
    Write-Output ('Validated pack installed. Review and commit the pack and capture evidence: ' + $OutputDirectory)
    Write-Output ('SHA-256: ' + (Get-FileHash $packs[0].FullName -Algorithm SHA256).Hash)
}
finally { Pop-Location }
