# Pre-merge review: `origin/cloud/sk-18` and `origin/cloud/f-sk-14`

Base: `origin/claude/verify-terminal-20261001` (dec005e4f). Read-only review; no builds or tests run.
Line numbers refer to the branch versions of the files.

**Scope notes.**
- SK-18: the three-dot diff is clean — 31 files, all under the new Import feature (5 branch commits, `6deadaeba`…`5603f6268`). Reviewed whole.
- SK-14: `git diff base...branch` reports *multiple merge bases* (552d66ed7, a9d2b2f8f) and drags ~13k lines of the earlier skills-downloads rounds into the diff; those rounds already entered the base (in newer, post-review form) with merge-train batches 1–7. The branch's own work is 14 commits (`acd66f6c7`…`1aebbdc63`) touching 53 files; the review is scoped to those. The branch is also ~95 base-commits stale, so the merge itself will need real conflict resolution on shared files (`SkillsPipeline.cs`, `TermsBox.cs`, `DetailView.cs`, `LibraryButtons.cs`, `HostLibraryBackend.cs`, `ThreadTimeline.cs` exist in newer form in the base).
- Spec documents read: `porting/owner_decisions.md` OD-0118; `porting/first-release/skills-ONDEMAND-PLAN.md` (5.1–5.6, DL-17/DL-18/DL-20); `porting/first-release/BUILD-PLAN.md` SK rows; the branches' own `porting/engineering_decisions.md` blocks ED-NEW-cloud-sk-18 and ED-NEW-cloud-sk-14.

---

## Branch 1 — `origin/cloud/sk-18` (import skills from other AI apps)

### Critical

**C1. The owner-ruled auto-detection is never wired in the app: the feature ships dark while all its tests pass.** *(spec mismatch — OD-0118 addendum "auto-detection", ED-NEW-cloud-sk-18 item 5)*
The production seam builds the import model **without** a detector: `src/Marrow.App/Port/Skills/Pages/SkillsPageSeams.cs:66` (`model = new ImportModel(controller);` — the detector parameter at `src/Marrow.App/Port/Skills/Import/ImportModel.cs:15` is left null). `new ImportDetector` appears nowhere under `src/` (only the ctor at `src/Marrow.Services/Port/Skills/Import/ImportDetector.cs:75` and the test fixture `tests/Marrow.App.Tests/Fixtures/ImportRig.cs:17`). Everything detector-gated is inert in production: scan-on-page-open and watch start (`src/Marrow.App/Port/Skills/Import/ImportArea.cs:52-56`), the quiet "Found N skills in A and B · Import" line (`ImportArea.cs:82-84`, `Detector?.Current` always null), stop-on-leave (`ImportArea.cs:63`), and dismissal persistence. No test exercises the default seam (`LibraryPageHarness` always overrides `SkillsPageSeams.Imports`; `ImportRig` hand-wires `detect: true`), so the green suite masks it. The manual flow (button → sheet → scan → import) is wired and works; the detector unit itself is well built (debounce, atomic `dismissed.json`, watch never writes). This holds the merge on its own: an accepted owner ruling is undelivered.

### Medium

**M1. A save/IO failure during import strands the sheet on "Importing…" forever; a faulted scan strands it on "Looking…".** *(correctness — unhandled error)*
`src/Marrow.Services/Port/Skills/Import/ImportController.cs:238` catches only `InvalidOperationException`. `library.Import` can also throw `IOException`/`UnauthorizedAccessException` from the `finally` save (`ImportedSkillsLibrary.cs:106-108` → `Save()` at `:346-371`: `Directory.CreateDirectory`, the `File.Copy` to `.unreadable` at `:349`, `FileOps.WriteAtomic`). The exception faults the task; the tail that publishes the result (`ImportController.cs:240-247`, `phase = Done` at `:245`) never runs, and the call sites are fire-and-forget (`src/Marrow.App/Port/Skills/Import/ImportSheetView.cs:235-236`: `_ = Controller.ImportSelectedAsync()` / `_ = Controller.ImportAllAsync()`), so the failure is unobserved and the sheet shows "Importing…" indefinitely. The existing `ImportWords.CouldNotSave` (`src/Marrow.Core/Port/Skills/Import/ImportWords.cs:46`) was written for this case and is never shown for IO failures. Same shape one level up: `ScanAsync` (`ImportController.cs:98-103`) has no guard, so a throwing scan faults the fire-and-forget `_ = model.Controller.OpenAsync()` (`ImportArea.cs:77`) and the sheet sticks on "Looking…" (the scanner is written never to throw — lower reachability).

