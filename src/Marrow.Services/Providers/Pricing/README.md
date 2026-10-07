# Authoritative model prices (RR-50)

`model-prices.json` is deliberately **unfilled**. No checked-in numeric value is a provider price. DeepSeek, Meta and Z.ai capped requests stop before transport until an exact model has a current official price and authoritative input/output limits. Unknown cost receipts retain durable holds; changing month or restarting does not release them.

OpenRouter capped requests read the public `https://openrouter.ai/api/v1/models` endpoint at runtime. The request binds the returned exact model and sets `provider.max_price` (USD per million prompt/completion tokens, USD per request) and the output limit. Auto routing, conditional overrides and unhandled extra charges are unavailable to the flat text budget. Contract source, inspected 2026-10-07: [official OpenRouter SDK](https://github.com/OpenRouterTeam/typescript-sdk/tree/3c88d7075ba91b7b7fbca33073126873823131ed/src/models), `model.ts`, `publicpricing.ts`, `providerpreferences.ts`. No live rates were retrieved in the cloud.

The existing caller-installed `KeySpendEstimator` contract also accepts an authoritative price for that session's exact provider/model. The caller must supply current rates and the declared model/request limits; installing a delegate does not establish a provider rate. Admission quotes the full declared input/output bounds, considers both cache interpretations, reserves the maximum against the shared cap, and clamps the request's output limit. The captured estimator settles that request even if the caller later replaces the price. A mismatched model, expired price, missing bound, invalid quote or insufficient cap stops dispatch. OpenRouter still uses its official models API and routing-price ceiling.

Private non-HTTP stream replacements may return an exact USD receipt under the existing head contract. Actual HTTP always requires a current quoted bound and reservation before dispatch, including private heads. An unpriced real request cannot use a later USD receipt to bypass admission. Tests cover both paths using only fakes.

On Windows with Python 3 installed, run these commands in PowerShell **from the repository root**:

```powershell
py porting/tools/generate-model-prices.py --inspect --captures "$env:TEMP/marrow-price-captures"
# Review the captured official pages. Fill the models array in
# src/Marrow.Services/Providers/Pricing/model-price-extraction.json
# with exact model/cell indices and verbatim unit/billing quotes; never type a rate.
py porting/tools/generate-model-prices.py
```

The default mapping file is `src/Marrow.Services/Providers/Pricing/model-price-extraction.json`. Its `sources` array supplies the official pages read by `--inspect`:

- DeepSeek: <https://api-docs.deepseek.com/quick_start/pricing>
- Meta: <https://dev.meta.ai/docs/pricing/>
- Z.ai: <https://docs.z.ai/guides/overview/pricing>

Generation reads the reviewed `models[].sourceUrl` entries in that mapping and fetches those official HTTPS pages again. To use a separate reviewed mapping, run `py porting/tools/generate-model-prices.py --map C:\path\reviewed-model-price-extraction.json`. Only the listed providers' official hosts are accepted.

Successful generation writes these two paths under the repository root, regardless of the working directory:

- `src/Marrow.Services/Providers/Pricing/model-prices.json`
- `src/Marrow.Services/Providers/ModelPriceData.cs`

Commit the reviewed mapping and both generated outputs together. The lead performs this retrieval and review locally on Windows; the checked-in unfilled mapping cannot generate prices.

Each model mapping specifies `service`, `model`, `sourceUrl`, `modelCell`, `inputCell`, `outputCell`, `tokenUnitQuote`, and optionally `cachedInputCell`, `requestCell`, `maximumInputCell`, `maximumOutputCell`. Cell coordinates are `[table, row, column]`, starting at zero. If no request-fee cell exists, `tokenOnlyQuote` must explicitly establish no request fees. Prices must be unambiguous USD per million tokens; bounds must be exact integer token counts. Review all tier, cache, reasoning, time and other billing conditions before mapping. The generic extractor refuses ambiguous/spanned tables; such pages need a reviewed extractor before use.

Every generation re-fetches official HTTPS pages, records UTC retrieval dates, SHA-256 hashes and mappings, and writes both the schema-backed JSON and compiled `ModelPriceData.cs`. A failed retrieval, unfilled mapping or rejected extraction preserves both prior files. Review and commit both outputs, then rebuild. Runtime accepts prices for less than 24 hours after retrieval and rejects future dates. Refresh and rebuild daily and whenever an official price changes; stale data stops capped requests. An installed build has no automatic page-price update, so releases needing longer operation require a separately reviewed updater. Missing limits can support spend accounting but cannot authorize a capped request.

All test amounts are fictional. Run the generator tests without provider access:

```powershell
py tests/fixtures/pricing/test_generate_model_prices.py
```
