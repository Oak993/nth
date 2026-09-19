# MCP protocol surface and OAuth callback assets (verbatim extraction)

Reference checkout: `E:/Droppy/reference/droppy-code`, commit `dd2b86efef9e77168f304a86e8a8411877578bbe` (read-only).

Files read completely, reconciled against `wc -l`:

| File | Lines | Ranges read |
| --- | --- | --- |
| `DroppyCode/Services/MCP/MCPHub.swift` | 554 | 1-280, 280-554 |
| `DroppyCode/Services/MCP/MCPOAuth.swift` | 929 | 1-240, 240-500, 500-760, 760-929 |
| `DroppyCode/Services/MCP/MCPProxy.swift` | 470 | 1-470 |
| `DroppyCode/Services/MCP/MCPProbe.swift` | 426 | 1-426 |
| `DroppyCode/Services/MCP/MCPStore.swift` | 356 | 1-356 |
| `DroppyCode/Services/MCP/MCPProviderConfig.swift` | 252 | 1-252 |
| `DroppyCode/Services/MCP/MCPBridgeSource.swift` | 252 | 1-252 |
| `DroppyCode/Services/MCP/MCPSessionOverrides.swift` | 84 | 1-84 |
| `DroppyCode/Services/MCP/MCPExternalSync.swift` | 64 | 1-64 |
| `DroppyCode/Services/MCP/MCPKeychain.swift` | 68 | 1-68 |
| `DroppyCode/Core/Models/MCP.swift` | 263 | 1-263 |
| `DroppyCode/Core/Models/MCPCatalog.swift` | 483 | 1-250, 250-483 |
| `DroppyCode/Core/Models/MCPCustom.swift` | 183 | 1-183 |

Total 4384 lines, matching `wc -l`.

Every claim below is labelled **SOURCE FACT**, **ENGINEERING DECISION** or **UNRESOLVED**.

---

## 0. Adjudication of the three critic findings

### 0.1 R037's provider-config rule: critic is PARTLY RIGHT

R037 `feature_notes` states: *"Claude, Codex and Gemini omit an empty env or headers object entirely; Copilot always emits headers, env and tools: [\"\*\"]"*.

**SOURCE FACT.** The source contradicts the Gemini half of that sentence. `MCPProviderConfig.gemini(_:)` (`MCPProviderConfig.swift:126-146`) emits `headers` for a remote server **unconditionally**, with no `isEmpty` guard, while it does guard `env` for a local server:

```swift
126    static func gemini(_ servers: [MCPResolvedServer]) -> JSONValue {
127        var entries: [String: JSONValue] = [:]
128        for server in servers {
129            if server.isRemote {
130                entries[server.id] = .object([
131                    "httpUrl": .string(server.url ?? ""),
132                    "headers": .object(server.headers.mapValues(JSONValue.string)),
133                ])
134            } else {
135                var entry: [String: JSONValue] = [
136                    "command": .string(server.command ?? ""),
137                    "args": .array(server.args.map(JSONValue.string)),
138                ]
139                if !server.env.isEmpty {
140                    entry["env"] = .object(server.env.mapValues(JSONValue.string))
141                }
142                entries[server.id] = .object(entry)
143            }
144        }
145        return .object(["mcpServers": .object(entries)])
146    }
```

So a Gemini remote entry for a server with no headers is written as `{"headers":{},"httpUrl":"<url>"}`: the empty object is present. R037's own `persisted_fields` row already says `Gemini config: root "mcpServers"; remote {httpUrl, headers}; local {command, args, env?}`, which is exactly right; only the prose note is wrong. The Claude and Codex halves of the note are correct (`MCPProviderConfig.swift:12-14`, `21-23`, `35-37`, `44-46`).

R037 also asserts *"Everything observable is sorted (ids, header keys, env keys, names), which keeps the written files stable between runs"*. **SOURCE FACT, wrong as stated.** `claude`, `codex`, `copilot` and `gemini` never sort anything themselves: they build unordered Swift dictionaries. Key order is stable only because `MCPProviderConfig.write` calls `value.data(pretty: true)` (`MCPProviderConfig.swift:242`) and `JSONValue.data(pretty:)` adds `.sortedKeys` (`DroppyCode/Core/Support/JSONValue.swift:59-63`):

```swift
59    func data(pretty: Bool = false) -> Data {
60        var options: JSONSerialization.WritingOptions = [.fragmentsAllowed, .withoutEscapingSlashes]
61        if pretty { options.formUnion([.prettyPrinted, .sortedKeys]) }
```

`acpServers` and `codexUserServerNames` do sort explicitly (`.keys.sorted()`, `names.sorted()`). `MCPProviderConfig.copilotConfigJSON()` returns `parsed.compactString`, and `compactString` calls `data()` with **no** `pretty` (`JSONValue.swift:65`), so `.sortedKeys` is NOT applied on that path.

**UNRESOLVED (minor):** whether the compact Copilot string handed to `--additional-mcp-config` is byte-stable across runs. It is re-serialized from a parsed `JSONValue` without `.sortedKeys`, so its key order is Foundation's dictionary iteration order.

### 0.2 R038's MCPStore field-value fallback chain: critic is RIGHT, R038 is wrong

R038 `ui_elements` states: *"value(for:of:) is the card field's displayed text: draft first, else the Keychain secret for a .secret field, else the connection's stored value, else field.defaultValue, else the empty string."* That describes a flat four-step chain.

**SOURCE FACT.** The real chain branches on `field.kind` with `if / else if`, so a `.secret` field never consults `connections[...].values`, and a non-secret field never consults the Keychain (`MCPStore.swift:63-71`):

```swift
63    func value(for field: MCPField, of entry: MCPCatalogEntry) -> String {
64        if let draft = drafts[entry.id]?[field.key] { return draft }
65        if field.kind == .secret {
66            if let secret = MCPKeychain.value(server: entry.id, field: field.key) { return secret }
67        } else if let stored = connections[entry.id]?.values[field.key] {
68            return stored
69        }
70        return field.defaultValue ?? ""
71    }
```

The exact chain is:

1. `drafts[entry.id]?[field.key]` if the key is present. An empty-string draft WINS and is returned as `""`; it does not fall through.
2. If `field.kind == .secret`: `MCPKeychain.value(server: entry.id, field: field.key)` if non-nil. `MCPKeychain.value` returns nil for a whitespace-only item and otherwise returns the whitespace-trimmed string (`MCPKeychain.swift:85-89`); it returns nil outright when `CaptureRun.isEnabled` (`MCPKeychain.swift:76`).
3. Else (`.text` or `.path`): `connections[entry.id]?.values[field.key]` if present. An empty stored string is returned as `""`.
4. `field.defaultValue ?? ""`.

There are **three other, different** value chains that must not be conflated with it:

- `storedValues(for:)` (`MCPStore.swift:293-301`): start from `connections[entry.id]?.values ?? [:]`, then for every field with `kind == .secret` overlay `MCPKeychain.value(...)` when non-nil. Drafts are NOT consulted and `field.defaultValue` is NOT applied here. This is what feeds `entry.resolve(values:)`.
- `MCPCatalogEntry.fill(_:values:)` (`MCP.swift:135-150`): per field, `values[field.key].flatMap { $0.isEmpty ? nil : $0 } ?? field.defaultValue ?? ""`. Here an empty value DOES fall through to the default, unlike step 1 and step 3 above.
- `storedValue(server:field:secret:)` (`MCPStore.swift:203-206`): Keychain when `secret` is true, otherwise `connections[id]?.values[field]`; no draft, no default. This is the custom-server editor's prefill path.

**ENGINEERING DECISION for the port:** reproduce all four chains separately; they are not interchangeable.

### 0.3 Missing verbatim OAuth callback assets, protocol field names, unreachable branches and dead APIs

Supplied below: protocol methods and field names in section 1, per-provider config formats in section 2, the OAuth flow in section 3, the verbatim callback page in section 4, the catalog in section 6, unreachable branches in section 8 and dead APIs in section 9.

---

## 1. MCP protocol surface, both directions

### 1.1 Protocol version string

**SOURCE FACT.** The literal `"2025-06-18"` appears in five places in the MCP layer and nowhere else:

- `MCPHub.swift:259` (`initializeParams()` `"protocolVersion"`)
- `MCPHub.swift:461` (`urlRequest.setValue("2025-06-18", forHTTPHeaderField: "MCP-Protocol-Version")`)
- `MCPProbe.swift:67` (`initializeParams()` `"protocolVersion"`)
- `MCPProbe.swift:314` (`request.setValue("2025-06-18", forHTTPHeaderField: "MCP-Protocol-Version")`)
- `MCPBridgeSource.swift:16` (JS `const PROTOCOL_VERSION = "2025-06-18";`)

### 1.2 JSON-RPC methods sent by Droppy Code (outbound), all three clients

**SOURCE FACT.** Exactly **four** method names are ever sent, and there are exactly **three** MCP clients in the codebase (`MCPHub`, `MCPProbe`, the installed JS bridge). There is no `resources/*`, `prompts/*`, `ping`, `completion/*`, `logging/*`, `roots/*`, `sampling/*` or `elicitation/*` anywhere in the MCP layer.

| Method | Kind | Sent by | Params sent |
| --- | --- | --- | --- |
| `initialize` | request | `MCPHub` (stdio + http), `MCPProbe` (stdio + http), bridge JS (stdio + http) | see 1.3 |
| `notifications/initialized` | notification | all three | see 1.4 |
| `tools/list` | request | all three | `{}` or `{"cursor": "<string>"}` |
| `tools/call` | request | `MCPHub`, bridge JS only | `{"name": "<tool>", "arguments": <object>}` |

`MCPProbe` never sends `tools/call`: it only initializes and lists.

### 1.3 `initialize` params, verbatim per client

`MCPHub.initializeParams()` (`MCPHub.swift:257-264`) and `MCPProbe.initializeParams()` (`MCPProbe.swift:65-71`) are byte-identical:

```swift
.object([
    "protocolVersion": .string("2025-06-18"),
    "capabilities": .object([:]),
    "clientInfo": .object(["name": .string("Droppy Code"), "version": .string("1.0")]),
])
```

The bridge JS differs: **no `version` field** (`MCPBridgeSource.swift:138` and `:203`):

```js
await request("initialize", { protocolVersion: PROTOCOL_VERSION, capabilities: {}, clientInfo: { name: "Droppy Code" } });
```

**SOURCE FACT.** Client name is the literal `"Droppy Code"` in all three; client version is the literal string `"1.0"` in Swift and absent in JS. `capabilities` is always an empty object: Droppy Code advertises no client capabilities. (Contrast `ACPSession.swift:127`, a different protocol, which sends `"clientInfo": ["name": "droppy-code", "title": "Droppy Code", "version": .string(AppInfo.version)]`.)

### 1.4 Request and notification envelopes, verbatim

`MCPHub.request(id:method:params:)` (`MCPHub.swift:265-273`) and `MCPProbe.request(id:method:params:)` (`MCPProbe.swift:73-81`) are identical:

```swift
var object: [String: JSONValue] = [
    "jsonrpc": .string("2.0"),
    "id": .int(id),
    "method": .string(method),
]
if let params { object["params"] = params }
```

`notification(method:)` (`MCPHub.swift:275-277`, `MCPProbe.swift:83-85`) is identical in both and carries **no `params` key at all**:

```swift
.object(["jsonrpc": .string("2.0"), "method": .string(method)])
```

