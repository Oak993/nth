# Provider argument vectors and handshakes, verbatim

Source: pinned read-only checkout `E:/Droppy/reference/droppy-code`, commit `dd2b86efef9e77168f304a86e8a8411877578bbe`.
All paths below are relative to `DroppyCode/Services/Providers/` unless stated otherwise.

Files read completely, reconciled against `wc -l`:

| File | Lines | Ranges read |
| --- | --- | --- |
| ClaudeSession.swift | 814 | 1-280, 280-560, 560-814 |
| CodexSession.swift | 1034 | 1-300, 300-650, 650-1034 |
| CopilotSession.swift | 1075 | 1-300, 300-620, 620-1075 |
| ACPSession.swift | 920 | 1-290, 290-620, 620-920 |
| CommandCodeSession.swift | 771 | 1-290, 290-560, 560-771 |
| PiSession.swift | 654 | 1-290, 290-654 |
| AntigravitySession.swift | 727 | 1-280, 280-520, 520-727 |
| PiCLI.swift | 217 | 1-217 (full, `cat -n`) |

Supporting files read for values these eight reference:
`../MCP/MCPSessionOverrides.swift` (14-30), `../MCP/MCPProviderConfig.swift` (77-236),
`ProviderRegistry.swift` (146), `PlanLimits.swift` (252-288).

Every claim is labelled **SOURCE FACT**, **ENGINEERING DECISION** or **UNRESOLVED**.

---

## 0. Adjudication of the two critic findings

### 0.1 "R041 misstates the Claude handshake argv" — **UPHELD**

**SOURCE FACT.** R041 states, verbatim:

> `handshake() argv omits --include-partial-messages and adds --no-session-persistence; it sends request_id "handshake" and only accepts a control_response whose response.request_id == "handshake"; 20-second watchdog terminate`

The actual handshake argv (`ClaudeSession.swift:188`) is:

```swift
arguments: ["-p", "--input-format", "stream-json", "--output-format", "stream-json", "--verbose", "--no-session-persistence", "--setting-sources", "user,project,local"],
```

Measured against the session base argv (`ClaudeSession.swift:85-90`), the handshake omits **four** things,
not one:

1. `--include-partial-messages`
2. `--permission-prompt-tool stdio` (two tokens)
3. `--allow-dangerously-skip-permissions`
4. `--permission-mode <permissionMode>` (two tokens)

It adds exactly one: `--no-session-persistence`, positioned **after `--verbose` and before `--setting-sources`**,
whereas in the session argv `--setting-sources user,project,local` sits between `--permission-prompt-tool stdio`
and `--allow-dangerously-skip-permissions`. So it is not a subset in order either.

The handshake argv carries **none** of the conditional session arguments (`--model`, `--effort`,
`--settings`, `--mcp-config`/`--strict-mcp-config`, `--system-prompt-snapshot`, `--agents`,
`--forward-subagent-text`, `--disallowedTools`, `--append-system-prompt`, `--resume`/`--resume-session-at`,
`--session-id`), because `handshake` builds its array as a literal and never appends to it.

R041's *protocol* claims about the handshake — request_id `"handshake"`, the strict
`response.request_id == "handshake"` filter, the 20-second watchdog — are **correct**, confirmed at
`ClaudeSession.swift:193-198` and `:198`. Its *argv* claim is **incomplete and therefore misleading**: it
describes a one-flag delta where the real delta is four omissions, one addition and an ordering change.

Where the critic would be **wrong**: R041's *base* session argv is reproduced exactly and matches
`:85-90` token for token. The defect is confined to the handshake sentence.

**SOURCE FACT.** A second R041 claim belongs to a *different file* and is accurate there:

> `CLI fallback launches -p --input-format stream-json --output-format stream-json --verbose in the system temporary directory, sends request_id "droppy-code-init" with {"subtype":"initialize","hooks":null}, then on its response sends request_id "droppy-code-usage" with {"subtype":"get_usage"}`

That code is `PlanLimits.swift:253-288`, not `ClaudeSession.swift`. Verified at `PlanLimits.swift:256`
(`arguments: ["-p", "--input-format", "stream-json", "--output-format", "stream-json", "--verbose"]`),
`:257` (`directory: FileManager.default.temporaryDirectory`), `:274`, `:280`.

### 0.2 "R044 omits the Copilot model filter and the entire account.getQuota percent/skip logic" — **UPHELD, both halves**

**SOURCE FACT**, from grepping `R044.json`:

- The token `disabled` occurs **once**, inside a sentence about MCP servers being "disabled by name for that
  session". The model-list filter `model["policy"]?["state"]?.string != "disabled"` is **absent**. R044 says
  only `Model fields read: models[].id, .name, .policy.state, .billing.multiplier, .supportedReasoningEfforts,
  .defaultReasoningEffort` and treats `policy.state == "unconfigured"` as a *detail string*, never naming
  `"disabled"` as a *skip* condition.
- `isUnlimitedEntitlement` occurs **once**, in a bare field roster: `Quota snapshot keys read:
  quotaSnapshots.premium_interactions, quotaSnapshots.chat, quotaSnapshots.completions; sub-fields
  isUnlimitedEntitlement, entitlementRequests, remainingPercentage, usedRequests, resetDate`. There is **no**
  percent formula, **no** clamp, **no** `entitlement > 0` guard, **no** `!snapshot.isNull` guard, **no**
  `guard !windows.isEmpty else { return nil }`. The window titles `Premium requests`, `Chat messages`,
  `Code completions` do not appear anywhere in R044.
- `skipped`, `filtered` and `excluded` do not occur at all.

R044 *does* correctly record that `ProviderRegistry.copilotAuto` "is prepended ahead of every listed model".
Both omissions the critic names are real. The full rules are given in sections 3.6 and 3.7.

---

## 1. Claude (`ClaudeSession.swift`)

### 1.1 Executable and session argument vector

**SOURCE FACT.** Executable: `configuration.executable` (a `URL`); `start()` throws
`ProviderError.notInstalled(configuration.provider)` when it is nil (`:83`). This file never hard-codes a
binary name.

`ClaudeSession.start()` at `:82`. Base array, verbatim (`:85-90`), **14 tokens** in this order:

```
-p
--input-format
stream-json
--output-format
stream-json
--verbose
--include-partial-messages
--permission-prompt-tool
stdio
--setting-sources
user,project,local
--allow-dangerously-skip-permissions
--permission-mode
<permissionMode>
```

`<permissionMode>` is `Self.modeName(configuration.runtimeMode, configuration.interactionMode)`, computed in
`init` (`:60`). The mapping (`:805-813`), the full 5-row table with exact strings:

| Condition | Value |
| --- | --- |
| `interaction == .plan` (tested first, overrides runtime) | `plan` |
| `.supervised` | `default` |
| `.autoAcceptEdits` | `acceptEdits` |
| `.auto` | `auto` |
| `.fullAccess` | `bypassPermissions` |

Conditional arguments, **appended in exactly this order**:

1. `:91` — `if let model = configuration.model, model != "default"` → `["--model", model]`
2. `:92` — `if let effort = configuration.effort, !effort.isEmpty` → `["--effort", effort]`
3. `:93` — `if configuration.fastMode` → `["--settings", #"{"fastMode":true}"#]`.
   The second token is the literal JSON text `{"fastMode":true}` (Swift raw string, no escapes).
4. `:96` — `if let mcpConfig = MCPProviderConfig.claudeConfigPath()` →
   `["--mcp-config", mcpConfig, "--strict-mcp-config"]` — **three tokens under one `if`**.
   `claudeConfigPath()` (`../MCP/MCPProviderConfig.swift:212-215`) returns `MCPPaths.claudeConfigURL.path`
   only when `serversPresent(in:key:"mcpServers")`, else nil, so with no connected MCP servers **neither**
   flag is passed and Claude reads its own config (comment `:94-95`).
5. `:100` — `if options.contains("--system-prompt-snapshot")` → `["--system-prompt-snapshot", "off"]`
6. `:102-120` — Hydra block, only `if let hydra = configuration.hydra`:
   - `hydra.runsNatively == true`:
     - `:108` → `["--agents", HydraPrompts.claudeAgents(hydra).compactString]`
     - `:109` → `hydraPolicy = HydraPrompts.policy(for: .claude, hydra)`
     - `:110` — `if options.contains("--forward-subagent-text")` → `arguments.append("--forward-subagent-text")` (one token)
     - `:112` — `if let cap = hydra.maxHeads` → sets the **environment** key
       `CLAUDE_CODE_MAX_CONCURRENT_SUBAGENTS = String(cap)`, not an argument
   - else (heads run on another provider):
     - `:117` → `hydraPolicy = HydraPrompts.fallbackPolicy(hydra)`
     - `:118` → `["--disallowedTools", "Agent", "Task"]` — **three tokens**: the flag, then two separate bare
       values `Agent` and `Task`, not one comma-joined value.
7. `:122-128` — **unconditional**, every session, Hydra or not:
   ```
   --append-system-prompt
   <[hydraPolicy, AntiSlopPolicy.instructions(enabled: configuration.antiSlopEnabled)].compactMap { $0 }.joined(separator: "\n\n")>
   ```
   With Hydra off and anti-slop off the second token is the empty string; the flag is still passed.
   Comment `:121-122`: "Exactly one combined system prompt segment for every session, Hydra or not."
8. `:129-134` — mutually exclusive tail:
   - `if let resumeID = configuration.resumeID` → `["--resume", resumeID]` (`:130`), then
     `if let anchor = configuration.resumeAt` → `["--resume-session-at", anchor]` (`:131`)
   - else → `["--session-id", id]` (`:133`), where `id = configuration.resumeID ?? UUID().uuidString.lowercased()` (`:84`)

**ENGINEERING DECISION (port).** Ordering is load-bearing for byte-parity of the spawned command line: the
eight groups must be appended in the order above, `--append-system-prompt` after the Hydra flags and before
`--resume`/`--session-id`.

### 1.2 `knownOptions` capability probe

**SOURCE FACT** (`:72-80`, called at `:98`). Cached per executable path in
`private static var knownOptions: [String: Set<String>]` (`:56`). On a cache miss it runs
`Shell.run(executable, ["--help"], environment: environment, timeout: 20)`, strips ANSI from
`result.output + result.errorOutput`, and keeps those members of the literal two-element array
`["--system-prompt-snapshot", "--forward-subagent-text"]` whose text appears in the help output. A failed
probe (`try?` yields nil) returns `[]` **and is not cached**, so the next launch probes again (comment `:69-71`).

### 1.3 Environment variables set

**SOURCE FACT.** Starts from `configuration.environment` (`:97`). The only key this file writes is
`CLAUDE_CODE_MAX_CONCURRENT_SUBAGENTS` (`:112`), value `String(cap)` from `hydra.maxHeads`, set only when
Hydra is on **and** runs natively **and** `maxHeads` is non-nil.

### 1.4 Session handshake (initialize)

**SOURCE FACT** (`:154-160`). After the process starts and the read task is attached:

```swift
var initialize: [String: JSONValue] = ["subtype": "initialize", "hooks": .null]
if configuration.hydra?.runsNatively == true {
    initialize["agentProgressSummaries"] = true
    initialize["forwardSubagentText"] = true
}
let initialization = try await control(.object(initialize))
```

The wire object is `{"subtype":"initialize","hooks":null}`, plus, only under native Hydra, the two boolean
fields `"agentProgressSummaries": true` and `"forwardSubagentText": true`.

`control(_:)` (`:302-311`) wraps it:

```json
{"type":"control_request","request_id":"droppy-code-<n>","request":<the object above>}
```

`<n>` is `controlCounter`, pre-incremented from 0, so the first request id is `droppy-code-1`. The literal
prefix is `droppy-code-` (`:305`).

After the initialize returns, `isStarted = true`, then:
- `Self.models(in: initialization)` → `.models(models, current: configuration.model)` when non-empty
- `SlashCommand.claudeCommands(initialization["commands"]?.array ?? [])` → `.commands(commands)` when non-empty

Model fields read (`:165-178`): top-level `models` array; per entry `value`, `displayName`, `description`,
`supportedEffortLevels`, `supportsFastMode`. `isDefault` is `value == "default"`; `fastTier` is the literal
`"fast"` when `supportsFastMode == true`, else nil.

### 1.5 Static `handshake(executable:directory:environment:)` — the catalog probe

**SOURCE FACT** (`:185-211`). A bare process, not a session: "it gets the initialize and nothing else" (`:184`).

Argv, verbatim (`:188`), **9 tokens**:

```
-p
--input-format
stream-json
--output-format
stream-json
--verbose
--no-session-persistence
--setting-sources
user,project,local
```

- Watchdog: `Task { try? await Task.sleep(for: .seconds(20)); if !Task.isCancelled { process.terminate() } }`
- `defer { watchdog.cancel(); process.terminate() }`
- Sends (`:198`), verbatim:
  ```json
  {"type":"control_request","request_id":"handshake","request":{"subtype":"initialize","hooks":null}}
  ```
  The literal request id is `handshake`; the `droppy-code-<n>` counter path is not used here.
- Accept filter (`:200-201`): `message["type"]?.string == "control_response"` **and**
  `message["response"]?["request_id"]?.string == "handshake"`; anything else `continue`s.
