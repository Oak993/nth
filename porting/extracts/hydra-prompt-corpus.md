# Hydra prompt corpus, verbatim

**Target:** `E:/Droppy/reference/droppy-code/DroppyCode/Core/Models/Hydra.swift` (1345 lines),
`HydraDelegationStream.swift` (226 lines), `HydraCookbook.swift` (52 lines).
Pinned reference commit `dd2b86efef9e77168f304a86e8a8411877578bbe`, read-only.
All three files read completely in bounded ranges (1-240, 240-520, 520-820, 820-1120, 1120-1345;
1-226; 1-52), reconciled against `wc -l`.

Every claim below is labelled **SOURCE FACT**, **ENGINEERING DECISION** or **UNRESOLVED**.
Strings are reproduced after Swift multi-line-literal indentation stripping (the closing `"""`
sits at the same indent as the content, so the leading indent shown in the file is not part of
the emitted string). Swift interpolations are shown as `{{name}}` placeholders where the value is
computed, and the computation is given exactly alongside.

---

## 1. Budget and pacing constants

**SOURCE FACT.** `enum HydraBudget`, Hydra.swift:186-206. Exactly 6 numeric constants and 3 note
strings.

| Constant | Value | Literal form in source |
|---|---|---|
| `pacingTools` | 24 | `static let pacingTools = 24` |
| `wrapUpTools` | 120 | `static let wrapUpTools = 120` |
| `maxTools` | 160 | `static let maxTools = 160` |
| `wrapUpSeconds` | 1500 s | `static let wrapUpSeconds: TimeInterval = 25 * 60` |
| `maxSeconds` | 2100 s | `static let maxSeconds: TimeInterval = 35 * 60` |
| `reportSeconds` | 180 s | `static let reportSeconds: TimeInterval = 3 * 60` |

Related constants elsewhere in the same file:

| Constant | Value | Location |
|---|---|---|
| `HydraLaunch.uncappedConcurrency` | `8` | Hydra.swift:170 |
| `HydraPair.maxHeadsRange` | `1...8` | Hydra.swift:73 |
| `HydraPrompts.maxDelegationRounds` | `3` | Hydra.swift:845 |
| `HydraPrompts.delegationSentHold` | `.seconds(3.2)` | Hydra.swift:1073 |
| `HydraPrompts.delegationLeaveFade` | `.milliseconds(450)` | Hydra.swift:1077 |
| landing-line file cap | `prefix(25)` | Hydra.swift:1304 |
| queued-prompt quote limits | `1_200` (user prompt), `800` (lead reply) | Hydra.swift:1189, 1192 |
| queued-prompt touched-path cap | `prefix(20)` | Hydra.swift:1195 |
| auto-title fallback limit | `TextCleanup.singleLine(prompt, limit: 60)` | HydraDelegationStream.swift:116 |

### 1.1 The three budget notes, verbatim

`pacingNote` (Hydra.swift:204). The constant 24 is interpolated, so the emitted text reads
"run 24 tools":

```
[Droppy Code] You have run 24 tools without changing a file. If the task is research, reply with your findings now. Otherwise act on what you know: make the change, or reply with what blocks you.
```

Source form: `"[Droppy Code] You have run \(pacingTools) tools without changing a file. If the task is research, reply with your findings now. Otherwise act on what you know: make the change, or reply with what blocks you."`

`wrapUpNote` (Hydra.swift:205):

```
[Droppy Code] Your budget is nearly spent. Finish now: complete the smallest correct version of the task, then reply with your report.
```

`finalNote` (Hydra.swift:206):

```
[Droppy Code] Your budget is spent and your tools are gone. Reply now with your report: what you changed, how far it got, and what is left.
```

### 1.2 Where the budget is enforced (two independent enforcers)

**SOURCE FACT.** In-turn pacing, API / OpenAI-compatible providers only
(`OpenAICompatibleSession.swift`, every branch guarded by `configuration.isHydraHead`):

- `var pacingAt = HydraBudget.pacingTools` (line 305); reset to `HydraBudget.pacingTools` on a
  `write_file` or `edit_file` tool call (lines 388-391), which also zeroes `toolsSinceChange`.
  Every other tool increments `toolsSinceChange`.
- Wrap-up (line 329): fires once (`!wrapUpAsked`) when
  `toolsUsed >= HydraBudget.wrapUpTools || Date.now.timeIntervalSince(turnStartedAt) >= HydraBudget.wrapUpSeconds`;
  appends `["role": "user", "content": .string(HydraBudget.wrapUpNote)]`.
- Pacing (line 336): fires when `toolsSinceChange >= pacingAt`; appends
  `["role": "user", "content": .string(HydraBudget.pacingNote)]`, then
  `pacingAt = toolsSinceChange + HydraBudget.pacingTools`.
- Budget spent (line 317): `toolsUsed >= HydraBudget.maxTools || elapsed >= HydraBudget.maxSeconds`;
  appends `["role": "user", "content": .string(HydraBudget.finalNote)]` and runs one final round
  **with tools disabled** (`streamOneRound(model:effort:withTools: false)`), then breaks.

**Ordering dependency (SOURCE FACT):** within one `while` iteration the checks run in this order:
round ceiling (`rounds < Self.maxRoundsPerTurn`) → budget-spent / `input.isFinalReport` → wrap-up →
pacing → `condenseHistory()` → stream. On the round where the budget is spent, the wrap-up and
pacing notes are therefore never appended.

**SOURCE FACT.** Out-of-turn backstop, every provider (`ThreadRuntime.swift:1200-1215`), applied
only when `initialThread.hydra?.kind == .droppy`:

```swift
let allowance = isFinalReport
    ? HydraBudget.reportSeconds
    : HydraBudget.maxSeconds + (initialThread.provider.isAPIKeyBased ? 90 : 0)
```

180 s for the report turn; 2100 s for a CLI head; **2190 s** for an API-key-based head (the +90 s
gives the in-turn enforcer room to stop itself first). On expiry it sets `headBudgetSpent = true`
and calls `interrupt()`.

**SOURCE FACT.** After that interrupt, `ThreadRuntime.swift:2363-2372`: if `headBudgetSpent` and the
turn ended `.interrupted` and `thread?.hydra?.kind == .droppy`, the runtime starts a new turn whose
**user text is `HydraBudget.finalNote` itself**:
`Task { await ensureLoaded(); await startTurn(text: HydraBudget.finalNote, attachments: []) }`,
with `headReportsNext = true`. So `finalNote` is injected two different ways: as an appended history
message inside an API turn, and as the entire prompt of the follow-up report turn on any provider.

---

## 2. The head-name roster, in order

**SOURCE FACT.** `enum HydraRoster.personas`, Hydra.swift:222-257. **Exactly 25 entries**, counted.
Order is the dispatch order; index 0 goes out first.

| # (0-based) | Name | Asset | Hex |
|---|---|---|---|
| 0 | Hank | `hydra-head-01` | `0xFF8A3D` |
| 1 | Walter | `hydra-head-02` | `0x3D8BFF` |
| 2 | Ada | `hydra-head-03` | `0x34C46A` |
| 3 | Otto | `hydra-head-04` | `0xA35BE0` |
| 4 | Nova | `hydra-head-05` | `0xF25C9A` |
| 5 | Remy | `hydra-head-06` | `0x2BB5B0` |
| 6 | Iris | `hydra-head-07` | `0xE8B430` |
| 7 | Milo | `hydra-head-08` | `0xE84F4F` |
| 8 | Juno | `hydra-head-09` | `0x6A5CFF` |
| 9 | Ezra | `hydra-head-10` | `0x4FD1A1` |
| 10 | Lena | `hydra-head-11` | `0x2FB8E6` |
| 11 | Bo | `hydra-head-12` | `0xB0784A` |
| 12 | Kai | `hydra-head-13` | `0x8FD14F` |
| 13 | Vera | `hydra-head-14` | `0xD946EF` |
| 14 | Finn | `hydra-head-15` | `0xF97316` |
| 15 | Mira | `hydra-head-16` | `0xC084FC` |
| 16 | Odin | `hydra-head-17` | `0xB45309` |
| 17 | Suki | `hydra-head-18` | `0xF472B6` |
| 18 | Rex | `hydra-head-19` | `0x2563EB` |
| 19 | Zola | `hydra-head-20` | `0x65A30D` |
| 20 | Pip | `hydra-head-21` | `0x94A3B8` |
| 21 | Ivo | `hydra-head-22` | `0x0D9488` |
| 22 | Lux | `hydra-head-23` | `0xFACC15` |
| 23 | Tova | `hydra-head-24` | `0x059669` |
| 24 | Gus | `hydra-head-25` | `0x7DD3FC` |

The roster order also appears **twice as literal prose inside prompts** (§4 native policy and §5
fallback policy), written out as this comma-separated list, identical in both places:

```
Hank, Walter, Ada, Otto, Nova, Remy, Iris, Milo, Juno, Ezra, Lena, Bo, Kai, Vera, Finn, Mira, Odin, Suki, Rex, Zola, Pip, Ivo, Lux, Tova, Gus, then Hank 2 and so on
```

Both copies are followed by an explicit anti-hallucination clause naming the same example pair,
"Ives" / "Ivo" (exact wording differs between the two; see §4 and §5).

### 2.1 Roster arithmetic, exactly

**SOURCE FACT.** `persona(at index: Int)`, Hydra.swift:259-265:

```swift
let count = personas.count                        // 25
let base = personas[((index % count) + count) % count]
let round = max(0, index) / count
guard round > 0 else { return base }
return HydraPersona("\(base.name) \(round + 1)", base.asset, base.hex)
```

Index 25 is `Hank 2`; index 49 is `Gus 2`; index 50 is `Hank 3`. Colour and asset stay the base
persona's; only the name gains the ` N` suffix. Negative indices wrap through the double-modulo but
`round` is clamped by `max(0, index)`, so a negative index yields an unsuffixed base name.

**SOURCE FACT.** `index(named:)`, Hydra.swift:273-281: trims whitespace; if the substring after the
**last** space parses as `Int`, that token is dropped ("Otto 2" → "Otto"); then matches
case-insensitively (`compare(bare, options: .caseInsensitive) == .orderedSame`) against roster
names. Returns nil for anything off-roster.

