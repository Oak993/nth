# MA-3: Local provider foundation

READY for the commissioned foundation. Branch `codex/ma-3`, based on `claude/verify-terminal-20261001` at `7a4cfad3ef57459faf2f593368412fd44d54de06`. This is the user's Local unit, not the different proposed MA-3 row in the MA-0 build-order table. B3 source baseline and parity completion records remain unchanged.

## Delivered

- Appended port-only `ProviderKind.Local`, with no key, credential-store access, executable requirement, hosted credits or subscription quota. Loopback servers have the exact model-layer summary `No limit, stays on this PC`. Private IP servers describe the network; arbitrary chosen hosts are named without a false on-PC privacy claim.
- Automatic discovery of loopback 11434, 1234, 8080 and 8000; any number of explicit HTTP(S) addresses, including LAN, DNS, IPv6 and an optional base path. Every server is its own entry. SHA-256 endpoint identity plus escaped original model ID prevents collisions across servers and survives rediscovery. Additional addresses persist as non-secret settings; no LAN scan or download.
- `/v1/models` and Ollama `/api/tags`, with optional Ollama `/api/show`, LM Studio `/api/v0/models`, and llama.cpp `/props`. Each server has a 1.5-second total deadline; eight probes run concurrently without a server-count limit. Cancellation preserves the previous complete snapshot. Malformed or failed servers do not advertise ready models.
- Explicit tool support and effective reported context where available. Ollama configured `num_ctx`, LM Studio loaded context and llama.cpp single-model per-slot context take precedence over theoretical model capacity. Unknown context is labelled `4096 context fallback`; unknown or unsupported tools run text only. No ability is guessed from a model name.
- A real `LocalSession` over `OpenAICompatibleSession`: native Marrow tools, approvals, SSE/history/usage, raw server model IDs and per-model context. Output reserve is bounded to `min(2048, context/4)`, at least one. Foreign-server selections fail closed. Local sends no bearer key or cookies and follows no redirects, even if a key was supplied accidentally. Existing providers keep their previous defaults.
- Registry status/catalog and runtime factory integration: discovery callers share one in-flight task; selected server and catalog revision bind the session. Changing endpoint or refreshing metadata replaces the bound snapshot and reuses the existing API transcript replay. No implicit switch to another server.
- Existing provider plumbing receives Local through its current lists. One exhaustive composer brand switch maps Local to the existing purple no-provider palette to prevent a crash. No controls, assets, styles, Handshake, account UI or model downloads were added.

Authority: OD-0114; OD-0049/OD-0060; the Local items in multi-account-NOTES.md; multi-account-FEASIBILITY.md section 5. Decisions are recorded under the byte-exact heading `## ED-410` in engineering_decisions.md.

## Test-first and review

Tests were authored before implementation and independently reviewed. The initial red run failed with three CS0234 errors because `Marrow.Services.Port.Local` did not exist (`/tmp/ma3-red.log`). Implementation then made the behavioral tests pass. Independent review closed address isolation, metadata/fallback, cancellation, model-switch/vision, cookie/redirect, persisted-address, shared-discovery, and runtime-revision findings. A final enum/capability census preserved all original provider scenarios and updated their explicit counts, command and policy sets for Local. The missing composer switch arm was caught by executed regressions and repaired, then reviewed.

New tests carry class-level `Rule=R-TEST-10` and `Feature=MA-3`, as tests/Marrow.Testing/Traceability.cs accepts. No new source-parity Req is claimed. The old scripted Hydra matrix retains all 15 original providers; Local's endpoint-bound behavior and actual system-policy predicate are checked separately with fake servers.

All Local HTTP tests use real fake servers on loopback with deterministic responses. No real model, real credential or internet inference is used. Both App and Services test modules install an empty Local discovery factory so legacy RefreshEverything tests cannot probe installed servers. Production retains the four default candidates. Native read proof reads a real file and asserts its role=tool result and call ID in the next request; supervised refusal proves a file remains untouched. The read test's existing non-parallel collection scopes Linux cwd to `/` and restores it so the unchanged Windows path gate can resolve the actual throwaway file. Storage/home are bound below that throwaway folder.

## Verification

Linux SDK 10.0.401; no new dependencies. Every test command sets `MARROW_PORT_DATA_DIR=/tmp/marrow-ma3-data` and adds `/workspace/.ma3-dotnet` to PATH. Baseline comparisons use a detached worktree at the exact SHA above with a separate `/tmp/marrow-ma3-baseline-data` root. All filters end with `&FullyQualifiedName!~Native`.

```text
dotnet build Marrow.sln --no-restore -c Debug -p:EnableWindowsTargeting=true -warnaserror
Build succeeded.
    0 Warning(s)
    0 Error(s)
Time Elapsed 00:01:10.09
```

Five consecutive runs of `--filter 'Feature=MA-3&FullyQualifiedName!~Native'` passed: Services 28/28 and App 1/1 in each run, zero failures and zero skips. All four Traceability classes passed, 12/12 checks.

The broader touched slices were run without hiding platform failures, then run against the base. Counts below are final; every failed test name matches its exact-base counterpart. The JSON evidence includes complete filters, commands, failure names/messages and TRX hashes.

| Slice | Branch total | Passed | Failed | Base passed / failed |
|---|---:|---:|---:|---:|
| core | 337 | 326 | 11 | 326 / 11 |
| services | 460 | 413 | 47 | 385 / 47 |
| app | 11 | 6 | 5 | 5 / 5 |
| ui | 3 | 3 | 0 | not rerun |
| settings | 88 | 64 | 24 | 63 / 24 |
| credits | 66 | 51 | 15 | 51 / 15 |

There are 102 pre-existing Linux failures across these broader slices: Windows ICU/kernel32/user32 calls, Windows fixture paths/CLI stand-ins, image downscaling without its Windows binding, and a Windows-specific socket-error string. No test was skipped, weakened to accept these results, or changed to fix unrelated Windows/Linux behavior. An additional Core cancellation-gate failure occurred once during a concurrent build/test run, passed alone and passed in the final whole Core slice; the final failure set matches the base exactly. The broader slices are not all-green on Linux.

Artifacts in this execution workspace: `/tmp/ma3-build.log`, `/tmp/ma3-final-*.log`, `/tmp/ma3-results/ma3-final-*.trx`, `/tmp/ma3-results/ma3-repeat-*.trx`, `/tmp/ma3-baseline-*.log` and `/tmp/ma3-baseline-results/*.trx`. Durable summary: [ma-3-EVIDENCE.json](ma-3-EVIDENCE.json).

## Scope of readiness

Readiness covers the requested Local foundation and fake-loopback checks. Real Windows/model/server smoke tests, native metadata version expansion and authenticated Local endpoints remain unverified. Token-aware input budgeting and automatic compaction are outside the commissioned numbered scope; context reporting and the output reserve do not guarantee that every history fits. This is not a Windows release/native-gate or full parity verification claim. No parity/task completion ledger is advanced for the differently scoped feasibility-table MA-3.