- Failure strings, verbatim:
  - missing `response`, or missing `response["commands"]` array → `ProviderError.failed("Claude could not load its commands.")` (`:204`)
  - stream ends with no match → `ProviderError.failed("Claude's command discovery did not finish. Try again.")` (`:208`)
- `onCancel: { process.terminate() }`

### 1.6 Method names, both directions

**SOURCE FACT.** App → CLI, one JSON object per line:

| Site | Object |
| --- | --- |
| `control` `:310` | `{"type":"control_request","request_id":"droppy-code-<n>","request":<request>}` |
| `writeUserMessage` `:317-322` | `{"type":"user","session_id":"","message":{"role":"user","content":<content>},"parent_tool_use_id":null}` |
| `sendToolResponse` `:326-329` | `{"type":"control_response","response":{"subtype":"success","request_id":"<id>","response":<response>}}` |
| unsupported control request `:400-403` | `{"type":"control_response","response":{"subtype":"error","request_id":"<id>","error":"Droppy Code does not support this request."}}` |

Control request subtypes the app sends, verbatim: `initialize` (`:154`, `:198`), `set_permission_mode`
with field `mode`, `set_model` with field `model`, `interrupt`, `stop_task` with field `task_id`.

CLI → app `type` values handled (`:329-383`), **9** plus default:
`control_response`, `control_request`, `control_cancel_request`, `system`, `stream_event`, `assistant`,
`user`, `result`, `rate_limit_event`.

`system` subtypes handled (`:385-418`), **5** plus default:
`init`, `compact_boundary`, `task_started`, `task_progress`, `task_notification`.

The only `control_request` subtype the app accepts from the CLI is `can_use_tool`; every other subtype gets
the error response above.

`stream_event` `event.type` handled: `message_start`, `content_block_start`, `content_block_delta`.
Content block types: `text`, `thinking`, `tool_use`. Delta types: `text_delta`, `thinking_delta`.

Tools whose results never get a row, `hiddenResultTools` (`:691`), exactly **5**:
`AskUserQuestion`, `ExitPlanMode`, `EnterPlanMode`, `TodoWrite`, `ToolSearch`.

Synthetic-transcript constants (`:492-493`): model name `<synthetic>`, placeholder set with exactly **2**
members: `No response requested.` and `(no content)`.

### 1.7 Resume / session arguments

**SOURCE FACT** (`:84`, `:129-134`, `:560-568`). `id = configuration.resumeID ?? UUID().uuidString.lowercased()`.
With a resume id: `--resume <resumeID>`, optionally followed by `--resume-session-at <anchor>`; without:
`--session-id <id>`. `start()` returns `id`.

If the CLI cannot resume, it answers with a `result` **before** the initialize is answered
(`error_during_execution`, no turns, its `errors` naming the session) and exits. `handleResult` (`:560-568`)
sees `isStarted == false`, stores the joined `errors` array into `launchError`, and `didClose` throws it from
`start()`. Comment `:561-564` records this precisely.

### 1.8 Upstream defects to reproduce

**SOURCE FACT.** `--append-system-prompt` is passed unconditionally even when both `hydraPolicy` and the
anti-slop instructions are absent, producing a flag with an empty-string value. Recorded, not fixed.

**SOURCE FACT.** `--disallowedTools Agent Task` emits two bare positional values after one flag. A parity
port must emit the same three tokens.

---

## 2. Codex (`CodexSession.swift`)

### 2.1 Executable and argument vector

**SOURCE FACT** (`:507`, inside `private static func connect`). There is exactly **one** argv in the whole
file, shared by `start()`, `listCommands`, `listModels`, `readPlanLimits` and `consumeResetCredit`:

```
app-server
```

**One token.** No conditional arguments at all; everything else is negotiated over JSON-RPC.

Framing: `JSONRPCConnection(process: process, sendsVersion: false)` (`:508`) — **no** `Content-Length`
header, and `sendsVersion: false` means no `"jsonrpc":"2.0"` field is emitted.

### 2.2 Environment

**SOURCE FACT.** None. `configuration.environment` is passed through unmodified; this file writes no key.

### 2.3 Handshake — `initialize` then `initialized`

**SOURCE FACT** (`:517-525`). After `connection.start()` and a 20-second watchdog
(`Task { try? await Task.sleep(for: .seconds(20)); if !Task.isCancelled { connection.close() } }`):

```swift
_ = try await connection.request("initialize", [
    "clientInfo": ["name": "droppy-code", "title": "Droppy Code", "version": .string(AppInfo.version)],
    "capabilities": ["experimentalApi": true],
])
```

Exact names and literals: method `initialize`; `clientInfo.name` = `droppy-code`; `clientInfo.title` =
`Droppy Code`; `clientInfo.version` = `AppInfo.version`; `capabilities.experimentalApi` = `true`.

On throw: `connection.close()` then rethrow. On success: `connection.notify("initialized")` (`:525`) — a
notification with **no** params, method name `initialized`.

### 2.4 Thread start / resume parameters

**SOURCE FACT.** `threadParameters` (`:221-248`), rebuilt on every access.

Base, in written order:
```swift
["cwd": .string(workingDirectory),
 "approvalPolicy": policy.approvalPolicy,
 "sandbox": policy.sandbox,
 "approvalsReviewer": policy.reviewer]
```
plus `"model"` when `configuration.model` is non-nil.

`policySettings(_:)` (`:1022-1033`), the full 4-row table with exact strings:

| RuntimeMode | approvalPolicy | sandbox | sandboxPolicy | reviewer |
| --- | --- | --- | --- | --- |
| `.supervised` | `untrusted` | `read-only` | `{"type":"readOnly"}` | `user` |
| `.autoAcceptEdits` | `on-request` | `workspace-write` | `{"type":"workspaceWrite"}` | `user` |
| `.auto` | `on-request` | `workspace-write` | `{"type":"workspaceWrite"}` | `auto_review` |
| `.fullAccess` | `never` | `danger-full-access` | `{"type":"dangerFullAccess"}` | `user` |

Note: `threadParameters` uses `policy.sandbox` (a plain string) under key `sandbox`; the turn parameters use
`policy.sandboxPolicy` (an object) under key `sandboxPolicy`. Both exist on every row.

Hydra:
- `runsNatively` → `config.merge(HydraPrompts.codexConfig(hydra)) { _, hydra in hydra }` and
  `params["developerInstructions"] = .string(HydraPrompts.policy(for: .codex, hydra))`
- else → `config["features"] = ["multi_agent": .bool(false)]` and
  `params["developerInstructions"] = .string(HydraPrompts.fallbackPolicy(hydra))`.
  Comment: told only in words, a lead still reached for `spawn_agent`, so the feature is switched off for the
  thread whatever the user's config enables.

Then `if !config.isEmpty { params["config"] = .object(config) }`.

Anti-slop, **unconditional**:
```swift
params["developerInstructions"] = .string(
    [existingInstructions, AntiSlopPolicy.instructions(enabled: configuration.antiSlopEnabled)]
        .compactMap { $0 }.joined(separator: "\n\n"))
```

MCP: `if let servers = MCPProviderConfig.codexOverrides()` → `config["mcp_servers"] = servers`, merged into
whatever `config` already holds, never replacing it. `codexOverrides()`
(`../MCP/MCPProviderConfig.swift:199-210`) returns nil when `MCPPaths.codexConfigURL` has no non-empty
`mcp_servers` object; otherwise it returns that object with `{"enabled": false}` added for every name in
`codexUserServerNames()` not already present.

### 2.5 Start vs resume

**SOURCE FACT** (`:80-101`):
- Resume: `params["threadId"] = .string(resumeID)`, `params["excludeTurns"] = true`, method `thread/resume`.
  Guard `result["thread"]?["id"]?.string == resumeID`, else
  `ProviderError.failed("Codex could not resume the original conversation.")`.
- New: method `thread/start`. Guard `result["thread"]?["id"]?.string` non-nil, else
  `ProviderError.failed("Codex did not start a thread.")`.
- Both then set `activeModel = result["model"]?.string`.

`requestThread` retry (`:104-120`): catches `RPCError` whose message contains `failed to load configuration`
**or** `invalid transport`, **and only when** `params["config"]?["mcp_servers"] != nil`. It strips
`config["mcp_servers"]`, drops `config` entirely if it becomes empty, retries, and emits the warning notice,
verbatim:
`Codex refused the MCP server configuration, so this session runs without MCP servers. Check MCP in Settings.`

### 2.6 Turn parameters (`turn/start`)

**SOURCE FACT** (`:130-152`):

```swift
["threadId": .string(threadID),
 "input": .array(content),
 "approvalPolicy": policy.approvalPolicy,
 "approvalsReviewer": policy.reviewer,
 "sandboxPolicy": policy.sandboxPolicy,
 "summary": "auto"]
```
`"summary"` is the literal string `auto` (`:139`). Then conditionally:
- non-empty effort → `params["effort"] = .string(effort)`
- `input.serviceTier` → `params["serviceTierForTurn"] = .string(tier)`
- `input.model ?? activeModel` → `params["model"] = .string(model)` **and** (`:146-153`):
  ```swift
  params["collaborationMode"] = [
      "mode": input.interactionMode == .plan ? "plan" : "default",
      "settings": ["model": .string(model),
                   "reasoning_effort": .optional(effort),
                   "developer_instructions": .null],
  ]
  ```
  **SOURCE FACT**, mixed casing to reproduce exactly: `collaborationMode` and `mode` are camelCase, while the
  nested `reasoning_effort` and `developer_instructions` are snake_case.

Content parts (`:126-129`): `input.command?.codexInput(for: input.text)` or
`[["type":"text","text":<text>]]`, then per image with `image.isImage`:
`["type":"localImage","path":<path>]`.

### 2.7 Method names, both directions

**SOURCE FACT.** App → server requests, **14 distinct** method names:
`initialize`, `thread/resume`, `thread/start`, `turn/start`, `turn/interrupt` (also used to stop one head),
`thread/compact/start`, `thread/turns/list`, `thread/rollback`, `thread/fork`, `thread/archive`,
`skills/list`, `model/list`, `account/rateLimits/read`, `account/rateLimitResetCredit/consume`.
App → server notification, **1**: `initialized`.

Server → app notifications handled in `handleNotification`, **17 distinct** names:
`thread/started`, `turn/started`, `item/started`, `item/completed`, `item/agentMessage/delta`,
`item/reasoning/summaryTextDelta`, `item/reasoning/textDelta`, `item/reasoning/summaryPartAdded`,
`item/commandExecution/outputDelta`, `item/plan/delta`, `turn/plan/updated`, `turn/diff/updated`,
`thread/tokenUsage/updated`, `error`, `turn/completed`, `serverRequest/resolved`, `thread/compacted`.
(`item/reasoning/summaryTextDelta` and `item/reasoning/textDelta` share one `case` label.)

Server → app requests handled, **5**:
`item/commandExecution/requestApproval`, `item/fileChange/requestApproval`,
`item/permissions/requestApproval`, `item/tool/requestUserInput`, `mcpServer/elicitation/request`.
Anything else → `respond(to: id, errorCode: -32601, message: "Droppy Code does not support \(method).")`.

`mcpServer/elicitation/request` is always answered `["action": "decline"]` and produces the notice
`\(server) asked for input, which Droppy Code cannot show yet.` with `server` defaulting to
`An MCP server`.

Decision option names (`decisionOptions`, `:983-1008`), the default list when the server sends none is the
4-element literal `["accept", "acceptForSession", "decline", "cancel"]`, mapped to titles
`Approve`, `Approve for session`, `Decline`, `Stop turn`. A command decision object carrying
`acceptWithExecpolicyAmendment` adds the option id `amend`, title `Always allow`. Options are then sorted by
the role order `[.approve, .approveAlways, .decline, .cancel]`.

### 2.8 Catalog and limits call arguments

**SOURCE FACT.**
- `listCommands`: `connection.request("skills/list", ["cwds": [.string(directory.path)], "forceReload": true])`,
  20-second watchdog, `defer { watchdog.cancel(); connection.close() }`.
- `listModels`: directory `FileManager.default.temporaryDirectory`; loop `for _ in 0..<10`; `cursor` is only
  included when the previous `nextCursor` was non-null; entries filtered `where model["hidden"]?.bool != true`.
- `readPlanLimits` / `consumeResetCredit`: directory `FileManager.default.temporaryDirectory`.
  `consumeResetCredit` params: `["idempotencyKey": .string(UUID().uuidString.lowercased())]` plus `"creditId"`
  when one is given.
- `readPlanLimits` snapshot collection: `rateLimitsByLimitId` object keys **sorted**, else the single
  `rateLimits` snapshot. Per snapshot it walks the two-element literal `["primary", "secondary"]` and reads
  `usedPercent`, `windowDurationMins`, `resetsAt`, `limitId`, `limitName`, `planType`. Window id is
  `"\(limitId ?? "codex")-\(key)"`. Empty result → `PlanLimits(..., problem: "Codex reported no usage windows for this account.")`.
  Auth-expiry hint: a message containing `token_expired` or `401` maps to
  `Codex's stored login has expired. Run \`codex login\` in a terminal, then check again.`

---

## 3. Copilot (`CopilotSession.swift`)

### 3.1 Executable and argument vector

**SOURCE FACT.** Executable named in the header doc (`:2`, `:6`): `copilot`. Argv built in
`private static func connect` (`:483-485`), shared by `start()`, `listModels`, `authStatus` and
`readPlanLimits`:

