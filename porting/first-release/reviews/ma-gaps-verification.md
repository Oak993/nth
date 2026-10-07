# T38 RR-49 / RR-50 — ma-gaps Windows handoff

**RR-49 DONE. RR-50 DONE. Verdict: READY for T9's Windows verification.** This is the lead-authorized engine handoff, not full release acceptance. Page-only production prices remain explicitly unfilled until the lead generates and commits them locally; unknown prices stop capped dispatch.

Merged origin/claude/verify-terminal-20261001 at 228f56b55d2223e36f8e8ec770d78848311b96e7 first (31c16a3c). That target contained T9's explicit rollback bd0ac8c1 of the held ma-gaps patch. The engine patch is restored over the newer stack with the pricing repair and production head-supply boundary. The superseded direct AskOthersModel filter, standalone LinkColumnModel and their held model-filter tests were not reinstated after the upstream rollback. Existing tests, assertions, tolerances and fixtures remain unchanged.

## Evidence that the release rows were missing

- RR-49: engine-review HandshakeStandbySession.cs:39-43 and E1 TextOnlyAccountHead.cs:9-10 originally admitted Claude and API/Local heads only. CodexSession.StartInjectedStandby:361-365, PiSession.StartInjectedStandby:303 and OpenCodeInjectionSession.StartInjectedStandby:46 load context without a model turn; they do not establish a no-tools checking/opinion mode. In ma-wire c23e031b, raw account ports and the host supplied every connected account, and generic models offered what they received. Rejection only at launch left unsafe offers visible.
- RR-50: engine-review OpenAICompatibleSession.cs:232,538-547 had an optional unassigned KeySpendEstimator; AppApiKeyRouting.cs:40 bound rotation without prices; ApiKeyPool.cs:119-120,290-302 accounted dollars after responses. Token-only replies lacked production dollar accounting and admission reservations.
- T9 regression: the held mandatory guard accepted only catalog quotes and refused the existing authoritative caller-price contract. The unchanged pricing/key-binding classes reproduced two failures and three passes before repair.

## Final behavior

RR-49 lives at the production boundary. ThreadPorts.CreateOpinionsModel and CreateLinkModel supply live head-only account projections; AskOthersComposerMount and LinkComposerHost bind them. AskOthersModel is byte-identical to the merge target and remains generic. Normal Account ports retain all services for primary routing, pins and popovers. Checking/opinion targets include only verified Claude/Pi and tool-disabled DeepSeek/Meta/Z.ai/OpenRouter/Local; every other CLI and unknown provider value fails closed. No new UI wording is introduced.

An unverified primary can supply its completed answer to a verified checker. Opinion Answer/Combine also privately launch the primary, so an unverified/missing primary yields no opinion heads. Live notifications clear stale selections. The engine host independently projects verified targets and refuses unsafe launches. Pi uses a fresh private session, excludes resource/tool discovery, verifies an empty runtime tool set and blocks every tool call. It cannot be adopted as a working session. A prompt or injected standby does not grant safety.

RR-50 accepts either the authoritative catalog/live OpenRouter quote or the existing caller-installed price for the exact provider/model. A capped request quotes declared input/output bounds under both cache interpretations, reserves the worst bound, clamps output, and settles with its captured estimator and original key. Invalid, stale, future, mismatched, missing-bound or unaffordable prices refuse dispatch. Uncapped caller prices retain receipt accounting without creating a false finite-cap hold on a refused key.

The existing private non-HTTP stream contract may return exact finite USD and settle through the shared cap. Actual HTTP independently requires a current bounded quote and reservation before transport, including private heads. There are no fixture-name, type, assembly or environment exceptions. A later receipt cannot authorize an unpriced real request. Unknown receipts retain durable conservative holds across restart/month changes; proven-unsent cancellations release holds.

## Authoritative sources and maintenance

Inspected read-only on 2026-10-07:

