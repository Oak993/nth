# T25 W finish-up: production Link and opinion view bindings

**READY for T9's Windows run of W and opinions together.** Production binding work is complete. This is admission to the Windows run, not a full-suite or release-parity verdict: the broad Linux suites abort on platform dependencies and two incoming T31 traceability checks remain red.

## Integration and bindings

Merged terminal `228f56b55` into W. Opinions `18dcbc9f3` was already an ancestor of terminal, so its merge reported already up to date. Terminal explicitly held both deliveries by reverting their source; this branch restores those held source changes with three-way application, retaining the terminal's later engine/design fixes. Existing W and T31 tests are restored unchanged.

`ThreadPortSeams.Bind`, called at production startup, now supplies `LinkComposerSeams`, `AskOthersComposerSeams` and `OpinionTimelineSeams` from the same app-owned `LiveThreadPorts` as account, card and usage ports. No production port remains on a fake. Null ambient defaults remain for isolated, uncomposed view tests.

The Link factory supplies the current/all-service live account ports and the real `LinkEngine`. The production `LinkComposerHost` mounts `LinkedAccountCapsule` and the existing popup's `LinkPopoverColumns`. Models resume subscriptions on attachment, suspend on detach, and are suspended on thread retirement and app shutdown. Weak model ownership avoids retaining discarded composers. UI consent uses the app's settings, shared between its models; no permission or switching default is enabled.

The composer routes selected sends to the real opinions engine. It records a unique, actual user timeline entry before the engine captures its conversation revision, associates the returned round before its first notification, and rolls the provisional entry back if preparation is declined/rejected. Successful submission clears only the draft that was actually sent. The real Combine post callback records its appended assistant entry ID and posted byline. Context delegates project real head errors and immutable account snapshots, including source composer settings and other heads' account defaults. Identical prompt texts never determine associations. Snapshots survive account rename/removal and view remount for the app-owned engine's round lifetime; E1 rounds themselves remain session-local. Posted timeline messages retain their existing persisted text/byline format.

OD-0116/0119's first-time Ask engine acceptance and the thread pin/card/usage wiring from W remain restored. OD-0120's UI checkbox starts unchecked and the E1 transfer guard remains in force; UI acceptance does not bypass that guard. OD-0122's existing shared in-sync tick is retained. An existing chip separator test exposed a legacy single-byte middle dot in restored production source; it now uses a Unicode escape, with the assertion unchanged.

## Commits

- `a13922253`: merge terminal engine/design waves, append-only ledger reconciliation.
- `320282adf`: restore held W and opinions source/tests/evidence.
- `19d9ffe3d`: two failing composed-app seam tests.
- `1c45aea9c`: production model/timeline/send/lifetime bindings and seven new functional proofs.
- The documentation commit containing this report appends `ED-NEW-ma-wire-finish-1` and the STATE checkpoint, and preserves the final byte-distinct terminal ledger preamble.

Both ledgers keep the original W bytes as an exact prefix and every incoming terminal block byte-exact. No old ledger bytes are rewritten; reconciliation is entirely append-only, including mixed CRLF/LF. Evidence includes the preservation check. Both requested branch tips are ancestors of this delivery.

## Tests

.NET SDK 10.0.401 / runtime 10.0.12. App/Services use `-p:EnableWindowsTargeting=true`; all data roots are throwaway `MARROW_PORT_DATA_DIR` directories. Added tests decorate only conversation context/provider dispatch with an offline recorder: identity, revision, consent and posting still use real app boundaries. Usage reads are overridden offline. No real network, provider CLI or model is used by the added tests. No existing test, assertion, reference or tolerance was edited.

| Run | Result |
| --- | --- |
| Test-first composition offers, before implementation | 0 passed, 2 failed on null offers |
| App binding/Link/opinions/tick/card regression plus traceability | 45 passed, 2 failed: all 44 functional tests pass, including 7 new proofs; 1 traceability check passes |
| Core `Accounts`, `Port.MultiAccount`, traceability | 519 passed |
| Services `Port.MultiAccount`, traceability | 174 passed, 2 Windows process/Git fixture failures |
| Services portable subset (excludes `HandshakeSummaryAdapterTests`) | 173 passed |
| Full Core `FullyQualifiedName!~Native` | Aborted: 635 passed, 24 failed before a kernel32 process-launch crash |
| Full Services `FullyQualifiedName!~Native` | Aborted: 4,122 passed, 1,212 failed, 52 skipped before a Windows watcher host crash |

App timeline regression uses the checked-in `tests/fixtures/handoff/icu-linux-bridge.c`, compiled outside the repository, forwarding to real system ICU 76. It supplies no fake Windows APIs. Broad failures include missing Windows APIs/ICU, Windows path/environment expectations and platform-dependent loopback/watcher checks; they do not establish full-suite success.

The two App traceability failures are **platform-independent, inherited T31 test-layout failures**, from the existing nested `HeldCombine` in `OpinionAnswerRowsTests.cs:150`: `Test_classes_and_Req_tests_have_the_shape_req_verify_can_read` and `Test_source_has_one_class_per_file_single_line_attributes_and_no_stray_type_keywords`. That test file is byte-identical to `origin/codex/ma-opinions-view`; this job leaves existing tests unchanged. They require test-owner follow-up and will also fail on Windows as currently written.

Compressed logs/TRX, exact failed-test names, ledger checks and SHA256SUMS are in `porting/evidence/ma-wire-finish/`.

## T9 Windows run list

Run W and opinions together on Windows .NET 10, without the ICU bridge:

1. Full Core and Services with `--filter "FullyQualifiedName!~Native"`.
2. App `Port.MultiAccount`, `Views.MultiAccount`, `Views.Composer.MultiAccount`, and `ThreadTimeline` suites. In particular, the Windows-dependent working-turn cases in `LiveThreadPortsTests`, `LiveAskEachTimeTests`, `LiveCapsuleCompletionTests`, plus `OpinionAnswerRowsTests`, `OpinionTimelinePlacementTests`, and all seven new binding/lifetime/send proofs.
3. Interactive native `ThreadTimelineLiveResizeNativeTests` and relevant native timeline, popup, review-key and routing/report cases. Exact failures from the Linux attempts are listed in the evidence; the T31 evidence also retains its broader native rerun list.
4. Native visual/interaction checks at 100% and 150%, light/dark: Link popup columns and capsule flight/unlink, actual checked/unchecked cross-service confirmation, opinion streaming/error cards, Combine placement/byline/closing note before later messages, and the shared Claude/Codex/Pi readiness tick. Existing tests remain offline; no live model run is claimed.

The two inherited traceability failures above are separate from the Windows-dependent list and must not be described as Linux-only failures.