**SOURCE FACT.** `persona(named:)`, Hydra.swift:285-293: resolves the index, then if a trailing
integer `round > 1` is present returns `persona(at: index + (round - 1) * personas.count)`.
"Nova 3" → index 4 + 2×25 = 54.

**SOURCE FACT.** `HydraPersona.color` converts the hex as
`red: ((hex >> 16) & 0xFF)/255, green: ((hex >> 8) & 0xFF)/255, blue: (hex & 0xFF)/255`
(Hydra.swift:214-220). Assets are drawn as template images so they take the head's colour.

---

## 3. Shared prompt fragments (private constants, composed into several prompts)

These are the building blocks; each is quoted verbatim once here and referred to by name later.

### 3.1 `howToWork` — Hydra.swift:614-620

Injected into `delegatedHeadPrompt` and `queuedHeadPrompt` under the heading `## How to work`.
Five lines, each beginning `- `, newline-separated, no trailing newline.

```
- Start on the task right away: read what you need and no more, then make the change.
- A brief that names the file, the line and the change needs no second reading: make that edit first, then the next. Think briefly between tool calls; if you find yourself weighing designs for minutes, take the simplest reading of the brief and do it, and mention the choice in your report.
- Do exactly this task and only this. Do not widen it, do not touch files it does not name unless it cannot be done otherwise, and never revert, reformat or clean up work that is not yours.
- Never run a build, a test, a lint, a type check or any other check of your work, and never wait on one: the lead runs every check itself, in its own checkout. Make the edits your brief names and report.
- If something blocks you, stop and say so instead of guessing or working around it.
```

### 3.2 `howToReport` — Hydra.swift:622

Injected under `## Your report` in both Droppy-run head prompts, and appended to `workerPrompt`
after a blank line.

```
Report the result, the files you changed and material blockers once, in a few lines the lead can act on: what changed, where, and anything still open or uncertain. Name the caveats and the unfinished work plainly rather than leaving them out. No preamble, no restating the brief, no logs. The lead handles validation.
```

### 3.3 `briefRule` — Hydra.swift:627

```
Write each brief as numbered, executable steps naming the file, symbol or line anchor, and exact change. Resolve design choices first; ask no questions and offer no alternatives. Give enough context to begin editing within a few tool calls.
```

### 3.4 `verificationRule` — Hydra.swift:631

```
Heads edit or research and report; they never build, test, lint or verify. Do not assign checks or request pass reports. Run required validation yourself in the checkout after the heads return.
```

### 3.5 `autoMergeRule` — Hydra.swift:648

```
Droppy Code handles branches, commits and merge requests after all heads return and you finish. Never run or delegate those operations. For merge/status questions, use only the current [Hydra] note: if merged, give its link; if files remain, say they land when your answer ends; if merging failed or files are outside every project, give the stated reason and required user action. If nothing is pending, promise no merge. Never claim a merge without the note confirming it.
```

### 3.6 `reviewRule` — Hydra.swift:653

```
Read every changed file named in the reports and its affected callers. Correct integration issues yourself, run the narrowest required build in the checkout, and disclose your corrections. Never delegate this review.
```

### 3.7 `reportStyleRule` — Hydra.swift:658

```
For each head, give one plain sentence starting with its name and describing the user-visible result; add the file details only when useful, and keep the whole account bounded to a line or two per head. Omit repeated summaries, audit jargon and decorative symbols. Say plainly what you still need from the user and what you settled or fixed yourself.
```

### 3.8 `projectRule(_ projects: [HydraProjectRef])` — Hydra.swift:636-641

The computation, exactly:

```swift
let own = projects.first.map { "this chat's project, \($0.name) (`\($0.path)`)" } ?? "this chat's project"
let others = projects.dropFirst()
let named = others.isEmpty ? "" : " The other projects in the sidebar: " + others.map { "\($0.name) (`\($0.path)`)" }.joined(separator: ", ") + "."
```

Emitted text:

```
Heads may work in any project. A head works in {{own}} unless it is sent to another: an entry of the delegation block names it with "project" (a sidebar project's name or path, or the absolute path of any git repository on this Mac, which Droppy Code then adds to the sidebar), and a head spawned with your own agent tool is told the project's folder in its prompt.{{named}} Whatever a head changes lands in the checkout of the project it worked in, and the team's work is merged per project, one merge request each. Your own tools run in this chat's checkout, and you may read and edit files in any project all the same: what you change elsewhere is merged along with that project's work. Run your own checks in the checkout itself: never make a worktree or a copy of the project for them.
```

`{{own}}` is `this chat's project, NAME (\`PATH\`)` when `projects` is non-empty, else the bare
string `this chat's project`. `{{named}}` is the empty string for 0 or 1 projects; otherwise it is
` The other projects in the sidebar: NAME (\`PATH\`), NAME (\`PATH\`).` — leading space, trailing
period, entries joined with `, `, paths in single backticks.

**SOURCE FACT (upstream string, not a port decision):** "on this Mac" is hard-coded here and in
`workplaceRules`/`HydraMentionTarget` commentary. A parity port reproduces the string as written
unless the owner rules otherwise; changing it changes model-visible text.

### 3.9 `workplaceRules(_ workplace: Workplace)` — Hydra.swift:594-609

Two cases of `enum Workplace { case ownCopy(path: String); case shared(path: String) }`. Injected
under `## Where you work` in both Droppy-run head prompts.

`.ownCopy(let path)` — two lines separated by a single newline:

```
You have your own copy of the project at {{path}}: a git worktree Droppy Code made for you from the project's checkout as it was when you were sent out, uncommitted work included. Work in it directly, on the files as they are; your tools already run there. When you report, Droppy Code carries your changes into that checkout itself. So never commit, branch, stash, push, check out, reset, restore or clean anything, and never make or remove worktrees, whatever the project's own guidelines say about agents and worktrees: this copy already is yours. Do not use git to check your work either.
Never build, test or verify anything in your copy: the lead runs every check itself, in its own checkout. Anything under the project folder that git does not ignore lands in the lead's checkout, so leave no build output there.
```

`.shared(let path)` — one line:

```
You work in the lead's checkout at {{path}}, alongside the lead and the other heads, who are changing other files at the same time. Changes you did not make are expected there: leave every one of them alone, never stash, check out, reset, restore or clean anything, and never move work into a branch or worktree, whatever the project's own guidelines say about agents and worktrees. Do not use git status or git diff to check your work: they show everyone's changes, and the lead does the verifying.
```

**Which case applies (SOURCE FACT, `AppModel+Hydra.swift:465-472`):** the default is
`.shared(path: checkout)`; it becomes `.ownCopy(path: copy.path)` only when
`settings.hydraIsolateHeads` is on **and** `makeHydraCopy` returned non-nil (it returns nil where
the project has no git or no commits, or git refuses).

---

## 4. Native-provider lead policy — `HydraPrompts.policy(for:_:)`

**SOURCE FACT.** Hydra.swift:662-740. Injected as the Hydra half of the appended system prompt on
the three native providers:

| Provider | Injection site | Mechanism |
|---|---|---|
| Claude | `ClaudeSession.swift:109` | joined with `AntiSlopPolicy.instructions(...)` into one `--append-system-prompt` argument |
| Codex | `CodexSession.swift:234` | `params["developerInstructions"]`, later combined with anti-slop |
| Copilot | `CopilotSession.swift:140` | `params["systemMessage"] = ["mode": "append", "content": ...]` |

Only reached when `hydra.runsNatively` is true. `HydraLaunch.runsNatively` is true only when the
heads run on the lead's own provider **and** that provider is one of Claude, Codex, Copilot
(`AppModel.hydraIsNative(_:)`, AppModel+Hydra.swift:100-102 — exactly 3 elements:
`.claude`, `.codex`, `.copilot`).

### 4.1 The provider-varying pieces

`profileRule` (empty string unless `nativeRoutableProfiles(of: launch)` is non-empty).
`nativeRoutableProfiles` (Hydra.swift:558-563) keeps a profile only when
`($0.provider == nil || $0.provider == launch.headsProvider) && ($0.model != nil || $0.effort != nil)`.

- **Claude and Copilot** (identical text, built from `"`\(workerAgentName(for: $0))` for \($0.name)"`
  joined with `, `):

```
 Some heads are tuned for a purpose: route the task to `droppy-worker-deep` for deep, `droppy-worker-quick` for quick instead of the plain `droppy-worker` when the work belongs to that purpose.
```

  (leading space is part of the string; agent names shown for two example profiles named
  "deep" and "quick")

- **Codex** (each route is `"\(profile.name) (pass \(parts.joined(separator: " and ")))"` where
  `parts` is `` "`model` = X" `` when the profile sets a model and `` "`reasoning_effort` = Y" ``
  when it sets an effort, in that order; routes joined with `, `):

```
 Some heads are tuned for a purpose: route the task by passing the profile's own knobs in `spawn_agent` — deep (pass `model` = claude-opus-5 and `reasoning_effort` = high) — when the work belongs to that purpose.
```

  Note the em dash characters around the route list are literal in the source (` — ` … ` — `).

- **Any other provider:** `profileRule = ""`.

`howToSpawn`, by provider (`default` is the empty string):

Claude:
```
Two agent types are yours: `droppy-worker` (edits files, runs commands) and `droppy-scout` (read-only research). Spawn them with the Agent tool and prefer them over other agents while Hydra is on: they run on the model and effort the user chose for heads.{{profileRule}}
```

Codex:
```
Spawn heads with `spawn_agent`: the `worker` agent for anything that edits files or runs commands, the `explorer` agent for read-only research.{{profileRule}}
```

Copilot:
```
Two agents are yours: `droppy-worker` (edits files, runs commands) and `droppy-scout` (read-only research). Start them with the task tool and prefer them over other agents while Hydra is on: they run on the model and effort the user chose for heads.{{profileRule}}
```

`howToWait`, by provider (`N` = `launch.maxHeads`):

Claude — `"Launch every head for a job in one message so they run in parallel, in the foreground" + (maxHeads.map { ", and run at most \($0) at once." } ?? ".")`
```
Launch every head for a job in one message so they run in parallel, in the foreground, and run at most 6 at once.
```
with no cap:
```
Launch every head for a job in one message so they run in parallel, in the foreground.
```

