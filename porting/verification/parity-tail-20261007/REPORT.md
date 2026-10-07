# T22 remaining parity records — 2026-10-07

Verdict: **NOT READY for Windows acceptance**. All four records were already implemented at the requested base. This change adds nine source-derived proof cases and closes stale implementation statuses; no production code or existing test was changed. No composer, EffortSlider, LiquidTrack or LiquidStrike record/file was changed. IMPLEMENTED does not mean VERIFIED.

Base `1291606cbac734da0b2afe125ad9c01227eed83b` from `origin/claude/verify-terminal-20261001`; test commit `5e5df33078d112af6eb9ab029643e8d4b213fe24` on `codex/parity-tail`. Accepted production tree `7e9a2d1fe92de8eee3c6c861172f3033484a5e68`; test tree `decc125760c68e0b4345da0a1d98850b5ef05255`. Build and raw test receipts are retained as lossless gzip; uncompressed hashes are in receipts.json. Earlier failed fixture attempts and the transient parallel-build copy retry remain in the receipts; final builds/runs below were sequential.

## Record evidence

| Record | What was missing | Source and existing implementation | Added and existing proof |
| --- | --- | --- | --- |
| PR-LAUNCH-071 | Ledger closure and clean-library/preparation regression evidence; no missing runtime behavior | Pin AppDelegate.swift:22-33, AppModel.swift:121-147,240-287; port src/Marrow.App/AppDelegate.cs:49-100 and Views/RootView.cs | ParityTailLaunchTests (3 cases): prepared unseen/seen tour, exact Welcome structure, no additional setup card, consent/download state, appearance does not persist hasSeenTour, and unprepared sheet gating. |
| PR-SETTINGS-249 | Ledger closure and About/corrupt-document regressions; no missing runtime behavior | Pin SettingsView.swift:1007-1057; port Views/Settings/SettingsView.cs:1068-1114; Marrow.App.csproj legal Content | ParityTailLicensesTests (4 cases): exact row/Show, one bottom 480x440 presentation, titles/order/18 and 8 spacing/13pt semibold/12pt selectable markdown, each corrupt UTF-8 document falls back independently. Existing SettingsViewTests.Licences_are_read_once and LicensesShippingTests cover cache, missing files and shipped build bytes. |
| UI-SIDEBAR-031 | Ledger closure and small-focus/monitor-detach regression; no missing runtime behavior | Pin SidebarView.swift:904-964,267-291; port SidebarView.cs:1532-1608,1628-1780 | ParityTailArchiveTests plus existing SidebarViewTests prove small metrics, tooltips/default focus/no ring, Escape on anchor and detached monitor. Existing tests cover 272 width, 9/6 spacing, padding, quoted semibold title, two-line truncation and anchor-window/modifier/Return/keypad behavior. |
| UI-SIDEBAR-032 | Ledger closure and deferred-delete/worktree-file regression; no missing runtime behavior | Pin SidebarView.swift:927-940,276-282,295-306; same port code | New Delete proof invokes real content handler and simulates the two Popup.Closed operations; asserts no deletion until dismissal and the next main turn, RemoveWorktree false, and real file preservation. Existing Archive/Delete test remains unchanged and fails on Linux user32.dll. |

Existing accepted adaptations remain: OD-0012 F1 branding; OD-0005 Windows material; OD-0009/ED-059 typography; ED-280/OD-0098 small controls. Launch uses OD-0018/ED-103 (delay starts after main appearance), OD-0096/OD-0098 (preparation and voice consent), OD-0113 (production preparation behind Boot B until reveal). No new adaptation was authorized or invented. The old STOP #7 comment in AppDelegateProofTests.cs:280 predates these implementations and remains untouched under the prohibition on changing existing tests.

The pinned source was cloned read-only at `52bc7926097cd622d08d6294e0d8b08a48c7516e`, tree `115745914ccc103dfe4e74d96c6c539faab2eedb`. reference-integrity.json proves a clean checkout and exact 441-path inventory: 89 digest matches directly, 352 match the inventory's Windows CRLF checkout transformation, zero other mismatches. No inventory normalization or reference edits. check_protected.mjs intentionally reports the three owner-authorized ledger/ED edits as protected; its raw failure is retained. This primary/orchestrator task has explicit user authorization to close these records and append decisions. No guard was edited or bypassed. The installed Gate A tool reports 5 failures/7 passes because its persisted reference path is E:/Droppy/reference/droppy-code-v1.7.0; manual reconciliation supplements but does not relabel that failed run. Gate B passes 8/8 before and after closure (parity-tail-gate-b-final.log.gz).

