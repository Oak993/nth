# T25 / MA-3b-W integration — 2026-10-07

Verdict: **NOT READY** for the Windows release gate. The requested production port bindings are implemented. Linux validation is incomplete/red; the real request-after-pin regression and Windows UI/runtime tests need a Windows run. No test failure is reclassified as a pass.

## Integrated source and authority

- Started `codex/ma-wire` from `origin/codex/ma-wave2` (`d61419abf`), merged reviewed engine `7b9104068` with `--no-ff` in `5c712074c`.
- `origin/codex/ma-e1` contained the ready link/opinion engines and integration contract. Merged `9242fc314` with `--no-ff` in `93314e441`.
- Read `multi-account-CHECKLIST.md`, job-0 types, ED-453 and E1 integration notes. The MA-3b-W plan reference points to `work/scratch/lead/ma-3b5-plan.md`, which is not in this checkout; used the supplied W packet and checked-in seam contracts for scope.
- Owner law OD-0114 through OD-0126 was read with `git show origin/claude/verify-terminal-20261001:porting/owner_decisions.md`, at remote commit `285b6b7a99b39e0e517f34c8d34a1860c945f83f`. No local owner-law rewrite.
- Both merge conflicts were ledger-only. Byte-line comparison against **each parent** found only insertions in `engineering_decisions.md` and `STATE.md`; all pre-existing bytes, including CRLF/LF, survived. No conflict markers remain.

## Production wiring

`Program` binds `ThreadPortSeams` on MainActor before `FinishLaunching`, using the same `WindowManager.Shared.Model` used by AppDelegate. One app-owned `LiveThreadPorts` supplies:

| Port / screen | Live boundary |
| --- | --- |
| IThreadAccountPort | Real connected account stores, plan-limit reports, API capacity, active runtime identity and `ProviderAccountPinID`. `PinAsync` calls `TryPinThreadAccountAsync`; Back to order clears the real pin; Use Settings clears the real thread choices. Disabled/out/capped/unknown accounts refuse without mutation. |
| ICardPort | Direct `MultiAccountRouting.State` / `Changed` / `AnswerAsync`, including forwarded cancellation and the API rotation card. |
| Capsule | Live account reports and the real standby/linking/in-sync stream. Primed and injected readiness share `InSyncReady` and the same tick. An immediate switch preserves a drawable tick for the design's 800 ms sync span; waiting clears it; detach cancels frames and subscriptions. |
| Popover | Fresh live per-thread/service model on each opening; actual pin/active identity, defaults and remembered choices. Owning models release their report subscriptions on close. |
| Usage | Selected thread's active account, all connected services, live reports and OpenRouter credits. Discarded/closed models release report subscriptions. Single-account fallback remains the existing panel. |
| Cards / notes | Answers reach the engine. An Ask follow-up card cannot be hidden by the previous card's completion callback. Waiting notes call the engine's Switch now action; unresolved transfer choices still ask, without rewriting the stored Wait preference. Handover sheets use the recorded archive/briefing rather than Settings example counts. |
| ILinkPort | Real E1 `LinkEngine` with stable completed-turn identity, captured author, user message and final answer. Forwarding occurs after turn-finished metadata settles. |
| IOpinionsPort | Real E1 `OpinionsEngine`: current context/revision, actual connected identities/caps, fresh per-account configuration, shared private API key routing, persisted directed consent and the real modal prompt. Combined text/byline append atomically to the real timeline and deduplicate by round ID. |

Revisions never reuse an old value after a runtime retires. Thread stop/deletion invalidates heads; shutdown disposes both engines and account subscriptions. Unrelated thread-list updates refresh availability without advancing every thread's revision.

The default remains Handshake off and choices unset. Ask records only its selected Remember scope and opens the normal switch/wait card; it grants no switch or model turn. Cross-service opinions/checks use the settings-backed consent and real modal; **Don't ask again** starts unchecked. Decline/cancellation prevents launch/context transfer.