```swift
var arguments = ["--headless", "--stdio", "--no-auto-update", "--log-level", "error"]
arguments += MCPSessionOverrides.copilotArguments()
```

Base: **5 tokens** in order — `--headless`, `--stdio`, `--no-auto-update`, `--log-level`, `error`.

`MCPSessionOverrides.copilotArguments()` (`../MCP/MCPSessionOverrides.swift:14-30`), verbatim behaviour:

1. `guard let json = MCPProviderConfig.copilotConfigJSON() else { return [] }` — with no connected servers,
   **nothing at all** is appended.
2. `ids` = the keys of `root["mcpServers"]` parsed out of that JSON (empty set if parsing fails).
3. `var arguments = ["--additional-mcp-config", json]` — the **entire config JSON text is one argv token**.
4. `for name in copilotUserServerNames() where !ids.contains(name) { arguments += ["--disable-mcp-server", name] }`
   — one flag+value pair per user server not among the connected ids, in whatever order
   `copilotUserServerNames()` yields.
5. `if ids.contains("github") { arguments.append("--disable-builtin-mcps") }` — the literal server id
   compared against is `github`.

Framing: `StdioProcess(..., framing: .contentLength)` and `JSONRPCConnection(process:, sendsVersion: true)`
— Content-Length framed, `jsonrpc` version emitted.

### 3.2 Environment

**SOURCE FACT.** None. `configuration.environment` passes through unmodified.

### 3.3 Handshake — `ping`

**SOURCE FACT** (`:497-502`):

```swift
let pong = try await connection.request("ping", ["message": "droppy-code"])
guard pong["protocolVersion"]?.int != nil else {
    connection.close()
    throw ProviderError.failed("This `copilot` does not speak the SDK protocol. Update it with `copilot update`.")
}
```

Method `ping`; single param field `message`, literal value `droppy-code`. The accept test is that the reply's
`protocolVersion` parses as an `Int` — the value itself is **never compared**. The failure string is verbatim,
backticks included. There is **no watchdog** on this handshake.

Header doc `:22-24` records the verification target: `copilot 1.0.83`, protocol version 3, and lists what was
verified live: framing, ping, account auth, session create/resume/delete, commands, agent and permission
modes, model and effort switching, rewind points, compaction and the event envelope. **SOURCE FACT**,
documentation only — the code never compares against 3.

### 3.4 Session create / resume parameters

**SOURCE FACT** (`:100-110`, `sessionParameters()` `:121-155`):

```swift
let id = configuration.resumeID ?? UUID().uuidString.lowercased()
var params = sessionParameters()
params["sessionId"] = .string(id)
_ = try await connection.request(configuration.resumeID == nil ? "session.create" : "session.resume", .object(params))
```

Method `session.create` for a fresh id, `session.resume` for a resume; the same parameter object either way,
plus `sessionId`.

`sessionParameters()` base object, exact keys and literal values:

```swift
["clientName": "droppy-code",
 "workingDirectory": .string(workingDirectory),
 "streaming": true,
 "requestPermission": true,
 "requestUserInput": true,
 "requestExitPlanMode": true,
 "includeSubAgentStreamingEvents": .bool(configuration.hydra?.runsNatively == true),
 "isExperimentalMode": .bool(configuration.runtimeMode == .auto)]
```

Then:
- `if let model, !model.isEmpty` → `params["model"] = .string(model)`
- `if let effort, !effort.isEmpty` → `params["reasoningEffort"] = .string(effort)`
- Hydra `runsNatively` → `params["customAgents"] = .array(HydraPrompts.copilotAgents(hydra))` and
  `hydraPolicy = HydraPrompts.policy(for: .copilot, hydra)`; else `hydraPolicy = HydraPrompts.fallbackPolicy(hydra)`
  and **no** `customAgents` key at all.
- **Unconditional**:
  ```swift
  params["systemMessage"] = ["mode": "append",
                             "content": .string([hydraPolicy, AntiSlopPolicy.instructions(enabled: configuration.antiSlopEnabled)]
                                 .compactMap { $0 }.joined(separator: "\n\n"))]
  ```
  `mode` is the literal string `append`.

After create/resume: `await applyModes(...)` then `loadCommands()`. Comment `:106-107`: a resumed session
comes back in interactive mode with manual permissions (verified live), so the thread's modes are applied
either way.

**SOURCE FACT**, restart coupling: `isExperimentalMode` is a **session-creation** parameter, and the doc
comment `:114-117` states that switching into or out of Auto therefore requires restarting the session.

### 3.5 Method names, both directions

**SOURCE FACT.** App → server requests, **18 distinct**:
`ping`, `session.create`, `session.resume`, `session.send`, `session.commands.invoke`, `session.abort`,
`session.history.compact`, `session.history.listRewindPoints`, `session.history.rewind`,
`session.permissions.setMode`, `session.mode.set`, `session.model.switchTo`,
`session.model.setReasoningEffort`, `session.commands.list`, `models.list`, `account.getCurrentAuth`,
`account.getQuota`, `session.permissions.handlePendingPermissionRequest`, `runtime.shutdown` (19 counting
create and resume separately).

Exact payloads worth reproducing:
- `session.send`: `["sessionId": …, "prompt": …]` plus `attachments` when non-empty, each element
  `["type":"file","path":…,"displayName":…,"mimeType":…]`, `"type"` being the literal `file`.
- `session.abort`: `["sessionId": …, "reason": "user_initiated"]`.
- `session.history.compact`: `["sessionId": …, "trigger": "manual"]`.
- `session.history.rewind`: `["sessionId": …, "eventId": …, "mode": "conversation"]`; success test
  `result["outcome"]?.string == "success"`; rewind point selection is `points[points.count - turns]["eventId"]`.
- `session.permissions.setMode`: `["sessionId": …, "mode": …, "source": "rpc"]`.
- `session.mode.set`: `mode` is `plan` or `interactive`.
- `session.permissions.handlePendingPermissionRequest`: `["sessionId": …, "requestId": …, "result": <decision>]`.

Permission mode names (`permissionModeName`, `:374-380`), full table:

| RuntimeMode | mode |
| --- | --- |
| `.supervised` | `manual` |
| `.autoAcceptEdits` | `manual` |
| `.auto` | `assisted` |
| `.fullAccess` | `allow-all` |

Permission decision objects:
- approve once: `["kind": "approve-once", "approvedInteractively": true]`
- reject: `["kind": "reject", "feedback": "The user declined this action."]`
- cancelled: `["kind": "cancelled", "reason": "The user stopped the turn."]`
- automatic: `["kind": "approve-once"]` (no `approvedInteractively`)
- session: `["kind": "approve-for-session", "approval": <inner>]`, or for a URL directly
  `["kind": "approve-for-session", "domain": <host>]`

Server → app: exactly **one** notification method, `session.event`, dispatched on `event.type`. The **23**
handled types:
`assistant.turn_start`, `assistant.message_delta`, `assistant.message`, `assistant.reasoning_delta`,
`assistant.reasoning`, `assistant.usage`, `session.usage_info`, `tool.execution_start`,
`tool.execution_partial_result`, `tool.execution_progress`, `tool.execution_complete`, `subagent.started`,
`subagent.completed`, `subagent.failed`, `permission.requested`, `permission.completed`,
`session.mode_changed`, `session.title_changed`, `session.error`, `session.warning`,
`session.compaction_complete`, `session.idle`, `session.shutdown`.

Server → app requests handled, **3**: `userInput.request`, `exitPlanMode.request`, `autoModeSwitch.request`.
`autoModeSwitch.request` is always answered `["response": "no"]` (comment: a rate limit offered a switch to
the Auto model tier; keep the model the user chose). Anything else →
`errorCode: -32601, message: "Droppy Code does not support \(method)."`

Commands Droppy Code answers with its own controls, `ownCommands` (`:72`), exactly **10**:
`allow-all`, `yolo`, `permissions`, `model`, `models`, `plan`, `session`, `sessions`, `cwd`, `cd`.

Tools that never get a row, `hiddenTools` (`:934`), exactly **5**:
`ask_user`, `exit_plan_mode`, `update_todo`, `task_complete`, `report_intent`.

Slash-command pattern (`:219`): `#/^/([A-Za-z][\w-]*)(?:\s+([\s\S]*))?$/#`.
Todo-line pattern (`:1046`): `#/^(?:[-*+]|\d+[.)])\s*\[(.?)\]\s*(.+)$/#`; `x` → done, `" "` or `""` →
pending, anything else → active.

### 3.6 The Copilot model filter rule (the critic's first omission)

**SOURCE FACT** (`listModels`, `:402-424`), verbatim:

```swift
let result = try await connection.request("models.list", [:])
var models = [ProviderRegistry.copilotAuto]
for model in result["models"]?.array ?? [] {
    guard let id = model["id"]?.string, model["policy"]?["state"]?.string != "disabled" else { continue }
    var details: [String] = []
    if let multiplier = model["billing"]?["multiplier"]?.double {
        details.append(multiplier == multiplier.rounded() ? "\(Int(multiplier))× premium requests" : "\(multiplier)× premium requests")
    }
    if model["policy"]?["state"]?.string == "unconfigured" {
        details.append("Enable it in your GitHub Copilot settings first")
    }
    models.append(ModelOption(
        id: id,
        name: model["name"]?.string ?? id,
        detail: details.isEmpty ? nil : details.joined(separator: " · "),
        efforts: (model["supportedReasoningEfforts"]?.array ?? []).compactMap(\.string),
        defaultEffort: model["defaultReasoningEffort"]?.string))
}
return models
```

The rule, stated precisely:

1. Request params are the empty object `[:]` — `models.list` takes no arguments.
2. The list is **seeded** with exactly one synthetic entry, `ProviderRegistry.copilotAuto`
   (`ProviderRegistry.swift:146`), verbatim:
   ```swift
   static let copilotAuto = ModelOption(id: "auto", name: "Auto", detail: "Copilot picks the model for each request", isDefault: true)
   ```
   id `auto`, name `Auto`, detail `Copilot picks the model for each request`, `isDefault: true`, no efforts,
   no defaultEffort. It is prepended, so it is always index 0 and survives even when every server model is
   filtered out.
3. **The filter:** an entry is skipped (`continue`) when `model["id"]` is not a string, **or** when
   `model["policy"]["state"]` equals the literal string `disabled`. Both conditions live in one `guard`.
   No other state is filtered — in particular `unconfigured` entries are **kept** and only annotated.
4. `unconfigured` appends the detail string `Enable it in your GitHub Copilot settings first` — exact casing,
   no trailing period.
5. The multiplier detail is `"\(Int(multiplier))× premium requests"` when `multiplier == multiplier.rounded()`,
   otherwise `"\(multiplier)× premium requests"`. The separator is `×` (U+00D7 MULTIPLICATION SIGN), not the
   letter x. Details are joined with `" · "` — space, U+00B7 MIDDLE DOT, space.
6. `policy.state` is read **twice** through two separate subscript chains, once in the guard and once for the
   `unconfigured` test. **Upstream inefficiency, recorded, not to be "fixed".**
