# Tactus native probes NP-17 and NP-19 to NP-21, and the native frame capture (unit P30; ED-NEW-pcctl-30; DESIGN.md
# 10.11 "Native" and 10.13). Owner-scheduled: run by hand, alone, in a quiet session, on a DEBUG build. NP-15, NP-16 and
# NP-18 run from scripts/pc-control-overlay-spike.ps1; this script lists the steps of the others, runs the native frame
# capture test when asked, and writes what you answer to %TEMP%\tactus-native-probes.log. It sends no input itself and
# changes no setting: every step is yours, by hand. Send the log to the Tactus unit owner; it goes into ED-NEW-pcctl-30.
param(
    [Parameter(Mandatory = $true)][string]$Exe,
    [string[]]$Only = @(),
    [switch]$Capture
)
$ErrorActionPreference = 'Stop'
$log = Join-Path $env:TEMP 'tactus-native-probes.log'
"probes $(Get-Date -Format s) exe $Exe" | Out-File -FilePath $log -Encoding utf8

$steps = [ordered]@{
    'NP-17' = @(
        'Open your usual apps (Word, Excel, Chrome, Teams, VS Code). Start nth with Tactus on and a chat working in Notepad.',
        'Press Ctrl+Alt+Space in each app: the pill must say "Paused" each time, and nothing may type in the app.',
        'Hold Ctrl+Alt+Space down for 2 s: it toggles once (MOD_NOREPEAT), not again and again.',
        'Switch the keyboard to Hindi, then Urdu: the Pause, Stop and Answer chords still work, and Settings > Tactus refuses a chord that types a character there.',
        'Start a Hindi or Urdu IME composition in Notepad and press Esc: only the composition is cancelled; Tactus keeps working.',
        'Press Esc three times fast while Tactus works: everything stops (path 5). Count how often that happened by accident today (count only, never the keys).'
    )
    'NP-19' = @(
        'Open Chrome, Edge, VS Code, Word, Explorer, Teams and Slack, each maximized, then snapped left.',
        'For each, let the pill dodge the window''s caption buttons: it must never cover minimize, maximize or close.',
        'Note any app where the pill covers a caption button, and whether it was maximized or snapped.'
    )
    'NP-20' = @(
        'With a card open and a reply typed in the card''s field: unplug the second monitor, then plug it back. The card and its draft must survive and move to a monitor you can see.',
        'Change the display scale (100 % to 150 %) with the card open: the pill keeps its physical size, then springs to the new scale.',
        'Drag the pill so it straddles two monitors with different scales, with a card open: no seam, no jump.',
        'Move the taskbar to the top (fixed, then auto-hide): the pill sits under it, never over it.',
        'Start a borderless fullscreen game or video, then an exclusive fullscreen one: the pill steps aside as DESIGN.md 10.5 says.',
        'Lock the PC (Win+L) with a card open, then unlock: the card is still there and still waits.'
    )
    'NP-21' = @(
        'For each confirm category (send, delete, pay): let Tactus prepare the step in Outlook classic, new Outlook, Gmail and Outlook on the web (Chrome and Edge), Teams, Slack and one shop checkout.',
        'Check that the card shows every row (recipients, subject, amount) exactly as the app shows it (UIA read).',
        'While the card waits, add a recipient by hand: the click must be refused as changed, and the card must show the new content.'
    )
}

foreach ($probe in $steps.Keys) {
    if ($Only.Count -gt 0 -and $Only -notcontains $probe) { continue }
    Write-Host ''
    Write-Host "== $probe" -ForegroundColor Cyan
    "== $probe" | Out-File -FilePath $log -Append -Encoding utf8
    foreach ($step in $steps[$probe]) {
        Write-Host "  - $step"
        $answer = Read-Host '    what you saw (Enter if as described)'
        "  - $step" | Out-File -FilePath $log -Append -Encoding utf8
        "    seen: $(if ($answer) { $answer } else { 'as described' })" | Out-File -FilePath $log -Append -Encoding utf8
    }
}

if ($Capture) {
    # the native frame capture: the spike's np16 pill, captured through WGC with --tactus-capture-test (about 2 minutes)
    Write-Host ''
    Write-Host '== native frame capture (PillCaptureNativeTests)' -ForegroundColor Cyan
    Read-Host 'Close other windows over the top of the primary monitor, then press Enter' | Out-Null
    $env:MARROW_TACTUS_EXE = $Exe
    $root = Split-Path -Parent $PSScriptRoot
    & dotnet test (Join-Path $root 'tests/Marrow.App.Tests') -c Debug --filter 'FullyQualifiedName~PillCaptureNativeTests' 2>&1 |
        Tee-Object -FilePath $log -Append | Select-String -Pattern 'Passed!|Failed!|Error Message|Assert' | ForEach-Object { Write-Host $_.Line }
}
Write-Host ''
Write-Host "Written to $log" -ForegroundColor Green