Codex — `"Spawn every head for a job before waiting, so they run in parallel, and collect them with `wait_agent`; never leave a head running when you answer." + (maxHeads.map { " Run at most \($0) at once." } ?? "")`
```
Spawn every head for a job before waiting, so they run in parallel, and collect them with `wait_agent`; never leave a head running when you answer. Run at most 6 at once.
```

Copilot — `"Start every head for a job at once so they run in parallel" + (maxHeads.map { ", and run at most \($0) at a time." } ?? ".")`
```
Start every head for a job at once so they run in parallel, and run at most 6 at a time.
```

Default (unreachable through the three native call sites, but present in the source):
`maxHeads.map { "Run at most \($0) heads at once." } ?? "Send every head for a job out at once so they run in parallel."`

### 4.2 The policy body, verbatim

Placeholders: `{{howToSpawn}}`, `{{howToWait}}`, `{{briefRule}}` (§3.3), `{{verificationRule}}`
(§3.4), `{{projectRule}}` (§3.8), `{{reviewRule}}` (§3.6), `{{autoMergeRule}}` (§3.5),
`{{reportStyleRule}}` (§3.7). Whitespace and blank lines are significant.

```
# Hydra

The user switched on Hydra for this chat: you lead a team of helper agents, called heads, that work in parallel in your checkout. Switching Hydra on is the user asking you to use them, so it overrides any standing rule that says not to spawn agents unless asked. {{howToSpawn}} The agents you spawn this way are the Hydra heads themselves: Droppy Code shows each one in the team's panel under a roster name and posts its outcome in the chat. There is no other launcher and no other kind of head, so never tell the user that Hydra heads cannot be started here; when the user asks for heads, spawn them this way.

Delegate first, work second. Anything bigger than a single obvious change to a single file is a job for heads: audits, reviews, a feature that spans files, a refactor, "check everything", research across many files or sources, several tasks in one message. In your first reply, look at the code only long enough to write good briefs, a minute and a handful of files rather than ten, then send out scouts for the reading and workers for the changes, all in that same message. Never spend minutes reading before you delegate, and never do inline what heads could be doing in parallel. Only a truly single-focus request, one file and one obvious change, is yours to do alone, and size the team to the work rather than to how many heads are free: a handful of independent pieces is a team, where three tiny steps that depend on each other are one head's task, or your own.

When you delegate:
- {{howToWait}}
- Give each head one self-contained task with the exact files, symbols and acceptance criteria it needs. Heads share the checkout but not your context, so write the task as if to a capable colleague who has read nothing yet.
- {{briefRule}}
- {{verificationRule}}
- {{projectRule}}
- Split the work so no two heads edit the same file and no head's task rests on another head's unfinished changes: they run in parallel and never see each other's work. Keep integration, verification and the final answer for yourself: never send out a head to verify, redo or finish another head's work.
- Tell the user in one line which heads you sent out and what each one does. Droppy Code names the heads in roster order (Hank, Walter, Ada, Otto, Nova, Remy, Iris, Milo, Juno, Ezra, Lena, Bo, Kai, Vera, Finn, Mira, Odin, Suki, Rex, Zola, Pip, Ivo, Lux, Tova, Gus, then Hank 2 and so on): announce each head by its task and use exactly those names in that order, never invented ones. There is no head called Ives; the roster has Ivo.
- After delegating, yield until reports arrive.
- Heads go out through the tools above, never through a fenced hydra block: that is the delegation format for providers without agent tools of their own. If you end a reply with one anyway, Droppy Code still sends those heads out as threads of their own, but your turn ends there and their reports come back as a later message.

When they report back:
- The checkout changes under you while heads work, and the user may be editing too. Never use git status or git diff to check on a head, and never reconcile, revert, stash or move changes you did not make.
- Take a report as done work: read the files it names if something matters, but do not redo the task and do not start it over because the tree looks different from what you expected. A head that failed leaves its part to you: do it or send it out again. A head the user stopped leaves its part alone unless the user asks.
- Then finish the job: integrate, run one verification if it matters, and answer the user. {{reportStyleRule}}
```

**Conditional inclusion, exactly (SOURCE FACT, Hydra.swift:738-739):**

```swift
\(reviewsHeads ? "- " + reviewRule + "\n" : "")- Then finish the job: ...
\(autoMerges ? "\n" + autoMergeRule + "\n" : "")
```

- With `reviewsHeads == true`, one extra bullet is inserted immediately before the
  "Then finish the job" bullet: `- ` + `reviewRule` + newline.
- With `autoMerges == true`, a blank line, then `autoMergeRule` on its own line, then a newline, is
  appended after the "Then finish the job" line. With `autoMerges == false` the final interpolation
  contributes nothing, but the literal still ends with a newline after the last bullet.

### 4.3 Upstream defect to reproduce

**SOURCE FACT / upstream defect.** In `policy(for:_:)` the delegation-block bullet says heads go out
"never through a fenced hydra block", and then concedes that Droppy Code sends them anyway. The
native lead is thus given two contradictory routes in one bullet. Reproduce as written; do not
reconcile.

---

## 5. Fallback (Droppy-run) lead policy — `HydraPrompts.fallbackPolicy(...)`

**SOURCE FACT.** Hydra.swift:874-902 (the parameterised form) and 906-908 (the `HydraLaunch`
convenience, which forwards `maxHeads`, `isolatesHeads`, `autoMerges`, `reviewsHeads`,
`headsLabel` as `heads`, `projects`, `headProfiles` as `profiles`).

Injection sites:

| Provider class | Site | Mechanism |
|---|---|---|
| Claude, heads elsewhere | `ClaudeSession.swift:117` | `--append-system-prompt`, **plus** `arguments += ["--disallowedTools", "Agent", "Task"]` |
| Codex, heads elsewhere | `CodexSession.swift:242` | `params["developerInstructions"]`, **plus** `config["features"] = ["multi_agent": .bool(false)]` |
| Copilot, heads elsewhere | `CopilotSession.swift:144` | appended `systemMessage`, no custom agents defined |
| Any OpenAI-compatible / API-key provider | `OpenAICompatibleSession.swift:1191` | joined with anti-slop into the appended system-prompt block, project guidelines appended after it |
| Any other CLI provider | `ThreadRuntime.swift:1279/1281` | prepended to **every** user message via `fallbackPreamble` / `fallbackReportPreamble` |

The split is `ThreadRuntime.keepsHydraPolicyInSystemPrompt(_:)` (line 1352-1353):
`AppModel.hydraIsNative(provider) || provider.isAPIKeyBased`. True → the policy lives in the system
prompt and only the short note is prepended per turn; false → the whole policy is prepended to each
message.

### 5.1 The computed pieces

```swift
let whereHeadsWork = isolated
    ? "Each head works in a copy of its project of its own and Droppy Code lands its changes in that project's checkout when it reports"
    : "The heads work in your checkout"
let team = maxHeads.map { "a team of up to \($0) helper agents" } ?? "a team of helper agents"
```

`whoTheHeadsAre` — empty when `heads` (the `HydraLaunch.headsLabel`) is nil; otherwise, with a
leading space:

```
 Your heads run on {{headsLabel}}, a different model from yours: it is quick, so give each one a well-bounded task with everything it needs written down, and keep the design, the judgement calls and the integration for yourself. The block is the only way to send heads out: never use an agent, task or sub-agent tool of your own, which would run heads on your own model instead of the one the user chose.
```

`whatHeadsCanDo` — **unconditional**, with a leading space:

```
 Every head, whatever its provider, can read files, edit files and run commands in its copy, with no permission prompts to wait on; brief it to do the work, not just to describe it.
```

`profileRule` — empty when `profiles` is empty; otherwise, with a leading space. The list is
`profiles.map(\.briefLabel).joined(separator: ", ")` and the JSON example always names
`profiles[0].name`:

```
 Some heads are tuned for a purpose: {{briefLabels}}. Route a task to one with "profile", as in `{"task": "...", "prompt": "...", "profile": "{{profiles[0].name}}"}`; a task that fits none of them goes without a profile and runs on the heads' shared model, as does one whose profile names none of these.
```

`briefLabel` (Hydra.swift:32-35) is computed as:
```swift
let on = [model, provider?.displayName].compactMap { $0 }.joined(separator: " on ")
return on.isEmpty ? name : "\(name) (\(on))"
```
so e.g. `deep (claude-opus-5 on Claude)`, or `quick (Claude)` when only provider `.claude` is set, or
bare `quick` when neither is set. Provider.swift:20-35 returns `Claude`; Hydra.swift:31's `Anthropic` comment is stale.

**Note (SOURCE FACT):** unlike the native path, `fallbackPolicy` passes `launch.headProfiles`
**unfiltered** — `nativeRoutableProfiles` is not applied — so a Droppy-run lead is told about
profiles that tune nothing and profiles on other providers.

### 5.2 The policy body, verbatim

The literal is one Swift multi-line string with an embedded fenced block. Placeholders:
`{{team}}`, `{{profileRule}}`, `{{whereHeadsWork}}`, `{{briefRule}}` (§3.3),
`{{verificationRule}}` (§3.4), `{{projectRule}}` (§3.8), `{{whoTheHeadsAre}}`,
`{{whatHeadsCanDo}}`, `{{reviewRule}}` (§3.6), `{{autoMergeRule}}` (§3.5),
`{{reportStyleRule}}` (§3.7).

Whitespace notes before reading: the first paragraph ends `not a team of heads.` followed by
**exactly nine spaces** and then `Finish your reply with one fenced block` — verified with
`cat -A`. Then a blank line, the fenced block, a blank line, and the closing paragraph.

````
[Hydra is on] You lead {{team}} ("heads"). Delegate first, work second: anything bigger than a single obvious change to a single file is a job for heads. Audits, reviews, a feature across several files, a refactor, "check everything", research across many files, several tasks in one message: in your first reply, look at the code only long enough to write good briefs, a minute and a handful of files rather than ten, and then send the heads out, all of them in that one block. Never spend minutes reading before you delegate, and never do inline what heads could be doing in parallel. Only a truly single-focus request, one file and one obvious change, is yours to do alone, and size the team to the work rather than to how many heads are free: a small job whose pieces depend on each other is one head's task, or your own, not a team of heads.         Finish your reply with one fenced block

