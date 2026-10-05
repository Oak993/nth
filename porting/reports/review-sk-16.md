# SK-16 independent review

**Verdict: FIX FIRST.** Ten findings below are reproduced by 17 failing `REVIEW_` cases. This branch changes only those tests and this report; it contains no production fixes.

Reviewed on Linux, 2026-10-05, with .NET SDK 10.0.12 and the repository's Avalonia dependencies.

## Scope and baselines

- Delivery: `cloud/f-sk-16`, `78b6ed14f94cb862d5d38588b2440421ff87d2f0`.
- Reviewed SK-15 backend: `codex/f-sk-15`, `c7526e100fe3b88537e53b6a8eba5a1cb3bce1c5`.
- Merge base: `76a61a4e27dde17b80fc23c72a1235c47280f2eb`.
- Review diff: `git diff 76a61a4e27dde17b80fc23c72a1235c47280f2eb 78b6ed14f`, including the merged SK-11/SK-12/SK-14/SK-15 work, fixtures, tests, decisions and recorded evidence. It comprises 294 files, 58,963 insertions and 149 deletions; 234 paths are under `src/` or `tests/`. The SK-16-only changes were also inspected separately, rather than treating the merge commits as already approved.
- Requirements: `porting/first-release/BUILD-PLAN.md` SK-16/DL-19 row; `skills-PLAN.md`; `skills-ONDEMAND-PLAN.md`, especially 3.2, 3.8, 4.4, 5.4 and DL-19/DL-20; `porting/engineering_decisions.md`'s complete `ED-368` block.

The delivery merged an older SK-15 line, not the reviewed `c7526e100` state. Comparing the heads shows the loss of four existing review-test files (`LibraryPageReviewTests`, `LibraryReviewTests`, `SkillSwitchReviewTests`, `LibraryBackendReviewTests`), the unbound-download admission guards, required-space rounding and reviewed wording. ED-368 item 3's assertion that SK-15 disabled no controls contradicts the specified SK-15 baseline and the port lead's instruction. Its availability flag is exposed but the pages/controller no longer enforce it.

## Numbered findings

1. **[P1] Unbound downloads are enabled and mutate Settings into failures.** `src/Marrow.App/Port/Skills/Pages/ActionArea.cs:50` (also 76, 103), `src/Marrow.Services/Port/Skills/Pages/LibraryController.cs:174` (also 425, 456, 604). With `FailClosedFirstInstall`, Download, Retry and Download all remain enabled. Calling Download queues the row and leaves it Failed; Continue starts a run which ends Partial. The seam correctly refuses the bytes, but the reviewed SK-15 requirement is disabled controls and no admission while unbound. Reproductions: the five Download/Download-all/Retry cases in `REVIEW_Sk16UnboundControlsTests`, plus `REVIEW_Unbound_single_download_does_not_queue_or_fail_a_row` (Available expected, Failed actual) and `REVIEW_Unbound_Continue_does_not_start_a_download_all_run` (Confirm expected, Partial actual). Restore both UI and backend guards; an enabled button which fails after a press is not the required behavior.

2. **[P1] Progress replay can deadlock an offer joining a Settings download.** `src/Marrow.Services/Port/Skills/Fetch/FetchFlight.cs:28`, reached through `SkillsDownloads.cs:108` and `SkillsOfferInstalls.cs:47`. `Listen` invokes caller code while holding the flight lock. If the callback synchronously dispatches to another thread which reads `Downloads.Running(id)`, that reader needs the same flight lock through `Last`; neither thread can proceed. The card additionally invokes caller code under its own lock. Reproduction: `REVIEW_Replayed_progress_allows_another_thread_to_read_shared_download_state` starts a held Settings transfer, joins through the real `Host.OfferDownloads` seam and asks another thread to read its state inside the replay callback. The bounded one-second read fails; the test then releases and drains the transfer. Move external callbacks outside locks and preserve replay ordering. The test does not require a real UI dispatcher or leave a deadlocked worker behind.

3. **[P1] An offer cannot report an item failure while another item remains pending.** `src/Marrow.Services/Port/Skills/Fetch/SkillsOfferInstalls.cs:28`. `Task.WhenAll` withholds a fault until every other task finishes. If one item fails and another transfer is stalled or is still running for a Settings requester, the offer keeps waiting instead of entering Failed with its reason and Retry (ONDEMAND 5.4). Reproduction: `REVIEW_Offer_failure_is_returned_without_waiting_for_another_held_item`; S1 fails with a fixture IOException, S3 enters a held transfer, and the offer times out rather than returning that IOException. Return the failure promptly and detach this card's remaining subscriptions without cancelling transfers still needed by another requester.

