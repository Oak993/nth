# T7 Sections words audit

The owner source is `E:/Droppy/work/design/decisions-2026-10-04/skills-downloads/`. Its `src/sd-page.js` and self-contained `skills-downloads.html` were copied byte-for-byte into `porting/first-release/design-src/skills-sections/`.

SHA-256: JS `dd146ce68fc31d4b00b4f603558857ac35e73c9b5df342eae1dd1e61d8080da4`; page `08eb5700d7c5d14c6919cfcfa8d023b43b61c73ea8477c809a1eae180fe255d3`. Each equals its owner-source file.

All 49 `LibraryWords` literal constants were compared as decoded Unicode strings against the copied JS. 35 occur byte-for-byte in the source. The 14 plan/engineering extensions are explicitly accounted for below. Dynamic templates were compared against TX, LV, `acs`, `incRow`, `costBox`, and `whyRm`; the existing Core words, sizes and plan tests retain their precise examples. `LibraryReviewTests.Running_and_partial_words_match_the_copied_Sections_source_byte_for_byte` independently checks the full running and partial strings and the shared literal strings, including punctuation and curly apostrophes.

Corrected differences:

- Running subtitle: `One at a time. Hide it and keep using Marrow.` (TX b:sub).
- Partial footer: `Retry tries the failed ones again. Or download them yourself.` (TX b:foot).

Source-identical literal constants: Waiting, Checking, Installed, DidNotDownload, Retry, Remove, Removing, Cancel, InEverySession, NotListed, SwitchNote, Needs, Source, Licence, RemoveWhy, ResetConfirm, PausedTitle, DoneTitle, RunningSub, PausedSub, EverythingHere, FactDownload, FactOnDisk, FactFrom, ConfirmFoot, RunningFoot, DoneFoot, PartialFoot, Continue, Pause, Resume, Close, Hide, AlreadyHere, Downloaded.

Plan / ED extensions (not silently attributed to the design):

- ReadyInNewChats: ONDEMAND-PLAN 4.4; now drawn beside Installed for a skill set. Running chats keep their generation.
- UpdateAvailable, Update, Damaged, Repair: ONDEMAND-PLAN 3.5/3.7 maintenance states absent from the Sections prototype; existing SK-15 decisions retain these verbs.
- FoundOnThisPc: ONDEMAND-PLAN 3.6, actual reported path only.
- ProvidersHeading, ProvidersNote: Q8/ONDEMAND-PLAN 4.3 read-only reports; no copying or download claims.
- ResetAction, ResetWhy: ONDEMAND-PLAN 3.8; two-step reset, preserving switches and accepted terms.
- DownloadsUnavailable: existing ED-354 reason, now a disabled-control explanation rather than a failure after Queued.
- SizeEstimateNote: T7 item 7; `Sizes are estimates. Unpacked size is not known yet.` No exact-size claim. SK-2 must supply measured unpacked sizes.
- SizeUnknown: existing ED-354 exclusion for a catalogue item without any size.
- Unavailable: existing ED-354 unavailable-host explanation.

Existing catalogue limitations remain explicit: no measured instruction-description token counts, no reviewed default-on metadata, and no unpacked-size field. The counts therefore omit the prototype's token estimates; singular grammar is retained by the existing exact tests. Terms/no-size skip suffixes come from the existing SK-15 decisions, not an invented prototype state. Fixture catalogue names, paths, licence text and item summaries are data, not UI-copy replacements.

`Your Claude account provides this` (ONDEMAND-PLAN 4.1 / PLAN 3.1) is outside the present SK-15 report seam: `ProviderSkillGroup` supplies provider/name/description but no synced-account origin or equivalence/steps-aside fact. An own skill or system skill also reports under Claude, so the page cannot truthfully identify a Claude-account-synced copy from that data. This remains UNRESOLVED for SK-11/SK-16 provider-origin wiring; no account-sync claim is inferred from a provider's name.

Manual fallback, Minimize/pill, Add and Tools are still the original SK-15 open items. The corrected partial footer is the exact owner wording, but this review does not claim a manual-file install path exists.