```hydra
[{"task": "short title", "prompt": "complete, self-contained instructions with the exact files and acceptance criteria"}]
```

and stop there: do not wait, poll or verify anything after it. When a request is yours to do alone, do it and end with no block at all: an empty block sends no heads and is not needed. An entry may also carry its head's announced name, as in `{"task": "...", "prompt": "...", "name": "Otto"}`: the announced name is authoritative and the spawned head carries exactly it, so repeating the same block spawns the same names. An entry for work in another project carries `"project"` with that project's name or path, as in `{"task": "...", "prompt": "...", "project": "gaze-site"}`. Name new heads with the next roster names in order after the team listed above (Hank, Walter, Ada, Otto, Nova, Remy, Iris, Milo, Juno, Ezra, Lena, Bo, Kai, Vera, Finn, Mira, Odin, Suki, Rex, Zola, Pip, Ivo, Lux, Tova, Gus, then Hank 2 and so on), and omit the name when unsure: the next heads in order go out instead. Never invent names outside the roster: there is no head called Ives (the roster has Ivo), and an unknown name falls back to the next head in order rather than renaming anyone.{{profileRule}} Inside a prompt never open a fenced code block of your own (three backticks would end the hydra block early and no head would go out): describe code in words, quote identifiers with single backticks, or indent a snippet by four spaces. Each entry goes out to a head the moment its closing brace streams, before the block is finished, so write the entries in the order the heads should start and complete one entry before beginning the next. {{whereHeadsWork}}; heads never see your context, so write every prompt for a capable colleague who has read nothing yet, with the exact files, symbols and acceptance criteria, give no two heads the same file, and give no head work that rests on another head's unfinished changes: they run in parallel and never see each other's work. Keep each brief bounded: one head, one task, one report you can act on. {{briefRule}} {{verificationRule}} {{projectRule}} A head can be sent to read and report as well as to change files, so the reading goes out in parallel too. Say in one line which heads you sent out and what each one does.{{whoTheHeadsAre}}{{whatHeadsCanDo}} The reports arrive as a later message with the work already in place: build on them, do not redo them, never send out heads to verify or redo other heads, and never use git status or git diff to check on heads, since the checkout changes under you while they work. A message that opens with [Hydra] is from Droppy Code, not the user.{{reviewsHeads ? " " + reviewRule : ""}}{{autoMerges ? " " + autoMergeRule : ""}} {{reportStyleRule}}
````

Conditional tails, exactly as written in the source (Hydra.swift:902):
`\(reviewsHeads ? " " + reviewRule : "")\(autoMerges ? " " + autoMergeRule : "") \(reportStyleRule)`
— each optional piece contributes a **leading** space; `reportStyleRule` is always preceded by one
space and always present.

### 5.3 Upstream defects to reproduce

**SOURCE FACT / upstream defect 1.** The nine-space run before "Finish your reply with one fenced
block" (Hydra.swift:896). Cosmetic in rendered Markdown but literally present in the prompt text.

**SOURCE FACT / upstream defect 2.** The policy tells the lead the announced name is authoritative
and that "repeating the same block spawns the same names", while `HydraRoster.index(named:)`
silently falls back to the next sequential head for any off-roster name. The two statements are
consistent only for roster names; the prompt does not say so in the same sentence.

**SOURCE FACT / upstream defect 3.** The policy forbids a fenced code block inside a prompt and
offers "indent a snippet by four spaces" as the alternative, but the streaming parser
(§7) treats a backtick at column 0 outside any entry as the block's closer — so an indented fence is
safe while a column-0 fence inside a *string* is also safe (it is inside a JSON string, tracked by
`inString`). The prohibition is stricter than the parser needs. Reproduce the prompt as written.

---

## 6. The delegation block grammar

### 6.1 Opening fence

**SOURCE FACT.** `delegationOpener(in:)`, Hydra.swift:1041-1043 — the authoritative regex:

```
/```[ \t]*hydra(?:-sent|-leaving)?[ \t]*\r?\n/   with .ignoresCase()
```

Backtick-triple, optional spaces/tabs, the word `hydra` in any case, optionally suffixed `-sent` or
`-leaving`, optional spaces/tabs, then CRLF or LF.

**SOURCE FACT.** `hasDelegationOpener(in tail: Substring)`, Hydra.swift:1073-1075, uses the same
regex against a streaming tail.

**SOURCE FACT.** `streamedDelegations(in text:)` (HydraDelegationStream.swift:17) uses a
**different, narrower** regex — `-leaving` is absent:

```
/```[ \t]*hydra(?:-sent)?[ \t]*\r?\n/   with .ignoresCase()
```

**Upstream defect to reproduce:** the whole-message parser accepts a `hydra-leaving` opener, the
streaming reader does not.

### 6.2 Closing fence and block range

**SOURCE FACT.** `lineStartFences(in:from:)`, Hydra.swift:1047-1058: every occurrence of ``` ``` ```
from `start` onward is collected, but kept only when it is at the very start of the text or
immediately preceded by a newline character. An info string after the fence does **not**
disqualify it.

**SOURCE FACT.** `delegationBlockRange(in:)`, Hydra.swift:1065-1069: range runs from the opener's
lower bound to the **upper bound of the last** line-start fence after the opener, or to
`text.endIndex` when there is none. Deliberately the last, not the first, so a fenced snippet
quoted inside a prompt does not truncate the block.

### 6.3 Body parsing, whole-message

**SOURCE FACT.** `delegations(in:)`, Hydra.swift:1085-1094. Ordering is load-bearing:

1. Body starts just after the opener's newline.
2. Candidate ends = every line-start fence after the body start, **reversed** (last first), then
   `text.endIndex` appended.
3. The first candidate whose body parses via `delegations(fromBody:)` wins.

`delegations(fromBody:)`, Hydra.swift:1100-1106:

```swift
guard let json = JSONValue.parse(body) else { return nil }
if let array = json.array, array.isEmpty { return [] }
let entries: [JSONValue] = json.array ?? (json.object == nil ? [] : [json])
let parsed = entries.compactMap(delegation(fromEntry:))
return parsed.isEmpty ? nil : parsed
```

So: a bare object is accepted as a one-entry block; an explicitly empty array returns `[]` (the
lead asking for nothing); an array whose entries all fail to parse returns nil (treated as
unreadable), **not** `[]`.

### 6.4 One entry — `delegation(fromEntry:)`

**SOURCE FACT.** HydraDelegationStream.swift:105-118. Exact field names and aliases:

| JSON field | Alias | Required | Handling |
|---|---|---|---|
| `prompt` | none | **yes** | trimmed of whitespace and newlines; empty → the whole entry is dropped (`return nil`) |
| `task` | none | no | trimmed; when empty, replaced by `TextCleanup.singleLine(prompt, limit: 60)` |
| `name` | `head` | no | `name` first, then `head`; trimmed; empty string → nil |
| `project` | `repo` | no | `project` first, then `repo`; trimmed; empty string → nil |
| `profile` | none | no | trimmed; empty string → nil |

That is **5 recognised keys plus 2 aliases = 7 accepted JSON field names**, counted:
`prompt`, `task`, `name`, `head`, `project`, `repo`, `profile`.

The emitted value:

```swift
HydraDelegation(task: task.isEmpty ? TextCleanup.singleLine(prompt, limit: 60) : task,
                prompt: prompt,
                name: announced,
                project: (project?.isEmpty == false) ? project : nil,
                profile: (profile?.isEmpty == false) ? profile : nil)
```

### 6.5 Block-state rewriting (info-string mutation)

**SOURCE FACT.** Three literal info strings exist: `hydra`, `hydra-sent`, `hydra-leaving`.

- `markingDelegationBlockSent(_:)` (Hydra.swift:1101 region, lines 1107-1110): replaces the opener
  range with the literal `` "```hydra-sent\n" `` — only if an opener exists and no sent/leaving
  block is already present.
- `markingDelegationBlockLeaving(_:)` (Hydra.swift:1098-1101): replaces the opener with the literal
  `` "```hydra-leaving\n" `` — only if a sent block exists and a leaving one does not.
- `hasSentDelegationBlock(in:)` matches `` /```[ \t]*hydra-(?:sent|leaving)[ \t]*\r?\n/ `` ignoring case.
- `hasLeavingDelegationBlock(in:)` matches `` /```[ \t]*hydra-leaving[ \t]*\r?\n/ `` ignoring case.
- `withoutDelegationBlock(_:)` removes `delegationBlockRange` and then trims whitespace and
  newlines from both ends.

**Timing (SOURCE FACT):** `ThreadRuntime.swift:2810-2826` — after sending, wait
`delegationSentHold` (3.2 s), mark leaving, wait `delegationLeaveFade` (450 ms), then strip the
block. Each step re-checks that a sent block is still present before acting.

---

## 7. Streaming delegation reader

**SOURCE FACT.** Two implementations exist and the source comment states they must agree on order
and count.

### 7.1 `streamedDelegations(inBody:)` — HydraDelegationStream.swift:29-98

Byte-level walk over `body.utf8`. Result type:

```swift
struct StreamedDelegations: Hashable, Sendable {
    var delegations: [HydraDelegation] = []
    var isComplete = false
}
```

Rules, in evaluation order:

1. Skip JSON whitespace (`isJSONWhitespace`: exactly `" "`, `"\n"`, `"\r"`, `"\t"` — 4 bytes).
2. First non-whitespace byte: `[` → `isArray = true`, advance past it; `{` → `isArray = false`, do
   **not** advance; anything else → return an empty, incomplete result.
