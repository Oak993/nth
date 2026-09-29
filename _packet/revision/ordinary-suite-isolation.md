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

The only compiled change from this investigation applies that collection-level exclusion
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
