# Direct worker-bypass checkpoint, 2026-09-22

The owner said "Bypass workers again, continue." Codex implemented and reviewed directly.
No worker, GLM, paid model, PC-control, app-launch or credential-file call was made in this run.
The HIGH packet received a separate adversarial pass by the director, not independent review.
No model-setting change or confirmed Ultra setting is claimed.

## Result

| Packet | Approved tests | Implementation | Added tests |
| --- | --- | --- | --- |
| T-M2-071 | 5f6d743; regression 68f4d76 | f5ef2bb | 53 |
| T-M2-075 | 3e45ce0, 86101fa, edcec0e; regression e4b9e4d | 7cd5125 | 47 |
| T-M2-076 | 30d4ad5 | a06858e | 15 |

One additional T-M2-023 MCP whitespace regression was approved in 3e45ce0 and fixed in 7cd5125.
Total increase: **3 packets, 17 requirements, 116 tests**. Current solution: **251 passed,
0 failed, 0 skipped**. No approved test was weakened or changed to accept the implementation.

Queue: 13 DONE / 662 (1.96%), 649 TODO (98.04%). M2: 13 DONE / 78 (16.67%),
65 TODO (83.33%). Requirements: 85 IMPLEMENTED / 3827 (2.22%), **0 VERIFIED / 3827**.
Percentages count packets/requirements, not effort or usable application completion.

## Source and upstream

Read StdioFramer.swift 1-81, Text.swift 1-129, MCP.swift 1-263, the FileEdit carrier at
Timeline.swift 170-175 and project.yml 1-65. Read the old-to-candidate diffs for these files.
Other source exploration (not implementation completion): HydraDelegationStream.swift 1-226
and its diff, JSONValue.swift 1-165, and the ProviderSettings packet/source mismatch recorded
in the previous continuation. Do not infer a complete reread of latest upstream from these reads.

`git ls-remote https://gitlab.com/droppyformac1/droppy-code.git refs/heads/main`, at approximately
04:00 UTC, still returned `417ab7d3676cdf258fb6b68444f0626aa6fdfbed`.
The pinned contract/Gate A remains `dd2b86efef9e77168f304a86e8a8411877578bbe`.

- StdioFramer is byte-identical in both snapshots, blob e1ebde91ece8d47b9de5d259b99efb10733b26db.
- Text.swift adds only String.trimmed; no implemented behavior changes. The new standalone
  extension is not yet claimed as a latest-upstream requirement.
- MCP changes its inline whitespace/newline trim to that equivalent helper.
- Timeline changes attachments and Hydra merge outcomes; FileEdit's carrier is unchanged.
- M2 source map now has 10 IDENTICAL/DONE, 3 CHANGED/DONE, 27 IDENTICAL/TODO, 38 CHANGED/TODO.
  The three changed DONE packets have individually reviewed equivalent covered behavior.

## Requirement-to-test map

All names below refer to tests under tests/Marrow.Core.Tests.

