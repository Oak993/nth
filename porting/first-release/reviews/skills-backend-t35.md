# T35 skills backend — NOT READY

Branch: `codex/skills-backend`, based on `origin/claude/verify-terminal-20261001` at `285b6b7a99b39e0e517f34c8d34a1860c945f83f`. Read audit from `origin/codex/release-audit` at `6b2e699407c9a6ef596887ca9dd335b56b2744f5`. Read CLAUDE.md, IMPLEMENTING.md, owner decisions OD-0099/amendments and OD-0118, skills specifications and BUILD-PLAN 1.5. No row was already done; each missing behaviour is cited in its report.

| Row | Result | Evidence / remaining reason |
| --- | --- | --- |
| RR-13 | BLOCKED | [Catalogue pin report](skills-backend-t35/RR-13.md): all 29 items remain incomplete. Existing assertions demand empty inventories/null trusted hashes; required upstream tree/artifact hosts are denied. No facts were invented. |
| RR-14 | BLOCKED | [Installer report](skills-backend-t35/RR-14.md): production tool/package plans and trusted restart descriptors remain absent; existing assertions require per-skill rejection and dropped tool state after reopen. |
| RR-15 | DONE (backend) | [Provider report](skills-backend-t35/RR-15.md): managed/imported generation composition, SK-11a..f launch/native tools, resume/reuse and child-exit leases connected. Live Windows acceptance remains. |
| RR-16 | DONE (backend) | [Provider-origin report](skills-backend-t35/RR-16.md): actual observations, current readable sync, distinct origins, equivalence and default library binding. |
| RR-45 | DONE | [Import report](skills-backend-t35/RR-45.md): bounded suffix-preserving names; Copy/Link maximum-length loop regression passes. |

Core/Services/runtime sources only. No shared UI file was edited; RR-17/18/44 remains with codex/skills-ui. No pre-existing test, tolerance, reference or assertion was changed. Engineering ledger entries ED-NEW-skills-backend-1 through -5 were appended in binary append mode; the original mixed-ending bytes remain an exact prefix. Owner ledger is unchanged. One commit is assigned to each requested row.

## Verification

Linux, official .NET SDK 10.0.401; C#/.NET 10 and Avalonia 12.1.2 unchanged. All added tests use local fixture trees, fake protection/locks/processes/credentials/offer flows; native sessions send no turn. No real download, install or provider request runs in the new tests. Read-only upstream provenance probes run outside tests, with URLs and received-byte hashes in the evidence folder.

| Check | Result |
| --- | --- |
| Core skills | 479 passed, 0 failed/skipped |
| Added Services regressions + Services traceability | 34 passed (31 additive cases + 3 existing meta-tests), 0 failed/skipped |
| Selected App runtime regressions + traceability | 63 passed (includes 12 additive runtime cases), 0 failed/skipped |
| Core traceability | 3 passed |
| Entire Services skills namespace | 891 passed, 16 failed, 48 skipped, 955 total |
| Untouched detached-base Services skills namespace | 860 passed, same 16 failures and 48 skips, 924 total; zero newly failing cases |
| Entire Core attempt | Aborted from unavailable Windows kernel32.dll; 2,128 passed/281 failed before the crash. Untouched base also aborts; differing asynchronous crash timing prevents an overall equivalence claim. |
| Entire Services attempt | Interrupted after about eight minutes of unsupported native Windows fixtures and repeated fake-HTTP/FileWatcher timeouts. No full-run success or completed total is claimed. |
| Builds / diff / immutable tests | App/Services/Core compiled with warnings as errors; git diff --check clean. Every pre-existing test file and catalogue file remains byte-identical to the base. |