The bridge JS notification DOES send `params`, and its callers pass `{}` (`MCPBridgeSource.swift:131-135`, `:139`, `:195-202`, `:204`):

```js
const notify = (method, params) => {
  const message = { jsonrpc: "2.0", method };
  if (params !== undefined) message.params = params;
  child.stdin.write(JSON.stringify(message) + "\n");
};
...
notify("notifications/initialized", {});
```

**SOURCE FACT, divergence to reproduce:** Swift sends `notifications/initialized` with no `params`; the JS bridge sends it with `"params":{}`.

**SOURCE FACT, id numbering.**

- `MCPHub`: per-client monotonic `nextID` starting at **1** (`MCPHub.swift:163`), shared by `initialize` and every later call. `stop()` (`MCPHub.swift:178-189`) clears `process`, `conversation`, `stdinHandle`, `initialized`, `sessionID` and `replies` but **not** `nextID`, so ids keep climbing across a restart of the same client.
- `MCPProbe`: hardcodes `id: 1` for `initialize` (`MCPProbe.swift:192`, `:274`) and starts `nextID = 2` for `tools/list` (`:203`, `:288`).
- Bridge JS: `let id = 0` with post-increment `id++` (`MCPBridgeSource.swift:88`, `:119`, `:186`, `:188`), so the first message on each connection has `"id": 0`.

### 1.5 JSON fields read from server replies (inbound), complete

**SOURCE FACT.** Every response field path the code reads, with the reading site:

| Path read | Where | Use |
| --- | --- | --- |
| `error` (object presence) | `MCPHub.swift:279-283`, `MCPProbe.swift:87-91` | non-nil object means throw |
| `error.message` (string) | `MCPHub.swift:281`, `MCPProbe.swift:89` | error text; fallback literal `"unknown error"` |
| `id` (int) | `MCPHub.swift:397`, `MCPProbe.swift:178` | reply correlation |
| `result` (object) | `MCPHub.swift:199`, `:229`, `:378`, `:433`; `MCPProbe.swift:194`, `:275` | required on `initialize` |
| `result.serverInfo.name` (string) | `MCPProbe.swift:196`, `:277` | `MCPProbeResult.serverName` |
| `result.serverInfo.version` (string) | `MCPProbe.swift:197`, `:278` | `MCPProbeResult.serverVersion`, persisted as `MCPConnection.serverVersion` |
| `result.tools` (array) | `MCPHub.swift:200`, `MCPProbe.swift:101` | tool list |
| `result.tools[].name` (string) | `MCPHub.swift:201`, `MCPProbe.swift:103` | required; the item is skipped when absent |
| `result.tools[].description` (string) | `MCPHub.swift:202`, `MCPProbe.swift:104` | see 1.6 |
| `result.tools[].inputSchema` (object) | `MCPHub.swift:204-208` | see 1.6 |
| `result.nextCursor` (string) | `MCPHub.swift:237`, `MCPProbe.swift:212`, `:296` | pagination; an empty string stops the loop |
| `result.content` (array) | `MCPHub.swift:231` | `tools/call` payload |
| `result.content[].text` (string) | `MCPHub.swift:242` | appended as one line |
| `result.content[].type == "image"` | `MCPHub.swift:235` | appended as the literal `[image]` |
| `result.content[].type == "resource"` | `MCPHub.swift:237` | appended as `[resource: <uri>]` |
| `result.content[].resource.uri` (string) | `MCPHub.swift:237` | the `<uri>`; empty string when absent |
| `result.isError` (bool) | `MCPHub.swift:242` | `MCPHub.Result.isError`; default `false` |

**SOURCE FACT.** `MCPProbe` never reads `content`, `isError`, `type` or `resource`. `MCPHub` never reads `serverInfo`: it discards the server name and version entirely.

`MCPHub.callTool` result assembly, verbatim (`MCPHub.swift:228-245`):

```swift
var lines: [String] = []
for item in result?["content"]?.array ?? [] {
    if let text = item["text"]?.string {
        lines.append(text)
    } else if item["type"]?.string == "image" {
        lines.append("[image]")
    } else if item["type"]?.string == "resource" {
        lines.append("[resource: \(item["resource"]?["uri"]?.string ?? "")]")
    }
}
return MCPHub.Result(
    text: lines.joined(separator: "\n"),
    isError: result?["isError"]?.bool ?? false
)
```

Note the ordering dependency: `text` is tested BEFORE `type`, so a content item that has both a `text` field and `"type": "image"` is rendered as its text. An item with none of the three shapes contributes no line at all.

Bridge JS inbound reads: `message.id` (`:111`), `response.error.message` (`:128`, `:192`), `response.result` (`:129`, `:193`), `result.tools` (`:144`, `:209`), `result.nextCursor` (`:145`, `:210`), `entry.name` (`:61`), `entry.description` (`:41`), `entry.inputSchema` (`:46`), `result.isError` (`:67`), `result.content` (`:67-68`), `item.type === "text"` with `item.text` (`:37`). Its `contentText` maps **every** non-text item to the literal `"[image]"` (`MCPBridgeSource.swift:35-38`); there is no `[resource: …]` form in the bridge. **SOURCE FACT, divergence from `MCPHub` to reproduce.**

### 1.6 Tool description and schema normalization, exact

**SOURCE FACT, three different rules for the same field.**

- `MCPHub` (`MCPHub.swift:202`, `:203`): `let rawDescription = item["description"]?.string ?? ""`, then `description: String(rawDescription.prefix(400))`. Truncated to **400** characters, newlines kept, no ellipsis appended.
- `MCPProbe` (`MCPProbe.swift:93-98`, `:104`): `firstLine` takes `components(separatedBy: .newlines).first`, trims `.whitespacesAndNewlines`, and returns nil when the result is empty. The rest of the description is discarded. Stored as `MCPToolSummary.summary` (optional).
- Bridge JS (`MCPBridgeSource.swift:40-43`, `:62`): `describe(entry, fallback)` returns `entry.description` when it is a non-empty string, else the fallback, which `wrapTools` passes as `String(entry.name)`. No truncation, no first-line rule.

Input schema fallback:

- `MCPHub` (`MCPHub.swift:204-208`): when `item["inputSchema"]` is absent or is not an object, the schema becomes exactly `.object(["type": .string("object"), "properties": .object([:])])`.
- Bridge JS (`MCPBridgeSource.swift:45-48`): `schemaOf` falls back to `{ type: "object" }` with **no `properties` key**. **SOURCE FACT, divergence to reproduce.**
- `MCPProbe` does not read `inputSchema` at all; `MCPToolSummary` has only `name` and `summary` (`MCP.swift:173-177`).

### 1.7 Tool call naming

**SOURCE FACT.** `MCPHub.Tool.callName` (`MCPHub.swift:37`):

```swift
var callName: String { "mcp__" + server + "__" + name }
```

