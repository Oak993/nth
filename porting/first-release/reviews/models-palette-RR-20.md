# T37 / RR-20 — automatic maker/model palettes and all-model blend

**BLOCKED (backend prerequisites implemented).** Decision:
ED-NEW-models-palette-2. The fixed OpenRouter purple fallback in
`EffortSlider.cs:1586` is unchanged. These changes do not claim a working
automatic palette engine, automatic scheduling or renderer integration.

SOURCE FACT: OD-0066 amends OD-0065: intensity is a function of the **current
global lineup**, not personal provider lists or discovery order. OD-0065 still
requires stable maker themes, a fixed logo source chain, order-independent
collision avoidance, and transient failures that never lock a fallback.
OD-0051 fixes the lead share at 1.6 times a worker; OD-0048 places workers left
of leads and the first lead against the knob.

Implemented in new files only:

- `Marrow.Services.Port.ModelsPalette.PublicModelCatalogue` fetches public
  OpenRouter `/models` without authorization. It preserves the complete public
  facts for later strength ranking. A versioned SHA-256 fingerprint canonicalizes
  property order and model order; conflicting duplicate IDs reject a snapshot.
  Array order and number spelling are retained as published by the fixed source.
- `PublicModelCatalogueStore` validates and atomically persists the global
  snapshot in the supplied isolated data root. Failed HTTP responses, malformed
  catalogues, transport errors and invalid caches never substitute a different
  lineup. An invalid cache is left untouched until a successful refresh.
- `Marrow.Core.Port.ModelsPalette.SelectedModelBlend.Spans` retains every
  selection, including several models of one maker. Workers retain their order;
  leads follow in reverse order so the first lead is against the knob. Integer
  weights 8:5 represent the approved 1.6 ratio without accumulated rounding.

Test-first evidence:

- New geometry/catalogue/cache suites initially failed to compile because their
  production types did not exist. No assertions were subsequently changed.
- Cache-validation regression: 2 failed / 2 passed before the repair. JSON
  string/null versions threw; implementation now checks the JSON kind first.
- Final Core slice: 9 passed, 0 failed/skipped (4 blend tests plus traceability
  and layering). Final Services slice: 64 passed, 0 failed/skipped (17 catalogue
  and cache tests, 41 existing OpenRouter API tests, traceability and layering).
- Ten independent data roots, different histories and a failing source converge
  on the same persisted catalogue and survive restart. This proves catalogue
  convergence **only**, not maker palette/shade convergence required by OD-0065.
  All requests use the in-process fake OpenRouter handler.

Still required before this row can close:

1. Read `E:\Droppy\work\design\auto-colour\PLAN.md`, `discovery.md`, its fixed
   logo source chain/collision rule, and the approved intensity `IR-3` source.
   They are absent from the requested base; the Windows connector is disconnected.
   OD-0066 cites measured agreement with an independent benchmark; reproducing
   that algorithm requires the actual source, not guesses about model names.
2. Implement logo extraction, palette/shade persistence, unknown-maker fallback
   and retry, shipped baseline/global-lineup merge, strength ranking, per-effort
   intensity and animated re-spacing from those sources.
3. Bind automatic discovery and the full engine to the selected models, titles,
   Jewel and locked living-water/ink arrival renderer. Geometry alone does not
   implement multi-model liquid blending, OKLab transitions or ink arrival.
4. Run the full ten-instance palette proof with history/order/failure variation.
   OD-0065's separate real-network ten-root proof was not run: this task expressly
   requires fakes only. It remains an external release acceptance requirement.

Network inventory for the new helper: GET
`https://openrouter.ai/api/v1/models`, keyless, Accept `application/json`, the
existing URLSession transport with 15 s inactivity timeout. It is not yet bound
to production startup/Settings; tests open no sockets. No logo host is guessed.

EffortSlider seam: **none**. `EffortSlider.cs`, `LiquidTrack.cs`,
`LiquidStrike.cs` remain byte-identical to the requested base for T14/Strike.
Future integration should enter `LiquidSpec.For(look)` (the existing ED-286
intensity seam); it still needs the full licensed algorithm and T14 merge.
Receipts: `porting/evidence/models-palette/rr20/`.
