<#
.SYNOPSIS
  Extracts one exact Marrow version from CHANGELOG.md, without packaging or networking.
.EXAMPLE
  pwsh scripts/extract-release-notes.ps1 -Version 1.7.0 -OutputFile release-notes.md
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Version,
    [string]$ChangelogFile = (Join-Path (Split-Path -Parent $PSScriptRoot) 'CHANGELOG.md'),
    [Parameter(Mandatory = $true)][string]$OutputFile
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'release-notes.ps1')
$notes = Get-ReleaseNotes -Version $Version -ChangelogFile $ChangelogFile
# Validate completely before touching the output. Preserve the section's text, line endings and Unicode, with no BOM added.
[System.IO.File]::WriteAllText($OutputFile, $notes, [System.Text.UTF8Encoding]::new($false))
