# Codex direct sprint, 2026-09-22

Started 02:41:36 UTC; target checkpoint 03:01:36 UTC. Owner instruction: "bypass all workers.
You take lead for 20 minutes and get as much work done as possible."
No new Cast, GLM, paid-worker, or computer-control call was made during this sprint.
All five packets are STANDARD. The owner-authorized direct sprint temporarily bypasses the
unfinished bake-off; it does not establish a model winner or waive tests/review/gates.

## Canonical result

Branch: `port/windows-exact-parity`, root `E:\Droppy\port`.

| Packet | Component | Approved tests commit | Feature/DONE commit | Total tests after landing |
| --- | --- | --- | --- | ---: |
| T-M2-001 | AntiSlopPolicy | a5c4159 | f6aa832 | 17 |
| T-M2-066 | RecentCache | b319b86 | 6162ed0 | 27 |
| T-M2-030 | MergeRequestLink | 7e8ed60 | b16fc26 | 48 |
| T-M2-059 | Coding | d21f8ce, additional regression fixtures 37966d2 | 1f55efd | 70 |
| T-M2-043 | Requests | e11a15b | 80f6687 | 78 |

T-M2-001 reuses the previously accepted DeepSeek implementation and independently checked
byte fixtures, reviewed again by the director. The other four implementations were written
directly by Codex. Approved tests were never weakened or edited to obtain a pass.

Queue: 5 DONE, 657 TODO, no in-progress packet in the canonical checkout.
M2: 5/78 complete (6.41%), 73/78 remaining (93.59%). All packets: 5/662 complete (0.76%),
657/662 remaining (99.24%). Requirements: 21/3827 IMPLEMENTED (0.55%), 0 VERIFIED.
These are packet/requirement counts, not estimates of elapsed effort or usable-app completion.

## Source and upstream boundary

Authoritative specification remains pinned to `dd2b86efef9e77168f304a86e8a8411877578bbe`.
Latest fetched candidate: `417ab7d3676cdf258fb6b68444f0626aa6fdfbed` in the separate
read-only `E:\Droppy\reference\droppy-code-417ab7d3` snapshot. Source comparison:
`git diff --shortstat <old> <new>` returned `127 files changed, 4671 insertions(+), 3079 deletions(-)`.
The earlier upstream check found 32 intervening commits (16 feature commits plus 16 merges).

Each completed Swift file was read in full. `git diff --exit-code <old> <new> -- <path>`
returned no diff for each. `git ls-tree` against both commits independently returned these
identical blobs (paths relative to `DroppyCode/`):

| Source | Identical Git blob |
| --- | --- |
| Core/Models/AntiSlopPolicy.swift | e06e7b8ef01e4f64a085f01a208ec7e69f80d6d2 |
| Core/Models/MergeRequestLink.swift | 20503159a6a70bc06e6e9cbfc35939905035bdf1 |
| Core/Models/Requests.swift | 63c85f2f363fed1cac1609891f326a3e57101449 |
| Core/Support/Coding.swift | 1a84492607b43d1b7b01acbfda9a4fac3c5d1941 |
| Core/Support/RecentCache.swift | bfc510193d26e8744649d5507f4b3e3c20c6ae83 |

This establishes unchanged component source, not unchanged integration callers. The broader
new-source audit is pending. Never relabel the old 433-file reading evidence as evidence for
the new 440-file tree. Hydra, Provider, Jev, zoom/font and UI changes require fresh requirements.

## Requirement-to-test evidence

Test paths below are under `tests/Marrow.Core.Tests/`; exact test methods are searchable there.

| Requirement | Test class and evidence |
| --- | --- |
| PR-STORE-129 | AntiSlopPolicyTests: Enabled_guidance_is_the_source_literal_byte_for_byte; enabled digest |
| PR-STORE-130 | AntiSlopPolicyTests: disabled byte fixture/digest and both Selector_returns methods |
| PR-HYDRA-055 | AntiSlopPolicyTests: exact enabled five-line block and U+2019 fixture; runtime prompt integration remains later |
| PR-STORE-053 | RecentCacheTests: Two_generations_remain_readable_until_the_next_wholesale_rollover; Contains_does_not_promote_and_current_hits_do_not_refresh_the_generation |
| PR-STORE-054 | RecentCacheTests: Reading_previous_promotes_and_can_itself_discard_the_old_generation; Promotion_preserves_original_cost_and_exact_budget_equality_does_not_roll |
| PR-STORE-055 | RecentCacheTests: rollover, exact-budget, replacement and maximum 64-bit cost tests |
| PR-STORE-056 | RecentCacheTests: Over_budget_replacement_removes_existing_value_even_from_previous (both generations) |
| PR-STORE-057 | RecentCacheTests: Nonpositive_limits_and_negative_costs_follow_source_clamps; maximum cost |
| PR-STORE-058 | RecentCacheTests: Removal_returns_current_or_previous_and_refunds_current_cost; replacement |
| PR-GIT-052 | MergeRequestLinkTests: Recognizes_source_path_shapes_without_restricting_hosts; Rejects_nonmatching_shapes_and_invalid_integers |
| PR-GIT-053 | MergeRequestLinkTests: recognition cases assert forge and exact label |
| PR-GIT-054 | MergeRequestLinkTests: recognition cases assert exact Merge title |
| PR-GIT-055 | MergeRequestLinkTests: Prompts_match_source_bytes_for_all_three_forges |
| PR-STORE-017 | CodingTests: Storage_sorts_keys_recursively_is_compact_and_escapes_slashes; date and escaping fixtures |
| PR-STORE-018 | CodingTests: date round-trip/offset and invalid-date fixtures; Storage_options_are_shared_and_frozen |
| PR-STORE-020 | CodingTests: missing/null/wrong-type defaults, fallback evaluation count, case-sensitive keys, invalid array; CodingIntegerTests: integral numeric forms |
| PR-RUNTIME-016 | RequestsTests: Approval_mapping_and_provider_fields_are_preserved (five kinds, free-form ID); exact enum cardinality |
| PR-RUNTIME-017 | RequestsTests: same five cases assert verbatim headlines |
| PR-RUNTIME-018 | RequestsTests: same five cases assert exact semantic symbol identifiers; glyph capture blocked by UNK-0007/UNK-0003 |
| PR-RUNTIME-019 | RequestsTests: Kinds_and_roles_have_exact_source_cardinality_and_options_use_value_equality |
| PR-RUNTIME-020 | RequestsTests: Multi_question_payload_preserves_all_fields_flags_and_optional_choice_detail; structural question equality and copied collection |

