# T38 / RR-49 and RR-50 — 2026-10-07

Base: `origin/codex/ma-engine-review`, `7b910406886137947a101c742e35e94a527ec871`. Delivery branch: `codex/ma-gaps`, no force push. Initial RR-49/RR-50 commits: `fd42bc76`, `ca9a531c`. Lead-approved omission follow-up: `c77043e0`; generator instructions/readiness follow in a separate RR-50 commit. Owner law OD-0114..0126 was read from `origin/claude/verify-terminal-20261001`; audit rows were read from `origin/codex/release-audit`. The unrelated RR-37/design-source message was withdrawn and no design branch was merged.

| Row | Source status under lead decisions | Delivered / Windows verification |
| --- | --- | --- |
| RR-49 | **DONE** | Shared capability rule omits every unverified CLI from production head targets, the link-column model and Ask-others selection. Claude and Pi retain mandatory restricted-launch/runtime verification. Installed Windows safety and the merged W/view bindings remain to verify. |
| RR-50 | **DONE** | Lead accepted runtime OpenRouter pricing plus the dated official-page generator/schema and conservative stop while data is unknown. README lists exact Windows commands, source URLs and both output paths. The lead retrieves/reviews/commits page-only data locally; unknown/stale/unbounded prices stop before dispatch. |

Verdict: **READY for Windows verification** under the lead's latest RR-49/RR-50 decisions. The earlier BLOCKED source verdict is superseded by ED-NEW-ma-gaps-3/4. Full Windows verification and job W/view integration remain necessary for release. Standby injection/warm start grants no checking/opinion turn; unsupported CLI heads are omitted before selection and still refused at execution.

## Evidence that the rows were missing

- Engine-review `HandshakeStandbySession.cs:39-43` and E1 `TextOnlyAccountHead.cs:9-10` allow Claude and API/Local heads only. `CodexSession.StartInjectedStandby:361-365`, `PiSession.StartInjectedStandby:303` and `OpenCodeInjectionSession.StartInjectedStandby:46` do not run a model turn, so do not establish RR-49 safety.
- Engine-review `OpenAICompatibleSession.cs:232,538-547` has an optional, unassigned `KeySpendEstimator`; `AppApiKeyRouting.cs:40` binds rotation without prices; `ApiKeyPool.cs:119-120,290-302` records dollars only after a response. Token-only replies have no production dollar estimate or pre-dispatch reservation.
- Before the omission follow-up, ma-wire `c23e031b` `AccountConversationHeadHost.Accounts` returned every connected account and `AskOthersModel.Rows` every enabled non-primary account. E1 `8303f397` `LinkEngine.Eligible`/`OpinionsEngine.EveryoneWithRoom` use that host projection. The execution-time rejection alone did not remove unsafe offers. Current filters: `ConversationHeadCapabilities.cs:11`, `AccountConversationHeadHost.cs:25`, `AskOthersModel.cs:36`, `LinkColumnModel.cs:23`.

The primary identity remains available as the source of a completed answer for a safe checker. E1 also privately launches the active account for opinion Answer/Combine, so Ask-others is hidden when that source service is unverified. Ordinary primary routing, account priority, pins and Settings retain all connected services. No UI wording was introduced.

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
| Lead follow-up Core Accounts/Traceability + head offers | **325/325 passed**, including 21 Ask-others and 12 link-column cases |
| Lead follow-up Services affected checks + concrete head host | **73/73 passed** |
| Lead follow-up App Windows-targeted build | Passed, zero warnings/errors |
| Final wider API session regression | 268 passed, 22 failed; all 22 names **and error messages** match the earlier full run |
| Full Core, no exclusion filter | Aborted on Windows `kernel32.dll` process-launch failure after 1,919 passed / 191 failed / 2,110 completed |
| Full Services, no exclusion filter | 4,258 passed / 1,332 failed / 60 skipped / 5,650 total; completed |
| Final App build with Windows targeting | Passed, zero warnings/errors |
| Python generator fake-source tests | 3/3 passed |
| Executed Pi JS guard with fake ExtensionAPI | PASS: empty tools, every call blocked, relock, no-turn proof, refused invalid policy |

The full Services run preceded the last seven edge-case regressions/fixes: incomplete receipts and unsent permission revocation (4 failed / 0 passed), absent cache breakdown (1 failed / 0 passed), and inconsistent complete totals (2 failed / 0 passed). All are included in the final 72/72 and 290-case wider runs. The wider failures are shell/path/MCP/OpenRouter fixture constraints and match that full run exactly. The broad failures were not all independently triaged or compared with a clean base. They are not accepted as a release pass. Both new compiled Pi stand-in cases fail on Linux's unresolved Windows fixture path and need Windows execution; all other new Services cases in the full snapshot passed. Existing skips were not altered.

