<#
.SYNOPSIS
  The one command vpk 1.2.161 runs on files while it packs (vpk pack --signTemplate "... {{file...}}"). Nothing else calls it
  except the packaging script, for the shared setup exe after it is built.

.DESCRIPTION
  nth installer PLAN.md sections 7, 9 and 11, VELOPACK_CHECK.md check 12, unit U7 (ED-NEW-inst-u7-7).

  vpk runs this on the pack folder after it has put Update.exe there (as Squirrel.exe) and before it builds the nupkg, and again
  on Velopack's Setup.exe after it is bundled. That is the only place inside vpk where Update.exe can be edited before it is
  packed, and Update.exe still says "Velopack" until it is: the config file lists the version-resource fields to write into
  each file by name (only Squirrel.exe today). vpk accepts only one of signTemplate, signParams and azureTrustedSignFile, so
  signing goes through this template too (signtool, with the owner's certificate or the Azure dlib).

  Signing is OFF unless the config says otherwise (sign.mode None, the default of scripts/package-windows.ps1): decision B (the
  signing route) is the owner's, and nothing is signed without his word for that build (OD-0058). Then this script only stamps,
  and the packaging script passes vpk a --signExclude that keeps vpk from calling it for any other file.

  The config is JSON: { "stamps": { "Squirrel.exe": { "FileDescription": "...", ... } }, "sign": { "mode": "None|SignTool|Azure", "description": "nth" } }.
  Certificate settings come from the environment (NTH_SIGN_SIGNTOOL, NTH_SIGN_SIGNTOOL_ARGS, NTH_SIGN_TIMESTAMP_URL,
  NTH_SIGN_AZURE_DLIB, NTH_SIGN_AZURE_METADATA), never from a file in the repository.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Config,
    [Parameter(ValueFromRemainingArguments)][string[]]$Files = @()
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'lib/NthPackaging.ps1')

$settings = Get-Content -LiteralPath $Config -Raw | ConvertFrom-Json
$mode = if ($settings.sign -and $settings.sign.mode) { [string]$settings.sign.mode } else { 'None' }
if ($mode -notin @('None', 'SignTool', 'Azure')) { throw "sign.mode must be None, SignTool or Azure, not '$mode'." }
$description = if ($settings.sign -and $settings.sign.description) { [string]$settings.sign.description } else { 'nth' }

# Check the signing settings before touching any file, so a build with signing on and no certificate stops at the first file.
if ($mode -ne 'None') { $null = Get-NthSignToolArguments -Mode $mode -Settings (Get-NthSignSettings) -Description $description }

foreach ($file in $Files) {
    if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { throw "$file does not exist." }
    $name = [IO.Path]::GetFileName($file)
    $stamp = if ($settings.stamps) { $settings.stamps.PSObject.Properties | Where-Object { $_.Name -ieq $name } | Select-Object -First 1 }
    if ($stamp) {
        $fields = @{}
        foreach ($property in $stamp.Value.PSObject.Properties) { $fields[$property.Name] = [string]$property.Value }
        Set-NthVersionResource -Path $file -Fields $fields
        Write-Host "stamped $name"
    }
    if ($mode -ne 'None') {
        Invoke-NthSign -Mode $mode -Path $file -Description $description
        Write-Host "signed $name"
    }
}
