# IMPLEMENTING: Swift-to-C# conventions for the Marrow port

Status: **PROPOSED**, 2026-09-22, by the conventions judge, for adoption as `porting/IMPLEMENTING.md`. Once the
orchestrator adopts this file, every rule in sections 1 to 6 binds every implementing agent. The engineering
decisions in section 7 (ED-064 to ED-086) are proposals: each says what it touches and which ones need an
orchestrator action (a project setting, a new project, a tool install). Nothing here changes a recorded decision
silently; where a rule restates or refines one, it names it.

- Baseline B2: Droppy Code 1.6.2, commit `1fbf81e61e28a079508a074c01ee1830a9cc23dc`, read-only at
  `E:\Droppy\reference\droppy-code-v1.6.2`.
- Target: C# on .NET 10 (SDK 10.0.401), Avalonia 12.1.2, xUnit 2.9.3, four layers (HANDOFF section 9, ADR-0002).
- Judged: `E:\Droppy\work\conventions\survey.md`; `draft-A.md` (fidelity first) with proto-A, 134 of 134 tests
  passing; `draft-B.md` (concurrency and lifecycle first) with proto-B, 71 of 71 plus an Avalonia 12.1.2 headless
  binding probe ALL PASS; `draft-C.md` (binding and testability first) with proto-C, 54 of 54. Every prototype was
  rebuilt and rerun for this judgement (Appendix C).
- Consolidated: `E:\Droppy\work\conventions\foundation-proto`, the foundation helper types of section 4 with their
  tests: 204 tests (190 Core, 14 UI including headless Avalonia), 0 warnings under the proposed settings, green
  in 5 consecutive runs.
- Appendix A scores every draft per construct; Appendix B lists the Swift behaviours no one could settle (the
  oracle list, O-01 to O-64); Appendix C is the evidence.

Labels, as the port uses them: **SOURCE FACT** (the pinned tree, `path:line`), **SEMANTICS** (documented or
well-established Swift, Foundation or .NET behaviour), **ENGINEERING DECISION**, **UNVERIFIED** (believed, not
settled; always with its oracle id `O-nn`). "Proved" below means a named test in foundation-proto or a draft's
prototype passes on this machine; a C#-side proof never proves the Swift side.

---

## 1. Where things are and what binds you

### 1.1 Paths

| What | Where |
|---|---|
| The port (branch `port/windows-exact-parity`); you work here, you never commit | `E:\Droppy\port` |
| Pinned Swift source, B2, **read-only** | `E:\Droppy\reference\droppy-code-v1.6.2` |
| B1 history (1.6.0); never implement from it, although HANDOFF.md and WORKER_BRIEF.md still name it | `E:\Droppy\reference\droppy-code` |
| Contract, amendments, owner rulings (OD-0001 to OD-0014) | `porting/PORT_CONTRACT.md`, `porting/contract_amendments.md`, `porting/owner_decisions.md` |
| Recorded engineering decisions (ED-001 to ED-063), platform adaptations, stack decision | `porting/engineering_decisions.md`, `porting/platform_adaptations.md`, `porting/adr/0002-stack-avalonia.md` |
| Requirements and UI records, quarantine list | `porting/parity_matrix.json` (PR-...), `porting/ui_contract.json` (UI-...), `porting/blocked_requirements.json` |
| Upstream defects the port reproduces | `porting/upstream_defects.md` |
| Unit order (one Swift file or one dependency cycle per unit) | `porting/deps/swift_deps.json` |
| Verification entrypoint; per-requirement Gate D status | `node porting/tools/verify.mjs [--gate X]`; `node porting/tools/req_verify.mjs` |
| Foundation helpers (section 4) and their tests | `E:\Droppy\work\conventions\foundation-proto` (to be copied into the port by the orchestrator) |

### 1.2 Authority

Highest first: `PORT_CONTRACT.md`; `contract_amendments.md`; owner decisions and every `deviation` field in the
spec; the pinned Swift source; recorded EDs, PAs and ADRs; this guide; `CLAUDE.md` and `AGENTS.md` (indexes). When
this guide disagrees with anything above it, the higher one wins: follow it and report the conflict (section 6).

### 1.3 The rules that do not bend (contract section 2, CLAUDE.md)

1. Copy, never invent: every behaviour, label, number, default, key, message, order and error path comes from the
   pinned Swift. Not from a README, a screenshot, a name, memory, another app, or the Electron Marrow tree at
   `C:\Users\omera\Downloads\Claude outputs\agent-workbench` (a different, deliberately divergent product).
2. Strings byte for byte, numbers exact. The only permitted differences are owner rulings (OD-0002 product
   name, OD-0012 F1 and the rest) and each requirement's `deviation` field.
3. Upstream defects are reproduced (AM-0001 Effect B). A consequential one (C1 to C6) is escalated, never fixed or
   copied on your own. Quarantined behaviour (BLK-nnnn) is never implemented either way: leave
   `// BLOCKED BLK-nnnn: owner decision pending` where marker mode applies.
4. No stub, mock, canned result or no-op where the source has behaviour, unless the source itself does that.
5. Never weaken a test, edit an expectation to match output, skip a test, or relabel missing work.
6. Never claim you read, ran or verified anything without the artifact. Compilation is not verification;
   implementation is not verification.
7. Never touch the reference folder, user data, credentials or the owner's installed apps. Never read an
   `auth.json` or credential file. Never send a prompt to a real AI CLI from a test. Before running anything, set
   `MARROW_PORT_DATA_DIR` to a throwaway folder.
8. Never add a NuGet package, a project reference or a project: propose it (section 6).

### 1.4 The unit loop

1. A unit is one Swift file, or one dependency cycle of `swift_deps.json`, taken in its level order. Read the
   whole Swift file, every requirement and UI record citing it, and the Swift of every callee whose semantics you
   rely on. Read the recorded EDs and PAs that name its area.
2. Tests first (section 5), then stop and report. The tests are reviewed adversarially before any implementation.
3. Implement until the tests pass without touching them. The diff is reviewed against the Swift, line by line
   for HIGH-risk units. The orchestrator merges and commits; you never do.
4. Never edit protected files: anything under `porting/`, any `.csproj`, `Directory.*.props`, `Marrow.sln`,
   `LayeringTests.cs`, `marrow.json`, `WORKER_BRIEF.md`, `HANDOFF.md`, and (once adopted) `.editorconfig`.
5. Done means all of these pass, with the real output in your report:

   ```
   dotnet build Marrow.sln -c Debug          (warnings are errors)
   dotnet test Marrow.sln                    (nothing failed, nothing skipped)
   node porting/tools/check_protected.mjs
   node porting/tools/verify.mjs --gate A
   node porting/tools/verify.mjs --gate B
   node porting/tools/req_verify.mjs         (your Req ids VERIFIED or UNBOUND, never NOT_RUN or FAILING)
   ```

6. The report: each requirement id and the test that proves it; every source ambiguity with its `file:line`;
   every guess (the right number is zero); every UNVERIFIED expectation with its `O-nn`; the exact commands and
   their real output.

### 1.5 Markers in code

Greppable, and nothing else is used for these purposes:

```csharp
// Source: DroppyCode/Core/Support/Shell.swift (B2 1fbf81e), :1-457.       file header (R-FILE-4)
// PORT-ONLY: <why this file has no Swift twin>.                             file header in Port/ only
// :72-75                                                                    a member or block citation
// DEVIATION OD-0012|ED-nnn|PA-nnnn: <what differs from the source>
// UPSTREAM DEFECT <n>, reproduced
// UNVERIFIED O-nn: <the Swift behaviour this line assumes>
// BLOCKED BLK-nnnn: owner decision pending
```

### 1.6 What this guide refines in older instructions

- WORKER_BRIEF "Dates are ISO-8601. Keys are written sorted.": true of the storage coder only
  (`JSONEncoder.storage`, `Coding.swift:19-34`). A plain `JSONEncoder()` (11 sites) writes a Date as a Double of
  seconds since 2001 and leaves keys unsorted; JSONSerialization never escapes slashes. See R-CODE-4, R-CODE-6.
- WORKER_BRIEF "@MainActor means Dispatcher.UIThread": Core and Services cannot reference Avalonia, so every
  layer uses `MainActor` (R-ISO-1). Only `Port/` code names `Dispatcher.UIThread`.
- WORKER_BRIEF and HANDOFF name the B1 reference folder: implement from B2 only.
- HANDOFF section 9 "persistence must match upstream byte for byte": kept, and enforced by the Swift byte writer
  (ED-072), which closes the emoji, U+2028, U+FEFF and number-text differences the current `Coding.Encode` has.
- ED-009, ED-022, ED-024, ED-027, ED-034, ED-042 and ED-044 name JavaScript or Node APIs (they predate ADR-0002). Their
  intent stands; the C# forms are R-STR-4 and R-STR-6 (ED-009), R-FMT-2 (ED-034), ED-126 (ED-027) and R-PROC-3 (the other four).
  One correction: ED-009 says Swift `lowercased()` is "exactly `String.prototype.toLowerCase()`". It is not:
  JavaScript applies the Final_Sigma context rule and Swift maps each scalar alone (R-STR-4, O-07).
- ED-054(3) points at `LEGACY_APP_NAMES` in the Electron Marrow tree. That mechanism is not part of this port;
  ED-054(1), (2) and (4) stand, and (3) has nothing to build here (reported to the orchestrator).

---

## 2. Layering, file placement, naming

### 2.1 Layers

| Project | May reference | Holds |
|---|---|---|
| `Marrow.Core` (net10.0) | .NET only: no package, no Marrow project, no UI framework | `DroppyCode/Core` twins; `Core/Port` foundation |
| `Marrow.Services` (net10.0-windows) | Marrow.Core | `DroppyCode/Services` twins |
| `Marrow.UI` (net10.0) | Marrow.Core, Avalonia, Avalonia.Skia | `DroppyCode/UI` twins; `UI/Port` view support |
| `Marrow.App` (net10.0-windows) | all | `DroppyCode/App` twins; start-up bindings |

`LayeringTests` fail the build's tests if a reference crosses a layer; never add one to get round it. Upstream
broke its own layering in 8 places; the port moves that code up a layer exactly as HANDOFF section 9 lists
(`MCP.swift`'s use of `MCPProxy` and `Storage.root` to Services, `WallpaperStore` to App, `MarkdownView`'s hydra
fences through an App hook, Core's `Color` extension to a plain RGBA value). A moved type keeps its Swift file name
and its header says where it came from.

### 2.2 R-FILE-1. One C# file per Swift file, same relative path, same base name

`DroppyCode/<Layer>/<dirs>/<Name>.swift` becomes `src/Marrow.<Layer>/<dirs>/<Name>.cs`, `+` included:
`App/AppModel+Hydra.swift` is `src/Marrow.App/AppModel+Hydra.cs` (six such files exist). Every type the Swift file
declares, private helpers included, is in its twin; an `extension X` in another Swift file is a `partial` part of X
in that file's twin. Never split a Swift file into several C# files and never merge two. (The existing
`FileEditStats` flattening in `Support/Text.cs` is recorded in that file.)
*Pitfall avoided:* one-type-per-file makes a port unfindable from the read ledger and the dependency graph.

### 2.3 R-FILE-2. Port-only code lives only in `Port/`

Code with no Swift twin (the foundation of section 4, start-up bindings) lives in `src/Marrow.<Layer>/Port/`,
namespace `Marrow.<Layer>.Port`, and starts `// PORT-ONLY: <why>`. Every file outside `Port/` has a Swift twin;
every file inside has none. The existing port-only `Support/FoundationText.cs` moves to `Port/` (ED-085). A Swift
twin keeps its Swift-facing API even when the machinery is port-only: `value(_:default:)` and `Lenient` are
Coding.swift's, so `Coding.Value` and `Coding.LenientList` stay in `Support/Coding.cs` and call `SwiftDecoding`.

### 2.4 R-FILE-3. Namespaces

`Marrow.<Layer>` plus the Swift folder path with dots: `App/Views/Chat` is `Marrow.App.Views.Chat`. Tests:
`Marrow.<Layer>.Tests.<folders>`, in `tests/Marrow.<Layer>.Tests/<folders>/<Name>Tests.cs`.

### 2.5 R-FILE-4. Citations to B2 `file:line`

The first comment after the usings is `// Source: DroppyCode/<path>.swift (B2 1fbf81e), :<first>-<last>.`; a
partial port names the lines not yet ported and why. Members, and statement blocks inside long members, carry
`// :<line>` or `// :<a>-<b>` (another file: `// <File>.swift:<a>-<b>`); view code cites each modifier line.
B2 line numbers only: B1 citations point at the wrong code in the 115 changed files. Citations are checked by a
tool (ED-081 proposes `porting/tools/cite_check.mjs`, from proto-C, which found four wrong citations on first run).

### 2.6 Names

- **R-NAME-1.** Type names are Swift's, verbatim, acronyms included: `MCPTransport`, `JSONValue`, `RPCID`,
  `ACPSession`. (`JsonValue` would collide with `System.Text.Json.Nodes.JsonValue` and break grep between the trees.)
  Generic parameters take a `T` prefix: `Value` becomes `TValue`. Nested Swift types stay nested.
- **R-NAME-2.** Members and enum cases: the Swift name with its first letter upper-cased and every other letter
  kept: `catalogID` is `CatalogID`, `isEnabled` is `IsEnabled`, `opencodeGo` is `OpencodeGo`. Wire names (raw
  values, JSON keys, defaults keys, argv flags) never come from C# names: they are written out.
- **R-NAME-3.** A member named like its nested type keeps the type and prefixes the member with the owner's
  name, cited: `MCPField.Kind` and the property `FieldKind` (`// Swift: kind`).
- **R-NAME-4.** Extensions: of an app type in another file, a `partial` part; of an enum, `static class
  <Enum>Extensions`; of a library type (`String`, `KeyedDecodingContainer`, `Color`) and free functions or globals,
  a `static class` named after the Swift file. Across assemblies (a Services, UI or App file extending a type of a
  lower assembly), one `static class` named after the Swift file with C# 14 extension blocks, per ED-164.
- **R-NAME-5.** Argument labels become parameter names; a call site names an argument where the Swift label
  carries meaning. A failable `init?` is `static X? Create(...)` (or a `TryX` method), never a throwing
  constructor; a throwing `init` is a constructor that throws.
- **R-NAME-6.** Access: `private`/`fileprivate` is `private`; module-internal is `public` when another layer uses
  it (four assemblies replace one Swift module), otherwise `internal`.
- **R-NAME-7.** A ported `async` function keeps its Swift name with no `Async` suffix (`finished(within:)` is
  `Finished(...)`) and takes no `CancellationToken` parameter the Swift function lacks: cancellation is ambient
  (R-TASK-2). Port infrastructure may use .NET names.

### 2.7 Text in source

User-visible strings, keys, messages and argv values are byte-for-byte copies (R-STR-9 for how to write them).
The only changes are the rename points of OD-0002 and OD-0012 F1 ("Droppy Code" to "Marrow" wherever the app shows
or sends it, except `droppy-code.json` and `DROPPY_CODE_PROJECT_ROOT`) and each requirement's `deviation` field,
each marked `// DEVIATION OD-nnnn`. Comments are ASCII and use no em dashes (the house style ED-015 and ED-030 name).

---

## 3. Construct-by-construct rules

Each rule gives the one C# shape, a short example, the pitfall it prevents and its evidence (a test in
foundation-proto unless another prototype is named). The helper types it uses are specified in section 4. Survey
section numbers are cited as "survey N".

### 3.1 The runtime model in one paragraph

There is one **main executor**: the Avalonia UI thread in the app, a `ManualMainLoop` in tests, reached only
through `MainActor`. Every Swift `actor` is a **strand** (a serial executor on the pool): one synchronous segment at
a time, released at every `await`. Everything nonisolated-and-`@concurrent`, detached, or a child task runs on the
**pool**. Every clock read and every wait goes through **`SwiftClock`** (a BCL `TimeProvider` a test replaces). The
**current Swift task** is ambient (`SwiftTask`): every task started by the helpers carries its own cancellation,
any code running inside it sees that cancellation, and work that is not a task (a GCD block, a timer, a view
update) runs outside any task. Nothing in product code blocks one executor waiting for another.

Build setting behind it (SOURCE FACT, `project.yml:17-18`): Swift 6 with `SWIFT_APPROACHABLE_CONCURRENCY: YES`,
so `NonisolatedNonsendingByDefault` applies: an unannotated nonisolated `async` function runs on its caller's
executor, and only `@concurrent` functions (20 sites) leave it (survey 3).

### 3.2 The main actor and isolation

**R-ISO-1. `@MainActor` is `MainActor` over `IMainExecutor` (ED-065).** Marrow.App calls
`MainActor.Bind(AvaloniaMainExecutor.Instance)` once at start-up, before any window; tests install a loop with
`TestRuntime`. Product code never names `Dispatcher.UIThread` outside `Port/`. A `@MainActor` type is an ordinal
class whose members assume the main thread and do not re-check on each call; a `@MainActor @Observable` type
derives `MainActorModel`, which checks every tracked assignment (R-OBS-6). An unbound main actor traps rather than
running work nowhere.
*Evidence:* `HeadlessTests.The_main_actor_bound_to_Avalonia_runs_posts_and_spawns_FIFO_on_the_UI_thread_never_inline`
(Avalonia 12.1.2 headless), `MainActorTests.An_unbound_main_actor_traps_instead_of_running_nowhere`, proto-B
`avalonia-binding.log`.

**R-ISO-2. Nonisolated `async` is a plain `async` method; `@concurrent` is `Task.Run` at the boundary.** An
unannotated nonisolated async function becomes a plain `async Task` method with no `ConfigureAwait`: called on
main it runs on main to its first await and resumes on main, which is `nonisolated(nonsending)`.

```csharp
// Core/Support/Shell.swift:35-36  @concurrent static func run(...)
public static Task<ShellResult> Run(string executable, IReadOnlyList<string> arguments /* ... */) =>
    Task.Run(() => RunBody(executable, arguments /* ... */));
```

*Pitfalls:* `ConfigureAwait(false)` in main-actor code resumes on the pool (a race Swift makes impossible);
`Task.Run` around a function that is not `@concurrent` reorders the side effects Swift runs before its first
suspension. `ConfigureAwait(false)` appears only inside `Port/` (ED-076 source scan).
*Evidence:* `MainActorTests.A_plain_async_call_runs_synchronously_up_to_its_first_await_and_Task_Run_does_not`,
`ConfigureAwait_false_leaves_the_main_actor`, `Awaits_inside_a_main_body_resume_on_main`.

**R-ISO-3. The interleaving invariant.** Main-actor code interleaves only at `await`. Add no `await` between a
read and a write of main-actor state that the Swift code does synchronously, and never make a Swift synchronous
function `async` in C#. The `sessionEpoch`, `finishingTurnID` and `unsentSessionID` ordering rules (ADR-0002)
depend on it.
*Evidence:* `MainActorTests.An_await_added_between_read_and_write_loses_an_update` (faithful: 2; one extra await: 1).

**R-ISO-4. Calls into and around the main actor.**

| Swift | C# | Semantics kept |
|---|---|---|
| `DispatchQueue.main.async { }` (12) | `MainActor.Post(() => ...)` | a later turn, never inline, even from main; outside any task |
| `Task { @MainActor in ... }`, or `Task { }` in main-actor code | `MainActor.Spawn(async () => ...)` | enqueued: starts after the creator returns; a new task |
| a throwing one, `Task<T, Error>` | `MainActor.SpawnThrowing(async () => ...)` | a thrown error faults `.Value` |
| `await MainActor.run { }` (9) | `await MainActor.Run(() => ...)` | inline, no suspension, when on main; one hop otherwise; same task |
| `await x.f()`, `f` a `@MainActor async` member, from off main | `await MainActor.Invoke(() => x.F())` | inline on main; else the whole body on main; same task |
| `await x.g()`, `g` a `@MainActor` sync member, from off main | `await MainActor.Run(() => x.G())` | as above |
| `MainActor.assumeIsolated { }` (14) | `MainActor.AssumeIsolated(() => ...)` | synchronous; traps off main; never replaced by Post |
| `DispatchQueue.main.asyncAfter(deadline:execute: item)` (7) | `var item = MainActor.AsyncAfter(delay, () => ...)`; `item.Cancel()` | timer from SwiftClock; cancellable until it starts |

The places that are off main in the port, where Swift needs an `await` to reach main and C# needs `Run` or
`Invoke`: `@concurrent` bodies, `TaskHandle.Detached` bodies, task-group and `async let` children, actor bodies,
`SerialQueue` and `GlobalQueue` work, process reader callbacks, timer callbacks, `FileSystemWatcher` events.
*Pitfalls:* replacing `assumeIsolated` with `Post` defers the body a turn; calling a spawned body directly runs it
before the creator's next line; `Task.Run` for a main-actor task moves main-actor state to the pool.
*Evidence:* `MainActorTests` (`Post_never_runs_inline_even_from_main`, `Spawned_main_task_starts_after_the_creator_returns`,
`Calling_the_async_lambda_directly_reorders_the_creator`, `Run_is_inline_when_already_on_main_and_one_hop_otherwise`,
`Invoke_runs_a_main_actor_async_body_on_main_across_its_awaits`, `AssumeIsolated_traps_off_main_and_runs_inline_on_main`,
`AsyncAfter_work_item_cancelled_after_the_timer_fired_does_not_run`).

**R-ISO-5. `nonisolated`, `Sendable` and friends.** A `nonisolated` synchronous member (107 sites) is a plain
member that touches no main-actor state (`TokenLedger.dayKey` is shared by background scans; adding a check breaks
them). `nonisolated(unsafe) static var` (3) is a plain static field, main-only by discipline, with no lock (a lock
changes timing and hides misuse). A `Sendable` struct is an immutable record with value collections (R-VAL-2). An
`@unchecked Sendable` class (10) ports its hand locking literally with `Mutex` or `SerialQueue`. An `@Sendable`
closure is a `Func`/`Action` capturing immutable values. A stored `@MainActor (X) -> Void` is an `Action<X>` whose
invoker calls it only on main.

### 3.3 Tasks and cancellation

**R-TASK-1. A stored Swift `Task` is a `TaskHandle` (ED-066).** `x?.cancel()` is `x?.Cancel()`, `x = nil` is
`x = null`, `await x.value` is `await x.Value` (or `await x.Completion` with no result), `x == y` is
`ReferenceEquals` (`ThumbnailCache.swift:37`, `inFlight[key] == task`). Where each task starts:

| Swift | Created in | C# |
|---|---|---|
| `Task { }` | main-actor code | `MainActor.Spawn` |
| `Task { @MainActor in }` | anywhere | `MainActor.Spawn` |
| `Task { }` | an actor method | the actor's protected `Spawn` |
| `Task { }` | nonisolated code | `TaskHandle.Detached` |
| `Task.detached(priority:) { }` (24) | anywhere | `TaskHandle.Detached` (priority dropped, ED-066) |
| `await Task.detached { }.value` | anywhere | `await TaskHandle.Detached(...).Value` |
| any of these throwing | | `SpawnThrowing`, `DetachedThrowing` |

An unstructured task is never cancelled by its creator's cancellation. A non-throwing Swift task can only end in a
trap if its body throws, so a C# exception escaping a non-throwing task's body is not caught: it reaches the main
loop or the pool and ends the process, as Swift would (ED-078).
*Evidence:* `SwiftTaskTests.Cancelling_a_task_does_not_cancel_tasks_it_spawned`, `Task_handles_compare_by_identity`,
`A_throwing_task_delivers_its_error_and_a_trap_in_a_task_escapes_to_the_main_loop`.

**R-TASK-2. Cancellation is ambient, as in Swift (ED-066).**

| Swift | C# |
|---|---|
| `Task.isCancelled` (102) | `SwiftTask.IsCancelled` (false outside any task) |
| `try Task.checkCancellation()` (40) | `SwiftTask.CheckCancellation()` (throws `CancellationError`) |
| `throw CancellationError()` | `throw new CancellationError()` |
| `catch is CancellationError` | `catch (CancellationError)` |
| a BCL API that takes a token | pass `SwiftTask.CancellationToken` |

Any function called inside a task, synchronous or not, sees that task's cancellation; an actor call and a
`MainActor.Run`/`Invoke` stay in the caller's task; work posted with `MainActor.Post`, `AsyncAfter`,
`SerialQueue.Async` or `GlobalQueue` runs outside any task, as a GCD block does. A BCL `OperationCanceledException`
is not `CancellationError`: convert it at that boundary to what the Swift API threw (URLSession throws
`URLError(.cancelled)`, not `CancellationError`), and never let an `HttpClient` timeout read as a cancellation.

