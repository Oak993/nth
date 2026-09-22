# Direct continuation checkpoint, 2026-09-22

The owner extended the direct sprint at approximately 03:01 UTC: continue with workers bypassed
while waiting for a ChatGPT reset. Codex continued directly; no Cast/subagent, GLM, paid-worker,
computer-control or credential-reading call was made. The active model cannot be switched or
independently confirmed as an Ultra setting through the tool interface; no such switch is claimed.
This continuation supersedes the original sprint report's instruction to resume worker routing.

## Result

Five additional STANDARD packets are DONE on `port/windows-exact-parity`:

| Packet | Scope | Approved test commit(s) | Feature commit |
| --- | --- | --- | --- |
| T-M2-023 | MCP model fields, categories, substitution, transport resolution, connection JSON and value semantics | f7ca6cf, 09ea533 | a520e33 |
| T-M2-025 | Catalog entries 1-10 | 359d398 | b3d976a |
| T-M2-026 | Catalog entries 11-20 | d43d0d0 | 0d388ea |
| T-M2-027 | Catalog entries 21-30 | 3402f8d | 3b24007 |
| T-M2-028 | Catalog entries 31-33, IDs, exact/normalized lookup and grouping | d3be35f | 7c0a180 |

Additional T-M2-023 source-derived regressions: standalone tool decoding tests `081e8b9`, fix
`84685b7`; null-root tests `f3a21f2`, fix `1e07e6f`; optional-null encoding tests `0a2d9d3`,
fix `8d1ac03`. All approved tests remain unchanged.

Canonical totals: 10 DONE, 652 TODO; M2 10/78 (12.82%) complete, 68/78 (87.18%) remaining.
All packets 10/662 (1.51%) complete; 98.49% remain. Requirements 68/3827 IMPLEMENTED (1.78%),
0 VERIFIED. Latest clean build: 135 tests passed, 0 failed, 0 skipped, warnings treated as errors.
The continuation adds 47 implemented requirement IDs and 57 tests to the previous checkpoint.
These percentages count packets and requirements, not equivalent work effort or app usability.

## Source evidence and specification correction

Read all 263 lines of pinned MCP.swift and all 483 lines of MCPCatalog.swift. MCPCatalog is
unchanged in the latest snapshot: both commits contain blob `4153cedff51deea3497b12143993ce943a70b0a6`.
MCP.swift changes from blob `6a4842a22f71257d333c4d60fcf129fdd458da30` to
`5538aaf7fe897a490f1f8a6e3251a52384214177`. The entire diff is one substitution at line 141:
`value.trimmingCharacters(in: .whitespacesAndNewlines)` becomes `value.trimmed`.
Latest Text.swift:132 defines `trimmed` as exactly that trimming operation. This supports unchanged
MCP behavior; it does not certify the rest of the changed Text.swift or the new source tree.

PR-MCP-036 incorrectly specified that "Hugging Face" lookup returns nil. Source lines 462-473
try normalized IDs first, then normalized names. Both the argument and the catalog name become
"hugging-face", so the name pass returns the huggingface entry. The original read ledger already
described this correctly. Commit `caf51d2` corrects the authored requirement and verification text,
then regenerates the parity matrix and T-M2-028 before its tests are written. No approved test was
changed and no source behavior was redesigned. Negative cases now cover extra whitespace and
unsupported punctuation, which the source does not normalize.

The global pin remains `dd2b86efef9e77168f304a86e8a8411877578bbe`. The latest candidate remains
`417ab7d3676cdf258fb6b68444f0626aa6fdfbed`; the broader 127-path reading/spec audit is unfinished.
The refreshed M2 blob map reports 9 IDENTICAL/DONE, 1 CHANGED/DONE (reviewed above),
28 IDENTICAL/TODO and 40 CHANGED/TODO. Unchanged source can still depend on changed types.

## Requirement mapping

Core test classes are under `tests/Marrow.Core.Tests/Models`; resolver tests under
`tests/Marrow.Services.Tests`. Every catalog snapshot compares all 19 projected fields, including
all nested fields, transport arguments/headers, environment values, source order and derived flags.
Expected tables were manually frozen from packet Expected text before implementation. A separate
restricted Swift literal parser emitted the production table from Swift. Fixtures were not generated
from that parser or from production code. Each resulting C# diff was read against source.

