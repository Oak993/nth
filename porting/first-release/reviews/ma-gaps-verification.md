# T38 / RR-49 and RR-50 — 2026-10-07

Base: `origin/codex/ma-engine-review`, `7b910406886137947a101c742e35e94a527ec871`. Delivery branch: `codex/ma-gaps`, no force push. RR-49 commit: `fd42bc76`. RR-50 is the following commit. Owner law OD-0114..0126 was read from `origin/claude/verify-terminal-20261001`; audit rows were read from `origin/codex/release-audit`. The unrelated RR-37/design-source message was withdrawn and no design branch was merged.

| Row | Release status | Delivered / remaining |
| --- | --- | --- |
| RR-49 | **BLOCKED** | Fresh Pi text-head adapter uses CLI no-tools/resource flags, one app-owned unconditional tool guard, and nonce-bound runtime proof before the question. Other unverified CLI heads still refuse explicitly. Installed Windows Pi proof and safe head capabilities for the remaining approved CLIs are absent. |
| RR-50 | **BLOCKED** | Exact dated pricing, runtime OpenRouter pricing API, persistent shared pre-dispatch cap reservations, token-only accounting, and the official-page generator/schema are implemented. Page-only data is explicitly unfilled; the Windows lead must retrieve/review/commit it and verify live accounting. Unknown/stale/unbounded prices stop before dispatch. |

Verdict: **NOT READY** for release. Safe refusal is the fallback; standby injection/warm start does not permit an opinion/checking model turn. No alternate service/account send is implied. Full Windows verification and job W integration remain necessary.

## Evidence that the rows were missing

- Engine-review `HandshakeStandbySession.cs:39-43` and E1 `TextOnlyAccountHead.cs:9-10` allow Claude and API/Local heads only. `CodexSession.StartInjectedStandby:361-365`, `PiSession.StartInjectedStandby:303` and `OpenCodeInjectionSession.StartInjectedStandby:46` do not run a model turn, so do not establish RR-49 safety.
- Engine-review `OpenAICompatibleSession.cs:232,538-547` has an optional, unassigned `KeySpendEstimator`; `AppApiKeyRouting.cs:40` binds rotation without prices; `ApiKeyPool.cs:119-120,290-302` records dollars only after a response. Token-only replies have no production dollar estimate or pre-dispatch reservation.

## Sources and maintenance

Pi's CLI/runtime contract was inspected read-only on 2026-10-07: [pi-mono 2db5e359](https://github.com/earendil-works/pi-mono/tree/2db5e359bf84c1c0be51d2c5c5c5c7cf27072c2b/packages/coding-agent), `docs/cli.md`, `docs/extensions.md`, `src/cli/args.ts`, extension types and RPC handler. No real CLI/model was invoked.

OpenRouter's official pricing/limit/routing contracts were inspected read-only on 2026-10-07: [typescript-sdk 3c88d707](https://github.com/OpenRouterTeam/typescript-sdk/tree/3c88d7075ba91b7b7fbca33073126873823131ed/src/models), `model.ts`, `publicpricing.ts`, `providerpreferences.ts`. `/api/v1/models` prices are USD per token; `provider.max_price` prompt/completion prices are USD per million tokens, request prices USD per request. No current production rate was retrieved in this cloud.

The page-only table has no prices. See [pricing README](../../../src/Marrow.Services/Providers/Pricing/README.md) and `porting/tools/generate-model-prices.py` for Windows commands, exact cell mappings, source dates/hashes, failure preservation and the 24-hour freshness rule. The compiled table requires daily reviewed regeneration/rebuild, or a future reviewed updater; expiry stops capped dispatch. Auto routing, conditional/extra OpenRouter charges, non-text billing and missing authoritative limits are unavailable to this flat-token bound. Full model-window/output maxima are conservative bounds, not guessed token counts.

Reservations persist in account-key metadata before HTTP. Actual receipts settle the original key/send month once; interrupted or incomplete receipts retain their hold across restart/month changes. Unknown uncapped receipts also block a subsequently enabled finite cap. There is no automatic expiry or receipt reconciliation UI. Permission/cap changes are rechecked before transport; a proven unsent cancellation releases its hold.

## Validation

SDK 10.0.401; .NET 10; Linux cloud. No SDK/project/package pins changed. New tests use fake sessions, in-process fake HTTP, fake ExtensionAPI and fake HTML; no real provider, CLI, model or credentials. Existing test files, assertions and tolerances are unchanged.

