# Independent review: FLAKE-1 (`codex/flake-1`, head 56a959fb9)

Reviewer: Claude (Linux, .NET SDK 10.0.112, `-p:EnableWindowsTargeting=true`, `FullyQualifiedName!~Native`).
Diff read: `889c9ebd..56a959fb` in full (28 files; production code untouched, confirmed from the stat). ED-371 and `porting/evidence/flake-1/README.md` read.

## Verdict: FIX FIRST

One real defect in the fixture synchronisation (finding 1, two failing REVIEW tests). No assertion that was loosened, no retry, no skip, no raised timeout, and no production change. The fix is small.

## Findings

1. **DEFECT (blocking, small): the named publication mutex is now taken in every mode and by every reader, and none of them tolerates an abandoned mutex.**
   `ProviderProbeStandIn.Main`, `PiStandIn.Write`, `ProviderProbeScript.RecordedCalls` (both test projects) and `PiScript.Read` call `WaitOne()` bare. A stand-in killed while inside the critical section (the Shell, cancellation and timeout tests all kill children) leaves the mutex abandoned. The next `WaitOne` throws `AbandonedMutexException`: the next stand-in launch crashes before recording its argv, or the test reader throws. Before this branch the lock was held only in `wait-for-release` mode; the branch widens the exposure to every provider and Pi test, which is the failure class FLAKE-1 is meant to remove. Fix: catch `AbandonedMutexException` at each `WaitOne` and continue (ownership is acquired). Tests, both failing today with `AbandonedMutexException`:
   `REVIEW_FixtureLockTests.REVIEW_reading_provider_probe_records_survives_a_stand_in_that_died_holding_the_lock` and `...REVIEW_reading_pi_records_survives_a_stand_in_that_died_holding_the_lock` (`tests/Marrow.Services.Tests/Fixtures/REVIEW_FixtureLockTests.cs`; they reproduce the reader side; the stand-in side is the same call pattern).

2. **Minor weakening (non-blocking, not given a test): `Cancelling_a_child_that_ignores_the_stop_request_kills_it_one_second_later_and_throws`.**
   Old: `ActiveTimers == 2` after `Cancel()`, proving the 120 s deadline sleep is still pending next to the 1 s kill timer (the in-code comment says the kill is a *second* timer). New: only `Contains(1 s)`. The deadline timer's presence is no longer asserted. Suggest `Contains(120 s) && Contains(1 s)`. A REVIEW test would be Windows-only (PowerShell, kernel32) and cannot run here, so none was written.

3. **Process note (non-blocking):** six of the nine groups (ThreadRuntimeProviderLoad, signed-in ProviderRegistrySignIn, Shell, Git, Pi, the AppModel usage-limit case) cannot run on Linux (Windows csc, kernel32/icu, PowerShell). I reproduced this: they fail at setup before any behavioural assertion, so for them the "0/50 before and 0/50 after" is only static evidence. Composer, chime and orb were never reproduced as flaky, so their causes remain hypotheses (the evidence README says so honestly). The ED block's own status is NOT READY. A Windows run of those groups is required before the flake is called fixed.

## Per changed test: what it proved before / after

| Test | Before | After | Verdict |
| --- | --- | --- | --- |
| WallpaperCard `An_indeterminate_progress_indicator...` | Installing state visible, progress shown, Choose disabled, then completes; relied on a 512 px image being slow enough. | Same assertions; a `MainExecutorGate` holds the background-to-main completion until `Release()`. Main-thread posts still pass so the view updates. Passes here. | Same proof, now deterministic. The state is held rather than raced. No fake hides the behaviour: the real install runs, only the completion hop is delayed. |
| ComposerWorkingDotsEase (8 cases) | Render tick + capture, then exact 0.32 s expectations. | Same, plus joining the compositor batch `Rendered`; the animation clock is not advanced. 28/28 of the three App classes I ran pass. | Proves the same or more. |
| AppModelTurnFinished (17 cases) | Chime/banner gates. | Same, in a `DisableParallelization` collection so the static FinishChime cache is not shared by concurrent manual main actors. | Same proof. Cause unproven (README admits). |
| ProviderProbe stand-ins and RecordedCalls (App and Services) | Lock only in `wait-for-release` mode. | Lock in all modes and readers. | Fixture race real (16-launch experiment). Finding 1 applies. |
| ProviderRegistrySignIn successful login | Count of two `app-server` launches, `Task.Delay(300)`; stand-in exited mid-initialize. | Offline JSON-RPC stand-in, waits for `LoadingLimits` to clear and the two launches on the injected loop, still asserts 2 launches, login/--version/login status calls, SignedIn, `SigningIn` null; **adds** the `offline-model` assertion. `SignIn` awaits `LoadCatalog`, so that assertion does not race. | Proves more. The limits read now succeeds rather than fails, a fidelity improvement, not a hidden behaviour. |
| Shell (5 cases) | Poll for armed file, `ActiveTimers == 1/2`. | `WaitForArmed` (fails fast on a launch exception, still asserts the file), exact deadline `Contains(120/121/1/7 s)`; every exact-boundary advance and termination assertion retained. | Equal or stronger except finding 2. |
| Git retry (4 cases) | Count, executable not running, one timer, stable 200 ms. | Count and the exact retry deadline `elapsed + delay` (3, 5, 7, 9, 11, 13 s); early-retry and cancel assertions retained. | Same proof, more precise; the retry timer exists only after the process has returned. |
| Pi silent timeout | Two stdin lines. | Two stdin lines **and** the 30 s timer registered; then 29,999 + 1 ms exact. | Stronger. |
| Orb allocation (4 states) | 20 warmup and 200 measured frames, zero bytes, on the runner thread, `job.FrameState` re-evaluated in the loop. | Same 20/200, exactly zero, on a dedicated thread; `FrameState` is a `readonly record struct`, so snapshotting it hides no allocation. | Same proof. |
| 4 new helper tests | n/a | Gate order/ambient/release, distinct deadlines, probe counts allocations. All pass. | Good; carry Rule R-TEST-5. |

No production change was made, so there is no production race fix to test; the ED correctly claims none confirmed.

## Run here

- Build of Core, Services, UI and App test projects: succeeded, 0 errors.
- Passing: 4 new helper tests; `OrbRendererTests` (22); Composer Ease, chime/banner (27/28) and the wallpaper progress case; Services traceability checks.
- Failing for environment reasons identical to the README (not branch faults): Shell and Git (kernel32, PowerShell), Pi (csc), AppModel usage-limit (`icu.dll`), wallpaper cases that decode pictures.
- Failing by design: the two REVIEW tests (finding 1).