4. **[P1] An app/catalogue update discards a successfully installed skill set after restart.** `src/Marrow.Services/Port/Skills/Host/CatalogueTrust.cs:43`, consumed by `SkillsHost.cs:95`. Restoration accepts only the current catalogue pin. Install a skill set at pin A, restart with the catalogue advertising pin B, and the host deletes its installed record, retains the tree as an orphan and exposes Available instead of UpdateAvailable. It cannot contribute the promised Skills and tools update row. This is an existing SK-13 unresolved obligation assigned to SK-16, still unhandled by this delivery; it affects supported skill sets, not only the unimplemented tools mentioned in ED-368 item 8(b). Reproduction: `REVIEW_Installed_skill_set_survives_restart_with_a_new_catalogue_pin_as_update_available` uses the same trusted fixture files with a new target pin and observes Available. Persist/recover the previously trusted descriptor safely; do not infer trust from arbitrary installed bytes.

5. **[P2] Offer progress counts a shared dependency twice.** `src/Marrow.Services/Port/Skills/Fetch/SkillsOfferInstalls.cs:45`, with dependency-weighted reports from `SkillsDownloads.cs:153`. The adapter multiplies each report by that item's own size, but the dependent's fraction already includes its prerequisites. A card containing S1 and S2, where S2 needs S1, therefore reports too many bytes. Reproduction: `REVIEW_Offer_progress_counts_dependency_bytes_once`; after S1 completes and S2 reports 25%, the unique byte count is 66, while the adapter reports 86. Aggregate distinct transfer bytes with a consistent denominator rather than reinterpreting a dependency-inclusive fraction as item-only progress.

6. **[P2] A previously installed item prevents the card's progress from reaching Checking.** `src/Marrow.Services/Port/Skills/Fetch/SkillsDownloads.cs:90`, `SkillsOfferInstalls.cs:35`. An item already here returns its folder without reporting completion. Its card entry stays at zero, so a card containing that item and a newly downloaded item ends its transfer reports with incomplete bytes and Downloading even though all paths are returned. This occurs when Settings finishes an item before the offer is accepted. Reproduction: `REVIEW_Already_installed_offer_item_reports_completed_progress` installs S1 first, then accepts S1 plus S3; Checking is expected, Downloading is observed. Account for cached completion in the adapter/shared seam.

7. **[P2] Skills changes during Update everything are never reconciled after it becomes idle.** `src/Marrow.App/Port/Updates/UpdateEverythingProduction.cs:50`. `Refresh` simply returns while the service is busy, with no pending refresh or idle subscription. An install/remove/reset change during a run therefore leaves rows for the old item set until some unrelated later pipeline change occurs; a later Check now still uses the old configured items. ED-368 item 7 describes this limitation but it does not deliver the BUILD-PLAN requirement for current Skills and tools rows. Reproduction: `REVIEW_A_skills_change_during_an_update_is_reconciled_when_the_run_finishes` holds an update, resets the host and releases the update; the idle service still has S1 instead of the refreshed S2 fixture row. Defer reconciliation until the current run ends.

8. **[P2] The required-space message understates the amount which blocks Continue.** `src/Marrow.Core/Port/Skills/Pages/DownloadAllPlan.cs:78`. Required space uses `SizeText.Free` (nearest rounding); the reviewed SK-15 branch rounded requirements upward. With roughly 12.46 GB required and 12.2 GB free, Continue is disabled but the message says "Needs 12 GB free on C:." The displayed requirement is already satisfied. Reproduction: `REVIEW_Required_space_never_advertises_less_than_the_admission_check_requires` expects "Needs 13 GB free on C:." and observes 12 GB. Keep ordinary free-space formatting separate from rounding up a blocking requirement.

9. **[P2] Installed skill sets lose the required new-chat explanation.** `src/Marrow.App/Port/Skills/Pages/ActionArea.cs:123`. ONDEMAND 4.4 explicitly requires the Settings row to say "Ready in new chats", because a running chat keeps its locked generation. The reviewed SK-15 branch had that explanation; this merged branch shows only Installed and trash. A user downloading during a chat receives no explanation for why its skills are unavailable there. Reproduction: `REVIEW_An_installed_skill_set_says_Ready_in_new_chats` inspects the actual action controls and finds no such text.

10. **[P2] The merged dialog removes reviewed guidance for Hide and manual recovery.** `src/Marrow.Core/Port/Skills/Pages/LibraryWords.cs:74` and `:106`. The SK-15 baseline's source-backed assertions require "One at a time. Hide it and keep using Marrow." and "Retry tries the failed ones again. Or download them yourself." The delivery truncates these to the first sentence. The running dialog no longer explains its Hide affordance, and a partially failed run loses the manual option required by the Download-all decision. Reproduction: both `REVIEW_Reviewed_SK15_dialog_words_are_preserved` theory rows. Restore the reviewed strings; the absence of the fallback's new design controls does not justify removing already-reviewed guidance.

## Checks which hold, and delivery limits

