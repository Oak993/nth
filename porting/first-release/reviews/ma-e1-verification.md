# T17 MA-3b-E1 verification

Verdict: **NOT READY for release**. E1 source and focused offline checks are complete; the required broad Linux runs are not green. Live job W binding, verified additional CLI heads/model pricing and native Windows validation remain. The independent reviewer found no remaining blocking source issue within E1 scope and performed read-only review, not an independent test run.

## Delivered behavior

`LinkEngine : ILinkPort` preserves explicit tap link/unlink and ordered intent, hides active/out/off targets, deduplicates completed answer ids and streams private checker results. The canonical completed-answer callback checks the author's last answer even when it just exhausted its cap, without sending a turn to that author. Cancellation, source/service changes and context epochs suppress stale work.

`OpinionsEngine : IOpinionsPort` fans this one message to the active Answer and selected other Opinions in parallel. Everyone with room includes all eligible connected identities and Local entries; unsupported services become visible errors, never silently omitted targets. Selection is confined to the round. Combine requires usable completed answers, runs on the original source, posts the byline once and changes no routing/pin/pill state. Ambiguous append failures cannot cause a second append attempt.

OD-0120 consent is asked before different-service conversation transfer, defaults off, and persists only explicit accepted-and-checked directed service pairs. Every modal starts with **Don't ask again** unchecked; Cancel, dismissal, cancellation or no owner cannot grant permission. This does not enable automatic Handshake/key/account switching (OD-0116). Invalid preference bytes are preserved and fail closed.

The concrete host creates fresh account-bound sessions, strips resume/Hydra/skill launch state, rechecks live ownership/context/cap state and uses the existing verified text-only boundary. API heads require shared private key routing; account fallback is refused and spend stays with the selected key. Capped token-only APIs without authoritative model pricing fail before send. OpenRouter uses reported charged USD; no prices were invented. Claude, DeepSeek, Meta, Zai, OpenRouter and Local have verified runner paths; other CLIs fail safely until an equivalent no-tools turn is proved. Startup/inter-turn tool, approval, question and agent events also fail closed.

The live integration obligations are in [multi-account-E1-integration.md](../multi-account-E1-integration.md). The original job-0 view seam remains unbound for job W.

## Provenance and commits

Branch `codex/ma-e1` started at `origin/claude/ma-engine` (`2fc98c90`). Only `codex/ma-e1` was pushed, without force. CLAUDE.md, IMPLEMENTING.md, contract/amendments, checklist, locked link/opinion design and existing engines/ports were read. OD-0114 through OD-0125 were read from the authoritative `origin/claude/verify-terminal-20261001:porting/owner_decisions.md`, commit `1291606cbac734da0b2afe125ad9c01227eed83b`; owner decisions take precedence over the page. No locked design or owner-decision file was changed.

| Commit | Item |
| --- | --- |
| `767d13a0` | Authorized no-ff merge of `claude/ma-3b5-base` for job-0 seams |
| `39d252ec` | Immediate ledger merge repair preserving original bytes and both incoming insertion blocks; conflict markers removed before push |
| `4ec202f5` | Per-answer link checking |
| `567c4172` | Parallel one-message opinions and single Combine |
| `3a5114db` | Persistent consent, actual modal, fresh restricted heads and private key binding |
| `9242fc31` | Epoch/ownership guards, completed last answer, live link state, port-visible errors and conservative cap pricing |

No tracked `global.json` exists on the working/base branches. SDK **10.0.401** was installed under `/workspace/.dotnet` per IMPLEMENTING.md; actual SDK/runtime build and test execution succeeded. Avalonia packages remain pinned to **12.1.2**.

## Validation

All E1 test additions use fakes; they invoke no real HTTP, CLI or model and write no credentials. Existing tests, assertions, tolerances and reference files are unchanged after the authorized base merge. Diff inspection shows E1 test files as additions only; subsequent fixture extensions preserve their existing behavior. No native tests, installer, packaging or real Windows UI/CLI verification was claimed.

