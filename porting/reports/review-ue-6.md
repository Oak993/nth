# Independent review: UE-6 "What's new" (branch `codex/f-ue-6`, head e3866d044)

Reviewer: independent, Linux headless (.NET SDK 10.0.112, PowerShell 7.6.6, Avalonia 12.1.2). Reviewed the two UE-6 commits
(`7246775a`, `e3866d04`) against `OD-0111` (read verbatim), ED-319/ED-320 (self-update), port_impacts 254, upstream_defects 90/91 and the
`ED-360` block. Production code was not changed.

## Verdict: FIX FIRST

The refusal paths, test isolation and traceability are sound. Three defects remain, none large; each has a failing `REVIEW_` test.

## Findings

1. **The guard can license notes that show no card in the app** (`scripts/release-notes.ps1:65-73`). A section ends only on a `#` heading,
   but the app's digest (`UpdateReleaseNotesDigest.IsHeader`, `AboutUpdateViews.cs`) also ends it on a bold line (`**Thanks**`).
   `## [1.7.0]` / `### New features` / `**Thanks**` / `- Someone` passes the preflight and extracts with exit 0, yet the digest is empty, so the
   update offer shows nothing. That breaks OD-0111's "Thanks … ignored; no recognised section refuses". Failing test:
   `REVIEW_ReleaseNotesDigestParityTests.Notes_the_guard_accepts_are_never_an_empty_digest_in_the_app`.
   Fix direction: treat a bold-only line as a section reset in the guard (as the digest does), or run the guard's final check through the same digest rules.
2. **Updates page and sidebar offer drop notes the About page shows** (`src/Marrow.App/Views/Settings/UpdateWhatsNew.cs:39`). `if (digest.IsEmpty) return;`
   shows nothing for a release whose notes have no recognised heading. `AboutSoftwareUpdateSection` (`AboutUpdateViews.cs:1397-1411`) falls back to a
   `MarkdownView` for the same notes. A release published without the script (or an older one) is offered on the Updates page and in the sidebar popover
   with no What's new at all, the install surfaces OD-0111 names. Failing tests (both surfaces):
   `REVIEW_WhatsNewOfferTests.Notes_without_a_recognised_section_are_still_shown_with_the_offer_as_About_shows_them(sidebar: False|True)`.
3. **What's new is read from the singleton checker, not from what the page offers** (`UpdateWhatsNew.cs:17-25`). A service with no Marrow row
   (a non-Velopack copy; its persisted offer survives, see `UpdateChecker.Check`) still gets 1.8.0's cards above rows that cannot install it.
   Failing test: `REVIEW_WhatsNewOfferTests.An_offer_with_no_Marrow_row_to_install_shows_no_Marrow_whats_new`.

## Notes that are not defects

- Script is stricter than the app (only `-` bullets, exact three titles): `*` bullets and `Fixes`/`Improvements` aliases refuse. This matches upstream's
  `build_changelog.py` regex (`^\s*-\s+`, port_impacts 254) and fails closed, so it is acceptable. Fenced or unclosed examples also fail closed.
- Refusals verified by running the scripts: missing, empty/whitespace, wrong or unidentified version, duplicate version, no populated recognised
  section, bracket-less heading. All exit non-zero with a diagnosis, before any `dotnet`/`vpk`/`gh` call, with no `artifacts/` and no output file
  created or overwritten (`Refuses` compares every file byte for byte). `-ValidateReleaseNotesOnly` returns before any write.
- Test isolation: the rig defines `vpk`, `dotnet`, `gh` as throwing guards; no test runs `vpk` or the network. Updater tests use `FakeSelfUpdateEngine`
  (the checker takes the engine branch, never `FetchReleases`); the loopback counter is 127.0.0.1 only.
- Gap (not a defect): UE-T72 asserts the cards exist and that "Update everything" exists, not that the cards come first. Placement is correct in the code
  (first child in both). No test covers the happy-path pack argument at run time (only a source-text check, UE-T74); nothing verifies the notes reached
  `releases.win.json` after `vpk pack`.
- `ED-360` loosens `Traceability.DecisionId`; `porting/tools/make_packets.mjs:118` still only knows `ED-\d{3}`, so the lead should number the decision.
- "What's new in Marrow X" is a new label on the Updates page, sidebar and the installed-notes About state; it is backed by OD-0111's wording, not by the source.

## Verification

- `dotnet build tests/Marrow.App.Tests -p:TreatWarningsAsErrors=false -p:EnableWindowsTargeting=true`: succeeded.
- Touched App slice (`WhatsNewTests|ReleaseNotes|UpdateChecker|AboutUpdateViews|UpdatesSettingsPage|UpdatePill|UpdatePopover|MarrowUpdateItem|Traceability`, `!~Native`):
  370 passed, 1 failed, 0 skipped. The failure, `AboutUpdateViewsSecondTests.The_slider_accessibility_label_reads_updating_marrow_and_the_value_is_the_whole_percent`
  (string mismatch), is outside UE-6's files; this host's ICU shim (system ICU 74 forwarded for the Windows-named imports, a test-host-only adapter) and locale are the likely cause. UNRESOLVED, not run on the base to confirm.
- REVIEW tests: 4 failing for the reasons above, plus Traceability passing (3). Core tests were not run (no changes under review there beyond one source-text test).