`ledger_apply.mjs` advances reading inventories from reader reports; it does not perform these implementation closures. Only the four JSON record spans and parity counts are edited, preserving all unrelated bytes. ED-387/2/3 and STATE are byte-exact appends.

## Validation and limits

- Final App test rebuild: zero warnings/errors (`parity-tail-build-final.log.gz`); rebuild only test project, production dependencies built earlier.
- New focused tests with supplemental Linux ICU76: 9 passed, 0 failed, 0 skipped; five repeat runs each 9/9. These repeats are focused runs, **not** the whole-suite five-run concurrency gate required by IMPLEMENTING.md:1335-1336.
- Same final nine tests without ICU forwarding: 7 passed, 2 failed, 0 skipped (Tour static initialization needs Windows ICU). Earlier unadapted attempts remain retained.
- Affected headless/license shipping tests with Linux ICU76: 24 passed, 1 failed, 0 skipped. Unchanged SidebarViewTests.The_archive_popovers_buttons_archive_under_panelSlide_and_delete_without_the_worktree fails on user32.dll through Chrome.PanelSlide. No assertion/tolerance/time bound was weakened.
- Requested `FullyQualifiedName!~Native`: Core 2844 passed / 344 failed / 0 skipped, **aborted** on unhandled kernel32.dll; Services 3856 passed / 1266 failed / 52 skipped, completed; UI 2047 passed / 298 failed / 0 skipped executed, **aborted** after lead terminated a stalled Linux testhost after about ten minutes. These are failures/partial results, not acceptance totals. Many failures involve Windows ICU/Win32/process/path APIs; no blanket platform exclusion beyond the requested filter was added.

Commands (from /workspace/marrow-port; SDK executable /workspace/scratch/dotnet/dotnet; data root MARROW_PORT_DATA_DIR=/workspace/scratch/parity-tail-data):

```sh
dotnet test tests/Marrow.Core.Tests/Marrow.Core.Tests.csproj --filter 'FullyQualifiedName!~Native' -p:EnableWindowsTargeting=true
dotnet test tests/Marrow.Services.Tests/Marrow.Services.Tests.csproj --filter 'FullyQualifiedName!~Native' -p:EnableWindowsTargeting=true
dotnet test tests/Marrow.UI.Tests/Marrow.UI.Tests.csproj --filter 'FullyQualifiedName!~Native' -p:EnableWindowsTargeting=true
dotnet build tests/Marrow.App.Tests/Marrow.App.Tests.csproj --no-restore -t:Rebuild -p:BuildProjectReferences=false -p:EnableWindowsTargeting=true
dotnet test tests/Marrow.App.Tests/Marrow.App.Tests.csproj --no-build --no-restore --filter 'FullyQualifiedName~ParityTail'
node porting/tools/verify.mjs --gate B
```

For supplemental runs only, LD_LIBRARY_PATH=/workspace/scratch/parity-tail-icu forwards 1,754 unversioned ICU symbols to the real Linux ICU76 libraries. `icu76-aliases.S.gz` retains the entire generated assembly; each export is a jump to its `_76` counterpart, no fake returns. Compile after decompressing with:

```sh
gcc -shared -fPIC -Wl,-z,defs -o icu.dll aliases.S /lib/x86_64-linux-gnu/libicuuc.so.76 /lib/x86_64-linux-gnu/libicui18n.so.76
```

This scratch-only support was never copied into repository/build/publish inputs and does not prove Windows icu.dll semantics. Launch fixtures use existing offline bootstrap/boot/cursor/motion/hardware seams; they do not exercise real provider probes, GPU warm-up, microphone/downloads or Boot B HWND cloaking. The archive Closed proof simulates teardown; native popup routing/placement/material must be tested on Windows. LicensesShippingTests proves project/build copy bytes; LicensesPackTests was not selected by the affected filter and would perform no package assertions when artifacts/releases is absent, so installer/update-package inclusion is **unverified**.

Windows still needs: all requested non-Native suites (and whole-suite repeats), unchanged affected Archive action test, real clean-profile boot/preparation/tour/provider behavior, native popup positioning/focus/key routing and material, source/Windows capture comparison at 100/125/150/200% scale, and installed/update-package license/notices/trademark bytes and cache/fallback. Independent source reviewer accepts only IMPLEMENTED, did not rerun the lead's receipts, and preserves these limits (independent-review.md).
