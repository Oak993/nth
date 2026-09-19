# ThreadRuntime ProviderEvent dispatch — verbatim extraction

Reference: `E:/Droppy/reference/droppy-code`, commit `dd2b86efef9e77168f304a86e8a8411877578bbe` (read-only).

Files read completely and reconciled against `wc -l`:

| File | Lines |
| --- | --- |
| `DroppyCode/App/Runtime/ThreadRuntime.swift` | 3637 |
| `DroppyCode/Services/Providers/ProviderSession.swift` | 203 |
| `DroppyCode/Core/Models/Timeline.swift` | 332 |

Label key: **SF** = SOURCE FACT, **ED** = ENGINEERING DECISION (for the port), **UNRESOLVED**.

---

## 0. Adjudication of the critic's finding (ThreadRuntime.swift:1490)

**Claim:** "ThreadRuntime.swift:1490 writes `providerSessionID` directly in the DeepSeek/Meta/Z.ai branch, bypassing `recordSessionID` at 1570-1577."

**Verdict: CONFIRMED, with a line correction.** (SF)

`ensureSession(directory:allowNewSession:)` has two launch branches. The API-key branch (entered at `if thread.provider.isAPIKeyBased {`, ThreadRuntime.swift:1430) ends:

```
1488	            guard session === candidate, !Task.isCancelled else { throw CancellationError() }
1489	            started = true
1490	            app.updateThread(threadID) { $0.providerSessionID = sessionID }
1491	            return candidate
1492	        }
```

The CLI branch ends:

```
1565	        guard session === candidate, !Task.isCancelled else { throw CancellationError() }
1566	        started = true
1567	        recordSessionID(sessionID)
1568	        return candidate
1569	    }
```

`recordSessionID` is at **1574-1581**, not 1570-1577 (1571-1573 are its doc comment):

```
1571	    /// Puts the session's ID on the thread, or holds it back while no message has gone
1572	    /// through the session (`unsentSessionID`). A thread whose conversation the session
1573	    /// picked up takes the ID at once: a provider may hand out a new one for it.
1574	    private func recordSessionID(_ sessionID: String) {
1575	        guard let app else { return }
1576	        if app.thread(threadID)?.providerSessionID == nil {
1577	            unsentSessionID = sessionID
1578	        } else {
1579	            app.updateThread(threadID) { $0.providerSessionID = sessionID }
1580	        }
1581	    }
```

**Behavioural consequence to reproduce (SF):** for `.deepseek`, `.meta`, `.zai` the session id is persisted onto the thread **immediately at start**, with no `unsentSessionID` hold-back. For every CLI provider it is held back until the first `send()` succeeds. This is not academic: `warmSession()` (2. below) is gated on `!thread.provider.isAPIKeyBased` (ThreadRuntime.swift:1382), so API providers never warm a session ahead of the first message; the only caller of `ensureSession` for them is inside `startTurn`, immediately before `session.send(...)`. So in practice the exposure window is small, but it is real for any path that starts an API session and then fails before `send` (e.g. `revert` for `default:` providers releases the session, and `ensureSession(allowNewSession: false)` is called from the Codex/Copilot revert branches only).

**Additional, stronger form of the same bypass (SF):** the API branch also writes `providerSessionID` when it *replaces* a session that would not resume (1482-1486), and it does not clear `providerResumeAt` and does not emit the "no longer has this conversation" notice that the CLI branch emits at 1554-1559. The API branch's `catch where` clause (1479) is also different from the CLI one (1548): API uses `thread.providerSessionID != nil && thread.providerResumeAt == nil && allowNewSession && session === candidate && !Task.isCancelled`; CLI uses `allowNewSession && session === candidate && !Task.isCancelled && Self.startsOver(after: error, resuming: thread)`.

---

## 1. The event type — exact case list

`ProviderSession.swift:82-118`. **30 cases. Counted, not estimated.**

| # | Case (verbatim) |
| --- | --- |
| 1 | `case sessionReady(sessionID: String)` |
| 2 | `case turnStarted(providerTurnID: String?)` |
| 3 | `case messageDelta(id: String, text: String)` |
| 4 | `case messageCompleted(id: String, text: String)` |
| 5 | `case reasoningDelta(id: String, text: String)` |
| 6 | `case reasoningCompleted(id: String, text: String)` |
| 7 | `case toolStarted(id: String, call: ToolCall)` |
| 8 | `case toolOutput(id: String, text: String)` |
| 9 | `case toolUpdated(id: String, update: ToolUpdate)` |
| 10 | `case planDelta(id: String, text: String)` |
| 11 | `case planCompleted(id: String, markdown: String)` |
| 12 | `case todos([TodoStep])` |
| 13 | `case approval(ApprovalRequest)` |
| 14 | `case question(QuestionRequest)` |
| 15 | `case requestResolved(id: String)` |
| 16 | `case usage(ContextUsage)` |
| 17 | `case diff(String)` |
| 18 | `case notice(Notice)` |
| 19 | `case usageLimit(resetsAt: Date?)` |
| 20 | `case modeChanged(InteractionMode)` |
| 21 | `case models([ModelOption], current: String?)` |
| 22 | `case commands([SlashCommand])` |
| 23 | `case title(String)` |
| 24 | `case assistantMessageID(String)` |
| 25 | `case turnCompleted(status: TurnStatus, error: String?)` |
| 26 | `case exited(error: String?)` |
| 27 | `case agentStarted(AgentSpawn)` |
| 28 | `indirect case agentEvent(agentID: String, ProviderEvent)` |
| 29 | `case agentProgress(agentID: String, summary: String?, lastTool: String?, tokens: Int?, toolCalls: Int?)` |
| 30 | `case agentFinished(agentID: String, status: TurnStatus, summary: String?)` |

`ToolUpdate` (`ProviderSession.swift:120-128`), 7 fields, all optional:
`title: String?`, `detail: String?`, `output: String?`, `status: ToolCall.Status?`, `exitCode: Int?`, `edits: [FileEdit]?`, `kind: ToolCall.Kind?`.

`AgentSpawn` (`ProviderSession.swift:61-79`), 8 fields:
`id: String`, `taskID: String?`, `toolUseID: String?`, `description: String`, `prompt: String?`, `model: String?`, `profile: String?`, `isBackground = true`.

### 1a. UPSTREAM DEFECT — the pinned checkout does not compile (SF)

Four provider sessions emit a `ProviderEvent` case that **does not exist** in the enum at this commit:

- `DroppyCode/Services/Providers/AntigravitySession.swift:308` — `onEvent?(.tokenSpend(TokenSpend(`
- `DroppyCode/Services/Providers/CommandCodeSession.swift:350` — `onEvent?(.tokenSpend(TokenSpend(`
- `DroppyCode/Services/Providers/CopilotSession.swift:554` — `onEvent?(.tokenSpend(TokenSpend(`
- `DroppyCode/Services/Providers/OpenAICompatibleSession.swift:494` — `onEvent?(.tokenSpend(TokenSpend(`

`grep -rn "enum ProviderEvent"` returns exactly one definition (`ProviderSession.swift:82`), and `git show HEAD:…/ProviderSession.swift` confirms the committed file has the same 30 cases with no `tokenSpend`. The commit is mid-refactor. Record and reproduce the *enum as committed*; do not invent the case. (UNRESOLVED for the port: decide whether to port the 30-case enum, or the 31-case enum the session adapters clearly expect.)

### 1b. UPSTREAM DEFECT — the token ledger is never written to a turn (SF)

`TurnRecord.tokenSpends: [TokenSpend]?` (`Timeline.swift:269`, decoded at `Timeline.swift:292`) is **never assigned anywhere in the repository**. `grep -rn "tokenSpends"` returns exactly three hits: the declaration, the decode, and one read at `DroppyCode/App/Views/Chat/HydraRunMetricsView.swift:135` (`guard let spends = turn.tokenSpends, !spends.isEmpty else { continue }`). **ThreadRuntime writes no token-ledger data at all.** The only ledger write reachable from a session is `TokenLedger.shared.record(spend:)` inside the session adapters themselves (e.g. `OpenAICompatibleSession.swift:490`, `CopilotSession.swift:549`), not through `ThreadRuntime`. The Hydra run-metrics view therefore always renders empty for spends. This is shipped-but-dead state; reproduce it as such.

