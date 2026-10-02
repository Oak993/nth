# Scripted stand-in MCP server over stdio for MCPHubDeadlineTests (R-TEST-10: a system binary, powershell.exe,
# running this script; no real AI CLI, no network). It answers only what MCPHub's client sends, and it holds every
# tools/list page and every "slow" tools/call until the test releases it, so the test decides (on the manual clock)
# how long the hub has waited when the reply finally comes.
#   MCP_GATE_DIR   a scratch folder the test and this script share.
#   MCP_PAGES      how many tools/list pages there are (default 1); page k is tool "t<k>", cursor "p<k+1>".
# Signals, all plain files in MCP_GATE_DIR:
#   seen-list-<k>    written when page k's request has been read      release-list-<k>  the test lets page k answer
#   seen-call-<k>    written when the k-th "slow" call has been read  release-call-<k>  the test lets it answer
#   quit             the script exits (the test's clean-up)
# tools/call "quick" answers at once; any other tool name is a "slow" call and answers "done-<k>" once released.
# Every reply is one compact JSON object per line, flushed immediately.
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$out = [Console]::Out
$dir = $env:MCP_GATE_DIR
$pages = 1
if (-not [string]::IsNullOrEmpty($env:MCP_PAGES)) { $pages = [int]$env:MCP_PAGES }

function Send($obj) {
    $json = ($obj | ConvertTo-Json -Depth 12 -Compress)
    $out.Write($json)
    $out.Write("`n")
    $out.Flush()
}

function Signal($name) {
    New-Item -ItemType File -Path (Join-Path $dir $name) -Force | Out-Null
}

# Waits for the test's release file; ends the script when the test says quit (or after a minute, a hang guard).
function WaitFor($name) {
    $path = Join-Path $dir $name
    for ($i = 0; $i -lt 3000; $i++) {
        if (Test-Path $path) { return }
        if (Test-Path (Join-Path $dir 'quit')) { exit 0 }
        Start-Sleep -Milliseconds 20
    }
    exit 0
}

$listCount = 0
$callCount = 0
while ($true) {
    $line = [Console]::In.ReadLine()
    if ($null -eq $line) { break }
    if ($line.Trim().Length -eq 0) { continue }
    try { $msg = $line | ConvertFrom-Json } catch { continue }
    $id = $msg.id
    switch ($msg.method) {
        'initialize' {
            Send @{ jsonrpc = '2.0'; id = $id; result = @{ protocolVersion = '2025-06-18' } }
        }
        'notifications/initialized' { }
        'tools/list' {
            $listCount++
            $k = $listCount
            Signal "seen-list-$k"
            WaitFor "release-list-$k"
            $result = @{ tools = @(@{ name = "t$k" }) }
            if ($k -lt $pages) { $result.nextCursor = "p$($k + 1)" }
            Send @{ jsonrpc = '2.0'; id = $id; result = $result }
        }
        'tools/call' {
            if ($msg.params.name -eq 'quick') {
                Send @{ jsonrpc = '2.0'; id = $id; result = @{ content = @(@{ type = 'text'; text = 'quick' }) } }
            } else {
                $callCount++
                $k = $callCount
                Signal "seen-call-$k"
                WaitFor "release-call-$k"
                Send @{ jsonrpc = '2.0'; id = $id; result = @{ content = @(@{ type = 'text'; text = "done-$k" }) } }
            }
        }
        default {
            Send @{ jsonrpc = '2.0'; id = $id; error = @{ code = -32601; message = "unknown method $($msg.method)" } }
        }
    }
}
