# Final ordinary-suite isolation review

The first unfiltered App attempt at `final-app-ordinary-1-2026-09-29T16-16-34-616Z-c69464`
stopped making observable progress. Its owned wrapper and testhost were explicitly stopped
after identity checks. The retained TRX contains 432 Passed results but is an aborted run,
not 432/432 completion. The exact original stalled test was not identified and no hang fix
is claimed. `final-app-hang-abort.json` preserves the process identities and reason.

The separately labelled bounded diagnostic run at
`app-hang-diagnostic-2026-09-29T16-30-22-969Z-cc3d49` used the installed VSTest blame
collector with 150-second per-test timeout and dump type none. All 3633 tests completed:
3626 passed, seven failed. There was no hang timeout or sequence identifying a hung test.
This diagnostic run is not substituted for either requested ordinary App pass.

Every one of its seven failed tests, including each individual Models/Mcp theory case,
then passed twice alone in fresh ordinary environments. All 14 exact selections contain
one Passed result. See `diagnostic-failure-rechecks.json`. None is counted as a repeated
product defect, mutation kill, or proof that the original abort was repaired.

## Independently observed collection interference

The diagnostic datacollector log records `SettingsViewTests.Theme_capsule_has_source_metrics_and_menu_checks_the_selected_theme_and_updates_Nord_detail`
active at 19:31:37.117–.212 local time, overlapping the two AppModelSubagent tests.
The model failures have concrete stacks through:

`AppModelHarness.MakeModel` at `Fixtures/AppModelFixtures.cs:160`,
`AppSettings` constructor at `src/Marrow.App/AppSettings.cs:1118`,
`ThemeManager.Current` at `src/Marrow.UI/Theme/AppTheme.cs:253-260`, and
the live `ThemePickerButton.Update` at `src/Marrow.App/Views/Settings/SettingsView.cs:930`.

One attempted to mutate an Avalonia logical parent from another thread; the other changed
the visual during its render pass. `ThemeManager` is explicitly process-wide and invokes
its event synchronously. The Headless collection previously permitted other collections
to run concurrently. The existing AppSettings collection already documents the same
global-state reason for `DisableParallelization = true` at `AppSettingsFixtures.cs:9-14`.

The first compiled change from this investigation applies that collection-level exclusion
to Headless. It does not change a test body, timeout, assertion, expectation, environment
requirement, native pointer guard or production source. Other collections keep their
four-thread assembly scheduling. This fixes the observed fixture ownership violation,
not a requirement for production to tolerate several synthetic main loops simultaneously.

The two glass-color mismatches are not independently proved to have the same cause.
The two native failures reported an already-held input button/modifier before injection;
both were correctly rejected and both passed twice alone. Those safety guards remain
unchanged. The source-backed collection repair does not claim to fix user/desktop input.

## Diagnostics references

Microsoft's VSTest documentation describes `--blame-hang-dump-type none` as terminating
the timed-out testhost without collecting a memory dump:
https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-test-vstest

xUnit's collection documentation defines `DisableParallelization = true` as excluding
that collection from simultaneous execution with other tests:
https://xunit.net/docs/running-tests-in-parallel

The final ordinary campaign uses normal `dotnet test --no-build` with TRX and detailed
console logging only. It does not use blame, a diagnostic timeout or caller opt-in variables.
Raw output is now written during execution rather than held until completion, so any
future interrupted command retains its observable output. Original evidence bytes remain
unchanged; the interrupted final index is archived before the new full campaign starts.

## Later full-suite recovery

The first Headless-only exclusion did not resolve whole-assembly completion. A second
ordinary App attempt at `final-app-ordinary-1-2026-09-29T16-41-26-033Z-56d6f3` also
stalled and was stopped only after verifying the owned wrapper/testhost identities.
Its partial TRX and `final-app-second-hang-abort.json` remain preserved, not counted as a pass.

The subsequent diagnostic at `app-hang-diagnostic-2026-09-29T16-52-10-244Z-e1b50c`
timed out with four incomplete tests in different model collections. All four passed
twice alone, with exactly one test/data case selected each time. One malformed initial
theory filter selected zero tests; it is retained and excluded, not treated as a pass.
The later self-stack diagnostic at `app-hang-diagnostic-2026-09-29T17-04-44-568Z-2b7f1f`
also timed out, with different incomplete tests. Pending tests are not automatically
the cause of a deadlock and are not reported as individually reproduced failures.

A temporary, explicitly enabled module initializer used the existing Core stack sampler
to capture only its own testhost, without a dump, new package or external process access.
Its sample preceded the eventual stall and therefore does not establish the cause.
The exact source is retained as an uncompiled `.cs.txt` draft; the compiled probe was
removed before the final build and tests. No diagnostic marker is a normal-gate requirement.

The final App assembly policy schedules xUnit collections sequentially, rather than
assuming class/collection separation isolates process-wide ThemeManager, DocumentPrefetch
and DiskWriter state from different manual-loop AppModel fixtures. This is an explicit
test-fixture isolation policy, not a claim that a particular production deadlock was
identified or repaired. Every test still executes, and tests' explicit concurrent jobs,
merge waiters, threads, child processes, native input, assertions and timeouts are unchanged.
The earlier four-collection cap is superseded by this policy and remains in Git history.

The third diagnostic also found an independent `PR-RUNTIME-198` test failure. Unlike the
pending tests, it failed twice alone: expected `Running`, observed `Interrupted` at the
existing assertion. Its fixture created a runtime, immediately rehearsed a turn, then
pumped again to assert before asynchronous history loading had necessarily finished.
The constructor starts `LoadDocument`; `FinishLoad` calls `Install`, which normalizes
Running turns to Interrupted. The fixture now awaits the existing `EnsureLoaded` task
before rehearsal, matching the explicit setup used elsewhere in that test class. All
original assertions remain unchanged, and the exact test passed twice after a clean build.
This is not a WindowManager mutation, and it is not counted toward any of the twelve records.

The repaired mixed ordinary gate passed 266/266. The final unfiltered App runs remain
the acceptance check; the mixed gate and diagnostics do not substitute for them.
Exact pending-test, red/red/green/green and mixed receipts are indexed in
`revision/app-suite-recovery.json`.