---

## 2. Session lifecycle

### 2.1 Fields that hold session state

| Field | Line | Meaning |
| --- | --- | --- |
| `session: (any ProviderSession)?` | 414 | the live session |
| `warmup: Task<Void, Never>?` | 416 | a session being started ahead of the first message |
| `unsentSessionID: String?` | 423 | the hold-back id |
| `sessionSignature: SessionSignature?` | 425 | what the live session was launched with |
| `antiSlopFallbackSent: Bool` | 430 | anti-slop prefix already delivered on this session |
| `continuationSent: Bool` | 435 | forwarded continuation already delivered on this session |
| `sessionEpoch: Int` | 440 | which session the runtime listens to |
| `isStartingSession: Bool` | 444 | `ensureSession` is awaiting `start()` |
| `finishingTurnID: UUID?` | 447 | the turn being finalized |
| `currentTurnID: UUID?` | 458 | the running turn |
| `closingTurnID: UUID?` | 464 | the turn being closed |
| `currentProviderDiff: String?` | 473 | latest `.diff` payload of the running turn |
| `currentProviderAnchor: String?` | 474 | latest `.assistantMessageID` payload of the running turn |

`turnIDForNewRows` (468): `currentTurnID ?? closingTurnID`. **Ordering dependency (SF):** rows created during `finishTurn`'s diff wait are still filed under the closing turn; `.diff` and `.assistantMessageID`, which test `currentTurnID != nil` rather than `turnIDForNewRows`, are **dropped** in that window.

### 2.2 `SessionSignature` (ThreadRuntime.swift:504-527) — 9 fields

`provider: ProviderKind`, `directory: String`, `launchRuntimeMode: RuntimeMode?`, `launchEffort: String?`, `launchFast: Bool?`, `launchModel: String?`, `launchInteraction: InteractionMode?`, `hydra: HydraLaunch?`, `antiSlopEnabled: Bool`.

Populated at 1411-1421 with exactly these conditionals:

- `launchRuntimeMode`: `thread.provider == .cursor || thread.provider == .grok || thread.provider == .devin || thread.provider == .antigravity ? thread.runtimeMode : copilotLaunchMode`, where `copilotLaunchMode` (1386) is `thread.provider == .copilot && thread.runtimeMode == .auto ? .auto : nil`.
- `launchEffort`: `thread.provider == .claude || thread.provider == .antigravity ? thread.effort : nil`
- `launchFast`: `thread.provider == .claude ? thread.fastMode : nil`
- `launchModel`: `thread.provider == .antigravity ? thread.model : nil`
- `launchInteraction`: `thread.provider == .antigravity ? thread.interactionMode : nil`
- `hydra`: `Self.keepsHydraPolicyInSystemPrompt(thread.provider) ? app.hydraLaunch(for: thread) : nil`
- `antiSlopEnabled`: `app.settings.antiSlopEnabled`

`keepsHydraPolicyInSystemPrompt(_:)` (1338-1340): `AppModel.hydraIsNative(provider) || provider.isAPIKeyBased`.

Reuse test (1422): `if let session, session.isRunning, sessionSignature == signature { return session }`.

### 2.3 `ensureSession` order of operations (1385-1569) — SF

1. `guard let app, let thread = app.thread(threadID) else { throw ProviderError.notRunning }`
2. compute `copilotLaunchMode`
3. if `app.hydraIsOn(thread)`, pair exists and `app.hydraHeadsProvider(of: pair) != thread.provider` → `await app.providers.loadCatalog(app.hydraHeadsProvider(of: pair))` **before** the signature is built (1404-1406).
4. build `hydra` and `signature`; reuse if it matches.
5. `await LoginEnvironment.load()` (1424) — *before* `releaseSession`.
6. `releaseSession(stop: true)` (1425)
7. `stopNativeHeads()` (1427)
8. **API branch** (`thread.provider.isAPIKeyBased`, 1430-1492):
   - `guard !app.settings.apiKey(for: thread.provider).isEmpty else { throw ProviderError.notInstalled(thread.provider) }`
   - `makeAPISession(resumeID:)` builds `SessionConfiguration` with `executable: nil`, `resumeAt: resumeID == nil ? nil : thread.providerResumeAt`, `apiKey:`, `hydra:`, `transcript: apiTranscript()`, `isHydraHead: thread.hydra?.kind == .droppy`, `antiSlopEnabled: signature.antiSlopEnabled`; switch: `.deepseek → DeepSeekSession`, `.meta → MetaSession`, `.zai → ZaiSession`, `default → DeepSeekSession`; then `observe(created)`.
   - `isStartingSession` is **NOT** set in this branch (contrast 1534). (SF; see 4.26 for the consequence.)
   - `defer`: if `!started` → `candidate.onEvent = nil`, then `releaseSession(stop: true)` if `session === candidate` else `candidate.stop()`.
   - retry clause at 1479, then 1490 direct write (see §0).
9. **CLI branch** (1494-1568): `guard let executable = app.providers.executable(for: thread.provider) else { throw ProviderError.notInstalled(thread.provider) }`; `makeSession(resumeID:)` with the 12-field `SessionConfiguration` at 1498-1512 (no `apiKey`, no `transcript`, no `isHydraHead`); session switch (1513-1524), **10 arms**: `.codex → CodexSession`, `.claude → ClaudeSession`, `.antigravity → AntigravitySession`, `.copilot → CopilotSession`, `.commandcode → CommandCodeSession`, `.pi → PiSession`, `.cursor, .opencode, .grok, .devin → ACPSession`, `.deepseek → DeepSeekSession`, `.meta → MetaSession`, `.zai → ZaiSession`. `isStartingSession = true` (1534) with `defer { isStartingSession = false; … }`. Retry on `Self.startsOver(after:resuming:)`; on `Self.lostConversation(error)` it clears `providerResumeAt` and, **only if `currentTurnID != nil`**, appends the warning notice, verbatim:

  > `"\(thread.provider.displayName) no longer has this conversation. It starts a new one here, without the messages above."`

  Finally `recordSessionID(sessionID)`.

`startsOver(after:resuming:)` (1586-1589): `guard thread.providerSessionID != nil else { return false }; return lostConversation(error) || thread.providerResumeAt == nil`.

`lostConversation(_:)` (1593-1597) — exact match strings, case-insensitive, on `error.localizedDescription`:
`"no conversation found"` OR `"no longer has this conversation"`.

### 2.4 The `unsentSessionID` hold-back — every writer and reader (SF)

- **Set**: `recordSessionID` (1577) when `app.thread(threadID)?.providerSessionID == nil`. Called from exactly two places: `ensureSession` CLI branch (1567) and `handle(.sessionReady)` (2082).
- **Consumed**: `startTurn`, after `session.send(...)` returns without throwing (1315-1318):
  ```
  if let unsentSessionID {
      self.unsentSessionID = nil
      app.updateThread(threadID) { $0.providerSessionID = unsentSessionID }
  }
  ```
- **Cleared without being written to the thread**: `releaseSession(stop:)` (2045) — `unsentSessionID = nil`, before the `guard let old = session else { return }`.
- **Branches that bypass the hold-back entirely** (write `providerSessionID` directly): ThreadRuntime.swift:**1490** (API-key launch), **1579** (inside `recordSessionID`, when the thread already had an id), **1829** (Codex rollback in `revert`). And these clear it: **1846**, **1863** (revert), and `revert` also sets `providerResumeAt` at **1854** / clears at **1847**, **1864**.

### 2.5 `observe` / `releaseSession` — the epoch rule (SF)

```
2030	    func observe(_ provider: any ProviderSession) {
2031	        sessionEpoch += 1
2032	        let epoch = sessionEpoch
2033	        provider.onEvent = { [weak self] event in
2034	            guard let self, self.sessionEpoch == epoch else { return }
2035	            self.handle(event)
2036	        }
2037	    }
```