**M2. A newer-Marrow (read-only) index makes the page's switch and Remove crash instead of being unavailable.** *(correctness; half of ED item 2's "shown and never changed")*
`ImportedSkillsLibrary.cs:125` and `:139` throw `InvalidOperationException` when `ReadOnly`; `ImportController.cs:263-274` passes it straight through, and `src/Marrow.App/Port/Skills/Import/ImportedSection.cs:73` (switch setter → `model.Controller.SetEnabled`) and `:115` (Remove confirm → `model.Controller.Remove`) invoke it on the UI thread with no guard (`ReadOnly`/`LoadProblem` surface nowhere in `src/Marrow.App`). On a downgraded profile, flipping a switch on the Skills page throws on the UI thread.

**M3. Re-opening the sheet while an import runs behind it corrupts the new session.** *(correctness — race)*
Escape only hides the sheet mid-import (`ImportArea.cs:126-128`); the page's "Import skills" button stays live and `Open()` (`ImportArea.cs:74-78`) calls `OpenAsync()` (`ImportController.cs:84-96`), which has no phase guard: it clears ticks/result/question and sets `Scanning` while the previous import's `Task.Run` is still inside `library.Import`. When that ends, its tail (`ImportController.cs:240-247`) unconditionally clears the new session's ticks, overwrites the summary and forces `phase = Done` — the user's fresh sheet jumps to the old import's result. (Two concurrent `library.Import` batches are not reachable this way because `RunAsync` requires phase `Choosing`; the stomp is.)

### Low

**L1. Every `List()` re-reads every imported skill's files from disk, synchronously, on the caller's thread.** `ImportedSkillsLibrary.cs:48-51` → `Resolve` `:166-196` → `ReadTree` `:201-224` (up to 200 files / 5 MB per skill, copies and linked originals alike). Called from `ImportController.cs:64` and `:71` and from `ImportDetector.cs:102` (`Detector.Current`) on every revision bump (`ImportArea` Sync→Detect) — UI-thread disk reads per redraw; fine for a few small skills, noticeable with many linked ones.

**L2. A failed Replace can orphan copy folders.** `ImportedSkillsLibrary.cs:92-105`: `entries.Remove(existing)` happens before `DeleteCopy(existing.Skill.Id)`; if `DeleteCopy` throws, the new copy folder stays on disk unindexed (the index is not saved, so the record self-heals on next load, but nothing ever cleans orphan `copies\<id>` folders). No data loss.

**L3. Reparse points are not always "reported".** ED item 1: "Links that are reparse points are reported, not followed." The plugin-tree walk skips them silently: `ForeignSkillScanner.cs:176` (contrast `:109` and `:164`, which do report). Not followed anywhere — good.

**L4. `UniqueName` loops forever on a name that can never become valid.** `ImportedSkillsLibrary.cs:302` (`for (var i = 2; Taken(candidate) || !SkillName.IsValid(candidate); i++)`): appending `-<app>-<i>` never fixes a `SkillName`-invalid base name. Defence-in-depth only — current callers feed scanner-validated names.

**L5. A page rebuilt mid-import forgets that the user hid the sheet.** `hidden` is per-`ImportArea` state (`ImportArea.cs:21`); leaving and returning to the Skills page mid-import rebuilds it with `hidden = false`, and `Sync` (`ImportArea.cs:94-107`) re-shows the sheet at once — ED item 4 says "the sheet returns at the end."

### Tests

**T1. (Medium) `No_line_when_nothing_is_found_and_none_without_a_detector` never tests the no-detector half.** `tests/Marrow.App.Tests/Views/Settings/ImportSkillsTests.cs:298-302` runs through `RunDetecting` (`:273-278`), which always builds `ImportRig(detect: true)`. The detector-less configuration — which is the production configuration (C1) — is untested despite the name.