3. `atLineStart` is initialised **true** (the body begins right after the opener's newline).
4. Inside a string (`inString`): `\` sets `escaped`; `"` ends the string when not escaped.
5. Outside a string: `"` opens a string; `{` sets `entryStart` when `depth == 0` and increments
   depth; `}` decrements depth — at `depth == 0` the byte range `entryStart...index` is parsed with
   `JSONValue.parse` and, if `delegation(fromEntry:)` yields a value, appended; if `!isArray` the
   result is marked complete and returned. `depth < 0` → complete, return.
6. `]` at `depth == 0` → complete, return.
7. `` ` `` at `depth == 0` **and** `atLineStart` → complete, return (the closing fence).
8. `atLineStart = (byte == "\n")` at the end of every iteration.

**Ordering dependency:** entries are emitted the moment their outermost `}` arrives, in block order,
so a head starts before the block finishes streaming. Entries that fail `delegation(fromEntry:)` are
skipped, matching the whole-block parser's `compactMap`.

### 7.2 `HydraDelegationStreamParser` — HydraDelegationStream.swift:124-226

A stateful class fed only newly appended text (`ingest(_:)`), identified by `turnID: UUID` and
`entryID: String`. It re-implements the opener match as a hand-rolled byte state machine
(`readOpener`/`readOpenerEnd`, states 0-15) and the body walk (`readBody`) with the same rules as
§7.1. Raw ASCII byte values appear as literals: 96 `` ` ``, 32 space, 9 tab, 104 `h`, 121 `y`,
100 `d`, 114 `r`, 97 `a`, 45 `-`, 115 `s`, 101 `e`, 110 `n`, 116 `t`, 13 CR, 10 LF, 91 `[`,
93 `]`, 123 `{`, 125 `}`, 34 `"`, 92 `\`. Lower-casing is `byte >= 65 && byte <= 90 ? byte + 32 : byte`.

**SOURCE FACT:** the state machine recognises the `-sent` suffix only (states 9-12 spell `s`,`e`,`n`,`t`);
`-leaving` is not recognised, matching `streamedDelegations(in:)` and differing from
`delegationOpener(in:)`.

**SOURCE FACT:** on any mismatch the machine resets to `opener = byte == 96 ? 1 : 0`, so a new fence
can begin immediately at the mismatching byte.

---

## 8. Head system prompts (native providers)

### 8.1 `workerPrompt` — Hydra.swift:743-747

Two paragraphs; the second is `howToReport` (§3.2) after a blank line.

```
You are a Hydra head in Droppy Code: one of several helpers working in parallel for a lead agent, in the lead's own checkout, where the lead and the other heads are changing other files at the same time. Do exactly the task you were given, and only that: do not widen it, do not touch files it does not name unless the task cannot be done otherwise, and never revert, reformat or clean up work that is not yours. Changes you did not make are expected in the checkout: leave them alone, never stash, check out, reset, restore or clean anything, and never move work into a branch or worktree, whatever the project's own guidelines say about agents and worktrees. Do not use git status or git diff to check your work: they show everyone's changes. Never run a build, a test, a lint or any other check of your work: the lead runs every check itself. Make the edits your brief names and report. If something blocks you, say so instead of guessing.

Report the result, the files you changed and material blockers once, in a few lines the lead can act on: what changed, where, and anything still open or uncertain. Name the caveats and the unfinished work plainly rather than leaving them out. No preamble, no restating the brief, no logs. The lead handles validation.
```

### 8.2 `scoutPrompt` — Hydra.swift:750-754

```
You are a Hydra head in Droppy Code: a read-only researcher working in parallel for a lead agent, in the lead's own checkout, where the lead and other heads are changing files at the same time; uncommitted changes there are theirs and expected. Answer exactly the question you were given, from the code and sources you can read. Change nothing.

Reply with a short report the lead can act on: the findings, with file paths and line references, and anything that contradicts what the lead assumed. No preamble.
```

### 8.3 Agent names

**SOURCE FACT.** Hydra.swift:541-542, 547-556.

```swift
static let workerAgentName = "droppy-worker"
static let scoutAgentName  = "droppy-scout"
static func workerAgentName(for profile: HydraHeadProfile) -> String { "\(workerAgentName)-\(profile.agentSlug)" }
static func profileName(forAgentName agentName: String?) -> String? {
    guard let agentName, agentName.hasPrefix("\(workerAgentName)-") else { return nil }
    return String(agentName.dropFirst(workerAgentName.count + 1))
}
```

`agentSlug` (Hydra.swift:39-41) maps a profile name to lowercase with every non-alphanumeric
character replaced by `-`: `"Deep Work"` → `"deep-work"`, so the agent name is
`droppy-worker-deep-work`.

`HydraHeadProfile.matches(_:)` (Hydra.swift:47-51) accepts either the trimmed case-insensitive name
or an exact `agentSlug` match.

Round-trip back to a profile name at `ClaudeSession.swift:440`
(`message["subagent_type"]?.string`) and `CopilotSession.swift:592` (`data["agentName"]?.string`).

### 8.4 Claude `--agents` JSON — Hydra.swift:757-789

Exact keys emitted (anti-slop text from `AntiSlopPolicy.instructions(enabled:)`):

`droppy-worker`:
```json
{
  "description": "Hydra head that implements one delegated task: edits files, runs commands. Use proactively when a request splits into independent pieces or touches several parts of the codebase.",
  "prompt": "<workerPrompt>\n\n<antiSlop>",
  "background": false
}
```

`droppy-scout`:
```json
{
  "description": "Hydra head for read-only research: finds files, reads code, gathers facts and reports back. Use proactively when a job needs many files or sources read.",
  "prompt": "<scoutPrompt>\n\n<antiSlop>",
  "tools": ["Read", "Grep", "Glob", "WebFetch", "WebSearch"],
  "background": false
}
```

The scout tool list has **exactly 5 entries in this order**: `Read`, `Grep`, `Glob`, `WebFetch`,
`WebSearch`.

Conditional keys, applied to **both** worker and scout:
- `model`: added only when `launch.workerModel != nil && model != "default"`.
- `effort`: added only when `launch.workerEffort != nil && !effort.isEmpty`.

Per routable profile, one extra agent keyed `droppy-worker-<slug>`, copied from the worker object
with:
```
"description": "Hydra head that implements one delegated task, tuned as the '<profile.name>' profile. Prefer it for work that belongs to '<profile.name>' over the plain droppy-worker."
```
plus `model` when `profile.model != nil && != "default"` and `effort` when
`profile.effort != nil && !effort.isEmpty`. Note the sentinel here is `"default"`.

**Companion flags at `ClaudeSession.swift:108-112`:** `--agents <json>`, then
`--forward-subagent-text` only when `options.contains("--forward-subagent-text")`, then
`environment["CLAUDE_CODE_MAX_CONCURRENT_SUBAGENTS"] = String(cap)` only when
`hydra.maxHeads != nil`.

### 8.5 Copilot `customAgents` JSON — Hydra.swift:792-823

An **array**, not an object. Each element:

```json
{
  "name": "...",
  "displayName": "...",
  "description": "...",
  "prompt": "...",
  "infer": true
}
```

plus optional `"tools"` (never set: both calls pass `tools: nil`), optional `"model"` when
`launch.workerModel != nil && model != "auto"` — sentinel `"auto"`, **not** `"default"` as on
Claude — and optional `"reasoningEffort"` when `launch.workerEffort` is non-empty.

The two base agents, in array order:

1. `name: "droppy-worker"`, `displayName: "Hydra worker"`,
   `description: "Hydra head that implements one delegated task: edits files, runs commands. Use when a request splits into independent pieces."`,
   `prompt: workerPrompt + "\n\n" + antiSlop`.
2. `name: "droppy-scout"`, `displayName: "Hydra scout"`,
   `description: "Hydra head for read-only research: finds files, reads code and reports back. Use when a job needs many files or sources read."`,
   `prompt: scoutPrompt + "\n\n" + antiSlop`.

Note these two Copilot descriptions differ from Claude's: no "proactively", and the scout's omits
"gathers facts".

Then one element per routable profile, cloned from the **worker** element with:
- `name` = `droppy-worker-<slug>`
- `displayName` = `Hydra worker · <profile.name>` (the separator is U+00B7 MIDDLE DOT surrounded by spaces)
- `description` = `Hydra head that implements one delegated task, tuned as the '<name>' profile. Prefer it for work that belongs to '<name>' over the plain droppy-worker.`
- `model` when `profile.model != nil && != "auto"`, `reasoningEffort` when `profile.effort` non-empty.

### 8.6 Codex config overrides — Hydra.swift:828-838

```swift
var agents: [String: JSONValue] = [:]
if let cap = launch.maxHeads { agents["max_concurrent_threads_per_session"] = .int(cap) }
if let model = launch.workerModel, !model.isEmpty { agents["default_subagent_model"] = .string(model) }
if let effort = launch.workerEffort, !effort.isEmpty { agents["default_subagent_reasoning_effort"] = .string(effort) }
var features: [String: JSONValue] = ["multi_agent": true]
if !nativeRoutableProfiles(of: launch).isEmpty {
    features["multi_agent_v2"] = .object(["expose_spawn_agent_model_overrides": true])
}
return ["agents": .object(agents), "features": .object(features)]
```

Exact persisted key names, counted — 6: `agents`, `max_concurrent_threads_per_session`,
`default_subagent_model`, `default_subagent_reasoning_effort`, `features`, `multi_agent`,
plus `multi_agent_v2` and `expose_spawn_agent_model_overrides` under the conditional branch (8 in
total when profiles exist).

**Note (SOURCE FACT):** Codex's model check is `!model.isEmpty` — there is **no** `"default"` /
`"auto"` sentinel, unlike Claude and Copilot. Three different sentinel conventions across three
providers; reproduce each exactly.

Matching back at `CodexSession.swift:749`:
`HydraPrompts.nativeRoutableProfiles(of: hydra).filter { $0.model == itemModel && $0.effort == itemEffort }`.

---

## 9. Droppy-run head briefs

### 9.1 `delegatedHeadPrompt(persona:delegation:workplace:)` — Hydra.swift:1152-1168

Injected at `ThreadRuntime.swift:2897`, handed to the head's runtime via
`headRuntime.sendHydraBrief(...)` (AppModel+Hydra.swift:477). Exact structure, heading levels
significant:

```
You are {{persona.name}}, a Hydra head in Droppy Code: one of several helpers working in parallel for a lead agent. The lead delegated this task to you.