```csharp
// App/Views/Sidebar/SidebarView.swift:396-402
if (!IsListScrolling) IsListScrolling = true;
scrollSettle?.Cancel();
scrollSettle = MainActor.Spawn(async () =>
{
    await SwiftTask.TrySleep(TimeSpan.FromMilliseconds(150)); // :399
    if (SwiftTask.IsCancelled || !IsListScrolling) return;     // :400
    IsListScrolling = false;                                    // :401
});
```

*Choice:* all three drafts threaded explicit `CancellationToken` parameters. That adds a parameter no Swift
signature has, needs every call site to pass the right token (a wrong or `None` token silently loses
cancellation), and cannot reach the synchronous helpers that check `Task.isCancelled`. The ambient task keeps
Swift's call shape and semantics; the only hazard, an `ExecutionContext` carrying a task into work that is not a
task, is handled once inside the helpers and is tested.
*Evidence:* `SwiftTaskTests.Task_isCancelled_is_visible_to_any_function_called_inside_the_task`,
`Outside_any_task_isCancelled_is_false_and_checkCancellation_does_not_throw`,
`Work_posted_from_a_cancelled_task_runs_outside_any_task`,
`ActorTests.An_actor_call_stays_in_the_callers_task_and_sees_its_cancellation`.

**R-TASK-3. `Task.sleep` (186 sites; ED-068).** `try? await Task.sleep(for: d)` (181) is
`await SwiftTask.TrySleep(d)`: it never throws, returns `false` when cancelled (the `nil` of `try?`), and the Swift
code after it runs and checks the task itself. `try await Task.sleep(for: d)` (5) is `await SwiftTask.Sleep(d)`:
it throws `CancellationError`, also when the task was cancelled before it started. A zero or negative duration
suspends once and resumes after work already queued (O-21); there is no 49.7-day cap; the clock is `SwiftClock`.
`.seconds(x)` with a Double is `SwiftTask.Seconds(x)` (negative kept, huge saturates, NaN or infinity trap: O-22);
`.milliseconds(n)` is `TimeSpan.FromMilliseconds(n)`. A sleep that emulates a frame tick (ThreadTimeline.swift:1168, :1211, :1223,
HydraFusedPairIcon.swift:40; not the Services flush delay at OpenAICompatibleSession.swift:224) is
`FrameClock.TrySleep` (R-VIEW-10, ED-117 (5)), because Windows timers quantize to about 15.6 ms; never call `timeBeginPeriod`.
*Pitfalls:* `Task.Delay(t, ct)` throws on cancel where 181 sites continue, throws for a negative span
(`AutoContinue.swift:105` passes one), waits forever at exactly -1 ms, and rejects more than 49.7 days.
*Evidence:* `SwiftTaskTests.TrySleep_returns_false_on_cancel_and_the_code_after_it_runs`,
`Sleep_throws_CancellationError_when_the_task_was_cancelled_before_it_started`,
`A_negative_or_zero_sleep_suspends_once_instead_of_throwing_or_waiting_forever`,
`A_sixty_day_sleep_is_accepted_and_completes`, `Seconds_traps_on_a_non_finite_interval_and_saturates_a_huge_one`.

**R-TASK-4. `withTaskCancellationHandler` (9).** `withTaskCancellationHandler { op } onCancel: { h }` is
`await SwiftTask.WithCancellationHandler(() => op, () => h)`. `h` runs at once on the cancelling thread, inside
`Cancel()`, and before `op` when the task is already cancelled. It may run on any thread: if it touches main-actor
state it hops (`MainActor.Spawn`), exactly as `JSONRPC.swift:82-86` does, and it takes no lock the canceller holds.
*Evidence:* `SwiftTaskTests.Cancellation_handler_runs_before_the_operation_when_already_cancelled`,
`Cancellation_handler_runs_inline_on_the_cancelling_thread`,
`ContinuationTests.Rpc_request_cancelled_while_pending_throws_cancellation_on_main`.

**R-TASK-5. Task groups and `async let` (6 and 9).**

| Swift | C# |
|---|---|
| `await withTaskGroup(of: T.self) { group in ... }` | `await TaskGroup<T>.Run(async group => { ... })` (`TaskGroup` for `Void`) |
| `group.addTask { ... }` | `group.AddTask(async () => ...)` |
| `await group.next()` / `while let (a, b) = await group.next()` | `await group.Next()` / `while (await group.Next() is (true, var (a, b)))` |
| `for await r in group` / `for await _ in group` | `await foreach (var r in group)` / `while (await group.Next())` |
| `group.cancelAll()`, `group.isCancelled` | `group.CancelAll()`, `group.IsCancelled` |
| `async let x = f()` ... `await x` | `await using var scope = new AsyncLetScope();` `var x = scope.Start(() => F());` ... `await x` |

Children run on the pool as child tasks: they see the parent task's cancellation and `CancelAll`, and a child
calling a `@MainActor` member hops with `MainActor.Invoke`/`Run`, as the Swift child closure hops at its `await`.
The group scope awaits every child before returning (it does not cancel them); an `async let` never awaited is
cancelled and awaited at scope exit. Which executor an `async let` child starts on is UNVERIFIED (O-20); the rule
keeps each callee's own isolation, which is observably the same at all 9 sites.

```csharp
// Services/Providers/ProviderRegistry.swift:340-354, refreshAll(): at most probeWidth checks at once
await TaskGroup.Run(async group =>
{
    while (next < providers.Count && next < ProbeWidth)
    {
        var provider = providers[next];
        group.AddTask(() => MainActor.Invoke(() => Refresh(provider)));
        next += 1;
    }
    while (await group.Next())
    {
        if (next >= providers.Count) continue;
        var provider = providers[next];
        next += 1;
        group.AddTask(() => MainActor.Invoke(() => Refresh(provider)));
    }
});
```

*Evidence:* `SwiftTaskTests.Task_group_children_see_parent_cancellation_and_the_scope_waits_for_every_child`,
`Task_group_next_returns_results_in_completion_order_and_nil_when_empty`,
`Async_let_children_run_concurrently_and_an_unread_one_is_cancelled_at_scope_exit`.

**R-TASK-6. Timing shapes.** Each timing shape in the source has its own contract; port the shape the site has,
never a generic "debounce", and test it with `TestRuntime.Advance` on both sides of each boundary.

| Shape | Source | C# |
|---|---|---|
| Debounce: a flag set once, a settle task re-armed on every tick | `SidebarView.swift:391-403` (ED-010) | the R-TASK-2 example |
| Throttle: not re-armed while one is pending | `TerminalPanel.swift:414-425` | `if (pendingSync is not null) return; pendingSync = MainActor.AsyncAfter(TimeSpan.FromMilliseconds(50), () => { pendingSync = null; SyncFrame(); });` |
| Time since last, monotonic | `ThreadRuntime.swift:425`, `:3087` | `lastFlushAt = SwiftClock.Now - TimeSpan.FromSeconds(1);` then `SwiftClock.Now - lastFlushAt >= limit` |
| Flush at most N ms after the first piece | `OpenAICompatibleSession.swift:150-200` (an actor) | `Actor` + `Spawn` + `TrySleep` |
| Newest-snapshot write coalescing | `Storage.swift:337-356` | `Mutex<Dictionary<..>>` + one `SerialQueue.Async` per first enqueue |
| Spacing by reserving slots | `ACPSession.swift:7-21` (1.5 s, wall clock) | `Actor`; `nextSlot` moves before the sleep |
| Poll with a deadline | `AppDelegate.swift:38-48` (ED-004) | `while (!done && SwiftDate.Now < deadline) await SwiftTask.TrySleep(TimeSpan.FromMilliseconds(250));` |

*Evidence:* `TimingShapeTests.Scroll_freeze_debounce_lifts_150_ms_after_the_last_tick`,
`Live_resize_sync_is_throttled_not_debounced`, `Continuous_instants_subtract_to_durations`,
`ActorTests.Launch_gate_spaces_a_batch_by_reserving_slots_before_sleeping`,
`SerialQueueTests.Disk_writer_coalesces_a_backlog_to_the_newest_snapshot`.

### 3.4 Actors

**R-ACTOR-1. A Swift `actor` (7) is `sealed class X : Actor`; each isolated method wraps its body in
`Isolated(...)` (ED-067).** Write the Swift body literally inside, awaits included; state is plain fields. The
strand releases the actor at every await (Swift actors are re-entrant), a call from inside the actor runs inline
(a same-actor call does not hop), and an actor call stays in the caller's task.

```csharp
// Services/MCP/MCPOAuth.swift:62-74
private sealed class OAuthRefreshGate : Actor
{
    private readonly Dictionary<string, TaskHandle<string>> flights = new(StringComparer.Ordinal);

    public Task<string> Token(string serverID, Func<Task<string>> refresh) => Isolated(async () =>
    {
        if (flights.TryGetValue(serverID, out var existing)) return await existing.Value; // :66-68
        var flight = TaskHandle.DetachedThrowing(refresh);                                  // :69 Task.detached
        flights[serverID] = flight;                                                         // :70
        try { return await flight.Value; }                                                  // :72
        finally { flights.Remove(serverID); }                                               // :71 defer: the FIRST caller
    });
}
```

Never `ConfigureAwait(false)` inside `Isolated`; never block inside it (`.Result`, `.Wait()`); never hold a
`lock` or `SemaphoreSlim` across an await in place of isolation; re-read actor state after every await, as the
Swift does. A caller that gives up must not cancel a shared flight: `ThumbnailCache` awaits `task.value` and then
checks `Task.isCancelled` (`await flight.Value;` then `SwiftTask.IsCancelled`). `nonisolated` members are ordinary
members that touch no actor state. A test that needs a deterministic actor passes the loop as its target.
*Pitfall:* a `SemaphoreSlim(1)` across the await serializes the callers, so the second refreshes again instead
of joining, a nested call deadlocks, and `ACPLaunchGate` spacing changes.
*Evidence:* `ActorTests.Concurrent_refreshes_join_one_flight`, `A_lock_held_across_await_breaks_the_join`,
`Actor_segments_never_overlap_and_a_same_actor_call_runs_inline` (200 concurrent callers, one segment at a time),
`Launch_gate_spaces_a_batch_by_reserving_slots_before_sleeping`.

### 3.5 Streams and continuations

**R-ASYNC-1. `AsyncStream` (6) and `for await` (15) (ED-069).** `AsyncStream.makeStream(of:)` is
`AsyncStream<T>.MakeStream()`; the closure form `AsyncStream { continuation in ... }` is
`AsyncStream<T>.Create(c => ...)` (the closure runs now); `for await x in s` is `await foreach (var x in s)`;
`continuation.onTermination = { reason in ... }` is `continuation.OnTermination = reason => ...`. Kept: unbounded
buffering before the consumer starts; `yield` after `finish()` is dropped and reports `Terminated`; cancelling the
consuming task ends the loop quietly, terminates the stream and calls `OnTermination(Cancelled)` before the
consumer sees the end; values already buffered are still delivered first (O-11); `OnTermination` runs once;
`Finish` is idempotent (`StdioProcess.swift:175-181` finishes twice); a second concurrent consumer traps.
*Pitfall:* `Channel<T>.ReadAllAsync` throws on cancellation where `for await` returns, and has no `onTermination`.
*Evidence:* `AsyncStreamTests` (7 tests, including `Channel_ReadAllAsync_throws_on_cancellation`).

**R-ASYNC-2. Continuations (13) (ED-069).** `withChecked(Throwing)Continuation { c in ... }` is
`CheckedContinuation<T>.With(c => ...)`: the closure runs before the caller suspends, `resume(returning:)` is
`Resume(value)`, `resume(throwing:)` is `Resume(error)`, and a second resume traps. `new TaskCompletionSource`
appears only inside `Port/`, always with `RunContinuationsAsynchronously`: without it a resume runs the awaiting
code inline, inside the resumer's lock, which is exactly what `Shell.swift:218-221` avoids. Waiter lists are taken
under the lock and resumed after it is released. `OnceContinuation` (`Shell.swift:218-243`) is a Swift type:
port it in `Support/Shell.cs` over `CheckedContinuation` and `Mutex`, as the Swift class wraps them.
*Evidence:* `ContinuationTests.Without_RunContinuationsAsynchronously_the_awaiter_runs_inside_the_lock` (the
pitfall), `CheckedContinuation_resumes_the_awaiter_outside_the_lock`, `CheckedContinuation_resumed_twice_traps`,
`The_continuation_closure_runs_before_the_caller_suspends`.

### 3.6 Locks, dispatch queues, threads, file events, notifications

**R-LOCK-1. `Mutex` (24) and `OSAllocatedUnfairLock` (1) are `Mutex<T>` (ED-069).** `m.withLock { v in ... }` is
`m.WithLock((ref T v) => ...)`; the value is reachable only inside; re-entry traps (.NET `Lock` and `Monitor` are
re-entrant and would accept it silently); the delegate is synchronous, so no await can occur inside; a `defer`
inside `withLock` (`Shell.swift:239`) stays inside as `try`/`finally`. Keep "take under the lock, act outside it".
*Evidence:* `SerialQueueTests.Mutex_reentry_traps_where_a_plain_Lock_would_succeed`.

**R-GCD-1. A private serial `DispatchQueue(label:)` (6) is `SerialQueue(label)` (ED-069).** `queue.async` is
`Async` (FIFO, outside any task), `queue.sync` is `Sync` (blocks until the item and everything before it ran, so
`queue.sync {}` is the barrier `Storage.swift:378` uses), `queue.asyncAfter` is `AsyncAfter`. `Sync` from the queue
itself traps, as GCD does. Call `Sync` only from the pool or a dedicated thread, never from an actor body, and from
main only where the Swift code blocks main too (the quit path). *Pitfall:* `Task.Run` per item loses FIFO order.
*Evidence:* `SerialQueueTests.Serial_queue_is_fifo_and_sync_on_itself_traps`,
`A_serial_queue_asyncAfter_item_cancelled_before_it_fires_never_runs`.

**R-GCD-2. The rest of GCD.** `DispatchQueue.main.async` is `MainActor.Post`; `DispatchQueue.main.asyncAfter` is
`MainActor.AsyncAfter`; `DispatchQueue.global(qos:).async` (`Shell.swift:91`) is `GlobalQueue.Async`;
`DispatchQueue.global().asyncAfter` (the kill ladders: `Shell.swift:113`, `StdioProcess.swift:125` and `:178`,
`MCPHub.swift:290`, `MCPProbe.swift:231`; timings decided by ED-027 and ED-047) is `GlobalQueue.AsyncAfter`; a
`DispatchWorkItem` is the returned `WorkItem` (`cancel()` prevents it until it starts). QoS is dropped (ED-066).
*Evidence:* `MainActorTests.AsyncAfter_work_item_cancelled_after_the_timer_fired_does_not_run`,
`SerialQueueTests.Global_asyncAfter_runs_on_the_pool_after_the_delay_outside_any_task`.

**R-GCD-3. Threads, timers, display links and file events.** `Thread` (`HangWatchdog.swift:50-59`) is
`new Thread(Loop) { IsBackground = true, Name = "hang-watchdog" }`, with its decision logic (a ping every 0.5 s at
`:52`, `stallThreshold` of 4 s at `:24` measured with `Date()`, one report per stall at `:74-75`; ED-003 covers the
report text) in a plain class driven by `SwiftClock` so tests can drive it; its
`Thread.sleep(forTimeInterval: 0.5)` blocks on a `SwiftClock` timer, not `Thread.Sleep`. A pool work item as a
watchdog starves exactly when it matters. `Timer` (0 sites) and `RunLoop` (only the dropped `SwitchLatency`,
ED-005): nothing to port; product code never creates `System.Threading.Timer`, `System.Timers.Timer` or
`DispatcherTimer` (ED-076 source scan). `CADisplayLink` and `TimelineView(.animation)` are frame callbacks
(R-VIEW-10). `DispatchSource.makeFileSystemObjectSource` (`CommandCodeSession.swift:654`) and `FSEventStream`
(`WorkingTreeWatch.swift:147`) become `FileSystemWatcher` (FileWatcher, F.FSWatcher) with events delivered on the queue
the source names: the working-tree watch's `SerialQueue` (WorkingTreeWatch.swift:121, :147) and main through
`MainActor.Post` for the approval directory (CommandCodeSession.swift:654), ED-116; the source's
own rescans absorb duplicate events; the watcher's `Error` event (buffer overflow) means "rescan everything". Neither source has a
latency. The one FSEvents stream passes latency 0 with kFSEventStreamCreateFlagNoDefer (WorkingTreeWatch.swift:142,
:145), and a dispatch source has none (CommandCodeSession.swift:654). The only coalescing is what those APIs do
without a clock: events that arrive before the handler runs are handed over together. For the tree watch that is
one batch of paths in order, as the FSEvents callback receives count paths (:172-176). For the directory watch it is
one handler run, as a vnode source merges pending events. Nothing waits on SwiftClock (ED-115; buffers and loss
ED-114; PA-0017).

**R-NOTE-1. `NotificationCenter` (33), KVO (0), Combine (0).** The one app-defined notification
(`ThemeManager.didChange`, `AppTheme.swift:221-226`) is a `public static event Action? DidChange` raised
synchronously where Swift posts; an observer that posts again recurses, as in Swift. An observer removed in
`deinit` is unsubscribed in `Dispose` (R-LIFE-1). AppKit window notifications become the matching Avalonia and
Win32 events per site. Whether `addObserver(queue: .main)` runs inline for a post on main is UNVERIFIED (O-19); it
is only observable for a post off main, and none exists.

### 3.7 Observation and SwiftUI views

**R-OBS-1. `@Observable final class X` is `sealed class X : ObservableModel` (ED-070).** One private field per
stored property, and a property whose getter is `Read(field)` and whose setter is `Set(ref field, value)`. A
constructor assigns backing fields, never properties: Swift runs no observer and notifies nobody for assignments
in `init`. A class that is not `@Observable` is a plain class with no `INotifyPropertyChanged`. The values an
observable holds are immutable records (R-VAL-1), so a change is "copy, change, compare, store", the pattern of
`AppModel.updateThread` (`AppModel.swift:705-712`).

```csharp
// App/AppSettings.swift:201-203: a stored property with a didSet
public bool NotifyWhenFinished
{
    get => Read(notifyWhenFinished);
    set => Set(ref notifyWhenFinished, value, _ => defaults.Set(NotifyKey, notifyWhenFinished));
}
```

*Evidence:* `ObservationTests.UpdateThread_on_immutable_records_saves_once_and_skips_a_no_op`.

**R-OBS-2. `didSet` runs on every assignment, after the store, with the old value, and never in `init`.** Pass it
as `didSet: oldValue => ...`; never add an equality check in front of it (Swift runs `didSet` for an equal value).
*Evidence:* `Loading_settings_writes_nothing_back_because_didSet_is_silent_in_init`,
`DidSet_runs_for_an_equal_value_but_bindings_are_not_told`.