| Requirement ID | Proof |
| --- | --- |
| PR-MCP-038 | MCPTests.Category_order_ids_and_titles_are_verbatim |
| PR-MCP-039 | MCPTests.Fill_uses_field_order_replaces_all_occurrences_and_preserves_unknown_tokens |
| PR-MCP-040 | MCPTests.Host_normalization_matches_the_ordered_source_scheme_loop (five cases) |
| PR-MCP-041 | MCPResolverTests.Stdio_fills_args_and_env_values_but_never_command_or_env_keys |
| PR-MCP-042 | MCPResolverTests.Http_keeps_Bearer_space_but_drops_a_wholly_empty_header |
| PR-MCP-043 | MCPResolverTests.OAuth_calls_only_the_proxy_route_supplier_and_keeps_upstream_separate |
| PR-MCP-044 | MCPTests.OAuth_URL_and_input_flags_are_derived_from_the_transport_and_required_fields |
| PR-MCP-045 | Same method's no-fields, optional-fields and required-fields assertions |
| PR-MCP-046 | Same method and all three MCPResolverTests assert derived transport flags |
| PR-MCP-047 | MCPTests.Connection_JSON_uses_exact_keys_and_restarts_with_date_tools_and_values; malformed field cases; MCPToolSummaryCodingTests and MCPNullRootTests |
| PR-MCP-001 | MCPCatalogTests.First_ten_entries_match_the_complete_frozen_expected_table, github row |
| PR-MCP-002 | Same fixture, gitlab row |
| PR-MCP-003 | Same fixture, playwright row |
| PR-MCP-004 | Same fixture, chrome-devtools row |
| PR-MCP-005 | Same fixture, context7 row; MCPCatalogResolutionTests preserves empty-key Bearer-space |
| PR-MCP-006 | Same fixture, filesystem row; Filesystem_home_default_uses_the_Windows_user_profile |
| PR-MCP-007 | Same fixture, fetch row |
| PR-MCP-008 | Same fixture, memory row |
| PR-MCP-009 | Same fixture, sequential-thinking row |
| PR-MCP-010 | Same fixture, git row |
| PR-MCP-011 | MCPCatalogSecondTests.Second_ten_entries_match_every_expected_field, brave-search row |
| PR-MCP-012 | Same fixture, tavily row |
| PR-MCP-013 | Same fixture, exa row |
| PR-MCP-014 | Same fixture, firecrawl row |
| PR-MCP-015 | Same fixture, perplexity row |
| PR-MCP-016 | Same fixture, slack row; MCPCatalogSlackTests checks the literal flag and token substitution |
| PR-MCP-017 | Same fixture, notion row |
| PR-MCP-018 | Same fixture, linear row |
| PR-MCP-019 | Same fixture, atlassian row |
| PR-MCP-020 | Same fixture, figma row |
| PR-MCP-021 | MCPCatalogThirdTests.Third_ten_entries_match_every_expected_field, sentry row |
| PR-MCP-022 | Same fixture, vercel row |
| PR-MCP-023 | Same fixture, cloudflare row |
| PR-MCP-024 | Same fixture, netlify row |
| PR-MCP-025 | Same fixture, supabase row |
| PR-MCP-026 | Same fixture, stripe row; actual challenge parsing remains later OAuth work |
| PR-MCP-027 | Same fixture, resend row |
| PR-MCP-028 | Same fixture, postgres row; MCPCatalogDatabaseTests exact resolved argv |
| PR-MCP-029 | Same fixture, sqlite row; MCPCatalogDatabaseTests preserves a path containing spaces |
| PR-MCP-030 | Same fixture, mongodb row including U+2192; MCPCatalogDatabaseTests env-only substitution |
| PR-MCP-031 | MCPCatalogFinalTests.Last_three_entries_match_every_expected_field, huggingface row; final resolution test |
| PR-MCP-032 | Same fixture, deepwiki row; final resolution test asserts empty headers |
| PR-MCP-033 | Same fixture, aws-docs row; final resolution test asserts ERROR |
| PR-MCP-034 | Catalog_has_exactly_33_ids_in_source_order_and_the_fourteen_OAuth_entries |
| PR-MCP-035 | Exact_id_lookup_is_case_sensitive_and_returns_null_for_unknown_ids |
| PR-MCP-036 | Normalized_lookup_uses_only_lowercase_underscore_and_space_replacement (13 cases); director source review of two-pass precedence |
| PR-MCP-037 | Categories_and_their_members_preserve_both_source_orders |