## Fakes and remaining boundaries

**No requested production port remains on a fake.** `ThreadPorts.None` and null ambient defaults remain intentional isolated-view/test defaults before application startup binds them. Test fixtures use scripted sessions, in-process HTTP, fake card events and recording consent; no real provider CLI, network or model was used by the added tests.

E1 still deliberately fails unsupported text-only CLI heads (anything other than verified Claude), and capped token-only API heads without authoritative model pricing. Supported E1 head services are Claude, DeepSeek, Meta, Zai, OpenRouter and Local. W supplies actual caps/private pools; it supplies no guessed price estimator. These failures are real engine errors, not simulated successes.

This checkout has the Ask-others Core model but no App link-column/opinions/Combine screen from the separate UI job. Their ports are ready for that presentation. The full MA-4 Handshake painter/sound presentation is also outside this seam binding; this change binds the capsule's live state/readiness tick. This report does not assert that the complete multi-account release UI is finished.

## Test-first evidence and final validation

SDK 10.0.401 installed outside the repo at `/tmp/ma-wire-dotnet`; solution restore used `EnableWindowsTargeting=true`. Windows target frameworks, Avalonia 12.1.2, test expectations/tolerances/references and incoming test files were not changed. Tests used separate disposable `MARROW_PORT_DATA_DIR` folders as required by the Linux harness.

Red evidence: missing live adapter compilation; Core first-time Ask rejection (3 failed / 1 passed before fix); API Ask rejection; Ask-follow-up UI timeout; missing/failing Switch-now action; immediate-switch tick disappeared before its first frame. New test metadata and helper layout were corrected to satisfy the existing meta-tests, without changing assertions.

| Run | Result |
| --- | --- |
| Core Accounts + Port.MultiAccount | **511 passed**, 0 failed, 0 skipped. Includes all five new Core Ask/Switch-now regressions. |
| Services Port.MultiAccount + Port.Accounts + Standby, !Native | 259 passed / 25 failed. Codex/Pi stand-in fixture paths and two HandshakeSummaryAdapter shell/native tests require Windows. |
| Services Port.MultiAccount + Port.Accounts, !Native | 224 passed / 2 failed (HandshakeSummaryAdapter native-shell paths). |
| Services portable account slice, explicitly excluding HandshakeSummaryAdapterTests | **223 passed**, 0 failed, 0 skipped. This smaller slice does not replace either failed broad run. |
| Final App Port.MultiAccount + Views.Composer.MultiAccount + Views.MultiAccount + Traceability/Layering, !Native | **112 passed / 1 failed**, 0 skipped, 113 total. All three App traceability tests pass. All 14 runnable new App tests pass; the fifteenth is the unresolved request-after-pin test below. |
| Core meta-tests | **5 passed**. |
| Services meta-tests | **6 passed**. |
| Full Core, `FullyQualifiedName!~Native` | **Aborted**, 2,197 passed / 272 failed / 2,469 reported before test-host crash. Missing Windows ICU/kernel32/shell32, Windows path/PowerShell and environment casing failures. An aborted run is incomplete. |
| Full Services, `FullyQualifiedName!~Native` | **4,212 passed / 1,290 failed / 52 skipped**, 5,554 total. Windows-native process/ICU/file-watcher APIs, Windows stand-in paths/PowerShell, environment casing and other assertions failed. |
| Broad App, `FullyQualifiedName!~Native` | **Interrupted/incomplete** after 761 failed results; stopped reporting progress for several minutes while the host remained busy. No complete pass count or final TRX was produced. Does not establish an App suite pass. |