Evidence: [verification summary](skills-backend-t35-evidence/verification-summary.json), [Core skills log](skills-backend-t35-evidence/core-skills-final.log), [final Services log](skills-backend-t35-evidence/services-trace-final.log), [App log](skills-backend-t35-evidence/app-final.log), baseline/final skills logs and explicitly labelled broad-run excerpts. Behavioural red witnesses and pinned upstream probes are in the same folder. Traceability's initial failures in the new fixtures were repaired by moving a helper to a separate file and adding rule metadata; all assertions stayed intact.

Commands used with MARROW_PORT_DATA_DIR set to a fresh test root:

```text
dotnet test tests/Marrow.Core.Tests --filter FullyQualifiedName~Port.Skills
dotnet test tests/Marrow.Services.Tests --filter FullyQualifiedName~Port.Skills
dotnet test tests/Marrow.App.Tests -p:EnableWindowsTargeting=true --filter "FullyQualifiedName~BackendProviderLaunchTests|FullyQualifiedName~CursorSkillsLaunchTests|FullyQualifiedName~AntigravitySkillsLaunchTests|FullyQualifiedName~ThreadRuntimeEnsureSessionTests|FullyQualifiedName~ThreadRuntimeSessionFactoryTests|FullyQualifiedName~ThreadRuntimeOpenRouterSessionTests|FullyQualifiedName~TraceabilityTests"
```

All three projects' TraceabilityTests were run; full Core and Services commands were attempted separately. Full Services skills failures were compared by exact failing case names against the untouched detached base, not dismissed from memory.

## Windows-run needs

1. Run full Core and Services on Windows 11 x64/.NET 10; repeat App runtime/traceability checks. The 16 Services skills failures involve Windows stand-ins/pipes/drive paths; 48 tests explicitly skip Linux. Native ProcessLauncher, kernel32/BCrypt, FileWatcher and fake TLS/HTTP fixtures need the intended platform.
2. Run real ACL/reparse/junction/held-handle tests for managed views, imported Copy/Link, maximum-length duplicate names, GC, uninstall/repair and restart. Verify original provider folders remain untouched.
3. Complete SK-11a..f WU-6..12 manual acceptance against pinned supported CLI versions: new/resumed Claude/Codex/Copilot/OpenCode/Go/Pi/Command Code/Grok, discovery/config precedence, namespaces/plugins, argv/env/shims, EOF versus exit and failed-start lifetime; native names/descriptions/use_skill/request_tool and invocation budget. Automated tests stay fake; live manual acceptance is separate.
4. After RR-13/14 contract and upstream-access blockers are resolved, verify all 29 real pinned catalogue entries and Windows installer/restart routes, including Python embed/pip, wheels, Node packages, MSI/SFX layouts, cancellation, damage/repair and updates. No such production installation is claimed here.

Release verdict: **NOT READY**. RR-13 and RR-14 remain mandatory release blockers, and Linux verification does not replace Windows acceptance.


## Round 2 — READY for T9 Windows verification

Authority: `origin/claude/lead-rulings:R-gaps-round2.md` (T35 and general interim-guard ruling), fetched/read without merging the branch. Its definition is: **“Verdict READY means READY for T9's Windows verification.”** Work remained on `codex/skills-backend`; only that branch is pushed, without force. No shared UI file changed. The Round 1 text above remains byte-exact; this section supersedes its assignments where the lead changed them.