Other retained test-first witnesses: missing head/policy APIs, missing budget/pricing APIs, generator absence; overflowing source rate (1 failed / 2 passed), invalid OpenRouter discounts (3 failed / 6 passed). Initial new-file import/fixture-arithmetic corrections happened before implementation; no existing test assertion changed.

Lead follow-up test-first witnesses: 21/21 new Ask-others cases and the production-host case failed against the unchanged imported selectors; link-column tests failed compilation before the projection existed. The first green attempt exposed two Traceability failures for nested fake helpers; those helpers moved to separate fixture files with assertions intact. Final 325/73 runs include Traceability. Every test file already present at `ca9a531c` was compared byte-for-byte and is unchanged. All new cases use fakes only. New receipts: `/tmp/ma-gaps-head-{offers,selection}-{red,final}.log`, `/tmp/ma-gaps-link-offers-red.log`, `/tmp/ma-gaps-head-app-build.log`, and corresponding TRX under `/tmp/ma-gaps-results/`.

The required `node porting/tools/verify.mjs --json` was also run (`/tmp/ma-gaps-lead-verify.json`). It does not pass here: Gate A lacks `E:/Droppy/reference/droppy-code-v1.7.0`, the solution/requirement Gate D has no Windows-bound receipt, and repository-wide stub/parity/package gates remain incomplete on this base. This run is not a release acceptance receipt. Run the entrypoint on Windows with the required reference checkout and merged release work.

Commands use `dotnet test tests/Marrow.Core.Tests/Marrow.Core.Tests.csproj` and `dotnet test tests/Marrow.Services.Tests/Marrow.Services.Tests.csproj -p:EnableWindowsTargeting=true`; full runs omit filters. Final affected filter includes Accounts/Traceability in Core and ApiModelPriceAccounting, ModelPriceCatalog, OpenRouterPriceSource, KeyRotation, TextOnlyAccountHeadGap, PiTextOnlyHeadPolicy, HandshakeStandby and Traceability in Services. The wider final filter additionally includes all OpenAICompatibleSession and OpenRouterSession cases. Full logs/TRX are under `/tmp/ma-gaps-*.log` and `/tmp/ma-gaps-results/` in this execution workspace.

## Windows work and overlap

1. Run complete Core/Services suites, the new compiled `PiTextOnlyHeadSessionTests`, and existing Pi session/standby suites. The App build alone is not runtime verification.
2. Prove installed Pi fresh private identity/history, empty tools and no actions/file edits/MCP/skill discovery, missing/invalid-proof stop, revocation/cancellation and disposal. Claude keeps its existing restricted-launch verification. All other CLI heads remain omitted; a future adapter requires verified runtime safety before any deliberate allowlist change.
3. Run the official-page generator locally, review exact API model identifiers, units, bounds and all billing conditions, commit JSON plus compiled C#, and rebuild. Verify priced token-only DeepSeek/Meta/Z.ai and OpenRouter receipts/caps against authoritative billing; exercise concurrent heads, price expiry/failure, unknown receipts, settings changes, local-month rollover and restart.
4. Job W must retain both budget-forwarding wrappers and the shared pool for regular and private heads. Verify cross-service consent/context epochs through price fetch and dispatch.
5. With the W/views branches joined, verify link-column binding uses `LinkColumnModel.Rows`/`ThreadAccount.CanBeConversationHead`, while normal account rows remain complete. Verify Ask-others rows, @all, stale taps/links, disabled/out/capped accounts and hidden Ask-others for an unverified active private Answer/Combine head. The link model accepts the same live `hasRoom` callback for API caps as the Ask-others model.

Overlap: E1/W own `TextOnlyAccountHead`, collector progress/startup guards, `AccountConversationHeadHost` and `PinnedConversationKeyRotation`; preserve the filtered host, Pi addition and `Budget` forwarding. W's `AppApiKeyRotation` needs the one-line `Budget` forwarder. The engine-review base lacked the selection types, so 17 existing W production/contract/model files were carried from `c23e031b` without merging the branch or copying views. Preserve the Ask-others eligibility change when joining ma-opinions-view/W; the new link-column projection shares that rule. ThreadAccount's no-five-hour boolean reads the same reported flag directly to avoid importing unrelated Settings wording. Engine-stack's Windows work overlaps `PiSession`/`PiCLI`; existing standby injection behavior was not changed. No view, design-source or owner-law files were edited.

ED entries: `ED-NEW-ma-gaps-1` through `ED-NEW-ma-gaps-4`. Historic entries remain unchanged; the new entries record the lead's superseding decisions. Historical ledger prefix is preserved byte-for-byte: 1,919,069 bytes, SHA-256 `230161cf5d0dbaacbad22a3949e63b5d5c9a7e2289d4084cce5791c2b65ab4af`.