```
2042	    private func releaseSession(stop: Bool) {
2043	        sessionEpoch += 1
2044	        sessionSignature = nil
2045	        unsentSessionID = nil
2050	        antiSlopFallbackSent = false
2051	        continuationSent = false
2052	        guard let old = session else { return }
2053	        session = nil
2054	        if stop { old.stop() }
2055	        Task { old.onEvent = nil }
2056	    }
```

**Ordering dependency (SF):** the three resets at 2044-2051 happen **before** the `guard`, so they run even when no session exists. `onEvent` is cleared one runloop turn later, deliberately, because a closing session usually speaks from inside that handler; the epoch check is what actually silences it.

`stopSession()` (2058-2075): `releaseSession(stop: true)`; `stopNativeHeads()`; `guard phase != .idle else { return }`; `saveNow()`; take `turnID = currentTurnID`; `currentTurnID = nil`; `Task { await finishTurn(status: .interrupted, turnID: turnID) }`.

### 2.6 `warmSession()` (1380-1391) — SF

Guards: `phase == .idle, session == nil, warmup == nil, !isReverting, let app, let thread = app.thread(threadID), !thread.isArchived, !thread.provider.isAPIKeyBased, let project = app.project(thread.projectID)`. Directory `thread.worktreePath ?? project.path`. Inside the task: `await ensureModel(thread)`, re-check `phase == .idle, session == nil` (else `warmup = nil; return`), then `if (try? await ensureSession(directory: directory)) != nil { warmup = nil }` — **a failed warm-up deliberately leaves `warmup` non-nil**, so keystrokes do not retry; the send makes its own.

### 2.7 `startTurn` (1158-1332) — full order, SF

1. `guard phase == .idle, !isReverting else { enqueueFollowUp(text:attachments:); return }`
2. `guard let app, let initialThread = app.thread(threadID), let project = app.project(initialThread.projectID) else { return }`
3. `guard !initialThread.isArchived else { enqueueFollowUp(...); return }`
4. `turnIndex = (turns.map(\.index).max() ?? -1) + 1`; `turn = TurnRecord(index: turnIndex)`
5. build `userItem = TimelineItem(turnID: turn.id, content: .user(UserMessage(text:attachments:hydraHeads:hydraBrief: hydraBrief ? true : nil)))`; `turn.userItemID = userItem.id`; `turns.append(turn)`; `saveRevision += 1`
6. `currentTurnID = turn.id`; `currentProviderDiff = nil`; `currentProviderAnchor = nil`; `append(userItem)`
7. `phase = .starting`; `turnStartedAt = .now`
8. `app.updateThread(threadID) { $0.updatedAt = .now; $0.lastStatus = .running }`; `app.reopenIfSettled(threadID)`
9. `hydraStream = nil`; if `hydraHeads == nil` → `hydraDelegationRounds = 0; hydraRefusedBlocks = 0`
10. a finished Droppy head told more → `app.updateHydraHead(threadID) { $0.status = .running; $0.finishedAt = nil }`
11. `isFinalReport = headReportsNext`; `headReportsNext = false`; if `initialThread.hydra?.kind == .droppy` the head budget task is armed with `allowance = isFinalReport ? HydraBudget.reportSeconds : HydraBudget.maxSeconds + (initialThread.provider.isAPIKeyBased ? 90 : 0)` → **180 s**, or **2100 s** (+90 = **2190 s** for API providers). On fire: `headBudgetSpent = !isFinalReport`, then `self.interrupt()`.
12. `scheduleSave()`; `generateTitle(from:attachments:)`
13. `await ensureModel(initialThread)`
14. `directory = initialThread.worktreePath ?? project.path`; `git = Git(directory)`; `checkpointRef = Git.checkpointRef(thread: threadID, turn: turnIndex, phase: "start")` → literally `refs/droppy-code/checkpoints/<uuid lowercased>/<turn>-start` (`Git.swift:409-411`).
15. `settledTree = nil; latestTree = nil; treeEpoch += 1`
16. checkpoint task started detached (`git.isRepository()` then `git.captureCheckpoint(ref)`), **concurrently** with the session start.
17. `if let warmup { await warmup.value }`; `start = Task { _ = try await self.ensureSession(directory: directory) }`; `if await checkpoint.value { updateTurn(turn.id) { $0.baseCheckpoint = checkpointRef } }`; `try await start.value`
18. if `SlashCommand.name(in: text) != nil` → `await app.providers.loadCommands(initialThread.provider, directory: directory)`
19. `guard currentTurnID == turn.id, let session, let thread = app.thread(threadID) else { return }`
20. resolve `command` from `app.providers.commands[ProviderRegistry.commandKey(thread.provider, directory: directory)]`
21. prompt assembly, **in this order** (each conditional):
    a. non-image attachments appended: `prompt += "\n\nAttached files:\n" + files.map { "- \($0.path)" }.joined(separator: "\n")`
    b. Hydra prefixes (1252-1276). With `launch = app.hydraLaunch(for: thread)`: `merge = launch.autoMerges ? HydraPrompts.mergeStatus(merges: hydraMerges, unmergedFiles: app.hydraUnmergedFileCount(of: threadID)) : nil`. If `!launch.runsNatively`: `team = HydraPrompts.teamStatus(app.hydraTeam(of: threadID).compactMap(\.hydra))`, `canDelegate = hydraDelegationRounds < HydraPrompts.maxDelegationRounds`; then one of four prefixes — `fallbackTurnNote(team:merge:)` / `fallbackReportNote(team:merge:canDelegate:)` when `keepsHydraPolicyInSystemPrompt(thread.provider)`, else `fallbackPreamble(launch, team:merge:)` (hydraHeads == nil) or `fallbackReportPreamble(launch, team:merge:canDelegate:)`. Else if `merge != nil`: `HydraPrompts.fallbackTurnNote(team: nil, merge: merge)`. Then, only when `hydraHeads == nil`: `prompt = HydraPrompts.delegationRequest(launch) + prompt`.
    c. anti-slop (1278-1287): only when `!keepsHydraPolicyInSystemPrompt(thread.provider)`, `SlashCommand.name(in: text) == nil`, `!antiSlopFallbackSent`; `instructions = AntiSlopPolicy.instructions(enabled: sessionSignature?.antiSlopEnabled ?? app.settings.antiSlopEnabled)`; `prompt = instructions + "\n\n" + prompt`; sets `injectedAntiSlopPolicy`, captures `antiSlopEpoch = sessionEpoch`.
    d. continuation (1291-1296): only when `let continuation, !continuationSent, SlashCommand.name(in: text) == nil`; `prompt = continuation.prompt + "\n\n" + prompt`; captures `continuationEpoch = sessionEpoch`.
22. `phase = .running`
23. `try await session.send(TurnInput(text: prompt, images: thread.provider.supportsImages ? attachments.filter(\.isImage) : [], model: thread.model, effort: thread.effort, serviceTier: serviceTier(for: thread), fastMode: thread.fastMode, runtimeMode: thread.runtimeMode, interactionMode: thread.interactionMode, isFinalReport: isFinalReport, command: command))`
24. post-send, **epoch-guarded** (1309-1314): `if injectedAntiSlopPolicy, sessionEpoch == antiSlopEpoch { antiSlopFallbackSent = true }`; same for `continuationSent`.
25. `unsentSessionID` consumed (1315-1318).
26. `if let command { app.providers.recordCommand(command.name, for: thread.provider) }`
27. `catch`: `guard currentTurnID == turn.id else { return }`; `appendNotice(.error, error.localizedDescription)`; `await finishTurn(status: .failed, turnID: turn.id)`.

`serviceTier(for:)` (1344-1349): nil unless `thread.provider == .codex` and the model has a `fastTier`; then `thread.fastMode ? tier : "default"` — note the literal `"default"`.

### 2.8 `interrupt()` (1655-1683) — SF

```
guard phase != .idle else { return }
let turnID = currentTurnID
interruptWatchdog?.cancel(); interruptWatchdog = nil
if phase == .starting {
    currentTurnID = nil
    Task { await finishTurn(status: .interrupted, turnID: turnID) }
    return
}
Task {
    guard currentTurnID == turnID, turnID != nil else { return }
    if let session { await session.interrupt() } else { await finishTurn(status: .interrupted, turnID: turnID) }
}
interruptWatchdog = Task { [weak self] in
    try? await Task.sleep(for: .seconds(8))
    guard let self, !Task.isCancelled, self.currentTurnID == turnID, turnID != nil else { return }
    self.releaseSession(stop: true)
    await self.finishTurn(status: .interrupted)
}
```