## Your task: {{delegation.task}}
{{withoutVerificationSteps(delegation.prompt)}}

## Where you work
{{workplaceRules(workplace)}}

## How to work
{{howToWork}}

## Your report
{{howToReport}}
```

### 9.2 `queuedHeadPrompt(persona:task:context:workplace:leadIsWorking:)` — Hydra.swift:1180-1216

Injected at `ThreadRuntime.swift:1020`. `leadIsWorking` defaults to `true`.

Context lines, built in this order and joined with `\n`:

1. When `context.lastUserPrompt` is non-nil and non-empty:
   `- The user last asked the lead: {{quoted(prompt, limit: 1_200)}}`
2. When `context.lastReply` is non-nil and non-empty:
   `- The lead's latest reply so far: {{quoted(reply, limit: 800)}}` — the ` so far` is present only
   when `leadIsWorking`; otherwise the line reads `- The lead's latest reply: ...`.
3. Only when `leadIsWorking` **and** `context.touchedPaths` is non-empty (paths are
   `touchedPaths.prefix(20).joined(separator: ", ")`):
   `- Files the lead is changing in its current turn: {{paths}}. Leave these alone; if your task cannot be done without touching one, keep the change minimal and say so in your report.`

When all three are absent the block is the single line:
```
The lead has not said anything yet.
```

`handoff`:
- `leadIsWorking == true`: `The user queued this task for you while the lead works on something else.`
- `leadIsWorking == false`: `The user sent this task straight to you; the lead is idle and will hear your report.`

Full template:

```
You are {{persona.name}}, a Hydra head in Droppy Code: a helper running in parallel with the lead agent. {{handoff}}

## What is going on in the main chat
{{contextBlock}}

## Your task
{{task}}

## Where you work
{{workplaceRules(workplace)}}

## How to work
{{howToWork}}

## Your report
{{howToReport}}
```

**Note (SOURCE FACT):** the queued brief does **not** pass the task through
`withoutVerificationSteps`; only the delegated brief does.

`quoted(_:limit:)` (Hydra.swift:1338-1342): trims, truncates to `limit` characters appending
U+2026 `…`, then wraps in curly quotes U+201C … U+201D:
```swift
return "“\(cut)”"
```

### 9.3 `withoutVerificationSteps(_:)` and the marker table — Hydra.swift:1126-1149

**SOURCE FACT.** `verificationMarkers` has **exactly 34 entries**, counted mechanically
(`sed -n '1128,1139p' Hydra.swift | grep -o '"[^"]*"' | wc -l` → 34), in this order:

| # | Marker (verbatim, matched lowercased as a substring) |
|---|---|
| 1 | `xcodebuild` |
| 2 | `swift build` |
| 3 | `swift test` |
| 4 | `swiftlint` |
| 5 | `npm test` |
| 6 | `npm run build` |
| 7 | `npm run test` |
| 8 | `yarn build` |
| 9 | `yarn test` |
| 10 | `pnpm test` |
| 11 | `cargo build` |
| 12 | `cargo test` |
| 13 | `go test` |
| 14 | `pytest` |
| 15 | `gradle` |
| 16 | `eslint` |
| 17 | `tsc ` (note the trailing space) |
| 18 | `build succeeded` |
| 19 | `run the build` |
| 20 | `run the tests` |
| 21 | `run the test suite` |
| 22 | `build and prove` |
| 23 | `prove it builds` |
| 24 | `prove it compiles` |
| 25 | `check that it builds` |
| 26 | `check it still builds` |
| 27 | `make sure it builds` |
| 28 | `make sure it compiles` |
| 29 | `confirm the build` |
| 30 | `verify the build` |
| 31 | `verify that it builds` |
| 32 | `make sure nothing else broke` |
| 33 | `run the suite` |
| 34 | `build verification` |

Exact source lines (Hydra.swift:1128-1139), reproduced so the grouping is unambiguous:
```swift
"xcodebuild", "swift build", "swift test", "swiftlint", "npm test", "npm run build",
"npm run test", "yarn build", "yarn test", "pnpm test", "cargo build", "cargo test",
"go test", "pytest", "gradle", "eslint", "tsc ", "build succeeded", "run the build",
"run the tests", "run the test suite", "build and prove", "prove it builds",
"prove it compiles", "check that it builds", "check it still builds",
"make sure it builds", "make sure it compiles", "confirm the build",
"verify the build", "verify that it builds", "make sure nothing else broke",
"run the suite", "build verification",
```
The table above lists all 34.

The filter, exactly (Hydra.swift:1143-1149):

```swift
let kept = prompt.split(separator: "\n", omittingEmptySubsequences: false).filter { line in
    let lowered = line.lowercased()
    return !verificationMarkers.contains { lowered.contains($0) }
}
if kept.count == prompt.split(separator: "\n", omittingEmptySubsequences: false).count { return prompt }
return kept.joined(separator: "\n")
    + "\nMake the assigned edits and report; the lead handles all validation."
```

So: line-granular, case-insensitive substring match; if nothing was dropped the prompt comes back
**byte-identical**; if anything was dropped, this exact sentence is appended on a new line:

```
Make the assigned edits and report; the lead handles all validation.
```

**Upstream defect to reproduce:** the filter is substring-based and unanchored, so a brief line
containing "gradle" or "pytest" in any context — including a file path such as
`tools/pytest_helpers.py` — is silently deleted from the head's brief.

---

## 10. Per-turn notes injected in front of user messages

### 10.1 `teamStatus(_:now:)` — Hydra.swift:848-871

Returns nil for an empty team. Otherwise `"Your heads so far: " + parts.joined(separator: ", ") + "."`
Each part, exactly:

| Status | Part |
|---|---|
| `.running`, elapsed < 1 min | `NAME (working)` |
| `.running`, elapsed ≥ 1 min | `NAME (working for Nm)` where `N = Int(now.timeIntervalSince(startedAt) / 60)` |
| `.completed`, landing nil | `NAME (done)` |
| `.completed`, `landing.isEmpty` | `NAME (done, changed nothing)` |
| `.completed`, `landing.landed`, 1 file | `NAME (done, landed 1 file)` |
| `.completed`, `landing.landed`, n files | `NAME (done, landed n files)` |
| `.completed`, landing present but not landed | `NAME (done, patch kept)` |
| `.failed` | `NAME (failed)` |
| `.stopped` | `NAME (stopped)` |

`HydraLanding.isEmpty` = `files.isEmpty && patchPath == nil && error == nil`;
`landed` = `!files.isEmpty && patchPath == nil && error == nil`.

### 10.2 `fallbackTurnNote(team:merge:)` — Hydra.swift:955-959

Returns `""` when both parts are nil. Otherwise:

```
[Hydra] {{parts joined with " "}}

---


```

exact form: `"[Hydra] " + parts.joined(separator: " ") + "\n\n---\n\n"`.

### 10.3 `fallbackReportNote(team:merge:canDelegate:)` — Hydra.swift:964-970

```swift
let more = canDelegate
    ? "If the reports open up the next stage of the work, or a head failed, or a piece of the user's request is still undone, send heads out again for exactly that, with the same ```hydra block at the end of your reply; never for verifying, redoing or finishing what a head already did."
    : "Send out no more heads for this request; whatever is left, do yourself."
return "[Hydra] Your heads reported back below." + (team.map { " " + $0 } ?? "") + (merge.map { " " + $0 } ?? "") + " " + more + "\n\n---\n\n"
```

**SOURCE FACT:** the `canDelegate` branch string contains a literal ```` ```hydra ```` inside running
prose. In a Markdown-rendered chat this opens a code fence inside the note.
**Upstream defect to reproduce.**

### 10.4 `fallbackPreamble` / `fallbackReportPreamble` — Hydra.swift:974-980

```swift
static func fallbackPreamble(_ launch: HydraLaunch, team: String?, merge: String? = nil) -> String {
    fallbackPolicy(launch) + "\n\n" + (fallbackTurnNote(team: team, merge: merge).isEmpty ? "---\n\n" : fallbackTurnNote(team: team, merge: merge))
}
static func fallbackReportPreamble(_ launch: HydraLaunch, team: String?, merge: String? = nil, canDelegate: Bool) -> String {
    fallbackPolicy(launch) + "\n\n" + fallbackReportNote(team: team, merge: merge, canDelegate: canDelegate)
}
```

`fallbackTurnNote` is evaluated **twice** in `fallbackPreamble` — harmless but a real inefficiency to
note, not to fix.

### 10.5 `delegationRequest(_:)` — Hydra.swift:936-949

Prepended at `ThreadRuntime.swift:1290` **only when `hydraHeads == nil`** (i.e. an ordinary user
message, never an automatic report). Applied on both native and fallback routes.

```
[Hydra setting: enabled]
Use Hydra heads for this request when it involves multiple files, an audit, broad research, or independent tasks. Keep a single obvious change in one file with the lead. {{route}} Follow any explicit request below to work alone or limit delegation.

---


```

`route`:
- `launch.runsNatively`: `Use the configured Hydra agent tools and head profiles.`
- otherwise: `Delegate through the final hydra block so Droppy Code runs the selected heads; do not use native agent tools.`

The trailing structure is literally `\n\n---\n\n` (the Swift literal ends with `---`, a blank line
and one more blank line before the closing `"""`).

### 10.6 Composition order in `ThreadRuntime.startTurn` — lines 1261-1304

**Ordering dependency (SOURCE FACT), outermost-last:**

1. `prompt = text`
2. `+= "\n\nAttached files:\n" + "- PATH" lines` for every non-image attachment
3. If `hydraLaunch != nil`:
   a. compute `merge` = `mergeStatus(...)` only when `launch.autoMerges`, else nil
   b. `!runsNatively`: prepend turn-note / report-note (system-prompt providers) or full
      preamble / report preamble (other CLIs)
   c. `runsNatively && merge != nil`: prepend `fallbackTurnNote(team: nil, merge: merge)`
   d. if `hydraHeads == nil`: prepend `delegationRequest(launch)`
4. If not a system-prompt provider, no slash command, and anti-slop not yet sent: prepend
   `AntiSlopPolicy.instructions(...) + "\n\n"`

So the final order in the message is: anti-slop → delegation request → Hydra note/preamble → user
text → attachment list.

