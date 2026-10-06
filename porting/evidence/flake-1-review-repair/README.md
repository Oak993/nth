# FLAKE-1 review repair — NOT READY

Fetched and fast-forward merged `cloud/review-flake-1` at `3e3323c4e33b8d2ce41caaa6a77b350c683d4baf`. Its report and REVIEW_ tests remain unchanged. Repair follows `porting/reports/review-flake-1.md`; no production source changed.

1. **Abandoned publication mutex: fixed.** Each of the five acquisitions (App provider reader, Services provider/Pi readers, provider writer, Pi writer) now catches only `AbandonedMutexException`. The exception means the caller owns the mutex; it proceeds through the original protected operation and releases ownership in the existing `finally`. Genuine contention still uses the blocking `WaitOne()`; no timeout, bypass, retry, or other exception suppression was introduced. Both unchanged REVIEW_ tests failed with the reported exception before the fix. Test-first additions also reproduce App reader abandonment and prove that a live holder blocks all three readers until a complete record is published.
2. **Shell's missing second-timer proof: restored.** The cancellation/ignored-stop test joins one atomic sorted deadline snapshot exactly equal to `[1 s, 120 s]`. This proves that the kill timer is registered alongside the still-pending process deadline, with exactly two active timers, before the one-second advance. All handler-arming, child-still-running, cancellation and termination assertions remain. The initial delivery's claim of no assertion weakening was incorrect for this assertion; the independent review identified it and this change restores it.
3. **Windows behavioral verification: outstanding.** Linux setup failures remain failures. Neither 0/50 attempts nor standalone fixture checks verify the native behavior. The original Composer/chime/orb flakes also remain unreproduced; their proposed causes are hypotheses. No READY claim or acceptance/DONE mark is made.

## Test-first and loaded results

Linux, .NET SDK 10.0.401, four independent CPU-bound workers in a four-CPU cgroup; every test invocation uses `-p:EnableWindowsTargeting=true` and `FullyQualifiedName!~Native`. Baseline and repaired binaries were built separately. Counts are passes per case; no skips or retries.

| Case | Before → after |
| --- | --- |
| REVIEW provider reader abandonment | 0/50 → 50/50 |
| REVIEW Pi reader abandonment | 0/50 → 50/50 |
| Added App provider reader abandonment | 0/50 → 50/50 |
| Added live-contention reader checks (App provider, Services provider, Pi) | 50/50 → 50/50, each |
| ThreadRuntimeProviderLoad (5), successful SignIn (1), Pi probe cases (2), Shell stop/timeout cases (5) | 0/50 → 0/50, each; Linux setup fails before behavior |

The actual old/new standalone writer source was additionally compiled into a separate executable with a harness. The harness retains a named-mutex handle while an owner thread dies, invokes the actual writer, verifies the complete record, and requires a different follower thread to acquire the released mutex. In live-owner mode it first observes a blocked writer and verifies that publication has not occurred, then releases the owner and requires completion. This is fixture verification, not substituted native provider/Pi behavior.

| Standalone writer case | Before → after |
| --- | --- |
| Provider abandonment | 0/50 → 50/50 |
| Pi abandonment | 0/50 → 50/50 |
| Provider live contention and release | 50/50 → 50/50 |
| Pi live contention and release | 50/50 → 50/50 |

The repaired solution builds with zero warnings/errors. All twelve project traceability checks pass. New tests carry `Rule=R-TEST-5`; the imported REVIEW traits are retained. Independent repair review confirms specific acquisition catches, preserved release, the restored exact two-timer proof, and unchanged REVIEW source. Its source SHA256 is `6cf633def7e462a823c4a51adb094f9a1497cbf35f609ff15cd56ce8c55aa0ad`.

## Windows work still required

- Run both compiled fixture writers and all three reader paths on Windows, including owner death, live contention, complete publication and ownership release. Linux's standalone writer harness uses .NET 10; the existing Windows fixtures use .NET Framework csc.exe and Windows mutex/file-sharing semantics.
- Run all five changed Shell stop/timeout cases, especially `Cancelling_a_child_that_ignores_the_stop_request_kills_it_one_second_later_and_throws`, to verify the restored `[1 s, 120 s]` assertion with the real child.
- Run ThreadRuntimeProviderLoad, successful ProviderRegistrySignIn, both Pi cases, four Git merge/retry cases, and AppModel's usage-limit case. Linux blockers remain Windows-backslash fixture lookup/.NET Framework compilation, kernel32.dll, icu.dll, Windows PowerShell, and case-insensitive duplicate proxy environment keys.
- Reproduce Composer, chime and orb's reported load failures before treating their fixes as conclusive, then run fifty loaded attempts before/after with all original assertions.

`run-windows.ps1` builds normally beneath the checkout (required for fixture source lookup) and runs all original selected cases plus the six review/reader cases fifty times under configurable CPU load. It records failures without retrying or skipping and returns failure if any attempt fails. Use it in separate before/after Windows checkouts; a clean Windows environment and throwaway `MARROW_PORT_DATA_DIR` are required. It was prepared on Linux and has **not** been executed on Windows. Native fixture owner-death writer checks remain a separate Windows requirement.

## Evidence

`counts.json` contains exact case names and both outcome counts. `runs.zip` preserves TRX/console logs, build and traceability outputs, actual runners, writer source/harness/results, source and binary hashes, and verification-entrypoint output. `tested-source-sha256.json` binds repaired source; the archive also binds the baseline source plus the test-first additions. `engineering-prefix.json` proves the entire review-head engineering log remains unchanged before its appended repair note.

The unmodified `node porting/tools/verify.mjs --json` still reports NOT ACCEPTABLE for missing pinned reference, existing production stubs/unimplemented gates, absent requirement-verification evidence and its Linux build without the Windows-targeting opt-in. These existing blockers are retained, not bypassed. The initial FLAKE-1 evidence remains historical evidence for `56a959fb9e18f44c1cd63b9b045d129bacea5800`; this follow-up corrects its missing-assertion and abandoned-lock findings.
