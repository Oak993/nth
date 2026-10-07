# Current release tracking inventory — T39, 2026-10-07

This inventory supersedes the dated checkpoints in STATE and the BUILD-PLAN's original “nothing built” snapshot. It reconciles source/ledger facts, not release acceptance. Branch `codex/release-pack` starts at `285b6b7a99b39e0e517f34c8d34a1860c945f83f`; audit authority is `origin/codex/release-audit` at `6b2e699407c9a6ef596887ca9dd335b56b2744f5`. B3 remains 1.7.0 build 21, commit `52bc7926097cd622d08d6294e0d8b08a48c7516e`. Machine-readable counts, all unknowns, and every one of the 136 first-release obligations are in [T39-current-inventory.json](T39-current-inventory.json). Recompute source counts with `node porting/tools/progress.mjs --json`; no percentage here represents product completeness.

| Inventory | Current source fact |
| --- | --- |
| PR requirements | 3992: 3944 IMPLEMENTED, 11 SPECIFIED, 15 RETIRED, 22 NOT_APPLICABLE_WITH_EVIDENCE |
| UI records | 1057: 1044 IMPLEMENTED, 5 RETIRED, 8 status UNSET |
| Applicable PR/UI | 5007; 4988 IMPLEMENTED; 0 VERIFIED at final release revision |
| Source reading | 441/441 READ; test-method parser counts 16619 |
| Quarantine | All 12 RESOLVED; none OPEN |
| Unknowns | 24 RESOLVED; UNK-0003 BLOCKED_ON_ACCESS, UNK-0009 BLOCKED_ON_ACCESS, UNK-0011 OPEN |
| Owner decisions | 124 imported headings, OD-0001..0126 except reserved OD-0029/0030; no missing ruling invented |
| First-release units | 136 obligations reconciled from the plan, every multi-account checklist bullet and late owner rulings; each lacks final semantic acceptance |

UNK-0009's feed choice is resolved by OD-0062/0100; live check/download/swap/restart remains blocked, and no publication was made. UNK-0003 has no matching macOS reference in this Linux session. UNK-0011's thirteen source ambiguities remain open. The 36 declared requirement dependencies stay intact, including historical dependencies on resolved unknowns. Original unknown discussion is historical beneath its corrected Status, not a renewed request for already-recorded owner decisions.

| Gate | Current result / remaining evidence |
| --- | --- |
| A | BLOCKED: pinned reference checkout unavailable on Linux; exact B3 manifest is retained |
| B | PASS: all 8 static checks, 3 open unknowns, 36 declared dependencies |
| C | BLOCKED: genuine SwiftFont marker plus missing exact-revision behavioral evidence |
| D | BLOCKED: no complete four-project final Windows TRX/discovery/semantic record; scoped Linux runs are partial |
| E | BLOCKED: package inspection, prerequisite/native and lifecycle artifacts absent; installer operations prohibited in T39 |
| F | BLOCKED: antecedent gates and independent final parity review not accepted |

The T39 row evidence lives in T39-RR-23/25/26/35/42/43.md. RR-23 and RR-35 inputs are complete; RR-25's genuine font marker and RR-26's final acceptance evidence remain blocked. RR-43 metadata inputs and compiled application PE checks are complete; its row report keeps setup PE inspection unverified because installer builds are prohibited. Gates never gain acceptance from plan or IMPLEMENTED labels.

The following later source/record evidence supersedes historical “missing” claims without promoting those units to final acceptance:

| Historical claim | Current tracked evidence and remaining limit |
| --- | --- |
| CZ-8 Speech missing | `src/Marrow.App/Port/Settings/SpeechSections.cs`, `CustomizationSettingsPage.cs`, `tests/Marrow.App.Tests/Port/Settings/CustomizationSpeechSectionTests.cs`; interaction/visual final receipts still required |
| SK-14 card/decline absent | `src/Marrow.App/Port/Skills/OfferCardControl.cs`, `SkillsOfferFlows.cs`, `src/Marrow.Services/Port/Skills/Pipelines/SkillsPipeline.cs`, `tests/Marrow.App.Tests/Port/Skills/SkillsOfferFlowsTests.cs`; preserve unresolved wording/design and live acceptance limits from later STATE notes |
| SK-18 not on original plan | OD-0118, `src/Marrow.Services/Port/Skills/Import/ImportedSkillsLibrary.cs`, unchanged `ImportedSkillsLibraryTests.cs`; FR-SK-18 included, final acceptance pending |
| Boot B never integrated | `src/Marrow.App/Port/AuroraBootPresentation.cs`, `src/Marrow.UI/Port/AuroraBootHandler.cs`, unchanged native boot tests; later STATE supersedes old compositor blocker but quiet-PC cadence and non-green full-suite interactions remain unaccepted |
| W2 catch-filter repair only on a unit branch | `porting/first-release/reports/W2-sk17-catch-filter.md`, actual AntigravitySkills/CursorSkills production filters and unchanged SkillsPreparationErrorTests; historical report is attributed evidence, not final-revision credit |
| UE-6 not on original plan | OD-0111, `src/Marrow.Core/Port/SelfUpdate/ReleaseNotesParser.cs`, unchanged ReleaseNotesPackagingTests; FR-UE-6 included |

OD-0029 and OD-0030 remain **RESERVED / NOT IMPORTED**. `owner_decisions.md`'s provenance paragraph following OD-0035 is unchanged byte-for-byte. There is no source ruling to implement or authority to renumber. Historical ledgers/reports are retained; missing Windows/macOS/live proof remains missing.