The supported first-install path is substantive: it refuses incomplete pins/owed terms/missing prerequisites/unbound seams before fetching, serializes pipeline operations, installs into temporary trees, verifies the trusted descriptor, prepares the route before commit and raises Changed after releasing the pipeline lock. Existing first-install and shared-flight tests pass. Settings and offers join the same host-owned downloads, and cancelling one subscriber can leave another subscriber's transfer running. The offer property creates adapters over that shared owner; its adapter identity is not a separate-download defect.

The loopback production tests pass: pinned raw-file paths and licence, verified installation, failure retaining `.part`, reopening with no automatic request, and a later explicit Download using Range/206 with server-log byte accounting. `ManualFiles` also accepts a matching fixture into the part folder and rejects wrong bytes. These checks establish the service paths, not live Windows/download acceptance.

Tools, Python sets and Node sets fail closed in `CatalogueInstallPlans.Plan` before it builds requests: only a Skillset or Plugin with one commit pin is accepted. Multi-pin sets are also unsupported. Incomplete catalogue pins are rejected earlier by the shared seam. This follows the stated unresolved installer-parameter boundary; no installer settings are guessed. Every shipped catalogue item remains Unpinned, so the application still cannot download its real catalogue. This is a disclosed limitation, not evidence of working tool/package downloads.

DL-20 remains manual and unexecuted. The changed tests use scripted seams, byte-returning/forbidden HTTP handlers, the guarded local HTTPS fake or compiled local provider stand-ins. The loopback tests make local HTTP requests to their fake; the guard refuses every other connection. No test path inspected performs a real-source artifact download, external provider call or vendor installer run. Installer tests use fake protection/process seams and local fixture copying. The new review tests use no HTTP client, socket, provider process or installer process.

ED-368 also correctly discloses that no application site constructs a SkillsOfferFlow/card while SK-14's locked design is absent, and that the manual fallback's vendor link/folder/Check again UI is not built. Consequently this is a partial wiring delivery; neither the service tests nor this review claim that the in-chat acceptance/retry flow is usable end to end.

## Verification

All commands ran from the repository root after `source /workspace/.tools/marrow-env.sh` and `export MARROW_PORT_DATA_DIR=/tmp/marrow-review-sk16-data` (an absolute disposable directory). The environment helper's relative default is not suitable for these fixtures.

```sh
dotnet build Marrow.sln -warnaserror -p:EnableWindowsTargeting=true
dotnet build Marrow.sln --no-restore -warnaserror -p:EnableWindowsTargeting=true -m:1 -nr:false
```

Both the delivery-only build and the final build including the review tests passed with **0 warnings and 0 errors**. One intervening parallel build exited MSB4166 (worker processes exited prematurely); the final serial build passed in 1:09.69. No production source or project was edited to obtain this result.

For each of `Marrow.Core.Tests`, `Marrow.Services.Tests`, `Marrow.App.Tests` and `Marrow.UI.Tests`:

```sh
dotnet test tests/<project> --no-build -p:EnableWindowsTargeting=true \
  --filter 'FullyQualifiedName~Skills&FullyQualifiedName!~Native'
dotnet test tests/<project> --no-build -p:EnableWindowsTargeting=true \
  --filter 'FullyQualifiedName~TraceabilityTests&FullyQualifiedName!~Native'
```

Final Skills results (including the intentionally red reviewer cases):

| Project | Passed | Failed | Skipped | New REVIEW failures |
| --- | ---: | ---: | ---: | ---: |
| Core | 442 | 3 | 0 | 3 |
| Services | 639 | 13 | 47 | 7 |
| App | 35 | 15 | 0 | 7 |
| UI | 0 | 0 | 0 | 0 (no matching Skills tests) |

All **17/17 REVIEW cases fail for the asserted behavior**. In particular, the UI controls are inspected before font measurement, avoiding the Linux font failures which affect existing page tests. The bounded replay/failure tests clean up their held flights in finally blocks. No disabled, skipped or unconditional failing reviewer case was added.

The same 14 other failures were observed in the delivery-only Skills run before adding these tests: Services has the Codex stand-in path failure, both Copilot stand-in path failures, the Linux drive-label assertion, and two Claude legacy/current event comparisons using different timestamps; App has three Cascadia Mono glyph failures, four ICU-dependent offer cases, and the Windows protection/uninstall case. These failures remain separate from the numbered findings. The slice is not green, and Windows execution is still required; no environmental failure was treated as evidence that a REVIEW assertion passed.

All **12 Traceability meta-tests passed** (three in each project). Each review file has one test class with the matching filename, class-level `Feature=SK-16`, `Spec=DL-19`, `Review=SK-16` and a recognized Decision or Rule citation. There are no helper classes embedded in the test files and no new ledger statuses or synthetic requirement IDs. Existing traceability tests and expectations were not changed.

TRX and build logs for this run were kept under `/tmp/review-sk16-results/` and `/tmp/review-sk16-*.log`; they are not committed. Committed review files are the four `REVIEW_Sk16*Tests.cs` files and this report only.