**T2. (Medium) `An_imported_skill_has_a_switch…` never operates the switch.** `ImportSkillsTests.cs:169`: `LibraryPageHarness.Press` only presses `Button`s (`tests/Marrow.App.Tests/Fixtures/LibraryPageHarness.cs:137-140`) while the switch is a self-drawn `SettingsSwitch` (`ImportedSection.cs:73-74`), so the assertion passes on the `|| TryFind(...) is not null` existence fallback; the enabled-state assertion (`:172`) is then produced by calling `rig.Controller.SetEnabled(id, false)` directly (`:170`), bypassing the row's binding. The test passes even if the switch's setter is a no-op. (The two-step-remove half of the same test is genuine.)

Checked and clean: the Services slice honestly covers the ED claims — the "library free while the question waits" regression (`tests/Marrow.Services.Tests/Port/Skills/Import/ImportedSkillsLibraryTests.cs:343-363`) and the before/after fingerprints proving nothing is written to foreign folders (`ForeignHome.cs:52-62`). No test touches a real home folder, network or CLI (all roots under `TestDataDirectory`/temp, `ForeignHome.cs:15`); the one real-OS test uses a real `FileSystemWatcher` on a temp folder (`ImportDetectorTests.cs:230`), which is appropriate.

### User-visible strings

**S1. (Low) Status line runs two sentences together.** `ImportSheetView.cs:132`: `ImportWords.FoundCount(skills, found.Count) + " " + ImportWords.Nothing` renders "7 skills in 3 places Nothing in those apps is moved or changed." — `FoundCount` (`ImportWords.cs:37`) has no trailing punctuation. All other strings match the owner-accepted wording (`ImportWords.cs`) exactly.

### What was checked and is sound (SK-18)

Every write lands under Marrow's own `skills\imported` (`ImportedSkillsLibrary.cs:36-40`; host root at `SkillsHost.cs:101`); `DeleteCopy` is id-guarded and cannot reach a Link's original (`ImportedSkillsLibrary.cs:288-291`); the scanner opens files read-only with share flags and follows no reparse point; the index write is atomic (`FileOps.WriteAtomic`); the duplicate question is asked with the library gate released (`ImportController.cs:210-235`, `ImportedSkillsLibrary.cs:66-72`); the generation handoff keeps the identity key byte-identical when there are no imports and folds each imported file's hash into it when there are (`SkillGenerations.cs` hunk: identity at the branch's `:232-236`, fingerprint `:251-259`), with catalogue skills winning names and the same budget/clash rules applied; thread marshalling from controller/detector callbacks to the UI thread is handled in `ImportModel`.

---

## Branch 2 — `origin/cloud/f-sk-14` (SK-14, the in-reply offer "Offer A")

### High

**H1. Accepting terms on the offer card blocks the UI thread behind any in-flight download.** *(correctness — unbounded synchronous wait)*
Full chain: the card's `TermsBox` accept (`src/Marrow.App/Port/Skills/Pages/TermsBox.cs:117-120`, second ctor `:31-39`) → `SkillsOfferFlow.AcceptTerms` (`src/Marrow.App/Port/Skills/SkillsOfferFlow.cs:120-123`) → `HostOfferTerms.Accept` (`src/Marrow.Services/Port/Skills/Offers/HostOfferTerms.cs:11`) → `TermsStep.Accept` (`src/Marrow.Services/Port/Skills/Terms/TermsStep.cs:49-54`) → `SkillsPipeline.AcceptTerms` (`src/Marrow.Services/Port/Skills/Pipelines/SkillsPipeline.cs:227-230`), which calls `operation.Wait()` **synchronously** — and the operation semaphore is held across a whole download + install (`SkillsPipeline.cs:95` in `InstallAsync`). Clicking "I have read these terms" in a chat while e.g. a 356 MB Settings download is running freezes the whole UI until that download ends. The branch's own review fixed exactly this shape for decline (`SetDeclined`, `SkillsPipeline.cs:204-210`: "Not under the operation lock: that is held for a whole download or install, and a decline answers a provider on the main actor"); the terms path keeps it. (The pre-existing Settings caller `HostLibraryBackend.AcceptTerms` has the same pattern; the branch adds the new in-chat caller.)

