# MA-2 account usage-limits foundation

Status: **NOT READY**. The new unit and traceability checks pass, but the required broader slices still fail on this Linux executor. Every failing test name also fails on the untouched starting commit; no introduced failing names remain. Windows validation of those paths is required before treating the full slice as ready.

Branch: `codex/ma-2`, from `claude/verify-terminal-20261001` at `7a4cfad3ef57459faf2f593368412fd44d54de06`. Authority: user assignment, OD-0114, multi-account-NOTES.md and multi-account-FEASIBILITY.md section 3. Engineering entry: byte-exact `## ED-406` appended to engineering_decisions.md, preserving all prior bytes. No UI files, project configuration, dependency versions, parity matrix or task queue changed. No PR, installer, native desktop run, actual credential read or live provider call performed.

Implemented account-keyed reads with fail-closed explicit roots and legacy single-account fallback retained; service/window-derived duration metadata and honest absent/unshared status; injected-clock polling with per-account snapshots, 60-second minimum, 3-minute active/1-minute near-threshold cadence, jitter, global concurrency, cancellation, backoff and Retry-After; source-account limit-event attribution and AutoContinue reset/read/resume guards; per-thread/account/window observational rate estimation and owner 95/98-percent no-data defaults. Account-store sign-in/in-use/plan-change composition is exposed through the scheduler; the account UI, full runtime account launch binding and Smart Handshake execution remain later units.

The rate estimator measures allowance changes during caller-supplied thread observations. External clients or simultaneous work sharing that budget can affect a delta; this is not exact per-thread billing or a promise that the next step fits. No 5-hour window is inferred from a plan name. Copilot and Antigravity explicit accounts fail closed because their sign-ins remain OS-vault scoped.

## Validation

.NET SDK 10.0.401 / runtime 10.0.12, Linux x64. Every test filter ends in `&FullyQualifiedName!~Native`; all test data uses throwaway MARROW_PORT_DATA_DIR folders. New HTTP tests use InProcessHttpHandler and synthetic tokens/keys; new scheduling tests use ManualTimeProvider and injected readers. AutoContinue timer regressions replace formatting at an internal test boundary to avoid Windows ICU in those new account tests; production formatting is unchanged.

- `dotnet build Marrow.sln -c Debug --no-restore -p:EnableWindowsTargeting=true -warnaserror`: exit 0, 0 warnings, 0 errors.
- `dotnet test Marrow.sln --no-restore -p:EnableWindowsTargeting=true --filter '(FullyQualifiedName~Traceability|FullyQualifiedName~Port.Usage|FullyQualifiedName~AutoContinueAccount)&FullyQualifiedName!~Native'`: exit 0, **51 passed, 0 failed, 0 skipped**. Core 3, UI 3, Services 34, App 11 = 39 new MA-2 tests plus 12 Traceability checks.
- `dotnet test Marrow.sln --no-build --no-restore -p:EnableWindowsTargeting=true --filter '(FullyQualifiedName~PlanLimits|FullyQualifiedName~UsageLimitSignal|FullyQualifiedName~ZaiAPI|FullyQualifiedName~CodexSession|FullyQualifiedName~AutoContinue|FullyQualifiedName~ThreadRuntimePart3|FullyQualifiedName~Traceability|FullyQualifiedName~Port.Usage)&FullyQualifiedName!~Native'`: exit 1. Core 3 passed; UI 3 passed; Services 285 passed / 190 failed; App 42 passed / 14 failed; 0 skipped.
- Untouched base, same legacy touched/Traceability filter (without the new Port.Usage selector), in detached `/workspace/ma2-base`: Core 3 passed; UI 3 passed; Services 254 passed / 190 failed; App 34 passed / 14 failed; 0 skipped. **All 204 failing names are identical**. Native/test-fixture dependencies remain despite the name filter: Windows `icu.dll`/native APIs, Windows-specific fixture paths and executable/bootstrap assumptions. See baseline-comparison.json for all names.
- Test-first red runs captured missing MA-2 API/types, then behavioral failures for an in-flight trigger deadline, retained service embargo, late-generation embargo and swallowed trap. Independent adversarial review inspected the tests before implementation and the changed source afterward; all its actionable findings were repaired and covered. It did not claim a separate executed Windows test run.
- One new AutoContinue test initially used Claude account keys with AppModelSamples.Thread's default Codex provider; it was corrected to the fixture thread's provider when the new provider-change guard exposed the mismatch. Its same-provider A/B reset expectations were retained. A legacy Command Code equality regression from duration metadata was fixed in production by restricting the new field to account reads, without changing the existing tests.
- `git diff --check`: exit 0. The protected-path checker exits 1 for the lead-authorized engineering_decisions.md append and this report; those are reviewed task evidence, not edits to guard policy. Its reference checks cannot run because the pinned Windows reference directories are absent here. Full parity gates/mark.mjs are not advanced for this new MA foundation unit.

## Evidence

Full logs reside in `/workspace/ma2-evidence/`; the SHA-256 fingerprint of the changed/new C# path-and-content manifest is `3bc48a5d056fc4db0be35ccbc020e38f17bdf1bf95fe9c77392dd76f6dc8001d`. That manifest binds the build/tests to the implementation bytes independently of later documentation/commit metadata.

- `red-services.log`: `a5cae23d66bf3853fc5a6aa84019f138f7fb022b23b8906f9a3cea30a1910d6f`
- `red-app.log`: `cffa5265a59c694fbdef69564dddcdd27fce78dbbd5928c42380a1e8d4b55482`
- `red-review-services.log`: `d3dc8edfdc8354393e5a1256bf9fe9a157dd4404cfbedc52f446ad6dfa1e6d13`
- `red-final-review.log`: `af20bf647f455212fb79ef6ac617609e557bc3c75fb64ceaba39cf7702585c93`
- `delivery-build.log`: `0fedbc3e1050ada92424172b4718851788930294b2cc350d93bf02354c39afb9`
- `verified-foundation.log`: `7b2ff119d264dcf1302bbc849661f51f5b37e996411518116dc062dab8c489f8`
- `base-touched.log`: `7dc37dd84d2ff3371ff0c9e7bf348fd01feea834e882b15553402a8bac56dc0c`
- `delivery-touched.log`: `52e3817fcd522b6504a0bc6811fdc9c644123ce529000f7317639217a8761353`
- `baseline-comparison.json`: `5a08e7f42f6381c6f140b8ae4eebc9fa7071d314850edcd88e3169c95f9bb3ae`
- `source-manifest.txt`: `3bc48a5d056fc4db0be35ccbc020e38f17bdf1bf95fe9c77392dd76f6dc8001d`

Broader test failures require the supported Windows fixture/native environment. No acceptance filter or existing expectation was weakened to hide them.