## Validation and observed output

Before implementation, new test references produced expected missing-type compiler failures.
Tests were reviewed against the packet and full Swift source, then committed before feature code.
For Coding, additional source-derived numeric regressions initially produced four failing cases;
the converter was corrected without changing those fixtures. Swift Foundation's integer slow path
was checked in its primary implementation:
https://raw.githubusercontent.com/swiftlang/swift-foundation/swift-6.0-RELEASE/Sources/FoundationEssentials/JSON/JSONDecoder.swift

Every packet ran `node E:\Droppy\bakeoff\run-packet-gates.mjs main <packet>` from the canonical
checkout. This runs these exact commands with `MARROW_PORT_DATA_DIR` set to a throwaway folder:

```
node porting/tools/check_protected.mjs
node porting/tools/verify.mjs --gate A --json
node porting/tools/verify.mjs --gate B --json
node porting/tools/verify.mjs --gate C --json
node porting/tools/verify.mjs --gate D --json
```

All five: protected exit 0, "protected paths unchanged against HEAD; reference checkout clean";
A exit 0/PASS=12; B exit 0/PASS=8. C exit 1: quarantine guard PASS, the existing two App stub
markers FAIL, remaining acceptance NOT_IMPLEMENTED. D exit 1: clean warnings-as-errors build
and all tests PASS, full acceptance NOT_IMPLEMENTED. Final packet: 78 passed, 0 failed, 0 skipped.
`git diff --check` passed and approved test diffs were empty before DONE.
`node porting/tools/mark.mjs <id> DONE --note ...` independently reran its required packet floor
and explicitly reported: "DONE is not VERIFIED."

Exact commands, stdout, stderr and exit codes are copied into the adjacent `direct-sprint-evidence/`
directory, with their original timestamped filenames. Original artifacts also remain under
`E:\Droppy\bakeoff`. Gate C/D incompleteness is baseline debt, not a claimed full pass.

## Translation boundaries and remaining work

- No invented product strings or numeric defaults were accepted. Test fixture labels are synthetic
  inputs, not application text. No quarantined implementation or new package/reference was added.
- RecentCache is a caller-owned C# class; TryGetValue/RemoveValue preserve Swift optional results.
  Future callers must not assume Swift struct copy semantics when sharing the cache instance.
- MergeRequestLink uses .NET Uri, Int64 and invariant parsing. General Foundation/.NET URL
  normalization equivalence is not established by the path fixtures; macOS oracle still needed.
- Coding fixtures cover compact recursive key sorting, slash escaping, UTC dates and lenient fields.
  Full Unicode key-order/escaping and date-parser parity have not been measured on macOS.
  Swift Lenient<T> is outside this packet's three requirements and remains to implement.
- Requests preserves role identity only. Plan resolution is PR-RUNTIME-138 in a later runtime
  packet. Secret masking and answer dispatch likewise belong to later UI/runtime work; no UI or
  logging of answers exists here. Symbol names are semantic IDs; licensed Windows glyphs remain open.
- No HIGH packet was executed, so no GLM review was due during this owner-directed worker bypass.
- No new balance measurement or worker cost claim is made for this sprint. Earlier bake-off cost
  evidence remains in `bakeoff-20260922.md`; previously launched worker requests may have separate usage.

## Resume after reset

1. Read HANDOFF and this report. Confirm clean canonical worktree and run verify/protected checks.
2. Preserve the old reference checkout. Audit the 127 changed upstream paths; update affected
   specification/read evidence before changing the pinned SHA. External inventories:
   `E:\Droppy\bakeoff\upstream-417ab7d3-source_inventory.json` and
   `E:\Droppy\bakeoff\upstream-417ab7d3-assignments.json` (mechanical inventory is NOT reading evidence).
3. The old bake-off is only 2/6 completed runs. T-M2-002 tests in isolated worktrees are based on
   changed Hydra source and must be reconciled before promotion. Ultra had one additional T002
   test rejection for missing JSON/Unicode coverage; no T002 DONE or final cost is claimed.
   T-M2-003 is unstarted. Do not declare a cleaner overall model from T001 alone.
4. Continue independent unchanged-source M2 packets while conducting the source audit. TokenSpend
   depends on ProviderKind, whose source changed; resolve that dependency before implementing it.
   JSONValue remains TODO; its 165-line source was inspected but no tests/code were written.
   ObservedValue remains TODO; interaction/render evidence and main-thread isolation need design
   consistent with the Core-layer restriction before accepting the packet.
5. Resume normal Ultra/DeepSeek/GLM routing after the temporary direct sprint, unless the owner
   extends the worker bypass. Use tool results and files for status; PC control is unnecessary.
