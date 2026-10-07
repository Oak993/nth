# T40 design gaps — RR-37 / RR-48

2026-10-07. Branch `codex/design-gaps`, based on `origin/claude/verify-terminal-20261001` (`285b6b7a`). Verdict: **NOT READY**. The Linux-verifiable implementation is complete; native acceptance and a frozen-test conflict remain open.

Audit authority: `origin/codex/release-audit` at `6b2e699407c9a6ef596887ca9dd335b56b2744f5`, `porting/first-release/reviews/release-readiness.md`, RR-37 and RR-48. Locked design mirror read with `git show` from `origin/claude/design-sources` at `26f8fbe89ee8480c9b18b4afe16d6c57bccb3610`; the mirror was not edited. Getting Ready: `work-design/decisions-2026-10-03/getting-ready/getting-ready.html` and `project/VariantI.dc.html`. Settings: `work-design/settings-open-rise-2-locked.dc.html`, `settings-rise/SPEC.md` and its tools. Historical engineering commentary was not treated as an approved deviation. Owner decisions and all original ledger bytes remain intact.

| Item | Status | Evidence / remaining work |
| --- | --- | --- |
| RR-48 manual files: one opening, verified bytes, writer turn and publication accounting | DONE | Three new regressions failed before repair. Hash and stage the same bytes; flush, take Downloader's PartFileTurns, publish, then count. Failed publish cleans staging and refetches. Existing manual/controller/seam tests pass. Commit `4cc82dc7`, ED-NEW-design-gaps-1. |
| RR-48 imported metadata / slow drives / generation lock | DONE | UI List and duplicate questions read metadata only. Refresh runs on workers; foreign copy/probe and linked snapshots read outside the metadata gate, and snapshots are captured outside the generation coordinator gate. Concurrent enable/remove state is retained. Held-read and coordinator regressions failed before repair and pass now. Commit `26153536`, ED-NEW-design-gaps-2. |
| RR-48 failed Replace cleanup | DONE | Delete the old copy before removing its indexed record; remove a new unpublished copy when replacement fails. Injected and real OS permission-failure regressions pass; the real filesystem test failed against the original code. Commit `26153536`. Windows held-handle/ACL checks remain on the native run list. |
| Manual Check Again offload | ALREADY DONE | Baseline controller already runs it on a worker; unchanged LibraryControllerManual/W3 manual placement tests pass. No speculative controller rewrite. |
| RR-37 Getting Ready saturation .6 → 1 | DONE (Linux implementation) | Live Skia subtree filter, CSS matrix, unchanged alpha, one group opacity, partial redraw, sibling/sheet exclusion, restoration and 21 hand-over samples. No raster snapshot/readback/frozen pane. Identity removes the sentinels; normal pane rendering allocates no custom draw operations/offscreen surface. Commit `1dc82b25`, ED-NEW-design-gaps-3. Native cost and full-frame parity still need Windows. |
| RR-37 native Acrylic open fade | BLOCKED on native acceptance; implementation complete | Pinned WinUI blur sprite follows the existing 80 ms curve. An expected native surface must exist and hold its zero-alpha state over a hidden draw before G; late-surface regression proved the old two-frame reveal was premature. Caption glyphs remain whole at G. Native graph/control/ownership tests pass; renderer pixels are not claimed. Commit `e49682ce`, ED-NEW-design-gaps-4. |
| RR-37 160 ms close / centre scale .985 | BLOCKED on native acceptance and frozen-test conflict; implementation complete | Exact curve and duration, common-root alpha and centre scale, card 1's specified extra fade, settled rows, cancellation/restoration and Reduce Motion pass on Linux. Native and managed paths never both fade/scale the same window. Unchanged SettingsRiseTests line 406 requires immediate hide and fails against the locked close. Commit `e49682ce`. |

## Linux validation

SDK .NET 10.0.401, pinned Avalonia 12.1.2. `EnableWindowsTargeting=true`; all application/test data under `/tmp/design-gaps-data`. No installed app, native desktop, real provider or release run.

- Affected Services suites: **157 passed, 3 Windows-only skips, 0 failed**, including manual/controller/import/generation tests.
- UI raster/filter/corner suites: **33 passed, 0 skipped/failed**.
- New App design regressions: **9 passed, 0 skipped/failed**.
- Broader GettingReady/SettingsRise/ImportSkills selection: **63 passed, 28 failed of 91**. Twenty-six failures require `user32.dll`; one scripted Getting Ready preparation failure also failed on the original base; one frozen Settings immediate-hide assertion conflicts with the approved close. Nothing was weakened to make that selection green.
- Solution and native probe builds: **0 warnings, 0 errors**.
- The single `node porting/tools/verify.mjs` entrypoint was run. The full run could not access the pinned `E:/` source and its App/UI test hosts did not finish after 14 minutes; it was stopped, not counted as a pass. Separate Gate A: 7 pass / 5 fail (missing pinned source). Gate B: 8 pass. Gate C: existing stub failure and unimplemented acceptance check. Gates E/F: unimplemented. Build and targeted test evidence do not substitute for those mandatory gates.
- `node porting/tools/mark.mjs T40 DONE` returned `no task T40 in the queue`; no queue mapping/status was invented. Formal T40 marking is BLOCKED.
- `git diff` against `285b6b7a` shows only added test files: **no baseline test, assertion, tolerance or reference changed**. Byte checks preserve the exact original prefixes of engineering_decisions.md and STATE.md; owner_decisions.md is byte-identical.

Raw evidence: `porting/reports/design-gaps/`. Red logs cover manual publication, import metadata/coordinator/cleanup, saturation pixels, missing RootView saturation, missing close/native bridge and premature native readiness. The new manual-clock fixture uses its actual 100 ns input quantization when evaluating the independent curve; no production timing quantization was introduced. An exploratory deferred-hide expectation was rejected after reading pinned Window.Hide: it already hides the HWND before IsVisible notification, so no such refactor was made.

## Needs Windows native check

Run the exact probes and review steps in [the native probe README](../../probes/design-gaps-native/README.md):

```powershell
dotnet run --project porting/probes/design-gaps-native/DesignGapsNative.csproj -- --dark
dotnet run --project porting/probes/design-gaps-native/DesignGapsNative.csproj -- --light
```

The probe uses a fresh scratch/in-memory application environment, the production Settings window, a stripe backdrop, composed-screen captures, native root/blur measurements, held poses and real-time frames. It compiled on Linux; execution there exits with the explicit interactive-Windows requirement. **Native DWM/Acrylic fade-and-scale evidence: needs Windows native check.** The owner instructed that the tunnel will not reconnect and that the lead's native session will run these probes before merge.

Windows run list: native open/close/ring/shadow pixels and frame times; dark/light and 100/125/150/200% DPI on both monitors; repeated shows, close cancellation, external hide and Reduce Motion; full Getting Ready strings/geometry/progress/lift/shadow/live-filter comparison and GPU cost; concurrent large manual file/downloader publication, Windows held handles and replacement cleanup; linked slow/removable drives while using the import page and independent chats; Windows generation ACL/held-handle/junction tests; unchanged App selection and full verifier against the pinned source. Resolve the immediate-hide test conflict before accepting RR-37. Do not infer native acceptance from software-raster or mocked-COM tests.