| Final run | Passed | Failed | Skipped | Total | Result |
| --- | ---: | ---: | ---: | ---: | --- |
| Core accounts, job-0 thread ports, consent, Traceability | 351 | 0 | 0 | 351 | Green |
| Services E1 plus existing standby/consent/Traceability | 84 | 0 | 0 | 84 | Green; five further runs each 84/84 |
| App job-0 shared views/seams, modal lifecycle, Traceability | 16 | 0 | 0 | 16 | Green on Linux headless; Windows-targeted App built |
| Core project, exact `!~Native` filter | 3566 | 459 | 0 | 4025 completed | Aborted by Windows-native test-host exception |
| Services project, exact `!~Native` filter | 4199 | 1288 | 52 | 5539 | Completed, non-green |

Pre-E1 broad baseline after the base merge: Core aborted after 1821 passed / 198 failed / 2019 completed; Services completed with 4141 passed / 1288 failed / 52 skipped / 5481 total. The final Services failure-name set is **identical** to that baseline. All **58 new E1 Services cases passed in the broad run**, accounting for its 58 additional passes and total cases. The existing skips were not changed. Aborted Core totals vary with test scheduling and cannot establish whole-project coverage.

Observed broad failures include unmarked `kernel32.dll`/`icu.dll` calls that survive the name filter, unavailable Windows shell/process behavior, duplicate proxy environment names under Windows case comparison, and native watcher delivery. These examples explain the Linux validation limit; all 1288 failures were not individually diagnosed. No tests were weakened, newly skipped or filtered away to get a green project result.

Tests were written first. Red behavioral witnesses retained in the session: opinion publication 30 pass / 1 fail before repair; context/completed-answer/error hardening 0 pass / 8 fail; live-link transitions 1 pass / 3 fail; no-USD cap pricing 2 pass / 1 fail. Missing engine/consent/runner/host/dialog/binder seam compiler failures were also observed before implementation. Independent review additionally identified the final check cancellation race and missing shared API spend binding; both were repaired in production.

Commands used SDK on PATH and isolated `MARROW_PORT_DATA_DIR` values. App builds/tests used `-p:EnableWindowsTargeting=true`. Broad final commands (assemblies built by the focused test commands):

```bash
dotnet test tests/Marrow.Core.Tests/Marrow.Core.Tests.csproj --no-build --no-restore --filter "FullyQualifiedName!~Native"
dotnet test tests/Marrow.Services.Tests/Marrow.Services.Tests.csproj --no-build --no-restore --filter "FullyQualifiedName!~Native"
```

Focused filters, exact TRX counters, red witnesses and comparison receipts are in [ma-e1-test-results.json](ma-e1-test-results.json). Raw session logs are `/tmp/marrow-{core,services,app}-e1-final.log`, `/tmp/marrow-{core,services}-project-final.log`, and `/tmp/marrow-ma-e1-results/*.trx`; these temporary files are not committed. Final source/code-doc whitespace checks were clean. The mixed-line-ending ledgers received byte-exact append-only entries ED-454 through ED-458; staged bytes and historical prefixes were checked separately without normalizing CRLF/LF.

## Remaining questions

- Job W must supply live connected/cap metadata, ownership epochs/notifications, full context, account-specific configuration, real modal/setting binding, completed-answer notifications and an atomic deduplicated timeline append.
- Other CLI services need a verified text-only model turn; standby injection/warm/deferred status is insufficient. They remain explicit safe failures in checks/opinions.
- Capped token-only API models need authoritative pricing through the existing estimator seam; absent pricing is blocked. No unverified rates are supplied by E1.
- Required project tests must be rerun on Windows, with native UI/CLI behavior and packaging verified there. Full App tests were not run; only the 16 stated headless checks ran.
- Cancellation sources/result history remain owned by the engine lifetime; shortening retention is a non-blocking cleanup opportunity recorded for integration.
