# CZ-7b Windows repair — READY

Tested implementation: `d0f2cdbab48b53b82e790ef0e2cc6e718c4eb709`, branch `codex/cz-7b-fix`, worktree `E:/Droppy/wt/cx/cz-7b-fix`. Delivery SHA is recorded in `E:/Droppy/work/scratch/handoff/codex-cz-7b-fix.md` after the report commit and push.

Base: staging `d919636fa6e9c1341147fcb9560d7f045f8fa114`. Input: `a9d577930039d9f40d58875408e06dc13a763814`. That input is already an ancestor through the previous merge/withdrawal; the requested merge reports already up to date. Commit `079104326` restores exactly withdrawal675b37b54's source/tests/CZ-7b report delta, including ED-583 popover integration and the two ED-584 null-host guards. Every restored file matches the pre-withdrawal candidate byte-for-byte. RR-11 is excluded.

## Repair and scoped exception

Commit `d0f2cdbab` separately repairs the approved test setup. CustomizationPageRig converted every 1/60s double step separately to integer ticks but subtracted the untruncated double: the requested stop boundary ended28.7us early, and Landing31.9us early. The fixture now converts the whole request once and conserves its integer ticks while advancing Main and Frames together. Exact production timers are unchanged.

The ED-292/CZ-T96 D test formerly chose the detached, nonfocusable PreviewStatePick container. Locked CZ-4.7.8 tests the Spoken language pick that fires langMode. It now focuses that mounted menu button, with successful Focus() and pre-open IsFocused checks. All94 original assertions remain byte-for-byte; the97 final calls add only a concrete button type check and the two preconditions. The post-open focus,1.6s answer, later orb-click question focus and no-persistence assertions remain intact. No production timer/focus change was made to accommodate the old setup. ED-NEW-cz-7b-fix quotes the old lines and the lead's scoped harness exception. Static independent review predates the approval; no extra reviewer was commissioned afterward.

## Windows verification

All builds/tests use unmodified heavy.sh and official wrappers, with fresh data and cleared provider keys. One own build/test at a time. No Native tests, Marrow launch, microphone/model/network/real CLI, installer or packaging run.

| Check | Result |
| --- | --- |
| Final App build | 0 warnings/errors |
| Original preview class, before exception | 15P/7F/0S of22 |
| Original four methods before exception, twice alone | 0P/14F executions |
| Preview class after exception | 22P/0F/0S |
| Original seven identities after repair, twice alone | 14P/0F across8 runs |
| Affected App classes (CZ, shared voice components, first-run consent/preparation, accessibility) | 237P/0F/0S |
| Full App non-native, once after repair | **10568P/235F/0S of10803**,33m38s |
| Every non-OD-0128 full-run failure, unchanged twice alone | 73whole methods,146runs,154P/0F/0S; all76 failing identities pass both rounds |
| V-T132 in full App | 224P/0F |
| V-T133 in full App | 71P/153F; exactly the immutable approved153 identities |
| Metadata verify.mjs | Gate A12/12; Gate B8/8 |

The raw full suite is red. Its159 accepted failures are the fixed153 OD-0128 matrix identities plus their six exact canonical/representative wrappers; no new identity, tolerance or reference change. The other76 failures pass twice alone under the standing failure rule. Their case-by-case disposition is [every-failure-disposition.md](every-failure-disposition.md), with complete TRX/log paths in [alone-assessment.json](alone-assessment.json).

## Every changed full-App result

The before run is T9's authentic `cdbec8a642ed56d5db14b75718461670ea9b819f` result:10347P/456F/0S of10803,33m3s. Restored079104326 differs from that candidate only in the two append-only ledgers: all source, tests and build inputs are identical. [baseline-provenance.json](baseline-provenance.json) authenticates the raw log and456 unique failing cases; both full runs execute10803 cases, with no skips. The TRX has two distinct passing test IDs sharing one truncated timeline-note display name; neither changed result.

**All225 changed identities are listed in [every-result-change.md](every-result-change.md):223 Failed→Passed and2 Passed→Failed.** Of the17 classes directly using CustomizationPageRig, exactly the seven requested cases change, all Failed→Passed. The other216 improvements in raw combined outcomes are recorded without attributing them to the fixture correction.

The two new raw failures are:

- `EffortSliderJewelFramesTests.Port_frames_render_for_the_side_by_side(appearance: "dark")`: `Strike lit capacity exceeded`. The unchanged whole method passes2/2 cases in each isolated round, including dark and light.
- `ImportSkillsTests.What_could_not_be_read_is_said_once_in_plain_words`: the read-note was absent in the combined run. The unchanged method passes1/1 in each isolated round.

Neither test uses CustomizationPageRig or changed source. Their full-run failures remain reported; neither repeats alone twice, so no production timing regression is established under the standing rule. No assertion or production behavior was altered to hide them. Full-App evidence is `app-full-after-clock.trx.gz`; before raw output is `app-full-before-clock.log.gz`; [full-app-comparison.json](full-app-comparison.json) includes every raw failure, message and classification.

## Evidence and scope

Scratch: `E:/Droppy/work/scratch/codex-cz-7b-fix`, including restoration patch/byte proof, clock/focus diagnostic source+JSON, approved proposal, red/green logs/TRX, review, before/after comparison and every isolated run. The handoff preserves checkpoints. Engineering/STATE ledger base prefixes remain byte-exact. No legacy queue packet exists for CZ-7b, so no unrelated task or requirement is marked DONE. This is the requested Windows repair/merge disposition, with native acceptance and the separately approved pill polish governed by their existing lead decisions.
