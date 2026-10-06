# OD-0111 / UE-6: read-only preflight shared by packaging and the standalone extractor.
# CHANGELOG uses ## [version] followed by ### parity sections. Extraction preserves the selected text and line endings.
function Get-MarkdownContentLines([string]$Text) {
    $fence = ''
    foreach ($line in [regex]::Matches($Text, '[^\n]+(?:\n|$)|\n')) {
        $trimmed = $line.Value.Trim()
        if ($fence) {
            $closing = '^' + [regex]::Escape($fence.Substring(0, 1)) + '{' + $fence.Length + ',}[ \t]*$'
            if ($trimmed -match $closing) { $fence = '' }
            continue
        }
        if ($trimmed -match '^(`{3,}|~{3,})') { $fence = $Matches[1]; continue }
        $line
    }
}

function Get-ReleaseNotes {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$Version,
        [Parameter(Mandatory = $true)][string]$ChangelogFile,
        [string]$ReleaseNotesFile = ''
    )

    if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Release notes: invalid package version '$Version'." }
    $explicit = -not [string]::IsNullOrEmpty($ReleaseNotesFile)
    $inputFile = if ($explicit) { $ReleaseNotesFile } else { $ChangelogFile }
    if (-not (Test-Path -LiteralPath $inputFile -PathType Leaf)) { throw "Release notes file not found: $inputFile (version $Version)." }
    $text = [System.IO.File]::ReadAllText((Resolve-Path -LiteralPath $inputFile).Path)
    if ([string]::IsNullOrWhiteSpace($text)) { throw "Release notes are empty: $inputFile (version $Version)." }

    # Version discovery excludes fenced examples. Any real H1/H2 heading bounds a CHANGELOG entry, including a malformed
    # neighbouring version: its sections cannot license this release. Explicit notes also support the promoted ## section headings.
    $lines = @(Get-MarkdownContentLines $text)
    $headings = @(
        foreach ($line in $lines) {
            $trimmed = $line.Value.Trim()
            if ($trimmed -match '^#{1,2}[ \t]+') {
                $identity = [regex]::Match($trimmed, '^##[ \t]+\[(?<version>[^\]\r\n]+)\](?:[ \t]+-.*)?[ \t]*$')
                [PSCustomObject]@{
                    Index = $line.Index
                    Version = if ($identity.Success) { $identity.Groups['version'].Value } else { '' }
                    VersionLike = [regex]::IsMatch($trimmed, '^#{1,2}[ \t]+(?:\[|[vV]?\d)')
                }
            }
        }
    )
    $selected = @($headings | Where-Object { $_.Version -ceq $Version })
    $versionHeadings = @($headings | Where-Object { $_.VersionLike })
    if ($selected.Count -ne 1 -or ($explicit -and $versionHeadings.Count -ne 1)) {
        throw "Release notes must identify exactly one section for version $Version; an explicit notes file must contain only that version: $inputFile."
    }
    $start = $selected[0].Index
    $end = $text.Length
    if (-not $explicit) {
        foreach ($heading in $headings) {
            if ($heading.Index -gt $start) { $end = $heading.Index; break }
        }
    }
    $notes = $text.Substring($start, $end - $start)

    # Only populated parity sections can license a release. Thanks and unknown headings reset the section and are ignored.
    # This guard is stricter than upstream's silent omission (defects 90/91); it does not turn unknown prose into a new card.
    $recognised = $false
    $inSection = $false
    foreach ($line in $lines) {
        if ($line.Index -lt $start -or $line.Index -ge $end) { continue }
        $trimmed = $line.Value.Trim()
        if ($trimmed -match '^#{1,6}[ \t]+(.+?)\s*$') {
            $title = $Matches[1].Trim().ToLowerInvariant()
            $inSection = $title -in @('new features', 'bug fixes', 'refinements')
        }
        elseif ($inSection -and $trimmed -match '^-\s+(.+)$') {
            # Upstream build_changelog.py strip_emphasis, in the same order (port_impacts 254).
            $bullet = $Matches[1].Replace('**', '').Replace('__', '').Replace('*', '').Replace('_', '').Trim()
            if (-not [string]::IsNullOrWhiteSpace($bullet)) { $recognised = $true }
        }
    }
    # Final authority is the exact parser compiled into the app's digest. The upstream-format checks above remain
    # deliberately stricter (fences, canonical titles, dash bullets and empty emphasis); they cannot license a card.
    # Add-Type compiles only this dependency-free source in memory: no build, artifacts, native tools or network.
    if ($recognised) {
        if (-not ('Marrow.Core.Port.SelfUpdate.ReleaseNotesParser' -as [type])) {
            $parser = Join-Path $PSScriptRoot '../src/Marrow.Core/Port/SelfUpdate/ReleaseNotesParser.cs'
            Add-Type -Path $parser -ErrorAction Stop
        }
        $recognised = [Marrow.Core.Port.SelfUpdate.ReleaseNotesParser]::Parse($notes).Count -gt 0
    }
    if (-not $recognised) {
        throw "Release notes for version $Version contain no populated recognised section (New features, Bug fixes, Refinements): $inputFile."
    }
    return $notes
}