- Pi: [official pi-mono, 2db5e359](https://github.com/earendil-works/pi-mono/tree/2db5e359bf84c1c0be51d2c5c5c7cf27072c2b/packages/coding-agent), CLI/extension documentation, argument parser, extension types and RPC handler. No real CLI/model was invoked.
- OpenRouter: [official SDK, 3c88d707](https://github.com/OpenRouterTeam/typescript-sdk/tree/3c88d7075ba91b7b7fbca33073126873823131ed/src/models), model/pricing/provider-preference schemas. Runtime uses https://openrouter.ai/api/v1/models, exact per-model prices/limits, and provider.max_price routing ceilings. No current provider rates were fetched in this cloud.
- Page-only prices: official DeepSeek https://api-docs.deepseek.com/quick_start/pricing, Meta https://dev.meta.ai/docs/pricing/, Z.ai https://docs.z.ai/guides/overview/pricing. These pages were not retrieved here. No rate was invented.

The [pricing README](../../../src/Marrow.Services/Providers/Pricing/README.md) states exact Windows commands, mapping/review steps, sources, and both outputs: Pricing/model-prices.json and ModelPriceData.cs. The generator re-fetches official HTTPS pages and records retrieval dates, hashes and reviewed exact-cell mappings. Failure preserves prior outputs. The schema/table have a clear unfilled state. Runtime rejects data aged 24 hours or more and future dates; page data requires reviewed daily regeneration/rebuild until a separate updater is approved. Missing limits, ambiguous conditions, extra/non-text billing and unsupported OpenRouter routing refuse capped requests.

## Verification receipts

.NET SDK 10.0.401, Linux cloud. No package/project/SDK pins changed. New tests use only fake sessions, offline ports, in-process HTTP, fake HTML and a fake Pi ExtensionAPI. No provider network, real CLI/model or credentials are used by tests.

| Check | Result |
| --- | --- |
| Core Accounts + MultiAccount + Traceability | **520/520 passed**, including all 13 unchanged AskOthersModel tests |
| Services heads, engines, pricing, key rotation, standby and Traceability | **137/137 passed**, no skips |
| Production head-supply + App Traceability | **7/7 passed**, including all four new supply/end-to-end selection cases |
| ConversationHeadPricingTests alone, twice | **3/3 passed each time** |
| ConversationHeadKeyBindingTests alone, twice | **2/2 passed each time** |
| Generator fake-source tests | **3/3 passed** |
| Executed Pi runtime guard with fake ExtensionAPI | **PASS**: empty tools, all calls blocked, relock, no-turn proof, failed policy refuses proof |
| Supplementary App link/opinion view smoke | **24 passed / 1 failed / 0 skipped**; inherited source-encoding case below |
| App Windows-targeted build | Passed, zero warnings/errors |

The supplementary failure is the unchanged AskOthersViewTests.Chips_render_each_selected_monogram_as_its_own_mark_with_the_page_separator: expected ·, received �. AskOthersChips.cs:29 contains a raw non-UTF-8 byte 0xB7 in that literal; the view, generic model, fixture and assertion are byte-identical to merge target 228f56b55. Production supply is not used by this fixture. No clean-base App run is claimed. T9/opinions-view must check source decoding on Windows; no UI literal or expectation was changed here.

Test-first witnesses: /tmp/ma-gaps-t9-pricing-red.log (the two unchanged failures), /tmp/ma-gaps-t9-new-contract-red.log (missing contract API), /tmp/ma-gaps-production-head-red.log (missing production factories), /tmp/ma-gaps-finish-services.log (unchanged uncapped key-rotation regression), /tmp/ma-gaps-uncapped-price-red.log (new refusal/rotation regression, 0 passed / 1 failed). Assertions were held fixed through production repairs.

Final logs: /tmp/ma-gaps-finish-{core,services-final,app,generator,pi-guard}.log; isolated repeats /tmp/ma-gaps-isolated-ConversationHead{Pricing,KeyBinding}Tests-{1,2}.log. TRX receipts are under /tmp/ma-gaps-results/, notably finish-core.trx, finish-services-final.trx, production-head-supply-final.trx, finish-app.trx and the four isolated-*.trx files.

The required node porting/tools/verify.mjs --json ran: 16 PASS / 8 FAIL / 4 NOT_IMPLEMENTED (/tmp/ma-gaps-finish-verify.json). It cannot pass here: the pinned E:/Droppy/reference/droppy-code-v1.7.0 checkout is absent, and repository-wide parity/package/stub and Windows requirement/solution gates are incomplete. It is not a release receipt. Earlier full Linux Core/Services runs are also not release passes: Core aborted at Windows process launch after 1,919 passed / 191 failed; Services completed with 4,258 passed / 1,332 failed / 60 skipped. These were not independently triaged against a clean base and were not repeated for this targeted repair.

Exact finish-up commands, from the repository root (cloud uses --no-restore and the locally installed SDK; isolated repeats also use --no-build):

    $env:MARROW_PORT_DATA_DIR = "$env:TEMP/marrow-ma-gaps-tests"
    dotnet test tests/Marrow.Core.Tests/Marrow.Core.Tests.csproj --filter "FullyQualifiedName~Accounts|FullyQualifiedName~Port.MultiAccount|FullyQualifiedName~TraceabilityTests"
    dotnet test tests/Marrow.Services.Tests/Marrow.Services.Tests.csproj -p:EnableWindowsTargeting=true --filter "FullyQualifiedName~TextOnlyAccountHead|FullyQualifiedName~ConversationHead|FullyQualifiedName~OpinionsEngine|FullyQualifiedName~LinkEngine|FullyQualifiedName~CompletedLinkAnswer|FullyQualifiedName~OpinionOutcome|FullyQualifiedName~CombineRevision|FullyQualifiedName~ApiModelPriceAccounting|FullyQualifiedName~ModelPriceCatalog|FullyQualifiedName~OpenRouterPriceSource|FullyQualifiedName~PriceContract|FullyQualifiedName~KeyRotation|FullyQualifiedName~HandshakeStandbySession|FullyQualifiedName~PiTextOnlyHeadPolicy|FullyQualifiedName~TraceabilityTests"
    dotnet test tests/Marrow.App.Tests/Marrow.App.Tests.csproj -p:EnableWindowsTargeting=true --filter "FullyQualifiedName~ProductionHeadSupplyTests|FullyQualifiedName~TraceabilityTests|FullyQualifiedName~LinkUpViewTests|FullyQualifiedName~LinkMonogramReviewProofTests|FullyQualifiedName~AskOthersViewTests"

## Windows work and branch overlap

1. Merge ma-wire and ma-gaps together. At last fetch, ma-wire was c23e031b; its LiveThreadPorts constructs the shared ThreadPorts. Preserve CreateOpinionsModel/CreateLinkModel and their bindings in AskOthersComposerMount/LinkComposerHost. Keep raw primary ports complete. The unfinished ma-wire branch was not merged here.
2. Preserve the host target filter, TextOnlyAccountHead refusal, Pi private adapter and both Budget forwarders (PinnedConversationKeyRotation, AppApiKeyRotation). E1/W overlap these host/rotation files; engine-stack overlaps PiSession/PiCLI. All incoming stack/review changes were retained.
3. Repeat both unchanged pricing classes alone twice on Windows; run full Core/Services and compiled PiTextOnlyHeadSessionTests, existing Pi/standby suites, joined production view/engine checks and the source-encoding case above. Run the required verification entrypoint with the pinned reference checkout.
4. Prove installed Pi private identity/history, empty tools, no actions/edits/MCP/skill discovery, missing/invalid proof refusal, cancellation/revocation/disposal. Claude retains its existing restricted-launch gate. Other CLI heads stay omitted unless a future reviewed runtime adapter proves safety.
5. Run the official-page generator per its README, review exact model identifiers, units, bounds and billing conditions, commit the mapping and both outputs, and rebuild. Verify authoritative token-only and OpenRouter receipts, concurrent shared caps, price expiry/failure, unknown receipts, settings changes, month rollover/restart, and permission changes through price retrieval/dispatch.

New records: ED-NEW-ma-gaps-5 (RR-49 boundary) and ED-NEW-ma-gaps-6 (RR-50 repair). Historical ED-460..463 and the rollback explanation remain intact. The merged ledger prefix is byte-exact: 2,049,897 bytes, SHA-256 752664d2d6a510b338b10d48fea34557a9a694b3f9e00212bd0a2f4f87d02129. No owner-law, locked design, existing test or unrelated STATE file was changed.
