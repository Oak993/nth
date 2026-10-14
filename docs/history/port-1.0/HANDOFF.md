# HANDOFF: Marrow Windows port

For the **orchestrator** (Codex, running inside Marrow) and for the **owner**. Worker models get the
shorter `WORKER_BRIEF.md` instead. Written 2026-09-22.

---

## 1. Where everything is

| What | Where |
| --- | --- |
| **The project (open this folder in Marrow)** | `E:\Droppy\port` |
| Original macOS source, pinned, **read-only** | `E:\Droppy\reference\droppy-code` (commit `dd2b86efef9e77168f304a86e8a8411877578bbe`) |
| The C# solution | `E:\Droppy\port\Marrow.sln` |
| Work queue (662 packets) | `E:\Droppy\port\porting\tasks\queue.json`, human index `porting\tasks\INDEX.md` |
| One task packet | `E:\Droppy\port\porting\tasks\T-M2-001.md` and so on |
| Rules workers follow | `E:\Droppy\port\WORKER_BRIEF.md` |
| The binding contract | `E:\Droppy\port\porting\PORT_CONTRACT.md` and `porting\contract_amendments.md` |
| The app you orchestrate from | Marrow 0.2.1, installed at `C:\Users\omera\AppData\Local\Programs\Marrow\` |

Do **not** confuse the two Marrows. The installed Marrow 0.2.1 is the *tool* (a different product, built
from `C:\Users\omera\Downloads\Claude outputs\agent-workbench`). `E:\Droppy\port` is the *project*: an
exact-parity port of Droppy Code that will also ship under the name Marrow. Never copy code or features
from the old Marrow into the port.

## 2. Where the project stands, honestly

**Direct-bypass checkpoint, 2026-09-22:** the owner said to bypass workers again and continue.
Thirteen M2 packets are committed on `port/windows-exact-parity`; see
[the latest report](porting/reports/direct-bypass-20260922.md),
[the continuation report](porting/reports/direct-continuation-20260922.md)
and [the original sprint report](porting/reports/direct-sprint-20260922.md) for commits, tests,
evidence and restart instructions. The current worker bypass remains in force unless the owner
changes it. This checkpoint supersedes older progress and routing statements below.

| | |
| --- | --- |
| Source read and evidenced | 433 of 433 files (Gate A: 12/12) |
| Specification | 3,827 requirements, 1,029 UI records (Gate B: 8/8) |
| Stack | Avalonia 12.1.2 on .NET 10, decided with evidence (ADR-0002); probe P1 passed |
| Solution and Core implementation | 4 layered projects + 3 test projects, builds clean, 251 tests pass |
| **Packets completed** | **13 of 662 (1.96%); M2: 13 of 78 (16.67%)** |
| **Requirements implemented** | **85 of 3,827 (2.22%)** |
| **Requirements verified** | **0 of 3,827** |

Completed: AntiSlopPolicy, RecentCache, MergeRequestLink, Coding, Requests, MCP models and
transport resolution, all four MCP catalog packets (33 entries plus lookups/grouping), StdioFramer,
text cleanup, compact diffs, app metadata, file-edit construction and nilIfEmpty.
The application entry point remains a stub; full Gates C/D/E/F are incomplete. DONE is not VERIFIED.

Latest GitLab snapshot discovered: `417ab7d3676cdf258fb6b68444f0626aa6fdfbed`, separately
checked out at `E:\Droppy\reference\droppy-code-417ab7d3`. It is 32 commits ahead with
127 changed paths. **The specification and Gate A still refer to the original pinned SHA.**
Ten completed packets cite identical Git blobs in both snapshots. T-M2-023's only source change
replaces inline trimming with the equivalent Text.trimmed helper, reviewed against its definition.
T-M2-075/076's source only adds that new extension; covered behavior and the FileEdit carrier are
unchanged. GitLab main was rechecked at approximately 04:00 UTC and still matches this snapshot.
Other changed-source packets, especially Hydra and Provider, need a source/specification update.
Do not mark the new snapshot fully read merely because the old Gate A passes.

## 3. Roles

- **Owner:** rules on quarantined behaviours and pending decisions (section 10). Runs the four probes
  that need a real desktop. Nobody else can do either.
- **Orchestrator (Codex, as the chat's lead):** assigns packets, reviews every result against the source
  and the gates, commits, updates the queue. Writes no feature code itself except on HIGH-risk packets
  where it chooses to. **Workers never commit; only the orchestrator does.** That rule is what makes
  `check_protected.mjs` work.
- **Workers (heads):** implement one packet at a time under `WORKER_BRIEF.md`. Which model a packet
  goes to is decided by section 4a, not by availability.

## 4a. Budget and model routing (owner's cap: $10)

**The owner's Cast, as configured in Marrow on 2026-09-22.** Route to these exact entries:

| Cast entry | Endpoint | Model id | Role |
| --- | --- | --- | --- |
| Codex (Director) | Codex | GPT-6-Astra, effort High | orchestrator; reviews and commits |
| OpenRouter | OpenRouter | `nvidia/nemotron-3-ultra-550b-a55b:free` | STANDARD packets |
| OpenRouter | OpenRouter | `deepseek/deepseek-v4.1-flash` | HIGH packets |
| Z.ai (GLM) | Z.ai (native) | `glm-5.3-flash` | review of every HIGH packet |

Both OpenRouter entries share one account. **Never** assign a STANDARD packet to the DeepSeek entry by
mistake: the only thing separating the free route from the paid one is the model id.

**Owner's decision, 2026-09-22: total paid spend on workers is capped at $10.** Route every packet by
its `risk` field in `porting/tasks/queue.json`:

| Packets | Head provider | Why |
| --- | --- | --- |
| `risk: STANDARD` (558) | **Nemotron 3 Ultra, free** (OpenRouter) | Mistakes here are cheap to catch in review |
| `risk: HIGH` (104) | **DeepSeek v4.1 Flash, paid** (via OpenRouter: `deepseek/deepseek-v4.1-flash`) | A plausible but wrong translation here costs data |
| M0 probes | the owner, or DeepSeek for P5 and P6 | P2, P3, P4, P7 need an unlocked desktop |
| **Review of every HIGH packet** | **GLM 5.3 Flash, paid** (native Z.ai preset, $4.95 balance) | A second model catches a translation that compiles, passes its own tests and still differs from the Swift |

**GLM 5.3 Flash is a REVIEWER, not a worker.** Its cached-input price is about 17x DeepSeek's
($0.050 vs $0.003 per million on OpenRouter's listing), and a worker's traffic is mostly cached input,
so as a worker it would cost several times more per packet. As a reviewer it reads the packet, the
Swift source and the diff, with no build loop, which is a few turns rather than about 80. After
DeepSeek finishes a HIGH packet and before `mark.mjs ... DONE`, send GLM this and nothing else:

> Review only. Do not edit any file. Packet: `porting/tasks/<id>.md`. Swift source: the file the
> packet names under `E:\Droppy\reference\droppy-code`. Change under review: `git diff HEAD`.
> For each requirement id, say MATCHES or DIFFERS, and for every DIFFERS quote the Swift line and the
> C# line. List any user-visible string that is not verbatim, any number that was changed, anything
> present in the C# with no source line behind it, and any quarantined (BLK) behaviour implemented.

Any DIFFERS goes back to the worker. Codex still makes the final call. Estimated at $1.50 to $3 for
all 104 reviews; measure the first few and report if it runs higher.

Rules the orchestrator must follow:

1. **Never send a STANDARD packet to the paid model.** When the free model's daily ceiling runs out,
   STOP assigning STANDARD packets until it resets. Work on reviews, or on HIGH packets, in the
   meantime. Overflowing easy work onto the paid model is exactly how the $10 would disappear.
2. **The $10 is a hard cap by construction:** it is OpenRouter credit, and the OpenRouter API key
   Marrow uses carries a **$10 credit limit**, so requests fail
   rather than overspend. Before each HIGH packet, check the remaining balance. If it would not cover
   the packet at the measured per-packet cost, stop and report to the owner rather than starting work
   that cannot finish.
3. **Measure, then plan.** After the bake-off (section 5), read the real spend per HIGH packet from the
   OpenRouter activity page (or `GET https://openrouter.ai/api/v1/key`, field `limit_remaining`),
   multiply by the HIGH packets remaining, and report the projection to the owner.
   **Calibration from real usage (owner's report, 2026-09-22):** $10 on DeepSeek v4.1 bought about
   550M tokens, an effective **$0.018 per million**, which implies the large majority of tokens were
   served from cache. At that rate the planning estimate is about **$0.05 per packet** (assuming ~2.85M
   tokens per packet: roughly 80 turns at 35K context), so the 104 HIGH packets come to about $5.40
   and $10 covers roughly 190 packets. The price per token is now evidenced; the **tokens per packet
   on this job is still an assumption** until the bake-off measures it. Keep the remaining ~$4.60 as a
   buffer until then. If the bake-off confirms the estimate, offer the owner to move the hardest
   STANDARD packets (large M7 views, anything adjacent to the runtime) onto the paid model.
4. **If the projection exceeds the balance**, tell the owner before continuing. The lever that does not
   cost quality is `deepseek-v4-flash` (much cheaper output) for the simpler HIGH packets; the lever
   that does is moving some HIGH packets to the free model, which needs the owner's approval.
5. Record the model on every packet: `node porting/tools/mark.mjs <id> ASSIGNED --by <model id>`, so
   rejection rates per model are visible later.

Throughput under this plan is set by the free model's daily request ceiling
(`GET https://openrouter.ai/api/v1/key`, field `free_model_daily_requests`). 558 packets at roughly 80
requests each is on the order of 45,000 free requests; divide by the ceiling for the number of days.

## 4. One-time setup in Marrow

Each step below was checked against the running 0.2.1 build on 2026-09-22.

1. **Add the worker models.** *Settings > API endpoints*:
   - **OpenRouter** preset with your OpenRouter key. It serves both
     `nvidia/nemotron-3-ultra-550b-a55b:free` and `deepseek/deepseek-v4.1-flash`. The preset's model
     list is empty in 0.2.1, so type the ids.
   - **Z.ai (GLM)** preset with your Z.ai key, model `glm-5.3-flash`.
   - Put the **$10 on OpenRouter** (not on DeepSeek's own platform, which this setup never calls), and
     give the OpenRouter key Marrow uses a **$10 credit limit** in OpenRouter's key settings. That
     makes the cap hold even if more credit is added to the account later.
2. **Add the project.** In the sidebar, *Projects*, the `+` button, choose the folder `E:\Droppy\port`.
   Marrow reads `marrow.json` and adds six one-click scripts: Build, Test, Verify all gates, Check
   protected files, Negative gate test, Regenerate task packets.
3. **Give the project its instructions.** Open the project's settings and paste the whole of
   `WORKER_BRIEF.md` into its **Instructions** field. Marrow sends project instructions to every agent
   in that project, including heads, so every worker sees the rules on every turn.
4. **Fix the team limits before the first delegation. This one is critical.** *Settings > Teams* is set
   today to **Minutes: Wrap up 1, Stop 2**. A worker reading a Swift file, writing tests, then running
   `dotnet build` and `dotnet test` needs far more than two minutes, so every head would be stopped
   mid-task with half-written files. Either switch on **No limit** under Minutes, or raise Wrap up and
   Stop to at least 20 and 30.
5. **Cap parallel heads.** *Heads working at the same time* is **No limit** today. Set it to **2 or 3**.
   More than that will hit the provider's rate limit and heads will start failing on 429 errors.
6. Leave **Where heads work: Their own git worktree**. Each head then works in an isolated copy, and a
   bad result can be thrown away without touching the main checkout.
7. **Point heads at the worker model.** In the project's team settings, set the head provider to the
   DeepSeek endpoint you added in step 1. The lead stays **Codex**. (In 0.2.1 a head's provider resolves
   as the delegation's own choice, then the project's `headProvider`, then the lead's provider. An API
   endpoint is a valid head provider.)
8. **Check the budget before planning throughput.** For DeepSeek, look at your balance in the DeepSeek
   console. For OpenRouter, `GET https://openrouter.ai/api/v1/key` with your key returns your limits.

## 5. Before the first real packet: a bake-off

Nobody has measured any model on *this* job yet, so do not commit to one on price alone. Take three
low-risk packets from M2 (pure Core logic, no UI, no processes), for example `T-M2-001`, `T-M2-002` and
`T-M2-003`, and run each through the full loop in section 6 with **both** worker models: Nemotron 3
Ultra (free) and DeepSeek v4.1 Flash. The question is whether the free model is good enough for the
558 STANDARD packets section 4a routes to it. Compare:

- did the tests-first step produce tests that actually test the source's "Expected" text?
- how many review rounds before `mark.mjs ... DONE` accepted it?
- did it paraphrase any string, round any number, or invent anything?
- tokens and cost per packet (Marrow's usage display, or the provider's console)

Keep whichever model gets packets through the gate with the fewest rejections. Multiply its measured
cost per packet by 662 to get a real budget, rather than trusting anyone's estimate.

## 6. The per-packet loop

```
1. Pick the next TODO packet in milestone order from porting/tasks/INDEX.md.
   node porting/tools/mark.mjs T-M2-001 ASSIGNED --by deepseek-v4.1-flash

2. Delegate to a head:  "Do porting/tasks/T-M2-001.md. Tests first, then stop and report."

3. REVIEW THE TESTS before any implementation exists (this is where a test that merely restates its
   own implementation gets caught):
   - does each test encode the packet's "Expected" text, with the exact strings and numbers?
   - open the Swift source and spot-check two or three of them yourself
   node porting/tools/mark.mjs T-M2-001 TESTS_REVIEW

4. "Tests approved. Now implement T-M2-001 until they pass. Do not change the tests."
   node porting/tools/mark.mjs T-M2-001 IMPLEMENTING

5. REVIEW THE RESULT (section 7). Then either:
   node porting/tools/mark.mjs T-M2-001 DONE --note "<one line>"
     (this REFUSES unless protected files, Gate A, Gate B and the build and tests all pass)
   git add -A && git commit -m "T-M2-001: <what it ports>"
   or:
   node porting/tools/mark.mjs T-M2-001 REJECTED --note "<why>"
   then discard the head's worktree and reassign.
```

`DONE` moves the packet's requirements to **IMPLEMENTED**, never to **VERIFIED**. Verified needs Gate D
evidence per requirement, which later milestones build.

## 7. The orchestrator's review, every packet

```powershell
git status
git diff --stat
node porting/tools/check_protected.mjs          # must report nothing changed
node porting/tools/verify.mjs --gate A           # 12/12
node porting/tools/verify.mjs --gate B           # 8/8
node porting/tools/verify.mjs --gate C           # quarantine guard must PASS (the stub check will
                                                 # fail until the M6 entry point lands; expected)
node porting/tools/verify.mjs --gate D           # build clean, tests pass, none skipped
```

Then read the diff against the packet, and against the Swift source for anything that looks invented.
The gates catch structural cheating. Only reading catches a plausible but wrong translation.

**Reject on sight:** any edit under `porting/`, to a `.csproj`, `Directory.*.props`, `Marrow.sln` or
`LayeringTests.cs`; a test changed so that it passes; a paraphrased user-visible string; a rounded
constant; a quarantined behaviour implemented either way; a new NuGet package the worker added itself;
an "equivalent" Windows API the worker guessed at instead of stopping to ask.

**HIGH-risk packets** (104 of the 662; listed as HIGH in `INDEX.md`): process control, ConPTY, git
checkpoints and revert, Hydra merge, OAuth and secrets, the updater, the glass compositor, and window
construction. On these, review every line, not a sample. The orchestrator may also write these itself.

## 8. Order of work

| Milestone | Packets | What | Notes |
| --- | --- | --- | --- |
| M0 | 10 | Probes P2 to P11 | **P2, P3, P4, P7 need the owner at an unlocked desktop** (packets T-M0-001, 002, 003, 006). The rest a worker can run headless. |
| M2 | 78 | Core models and pure support | **Start here.** Pure logic, easiest to verify, and runs in parallel with M0. |
| M3 | 25 | Store and git | Depends on M0 P8 (file watch). Git packets are HIGH. |
| M4 | 145 | Providers and MCP | Depends on M0 P5 and P6 (ConPTY, Job Objects, termination). |
| M5 | 67 | Theme, chrome, glass, markdown, sound | Glass depends on M0 P2, P3, P4. |
| M6 | 88 | App shell, windows, model, runtime | Replaces the stub entry point; many HIGH. |
| M7 | 241 | Feature views | The bulk of the UI. |
| M8 | 8 | Update and packaging | Live update path is BLOCKED (UNK-0009). |

M2 can begin the moment setup is done. It does not wait for any probe.

## 9. Engineering decisions workers need (already decided)

- **Four layers, enforced by the compiler.** `Marrow.Core` depends on nothing. `Marrow.Services` and
  `Marrow.UI` depend only on Core. `Marrow.App` depends on all. `LayeringTests.cs` fails the build's
  tests if a worker adds a reference to get round this; it was verified by planting a violation and
  watching it fail.
- **Upstream broke its own layering in 8 places** (upstream's layers are one Swift module, so nothing
  enforced it). The port resolves them by moving code up a layer, as upstream's own AGENTS.md prescribes:
  `MCP.swift`'s use of `MCPProxy` and `Storage.root` moves to Services; `WallpaperStore` moves to App;
  `MarkdownView` renders `hydra` fenced blocks through a hook the App layer supplies rather than naming
  a chat view; Core's `Color` extension becomes a plain RGBA value, converted in UI.
- **Persistence must match upstream byte for byte:** JSON with ISO-8601 dates and sorted keys; lenient
  decoding where a missing *or wrong-typed* key silently gives the default; `Lenient<T>` lists where one
  bad element becomes null without failing the list; settings keys keep their exact upstream names and
  defaults. Source: `DroppyCode/Core/Support/Coding.swift`.
- **Isolated data folder during development.** The port's store root would be `%LOCALAPPDATA%\Marrow`,
  which is **the installed Marrow's own data folder**. Every test and every run sets
  `MARROW_PORT_DATA_DIR` to a throwaway folder (UNK-0024). A worker who skips this can damage the very
  app driving the work.
- **The `cmd` executable.** Command Code's CLI is literally named `cmd`, which on Windows resolves to
  `C:\Windows\System32\cmd.exe`. Candidates are rejected by their Windows version resource (PA-0016).
- **Terminal and commands run in PowerShell.** Marrow's terminal shell is *PowerShell 7, then Windows
  PowerShell*, so workers' `run_command` calls use PowerShell syntax.
- **Glass is Acrylic over the desktop** (OD-0005) and a custom SKSL shader over app-drawn content,
  with `.continuous` corners as a superellipse, exactly as probe P1 demonstrated.

## 10. What is blocked, and on whom

**Quarantined: owner decides, neither option approved** (`porting/blocked_requirements.json`). Gate C
fails if any of these is implemented either way, so no worker can settle one by accident:

| Entry | Behaviour |
| --- | --- |
| BLK-0001 | Custom MCP server delete: unconfirmed, destroys stored secrets |
| BLK-0002 | A malformed custom-MCP variable silently discards every custom server |
| BLK-0003 | A checkpoint can be truncated silently; a later revert deletes work |
| BLK-0004 | Revert can act on the wrong files and report success |
| BLK-0005 | The legacy migration folder name diverges under the rename |
| BLK-0006 | API key written in plain text when protected storage refuses it |
| BLK-0007 | Plan limits read other apps' credential files and send the token on |
| BLK-0008 | The read-only command classifier doubles as the auto-approval gate |
| BLK-0009 | Remove project: unconfirmed, ignores confirm-before-deleting |
| BLK-0010 | An undecodable library.json starts empty and the next save overwrites it |
| BLK-0011 | An unreadable title flag lets the auto-titler overwrite a typed name |
| BLK-0012 | Disconnect reports a credential revoked when it was not |

Seven of these are **marker mode**: the behaviour is one path inside code the app needs, so the worker
implements the surrounding code and leaves `// BLOCKED BLK-xxxx` at that path. Gate C checks the marker
is present.

**Also open:** 27 ordinary product choices in `porting/owner_decisions_pending.md` (window buttons on
the left or right, keyboard shortcut notation, key labels by position or layout, and so on); UNK-0023
(always draw an app backdrop?); UNK-0024 (the shipping data folder once both Marrows exist).

## 11. Risks to watch

- **Plausible but wrong.** A cheap model's commonest failure here is code that compiles, passes its own
  tests, and quietly differs from the source: a string reworded, a timeout rounded, a branch dropped.
  Tests-first plus reading the diff against the Swift is the only defence.
- **Invented APIs.** Avalonia 12 and SkiaSharp 3 differ from the versions most models learned. A worker
  that "uses" an API that does not exist will fail the build, which is fine. One that picks a *real but
  wrong* API will not fail. Watch the HIGH-risk packets.
- **Visual parity cannot be verified** from here (no macOS access, OD-0001). Source-defined geometry,
  colours and strings can be; the rendered look cannot. Never let a report say "pixel-perfect".
- **The final result is capped** at *VERIFIED WITH USER-APPROVED DEVIATIONS*, because of the Marrow
  name and the Acrylic glass. That is expected, not a failure.

## 12. Tools

| Command | Purpose | Who |
| --- | --- | --- |
| `node porting/tools/verify.mjs [--gate X]` | The single verification entrypoint; exit 1 if anything mandatory is unmet | anyone |
| `node porting/tools/check_protected.mjs` | Detects edits to files workers must not touch | orchestrator |
| `node porting/tools/mark.mjs <T-id> <STATUS>` | Records a packet's outcome; refuses DONE unless the gates pass | orchestrator |
| `node porting/tools/gen_tasks.mjs` | Regenerates packets from the spec, keeping queue status | orchestrator |
| `node porting/tools/negative_test.mjs` | Proves the gate still catches six injected faults | orchestrator |
| `node porting/tools/merge_spec.mjs` | Rebuilds the parity matrix from `porting/spec/` | orchestrator |