Watchdog: **8 seconds**. `takeBackRunningTurn()` (642-704) polls `for _ in 0..<200 where phase != .idle { try? await Task.sleep(for: .milliseconds(50)) }` — **200 × 50 ms = 10 s**, then `revert(to: turnID, restoreFiles: false)`, and on failure appends `.error` with `"The stopped message was kept: \(error.localizedDescription)"`.

### 2.9 `compact()` (1702-1721) — SF

`guard phase == .idle`; if no running session → `appendNotice(.info, "Nothing to compact yet.")`. Else `appendNotice(.info, "Compacting context.")`, then `try await session.compact()` and on success `antiSlopFallbackSent = false; continuationSent = false`; on throw `appendNotice(.warning, error.localizedDescription)`.

---

## 3. `handle(_:)` — the dispatch (ThreadRuntime.swift:2079-2196)

The switch is **exhaustive with no `default:`**; all 30 cases appear. The only entry points are the `observe` closure (2033-2036) and the public `rehearse(_:)` (3618-3620), which is the website-capture / head-rehearsal path and goes through the identical code.

---

## 4. Case-by-case specification

### 4.1 `.sessionReady(sessionID:)` — line 2081-2082
- Calls `recordSessionID(sessionID)`.
- **State:** `unsentSessionID = sessionID` when the thread has no `providerSessionID`, else `app.updateThread { $0.providerSessionID = sessionID }`.
- **Timeline:** none. **Persist:** thread record only (no `saveRevision`). **Triggers:** none.
- **Idempotency:** last one wins; repeated events overwrite.
- In `.agentEvent` it is **dropped** (2508).

### 4.2 `.turnStarted(providerTurnID:)` — 2083-2087
- `phase = .running` unconditionally (so a `.starting` turn is promoted even before `send` returns on some adapters).
- `if let currentTurnID, let providerTurnID { updateTurn(currentTurnID) { $0.providerTurnID = providerTurnID } }` → `saveRevision += 1` via `updateTurn`.
- **Ordering:** `providerTurnID` is what `revert` uses for Codex rollback (1823); a turn that never gets it cannot be reverted on Codex (error string at 1831).
- **Idempotency:** last value wins.

### 4.3 `.messageDelta(id:text:)` — 2088-2089
- `queueDelta(id, .message, text)`.
- **Row creation (2947-2951):** `guard !text.isEmpty else { return }` — an empty opening delta creates **no** row; then `append(TimelineItem(id: id, turnID: turnIDForNewRows, content: .assistant(AssistantMessage(text: "", isStreaming: true))))`.
- Text is flattened: `let prose = kind == .toolOutput ? text : TextCleanup.withoutEmDashes(text)` (2966).
- Accumulated into `pendingDeltas[id]`, flushed by `flushDeltas()` (see §5).

### 4.4 `.messageCompleted(id:text:)` — 2090-2092
- `flushDeltas()` **first**, then `completeMessage(id, text:)` (3096-3114).
- `completeMessage`: `text = TextCleanup.withoutEmDashes(text)`. No entry → if text empty, return; else append a non-streaming assistant row under `turnIDForNewRows`. Entry present but not `.assistant` → return (payload dropped). Otherwise `if !text.isEmpty { message.text = text }` (**a completed event with empty text keeps the streamed text**), `message.isStreaming = false`; if the result has no non-whitespace character → `remove(id)`; else write back, `saveRevision += 1`, and `if let turnID = currentTurnID { scanStreamedDelegations(entryID: id, turnID: turnID) }`. Always `scheduleSave()`.

### 4.5 `.reasoningDelta(id:text:)` — 2093-2094
- `queueDelta(id, .reasoning, text)`; same empty-text row suppression (2955) — the comment names Claude Code's redacted reasoning explicitly.

### 4.6 `.reasoningCompleted(id:text:)` — 2095-2097
- `flushDeltas()`, then `completeReasoning` (3116-3132): identical shape to `completeMessage` minus the delegation scan; blank result → `remove(id)`.

### 4.7 `.toolStarted(id:call:)` — 2098-2101
```
flushDeltas()
upsertTool(id, call)
if call.kind == .command { watchCommand(id, call) }
```
- `upsertTool` (3147-3166): `noteToolMayWrite(call.kind)`; `call.edits = Self.capped(call.edits)` (drops any `diff` over `editDiffLimit = 200_000` UTF-8 bytes, 3366/3371-3378). New row → `append(... .tool(call))` then `noteHydraHeadEvent(edits: call.edits.count, tool: true)`. Existing row → `if !call.title.isEmpty { existing.title = call.title }`; `existing.detail = call.detail ?? existing.detail`; `existing.kind = call.kind`; `landed = !call.edits.isEmpty && existing.edits.isEmpty ? call.edits.count : 0`; `if !call.edits.isEmpty { existing.edits = call.edits }` (**note: the replacement is `call.edits`, uncapped on this path — `capped` was applied to the local `call` at 3150, so it is capped; but `existing.edits` is wholesale replaced, not merged**); `if existing.status == .running, call.status != .running { existing.finish(call.status) }`; `saveRevision += 1`; `noteHydraHeadEvent(edits: landed)`.
- `noteToolMayWrite(_:)` (3183-3187): returns early for `nil`, `.read`, `.search`, `.web`, `.command`; otherwise `settledTree = nil; treeEpoch += 1`. **5 exempt kinds out of the 8 `ToolCall.Kind` values** (`command, read, edit, search, web, mcp, agent, other`), so `.edit`, `.mcp`, `.agent`, `.other` invalidate the tree.
- `watchCommand(id, call)` (3229-3237): `guard commandTrees[id] == nil, ShellCommandKind.mayWriteFiles(call.title), repositoryGit != nil, let before = treeBeforeCommand()`; then `commandTrees[id] = Task { before }`, `settledTree = nil`, `treeEpoch += 1`.
- **Does not** call `scheduleSave()` (contrast `applyToolUpdate`).

### 4.8 `.toolOutput(id:text:)` — 2102-2103
- `queueDelta(id, .toolOutput, text)`. **If no entry exists the delta is discarded** (`case .toolOutput: return`, 2959-2960) — output for an unknown tool id is lost, by design.
- Tool output is **not** em-dash flattened (2966).
- On flush, `ToolCall.appendOutput` trims to `ToolCall.outputLimit = 60_000` UTF-8 bytes by replacing with `"…" + String(output.suffix(outputLimit / 2))` = suffix of **30 000 characters** (`Timeline.swift:137, 149-167`).

### 4.9 `.toolUpdated(id:update:)` — 2104-2113
```
flushDeltas()
applyToolUpdate(id, update)
if let entry = entryIndex[id], case .tool(let call) = entry.item.content,
   call.kind == .command, call.status != .running {
    settleCommand(id)
}
if let status = update.status, status != .running, let headID = hydraToolHeads[id] {
    hydraToolFinished(headID, status: status, output: update.output)
}
```
- `applyToolUpdate` (3189-3212): `noteToolMayWrite(update.kind)`; **creates the row if missing** — `ToolCall(kind: update.kind ?? .other, title: update.title ?? "Tool")` (literal fallback title `"Tool"`); then field-by-field — `title` only if non-empty, `detail` if non-nil, `kind` if non-nil, `edits` replaced (and counted as `landed` only when `call.edits.isEmpty` beforehand) through `Self.capped`, `output` via `setOutput` (replace, then trim), `exitCode`, and `status`: `if status == .running { call.status = .running } else { call.finish(status) }` — `finish` sets `finishedAt` only the first time (`Timeline.swift:159-162`). Then `saveRevision += 1`, `noteHydraHeadEvent(edits: landed)`, `scheduleSave()`.
- `settleCommand(id)` (3271-3300) is driven off the *post-update* row state, so the ordering (`applyToolUpdate` first) is load-bearing.
- `hydraToolFinished(headID, status:output:)` (3528-3537) maps `ToolCall.Status → TurnStatus`: `.completed → .completed`, `.declined → .interrupted`, `default → .failed` (covers `.running` and `.failed`), and requires `info.kind == .native, !info.isBackground, !info.isFinished, let nativeID = info.nativeID`.

