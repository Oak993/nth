# Tactus native smoke and probes (unit 15, P19; ED-NEW-pcctl-43; DESIGN.md 5.4 items 1 to 11). Run by hand, alone, with
# focus, only with the owner's go-ahead, on a quiet desktop with nothing private open. It builds Marrow.App.Tests once,
# then walks the checklist: "auto" items run one native test by itself, "guided" items run one while you do what the
# steps say, "chat" and "app" items are checks you do in nth itself, "spike" items are probes of a DEBUG build that
# scripts/pc-control-overlay-spike.ps1 runs (you note their RESULT lines here), and "blocked" items wait for a later unit. Each test
# writes RESULT lines to %TEMP%\tactus-smoke\<probe>.log; this script shows them live, asks for your verdict, and writes
# everything to %TEMP%\tactus-smoke\summary-<time>.txt. Send that file to the Tactus unit owner: its RESULT lines go
# into ED-NEW-pcctl-43, and np4-proofs.json (from NP-4) is an optional
# import for development builds only (ED-NEW-pcctl-45): the app learns R4's ways per exe, version and framework itself.
#   -List          print the checklist and exit
#   -Only S1,NP-4  run only these items
#   -NoBuild       skip the build (after a first run)
# P30 adds NP-15 to NP-21 (DESIGN.md 10.13); NP-15 and NP-16 are in scripts/pc-control-overlay-spike.ps1.
param(
    [string[]]$Only = @(),
    [switch]$NoBuild,
    [switch]$List
)
$ErrorActionPreference = 'Stop'

$repo = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repo 'tests\Marrow.App.Tests'
$logs = Join-Path $env:TEMP 'tactus-smoke'
$ns = 'Marrow.App.Tests.Port.PcControl.Native'
New-Item -ItemType Directory -Force $logs | Out-Null
$summary = Join-Path $logs ('summary-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.txt')

function Item($id, $kind, $title, $test, $probes, $steps) {
    [pscustomobject]@{ Id = $id; Kind = $kind; Title = $title; Test = $test; Probes = $probes; Steps = $steps }
}

# The 40 everyday tasks of NP-9 (DESIGN.md 2.12: at least 95 % success with a strong model, reported per rung). Each runs
# in a fresh chat with Tactus on; nothing is sent, bought or deleted. Keep a scratch folder Documents\tactus-np9.
$np9 = @(
    'Notepad: write a three-line shopping list and save it as Documents\tactus-np9\list.txt.',
    'Notepad: open list.txt, add "milk" as a fourth line, save.',
    'Notepad: use Find to count how often "milk" appears in list.txt.',
    'Calculator: work out 17.5 % of 2480 and tell me the result.',
    'Calculator: convert 72 degrees Fahrenheit to Celsius with its converter.',
    'Calculator: count the days between 1 January 2026 and 12 October 2026 (date calculation).',
    'File Explorer: rename Documents\tactus-np9\list.txt to groceries.txt.',
    'File Explorer: make a folder "images" in tactus-np9.',
    'File Explorer: sort Downloads by date modified and tell me the newest file name.',
    'File Explorer: tell me the size of the tactus-np9 folder.',
    'Paint: draw a rectangle and save it as tactus-np9\box.png.',
    'Paint: open box.png, resize it to 50 %, save it as box-small.png.',
    'File Explorer: copy box.png into the images folder.',
    'File Explorer: compress the images folder to images.zip.',
    'File Explorer: extract images.zip into images-copy.',
    'Photos: open box.png and tell me its pixel size.',
    'Edge: open example.com and read me its heading.',
    'Edge: search for "Windows 11 keyboard shortcuts" and list the first three result titles.',
    'Edge: open wikipedia.org in a new tab and tell me the title of today''s featured article.',
    'Edge: open example.com and wikipedia.org in two tabs, then switch back to the first.',
    'Edge: open the browser settings and tell me the default search engine (read only).',
    'Chrome: open example.com and read me its heading.',
    'Word: new document, a heading and two short paragraphs, the heading bold, saved to tactus-np9\notes.docx.',
    'Word: in notes.docx replace every "the" with "THE".',
    'Word: insert a 3 by 3 table and fill its first row with A, B, C.',
    'Excel: put 12, 7, 30, 5 and 46 in A1:A5, a SUM in A6, tell me the total.',
    'Excel: put prices in B1:B5 and format them as currency.',
    'Excel: make a column chart of A1:A5.',
    'Excel: sort a ten-row list by its second column.',
    'VS Code: open the tactus-np9 folder and create hello.py that prints hello.',
    'VS Code: find every file in the folder that contains "hello".',
    'VS Code: rename hello.py to main.py in its Explorer pane.',
    'Outlook: write a draft to yourself with the subject "Tactus NP-9" (do not send it).',
    'Outlook: find the newest email from yourself and read me its subject.',
    'Outlook calendar: add a 30-minute event "Tactus test" tomorrow at 10:00 with no attendees.',
    'Clock: start a one-minute timer.',
    'Task Manager: tell me which app uses the most memory (read only).',
    'Settings (read only): tell me this PC''s display scale on each monitor.',
    'Two apps at once: copy the Excel total into groceries.txt in Notepad.',
    'Two apps at once: put example.com''s heading from Edge into notes.docx in Word.'
)

