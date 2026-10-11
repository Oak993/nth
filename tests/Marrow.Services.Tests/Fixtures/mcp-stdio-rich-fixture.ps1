# Scripted stand-in MCP server over stdio for the W18 client upgrade tests (MCPClientUpgradeTests; web surfaces
# DESIGN.md 7.6): a system binary, powershell.exe, running this script; no real AI CLI and no network.
# It speaks the handshake, paged tool/resource lists, resources/read, and answers tools/call by name. A few tools make
# the server talk first, so the client's inbound handling can be observed:
#   change-tools      switches the tool list (adds "fresh-tool") and sends notifications/tools/list_changed, then replies.
#   change-resources  sends notifications/resources/list_changed, then replies.
#   inbound-ping      sends the client a request {id: 900, method: "ping"}, reads the client's answer line, and replies
#                     with that answer as text.
#   inbound-unknown   sends the client a request {id: 901, method: "roots/list"} and replies with the answer as text.
#   id-collision      sends the client a request that uses the SAME id as the pending tools/call, reads the answer, and
#                     only then sends the real reply: a client that took the request for a reply would return early.
# Every reply is one compact JSON object per line, flushed immediately.
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$out = [Console]::Out
$freshTools = $false
$freshResources = $false
$initParams = ''

function Send($obj) {
    $json = ($obj | ConvertTo-Json -Depth 12 -Compress)
    $out.Write($json)
    $out.Write("`n")
    $out.Flush()
}