### 10.7 `mergeStatus(merges:unmergedFiles:now:)` — Hydra.swift:911-933

Helpers:
- `files(_ count:)` → `"1 file"` when 1, else `"N files"`.
- `when(_ date:)` → `date.formatted(date: .omitted, time: .shortened)`.
- `merged(_ record:)` →
  `"the team's work already merged as {{label url}}{{ in PROJECT}} at {{time}} ({{files}})."`
  where the link is `[record.label, record.url?.absoluteString].compactMap { $0 }.joined(separator: " ")`
  and the scope is `record.project.map { " in \($0)" } ?? ""`.
- `pendingLine(_ prefix:)` →
  `"{{prefix}} is not merged yet; it goes out by itself once you finish this answer with every head back."`
  for one file, and
  `"{{prefix}} are not merged yet; they go out by themselves once you finish this answer with every head back."`
  for several.

`pending` is `pendingLine("N files changed since then")` when `unmergedFiles > 0`, else exactly:
```
Nothing has changed since, so no merge is pending and none is coming.
```

With no merge records at all: nil when `unmergedFiles == 0`, else
`"Merge state: " + pendingLine("N files changed in this chat")`.

With records, by `merges.last.outcome`:
- `.merged` → `"Merge state: " + merged(last) + " " + pending`
- `.failed` →
```
Merge state: the last merge failed at {{time}}: {{detail ?? "no reason recorded"}}. The work is still in the checkout and is retried once you finish this answer; if the reason needs the user (a sign-in, a conflict), say exactly that.
```
- `.stray` → parts joined with `" "`: `"Merge state:"`, then `merged(lastMergedRecord)` if any
  `.merged` record exists, then
  `"At {{time}} {{detail ?? "files were changed outside every project"}}"`, then `pending`.

---

## 11. Report message back to the lead — `reportMessage(_:stillWorking:reviewsHeads:)`

**SOURCE FACT.** Hydra.swift:1221-1296. Injected at `ThreadRuntime.swift:2651`:
`HydraPrompts.reportMessage(reports, stillWorking: stillWorking, reviewsHeads: app.settings.hydraReviewHeads)`.

### 11.1 Overall shape

```
{{opening}}

{{sections joined with "\n\n"}}

{{closing joined with " "}}
```

### 11.2 Opening

```
Hydra reports: {{list(names)}} finished.
```

plus, when `stillWorking` is non-empty, appended after a space:
```
{{list(stillWorking)}} is still at work; its report comes as a later message.
```
for one, or
```
{{list(stillWorking)}} are still at work; their reports come as a later message.
```
for several.

`list(_:)` (Hydra.swift:1330-1336): 0 → `""`; 1 → the name; else
`names.dropLast().joined(separator: ", ") + " and " + names.last`. So three names give
`A, B and C` (no Oxford comma).

### 11.3 Per-head section

```
## {{persona.name}}: {{report.task}}{{outcome}}{{took}}
{{landingLine, when non-nil}}
{{report.text trimmed, or "No report." when empty}}
```

`outcome`: `""` for `.completed` and `.running`; `" (failed)"` for `.failed`;
`" (stopped by the user before finishing)"` for `.stopped`.

`took`: `""` when nothing to report, else `" (" + effort.joined(separator: ", ") + ")"` where
`effort` is built in this order: the duration when `report.elapsed >= 1`, then
`"1 tool"` / `"N tools"` when `report.toolCalls > 0`.

`duration(_:)` (Hydra.swift:1325-1328):
```swift
let total = Int(seconds)
return total < 60 ? "\(total)s" : "\(total / 60)m \(total % 60)s"
```
so `45s`, `2m 7s`, `0m 0s` is impossible (it would be `0s`).

### 11.4 `landingLine(for:)` — Hydra.swift:1300-1318

With `report.landing == nil`:
- `report.copyPath == nil` → no line at all
- `.completed` → `Changed nothing.`
- otherwise → `Nothing landed; its unfinished work is in its copy at {{copyPath}}.`

With a landing:
- `landing.isEmpty` → `Changed nothing.`
- else the file list is `landing.files.prefix(25)` mapped to
  `"{{path}} (+{{additions}} −{{deletions}})"` — **the minus is U+2212 MINUS SIGN, not a hyphen** —
  with `" with conflicts"` appended when `landing.conflicts.contains(file.path)`; joined with `", "`;
  then `", and N more"` when `landing.files.count > 25`.
- `omitted` = `""` when `droppedBuildOutputFiles == 0`, else
  `"; 1 build-output file was left out"` / `"; N build-output files were left out"`.
- `landing.patchPath != nil` → `Did not land: {{listed}}{{omitted}}.`
- `landing.error != nil` → `Did not land ({{error}}): {{listed}}{{omitted}}.`
- otherwise → `Landed in your checkout: {{listed}}{{omitted}}.`

**Ordering dependency:** `patchPath` is checked before `error`, so a landing with both reports the
patch form.

### 11.5 Closing sentences, in emission order

**SOURCE FACT.** Built as an array and joined with a single space. Conditions and exact strings, in
the order they are appended (Hydra.swift:1252-1291):

1. any `report.landing?.landed == true` →
   `The changes listed above are in your checkout already; nothing needs applying.`
2. any report with `copyPath == nil && status == .completed` →
   `The heads worked in your checkout, so their changes are there already.`
3. any report whose `landing.conflicts` is non-empty →
   `A file listed with conflicts was merged three-way and keeps conflict markers: resolve those first, and tell the user in one line that you settled them, so the landing card above does not read as broken.`
4. For every file path changed by more than one head, **sorted by path ascending** —
   `{{list(names)}} both changed {{path}}: read it as it is now before you build on it.`
   (the word "both" is used regardless of how many heads touched it — upstream defect to reproduce)
5. For each report with `landing.patchPath` non-nil, in report order —
   `{{name}}'s changes would not apply on their own; the patch is at {{path}}. Apply it with `git apply --3way {{path}}` and settle what conflicts.`
   (the git command is wrapped in single backticks in the literal)
6. any `.failed` → `A head that failed leaves its part to you: do it yourself or send it out again.`
7. any `.stopped` → `A head the user stopped leaves its part alone unless the user asks for it.`
8. any `origin == .queued` → `The user queued that work for the heads while you were busy; take it into account and carry on with the main job.`
9. any `origin == .sent` → `The user sent that work straight to the heads instead of to you; take it into account, and reply to the user on it as if they had asked you.`
10. **always exactly one of these two.** With `reviewsHeads == true`:
    `Do not check any of this with git status or git diff: the checkout changes under you while heads work, and the user may be editing too. Do not reconcile or revert anything. ` + `reviewRule` (§3.6).
    With `reviewsHeads == false`:
    `Do not check any of this with git status or git diff: the checkout changes under you while heads work, and the user may be editing too. Do not reconcile, revert or redo anything. Build on the reports, read the files they name if something matters, run one verification if it matters, and finish the job.`
11. **always** — `reportStyleRule` (§3.7).
12. `stillWorking` non-empty, one head →
    `Do not wait for {{name}} and do not take over its tasks; tell the user it is still at work and that you will hear from it.`
    several →
    `Do not wait for {{list}} and do not take over their tasks; tell the user they are still at work and that you will hear from them.`

Sentences 1 and 2 are not mutually exclusive: a mixed batch (some heads with copies, some without)
emits both.

---

## 12. Refusal messages to the lead

### 12.1 `heldBackMessage(count:)` — Hydra.swift:1032-1037

Fires at `ThreadRuntime.swift:2783` when `hydraDelegationRounds >= maxDelegationRounds` (3).

```
Hydra held back {{count == 1 ? "a head" : "N heads"}}.
None of the heads you asked for went out: this request has had all 3 of its rounds of heads already. Do the rest yourself now, without git status or git diff on the heads' work, and answer the user.
```

The `3` is `\(maxDelegationRounds)`, interpolated.

### 12.2 `unreadableBlockMessage(reason:)` — Hydra.swift:1116-1123

Fires at `ThreadRuntime.swift:2763` with `reason: nil`, so in shipped code the emitted text is the
base plus the tail with no middle clause. The full computation:

```swift
var message = "[Hydra] Your delegation block could not be read, so no heads went out."
if let reason = reason?.trimmingCharacters(in: .whitespacesAndNewlines), !reason.isEmpty {
    message += " " + reason + (reason.last.map { ".!?".contains($0) } == true ? "" : ".")
}
return message + " Write the block again at the end of your reply: one fenced block whose info string is hydra, holding a JSON array of objects with a task string and a prompt string. Inside a prompt never open a fenced code block of your own (no three backticks): describe code in words, quote identifiers with single backticks, or indent snippets. This does not count as a round of heads."
```

Emitted text on the shipped path (`reason == nil`):

```
[Hydra] Your delegation block could not be read, so no heads went out. Write the block again at the end of your reply: one fenced block whose info string is hydra, holding a JSON array of objects with a task string and a prompt string. Inside a prompt never open a fenced code block of your own (no three backticks): describe code in words, quote identifiers with single backticks, or indent snippets. This does not count as a round of heads.
```

**SOURCE FACT — dead parameter.** `grep` over the repository finds exactly one call site
(`ThreadRuntime.swift:2763`) and it passes `nil`. The `reason` branch, including the
`".!?"` terminal-punctuation set, is unreachable in shipped code. Port it for structural parity but
do not expect to exercise it.

---

## 13. Anti-slop text that rides alongside every Hydra prompt

**SOURCE FACT.** `AntiSlopPolicy` (`DroppyCode/Core/Models/AntiSlopPolicy.swift`). Included in
`claudeAgents`, `copilotAgents`, and joined with the Hydra policy in all four session types.

`enabledInstructions`:

```
[Droppy Code anti-slop guidance]
Give the shortest complete answer, leading with the result. Use plain, natural language. Skip filler, hype, stock openings, repeated summaries and unsolicited closers. Preserve requested detail, facts, uncertainty, blockers and required validation.
Make focused edits using existing patterns. Add comments only for non-obvious reasons, constraints or pitfalls; omit banners, obvious narration and speculative TODOs. Preserve useful documentation. Avoid unrelated cleanup, dependencies and abstractions.
For UI work, follow the product’s components, typography and colors. Prefer purposeful structure and real content; preserve accessibility, responsive layouts and visible loading, empty and error states.
Reuse evidence, read relevant sections and batch independent lookups. Avoid repeated scans, polling and echoed logs. Perform required checks; brevity never excuses incomplete work. Apply this guidance to delegated work too. Report changes, affected files and material limits once. User requests and project requirements take precedence.
```

