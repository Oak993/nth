# Authoritative model prices (RR-50)

`model-prices.json` is deliberately **unfilled**. No checked-in numeric value is a provider price. DeepSeek, Meta and Z.ai capped requests stop before transport until an exact model has a current official price and authoritative input/output limits. Unknown cost receipts retain durable holds; changing month or restarting does not release them.

OpenRouter capped requests read the public `https://openrouter.ai/api/v1/models` endpoint at runtime. The request binds the returned exact model and sets `provider.max_price` (USD per million prompt/completion tokens, USD per request) and the output limit. Auto routing, conditional overrides and unhandled extra charges are unavailable to the flat text budget. Contract source, inspected 2026-10-07: [official OpenRouter SDK](https://github.com/OpenRouterTeam/typescript-sdk/tree/3c88d7075ba91b7b7fbca33073126873823131ed/src/models), `model.ts`, `publicpricing.ts`, `providerpreferences.ts`. No live rates were retrieved in the cloud.

On Windows with Python 3 installed:

```powershell
py porting/tools/generate-model-prices.py --inspect --captures "$env:TEMP/marrow-price-captures"
# Review the captured official pages. Fill model-price-extraction.json's models array
# with exact model/cell indices and verbatim unit/billing quotes; never type a rate.
py porting/tools/generate-model-prices.py
```

Each model mapping specifies `service`, `model`, `sourceUrl`, `modelCell`, `inputCell`, `outputCell`, `tokenUnitQuote`, and optionally `cachedInputCell`, `requestCell`, `maximumInputCell`, `maximumOutputCell`. Cell coordinates are `[table, row, column]`, starting at zero. If no request-fee cell exists, `tokenOnlyQuote` must explicitly establish no request fees. Prices must be unambiguous USD per million tokens; bounds must be exact integer token counts. Review all tier, cache, reasoning, time and other billing conditions before mapping. The generic extractor refuses ambiguous/spanned tables; such pages need a reviewed extractor before use.

Every generation re-fetches official HTTPS pages, records UTC retrieval dates, SHA-256 hashes and mappings, and writes both the schema-backed JSON and compiled `ModelPriceData.cs`. A failed retrieval, unfilled mapping or rejected extraction preserves both prior files. Review and commit both outputs, then rebuild. Runtime accepts prices for less than 24 hours after retrieval and rejects future dates. Refresh and rebuild daily and whenever an official price changes; stale data stops capped requests. An installed build has no automatic page-price update, so releases needing longer operation require a separately reviewed updater. Missing limits can support spend accounting but cannot authorize a capped request.

All test amounts are fictional. Run the generator tests without provider access:

```powershell
py tests/fixtures/pricing/test_generate_model_prices.py
```