| Requirement | Tests proving implemented behavior |
| --- | --- |
| PR-PROVACP-149 | StdioFramerTests.Lines_split_only_on_LF_preserving_CR_empty_messages_and_arbitrary_bytes; Lines_survive_every_two_chunk_split_without_duplication |
| PR-PROVACP-150 | One_MiB_byte_at_a_time_is_intact_and_does_not_take_quadratic_time (both modes); One_MiB_header_byte_at_a_time_does_not_rescan_its_entire_prefix; Input_and_returned_messages_do_not_alias_the_mutable_remainder |
| PR-PROVACP-151 | Content_length_survives_every_pair_of_chunk_boundaries_including_separator_overlap; Body_length_counts_bytes_not_characters_and_preserves_nontext_bytes; Only_the_literal_CRLFCRLF_ends_a_header; Invalid_header_does_not_consume_a_body_and_garbage_becomes_part_of_the_next_header |
| PR-PROVACP-152 | Malformed_header_is_discarded_as_a_whole_then_next_header_is_read; Swift_header_name_trimming_integer_and_first_matching_line_rules_are_preserved; Zero_length_is_a_complete_empty_message; A_valid_64_bit_length_waits_for_its_body_instead_of_being_rejected_or_allocated_up_front; A_colon_with_a_combining_mark_is_not_a_colon_separator; StdioFramerWhitespaceTests |
| PR-PROVACP-153 | Finish_flushes_only_nonempty_line_remainder_and_resets_scanning; Finish_discards_partial_header_or_body_and_resets_all_content_length_state |
| PR-STORE-008 | AppInfoTests.Packaged_development_flag_selects_the_exact_hidden_state_directory; AppInfoBuildChannelTests.Cached_application_info_uses_the_compiled_build_channel |
| PR-STORE-009 | AppInfoTests.Missing_and_wrongly_typed_metadata_use_the_three_literal_fallbacks; Present_strings_including_empty_ones_are_snapshotted_without_normalization; Assembly_metadata_uses_the_original_three_keys_and_an_explicit_build_channel |
| PR-STORE-105 | TextCleanupTests.Four_long_dashes_become_hyphens_with_spacing_and_en_dash_preserved |
| PR-STORE-106 | Dash_free_or_nonmatching_grapheme_text_is_returned_by_identity; Byte_prescan_copies_the_exact_iterator_consumption_rules; Dash_free_model_prose_throughput_guard |
| PR-STORE-107 | ANSI_cleanup_removes_only_the_source_CSI_pattern; TextCleanupUnicodeTests.ANSI_pattern_retains_Swift_grapheme_boundaries_and_canonical_equivalence |
| PR-STORE-108 | Last_lines_strips_ANSI_trims_each_line_discards_blanks_and_defaults_to_four |
| PR-STORE-109 | Summary_uses_120_graphemes_with_the_exact_single_ellipsis; Summary_skips_leading_newlines_but_does_not_skip_a_whitespace_only_first_line; Summary_counts_extended_graphemes_not_UTF16_units_or_Unicode_scalars |
| PR-STORE-110, PR-STORE-111 | SimpleDiffTests.Exact_200000_UTF8_input_sum_limits_only_hunk_storage; Limit_is_bytes_and_over_limit_counts_still_trim_common_prefix_and_suffix |
| PR-STORE-112 | One_hunk_preserves_three_old_context_lines_and_removals_before_additions; Empty_identical_and_CRLF_inputs_keep_the_source_hunk_quirks; Canonically_equivalent_lines_are_context_in_their_original_old_spelling |
| PR-STORE-113 | FileEditTextTests.Convenience_constructor_passes_the_path_and_all_three_computed_stats_through; Over_cap_edit_keeps_the_counts_while_omitting_diff_from_storage; Memberwise_defaults_and_exact_storage_keys_survive_a_round_trip; Invalid_persisted_edit_is_rejected_without_inventing_defaults; Explicit_null_diff_and_integral_JSON_number_follow_the_storage_decoder |
| PR-STORE-114 | Nil_if_empty_preserves_every_nonempty_string_by_identity; Nil_if_empty_returns_null_for_only_the_empty_string |
| PR-MCP-040 (existing packet regression) | MCPWhitespaceTests.Host_placeholder_trims_Foundation_zero_width_space_before_removing_scheme_and_slash |

## Review findings and corrections

1. PR-PROVACP-152: first implementation used Unicode Zs plus tab, following the Foundation API
   comment. The actual CFUniChar.c whitespace implementation includes U+200B. The separately
   committed test failed with `Assert.Single() Failure: The collection was empty`; fixed the
   production character set. The same source-derived correction was applied to MCP host trimming.
2. PR-STORE-107: a direct .NET Regex transcription passed ordinary CSI fixtures but violated Swift
   grapheme/canonical-equivalence semantics. The separately committed five-case regression had
   three failures (combining final character, Greek question mark, Kelvin sign). Implemented the
   exact fixed pattern over NFC graphemes; all five pass. No broader ANSI forms were added.
3. Each final production diff was reread against source after gate checks. No remaining known
   discrepancy in the packet scopes. No claim of independent GLM approval or macOS runtime parity.

