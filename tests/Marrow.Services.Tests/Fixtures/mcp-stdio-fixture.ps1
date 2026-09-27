# Scripted stand-in MCP server over stdio for MCPHubTests / MCPClientTests (R-TEST-10: a system binary,
# powershell.exe, running this script; no real AI CLI). It speaks only the handful of JSON-RPC messages
# MCPHub.swift's MCPClient sends (initialize, notifications/initialized, tools/list, tools/call), plus a few
# test-only methods that let the tests drive the timeout, error and exit-code paths:
#   test/noreply  - accepted, never answered (drives "The server didn't answer in time.")
#   test/error    - answered with a JSON-RPC error object (drives checkReply's "unknown error" fallback too
#                   when the caller sends test/error/blank)
#   test/exit     - the process exits with code 7 (drives "The server quit (exit 7).")
# Two tools/call names support B3-only lane A15 coverage (MCPWire/MCPStdioConnection, PR-MCP-186/PR-MCP-237):
#   which-id              - replies with the JSON-RPC request's own id echoed into the text content, so a test
#                           can prove a relaunched connection's ids restart at 1 (PR-MCP-237) without any other
#                           observable channel (the id MCPHub assigns is otherwise invisible to the caller).
#   initialize-id         - reports the actual initialize id this process received, independently of
#                           which-id's counter observation (MCPHub.swift:288,295; MCPWire.swift:126,164-168).
#   string-id-then-real   - first sends a reply for this request whose "id" is a JSON STRING holding the same
#                           digits, which RPCID.Create/Handle must NOT match to the waiting int-keyed request
#                           (PR-MCP-186 b3_note, "a string id no longer matches"); then, after a short delay,
#                           sends the real numeric-id reply. A test asserts it receives the real reply's text,
#                           never the string-id one, proving the mismatch was never delivered.
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
            $initializeId = $id
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
            } elseif ($toolName -eq 'which-id') {
                Send @{ jsonrpc = '2.0'; id = $id; result = @{ content = @(@{ type = 'text'; text = ("id:" + $id) }) } }
            } elseif ($toolName -eq 'initialize-id') {
                Send @{ jsonrpc = '2.0'; id = $id; result = @{ content = @(@{ type = 'text'; text = ("initialize-id:" + $initializeId) }) } }
            } elseif ($toolName -eq 'string-id-then-real') {
                Send @{ jsonrpc = '2.0'; id = [string]$id; result = @{ content = @(@{ type = 'text'; text = 'WRONG-STRING-ID' }) } }
                Start-Sleep -Milliseconds 150
                Send @{ jsonrpc = '2.0'; id = $id; result = @{ content = @(@{ type = 'text'; text = ("echo:" + ($callArgs | ConvertTo-Json -Compress)) }) } }
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