Note the U+2019 RIGHT SINGLE QUOTATION MARK in `product’s` and the US spelling `colors`, both
against the porting repo's own house style. Reproduce verbatim; this is model-visible text.

`disabledInstructions`:

```
[Droppy Code anti-slop guidance: off]
Disregard earlier app-provided anti-slop guidance. Continue following user requests and project requirements.
```

`instructions(enabled:)` returns one or the other; it never returns an empty string, so the
`!instructions.isEmpty` guard at `ThreadRuntime.swift:1300` is always true.

---

## 14. HydraCookbook — the pair recipes

**SOURCE FACT.** `HydraCookbook.recipes`, HydraCookbook.swift:28-40. **Exactly 10 recipes**, counted,
"strongest first".

| # | `id` | `title` | `maxHeads` |
|---|---|---|---|
| 1 | `claude-fable-opus` | `Fable 5.1 leads Opus 5` | nil |
| 2 | `claude-opus-gemini-flash` | `Opus 5 leads Gemini 3.8 Flash` | 6 |
| 3 | `claude-opus-deepseek` | `Opus 5 leads DeepSeek V4.1` | nil |
| 4 | `claude-opus-glm` | `Opus 5 leads GLM-5.3 Flash` | nil |
| 5 | `claude-fable-composer` | `Fable 5.1 leads Composer 2.5` | nil |
| 6 | `astra-spark` | `Astra 6 leads Spark 1.3 Contributor` | nil |
| 7 | `astra-terra` | `Astra 6 leads Terra 5.6` | nil |
| 8 | `astra-astra` | `Astra 6 high, Astra 6 medium` | nil |
| 9 | `claude-opus-opus` | `Opus 5 max, Opus 5 medium` | nil |
| 10 | `claude-fable-fable` | `Fable 5.1 high, Fable 5.1 medium` | nil |

Sides, exactly (`providers` best-first, `models` best-first, `effort`):

| # | lead.providers | lead.models | lead.effort | heads.providers | heads.models | heads.effort |
|---|---|---|---|---|---|---|
| 1 | `[.claude, .commandcode]` | `["Fable"]` | nil | `[.claude, .commandcode]` | `["Opus"]` | nil |
| 2 | `[.claude, .commandcode]` | `["Opus"]` | `"high"` | `[.antigravity, .commandcode]` | `["Gemini 3.8 Flash", "Gemini", "Flash"]` | `"medium"` |
| 3 | `[.claude, .commandcode]` | `["Opus"]` | `"high"` | `[.deepseek, .commandcode]` | `["V4.1 Flash", "V4.1", "DeepSeek"]` | `"high"` |
| 4 | `[.claude, .commandcode]` | `["Opus"]` | `"high"` | `[.zai, .commandcode]` | `["5.3 Flash", "GLM"]` | `"high"` |
| 5 | `[.claude, .commandcode]` | `["Fable"]` | `"high"` | `[.cursor]` | `["Composer"]` | nil |
| 6 | `[.codex, .commandcode]` | `["Astra"]` | `"high"` | `[.meta, .commandcode]` | `["Contributor"]` | `"medium"` |
| 7 | `[.codex, .commandcode]` | `["Astra"]` | `"high"` | `[.codex, .commandcode]` | `["Terra"]` | `"medium"` |
| 8 | `[.codex, .commandcode]` | `["Astra"]` | `"high"` | `[.codex, .commandcode]` | `["Astra"]` | `"medium"` |
| 9 | `[.claude, .commandcode]` | `["Opus"]` | `"max"` | `[.claude, .commandcode]` | `["Opus"]` | `"medium"` |
| 10 | `[.claude, .commandcode]` | `["Fable"]` | `"high"` | `[.claude, .commandcode]` | `["Fable"]` | `"medium"` |

Taglines, verbatim, in the same order:

1. `The deepest lead over the strongest builders: Fable plans and checks, Opus heads build. Set the thinking effort of each side yourself.`
2. `A careful lead and a wide, fast team: Opus writes the briefs, six Gemini Flash heads sprint through them.`
3. `Opus keeps the plan tight; DeepSeek V4.1 Flash heads keep the bill small on the routine parts.`
4. `Opus keeps the plan tight; GLM-5.3 Flash heads on a Z.ai Coding Plan keep the bill flat.`
5. `Fable thinks; Cursor's Composer heads type at the speed Composer is known for.`
6. `GPT-6 Astra orchestrates at high; Muse Spark Contributor heads build at medium.`
7. `OpenAI top to bottom: Astra plans and reviews, Terra does the work at medium.`
8. `One model, two efforts: Astra thinks hard about the plan and the review, and Astra heads build at a working effort.`
9. `Opus thinks as hard as it can where it counts; Opus heads at medium do the building.`
10. `For the hardest, longest jobs: every seat on the most capable model, the lead at high and the heads at medium.`

Two helpers:

```swift
static func effort(_ wanted: String?, for option: ModelOption?) -> String? {
    guard let wanted, let option, option.efforts.contains(wanted) else { return nil }
    return wanted
}
static func isAdded(_ pair: HydraPair, in pairs: [HydraPair]) -> Bool {
    pairs.contains { $0.provider == pair.provider && $0.headsProvider == pair.headsProvider && $0.orchestratorModel == pair.orchestratorModel && $0.workerModel == pair.workerModel }
}
```

`isAdded` compares 4 fields and ignores efforts, `maxHeads`, `name` and `headProfiles`.

---

## 15. Other exact strings, sets and keys in these files

### 15.1 `HydraMergeStage` — 7 cases, in order (Hydra.swift:494-530)

| Order | Case | `words` |
|---|---|---|
| 0 | `gathering` | `Gathering the team's files` |
| 1 | `committing` | `Writing the commit` |
| 2 | `describing` | `Writing the commit message` |
| 3 | `pushing` | `Pushing the branch` |
| 4 | `opening` | `Opening the merge request` |
| 5 | `merging` | `Merging` |
| 6 | `syncing` | `Bringing the checkout up to date` |

The doc comment states that with several projects the stages after the first repeat per project.

### 15.2 Persisted enum raw values

`HydraHeadInfo.Kind` (2): `native`, `droppy`.
`HydraHeadInfo.Origin` (3): `delegated`, `queued`, `sent`.
`HydraHeadInfo.Status` (4): `running`, `completed`, `failed`, `stopped`; `isFinished` is
`self != .running`.

### 15.3 `HydraMentionTarget.scheme`

```swift
static let scheme = "droppycode-head"
var url: URL { URL(string: "\(Self.scheme)://\(threadID.uuidString)") ?? URL(fileURLWithPath: "/") }
```
Parsing requires `url.scheme == scheme` and a host parsable as `UUID`.

### 15.4 Decoding defaults worth porting exactly

**SOURCE FACT, Hydra.swift:398-420.** `HydraHeadInfo.init(from:)` defaults:
`index` 0, `task` `""`, `kind` `.droppy`, `origin` `.delegated`, **`status` `.stopped`** (explicitly
never `.completed`), `toolCalls` 0, `tokens` 0, `startedAt` `.now`, `isBackground` true, and
`canStop` defaults to `kind == .droppy` rather than to a fixed value.

**SOURCE FACT, Hydra.swift:88.** `HydraPair.init(from:)` runs `maxHeads` through
`Self.clampedCap(...)`, which clamps into `1...8`.

---

## 16. Summary of upstream defects recorded (not to be fixed)

1. **Nine-space run** in `fallbackPolicy` before "Finish your reply with one fenced block"
   (Hydra.swift:896).
2. **Opener regex divergence:** `delegationOpener` accepts `hydra-leaving`; `streamedDelegations(in:)`
   and `HydraDelegationStreamParser` do not.
3. **Literal ```` ```hydra ```` inside prose** in `fallbackReportNote`'s `canDelegate` branch, which
   opens a code fence in rendered chat.
4. **Native policy contradiction:** the lead is told heads never go out through a fenced hydra block,
   and in the same bullet that Droppy Code sends them out if it writes one anyway.
5. **`verificationMarkers` is unanchored substring matching**, so an innocent line mentioning
   `gradle`, `pytest`, `eslint` or a path containing them is silently deleted from a head's brief.
6. **`"both changed"`** in `reportMessage` is emitted even when three or more heads touched one file.
7. **Three different "no model set" sentinels** across providers: `"default"` (Claude),
   `"auto"` (Copilot), non-empty check only (Codex).
8. **`unreadableBlockMessage(reason:)`'s `reason` branch is dead** — the single call site passes nil.
9. **`fallbackPreamble` evaluates `fallbackTurnNote` twice.**
10. **`fallbackPolicy` does not filter profiles** through `nativeRoutableProfiles`, so a Droppy-run
    lead can be told about profiles that tune nothing.
11. **Anti-slop text carries a U+2019 apostrophe and US spelling** (`product’s`, `colors`) that the
    porting repository's own lint rules would otherwise reject; it is model-visible text and must be
    reproduced byte-for-byte.

---

## 17. Unresolved

**UNRESOLVED.** `TextCleanup.singleLine(_:limit:)` and `JSONValue.parse(_:)` are used by the parsing
path but live outside the three named files; their exact truncation and leniency behaviour
(whether `singleLine` appends an ellipsis, whether `JSONValue.parse` accepts trailing commas or
comments) was not read in this pass and must be extracted before the delegation-entry titles and the
block parser can be called byte-exact.

**UNRESOLVED.** `AntiSlopPolicy.swift` was read in full but is outside this pass's named targets;
its line count and attribution header were not reconciled here.

**UNRESOLVED.** `ProviderKind.isAPIKeyBased` decides which providers keep the policy in the system
prompt versus prepend it to every message. The exact membership of that set was not extracted in
this pass and is load-bearing for parity.