| Row | Round 2 requested work | Release-row status / evidence |
| --- | --- | --- |
| RR-18 backend | DONE | Folder/ZIP/GitHub Check/Add API, immutable validated snapshots, atomically persisted own registry/provenance, command registration/child PATH, own skill/plugin generations, own priority and explicit invocation flags. Optional GitHub sub-folder paths now work with the existing Location field; only the selected folder is fetched, and normalized SubPath persists. [API](skills-backend-t35/custom-source-api.md), [T36 handoff](skills-backend-t35/T36-handoff-round2.md). |
| RR-44 backend | DONE | Reserve start enqueue order under the flight-creation gate before callbacks. Existing joins/cancellation/dependencies preserved; T36 owns selection/in-flight presentation. `SkillsDownloads.cs:143`, OrderedServiceStartsTests. |
| RR-13 | DONE: pin generator | BLOCKED for release catalogue completion: real official-source run, metadata/licence scope review and population of all 29 entries remain. The generator computes exact downloaded-byte SHA-256/size and source-observed version/licence with URL/date/source-body hashes. No catalogue facts were invented or changed. |
| RR-14 | DONE: offline manifest preparation and pin handoff | BLOCKED for release installers/restart descriptors: real data and stronger route tests must land before the interim throw guard is retired. The generator/recipes do not claim to connect production installers. |
| RR-15 | ALREADY DONE in Round 1 | Owned managed/imported provider generations and SK-11a..f launch/native-tool adapters revalidated in Services skills and selected App runtime tests; custom own skill/plugin bindings extend that composition. [Original code/evidence report](skills-backend-t35/RR-15.md). |
| RR-16 | ALREADY DONE in Round 1 | ProviderOriginReportsTests, HostProviderOriginsTests and SyncedProviderRemovalTests pass again. Origins/equivalence come from actual observations and currently readable sync, with no account inference from matching names. [Original code/evidence report](skills-backend-t35/RR-16.md). |
| RR-45 | ALREADY DONE in Round 1 | Both Copy/Link KeepBothMaximumNameTests pass again; suffix reservation prevents maximum-length names looping. [Original code/evidence report](skills-backend-t35/RR-45.md). |

Missing work was verified before implementation. At the Round 1 tip the custom backend/types did not exist (first fixture compile failure); the early backend then lacked command/generation composition and own priority (recorded behavioral failures). Its root-only `CustomSourcePaths.Repository` rejected `owner/repository/sub-folder`; URI AbsolutePath could erase `..`. Subtree tests first gave 11 failures/9 passes, then 20/20 passed. Four extra repository normalization cases first gave three failures, then all passed. Fake HTTP assertions require exactly the commit metadata and selected-folder listings/raw files; a full codeload request would fail the transport fixture. Outside paths, links/submodules, Windows case collisions, non-child paths and inconsistent byte lengths fail closed. `CustomSources.cs:37`, `PublicGitHubSource.cs:12`, `CustomSourcePaths.cs:89` implement the final contract.

ParallelCustomInstallTests first timed out because Add copied both selected trees under the registry gate. Private tree copies now overlap, while name/command reservations and registry commits remain serialized; failed commits roll back only the affected tree and preserve its retry token. OrderedServiceStartsTests first observed `[S3,S1]` instead of `[S1,S3]` when the first request's observer was blocked. Enqueuing Run under Join's creation gate fixes the ordering without holding that gate for transfers or callbacks. Red/green witnesses are retained in [Round 2 evidence](skills-backend-t35-evidence/round2-verification-summary.json).

Push sequence: `ca504ef5` early RR-18 API; `ef5ac4d2` RR-44 starts; `a0437bbf` commands/plugin generations; `82f61af1` own priority/invocation; `a62b4deb` subtree/concurrent-copy fix, pushed before finishing the pin report; `286ef087` RR-13 generator; the RR-14 recipe/report commit follows. T36's current UI request/candidate adapter requires no signature change. Direct notification was attempted to its existing chat using `codex queue`; the app returned “no rollout found” for that thread. Its branch has no open PR through the connector; a GitHub commit-comment attempt was rejected “Forbidden”. No notification delivery is claimed. The pushed T36 handoff is available to merge; [delivery evidence](skills-backend-t35-evidence/round2-t36-notification.log) records the limitation.

### Generator and local-run handoff

