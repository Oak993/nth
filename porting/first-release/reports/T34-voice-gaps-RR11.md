# T34 RR-11 — notes, AVX2 and countdown

**BLOCKED: exact Countdown C mounting requires protected pill/composer integration files.** Notes and processor binding are implemented and covered with fakes. Authority: audit RR-11, OD-0106 Notes A/Countdown C including its amendments, voice-SPEC V-14.5/V-14.6 and BUILD-PLAN §1.1/VT-17. Base: `285b6b7a99b39e0e517f34c8d34a1860c945f83f`. No whole row was ALREADY DONE.

## Missing-path proof

The base has no production `new VoiceNoteBridge`, no production `new CapCountdownLine`, and only fail-closed/scripted `IProcessorFeatures`. `Notes/FailClosedProcessorFeatures.cs:8` throws when AVX2 is queried. `Countdown/CapCountdownLine.cs:23` subscribes to the cap and computes frames/announcements but has no drawing caller. The base has no `VoicePill`/`VoicePillSlot` source file. Generic composer notes do not select all twelve picked destinations.

## Completed work

`RuntimeProcessorFeatures` reports .NET `Avx2.IsSupported`, which includes usable CPU/OS instruction support, without CPU-name/vendor guesses. Startup binds it; tests inject both facts rather than reading hardware.

`VoiceReleaseNotes` constructs the bridge when `ComposerTextView` attaches to its typed composer ancestor, disposes on detach, and uses the existing field/notice surfaces. All picked wording, one-link destinations and 5.2-second duration are preserved. Windows links use the exact `ms-settings` destinations. Internal links open Voice typing and bring the named Microphone, Speech model, Engine or OpenAI key row into view. A per-session channel supplies the existing polite announcer. The existing controller retains its blocked/privacy/cancel/no-speech notes. Cached content and an occurrence counter prevent duplicate announcements on draft/surface refresh; tracking is installed once before attachment and restores the error after generic composer updates. A retry dismisses the previous error timer.

New fake tests cover all twelve destinations/words/duration, expiry and announcements, bound processor facts, cache-preserving refresh, and an actual mounted error surviving a draft update, moving from field to notice and expiring. Refresh first failed with expected 2 callbacks versus actual 1, then passed after the repair. Existing bridge/countdown expectations were not edited.

## Remaining exact work

Countdown C must sit inside the pill at left 36/right 42/bottom 5, height 2, with the ruled cap timing, color/brightness/Reduce Motion transitions and the exact `30 seconds left.` / `10 seconds left.` announcements. Constructing another line elsewhere would not implement this design. The required `VoicePill`, `VoicePillSlot`, `VoiceOrbSlot`, `ComposerVoiceController` and `ComposerView` integration belongs to `codex/vt-12-finish` and/or `codex/cz-7b`; those files were not changed or imported. The existing standalone countdown tests pass, but they do not prove a mounted line. RR-11 remains open.

This base has no pill download presentation, so the notes adapter's suppression callback is false. After the preparation/pill owner integration, bind it to the actual first-download face so the still-downloading note stays suppressed while the pill shows that face. OpenAI key activation also needs the VT-16 owner's key-control integration; bringing the exact row into view here does not implement that editor.

Windows run: repeat note/bridge/countdown fake tests and mounted error test; test every link against actual Windows Settings/Marrow rows and a screen reader; verify error movement/expiry/retry through detached/reattached composers. After owner integration, mount and render Countdown C at 270 s, thresholds 15/5 s, 30/10 s announcements and cap completion, for light/dark, scale factors and Reduce Motion; verify preparation-note suppression. No microphone, model or network is needed for these fake tests.

Engineering entry: ED-558. Final validation and limitations are in `T34-voice-gaps-validation.md`.


## Round 2 (2026-10-07)

**BLOCKED here; stopped and handed to T33 by the lead.** `45096401f90e1d0ae74a9e156cb9f0cb3fa67e60:R-gaps-round2.md`, T34: "Stop RR-11 here. The lead hands it to T33 after VT-12 is READY." Exact countdown and preparation/cloud hooks depend on local `codex/vt-12-finish` (T33) and `codex/vt-16-fix` (T16). No RR-11 production file was edited in Round 2; no dependency branch was merged. Existing notes/countdown tests were rerun unchanged in the 121/121 combined affected App run. Their green results do not close the integration handoff.
