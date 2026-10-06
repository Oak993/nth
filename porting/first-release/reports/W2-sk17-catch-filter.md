# W2(b) SK-17 provider preparation error repair

Date: 2026-10-06. Scope: review finding 7 only, E:\Droppy\work\scratch\lead\fledge\review-trialh-sk17.md. Worktree E:\Droppy\wt\fl\sk17-fix; branch codex/fledge-sk-17-fix; clean starting head 2bbd272c9fb5dcbbb6aecd032f8d941f48cda605. Final local head: E:\Droppy\work\scratch\handoff\codex-W2-sk17-fix.md. **READY for independent orchestrator check; PUSH HELD** under the later coordination instruction.

The real generation composer can reject a pinned disclosure with System.IO.InvalidDataException, which is not an IOException. The two app preparation catch filters now explicitly include that type, preserving SwiftTry.IsThrownError and every existing IOException, UnauthorizedAccessException, JsonException, DecoderFallbackException and ArgumentException case. The result is ProviderError.Case.Failed with the same original message. Production diff: exactly one exception alternative added in AntigravitySkills.cs and one in CursorSkills.cs. No changed messages, generation verification or provider launch behavior.

## Acceptance proof

Test-first commit 4f8300c2b87314c32f5ec796572e14908795b509 contains SkillsPreparationErrorTests.A_corrupt_pinned_disclosure_becomes_a_localized_provider_failure_through_real_preparation, with Antigravity and Cursor cases. Both failed twice alone, each showing Expected ProviderError / Actual System.IO.InvalidDataException; raw red logs are adjacent. The build passed with zero warnings/errors before any implementation change. Tests were not changed after red.

Each case creates an isolated rooted host with fake store protection and fail-closed transfer/process seams, registers a hash-verified installed pin, and binds Storage to that same root. Its pinned SKILL.md frontmatter name disagrees with the frozen catalogue. The real pipeline lease verifies successfully; the real provider composer rejects the name with InvalidDataException. The test then calls the actual default Prepare.Current delegate (asserting PrepareOwned) through the app wrapper, and requires ProviderError.Case.Failed plus identical SwiftErrors localized text. Pin bytes remain unchanged and no view is generated. No fake Prepare override, source-string proof, real CLI, network, model, account or user project is involved.

## Commands and results

All commands run in native C:\Program Files\Git\bin\bash.exe. The exact red.sh/green.sh scripts are retained. They source E:/Droppy/work/scratch/lead/heavy.sh unchanged, heavy_acquire, trap heavy_release EXIT, create disposable MARROW_PORT_DATA_DIR roots, and build with dotnet build tests/Marrow.App.Tests -v q --nologo. This also builds the referenced Services test project. Test runs use bash E:/Droppy/work/claude-verify/one-full.sh E:/Droppy/wt/fl/sk17-fix <project> <filter>, never bare dotnet test.

Red filters, each run twice separately:

- `(FullyQualifiedName~SkillsPreparationErrorTests&DisplayName~Antigravity)&FullyQualifiedName!~Native`
- `(FullyQualifiedName~SkillsPreparationErrorTests&DisplayName~Cursor)&FullyQualifiedName!~Native`

Green App filter: `(FullyQualifiedName~SkillsPreparationErrorTests|FullyQualifiedName~AntigravitySkillsLaunchTests|FullyQualifiedName~CursorSkillsLaunchTests|FullyQualifiedName~TraceabilityTests)&FullyQualifiedName!~Native`

Green Services filter: `(FullyQualifiedName~AntigravitySkillsComposerTests|FullyQualifiedName~TraceabilityTests)&FullyQualifiedName!~Native`

| Selection | Result |
| --- | --- |
| App real preparation + affected launch regressions + Traceability | Passed!  - Failed:     0, Passed:    25, Skipped:     0, Total:    25, Duration: 704 ms - Marrow.App.Tests.dll (net10.0) |
| Services real composer + Traceability | Passed!  - Failed:     0, Passed:     5, Skipped:     0, Total:     5, Duration: 296 ms - Marrow.Services.Tests.dll (net10.0) |

Red/green builds: 0 warnings, 0 errors. Both new acceptance cases are included in App's 25 green cases. No remaining failing or skipped case in these selections. No full-suite claim, self-mutation, live recheck, Fledge, owner app, native run, installer or packaging. Independent orchestration validation/push remains pending by explicit instruction.

## Audit

Only the two provider wrapper files, the new App acceptance test, this report/evidence, and SK-17-specific ED/STATE suffixes are changed from the named base. The initial claude/fledge-sk-17-fix ref is retained; only the local codex/fledge-sk-17-fix branch receives new commits. Both ledger prefixes match their original bytes and hashes; ledger-prefixes.json records lengths/hashes. Artifact-hashes.json covers the raw logs/scripts/audit. No trial-h, SK-18, Settings, ma-ui or merge-train changes. ED-380 records the narrow decision. No push was performed.