New checked-in code: `tools/skills-catalogue/pin-generator.mjs`, `generate-pins.mjs`, `pin-manifest.mjs`, `prepare-pin-manifest.mjs`, and their two fixture test files. Imports have no network effects; generatePins requires an injected request function. The real-source command is explicit/manual, constrains official HTTPS origins and each redirect, stages complete output in a new folder, saves the exact hashed artifact bytes and raw metadata bodies, and never writes the embedded catalogue or runs an installer. Every version/licence comes from a selected field/body in the source's own fetched metadata. Unknown/missing facts, bad UTF-8/base64, digest/size/version mismatches, unsafe output paths and incomplete 29-ID coverage stop the run.

The local owner thread must use the existing resolver/signature prerequisites, prepare and review metadata recipes, then perform the real generator run. LibreOffice release metadata, repositories without a unique applicable licence and packages with null/legacy licence fields need actual upstream selection. Null selectors intentionally block; no invented licence/version fills them. Mixed per-folder licence scope and terms need review. [README workflow](../../../tools/skills-catalogue/README.md) gives the exact commands. The output `pins.json` records requested/final URLs, per-read UTC dates, source metadata size/SHA-256/selector/value, and artifact size/SHA-256; raw metadata is retained under its content hash. **No real generator/source fetch or installation was run in Round 2 tests or verification.**

### Interim guards left untouched

All of `tests/Marrow.Core.Tests/Port/Skills/Catalogue/SkillsCataloguePinsTests.cs` remains byte-identical, including these exact methods:

- Every_vendor_pin_is_the_commit_the_plan_prints
- O3_carries_its_four_pins_one_per_skill_and_claude_plugins_official_only_the_short_pin_the_plan_has
- Every_source_url_is_the_repository_or_listing_the_plan_names
- Every_tool_version_is_the_one_ONDEMAND_2_2_prints
- The_exact_sizes_and_urls_the_plan_prints_are_there_and_no_other_size_is_claimed
- The_hash_prefixes_the_plan_prints_are_kept_as_hints_and_no_full_hash_is_claimed_without_a_pin_run
- Licence_names_and_terms_classes_are_the_plan_s_for_the_items_it_classifies
- No_item_claims_to_be_pinned_while_a_value_is_still_missing_and_the_report_says_which_items_lack_real_values
- The_missing_lists_name_the_fields_the_plan_does_not_give
- The_strict_gate_a_release_will_use_fails_while_any_value_is_missing_and_names_each_item

Also untouched: `tests/Marrow.Services.Tests/Port/Skills/Fetch/ProductionSeamsTests.cs:101`, **What_the_catalogue_gives_no_install_parameters_for_fails_closed_with_the_one_unavailable_message**. It requires FirstInstallUnavailableException for the tool and per-skill-pin routes, the exact unavailable message and no fake HTTP requests. No guard was retired. As the lead requires, real generator data, an ED quoting superseded assertions, and stronger replacements must land together when retiring those interim guards; real behavior/safety assertions stay.

### Final Round 2 verification

Linux/.NET 10.0.401; Avalonia 12.1.2 unchanged. Custom source files, registry effects and HTTP requests use fakes. Owned generation publication uses the existing disposable test store with fake protection/locks; no real provider process or installer runs in new tests.

| Check | Result |
| --- | --- |
| Custom backend + ordered starts + Services traceability | 57 passed: 53 custom cases, one ordered-start regression, three meta-tests |
| Core skills + traceability | 482 passed, 0 failed/skipped |
| Entire Services skills + traceability | 948 passed, 16 failed, 48 skipped, 1012 total |
| Services failure comparison | Same 16 exact failing case names as the untouched original baseline; zero new failing cases |
| Selected App/runtime + traceability | 63 passed, 0 failed/skipped; rebuilt after final API/generation changes |
| Pin generator + recipe fixtures | 19 passed: 14 generator, five recipe; fake bytes/requests only |
| Existing catalogue pure-helper selftest | Passed |
| CLI syntax / build / diff | Both new CLIs parse; Core/Services/App compile; source/document diff checks pass. Stored terminal transcriptions trim only trailing renderer padding. |
| Immutable tests/ledgers/catalogue | All 2935 files under Round 1 tests and all 2927 original-base test files byte-identical. Original mixed-ending engineering ledger is a byte-exact prefix; owner ledger and embedded catalogue are byte-identical. Original report is a byte-exact prefix. ED-NEW-skills-backend-6 through -12 appended in binary mode. |