7. There is no `hidden` filter (unlike Codex's `model/list`) and no per-model `isDefault` from the server, so
   `copilotAuto` is the only entry with `isDefault: true`.

`ProviderRegistry.swift:190` and `:463` show the related fallback: when the catalog is empty,
`catalogs[.copilot] = [Self.copilotAuto]`, and a catalog equal to `[Self.copilotAuto]` counts as "not yet
loaded". **SOURCE FACT**, outside this pass's files but load-bearing for the filter's meaning.

### 3.7 The full `account.getQuota` percent/skip algorithm (the critic's second omission)

**SOURCE FACT** (`readPlanLimits`, `:444-470`), verbatim:

```swift
let quota = try await connection.request("account.getQuota", [:])
let titles = [("premium_interactions", "Premium requests"), ("chat", "Chat messages"), ("completions", "Code completions")]
var windows: [PlanLimits.Window] = []
for (key, title) in titles {
    guard let snapshot = quota["quotaSnapshots"]?[key], !snapshot.isNull,
          snapshot["isUnlimitedEntitlement"]?.bool != true,
          let entitlement = snapshot["entitlementRequests"]?.double, entitlement > 0 else { continue }
    let remaining = snapshot["remainingPercentage"]?.double
    let used = snapshot["usedRequests"]?.double ?? 0
    let percent = remaining.map { max(0, min(100, 100 - $0)) } ?? min(100, used / entitlement * 100)
    windows.append(PlanLimits.Window(
        id: key,
        title: title,
        percent: percent,
        resetsAt: snapshot["resetDate"]?.string.flatMap(parseDate)))
}
guard !windows.isEmpty else { return nil }
let auth = try? await connection.request("account.getCurrentAuth", [:])
let plan = auth?["authInfo"]?["copilotUser"]?["copilot_plan"]?.string
return PlanLimits(planName: PlanLimitsReader.planName(plan).map { "Copilot \($0)" }, windows: windows)
```

**The table.** Exactly **3** entries, iterated in this fixed order; the iteration order *is* the window order
in the UI:

| # | snapshot key | window title (verbatim) | window id |
| --- | --- | --- | --- |
| 1 | `premium_interactions` | `Premium requests` | `premium_interactions` |
| 2 | `chat` | `Chat messages` | `chat` |
| 3 | `completions` | `Code completions` | `completions` |

The window `id` is the **snapshot key**, not the title. **SOURCE FACT.**

**The skip conditions.** A snapshot is skipped (`continue`, no window produced) when **any** of these holds,
evaluated left to right in one `guard`:

1. `quota["quotaSnapshots"]?[key]` is absent (the optional chain yields nil); **or**
2. the snapshot is JSON `null` (`!snapshot.isNull` fails); **or**
3. `snapshot["isUnlimitedEntitlement"]?.bool == true`. Note the test is written `!= true`, so a **missing** or
   **non-boolean** `isUnlimitedEntitlement` does **not** skip — only an explicit `true` does; **or**
4. `snapshot["entitlementRequests"]?.double` is absent, **or** it is present and `<= 0`. Zero and negative
   entitlements are skipped.

**The percent computation.** Two branches, selected by whether `remainingPercentage` parses as a `Double`:

- **Branch A — `remainingPercentage` present:**
  `percent = max(0, min(100, 100 - remainingPercentage))`
  Clamped at **both** ends into [0, 100]. `remainingPercentage` is a *remaining* figure, so the displayed
  percent is its complement.
- **Branch B — `remainingPercentage` absent:**
  `used = snapshot["usedRequests"]?.double ?? 0`
  `percent = min(100, used / entitlement * 100)`
  Clamped only at the **top**. Evaluation order is `(used / entitlement) * 100` (Swift left-to-right for
  equal-precedence `/` and `*`).

The `entitlement > 0` guard is what makes branch B's division safe.

**Reset time.** `resetsAt: snapshot["resetDate"]?.string.flatMap(parseDate)`. `parseDate` (`:472-477`):

```swift
private static func parseDate(_ text: String) -> Date? {
    let formatter = ISO8601DateFormatter()
    if let date = formatter.date(from: text) { return date }
    formatter.formatOptions = [.withFullDate]
    return formatter.date(from: text)
}
```
Full ISO 8601 first; on failure, retry with `formatOptions = [.withFullDate]` (a bare `YYYY-MM-DD`).

**The empty case.** `guard !windows.isEmpty else { return nil }` — the function returns `nil`, **not** a
`PlanLimits` carrying a `problem` string. Codex's `readPlanLimits` returns
`PlanLimits(..., problem: "Codex reported no usage windows for this account.")` in the same situation.
**SOURCE FACT**, an asymmetry to reproduce, not to harmonize.

**The plan name.** Fetched only *after* at least one window exists, with `try?` so a failure is silent:
`account.getCurrentAuth` with params `[:]`, field path `authInfo.copilotUser.copilot_plan`, mapped through
`PlanLimitsReader.planName(_:)` and prefixed `"Copilot \($0)"` — the literal prefix is `Copilot ` with a
trailing space. A nil result from `planName(plan)` leaves the whole `planName` nil, because the `.map` does
not fire.

**Upstream defect, recorded.** Branch B is not clamped at the bottom: a negative `usedRequests` yields a
negative percent, which branch A could never produce.

**Upstream behaviour, recorded.** `readPlanLimits` spawns one `copilot --headless --stdio` process and makes
**two** requests on it (`account.getQuota`, then `account.getCurrentAuth`), whereas `listModels` and
`authStatus` each spawn their own.

### 3.8 `account.getCurrentAuth` for auth status

**SOURCE FACT** (`:428-441`):
- Returns `.unknown` when `connect` throws, and `.unknown` when the request throws.
- `guard let auth = result["authInfo"], !auth.isNull else { return .signedOut }`
- `var account = auth["login"]?.string`
- `if auth["copilotUser"]?["access_type_sku"]?.string == "no_access" { account = (account ?? "GitHub") + " · no Copilot access" }`
  — the literal suffix is ` · no Copilot access` (space, U+00B7, space, then the text); the fallback login
  text is `GitHub`. Comment: signed in to GitHub without a Copilot plan; the turn would fail with HTTP 403.
- `return .signedIn(account)`

Error augmentation (`errorMessage`, `:718-732`): after the marker `GenericFailure, ` the remainder is parsed
as JSON and rewritten as `"HTTP \(status): \(body.trimmed)"`. A `statusCode == 403`, a
`remediation == "sign_in"`, or a message containing `not authorized to use this Copilot feature` appends
` Check that your GitHub account has Copilot access, or sign in again with \`copilot login\`.`

### 3.9 Resume / session arguments

**SOURCE FACT.** There are no resume *arguments*; the resume is the `session.resume` method plus `sessionId`.
Sessions live in `~/.copilot/session-state` (header `:7-8`) under Droppy Code's own UUID. `start()` returns
`id`. Comment `:104`: resume fails for an id the CLI no longer has, and the thread then starts a fresh session.

---

## 4. ACP agents — Cursor, OpenCode, Grok, Devin (`ACPSession.swift`)

### 4.1 Launch argument vectors, per provider

**SOURCE FACT** (`launchArguments`, `:78-102`). Four provider cases plus a default, reproduced verbatim
including token order. Grok and Devin place their flags *before* the subcommand, and Grok's `.fullAccess` row
puts `--always-approve` *between* `agent` and `stdio`:

**`.cursor`** (switch on `configuration.runtimeMode`):

| RuntimeMode | argv |
| --- | --- |
| `.auto` | `--auto-review`, `acp` |
| `.fullAccess` | `--force`, `acp` |
| default (`.supervised`, `.autoAcceptEdits`) | `acp` |

**`.grok`**:

| RuntimeMode | argv |
| --- | --- |
| `.supervised` | `--permission-mode`, `default`, `agent`, `stdio` |
| `.autoAcceptEdits` | `--permission-mode`, `acceptEdits`, `agent`, `stdio` |
| `.auto` | `--permission-mode`, `auto`, `agent`, `stdio` |
| `.fullAccess` | `agent`, `--always-approve`, `stdio` |

**`.devin`** (comment `:91-93`: auto approves read-only tools, accept-edits adds workspace edits, smart adds
model-judged-safe actions, dangerous approves everything; "Passed before the subcommand, like its CLI expects"):

| RuntimeMode | argv |
| --- | --- |
| `.supervised` | `--permission-mode`, `auto`, `acp` |
| `.autoAcceptEdits` | `--permission-mode`, `accept-edits`, `acp` |
| `.auto` | `--permission-mode`, `smart`, `acp` |
| `.fullAccess` | `--permission-mode`, `dangerous`, `acp` |

**default** (covers OpenCode and any other ACP provider): `acp`

**SOURCE FACT**, spelling asymmetry to reproduce: Grok's accept-edits value is camelCase `acceptEdits`;
Devin's is hyphenated `accept-edits`.

These arguments read `configuration.runtimeMode`, the **launch-time** mode. A later mode change does **not**
relaunch the process; it is applied through `session/set_mode` or `session/set_config_option` instead.

### 4.2 Launch gate

**SOURCE FACT** (`:7-21`, `:26`, `:105`). `private static let launchGate = ACPLaunchGate(spacing: 1.5)` — an
actor that spaces process starts **1.5 seconds** apart. `start()` calls `await Self.launchGate.wait()` before
spawning. Rationale in the header comment: an agent keeps a store of its own on the machine (OpenCode's is
SQLite) and several starting in the same instant, as a team of heads does, trip over its lock:
"database is locked", and a head's first turn is over before it began. A lone start never waits; a batch
starts one every `spacing` seconds.

### 4.3 Environment

**SOURCE FACT.** None. `configuration.environment` passes through unmodified.

### 4.4 Handshake — `initialize`

**SOURCE FACT** (`:124-135`). Framing: `JSONRPCConnection(process: process, sendsVersion: true)`.

```swift
let initialized = try await connection.request("initialize", [
    "protocolVersion": 1,
    "clientCapabilities": clientCapabilities,
    "clientInfo": ["name": "droppy-code", "title": "Droppy Code", "version": .string(AppInfo.version)],
])
```

`protocolVersion` is the integer literal **1**. `clientInfo` is identical in shape and literals to Codex's:
`name` = `droppy-code`, `title` = `Droppy Code`, `version` = `AppInfo.version`.

`clientCapabilities` (`:156-166`):
```swift
var capabilities: [String: JSONValue] = [
    "fs": ["readTextFile": false, "writeTextFile": false],
    "terminal": false,
]
if usesParameterizedModels { capabilities["_meta"] = ["parameterizedModelPicker": true] }
```
`usesParameterizedModels` is `configuration.provider == .cursor` (`:63`) — **only Cursor** gets
`_meta.parameterizedModelPicker = true`. Comment `:154-155`: Cursor hides effort and Fast behind this
undocumented `_meta` flag; without it the agent only returns one baked-in variant per model and rejects any
other effort.

Fields read from the reply:
- `agentCapabilities.loadSession` (bool) → `canLoadSessions`, default `false`
- `agentCapabilities.mcpCapabilities.http` (bool) → `supportsHTTPMCP`, default `false`
- `authMethods` (array) → `authMethods = compactMap { $0["id"]?.string }`
- `_meta.modelState` → `applyModels(state)` when present

### 4.5 Authentication retry

**SOURCE FACT** (`:136-140`):

```swift
do { session = try await openSession(connection) }
catch let error as RPCError where error.message.localizedCaseInsensitiveContains("auth") && !authMethods.isEmpty {
    _ = try await connection.request("authenticate", ["methodId": .string(authMethods[0])])
    session = try await openSession(connection)
}
```
The catch fires on a case-insensitive substring match of the literal `auth` in the RPC error message, and only
when `authMethods` is non-empty. It always picks `authMethods[0]` — **the first advertised method, never a
choice**. Method `authenticate`, single field `methodId`. **One retry only.**

### 4.6 Session open — new vs load

**SOURCE FACT** (`openSession`, `:251-259`):

```swift
let base: [String: JSONValue] = ["cwd": .string(workingDirectory),
                                 "mcpServers": MCPProviderConfig.acpServers(allowsHTTP: supportsHTTPMCP)]
```

- Resume path, only when `configuration.resumeID != nil` **and** `canLoadSessions`:
  `params["sessionId"] = .string(resumeID)`, `isReplaying = true`, `try? await connection.request("session/load", .object(params))`,
  `isReplaying = false`. The `try?` means a failed load falls through silently to a new session.
  While `isReplaying`, `handleNotification` drops every `session/update`, so the replay is not re-rendered.
- New path: `connection.request("session/new", .object(base))`, `sessionID = result["sessionId"]?.string`.

`MCPProviderConfig.acpServers(allowsHTTP:)` (`../MCP/MCPProviderConfig.swift:77+`) returns `.array([])` when
`MCPPaths.claudeConfigURL` has no non-empty `mcpServers`; ids are emitted **sorted**; HTTP entries go through
directly when `allowsHTTP`, otherwise via `mcp-remote` (header comment `:242-243`).

Failure after open: `ProviderError.failed("\(configuration.provider.displayName) did not start a session.")`.

### 4.7 Method names, both directions

**SOURCE FACT.** App → agent requests, **8**:
`initialize`, `authenticate`, `session/load`, `session/new`, `session/prompt`, `session/set_model`,
`session/set_mode`, `session/set_config_option`.
App → agent notification, **1**: `session/cancel` with `["sessionId": …]`.

`session/prompt` params: `["sessionId": …, "prompt": .array(prompt)]`, where the prompt parts are
`["type":"text","text":…]` first, then per image `["type":"image","mimeType":…,"data":<base64>]`. It is fired
in a detached `Task`, and its result's `stopReason` ends the turn.

`session/set_model` params: `["sessionId": …, "modelId": .string(wireID)]`.
`session/set_mode` params: `["sessionId": …, "modeId": .string(mode)]`.
`session/set_config_option` params: `["sessionId": …, "configId": …, "value": .string(value)]`; the reply's
`configOptions` array is fed back into `applyConfigOptions`.

Agent → app: exactly **one** notification method, `session/update`, dispatched on `update["sessionUpdate"]`.
The **10** handled values:
`agent_message_chunk`, `agent_thought_chunk`, `tool_call`, `tool_call_update`, `plan`,
`available_commands_update`, `current_mode_update`, `config_option_update`, `session_info_update`,
`usage_update`.

Agent → app: exactly **one** request method, `session/request_permission`; anything else is answered
`errorCode: -32601, message: "Method not found"`.

Permission answers:
- selected: `["outcome": ["outcome": "selected", "optionId": .string(optionID)]]`
- cancelled: `["outcome": ["outcome": "cancelled"]]`

Option kinds read: `allow_always` → `.approveAlways`; `reject_once` and `reject_always` → `.decline`;
everything else → `.approve`. Automatic selection prefers the option whose `kind` is `allow_once`, falling
back to `allow_always`, and only fires for `.fullAccess` (all kinds) or `.autoAcceptEdits` (`kind == .edit`).

`stopReason` values compared: `cancelled` → interrupted; `refusal` → failed with
`The agent declined this request.`; anything else → completed.

`compact()` always throws `ProviderError.failed("\(configuration.provider.displayName) compacts its own context.")`.
`answerQuestion` is an empty method — ACP has no question channel.

### 4.8 Mode resolution

**SOURCE FACT** (`resolveModes`, `:300-304`):
```swift
planMode = ids.first { $0 == "plan" }
buildMode = ids.first { ["agent", "build", "code", "default"].contains($0) }
    ?? ids.first { $0 != "plan" && $0 != "ask" }
```
The build-mode membership set has exactly **4** ids: `agent`, `build`, `code`, `default`. (Preference is by
the agent's own list order, since `ids.first` walks that order.) The fallback excludes exactly **2** ids:
`plan` and `ask`.

Config-option categories handled (`applyConfigOptions`, `:326-393`), keyed by
`option["category"]?.string ?? id`: `model`, `mode`, `thought_level` and `effort` (one label), `model_config`
(only when `id == "fast"`). Comment `:365-366`: Cursor also advertises a boolean `thinking` thought_level;
only the scale drives the effort slider.

### 4.9 Effort scale recognition and ordering

**SOURCE FACT** (`isEffortScale`, `:408-411`): the known set has exactly **7** members:
`none`, `low`, `medium`, `high`, `xhigh`, `max`, `extra-high`. A config option qualifies as an effort scale
when **any** of its values (lowercased) is in that set.

**SOURCE FACT** (`orderedEfforts`, `:417-431`): the rank table has exactly **11** entries:
`none`:0, `minimal`:1, `low`:2, `medium`:3, `high`:4, `xhigh`:5, `extra-high`:5, `extra_high`:5,
`extra high`:5, `max`:6, `ultra`:6.
Guard: `ranked.count(where: { $0.rank != nil }) > 1` — with 0 or 1 recognized values the input order is
returned unchanged. The sort falls back to the original offset for equal or unknown ranks, so an unknown level
keeps its place among its neighbours. Reason (`:412-415`): Grok's ACP advertises its levels from extra high
down to low and the slider reads left to right as low to high.

### 4.10 Catalog probe

**SOURCE FACT** (`probeModels`, `:228-241`): builds a throwaway `ACPSession` with
`runtimeMode: .supervised, interactionMode: .build` in
`FileManager.default.temporaryDirectory.appendingPathComponent("droppy-code-catalog", isDirectory: true)`
— the literal folder name is `droppy-code-catalog`. It calls `start()`, then `probeModelEfforts()`, then
`stop()` via `defer`.

`probeModelEfforts` (`:288-298`): walks `models where model.efforts.isEmpty`, making each current via
`setConfigOption(modelConfigID, value: model.id)`, then restores the original model if it differs
(`!Self.sameModel(original, currentModel)`). It returns immediately when `modelConfigID` is nil. Rationale
(`:281-287`): Cursor and OpenCode report the effort scale of the current model only, and models that arrive
with a scale (Grok lists them in `_meta.reasoningEfforts`) are skipped by the `where` filter.

### 4.11 Cursor parameterized ids

**SOURCE FACT** (`baseModelID`, `:435-438`): Cursor's ids are `model[effort=high,…]`; the base is everything
before the first `[`. Applied only when `usesParameterizedModels`. `sameModel` compares base ids and returns
`false` if either side is nil.

### 4.12 Tool-title cleanup tables

**SOURCE FACT**, all four counted element by element from the source.

`subjectPhrases` (`:778-782`) — **16** entries, longest phrases first so "searched the web for" wins over
"searched for", each carrying its trailing space and matched as-is against the lowercased title:
`searched web for `, `search web for `, `searching web for `, `searched the web for `, `search the web for `,
`searched for `, `searching for `, `search for `, `reading file `, `read file `, `listed directory `,
`listing directory `, `list directory `, `listed files in `, `list files in `, `listing files in `

`subjectVerbs` (`:783-789`) — **48** entries:
`read`, `reading`, `viewed`, `viewing`, `view`, `fetched`, `fetching`, `fetch`, `browsed`, `browsing`,
`searched`, `searching`, `search`, `grep`, `glob`, `listed`, `listing`, `list`, `wrote`, `writing`,
`write`, `edited`, `editing`, `edit`, `modified`, `modifying`, `modify`, `updated`, `updating`, `update`,
`created`, `creating`, `create`, `deleted`, `deleting`, `delete`, `ran`, `running`, `run`, `executed`,
`executing`, `execute`, `called`, `calling`, `call`, `opened`, `opening`, `open`

Phrase and verb stripping only applies when the row's kind is one of `.read`, `.edit`, `.search`, `.web`
(`rowHasVerb`), and a verb is only dropped when something remains after it.

`isBareToolName`'s set (`:816-821`) — **35** entries:
`read`, `write`, `edit`, `multiedit`, `patch`, `bash`, `shell`, `list`, `ls`, `glob`, `grep`, `search`,
`webfetch`, `websearch`, `fetch`, `todowrite`, `todoread`, `task`, `tool`, `apply_patch`, `read_file`,
`write_file`, `edit_file`, `list_files`, `search_files`, `run_command`, `run_terminal_cmd`, `web_search`,
`web_fetch`, `codebase_search`, `file_search`, `grep_search`, `list_dir`, `view_file`, `replace_file_content`

`isFallbackTitle`'s literal list (`:843-844`) — **10** entries, falling back to `isBareToolName` otherwise:
`run command`, `read file`, `edit file`, `write file`, `search`, `fetch`, `tool`, `agent`, `file`, `files`

`pathKeys` (`:766`) — **7** entries, in this order:
`filePath`, `file_path`, `path`, `file`, `notebook_path`, `target_file`, `targetFile`

Quote stripping (`:797-800`) uses exactly **4** delimiter pairs: `'`/`'`, `"`/`"`, `“`/`”`, `` ` ``/`` ` ``.

ACP tool `kind` mapping (`:857-866`), **6** named cases plus default:
`read` → `.read`; `edit`, `delete`, `move` → `.edit`; `search` → `.search`; `execute` → `.command`;
`fetch` → `.web`; anything else → `.other`.

`fallbackTitle` (`:868-879`), the full 8-row table: `.command` → `Run command`, `.read` → `Read file`,
`.edit` → `Edit file`, `.search` → `Search`, `.web` → `Fetch`, `.mcp` → `Tool`, `.agent` → `Agent`,
`.other` → `Tool`.

`status` (`:882-888`): `completed` → `.completed`, `failed` → `.failed`, anything else → `.running`.

Todo detection (`isTodoTitle`, `:645-659`): the exact-match set has **5** members — `todo`, `todos`,
`todowrite`, `todo write`, `write todos` — plus the numeric-prefix rule for the two suffixes ` todos` and
` todo` (e.g. OpenCode's `3 todos`).

`parseTodoItems` (`:686-699`): text keys tried in order `content`, `text`, `step`, `title`; status
`completed` / `complete` / `done` → `.done`, `in_progress` / `inProgress` / `in-progress` / `active` →
`.active`, anything else → `.pending` (comment `:684-685`: unknown statuses stay pending so a new provider
state never drops an item).

---

## 5. Command Code (`CommandCodeSession.swift`)

### 5.1 Executable and per-turn argument vector

**SOURCE FACT.** Executable named in the header doc (`:3`, `:20`): `cmd`; the verified build is
`command-code 1.53.1` (`:19`). There is **no long-lived process**: `start()` (`:98`) spawns nothing, and each
`send()` spawns one `cmd -p` process (header `:5-7`).

Base array (`:144-151`), **10 tokens** in this order:

```
-p
--output-format
json
--skip-onboarding
--no-auto-update
--trust
--tools-enable
todo_write
--max-turns
<String(isFinalReport ? 1 : Self.maxTurns)>
```

`Self.maxTurns` is the literal `400` (`:79`). Comment `:75-76`: "The CLI's own default is 100, short for a
long agentic job." So the value is `1` for a final report and `400` otherwise.

Conditional arguments, appended in exactly this order:

1. `if let model, !model.isEmpty` → `["--model", model]`
2. `if let effort, !effort.isEmpty, Self.takesEffort(effort, model: model)` → `["--effort", effort]`
3. mutually exclusive:
   - `if interactionMode == .plan` → `["--permission-mode", "plan"]`
   - else → `["--yolo"]` (comment: print mode withholds writes and commands unless told otherwise; the mod
     below is what asks the user)
4. `--mod <modURL.path>` when **all** of: `modURL != nil` **and**
   (`runtimeMode != .fullAccess` **or** `interactionMode == .plan` **or**
   `FileManager.default.fileExists(atPath: MCPPaths.claudeConfigURL.path)`).
   Full access in build mode with no connected MCP therefore loads **no gate at all**.
5. `if let sessionID, !sessionID.isEmpty` → `["--resume", sessionID]`

`takesEffort` (`:213-216`): `guard let model else { return false }`, then
`CommandCodeAPI.efforts(for: model).contains(effort)`. Comment `:209-212` records the upstream failure this
avoids: the CLI refuses a run outright for a model with no adjustable effort, with
`"LongCat 2.0 has no adjustable reasoning effort"` and exit 1.

### 5.2 Environment variables set

**SOURCE FACT** (`:167-177`), exactly **3** keys, all prefixed `DROPPY_CODE_`:

| Key | Value | Condition |
| --- | --- | --- |
| `DROPPY_CODE_APPROVALS` | `approvalDirectory.path` | `runtimeMode != .fullAccess` **and** `approvalDirectory != nil`; **explicitly set to nil** otherwise |
| `DROPPY_CODE_MCP_BRIDGE` | `bridge.path` | `try? MCPBridge.installBridge()` succeeded |
| `DROPPY_CODE_MCP_CONFIG` | `MCPPaths.claudeConfigURL.path` | that file exists |

The explicit `environment["DROPPY_CODE_APPROVALS"] = nil` in the else branch clears an inherited value rather
than leaving it standing. **SOURCE FACT.**

### 5.3 Handshake

**SOURCE FACT.** There is none. The protocol is one-shot newline-delimited JSON per run (header `:5-7`).
`start()` (`:98-110`) only: checks the transcript exists, installs the mod (`CommandCodeAPI.installMod()`),
creates the approval directory `CommandCodeAPI.supportDirectory/approvals/<UUID>`, starts the FS watcher, sets
`started = true`, and returns `sessionID ?? ""`.

The prompt is written to stdin and the stream closed:
```swift
process.write(Data(Self.prompt(for: input).utf8))
process.closeInput()
```
`prompt(for:)` (`:210-218`): the user's text, plus, when attachments exist on disk,
`"\n\nAttached files (read them with read_file):\n"` followed by `- <path>` lines joined with `\n`.
Exact strings, including the parenthetical and the `- ` bullet.

### 5.4 Session id and resume

**SOURCE FACT.** `sessionID` comes from `configuration.resumeID` in `init` (`:86`), nil when empty.
`start()` throws `ProviderError.failed("Command Code no longer has this conversation; starting a new one.")`
when `transcriptExists` is false.

`transcriptExists` (`:118-124`): home = `environment["HOME"]` (non-empty) else `LoginEnvironment.homeDirectory`;
probes `<home>/.commandcode/projects/<any folder>/<sessionID>.jsonl`. The literal path segment is
`.commandcode/projects`, the extension `.jsonl`.

The id is then updated from the stream: the `run_start` event's `sessionId` and the `result` line's
`sessionId`, each emitting `.sessionReady(sessionID:)` when it differs from the current one.

### 5.5 Frame and event names

**SOURCE FACT.** Top-level `type` values: `event` and `result`. The result line's fields: `subtype` (default
`"success"`), `stopReason`, `error`, `sessionId`.

`event.type` handled — **23 distinct names across 22 `case` labels** (`tool_hook_blocked` and `tool_denied`
share one label):
`run_start`, `message_start`, `text_delta`, `thinking_start`, `thinking_delta`, `thinking_end`,
`message_end`, `model_request_start`, `model_request_end`, `tool_queued`, `tool_update`, `tool_completed`,
`tool_errored`, `tool_hook_blocked`, `tool_denied`, `subagent_progress`, `session_titled`,
`compaction_start`, `compaction_done`, `notice`, `run_error`, `interrupted`, `run_end`.

The `default` comment (`:421-423`) names the **9** frames deliberately ignored, verbatim:
`turn_start`, `turn_end`, `message_update`, `model_trace`, `tool_running`, `tool_hooks`,
`tool_input_repaired`, `subagent_start`, `subagent_stop`.

Id namespacing (`:41`, `:293`, `:302`, `:311`): `launchID = String(UUID().uuidString.prefix(8))`; message ids
are `cc-<launchID>-m<n>`, thinking ids `cc-<launchID>-r<n>`, plan ids `cc-<launchID>-plan<turnCounter>`.

### 5.6 The approval gate protocol (file-based, not RPC)

**SOURCE FACT.** Directory: `CommandCodeAPI.supportDirectory / "approvals" / <UUID>`, created in `start()`,
watched with a `DispatchSource.makeFileSystemObjectSource(..., eventMask: [.write], queue: .main)`.

Request files: any entry whose name ends with the literal `.request.json`, processed in **lexicographic order
of `lastPathComponent`** and **deleted immediately after reading**. Id = `request["toolCallId"]?.string`, else
the file name minus `.request.json`. Fields read: `toolCallId`, `toolName`, `input`.

Reply files: written to `<id>.reply.json` via a `<id>.reply.json.tmp` plus `replaceItemAt`, falling back to a
direct atomic write. Payloads, verbatim:
- allow: `{"decision":"allow"}`
- deny: `{"decision":"deny","reason":"The user declined this in Droppy Code. Do not retry it; take another approach or explain what you would need."}`

Auto-approval table (`autoApproves`, `:697-729`), where `isEdit = (name == "write_file" || name == "edit_file")`:

| RuntimeMode | Rule |
| --- | --- |
| `.fullAccess` | `true` (comment: never reaches here, it loads no gate) |
| `.supervised` | `false` |
| `.autoAcceptEdits` | `isEdit` |
| `.auto` | `isEdit` → true; else `shell_command` / `powershell` / `monitor_command` → `!ShellCommandKind.mayWriteFiles(input["command"]?.string ?? "")`; else `kill_shell` / `cron_create` / `cron_delete` / `task_stop` → true; else false |

The `.auto` tool allow-list has exactly **4** names: `kill_shell`, `cron_create`, `cron_delete`, `task_stop`.
The shell-name list has exactly **3**: `shell_command`, `powershell`, `monitor_command`.

Approval options offered (`makeApproval`), always exactly **3**, in this order:
`allow` / `Approve` / `.approve`, `always` / `Approve for session` / `.approveAlways`,
`deny` / `Decline` / `.decline`.

Session grant keys (`grantKey`): `.command` → `command`, `.fileChange` → `fileChange`,
`.tool`/`.permissions`/`.plan` → `tool`.

### 5.7 Exit-code table

**SOURCE FACT** (`didClose`, `:434-496`), applied only when **no** result line arrived. Nine explicit codes
plus a default; messages verbatim, including backticks and the `→` arrows:

| Status | Outcome |
| --- | --- |
| `interruptRequested` or `130` | `.turnCompleted(status: .interrupted, error: nil)` (tested before the result line) |
| `0` | completed, no error |
| `3` | failed, `Command Code is not signed in. Run \`cmd login\` in Terminal, or add a Studio API key in Settings → Providers → Command Code.` |
| `4` | failed, `tail ?? "Command Code was denied permission."` |
| `5` | `.usageLimit(resetsAt: nil)` then failed, `tail ?? "Command Code is rate limited. Wait for the window to reset, or buy on-demand credits, which are never throttled."` |
| `6` | failed, `Could not reach Command Code. Check the connection and try again.` |
| `7` | failed, `tail ?? "Command Code's API returned a server error. Try again in a moment."` |
| `8` | `noteTurnCap()` then **completed** |
| `9` | failed, `The model produced no response.` |
| `10` | failed, `Command Code is out of credits. Top up at commandcode.ai/billing, or wait for the monthly credits to reset.` |
| default | failed, `runError ?? tail ?? "Command Code exited with code \(status)."` |

When a result line **did** arrive: `subtype == "success"` → completed (with `noteTurnCap()` when
`stopReason == "max_turns"`); `subtype == "max_turns"` → `noteTurnCap()` then **completed**; anything else →
failed, with `Self.isRateLimit(error)` first emitting `.usageLimit(resetsAt: nil)`.

`noteTurnCap()` (`:499-502`) is suppressed when `isFinalReport`; its notice is
`Command Code stopped at its cap of 400 round trips for one message. Send another to carry on.`
(the number is interpolated from `Self.maxTurns`).

`isRateLimit` (`:504-507`) matches the lowercased text against exactly **3** substrings:
`rate limit`, `usage exceeded`, `window limit`.

`friendlyError` (`:510-519`) rewrites two families: `not authenticated` / `unauthorized` / `session expired`
→ the sign-in message; `insufficient credits` / `out of credits` → the credits message.

---

## 6. Pi (`PiSession.swift` + `PiCLI.swift`)

### 6.1 Executable and session argument vector

**SOURCE FACT.** Executable: `pi`, the npm package `@earendil-works/pi-coding-agent`
(`PiSession.swift:3`, `PiCLI.swift:3-4`).

`start()` (`PiSession.swift:80-155`). Base (`:92-93`):

```swift
var arguments = ["--mode", "rpc", "-e", extensionURL.path]
arguments.append(runtimeMode == .auto || runtimeMode == .fullAccess ? "--approve" : "--no-approve")
```

Tokens 1-4 are `--mode`, `rpc`, `-e`, `<extension path>`; token 5 is **always present**: `--approve` for
`.auto` **or** `.fullAccess`, `--no-approve` for `.supervised` and `.autoAcceptEdits`.
`extensionURL` comes from `PiCLI.installExtension()` (`PiCLI.swift:117-126`), which writes `droppy-gate.ts`
into `PiCLI.supportDirectory` and returns its path, rewriting only when the text changed.

Conditional arguments, in this order:

1. `:94-96` — `if let model, !model.isEmpty, model != "default", let slash = model.firstIndex(of: "/")` →
   `["--provider", String(model[..<slash]), "--model", String(model[model.index(after: slash)...])]`
   — **four tokens**. The catalog id form is `provider/modelId`, split on the **first** `/`. A model id
   with no `/` adds neither flag, and the literal `default` is excluded.
2. `:97-99` — `if let effort, !effort.isEmpty, PiCLI.thinkingLevels.contains(effort)` → `["--thinking", effort]`
3. `:100` — `if let sessionID` → `["--session", sessionID]` (the session id is a **file path**, see 6.4)

`PiCLI.thinkingLevels` (`PiCLI.swift:11`), exactly **7** members in this order:
`off`, `minimal`, `low`, `medium`, `high`, `xhigh`, `max`.
Comment `PiCLI.swift:10`: "Pi's `--thinking` levels, which Droppy's effort strings map to 1:1."

Framing: `StdioProcess(..., framing: .lines)` — strict LF-delimited JSONL, one JSON object per line
(header `:5-6`).

### 6.2 Environment variables set

**SOURCE FACT** (`:102-110`), exactly **4** keys:

| Key | Value | Condition |
| --- | --- | --- |
| `DROPPY_CODE_PI_GATE` | `gateURL.path` | always |
| `PI_SKIP_VERSION_CHECK` | `1` | always |
| `DROPPY_CODE_MCP_BRIDGE` | `bridge.path` | `try? MCPBridge.installBridge()` succeeded |
| `DROPPY_CODE_MCP_CONFIG` | `MCPPaths.claudeConfigURL.path` | that file exists |

`PiCLI.listModels` sets only `PI_SKIP_VERSION_CHECK = "1"` (`PiCLI.swift:50`).

### 6.3 Handshake — `get_state`, then `get_commands`

**SOURCE FACT** (`:135-152`). There is no `initialize`. After the process starts and the reader attaches:

```swift
let state = await request(["type": .string("get_state")])
let data = state["data"] ?? state
if let file = data["sessionFile"]?.string, !file.isEmpty { self.sessionID = file }
else if let id = data["sessionId"]?.string, !id.isEmpty { self.sessionID = id }
contextWindow = data["model"]?["contextWindow"]?.int
onEvent?(.sessionReady(sessionID: self.sessionID ?? ""))

let commandsResponse = await request(["type": .string("get_commands")])
let commandsData = commandsResponse["data"] ?? commandsResponse
if let entries = commandsData["commands"]?.array { /* SlashCommand(name:detail:) from name / description */ }
started = true
```

`request(_:)` (`:259-269`) assigns `line["id"] = "droppy-\(requestCounter)"` with the counter pre-incremented
from 0, so the first id is `droppy-1`. The literal prefix is `droppy-` — **not** `droppy-code-`, unlike
Claude. It resolves on a `{"type":"response","id":…}` line, or with `.null` after **30 seconds**. The timeout
never throws.

Both responses accept either shape: the payload is read from `["data"]` when present, else from the message
itself. **SOURCE FACT.**

### 6.4 Session id and resume

**SOURCE FACT.** Pi's session id **is a filesystem path** — the session file pi reports in `get_state`
(header `:8-10`, field `sessionFile`). `init` (`:70`) takes it from `configuration.resumeID`, nil when empty.
`start()` (`:82-84`) throws `ProviderError.failed("Pi no longer has this conversation; starting a new one.")`
when `!FileManager.default.fileExists(atPath: sessionID)` — a plain file-existence check, not a directory scan
(contrast Command Code, 5.4).

Resume argument: `["--session", sessionID]`.

### 6.5 Method names, both directions

**SOURCE FACT.** App → pi lines, every one an object with a `type` field, **9 distinct**:

| `type` | Extra fields |
| --- | --- |
| `get_state` | `id` |
| `get_commands` | `id` |
| `get_available_models` | `id` (catalog probe only, `PiCLI.swift:64`) |
| `set_model` | `provider`, `modelId` |
| `set_thinking_level` | `level` |
| `prompt` | `message`, optionally `images` |
| `abort` | — |
| `compact` | — |
| `extension_ui_response` | `id`, plus one of `value` / `confirmed` / `cancelled` |

Prompt image parts: `["type":"image","data":<base64>,"mimeType":<mimeType>]`, only for `image.isImage` with a
readable file.

`extension_ui_response` payload variants:
- approval: `{"type":"extension_ui_response","id":…,"value":"<Allow|Allow for session|Deny>"}`
- cancelled question: `…,"cancelled":true`
- confirm question: `…,"confirmed":<texts.first == "Yes">`
- input / editor / select question: `…,"value":<first answer>`

The three approval strings are exactly `Allow`, `Allow for session`, `Deny`
(`PiSession.swift:216-221`, `PiCLI.swift:211`).

Pi → app `type` values handled (`:300-343`), **13**:
`response`, `message_start`, `message_update`, `message_end`, `tool_execution_start`,
`tool_execution_update`, `tool_execution_end`, `agent_end`, `compaction_start`, `compaction_end`,
`auto_retry_start`, `extension_error`, `extension_ui_request`.

`extension_ui_request` methods handled, **8 names across 6 labels**:
`select`, `confirm`, `input`, `editor`, `notify`, and the four-name no-op label
`setStatus` / `setWidget` / `setTitle` / `set_editor_text`.

Tool kinds mapped in `handleToolStart` (`:415-422`): `bash` → command; `read` → read; `edit` and `write` →
edit; `grep`, `find`, `ls` → search; `mcp__` prefix → mcp; everything else → other.

Usage field fallbacks (`:370-377`): `totalTokens`, else
`(input|inputTokens) + (output|outputTokens) + (cacheRead|cacheReadTokens) + (cacheWrite|cacheWriteTokens)`.
The comment `:366-369` records that the one-expression form made `swiftc -typecheck` give up with
"unable to type-check this expression in reasonable time", so each part is named; the arithmetic is unchanged.

### 6.6 The gate file and the `droppy-gate` select protocol

**SOURCE FACT.** Gate file (`:86-90`, `writeGate` `:279-288`): path
`PiCLI.supportDirectory / "gates" / <UUID>.json`, rewritten before **every** turn. Contents, verbatim keys:

```json
{"runtimeMode":"<RuntimeMode.rawValue>","interactionMode":"<InteractionMode.rawValue>","finalReport":<bool>,"systemPrompt":""}
```

`systemPrompt` is **always the empty string** in the shipped code — the extension's `before_agent_start` hook
reads it and appends it only when non-empty, so that branch is currently **unreachable in the shipped build**.
**SOURCE FACT, dead-but-shipped code path**, retained for parity.

The select title is the literal prefix `droppy-gate ` (with trailing space) followed by the compact JSON
payload (`PiCLI.swift:211`, matched at `PiSession.swift:551-552` with
`title.hasPrefix("droppy-gate ")` and `JSONValue.parse(String(title.dropFirst("droppy-gate ".count)))`).
Payload fields (`PiCLI.swift:210`): `kind`, `toolName`, `toolCallId`, `input`.
`kind` values: `command` (for `bash`), `fileChange` (for `edit` or `write`), `tool` (everything else).
Options array, in this order: `["Allow", "Allow for session", "Deny"]`.

A select whose title does **not** start with `droppy-gate ` is surfaced as a plain question instead.

### 6.7 The extension's own gate rules (`PiCLI.extensionSource`)

**SOURCE FACT** (`PiCLI.swift:136-216`), a TypeScript module written to disk as `droppy-gate.ts`.

Sets, with exact element counts:
- `readOnly` (`:140`) — **4** tool names: `read`, `grep`, `find`, `ls`
- `readOnlyBinaries` (`:142-145`) — **18** names, verbatim in source order:
  `ls`, `cat`, `head`, `tail`, `grep`, `rg`, `find`, `wc`, `pwd`, `echo`,
  `which`, `stat`, `file`, `du`, `tree`, `sort`, `uniq`, `diff`
- `readOnlyGit` (`:146`) — **6** subcommands: `status`, `diff`, `log`, `show`, `branch`, `blame`

`readGate()` (`:148-155`): reads `process.env.DROPPY_CODE_PI_GATE || ""` and `JSON.parse`s it; any throw
yields `{}`.

`isReadOnlyCommand` (`:157-165`): splits on the regex `/&&|\|\||;|\|/`; **every** segment must qualify; an
empty segment qualifies; a segment whose first word is `git` qualifies only when it has a second word in
`readOnlyGit`; otherwise the first word must be in `readOnlyBinaries`.

`before_agent_start` hook (`:187-195`), the three candidate strings in this order, joined with `"\n\n"` and
appended to the existing system prompt with a leading `"\n\n"`:
1. `if (gate.finalReport)` → `Reply with your final report now: no tools, one message.`
2. `if (gate.interactionMode === "plan")` → `You are in plan mode: read-only. Do not edit files or run commands that change anything; investigate, then end with the plan as markdown.`
3. `if (typeof gate.systemPrompt === "string" && gate.systemPrompt.length > 0)` → that string (unreachable, see 6.6)

If `extra.length === 0` the hook returns nothing and the prompt is untouched.

`tool_call` hook (`:197-214`), the decision ladder in exact order:
1. `gate.finalReport` → `{block: true, reason: "Final report: no tools"}`
2. `readOnly.has(event.toolName)` → return (allow)
3. `gate.interactionMode === "plan"` → `{block: true, reason: "Plan mode is read-only"}`
4. `gate.runtimeMode === "fullAccess"` → return (allow)
5. compute `kind`
6. `kind === "fileChange" && (gate.runtimeMode === "autoAcceptEdits" || gate.runtimeMode === "auto")` → allow
7. `kind === "command" && gate.runtimeMode === "auto" && isReadOnlyCommand(event.input && event.input.command)` → allow
8. else ask over `ctx.ui.select`; `Allow` or `Allow for session` → `undefined` (allow); anything else →
   `{block: true, reason: "The user declined this " + (kind === "command" ? "command" : kind === "fileChange" ? "file change" : "tool call")}`

String truncation before asking (`:206-209`): every string value in `event.input` longer than **20000**
characters is sliced to its first 20000.

MCP registration (`:169-183`): tool names are `"mcp__" + tool.server + "__" + tool.name`; labels
`tool.server + " · " + tool.name`; parameters `Type.Unsafe(tool.inputSchema)`; result
`{content: [{type: "text", text}], details: {}}`. Errors are logged as `"droppy mcp: " + message` on stderr
and swallowed.

The decline-detection in `handleToolEnd` (`:454-458`) keys on the output starting with the literal
`The user declined this` — which is exactly the prefix the extension's block reason produces. **SOURCE FACT**,
an ordering dependency between the two files.

### 6.8 The catalog probe (`PiCLI.listModels`)

**SOURCE FACT** (`PiCLI.swift:47-112`). Argv (`:53`), **8 tokens**:

```
--mode
rpc
--no-session
--no-extensions
--no-skills
--no-prompt-templates
--no-themes
--no-context-files
```

Directory: the resolved home (`environment["HOME"]` non-empty, else `LoginEnvironment.homeDirectory`).
Framing `.lines`. It sends two lines with **fixed literal ids**:
`{"type":"get_state","id":"state"}` and `{"type":"get_available_models","id":"models"}`, then reads until both
arrive or a **30-second** timeout terminates the process.

Model mapping: items come from `data.models` or `models`; each needs `provider` and `id`;
`fullID = "\(provider)/\(id)"`; `efforts` = `thinkingLevels` when `item["reasoning"]?.bool == true`, else `[]`;
`defaultEffort` = `medium` when reasoning, else nil. The current-model test handles both a slash-bearing
`currentID` (compare `fullID`) and a bare one (compare provider and id separately). An empty list throws
`ProviderError.failed("Pi listed no models.")`. Sorted by `(detail ?? "", name)` — that is, by provider then name.

`authStatus` (`PiCLI.swift:31-44`): reads `<configDir>/auth.json`, and for a non-empty object returns
`.signedIn(object.keys.sorted().joined(separator: ", "))`. The env-key fallback list has exactly **8** names,
in source order: `ANTHROPIC_API_KEY`, `OPENAI_API_KEY`, `GEMINI_API_KEY`, `OPENROUTER_API_KEY`,
`GROQ_API_KEY`, `MISTRAL_API_KEY`, `DEEPSEEK_API_KEY`, `XAI_API_KEY`. Otherwise `.signedOut`.

`configDirectory` (`PiCLI.swift:21-27`): `PI_CODING_AGENT_DIR` when set and non-empty, else `<home>/.pi/agent`.
`supportDirectory` (`:14-18`): `<Application Support>/Droppy Code/pi`.

### 6.9 Known upstream oddities

**SOURCE FACT** (`unifiedDiff`, `:481-489`): `let relative = path.hasPrefix("/") ? path : path` — both branches
are identical, so the ternary is a no-op and the path is never made relative.

**SOURCE FACT** (`request(_:)`, `:259-269`): the 30-second timeout resolves `.null` silently rather than
throwing, so a hung `get_state` leaves `start()` returning an empty session id instead of an error.

**SOURCE FACT** (`stopAgent(_:)`, `:213`): `func stopAgent(_ id: String) async -> Bool { false }` — Pi has no
per-head stop.

---

## 7. Antigravity (`AntigravitySession.swift`)

### 7.1 Executable and argument vector

**SOURCE FACT.** Executable: `agy` (header `:3`); verified live against `agy 1.2.2` (`:11-13`), for the
models TSV, the `/usage` and `/credits` headless envelopes, tool step shapes and `denied_actions`.

Base (`:76-81`), **8 tokens** in this order:

```
--input-format
stream-json
--output-format
stream-json
--print-timeout
30m
--add-dir
<workingDirectory>
```

`--print-timeout`'s value is the literal string `30m`. The `--add-dir` rationale (`:70-74`): the CLI does not
adopt its cwd (verified live, git repo or not); without the flag the agent works in
`~/.gemini/antigravity-cli/scratch` and spends its first steps hunting the home directory for the project.

Conditional arguments, in this order:

1. `:82-83` — `let connected = MCPProviderConfig.connectedIDs(); if !connected.isEmpty` →
   `arguments += ["--allowed-mcp-server-names"] + connected`
   — **one flag token followed by every id as its own token**, not a comma-joined value.
   `connectedIDs()` (`../MCP/MCPProviderConfig.swift:230-236`) returns the `mcpServers` keys **sorted**, or
   `[]` when the file is missing or the object empty, so the id order is deterministic and lexicographic.
2. `:84-86` — `let (resolvedModel, resolvedEffort) = Self.normalize(model: model, effort: effort)`, then
   `if let resolvedModel, !resolvedModel.isEmpty` → `["--model", resolvedModel]`, then
   `if let resolvedEffort, !resolvedEffort.isEmpty` → `["--effort", resolvedEffort]`
3. `:87` — `arguments += modeArguments(runtimeMode: configuration.runtimeMode, interaction: configuration.interactionMode)`
4. `:88-90` — `if let resumeID = configuration.resumeID, !resumeID.isEmpty` → `["--conversation", resumeID]`

`modeArguments` (`:123-136`), verbatim:

| Condition | Tokens appended |
| --- | --- |
| `interaction == .plan` | `--mode`, `plan` |
| else `.supervised` | *(nothing — the default review mode is the absence of the flag, comment `:114-117`)* |
| else `.autoAcceptEdits` / `.auto` / `.fullAccess` | `--mode`, `accept-edits` |
| then, **separately**, `runtimeMode == .fullAccess` | `--dangerously-skip-permissions` |

**SOURCE FACT**, an interaction to reproduce exactly: `.fullAccess` in **plan** interaction mode yields
`--mode plan --dangerously-skip-permissions`, because the `--dangerously-skip-permissions` test sits outside
the plan/else branch.

Comment `:114-115`: `agy --mode` only knows `accept-edits` and `plan`. Comment `:116-118`: Full access
additionally approves every tool, since headless runs soft-deny unapproved actions instead of prompting.

### 7.2 Environment

**SOURCE FACT.** None. `configuration.environment` passes through unmodified.

### 7.3 Handshake — the `init` event, not a request

**SOURCE FACT** (`:108-116`, `:186-194`). There is **no outbound handshake**. `start()` returns a
`withCheckedThrowingContinuation` resolved by the first inbound `{"event":"init"}` line carrying a non-empty
`conversation_id`:

```swift
case "init":
    guard let id = message["conversation_id"]?.string, !id.isEmpty else { return }
    conversationID = id
    if let continuation = initContinuation { initContinuation = nil; continuation.resume(returning: id) }
    onEvent?(.sessionReady(sessionID: id))
```

Watchdog (`:110-115`, `failStartAfterTimeout` `:141-146`): a detached `Task` sleeps **30 seconds**, then
terminates the process and resumes the continuation with, verbatim:
``ProviderError.failed("Antigravity did not answer. Is `agy` signed in? Run `agy` once in Terminal.")``
(the backticks around `agy` are part of the string).

### 7.4 Turn message

**SOURCE FACT** (`:156`), the only object the app ever writes:

```json
{"event":"user","message":{"content":"<input.text>"}}
```

Images are never sent. Comment `:150-151`: "Stream-json only takes text blocks; anything else ends the session
with an error, so images never go on the wire (see supportsImages)."

There is **no interrupt message**. `interrupt()` (`:159-171`) sets `interruptRequested = true`, sets
`isStopping = true` (so `didClose` does not follow with `.exited` and the chat does not read "Antigravity
stopped unexpectedly"), then terminates the process; the conversation persists server-side for the next turn's
`--conversation` resume. `start` clears `isStopping` again for the next launch (`:100`).

`compact()` always throws `ProviderError.failed("Antigravity manages its own context.")`.
`resolveApproval` and `answerQuestion` are both empty methods (`:176-177`) — there is no approval channel.

### 7.5 Event names

**SOURCE FACT.** Top-level `event` values handled (`:185-199`), **3**: `init`, `step_update`, `result`.

`step_update.step_type` handled (`:202-244`), **2** plus default: `agent_response`, `tool`.
The default comment (`:241-243`) names the two ignored types: `user_input` acknowledgements and `checkpoint`
markers.

Tool step states compared: `ACTIVE`, `ERROR`, `DONE`. Fields read: `step_index`, `tool_name`,
`tool_info.name`, `tool_info.parameters`, `tool_info.error.message`, `tool_info.output`, `state`, `usage`.
Comment `:221-222`: errors ride on the `error` field (the documented shape) as well as an `ERROR` state, so
either ends the call as a failure.

`isDenied` (`:266-270`) matches the lowercased message against exactly **3** substrings:
`permission check failed`, `denied permission`, `permission denied`.

`result` fields read (`:290-357`): `usage` (with `total_tokens`, `input_tokens`, `output_tokens`), `response`,
`denied_actions` (elements' `display_name` then `action`), `conversation_id`, `status`, `error`.

`result.status` values compared, verbatim:
- `WAITING` **with a non-empty `denied_actions`** (tested first) → **failed** with
  `Antigravity held back <names> waiting for a permission it could not ask for. Use Full access for this chat.`
- `SUCCESS` or `WAITING` → completed
- `CANCELED` or `INTERRUPTED` → interrupted
- default → failed with `result["error"]` when non-empty, else `Antigravity stopped before finishing.`

Denied-actions notice (`:327-334`):
`Antigravity held back <names> without prompting. Use Full access or add an allow rule in the CLI's settings.`
where `names` is the denied list joined with `", "`.

Id namespacing (`:38`, `:207`, `:210`, `:322`): `launchID = String(UUID().uuidString.prefix(8))`; message ids
are `agy-<launchID>-message-<step_index>`, tool ids `agy-<launchID>-tool-<step_index>`, and a result-only
reply is `agy-<launchID>-result-<8-char UUID prefix>`. Comment `:35-37`: `step_index` counts up within one
process but a resumed conversation starts over, and a repeated id would stream a new turn's text into an old
entry.

Spend baseline (`:33-35`, `:60`, `:293-317`): `spendBaselineKnown = configuration.resumeID?.isEmpty != false`,
so a resumed conversation's first cumulative total seeds the baseline and is **not** charged.

### 7.6 Model / effort normalization (a computed argv value)

**SOURCE FACT** (`normalize`, `:415-435`), the exact computation:

```swift
guard let model, !model.isEmpty else { return (nil, nil) }
let parsed = parseModelRow(slug: model, name: "")
let baseSlug = parsed.baseSlug
if baseSlug.contains("claude") { return (baseSlug, nil) }
var resolvedEffort = effort
if resolvedEffort == nil || resolvedEffort?.isEmpty == true { resolvedEffort = parsed.effort }
if resolvedEffort == nil || resolvedEffort?.isEmpty == true {
    resolvedEffort = baseSlug == "gpt-oss-120b" ? "medium" : "high"
}
if baseSlug == "gemini-3.1-pro" && resolvedEffort == "medium" { resolvedEffort = "high" }
else if baseSlug == "gpt-oss-120b" { resolvedEffort = "medium" }
return (baseSlug, resolvedEffort)
```

As rules:

1. An empty or nil model yields `(nil, nil)` — **neither `--model` nor `--effort` is passed**, whatever the
   effort setting.
2. `parseModelRow` (`:396-409`) strips a trailing `-high`, `-medium` or `-low` from the slug (tested **in that
   order**) and the matching ` (High)` / ` (Medium)` / ` (Low)` suffix from the display name, returning
   `(baseSlug, baseName, effort)`.
3. Any base slug **containing the substring** `claude` gets `--model <baseSlug>` and **no** `--effort`.
4. Effort precedence: the explicit `effort` argument, then the effort parsed out of the slug suffix, then a
   default of `medium` for `gpt-oss-120b` and `high` for everything else.
5. Two hard overrides, applied **after** the default: `gemini-3.1-pro` with `medium` becomes `high`;
   `gpt-oss-120b` is **always forced to `medium`**, overriding even an explicit `high` or `low`.

The literal model identifiers compared are exactly three: the substring `claude`, and the exact slugs
`gemini-3.1-pro` and `gpt-oss-120b`.

`AntigravitySession.efforts` (`:53`): `["low", "medium", "high"]` — exactly **3**, described as "Reasoning
efforts `agy --effort` accepts."

### 7.7 Catalog and limits argv

**SOURCE FACT.** All of these are short-lived `Shell.run` invocations, not the session process:

| Call | argv | timeout |
| --- | --- | --- |
| `listModels` models fetch (`:685`) | `models` | 30 s |
| `listModels` default fetch (`:686`) | `-p`, `/model`, `--output-format`, `json` | 30 s |
| `authStatus` (`:525`) | `models` | 20 s |
| `readPlanLimits` usage (`:546`) | `-p`, `/usage`, `--output-format`, `json` | 30 s |
| `readPlanLimits` credits (`:547`) | `-p`, `/credits`, `--output-format`, `json` | 30 s |

The two `readPlanLimits` fetches and the two `listModels` fetches each run concurrently via `async let`.
Comment `:540-542`: both limit calls are zero-token headless calls, so reading limits never spends quota.

`agy models` output (`:680-682`): `slug<TAB>Name` rows on stdout, progress on stderr; rows split with
`maxSplits: 1`. `isSlug` (`:516-520`) requires a leading letter or number and allows only letters, numbers,
`-`, `_` and `.`, with no spaces. The current model id is read from `json["command"]["data"]["id"]`.

Usage parsing (`:562-580`): `json["command"]["data"]["groups"][].buckets[]` with fields `remaining_fraction`,
`id`, `name`, `reset_time`; percent is `max(0, min(100, (1 - remaining) * 100))`; title is
`"\(groupName) · \(bucketName ?? "Limit")"` with `groupName` defaulting to `Models`; the window id defaults to
`"\(groupName)-\(windows.count)"`.
Text fallback (`:584-601`): tab-separated `Group<TAB>Bucket<TAB>95%<TAB><ISO date>` rows where the percent
column is what **remains**, so `percent = max(0, min(100, 100 - remaining))` and the id is `"usage-\(windows.count)"`.
Credits parsing (`:603-626`): `remaining_credits` (Int); percent is `remaining > 0 ? 0 : 100`; id `ai-credits`;
title `"AI credits · \(remaining) remaining"`. The text fallback takes the first line whose first column
case-insensitively contains `credit` and whose second column parses as an `Int`.

`readPlanLimits` returns `nil` when no windows were produced, and `PlanLimits(planName: nil, windows: windows)`
otherwise — **Antigravity never reports a plan name.** **SOURCE FACT.**

### 7.8 Tool kind table

**SOURCE FACT** (`kind(of:)`, `:686-708`), the complete mapping with true element counts:

| Kind | Tool names | Count |
| --- | --- | --- |
| `.command` | `run_command`, `send_command_input`, `command_status`, `wait`, `wait_5_seconds`, `schedule` | 6 |
| `.read` | `view_file`, `view_file_outline`, `view_code_item`, `view_content_chunk`, `read_resource`, `read_browser_page`, `capture_browser_screenshot`, `capture_browser_console_logs` | 8 |
| `.edit` | `write_to_file`, `replace_file_content`, `multi_replace_file_content`, `sed_file`, `notebook_edit`, `notebook_execution` | 6 |
| `.search` | `list_dir`, `grep_search`, `find_by_name`, `list_resources`, `list_browser_pages` | 5 |
| `.web` | `search_web`, `read_url_content`, `open_browser_url`, `browser_click_element`, `browser_drag_pixel_to_pixel`, `browser_get_dom`, `browser_get_network_request`, `browser_input`, `browser_list_network_requests`, `browser_mouse_down`, `browser_mouse_up`, `browser_move_mouse`, `browser_press_key`, `browser_refresh_page`, `browser_resize_window`, `browser_scroll`, `browser_scroll_dom`, `browser_select_option`, `click_browser_pixel`, `execute_browser_javascript` | 20 |
| `.agent` | `invoke_subagent`, `browser_subagent`, `define_subagent`, `manage_subagents` | 4 |
| `.mcp` | `call_mcp_tool` | 1 |
| `.other` | everything else (default) | — |

Total named tools: **50**.

Parameter key lists (`:711-722`), exact contents and order:
- `path(in:)` — **11** keys, in this order: `AbsolutePath`, `TargetFile`, `NotebookPath`, `FilePath`, `File`,
  `file`, `path`, `Path`, `DirectoryPath`, `directory`, `dir`
- command: `CommandLine`, `command`, `Command` (3)
- search pattern: `Query`, `query`, `Pattern`, `pattern` (4)
- search folder: `SearchPath`, `SearchDirectory`, `DirectoryPath`, `Path`, `path` (5)
- web subject: `Url`, `URL`, `url`, `query`, `Query` (5)
- agent task: `Task`, `task`, `Prompt`, `prompt`, `Description`, `description` (6)
- MCP server: `ServerName`, `server_name`, `Server`, `server` (4); MCP tool: `ToolName`, `tool_name`, `Tool`,
  `tool` (4); MCP input: `Arguments`, `arguments`, `Input` (3)

Comment `:625-629`: the stream sends a *display subset* of the parameters (`AbsolutePath`, `TargetFile`,
`SearchDirectory`, ...), **never the edit content** — which is why `finishEdit` snapshots the file before and
after instead of reading a diff off the wire.

---

## 8. Cross-provider summary

**SOURCE FACT.** The fixed leading argv of each spawned process:

| Provider | Fixed leading argv |
| --- | --- |
| Claude, session | `-p --input-format stream-json --output-format stream-json --verbose --include-partial-messages --permission-prompt-tool stdio --setting-sources user,project,local --allow-dangerously-skip-permissions --permission-mode <mode>` |
| Claude, handshake | `-p --input-format stream-json --output-format stream-json --verbose --no-session-persistence --setting-sources user,project,local` |
| Claude, plan limits (`PlanLimits.swift`) | `-p --input-format stream-json --output-format stream-json --verbose` |
| Codex, every use | `app-server` |
| Copilot, every use | `--headless --stdio --no-auto-update --log-level error` |
| Cursor | `acp`, or `--auto-review acp`, or `--force acp` |
| OpenCode and the ACP default | `acp` |
| Grok | `--permission-mode <default\|acceptEdits\|auto> agent stdio`, or `agent --always-approve stdio` |
| Devin | `--permission-mode <auto\|accept-edits\|smart\|dangerous> acp` |
| Command Code, per turn | `-p --output-format json --skip-onboarding --no-auto-update --trust --tools-enable todo_write --max-turns <1\|400>` |
| Pi, session | `--mode rpc -e <ext path> <--approve\|--no-approve>` |
| Pi, catalog | `--mode rpc --no-session --no-extensions --no-skills --no-prompt-templates --no-themes --no-context-files` |
| Antigravity | `--input-format stream-json --output-format stream-json --print-timeout 30m --add-dir <cwd>` |

**SOURCE FACT.** Executable names actually named in these files: `copilot` (CopilotSession header),
`cmd` (CommandCodeSession header), `pi` (PiSession / PiCLI headers), `agy` (AntigravitySession header),
`codex` (CodexSession header, "Drives `codex app-server`"). Claude's and the four ACP providers' binary names
are **not** in these files; `ACPSession` and `ClaudeSession` only ever use `configuration.executable`.

**UNRESOLVED.** The executable names for Cursor, OpenCode, Grok and Devin are resolved in
`ProviderRegistry.swift`, which was outside this pass's read set. They must be read there before the port
hard-codes anything.

**SOURCE FACT.** Client identity across the handshakes:

| Provider | Identity fields |
| --- | --- |
| Codex | `clientInfo: {name: "droppy-code", title: "Droppy Code", version: AppInfo.version}` |
| ACP | `clientInfo: {name: "droppy-code", title: "Droppy Code", version: AppInfo.version}` |
| Copilot | `clientName: "droppy-code"` (session param); `ping` param `message: "droppy-code"` |
| Claude | none; control request id prefix `droppy-code-`, handshake id the literal `handshake` |
| Pi | none; request id prefix `droppy-`; catalog ids the literals `state` and `models` |
| Command Code | none; env prefix `DROPPY_CODE_` |
| Antigravity | none at all |

**SOURCE FACT.** Watchdog and timeout values:

| Site | Value |
| --- | --- |
| Claude `knownOptions` `--help` probe | 20 s |
| Claude `handshake` | 20 s |
| Claude plan-limits probe (`PlanLimits.swift`) | 20 s |
| Codex `connect` | 20 s |
| Codex `listCommands` | 20 s |
| Codex `rollback` history walk | 30 s |
| Copilot `connect` | none |
| Copilot `stop()` shutdown backstop | 3 s |
| ACP launch gate spacing | 1.5 s |
| Pi `request(_:)` | 30 s (resolves `.null`, never throws) |
| Pi `listModels` | 30 s |
| Antigravity `start` init | 30 s |
| Antigravity `--print-timeout` (passed to the CLI) | `30m` |
| Antigravity `Shell.run` calls | 20 s (`authStatus`), 30 s (all others) |

---

## 9. Ordering dependencies and conditional-inclusion gotchas

1. **Claude**: `--append-system-prompt` must come after the Hydra flags and before `--resume`/`--session-id`.
   It is emitted unconditionally, possibly with an empty value.
2. **Claude**: `--system-prompt-snapshot off` and `--forward-subagent-text` are gated on a cached `--help`
   probe, and a failed probe is **not** cached — so behaviour can differ between the first and second launch
   after a probe failure.
3. **Claude**: `--mcp-config` and `--strict-mcp-config` are one unit; with no connected servers neither is
   passed and the CLI reads the user's own config.
4. **Claude handshake**: omits four session flags and inserts `--no-session-persistence` at a position no
   session flag occupies. Not a prefix or subset in order.
5. **Copilot**: `--additional-mcp-config` takes the whole MCP config JSON as one argv token. Escaping is the
   port's problem; the source uses `Process` with no shell.
6. **Copilot**: `isExperimentalMode` is a session-creation parameter, so switching into or out of Auto
   requires restarting the session, not a mode call.
7. **Copilot quota**: three windows in fixed table order, ids are the snapshot keys, and the two percent
   branches clamp differently.
8. **Copilot models**: `copilotAuto` is prepended before the loop, so it survives even when every server model
   is `disabled`.
9. **ACP**: `launchArguments` reads the launch-time `configuration.runtimeMode`; a later mode change never
   relaunches. The 1.5-second `ACPLaunchGate` serializes starts across all ACP sessions.
10. **ACP**: Grok's `.fullAccess` row reorders the subcommand (`agent --always-approve stdio`), unlike its
    other three rows; Grok uses `acceptEdits` where Devin uses `accept-edits`.
11. **ACP**: `_meta.parameterizedModelPicker` is Cursor-only and changes what the agent reports — ids come back
    bracketed (`model[effort=high]`) and must be base-stripped before use and before comparison.
12. **ACP**: a failed `session/load` is swallowed by `try?` and silently falls through to `session/new`.
13. **Command Code**: `--mod` is skipped entirely in Full access + build mode with no connected MCP, which also
    means **no approval gate at all**; `DROPPY_CODE_APPROVALS` is explicitly cleared in that case.
14. **Command Code**: `--max-turns` is `1` for a final-report turn and `400` otherwise, and a `max_turns` stop
    is reported as a **completed** turn plus a warning notice, not a failure.
15. **Pi**: `--provider`/`--model` are only emitted when the model id contains a `/`; the split is on the first
    `/`, and the literal `default` is excluded.
16. **Pi**: the gate file is rewritten before **every** turn, so `finalReport` and `interactionMode` are
    per-turn, while `--approve`/`--no-approve` is fixed at launch from `configuration.runtimeMode`. A runtime
    mode change mid-session therefore changes the gate's behaviour but not the CLI flag.
17. **Pi**: the decline detection in `handleToolEnd` depends on the extension's block reason starting with
    `The user declined this` — the two files must change together.
18. **Antigravity**: `--allowed-mcp-server-names` takes N following tokens, not a joined list, and the ids are
    sorted.
19. **Antigravity**: `--dangerously-skip-permissions` is appended after the mode block and is **not**
    suppressed in plan mode.
20. **Antigravity**: `normalize` can force an effort the user did not choose (`gpt-oss-120b` → always `medium`;
    `gemini-3.1-pro` + `medium` → `high`) and suppresses `--effort` entirely for any slug containing `claude`.

---

## 10. Upstream defects recorded (reproduce, do not fix)

1. **Claude**: `--append-system-prompt` is passed with an empty value when Hydra and anti-slop are both off.
2. **Claude**: `--disallowedTools Agent Task` emits two bare positionals after one flag.
3. **Copilot**: quota branch B (`usedRequests`) is clamped only at 100, so a negative `usedRequests` yields a
   negative percent; branch A clamps both ends.
4. **Copilot**: `readPlanLimits` returns a bare `nil` when no windows qualify, while Codex's returns a
   `PlanLimits` with a `problem` string in the same situation.
5. **Copilot**: `listModels` reads `model["policy"]["state"]` twice through two separate subscript chains.
6. **Pi**: `unifiedDiff`'s `path.hasPrefix("/") ? path : path` is a no-op ternary; paths are never relativized.
7. **Pi**: the gate file's `systemPrompt` is always `""`, making the extension's third `before_agent_start`
   append branch unreachable in the shipped build.
8. **Pi**: `request(_:)`'s 30-second timeout resolves `.null` silently, so `start()` can return an empty
   session id instead of throwing.
9. **Antigravity**: `--dangerously-skip-permissions` is appended even in plan mode.
10. **Antigravity**: `parseCredits(_:fallback:)` takes a `fallback response:` parameter whose label is
    declared but whose structured branch returns before using it — the fallback only runs when
    `remaining_credits` is absent, which is correct, but the `data?` path never consults `response`. Noted for
    faithful transcription rather than simplification.