The remaining new failure is `LiveThreadPortsTests.The_composition_offer_exposes_real_engines_and_pins_the_next_request`: `ManualAccountPinRig.Send` fails with `SwiftErrors.LocalizedDescription(NotSupportedException)` during its first send, **before** constructing `LiveThreadPorts`. The existing `ManualAccountPinTests.A_popover_pin_uses_the_chosen_key_on_the_next_request_and_retains_the_live_conversation` reproduces the same failure. The separate live boundary test verifies real pin persistence, original active identity before dispatch, clear, retirement/reopening and no HTTP request. It does not substitute for the failed request-after-pin assertion. Windows rerun and investigation remain required; this report does not claim a proven Windows-only root cause for that exception or every broad assertion failure.

Commands (each uses its own `MARROW_PORT_DATA_DIR`; builds also use `--no-restore -p:EnableWindowsTargeting=true -m:1`):

```sh
dotnet test tests/Marrow.Core.Tests --no-build --filter 'FullyQualifiedName!~Native'
dotnet test tests/Marrow.Services.Tests --no-build --filter 'FullyQualifiedName!~Native'
dotnet test tests/Marrow.App.Tests --no-build --filter 'FullyQualifiedName!~Native'
dotnet test tests/Marrow.Core.Tests --no-build --filter 'FullyQualifiedName~Accounts|FullyQualifiedName~Port.MultiAccount'
dotnet test tests/Marrow.Services.Tests --no-build --filter '(FullyQualifiedName~Port.MultiAccount|FullyQualifiedName~Port.Accounts)&FullyQualifiedName!~Native&FullyQualifiedName!~HandshakeSummaryAdapterTests'
dotnet test tests/Marrow.App.Tests --no-build --filter '(FullyQualifiedName~Port.MultiAccount|FullyQualifiedName~Views.Composer.MultiAccount|FullyQualifiedName~Views.MultiAccount|FullyQualifiedName~Traceability|FullyQualifiedName~Layering)&FullyQualifiedName!~Native'
```

Raw local evidence is in `/tmp/ma-wire-{core,core-account,services,services-account,services-linux,services-portable,app,app-account,live,tick-red,core-traceability,services-traceability}.log` and completed runs' matching `.trx` files. The committed TSVs preserve exact reported failing test names and first error lines; they are evidence, not a skip list. Earlier App meta-test failures in the broad TSV were fixed and are green in the final targeted run.

## Windows run list

- Rerun the new request-after-pin test, the existing `ManualAccountPinTests` and `ManualContextPinTests`, and all App `Port.Accounts` routing/adoption/consent/cap/wait/injection tests.
- Run all three full Core/Services/App `!Native` commands on Windows and triage every failure. The exact observed App methods without a subsequent green result are in [app-windows-rerun-list.txt](app-windows-rerun-list.txt) (517 methods); observed failures are in [app-linux-failures.tsv](app-linux-failures.tsv). The interrupted App run leaves additional unexecuted tests unverified.
- The 34 App source files matched by the Native exclusion are listed in [app-native-windows-list.txt](app-native-windows-list.txt). Use the repository's native verification protocol for actual native cases. Multi-account presentation priorities include `HandshakeNativeFrameTests`, `PopoverGlassNativeTests`, `GlassPanelAndRimNativeTests`, timeline/native popover cases and the real Windows capsule/popover/usage/card flow at 100% and 150% DPI. Files whose names contain Native are listed even if they are dependency meta-tests rather than native captures.
- Services `CodexStandbyTests`, `PiStandbyTests` and `HandshakeSummaryAdapterTests`, plus App `InjectedStandbyHostTests` / `OpenCodeStandbyHostTests`, need Windows fixture/native verification. No real-program or model check was performed in this Linux task.

No MA feature is marked DONE and no release readiness is asserted without the Windows gate.

## Task commits

- `5c712074c` reviewed-engine merge.
- `93314e441` E1 engine merge.
- `905d9d4b9` Core first-time Ask acceptance.
- `0042c8c84` API Ask, follow-up card and Switch-now action.
- `6bfd3a197` app composition, live ports, screen observations and lifecycle boundaries.
- Final evidence/ledger commit follows these implementation commits; delivery pushes only `codex/ma-wire`, without force.