[Verification summary and exact baseline failure list](skills-backend-t35-evidence/round2-verification-summary.json), [Core log](skills-backend-t35-evidence/round2-core-final.log), [Services log](skills-backend-t35-evidence/round2-services-final.log), [App log](skills-backend-t35-evidence/round2-app-final.log), [custom/ordered log](skills-backend-t35-evidence/round2-custom-final.log), [pin fixtures](skills-backend-t35-evidence/round2-pins-final.log). Full Core/Services Linux limitations from Round 1 remain; no full-suite success is claimed. The 16 Services failures concern Windows stand-ins/pipes/drive paths, and 48 cases explicitly skip Linux.

Commands, with MARROW_PORT_DATA_DIR set to the test root:

```text
dotnet test tests/Marrow.Core.Tests --filter "FullyQualifiedName~Port.Skills|FullyQualifiedName~TraceabilityTests"
dotnet test tests/Marrow.Services.Tests --filter "FullyQualifiedName~Port.Skills|FullyQualifiedName~TraceabilityTests"
dotnet test tests/Marrow.App.Tests -p:EnableWindowsTargeting=true --filter "FullyQualifiedName~BackendProviderLaunchTests|FullyQualifiedName~CursorSkillsLaunchTests|FullyQualifiedName~AntigravitySkillsLaunchTests|FullyQualifiedName~ThreadRuntimeEnsureSessionTests|FullyQualifiedName~ThreadRuntimeSessionFactoryTests|FullyQualifiedName~ThreadRuntimeOpenRouterSessionTests|FullyQualifiedName~TraceabilityTests"
node --test tools/skills-catalogue/pin-generator.test.mjs tools/skills-catalogue/pin-manifest.test.mjs
node tools/skills-catalogue/selftest.mjs
```

### Windows-run needs

1. T9: full Core/Services and selected App/runtime/traceability on Windows 11 x64/.NET 10, including the exact 16 baseline failures and 48 Linux skips, native kernel32/BCrypt/processes/pipes/FileWatcher/HTTP fixtures. Do not waive or rewrite assertions.
2. RR-18: physical Folder/ZIP/public GitHub root/sub-folder Check/Add, persisted provenance after restart, Unicode/spaces, links/reparse/junction refusal, bounded/incomplete contents listings, concurrent selected installs, cancellation/rollback, enable/remove and no writes to original sources. Verify .cmd argv/quoting/Unicode/exit status and PATH priority in actual provider children.
3. RR-15/16/45: SK-11a..f discovery, native launch/tool requests, own plugin hooks/manifests, explicit-only invocation, active generation lifetime after Remove, new/resumed/failed child exits and actual provider account/plugin/user origins. Repeat maximum-length Copy/Link with real ACLs/junctions and original-folder fingerprints.
4. Local owner pin thread: real official sources and source metadata/licence scope, signature prerequisites, all 29 facts/change report and catalogue publication; only then same-commit stronger replacements/retirement of the listed interim guards. Complete and verify RR-14 Windows tool/Python/Node/per-skill installers and trusted restart descriptors, cancellation/repair/update. This is separate from fake automated tests.
5. T36: merge the pushed RR-18/44 backend and complete its already assigned UI verification. T35 made no UI change.

Round 2 verdict: **READY for T9 Windows verification**, as defined by the lead. Overall release verdict remains **NOT READY** until RR-13/14 real data/installers and Windows acceptance are complete. Direct T36 chat/comment delivery is blocked by the available app/network routes; its merge handoff is checked in and pushed.