$items = @(
    (Item 'S1' 'auto' 'Notepad: type two lines and save to a temp file' "$ns.PcSmokeAppsNativeTests.S1_" @('s1-notepad') @(
        'Hands off. Notepad opens, gets two lines and is saved through the Save as dialog to %TEMP%\tactus-smoke.')),
    (Item 'S2' 'auto' 'Calculator: 12 + 30 = by element clicks, 42 read from the tree' "$ns.PcSmokeAppsNativeTests.S2_" @('s2-calculator') @(
        'Hands off. Calculator must be in Standard mode.')),
    (Item 'S3' 'auto' 'File Explorer: rename a file through its own UI' "$ns.PcSmokeAppsNativeTests.S3_" @('s3-explorer') @(
        'Hands off. A temp folder opens; tactus-before.txt becomes tactus-after.txt.')),
    (Item 'S4' 'guided' 'Paint: a borrowed drag draws a line (R4)' "$ns.PcSmokeParallelNativeTests.S4_" @('s4-paint') @(
        'Hands off the mouse and keyboard: this item borrows them (real input), with Paint''s proofs assumed for the test.',
        'Then open %TEMP%\tactus-smoke\s4-after.jpg: a line must be on the canvas.')),
    (Item 'S5' 'auto' 'A covered window is still captured' "$ns.PcSmokeAppsNativeTests.S5_" @('s5-covered') @(
        'Hands off. Then open %TEMP%\tactus-smoke\s5-covered.jpg: it must show Notepad with TACTUS COVERED WINDOW MARKER, not the cover.')),
    (Item 'S6' 'guided' 'Point clicks land on a 100 % and a 150 % monitor' "$ns.PcSmokeAppsNativeTests.S6_" @('s6-dpi') @(
        'Needs two monitors at different scales. When asked, drag "Tactus smoke dpi B" to the other monitor within 15 s.')),
    (Item 'S7' 'guided' 'Parallel use: long batches while you type; Esc does not stop them' "$ns.PcSmokeParallelNativeTests.S7_Long_batches" @('s7-parallel') @(
        'When asked, click into "Tactus smoke desk" and keep typing, moving the mouse and pressing Esc there for 45 s.',
        'Your typing must arrive whole in the desk window; every batch must finish.')),
    (Item 'S7b' 'guided' 'Parallel use: Ctrl+Alt+Esc stops a running batch' "$ns.PcSmokeParallelNativeTests.S7_The_stop_chord" @('s7-stop') @(
        'When asked, press Ctrl+Alt+Esc once.')),
    (Item 'S8' 'auto' 'A password field: typing is refused' "$ns.PcSmokeAppsNativeTests.S8_" @('s8-password') @(
        'Hands off. A small window with a password box opens; nothing may be typed into it.')),
    (Item 'S9' 'guided' 'A UAC prompt: secure_desktop' "$ns.PcSmokeAppsNativeTests.S9_" @('s9-uac') @(
        'A UAC prompt for cmd.exe appears. Wait for the RESULT line, then answer No.')),
    (Item 'S10' 'chat' 'End to end from one chat per provider' $null @() @(
        'In nth: Settings, Tactus on (real input stays off). For each of: Claude Code CLI, Codex CLI, one ACP agent, Z.ai (GLM vision), OpenRouter, a local text-only model, Pi or Command Code:',
        '  new chat, ask: "Open Notepad, type hello from <provider>, and tell me the window title."',
        '  Expect: the approval card for Notepad, the text typed, the Tactus rows in the timeline, the right answer.',
        '  Expect no provider prompt of its own for the nth-pc tools (Codex: no computer-use plugin; ACP and Copilot: no permission ask for nth-pc).',
        '  Vision models get the screenshot; the text-only model works from the tree; Pi and Command Code mention a saved screenshot file.',
        'Record pass or fail per provider in the note.')),
    (Item 'NP-1' 'guided' 'Rung matrix: Invoke, SetValue, EM_REPLACESEL, WM_CHAR, posted click' "$ns.PcRungMatrixNativeTests" @('np1-rungs') @(
        'Before: open what you have of Word (blank document), Excel (blank workbook), Chrome and Edge (new tab), VS Code (untitled file), Slack (your own DM).',
        'Hands off. The probe types short "np1" markers into their text fields and presses no Enter; close the documents without saving after.')),
    (Item 'NP-2' 'guided' 'Chromium and Electron tree fill and staleness' "$ns.PcTreeFillNativeTests" @('np2-tree-fill') @(
        'Before: open VS Code and Slack if you have them. Chrome and Edge get a new window with a local test page. Hands off.')),
    (Item 'NP-4' 'guided' 'Borrow timing and restore, first run (apps on the 100 % monitor)' "$ns.PcBorrowProbeNativeTests" @('np4-borrow') @(
        'When asked, click into the "Tactus smoke desk" text box; then hands off for about two minutes.',
        'The proofs of this run are kept as %TEMP%\tactus-smoke\np4-proofs-NP-4.json.')),
    (Item 'NP-4b' 'guided' 'Borrow timing and restore, second run (apps on the 150 % monitor)' "$ns.PcBorrowProbeNativeTests" @('np4-borrow') @(
        'Make the 150 % monitor the main display for this run (or move each test app there as it opens), then as NP-4.',
        'Kept as np4-proofs-NP-4b.json. Only pairs proven in both files may be imported.')),
    (Item 'NP-5' 'auto' 'Child sessions: state read only' "$ns.PcChildSessionNativeTests" @('np5-child-sessions') @(
        'Nothing is changed. Whether enabling works with Remote Desktop off is tried with P18.')),
    (Item 'NP-5b' 'guided' 'Child sessions turned on by you, with Remote Desktop off: nth reads the workspace Ready' "$ns.PcWorkspaceNativeTests.NP5_" @('np5-workspace-ready') @(
        'Before Enter: in nth, Settings, Tactus, Separate workspace, Set up. Copy the setup command, open Terminal (Admin) yourself, paste it and run it.',
        'The test only reads: child sessions on, Remote Desktop and TermService as they were, and what Settings shows. It changes nothing.',
        'To undo later, run the undo command from the same panel in Terminal (Admin). Leave child sessions on for NP-6 and NP-7.')),
    (Item 'NP-6' 'spike' 'Workspace agent start and real input inside while you type on the desk' $null @() @(
        'On a DEBUG build: scripts\pc-control-overlay-spike.ps1 -Exe <DEBUG nth.exe> -Only np6-workspace, and follow its steps.',
        'Note its RESULT line and whether your desk typing arrived whole.')),
    (Item 'NP-7' 'spike' 'Workspace view cloaked: rendering and input' $null @() @(
        'On a DEBUG build: scripts\pc-control-overlay-spike.ps1 -Exe <DEBUG nth.exe> -Only np7-cloaked.',
        'Note its RESULT line. The separate workspace stays a preview (off) until NP-5b, NP-6, NP-7 and NP-11 pass.')),
    (Item 'NP-8' 'auto' 'Latency: the ten-field form plus Save as one batch' "$ns.PcLatencyNativeTests.NP8_The_ten_field" @('np8-form') @(
        'Hands off. Targets: 0.6 s p50, 1.5 s p95 tool time.')),
    (Item 'NP-8b' 'guided' 'Overhead of Tactus idle in Chrome, Edge, VS Code and Slack' "$ns.PcLatencyNativeTests.NP8_Tactus_idle" @('np8-overhead') @(
        'Before: open whichever you have, each on a quiet page or file. Hands off for about a minute per app. Budget: 3 % CPU, 50 MB.')),
    (Item 'NP-8c' 'guided' 'GPU cost of a capture pool on a playing video' "$ns.PcLatencyNativeTests.NP8_A_capture_pool" @('np8-video') @(
        'Before: play a video in Edge (one window). When asked, read the GPU column in Task Manager for Edge and testhost for 30 s; put both in the note.')),
    (Item 'NP-9' 'chat' 'The 40-task real suite' $null @() (@('In nth with a strong model and Tactus on, one fresh chat per task. Note per task: done or not, the model turns, and the rungs the audit log shows.') + ($np9 | ForEach-Object -Begin { $n = 0 } -Process { $n++; "  $n. $_" }))),
    (Item 'NP-10' 'guided' 'IME composition signals (Hindi, Urdu)' "$ns.PcImeProbeNativeTests" @('np10-ime') @(
        'When asked: in Notepad (and Word, Chrome, Outlook if open) switch to Hindi, compose two words, then Urdu, two words, within 90 s.')),
    (Item 'NP-11' 'spike' 'ActiveX hosting spike in Avalonia 12' $null @() @(
        'On a DEBUG build: scripts\pc-control-overlay-spike.ps1 -Exe <DEBUG nth.exe> -Only np11-rdp (after NP-5b for the connected phases).',
        'Note its RESULT line.')),
    (Item 'NP-12' 'guided' 'Quiet peek of minimized apps' "$ns.PcPeekProbeNativeTests" @('np12-peek') @(
        'Before: open Outlook, Excel and Chrome if you have them. Each is minimized, then peeked after a 3 s warning.',
        'Watch the screen and the taskbar at each peek; in the note say per app whether you saw an animation, a flash or a taskbar change.')),
    (Item 'NP-13' 'guided' 'Lock screen' "$ns.PcLockedSessionNativeTests.NP13_Lock_screen" @('np13-lock') @(
        'When asked, press Win+L and stay locked for 45 s, then unlock.')),
    (Item 'NP-13b' 'guided' 'Display off' "$ns.PcLockedSessionNativeTests.NP13_Display_off" @('np13-display-off') @(
        'When asked, turn the monitor off (its power button) for 45 s, then on.')),
    (Item 'NP-13c' 'guided' 'Remote Desktop disconnect' "$ns.PcLockedSessionNativeTests.NP13_Remote_Desktop" @('np13-disconnect') @(
        'Only if you reach this PC by Remote Desktop: start the script in that session, and when asked close the Remote Desktop window (disconnect) for 45 s, then reconnect.')),
    (Item 'NP-14' 'guided' 'Stop chord behind an elevated window' "$ns.PcStopReachNativeTests.NP14_Behind_an_elevated" @('np14-elevated') @(
        'Before: open Task Manager or Terminal as administrator. When asked, bring it to the front, type in it for 10 s, then press Ctrl+Alt+Esc.')),
    (Item 'NP-14b' 'guided' 'Stop chord behind an exclusive-fullscreen game' "$ns.PcStopReachNativeTests.NP14_Behind_an_exclusive" @('np14-fullscreen') @(
        'Before: start a game in exclusive fullscreen if you have one. When asked, switch to it, play for 10 s, then press Ctrl+Alt+Esc.')),
    (Item 'NP-14c' 'app' 'Pill Stop and tray Stop with an elevated window in front' $null @() @(
        'In nth with Tactus on: start a long Tactus task in a chat ("type the numbers 1 to 300 in Notepad, one per line").',
        'Bring an elevated window to the front; press the pill''s Stop. Start it again; use the tray icon''s Stop. Both must stop it at once.')),
    (Item 'NP-3' 'guided' 'Synthetic pen (runs last; decides only whether R3 is ever switched on)' "$ns.PcPenProbeNativeTests" @('np3-pen') @(
        'Hands off the mouse. Paint opens; a short pen stroke is injected. Open %TEMP%\tactus-smoke\np3-after.jpg for the stroke.'))
)