### Medium

**M1. The 5.3 "currently downloading → the offer attaches to that download's progress" path is dead in the production wiring.** *(spec mismatch — ONDEMAND 5.3 guards)*
The detector's in-flight guard (`src/Marrow.Core/Port/Skills/Detect/MissingToolDetector.cs:138`, `:147`) reads `context.State`; the wiring supplies the pipeline's persisted state (`src/Marrow.App/Port/Skills/SkillsOfferFlows.cs:20`: `() => host.Pipeline.State`); but `SkillsPipeline` never commits Queued/Downloading/Checking records — only terminal ones (its own doc comment: "Nothing about a download in flight is written to state.json", `SkillsPipeline.cs:88-91`; the only commit point is `:237`). So `AttachToDownload` never fires and `SkillsOfferFlow.cs:107` (`Begin(attaching: true)`) is unreachable: while Settings (or another chat) is downloading an item, a chat signal shows a fresh "… isn't on this PC" card with its own Download button instead of attaching to the running download's progress. No duplicate bytes result (accepting joins the flight via `SkillsDownloads.Join`, `src/Marrow.Services/Port/Skills/Fetch/SkillsDownloads.cs:111-146`), and SK-16's row shows the download in Settings, but 5.3's attach behaviour and words are missing, and no test exercises it through the real wiring (`SkillsOfferFlowsTests` runs against an idle pipeline). Fix the wiring or record the divergence as an ED before merge.

