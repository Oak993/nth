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


## Round 2 — 2026-10-07

**BLOCKED (substantial engine delivered; release acceptance is not granted).** ED-NEW-models-palette-6. The original missing-design blocker is resolved by the lead mirror; the remaining implementation and proof gaps below are explicitly separate.

New Core code implements the archived IR-3 ranking/intensity algorithm and its complete reference cells, global lineup membership and class/version/legacy/name handling, integer 64x64 dominant-colour extraction, D3 frozen nearest-free search, pinned toc and ICANN domain votes. `PaletteArrangement` is the locked v22 drawing arrangement: BOTH workers and leads reverse before drawing, every selection remains, weights are exactly 8:5, each maker's heart occupies the middle third, and inter-heart boundaries mix in OKLab. The older Round-1 `SelectedModelBlend` and its assertions remain unchanged; it is not substituted for the newly evidenced v22 arrangement.

New Services code uses a dedicated BelowNormal actor and captured injected keyless HTTP; global Date/Age six-hour market windows and boundary+15-minute publication; cutoff/future filtering, id-only prefetch; validated priority outcomes and exact HF org/user/maker probes, 4/10-second HTTP and 8-second maker caps; shared HF budget and numeric Retry-After; two distinct server-day agreeing evidence observations; checksummed atomic evidence/market caches, individual corruption quarantine and future-schema read-only handling; debounce/network notifications, single-flight and RefreshNow. Every model gets frozen dark/light effort tables, title/glow/motion and a 256-pixel RGBA row. New App seams coalesce snapshot/progress to one post per 100 ms and read frozen data without I/O. `PaletteGlide` retains the actual in-flight RGB blend on retarget, shares its 1.2 s clock across row/title/glow/motion, waits for arrival, and allocates zero bytes in warm sampling. The UI gate remains OFF as PLAN 9.2 requires. `ModelPickerSeam` is additive and requires the caller's approved colour delegate.

Evidence: `rr20-core-final3.trx` 19 pass / 1 fail / 0 skip; `rr20-services-final2.trx` 35 pass / 0 fail / 0 skip; decoder `rr20-decoder-green.trx` 2 pass; App frozen-read/glide proofs in `rr21-app-final3.trx`. Ten isolated fake roots with different histories, cultures and restart converge on a full palette snapshot digest, not only catalogue facts. All intensity cells/levels and baseline lineups agree with executable archived IR-3. Null/malformed seeded-cache launch reds, source identity reds, new-type reds and their green receipts are retained. Original tests/tolerances/references/assertions remain byte-exact.

The retained `PaletteReferenceTests.Harness_word_order_effort_synonyms_and_new_generation_preserve_global_identity` fails its final strict assertion (line 76). Input: one new `openai/gpt-7`, price 0, created 2000000000, context 1000000. Both the port and archived Python place off-catalogue `gpt-7-mini` at level 1 of 4, while the strict test requires >1. `reproduce_ir3.py` runs offline against the byte-exact mirrored source; `ir3-conflict.json` preserves the result. PLAN 12 records the id-only/legacy residual. This needs a source-rule ruling; the assertion and source are unchanged.

Other uncredited release work: complete versioned release pack/bake generator and authentic logo/placeholder evidence blobs (binary blobs are absent from this text mirror); append-only bake/upgrade/seed-rule re-extraction, bounded tombstones, embedded snapshot 0 and Prime lifecycle; complete HF website/domain/RateLimit/placeholder handling and retry diagnostics; the complete name/provider/router policy; pack-version reset/adoption and full shader integration. The current source-derived input pack is labelled as such and does not pretend to supply any of these. In particular mono HF avatars with missing website/placeholder evidence remain unavailable; no lower-source mono answer is fabricated. The API supports automatic Start/trigger checks, but production launch/resume/provider-list lifecycle integration beyond the Settings read path is not claimed.

Root's only build-configuration change adds three deterministic embedded resources to Core.csproj, preserving every existing CRLF/LF byte. No dependencies, original test configuration or guard is altered. The pre-commit protected-path diagnostic flags this intentional root-owned change; it is not presented as a worker permission exception. Pinned PSL licence/source/derived hashes are included.

Windows/T9 needs: real 10-root proof if separately authorised (this task ran only fakes); native codec/ICO fixtures and filesystem atomic/locked-cache/retry cases; after W1/W2/W3 decisions and release pack data, merged Strike renderer identity, retarget/arrival frames, all selected-model shader boundaries, theme/title/glow and reduced motion/hidden/frontmost behaviour, 10-second real 60 Hz cadence and performance. The original EffortBrand assertions stay intact. Verdict for this row: NOT READY.

Final backend follow-up: PaletteFrozenReservationTests preserved a real red (Acme derived against IR-3 example swatches). Freeze only the 14 authoritative locked maker palettes plus Purple/Fast/FastFill, not unbaked reference examples; the original expected stops remain unchanged and pass. All Services palette/catalogue plus original traceability/layering guards now pass 42/42 in `rr20-services-verified.trx`. The two newly authored nested fake helper types were moved verbatim to dedicated Fixtures files after the unchanged source-shape guards rejected them; all test methods/attributes/assertions are unchanged. The strict Core new-generation red remains.
