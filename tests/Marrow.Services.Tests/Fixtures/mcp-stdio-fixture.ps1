# Scripted stand-in MCP server over stdio for MCPHubTests / MCPClientTests (R-TEST-10: a system binary,
# powershell.exe, running this script; no real AI CLI). It speaks only the handful of JSON-RPC messages
# MCPHub.swift's MCPClient sends (initialize, notifications/initialized, tools/list, tools/call), plus a few
# test-only methods that let the tests drive the timeout, error and exit-code paths:
#   test/noreply  - accepted, never answered (drives "The server didn't answer in time.")
#   test/error    - answered with a JSON-RPC error object (drives checkReply's "unknown error" fallback too
#                   when the caller sends test/error/blank)
#   test/exit     - the process exits with code 7 (drives "The server quit (exit 7).")
# Every reply is one compact JSON object per line, flushed immediately.
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$out = [Console]::Out

function Send($obj) {
    $json = ($obj | ConvertTo-Json -Depth 12 -Compress)
    $out.Write($json)
    $out.Write("`n")
    $out.Flush()
}

while ($true) {
    $line = [Console]::In.ReadLine()
    if ($null -eq $line) { break }
    if ($line.Trim().Length -eq 0) { continue }
    try { $msg = $line | ConvertFrom-Json } catch { continue }
    $method = $msg.method
    $id = $msg.id

    switch ($method) {
        'initialize' {
            Send @{ jsonrpc = '2.0'; id = $id; result = @{ protocolVersion = '2025-06-18' } }
        }
        'notifications/initialized' {
            # no reply: a notification carries no id
        }
        'tools/list' {
            $cursor = $msg.params.cursor
            $solo = $env:MCP_FIXTURE_TOOL
            if (-not [string]::IsNullOrEmpty($solo)) {
                Send @{ jsonrpc = '2.0'; id = $id; result = @{ tools = @(@{ name = $solo }) } }
            } elseif ([string]::IsNullOrEmpty($cursor)) {
                Send @{
                    jsonrpc = '2.0'; id = $id
                    result  = @{
                        tools      = @(
                            @{ name = 'echo'; description = ('x' * 500); inputSchema = @{ type = 'object'; properties = @{ text = @{ type = 'string' } } } },
                            @{ name = 'blank'; description = $null }
                        )
                        nextCursor = 'page-2'
                    }
                }
            } else {
                Send @{ jsonrpc = '2.0'; id = $id; result = @{ tools = @(@{ name = 'second-page' }) } }
            }
        }
        'tools/call' {
            $callArgs = $msg.params.arguments
            $toolName = $msg.params.name
            if ($toolName -eq 'error-tool') {
                Send @{ jsonrpc = '2.0'; id = $id; result = @{ content = @(@{ type = 'text'; text = 'failed' }); isError = $true } }
            } elseif ($toolName -eq 'exit-server') {
                exit 7
            } elseif ($toolName -eq 'mixed-content') {
                Send @{
                    jsonrpc = '2.0'; id = $id
                    result  = @{
                        content = @(
                            @{ type = 'text'; text = 'first line' },
                            @{ type = 'image' },
                            @{ type = 'resource'; resource = @{ uri = 'file:///r.txt' } },
                            @{ type = 'resource' },
                            @{ type = 'other' }
                        )
                    }
                }
            } else {
                Send @{ jsonrpc = '2.0'; id = $id; result = @{ content = @(@{ type = 'text'; text = ("echo:" + ($callArgs | ConvertTo-Json -Compress)) }) } }
            }
        }
        'test/noreply' {
            # intentionally silent
        }
        'test/error' {
            Send @{ jsonrpc = '2.0'; id = $id; error = @{ code = -1; message = 'boom' } }
        }
        'test/error-blank' {
            Send @{ jsonrpc = '2.0'; id = $id; error = @{ code = -1 } }
        }
        'test/exit' {
            exit 7
        }
        default {
            Send @{ jsonrpc = '2.0'; id = $id; error = @{ code = -32601; message = "unknown method $method" } }
        }
    }
}
