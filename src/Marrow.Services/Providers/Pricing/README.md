# Authoritative model prices (RR-50)

`model-prices.json` contains only prices passing every source-evidence guard. It remains **unfilled** as of the 2026-10-07 local fetch: the nine known chat model IDs have parseable token cells, but none of the reviewed official pages explicitly quotes a per-request rate or no request fees. DeepSeek and Meta additionally lack an exact USD-per-million-token quote. The manifest records each model's reasons and the observed source cells; those observations are not published prices. Capped requests stop before transport until an exact model has a current official price and authoritative input/output limits. Unknown cost receipts retain durable holds; changing month or restarting does not release them.

OpenRouter capped requests read the public `https://openrouter.ai/api/v1/models` endpoint at runtime. The request binds the returned exact model and sets `provider.max_price` (USD per million prompt/completion tokens, USD per request) and the output limit. Auto routing, conditional overrides and unhandled extra charges are unavailable to the flat text budget. Contract source, inspected 2026-10-07: [official OpenRouter SDK](https://github.com/OpenRouterTeam/typescript-sdk/tree/3c88d7075ba91b7b7fbca33073126873823131ed/src/models), `model.ts`, `publicpricing.ts`, `providerpreferences.ts`. No live rates were retrieved in the cloud.

On Windows with Python 3 installed:

```powershell
py porting/tools/generate-model-prices.py --inspect --captures "$env:TEMP/marrow-price-captures"
# Review the captured official pages, exact ID map and extraction mappings.
# Change only source cell indices and verbatim evidence quotes; never type a rate.
py porting/tools/generate-model-prices.py
```

`model-price-ids.json` binds each API ID to the exact official display text and source URL. No case folding, fuzzy matching, or implicit aliasing is performed. Scope is the nine known text chat IDs offered by DeepSeekAPI, MetaAPI and ZaiAPI; unknown dynamic IDs remain unfilled. Meta Standard and Contributor are different exact API IDs, not interchangeable billing tiers.

Each extraction mapping specifies `service`, `model`, `sourceUrl`, `modelCell`, `inputCell`, `outputCell`, `tokenUnitQuote`, and optionally `cachedInputCell`, `requestCell`, `maximumInputCell`, `maximumOutputCell`. Cell coordinates are `[table, row, column]`, starting at zero. `checks` bind the exact headers and condition labels. DeepSeek spans expand into a rectangular grid, repeating the exact source text; overlapping, incomplete, excessive, or out-of-table spans fail closed. Meta's `meta-marketing` parser reads the official product page's div layout and streamed HTML fragments without executing scripts or interpreting JavaScript/Flight objects. Header/row changes fail closed. `/docs/pricing` returns an empty shell and is not used as a price source.

`inputAlternatives`, `outputAlternatives`, `cachedInputAlternatives`, and `requestAlternatives` list every applicable conditional cell. Nonempty alternatives require verbatim `billingConditionQuotes`; generation selects the highest numeric rate independently for each charge. DeepSeek maps both peak and off-peak cells. Never average, use an off-peak default, or import a historic rate from a news page. `storageCell` records Z.ai's conditional `Limited-time Free`; a paid storage charge cannot be represented by the flat token/request catalog and leaves that model unfilled. Unknown future storage rates are never guessed.

If no request-fee cell exists, `tokenOnlyQuote` must explicitly establish no request fees. Token-based billing, no minimums, no upfront commitment, and no extra cache fees do not satisfy that guard. Prices must be unambiguous USD per million tokens; bounds must be exact integer token counts. Missing K/M bounds are not converted by guessing. Supplemental billing quotes in `sources` are audited and source-hashed; they do not bypass the model's original unit/request-fee guards or substitute built-in tool fees for a per-request charge.

Every generation re-fetches official HTTPS pages, records UTC retrieval dates, SHA-256 hashes and mappings, and writes the schema-backed JSON, compiled `ModelPriceData.cs`, and `model-price-fetch-manifest.json`. With a reviewed `identityMap`, failures leave only affected models unfilled and remove their previous rates; other valid models are published. Each manifest row records filled/unfilled status, exact identity, billing conditions, highest selected cell, and rejection reasons. Empty maps or duplicate identities abort before replacing outputs. Legacy maps without `identityMap` retain their original all-or-nothing preservation behavior. Review and commit all generated outputs, then rebuild. Runtime accepts prices for less than 24 hours after retrieval and rejects future dates. Refresh and rebuild daily and whenever an official price changes; stale data stops capped requests. An installed build has no automatic page-price update, so releases needing longer operation require a separately reviewed updater. Missing limits can support spend accounting but cannot authorize a capped request.

Original unit-test amounts are fictional; new capture regressions contain verbatim public source HTML and a source/hash/date manifest. Run both test files without provider access:

```powershell
py tests/fixtures/pricing/test_generate_model_prices.py
py tests/fixtures/pricing/test_reviewed_model_prices.py
```