**M2. "Check again" hashes and copies on the UI thread before "Checking…" can appear.** *(correctness; defeats the ED item 5/6 transition)*
`src/Marrow.Services/Port/Skills/Pages/LibraryController.cs:449`: `_ = backend.PlaceManualFiles(item);` is fully synchronous (`HostLibraryBackend.cs:40` → `ManualFiles.PlaceFolder`, `src/Marrow.Services/Port/Skills/Fetch/ManualFiles.cs:28-51`: reads, SHA-256-hashes and copies every size-matching file — up to ~356 MB for T3's MSI, ~325 MB for T10) and runs on the button handler's UI thread (`src/Marrow.App/Port/Skills/Pages/ManualBlock.cs:102`) *before* `Download(id)` at `:450` flips the overlay that swaps the button for the spinner (`ManualBlock.cs:89`, `:99`). Every comparable controller op defers its disk work (e.g. `LibraryController.cs:181`). On big items the app freezes for seconds with "Check again" still on screen, and the promised "Check again → Checking…" transition cannot show during the placement. A `Task.Run` fixes it.

### Low

**L1. The Attach seam is fail-open across its check-then-act window.** `src/Marrow.Services/Port/Skills/Fetch/SkillsOfferInstalls.cs:20-25` checks `IsRunning` for every item, then `Run` → `downloads.Install` → `Join` (`SkillsDownloads.cs:117-122`) creates and starts a **new** flight if the original ended in between — a fresh download started without the user's yes (5.6: "it never starts a download without the user's yes"). Currently unreachable in production (M1), but the seam does not fail closed.

**L2. In-order flight scheduling is only approximately ordered, and the exclusive-scheduler prefix can hold real file I/O.** `SkillsDownloads.cs:146`: the flight is created under `lock (gate)` but `Task.Factory.StartNew(..., starts)` runs after the lock is released, so two concurrent asks can start out of order — the inversion ED item 9 says is fixed. Also the "short part" of a flight includes `InstallAsync`'s validation block (`SkillsPipeline.cs:102-113`) with `CollectCore()`'s recursive deletes, all on the single exclusive start thread (`SkillsDownloads.cs:26`). No deadlock found (all waits are async; continuations leave the scheduler).

**L3. The card's terminal collapse still removes ~50–60 pt in one frame.** Title and host live outside the CrossfadeSlot (`src/Marrow.App/Port/Skills/OfferCardControl.cs:53-55`); on done/continuing/declined only the slot's content glides to zero height, then `HideWhenSettled` (`OfferCardControl.cs:138-148`, called at `:126`) flips `IsVisible` false, removing title + host + padding atomically — against the comment's own "the composer below it moves with the glide, never in one frame" (`:137`). `REVIEW_Terminal_state_fades_before_collapsing_the_card` asserts frame-0 stability and final invisibility but no per-frame continuity at the collapse, so it passes.

**L4. Downloading draws no byte counts, and "0%" when there is no total.** 5.4 and ED item 2 say "bytes and percent"; the card draws bar + percent + cancel only (`OfferCardControl.cs:183-190`). When `BytesTotal` is null the projector yields `Percent = null` (`src/Marrow.Services/Port/Skills/Offers/OfferCardProjector.cs:26-27`, rule "none without a total") but the card renders `(view.Percent ?? 0) + "%"` (`OfferCardControl.cs:131`) — a constant "0%" (reached in the first frames before the first progress event, and via scripted/SK-12 sources).

**L5. `PlaceFolder`'s directory enumeration sits outside the try.** `ManualFiles.cs:32-37`: the lazy `foreach` enumerator escapes the per-file filter (`:37`, `:48`) if the folder is deleted/ACL'd between the `Exists` check and `MoveNext` — an `IOException`/`UnauthorizedAccessException` then escapes to the UI handler via `CheckAgain` (`LibraryController.cs:449`). Narrow race.

**L6. `Keep` records the match before the `.part` is durably written, and bypasses the downloader's writer turn.** `ManualFiles.cs:67-70`: `found.Add(hash)` precedes `Directory.CreateDirectory`/`File.Create`/`File.Move`; a write failure is swallowed (`:48`) and the placement silently lost (fails safe — the bytes are fetched normally later). It also ignores the downloader's `PartFileTurns`, so it can collide with an in-flight transfer pinning the same hash. Related: the loose-file path hashes one opening of the file and copies a second (`:57-66` vs `:74-75`); a browser still writing the file can place a `.part` that fails the pin (the downloader re-verifies and refetches — fails safe).

**L7. `CheckAgain`'s `Catalogue.Items.First` can throw on a stale Failed record.** `LibraryController.cs:447` — after a catalogue update drops/renames an item while its persisted record is Failed, `First` throws `InvalidOperationException` out of the lock into the UI handler; `ManualFallbackOf` (`:431`) uses `FirstOrDefault` for the identical lookup.

### Tests

**T1. (Medium) `REVIEW_SkillsBuildTests` spawns the real `dotnet` CLI with a 60-second cap.** `tests/Marrow.Core.Tests/Port/Skills/Pages/REVIEW_SkillsBuildTests.cs:21-32`: `dotnet build -t:Rebuild` (offline via `--no-restore`, outputs to a disposable %TEMP% dir, kills the tree at 60 s). It tests what its name says (review finding 1: warnings-as-errors), but a full Rebuild within 60 s can turn a loaded machine into a hard failure rather than an environmental skip, and it depends on the ambient SDK.

**T2. (Low) Stale comments contradict the code and the tests they head.** `src/Marrow.App/Port/Skills/SkillsOfferFlows.cs:10-12` still says "nothing in the app calls this yet … no thread gets one" — `src/Marrow.App/AppModel.cs:484-492` now creates a flow per chat and the card is mounted inline in the reply (`src/Marrow.App/Views/Chat/ThreadTimeline.cs` hunk at `:246-252`; `SkillsOfferRows.cs:18-24`). `tests/Marrow.App.Tests/Port/Skills/OfferCardTests.cs:15-18` and `tests/Marrow.Services.Tests/Port/Skills/Offers/OfferCardProjectorTests.cs:2-4` still say the card has no asked sentence/why/decline/captions and sits "above the composer" — item 16 added the words and moved it inline.

**T3. (Low) The "only move forward" claim is enforced but never tested.** `OfferCardTests.Downloading_shows_the_bar_and_the_percent_that_only_move_forward…` feeds only forward-moving values; the monotonicity lives in `SkillsOfferInstalls.Card.Part` (`SkillsOfferInstalls.cs:83`, `Math.Max`), which no test drives backwards. Also: no App-level test asserts the *absence* of the manual block for a fallback-less item (`ManualBlock.cs:29`; P6/A6/T1p at `ManualFallback.cs:50-51`), and ED item 5's coverage claim ("every item it lists that the catalogue has") is broader than `ManualFallbackTests.cs:16-41` (no InlineData for A2 or P2–P5).

Checked and clean: every SK-14 fixture is offline — scripted offers, fake backends, the loopback fake, temp roots; a sweep of all 21 SK-14 test/fixture files finds no `HttpClient`/socket/`GetFolderPath`/`Process.Start`/home access other than T1. The decline ordering is right (state.json written before the provider is answered, `SkillsOfferFlow.cs:197-204`; `SetDeclined` under `gate` only, merge-safe with install commits); the state machine matches 5.4 (cancel → asked, Retry → downloading, terms hold the primary off, decline only in asked/failed); Return acts only with the card focused (`OfferCardControl.cs:228-236`); percent is floored and capped (`OfferCardProjector.cs:27`); the bar is the locked 150 wide (`OfferCardControl.cs:184`); a held command's double-answer is guarded by epoch + approval membership; `request_tool` waiters are answered exactly once and unblock on cancellation.

### User-visible strings

**S1. (Low) The why line embeds the full command line, untruncated.** `src/Marrow.Core/Port/Skills/Pages/OfferWords.cs:17` ("`{command}` can't run without it."), fed the whole command line from `SkillsOfferFlow.cs:53-54` and wrapped without a cap (`OfferCardControl.cs:212-217`). A long pipeline/script command makes the card grow without bound; suggest truncating (first token + ellipsis). Everything else — "… isn't on this PC." (plural joining), "The {skill} skill needs it.", the three captions, "Don't ask again in this chat", the ManualFallbackWords block — reads correctly and matches the locked design byte-for-byte.

---

## Unsure (either branch)

- **SK-18, `ImportedSkillsLibrary.cs:206-209`:** whether `Directory.EnumerateFiles(root, "*", AllDirectories)` follows directory reparse points into a link cycle is runtime-specific; `IsLinkedPath` skips files *under* links, and a cycle would surface as one failed-import row at worst. Bounded either way.
- **SK-18, `SkillGenerations.cs` (branch `:181`):** `ImportedSkillsLibrary.Snapshot()`'s foreign-disk reads now run inside the generations' global gate; a linked skill on a slow/network drive serializes every provider's generation builds behind that read. Consistent with the class's pool-thread contract; latency risk I could not quantify.
- **SK-18, `ImportController.cs:84-96`:** `OpenAsync` clears a pending `question` without releasing its `answer` semaphore — if it ever ran while the import thread waits on a duplicate answer, that thread would block forever. I found no UI path that reaches this (Escape answers Skip-for-rest first, `ImportArea.cs:129-132`); a one-line release would remove the question.
- **SK-14, `SkillsOfferFlow.cs:74-77` (in `Observe`):** when one event yields both an offered item and an attach item, the offered item takes the card and the attach item is silently dropped (no join, no attach). Unreachable in production today (M1), so impact unclear.
- **SK-14, `HostLibraryBackend.cs:42-45` / `ManualFallback.cs:46,55,57`:** `OpenManualPage` enforces scheme+host from the embedded (trusted) catalogue but has no independent host allow-list, and `Strip(url.Host)` silently drops a non-default port/userinfo. No current catalogue URL is affected; worth a deliberate decision rather than a fix.
- **SK-14, `ManualFallback.cs:37-38`:** latent pluralization wart — "the one skill folders at their pinned commits (…)" if a per-skill item ever yields exactly one distinct pin; no current catalogue item reaches it.

## Bottom line

- **SK-18: hold the merge** until C1 is fixed (wire `ImportDetector` in `SkillsPageSeams.DefaultImports` with a `dismissed.json` path, plus one test over the default seam), and fix M1–M3 in the same pass. The import core (scanner, library, handoff) is solid and honestly tested.
- **SK-14: fix H1 (offload or async the terms-accept wait) and either wire or record M1 before merge.** M2 deserves the same pass. The rest is Low and can ride follow-ups.