### 4.10 `.planDelta(id:text:)` — 2114-2115
- `queueDelta(id, .plan, text)`. **Unlike message/reasoning, an empty first delta still creates the row**: `append(... .plan(ProposedPlan(markdown: "", state: .drafting)))` (2957-2958) with no emptiness guard. (SF — asymmetry to reproduce.)
- Plan deltas do **not** set `heard` in `flushDeltas` (`if delta.kind != .plan { heard = true }`, 3087), so a plan-only stream does not restart a head's watchdog clock.

### 4.11 `.planCompleted(id:markdown:)` — 2116-2118
- `flushDeltas()`, then `completePlan` (3134-3145): `markdown = TextCleanup.withoutEmDashes(markdown)`; existing `.plan` row → keep old markdown if the new one is empty, `plan.state = .proposed`, `saveRevision += 1`; no row and non-empty markdown → append a `.proposed` plan under `turnIDForNewRows`. Always `scheduleSave()`.
- `endStreaming` (2928-2942) also promotes `.drafting → .proposed`.

### 4.12 `.todos([TodoStep])` — 2119-2120
- `upsertTodos(steps)` (3403-3427). Fast path: `todosEntryID` cached and its `entry.item.turnID == currentTurnID` → overwrite, `saveRevision += 1`, **return without `scheduleSave()`**. Otherwise scan `entries.reversed()`, breaking at the first entry whose `turnID` is non-nil and `!= currentTurnID`; found → cache id and overwrite; not found and `!steps.isEmpty` → append a new `.todos` row under `turnIDForNewRows` and cache its id. Then `scheduleSave()`.
- **Idempotency:** one todos row per turn; an empty `steps` array with no existing row creates nothing.

### 4.13 `.approval(ApprovalRequest)` — 2121-2124
```
approvals.removeAll { $0.id == request.id }
approvals.append(request)
app?.threadNeedsAttention(threadID)
```
- De-dupes by id then appends at the end (so a re-sent approval moves to the back of the list).
- `threadNeedsAttention` is the user-attention trigger (`AppModel.swift` — `notify(threadID:title:body: "Waiting for your decision.")` and `NSApp.requestUserAttention(.informationalRequest)`).
- Cleared wholesale by `.exited` (2174), by `finishTurn` (2243), and by `resolve(_:option:)` (1686).
- `resolve` (1685-1695) additionally, when `request.kind == .plan` and `request.toolItemID` names a `.plan` row, sets `plan.state = option.role == .approve ? .accepted : .dismissed`, `saveRevision += 1`, `scheduleSave()`; then `session?.resolveApproval(request.id, optionID: option.id)`.

### 4.14 `.question(QuestionRequest)` — 2125-2128
- Same shape as `.approval` on `questions`; `app?.threadNeedsAttention(threadID)`.
- `answer(_:answers:)` (1697-1700): removes by id, `session?.answerQuestion(request.id, answers: answers)`.

### 4.15 `.requestResolved(id:)` — 2129-2131
- `approvals.removeAll { $0.id == id }`; `questions.removeAll { $0.id == id }`. No timeline, no persist, no session call.

### 4.16 `.usage(ContextUsage)` — 2132-2139
```
self.usage = usage
saveRevision += 1
if let provider = thread?.provider {
    app?.providers.invalidateCredits(provider)
    app?.providers.invalidatePlanLimits(provider)
}
```
- **No `scheduleSave()`** — the write rides on the next save. Persisted as `ThreadDocument.usage` (3535).
- `usage` is cleared to nil by `revert` (1890) and seeded from the document at install (`usage = usage ?? document.usage`, 553).
- **This is the only "ledger-adjacent" write in the dispatch.** No `TokenSpend` is recorded here (see §1b).
- In `.agentEvent` it is **dropped** (2508) — a native head's usage never updates anything.

### 4.17 `.diff(String)` — 2140-2141
- `if currentTurnID != nil { currentProviderDiff = diff }`. Last one wins (the comment at 470-472 says Codex re-sends the whole turn diff on every file change). Dropped when `currentTurnID == nil`, including during `finishTurn`'s wait.
- Consumed in `finishTurn` (2258-2266) → `TurnRecord.providerDiff`.

### 4.18 `.notice(Notice)` — 2142-2143
- `appendNotice(notice.level, notice.message)` (3429-3432): appends `TimelineItem(turnID: turnIDForNewRows, content: .notice(Notice(level:message:)))` and `scheduleSave()`.
- **The `Notice.limit` field is discarded**: only `level` and `message` are carried through. `LimitNotice` is set elsewhere (by `AutoContinue`), never by this path. (SF — do not forward `limit` in the port.)

### 4.19 `.usageLimit(resetsAt:)` — 2144-2145
- `app?.autoContinue.noteLimit(threadID, resetsAt: resetsAt)`. `noteLimit` (`AutoContinue.swift:43-47`): `if let existing = noted[threadID], existing != nil, resetsAt == nil { return }` — a later note without a time never overwrites an earlier one with a time; else `noted[threadID] = resetsAt`.
- Nothing lands on the timeline here; the wait/notice is `AutoContinue`'s, driven from `AppModel.turnFinished` → `autoContinue.turnFinished(id, status:continues:)`, which only engages when `app.settings.autoContinueAfterLimit, status == .failed, !continues, !thread.isHydraHead`.

### 4.20 `.modeChanged(InteractionMode)` — 2146-2147
- `app?.updateThread(threadID) { $0.interactionMode = mode }`. No timeline, no `saveRevision`.

### 4.21 `.models([ModelOption], current: String?)` — 2148-2149
- `if let provider = thread?.provider { app?.providers.updateCatalog(list, for: provider) }`.
- **The `current:` payload is bound to `_` and discarded** (SF).

### 4.22 `.commands([SlashCommand])` — 2150-2153
- `if let app, let thread, let project = app.project(thread.projectID) { app.providers.updateCommands(list, for: thread.provider, directory: thread.worktreePath ?? project.path) }`.

### 4.23 `.title(String)` — 2154-2158
```
guard turns.count <= 1, let app, let thread, !thread.hasCustomTitle,
      app.textEngine(preferring: thread.provider) == nil else { break }
app.updateThread(threadID) { $0.title = TextCleanup.withoutEmDashes(title) }
```
- Four conditions, all required: at most one turn; no custom title; **and no local text engine available** (a provider title is only a fallback for when Droppy Code cannot name the chat itself).

### 4.24 `.assistantMessageID(String)` — 2159-2160
- `if currentTurnID != nil { currentProviderAnchor = anchor }`. Last one wins. Consumed in `finishTurn` → `TurnRecord.providerAnchor` and, when `thread?.providerResumeAt != nil`, `app?.updateThread { $0.providerResumeAt = providerAnchor }` (2268-2270).
- This anchor is what Claude's `revert` requires (1850: `let anchor = turns[index - 1].providerAnchor`).

### 4.25 `.turnCompleted(status:error:)` — 2161-2165
```
flushDeltas()
if let error { appendNotice(.error, error) }
let turnID = currentTurnID
Task { await finishTurn(status: status, turnID: turnID) }
```
- The error notice lands **before** the turn closes, so it is filed under the turn (via `turnIDForNewRows`).
- `finishTurn` is async and claims the turn through `finishingTurnID` (2214-2216), so a `.turnCompleted` followed by `.exited` finalizes once.

### 4.26 `.exited(error:)` — 2166-2182
```
flushDeltas()
if isStartingSession { break }
releaseSession(stop: false)
approvals.removeAll()
questions.removeAll()
stopNativeHeads()
if currentTurnID != nil {
    let name = thread?.provider.displayName ?? "The agent"
    appendNotice(.error, error ?? "\(name) stopped unexpectedly.")
    let turnID = currentTurnID
    Task { await finishTurn(status: .failed, turnID: turnID) }
}
```
- Exact fallback strings: `"The agent"` and `"\(name) stopped unexpectedly."`.
- `releaseSession(stop: false)` — the process is already gone, so it is not stopped again.
- **The `isStartingSession` bail-out is CLI-only** (`isStartingSession` is set at 1534, in the CLI branch alone; the API branch at 1430-1492 never sets it). So an API session that exits during `start()` takes the release-and-fail path here *and* is reported by `start()` throwing. (SF — upstream asymmetry; reproduce it.)

