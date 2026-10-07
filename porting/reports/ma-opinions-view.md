# MA-3b-7 opinions view — NOT READY

Branch: `codex/ma-opinions-view`. Setup merged wave2 into wave1 with merge commit `c3599547`. Base parents: wave1 `ecbac610`, wave2 `5144f5f3`. No force push, no other branch push.

Implementation is complete and passes its Linux headless checks. Release parity is **NOT READY**: native Windows captures/runtime checks and production W binding are unverified. No visual parity, native cadence or real engine execution is claimed.

## Sources and boundaries

- Locked design: `porting/first-release/design-src/multi-account/multi-account.html`, #opinions :4795–4871 plus final CSS overrides, account palette, Scene.add spring and Reduce Motion rules. SHA-256 `909bee162d1d794cb704c50bf2712735768b70eedd3c33d570095e8ff59eddb8`.
- Checklist item 5: `porting/first-release/multi-account-CHECKLIST.md`, SHA-256 `2eddbd5085042555d4b4415d34ba0d7c34c963c2cbf46517d76ea4c22487afee`.
- Owner law: OD-0114 through OD-0126 from `git show origin/claude/verify-terminal-20261001:porting/owner_decisions.md`, ref `285b6b7a99b39e0e517f34c8d34a1860c945f83f`. Opinions=B under OD-0115. Cross-service confirmation remains in wave1's Ask-others view under OD-0120.
- Inspected E1 `8303f39722a1d9ec6966d83c275fc579c3b219be` for interface expectations; no engine merge or engine edits.
- New files only under `src/Marrow.App/Port/MultiAccount/Opinions/` and two new test files. One added source seam line in `ThreadTimeline.cs`. No Core model, Ask-others view, popover, usage panel, cards 11–14, Handshake view or existing test edits. Engineering ledger is byte-preserving append only.

## Delivered behavior

Stable per-round, per-account cards preserve engine order, lead first as Answer, others as Opinion. The grid uses equal widths with 200 px minimum, 8 px gap; card padding 10/11 px, 12 px radius, text 12/17 px, exact heading/mono sizes. Complete backtick spans stream as monospace and unfinished spans remain literal. Changes to one head preserve the other card controls and text. Account palettes and snapshot names are retained.

Combine appears after every head finishes with nonempty text and no projected error. It uses the locked 26 px chrome, glass, rims, shadows, glyph and state curves. The control disables before awaiting the port and calls CombineAsync at most once, including pending clicks or a failed call. A failed call shows the error without claiming completion or allowing a potentially duplicate post. E1 owns actual synthesis/posting.

Combined byline text is supplied by the engine projection, with a fake-compatible fallback such as `Claude Max 1 · combined 4 answers`. Closing note is `That was for this message only · the thread carries on with Deep work, step 1, Claude Max 1` for that continuation, with dynamic pill/step/account values. Both are docked under the explicitly associated combined assistant row, before later messages. Cards are docked under the explicitly associated source user row. Existing selectable message text and menus remain the same controls. Hidden/unmounted entries undock their injected controls.

Normal arrival follows the page's sampled spring(200,22). Reduce Motion is the page's 260 ms CSS ease opacity fade with no transform, including enabling it while Combine is held. Streams do not restart arrival. Combine has its own arrival after all heads complete; reparenting does not cancel the first visible arrival. Notes resolve theme from their own mounted tree.

## W integration required

Job-0 IOpinionsPort exposes rounds/text/Done/Combined, but no timeline-entry IDs or errors. Install both `ThreadPortSeams.Offer` (IOpinionsPort plus account ports) and `OpinionTimelineSeams.Offer` before constructing the timeline. The latter needs:

1. PromptEntry: round -> exact sent TimelineEntry.Id, recorded from AskOthersView.Sent.
2. CombinedEntry: round -> exact appended TimelineEntry.Id, recorded by E1/W PostCombined.
3. HeadError: E1 head error projection; failed partial text must not enable Combine.
4. AccountsAtAsk: historical account metadata snapshot in the round's canonical order.
5. PostedByline: exact engine-posted historical byline, including pre-rename source name.

Persist/recover these associations with the round history. Never associate by prompt text: identical prompts are distinct messages. An unbound projection draws no opinion UI. Tests bind the job-0 fake using lead-first, engine-shaped rounds; the existing fake is unchanged. No actual CLI/model/network requests were made.

## Validation

.NET SDK 10.0.401 installed outside the repo at `/workspace/.dotnet`. Linux App targeting uses `-p:EnableWindowsTargeting=true`; App fixtures use throwaway `MARROW_PORT_DATA_DIR=/tmp/ma-opinions-data`. The existing real-ICU forwarding bridge was compiled from `tests/fixtures/handoff/icu-linux-bridge.c` to `/tmp/ma-opinions-native/icu.dll` and loaded via LD_LIBRARY_PATH. No Windows native calls were faked. The bridge has a limited export set.