MCPValueSemanticsTests separately proves copied input collections, content equality/hashing,
order-insensitive dictionary equality and sensitivity to changed scalar/collection contents.

## Commands and observed failures

Each packet followed ASSIGNED -> tests-first red -> TESTS_REVIEW -> test commit -> IMPLEMENTING
-> gate checks -> source diff review -> DONE -> feature commit. The runner command was
`node E:\Droppy\bakeoff\run-packet-gates.mjs main <packet>`, executing protected checks and
`verify.mjs --gate A/B/C/D --json` with a throwaway MARROW_PORT_DATA_DIR. DONE independently
reran its required build/test floor. `git diff --check` passed; approved-test diffs stayed empty.

Observed failures and fixes, not hidden passes:

- Initial MCP model compile: C# 14 reserves `field` inside property accessors. Renamed the lambda
  local to `item`; no test change.
- Collection value-semantics regression: 3 failures before copying/content equality was implemented.
- Catalog second/third batches: each new fixture and resolver test failed against the shorter table,
  then passed after entries were added.
- Standalone tool-summary decode: 2 failures (missing and null name) before a strict converter.
- Null JSON roots: 2 failures before opting converters into null handling and rejecting nonobjects.
- Optional-null encoding: 2 failures before retaining explicit null output for absent optional values,
  independently of the rejection of null when decoding a required Swift struct.
- The final null-root gate run detected the director's uncommitted source-map report refresh.
  This was an administrative protected-path change, not a worker edit; it is committed with this
  report before the final protected/gate rerun. That failed artifact remains evidence, not a pass.

All final checks: A 12/12; B 8/8; quarantine guard passes. Full C still fails on the two original
App stub markers and has one unfinished acceptance check. D build/test floor passes but its full
acceptance check is NOT_IMPLEMENTED. E/F remain incomplete. No claim of full app acceptance.
Exact timestamped command/output JSON files are copied under `direct-continuation-evidence/`.
The final accepted run is `main-T-M2-023-gates-1790047448205.json`: protected exit 0,
A12/B8, quarantine pass and 135 tests passing. The intermediate clean 133-test rerun is
`main-T-M2-023-gates-1790047399691.json`.

## Scope and next work

- The catalog is data. No server was launched, no network connection made, no message sent, and
  no real credential read by product tests. URI strings and sample command names are verbatim data.
- Per architecture V1, the resolver lives in Services and receives a lazy proxy-route supplier.
  Real port-file persistence, listener lifetime, sign-in, token refresh and config writers are later
  Services packets. Tests prove the boundary and token separation, not a running OAuth system.
- Connection JSON round-trips through memory. Full app restart, card rendering, icon licensing,
  secret filtering at the store boundary and process lifecycle still need their integration packets.
- No new user-visible string or numeric default was invented. Windows home-directory resolution
  follows the packet's explicit adaptation. Exhaustive Unicode lowercasing/trimming parity with
  Darwin is not established by these fixtures; a macOS oracle remains useful.
- JSONValue (T-M2-064) remains TODO. Primary Swift Foundation research exposed numeric bridging,
  number description and pretty/sorted serialization details that must not be guessed; no code
  or tests were written for it. Research reference:
  https://raw.githubusercontent.com/swiftlang/swift-corelibs-foundation/main/Sources/Foundation/JSONSerialization.swift
- T-M2-042 has a specification routing defect: PR-PROVREG-075..082 cite ProviderSettings.swift,
  but that file is only a 12-line interface. ProviderStatus is actually at
  Services/Providers/ProviderRegistry.swift:3-28. Correct evidence/routing before implementing;
  PR-PROVREG-083 also depends on ProviderKind, whose upstream source changed. No guessed enum added.
- CaptureRun (T-M2-058) has an open capture-mode engineering decision and depends on the settings
  suite/store boundary. SwitchLatency (T-M2-074) needs an evidenced run-loop adapter. Neither was
  marked assigned or done merely because its Swift file is short.
- Next session: retain worker bypass, read HANDOFF, confirm clean checkout and gates, then continue
  the upstream audit or another source-settled packet. Do not rerun the external catalog emission
  helper blindly: its output contains the data only and would overwrite the completed lookup methods.