**R-OBS-3. Who is told, and when.** `Set` tells trackers and bindings only when the value changed under
`EqualityComparer<T>.Default`: Swift `==` for an Equatable struct or enum (a C# record or enum), identity for a
class. A property whose Swift type is neither Equatable nor a class (an unconstrained generic such as
`ObservedValue<Value>`) uses `SetAlways`, which tells them on every assignment. Whether Swift 6.2 skips the
notification for an equal value is UNVERIFIED (O-05); an equal value renders the same either way, so only a render
count could differ, and no requirement counts renders.
*Evidence:* `DidSet_runs_for_an_equal_value_but_bindings_are_not_told`, `A_non_Equatable_value_notifies_on_every_set`.

**R-OBS-4. Computed properties and tracking.** A computed property that reads tracked state is
`Computed(() => ...)`: recomputed on every read, as in Swift, and a binding to it is told when anything it read
changes, including a cell of another object (`AppModel.swift:292-294` reads a cell held in an
`@ObservationIgnored` dictionary). `withObservationTracking` (no direct use) is `ObservationTracking.WithTracking`.
Tracking is per access: whatever did not read `b` is not told when `b` changes.
*Evidence:* `A_computed_property_follows_its_own_cell_and_ignores_sibling_cells`,
`WithTracking_fires_once_and_only_for_what_was_read`,
`HeadlessTests.An_Avalonia_binding_follows_a_computed_property_across_a_cell`.

**R-OBS-5. `@ObservationIgnored` is a plain field or auto-property**, never `Read` or `Set`. Per-item cells kept
beside a tracked collection (`threadCells`, `AppModel.swift:29-44`) port as they are, including "settle the
dictionary, then write the cells" (`:150-190`), although C# would not trap on the exclusivity Swift guards against.

**R-OBS-6. `@MainActor @Observable` is `MainActorModel`**: every tracked assignment asserts the main actor, so the
off-main mutation Swift rejects at compile time traps in a test.
*Evidence:* `A_main_actor_model_mutated_off_main_traps`.

**R-VIEW-1. A SwiftUI `View` is a code-first Avalonia control in the twin file (ED-071).** No AXAML: it would
split one Swift file into two (R-FILE-1) and lose the per-modifier citations. The view's `let` inputs are
constructor parameters (or an `Update(...)` method on a `ForEach` row, R-VIEW-8); `body` is built once, in the
constructor; each part of `body` that reads observable state is attached with
`control.Track(() => expression, value => apply)` (R-VIEW-4). The branches of an `if`/`else` or `switch` in `body`
are different controls: switching branches replaces the control and loses its `@State`, as SwiftUI does.

**R-VIEW-2. View property wrappers.**

| Swift | C# |
|---|---|
| `@State private var x = v` that `body` reads | a property of a private `sealed class ViewState : MainActorModel` in the twin file, one instance per control, so it lives exactly as long as the view's identity |
| `@State` read only by actions | a private field |
| `@Binding var x: T` | a `Binding<T>` constructor parameter (R-VIEW-7) |
| `@Bindable var model`, `$model.x` | `new Binding<T>(() => model.X, v => model.X = v)` |
| `@Environment(\.key) var x` | the inherited attached property for that key (R-VIEW-6) |
| `@FocusState`, `@GestureState` | the control's focus state; a field reset when the gesture ends |
| a `@ViewBuilder` parameter | a `Func<Control>` |

**R-VIEW-3. `.task`, `.task(id:)`, `.onAppear`, `.onDisappear`.** `.task(id: x) { ... }` is
`control.Task(() => x, async id => ...)`: a new main-actor task started on attach (so on a later turn), cancelled
on detach, cancelled and restarted when the id changes under `==`. `.task { }` is the same with a constant id. The
body reads its cancellation through `SwiftTask.IsCancelled` and `TrySleep` (R-TASK-2, R-TASK-3). `.onAppear` is
`AttachedToVisualTree`; `.onDisappear` is `DetachedFromVisualTree`.
*Evidence:* `ViewSupportTests.ViewTask_starts_after_appear_cancels_on_disappear_and_restarts_on_a_new_id`,
`HeadlessTests.A_task_modifier_starts_on_attach_and_is_cancelled_on_detach`.

**R-VIEW-4. Re-application and animation.** A burst of writes in one main turn is one apply, run outside any
task. `withAnimation(a) { change }` is `Transaction.WithAnimation(a, () => change)`, and every apply scheduled by a
change made inside it runs with `Transaction.Current == a`, so it animates exactly what SwiftUI animates.
`.animation(a, value: v)` is an `OnChange` on `v` whose action applies with `a`. The curves (spring response and
damping, ED-011) are defined by the units that port `Chrome.swift`, not by the foundation.
*Evidence:* `Tracked_coalesces_a_burst_into_one_apply_and_carries_the_animation`,
`A_computed_property_tells_its_binding_when_another_objects_cell_changes`.

**R-VIEW-5. `.onChange(of: v) { old, new in ... }`** is `new OnChange<T>(() => v, (old, now) => ...)`, with
`initial: true` where the Swift passes it: after the turn, once, only when the value differs; a change that reverts
within the turn reports nothing.
*Evidence:* `OnChange_reports_old_and_new_after_the_turn_and_ignores_a_change_that_reverts`.

**R-VIEW-6. Environment.** Each environment key the source reads is an inherited attached property
(`EnvironmentValues.AccessibilityReduceMotionProperty` is the pattern); `.environment(\.key, v)` sets it on the
subtree's root and the nearest ancestor wins. `\.accessibilityReduceMotion` follows Windows' "Animation effects"
setting (`SPI_GETCLIENTAREAANIMATION`, inverted), re-read on `WM_SETTINGCHANGE` (ED-084). `\.colorScheme` (78
reads) is the theme value the theme units define.
*Evidence:* `HeadlessTests.Environment_values_come_from_the_nearest_ancestor`.

**R-VIEW-7. `Binding<T>`.** Reads and writes go through the source property: a view reading `binding.Value` is
tracked on the source, and a write runs the source's setter and its `didSet`. `.constant(v)` is
`Binding<T>.Constant(v)`. The one first-class key path (`ChatView.swift:611`,
`ReferenceWritableKeyPath<ThreadRuntime, PanelDockCorner>`) is a getter and setter pair.
*Evidence:* `A_binding_reads_and_writes_through_the_source_property`.

**R-VIEW-8. Identity.** `ForEach(items, id: \.id) { row }` is `ForEach<TItem, TId>`: a surviving id keeps its
control (and so its `@State`) and has its inputs updated in place; a new id gets a new control; a vanished id's
control is removed and disposed; children follow the items' order. Rows are keyed by the Swift id, never by index
(`id: \.self` keys by the value). `.id(x)` replaces the control when `x` changes. Duplicate ids keep the first
(UNVERIFIED O-18; no source site relies on duplicates).
*Evidence:* `HeadlessTests.ForEach_keeps_a_surviving_rows_control_and_state_and_drops_the_vanished_one`.

**R-VIEW-9. Presentation.** `.popover(isPresented:)`, sheets and extra windows present through
`Presentation.Presenter` (an `Ambient` seam): production opens the Avalonia `Popup`; headless tests override it,
because Avalonia 12.1.2 headless cannot open a `Popup` in a theme-less window. Windows themselves are PA-0002's.

**R-VIEW-10. Frame-driven work (ED-083).** `TimelineView(.animation)`, `CADisplayLink` and every sleep that stands
for a frame tick (17 ms, 30 ms) run from Avalonia's frame clock (`TopLevel.RequestAnimationFrame`), behind a seam a
test drives by hand (section 4.3). Frames are the only wake-ups; durations and dates are read on SwiftClock and
SwiftDate at the frame; a span is complete at a frame 1 ms early at most; a TimelineView updates relative to its last
draw and passes SwiftDate.Now; a display link ticks first at the next frame; a schedule change never draws by itself
(ED-117, O-34). Never a `DispatcherTimer`, never `timeBeginPeriod`: Windows timers are
quantized to the 15.6 ms system tick, so a 17 ms timer does not tick every 17 ms.

**R-VIEW-11. Numbers, colours, type and symbols.** Sizes, paddings, radii, offsets and font sizes are the
source's numbers, one point to one DIP (ED-071); `CGFloat` is `double`. Colours come from the Chrome units
(ED-051, ED-062), text styles from ED-059 and PA-0013, symbols from PA-0012, the surface treatment from PA-0001.
Never take a value from a screenshot or an estimate.

### 3.8 `Codable` and JSON (ED-072)

**R-CODE-1. Decoding is strict exactly where Swift's is.** Every `init(from:)`, synthesized or hand-written,
becomes one reader function built from `SwiftDecoding`, field by field:

| Swift | C# |
|---|---|
| `try c.decode(T.self, forKey: .k)`; a synthesized non-optional `var`, even one with a default value | `Decode(e, "k", ReadT)` |
| `try c.decodeIfPresent(T.self, forKey: .k)`; a synthesized optional | `DecodeIfPresent(e, "k", ReadT)` (`DecodeIfPresentValue` for a value type) |
| `String`, `Bool`, `Int`, `Double`, `UUID` | `ReadString`, `ReadBool`, `ReadInt`, `ReadDouble`, `ReadUUID` |
| `Date` | `ReadStorageDate` or `ReadDeferredDate`, by the decoder (R-CODE-6) |
| `[T]`, `[String: T]` | `ReadArray(e, ReadT)`, `ReadMap(e, ReadT)` |
| `enum E: String, Codable` | `ReadRawValue<E>` (an unknown raw value is `dataCorrupted`) |
| an enum with associated values | `SwiftEnumCoding.ReadCase` (R-ENUM-2) |

A missing key throws `keyNotFound`, `null` throws `valueNotFound`, a wrong type throws `typeMismatch`
(`DecodingError.Kind`); `Bool` accepts only `true`/`false`; `Int` accepts an exactly integral number (`1.0`,
`1e2`). A synthesized `let` that has an initial value is not decoded (Swift warns and keeps the value) but is
encoded. System.Text.Json's default binding is never used for a model: it keeps a C# default where Swift throws.
*Evidence:* `SwiftDecodingTests.Synthesized_fields_are_strict_even_with_a_declared_default`,
`Default_System_Text_Json_binding_is_too_lenient`, `DecodeIfPresent_gives_nil_for_missing_or_null_but_throws_on_a_wrong_type`,
`Int_decoding_follows_JSONDecoder`.

**R-CODE-2. Leniency is the source's own, never more, never less.** `container.value(.k, default: x)`
(`Coding.swift:4-7`) is `Coding.Value(e, "k", ReadT, () => x)`, the twin in `Support/Coding.cs` over
`SwiftDecoding.Value`: missing, null, a wrong type or any nested failure gives the default, evaluated lazily and
afresh each time, and a trap still propagates. `[Lenient<T>]` then `compactMap(\.value)` is `LenientList`: a bad
element is dropped and the rest survive. A hand-written `init(from:)` keeps its own strict and lenient split, line
by line.
*Evidence:* `Value_falls_back_lazily_and_freshly_on_missing_null_or_wrong_type`,
`A_custom_initializer_keeps_its_own_strict_and_lenient_split`, `A_lenient_list_drops_the_bad_element_and_keeps_the_rest`.

**R-CODE-3. Encoding writes Swift's bytes.** A model's `encode(to:)` is a converter or writer function that
produces the members in Swift's order (a synthesized encoder: declaration order; an optional that is `nil` is
omitted, because synthesis uses `encodeIfPresent`). The bytes come from `SwiftJson.Encode(value, options, style)` or
`SwiftJson.Write(element, style)`: raw UTF-8 for every scalar that is not a C0 control (no escape for emoji, U+2028,
U+2029 or U+FEFF), the short escapes for backspace, tab, newline, form feed and carriage return, a lowercase
six-character escape for the other C0 controls (O-12), and Swift's Double text. `JsonSerializer.Serialize` and
`Utf8JsonWriter` bytes are never shipped: the relaxed encoder the current `Coding.cs` uses escapes emoji and
U+2028, and `Utf8JsonWriter` writes `1E-05`.
*Evidence:* `SwiftJsonTests.Non_ASCII_is_written_as_raw_UTF8_not_escaped`,
`The_relaxed_encoder_the_existing_Coding_cs_uses_escapes_emoji_and_line_separators`,
`Controls_use_short_forms_and_lowercase_hex`, `Storage_bytes_of_a_model_match_Swift_end_to_end`, `Output_is_deterministic`.

**R-CODE-4. One named style per Swift writer, chosen from the Swift line.**

| Swift writer | `SwiftJsonStyle` | Keys | Layout | Slash |
|---|---|---|---|---|
| `JSONEncoder.storage` (`Coding.swift:19-26`) | `Storage` | sorted | compact | escaped |
| a plain `JSONEncoder()` (11 sites) | `PlainEncoder` | unsorted | compact | escaped |
| `[.prettyPrinted, .sortedKeys]` (`MCPStore.swift:262-275`) | `PrettySorted` | sorted | pretty | escaped |
| `JSONValue.data()`: JSONSerialization, `.withoutEscapingSlashes` | `SerializationCompact` | unsorted | compact | kept |
| `JSONValue.prettyString` | `SerializationPretty` | sorted | pretty | kept |

Pretty is Apple's layout: two-space indent and `"key" : value`; empty containers are O-04. A writer the table
lacks gets a new named style and a test, never the nearest one. "Unsorted" is Swift `Dictionary` order, which is
unspecified (R-COLL-2): the port keeps its element order, and no test asserts an order the Swift does not fix.
*Evidence:* `Slash_escaping_depends_on_the_writer`, `Pretty_layout_is_Apples_key_space_colon_space_and_two_space_indent`.

**R-CODE-5. Numbers in JSON.** A Double is written by `SwiftJson.WriteDouble` (Swift's text without a trailing
`.0`: `1`, `0.5`, `1e-05`); an `Int` is an integer literal; a non-finite Double is an `EncodingError`.
*Evidence:* `Doubles_use_Swift_text_and_integer_literals_stay`, `A_Double_in_JSON_drops_a_trailing_point_zero`,
`A_non_finite_Double_is_an_EncodingError`.

**R-CODE-6. Dates in JSON.** The storage coder writes `.iso8601` (`FoundationISO8601.Format`: whole seconds by
floor, UTC, `Z`) and reads with `ReadStorageDate`, which keeps the existing lenient rule (fractional seconds
accepted) until O-02 settles it. A plain `JSONEncoder()` writes a Date as a Double of seconds since 2001
(`SwiftDate.ReferenceSeconds`, read back with `ReadDeferredDate`).
*Evidence:* `SwiftDateTests.A_plain_JSONEncoder_date_is_reference_date_seconds`,
`ISO8601_writes_whole_seconds_by_floor_in_UTC`, `Storage_decoding_keeps_the_existing_lenient_rule_and_the_strict_one_is_parked`.

**R-CODE-7. UUIDs in JSON** are `uuidString`, uppercase, through `SwiftUUID.JsonConverter`; reading accepts either
case, in the 36-character form only (R-UUID-1).
*Evidence:* `SwiftUUIDTests.The_converter_writes_uuidString_and_reads_strictly`.

**R-CODE-8. Reading JSON.** Parse with `JsonDocument.Parse(bytes, SwiftJson.DocumentOptions)`: 512 levels
(O-24), where the default stops at 64. A repeated key keeps its last value, once. JSONSerialization numbers
classify by lexical form (`SwiftJSONNumber.IsInt`: `1` is an integer, `1.0` a Double; big integers are O-09). A
leading BOM is O-15.
*Evidence:* `SwiftJsonTests.Nesting_past_64_levels_needs_the_document_options`,
`A_duplicated_key_keeps_its_last_value_once`, `SwiftDecodingTests.JSONSerialization_numbers_classify_by_lexical_form`,
`ValueCollectionTests.A_repeated_key_keeps_the_last_value`.

**R-CODE-9. Sorted keys are ordinal until O-01 settles them** (`SwiftJson.SortedKeyOrder`). Ordinal equals
Foundation's order for fixed `CodingKey`s made of lowercase letters; camelCase siblings where an uppercase letter
meets a lowercase letter or `_` at the same position may differ under the parked comparer, and stay under O-01; it can differ only for dictionaries with user keys (environment
variables, headers), where Foundation is believed to compare digit runs numerically and letters without case
(`FoundationKeyComparer`, parked). The oracle's answer is a one-line change.
*Evidence:* `Sorted_keys_stay_ordinal_until_the_oracle_and_Foundation_order_differs_only_on_user_keys`.

### 3.9 Enums, protocols and value semantics

**R-ENUM-1. `enum X: String` (31) is a C# enum whose every member carries `[RawValue("...")]` (ED-075).** The raw
value is written out, the case name where Swift spells none. `x.rawValue` is `SwiftEnum<X>.RawValue(x)`,
`X(rawValue: s)` is `SwiftEnum<X>.FromRawValue(s)` (exact and case-sensitive, compared as Swift compares strings,
never numeric, never flags), `X.allCases` is `SwiftEnum<X>.AllCases` (declaration order). No explicit numeric
values: the type initializer traps on one, on a missing attribute and on a repeated raw value. `Enum.Parse`,
`Enum.TryParse`, `ToString()` and `Enum.GetValues` never touch a ported enum. A switch expression over a ported
enum has no discard arm: with CS8524 suppressed (ED-075), a missing named case is error CS8509. The one
`enum HydraMergeStage: Int` (`Hydra.swift:509`) keeps the implicit values 0, 1, 2, which are its raw values, and
takes no `[RawValue]`.

```csharp
// Illustration only
public enum Sample { [RawValue("first")] First, [RawValue("secondCase")] SecondCase }
static int Weight(Sample s) => s switch { Sample.First => 1, Sample.SecondCase => 2 }; // no `_ =>`
```

*Evidence:* `SwiftEnumTests.Raw_values_are_exact_and_allCases_keeps_declaration_order`,
`An_enum_member_without_a_raw_value_is_refused`, `A_raw_value_matches_canonically_as_a_Swift_string_switch_does`,
`A_raw_value_decodes_exactly_and_an_unknown_one_is_dataCorrupted`; CS8509 in `negative-checks.log`.

**R-ENUM-2. An enum with associated values (156 `case x(`) is an abstract record with a private constructor and
nested sealed records**, the existing `MCP.cs:8-22` convention. `case .x(let a, let b)` is a type pattern with
deconstruction; `guard case .reasoning(let block) = content` is `if (content is not Reasoning block) return ...`;
`indirect` needs nothing. Codable follows SE-0295: `{"name":{"_0":v}}` for an unlabelled value,
`{"name":{"label":v}}` for a labelled one, through `SwiftEnumCoding.ReadCase` and `WriteCase`.
*Evidence:* `SwiftDecodingTests.An_enum_with_associated_values_round_trips_as_case_then_underscore_zero`.

**R-ENUM-3. Exhaustiveness over record unions.** C# cannot check a switch over a record hierarchy: the
`_ => throw new UnreachableException()` arm the compiler demands also hides a forgotten case. So every union has a
test that builds one sample per case with `UnionCoverage.AssertCovers` (which fails if a case has no sample) and
runs every switch-bearing member over them. `@unknown default` (2, both on Apple enums) has no counterpart.
*Evidence:* `Every_union_switch_is_run_over_one_sample_per_case`,
`A_forgotten_union_case_is_caught_by_the_harness_not_the_compiler`, `An_incomplete_sample_set_fails_instead_of_passing_vacuously`.

**R-PROTO-1. Protocols, extensions, generics.** The three protocols (all class-bound) are interfaces. A method
that is a requirement and has a protocol-extension default (`ProviderSession.stopAgent`) is a default interface
member: both dispatch dynamically. A method only in a protocol extension dispatches statically in Swift: port it
as a static extension method, never a default interface member (no such method exists today). `any P` is the
interface type; `any Encodable & Sendable` (`Storage.swift:343`) is `object` written by its runtime type; generic
constraints map directly (`where T : IEquatable<T>`); `some View` is `Control`. Key-path closures (`\.value`) are
lambdas.

**R-VAL-1. A Swift struct is an immutable `sealed record` (ED-077).** Exception: ModelPin (AppSettings.swift:96-101)
is a `readonly record struct`, because `RowDrag<ModelPin>` needs a value type for its `ID?`; never observe its default
value (R-COLL-3) (ED-149). Any other exception needs its own ED. `var copy = x; copy.f = v` is
`var copy = x with { F = v }`; an `(inout T) -> Void` closure is a `Func<T, T>` returning the changed copy; a
`mutating` method returns the new value. A mutable class never stands in for a Swift struct: `var thread =
threads[i]` must copy, or `updateThread` compares a thread with itself and skips the save.
*Evidence:* `ObservationTests.UpdateThread_on_immutable_records_saves_once_and_skips_a_no_op`,
`A_mutable_class_alias_makes_the_same_code_skip_the_save`.

**R-VAL-2. Collections inside a record are `ValueList<T>`, `ValueMap<TKey, TValue>` or `ValueSet<T>`**, so the
record's generated equality and hash compare contents, as Swift's synthesized `Equatable` and `Hashable` do. A map
or set whose keys hold user or external text takes `SwiftStringComparer.Instance` (R-STR-2). `MCPValues.List` and
`Map` migrate to these (section 4.4).
*Evidence:* `ValueCollectionTests.Collections_in_records_compare_by_content_like_Swift_values`,
`BCL_collections_in_records_compare_by_reference`, `A_map_of_external_text_takes_the_Swift_string_comparer`.

**R-VAL-3. A copy is independent.** `Appending`, `SetItem` and `Mutating(change)` return a new collection and never
touch the original; a collection read from a model is never cast back to a mutable type.
*Evidence:* `Mutating_a_copy_never_changes_the_original`.

**R-VAL-4. Equality that differs.** A record compares Doubles with `double.Equals`, for which NaN equals NaN;
Swift's synthesized `==` says NaN is not equal to NaN. Where a model's Double can hold NaN and its equality is
observable (a `guard thread != old` save, an `onChange`), write the record's `Equals` with `==` on that field, or
stop and report. The 16 custom `==` implementations (`TimelineEntry`, `ThreadRuntime.swift:47`; `MarkdownView`;
`DiffFileCard`) compare a subset on purpose: port them verbatim as `Equals` and a matching `GetHashCode`.
`Identifiable.id` is the `Id` a `ForEach` keys on; `Comparable` (`AppVersion`, `UpdateChecker.swift:7-37`) is
`IComparable<T>` with the same logic.
*Evidence:* `SwiftNumberTests.Record_equality_treats_NaN_as_equal_unlike_Swift`.

### 3.10 Strings and regular expressions (ED-073)