function ToolList() {
    $tools = @(
        @{ name = 'plain'; description = 'text only' },
        @{ name = 'board'; description = 'a board'; _meta = @{ ui = @{ resourceUri = 'ui://board/view.html' } } },
        @{ name = 'flat'; description = 'old flat key'; _meta = @{ 'ui/resourceUri' = 'ui://flat/view.html' } },
        @{ name = 'openai'; description = 'openai key'; _meta = @{ 'openai/outputTemplate' = 'ui://openai/view.html' } },
        @{ name = 'refresh'; description = 'app only'; _meta = @{ ui = @{ resourceUri = 'ui://board/view.html'; visibility = @('app') } } },
        @{ name = 'modelonly'; description = 'model only'; _meta = @{ ui = @{ visibility = @('model') } } },
        @{ name = 'bad-uri'; description = 'not a ui address'; _meta = @{ ui = @{ resourceUri = 'https://evil.example/x' } } },
        @{ name = 'picture' }, @{ name = 'links' }, @{ name = 'structured-only' }, @{ name = 'client-caps' },
        @{ name = 'change-tools' }, @{ name = 'change-resources' },
        @{ name = 'inbound-ping' }, @{ name = 'inbound-unknown' }, @{ name = 'id-collision' }
    )
    if ($script:freshTools) { $tools += @{ name = 'fresh-tool' } }
    return $tools
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
            $script:initParams = ($msg.params | ConvertTo-Json -Depth 10 -Compress)
            $caps = @{ tools = @{ listChanged = $true }; resources = @{ listChanged = $true } }
            if ($env:MCP_RICH_CAPS -eq 'none-resources') { $caps = @{ tools = @{ } } }
            Send @{ jsonrpc = '2.0'; id = $id; result = @{ protocolVersion = '2025-06-18'; capabilities = $caps } }
        }
        'notifications/initialized' { }
        'tools/list' {
            Send @{ jsonrpc = '2.0'; id = $id; result = @{ tools = @(ToolList) } }
        }
        'resources/list' {
            $cursor = $msg.params.cursor
            if ([string]::IsNullOrEmpty($cursor)) {
                $first = @(@{ uri = 'file:///notes.txt'; name = 'notes'; title = 'Notes'; description = 'plain notes'; mimeType = 'text/plain'; size = 12 })
                if ($script:freshResources) { $first += @{ uri = 'file:///fresh.txt'; name = 'fresh' } }
                Send @{ jsonrpc = '2.0'; id = $id; result = @{ resources = $first; nextCursor = 'page-2' } }
            } else {
                Send @{ jsonrpc = '2.0'; id = $id; result = @{
                    resources = @(@{ uri = 'ui://board/view.html'; name = 'board-view'; mimeType = 'text/html;profile=mcp-app'; _meta = @{ ui = @{ prefersBorder = $true } } }) } }
            }
        }
        'resources/templates/list' {
            Send @{ jsonrpc = '2.0'; id = $id; result = @{
                resourceTemplates = @(@{ uriTemplate = 'file:///logs/{day}.txt'; name = 'logs'; description = 'daily logs'; mimeType = 'text/plain' }) } }
        }
        'resources/read' {
            $uri = $msg.params.uri
            if ($uri -eq 'ui://board/view.html') {
                Send @{ jsonrpc = '2.0'; id = $id; result = @{ contents = @(@{
                    uri = $uri; mimeType = 'text/html;profile=mcp-app'; text = '<html>board</html>';
                    _meta = @{ ui = @{
                        csp = @{ connectDomains = @('api.example.com'); resourceDomains = @('cdn.example.com', '*.example.org') };
                        permissions = @{ camera = @{ }; geolocation = @{ } };
                        domain = 'board.example.com'; prefersBorder = $true } } }) } }
            } elseif ($uri -eq 'ui://blob/view.html') {
                $b64 = [Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes('<html>blob</html>'))
                Send @{ jsonrpc = '2.0'; id = $id; result = @{ contents = @(@{ uri = $uri; mimeType = 'text/html;profile=mcp-app'; blob = $b64 }) } }
            } elseif ($uri -eq 'ui://wrong/mime') {
                Send @{ jsonrpc = '2.0'; id = $id; result = @{ contents = @(@{ uri = $uri; mimeType = 'text/html'; text = '<html>plain page</html>' }) } }
            } elseif ($uri -eq 'file:///notes.txt') {
                Send @{ jsonrpc = '2.0'; id = $id; result = @{ contents = @(@{ uri = $uri; mimeType = 'text/plain'; text = 'hello notes' }) } }
            } else {
                Send @{ jsonrpc = '2.0'; id = $id; error = @{ code = -32002; message = 'Resource not found' } }
            }
        }
        'tools/call' {
            $toolName = $msg.params.name
            if ($toolName -eq 'plain') {
                Send @{ jsonrpc = '2.0'; id = $id; result = @{ content = @(@{ type = 'text'; text = 'plain-ok' }) } }
            } elseif ($toolName -eq 'board' -or $toolName -eq 'refresh' -or $toolName -eq 'modelonly' -or $toolName -eq 'flat' -or $toolName -eq 'openai' -or $toolName -eq 'bad-uri') {
                Send @{ jsonrpc = '2.0'; id = $id; result = @{
                    content = @(@{ type = 'text'; text = ('ran:' + $toolName) });
                    structuredContent = @{ columns = @('todo', 'done'); count = 3 };
                    _meta = @{ viewState = 'abc' } } }
            } elseif ($toolName -eq 'picture') {
                Send @{ jsonrpc = '2.0'; id = $id; result = @{ content = @(
                    @{ type = 'text'; text = 'here is the chart' },
                    @{ type = 'image'; data = 'iVBORw0KGgo='; mimeType = 'image/png' }) } }
            } elseif ($toolName -eq 'links') {
                Send @{ jsonrpc = '2.0'; id = $id; result = @{ content = @(
                    @{ type = 'resource_link'; uri = 'file:///report.pdf'; name = 'report'; mimeType = 'application/pdf'; description = 'the report' },
                    @{ type = 'resource'; resource = @{ uri = 'file:///inline.txt'; mimeType = 'text/plain'; text = 'inline words' } },
                    @{ type = 'resource'; resource = @{ uri = 'ui://board/view.html'; mimeType = 'text/html;profile=mcp-app'; text = '<html>do not show the model</html>' } }) } }
            } elseif ($toolName -eq 'client-caps') {
                Send @{ jsonrpc = '2.0'; id = $id; result = @{ content = @(@{ type = 'text'; text = $script:initParams }) } }
            } elseif ($toolName -eq 'structured-only') {
                Send @{ jsonrpc = '2.0'; id = $id; result = @{ content = @(); structuredContent = @{ answer = 42 } } }
            } elseif ($toolName -eq 'change-tools') {
                $script:freshTools = $true
                Send @{ jsonrpc = '2.0'; method = 'notifications/tools/list_changed' }
                Send @{ jsonrpc = '2.0'; id = $id; result = @{ content = @(@{ type = 'text'; text = 'changed' }) } }
            } elseif ($toolName -eq 'change-resources') {
                $script:freshResources = $true
                Send @{ jsonrpc = '2.0'; method = 'notifications/resources/list_changed' }
                Send @{ jsonrpc = '2.0'; id = $id; result = @{ content = @(@{ type = 'text'; text = 'changed' }) } }
            } elseif ($toolName -eq 'inbound-ping') {
                Send @{ jsonrpc = '2.0'; id = 900; method = 'ping' }
                $answer = [Console]::In.ReadLine()
                Send @{ jsonrpc = '2.0'; id = $id; result = @{ content = @(@{ type = 'text'; text = ('answer:' + $answer) }) } }
            } elseif ($toolName -eq 'inbound-unknown') {
                Send @{ jsonrpc = '2.0'; id = 901; method = 'roots/list' }
                $answer = [Console]::In.ReadLine()
                Send @{ jsonrpc = '2.0'; id = $id; result = @{ content = @(@{ type = 'text'; text = ('answer:' + $answer) }) } }
            } elseif ($toolName -eq 'id-collision') {
                Send @{ jsonrpc = '2.0'; id = $id; method = 'ping' }
                $answer = [Console]::In.ReadLine()
                Send @{ jsonrpc = '2.0'; id = $id; result = @{ content = @(@{ type = 'text'; text = ('real-reply-after:' + $answer) }) } }
            } else {
                Send @{ jsonrpc = '2.0'; id = $id; result = @{ content = @(@{ type = 'text'; text = ('echo:' + $toolName) }) } }
            }
        }
        default {
            if ($null -ne $id) {
                Send @{ jsonrpc = '2.0'; id = $id; error = @{ code = -32601; message = "unknown method $method" } }
            }
        }
    }
}