if ($List) {
    foreach ($item in $items) { Write-Host ("{0,-7} {1,-8} {2}" -f $item.Id, $item.Kind, $item.Title) }
    return
}

if (-not $NoBuild) {
    Write-Host 'Building Marrow.App.Tests ...' -ForegroundColor Cyan
    & dotnet build $project --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw 'The build failed.' }
}

function Run-Test($item) {
    foreach ($probe in $item.Probes) { Remove-Item (Join-Path $logs "$probe.log") -ErrorAction SilentlyContinue }
    $out = Join-Path $logs "$($item.Id)-dotnet.txt"
    $process = Start-Process dotnet -ArgumentList @('test', "`"$project`"", '--no-build', '--nologo', '--filter', "`"FullyQualifiedName~$($item.Test)`"") `
        -NoNewWindow -PassThru -RedirectStandardOutput $out
    $seen = @{}
    while ($true) {
        $done = $process.HasExited
        foreach ($probe in $item.Probes) {
            $log = Join-Path $logs "$probe.log"
            if (-not (Test-Path $log)) { continue }
            $lines = @(Get-Content $log)
            $from = [int]$seen[$probe]
            for ($i = $from; $i -lt $lines.Count; $i++) {
                $color = if ($lines[$i] -like 'RESULT*') { 'Green' } else { 'DarkGray' }
                Write-Host $lines[$i] -ForegroundColor $color
            }
            $seen[$probe] = $lines.Count
        }
        if ($done) { break }
        Start-Sleep -Milliseconds 300
    }
    $verdict = Get-Content $out | Select-String -Pattern 'Passed!|Failed!|No test matches' | Select-Object -First 1
    if ($verdict) { Write-Host $verdict.Line -ForegroundColor Cyan }
    $results = foreach ($probe in $item.Probes) {
        $log = Join-Path $logs "$probe.log"
        if (Test-Path $log) { Get-Content $log | Where-Object { $_ -like 'RESULT*' } }
    }
    [pscustomobject]@{ Test = if ($verdict) { $verdict.Line.Trim() } else { 'no test result' }; Results = @($results) }
}

"Tactus smoke $(Get-Date -Format s) on $env:COMPUTERNAME" | Set-Content $summary
foreach ($item in $items) {
    if ($Only.Count -gt 0 -and $Only -notcontains $item.Id) { continue }
    Write-Host ''
    Write-Host "== $($item.Id) [$($item.Kind)] $($item.Title)" -ForegroundColor Cyan
    $item.Steps | ForEach-Object { Write-Host "  $_" }
    if ($item.Kind -eq 'blocked') {
        Add-Content $summary "$($item.Id) blocked: $($item.Steps -join ' ')"
        continue
    }
    $go = Read-Host 'Enter to start, s to skip'
    if ($go -eq 's') {
        Add-Content $summary "$($item.Id) skipped"
        continue
    }
    $run = $null
    if ($item.Test) { $run = Run-Test $item }
    if ($item.Id -like 'NP-4*') {
        Copy-Item (Join-Path $logs 'np4-proofs.json') (Join-Path $logs "np4-proofs-$($item.Id).json") -ErrorAction SilentlyContinue
    }
    $answer = Read-Host 'Verdict: p pass, f fail, s skip'
    $note = Read-Host 'Note (what you saw; Enter for none)'
    $word = switch ($answer) { 'p' { 'pass' } 'f' { 'FAIL' } default { 'skip' } }
    Add-Content $summary "$($item.Id) $word - $($item.Title)"
    if ($run) {
        Add-Content $summary "  test: $($run.Test)"
        $run.Results | ForEach-Object { Add-Content $summary "  $_" }
    }
    if ($note) { Add-Content $summary "  owner: $note" }
}
Write-Host ''
Write-Host "Summary: $summary" -ForegroundColor Green