**R-STR-1. Counting, slicing and iterating use Characters.** `s.count` is `SwiftString.Count(s)`;
`prefix(n)`, `suffix(n)`, `dropFirst(k)`, `dropLast(k)` are `SwiftString.Prefix`, `Suffix`, `DropFirst`, `DropLast`
(a negative count traps); `for c in s` is `foreach (var c in SwiftString.Characters(s))`;
`s.unicodeScalars` is `s.EnumerateRunes()`. Each count uses the unit the Swift uses: Characters (`count`), UTF-8
(`utf8.count`, `SwiftString.Utf8Count`), UTF-16 (`utf16.count`, `(s as NSString).length`, `NSRange`: `s.Length`),
scalars. Where the source mixes units (`Git.swift:159`, `utf8.count > 40_000` then `prefix(40_000)` Characters,
upstream defect 75; ED-028's clip), reproduce the mix. `s.Length`, `Substring` and ranges never stand for a
Character count.
*Evidence:* `SwiftStringTests.Count_is_Characters_and_CRLF_is_one_Character`,
`Slicing_keeps_whole_Characters_and_a_negative_count_traps`.

**R-STR-2. Equality, order, prefix, suffix and hashing are canonical.** Swift `==` is `SwiftString.Equal`, `<` and
`sorted()` are `SwiftString.Compare` (scalar order of the NFC forms, not UTF-16 order), `hasPrefix`/`hasSuffix` are
`SwiftString.HasPrefix`/`HasSuffix` (whole Characters, so a prefix ending in `\r` does not match `\r\n` text, on the
ASCII path too, O-06). A `Set<String>`, a `Dictionary` key or a sort over user or external text takes
`SwiftStringComparer.Instance`. C# `==`, `string.Equals(..., Ordinal)` and `StartsWith(..., Ordinal)` are exact only
where both strings come from the port itself (raw values, JSON keys it wrote). Even an ASCII literal is not safe
against outside text: U+212A KELVIN SIGN equals `"K"` in Swift. Culture comparison (the default of `OrderBy` on
strings, `string.Compare`, `CompareTo`) is never right.
*Evidence:* `Equality_is_canonical_equivalence_also_for_set_and_dictionary_keys`,
`Ordering_is_by_scalar_value_not_UTF16_unit_and_default_OrderBy_is_cultural`,
`HasPrefix_and_hasSuffix_compare_whole_Characters_canonically`, `A_prefix_or_suffix_that_splits_CRLF_does_not_match_even_on_ASCII`.

**R-STR-3. The three line splitters are three rules; transcribe each site's.** `split(separator: "\n")` (22) is
`SwiftString.Split(s, "\n")`: the separator is one Character, so `\r\n` is never split, and empty pieces are
dropped unless `omittingEmptySubsequences: false`. `components(separatedBy: .newlines)` (12) is
`SwiftString.ComponentsSeparatedByNewlines`: every newline scalar splits, so `\r\n` yields an empty piece.
`components(separatedBy: "\n")` (11) is NSString-based: `s.Split("\n")` is believed, and whether Foundation also
splits inside `\r\n` is O-13, so mark the line. .NET's `Split('\n')` leaves a `\r` on each CRLF piece and is none of
the three. `split(whereSeparator: \.isWhitespace)` tests each Character with `SwiftCharacter.IsWhitespace`.
*Evidence:* `The_three_line_splitters_disagree_on_CRLF`, `Split_follows_the_stdlib_for_maxSplits_and_empty_pieces`.

**R-STR-4. Case mapping (ED-073, restating ED-009 part 1).** `lowercased()` and `uppercased()` are
`SwiftString.Lowercased`/`Uppercased`: full, locale-independent, per-scalar mapping through ICU's root locale in
`System32\icu.dll` (U+0130 lowercases to `i` plus U+0307; U+00DF uppercases to `SS`; no Final_Sigma context, O-07).
Never `ToLower()`, `ToUpper()`, `ToLowerInvariant()`, `ToUpperInvariant()` or `char.ToLower` on text: the invariant
forms are simple 1:1 mappings and the others are culture-sensitive (`MCPCatalog.cs:80` is on the repair list).
*Evidence:* `Case_mapping_is_full_and_locale_independent`, `Lowercase_maps_each_scalar_alone_so_no_final_sigma_applies`.

**R-STR-5. Character properties are Unicode's, read through ICU.** `c.isNumber` is `SwiftCharacter.IsNumber`
(Numeric_Type is not None: U+00B2, U+00BD, Roman and CJK numerals), `isLetter` is `IsLetter` (Alphabetic),
`isWhitespace` is `IsWhitespace` (White_Space). Each reads the Character's first scalar. `char.IsDigit`,
`char.IsNumber`, `char.IsLetter` and `char.IsWhiteSpace` never stand for them (other categories, and they fail on
astral scalars).
*Evidence:* `Character_properties_follow_Unicode_not_dotnet_categories`.

**R-STR-6. Case-insensitive search and comparison (ED-009 part 2).** `localizedCaseInsensitiveContains` (34) is
`SwiftString.LocalizedCaseInsensitiveContains` (current culture, case-insensitive, accent-sensitive; an empty needle
is never contained, O-08; keep each call site's own guard too). `caseInsensitiveCompare` (3) and
`range(of:options: .caseInsensitive)` without a locale are `SwiftString.CaseInsensitiveCompare` and a search over
folded text (Unicode default folding, O-07). `localizedCaseInsensitiveCompare` (1) is
`CultureInfo.CurrentCulture.CompareInfo.Compare(a, b, CompareOptions.IgnoreCase)`. `OrdinalIgnoreCase` is never one
of them (ED-009); `MCP.cs:82` uses it for `MCP.swift:142` and is noted on the repair list.
*Evidence:* `LocalizedCaseInsensitiveContains_rejects_an_empty_needle_and_stays_accent_sensitive`.

**R-STR-7. Trimming uses Foundation's sets.** `trimmingCharacters(in: .whitespaces)` is `FoundationText.Trim(s)`,
`.whitespacesAndNewlines` is `FoundationText.Trim(s, includeNewlines: true)` (Foundation's whitespace includes
U+200B), `CharacterSet(charactersIn: "()")` trims exactly those scalars. `string.Trim()` never ports a Swift trim.
*Evidence:* `Trimming_uses_Foundation_sets_not_String_Trim`.

**R-STR-8. Bytes to text as the Swift converts them.** `String(decoding: d, as: UTF8.self)` (14) is
`SwiftText.DecodeUtf8Repairing` (U+FFFD for invalid bytes); `String(data: d, encoding: .utf8)` (13) and
`String(contentsOf: url, encoding: .utf8)` (11) read the bytes and use `SwiftText.DecodeUtf8OrNil` (null on invalid
UTF-8; a BOM is kept, O-15); `Data(s.utf8)` is `Encoding.UTF8.GetBytes(s)` (no BOM). `File.ReadAllText`,
`File.ReadAllLines`, a default `StreamReader` and `BeginOutputReadLine` never read text a Swift API reads: they strip
a BOM and repair silently.
*Evidence:* `UTF8_decoding_has_a_failing_and_a_repairing_form`.

**R-STR-9. Writing text in C# source.** A user-visible string, key, message, prompt or argv value is copied byte
for byte from the Swift literal. Every non-ASCII character in C# source is an eight-digit escape, `\U0000XXXX`,
with a comment naming the character (`"couldn\U00002019t"  // U+2019 RIGHT SINGLE QUOTATION MARK`): never the raw
character (invisible differences such as U+00A0 against a space) and never a four-digit escape (the agents' file
tools were observed to decode four-digit escapes into raw characters while writing). Never `\x` (its length is
variable). A Swift multi-line literal becomes the string it evaluates to (indentation stripped, `\` line joins
applied); a long one (a prompt) gets a test comparing its UTF-8 bytes with the Swift literal's. Interpolated numbers
follow R-NUM-2.

**R-STR-10. An index into a string is a position of the unit the Swift used.** A `String.Index` walked by
Characters is a UTF-16 offset produced by `SwiftString` helpers, never computed by arithmetic on `Length`;
an `NSRange` from NSString or NSRegularExpression is a UTF-16 range and ports directly.

**R-RE-1. Swift `Regex` literals are .NET regexes with pinned options.** Always `RegexOptions.CultureInvariant`;
`.ignoresCase()` adds `IgnoreCase`; write literals as `[GeneratedRegex]` members so the pattern is checked at build.
`wholeMatch(of:)` is `\A(?:...)\z`; `firstMatch` is `Match`; `matches(of:)` is `Matches`; `$` is written `\z` (.NET's
`$` also matches before a final newline; Swift's anchor is O-13), marked `// UNVERIFIED O-13`. An unmatched optional
capture is `null` (`g.Success ? g.Value : null`), never `""`. `replacing(regex, with: "text")` is literal: use a
`MatchEvaluator` or escape `$` as `$$`, because .NET reads `$1` in a replacement string. `NSRegularExpression` and
`range(of:options: .regularExpression)` are ICU over UTF-16, as .NET is; `.backwards` returns the last match (O-13).
*Evidence:* `Dotnet_dollar_matches_before_a_final_newline_so_the_port_writes_z`.

**R-RE-2. Swift `Regex` matches Characters; .NET matches UTF-16 units.** `.` consumes one Character in Swift, `\s`
matches the single Character `\r\n`, and a character class sees a whole Character. Where the input can hold
combining marks, emoji sequences or CRLF (user text, CLI output), check every match edge with
`SwiftString.IsCharacterBoundary` and treat a split Character as no match, or stop and report the site. Whether
`\r?\n` matches the Character `\r\n` (the Hydra fence patterns, `Hydra.swift:1093`, `:1130`, `:1182`, `:1187`,
`HydraDelegationStream.swift:17`) is O-13 and load-bearing on Windows text.
*Evidence:* `Dotnet_regex_can_end_inside_a_Character`.

### 3.11 Numbers and formatting (ED-073, ED-074)

**R-NUM-1. Parsing.** `Int(s)` is `SwiftNumber.ParseInt` (ASCII digits with one optional sign, no whitespace, no
underscores, 64-bit range); `Double(s)` is `SwiftNumber.ParseDouble` (`inf`, `nan`, hex floats and exponents
accepted, whitespace rejected); `Scanner(string:).scanHexInt64` is `SwiftScanner.ScanHexInt64` (the value stays 0 on
failure, so `Color(hex: "zz")` is black; overflow and a bare `0x` are O-16). `int.Parse`, `long.TryParse`,
`double.Parse` and `Convert.ToInt32` never parse Swift text: they accept whitespace, culture signs and separators.
*Evidence:* `ParseInt_matches_Swift_Int_init`, `ParseDouble_matches_Swift_Double_init`,
`ScanHexInt64_reads_a_prefix_and_leaves_zero_on_failure`.

**R-NUM-2. Printing.** `String(d)`, `"\(d)"` and `d.description` for a Double are `SwiftNumber.Description(d)`
(`1.0`, `0.0001`, `1e-05`, `-0.0`, `inf`, `nan`; exponent form above 2^54 is O-03). An integer is
`n.ToString(CultureInfo.InvariantCulture)`. `String(format: "%.Nf", x)` is `SwiftNumber.Fixed(x, N)` (the exact
binary value rounded half-even, so 0.25 gives `0.2`). A C# interpolation of any number passes the invariant culture
or goes through these helpers; a Double never reaches `ToString()` or an interpolation hole directly.
*Evidence:* `Description_matches_Swift_away_from_the_upper_threshold`, `Dotnet_default_double_text_is_not_Swift_text`,
`Fixed_is_printf_on_the_exact_binary_value`, `Fixed_agrees_with_dotnet_F_for_finite_values_and_spells_non_finite_as_printf`.

**R-NUM-3. Rounding.** `.rounded()` is `SwiftNumber.Rounded` (half away from zero); `.rounded(.up)` and
`.rounded(.down)` are `Math.Ceiling` and `Math.Floor`; `.rounded(.towardZero)` is `Math.Truncate`;
`.rounded(.toNearestOrEven)` is `Math.Round(x, MidpointRounding.ToEven)`. A bare `Math.Round(x)` is never written.
*Evidence:* `Rounded_is_half_away_from_zero`.

**R-NUM-4. Integers, conversion and overflow (ED-074).** Swift `Int` is `long`; `int` is used only for a .NET
collection index or count whose Swift value is bounded the same way. With `CheckForOverflowUnderflow` on, `+`, `-`,
`*` and narrowing casts trap as Swift's do; Swift's `&+`, `&-`, `&*` are `unchecked(...)`. `Int(d)` is
`SwiftNumber.TruncatingToInt(d)` (toward zero; traps on NaN, infinity or out of range); `Int16(x)` and friends are
checked casts. `max`/`min` on Doubles that can be NaN are written as the stdlib defines them (`y >= x ? y : x`,
`y < x ? y : x`), not `Math.Max`/`Math.Min`, which propagate NaN from either side.
*Evidence:* `Int_conversion_and_arithmetic_trap_like_Swift`; `port-settings-check.log` (239 + 10 + 2 existing tests
pass with checked arithmetic on).

**R-FMT-1. Formatters with a fixed locale.** `NumberFormatter` `en_US` `.decimal` with `maximumFractionDigits = 0`
(`ZaiAPI.swift:213-218`) is `SwiftFormat.DecimalNoFraction(v, CultureInfo.GetCultureInfo("en-US"))` (grouping and
half-even rounding; `"N0"` alone rounds half away from zero). The `en_US_POSIX` `DateFormatter` with
`yyyy-MM-dd HH:mm:ss` in `Asia/Shanghai` (`ZaiAPI.swift:205-211`) is `DateTime.TryParseExact` with the invariant
culture in `TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai")` (Windows accepts IANA ids through ICU).
*Evidence:* `NumberFormatter_decimal_rounds_half_even_with_the_locale_grouping`.

**R-FMT-2. The user's locale, time zones and calendar (restating ED-034).** `Date.formatted(...)`,
`.relative(presentation: .named)`, `.spellOut`, `.currency(code:)`, `Int.formatted()` and `ByteCountFormatter`
follow the user's locale through ICU skeletons; they are ported only by the ICU formatter unit (section 4.3), whose
outputs are O-23 (U+202F before AM/PM, `ByteCountFormatter` wording). Locale.current is SwiftFormatters.CurrentLocale,
whose default is the bundle-matched en_<region> id of ED-132 (O-37). `TimeZone(identifier: id)` is
`TimeZoneInfo.TryFindSystemTimeZoneById(id, out var zone)` (IANA ids work on Windows through ICU), and an unknown id
falls through to the local zone exactly as ED-034 decides. `Calendar.current` is an open decision (section 7,
open questions): a site that uses it stops and reports until it is decided.

**R-FMT-3. `String(format:)` (6) by specifier.** `%.1f` is `SwiftNumber.Fixed(x, 1)`; `%04d`, `%02d` are
`n.ToString("D4" or "D2", CultureInfo.InvariantCulture)` for a non-negative `n` (printf counts the sign in the width
and .NET does not, so a negative value is padded by hand); `%06X` is `n.ToString("X6", CultureInfo.InvariantCulture)`
for a non-negative `n`; `%@` is the string; `%d`/`%ld` is the invariant integer. The POSIX locale applies: the
decimal point is always `.`.

### 3.12 UUIDs, dates and clocks (ED-073, ED-068)

**R-UUID-1. `uuidString` is `SwiftUUID.UuidString(id)`: uppercase 8-4-4-4-12**, in file names
(`Storage.swift:51`, `"\(id.uuidString).json"`), JSON (R-CODE-7) and everywhere else. `.lowercased()` is written
literally (`SwiftString.Lowercased(SwiftUUID.UuidString(id))`), `.prefix(8)` is `SwiftString.Prefix(..., 8)`.
`UUID(uuidString: s)` is `SwiftUUID.Parse(s)` (the hyphenated 36-character form, either case). `UUID()` is
`Guid.NewGuid()`. `Guid.ToString()` (lowercase), `Guid.Parse` and `Guid.TryParse` (braces, no hyphens, hex
structs) never appear outside `Port/`.
*Evidence:* `SwiftUUIDTests.UuidString_is_uppercase_where_Guid_ToString_is_lowercase`,
`UUID_parsing_takes_only_the_hyphenated_36_character_form`, `Guid_TryParse_accepts_forms_Foundation_rejects`.

**R-TIME-1. `Date` is a UTC `DateTimeOffset` with Swift's Double arithmetic through `SwiftDate`.** `Date()` and `.now`
are `SwiftDate.Now` (the `SwiftClock` provider, so tests control it); `timeIntervalSince` is
`SwiftDate.TimeIntervalSince`; `addingTimeInterval` is `SwiftDate.AddingTimeInterval`; `timeIntervalSince1970` and
`Date(timeIntervalSince1970:)` are `Seconds1970` and `From1970Seconds`; `distantFuture` is `SwiftDate.DistantFuture`
(4001-01-01, exact). `distantPast` is Julian 0001-01-01, two days before anything a `DateTimeOffset` holds, so
`SwiftDate.DistantPast` is a sentinel (`DateTimeOffset.MinValue`) that sorts first and converts back to exactly
`DistantPastSeconds`; any other arithmetic on it stops and reports. Conversions are exact from 2018 on; before that
a 100 ns tick is coarser than the Double, and a site that compares such dates for exact equality after arithmetic
stops and reports. `DateTime.Now`, `DateTime.UtcNow`, `DateTimeOffset.Now` and `DateTimeOffset.UtcNow`
are never read.
*Evidence:* `SwiftDateTests.Reference_seconds_round_trip_exactly_for_present_day_dates`,
`Before_2018_a_tick_is_coarser_than_the_Double`, `Arithmetic_is_in_Double_seconds`,
`Distant_past_is_a_sentinel_and_distant_future_is_exact`, `TimingShapeTests.Date_now_reads_the_same_clock_the_timers_run_on`.

**R-TIME-2. `ContinuousClock` (18) and `Duration`.** `ContinuousClock.now` is `SwiftClock.Now` (a `ContinuousInstant`
on the provider's monotonic timestamp); `Duration` is `TimeSpan`; `.seconds(n)`/`.milliseconds(n)` are
`TimeSpan.FromSeconds`/`FromMilliseconds` (a Double interval in a sleep goes through `SwiftTask.Seconds`, R-TASK-3).
`Stopwatch`, `Environment.TickCount64` and `DateTime` differences never measure elapsed time. `ACPSession.swift:16-19`
schedules with the wall clock and stays on `SwiftDate.Now`.
*Evidence:* `TimingShapeTests.Continuous_instants_subtract_to_durations`.

**R-TIME-3. ISO-8601.** `ISO8601.string(from:)` and `ISO8601DateFormatter().string(from:)` are
`FoundationISO8601.Format` (whole seconds by floor, UTC, `Z`). `ISO8601.date(from:)` tries
`FoundationISO8601.ParseFractional` and `ParsePlain` in the Swift's order; `dateOnly` is `ParseDateOnly`; each style
rejects the other's shape. Whether `ISO8601FormatStyle` accepts offsets such as `+03:00` is O-17. Storage decoding is
R-CODE-6.
*Evidence:* `ISO8601_writes_whole_seconds_by_floor_in_UTC`, `The_fractional_and_plain_styles_each_reject_the_other_shape`.

### 3.13 Paths, files, processes, settings, secrets, network

**R-PATH-1. Swift path and URL semantics, never `System.IO.Path` or `Uri`.** `(p as NSString).lastPathComponent` is
`SwiftPath.LastPathComponent` (`"a/b/"` gives `b`; `Path.GetFileName` gives `""`). Everything else waits for the
path unit (section 4.3): `URL(fileURLWithPath:)`, `appendingPathComponent`, `deletingLastPathComponent`,
`pathExtension`, `standardizedFileURL`, `.path` (no trailing separator), and the one absoluteness predicate and
normalization that ED-019, ED-026, ED-036, ED-038, ED-039 and ED-045 decide. `URL(string:)` accepts almost anything
since macOS 14 (percent-encoding invalid characters, relative references); `url.host` keeps its case and drops IPv6
brackets; `url.path` is decoded: all of that is the URL unit's, never `new Uri(...)` at a call site.

**R-FS-1. Files.** Read bytes, then decode as R-STR-8. Every write, replace, rename and delete goes through the
PA-0015 file unit: `.atomic` writes are a temporary file plus a replace, retried with backoff on a transient sharing
violation (waits 100, 200, ..., 700 ms on SwiftClock, 8 attempts, 2.8 s in total, ED-122; transient errors as
ED-125 classifies them), read-only cleared before delete, and the bytes verified. `fileExists(atPath:)` is true for a directory
too (`File.Exists(p) || Directory.Exists(p)`); `removeItem` on a directory is recursive; `contentsOfDirectory` order
is unspecified (R-COLL-2); `isExecutableFile` is PATHEXT (ED-031). Tests use real temporary folders under
`MARROW_PORT_DATA_DIR`, never a file-system abstraction.

**R-PROC-1. Processes follow ADR-0002: a hand-rolled `CreateProcessW`** with the Job Object (and ConPTY for the
terminal) in one attribute list, in the process unit (section 4.3). `Process.Start` and `UseShellExecute` never
appear. The command line is built from the Swift argv with the MSVCRT quoting rules (what `CommandLineToArgvW`
parses back); nothing passes through `cmd /c` unless a recorded decision says so. `process.environment = env`
replaces the whole environment: the block is built exactly as the Swift builds it (PA-0009), environment
dictionaries use `OrdinalIgnoreCase` (ED-128), so a name set again in another case keeps the first spelling and takes
the last value, and the block is sorted as Windows requires. Executables resolve by PA-0010,
PA-0016 and ED-020. No input means stdin at the null device. `Shell.run`'s order is the contract: deadline, the
termination ladder of ED-047 as ED-126 applies it (through `GlobalQueue.AsyncAfter`), exit status (not decided yet:
an owner question of 2026-09-23).

**R-PROC-2. Pipes.** Each output stream is read as bytes by its own reader on the pool; lines are split on the byte
the Swift splits on (`0x0A`), never with `StreamReader.ReadLine`, which also ends a line at a lone `\r`. The bounded
tail of PA-0008 (trim to `limit` once the buffer passes `2 * limit`) and the 3 s drain window
(`finished(within: 3)`) are kept; the termination callback can run before the pipes drain, as in Swift.
`StdioProcess.send` framing (`\n` or `Content-Length: N\r\n\r\n`) is written in order on its serial queue.

**R-PROC-3. Decisions recorded in Node terms (ED-022, ED-024, ED-042, ED-044) keep their intent.** ED-024: the
argv is one JSON token built exactly as the source builds it, quoted by the MSVCRT rules, and the assembled
command line is measured against 32,767 characters before the spawn, failing with an explicit error rather than
truncating. ED-022: `DROPPY_CODE_MCP_BRIDGE` holds the file URL Node's `pathToFileURL` would produce (test `#`, `%`,
space and non-ASCII paths; do not assume `Uri.AbsoluteUri` agrees), while `--mod` and `-e` take the bare path.
ED-042 governs the JavaScript bridge text the app writes (`MCPBridgeSource.swift`) and applies to that text as
written. ED-044: the file identity is the volume serial number and file index from `GetFileInformationByHandle`
(P/Invoke, in Services), null when the index is 0 or the call fails.

**R-SET-1. `UserDefaults` (ED-079).** `UserDefaults.standard` is `UserDefaults.Standard.Current` (an
`ISettingsStore`, unbound until the production store is decided, and trapping rather than silently losing a write).
Key names are the source's, written out. `object(forKey: k) as? Bool ?? d` is `Plist.AsBool(store.Object(k)) ?? d`
(and `AsInt`, `AsDouble`): absent is distinct from `false` and 0, and the property-list bridging of `as?` applies (a
stored integer 1 reads as `true`). `bool(forKey:)` is `store.Bool(k)` (absent is `false`); `string(forKey:)` is
`store.String(k)` (an integer renders as text; a Double traps until O-10 settles it); `data`, `array`,
`dictionary` and `stringArray` have their getters. `integer(forKey:)` and `double(forKey:)` convert more types than
`as?` does: they are not in the foundation, so a unit that needs them stops and reports. A JSON blob in defaults
(`AppSettings.swift:860-866`) is written by the plain encoder (R-CODE-4).
*Evidence:* `UserDefaultsTests` (5 tests).

**R-SEC-1. Keychain (8 `SecItem*`, Services only)** goes to the secret-store unit: DPAPI CurrentUser or Credential
Manager (ADR-0002 deviation 8), keeping the service (`AppInfo.name`, renamed as OD-0012 decides) and account names
(`"MCP " + server + " " + field`, `MCPKeychain.swift:7-9`), trimming on write and read, "empty means delete", the
Boolean "did the store take it" result, delete-then-add (`APIKeychain.swift:22-40`) or update-then-add
(`MCPKeychain.swift:42-49`) per site, and the defaults fallback copies only when the store refused
(`AppSettings.swift:434-437`, `:479-482`). A secret is never logged, never put in a test, and never read from another
app's credential file.

**R-NET-1. `URLSession` (41) is one shared `HttpClient`** in the HTTP unit (section 4.3), configured as
`URLSession.shared` behaves: redirects followed; gzip, deflate and br decoded; a non-2xx status is a response, not
an error (never `EnsureSuccessStatusCode`); `timeoutIntervalForRequest` (60 s) is an inactivity limit re-armed by
every received chunk, not a total, so `HttpClient.Timeout` is infinite and the unit arms its own timer; cancellation
surfaces as the Swift error (`URLError(.cancelled)`), not `CancellationError` (R-TASK-2). The default `User-Agent`
(`Droppy%20Code/<build> CFNetwork/...`) and cookie persistence are observable to servers: a site that depends on
them stops and reports.

**R-NET-2. Network.framework (`NWListener`, `NWConnection`: `MCPProxy.swift`, `MCPOAuth.swift`)** is loopback sockets
in Services, reproducing the byte framing ED-043 decides (one 1-byte chunk per source byte, one write per frame).

### 3.14 Errors, lifetime, collections, conditionals

**R-ERR-1. `try?`, `catch` and traps (ED-078).** `try? f()` is `SwiftTry.Optional(() => F())`
(`OptionalValue` for a value type, `Ignore` for a discarded result, and the `Async` forms after `await`); it turns
every thrown error, `CancellationError` included, into `null`, and never a trap. A Swift catch-all
`do { } catch { }` is `catch (Exception error) when (SwiftTry.IsThrownError(error))`; `catch let e as T` is
`catch (T e)`; `catch is CancellationError` is `catch (CancellationError)`. Traps are never caught:
`x!` is `SwiftTraps.Unwrap(x)`; `fatalError(m)` and a failed `precondition(c, m)` are `throw SwiftTraps.Fatal(m)`;
`assert` and `assertionFailure` are `Debug.Assert` (both vanish in a release build); an index out of range, an
overflow, a failed checked cast and a division by zero are the .NET exceptions `IsThrownError` excludes. A
non-throwing task's body that throws ends the process (R-TASK-1).
*Evidence:* `SwiftErrorTests.Try_question_turns_thrown_errors_and_cancellation_into_nil`,
`Try_question_never_swallows_a_trap`, `Try_question_await_behaves_the_same`, `A_force_unwrap_of_nil_traps`,
`SwiftTaskTests.A_throwing_task_delivers_its_error_and_a_trap_in_a_task_escapes_to_the_main_loop`.

**R-ERR-2. `error.localizedDescription` (57) is `SwiftErrors.LocalizedDescription(error)`**: a `LocalizedError`'s
`errorDescription`; for a Swift error without one, Foundation's generic text with U+2019 and the domain and code
(O-14). For a Foundation `NSError` (file, URL) Apple's localized text cannot be reproduced on Windows: the helper
throws instead of inventing, and each such site (`MCPStore.swift:352`, `UsageViews.swift:301`,
`ACPSession.swift:616`) needs a recorded decision (section 6).
*Evidence:* `LocalizedDescription_gives_the_description_or_Foundations_generic_text`,
`LocalizedDescription_refuses_to_invent_text_for_a_foreign_error`.

**R-ERR-3. Error types and `defer`.** A Swift error type is a class deriving `SwiftError`: `errorDescription` is
`ErrorDescription` (user-visible text, byte for byte); an error enum keeps its cases as a nested record union the
catch site switches over; an error Foundation would print by domain and code implements `ISwiftErrorIdentity`.
`defer` (58) is `try`/`finally`: it runs after the return value is evaluated, in reverse order, once per loop
iteration, and a `defer` inside `withLock` stays inside the lock. `@discardableResult` needs nothing.

**R-LIFE-1. `deinit` and `[weak self]` become explicit ends of life.** ARC ends an object when its last strong
reference goes; the GC does not. Each `deinit` (7) that removes an observer, cancels work or closes a handle is
`Dispose`, called at the exact point the Swift releases the last reference (the owner sets the field to `null`,
removes it from a dictionary, or the control detaches); an `isolated deinit` disposes on main. Each `[weak self]`
loop or handler that "stops when the owner dies" (113; `StdioProcess.swift:55-59`, `JSONRPC.swift:61-67`) is
cancelled explicitly in that `Dispose`. No finalizers; a `WeakReference<T>` alone never ends anything.

**R-LIFE-2. Statics, singletons and `lazy`.** A Swift global or `static let` initializes lazily, once, thread-safely,
on first access, and several have side effects (`TokenLedger()` reads files; `MCPProxy.portBox` reads defaults).
C# static field initializers can run earlier, so a side-effecting one is `Lazy<T>` (or a static constructor); a
`static let shared` that tests replace is an `Ambient<T>`. `lazy var` (4) is a field with `??=`.

**R-COLL-1. Sorting is stable, with the Swift predicate.** `sorted(by:)` and `sort(by:)` are `OrderBy` or
`OrderByDescending` with the same key or a comparer built from the predicate; `List<T>.Sort` and `Array.Sort` are
unstable and never port a Swift sort. `sorted()` on strings uses `SwiftStringComparer.Instance`.
`min(by:)`/`max(by:)` keep the first extreme, as LINQ `MinBy`/`MaxBy` do.
*Evidence:* `ValueCollectionTests.OrderBy_is_stable_and_List_Sort_is_not`,
`SwiftStringTests.Ordering_is_by_scalar_value_not_UTF16_unit_and_default_OrderBy_is_cultural`.

**R-COLL-2. `Dictionary` and `Set` order is unspecified in Swift** (randomly seeded per process). Transcribe the
source's sorts exactly. Where the Swift shows or writes an unsorted dictionary's order (UI, file, JSON, argv), the
port keeps its own order and no test asserts one; if a requirement depends on that order, stop and report.

**R-COLL-3. Absent values and traps.** `first`, `last`, `min()`, `max()`, `popLast()`, `randomElement()` and
`dict[key]` give `nil`: write `list.Count > 0 ? list[0] : null` and `TryGetValue`, never `FirstOrDefault()`,
`Min()` or `GetValueOrDefault()` on a value type (they give 0 or throw). An out-of-range subscript,
`removeFirst()`/`removeLast()` on an empty array and `Dictionary(uniqueKeysWithValues:)` with a repeated key trap:
the last is a `TryAdd` loop that throws `SwiftTraps.Fatal`, because `ToDictionary`'s `ArgumentException` would be
swallowed by a ported `try?`. `Dictionary(grouping:by:)` is `GroupBy` with unspecified key order; `reduce(into:)`
is a loop.

**R-COND-1. Optionals and conditions.** `T?` is `T?`; `if let x = y` is `if (y is { } x)`; `guard let x = y else
{ return }` is `if (y is not { } x) return;`; `x == nil` is `x is null`; `??` and `?.` port as written; `if case` and
`switch` patterns with `where` are `is`/`switch` patterns with `when`; a `Bool?` condition is compared explicitly
(`x == true`). `#if DEBUG` (2) stays `#if DEBUG`. `@available` and `#available` (11) gate macOS APIs and are always
true on the deployment target (macOS 26): port the available branch only.

---

## 4. The foundation helper types: build these first

Everything in this section exists, builds and passes in `E:\Droppy\work\conventions\foundation-proto` (Appendix
C). Signatures are final: a unit that needs a change stops and reports (section 6). Namespaces are
`Marrow.Core.Port`, `Marrow.UI.Port` and `Marrow.Testing`; every file starts with `PORT-ONLY` and names the draft
it came from.

### 4.1 The types

Core (`src/Marrow.Core/Port`, .NET only). Counts in the Tests column are test methods; a theory adds rows.

| File | Final signature (abridged to the public surface) | Swift construct served | Tests |
|---|---|---|---|
| `Ambient.cs` | `sealed class Ambient<T> where T : class`: `Ambient(T)`, `Ambient(Func<T>)`, `T Current`, `void SetProcessDefault(T)`, `IDisposable Override(T)` (per async flow) | statics and singletons tests replace (`static let shared`, `UserDefaults.standard`, the main executor, the clock) | `MainActorTests.The_main_actor_override_is_scoped_to_the_async_flow`, `HeadlessTests.An_async_local_override_flows_into_work_posted_to_the_UI_thread` |
| `Executors.cs` | `interface IExecutor { void Enqueue(Action) }`; `interface IMainExecutor : IExecutor { bool CheckAccess() }`; `ThreadPoolExecutor.Shared`; `sealed class SerialExecutor(IExecutor? target = null) : SynchronizationContext, IExecutor` (`bool IsCurrent`; `Send` from outside traps) | executors under actors and queues | through `ActorTests`, `SerialQueueTests` |
| `MainActor.cs` | `static class MainActor`: `Ambient<IMainExecutor> Executor`, `Bind(IMainExecutor)`, `bool IsIsolated`, `AssumeIsolated()`, `AssumeIsolated(Action)`, `T AssumeIsolated<T>(Func<T>)`, `Post(Action)`, `WorkItem AsyncAfter(TimeSpan, Action)`, `TaskHandle Spawn(Func<Task>)`, `TaskHandle<T> Spawn<T>(Func<Task<T>>)`, `TaskHandle<T> SpawnThrowing<T>(Func<Task<T>>)`, `Task<T> Run<T>(Func<T>)`, `Task Run(Action)`, `Task<T> Invoke<T>(Func<Task<T>>)`, `Task Invoke(Func<Task>)`; `sealed class WorkItem(Action)`: `Cancel()`, `IsCancelled` | `@MainActor`, `DispatchQueue.main.async`/`asyncAfter`, `MainActor.run`, `assumeIsolated`, `Task { @MainActor in }`, `DispatchWorkItem` | `MainActorTests` (13) |
| `SwiftTask.cs` | `static class SwiftTask`: `IsInTask`, `IsCancelled`, `CancellationToken`, `CheckCancellation()`, `Task Sleep(TimeSpan)`, `Task<bool> TrySleep(TimeSpan)`, `TimeSpan Seconds(double)`, `Task<T> WithCancellationHandler<T>(Func<Task<T>>, Action)`, `Task WithCancellationHandler(Func<Task>, Action)`; `sealed class CancellationError : OperationCanceledException, ISwiftErrorIdentity`; `class TaskHandle`: `Completion`, `IsCancelled`, `Cancel()`, `static Detached(Func<Task>)`, `static Detached<T>`, `static DetachedThrowing<T>`; `sealed class TaskHandle<T>`: `Task<T> Value`; `sealed class TaskGroup<T> : IAsyncEnumerable<T>`: `AddTask`, `Task<(bool HasValue, T Value)> Next()`, `CancelAll()`, `IsCancelled`, `static Run(...)`; `sealed class TaskGroup` (Void); `sealed class AsyncLetScope : IAsyncDisposable`: `Task<T> Start<T>(Func<Task<T>>)` | `Task`, `Task.detached`, `Task.isCancelled`, `checkCancellation`, `Task.sleep`, `withTaskCancellationHandler`, `withTaskGroup`, `async let` | `SwiftTaskTests` (16) |
| `SwiftClock.cs` | `readonly record struct ContinuousInstant(long Ticks)` with `-`, `+` and comparisons; `static class SwiftClock`: `Ambient<TimeProvider> Provider`, `ContinuousInstant Now` | `ContinuousClock`, the one clock every timer and stamp uses | `TimingShapeTests` (4) |
| `Actor.cs` | `abstract class Actor(IExecutor? target = null)`: `IsIsolated`, protected `AssertIsolated()`, `Isolated<T>(Func<Task<T>>)`, `Isolated<T>(Func<T>)`, `Isolated(Func<Task>)`, `Isolated(Action)`, `TaskHandle Spawn(Func<Task>)`; `sealed class SerialQueue(string label, IExecutor? target = null)`: `Label`, `IsCurrent`, `Async(Action)`, `WorkItem AsyncAfter(TimeSpan, Action)`, `T Sync<T>(Func<T>)`, `Sync(Action)`; `static class GlobalQueue`: `Async(Action)`, `WorkItem AsyncAfter(TimeSpan, Action)` | `actor`, `DispatchQueue(label:)`, `DispatchQueue.global()` | `ActorTests` (5), `SerialQueueTests` (5) |
| `AsyncStream.cs` | `sealed class AsyncStream<T> : IAsyncEnumerable<T>`: `enum Termination { Finished, Cancelled }`, `enum YieldResult { Enqueued, Terminated }`, `static (AsyncStream<T>, Continuation) MakeStream()`, `static AsyncStream<T> Create(Action<Continuation>)`; `Continuation`: `YieldResult Yield(T)`, `Finish()`, `Action<Termination>? OnTermination` | `AsyncStream`, `makeStream`, `for await` | `AsyncStreamTests` (7) |
| `Continuations.cs` | `sealed class CheckedContinuation<T>`: `Resume(T)`, `Resume(Exception)`, `static Task<T> With(Action<CheckedContinuation<T>>)`; `sealed class Mutex<T>(T)`: `TResult WithLock<TResult>(Body<TResult>)`, `WithLock(Body)`, `delegate TResult Body<TResult>(ref T value)` | `withChecked(Throwing)Continuation`, `Mutex`, `OSAllocatedUnfairLock` | `ContinuationTests` (5), `SerialQueueTests.Mutex_reentry_traps_where_a_plain_Lock_would_succeed` |
| `Observation.cs` | `abstract class ObservableModel : INotifyPropertyChanged`: protected `Read`, `Set(ref T, T, Action<T>? didSet)`, `SetAlways(...)`, `Computed(Func<T>)`, `Notify(string)`, virtual `CheckIsolation()`, public `TrackerCount(string)`; `abstract class MainActorModel : ObservableModel`; `static class ObservationTracking`: `WithTracking<T>(Func<T>, Action)`, `Track<T>(Func<T>, Action)`; `sealed class ObservationTracker`: `Cancel()` | `@Observable`, `didSet`, computed properties, `withObservationTracking`, `@MainActor @Observable` | `ObservationTests` (8) |
| `SwiftErrors.cs` | `sealed class SwiftTrapException(string)`; `static class SwiftTraps`: `Unwrap<T>(T?)` (class and struct), `Fatal(string)`; `static class SwiftTry`: `IsThrownError(Exception)`, `Optional`, `OptionalValue`, `Ignore` and their `Async` forms; `interface ILocalizedError`; `interface ISwiftErrorIdentity`; `abstract class SwiftError : Exception, ILocalizedError`; `sealed class EncodingError`; `static class SwiftErrors`: `CouldNotComplete`, `LocalizedDescription(Exception)` | traps, `x!`, `fatalError`, `try?`, `catch`, `LocalizedError`, `localizedDescription` | `SwiftErrorTests` (6) |
| `IcuNative.cs` | internal: `u_strToLower`, `u_strToUpper`, `u_strFoldCase`, `u_hasBinaryProperty`, `u_getIntPropertyValue` from `System32\icu.dll`; per-scalar `Map` | ICU behind case mapping and Character properties | through `SwiftStringTests` |
| `SwiftString.cs` | `static class SwiftString`: `Characters`, `Count`, `Utf8Count`, `Prefix`, `DropFirst`, `Suffix`, `DropLast`, `IsCharacterBoundary`, `Equal`, `Compare`, `HasPrefix`, `HasSuffix`, `Split(string, string, int maxSplits = int.MaxValue, bool omittingEmptySubsequences = true)`, `ComponentsSeparatedByNewlines`, `Lowercased`, `Uppercased`, `CaseInsensitiveCompare`, `LocalizedCaseInsensitiveContains(string, string, CultureInfo? = null)`; `sealed class SwiftStringComparer : IEqualityComparer<string>, IComparer<string>` (`Instance`); `static class SwiftCharacter`: `IsNumber`, `IsLetter`, `IsWhitespace` | `String`, `Character`, `hasPrefix`, `split`, `lowercased`, `localizedCaseInsensitiveContains` | `SwiftStringTests` (16) |
| `FoundationText.cs` | `static class FoundationText`: `IsWhitespace(char)`, `IsNewline(char)`, `Trim(ReadOnlySpan<char>, bool includeNewlines = false)`, `Trim(string, bool includeNewlines = false)` | `trimmingCharacters(in:)` | `Trimming_uses_Foundation_sets_not_String_Trim` |
| `SwiftNumber.cs` | `static class SwiftNumber`: `ExponentialAbove`, `Description(double)`, `JsonText(double)`, `Rounded(double)`, `TruncatingToInt(double)`, `long? ParseInt(string)`, `double? ParseDouble(string)`, `Fixed(double, int)`; `SwiftFormat.DecimalNoFraction(double, CultureInfo)`; `SwiftScanner.ScanHexInt64(string, out ulong)`; `SwiftText.DecodeUtf8OrNil(ReadOnlySpan<byte>)`, `DecodeUtf8Repairing(ReadOnlySpan<byte>)` | `Double.description`, `Int(String)`, `.rounded()`, `Int(double)`, `String(format:)`, `NumberFormatter`, `Scanner`, `String(data:encoding:)` | `SwiftNumberTests` (14) |
| `SwiftUUID.cs` | `static class SwiftUUID`: `UuidString(Guid)`, `Guid? Parse(string?)`, nested `JsonConverter : JsonConverter<Guid>` | `uuidString`, `UUID(uuidString:)`, Codable `UUID` | `SwiftUUIDTests` (4) |
| `SwiftDate.cs` | `static class SwiftDate`: `ReferenceTo1970`, `DistantPastSeconds`, `ReferenceDate`, `DistantPast`, `DistantFuture`, `Now`, `ReferenceSeconds`, `FromReferenceSeconds`, `Seconds1970`, `From1970Seconds`, `AddingTimeInterval`, `TimeIntervalSince`; `static class FoundationISO8601`: `Format`, `StorageDate`, `StorageDateWholeSecondsOnly` (parked, O-02), `ParseFractional`, `ParsePlain`, `ParseDateOnly` | `Date`, `ISO8601` | `SwiftDateTests` (8) |
| `SwiftJson.cs` | `sealed record SwiftJsonStyle(bool SortedKeys, bool PrettyPrinted, bool EscapeSlashes)` with `Storage`, `PlainEncoder`, `PrettySorted`, `SerializationCompact`, `SerializationPretty`; `static class SwiftJson`: `SortedKeyOrder`, `DocumentOptions`, `byte[] Write(JsonElement, SwiftJsonStyle)`, `WriteString`, `byte[] Encode<T>(T, JsonSerializerOptions, SwiftJsonStyle)`, `WriteDouble(Utf8JsonWriter, double)`, `WriteDouble(Utf8JsonWriter, string, double)`; `sealed class FoundationKeyComparer` (parked, O-01) | `JSONEncoder`, `JSONSerialization` output, reading limits | `SwiftJsonTests` (11) |
| `SwiftDecoding.cs` | `sealed class DecodingError(string kind, string message) : JsonException` (`Kind`); `static class SwiftDecoding`: `delegate T Reader<out T>(JsonElement)`, `Decode`, `DecodeIfPresent` (class), `DecodeIfPresentValue` (struct), `Value`, `LenientList`, `ReadString`, `ReadBool`, `ReadInt`, `ReadDouble`, `ReadUUID`, `ReadStorageDate`, `ReadDeferredDate`, `ReadRawValue<T>`, `ValueList<T> ReadArray<T>`, `ValueMap<string, T> ReadMap<T>`; `static class SwiftEnumCoding`: `ReadCase`, `WriteCase`; `static class SwiftJSONNumber`: `IsInt(JsonElement, out long)` | `init(from:)`, `decodeIfPresent`, `value(_:default:)`, `Lenient`, SE-0295 enums | `SwiftDecodingTests` (9) |
| `SwiftEnum.cs` | `sealed class RawValueAttribute(string)`; `static class SwiftEnum<TEnum> where TEnum : struct, Enum`: `AllCases`, `RawValue(TEnum)`, `TEnum? FromRawValue(string?)` | `enum X: String, CaseIterable` | `SwiftEnumTests` (7) |
| `ValueCollections.cs` | `sealed class ValueList<T> : ReadOnlyCollection<T>, IEquatable<ValueList<T>>` (`Empty`, `Appending`, `SetItem`, `Mutating`, from `T[]`); `sealed class ValueMap<TKey, TValue> : ReadOnlyDictionary<TKey, TValue>, IEquatable<...>` (comparer, last key wins, `Mutating`); `sealed class ValueSet<T> : IReadOnlyCollection<T>, IEquatable<ValueSet<T>>` (`Contains`, `Mutating`) | `Array`, `Dictionary`, `Set` inside value types | `ValueCollectionTests` (6) |
| `UserDefaults.cs` | `interface ISettingsStore { object? Object(string); void Set(string, object?); void RemoveObject(string); }`; `static class UserDefaults`: `Ambient<ISettingsStore> Standard` (unbound traps); `static class Plist`: `AsBool`, `AsInt`, `AsDouble`, extension `Bool`, `String`, `Data`, `Array`, `Dictionary`, `StringArray` | `UserDefaults`, `as?` bridging | `UserDefaultsTests` (5) |
| `SwiftPath.cs` | `static class SwiftPath`: `LastPathComponent(string)` | `lastPathComponent` | through `SwiftStringTests` and 4.3 |

UI (`src/Marrow.UI/Port`, Avalonia):

| File | Final signature | Swift construct served | Tests |
|---|---|---|---|
| `AvaloniaMainExecutor.cs` | `sealed class AvaloniaMainExecutor : IMainExecutor` (`Instance`; `Enqueue` is `Dispatcher.UIThread.Post(work, DispatcherPriority.Default)`) | the main thread | `HeadlessTests` (6) |
| `ViewSupport.cs` | `abstract record Animation`; `static class Transaction`: `Current`, `WithAnimation(Animation?, Action)`; `sealed class Tracked<T>(Func<T>, Action<T>) : IDisposable`; `sealed class OnChange<T>(Func<T>, Action<T, T>, bool initial = false) : IDisposable`; `sealed class ViewTask<TId>(TId, Func<TId, Task>)`: `Running`, `Appear()`, `Disappear()`, `UpdateId(TId)`; `sealed class Binding<T>(Func<T>, Action<T>)`: `Value`, `static Constant(T)` | body re-evaluation, `withAnimation`, `.onChange`, `.task(id:)`, `Binding` | `ViewSupportTests` (5) |
| `ViewLifetime.cs` | `static class ViewLifetime`: `control.Track(value, apply)`, `control.Task(id, body)`; `sealed class EnvironmentValues`: `AccessibilityReduceMotionProperty` (inherited attached); `interface IPopoverPresenter`, `static class Presentation`: `Ambient<IPopoverPresenter> Presenter`; `sealed class ForEach<TItem, TId>(Panel, Func<TItem, TId>, Func<TItem, Control>, Action<Control, TItem>)`: `Apply(IReadOnlyList<TItem>)` | view lifetimes, `@Environment`, `.popover`, `ForEach` identity | `HeadlessTests` |

Tests (`tests/Marrow.Testing`, a class library, `IsTestProject` false, referencing Marrow.Core and xunit):

| File | Final signature | Serves | Tests |
|---|---|---|---|
| `ManualMainLoop.cs` | `sealed class ManualMainLoop : IMainExecutor`: `Context`, `Pending`, `Enqueue`, `int RunUntilIdle()`, `RunUntil(Func<bool>, TimeSpan?)`, `T Run<T>(Func<Task<T>>, TimeSpan?)`, `Run(Func<Task>, TimeSpan?)` | a deterministic main thread | every concurrency test |
| `ManualTimeProvider.cs` | `sealed class ManualTimeProvider(DateTimeOffset start) : TimeProvider`: `Elapsed`, `ActiveTimers`, `Advance(TimeSpan, Action? afterEach = null)` (fires due timers in order) | a clock that moves only when told | `TimingShapeTests` |
| `TestRuntime.cs` | `sealed class TestRuntime : IDisposable`: `Loop`, `Time`, `Defaults`, `RunUntilIdle()`, `OnMain(Action)`, `T OnMain<T>(Func<T>)`, `Advance(TimeSpan)` (pumps main after every timer), `AdvanceMilliseconds(double)`; `sealed class InMemorySettingsStore : ISettingsStore` (`Writes`) | one runtime per test: main, clock and defaults overridden per async flow | everywhere |
| `UnionCoverage.cs` | `static class UnionCoverage`: `Cases<TUnion>()`, `AssertCovers<TUnion>(params TUnion[])`; `sealed class ChangeLog(INotifyPropertyChanged) : IDisposable` | exhaustiveness of record unions; what a binding was told | `SwiftEnumTests`, `ObservationTests` |
| `Traceability.cs` | `static class Traceability`: `AssertEveryTestNamesWhatItProves(Assembly)`, `AssertShapesReqVerifyCanRead(Assembly)`, `AssertSourceLayout(Assembly, string projectDirectory)` | the Gate D shape (section 5) | `TraceabilityTests` in each test project |

### 4.2 Adopting the foundation into the port (orchestrator steps)

1. Copy `foundation-proto\src\Marrow.Core\Port\*.cs` to `src\Marrow.Core\Port\` and `src\Marrow.UI\Port\*.cs` to
   `src\Marrow.UI\Port\`. Delete `src\Marrow.Core\Support\FoundationText.cs` (its public copy is in `Port/`,
   ED-085) and fix the usings.
2. Add `tests\Marrow.Testing\` to `Marrow.sln` and reference it from every test project (ED-080; a new project).
3. Copy the tests: `tests\Marrow.Core.Tests\Port\*`, `Fixtures\*` and `TraceabilityTests.cs`;
   `tests\Marrow.UI.Tests\Port\*`, `Fixtures\HeadlessFixtures.cs` and `TraceabilityTests.cs`; add a
   `TraceabilityTests.cs` to `Marrow.Services.Tests` and `Marrow.App.Tests`.
4. `Directory.Build.props`: `CheckForOverflowUnderflow` true (ED-074), `NoWarn` CS8524 (ED-075),
   `AnalysisLevel` latest (ED-076). `.editorconfig`: CA1304, CA1305, CA1307, CA1309, CA1310, CA1311 at `error`
   (ED-076). The trial on a copy of the port at HEAD 4f70ef7 found exactly 8 errors, all in tests:
   `AntiSlopPolicyTests.cs` lines 65, 80, 81, 82, 83, 116 (CA1307), `MCPCatalogTests.cs:54` (CA1305),
   `CodingTests.cs:37` (CA1307), each a one-line fix (a `StringComparison` or a culture argument) not yet applied
   anywhere. With the analyzers off and checked arithmetic and CS8524 on, all 239 + 10 + 2 existing tests pass.
5. Convert the existing `[Fact] // PR-...` comments to `[Trait("Req", "PR-...")]` (R-TEST-1), so the meta-tests
   pass on the existing tests.
6. Run the Done commands of section 1.4, then the source-scan test once it exists (4.3).

### 4.3 The next foundation units (not in the prototype)

Each is a unit of its own, taken before the first Swift unit that needs it, with tests first:

| Unit | Place | Implements | First tests |
|---|---|---|---|
| Path identity: `appendingPathComponent`, `deletingLastPathComponent`, `pathExtension`, `standardizedFileURL`, `.path`, absoluteness, normalization, containment | `Core/Port` | ED-019, ED-026, ED-036, ED-038, ED-039, ED-045 as one predicate and one normalization; R-PATH-1 | the cases each ED lists; drive, UNC, `\\?\`, bare `\`, trailing separators |
| `URL(string:)` | `Core/Port` | the macOS 14 leniency, `host` case and brackets, decoded `path` (R-PATH-1) | `"foo bar"`, IPv6 hosts, `MarkdownView.swift:677` |
| ICU formatters | `Core/Port` | `udat` skeletons, `ureldatefmt`, `unum` spell-out, currency, `Int.formatted()`; `ByteCountFormatter` from its documented rules (R-FMT-2) | O-23 fixtures, marked Oracle until settled |
| Process launcher | `Core/Port` (Shell.swift and StdioProcess.swift are Core) | ADR-0002 `CreateProcessW` with Job Object and ConPTY; PA-0007, PA-0008, PA-0009, PA-0010, PA-0016; ED-020, ED-024, ED-027, ED-047 (R-PROC-1 to R-PROC-3) | the ladders under the manual clock; environment block order; quoting round trip; 32,767 limit |
| File operations | `Core/Port` | PA-0015: retried replace, rename and delete, read-only clearing, atomic write, verification (R-FS-1) | a handle held open while the helper retries under the manual clock |
| Production settings store | `Services/Port` | ED-079 once the format is decided | absent versus present, key names, write order |
| Secret store | `Services/Port` | ADR-0002 deviation 8; R-SEC-1 | trimming, empty means delete, the Boolean result |
| HTTP | `Services/Port` | R-NET-1 (inactivity timeout, decompression, redirects, status handling) | a loopback server |
| Frame clock | `UI/Port` | ED-083; R-VIEW-10 | a manual frame source |
| File-system watcher | `Services/Port` | R-GCD-3 (events delivered where the source delivers them, ED-116; overflow or any Error means rescan, ED-114; events waiting for the handler are batched, no latency, ED-115) | a real temporary folder |
| Source scan | `tests/Marrow.Testing` plus one test per test project | ED-076; the list in section 5.2 | planted violations |
| Citation check | `porting/tools/cite_check.mjs` | ED-081 (from proto-C) | the four wrong citations it found |

### 4.4 Repairs to existing Core code (next Core packet)

| Where | Now | Repair | Rule |
|---|---|---|---|
| `Support/Coding.cs` `Encode` | `UnsafeRelaxedJsonEscaping` plus `Replace("/", "\\/")` | `SwiftJson.Encode(value, StorageOptions, SwiftJsonStyle.Storage)`: fixes emoji, U+2028, U+FEFF and number text | R-CODE-3, ED-072 |
| `Support/Coding.cs` options | no `Guid` converter; default depth | add `SwiftUUID.JsonConverter`; read with `SwiftJson.DocumentOptions` (512) | R-CODE-7, R-CODE-8 |
| `Support/Coding.cs` `Value` | its own catch list | keep the Swift-facing `Coding.Value`, delegate to `SwiftDecoding.Value`; add `Coding.LenientList` | R-CODE-2, R-FILE-2 |
| `Support/Coding.cs` `WriteSorted`, `StorageDateConverter` | ordinal keys; lenient dates | unchanged until O-01 and O-02 | R-CODE-9, R-CODE-6 |
| `Models/MCP.cs` `MCPValues.List`/`Map` | internal wrappers | `ValueList`/`ValueMap` | R-VAL-2, ED-077 |
| `Models/MCP.cs` `MCPCategoryExtensions` | `Id` switch with discard arms | `[RawValue]` plus `SwiftEnum<T>`; no discard arms | R-ENUM-1, ED-075 |
| `Models/MCP.cs:82` | `StartsWith(x, OrdinalIgnoreCase)` for `MCP.swift:142` | the literal `lowercased().hasPrefix(x)` | R-STR-4, R-STR-6 |
| `Models/MCPCatalog.cs:80` | `ToLowerInvariant()` | `SwiftString.Lowercased` | R-STR-4 |
| `Support/FoundationText.cs` | internal, in `Support/` | `Port/FoundationText.cs`, public | R-FILE-2, ED-085 |
| `Support/Text.cs` | its own grapheme and NFC helpers | call `SwiftString` where they duplicate it | R-STR-1, R-STR-2 |
| existing tests | `[Fact] // PR-...`; 8 analyzer errors | traits; the 8 fixes of 4.2 | R-TEST-1, ED-076 |
| `Models/MCPCatalog.cs:87` | `Enum.GetValues` | `SwiftEnum.AllCases` | R-ENUM-1 |
| `Support/StdioFramer.cs:125` (the `MergeRequestLink.cs:27` site is gone at afeef2d) | `long.TryParse` | `SwiftNumber.ParseInt` | R-NUM-1 |
| `Models/MergeRequestLink.cs:55` (was :39), `Models/Requests.cs:35`, `:46` | discard arms | removed | R-ENUM-1, ED-075 |
| `Support/StdioFramer.cs:118` | `ToLowerInvariant` | `SwiftString.Lowercased` plus `SwiftString.Equal` | R-STR-4, R-STR-2 |
| `Services/MCP/MCPResolver.cs:21` | discard arm | `UnreachableException` arm plus a UnionCoverage test in Marrow.Services.Tests | R-ENUM-3 |
| `Models/MCP.cs:234-237` | converter | direct member writes | R-CODE-3 |
| every R-STR-9 site in src and tests, `Port/CocoaError.cs` included | four-digit escapes, raw non-ASCII | eight-digit escapes, each with a comment naming the character | R-STR-9 |
| `Support/StdioProcess.cs:90` | bare `catch` | `catch (Exception error) when (SwiftTry.IsThrownError(error))`, same cleanup, then `throw;` (`StdioProcess.swift:78-84`) | R-ERR-1, ED-157 |
| `Support/StdioProcess.cs:19`, `:152`, `:198` | `TaskCompletionSource<int>` outside `Port/` | `List<CheckedContinuation<int>>`, resumed after the lock is released (`StdioProcess.swift:27`, `:131-139`, `:167-171`) | R-ASYNC-2, ED-157 |
| `src/Marrow.App/Window/ThreadWindow.cs:132`, `:188` | `_ => null,` over Avalonia `PhysicalKey` | unchanged: an entry in `porting/source_scan_allow.json` (`ThreadWindow.swift:13-59`) | R-ENUM-1, ED-112, ED-157 |
| `tests/Marrow.UI.Tests/Port/SwiftColorTests.cs:162`, `:165`, `SwiftSoundTests.cs:91`, `tests/Marrow.Services.Tests/Port/FileWatcherTests.cs:217` | four-digit escapes, raw non-ASCII | eight-digit escapes with a comment naming each character, values unchanged | R-STR-9, ED-157 |
| `Support/Coding.cs:89` (was :92) `StorageIntegerConverter.Write` | `WriteNumberValue(long)` | unchanged: an entry in `porting/source_scan_allow.json` | R-CODE-5, ED-108, ED-157 |

The rows from `Models/MCPCatalog.cs:87` down are the source-scan packet, the unit Core.Repair.ScanClean. It lands
together with F.SourceScan as one merge and turns F.SourceScan's four real-tree tests green (ED-108). It covers all four layers and every finding the
real-tree tests print after its last merge of impl/b2; the allowlist holds exactly the two entries named above (ED-157).

---

## 5. Test conventions

### 5.1 Rules

**R-TEST-1. Every test names what it proves.** At least one `[Trait("Req", "PR-AREA-nnn")]` (or `UI-AREA-nnn`), a
`[Trait("Rule", "R-XXX-n")]` or a `[Trait("Decision", "ED-nnn")]`; one trait per id; ids well formed. A test that
verifies a requirement carries its `Req` trait, followed on the same line by the Swift citation comment:

```csharp
[Fact]
[Trait("Req", "PR-SIDEBAR-041")] // App/Views/Sidebar/SidebarView.swift:391-403
public void Scroll_freeze_debounce_lifts_150_ms_after_the_last_tick()
```

Checked by `Traceability.AssertEveryTestNamesWhatItProves`.

**R-TEST-2. Shapes `req_verify.mjs` can read.** It reads traits from test source, line by line (the TRX carries
none): a Req test is `public [async] void|Task|Task<T> Name(`, an instance method of a top-level class; no
`DisplayName` (it renames the TRX entry); no nested types in a test class. Checked by
`Traceability.AssertShapesReqVerifyCanRead`.

**R-TEST-3. Source layout.** `tests/Marrow.<Layer>.Tests/<Folder>/<SwiftFileBase>Tests.cs` (a `+` in the Swift base name is dropped, since a class name cannot hold it: `AppModel+Hydra.swift` is tested by `AppModelHydraTests.cs`, class `AppModelHydraTests`; the source file keeps the `+` per R-FILE-1, ED-164) mirrors the source
file, one `public sealed class <SwiftFileBase>Tests` per file (a suffix such as `MCPCatalogSecondTests` when one
Swift file needs several); helper types live in `Fixtures/`. Every attribute fits on one line and a `Req` trait
starts its own line. No `class X` or `record X` text may appear anywhere else in a test file on a line that is not
a whole-line comment: `req_verify` takes the last such word as the class of every later test, trailing comments and
string literals included (`negative-checks.log` shows a Req id credited to the wrong class). Method names are
sentences with underscores stating the Swift behaviour. Checked by `Traceability.AssertSourceLayout`.

**R-TEST-4. Expected values come from the source, and bytes are asserted as bytes.** An expectation is transcribed
from the pinned Swift (cited `file:line`), a reference capture or a settled oracle item, never from running the
port. Persisted and user-visible text is compared as the exact string or UTF-8 bytes, never through a normalizing
or JSON-equivalence comparer; key order is asserted only for sorted styles. Where a rule exists because the obvious
.NET API is wrong, one test runs that API and asserts its different result (the negative control).

**R-TEST-5. Determinism.** A test that touches main, time or defaults starts with
`using var runtime = new TestRuntime();`. No wall clock, no `Task.Delay`, no sleeping to wait for work, no real
timers: advance the manual clock and pump. Overrides are per async flow, so test classes run in parallel;
nothing process-global changes outside one non-parallel collection. A unit that adds concurrency tests runs the
whole suite five times in a row and reports all five results.

**R-TEST-6. UNVERIFIED expectations are labelled, and cannot verify a requirement.** A test whose expected value
rests on an open item carries `[Trait("Oracle", "O-nn")]`, never a `Req` trait, and says so in a comment. When the
oracle settles the item, the expectation is replaced by the settled value with its evidence, the `Oracle` trait
goes, and the `Req` trait may come. An expectation is never edited to match the port.

**R-TEST-7. Headless UI tests.** One `HeadlessUnitTestSession` per test assembly, held by a collection fixture
(`[Collection("Headless")]`); each test runs its body through `HeadlessSession.OnUiThread`, which binds
`MainActor` to Avalonia. `Avalonia.Headless.XUnit` is not used (12.1.2 depends on xunit v3; the port pins xunit
2.9.3). Presentation goes through the popover seam (R-VIEW-9). View mechanics that need no Avalonia object
(`Tracked`, `OnChange`, `ViewTask`, `Binding`) are tested on `TestRuntime`.

**R-TEST-8. Fixtures derive from the source.** A sample type is a trimmed shape of a real Swift type, cited
`file:line`, never an invented one; JSON fixtures are bytes the Swift writes, derived from the rules of 3.8 with
citations; CLI stand-ins are scripted, offline programs in `tests/fixtures`.

**R-TEST-9. Files and environment.** `MARROW_PORT_DATA_DIR` points at a throwaway folder before anything runs;
tests create their own real temporary folders under it (no file-system abstraction); nothing touches
`%LOCALAPPDATA%\Marrow`, the user's profile, the reference folder or any credential file. File-lock behaviour
(PA-0015) is tested by holding a real handle open.

**R-TEST-10. No real agents, no network, no skips.** No test sends a prompt to a real AI CLI, calls a paid API,
uses a signed-in agent or reaches the internet: processes are system binaries or `tests/fixtures` stand-ins, HTTP
is a loopback server. No `Skip`: a skipped test is not verified and hiding a failure is forbidden.

### 5.2 The source-scan list (ED-076, to be built as a unit, 4.3)

One test per test project scans its layer's `src` folder as text, outside `Port/`. Each pattern is a rule's
fingerprint; an allowed exception is listed with its Swift line in `porting/source_scan_allow.json` (a protected
file, so the orchestrator approves each entry).

| Pattern | Rule |
|---|---|
| `ConfigureAwait(`, `Dispatcher.UIThread` | R-ISO-1, R-ISO-2 |
| `Task.Delay(`, `Thread.Sleep(` | R-TASK-3 |
| `new Timer(`, `System.Timers.Timer`, `DispatcherTimer`, `timeBeginPeriod` | R-GCD-3, R-VIEW-10 |
| `DateTime.Now`, `DateTime.UtcNow`, `DateTimeOffset.Now`, `DateTimeOffset.UtcNow`, `Stopwatch`, `Environment.TickCount` | R-TIME-1, R-TIME-2 |
| `new TaskCompletionSource`, `Channel.Create`, `SemaphoreSlim` | R-ASYNC-1, R-ASYNC-2, R-ACTOR-1 |
| `.ToLower(`, `.ToUpper(`, `ToLowerInvariant(`, `ToUpperInvariant(`, `IgnoreCase` in a `StringComparison` or `StringComparer` | R-STR-4, R-STR-6 |
| `.Trim()`, `File.ReadAllText`, `File.ReadAllLines`, `File.WriteAll`, `File.AppendAll`, `.ReadLine(` | R-STR-7, R-STR-8, R-FS-1, R-PROC-2 |
| `.Parse(` or `.TryParse(` on a number, `Guid`, `Enum`; `Convert.To`; `Enum.GetValues`, `Enum.GetNames` | R-NUM-1, R-UUID-1, R-ENUM-1 |
| `Math.Round(` | R-NUM-3 |
| `JsonSerializer.Serialize`, `JsonSerializer.Deserialize`, `WriteNumberValue(` | R-CODE-1, R-CODE-3, R-CODE-5 |
| `.Sort(` | R-COLL-1 |
| `Process.Start`, `ProcessStartInfo` | R-PROC-1 |
| `new Uri(`, `Uri.TryCreate`, `Path.GetFileName`, `Path.GetExtension`, `Path.GetDirectoryName`, `Path.Combine` | R-PATH-1 |
| `catch (Exception` without `when (SwiftTry.IsThrownError`, a bare `catch` | R-ERR-1 |
| `_ =>` not followed by `throw new UnreachableException()` | R-ENUM-1, R-ENUM-3 |
| a raw non-ASCII character, a four-digit escape or an `\x` escape in a literal, in every file, tests included (an escaped backslash before `u`, as in expected JSON text, is not an escape) | R-STR-9 |

---

## 6. When the source is ambiguous or a dependency is missing: stop and report

Never invent. Stop at the site, and report, when any of these holds:

- the Swift can be read two ways with different observable results;
- the behaviour rests on a Swift or Foundation fact nobody has settled (a new oracle item, or an O item whose rule
  says stop: `Calendar.current`, `integer(forKey:)`, an `NSError` text, a visible dictionary order, `distantPast`
  arithmetic, exact equality of pre-2018 dates, the default `User-Agent` or cookies);
- a dependency is missing: a callee not yet ported, a foundation unit of 4.3 not yet built, a type in a higher
  layer;
- a rule here conflicts with a higher authority (1.2), with the Swift, or with another rule;
- the site needs something you may not do: a package, a project, a project reference, a protected file, a
  recorded decision changed, a deviation not already granted;
- a test would need a real CLI, the network, a credential or user data.

What you do: leave that site unwritten. No placeholder value, no no-op, no guessed default, no shipped
`NotImplementedException`; a unit with an open stop item is not Done. Write the tests that can be written (red is
fine before review; an expectation that rests on an open item takes the `Oracle` trait). Carry on with parts of the
unit that do not depend on the site. Report:

```
STOP <unit> #<n>
Kind: ambiguity | unverified | missing dependency | conflict | forbidden need | platform gap | untestable
Swift: DroppyCode/<path>.swift:<a>-<b> (B2 1fbf81e), quoted if three lines or fewer
Requirements: PR-..., UI-...
What is unclear or missing: <one paragraph>
Readings or options: (a) <reading> -> <observable result>; (b) ...
Needed to proceed: a decision (ED or owner), oracle item O-nn, unit <name>, package <name>, protected edit <file>
Done so far: <tests written and their state>; nothing implemented at this site
```

The orchestrator answers by recording a decision, scheduling the dependency, splitting the unit or asking the
owner. Until then the site stays unwritten.

---

## 7. Proposed engineering decisions (ED-064 to ED-086)

ED-063 is the last recorded decision at port HEAD 6da2a58 (checked 2026-09-23), so these start at ED-064. None is recorded by this
guide. Each gives the decision, why, where it came from (the drafts' own numbers), what it supersedes, its evidence
and the orchestrator action it needs. "Adoption" means the steps of section 4.2.

**ED-064 Implementing conventions.** This guide becomes `porting/IMPLEMENTING.md`: one C# file per Swift file at
the same relative path (R-FILE-1), namespaces (R-FILE-3), B2 citations (R-FILE-4), Swift names (R-NAME-1 to
R-NAME-7), the markers of 1.5 and the unit loop of 1.4. *Why:* the read ledger, the dependency graph and review all
key on Swift files; one convention for three drafts. *From:* A section 2, B section 39, C sections 2 and 3.
*Refines:* WORKER_BRIEF and HANDOFF wording listed in 1.6; nothing recorded is contradicted. *Action:* place the
file (protected path).

**ED-065 The main actor.** `MainActor` over `IMainExecutor` in Core, bound once by Marrow.App to
`AvaloniaMainExecutor` (`Dispatcher.UIThread.Post` at `DispatcherPriority.Default`, FIFO, never inline); an unbound
main actor traps; statics that tests replace are `Ambient<T>` seams overridden per async flow. *Why:* ADR-0002
constraint 11 names the dispatcher as the carrier of `@MainActor`, and 27 Core and Services main-actor sites cannot
reference Avalonia. *From:* B ED-059, C ED-060 and ED-061, A ED-058 (part). *Evidence:*
`HeadlessTests.The_main_actor_bound_to_Avalonia_runs_posts_and_spawns_FIFO_on_the_UI_thread_never_inline`,
`MainActorTests` (13), proto-B `avalonia-binding.log`. *Action:* adoption; the start-up bind in the first App unit.

**ED-066 The ambient Swift task model.** The current Swift task (its cancellation) is ambient, as in Swift: set by
every helper that starts a task (`MainActor.Spawn`, `TaskHandle.Detached`, `Actor.Spawn`, task-group and `async
let` children), kept by calls into actors and `MainActor.Run`/`Invoke`, and cleared for work that is not a task
(`Post`, `AsyncAfter`, `SerialQueue`, `GlobalQueue`). Stored tasks are `TaskHandle`; placement follows R-TASK-1;
priorities and QoS are dropped; a non-throwing task's escaping exception ends the process. *Why:* all three drafts
threaded explicit `CancellationToken` parameters, which add a parameter no Swift signature has, cannot reach the
synchronous helpers that test `Task.isCancelled`, and lose cancellation silently when a call site passes the wrong
token or none. *From:* new in this judgement; B's `TaskHandle` and placement table kept. *Supersedes:* A R-3.8 and
ED-072 (token forms), B ED-062 (token forms), C R-TASK-1 to R-TASK-3 (token forms). *Evidence:* `SwiftTaskTests`
(16), `ActorTests.An_actor_call_stays_in_the_callers_task_and_sees_its_cancellation`. *Action:* adoption.

**ED-067 Actors are strands.** A Swift `actor` derives from `Actor`: one synchronous segment at a time on a strand
over the pool, released at every await, a same-actor call inline, and never a lock or semaphore across an await.
*Why:* Swift actors are re-entrant, and `OAuthRefreshGate` and `ACPLaunchGate` depend on it. *From:* B ED-060.
*Not adopted:* A's `SwiftActor` on an exclusive scheduler (a same-actor call queues instead of running inline);
C's lock around each synchronous segment (the porter must re-cut the Swift body by hand). *Evidence:* `ActorTests`
(5). *Action:* adoption.

**ED-068 One clock, and Swift's sleep.** Every stamp, timer and wait reads `SwiftClock.Provider` (the BCL
`TimeProvider`); `try? await Task.sleep` is `TrySleep` (never throws, reports cancellation), `try await` is `Sleep`;
zero or negative suspends once (O-21); no 49.7-day cap; `SwiftTask.Seconds` traps on NaN or infinity and saturates a
huge interval (O-22). *Why:* 186 sleeps and seven timing shapes must be testable to the millisecond, and
`Task.Delay` differs on cancellation, negatives, -1 ms and long waits. *From:* B ED-061 and ED-063, C ED-062, A
ED-068 and ED-072. *Not adopted:* B's "NaN is zero" (Swift is believed to trap on a NaN duration, O-22). *Evidence:* `SwiftTaskTests`
sleep tests, `TimingShapeTests`. *Action:* adoption.

**ED-069 Streams, continuations, locks and queues.** `AsyncStream<T>` with Swift's storage semantics (buffering,
dropped yields after finish, quiet cancellation, buffered items drained first, `onTermination` once and before the
consumer wakes); `CheckedContinuation<T>` (asynchronous resumption, a second resume traps); `Mutex<T>`
(non-re-entrant, traps on re-entry); `SerialQueue` (a strand, FIFO, `Sync` as a barrier, traps on itself);
`GlobalQueue`. *Why:* `Channel<T>` throws on cancellation, a plain `TaskCompletionSource` resumes inside the
resumer's lock, .NET locks are re-entrant, `Task.Run` per item loses FIFO. *From:* B ED-064, ED-065, ED-066; A ED-073
and ED-074; C ED-071. *Not adopted:* C's "a second resume is a no-op" (Swift traps). *Evidence:* `AsyncStreamTests`
(7), `ContinuationTests` (5), `SerialQueueTests` (5). *Action:* adoption.

**ED-070 Observation.** `@Observable` is `ObservableModel` with per-access tracking across objects (`Read`, `Set`,
`SetAlways`, `Computed`, `WithTracking`); `didSet` runs on every assignment and never in `init`; an equal value does
not notify (UNVERIFIED O-05, no rendered difference); `@MainActor @Observable` is `MainActorModel`. No MVVM package.
*Why:* only per-access tracking reproduces the source's narrow dependencies (`ObservedValue` cells, the render
budget); CommunityToolkit.Mvvm and ReactiveUI cannot follow a computed property into another object and add a
package. *From:* C ED-059 (tracking), B ED-067 (`SetAlways`, isolation), A ED-066 (the equal-value policy in one
place). *Evidence:* `ObservationTests` (8), `ViewSupportTests`, `HeadlessTests`. *Action:* adoption.

**ED-071 SwiftUI views.** One code-first Avalonia control per SwiftUI view, in the twin file, with no AXAML and no
invented view model; the view mechanics live in `UI/Port` (`Tracked`, `OnChange`, `ViewTask`, `Binding`,
`Transaction`, `ForEach`, `EnvironmentValues`, the `Presentation` presenter seam); `@State` read by `body` lives in a
per-control `MainActorModel`; one point is one DIP. *Why:* one Swift file stays one C# file with per-modifier
citations; identity and state stay where SwiftUI keeps them; headless tests construct controls directly. *From:* C
ED-058 and ED-072, A R-4.5. *Evidence:* `ViewSupportTests` (5), `HeadlessTests` (6). *Action:* adoption; confirm "one
point is one DIP" alongside PA-0013 and ED-059.

**ED-072 JSON.** Swift's bytes through `SwiftJson` with one named style per Swift writer; decoding through reader
functions with JSONDecoder's strictness and the source's own leniency; SE-0295 enum shapes; reads at 512 levels
(O-24); sorted keys stay ordinal (O-01) and storage dates stay lenient (O-02) until the oracle answers, each a
one-line switch. *Why:* the current `Coding.Encode` differs from Swift for emoji, U+2028, U+FEFF and Double text
(proven); the default System.Text.Json binder keeps defaults where Swift throws (proven). *From:* A ED-059 and
ED-062, B ED-071 and ED-072, C ED-064 (its names). *Not adopted:* A ED-060 and ED-061 (changing key order and date
strictness before the oracle); C's container API (`SwiftDecoder`, `KeyedDecodingContainer`), which adds an object
model no requirement observes. *Supersedes:* `Coding.Encode`'s relaxed encoder. *Evidence:* `SwiftJsonTests` (11),
`SwiftDecodingTests` (9). *Action:* the Core repair packet (4.4).

**ED-073 Swift value semantics for text, numbers, dates and UUIDs.** `SwiftString` (Characters, canonical equality
and hashing, scalar order, Character-boundary prefix and suffix on the ASCII path too), per-scalar ICU root case
mapping and Unicode properties from `System32\icu.dll` by P/Invoke (no package; Windows' ICU version is a named,
bounded deviation), `SwiftNumber` (Swift `description`, exact `Fixed`, Swift parsing), `SwiftDate` (UTC
`DateTimeOffset` with Swift's Double arithmetic and exact conversion), `SwiftUUID` (uppercase, strict). Restates
ED-009 part 1 for C# and corrects its JavaScript claim (Swift applies no Final_Sigma context), restates ED-009 part
2 (R-STR-6) and ED-034 (R-FMT-2). *From:* A ED-065, ED-068, ED-069; B ED-070 and ED-071; C ED-067. *Not adopted:* A's
ASCII `HasPrefix` fast path without the CRLF check; B's and C's whole-string ICU mapping (which applies the context
rule Swift does not); the 2^53 (A) and 1e16 (B) exponent thresholds: none of the three is verified, and 2^54 is kept only as the
believed SwiftDtoa test (`fabs(d) > 0x1.0p54`), one constant to change when O-03 is settled.
*Evidence:* `SwiftStringTests` (16), `SwiftNumberTests` (14), `SwiftDateTests` (8), `SwiftUUIDTests` (4).
*Action:* re-word ED-009 and ED-034 formally.

**ED-074 Checked arithmetic.** `<CheckForOverflowUnderflow>true</CheckForOverflowUnderflow>` in
`Directory.Build.props`; Swift's `&+`, `&-`, `&*` are `unchecked(...)`. *Why:* Swift traps on overflow and narrowing;
a C# port that wraps continues in a state the source cannot reach. *From:* A ED-067, B ED-073, C ED-068. *Evidence:*
`Int_conversion_and_arithmetic_trap_like_Swift`; the trial on a copy of the port: all 251 existing tests pass.
*Action:* edit the protected props file.

**ED-075 Enums.** `[RawValue]` plus `SwiftEnum<T>` (declaration order, exact raw spelling compared as Swift strings
compare, no numeric values); `NoWarn` CS8524 and no discard arm on enum switches, so a missing case is CS8509; record
unions end in `UnreachableException` and have a `UnionCoverage` test. *Why:* `Enum.TryParse` accepts numbers, other
cases and flags, `Enum.GetValues` sorts by value, and C# cannot check a union switch. *From:* B ED-069, C ED-065, A
ED-063. *Supersedes:* the `Id` switch and discard arms of `MCPCategoryExtensions`. *Evidence:* `SwiftEnumTests` (7);
CS8509 in `negative-checks.log`. *Action:* edit the props file; Core repair.

**ED-076 Analyzers and the source scan.** `AnalysisLevel` latest with CA1304, CA1305, CA1307, CA1309, CA1310 and
CA1311 as errors in `.editorconfig`, plus the source-scan test of 5.2 with its allowlist in
`porting/source_scan_allow.json`. `Microsoft.CodeAnalysis.BannedApiAnalyzers` is not adopted: it is a package; the
SDK analyzers and the scan cover the list; revisit if the scan leaks. *Why:* the culture and comparison defaults are
the rules most often broken by habit, and the compiler can hold them. *From:* C ED-074, A ED-077, B ED-061 and 38.6.
*Evidence:* planted CA1304, CA1305, CA1310, CA1311 errors in `negative-checks.log`; on the port copy, 8 errors, all in tests (`port-settings-check.log`).
*Action:* edit props and `.editorconfig`; create the allowlist; build the scan (4.3).

**ED-077 Value semantics.** Models are immutable sealed records; collections in them are `ValueList`, `ValueMap`,
`ValueSet` (public in `Core/Port`); `inout` closures are `Func<T, T>`; a NaN-sensitive equality is written by hand.
*Why:* the alias bug in the `updateThread` guard and reference equality of collections in records (both proven).
*From:* A ED-064, C ED-066. *Supersedes:* `MCPValues.List`/`Map`. *Evidence:* `ValueCollectionTests` (6),
`ObservationTests.A_mutable_class_alias_makes_the_same_code_skip_the_save`. *Action:* Core repair.

**ED-078 Errors and traps.** `SwiftError`, `ILocalizedError`, `ISwiftErrorIdentity`; `SwiftTrapException` and the
trap-like .NET exceptions are never caught by a ported `try?` or catch-all (`SwiftTry.IsThrownError`); an `NSError`
text shown to a user is a per-site decision; a non-throwing task's exception escapes. *Why:* 559 `try?` and 116
`catch`; a bare C# catch swallows traps (proven). *From:* A ED-070, C ED-068 (traps). *Not adopted:* B's broad
`try?` (catches traps). *Evidence:* `SwiftErrorTests` (6),
`A_throwing_task_delivers_its_error_and_a_trap_in_a_task_escapes_to_the_main_loop`. *Action:* adoption.

**ED-079 Settings.** `UserDefaults.standard` is `UserDefaults.Standard` (`Ambient<ISettingsStore>`): unbound, it
traps; `Plist` reproduces the `as?` bridging and the typed getters; `string(forKey:)` of a Double traps until O-10.
The production store's file format stays open. *Why:* absent versus `false`, key names and bridging are behaviour;
silently losing settings is not. *From:* C (seam; its ED-077 proposes a JSON file for production), B section 29, A
section 7.7. *Not adopted:* C's in-memory default store (a missing binding would lose every setting silently).
*Evidence:* `UserDefaultsTests` (5). *Action:* decide the production format.

**ED-080 The deterministic test runtime and `tests/Marrow.Testing`.** `ManualMainLoop`, `ManualTimeProvider`,
`TestRuntime`, `InMemorySettingsStore`, `UnionCoverage`, `ChangeLog` and `Traceability` in a class library (not a
test project) referenced by every test project. *Why:* four test projects need the same runtime; linked files are
the alternative. `Microsoft.Extensions.TimeProvider.Testing` is not used: a package, and it does not pump main
between timers. *From:* B ED-068, C ED-070. *Action:* add the project.

**ED-081 Traceability.** The traits of R-TEST-1 with checked id formats, the three meta-tests in every test project,
the layout rules `req_verify.mjs` needs, and `porting/tools/cite_check.mjs` (from proto-C) for B2 citations, run in
the same gate as `req_verify.mjs`. *Why:* Gate D reads test source line by line; a comment id is not counted and a
stray `class` word credits a requirement to the wrong class (shown in `negative-checks.log`). *From:* C ED-069, B
38.2, A R-10.2 (its comment form not adopted). *Evidence:* `TraceabilityTests` (3 per project). *Action:* add the
tool (protected path).

**ED-082 Headless UI tests.** `HeadlessUnitTestSession` per test assembly in a collection fixture, on xunit 2.9.3;
`Avalonia.Headless.XUnit` is not used (its 12.1.2 package depends on `xunit.v3.extensibility.core` 3.2.2). *From:*
C ED-070 and its section 19.8. *Evidence:* `HeadlessTests` (6). *Action:* none (`Avalonia.Headless` is already
pinned).

**ED-083 Frame clock and timer resolution.** Frame-driven work (`TimelineView(.animation)`, `CADisplayLink`, sleeps
standing for a frame tick) runs from `TopLevel.RequestAnimationFrame` behind a test seam; no `timeBeginPeriod`, no
`DispatcherTimer`; the quantization of other short sleeps to the 15.6 ms tick is a recorded platform difference.
*From:* B ED-078, A R-3.9. *Evidence:* not prototyped (unit in 4.3). *Action:* none.

**ED-084 Reduce motion.** `\.accessibilityReduceMotion` follows Windows' animation-effects setting
(`SPI_GETCLIENTAREAANIMATION`, inverted), re-read on `WM_SETTINGCHANGE`, as an inherited environment property.
Owner-visible only in that animations obey the platform's own switch. *From:* B ED-080, C ED-075, A R-4.5.
*Action:* also record it as a platform adaptation.

**ED-085 Port-only code in `Port/`.** Code without a Swift twin lives in `src/Marrow.<Layer>/Port`, namespace
`Marrow.<Layer>.Port`, header `PORT-ONLY`; every other file has a twin; a twin keeps its Swift-facing API
(`Coding.Value`) over port-only machinery; `FoundationText` moves to `Core/Port` and becomes public. *From:* C ED-076
and its `Port` namespace. *Action:* Core repair.

**ED-086 The Swift oracle and the UNVERIFIED policy.** Items O-01 to O-24 (Appendix B) are settled only by running
Swift. Under OD-0001 (no macOS), the swift.org Windows toolchain (Swift 6.2), installed outside the port, settles
the standard-library and swift-foundation items by one-file programs whose output is checked in as fixtures; the
items that come from swift-corelibs-foundation on Windows (JSONSerialization, NSString, `ByteCountFormatter`,
`NumberFormatter`, `UserDefaults`, `Scanner`) stay UNVERIFIED; whether swift-foundation answers transfer to macOS
26 is itself to be confirmed first. Until then each parked choice is one constant or one line, and every test that
rests on one carries its `Oracle` trait (R-TEST-6). *From:* B ED-079, survey 31, A section 11. *Action:* the tool
install is the orchestrator's decision.

### 7.1 Open questions for the orchestrator

1. **Numbering.** ED-064 to ED-086 assume nothing is recorded after ED-063. If a decision is recorded first,
   renumber these in one pass; foundation-proto cites them in comments and `Decision` traits.
2. **Project settings (ED-074, ED-075, ED-076).** Edit `Directory.Build.props` and `.editorconfig`, and apply the 8
   test fixes of 4.2.
3. **`tests/Marrow.Testing` (ED-080)**, or linked files instead.
4. **The Swift toolchain (ED-086).** Install or not; without it the O items stay UNVERIFIED and their parked
   choices ship.
5. **`Calendar.current` (R-FMT-2).** The user's Windows calendar (`CultureInfo.CurrentCulture.Calendar` with the
   local zone, draft A's ED-076) or Gregorian. It changes persisted day keys (`TokenLedger.swift:192-195`) for users
   of a non-Gregorian calendar, so it may be the owner's call. Sites stop until it is decided.
6. **The production settings store format (ED-079).** Draft C proposes a JSON file of typed property-list values
   written through a `SerialQueue`.
7. **The Core repair packet (4.4)** and the formal re-wording of ED-009 and ED-034 (ED-073).
8. **ED-054(3)** points at `LEGACY_APP_NAMES` in the Electron Marrow tree; that mechanism is not part of this port,
   so nothing is built for it. ED-054(1), (2) and (4) stand.
9. **Stale B1 references.** HANDOFF.md and WORKER_BRIEF.md still name `E:\Droppy\reference\droppy-code` (1.6.0);
   implementation is from B2 only.
10. **The ICU version.** Case mapping, Character properties and future formatters use Windows' `icu.dll`, not
    Apple's ICU: record it as a named deviation.
11. **Parked UNVERIFIED choices.** Accept the parked defaults of Appendix B (ordinal keys, lenient storage dates,
    2^54, no notify on equal values, the CRLF prefix rule and the rest) until the oracle answers.
12. **Owner-visible network defaults.** The `User-Agent` a server sees and cookie persistence (R-NET-1).
13. **`NSError` texts** at `MCPStore.swift:352`, `UsageViews.swift:301` and `ACPSession.swift:616` need per-site
    decisions (R-ERR-2).
14. **`integer(forKey:)` and `double(forKey:)`** conversions, if a unit needs them (R-SET-1).

---

## Appendix A. How the drafts were judged

Each draft was scored per construct on five criteria, 1 (unsafe) to 5 (best available), in this order:
**P** exact-parity safety, **I** internal consistency, **T** testability (and whether its prototype proved it),
**S** simplicity for a porter, **X** compatibility with the existing code and recorded decisions. A dash means the
draft has no rule for it. The chosen column names what this guide adopted; section 7 has the matching decision.

| Construct | A (P I T S X) | B (P I T S X) | C (P I T S X) | Chosen, and why |
|---|---|---|---|---|
| Files, names, citations | 4 4 3 4 4 | 4 4 4 4 4 | 5 5 5 4 5 | C: `Port/` for port-only code, traits, `cite_check.mjs`; A's escape rule, strengthened (R-STR-9) |
| Main actor | 4 4 3 4 4 | 5 5 5 4 5 | 5 4 5 4 5 | B's operations and proven binding, with C's `Ambient<T>` seam |
| Task handles, cancellation | 3 4 4 3 4 | 3 4 5 3 4 | 3 3 4 3 4 | none as written: all three thread explicit tokens; the ambient model (ED-066) keeps B's `TaskHandle` and placement table |
| Sleep | 4 4 5 4 4 | 3 5 5 5 4 | 4 4 5 4 4 | B's `TrySleep`/`Sleep` and clock seam; A's and C's `Seconds`; B's NaN-to-zero dropped |
| Task groups, `async let` | 4 4 3 4 4 | 4 4 4 4 4 | 3 3 2 3 4 | B's rules, as helper types (`TaskGroup`, `AsyncLetScope`) instead of per-site `WhenAll` code |
| Actors | 4 4 4 4 4 | 5 5 5 4 5 | 3 3 4 2 4 | B: a strand runs a same-actor call inline (A queues it) and keeps the Swift body intact (C re-cuts it around a lock) |
| `AsyncStream` | 4 4 4 4 4 | 5 5 5 4 5 | 4 4 4 4 4 | B: Swift's storage semantics, `onTermination` before the consumer wakes |
| Continuations | 3 4 4 4 3 | 5 5 5 4 5 | 3 4 4 4 4 | B: a second resume traps (C makes it a no-op); `OnceContinuation` is a Swift type, not foundation (A) |
| Locks, queues, GCD, threads | 5 4 5 4 4 | 5 5 5 4 5 | 4 4 4 4 4 | B: strands over the pool, `GlobalQueue`, the `Sync` barrier (C spends a thread per queue) |
| Deterministic time, timing shapes | 4 4 4 3 4 | 5 5 5 4 5 | 4 4 4 4 4 | B: `TestRuntime` pumps main after every timer; the timing-shape catalogue |
| Observation | 3 4 3 4 4 | 4 5 4 4 5 | 5 4 5 4 5 | C's per-access tracking across objects, with B's `SetAlways` and isolation check |
| SwiftUI views | 3 3 2 3 4 | 4 4 3 4 4 | 5 5 5 4 5 | C: code-first controls, lifetimes, identity, the presenter seam, proven headless |
| JSON writing | 5 5 5 4 4 | 3 4 3 4 5 | 2 4 4 4 5 | A's Swift byte writer; C's `SwiftCodec.Encode` keeps `Coding.cs` escaping, which differs from Swift |
| JSON reading | 5 5 5 4 5 | 4 4 4 4 5 | 5 4 5 3 4 | A's reader functions with C's Swift-shaped names; C's container object model not adopted |
| Sorted keys, storage dates | 3 4 4 4 3 | 4 4 4 4 5 | 4 4 4 4 5 | B and C: both parked as one-line switches; A changes both before the oracle |
| Raw-value enums | 4 4 4 3 4 | 4 5 4 4 4 | 5 5 5 5 4 | C's `[RawValue]` and `SwiftEnum<T>`, with B's CS8524 setting |
| Unions, exhaustiveness | 3 4 3 4 4 | 5 5 5 4 4 | 4 4 4 4 4 | B: `UnionCoverage` makes the missing check mechanical |
| Value semantics | 5 5 5 4 4 | 4 4 4 4 4 | 5 5 5 4 4 | A and C (the same design): value collections, records, `Func<T, T>` |
| Strings | 4 5 5 4 4 | 5 4 4 4 4 | 4 4 4 4 4 | A's `SwiftString`, with B's CRLF rule on the ASCII prefix path |
| Case mapping, properties | 5 5 5 4 4 | 3 4 4 4 4 | 3 4 4 4 4 | A: per-scalar ICU mapping; B and C map the whole string, which applies a context rule Swift does not |
| Regular expressions | 4 4 3 4 4 | 4 4 3 4 4 | 3 3 2 4 4 | A and B: pinned options, `\z`, grapheme post-checks, O-13 |
| Numbers | 4 5 5 4 4 | 4 4 5 4 4 | 5 4 5 4 4 | A's `SwiftNumber` with C's exact `Fixed`; the exponent threshold parked (O-03) |
| Formatters, locale, calendar | 3 4 3 3 3 | 4 4 3 4 4 | 4 4 3 4 4 | B and C; A decides `Calendar.current` although it changes persisted keys (left open) |
| UUIDs, dates, clocks | 5 5 5 4 4 | 4 4 4 4 5 | 4 4 4 4 5 | A: exact Double-to-tick conversion, uppercase UUID text, strict parsing |
| Errors, traps | 5 5 5 4 4 | 3 4 4 5 4 | 4 4 4 4 4 | A: `IsThrownError` keeps traps out of `try?` (B's `try?` catches them) |
| Settings | 4 4 3 4 4 | 4 4 4 4 4 | 3 4 5 4 4 | C's seam, with a trapping default instead of its in-memory store |
| Processes, files, network, secrets | 3 3 2 4 4 | 5 5 3 4 5 | 4 4 4 4 4 | B: the order-is-the-contract templates on ADR-0002's `CreateProcessW`; C's real-folder testing |
| Lifetime, collections | 4 4 4 4 4 | 5 5 4 4 5 | 4 4 4 4 4 | B: explicit ends of life, `Lazy<T>` statics, stable sorts |
| Tests, traceability | 3 4 4 4 3 | 4 4 4 4 4 | 5 5 5 4 5 | C's traits and meta-tests (A's comment ids are invisible to Gate D), plus this judgement's source-layout check |
| Project settings | 5 4 4 5 4 | 5 5 5 5 4 | 5 5 5 5 4 | all three agree; tried here on a copy of the port (8 analyzer errors, all in tests; 251 tests green under checked arithmetic) |
| Headless UI tests | - | 4 4 4 4 4 | 5 5 5 4 5 | C: a session fixture on xunit 2.9.3 |

**Rules not adopted, and why.** None of these was adopted as written; the ones marked parked stay one line away
from adoption once the oracle answers.

| Rule (draft) | Why not |
|---|---|
| Explicit `CancellationToken` parameters (A, B, C) | a parameter no Swift signature has; cannot reach synchronous `Task.isCancelled` checks; a wrong token loses cancellation silently (ED-066) |
| Foundation key order now (A ED-060) | UNVERIFIED; would change existing bytes before the oracle; parked (O-01) |
| Strict storage dates now (A ED-061) | UNVERIFIED; a strict port would drop records Swift may keep; parked (O-02) |
| Broad `try?` (B) | catches traps, so the port continues where Swift crashes (R-ERR-1) |
| NaN and negative durations as zero (B ED-063) | Swift is believed to trap on NaN (O-22); a negative sleep suspends (O-21) |
| Whole-string ICU case mapping (B ED-070, C ED-067) | applies Final_Sigma, which Swift's per-scalar mapping does not (O-07) |
| ASCII `HasPrefix` without the CRLF check (A) | `"\r\n"` is one Character on the ASCII path too (O-06) |
| In-memory default settings store (C) | an unbound store would lose settings silently; the port traps instead |
| `SwiftCodec.Encode` over the `Coding.cs` escaping (C) | differs from Swift for emoji, U+2028 and U+FEFF (proven) |
| Exponent thresholds 2^53 (A) and 1e16 (B) | no threshold is verified; 2^54 is parked as the believed one (O-03) |
| `Microsoft.CodeAnalysis.BannedApiAnalyzers` (B, optional) | a package; the SDK analyzers plus the source scan cover the list; flagged for the orchestrator (ED-076) |
| `SwiftActor` on an exclusive scheduler (A ED-071) | a same-actor call queues behind other work instead of running inline |
| A lock around each synchronous actor segment (C R-ACTOR-1) | the porter must re-cut every Swift body by hand; easy to get wrong |
| A second continuation resume as a no-op (C R-TASK-6) | Swift traps on it |
| `Calendar.current` decided as the Windows user calendar (A ED-076) | changes persisted day keys; an owner or orchestrator call (7.1) |
| Codable container object model (C ED-064) | machinery no requirement observes; reader functions give the same strictness |
| Requirement ids in comments (A R-10.2) | `req_verify.mjs` does not count them, which would starve Gate D |
| Four-digit escapes in source (A R-10.8) | agent file tools were seen decoding them into raw characters |
| `OnceContinuation` in the foundation (A ED-058) | a Swift type (`Shell.swift:218-243`); its twin belongs in `Support/Shell.cs` |
| A dedicated thread per serial queue (C ED-071) | the same FIFO semantics on a strand, without a thread per queue |
| `Avalonia.Headless.XUnit`, `Microsoft.Extensions.TimeProvider.Testing`, CommunityToolkit.Mvvm, ReactiveUI | already rejected by the drafts themselves, confirmed here: packages, and respectively xunit v3, no main-loop pumping, no cross-object tracking |

No draft rule was found that weakens a gate other than A's comment-form requirement ids (above). No draft adds a
package without flagging it.

---

## Appendix B. The oracle list: Swift behaviour nobody could settle

Every item is UNVERIFIED. The parked choice is what the port does until the oracle answers; changing it is the one
place named. "Windows toolchain" says whether the swift.org Windows toolchain can settle it (survey 31: the
standard library and swift-foundation yes, swift-corelibs-foundation no; whether swift-foundation answers transfer
to macOS 26 is itself to be confirmed).

| Id | Question | Where it matters | Parked choice (where it lives) | Test | Windows toolchain |
|---|---|---|---|---|---|
| O-01 | `.sortedKeys` order for keys with digits, case or non-ASCII | user dictionaries in storage (environment, headers) | ordinal (`SwiftJson.SortedKeyOrder`) | `Sorted_keys_stay_ordinal_until_the_oracle_...` | yes |
| O-02 | does `.iso8601` decoding reject fractional seconds | loading stored dates | accepted (`FoundationISO8601.StorageDate`) | `Storage_decoding_keeps_the_existing_lenient_rule_...` | yes |
| O-03 | `Double.description` switch to exponent form: 2^53, 1e16 or 2^54 | interpolated Doubles, `JSONValue.displayText` | above 2^54 (`SwiftNumber.ExponentialAbove`) | `Description_switches_to_exponent_form_above_two_to_the_54` | yes |
| O-04 | pretty-printer empty containers and spacing | `MCPStore.swift:262-275`, `JSONValue.prettyString` | `"key" : value`; an empty container prints its open bracket, an empty line, and its close bracket at the member's indent (`SwiftJson` writer) | `Pretty_layout_is_Apples_...` | encoder yes; JSONSerialization no |
| O-05 | does an equal assignment notify observers | render counts only | no notification (`ObservableModel.Set`) | `DidSet_runs_for_an_equal_value_but_bindings_are_not_told` | yes |
| O-06 | ASCII `hasPrefix` across a CRLF boundary | Windows-origin text | Character boundary kept (`SwiftString.HasPrefix`) | `A_prefix_or_suffix_that_splits_CRLF_...` | yes |
| O-07 | per-scalar lowercasing; `caseInsensitiveCompare` folding | search, sorting, matching | per-scalar ICU root; default folding (`SwiftString`) | `Lowercase_maps_each_scalar_alone_...` | lowercasing yes; folding no |
| O-08 | `localizedCaseInsensitiveContains("")` | pickers (all 13 sites also guard) | false (`SwiftString.LocalizedCaseInsensitiveContains`) | `LocalizedCaseInsensitiveContains_rejects_an_empty_needle_...` | no |
| O-09 | duplicate keys, big integers and number text in JSONSerialization and JSONDecoder | MCP and CLI JSON | last key wins; lexical int or Double (`SwiftJSONNumber.IsInt`, `ValueMap`); an overflowing literal (`1e400`, a 400-digit integer) parses to +/-infinity in the port (`JsonElement.GetDouble`), which Darwin may reject (ED-146) | `A_duplicated_key_keeps_its_last_value_once`, `JSONSerialization_numbers_classify_by_lexical_form` | decoder yes; serialization no |
| O-10 | `string(forKey:)` of a stored Double | settings read as text | traps (`Plist.String`) | `String_for_key_renders_integers_and_refuses_to_guess_a_Double` | no |
| O-11 | buffered `AsyncStream` elements after the consumer is cancelled | stream shutdown | drained first (`AsyncStream`) | `Buffered_elements_drain_after_cancellation` | yes |
| O-12 | control-character escapes in JSON | any control character in stored text | short forms, lowercase hex (`SwiftJson` writer) | `Controls_use_short_forms_and_lowercase_hex` | encoder yes; serialization no |
| O-13 | Swift `Regex` `$` and `\r?\n` against the Character `\r\n`; `components(separatedBy: "\n")` on CRLF; `.backwards` | Hydra fences, CLI output parsing | `\z`; UTF-16 split; last match (R-RE-1, R-STR-3) | `Dotnet_dollar_matches_before_a_final_newline_...` (C# side only) | Regex yes; NSString no |
| O-14 | `localizedDescription` of a Swift error without a description | error text shown to users | `The operation couldn` U+2019 `t be completed. (Domain error N.)` (`SwiftErrors`) | `LocalizedDescription_gives_the_description_or_...` | no |
| O-15 | a UTF-8 BOM in `String(data:encoding:)` and in JSON input | files written by other tools | BOM kept; JSON reading unhandled (`SwiftText`) | `UTF8_decoding_has_a_failing_and_a_repairing_form` | partly |
| O-16 | `scanHexInt64` overflow and a bare `0x` | theme colours | saturate; a bare `0x` fails (`SwiftScanner`) | none (the prefix and failure cases: `ScanHexInt64_reads_a_prefix_...`) | no |
| O-17 | `ISO8601FormatStyle` parsing offsets such as `+03:00` | CLI timestamps (nine sites) | `Z` or `+HH:MM` accepted (`FoundationISO8601`) | none for offsets | yes |
| O-18 | `ForEach` with duplicate ids | none known | the first kept (`ForEach`) | none | no (SwiftUI) |
| O-19 | `addObserver(queue: .main)` for a post on main: inline or later | none known (no post off main) | synchronous event | none | no |
| O-20 | the executor an `async let` child starts on | the 9 `async let` sites | each callee keeps its own isolation (`AsyncLetScope`) | `Async_let_children_run_concurrently_...` | yes |
| O-21 | `Task.sleep` of zero or a negative duration | `AutoContinue.swift:105` and computed delays | suspends once (`SwiftTask.Sleep`, `TrySleep`) | `A_negative_or_zero_sleep_suspends_once_...` | yes |
| O-22 | `Duration.seconds` of NaN, infinity or a huge Double | computed delays | traps; huge saturates (`SwiftTask.Seconds`) | `Seconds_traps_on_a_non_finite_interval_...` | yes |
| O-23 | ICU formatter outputs (U+202F before AM/PM, named relative dates, spell-out, currency, `ByteCountFormatter` wording) | dates, sizes and credits in the UI | U+202F before a in the en family (ED-135); `ByteCountFormatter` words English in every locale, number per locale (ED-133); the rest is CLDR/ICU output as `SwiftFormattersTests` states | `SwiftFormattersTests` | FormatStyle yes; ByteCountFormatter no |
| O-24 | JSON nesting limit (512) in JSONDecoder and JSONSerialization | deep MCP schemas and values | 512 (`SwiftJson.DocumentOptions`) | `Nesting_past_64_levels_needs_the_document_options` | decoder yes; serialization no |
| O-25 | Foundation’s English text for `CocoaError` 256 and 259 with no file name | `RevertPreview.swift:33`, `:36`, `WorkingTreeWatch.swift:333` | "The file couldn’t be opened." and "The file couldn’t be opened because it isn’t in the correct format." (ED-100) | `CocoaErrorTests` | no (a macOS run) |
| O-26 | `URLSession.AsyncBytes.lines` (AsyncLineSequence): which bytes end a line, and whether an empty line is yielded | `OpenAICompatibleSession.swift:636-637` (the error body is built as line + "\n", so empty lines change the text), `:742-744` (the SSE drain) | LF, CR, CRLF (one line, also when split across chunks), NEL, LS and PS each end a line; no empty line is yielded (`Http.cs`, `AsyncBytes.Lines`) | `Empty_lines_between_events_are_not_yielded`, `A_carriage_return_line_feed_pair_and_a_lone_carriage_return_each_end_one_line`, `A_next_line_line_separator_or_paragraph_separator_also_ends_a_line` | no (a macOS run) |
| O-27 | the request headers URLSession adds | `LinkTitles.swift:47`, `MarkdownView.swift:946-949`, `MCPProxy.swift:317-342` | Accept `*/*` when none is set; Accept-Language `en-US,en;q=0.9` (the app ships English only; the region subtag is part of the question); Accept-Encoding `gzip, deflate, br` with the body decoded; Content-Type `application/x-www-form-urlencoded` for a body sent without one (`Http.cs`, ED-118) | `A_request_without_Accept_goes_out_with_the_headers_URLSession_adds`, `A_body_sent_without_a_Content_Type_goes_out_as_a_form` | no (a macOS run) |
| O-28 | cookie storage | every `URLSession.shared` site; `UpdateChecker.swift:101-110` (ephemeral) | one process-wide RFC 6265 store for `URLSession.Shared`, Set-Cookie stored and sent back to matching requests; each ephemeral session has its own memory store for its life; memory only until the owner answers the persistence question (`Http.cs`, ED-118) | `A_cookie_set_by_a_server_goes_back_to_it_on_the_next_request_of_the_shared_session`, `An_ephemeral_session_keeps_its_cookies_to_itself` | no (a macOS run) |
| O-29 | which request headers survive a redirect | `PlanLimits.swift:243-246`, `CommandCodeAPI.swift:169-171`, `MCPProxy.swift:326-339` | same origin: every field kept, Authorization included (believed to be CFNetwork's behaviour); another origin (scheme, host and port per RFC 6454): Authorization dropped, the other fields kept. That half is a safety default, not a reading of CFNetwork: if the oracle shows Apple keeps Authorization across origins, the change goes to the owner, not straight into the constant (`Http.cs`) | `A_same_origin_redirect_keeps_Authorization_Accept_and_custom_headers`, the cross-origin test (a second loopback server on another port) | no (a macOS run) |
| O-30 | a repeated response header | `MCPOAuth.swift:259-280` | one `allHeaderFields` entry holding the values joined with ", " in arrival order; `value(forHTTPHeaderField:)` returns the same string (`Http.cs`) | `Two_challenge_header_lines_become_one_field_with_both_values_joined_by_a_comma_and_space` | no (a macOS run) |
| O-31 | which idle limit applies when a request keeps its default 60 s on a session that sets `timeoutIntervalForRequest` | `UpdateChecker.swift:105`, `:190`, `:223` | the configuration's value (15 s) (`Http.cs`) | `A_request_made_from_a_URL_alone_on_the_update_checker_session_idles_out_after_the_configuration_s_15_seconds` | no (a macOS run) |
| O-32 | the error a pending `AsyncBytes` read throws when its task is cancelled; it decides between `coalescer.finish()` and `coalescer.fail(chunkError:)` on a stop | `OpenAICompatibleSession.swift:652-659`, `:784-788` | `URLError(.cancelled)`, as R-NET-1 and R-TASK-2 say for `data(for:)` (`Http.cs`) | `Cancelling_the_task_while_a_stream_read_is_pending_fails_that_read_with_URLError_cancelled` | no (a macOS run) |
| O-33 | whether `URLCache.shared` answers a `URLSession.shared` GET | `PlanLimits.swift:247`, `LinkTitles.swift:47`, `MarkdownView.swift:949` | in process, a stored answer still fresh is returned without a request: GET, status 200-299, from the server, explicit freshness (max-age or Expires), not no-store; age measured on `SwiftClock`; no heuristic freshness and no size cut-off; memory only until the owner answers the persistence question (`Http.cs`, ED-118) | `A_cacheable_answer_to_a_GET_on_the_shared_session_is_served_again_from_the_cache` | no (a macOS run) |
| O-34 | `TimelineView(.animation(minimumInterval:paused:))` and `CADisplayLink.preferredFrameRateRange` cadence against the display rate (including whether `CAFrameRateRange.minimum` binds); the date a TimelineView context carries at a frame and at a redraw caused by the parent; the first display-link tick; whether a paused change redraws | the six `TimelineView(.animation)` sites (`HydraProgressBar.swift:142`, `:507`, `TimelineRows.swift:499`, `EffortSlider.swift:1050`, `AboutUpdateViews.swift:364`, `:450`), `StreamVeil.swift:174-178`, the frame-tick sleeps (`ThreadTimeline.swift:1168`, `:1211`, `:1223`, `HydraFusedPairIcon.swift:40`) | measured from the last draw, complete 1 ms early at most; SwiftDate.Now at the frame; the last draw's date on Redraw; first tick at the next frame; Minimum and Maximum not read; no draw on a schedule change (`FrameClock`, ED-117) | the F.FrameClock cadence tests | no (SwiftUI, Core Animation) |
| O-35 | `FileManager.replaceItemAt(_:withItemAt:)` when the original item does not exist: throw, or move the new item into place | `CommandCodeSession.swift:758` (every first reply) | throws file-not-found, nothing moved, no retry (`FileOps.ReplaceItemAt`, ED-123) | `FileOpsTests.Replacing_a_missing_original_...` | no (swift-corelibs-foundation; a macOS run) |
| O-36 | whether an `.atomic` write over an existing read-only file succeeds and leaves the result read-only (swift-foundation `writeToFileAux` copies the destination's mode to the temporary file before the rename) | `NativeFileTools.swift:88`, `:118` (an agent's edit to a read-only project file) | the write succeeds and the result is read-only; a new file, or one that was not read-only, is not made read-only (`FileOps.WriteAtomic`, ED-124) | `An_atomic_write_over_a_read_only_file_...` and its retries-exhausted variant | no (the Windows branch differs; a macOS run) |
| O-37 | `Locale.current` of an English-only app: the user's region with its language replaced by en through swift-foundation bundle matching, with the Windows regional format and short-time hour cycle standing for AppleLocale and AppleICUForce24HourTime/AppleICUForce12HourTime | every default-locale date, number and size in the UI | en_<region> from `CultureInfo.CurrentCulture`, plus the hours keyword (`SwiftFormatters.CurrentLocale`, ED-132, ED-134) | `SwiftFormattersTests` default-locale and hour-cycle cases | yes (swift-foundation; transfer to macOS 26 unconfirmed, ED-086) |
| O-38 | `URL(string:)` beyond R-PATH-1: nil for a non-digit port or text before the first ":" that is not a valid scheme; the "/?#[]@" no-scheme heuristic; the scheme kept as written; only an invalid component re-encoded, whole, with its own allowed set (an existing escape becomes %25; the user-info, query and fragment sets); the empty scheme and empty port of `allowEmptyScheme: true` | typed and pasted addresses: `MCPOAuth.swift:201`, `MCPCustom.swift:85`, `MCPSettingsPage.swift:1063`, `MCPProbe.swift:363`, `Git.swift:146`, `MarkdownView.swift:743` | swift-foundation 6.2 reading (`SwiftURL.Create`, ED-136) | `SwiftURLTests` (Oracle O-38) | yes |
| O-39 | "\" in `URL(string:)` on macOS (kept and encoded %5C, where the Windows build turns it into "/", `URLParser.swift:632-634`) and the POSIX shape of a file URL path (`URL.swift:1412-1415`) | `TimelineRows.swift:380`, `LinkParagraphView.swift:95`, `MCPHub.swift:423`, `AttachmentPreview.swift:67` | not converted; POSIX style (`SwiftURL`, ED-136) | `SwiftURLTests` (Oracle O-39) | no (a macOS run) |
| O-40 | `url.host` and `host()` beyond R-PATH-1: `host()` stays percent-encoded; a non-ASCII host gives the IDNA text from `host` and the percent-encoded Unicode name from `host()` (UIDNAHookICU, here over Windows icu.dll); an empty host is nil unless a user, password or port is present (`URL_Swift.swift:452-457`) | `MCPProbe.swift:362-363`, `MCPCustom.swift:85`, `MCPSettingsPage.swift:1063`, `CopilotSession.swift:888`, `MarkdownView.swift:673`, `Hydra.swift:448` | swift-foundation 6.2 reading (`SwiftURL.Host`, `HostComponent`, ED-136) | `SwiftURLTests` (Oracle O-40) | yes, if the toolchain's FoundationInternationalization supplies the UIDNA hook (to confirm) |
| O-41 | `url.path` beyond R-PATH-1 and survey 20: every trailing slash dropped after decoding ("%2F" at the end and "//" included); escapes that are not UTF-8 give ""; a file URL keeps %2F and %00 (`URL_Swift.swift:524-530`, `:574-578`, `:616`; `String+Path.swift:413-428`) | `MarkdownView.swift:677`, `LinkTitles.swift:16`, `MCPProbe.swift:355-357`, `AttachmentPreview.swift:67` | swift-foundation 6.2 reading (`SwiftURL.Path`, ED-136) | `SwiftURLTests` (Oracle O-41) | non-file yes; file URLs partly |
| O-42 | does macOS Foundation's `URL.pathExtension` equal swift-foundation's `String.pathExtension` (hidden name, trailing dot, "." or ".." stem, an extension holding a space, "/" or a bidi control) | `Storage.swift:99`, `:184`, `Wallpaper.swift:127`, `DownloadsPopover.swift:70` | the swift-foundation rule (`SwiftPath.FileURLPathExtension`, ED-140) | `SwiftPathTests` (Oracle O-42) | yes (FoundationEssentials; transfer per ED-086) |
| O-43 | `URL.deletingLastPathComponent()` at the root on a macOS 26 build: the root itself or "/../" (URL.compatibility1) | none reachable (`Git.swift:66-73`, `ThreadRuntime.swift:1966-1972` stop at the repository root; `NativeFileTools.swift:86` fails at the write either way) | the root itself (`SwiftPath.FileURLDeletingLastPathComponentPath`, ED-140) | `SwiftPathTests` (Oracle O-43) | no (the branch exists only in Foundation.framework) |
| O-44 | `URL.pathComponents` of a non-file URL: whether each component is percent-decoded, split before or after decoding, empty segments, "." and "..", "\", a relative reference | `MergeRequestLink.swift:24` | swift-foundation 6.2 (`URL_Swift.swift:641-647`, `String+Path.swift:49-77`): split the encoded path on runs of "/", keep "." and "..", decode each component as UTF-8 (undecodable gives ""), drop a final "/"; "\" stays percent-encoded in one component (`SwiftURL.PathComponents`, ED-143, ED-144). Decode-then-split (older Foundation) is a reported alternative, not source-checked | `Oracle_path_shapes_that_pathComponents_decides_*`, the two "%2B95" rows, the F.URL PathComponents tests | yes for swift-foundation, except rows with "\" (the Windows toolchain converts backslashes, `URLParser.swift:632-634`); transfer to macOS 26 per ED-086 |
| O-45 | the default time zone of `Date.ISO8601FormatStyle().year().month().day()` when it parses a bare date, and whether it rejects text after the day (PR-STORE-169 failure case 2, UNK-0003) | `CopilotSession.parseDate`, the Copilot quota reset date | midnight UTC; text after the day rejected (`FoundationISO8601.ParseDateOnly`) | `ISO8601Tests.A_bare_date_is_midnight_UTC_as_the_parked_rule_says`, `SwiftDateTests.A_bare_date_parses_to_midnight_UTC_as_the_parked_rule_says` | yes (swift-foundation) |
| O-46 | whether `Date.ISO8601FormatStyle().format` truncates or rounds a sub-second part (PR-STORE-170 failure case, UNK-0003) | `ISO8601.string(from:)`, the hang file name, the prompt date line | floor (`FoundationISO8601.Format`) | `ISO8601Tests.A_sub_second_part_is_dropped_as_the_parked_rule_says`, `SwiftDateTests.ISO8601_writes_whole_seconds_by_floor_in_UTC` | yes (swift-foundation) |
| O-47 | whether Darwin `JSONSerialization.data(withJSONObject:options:)` throws a Swift error for a non-finite number (NaN, infinity) or raises an NSException that `try?` cannot catch | `JSONValue.swift:62` (`data`, `compactString`, `prettyString`, and `displayText` of an array or object at `:128`); no Swift construction reaches it; the port's parse does (.NET `JsonElement.GetDouble` gives +/-infinity for `1e400` or a 400-digit integer), and whether Darwin's parse does is O-09 | the whole output is the four bytes `null` (the shared writer's `EncodingError`, R-CODE-5, caught by `try?`) (ED-146) | `A_value_the_writer_refuses_falls_back_to_the_four_bytes_null_for_the_whole_output` | no (swift-corelibs-foundation throws; a macOS run) |
| O-48 | how many redirects URLSession follows before URLError(.httpTooManyRedirects) | `MCPHub.swift:455-465`, `LinkTitles.swift:47-49`, `PlanLimits.swift:243-249`, every `URLSession.shared` site (no redirect delegate in the tree) | 20 followed, the 21st redirect response fails before another request (swift-corelibs-foundation 6.2 `HTTPURLProtocol.swift:459-465`, "This value mirrors Darwin"; `Http.cs` `MaximumRedirects`, ED-152). Not adopted from corelibs: it also follows 305 and 306 and turns HEAD into GET on a 303; `Http.cs` does neither and no Swift site depends on it | `HttpTests.Twenty_redirects_are_followed_and_the_final_response_is_returned`, `HttpTests.The_21st_redirect_fails_with_httpTooManyRedirects_and_is_not_followed` | no (CFNetwork; a macOS run) |
| O-49 | SwiftUI `Text.baselineOffset` on a `Text(Image)` run: sign (positive raises), magnitude in points, image bottom on the text baseline at 0, and the resulting line height | `MarkdownView.swift:386`, `:491-493` (-3.2 on a 16 pt template glyph) | the port puts the child's baseline h + offset below its top through `TextBlock.BaselineOffset`, set locally, 1e-6 when the sum is 0 (`StyledText`, ED-158) | the `StyledTextTests` image-run baseline tests (12.8 for -3.2; 16 for none; 1e-6 for -16) | no (SwiftUI; a macOS run) |
| O-50 | whether a Text modifier applied to a concatenated Text overrides the same modifier already on a segment (SwiftUI resolves outside in: the inner one wins) | `LinkPopover.swift:80` with `Popovers.swift:307-308`; every `Text + Text` site with an outer modifier | inner wins: a modifier sets its trait only where the run's trait is still null (`StyledText`, ED-158) | `StyledTextTests.An_inner_trait_wins_over_the_same_trait_applied_outside` | no (SwiftUI; a macOS run) |
| O-51 | `BlurReplaceTransition(.downUp)` values on macOS 26: opacity, blur radius and scale at the active state, and their order | `UsageViews.swift:273`, `:279`, `:352`, `:360`, `:364` | opacity 0, blur 7, scale 0.9 about the centre for insertion and removal; opacity, then blur, then scale; linear in progress; Avalonia `BlurEffect` radius 7 taken as equal (OpenSwiftUI `BlurTransition.swift`, audited 6.5.4; `SwiftTransition.BlurReplace`, ED-159) | the O-51 insertion and removal tests in `SwiftAnimationTransitionTests` | no (SwiftUI; a macOS run) |
| O-52 | `AnyTransition.combined(with:)` nesting: whether the first transition's modifiers sit inside the second's | `ChatView.swift:1098-1099`, `:278`, `:603-604`, `TimelineMinimap.swift:237`, `SettingsView.swift:557`, `TimelineRows.swift:1068-1069` | first inside second (OpenSwiftUI `CombiningTransition.swift:69-74`, audited 6.5.4; the accumulator in `SwiftTransition.Apply`, ED-159) | the O-52 composition test in `SwiftAnimationTransitionTests` (corners (20,18)-(80,42)) | no (SwiftUI; a macOS run) |
| O-53 | whether `CGImageSourceCreateThumbnailAtIndex` with the options at `ThumbnailCache.swift:54-59` returns an image for WebP, AVIF, HEIC/HEIF and subsampled YCbCr TIFF on macOS 26, and applies their orientation (HEIF irot/imir, EXIF) | `ThumbnailCache.swift:44-62`; callers `AttachmentPreview.swift:9-12`, `:376`, `:437`, `DownloadsPopover.swift:29-31`, `:79`, `TimelineRows.swift:1346` | yes to both on the Mac (believed: support predates the 26.0 target); the port decodes through WIC where the running user has the codec, else null, and reads only JPEG/TIFF orientation (NAMED DEVIATION, ED-160) | none (the codec is per user; a test would depend on the account) | no (a macOS run) |
| O-54 | whether `CGImageSourceCreateThumbnailAtIndex` yields an image for an SVG file on macOS 26 | `ThumbnailCache.swift:44-62` via `AttachmentPreview.swift:9-12`, `:376`, `:437` | nil (ImageIO sources are raster); the port also gives null, WIC has no SVG decoder (ED-160) | `ThumbnailCacheTests.An_svg_file_yields_no_thumbnail_and_is_decoded_again_next_time` | no (a macOS run) |
| O-55 | whether a macOS FSEvents stream (FileEvents) reports a write made through a descriptor still open before a later cookie file's event | `WorkingTreeWatch.swift:15-18`, `:241-273`, `:300-301` (a cached tree reused while a writer holds a file open) | no: a write is counted when its handle closes; NTFS notifies at handle cleanup (measured by gpt6-workingtree-test-run1..5.log once they exist); xnu main `vfs_vnops.c` `vn_close` emits FSE_CONTENT_MODIFIED under FWASWRITTEN; no flush or probe is added (ED-161) | `WorkingTreeWatchTests.Write_through_a_handle_still_open_is_counted_once_the_handle_closes` | no (kernel behaviour; a macOS run) |
| O-56 | whether a macOS FSEvents stream created with kFSEventStreamCreateFlagFileEvents reports a directory whose entries were created or deleted (as well as the entries) | `WorkingTreeWatch.swift:142`, `:195-196` (cookie files inside the git directory, skipped by the gitPrefix match only) | no: only the entries' own paths; the port's FileWatcher drops a directory's Changed event (ED-166) | `FileWatcherTests` Decision ED-166 test; `WorkingTreeWatchTests.Cookie_event_allows_a_cached_tree_to_be_reused` | no (a macOS run) |
| O-57 | whether macOS 26's URLComponents.queryItems setter keeps a name or value as written when every character is in the query-item set (the query set less `&` and `=`) and otherwise percent-encodes every byte outside it (space, `&`, `=`, `#`, `%`, non-ASCII as upper-case UTF-8 hex), keeping `+`, `/`, `?`, `:` and `@` | `MCPOAuth.swift:581-598`, `:772-778`; `LinkTitles.swift:63-67` | yes, per swift-foundation 6.2 URLComponents.swift:638-667 and URLParser.swift:1218-1266 (ED-169) | URLComponentsTests (Oracle trait) | no (a macOS run) |
| O-58 | whether macOS 26's URLComponents.queryItems getter splits on `&` at the first `=`, reads an item without `=` as a nil value, gives [] for an empty query and nil for none, leaves `+` undecoded, and turns a non-UTF-8 escape into an empty name or a nil value | `MCPOAuth.swift:526-535`, `:582`; `LinkTitles.swift:25-26` | yes, per swift-foundation 6.2 URLComponents.swift:590-636 (ED-169) | URLComponentsTests (Oracle trait) | no (a macOS run) |
| O-59 | whether macOS 26's URLComponents.host IDNA-decodes a host that was not percent-encoded, keeps ASCII case as written and keeps IP-literal brackets (`[::1]`), unlike URL.host | `MCPCustom.swift:174-180`; `MCPOAuth.swift:801-810` | yes, per swift-foundation 6.2 URLComponents.swift:184-193, IDNA through Windows icu.dll as SwiftURL does (ED-169) | URLComponentsTests (Oracle trait) | no (a macOS run) |
| O-60 | whether macOS 26's URLComponents.url rebuilds the string as computedString does (the port rewritten from Int, so `0443` becomes `443`; user, password and `@` dropped when both are nil) and parses it through URL(stringOrEmpty:) on the Swift URL path rather than CFURLCreateWithString | `AppModel+HydraMerge.swift:384-388`; `MCPOAuth.swift:598`; `LinkTitles.swift:67` | yes, per swift-foundation 6.2 URLComponents.swift:434-490, :765-773 and URL.swift:611-614 (ED-169) | URLComponentsTests (Oracle trait) | no (a macOS run) |
| O-61 | the order of macOS 26 NSFontManager.shared.availableFontFamilies (sorted by name, or another order) | `TerminalFont.swift:108`, `:115`, `:126` (first match wins in nerdSibling, firstInstalledNerdFont and resolve) | sorted ordinal case-insensitively (ED-175) | SwiftFont and TerminalFont tests with Oracle O-61 | no (a macOS run) |
| O-62 | the curve of a lone SwiftUI CubicKeyframe with no velocities or neighbours (from initialValue to its target) | `Genie.swift:155-168` | Catmull-Rom with duplicated endpoints: start + (end - start) * (-t^3 + 1.5 t^2 + 0.5 t) (ED-178) | GenieTests with Oracle O-62 | no (a macOS run) |
| O-63 | SF Symbol metrics against the Lucide stand-in: the square a symbol takes beside text of point size S, its vertical placement, the .imageScale factors, how a .fill variant fills and knocks out, and the geometry of composed renderings | every `Image(systemName:)` site (PA-0012) | ED-182's values (1.2 S square on the cap-height centre; 0.8/1.0/1.25; container filled, inner knocked out; 50 % inner glyph, (2,2)-(22,22) slash, r 4 badge at (18,6), Inter Semibold 11/24 text) | SwiftImageTests with Oracle O-63 | yes: measure the eight tour screenshots (OD-0004) |
| O-64 | the weight macOS 26 gives SwiftUI's plain `Font.headline` (bold, as the macOS type ramp lists it, or semibold, as the upstream comment at B1 ChatZoom.swift:59-61 implies) | `TourView.swift:106`, `RequestCards.swift:13` | bold (ED-091, ED-186); `.chat(.headline)` is semibold by the source either way | SwiftFontTests and TourViewTests with Oracle O-64 | no (a macOS run) |

---

## Appendix C. Evidence

All commands ran on this PC (Windows 11, .NET SDK 10.0.401) with `MARROW_PORT_DATA_DIR` set to a throwaway
folder. Nothing under `E:\Droppy\port` or `E:\Droppy\reference` was modified; the port-settings trial ran on a
scratch copy.

| What | Command | Result | Log |
|---|---|---|---|
| proto-A | `dotnet test` in `E:\Droppy\work\conventions\proto-A` | 134 passed of 134, exit 0 | `foundation-proto\evidence\proto-A.test.log` |
| proto-B | `dotnet test` in `proto-B` | 71 passed of 71, exit 0 | `evidence\proto-B.test.log` |
| proto-B Avalonia probe | `dotnet run` of `Proto.AvaloniaBinding` | ALL PASS, exit 0 | `evidence\proto-B.avalonia.log` |
| proto-C | `dotnet test` in `proto-C` | 54 passed of 54, exit 0 | `evidence\proto-C.test.log` |
| foundation-proto build | `dotnet build foundation.slnx -c Debug --no-incremental` after deleting every `bin` and `obj` | 0 warnings, 0 errors, exit 0 | `foundation-proto\build.log` |
| foundation-proto tests | `dotnet test foundation.slnx --no-build --logger "console;verbosity=normal"` | 190 passed (Core) and 14 passed (UI), 0 failed, 0 skipped, exit 0 | `foundation-proto\test.log` |
| repeat | the same, five more times | 204 of 204 each time | `foundation-proto\repeat.log` |
| negative checks | planted violations on a scratch copy | CS8509, CA1304, CA1305, CA1310, CA1311 fire; the three meta-tests fail on planted layout and trait errors; `req_verify`'s parser credits two Req ids to a class named `Foo` and loses a third | `foundation-proto\negative-checks.log`, `tools\reqparse.mjs` |
| proposed settings on the port | a copy of `E:\Droppy\port` at HEAD 4f70ef7 with the props and `.editorconfig` of 4.2 | 8 analyzer errors, all in tests; with the analyzers off, 239 + 10 + 2 existing tests pass under checked arithmetic and CS8524 | `foundation-proto\port-settings-check.log` |

Facts checked in files rather than by running: `Avalonia.Headless.XUnit` 12.1.2 depends on
`xunit.v3.extensibility.core` 3.2.2 (its nuspec); `req_verify.mjs` reads traits from test source line by line
(lines 58 to 89, copied into `tools\reqparse.mjs`); ED-063 is the last recorded decision at port HEAD 6da2a58, and that commit (after the settings trial at 4f70ef7) changes
only `porting/` files, no code, tests or build settings;
`Avalonia.Headless` 12.1.2 and xunit 2.9.3 are already pinned in the port's `Directory.Packages.props`.

Citations: a script confirmed that every `File.swift:line` in this guide and in foundation-proto (106) names an
existing B2 file and a line in range, and about 70 were read against the source. Four, inherited from the survey
and draft B, pointed at neighbouring code and are corrected here: `HangWatchdog.swift:47-56` is `:50-59`,
`JSONRPC.swift:79-82` is `:82-86`, `Storage.swift:336-352` is `:337-356`, and the `queue.sync {}` barrier is
`Storage.swift:378`. `cite_check.mjs` (ED-081) should run on every packet.

What the prototypes do not prove: any Swift-side fact (every O item); the units of 4.3; process, file-lock,
network and secret behaviour; the production settings store; frame clocks; formatters; the port's real views.