`MCPHub.parse(callName:)` (`MCPHub.swift:108-117`) requires the prefix `"mcp__"`, then finds the **first** `"__"` in the remainder; both halves must be non-empty or it returns nil. Consequence: a tool name containing `__` survives the round trip, a server id containing `__` does not. Call sites: `DroppyCode/Services/Providers/OpenAICompatibleSession.swift:63` (the OpenAI function name) and `:898` (parsing a model's tool call back).

### 1.8 Tool ordering and pagination

**SOURCE FACT.** `MCPHub.tools()` (`MCPHub.swift:50-77`) iterates `current.keys.sorted()`, then sorts the whole result:

```swift
return all.sorted {
    if $0.server != $1.server { return $0.server < $1.server }
    return $0.name < $1.name
}
```

**SOURCE FACT, pagination bound.** `MCPHub.listTools()` (`MCPHub.swift:195`) and both `MCPProbe` list loops (`MCPProbe.swift:204`, `:289`) run `for _ in 0..<10`: at most **10** pages, after which remaining tools are silently dropped with no error. The bridge JS uses an unbounded `for (;;)` (`MCPBridgeSource.swift:142`, `:207`). **SOURCE FACT, upstream defect to reproduce: a server with more than 10 pages of tools loses the tail silently in the Swift clients.**

### 1.9 Timeouts and intervals, exact numbers

**SOURCE FACT.**

| Operation | Value | Site |
| --- | --- | --- |
| `MCPHub` stdio `initialize` | 60 s | `MCPHub.swift:377` |
| `MCPHub` `tools/list` (stdio + http) | 60 s | `MCPHub.swift:200` |
| `MCPHub` `tools/call` (stdio + http) | 120 s | `MCPHub.swift:237` |
| `MCPHub` http `initialize` POST | 60 s | `MCPHub.swift:428` |
| `MCPHub` http `notifications/initialized` POST | 30 s | `MCPHub.swift:439` |
| `MCPHub` stdio reply poll interval | 10 ms | `MCPHub.swift:415` |
| `MCPHub` terminate grace before SIGKILL | 1 s | `MCPHub.swift:289` |
| `MCPProbe` stdio deadline, normal | 90 s | `MCPProbe.swift:116` |
| `MCPProbe` stdio deadline when `args` contains `mcp-remote` | 240 s | `MCPProbe.swift:115-116` |
| `MCPProbe` stdio reply poll interval | 5 ms | `MCPProbe.swift:188` |
| `MCPProbe` remote POST | 30 s | `MCPProbe.swift:308` |
| `MCPProbe` terminate grace before SIGKILL | 1 s | `MCPProbe.swift:230` |
| `MCPProbe` stderr cap / retained suffix | 64000 / 16000 bytes | `MCPProbe.swift:403` |
| `MCPProbe` HTTP error body kept | first 2000 chars, trimmed | `MCPProbe.swift:323-324` |
| `MCPOAuth` protected-resource probe GET | 30 s | `MCPOAuth.swift:251` |
| `MCPOAuth` metadata GET | 30 s | `MCPOAuth.swift:704` |
| `MCPOAuth` registration POST | 30 s | `MCPOAuth.swift:748` |
| `MCPOAuth` token / refresh POST | 30 s | `MCPOAuth.swift:770` |
| `MCPOAuth` callback listener lifetime | 300 s | `MCPOAuth.swift:129` (`callbackTimeout`) |
| `MCPOAuth` token refresh skew | 90 s before `expires_at` | `MCPOAuth.swift:173` |
| `MCPOAuth` callback head size cap | 1048576 bytes (`1024 * 1024`) | `MCPOAuth.swift:483` |
| `MCPOAuth` callback receive chunk | 65536 bytes max, 1 byte min | `MCPOAuth.swift:466` |
| `MCPProxy` upstream request | 3600 s (`3_600`) | `MCPProxy.swift:325` |
| `MCPProxy` head limit | 65536 (`65_536`) | `MCPProxy.swift:49` |
| `MCPProxy` body limit | 33554432 (`32 * 1_024 * 1_024`) | `MCPProxy.swift:50` |
| `MCPProxy` hard fallback port | 48123 | `MCPProxy.swift:161`, `:172`, `:174` |
| Bridge JS per-server connect | 15000 ms | `MCPBridgeSource.swift:17` (`CONNECT_TIMEOUT_MS`) |

### 1.10 HTTP headers on MCP POSTs

**SOURCE FACT.** `MCPHub.post` (`MCPHub.swift:454-462`) and `MCPProbe.postJSON` (`MCPProbe.swift:302-316`) set, in this order, the per-server headers first and then:

```
Content-Type: application/json
Accept: application/json, text/event-stream
MCP-Protocol-Version: 2025-06-18
Mcp-Session-Id: <session>      (only when a session id is held)
```

**Ordering dependency to reproduce:** because the per-server headers are applied first (`for (field, value) in headers { request.setValue(value, forHTTPHeaderField: field) }`), a server-supplied `Content-Type` or `Accept` is overwritten by the fixed values, while any other server header survives.

Session id is read back from the response header `Mcp-Session-Id` (`MCPHub.swift:468`, `MCPProbe.swift:321`) and applied as `sessionID = posted.sessionID ?? sessionID`: a reply without the header keeps the previous value.

Bridge JS `postMessage` (`MCPBridgeSource.swift:154-158`) spreads the caller's headers first, then forces `"Content-Type": "application/json"` and `Accept: "application/json, text/event-stream"`, and uses the **lowercase** name `"mcp-session-id"` both to send and to read back. It sends **no** `MCP-Protocol-Version` header. **SOURCE FACT, divergence to reproduce.**

### 1.11 SSE handling

**SOURCE FACT.** `MCPHub.firstSSEMessage` (`MCPHub.swift:480-492`) and `MCPProbe.firstSSEMessage` (`MCPProbe.swift:335-347`) are byte-identical:

```swift
private static func firstSSEMessage(_ data: Data) -> JSONValue? {
    let text = String(decoding: data, as: UTF8.self)
    for line in text.components(separatedBy: .newlines) {
        let trimmed = line.trimmingCharacters(in: .whitespaces)
        guard trimmed.hasPrefix("data:") else { continue }
        let payload = trimmed.dropFirst(5).trimmingCharacters(in: .whitespaces)
        guard let value = JSONValue.parse(String(payload)),
              value.object != nil, value["id"] != nil
        else { continue }
        return value
    }
    return nil
}
```

Trigger: `contentType.contains("text/event-stream")` on the response `Content-Type` (`MCPHub.swift:472-473`, `MCPProbe.swift:328`). A frame without an `id` (a notification) is skipped.

Bridge JS `postMessage` (`MCPBridgeSource.swift:161-172`) differs twice: it skips a `data` payload equal to the literal `"[DONE]"`, and it accepts the first parsed object satisfying `("result" in parsed || "error" in parsed)`, **without** requiring an `id`. **SOURCE FACT, divergence to reproduce.**

### 1.12 Error strings produced by the protocol layer

**SOURCE FACT, `MCPHubError` has exactly 2 cases** (`MCPHub.swift:3-15`): `.unknownTool(String)` rendering `"Unknown tool: \(name)."`, and `.server(String)` rendering the message verbatim.

Every literal `MCPHubError.server` message in `MCPHub`, complete (10 distinct strings):

1. `"The server quit."` (`:301`, `:312`, `:317`, `:409`)
2. `"The server quit (exit \(code))."` (`:405`)
3. `"The server didn't answer in time."` (`:413`)
4. `"\(command) isn't installed."` (`:326`)
5. `"The server sent something unexpected: missing result."` (`:379`, `:434`)
6. `"The server sent something unexpected: empty reply."` (`:430`, `:448`)
7. `"The server sent something unexpected: invalid reply."` (`:469`)
8. `"The server address is invalid."` (`:423`)
9. `"The server answered \(http.statusCode)."` (`:470`)
10. `"unknown error"` (`:281`, the fallback for a missing `error.message`)

Plus `print("MCPHub: \(id): \(error.localizedDescription)")` (`MCPHub.swift:67`), which is stdout logging, not user-visible text.

**SOURCE FACT, `MCPProbeError` has exactly 8 cases** (`MCPProbe.swift:3-49`), with these exact `errorDescription` strings:

| Case | Rendered text |
| --- | --- |
| `.commandMissing("npx")` | `"npx isn't installed. Install Node.js from nodejs.org, then try again."` |
| `.commandMissing("uvx")` | "uvx isn't installed. Run \`brew install uv\` in Terminal, then try again." (the backticks are literal) |
| `.commandMissing(other)` | `"\(name) isn't installed. Install it, then try again."` |
| `.exited(code, stderr)` with a last non-empty stderr line | `"The server quit (exit \(code)). \(last)"` |
| `.exited(code, stderr)` with none | `"The server quit (exit \(code))."` |
| `.timedOut` | `"The server didn't answer in time."` |
| `.http(401 or 403, _)` | `"That key was refused. Press Try again and paste a new one."` |
| `.http(404, _)` | `"Nothing answered at that address. Check the address and try again."` |
| `.http(other, _)` | `"The server answered \(status)."` |
| `.unauthorizedOAuth` | `"Sign-in was rejected. Sign in again in your browser."` |
| `.needsSetup(guide)` | `guide.reason` |
| `.unavailable(host)` | `"\(host) didn't answer (404). If the address is right, try again in a few minutes."` |
| `.malformed(detail)` | `"The server sent something unexpected: \(detail)"` |

`.malformed` detail strings used internally: `"missing command or URL"` (`:60`), `"invalid URL"` (`:241`), `"empty reply"` (`:268`), `"invalid reply"` (`:270`, `:319`), `"missing result"` (`:195`, `:276`), and `error["message"]?.string ?? "unknown error"` (`:89`).

---

## 2. Exact per-provider config file paths and formats written

### 2.1 Paths

**SOURCE FACT** (`MCP.swift:236-253`, `MCPProviderConfig.swift:238`, `MCPProxy.swift:15`, `MCPExternalSync.swift:52`):

| Constant | Path | Format |
| --- | --- | --- |
| `MCPPaths.directory` | `<Storage.root>/mcp/` (created with `withIntermediateDirectories: true`) | directory |
| `MCPPaths.connectionsURL` | `<Storage.root>/mcp.json` | `[MCPConnection]` JSON array |
| `MCPPaths.customsURL` | `<Storage.root>/custom-mcp.json` | `[MCPCustomServer]` JSON array |
| `MCPPaths.claudeConfigURL` | `<Storage.root>/mcp/claude.json` | `{"mcpServers": {...}}` |
| `MCPPaths.codexConfigURL` | `<Storage.root>/mcp/codex.json` | `{"mcp_servers": {...}}` |
| `MCPPaths.copilotConfigURL` | `<Storage.root>/mcp/copilot.json` | `{"mcpServers": {...}}` |
| `MCPProviderConfig.geminiSettingsURL` (private) | `<Storage.root>/mcp/gemini-settings.json` | `{"mcpServers": {...}}` |
| `MCPProxy.portFile` (private) | `<Storage.root>/mcp/proxy-port` | decimal `UInt16` as UTF-8 text |
| `MCPExternalSync.managedURL` (private) | `<Storage.root>/mcp/managed.json` | `{"cursor":[ids],"opencode":[ids],"gemini":[ids]}` |
| `MCPBridge` install path | `<Application Support>/<AppInfo.name>/MCP/droppy-mcp-bridge.mjs` | ESM JavaScript |

`Storage.root` (`DroppyCode/Services/Store/Storage.swift:19-26`) is `CaptureRun.storageRoot` when a capture run is active, else `<~/Library/Application Support>/<AppInfo.name>/`. `AppInfo.name` is `CFBundleDisplayName` or the literal fallback `"Droppy Code"` (`DroppyCode/Core/Support/Text.swift:4`). **ENGINEERING DECISION for the Windows port:** `Storage.root` maps to the app's data directory; the relative layout under it (`mcp/claude.json` etc.) must be preserved verbatim because the file names are what providers are pointed at.

### 2.2 Claude (`claude.json`), verbatim rules

`MCPProviderConfig.claude(_:)`, `MCPProviderConfig.swift:4-28`. Root key `"mcpServers"`, one entry per `server.id`.

- Remote (`server.isRemote`, which is `url != nil`, `MCP.swift:118`): `{"type": "http", "url": <server.url ?? "">}`; plus `"headers"` **only when `server.headers` is non-empty**.
- Local: `{"command": <server.command ?? "">, "args": [<strings>]}`; plus `"env"` **only when `server.env` is non-empty**.
- A local entry carries **no `type` key at all**.

Consumed by `ClaudeSession.swift:96`: `arguments += ["--mcp-config", mcpConfig, "--strict-mcp-config"]`, where `mcpConfig` is `MCPProviderConfig.claudeConfigPath()` and is nil (so the flags are omitted entirely) unless the file parses and `mcpServers` is a non-empty object (`MCPProviderConfig.swift:212-215`, `245-251`).

`claude.json` is also read back by four other things: `MCPHub.readSpecs()` (`MCPHub.swift:118-142`), `MCPProviderConfig.acpServers` (`:78`), `MCPProviderConfig.connectedIDs` (`:231`), `MCPSessionOverrides.opencodeEnvironment` (`:165`), and the installed JS bridge via `DROPPY_CODE_MCP_CONFIG` (`PiSession.swift:109`, `CommandCodeSession.swift:178`, both set it to `MCPPaths.claudeConfigURL.path`). **Ordering dependency:** every one of those consumers runs off the file, not off the in-memory `enabledServers`, so they see whatever `writeAll` last wrote.

### 2.3 Codex (`codex.json`), verbatim rules

`MCPProviderConfig.codex(_:)`, `MCPProviderConfig.swift:30-51`. Root key `"mcp_servers"` (snake case, unlike every other provider).

- Remote: `{"url": <url>}`; plus `"http_headers"` **only when headers are non-empty**. Note the key is `http_headers`, not `headers`, and there is **no `type` key**.
- Local: `{"command": <command>, "args": [...]}`; plus `"env"` only when non-empty.

`MCPProviderConfig.codexOverrides()` (`:199-210`) returns `nil` unless the written file parses and `mcp_servers` is a non-empty object; otherwise it returns that object with, for every name in `codexUserServerNames()` not already a key, the added entry `{"enabled": false}`. Consumed at `CodexSession.swift:257`.

`codexUserServerNames()` (`:163-197`) parses `~/.codex/config.toml` line by line:

1. Trim whitespace; require prefix `"[mcp_servers."`.
2. Drop that prefix; require a `"]"` to exist; the text after it must be empty or start with `"#"`.
3. Take the text before the `"]"`. If it starts with `'` or `"`, the name is the content up to the matching same quote; else if it contains a `.`, the name is the part before the first `.`; else the whole segment.
4. Trim whitespace.
5. Drop the name entirely when it is empty or contains any of: whitespace, `[`, `]`, `'`, `"`, `,`, `.`.
6. Insert into a `Set<String>`, return `names.sorted()`.

The in-file comments (`:161-162`, `:175-177`, `:190-192`) state why: a nested-table path such as `imap-email.env` names an entry that exists nowhere in the user's config, and Codex then rejects its whole configuration, failing every turn at once.

### 2.4 Copilot (`copilot.json`), verbatim rules

`MCPProviderConfig.copilot(_:)`, `MCPProviderConfig.swift:53-75`. Root key `"mcpServers"`.

- Remote: `{"type": "http", "url": <url>, "headers": {...}, "tools": ["*"]}` — **headers always present**, even empty.
- Local: `{"type": "local", "command": <command>, "args": [...], "env": {...}, "tools": ["*"]}` — **env always present**, even empty.
- `"tools": ["*"]` is a one-element array holding the literal string `*`, on both branches.

`MCPSessionOverrides.copilotArguments()` (`MCPSessionOverrides.swift:146-162`) builds, in this exact order:

```swift
var arguments = ["--additional-mcp-config", json]
for name in copilotUserServerNames() where !ids.contains(name) {
    arguments += ["--disable-mcp-server", name]
}
if ids.contains("github") {
    arguments.append("--disable-builtin-mcps")
}
return arguments
```

`json` is `MCPProviderConfig.copilotConfigJSON()` (the whole file as a compact JSON **string**, not a path); the function returns `[]` immediately when that is nil. `ids` is the set of keys of `mcpServers` inside that string. `copilotUserServerNames()` (`:136-144`) returns the sorted keys of `mcpServers` in `~/.copilot/mcp-config.json`. **Conditional inclusion:** `--disable-builtin-mcps` is appended only when a server with the exact id `"github"` is connected. Consumed at `CopilotSession.swift:486`.

### 2.5 Gemini (`gemini-settings.json`), verbatim rules

`MCPProviderConfig.gemini(_:)`, `MCPProviderConfig.swift:126-146`. Root key `"mcpServers"`.

- Remote: `{"httpUrl": <url>, "headers": {...}}` — the key is **`httpUrl`**, not `url`, and **headers is always present**, even empty. There is no `type` key.
- Local: `{"command": <command>, "args": [...]}`; `"env"` only when non-empty.

Consumed at `ProviderRegistry.swift:274`: `if provider == .antigravity, let path = MCPProviderConfig.geminiSettingsPath() { environment["GEMINI_CLI_SYSTEM_SETTINGS_PATH"] = path }`. `geminiSettingsPath()` returns nil unless the file parses and `mcpServers` is a non-empty object (`MCPProviderConfig.swift:225-228`).

### 2.6 ACP (`acpServers(allowsHTTP:)`), verbatim rules

`MCPProviderConfig.swift:77-124`. **Not a file:** it returns a JSON **array** built by re-reading `claude.json`, and returns `.array([])` when the file is missing, unparseable, or `mcpServers` is empty or absent. Ids are iterated `object.keys.sorted()`.

An entry counts as remote when `entry["type"]?.string == "http"` **or** `entry["url"]?.string != nil`.

- Remote with `allowsHTTP == true`:
  `{"type": "http", "name": <id>, "url": <url>, "headers": [{"name": <k>, "value": <v>}, ...]}`, header keys in `headers.keys.sorted()` order.
- Remote with `allowsHTTP == false`, rewritten as a stdio launch. The exact argument vector, in order:
  `["-y", "mcp-remote", <url>]` then, per header key in sorted order, `"--header"` followed by the single string `"<key>: <value>"` (a colon and one space). The entry is
  `{"name": <id>, "command": "npx", "args": [...], "env": []}` — note `env` is an **empty array**, not an object, and there is no `type` key.
- Local: `{"name": <id>, "command": <entry.command ?? "">, "args": <entry["args"]?.array ?? []>, "env": [{"name": <k>, "value": <v>}, ...]}`, env keys sorted. Args are passed through as the raw JSON array from the file, not re-stringified.

`allowsHTTP` comes from `ACPSession.swift:130`: `initialized["agentCapabilities"]?["mcpCapabilities"]?["http"]?.bool ?? false`, and the array is passed as `params["mcpServers"]` of `session/new` or `session/load` (`ACPSession.swift:254`). **Ordering dependency:** `acpServers` must run after `writeAll`, because it reads the file.

### 2.7 OpenCode (`OPENCODE_CONFIG_CONTENT`), verbatim rules

`MCPSessionOverrides.opencodeEnvironment()` (`MCPSessionOverrides.swift:164-203`). Not a file either: it reads `claude.json` and returns an environment variable.

- Returns `[:]` when the file is missing/unparseable or `mcpServers` is empty, and again when the built `mcp` object ends up empty.
- Remote branch, when `entry["type"] as? String == "http"`: `{"type": "remote", "url": <url or "">, "enabled": true}`, plus `"headers"` only when non-empty.
- Local branch: requires a non-empty `command` string or the entry is skipped. `{"type": "local", "command": [<command>] + <args>, "enabled": true}` — **command is an array whose first element is the executable**, plus `"environment"` (not `env`) only when non-empty.
- Root object: `{"$schema": "https://opencode.ai/config.json", "mcp": <mcp>}`, serialized with `[.sortedKeys]` and returned as `["OPENCODE_CONFIG_CONTENT": <json>]`.

Consumed at `ProviderRegistry.swift:272`.

### 2.8 Antigravity

`AntigravitySession.swift:83-84`: `let connected = MCPProviderConfig.connectedIDs()`, and when non-empty, `arguments += ["--allowed-mcp-server-names"] + connected`. `connectedIDs()` (`MCPProviderConfig.swift:230-236`) returns `object.keys.sorted()` of `claude.json`'s `mcpServers`, or `[]`.

### 2.9 `writeAll`, the only writer

**SOURCE FACT** (`MCPProviderConfig.swift:148-159`):

```swift
static func writeAll(_ servers: [MCPResolvedServer]) {
    if servers.isEmpty {
        for url in [MCPPaths.claudeConfigURL, MCPPaths.codexConfigURL, MCPPaths.copilotConfigURL, geminiSettingsURL] {
            try? FileManager.default.removeItem(at: url)
        }
        return
    }
    write(claude(servers), to: MCPPaths.claudeConfigURL)
    write(codex(servers), to: MCPPaths.codexConfigURL)
    write(copilot(servers), to: MCPPaths.copilotConfigURL)
    write(gemini(servers), to: geminiSettingsURL)
}
```

`write` (`:240-243`) creates the parent directory then writes `value.data(pretty: true)` with `options: .atomic`. **SOURCE FACT:** an empty server list DELETES all four files rather than writing empty ones, which is what makes `claudeConfigPath()`, `geminiSettingsPath()`, `copilotConfigJSON()`, `codexOverrides()`, `connectedIDs()` and `acpServers` all degrade to nil / empty together.

### 2.10 `MCPExternalSync`, one-time cleanup only

**SOURCE FACT** (`MCPExternalSync.swift:7-28`). This is NOT a writer of new config. It returns immediately unless `<Storage.root>/mcp/managed.json` exists. When it does, it removes exactly the listed ids from three third-party files and then deletes `managed.json`:

| Managed key | File | Root key |
| --- | --- | --- |
| `cursor` | `~/.cursor/mcp.json` | `mcpServers` |
| `opencode` | `~/.config/opencode/opencode.json` | `mcp` |
| `gemini` | `~/.gemini/settings.json` | `mcpServers` |

`remove` (`:33-50`) skips an empty id list, a missing file and invalid JSON, rewrites only when something was actually removed, and writes with `[.prettyPrinted, .sortedKeys]` and `.atomic`. The `servers` parameter is accepted and **never used** (see section 9).

---

## 3. The OAuth flow, exact

### 3.1 Entry point and step order

**SOURCE FACT** (`MCPOAuth.signIn`, `MCPOAuth.swift:133-160`), in this exact order:

1. `discover(url:)` to get `AuthServerMetadata`.
2. `verifier = randomToken(byteCount: 32)`.
3. `challenge = pkceChallenge(for: verifier)`.
4. `state = randomToken(byteCount: 24)`.
5. `runCallbackServer(serverName:expectedState:onReady:)`. Only inside the listener's `.ready` handler, once the OS has assigned a port and the redirect URI therefore exists, does it:
   a. `clientRegistration(serverID:metadata:redirectURI:)`,
   b. build `authorizationURL(...)`,
   c. `shared.store(redirectURI:client:)`,
   d. `NSWorkspace.shared.open(ready.url)` on the main actor.
6. `exchangeCode(...)` with the code, the stored redirect URI and the verifier.

**Ordering dependency to reproduce:** dynamic client registration happens AFTER the loopback port is known, because the redirect URI (which includes the port) is part of the registration body. A port that changes between runs therefore invalidates nothing, because the cached client is reused by issuer (see 3.4).

### 3.2 Redirect URI, exact

**SOURCE FACT** (`MCPOAuth.swift:428`, and the listener parameters at `:404-406`):

```swift
parameters.requiredLocalEndpoint = NWEndpoint.hostPort(host: "127.0.0.1", port: .any)
...
let redirectURI = "http://127.0.0.1:\(port)/callback"
```

The redirect URI is therefore always `http://127.0.0.1:<ephemeral port>/callback`. The host is the literal `127.0.0.1` (never `localhost`), the scheme is `http`, the path is `/callback`, and the port is whatever the kernel assigned via `port: .any`. There is no custom URL scheme and no `?` query in the registered URI.

### 3.3 Discovery, exact

**SOURCE FACT** (`MCPOAuth.discover`, `MCPOAuth.swift:199-247`).

Step 1, `protectedResourceMetadataURL(baseURL:)` (`:249-291`): issue a `GET` to the server URL with `Accept: application/json` and a 30 s timeout. Proceed only when the response is an `HTTPURLResponse` with status exactly **401**; anything else returns `(nil, nil)`. Then collect every header field whose name matches `WWW-Authenticate` case-insensitively (both `String` and `[String]` values are handled), and scan the challenges for:

- `resource_metadata` via `challengeParameter` (`:290-302`), which accepts a quoted value or, per the in-file comment about Stripe, a bare value up to the next `,` or space; nil when absent or empty.
- `scope` via `challengeScopes` (`:304-308`), split on single spaces, empties dropped, nil when the result is empty.

Step 2a, when a `resource_metadata` URL was found: `fetchJSON` it and read `authorization_servers[0]` (string) into `issuer`, `resource` (string), and `scopes_supported` (array of non-empty strings) into `resourceScopes`. A failure here is swallowed (`try?`).

Step 2b, else: build exactly these two candidate URLs, in order (`MCPOAuth.swift:219-222`):

```
<origin> + "/.well-known/oauth-protected-resource" + <path of the server URL>
<origin> + "/.well-known/oauth-protected-resource"
```

and take the first that parses AND has either `authorization_servers` as an array or `resource` as a string.

Step 3: `if resourceScopes == nil { resourceScopes = challenge.challengeScopes }` (`:238-240`), so the WWW-Authenticate `scope` is a fallback only.

Step 4 (`MCPOAuth.swift:242-246`): `normalizedIssuer = (issuer ?? fallbackIssuer).trimmingCharacters(in: CharacterSet(charactersIn: "/"))`, where `fallbackIssuer = try origin(of: url)`. `trimmingCharacters(in:)` strips `/` from the leading and trailing ends only, so `https://gitlab.com/` becomes `https://gitlab.com` and interior slashes are untouched. An empty result throws `.discoveryFailed("That address doesn't look like a URL.")`.

Step 5, `fetchAuthorizationServerMetadata(issuer:resource:resourceScopes:)` (`:319-372`). The candidate list, in order:

```
(only when the issuer has a non-empty path other than "/")
<origin> + "/.well-known/oauth-authorization-server" + <issuer path>
<origin> + "/.well-known/openid-configuration" + <issuer path>
(always)
<issuer> + "/.well-known/oauth-authorization-server"
<issuer> + "/.well-known/openid-configuration"
```

The in-file comment (`:312-315`) names Stripe's `https://access.stripe.com/mcp` as the reason for the path-after-well-known forms. The first candidate whose JSON has non-empty `authorization_endpoint` AND non-empty `token_endpoint` wins; `registration_endpoint` is read optionally. Otherwise `lastError` is set to `"It's missing the addresses needed to sign in."` (or the caught error's description) and, after all candidates, `MCPOAuthError.metadataFailed(lastError)` is thrown.

**SOURCE FACT, field names read from authorization-server metadata (4):** `authorization_endpoint`, `token_endpoint`, `registration_endpoint`, and from protected-resource metadata (3): `authorization_servers`, `resource`, `scopes_supported`.

### 3.4 Dynamic client registration, exact

**SOURCE FACT** (`MCPOAuth.clientRegistration`, `MCPOAuth.swift:355-412`).

Cache hit: `MCPKeychain.value(server: serverID, field: "oauth-client")` parses, has a non-empty `client_id`, **and** its `issuer` equals `metadata.issuer` exactly. Only then is it reused, with `client_secret` read optionally.

Otherwise, when `metadata.registrationEndpoint` is nil or empty, throw `.registrationUnsupported` (`"This server doesn't let apps sign in on their own."`).

Otherwise POST this exact body (`MCPOAuth.swift:366-373`), as JSON:

```json
{
  "client_name": "Droppy Code",
  "redirect_uris": ["<redirectURI>"],
  "grant_types": ["authorization_code", "refresh_token"],
  "response_types": ["code"],
  "token_endpoint_auth_method": "none"
}
```

plus, **only when `metadata.resourceScopes` is non-empty**, `"scope": <scopes joined by a single space>`.

The POST sets `Content-Type: application/json` and `Accept: application/json`, 30 s timeout (`:748-757`).

On a non-2xx status or a missing/empty `client_id`, the thrown message is `oauthErrorMessage(data:)` when the body supplies one, else one of exactly two literals (`:383-385`):

- status 403: `"The server only signs in apps it has approved (403), so this one can't yet."`
- any other: `"The server refused to set things up (\(response.statusCode))."`

`oauthErrorMessage` (`:791-797`) prefers a non-empty `error_description`, else `error`.

On success the Keychain field `oauth-client` is written with `JSONSerialization` `[.sortedKeys]` containing `client_id` and `issuer`, plus `client_secret` only when non-empty.

### 3.5 Authorization URL, exact query items

**SOURCE FACT** (`MCPOAuth.authorizationURL`, `MCPOAuth.swift:573-599`). Starting from any query items already present on the authorization endpoint, it **appends**, in this order:

```
response_type = code
client_id = <client id>
redirect_uri = http://127.0.0.1:<port>/callback
code_challenge = <base64url(SHA256(verifier))>
code_challenge_method = S256
state = <base64url of 24 random bytes>
```

then, conditionally:

```
resource = <metadata.resource>            (only when non-nil and non-empty)
scope = <resourceScopes joined by " ">    (only when non-nil and non-empty)
```

**SOURCE FACT, scopes.** Droppy Code never hardcodes a scope string. Scopes come only from the protected-resource metadata's `scopes_supported`, falling back to the WWW-Authenticate `scope` parameter; when neither exists, the `scope` parameter is omitted from both the registration body and the authorization URL entirely.

### 3.6 PKCE and randomness, exact

**SOURCE FACT** (`MCPOAuth.swift:815-833`):

```swift
private static func randomToken(byteCount: Int) -> String {
    var bytes = [UInt8](repeating: 0, count: byteCount)
    for index in bytes.indices {
        bytes[index] = UInt8.random(in: UInt8.min...UInt8.max)
    }
    return base64url(Data(bytes))
}

private static func pkceChallenge(for verifier: String) -> String {
    let digest = SHA256.hash(data: Data(verifier.utf8))
    return base64url(Data(digest))
}

private static func base64url(_ data: Data) -> String {
    data.base64EncodedString()
        .replacingOccurrences(of: "+", with: "-")
        .replacingOccurrences(of: "/", with: "_")
        .replacingOccurrences(of: "=", with: "")
}
```

Verifier: 32 random bytes, base64url, so 43 characters. State: 24 random bytes, so 32 characters. Method is fixed `S256`. **SOURCE FACT, upstream defect to record:** randomness comes from `UInt8.random(in:)` (Swift's default `SystemRandomNumberGenerator`), not from `SecRandomCopyBytes` or `CryptoKit`, even though `CryptoKit` is imported for SHA256.

### 3.7 Token exchange, exact form fields

**SOURCE FACT** (`MCPOAuth.exchangeCode`, `:600-640`). `postForm` sends
`Content-Type: application/x-www-form-urlencoded`, `Accept: application/json`, 30 s, with the body built as `URLComponents.percentEncodedQuery` from the ordered pairs (`:767-789`):

```
grant_type = authorization_code
code = <code>
redirect_uri = <redirectURI>
client_id = <client id>
code_verifier = <verifier>
client_secret = <secret>       (only when the registered client has a non-empty secret)
resource = <metadata.resource> (only when non-nil and non-empty)
```

Success requires 2xx AND a parseable body AND a non-empty `access_token`; otherwise `.tokenFailed(oauthErrorMessage(data:) ?? "The server didn't hand back a token.")`.

Response fields read: `access_token`, `refresh_token`, `expires_in` (`:626-630`).

### 3.8 Refresh, exact

**SOURCE FACT** (`MCPOAuth.accessToken`, `:159-179`, and `performRefresh`, `:641-680`).

`accessToken(serverID:)`:

1. Read the Keychain field `oauth`; require a parseable JSON object with a non-empty `access_token`, else throw `.signedOut` (`"You're signed out. Sign in again to continue."`).
2. `needsRefresh` is true only when `expires_at` exists as a double AND `Date().timeIntervalSince1970 >= expires_at - 90`. **A token with no `expires_at` is never refreshed.**
3. When refresh is needed but `refresh_token` is missing or empty, throw `.signedOut`.
4. Refresh through `OAuthRefreshGate` (`:60-72`), an actor that coalesces concurrent refreshes per `serverID` into one `Task.detached`.

`performRefresh` requires stored `token_endpoint` and `client_id` to be non-empty, else `.signedOut`. Form fields, in order:

```
grant_type = refresh_token
refresh_token = <refresh token>
client_id = <stored client_id>
client_secret = <stored client_secret>  (only when non-empty)
resource = <stored resource>            (only when non-empty)
```

Failure message: `oauthErrorMessage(data:) ?? "The sign-in expired."`. On success it re-stores the session with `refresh_token` = the new one **or the old one when the server returned none** (`json["refresh_token"]?.string ?? refreshToken`, `:671`).

### 3.9 Persisted Keychain shape, exact key names

**SOURCE FACT.** `MCPKeychain` (`MCPKeychain.swift:71-73`) addresses items as
`kSecClassGenericPassword` with `kSecAttrService = AppInfo.name` and
`kSecAttrAccount = "MCP " + server + " " + field` — one space after `MCP` and one between server and field. Accessibility on create is `kSecAttrAccessibleWhenUnlocked` (`:111`). `set` with a whitespace-only value deletes the item and returns `true` (`:95-99`). Reads and writes are attempted `SecItemUpdate` first, then `SecItemAdd` only on `errSecItemNotFound` (`:106-113`).

Two OAuth field names exist, both written by `MCPOAuth` and both deleted by `signOut` (`:188-191`):

- field `"oauth"` — the session. Keys, from `storeSession` (`:681-714`), written with `JSONSerialization` `[.sortedKeys]`:
  - always: `access_token`, `token_endpoint`, `client_id`
  - `refresh_token` only when non-nil and non-empty
  - `expires_at` only when `expires_in` was present; value is `Date().timeIntervalSince1970 + Double(expiresIn)` (absolute Unix seconds, not a duration)
  - `client_secret` only when non-nil and non-empty
  - `resource` only when non-nil and non-empty
- field `"oauth-client"` — the registered client. Keys: `client_id`, `issuer`, and `client_secret` only when non-empty.

`isSignedIn(serverID:)` is exactly `MCPKeychain.value(server: serverID, field: "oauth") != nil` (`:184-186`): it does not validate the JSON.

### 3.10 `MCPOAuthError`, all 11 cases with exact text

**SOURCE FACT** (`MCPOAuth.swift:5-44`):

| Case | `errorDescription` |
| --- | --- |
| `.discoveryFailed(detail)` | empty detail: `"Couldn't find the sign-in page."`; else `"Couldn't find the sign-in page. \(detail)"` |
| `.metadataFailed(detail)` | empty: `"The server's sign-in details didn't make sense."`; else that plus `" \(detail)"` |
| `.registrationUnsupported` | `"This server doesn't let apps sign in on their own."` |
| `.registrationFailed(detail)` | empty: `"Sign-in setup failed."`; else `"Sign-in setup failed. \(detail)"` |
| `.tokenFailed(detail)` | empty: `"Couldn't finish signing in."`; else `"Couldn't finish signing in. \(detail)"` |
| `.callbackFailed(detail)` | empty: `"Sign-in didn't finish."`; else **just `detail`** (no prefix) |
| `.stateMismatch` | `"Sign-in didn't finish. Try again."` |
| `.timedOut` | `"Sign-in timed out. Try again and complete it in the browser."` |
| `.cancelled` | `"Sign-in was cancelled."` |
| `.signedOut` | `"You're signed out. Sign in again to continue."` |
| `.network(detail)` | empty: `"The network didn't answer."`; else **just `detail`** |

Literal `.network` details used: `"Couldn't listen for the sign-in reply."` (`:414`, `:425`), `"The connection to the browser broke."` (`:439`), `"That address isn't valid."` (`:718`, `:747`, `:769`), `"The server didn't answer properly."` (`:729`, `:757`, `:781`), and the empty string `""` from the generic catch arms (`:735`, `:763`, `:787`). Literal `.discoveryFailed` detail: `"That address doesn't look like a URL."` (`:201`, `:244`, `:804`). Literal `.metadataFailed` details: `"The authorization address isn't valid."` (`:147`) and `"The server answered \(response.statusCode)."` (`:738`).

### 3.11 The callback HTTP server, exact behavior

**SOURCE FACT** (`MCPOAuth.swift:403-459`, `:460-570`).

- `NWListener` over `NWParameters.tcp` with `requiredLocalEndpoint = hostPort("127.0.0.1", .any)`. A construction failure throws `.network("Couldn't listen for the sign-in reply.")`; `.failed` state gives `.network("The connection to the browser broke.")`; `.cancelled` gives `.cancelled`.
- `queue.asyncAfter(deadline: .now() + callbackTimeout)` fires unconditionally at +300 s and calls `wait.finish(.failure(.timedOut))`; `CallbackWait.finish` is idempotent under a lock (`:100-121`), so a completed flow is unaffected.
- Request reading (`:460-495`) accumulates until `\r\n\r\n`; it gives up (cancels the connection, no reply) when the stream completes, errors, or the buffer exceeds `1024 * 1024` bytes without a header terminator.
- Only the **request line** is parsed (`:503-515`). Method must be exactly `GET` (`parts[0] == "GET"`, and `parts.count >= 2`), else a `405 Method Not Allowed` with body `<html><body>Method not allowed.</body></html>`.
- Target must be exactly `/callback` or start with `/callback?`, else `404 Not Found` with body `<html><body>Not found.</body></html>`.
- Query items are parsed by prefixing `http://127.0.0.1` to the target.

Branch order and exact outcome (`:527-561`):

1. `error` present and non-empty: message is `error_description` when non-empty, else `"The sign-in was refused (\(providerError))."`. Renders the **failure page** with status 200 and finishes `.callbackFailed(message)`.
2. `code` missing or empty: failure page with message `"The browser didn't send back a code."`, status 200, finishes `.callbackFailed("The browser didn't send back a code.")`.
3. `state` not equal to the expected state: failure page with message `"Sign-in didn't finish. Try again."`, status 200, finishes `.stateMismatch`.
4. Otherwise: **success page**, status 200; only after the page is sent does it read `shared.current()` and, if the client or redirect URI is missing, finish `.callbackFailed("Sign-in didn't finish.")`, else succeed.

**SOURCE FACT:** every OAuth outcome page is served with HTTP **200**; only the method and path rejections use 405 and 404.

`sendHTML` (`:563-570`) writes exactly:

```
HTTP/1.1 <status> <statusText>\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: <byte count>\r\nConnection: close\r\n\r\n
```

followed by the UTF-8 body, and runs the `done` closure from the send completion.

---

## 4. The OAuth callback page, verbatim

**SOURCE FACT.** Two public entry points and one private builder (`MCPOAuth.swift:835-929`). Reproduced here byte-for-byte from the source; the `\u{2014}` in `failurePage`'s head title is an em dash (U+2014), and it is the only one.

```swift
    static func successPage(serverName: String) -> String {
        page(
            headTitle: "Signed in to \(escapeHTML(serverName))",
            heading: "Signed in to \(escapeHTML(serverName))",
            message: "You can close this tab and head back to Droppy Code."
        )
    }

    static func failurePage(serverName: String, message: String) -> String {
        page(
            headTitle: "Sign-in didn't finish \u{2014} \(escapeHTML(serverName))",
            heading: "Sign-in didn't finish",
            message: escapeHTML(message)
        )
    }
```

Note the asymmetry: the success page escapes the server name into BOTH the `<title>` and the `<h1>`; the failure page escapes the server name into the `<title>` only, and its `<h1>` is the fixed string `Sign-in didn't finish`.

The page builder, verbatim (`MCPOAuth.swift:853-912`), as a Swift multi-line string literal with three interpolations `\(headTitle)`, `\(heading)`, `\(message)`:

```html
<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<meta name="color-scheme" content="dark">
<title>\(headTitle)</title>
<style>
* { box-sizing: border-box; }
html, body { margin: 0; padding: 0; }
body {
  background: #0B0B0F;
  color: #F5F5F7;
  font-family: -apple-system, "SF Pro Text", "Helvetica Neue", Helvetica, sans-serif;
  min-height: 100vh;
  display: flex;
  align-items: center;
  justify-content: center;
}
.card {
  width: 420px;
  max-width: calc(100vw - 48px);
  padding: 40px;
  border-radius: 22px;
  background: rgba(255, 255, 255, 0.06);
  text-align: center;
}
h1 {
  font-size: 22px;
  font-weight: 600;
  color: #F5F5F7;
  margin: 20px 0 8px;
}
p {
  font-size: 14px;
  color: rgba(255, 255, 255, 0.62);
  line-height: 1.5;
  margin: 0 0 24px;
}
.wordmark {
  font-size: 12px;
  color: rgba(255, 255, 255, 0.4);
  letter-spacing: 0.04em;
}
</style>
</head>
<body>
<div class="card">
<svg width="56" height="56" viewBox="0 0 100 100" opacity="0.9" xmlns="http://www.w3.org/2000/svg"><path fill="#FFFFFF" fill-rule="nonzero" d="M16.00 94.00 C16.00 94.00 16.17 80.33 17.00 74.00 C17.83 67.67 19.50 61.00 21.00 56.00 C22.50 51.00 25.50 47.33 26.00 44.00 C26.50 40.67 26.17 38.83 24.00 36.00 C21.83 33.17 15.83 30.17 13.00 27.00 C10.17 23.83 7.00 17.00 7.00 17.00 C7.00 17.00 16.50 24.50 20.00 27.00 C23.50 29.50 27.50 33.17 28.00 32.00 C28.50 30.83 24.33 24.50 23.00 20.00 C21.67 15.50 20.00 5.00 20.00 5.00 C20.00 5.00 27.50 12.67 31.00 16.00 C34.50 19.33 37.17 23.67 41.00 25.00 C44.83 26.33 50.17 23.33 54.00 24.00 C57.83 24.67 61.00 26.67 64.00 29.00 C67.00 31.33 68.67 34.50 72.00 38.00 C75.33 41.50 80.33 46.33 84.00 50.00 C87.67 53.67 92.17 57.33 94.00 60.00 C95.83 62.67 95.33 64.33 95.00 66.00 C94.67 67.67 93.83 69.00 92.00 70.00 C90.17 71.00 87.33 71.67 84.00 72.00 C80.67 72.33 75.33 71.50 72.00 72.00 C68.67 72.50 67.33 72.67 64.00 75.00 C60.67 77.33 55.33 83.17 52.00 86.00 C48.67 88.83 46.00 90.67 44.00 92.00 C42.00 93.33 40.00 94.00 40.00 94.00 C40.00 94.00 16.00 94.00 16.00 94.00 Z M63.00 38.00 C60.24 38.00 58.00 40.24 58.00 43.00 C58.00 45.76 60.24 48.00 63.00 48.00 C65.76 48.00 68.00 45.76 68.00 43.00 C68.00 40.24 65.76 38.00 63.00 38.00 Z M86.00 61.20 C85.01 61.20 84.20 62.01 84.20 63.00 C84.20 63.99 85.01 64.80 86.00 64.80 C86.99 64.80 87.80 63.99 87.80 63.00 C87.80 62.01 86.99 61.20 86.00 61.20 Z"/></svg>
<h1>\(heading)</h1>
<p>\(message)</p>
<div class="wordmark">Droppy Code</div>
</div>
<script>setTimeout(() => window.close(), 1500);</script>
</body>
</html>
```

**SOURCE FACT, the page's exact assets:**

- Background `#0B0B0F`, text `#F5F5F7`, card `rgba(255, 255, 255, 0.06)`, paragraph `rgba(255, 255, 255, 0.62)`, wordmark `rgba(255, 255, 255, 0.4)`.
- Card: `width: 420px`, `max-width: calc(100vw - 48px)`, `padding: 40px`, `border-radius: 22px`.
- `h1`: `22px` / `600` / `margin: 20px 0 8px`. `p`: `14px` / `line-height: 1.5` / `margin: 0 0 24px`. `.wordmark`: `12px` / `letter-spacing: 0.04em`.
- Font stack, verbatim: `-apple-system, "SF Pro Text", "Helvetica Neue", Helvetica, sans-serif`.
- `<meta name="color-scheme" content="dark">` and `<html lang="en">`; there is no light-mode variant.
- One inline SVG, 56x56, `viewBox="0 0 100 100"`, `opacity="0.9"`, a single `<path>` with `fill="#FFFFFF"` and `fill-rule="nonzero"` (the Droppy snake mark). No external asset is referenced.
- The literal wordmark text is `Droppy Code`.
- The only script is `setTimeout(() => window.close(), 1500);` — the tab tries to close itself after **1500 ms**.

**ENGINEERING DECISION for the Windows port:** the wordmark text, the SVG mark and the "head back to Droppy Code" copy are Droppy branding. Under the port's own naming rules these are the strings that must be renamed; the geometry, colours, sizes and the 1500 ms self-close are behavioral parity and should be kept exactly.

`escapeHTML` (`MCPOAuth.swift:914-928`) escapes exactly five characters, in this order of testing: `&` to `&amp;`, `<` to `&lt;`, `>` to `&gt;`, `"` to `&quot;`, `'` to `&#39;`. Nothing else is escaped, and the function is char-by-char over the string.

---

## 5. The local proxy, exact

**SOURCE FACT** (`MCPProxy.swift`). Every OAuth server is reached by every provider through this proxy, so no token ever lands in a config file (`MCP.swift:11-14`).

- Route URL: `MCPProxy.url(for:)` (`:35-37`) returns exactly `"http://127.0.0.1:\(port)/mcp/\(serverID)"`.
- Port persistence: `<Storage.root>/mcp/proxy-port`, a decimal `UInt16` as UTF-8 text. `loadPort` reads it (whitespace-trimmed, non-zero), else picks a free port and writes it (`:17-25`). `repickPort` picks again and rewrites after a bind failure, once (`:28-33`, `:125-131`).
- `pickFreePort` (`:159-175`): `socket(AF_INET, SOCK_STREAM, 0)`, bind `127.0.0.1:0`, `getsockname`, close. Hard fallback **48123** on socket failure, bind/getsockname failure, or a returned port of 0. The in-file comment records that asking `NWListener` for an ephemeral port fails with EINVAL on current macOS.
- Listener parameters (`:100-103`): `requiredInterfaceType = .loopback`, `allowLocalEndpointReuse = true`, `requiredLocalEndpoint = .hostPort("127.0.0.1", <port>)`. The comment at `:93-97` records that naming the port a second time through `NWListener(using:on:)` is rejected outright (EINVAL) and left the proxy silent.
- Path routing (`:284-303`): strip the query at the first `?`; require the prefix `/mcp/`; the remainder is the server id and must be non-empty and contain no `/`. Any failure, **including an unknown server id**, answers **502** with body `{"error":"unknown server"}`. The comment at `:286-287` records why a 404 is never used: the app reads a 404 seen through the proxy as the server's own answer (GitLab answers 404 while MCP is off).
- Request limits: `transfer-encoding` containing `chunked` gives **411** with `{"error":"chunked request bodies are not supported"}`; a head over 65536 bytes gives **413** `{"error":"request head too large"}`; a `content-length` over 33554432 gives **413** `{"error":"request body too large"}`; an unparseable head gives **400** `{"error":"bad request"}`; an upstream throw gives **502** `{"error":"upstream request failed"}`.
- Header pass-through, exactly 5 pairs in this order (`:326-332`), lowercase client name to canonical upstream name:

```
("content-type",        "Content-Type")
("accept",              "Accept")
("mcp-session-id",      "Mcp-Session-Id")
("mcp-protocol-version","MCP-Protocol-Version")
("last-event-id",       "Last-Event-ID")
```

  Nothing else is forwarded: no cookies, no user agent, no authorization from the client.
- Route headers are applied next, then, when `route.usesOAuth`, `Authorization: Bearer <token>` from `MCPOAuth.accessToken(serverID:)`. A throw there answers **401** with `{"error":"<error.localizedDescription>"}` and returns.
- One 401 retry (`:356-362`): when the upstream answers 401 on an OAuth route, it asks `MCPOAuth.accessToken` again (`try?`) and re-sends once. There is no second retry.
- Response write-back (`:369-411`): copies back exactly `Content-Type`, `Mcp-Session-Id` and `Cache-Control` when present. For `text/event-stream` it adds `Transfer-Encoding: chunked` and `Connection: close`; otherwise it buffers the whole body and adds `Content-Length` and `Connection: close`.
- Reason phrases, the complete 12-entry table plus default (`:443-460`): 200 `OK`, 201 `Created`, 202 `Accepted`, 204 `No Content`, 400 `Bad Request`, 401 `Unauthorized`, 404 `Not Found`, 405 `Method Not Allowed`, 411 `Length Required`, 413 `Content Too Large`, 500 `Internal Server Error`, 502 `Bad Gateway`; default is `OK` for any other 2xx and `Error` otherwise.
- `jsonError` (`:462-469`) escapes `\` to `\\`, `"` to `\"`, and replaces `\n` and `\r` with a single space, then wraps as `{"error":"<escaped>"}`.

**SOURCE FACT, upstream defect to reproduce (or to record and deliberately diverge from).** The SSE relay frames **one HTTP chunk per byte** (`MCPProxy.swift:386-392`):

```swift
for try await byte in stream {
    var frame = Data("1\r\n".utf8)
    frame.append(byte)
    frame.append(Data("\r\n".utf8))
    await send(connection, frame)
}
```

`URLSession.AsyncBytes` yields `UInt8`, so each payload byte is sent as the chunk `1\r\n<byte>\r\n` — six bytes on the wire per payload byte, and one awaited `NWConnection.send` per byte. The non-SSE path likewise accumulates the body one byte at a time (`:400-402`). This is a correctness-preserving but severely inefficient implementation; a parity port should note it explicitly rather than silently "fixing" it, because timing-sensitive downstream behavior may depend on the flush-per-byte.

---

## 6. The catalog, complete

**SOURCE FACT.** `MCPCatalog.entries` (`MCPCatalog.swift:4-448`) has exactly **33** entries, counted by counting the `id:` lines. In source order, with transport:

| # | id | name | vendor | category | transport |
| --- | --- | --- | --- | --- | --- |
| 1 | `github` | GitHub | GitHub | developer | http `https://api.githubcopilot.com/mcp/` |
| 2 | `gitlab` | GitLab | GitLab | developer | oauth `https://{instance}/api/v4/mcp` |
| 3 | `playwright` | Playwright | Microsoft | browser | stdio `npx -y @playwright/mcp@latest` |
| 4 | `chrome-devtools` | Chrome DevTools | Google | browser | stdio `npx -y chrome-devtools-mcp@latest` |
| 5 | `context7` | Context7 | Upstash | knowledge | http `https://mcp.context7.com/mcp` |
| 6 | `filesystem` | Filesystem | Anthropic | developer | stdio `npx -y @modelcontextprotocol/server-filesystem {root}` |
| 7 | `fetch` | Fetch | Anthropic | browser | stdio `uvx mcp-server-fetch` |
| 8 | `memory` | Memory | Anthropic | knowledge | stdio `npx -y @modelcontextprotocol/server-memory` |
| 9 | `sequential-thinking` | Sequential Thinking | Anthropic | knowledge | stdio `npx -y @modelcontextprotocol/server-sequential-thinking` |
| 10 | `git` | Git | Anthropic | developer | stdio `uvx mcp-server-git` |
| 11 | `brave-search` | Brave Search | Brave | search | stdio `npx -y @brave/brave-search-mcp-server` |
| 12 | `tavily` | Tavily | Tavily | search | oauth `https://mcp.tavily.com/mcp` |
| 13 | `exa` | Exa | Exa | search | oauth `https://mcp.exa.ai/mcp` |
| 14 | `firecrawl` | Firecrawl | Firecrawl | browser | stdio `npx -y firecrawl-mcp` |
| 15 | `perplexity` | Perplexity | Perplexity | search | stdio `npx -y @perplexity-ai/mcp-server` |
| 16 | `slack` | Slack | Slack | work | stdio `npx -y slack-mcp-server@latest --transport stdio` |
| 17 | `notion` | Notion | Notion | work | oauth `https://mcp.notion.com/mcp` |
| 18 | `linear` | Linear | Linear | work | oauth `https://mcp.linear.app/mcp` |
| 19 | `atlassian` | Atlassian | Atlassian | work | oauth `https://mcp.atlassian.com/v2/mcp` |
| 20 | `figma` | Figma | Figma | work | oauth `https://mcp.figma.com/mcp` |
| 21 | `sentry` | Sentry | Sentry | developer | oauth `https://mcp.sentry.dev/mcp` |
| 22 | `vercel` | Vercel | Vercel | cloud | oauth `https://mcp.vercel.com` |
| 23 | `cloudflare` | Cloudflare | Cloudflare | cloud | oauth `https://bindings.mcp.cloudflare.com/mcp` |
| 24 | `netlify` | Netlify | Netlify | cloud | oauth `https://netlify-mcp.netlify.app/mcp` |
| 25 | `supabase` | Supabase | Supabase | data | oauth `https://mcp.supabase.com/mcp` |
| 26 | `stripe` | Stripe | Stripe | work | oauth `https://mcp.stripe.com` |
| 27 | `resend` | Resend | Resend | work | oauth `https://mcp.resend.com/mcp` |
| 28 | `postgres` | PostgreSQL | PostgreSQL | data | stdio `npx -y @bytebase/dbhub@latest --transport stdio --dsn {url}` |
| 29 | `sqlite` | SQLite | SQLite | data | stdio `uvx mcp-server-sqlite --db-path {path}` |
| 30 | `mongodb` | MongoDB | MongoDB | data | stdio `npx -y mongodb-mcp-server` |
| 31 | `huggingface` | Hugging Face | Hugging Face | knowledge | http `https://huggingface.co/mcp` |
| 32 | `deepwiki` | DeepWiki | Cognition | knowledge | http `https://mcp.deepwiki.com/mcp` |
| 33 | `aws-docs` | AWS Docs | Amazon Web Services | cloud | stdio `uvx awslabs.aws-documentation-mcp-server@latest` |

Counts, verified by counting: **33** total; **15** stdio, **4** http, **14** oauth. By category: developer **5**, browser **4**, search **4**, work **7**, data **4**, cloud **4**, knowledge **5** (sum 33). **12** entries declare `fields` (12 `MCPField` initializers in total, so exactly one field each); **6** declare `env`; **8** declare `keysURL`; **5** set `isMonochrome: true`; all **33** declare `sampleTools`.

The **14** OAuth entries, the only ones that drive `MCPOAuth`: `gitlab`, `tavily`, `exa`, `notion`, `linear`, `atlassian`, `figma`, `sentry`, `vercel`, `cloudflare`, `netlify`, `supabase`, `stripe`, `resend`.

The **5** monochrome entries: `github` (`0x24292F`), `notion` (`0x000000`), `vercel` (`0x000000`), `resend` (`0x000000`), `deepwiki` (`0x1D1D1F`).

Brand colours, all 33 as `0xRRGGBB`: github `0x24292F`, gitlab `0xFC6D26`, playwright `0x2EAD33`, chrome-devtools `0x4285F4`, context7 `0x5B8DEF`, filesystem `0x8E8E93`, fetch `0x0A84FF`, memory `0xAF52DE`, sequential-thinking `0x30B0C7`, git `0xF05032`, brave-search `0xFB542B`, tavily `0x0D9488`, exa `0x1F4FFF`, firecrawl `0xFF6B1A`, perplexity `0x20808D`, slack `0x4A154B`, notion `0x000000`, linear `0x5E6AD2`, atlassian `0x0052CC`, figma `0xF24E1E`, sentry `0x7B61FF`, vercel `0x000000`, cloudflare `0xF38020`, netlify `0x00C7B7`, supabase `0x3ECF8E`, stripe `0x635BFF`, resend `0x000000`, postgres `0x4169E1`, sqlite `0x0F80CC`, mongodb `0x47A248`, huggingface `0xFFB000`, deepwiki `0x1D1D1F`, aws-docs `0xFF9900`. Asset names are `mcp-<id>` for all 33.

The **12** fields, verbatim:

| Entry | key | label | placeholder | kind | help | default | required |
| --- | --- | --- | --- | --- | --- | --- | --- |
| github | `token` | `Personal access token` | `ghp_…` | secret | `Create a fine-grained token on the page, then copy it` | - | yes |
| gitlab | `instance` | `GitLab address` | `gitlab.com` | text | `gitlab.com, or your own instance's host` | `gitlab.com` | **no** |
| context7 | `key` | `API key` | `ctx7sk-…` | secret | `Optional. Raises the rate limit` | - | **no** |
| filesystem | `root` | `Folder` | `/Users/you/Projects` | path | `The folder the server may use` | `NSHomeDirectory()` (computed) | yes |
| brave-search | `key` | `API key` | `BSA…` | secret | `Make a key on the page, then copy it` | - | yes |
| firecrawl | `key` | `API key` | `fc-…` | secret | `Make a key on the page, then copy it` | - | yes |
| perplexity | `key` | `API key` | `pplx-…` | secret | `Make a key on the page, then copy it` | - | yes |
| slack | `token` | `Bot token` | `xoxb-…` | secret | `Copy the Bot User OAuth Token from your app, then invite the bot to the channels it should read` | - | yes |
| postgres | `url` | `Connection string` | `postgresql://user:pass@host:5432/db` | secret | `The connection string; a read-only user is safest` | - | yes |
| sqlite | `path` | `Database file` | `/path/to/app.db` | path | `Created if it does not exist` | - | yes |
| mongodb | `url` | `Connection string` | `mongodb+srv://…` | secret | `In Atlas: Connect → Drivers, then copy the string` | - | yes |
| huggingface | `token` | `Access token` | `hf_…` | secret | `Optional. Raises the limits` | - | **no** |

The ellipses in the placeholders are the single character U+2026, not three dots. `mongodb`'s help contains U+2192 (a right arrow).

The **6** `env` maps, verbatim:

- brave-search: `["BRAVE_API_KEY": "{key}"]`
- firecrawl: `["FIRECRAWL_API_KEY": "{key}"]`
- perplexity: `["PERPLEXITY_API_KEY": "{key}"]`
- slack: `["SLACK_MCP_XOXB_TOKEN": "{token}", "SLACK_MCP_ADD_MESSAGE_TOOL": "true"]`
- mongodb: `["MDB_MCP_CONNECTION_STRING": "{url}"]`
- aws-docs: `["FASTMCP_LOG_LEVEL": "ERROR"]`

The **3** non-empty header maps on http entries: github `["Authorization": "Bearer {token}"]`, context7 `["Authorization": "Bearer {key}"]`, huggingface `["Authorization": "Bearer {token}"]`. The fourth http entry, deepwiki, declares `headers: [:]`.

The **8** `keysURL` values: github `https://github.com/settings/personal-access-tokens/new`, context7 `https://context7.com/dashboard`, brave-search `https://api-dashboard.search.brave.com/app/keys`, firecrawl `https://www.firecrawl.dev/app/api-keys`, perplexity `https://www.perplexity.ai/settings/api`, slack `https://api.slack.com/apps`, mongodb `https://cloud.mongodb.com`, huggingface `https://huggingface.co/settings/tokens`.

Catalog lookup helpers (`MCPCatalog.swift:450-481`): `entry(id:)` is a linear `first { $0.id == id }`; `entry(matching:)` normalizes by lowercasing and replacing `_` and space with `-`, matching ids first then names; `byCategory` walks `MCPCategory.allCases` in declaration order (`developer, browser, search, work, data, cloud, knowledge`) and drops empty groups, so the Settings page order is fixed by the enum, not by the entry array.

---

## 7. The store, the resolve pipeline and the GitLab setup guide

### 7.1 `enabledServers` filter and order

**SOURCE FACT** (`MCPStore.swift:41-49`): `connections.values.filter { $0.isEnabled && $0.connectedAt != nil }`, each mapped through `entry(for:)` (customs first, then catalog) and `entry.resolve(values: storedValues(for: entry))`, then `.sorted { $0.id < $1.id }`. A connection whose id resolves to no entry is dropped by `compactMap`.

### 7.2 `resolve`

**SOURCE FACT** (`MCP.swift:152-169`):

- `.stdio(command, args)`: `server.command = command`; `server.args = args.map(fill)`; `server.env = env.mapValues(fill).filter { !$0.value.isEmpty }` — an optional field left empty **drops its variable** rather than passing an empty string. The command itself is NOT filled.
- `.http(url, headers)`: `server.url = fill(url)`; `server.headers = headers.mapValues(fill).filter { !$0.value.isEmpty }` — same drop rule.
- `.oauth(url)`: `server.url = MCPProxy.url(for: id)` (the proxy route, NOT the upstream), and `server.oauthUpstream = fill(url)`.

### 7.3 `fill` host normalization

**SOURCE FACT** (`MCP.swift:135-150`). When the template contains the exact substring `"://" + "{key}"`, the value is trimmed of whitespace and newlines, then any leading `https://` or `http://` (case-insensitively matched via `value.lowercased().hasPrefix(scheme)`) is dropped, then every trailing `/` is removed in a `while` loop. Only then is the token replaced. Fields are processed in declaration order, so a value that itself contains another field's token would be substituted on a later pass. This rule exists for `gitlab`'s `https://{instance}/api/v4/mcp`.

### 7.4 `exportAll` order

**SOURCE FACT** (`MCPStore.swift:279-290`), in this exact order:

1. `MCPProxy.shared.replaceAll(...)` with one route per server that has a non-nil `oauthUpstream`, built as `(serverID: server.id, upstream: remote, headers: [:], usesOAuth: true)`.
2. `MCPProviderConfig.writeAll(servers)`.
3. `MCPExternalSync.sync(servers)`.
4. `Task { await MCPHub.shared.reload() }` (asynchronous, so the hub picks up the new config on its next use).

Called from `init()` (after `MCPProxy.shared.start()`), `disconnect`, `setEnabled`, `saveCustom` (only when a connection existed), and the probe's success path. **Not** called from `connect` or `saveCustomValues` directly; `connect` only `save()`s and starts the probe.

### 7.5 OAuth sign-out triggers inside the store

**SOURCE FACT** (`MCPStore.swift:90-102`, `:125-128`, `:323-333`). Four places sign out and unregister the proxy route:

1. `connect`, when the entry is OAuth and merging the current drafts changes `entry.oauthURL(values:)` (a different GitLab instance).
2. `connect`, when the entry is OAuth and the current state is `.failed` (so a Try again cannot reuse a rejected token).
3. `disconnect`, when the entry is OAuth.
4. `startProbe`, on `MCPProbeError.unauthorizedOAuth` OR on `MCPProbeError.http(401 or 403, _)`, in both cases only when `resolved.oauthUpstream != nil`, then it re-runs `probeOnce` exactly once. A `try Task.checkCancellation()` runs before each retry.

`probeOnce` (`MCPStore.swift:303-314`): when `resolved.oauthUpstream` is non-nil, sign in through `MCPOAuth.signIn` **only if not already signed in**, then always `MCPProxy.shared.register(...)`, then probe. So the probe always goes through the proxy route, exactly as the providers will.

### 7.6 The GitLab setup guide, verbatim

**SOURCE FACT** (`MCPProbe.swift:353-377`). `isGitLabMCP(_:)` is `URL(string:)?.path.lowercased().hasSuffix("/api/v4/mcp")`. `gitLabServerDisabled(upstream:)` computes the origin as `"\(url.scheme ?? "https")://\(host)"`, falling back to the literal `"https://gitlab.com"` when the URL or host is missing, and returns:

- title: `Turn on GitLab's MCP server`
- reason: `You're signed in, but GitLab keeps its MCP server off until a group owner turns it on. Until then, the address answers not found.`
- steps, in order, 4 of them:
  1. `Open your groups and pick the top-level group your projects live in`
  2. `Go to Settings › General and expand Permissions and group features`
  3. `Under MCP client access, tick Allow connection to GitLab and save`
  4. `Come back here and press Check again`
- pageLabel: `Open my groups`
- pageURL: `<origin> + "/dashboard/groups"`
- note: `GitLab can take a minute to apply the change.`

Step 2 contains U+203A (single right-pointing angle quotation mark), not a `>`.

Trigger conditions (`MCPProbe.swift:250-264`), in order:

1. `.http(401 or 403, body)` with a non-nil `oauthUpstream`: when status is 403 AND `body.localizedCaseInsensitiveContains("MCP server disabled")`, throw `.needsSetup(gitLabServerDisabled(upstream: oauthUpstream ?? urlString))`; otherwise `.unauthorizedOAuth`.
2. `.http(404, _)` with a non-nil `oauthUpstream`: when `isGitLabMCP(upstream)` throw `.needsSetup(...)`; else `.unavailable(host: URL(string: upstream)?.host ?? "The server")`.

The in-file comment (`:257-259`) records that GitLab 19.4 and earlier answer 404 rather than 403 for the same cause.

---

## 8. Unreachable, conditional and defect-bearing branches

**SOURCE FACT, `MCPProbe` OAuth-over-stdio path is unreachable from the catalog.** `probeStdio` sets `let isOAuth = server.args.contains("mcp-remote")` (`MCPProbe.swift:115`). No catalog entry has `mcp-remote` in its args (all 15 stdio entries are listed in section 6), and an OAuth catalog entry resolves to an http URL, never to stdio. The only way to reach it is a user-defined custom stdio server whose args contain the literal `mcp-remote`. Consequences that are therefore also effectively unreachable for catalog servers: the 240 s deadline (`:116`) and `exitError`'s `.unauthorizedOAuth` branch, which fires only when `isOAuth` is true AND stderr contains `"Unauthorized"` or `"invalid_grant"` (`:218-224`). `acpServers(allowsHTTP: false)` does produce `mcp-remote` argument vectors, but those go to an ACP agent, never to `MCPProbe`.

**SOURCE FACT, `MCPOAuth.discover`'s second discovery branch is reachable, not dead.** `protectedResourceMetadataURL` returns `(nil, nil)` whenever the probe request fails or the status is not exactly 401 (`MCPOAuth.swift:253-257`), which is the common case; the `else if let requestURL = URL(string: url)` binding at `:215` always succeeds because `URL(string: url)` was already validated non-nil at `:200`.

**SOURCE FACT, upstream oddities to record, not fix:**

1. `MCPOAuth.swift:218` — `let path = requestURL.path.isEmpty ? "" : requestURL.path` is a no-op ternary: both branches are the same value.
2. `MCPOAuth.swift:280` — `_ = data` discards the body of the 401 probe deliberately; only headers are used.
3. `MCPOAuth.swift:417` — `MCPOAuthError.registrationFailed(oauthErrorMessage(data: data) ?? refusal)` computes `refusal` (including the 403 string) even when the server supplied an `error_description`, so the crafted 403 message is only seen when the body carries no OAuth error.
4. `MCPHub.swift:412` — `replies[id] = nil` on the timeout path is a no-op: the key was just proven absent by `removeValue(forKey: id)` returning nil.
5. `MCPHub.swift:178-189` — `stop()` does not reset `nextID` or `didRestart`/`dead`, so a restarted client continues its id sequence.
6. `MCPHub.swift:311-322` — a stdio client restarts **once**; on a second death it sets `dead = true` permanently and every later call throws `"The server quit."` until `MCPHub.reload()` rebuilds the client.
7. `MCPProxy.swift:386-392` — per-byte SSE chunking (section 5).
8. `MCPOAuth.swift:818` — non-cryptographic RNG for the PKCE verifier and state (section 3.6).
9. `MCPStore.swift:236-239` — `saveCustomValues` persists nothing when there was no existing connection AND every value came out empty, so an all-empty edit silently leaves no record.
10. `MCPHub.swift:78-97` — `call` rejects an unknown tool only when a tool cache for that server already exists; on a cold hub the call is forwarded to the server regardless of whether the tool was listed.

---

## 9. Dead and debug-only APIs

**SOURCE FACT, verified by a repository-wide `grep` for each symbol.**

| Symbol | Site | Status |
| --- | --- | --- |
| `MCPOAuth.refreshIfNeeded(serverID:)` | `MCPOAuth.swift:193-195` | **Dead.** No call site anywhere in the repo (the only other `refreshIfNeeded` is `TokenLedger`'s, called from `TokenActivitySection.swift:97`). |
| `MCPProxy.stop()` | `MCPProxy.swift:142-154` | **Dead.** Nothing calls it; the proxy is started in `MCPStore.init` and never stopped. |
| `MCPCatalog.ids` | `MCPCatalog.swift:481` | **Dead.** No call site. |
| `MCPOAuth.successPage` / `failurePage` | `MCPOAuth.swift:837`, `:845` | Declared non-private `static` but used only inside `MCPOAuth` (`:532`, `:539`, `:546`, `:552`). Reachable, over-exposed. |
| `MCPExternalSync.sync(_ servers:)` parameter | `MCPExternalSync.swift:7` | **Unused parameter.** The body never reads `servers`; the function is a one-time cleanup gated on `managed.json` existing. |
| `MCPKeychain` under a capture run | `MCPKeychain.swift:76` | Debug/capture-only guard: `guard !CaptureRun.isEnabled else { return nil }` makes every secret read return nil when the app launched with `--website-captures` or `--tour-captures` (`CaptureRun.swift:10-27`). Writes and deletes are NOT guarded. |
| `MCPProbeError.commandMissing` default arm | `MCPProbe.swift:22` | Reachable only for a custom server's command: the catalog uses only `npx` and `uvx`, both of which have dedicated arms. |

All other public symbols in the thirteen files have at least one call site.

---

## 10. Cross-file summary of exact protocol constants for the port

**SOURCE FACT, consolidated.**

- Protocol version: `2025-06-18` (both as the `initialize` param and as the `MCP-Protocol-Version` HTTP header).
- Client name: `Droppy Code`. Client version: `1.0` (Swift clients only).
- Methods out: `initialize`, `notifications/initialized`, `tools/list`, `tools/call`. Nothing else.
- Params keys out: `protocolVersion`, `capabilities`, `clientInfo` (`name`, `version`), `cursor`, `name`, `arguments`.
- Fields in: `jsonrpc`, `id`, `result`, `error.message`, `result.serverInfo.name`, `result.serverInfo.version`, `result.tools[].name`, `result.tools[].description`, `result.tools[].inputSchema`, `result.nextCursor`, `result.content[].text`, `result.content[].type`, `result.content[].resource.uri`, `result.isError`.
- HTTP headers out: `Content-Type: application/json`, `Accept: application/json, text/event-stream`, `MCP-Protocol-Version: 2025-06-18`, `Mcp-Session-Id`.
- HTTP header in: `Mcp-Session-Id`, `Content-Type`.
- Tool call name grammar: `mcp__<server>__<tool>`.
- OAuth redirect URI: `http://127.0.0.1:<ephemeral port>/callback`.
- OAuth PKCE: `S256`, verifier 32 random bytes base64url, state 24 random bytes base64url.
- OAuth scopes: never hardcoded; from `scopes_supported` or the `scope` challenge parameter, joined by a single space, omitted when absent.
- Keychain account format: `"MCP " + <server id> + " " + <field>`; OAuth fields `oauth` and `oauth-client`.
- Proxy route: `http://127.0.0.1:<port>/mcp/<server id>`; unknown route answers 502, never 404.
- Provider root keys: Claude `mcpServers`, Codex `mcp_servers`, Copilot `mcpServers`, Gemini `mcpServers`, OpenCode `mcp` (inside `OPENCODE_CONFIG_CONTENT`), ACP a bare array.
- Catalog: 33 entries, 14 of them OAuth.
