# Tactus overlay spike (units P25a and P18a; ED-NEW-pcctl-22, ED-NEW-pcctl-33; DESIGN.md 10.6 and 10.13 NP-15, NP-16, NP-18;
# DESIGN.md 2.13 NP-11 for np11-rdp, which opens one normal window instead of bands). Run by hand, alone, in a
# quiet window, on a DEBUG build. Nothing here sends input, captures the screen or drives another window: each probe
# opens band windows at the top of the primary work area, logs to the console and %TEMP%\tactus-spike-<probe>.log, and
# exits by itself after 90 s with a RESULT line. Send every RESULT line and every eye check to the Tactus unit owner:
# they go into ED-NEW-pcctl-22 and set P25's one hit-test switch and driver path (ED-NEW-pcctl-22 item 5).
param(
    [Parameter(Mandatory = $true)][string]$Exe,
    [string[]]$Only = @()
)
$ErrorActionPreference = 'Stop'

$steps = [ordered]@{
    'np15-region' = @(
        'Open Notepad under the top band, so a click that passes through types a caret move there.',
        'While the pill rests: click it, tap it with a pen, touch it. Each must log "press Mouse/Pen/Touch ... inside the pill True".',
        'Click beside the pill (within the band, 100 px to its left and right): Notepad must get the click, nothing logged.',
        'While the pill travels (every 3 s): click through it onto Notepad. Notepad gets it; the log says "grab in flight".',
        'Watch the start and the end of each travel for a flicker where the pill hands over between the two bands.',
        'Open Task Manager with "Always on top" and drag it over the pill: the log says "covered" and the pill comes back on top.',
        'If the click-through band draws nothing, rerun with: -Only np15-region and add --lwa by hand.'
    )
    'np15-pixel' = @(
        'Click the pill five times: five "inside the pill True" lines.',
        'Click the transparent band beside the pill five times. Per-pixel hit testing works only if Notepad got those clicks and RESULT counts 0 presses on transparent pixels.'
    )
    'np15-flip' = @(
        'Move the mouse slowly onto the resting pill and click just inside its edge at once: the press is taken.',
        'Click beside it: Notepad gets it. Click the moving pill. Then read RESULT: p95 record to hit-testable must be under 4 ms.'
    )
    'np16' = @(
        'Watch the pill travel (728 DIP, then wide) for judder; a card expands at 5 s and every 20 s after.',
        'Run it twice: first as is (cold), then again with --warm. Compare "longest frame after each expand" (the first value).',
        'With two monitors of different DPI or refresh rate, run once with --straddle and watch the seam as the pill crosses.',
        'Run once on a 60 Hz and once on a 144 Hz monitor if one is available (move it to primary). Read "render timer follows it".'
    )
    'np18-focus' = @(
        'Click in Notepad and type a word. Click the card''s field, type, press Enter: Notepad is in front again with the caret after your word.',
        'Repeat with Word (caret in the body) and with a browser''s address bar.',
        'Click the field, type, then click Notepad: Notepad stays in front and the log says the overlay lost the keyboard.'
    )
    'np11-rdp' = @(
        'Nothing to do by hand: a window "Tactus NP-11" opens with the Remote Desktop control in it. It never turns child sessions on.',
        'With child sessions off (this PC on 2026-10-11) it hosts the control and stops: the RESULT says hosted, extended and childSessions=False.',
        'With child sessions on (after NP-5): it connects, then measures 10 s visible, 10 s cloaked, 10 s minimized. Watch that the window comes back.',
        'If the control does not load, run again with --rdp-class and a newer class (for example MsTscAx.MsTscAx.10) and note which one loaded.'
    )
}

$runs = @(
    @{ Probe = 'np15-region'; Extra = @() },
    @{ Probe = 'np15-pixel'; Extra = @() },
    @{ Probe = 'np15-flip'; Extra = @() },
    @{ Probe = 'np16'; Extra = @() },
    @{ Probe = 'np16'; Extra = @('--warm') },
    @{ Probe = 'np16'; Extra = @('--straddle') },
    @{ Probe = 'np18-focus'; Extra = @() },
    @{ Probe = 'np11-rdp'; Extra = @() }
)

foreach ($run in $runs) {
    if ($Only.Count -gt 0 -and $Only -notcontains $run.Probe) { continue }
    Write-Host ''
    Write-Host "== $($run.Probe) $($run.Extra -join ' ')" -ForegroundColor Cyan
    $steps[$run.Probe] | ForEach-Object { Write-Host "  - $_" }
    Read-Host 'Press Enter to start (it ends by itself after 90 s)' | Out-Null
    & $Exe --tactus-overlay-spike $run.Probe @($run.Extra)
    $log = Join-Path $env:TEMP "tactus-spike-$($run.Probe).log"
    if (Test-Path $log) { Get-Content $log | Select-String 'RESULT' | ForEach-Object { Write-Host $_.Line -ForegroundColor Green } }
}