| Check | Result |
| --- | --- |
| Initial existing standby baseline | 18/18 passed |
| RR-49 head policy/standby/traceability | 37/37 passed |
| Final Core Accounts + Traceability | 292/292 passed |
| Final Services gap/pricing/key rotation/standby/traceability | 72/72 passed |
| Final wider API session regression | 268 passed, 22 failed; all 22 names **and error messages** match the earlier full run |
| Full Core, no exclusion filter | Aborted on Windows `kernel32.dll` process-launch failure after 1,919 passed / 191 failed / 2,110 completed |
| Full Services, no exclusion filter | 4,258 passed / 1,332 failed / 60 skipped / 5,650 total; completed |
| Final App build with Windows targeting | Passed, zero warnings/errors |
| Python generator fake-source tests | 3/3 passed |
| Executed Pi JS guard with fake ExtensionAPI | PASS: empty tools, every call blocked, relock, no-turn proof, refused invalid policy |

The full Services run preceded the last seven edge-case regressions/fixes: incomplete receipts and unsent permission revocation (4 failed / 0 passed), absent cache breakdown (1 failed / 0 passed), and inconsistent complete totals (2 failed / 0 passed). All are included in the final 72/72 and 290-case wider runs. The wider failures are shell/path/MCP/OpenRouter fixture constraints and match that full run exactly. The broad failures were not all independently triaged or compared with a clean base. They are not accepted as a release pass. Both new compiled Pi stand-in cases fail on Linux's unresolved Windows fixture path and need Windows execution; all other new Services cases in the full snapshot passed. Existing skips were not altered.

Other retained test-first witnesses: missing head/policy APIs, missing budget/pricing APIs, generator absence; overflowing source rate (1 failed / 2 passed), invalid OpenRouter discounts (3 failed / 6 passed). Initial new-file import/fixture-arithmetic corrections happened before implementation; no existing test assertion changed.

Commands use `dotnet test tests/Marrow.Core.Tests/Marrow.Core.Tests.csproj` and `dotnet test tests/Marrow.Services.Tests/Marrow.Services.Tests.csproj -p:EnableWindowsTargeting=true`; full runs omit filters. Final affected filter includes Accounts/Traceability in Core and ApiModelPriceAccounting, ModelPriceCatalog, OpenRouterPriceSource, KeyRotation, TextOnlyAccountHeadGap, PiTextOnlyHeadPolicy, HandshakeStandby and Traceability in Services. The wider final filter additionally includes all OpenAICompatibleSession and OpenRouterSession cases. Full logs/TRX are under `/tmp/ma-gaps-*.log` and `/tmp/ma-gaps-results/` in this execution workspace.

## Windows work and overlap

1. Run complete Core/Services suites, the new compiled `PiTextOnlyHeadSessionTests`, and existing Pi session/standby suites. The App build alone is not runtime verification.
2. Prove installed Pi fresh private identity/history, empty tools and no actions/file edits/MCP/skill discovery, missing/invalid-proof stop, revocation/cancellation and disposal. Prove a complete no-tools runtime contract for every other approved CLI before enabling its checking/opinion head. Claude keeps its existing restricted-launch verification.
3. Run the official-page generator locally, review exact API model identifiers, units, bounds and all billing conditions, commit JSON plus compiled C#, and rebuild. Verify priced token-only DeepSeek/Meta/Z.ai and OpenRouter receipts/caps against authoritative billing; exercise concurrent heads, price expiry/failure, unknown receipts, settings changes, local-month rollover and restart.
4. Job W must retain both budget-forwarding wrappers and the shared pool for regular and private heads. Verify cross-service consent/context epochs through price fetch and dispatch.

Overlap: E1/W own `TextOnlyAccountHead`, collector progress/startup guards and `PinnedConversationKeyRotation`; preserve E1's host and merge the Pi addition plus `Budget` forwarding. W's `AppApiKeyRotation` needs the one-line `Budget` forwarder. Engine-stack's Windows work overlaps `PiSession`/`PiCLI`; existing standby injection behavior was not changed. No views, design sources or owner-law files were edited.

ED entries: `ED-NEW-ma-gaps-1` and `ED-NEW-ma-gaps-2`. Historical ledger prefix is preserved byte-for-byte: 1,919,069 bytes, SHA-256 `230161cf5d0dbaacbad22a3949e63b5d5c9a7e2289d4084cce5791c2b65ab4af`.