### 4.27 `.agentStarted(AgentSpawn)` — 2183-2184 → `hydraAgentStarted` (2452-2474)
- `guard let app, let thread, app.hydraIsOn(thread) else { return }`.
- **Known head** (`hydraNativeHeads[spawn.id]` set): `named = !spawn.description.isEmpty && spawn.description != "Subagent"` (literal `"Subagent"`); if `named` and the task changed → `info.task = spawn.description` and `head.title = "\(info.persona.name) · \(TextCleanup.singleLine(spawn.description, limit: 60))"` (middle dot separator, limit **60**); `if info.toolUseID == nil { info.toolUseID = spawn.toolUseID }`; then `hydraToolHeads[toolUseID] = headID`; and if `spawn.prompt` is present and the head runtime `!hasSentPrompts` → `headRuntime.rehearseBrief(prompt)`.
- **New head**: `guard let head = app.spawnNativeHead(from: threadID, spawn: spawn) else { return }`; `hydraNativeHeads[spawn.id] = head.id`; `hydraToolHeads[toolUseID] = head.id`.
- Idempotent by `spawn.id`.

### 4.28 `.agentEvent(agentID:_:)` — 2185-2186 → `hydraAgentEvent` (2477-2513)
- Late-announcement recovery (2481-2483): if the agent is unknown but `entryIndex[agentID]` is a `.tool` row of kind `.agent`, it synthesizes `AgentSpawn(id: agentID, taskID: nil, toolUseID: agentID, description: call.title, prompt: nil, model: nil, isBackground: false)` and calls `hydraAgentStarted`. (Claude's agent id is the spawning tool call.)
- `guard let headID = hydraNativeHeads[agentID], let headRuntime = app.existingRuntime(for: headID) else { return }`.
- Inner switch — **10 named arms, 20 cases fall to `default`**:
  - `.turnStarted`: if `!headRuntime.isRunning` → `headRuntime.rehearseTurn(nil)` and `app.updateHydraHead(headID) { $0.status = .running; $0.finishedAt = nil }`. **The event is not rehearsed.**
  - `.turnCompleted(let status, _)`: `hydraAgentFinished(agentID, status: status, summary: nil)` — the error payload is dropped.
  - `.toolStarted(let toolID, let call)`: `headRuntime.hydraToolCalls += 1`; `headRuntime.hydraActivity = ToolPresentation.label(for: call)`; `headRuntime.hydraLastEventAt = .now`; `noteHydraEdits(call.edits)`; if `call.kind == .command` → `watchHydraCommand("\(agentID)-\(toolID)", call)` (key format is literally `agentID + "-" + toolID`); then `headRuntime.rehearse(event)`.
  - `.toolUpdated(let toolID, let update)`: `if let edits = update.edits { noteHydraEdits(edits) }`; `if let status = update.status, status != .running { settleHydraCommand("\(agentID)-\(toolID)") }`; `headRuntime.rehearse(event)`.
  - `.usage, .sessionReady, .models, .commands, .title, .assistantMessageID`: `break` — **6 cases silently dropped**.
  - `default`: `headRuntime.rehearse(event)` — the remaining **20** cases: `messageDelta, messageCompleted, reasoningDelta, reasoningCompleted, toolOutput, planDelta, planCompleted, todos, approval, question, requestResolved, diff, notice, usageLimit, modeChanged, exited, agentStarted, agentEvent, agentProgress, agentFinished`.
- `noteHydraEdits` (3317-3322): `guard !edits.isEmpty, let root = workingRoot`; for each edit, `TouchedPaths.relative(edit.path, root: root)` → `hydraTouched.insert(path)`. These land on the **lead's** turn in `finishTurn` (2299-2300).
- **UPSTREAM DEFECT to record (SF/UNRESOLVED):** a nested `.approval` or `.question` is rehearsed onto the head runtime, whose `resolve`/`answer` call `session?.resolveApproval(…)` on a runtime that has no session (a native head has no `ProviderSession` of its own). Such a request can be shown and dismissed locally but never answered back to the provider. Likewise a nested `.exited` runs `releaseSession(stop: false)` on a session-less head runtime and fails that head's rehearsed turn.

### 4.29 `.agentProgress(agentID:summary:lastTool:tokens:toolCalls:)` — 2187-2192
```
guard let headID = hydraNativeHeads[agentID], let headRuntime = app?.runtime(for: headID) else { break }
if let summary, !summary.isEmpty { headRuntime.hydraActivity = summary } else if let lastTool { headRuntime.hydraActivity = lastTool }
if let tokens { headRuntime.hydraTokens = tokens }
if let toolCalls { headRuntime.hydraToolCalls = toolCalls }
headRuntime.hydraLastEventAt = .now
```
- **Precedence:** a non-empty `summary` wins; an empty-string summary falls through to `lastTool`; a nil summary falls through to `lastTool`. `tokens`/`toolCalls` are **assignments, not maxima** (contrast `hydraLiveInfo`, 344-351, which takes `max`).
- Uses `app?.runtime(for:)` (creates if needed), unlike 4.28 which uses `existingRuntime(for:)`.
- These fields live on the runtime, not the thread record, deliberately (comment 329-332).

### 4.30 `.agentFinished(agentID:status:summary:)` — 2193-2194 → `hydraAgentFinished` (2517-2523)
```
guard let app, let headID = hydraNativeHeads[agentID] else { return }
app.finishHydraHead(headID, status: status, summary: summary)
if let headRuntime = app.existingRuntime(for: headID), headRuntime.isRunning {
    headRuntime.rehearse(.turnCompleted(status: status, error: nil))
}
```
- `hydraNativeHeads` is **not** cleared here (only `stopNativeHeads()` at 2560 clears it), so a head can be finished more than once; `finishHydraHead` and `hydraToolFinished`'s `!info.isFinished` guard are what make it idempotent.

---

## 5. Streaming, flushing and timeline mutation

### 5.1 `queueDelta` pacing (2944-2991)
- `pendingDeltas[id, default: PendingDelta(kind: kind, text: "")].text += prose` — **the first delta's `kind` wins for the whole pending batch**; a later delta of a different kind for the same id appends into the earlier kind's buffer.
- `let isHead = thread?.hydra != nil`; if head → `StreamLoad.shared.noteStreaming(self)`.
- Immediate flush if `ContinuousClock.now - lastFlushAt >= (isHead ? StreamLoad.shared.headFlushWindow : Self.flushWindow)`; `flushWindow = .milliseconds(45)` (3032).
- Else schedule: `delay = Self.flushDelay(for: pendingFlushLength(), scrolling: ScrollActivity.shared.isScrolling)`, and `if isHead { delay = max(delay, StreamLoad.shared.headFlushDelayMilliseconds) }`.
- `flushDelay(for:scrolling:)` (3020-3030), exact table:
  - `..<4_096` → **45** ms
  - `..<32_768` → **90** ms
  - default → **150** ms
  - then `scrolling ? max(paced, 160) : paced`.
- `StreamLoad` (3040-3071): `headFlushBudget = 70` ms, `stale = .seconds(2)`, `headFlushDelayMilliseconds = max(45, streamingCount * 70)`.
- `pendingFlushLength()` (2995-3016) skips `.toolOutput` and returns the longest `existing text count + delta count` over `.assistant`, `.reasoning`, `.plan`.

### 5.2 `flushDeltas(scheduleSave:)` (3073-3094)
```
flushTask?.cancel(); flushTask = nil
guard !pendingDeltas.isEmpty else { return }
lastFlushAt = .now
let deltas = pendingDeltas; pendingDeltas.removeAll()
for (id, delta) in deltas {
    guard let entry = entryIndex[id], entry.item.content.appendStreamed(delta.text, kind: delta.kind) else { continue }
    saveRevision += 1
    if delta.kind != .plan { heard = true }
    if delta.kind == .message, entry.kind == .assistant, !streamedRows.contains(id) { streamedRows.append(id) }
}
if heard { noteHydraHeadEvent() }
if let turnID = currentTurnID { for id in streamedRows { scanStreamedDelegations(entryID: id, turnID: turnID) } }
if scheduleSave { self.scheduleSave() }
```
- `appendStreamed` (3549-3571) matches 4 `(kind, content)` pairs and returns `false` for every other combination (the delta is then silently dropped). The `self = .todos([])` line in each arm is the uniqueness trick that keeps the append amortized O(delta); it is not observable state.
- **Dictionary iteration order is unspecified**, so multiple rows flushing in one pass have no defined relative order. (ED for the port: iterate deterministically; behaviour is unchanged because each id writes its own row.)

### 5.3 `append` / `remove` / `endStreaming`
- `append` (2914-2920): `for entry in entries.suffix(6) { endStreaming(entry) }` — **only the last 6 rows** are closed; then push, index, `saveRevision += 1`. No `scheduleSave`.
- `endStreaming` (2928-2942): clears `AssistantMessage.isStreaming`, `ReasoningBlock.isStreaming`, and promotes `ProposedPlan.state .drafting → .proposed`.
- `remove` (2922-2926): drop from `entries` and `entryIndex`, `saveRevision += 1`.

---

## 6. `finishTurn(status:turnID:)` (2201-2387) — the full sequence

1. `interruptWatchdog?.cancel(); interruptWatchdog = nil; headBudget?.cancel(); headBudget = nil`
2. `guard let turnID = explicitTurnID ?? currentTurnID, let turn = turns.first(where: { $0.id == turnID })` — else `phase = .idle; turnStartedAt = nil; drainPendingSend(); return`
3. `guard finishingTurnID != turnID else { return }`; `finishingTurnID = turnID`; `defer { finishingTurnID = nil }` — **claimed before the first suspension**.
4. `for id in Array(commandTrees.keys) { settleCommand(id) }`; `for key in Array(hydraCommandTrees.keys) { settleHydraCommand(key) }`
5. `guard currentTurnID == nil || currentTurnID == turnID else { return }`
6. `closingTurnID = turnID`; `defer { closingTurnID = nil }`
7. `settles = Array(commandSettles.values); commandSettles.removeAll()`; if `status == .completed` → await all; else race them against `try? await Task.sleep(for: .seconds(3))` in a task group and `cancelAll()`. **3-second grace for stopped/failed turns.**
8. `currentTurnID = nil`; `flushDeltas()`; `approvals.removeAll()`; `questions.removeAll()`
9. one pass over `entries where entry.item.turnID == turnID`: `endStreaming(entry)`; for `.tool` rows still `.running` → `call.finish(status == .completed ? .completed : .failed)`, `saveRevision += 1`; collect `reportedEdits`.
10. take and clear `currentProviderDiff` / `currentProviderAnchor`; `updateTurn(turnID) { $0.status = status; $0.completedAt = .now; if let providerDiff {...}; if let providerAnchor {...} }`
11. `if let providerAnchor, thread?.providerResumeAt != nil { app?.updateThread(threadID) { $0.providerResumeAt = providerAnchor } }` — **only updates an anchor that already exists; it never creates one.**
12. `providerPatch = providerDiff ?? turns.first(where: { $0.id == turnID })?.providerDiff ?? ""`; `providerFiles = providerPatch.isEmpty ? [] : await Self.parseDiff(providerPatch)`
13. end checkpoint, only when `turn.baseCheckpoint != nil`: `ref = Git.checkpointRef(thread: threadID, turn: turn.index, phase: "end")`, `if (try? await git.captureCheckpoint(ref)) != nil { endCheckpoint = ref }`
14. `touched` = reported edits mapped through `TouchedPaths.relative(edit.path, root:)` (paths outside the checkout dropped), **union `hydraTouched`**, then `hydraTouched.removeAll()`; `updateTurn(turnID) { if let endCheckpoint {...}; $0.touchedPaths = touched.sorted() }` — one write, deliberately.
15. `diffRevision += 1` (which resets `revisionDiffs = RecentCache(limit: 4)` and `changeStatsRevision = nil`, 196-201)
16. `changes = FileChangeSummary(files: await parsedDiff(selection: turnID, providerFiles: providerFiles))`
17. `summary = TurnSummary(turnID:, status:, duration: Date.now.timeIntervalSince(turn.startedAt), filesChanged: changes.files.count, additions: changes.additions, deletions: changes.deletions, changes: changes)`; `append(TimelineItem(turnID: turnID, content: .turnEnd(summary)))`
18. `phase = .idle`; `turnStartedAt = nil`; `saveNow()`
19. `settleForegroundHeads(after: status)` (2542-2548): every non-background, unfinished native head is finished with `status == .completed ? .completed : .interrupted`.
20. `guard thread?.isArchived != true else { headBudgetSpent = false; app?.turnFinished(threadID, status: status, continues: false); return }`
21. **continuation decision** (2339-2353), in this exact order:
    - `if pendingSend != nil` → `continues = drainPendingSend()`
    - `else if status == .completed, case let delegation = spawnDelegatedHeads(for: turnID), delegation != .none` → `continues = true`; and `if delegation == .headsOut { _ = drainFollowUps(after: status) }`
    - `else if status == .completed, flushHydraReports()` → `continues = true`
    - `else` → `if status != .completed { dropDelegationBlock(for: turnID, status: status) }`; `continues = drainFollowUps(after: status)`
22. `if !continues, status != .interrupted, flushHydraReports() { continues = true }`
23. `if !continues, status == .completed, let app, app.runningDroppyHeads(of: threadID) > 0 { continues = true }`
24. head budget: `if headBudgetSpent { headBudgetSpent = false; if status == .interrupted, thread?.hydra?.kind == .droppy { headReportsNext = true; autoStartedTurnToken &+= 1; Task { await ensureLoaded(); await startTurn(text: HydraBudget.finalNote, attachments: []) }; continues = true } }`
    - `HydraBudget.finalNote` verbatim (`Hydra.swift:200`): `"[Droppy Code] Your budget is spent and your tools are gone. Reply now with your report: what you changed, how far it got, and what is left."`
25. `app?.turnFinished(threadID, status: status, continues: continues)` — **the only notification/sound trigger.** In `AppModel.turnFinished` (AppModel.swift:1173-1216): sets `lastStatus`, `updatedAt`, and `hasUnread = !isVisible` for non-heads; `updateDockBadge()`; releases a deferred session; `if autoContinue.turnFinished(...) { return }`; heads return after `hydraHeadTurnFinished`; `guard !continues else { return }`; `chimed = settings.chimeWhenFinished && status != .interrupted` → `FinishChime.play()`; then `guard !isVisible, settings.notifyWhenFinished` → `notify(threadID:title:body:sound: chimed ? nil : .default)` with body strings exactly `"Finished."`, `"Stopped."`, `"Stopped with an error."`, `""`.
26. auto-merge (2381-2386): `if !continues, status == .completed, hydraPendingReports.isEmpty, hydraBatches.isEmpty, hydraWaiting.isEmpty, let app, let thread, app.hydraIsOn(thread), app.settings.hydraAutoMerge, app.runningHydraHeads(of: threadID) == 0 { Task { await app.autoMergeHydraWork(of: threadID) } }` — **9 conjuncts, all required.**

---

## 7. Numeric and string constants (exact)

| Constant | Value | Site |
| --- | --- | --- |
| `flushWindow` | `.milliseconds(45)` | 3032 |
| flush delay tiers | 45 / 90 / 150 ms at `<4_096` / `<32_768` / else | 3020-3025 |
| scrolling floor | `max(paced, 160)` ms | 3029 |
| `StreamLoad.headFlushBudget` | `70` ms | 3044 |
| `StreamLoad.stale` | `.seconds(2)` | 3045 |
| `headFlushDelayMilliseconds` | `max(45, streamingCount * 70)` | 3065 |
| interrupt watchdog | `.seconds(8)` | 1678 |
| take-back poll | `200 × .milliseconds(50)` | 656 |
| finishTurn diff grace (non-completed) | `.seconds(3)` | 2236 |
| idle save debounce | `.milliseconds(800)` | 3466 |
| running save period | `.seconds(5)` | 3478 |
| `editDiffLimit` | `200_000` UTF-8 bytes | 3366 |
| `ToolCall.outputLimit` | `60_000` UTF-8 bytes, trimmed to `"…" + suffix(30_000)` | Timeline.swift:137, 164-167 |
| `maxDelegatedTasks` | `32` | 2658 |
| `maxRefusedBlocks` | `3` | 2664 |
| `HydraPrompts.maxDelegationRounds` | `3` | Hydra.swift:847 |
| `HydraPrompts.delegationSentHold` | `.seconds(3.2)` | Hydra.swift:1084 |
| `HydraPrompts.delegationLeaveFade` | `.milliseconds(450)` | Hydra.swift:1088 |
| `HydraBudget.maxSeconds` | `35 * 60` = 2100 s | Hydra.swift:194 |
| `HydraBudget.reportSeconds` | `3 * 60` = 180 s | Hydra.swift:196 |
| API-provider head budget bonus | `+ 90` s | 1206 |
| `hydraMerges` cap | `20` (`removeFirst(count - 20)`) | 2430 |
| `apiTranscript` message cap | `suffix(40)` | 1636 |
| `apiTranscript` char budget | `60_000`, truncation floor `budget > 200`, prefix `"…"` | 1637-1648 |
| `turnDiffs` cache | `RecentCache(limit: 64)` | 1920 |
| `revisionDiffs` cache | `RecentCache(limit: 4)` | 1921, 198 |
| `append` closes streaming on | `entries.suffix(6)` | 2915 |
| title single-line limit (thread naming) | `48` | 3448 |
| head title task limit | `60` | 2460 |
| `Git.checkpointRef` | `"refs/droppy-code/checkpoints/\(thread.uuidString.lowercased())/\(turn)-\(phase)"` with phases `"start"` / `"end"` | Git.swift:409-411 |

User-visible strings emitted from this file (verbatim, in line order):

1. `"The stopped message was kept: \(error.localizedDescription)"` — 698
2. `"\(members.count) messages sent together. The pictures and files a message came with are named under it, the pictures numbered in the order they are attached."` — 910
3. `"Message \(number):"`, `"Attached to message \(number): " + named.joined(separator: ", ") + "."`, `"file \(attachment.path)"`, `"image \(imageNumber) (\(attachment.name))"` — 898-906
4. `"Implement the plan."` — 1129
5. `"\(thread.provider.displayName) no longer has this conversation. It starts a new one here, without the messages above."` — 1557
6. `"The agent session is not running."` / `"\(displayName) is not installed. Install it and sign in with `\(loginCommand)`."` / `"\(displayName) needs an API key. Add one in Settings → Providers → \(displayName)."` — ProviderSession.swift:160-167
7. `"No file checkpoint is available for this message. You can still keep the file changes."` — 1771
8. `"Wait for the running turn to finish first."` — 1802
9. `"This message is no longer in the thread."` — 1805
10. `"Stop the running agents before reverting this conversation."` — 1808
11. `"This turn has no file checkpoint. Choose to keep the file changes instead."` — 1817
12. `"The original Codex conversation is unavailable."` — 1826
13. `"The selected message has no Codex turn ID, so its history cannot be safely reverted."` — 1831
14. `"The original Copilot conversation is unavailable."` — 1837
15. `"Claude has no resume point before this message. The conversation was kept."` — 1851
16. `"Conversation reverted, but files could not be restored: \(error.localizedDescription)"` — 1875
17. `"Nothing to compact yet."` — 1705
18. `"Compacting context."` — 1708
19. `"\(name) stopped unexpectedly."` with `name` defaulting to `"The agent"` — 2178-2179 (`name` at 2178, notice at 2179)
20. `"Sent out heads."` — 576, 2827
21. `"Asked for heads."` — 2684, 2696
22. `"Asking for heads."` — 2759
23. `"Done, with no heads."` — 2769
24. `"Asking for more heads."` — 2779
25. `"No heads went out: the lead did this itself."` — 2773
26. `"No heads went out\n\(dropped) The block was dropped from the reply."` — 2842
27. `"Its delegation block could not be read, and it had been told so already."` — 2763
28. `"This request has had all \(HydraPrompts.maxDelegationRounds) of its rounds of heads, and the lead had been told so already."` — 2783
29. `"Hydra sent \(stream.sent == 1 ? "one head" : "\(stream.sent) heads") before the turn \(status == .interrupted ? "was stopped" : "failed"); the rest of the block never went out. Ask again to send them."` — 2689
30. `"Hydra sent no heads: the turn \(status == .interrupted ? "was stopped" : "failed") before they could go out. Ask again to send them."` — 2700
31. multi-line note, 2746-2749 and 2798-2801 (two copies, different counts; the literal text begins on 2747 and 2799):
    ```
    Not every task went out
    \(sent) of the \(total) tasks in that block went to heads. The last \(total - sent) did not: ask for them again once these report back.
    ```
32. `"Hydra: \(note)"` — 2887
33. `"Tool"` (fallback tool title) — 3192
34. `"Subagent"` (the description treated as unnamed) — 2455

---

## 8. Persistence

- `scheduleSave()` (3461-3482): `guard persistenceEnabled`; when `phase == .idle` it cancels and re-arms an **800 ms** task; while a turn runs it coalesces into a single **5 s** task (`guard saveTask == nil else { return }`).
- `saveNow()` (3484-3500): cancels and clears `saveTask` **first**; `guard persistenceEnabled, !isLoadingHistory`; `guard saveRevision != lastSavedRevision`; `lastSavedRevision = saveRevision`; `DiskWriter.shared.encodeAndWrite(snapshotForPersistence(), to: Storage.threadURL(threadID))`.
- `snapshotForPersistence()` (3530-3540): `flushDeltas(scheduleSave: false)` first, then `items = entries.map(\.item)`, `turns`, `usage`, `continuation`, `followUps`, `hydraMerges`. **`ThreadDocument` has 7 stored properties; `tokenSpends` rides inside `turns`, always nil.**
- Load/install (`install(_:)`, 551-612): running tools are failed, streaming flags cleared, empty reasoning dropped (`!$0.isEmptyReasoning`), every item passed through `cleanedOfEmDashes` (assistant / reasoning / plan only — `Timeline.swift:38-54`), sent delegation cards stripped, running turns set to `.interrupted`, and a synthetic `.turnEnd` written for any turn that has no end marker with `filesChanged: turns[index].touchedPaths?.count ?? 0, additions: 0, deletions: 0, changes: nil`.

---

## 9. Ordering and idempotency rules the port must honour

1. `flushDeltas()` runs **before** every completion/update handler (`messageCompleted`, `reasoningCompleted`, `toolStarted`, `toolUpdated`, `planCompleted`, `turnCompleted`, `exited`). Reordering loses text.
2. `finishingTurnID` is claimed **before** the first `await` in `finishTurn`; `.turnCompleted` + `.exited` for one turn finalize once.
3. `closingTurnID` keeps late rows on the closing turn; `.diff` and `.assistantMessageID` are the two cases that use `currentTurnID` instead and are therefore dropped in that window.
4. `sessionEpoch` is bumped in both `observe` and `releaseSession`; a released session's events are dropped by the closure's own guard, and `onEvent = nil` happens one runloop turn later.
5. `unsentSessionID` is consumed only after a successful `send`; `releaseSession` discards it.
6. `antiSlopFallbackSent` / `continuationSent` are committed only when `sessionEpoch` still matches the value captured before `send`.
7. Approvals and questions de-dupe by id then append (order changes on re-send).
8. Edits count as "landed" exactly once per tool row (`existing.edits.isEmpty` test in both `upsertTool` and `applyToolUpdate`).
9. `ToolCall.finish` stamps `finishedAt` once; a second terminal status changes `status` but not the timestamp.
10. `hydraNativeHeads` is never pruned on `agentFinished`; only `stopNativeHeads()` clears it.
11. `.todos` writes are idempotent per turn through `todosEntryID`, but the cache is only valid while `entry.item.turnID == currentTurnID`.
12. `usage` mutates `saveRevision` without scheduling a save; it reaches disk on the next scheduled or forced save.