Primary supporting implementation sources:
[CoreFoundation whitespace](https://raw.githubusercontent.com/swiftlang/swift-corelibs-foundation/main/Sources/CoreFoundation/CFUniChar.c),
[Swift split](https://raw.githubusercontent.com/swiftlang/swift/main/stdlib/public/core/Collection.swift),
[Character newline semantics](https://raw.githubusercontent.com/swiftlang/swift/main/stdlib/public/core/CharacterProperties.swift),
[Swift regex custom classes](https://github.com/swiftlang/swift-evolution/blob/main/proposals/0363-unicode-for-string-processing.md#custom-classes).
These establish library semantics, not a matching macOS runtime oracle.

## Engineering decisions, not source facts

- Use an owned framer instance with an amortized byte-array buffer, Int64 declared lengths, and
  copied output arrays. Capacity growth is implementation detail, not a protocol limit.
  Swift struct assignment-copy semantics are not exposed by this class API; callers must own a
  framer per stream. No subprocess consumer exists yet.
- Windows application metadata is stored as AssemblyMetadata attributes with the three original
  dictionary keys, cached once in AppInfo.Current. DEBUG is the development-channel build flag,
  matching the pinned project.yml Debug/Release selection. Source version/build 1.6.0/19 remain
  literal. OD-0002 changes Droppy Code / Droppy Code Dev to Marrow / Marrow Dev. No project/package
  file changed. This does not settle the shipping data-directory collision or migration decisions.
- FileEdit.Stats is flattened to FileEditStats; the FileEdit carrier uses Int64 counters and exact
  Codable field names. It borrows only the necessary six-line source type, not the rest of Timeline.
- The UTF-8 prescan is callable for malformed-byte fixtures, which cannot be represented by a valid
  Swift string. Ten-second test budgets are generous infrastructure guards, not invented product
  timeouts. Literal product numbers and user-facing strings are source-derived or the approved rename.
- Guessed product behavior: zero. Real provider integration, the seven prose-cleanup call sites,
  UI captures, matching-macOS Unicode/runtime behavior and packaging acceptance remain unverified.

## Commands and authentic outcomes

All test/build/gate commands used a fresh MARROW_PORT_DATA_DIR under TEMP; no app was launched.

- Start: `node porting/tools/verify.mjs` returned full acceptance exit 1, PASS 22 / FAIL 1 /
  NOT_IMPLEMENTED 4; build floor 135 tests. `node porting/tools/check_protected.mjs` passed.
  The one failure was the existing Program.cs entry stub; no previously passing gate regressed.
- Before implementations: filtered `dotnet test tests/Marrow.Core.Tests/Marrow.Core.Tests.csproj
  --filter ... --no-restore` calls produced missing-type/member CS0246/CS0103/CS1061 errors as
  expected. New regression tests were added before fixes; failures described above are actual outputs.
- `node E:/Droppy/bakeoff/run-packet-gates.mjs main T-M2-071`: final floor 188 tests.
- Same command for T-M2-075: final floor 236 tests.
- Same command for T-M2-076: final floor 251 tests.
- Every packet: protected unchanged/reference clean, A 12/12, B 8/8, quarantine guard passed,
  warnings-as-errors build clean. C/D full acceptance exit 1 is expected; the unchanged app stub and
  not-yet-built acceptance checks are reported, never treated as full passes.
- `git diff --check` passed. `git diff -- tests/` was empty after implementation.
- Release build/test and a read-only assembly-metadata probe exercised both build channels.
  The probe did not execute the app. Its initial file-based build emitted IL2026 about reflection
  under trimming; this is a standalone audit-helper warning, not a solution-build warning.

Raw packet and final logs are in `direct-bypass-evidence/`. Final logs bind results to implementation
commit a06858e and include command, cwd, timestamp, environment, exit, stdout and stderr.

## Remaining work / resume

Remain on the canonical branch and keep workers bypassed until the owner changes that instruction.
Rerun setup/protected checks, then continue M2. Do not repeat the incomplete model bake-off during
the bypass. Its earlier measurements remain in the original reports; this run adds no model cost
measurements and makes no fresh account-balance claim.

The broad latest-upstream reread/spec refresh is still open. JSONValue is an important next dependency
but its Foundation number boxing, sorted pretty output and Swift Double rendering need exact evidence.
Hydra/Provider have real changes in latest upstream; avoid silently implementing only old behavior.
MCP paths/state needs the store-root boundary; ProviderSettings packet has misplaced ProviderStatus
citations; Shortcuts needs the recorded Windows modifier decisions. M0 desktop probes and all full
app/visual/install acceptance remain incomplete. See the existing registers for individual blockers.

Status: **INCOMPLETE**. This checkpoint is implemented foundation code, not a finished application.