| Check | Result |
| --- | --- |
| Test-first new API compile red | Missing OpinionAnswerRows API, retained log |
| Per-message placement API red | Missing placement API; log also records a subsequently corrected new-fixture typo |
| Core `FullyQualifiedName~Port.MultiAccount` | 220 passed, 0 failed/skipped |
| Final App `FullyQualifiedName~Views.MultiAccount` | 63 passed, 0 failed/skipped, including all 11 new cases |
| App `Views.MultiAccount OR Port.MultiAccount OR ThreadTimeline` | 122 passed, 133 failed, 0 skipped; all 11 new cases passed |
| Full Core with existing ICU bridge | 2,988 passed, 268 failed, then aborted on missing kernel32.dll in ProcessLauncherSpawnTests; incomplete suite |
| Independent source review | No outstanding actionable findings; no independent execution or Windows captures |

Broader App failures: 83 missing `uloc_forLanguageTag` ICU bridge export, 49 missing user32.dll, one native fixture requires interactive Windows x64. Full Core failures include missing Windows APIs/executables, limited ICU exports, case-insensitive environment collision for HTTPS_PROXY, and Windows path/error expectations. These results do not establish full-suite success. The initial full Core attempt without the bridge is also retained; it aborted with 4,283 passes and 563 failures. Existing assertions/tolerances were not changed.

Representative commands (run from repo root with the environment above):

```sh
/workspace/.dotnet/dotnet test tests/Marrow.Core.Tests/Marrow.Core.Tests.csproj --no-build --filter FullyQualifiedName~Port.MultiAccount
/workspace/.dotnet/dotnet test tests/Marrow.Core.Tests/Marrow.Core.Tests.csproj --no-build
/workspace/.dotnet/dotnet test tests/Marrow.App.Tests/Marrow.App.Tests.csproj -p:EnableWindowsTargeting=true --filter 'FullyQualifiedName~Views.MultiAccount|FullyQualifiedName~Port.MultiAccount|FullyQualifiedName~ThreadTimeline'
/workspace/.dotnet/dotnet test tests/Marrow.App.Tests/Marrow.App.Tests.csproj -p:EnableWindowsTargeting=true --no-build --filter FullyQualifiedName~Views.MultiAccount
```

Raw logs/TRX (large reports gzip-compressed), the exact failed-test rerun list and reviewer scope are in `porting/evidence/ma-opinions-view/`. A preliminary Gate D invocation failed its generic build/requirement checks; it does not represent the successful targeted App build above. Gate D/F completion, requirement verification and visual parity are not claimed. `mark.mjs MA-3b-7 DONE` found no MA-3b-7 queue entry, made no queue change; this report is the task handoff.

## Windows run list

- Repeat OpinionAnswerRowsTests, OpinionTimelinePlacementTests, all Views.MultiAccount, broader ThreadTimeline tests, and full Core on Windows .NET 10 with no bridge. `platform-rerun-tests.txt` names every failed test from the Linux attempts.
- Run existing `ThreadTimelineLiveResizeNativeTests` on an interactive Windows x64 desktop, plus timeline native report/popover/review-key checks relevant to the injected rows.
- Capture locked #opinions B and this view at light/dark, 100%/150% scaling: equal grid widths and wrapping, all account marks, Answer/Opinion labels, mono code, streaming cards and exact text/baselines. Include narrow and multi-column layouts.
- Compare Combine idle/hover/held/keyboard-focus/disabled: glass blur(14px)/saturate(160%), inset rim/highlight order, drop shadows, focus ring, 26 px height, .97 pressed scale, .86 pressed opacity, .5 used opacity and exact transition curves. Native snapshot/partial-redraw behavior remains unverified.
- Compare normal spring and 260 ms Reduce Motion opacity-only arrival, independent Combine and closing-note arrival, live Reduce Motion while held, visual cadence and clipping.
- Verify combined byline dot/11 px text, exact closing pill/checkmark/body/emphasis, independent dark/light theme changes with the prompt hidden, and ordering before later messages after scrolling/history redraw.
- Bind E1/W projections and verify production lead-first rounds, parallel streaming, error/cancellation readiness, exact source/combined entry associations, historical account rename/deletion, posted byline persistence and restart. Confirm one port call produces one actual combined assistant message. This binding is a required follow-on, not implemented in the owned files here.

No design deviation is approved or introduced as a substitute for these pending checks. Verdict: **NOT READY**.
