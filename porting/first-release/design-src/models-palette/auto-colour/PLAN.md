# Automatic maker-colour engine: implementation plan (engine version AC-3)

Written 2026-10-02; revised the same day to **AC-2** after adversarial hardening round 1 (section 13), then to **AC-3**
after round 2 (section 14 lists every round-2 finding and how it is resolved; the intensity draft is now **IR-3**).
This plan judges the two designs in this folder, `design-minimal-robust.md` ("Tincture") and
`design-premium-complete.md` ("AC-1"), and replaces both as the build reference. Its inputs are:
- the owner rulings OD-0047, OD-0049 to OD-0052, OD-0057, OD-0058 and OD-0060 to OD-0067 (`porting/owner_decisions.md`);
- the research notes `sources.md`, `discovery.md` and `current.md`;
- `../intensity/proposal-brand-anchored.md` (BA-1) and the intensity rule draft `../intensity/intensity.py`, now **IR-3**;
- the hardening probes in `hardening/` (round 1) and `hardening/r2/` (round 2);
- the port at `E:/Droppy/wt/cl/terminal-verify`, checked for the seams and line numbers cited here.

Labels: **SOURCE FACT** means an owner ruling or the port as it is. **ENGINEERING DECISION** means this plan's choice.
**OWNER** means the item waits for the owner.

What the owner asked for, restated (OD-0064, with OD-0065, OD-0066 and OD-0067):
- The app colours every model by its maker. OpenRouter is a router, never a maker.
- For a model or maker the app has never seen, the app does the work itself, within seconds:
  1. it notices the model;
  2. it works out the maker;
  3. it looks the maker up online with no API key;
  4. it derives a distinct, premium palette;
  5. it places the model by strength;
  6. it applies the effort shades;
  7. it saves the result and uses it from then on.
- Every user gets the same colours (OD-0065), verified by 10 instances.
- A stronger new model re-spaces its family; the same lineup gives the same shades everywhere; re-spacing glides
  (OD-0066).
- A Settings control refreshes the palettes on demand, with the same result as the automatic path, progress and a
  short summary (OD-0067).
- Nobody has to come back to a development session to have a new model or maker coloured.

### What changed from AC-1, in one paragraph

AC-1 let each machine lock its own first answer for ever. Round 1 showed that this first answer depends on the
machine: its build, its network (a proxy page, a rate limit, an outage), the hints it happened to have, the order it met
makers in, its catalogue's age and its locale. AC-2 makes every input to a colour **global**: a versioned colour data
pack (section 5.6) that always outranks local state, a lineup cut at a shared 6-hour boundary, a global effort ladder,
invariant-culture parsing, post-pack makers derived only against the pack's frozen set, and local seeds that are a
re-verifiable cache of the current public evidence rather than a permanent lock. A machine's own failures can delay a
colour (the neutral shows), never change it. The on-demand refresh (OD-0067) is designed in section 3A.

### What changed from AC-2, in one paragraph

Round 2 showed that AC-2 still let a machine's own history choose its colours: which fetches it accepted (a pulled
newest row froze it; removals counted its own fetches; facts came from its last fetch; an upgrade unioned old rows back
in), which seed key it last held, which build's pack its post-pack makers were derived against, and which provider
spelled a model or an effort word. AC-3 makes the lineup a **pure function of the pack and one boundary snapshot per
6-hour window**, published at one common instant (section 6.2); keeps withdrawn rows as **tombstones**; places an
unknown model by its **id alone** (section 4.4); maps **effort synonyms** and **harness spellings** before ranking;
derives post-pack makers against a reserved set **frozen per `DeriveVersion`** (section 5.3); schedules re-verification
on **global day windows** instead of process-local ticks (section 5.5); and never keeps a seed computed for a different
key on screen. IR-3 fixes the ranking cases (legacy cap by class, dated successors, speed variants, family-word steps,
a plain next generation). The refresh publishes in stages, precomputes every shade off the UI thread and no longer
reloads provider lists (section 3A).

---

## 1. Verdict on the two designs

The plan uses Tincture as the spine and takes AC-1's layering, determinism details and collision gates where Tincture
falls short. It also corrects claims that the research, the rulings and the round-1 hardening contradict.

### 1.1 Taken from Tincture (simpler, and enough)

| Choice | Why |
|---|---|
| **Persist only seeds, never palettes.** A seed is one brand colour or "mono", with its evidence. Palettes, ranks and shades are recomputed from seeds and public facts. AC-2 adds the evidence itself: the 64 x 64 resample of each source image (at most 16 KB each), so a seed can be recomputed when the seed rules change (section 5.5). | When a derivation rule changes, as it will when the OD-0063 canvas is approved, no stored palette goes stale. A seed-rule change re-derives the seed from the stored evidence instead of keeping an old output (F1, R3). |
| **Seed chain decided by strict priority, with three outcomes per source** (Hit, decisive Miss, transient Unavailable). An Unavailable above the winner leaves the result Undecided. | This is the literal reading of OD-0065: "never locks a fallback colour from a different source". |
| **Four hosts.** The chain uses openrouter.ai, huggingface.co, cdn-avatars.huggingface.co and t0.gstatic.com. | The HF avatar and faviconV2 together cover 25 of 25 makers (`sources.md`). Each extra host is another failure mode. |
| **Undecided shows the neutral shade and saves no palette.** | AC-1's "provisional" palette from lower sources is the fallback-from-another-source outcome OD-0065 forbids. |
| **One immutable snapshot.** It is published by a volatile swap. The UI reads it synchronously, with no allocation. | Simpler than one cell per maker, and just as cheap. |

### 1.2 Taken from AC-1 (Tincture is wrong or too thin here)

| Choice | Why |
|---|---|
| **Layering.** The pure maths goes in Core, the network engine in Services, the Skia decoder in UI and the wiring in App. | SOURCE FACT: Core depends on nothing; Services and UI depend only on Core; only UI references `Avalonia.Skia`. |
| **The lobehub toc is pinned, never fetched at runtime.** AC-2 moves the pin into the colour data pack (section 5.6), so it can move forward without a new binary. | Two users a week apart would otherwise read different curated colours. |
| **A managed area-average resample to 64 x 64, snapped to the deriver grid** (integer indices: hue in 1 degree, L and C in 0.005). | Deterministic on every machine (round 1, `hardening/h4_libm_ulp.py`, `h5_dominant_edges.py`: 0 of 15,990 cells and 0 of 24 seeds change under libm, premultiplied-alpha or 1-LSB noise). |
| **A CLI-only maker is never checked against other CLI-only makers.** AC-2 extends this to every post-pack maker (section 5.3). | Different users see different post-pack makers at different times. |
| **A collision gate over each maker's whole shade cloud** (H2), in addition to the heart gate (H1). | See 1.3(a). |
| **Hue-band premium bounds, a heart L of at most 0.80, a mono chroma floor of 0.05 and a chromatic floor of 0.09.** | BA-1's risks section; a heart above L 0.80 hides the white sparks. |
| **Fast amber and FastFill are in the reserved set.** | SOURCE FACT: the fast-mode bolt draws over the liquid (OD-0063). |
| **Network-available and resume are triggers. A 250 ms debounce groups a batch. A diff by (id, name, detail) catches alias drift.** AC-2 moves the diff off the UI thread (R11). | `discovery.md` section 8. |

Withdrawn from AC-1 by AC-2: "five transient failures of one source, spread over at least 24 h, count as a decisive
Miss". It let one machine's network create a permanent, machine-specific outcome (F2, R2). Liveness now comes from the
data pack (section 5.6): a maker that stays undecided on one machine shows the neutral until the next pack ships its
decided seed to everyone.

### 1.3 Corrections to both designs

**(a) Tincture's separation guarantee does not hold under BA-1.** BA-1 measures a heart drift of 0.12 to 0.16, so the
collision check runs over the cloud the approved intensity rule produces (H2 through `IIntensityRule`). Until approval,
`IdentityRule` makes H2 equal H1.

**(b) The ranking order is decided** (OD-0066, ENGINEERING DECISION): class and tier words first, then version, then
release date, then price. AC-2 adds, inside that order: the OpenAI family ladder placed on the generic scale, down-words
inside a family, a lineup-relative legacy cap, order-free collapse, equal-key groups and a visible-level cap (section
4.4, IR-2). AC-3 (IR-3) measures the legacy cap against the same or a higher class only, splits dated successors, ranks
a baseless speed variant below its class, puts a plain next generation above the family and gives visible steps to
family words before modifiers.

**(c) Every catalogue maker is resolved, but post-pack makers never depend on each other.** AC-1 derived each maker
against every earlier-epoch maker, so a predecessor that was Pending on one machine and decided on another gave
different palettes (F4, R9). AC-2 derives a post-pack maker only against the pack's frozen reserved set. Fetch order,
local epochs and predecessor states no longer exist as inputs. AC-3 freezes that set per `DeriveVersion` rather than
per pack, so a post-pack maker has the same palette on every build that shares the `DeriveVersion` (R2-5).

**(d) Smaller corrections.**
- AC-1 started the engine after `await Providers.RefreshAll()` (`AppModel.cs:317`), so every launch replayed the ink
  arrival for colours the user already had. AC-2 constructs the engine in the `AppModel` constructor and publishes
  snapshot 0 before the main window shows (section 3, step 1; R4).
- The 3 s settle is owned by the card, not the engine (section 4.5).
- The override file (AC-1 section 8) and a Settings "reset colour" control are new configuration and UI and wait for the
  owner (O3). `Reset(maker)` exists only as an engine API, for the tests and the bake tool. The OD-0067 refresh is not a
  reset and never discards a seed (section 3A).
- Logos are not stored. Their SHA-256 and their 64 x 64 resample are (evidence for re-derivation, not a logo picker).

---

## 2. Components and files

Every engine type lives in a `Colour` folder of its layer. The test projects mirror them. New or changed in AC-2: C9,
C12 to C20, C24 to C26. Changed again in AC-3: C3, C8, C9, C12, C14 to C18, C24 to C26 (section 14).

| # | Component | Layer and file | Pure? | Responsibility |
|---|---|---|---|---|
| C1 | `OKLab` | `src/Marrow.Core/Colour/OKLab.cs` | yes | sRGB and OKLab/OKLCH conversion, distance, gamut test, chroma bisection, quantisation helpers. Moved from `LiquidTrack.cs:287-309`. |
| C2 | Colour types | `src/Marrow.Core/Colour/Types.cs` | yes | `MakerKey`; `Seed` (Chromatic as **integer grid indices** (hue, L, C) or Mono; source; evidence); `Evidence` (per source: status, SHA-256 of the bytes, the 64 x 64 RGBA resample); `BrandPalette` (value equality, a `TrackLook` key); `Shade`; `ShadeState` (Ready, **Known**, Pending, Router). |
| C3 | `MakerDirectory` + `Makers.Resolve` | `src/Marrow.Core/Colour/Makers.cs`; table from the pack | yes | Slug normalisation (ordinal, invariant); routers; aliases; family words **anchored to the start of the last id segment**; provider-to-maker map; catalogue match **against the boundary lineup only**, ordinal tie-break; `Resolve(provider, modelId, displayName)`, memoised (section 4.1). |
| C4 | `DominantColour` | `src/Marrow.Core/Colour/DominantColour.cs` | yes | A port of `derive.py dominant()`, the managed area-average 64 x 64 resample (kept as evidence) and grid snapping to integer indices. Versioned by `SeedVersion`. |
| C5 | `DomainVote` | `src/Marrow.Core/Colour/DomainVote.cs`; PSL from the pack | yes | Registrable domain (pinned PSL ICANN section), aggregator denylist, 2 agreeing votes or 1 from the HF page or OR providers. Never `<slug>.com`. |
| C6 | `IIntensityRule` + `IdentityRule` | `src/Marrow.Core/Colour/IntensityRule.cs` | yes | `(palette, level of levels, effort word, theme) -> Shade` (stops, title, glow and every s-driven dial); `Levels(palette) -> (V, positions)` (visible levels); `Cloud(palette)` for H2; a version tag. Effort words pass through the pack's synonym table first. `IdentityRule`: one level, the anchor for every state. IR-3 is the draft port of `intensity.py` (U22, after approval). |
| C7 | `PaletteDeriver` | `src/Marrow.Core/Colour/PaletteDeriver.cs` | yes | Seed to palette against an **explicit** reserved set (section 5). |
| C8 | `LineupRanker` | `src/Marrow.Core/Colour/LineupRanker.cs`; ladders from the pack | yes | Eligibility at boundary B; order-free collapse with dated successors; tombstones; OD-0066 precedence with the IR-3 refinements; groups; levels per palette; harness-spelling canonicalisation; id-only virtual placement; alias resolution; a lineup digest (section 4.4). |
| C9 | `ColourPack` (was `Baseline`) | `src/Marrow.Core/Colour/ColourPack.cs` + embedded `Resources/colour/pack.v<N>.json` | data | **The colour data pack** (section 5.6): `PackVersion`; the fixed palette (single source of `LiquidStopsHex`); makers table; ladders; effort ladder words **and synonyms**; **harness rewrites**; pinned toc; PSL; placeholder SHAs; owner and baked seeds with evidence; **append-only** bake epochs; **the frozen post-pack reserved list of its `DeriveVersion`**; the market snapshot with its boundary **and tombstones**; **the precomputed pack-only snapshot 0**; the minimum engine it needs. |
| C10 | `ILogoDecoder` | `src/Marrow.Core/Colour/ILogoDecoder.cs` | interface | Bytes to RGBA, or a classified failure (section 4.2: a validated Miss or Unavailable). |
| C11 | `SkiaLogoDecoder` | `src/Marrow.UI/Colour/SkiaLogoDecoder.cs` | no | Magic bytes (PNG, ICO, WebP, JPEG, GIF first frame); 512 KB and 1024 px caps before decoding; largest ICO frame; no SVG; the Skia version Avalonia 12.1.2 pins. |
| C12 | `MarketCatalogue` | `src/Marrow.Services/Colour/MarketCatalogue.cs` | no | Keyless `GET openrouter.ai/api/v1/models` and `/providers` through the **engine's own injected `URLSession`** (no URLCache; R6, R7). Single-flight; generation time `Date - Age`; **one boundary snapshot per 6-hour window, full-list replacement, never a per-row union**; tombstones from the pack; publication at boundary + 15 min in server time; `created` clamp (section 6.2). Streaming `Utf8JsonReader`; every number parsed with `Utf8Parser` / `CultureInfo.InvariantCulture` (F7). |
| C13 | `SeedChain` | `src/Marrow.Services/Colour/SeedChain.cs` | no | `Find(maker, inputs, ct) -> Decisive(seed, evidence) or Undecided(reason)`. Never throws. Response validation and outcomes in section 4.2. Inputs only from global facts (F3). |
| C14 | `ColourStore` | `src/Marrow.Services/Colour/ColourStore.cs` | no | `StoragePaths.Directory("colour")`: `seeds.json`, `market.json` (+ `market.prev.json`), `snapshot.json` (warm start, with a content hash). Versioned schema; validation and quarantine **for every file**; newer schema read-only; locked file retried, never rebuilt; first write `File.Move`, later `File.Replace` (section 6.3; R10, R2-A12). |
| C15 | `ColourEngine` | `src/Marrow.Services/Colour/ColourEngine.cs` | no | An `Actor` on **a dedicated `BelowNormal`-priority thread** (ED-067; R2-A14). Triggers, debounce, the maker queue (new makers before re-verifications), seed workers, re-verification **on global day windows**, back-off (monotonic within a process, window indices across restarts), the market watch, `RefreshNow()` (section 3A), `Reset(maker)` (API only). The store's only writer. Publishes one settled `ColourSnapshot` per batch, with every shade precomputed. |
| C16 | `ColourSnapshot` | `src/Marrow.Services/Colour/ColourSnapshot.cs` | data | Immutable: per maker the state and palette; per lineup the groups, levels and digest; a model-to-(maker, level) map; **a frozen shade table per (palette, level, effort word, theme), precomputed on the actor, with the two gradient rows** (R2-A6, R2-A15); `PackVersion`, `EngineVersion`, boundary B; a canonical integer digest (F10). |
| C17 | Catalogue hooks | `src/Marrow.Services/Providers/ProviderRegistry.cs` | no | `CatalogChanged(provider, oldList, newList)`, raised after the persist in `UpdateCatalog` (`:1435`) with the two list **references** only; the diff by (id, name, detail) runs on the engine actor (R11). `EnumerateCatalogs()` for the launch reconcile and the refresh. (AC-3: no `CatalogLoad`; the refresh no longer reloads provider lists, R2-A5.) |
| C18 | `ModelShades` (UI-thread facade) | `src/Marrow.App/Colour/ModelShades.cs` | no | `Resolve(provider, modelId, name, effortWord, theme) -> ShadeState`. Synchronous, no I/O, no allocation once warm; **reads only the snapshot's frozen tables; a miss returns the previous snapshot's value or Pending and posts a request, it never computes a shade**. Coalesces snapshot changes and refresh progress into one dispatcher post, at most 10 per second. Owns the UI gate (section 9). |
| C19 | App wiring | `src/Marrow.App/AppModel.cs`, `AppDelegate.cs` | no | Constructs the engine in the `AppModel` constructor; `Prime()` before `ShowMain`: the pack-only snapshot synchronously, then the stored snapshot within 50 ms (section 3, step 1); starts the network triggers after bootstrap. Subscribes to `CatalogChanged`, resume and `INetworkStatus`. Stops the engine on exit. |
| C20 | Slider carrier | `src/Marrow.App/Views/Composer/EffortSlider.cs` (`:441-547`, `:782`, `:860-899`, `:1073-1215`, `:1302-1357`), `HydraFusedPairIcon.cs:66-125`, `Views/Settings/ModelsSettingsPage.cs:415` | no | `TrackLook` carries a `BrandPalette`; `EffortBrand` stays a facade over the fixed palette. A late colour or a re-spacing reaches the card through `Track`/`UpdateLook`. Identity change (cross-fade) only for a provider or kind change; a colour change of the same look is a glide (C25). |
| C21 | Loading water and ink arrival | `src/Marrow.App/Views/Composer/LiquidTrack.cs` | no | The locked v22 look (OD-0050, OD-0052), unchanged. |
| C22 | Bake tool | `tools/colour-baseline/` | no | Runs the same engine headless on the real network and writes the next pack `pack.v<N+1>.json` (append-only for seeds; section 5.6). Local only (OD-0058). Entry point for the OD-0065 evidence run (section 10.5). |
| C23 | Network inventory | `porting/network_inventory.md` (new) | doc | Section 8. |
| C24 | `RefreshRun` | `src/Marrow.Services/Colour/RefreshRun.cs` | no | The OD-0067 run: single-flight, phases, **staged publication**, progress, per-source status, summary (section 3A). |
| C25 | Re-spacing glide | `src/Marrow.App/Views/Composer/EffortSlider.cs` (`TrackEffect`), `LiquidTrack.cs` | no | Glides the final stops, **the title, the glow and every s-driven dial** inside the same `TrackEffect` (no time reset), from the snapshot's two precomputed gradient rows and one uniform `t` (section 4.6; R5, R2-OD66-G, R2-A15). |
| C26 | Test infrastructure | `tests/Marrow.Testing/` | test | `FailClosedHandler` (**loopback allowed, everything else fails**) installed by a `ModuleInitializer` in every test assembly, with a build check that every test project references `Marrow.Testing`; `FakeNetworkStatus`; `FakeColourEngine` for the App fixtures (section 10.1; R6, R2-A9). |

---

## 3. Data flow

The example is a new maker `acme`, whose first model `acme/nova-2` appears on OpenRouter after the pack was baked.
Timings are measured medians from one machine on one day (`chain_timing.json`, `latency.json`); indicative only.

1. **Launch** (R4; AC-3: R2-A7, R2-7).
   - The `AppModel` constructor creates `ColourEngine` with its injected `URLSession`, `ColourStore`, the embedded pack
     and `INetworkStatus`.
   - `Prime()` step 1, synchronous, no file I/O: the **pack-only snapshot** (palettes, lineups and shade tables
     precomputed by the bake and embedded in the pack; about 5 ms to load). It always exists before `ShowMain`, so the
     first frame is never the compiled fallback.
   - `Prime()` step 2, awaited up to 50 ms: read `snapshot.json` and verify its own content hash. If it verifies it
     becomes snapshot 0 **even when its pack, engine or store digests no longer match** (an app update): it is the
     colour this machine last showed, an earlier agreed colour. The engine then recomputes on the actor and every
     change **glides**. Only a maker absent from it is Pending.
   - If step 2 misses the 50 ms budget (cold disk, Defender, OneDrive) the window shows the pack-only snapshot: pack
     and locked makers draw their colours; a post-pack maker draws clear **Pending** water; when the stored snapshot
     lands, post-pack makers take the normal Pending-to-Ready arrival and anything else that differs glides.
   - **Known** means only "the first frame a card draws already has this colour". Any later change of that card's
     colour is a glide, never a snap. Network triggers start only after bootstrap.
2. **Noticing a model.** Each trigger posts `(provider, list reference)` or `(provider, modelId, name)` to the actor,
   at about 1 µs on the caller's thread, with a 250 ms debounce.
   - **T1:** `CatalogChanged` (any CLI, ACP or API list; alias drift through name and detail). Diffed on the actor.
   - **T2:** the launch reconcile over `EnumerateCatalogs()`, pinned models and open threads.
   - **T3:** `ModelShades.Resolve` for a model with no snapshot entry.
   - **T4:** the market watch (section 6.2): a keyless `/api/v1/models` just after each 6-hour UTC boundary (1 to 10
     min jitter, retried inside the 15-minute window), and at launch, resume, network-available and Refresh when the
     current window has no boundary snapshot yet. Later fetches in a window only prefetch. The lineup moves only when a
     boundary snapshot is published, at boundary + 15 min in server time, the same instant for everyone.
   - **T5:** reserved for a router's answer naming the model that answered (OWNER, O5).
3. **Reconcile** (pure, under 5 ms).
   - Every model is mapped to its maker (section 4.1) and placed in its maker's lineup at boundary B (section 4.4).
   - Each maker with no usable seed (section 5.5) and a catalogue row is queued for a lookup. A maker with no catalogue
     row and no pack entry is queued for the slug-only lookup (section 4.2).
   - `acme` enters the snapshot as **Pending**, carrying its neutral shade (section 4.3).
4. **Seed fetch** (`SeedChain`). Up to 2 makers in parallel; makers the user can see go first (order cannot change any
   result: every input is global and every post-pack derivation is independent).
   - **Stage A** (0 to 0.4 s, in parallel): the pinned toc match (0 ms); HF `overview` for the org named by the
     catalogue's `hugging_face_id` (0.18 s); the HF org page for its website link (0.33 s); OR `/providers`.
   - **Stage B** (0.2 to 1.2 s, speculative): the HF avatar (0.15 s); the domain vote, then faviconV2 at 256 px (0.10 s).
   - The result is decided by priority, never by which answer arrives first.
5. **Decisive, median 0.5 s** (p95 1.2 s, max 2.0 s).
   1. The seed and its evidence (status, SHA-256 and 64 x 64 resample per source; the input-set digest; `SeedVersion`;
      `PackVersion`) are written atomically to `seeds.json`.
   2. `acme`'s palette is derived against the pack's frozen reserved set (section 5.3), under 50 ms. No other maker is
      touched.
   3. One settled snapshot is published, with one coalesced UI post.
6. **On screen** (UI gate on, section 9). Pending to Ready: the v22 ink arrival plays (ink at max(t_ready, 0.8 s)).
7. **3 s with no decision.** The card releases its pending neutral as ink; a later seed arrives into the neutral water.
8. **Undecided.** Nothing is saved but scheduling data. The maker backs off (section 6.1) on the monotonic clock and the
   neutral stays. No number of failures ever turns into a decisive outcome.
9. **Later lineup change** (OD-0066). At the next boundary the catalogue shows a stronger sibling; that maker's groups
   and levels are recomputed and one snapshot is published at boundary + 15 min. Each open track **glides** its final
   stops, title, glow and s-driven dials over 1.2 s inside its running effect (section 4.6). The palette does not
   change. Until then the new sibling, if a provider list already shows it, is placed by its id alone (section 4.4).
10. **Next pack.** When the next pack is adopted (section 5.6) and carries `acme`, the pack seed outranks the local one;
    if they differ, the card glides to the pack palette. Every machine then holds the same seed.

---

## 3A. On-demand refresh (OD-0067) (R1)

### 3A.1 Contract

`ColourEngine.RefreshNow(): RefreshRun` (C24). ENGINEERING DECISION, test-first in U19b:
- **Single-flight.** A press while a run is in stage 1 (below) returns the same `RefreshRun` (joins it). A press after
  stage 1 joins the background remainder and never restarts it. A press before `Start()` queues and joins the first
  run; its summary baseline is snapshot 0, never the compiled palette (R2-A4).
- **Posts to the actor and never runs on the UI thread.** `RefreshNow` itself is a post of about 1 µs. The run does no
  provider handshakes, no list parsing and no catalogue writes on the UI thread: it does not reload provider lists at
  all (R2-A5).
- **Same path as the automatic checks.** It brings forward exactly what the automatic path does, so its result equals
  "wait for the automatic path" on the same public facts (OD-0065). It never discards a seed, never touches a pack
  seed, and never changes how failures are counted (no counting exists; section 4.2). `Reset` stays API-only.
- **Cooldown (R2-A11).** A Refresh-triggered market fetch happens at most once per 120 s (the CDN max-age); a press
  inside that time reuses the last result. Refresh-triggered failures never advance a back-off ladder.

### 3A.2 Phases

1. **Catalogue.** A market fetch (N1) with `Cache-Control: no-cache` on the engine's cache-less `URLSession`, through
   the single-flight fetch (a fetch in flight is joined, not duplicated). If the current 6-hour window has no boundary
   snapshot yet (offline at the boundary, just launched), this fetch becomes it, exactly as the automatic launch fetch
   would; otherwise it only **prefetches**: it can place newly listed models by id and start their makers' lookups
   (with the inputs the boundary lineup gives, so a maker it does not list yet runs the slug-only chain, as it would for
   every user in this window), but it never changes the lineup's membership or facts before the next boundary (section
   6.2; R2-1, R2-A8).
2. **Providers.** No reload. The run reads the provider lists as currently loaded (`EnumerateCatalogs()`), so every
   model the user can pick is placed; the providers' own load paths keep those lists current. The summary shows when
   each list was last loaded (R2-A5, R2-A10).
3. **Makers.** Every Undecided maker's back-off is brought forward and the normal `SeedChain` runs, new makers first in
   the HF queue. A local (non-pack) seed is re-verified only if it has no observation in the current UTC day window,
   which is exactly what the automatic path does at its first online moment of that window (section 5.5; R2-A3,
   R2-A11). The replacement rule (two agreeing observations in two distinct day windows) is unchanged.
4. **Applying, in stages (R2-A4).**
   - **Stage 1** publishes one snapshot when phase 1 and the makers the user can see have finished, bounded at 8 s,
     with one shared glide timestamp for every track it changes.
   - After stage 1, background results (makers off screen, re-verifications) publish through the normal per-batch
     path, and the summary updates from them. A run never holds back an automatic publication.

### 3A.3 Progress and summary

- **Progress:** `(phase, k, m)`: catalogue; makers k of m; applying. Throttled to at most 10 updates a second and
  merged into the same coalesced dispatcher post as the snapshot (R11).
- **Summary:** the diff of `ColourSnapshot(before)` against `ColourSnapshot(after)`, shown as **counts plus the top 5
  per category**; the full list sits behind a virtualised details view that refreshes only at stage ends (R2-A14):
  - new models seen: "placed now at level k of n (by name); family re-spacing at <next boundary + 15 min, local
    time>" (R2-A8);
  - families re-spaced (maker, members whose level changed);
  - new makers coloured;
  - still pending, with the reason (offline, blocked by the network, rate-limited, source answered something that was
    not the expected content);
  - crowded makers (section 5.2);
  - provider lists, with the time each was last loaded;
  - "checked at" with the boundary B in use, and whether this run's fetch was the window's snapshot or a prefetch.
- The control's look (proposed: Settings > Appearance, a "Colours" row with Refresh and a "last checked" line) is new
  UI and waits for its canvas (W6). U24 builds it after approval.

### 3A.4 Tests (U19b)

- Refresh against waiting for the automatic ticks on the same fixtures gives identical digests at every boundary.
- A double press (200 ms apart) and a press during the automatic boundary fetch give one run and one fetch; a press
  after stage 1 joins the background remainder; presses inside 120 s reuse the last fetch.
- **A mid-window Refresh changes neither membership nor facts** until the next boundary publication; a Refresh in a
  window with no snapshot yet makes that fetch the window's snapshot, identical to the automatic launch fetch.
- Refresh never changes a pack seed; a local seed changes only after two agreeing observations in two day windows; a
  Refresh in a window that already has an observation re-verifies nothing.
- A response generated before the current boundary (`Date - Age`) never becomes the window's snapshot.
- Progress posts at most 10 per second; stage 1 publishes exactly one snapshot; automatic publications during a run
  are not delayed.
- Refresh performs zero provider reloads and zero shade computations on the dispatcher thread; 20 presses offline do
  not advance any back-off ladder.

---

## 4. Lookup chain

### 4.1 Model to maker (`Makers.Resolve`, pure; the first rule that matches wins)

All string work is ordinal and culture-free: `ToLowerInvariant`, `StringComparison.Ordinal`, never `ToLower()` or a
culture comparison (F7: under tr-TR, `ToLower()` turns `Kimi-K2-Instruct` into `kimi-k2-ınstruct`).

1. **Strip routing.** Remove a leading `~`, a `:variant` suffix and a `[...]` suffix. Remove `opencode-go/` and a Pi
   router host segment (in Pi's `openrouter/anthropic/...` the maker is the second segment). An `openrouter/<x>` row is
   a router, giving `MakerKey(Router)`.
2. **Vendor prefix**, through the alias table (`meta-llama` -> `meta`, `x-ai` -> `xai`, `z-ai` -> `zai`,
   `bytedance-seed` -> `bytedance`, `mistralai` -> `mistral`, `moonshotai` -> `moonshot`, ...). A router vendor gives
   `Router`.
3. **First-party provider:** Claude -> anthropic, Codex -> openai, Antigravity -> google, DeepSeek, Meta, Z.ai,
   Grok -> xai.
4. **Catalogue match:** a normalised id or name equals a row **of the current boundary lineup** (pack rows,
   tombstones and the boundary snapshot; never a prefetched or provider-list row); that row's maker. Two makers
   matching: the ordinally smaller maker slug (R2-10).
5. **Family word**, from the pack's table, **anchored to the start of the last id segment** (`^(gpt|o\d+|codex)\b`,
   ...), never matched inside a name: `claude`, `gpt|o\d+|codex`, `gemini|gemma`, `grok`, `glm`, `kimi`,
   `muse|llama`, `deepseek`, `qwen|qwq`, `mistral|codestral|devstral`, `minimax`, `nemotron`, `granite`, `sonar`,
   `command|aya`, ... So `hermes-3-llama-3.1-70b`, `dolphin-mistral-24b`, `aion-rp-llama-3.1-8b` and `solar-pro4` (an
   unanchored `o\d` matched it) never resolve to Meta, Mistral or OpenAI by a word inside their names (R2-10).
6. **Name prefix** before ": ".
7. **Otherwise `ProviderOwn`:** the provider's own locked palette.

An unrecognised word never starts an online lookup. Which palette a harness provider shows is a policy switch:
OpenRouter and Pi with a router host use the maker (OD-0063); every other harness keeps its own until O6.

### 4.2 Maker to seed (`SeedChain`; keyless; fixed priority)

**Inputs are global facts only** (F3). A lookup uses:
- the pack (toc entry, owner seed, baked seed, makers-table domain if any);
- the maker's catalogue rows at boundary B: `hugging_face_id` (the most common value across the rows, ties broken
  ordinally), names, ids;
- the OR `/providers` response (a public list), fetched after B;
- the HF org overview and org page for that org, and the avatar it names;
- the exact-slug HF probe `/api/organizations/<slug>/overview` (the same request for everyone; it replaces AC-1's fuzzy
  `quicksearch`, which returned XiaomiMiMo, XiaomiRobotics and xiaomi-research for "Xiaomi" with no exact match).

A maker with **no catalogue row** (CLI-only) runs the slug-only chain (toc by slug, the exact-slug HF probe, faviconV2
only on a makers-table domain). Its input-set digest says "slug-only"; when a catalogue row appears the input set
changes and the chain re-runs. **A missing input** (for example `/providers` not fetched yet) makes the source
Unavailable, never "nothing to look up".

Each source returns one of three outcomes. AC-2 validates every response before classifying it (F2, R2):

| Outcome | Covers |
|---|---|
| **Hit** | A validated success: JSON with `Content-Type: application/json` that parses and has the expected fields, or image bytes whose magic matches the declared type and decode; the final host (after at most 3 redirects) is the requested host or its documented CDN. |
| **Miss** (decisive) | A **validated** negative from the origin: an HF 404 or 410 with HF's JSON error body; faviconV2's own 404 placeholder (726 B, by SHA); the HF placeholder avatar (43,889 B, by SHA); an image that decoded and is under 32 px or under 2 % opaque; an aggregator domain; no toc entry (a pack fact); no `hugging_face_id` on any catalogue row **and** a validated 404 from the exact-slug probe; no accepted domain from validated inputs. |
| **Unavailable** (transient) | Everything else: timeout (4 s per request), DNS, TLS, connect, 401, 403, 407, 429, 5xx; **any 2xx whose content type or magic bytes do not match** (a proxy's HTML block page, a captive portal); a final host that is not the requested one; a truncated body; bytes that pass the magic check but do not decode; a missing input. |

There is **no rule that turns Unavailable into Miss**, whatever the count or the elapsed time. Back-off timing inside
one process uses the monotonic clock (`SwiftClock` monotonic ticks), never wall time; across restarts only **window
indices** derived from server `Date` headers are persisted, never ticks (`SwiftClock.Now` restarts at every boot,
`SwiftClock.cs:27-32`; R2-A3). Persisted scheduling data only decides *when* to try, never *what* the result is.

The chain, in priority order:
1. **Owner seed, then baked seed, from the pack** (no network). Ends the chain and outranks any local seed (section 5.5).
2. **Pinned toc colour, if chromatic** (OKLCH C >= 0.06 and 0.25 < L < 0.95), matched by normalised slug, slug without
   `-ai`, display name and HF org.
3. **HF org avatar**, then `Dominant`. Before faviconV2 because it is the maker's own mark for 20 of 22, byte-identical
   on repeat fetches for 22 of 22, and favicons are often black or white tiles.
4. **Google faviconV2** at 256 px on the voted domain, then `Dominant`.
5. **Mono.** Every source decisive, none chromatic. A normal outcome (9 of 25 makers).

How the result is decided:
- The first chromatic Hit decides, but only if every source above it was decisive. An Unavailable above the eventual
  winner makes the result Undecided, and nothing is stored.
- Lower sources run in parallel to save time; their answers count only once everything above them is decisive.
- 8 s cap per maker. One HF budget for all makers: at most 60 requests per 300 s, honouring `RateLimit` and
  `Retry-After`; at most 3 HF API calls per maker.

Never used: DuckDuckGo `ip3`, maker sites, the lobehub PNGs, Wikidata, OpenRouter author pages, icon.horse, Clearbit,
logo.dev, Brandfetch, faviconkit, Google `s2`, `og:image`, `theme-color`, HF `quicksearch`.

### 4.3 Seed to palette (pure)

1. the fixed palette, if the maker is locked;
2. otherwise `PaletteDeriver.Derive(seed, reserved(maker), rule)` (section 5.3);
3. while Pending, `Derive(Mono, reserved(maker), rule)`, the neutral.

Seeds are independent of each other, and so are post-pack palettes; fetch order never matters.

### 4.4 Palette to shade (pure; IR-3)

`rule.Shade(palette, level of levels, effort word, theme)`:

- **Effort (F5).** `e` comes from **one global word ladder**, versioned in the pack with the rule: off/none 0,
  minimal 1/6, low 2/6, medium 3/6, high 4/6, xhigh 5/6, max 1; no knob gives 1; an unknown word gives 1/2 and is
  reported. The model's own list only decides which stops its knob offers. So the same (model, effort word) has the
  same shade through every provider: IR-1 gave deepseek-v4-pro "high" dE 0.045 between DeepSeek's [low, high, max] and
  Command Code's [high, max]; IR-2 gives 0 (`metrics.hardening.F5_same_word_same_shade`).
- **Effort synonyms (R2-3).** Before the ladder, the word is lower-cased (ordinal) and mapped through the pack's
  synonym table: `extra-high`, `extra_high`, `extra high` -> `xhigh`; `ultra` -> `max`. These are exactly the extra keys
  of the port's `ACPSession.EffortRanks` (`ACPSession.cs:551-552`), with the same ranks. The pack, not the provider
  code, is the colour's single source (it must be versioned with the rule and include `off`); a test asserts every
  `EffortRanks` key maps to the same rank on the pack ladder, and another enumerates every word a provider can emit
  (Claude, Codex and Copilot `supportedReasoningEfforts`, Command Code, ACP agents, DeepSeek, Meta, Pi, OpenRouter
  reasoning) and asserts none falls to the unknown 1/2 (`metrics.hardening.R2-3_effort_synonyms`).
- **Lineup** (`LineupRanker`, OD-0066), over the global catalogue at boundary B (the pack's rows and tombstones plus
  the window's boundary snapshot, section 6.2) and the pack's builtin rows, never the user's lists:
  1. *Eligibility:* text chat; not an alias (`~...`, `alias_target`); not expired at B; not a router or a non-chat
     specialist; `created` not later than the snapshot's generation time plus 1 day (a millisecond value is
     ineligible, R2-A1). AC-3 drops AC-2's "with tools": the executable reference never tested it, and a row without
     tools is still a model a user can pick, which would otherwise fall to the weaker virtual placement
     (`supported_parameters` is therefore not a lineup input; R2-9).
  2. *Collapse (OD66-1, OD66-5, F9).* Same-model labels (`:free`, `:batch`, `-preview`, `-exp`, `-thinking`,
     `-latest`, `-instruct`, dated snapshots ...) always collapse. The member's facts come from one row chosen by a
     total, input-order-free rule: the base row, then the earliest `created`, then the higher price, the larger context,
     the id compared ordinally.
     - **Dated successors (R2-OD66-B).** A dated row (`-2512`, `-2025-07-28`) created more than 30 days after the base
       row (or, with no base row, after the earliest dated row) is its own member, named `<base>-<date digits>`, with
       the base's class and version and its own day, so it ranks above its base and re-spaces the lineup. Mistral
       Large 3 (`mistral-large-2512`) is now Mistral's level 1 and the 2024 `mistral-large` row is legacy. Price is not
       a criterion: older dated snapshots are often priced unlike their alias and must not outrank it.
     - **Speed variants (OD66-1, R2-OD66-C).** `-fast`, `-highspeed`, `-ultraspeed` collapse **only onto a base row
       that exists in the same lineup**. Otherwise the variant is its own member ranked **just below its base's class**
       (tier minus 0.05), whatever up-words it carries (`gpt-6.2-astra-pro-fast` sits below `gpt-6-astra-pro`), and it
       shares its base class's major class for the level allocation. `highspeed` and `ultraspeed` join `fast` on the
       speed ladder.
  3. *Class* (tier words). IR-2 refinements: the OpenAI family ladder sits on the generic scale (Luna 4.5 between plain
     and plus, Terra 5.5, Sol 6.5 between pro and max, Astra 9), so Sol outranks `gpt-5.5-pro` and `o1-pro` and Luna
     outranks a plain `gpt-4` (OD66-3); down-words apply inside a family (X-mini < X, X-nano < X; OD66-2); `muse` is a
     Meta class word above Llama (OD66-10); modifiers only lift. **IR-3, next generation (R2-OD66-F):** a member with
     no family word and no down-word, sharing a family member's stem (`gpt`), whose major version is above every family
     member's, ranks above the family: a plain `gpt-7` is OpenAI's level 1, `gpt-7-mini` is not lifted. (A generation-
     first rule for every member would put `gpt-6-luna` above `gpt-5.6-terra`, against the owner's Terra > Luna.)
  4. *Legacy cap (OD66-3, R2-OD66-A):* a member is legacy if a member of the **same or a higher class** (next
     generation, then tier) was released more than 548 days after it; legacy members rank below every current member
     (lineup-relative, never the clock). A weaker newcomer (a Haiku, Gemma, Luna Nano, Muse Glimmer, a re-listed old
     model) can therefore never make the flagship legacy and take the top shade; `o1-pro`, `gpt-4` and `gpt-3.5-turbo`
     stay legacy because newer GPT-6 members outrank them.
  5. *Then* version, release day, price. Members equal on (class, version, day, price) form **one group with one
     shade** (OD66-9); context and id only order the list.
  6. *Levels (OD66-4, OD66-7, R2-OD66-E).* Each palette has V visible levels, measured: positions spaced by perceptual
     distance along the shade path at max effort, the count capped so adjacent levels differ by at least 0.02 OKLab
     (`Levels` in C6). Measured: 6 to 9 for the chromatic palettes, **4 for Silver** (W1). If the groups fit, every
     group gets its own level (one group: s = 0.5, the anchor; few groups stay near it). If not, the scale is
     top-anchored with exactly V - 1 steps, given in this order:
     1. the flagship alone, when it differs from the next group by version, day or price (never when only a modifier
        sets it apart: Astra Pro | Astra is not worth one of Silver's three steps);
     2. family-word boundaries, top-down (Astra | Sol | Terra | Luna);
     3. every other class boundary, top-down (generic tiers, legacy, next generation);
     4. modifier boundaries inside a family word (Sol Pro | Sol);
     5. each class's head, then each class's second member, and so on.
     The rest merges from the bottom. OpenAI under Silver now shows **Astra (+Pro) | Sol (+Pro) | Terra with the
     generic Pros | Luna and below** after Astra joins, and **Sol Pro 6.1 | the Sols | Terra | Luna and below** before
     (the owner's OD-0066 example; `metrics.hardening.R2-OD66-E_owner_example_astra_sol_terra_luna`).
  7. *Off-catalogue placement (OD66-6, R2-2, R2-OD66-D):* a model in a provider list but not in the lineup (before the
     catalogue lists it, CLI-only) is placed by the same precedence as one more virtual member, without moving anyone
     else, **from its id alone**: it never takes price or `created` from a provider list (the user's own OpenRouter API
     list, a CLI), so one id gives one placement from every provider. A model with (class, version) peers inherits the
     facts of the earliest of them, so it shares that peer's level and can never outrank its peers; a model with no
     peer is treated as current. `claude-fable-6` seen in the Claude CLI first takes the top level (tied with Fable 5.1
     until the catalogue lists it). **Withdrawn rows never come here:** a pack row that leaves the catalogue stays in
     the lineup as a **tombstone** with its last facts (section 6.2), so a withdrawn `claude-opus-4.1`, and every one
     of the 314 catalogue members withdrawn in turn, keeps exactly its listed level.
  8. *Harness spellings (R2-OD66-D).* Before lookup and placement, the pack's rewrites put a harness id into catalogue
     word order (`claude-4.5-opus` -> `claude-opus-4.5`, `opus-4.5` -> `claude-opus-4.5`), and effort or thinking
     suffixes (`-low` ... `-xhigh`, `-max`, `-thinking`) are stripped when what remains is a lineup member (`-max` only
     then, since it is also a modifier). `claude-5.5-opus-high-thinking`, `claude-opus-5.5-high`, `grok-4.7-high`,
     `muse-spark-1.2-high` and `gemini-3.1-pro-high` now take exactly their member's s (17 spellings checked).
  9. *Aliases (OD66-8):* the catalogue's `alias_target` or `canonical_slug` when present; otherwise the members with
     all the alias's words, skipping modifier siblings the alias does not name, preferring an exact price match.
- **Under `IdentityRule`** (until W1) every state shows the anchor; the ranker still computes groups and levels, so the
  determinism tests cover them now.

`intensity.py` IR-3 is the executable reference: `intensity-table.json` `metrics.hardening` re-runs every round-1 and
round-2 case (all pass; MD5 in section 14.3).

### 4.5 What the card shows

| `ShadeState` | Card |
|---|---|
| Ready | The palette. An arrival plays only if the card had shown Pending. |
| Known | The colour the card's first frame draws (the pack-only or stored snapshot 0): drawn directly, no arrival. Any later change of that card's colour glides; Known never means a snap after a frame was drawn (R4, R2-A7, R2-7). |
| Pending | Clear living water. After 3 s on `SwiftClock`, the card releases the carried neutral as ink. |
| Router | Clear water at the effort's motion (O5). |

### 4.6 Re-spacing glide (C25, U15b; R5, OD66-11)

- A colour change of the same provider and kind keeps the **same `TrackEffect`**: the same `startedAt`, currents,
  bubbles and flow. Only the provider or kind changing creates a new effect with the 0.3 s cross-fade
  (`EffortSlider.cs:860-899`).
- The glide interpolates the **final stops** over 1.2 s (never `s`, so a tiny change of s is never a jump). The two
  gradient rows (from, to) **come precomputed with the snapshot** (C16), so they are present on the first frame that
  sees the snapshot; the base shader blends them with a uniform `t`. No per-frame allocation, no OKLab on the UI thread.
- **Glide scope (R2-OD66-G).** The title colour, the glow colour and every s-driven dial (`caustic_alpha`,
  `glow_alpha`, `breathe_saturate_amp`, Silver's `specular_scale`, Grok's `oil_sheen_alpha`) interpolate with the
  **same `t` over the same 1.2 s** as the stops. The 0.6 s motion ease applies only to effort changes. (Measured: a
  re-spacing moves Opus 5.5's title by dE 0.025 and Sol Pro's `specular_scale` from 1.15 to 0.92.)
- **Start and retarget (R2-A15).** Each track starts its glide on the first frame its rows are present, at that frame's
  time; with precomputed rows every track that a snapshot changes starts on the same frame. A retarget mid-glide takes
  as its new "from" row exactly what the shader drew (the RGB blend of the current rows at the current `t`), so a
  retarget is never a jump. A glide requested during an arrival waits for the arrival to end. Inside a refresh, stage
  1 publishes one snapshot, so every track it changes starts on one frame.

---

## 5. Palette rules (`PaletteDeriver`, constants versioned `DeriveVersion` D2)

### 5.1 Target from the seed
- **Chromatic.** Hue kept. L and C clamped into the hue band, interpolated linearly between band centres:

  | Hue band (degrees) | Heart L | Heart C |
  |---|---|---|
  | 345-25 | 0.50-0.70 | 0.13-0.21 |
  | 25-45 | 0.52-0.70 | 0.12-0.20 |
  | 45-80 | 0.62-0.78 | 0.12-0.18 |
  | 80-110 | 0.72-0.80 | 0.12-0.17 |
  | 110-150 | 0.58-0.76 | 0.12-0.18 |
  | 150-240 | 0.55-0.75 | 0.09-0.15 |
  | 240-290 | 0.45-0.68 | 0.12-0.22 |
  | 290-345 | 0.50-0.70 | 0.12-0.22 |

- **Mono** (and the pending neutral): start at L 0.56, C 0.045, h 250. Band L 0.42-0.68, C 0.05-0.07; a hue move costs
  |dh|/360.
- The heart L is never above 0.80.

### 5.2 Nearest free shade
- **Grid** (integer indices): hue offsets -180 to +180 in 3-degree steps; L in 0.02 steps across the band; C in
  {C, 0.8C, 1.2C} clamped (mono {0.05, 0.06, 0.07}).
- **Gamut:** a stop out of sRGB loses chroma by bisection (fixed iteration count). A heart losing more than 0.02 C is
  rejected.
- **Cost:** `|dh|/30 + |dL|/0.08 + |dC|/0.06`, quantised to an integer number of 1e-9 units before comparison.
- **Winner:** the cheapest candidate passing the gates, under a total order: cost, |dh|, positive dh, L index, C index.
  If none passes, the candidate with the largest minimum H1 distance wins, flagged `crowded` (listed in the refresh
  summary).
- **Gates:**
  - **H1:** the heart at least **0.060** OKLab from every reserved colour (ENGINEERING DECISION pending W4; AC-1 had
    0.080). Measured capacity of the AC-1 band grid by greedy packing (`cap.py`, 7,888 candidates, best of 5 orders):
    51 hearts at 0.080, 64 at 0.070, **97 at 0.060**, 143 at 0.050. Today's need is about 75 (15 locked, 2 fast, about
    58 catalogue makers), so 0.080 would make crowding the norm (R8). For a gradient brand all three stops count.
  - **H2:** for every reserved maker and every grid state of `rule.Cloud`, the same-state heart at least 0.035 away.
    DeepSeek and Meta grandfathered. Under `IdentityRule` H2 equals H1.
  - **H3:** within the maker, adjacent **levels** differ by at least 0.020 at max effort (guaranteed by `Levels`, IR-2 and IR-3)
    and adjacent efforts by at least 0.012 (see W1: the global ladder measures 0.009 median).
- **Speed:** filter by H1, sort, then H2 and H3 lazily over clouds cached per (palette, rule version).

### 5.3 The reserved set (order-independent, OD-0065) (F4, R9)

`reserved(X)` for a **post-pack** maker (no pack seed, catalogue or CLI-only), in a fixed order, is **frozen per
`DeriveVersion`**, not per pack (R2-5):
1. the 15 locked palettes, including the null-provider Purple;
2. the owner's NVIDIA green, once approved;
3. Fast `(1.0, 0.84, 0.36)` and FastFill `(0.93, 0.66, 0.13)`;
4. the palettes of the makers on the **frozen reserved list of this `DeriveVersion`**: the active pack makers at the
   time the `DeriveVersion` was cut, in their bake-epoch order. The list ships in every pack of that `DeriveVersion`
   and changes only with a `DeriveVersion` bump (an owner-approved build). Retiring an inactive maker or adding a
   newly baked one never changes it.

A **pack** maker baked in a later pack of the same `DeriveVersion` gets the **next bake epoch, after every existing
one** (append-only; never an earlier epoch because its first `created` is older). The bake derives it exactly as a
post-pack maker (against items 1 to 4); only if that palette fails H1 against an earlier-epoch baked maker outside the
frozen list is it re-derived against items 1 to 4 plus those makers. Existing pack makers never change when a maker is
baked.

A post-pack maker is derived against items 1 to 4 only, **never against another post-pack maker and never against a
pack maker outside the frozen list**. What follows:
- No local epoch, no predecessor state, no cascade: a post-pack palette is a function of (`DeriveVersion`, its own
  seed). It is the same on builds N and N+1 whatever pack N+1 adds or retires (the round-2 probe found 52 post-pack
  seeds whose heart moved when one maker was baked; with the frozen list none can move, by construction).
- A maker moving from post-pack to baked keeps its palette unless the bake must resolve a collision; that change is a
  pack adoption (global, glided).
- Two post-pack makers can sit closer than H1 until the next pack. The engine reports it as a `collision pending bake`
  diagnostic; the bake tool resolves it for everyone.
- Derivation is O(1) per maker against a fixed set (R8: no O(M^2) re-derivation).

### 5.4 From the heart to the full palette
- **Stops:** dark (L-0.07, C x 1.02), heart, light (min(0.88, L+0.10), C x 0.88). Same hue, gamut-fitted.
- **Title per theme:** the heart lifted 22 % towards white, then L adjusted until WCAG contrast is at least 3:1 against
  the card glass of each theme.
- **Glow:** the heart at 0.6. **Spark:** white. `HasOilSheen` and `IsGradient` false for a derived maker; the heart L at
  least 0.18 from the dark window background and 0.12 from the light one.

### 5.5 Determinism, versions and seed authority (F1, R3, F7, F10)

**Versions.** `EngineVersion` AC-3 = (`SeedVersion` S2, `DeriveVersion` **D3**) plus the pack's `PackVersion`. D3: the
frozen reserved list, append-only epochs and IR-3 (the seed chain is unchanged, so S2 stays):
- `SeedVersion`: the chain, the response validation, the resample, Dominant's thresholds and the grid snap.
- `DeriveVersion`: the bands, gates, reserved-set rule and the intensity-rule version (Identity until W1; IR-3 draft).
- `PackVersion`: the data (section 5.6). A pack names the minimum engine it needs.

**What a palette depends on:** `DeriveVersion`, `SeedVersion`, the maker's seed and, for a pack maker, `PackVersion`;
a shade adds the lineup at boundary B. Nothing else: no wall clock, locale, user lists, keys, fetch history, fetch
order, provider spelling, process lifetime or other post-pack makers.

**Seed authority, highest first:**
1. an owner seed in the running pack;
2. a baked seed in the running pack;
3. a local seed, valid only while its key matches: (`SeedVersion`, the pack's toc digest for that maker, the input-set
   digest). A key mismatch (the pack's toc gained the maker, the seed rules changed, the maker gained a catalogue row,
   the most common `hugging_face_id` changed) re-runs or re-derives it. **Only a seed computed for the current key is
   ever shown** (R2-4): if the new result is decisive at once (a pack toc entry, a re-derivation from stored
   resamples) the card glides to it; otherwise the card glides to the **pending neutral**, exactly what a fresh install
   with the same failure shows, until the new chain is decisive. A seed from different inputs never stays on screen
   (the slug chain and the `hugging_face_id` chain read different HF orgs for `xai`, `meituan` and `meta`).

A switch between authorities glides (section 4.6).

**Local seeds are a cache of the current public evidence, not a lock.**
- When only Dominant or the snap changes (`SeedVersion` bump without a chain change), the seed is recomputed from the
  stored 64 x 64 resamples: identical inputs give identical outputs on every machine.
- **Re-verification on global day windows (R2-A3, R2-8).** Each local seed is re-verified once per UTC day window
  (day index = server time / 24 h, server time = the last `Date` header plus monotonic time since it), at the first
  online moment of that window; window indices are persisted, ticks never are, so a process that lives 10 h a day and
  a machine that reboots re-verify in the same windows as one that never sleeps. A Refresh re-verifies only a seed with
  no observation in the current window (what the automatic path would do at that moment); it never adds a second
  observation to a window.
- A re-verification replaces the seed only with a decisive result whose evidence SHA differs **and** that observations
  from **two distinct day windows** agree on (hysteresis against a flapping source). A failed re-verification keeps
  the seed.
- **True bound:** a long-running user replaces a seed within two day windows of the evidence changing (at most 48 h
  online); a fresh install adopts the current evidence at once; the next pack freezes it for everyone. AC-2's "about
  30 h" was wrong (a 24 h cadence with two observations is up to 48 h) and depended on Refresh presses.
- **Alias merges (R2-A13).** When a makers-table update merges two stored keys, both local seeds are dropped to
  Undecided (the card glides to the pending neutral) and the merged key re-runs its chain; no stored subset decides
  which seed survives. An alias rename without a merge remaps the key.

**Arithmetic and representation.** `double` only; costs quantised; searches over integer indices; seeds, grid
positions and levels stored and digested as **integers**, never as snapped doubles (F10: a snapped 0.7 serialises as
0.7000000000000001). Every parse and format uses `CultureInfo.InvariantCulture` or `Utf8Parser`; the engine actor sets
`CultureInfo.DefaultThreadCurrentCulture` to invariant; Core and Services build with CA1304, CA1305, CA1307, CA1309
and CA1310 as errors (F7: de-DE `double.Parse("0.000003")` is 3).

**Stability rules:**
- A pack or owner seed is never re-fetched.
- A locked OD-0051 brand is never derived.
- A shown palette changes only through: a pack adoption; an `EngineVersion` bump in a build the owner approved; a local
  seed's re-verified replacement (two agreeing observations in two day windows); a key change (to the current key's
  seed or the neutral); a lineup boundary publication (shades only). Each glides.

### 5.6 The colour data pack (F8)

- **Content:** everything that defines a colour but is not code: the fixed palette, makers table and aliases, ladders
  and family words, effort ladder words **and synonyms**, **harness rewrites**, pinned toc, PSL section, placeholder
  SHAs, owner seeds, baked seeds **with their evidence resamples**, append-only bake epochs, **the frozen post-pack
  reserved list of its `DeriveVersion`**, the market snapshot with its boundary **and tombstones** (rows withdrawn since
  an earlier pack, with their last facts, kept for 24 months after withdrawal), **the precomputed pack-only snapshot**
  (palettes, lineups, shade tables) for `Prime()`, `PackVersion` and the minimum `EngineVersion`.
- **Delivery:** embedded in every build (`Resources/colour/pack.v<N>.json`). ENGINEERING DECISION, delivery channel
  OWNER (O7): later also through the app's own update feed (OD-0062, Velopack), adoptable without a binary update.
  Nothing is published without the owner's word (OD-0058).
- **Rules:** a build runs the newest pack it has verified (embedded or delivered, signature-checked), and packs only
  move forward. Engine-code changes that alter outputs ship with a pack that requires them. The OD-0065 evidence digest
  records `PackVersion`.
- **Until O7 is answered**, every build carries its pack, so two users on the same build agree. Users on different
  builds of the same `DeriveVersion` differ only in what the newer pack adds or changes: its new baked makers (a
  collision the bake resolved), its new tombstones and its newer market snapshot, until the older user's next boundary
  snapshot supersedes that snapshot. Post-pack makers in neither pack agree across those builds (R2-5). A `DeriveVersion`
  bump changes palettes for everyone on the new build; OD-0065 across builds therefore rests on O7 or on prompt updates
  (section 12).

---

## 6. Caching and persistence

### 6.1 What is stored

| What | Where | Lifetime |
|---|---|---|
| The colour data pack | embedded `Resources/colour/pack.v<N>.json` (later also delivered, O7) | per pack |
| Local seeds: seed (integers), source, per-source status, SHA-256 and 64 x 64 resample, input-set digest, `SeedVersion`, `PackVersion`, the last re-verification observations with their **day-window indices** | `colour/seeds.json` | until a pack seed supersedes it or `Reset` |
| Undecided makers: next attempt as a **window index plus an in-process monotonic deadline** (scheduling only), last reason per source | `colour/seeds.json` | until decisive |
| Market catalogue: **the current boundary snapshot** (the full trimmed list, its boundary index, `Date`, `Age`, generation time) and, separately, the latest prefetch | `colour/market.json` (previous accepted file kept as `market.prev.json`) | replaced when the next boundary snapshot is published |
| Last published snapshot (digests of pack, engine and store, **and a hash of its own content**) | `colour/snapshot.json` | rewritten per settled snapshot |
| OR `/providers` | memory | 24 h |
| Palettes, ranks, levels, clouds; the frozen shade tables and gradient rows | memory (`ColourSnapshot`) | until the next snapshot |

- **Back-off for an Undecided maker:** 30 s, 5 min, 1 h, 6 h, then once per day window. Within a process the deadline is
  monotonic; across restarts only the window index survives. Brought forward, no more often than every 30 s, on
  network-available, a Refresh, or a new model of that maker. **Attempts triggered by a Refresh never advance the
  ladder** (R2-A11). Timing only.
- **Writes** are debounced to one atomic write per settled batch.
- **Logos** are never stored; only their SHA-256 and 64 x 64 resample.

### 6.2 The market catalogue: one boundary snapshot per window (F6, R7; AC-3: R2-1, R2-6, R2-A1, R2-A2, R2-A8)

AC-2's rules (per-row union with the pack, "apply only if the maximum `created` is not lower", removal after two
accepted fetches, facts from the last accepted fetch) made the lineup depend on each machine's fetch history: a pulled
newest row froze every user who had seen it (6 rejected responses each in the round-2 simulation), removals counted
launches, resumes and Refresh presses, and an upgrade unioned removed rows back in. AC-3 replaces them:

- **Windows.** Boundaries b_k every 6 hours UTC. Server time = the last response's `Date` plus monotonic time since it
  (never the local wall clock). A response's **generation time** is `g = Date - Age` (`Age` absent counts as 0), so a
  CDN-cached body is dated by when it was generated, not when it was served.
- **The boundary snapshot S_k** is the **first** accepted `/api/v1/models` response whose generation time falls in the
  window `[b_k, b_k + 15 min]`. The automatic poll runs 1 to 10 minutes after b_k and retries inside the window (a body
  generated before b_k is refused for the window and retried after the CDN max-age of 120 s). Later responses in the
  same 6-hour period never change S_k: they are **prefetches**, used only to place newly listed models by id and to
  start their makers' lookups. A seed chain's inputs (`hugging_face_id`, names) come from the boundary lineup only
  (section 4.2), never from a prefetch, so every user in a window looks a maker up with the same inputs.
- **Lineup at window k = a pure function of (pack, S_k).** Membership: the pack's rows and tombstones, plus every
  eligible row of S_k with `created < B_k` (B_k = b_k - 30 min) and not expired at B_k. Facts (price, context,
  expiry, names, `hugging_face_id`): S_k's for rows in S_k, the pack's for the rest. **Catalogues are full snapshots,
  never unioned per row:** a post-pack row absent from S_k is gone at window k for everyone who holds S_k; a pack row
  absent from S_k stays as a tombstone with the pack's facts. No fetch count, no hysteresis, no "maximum created"
  test.
- **One publication instant.** The lineup of window k is published at **b_k + 15 min in server time** on every
  machine that holds S_k, so the 1 to 10 minute poll jitter never shows. A machine that gets its first response of
  the period after the window (offline at the boundary, launched later) adopts that response as S_k and publishes at
  once.
- **Pack versus market.json.** On launch and after an upgrade, the lineup uses whichever of {the pack's snapshot,
  `market.json`'s S_k} has the **newer boundary**; the older one is ignored, never merged. An upgraded install
  therefore equals a fresh install of the same build once both hold the same window's snapshot (qwen-plus removed
  between the old `market.json` and the new pack stays removed, R2-6).
- **Bad rows (R2-A1).** A row whose `created` is later than `g + 1 day` (a millisecond value) is ineligible in that
  snapshot. A withdrawn newest row simply leaves at the next boundary. Neither can freeze a machine.
- **Single-flight.** One fetch at a time; joiners share it. **No cache:** the engine's `URLSession` has no `URLCache`;
  a forced fetch also sends `Cache-Control: no-cache`.
- **Residuals (stated, not hidden):** (1) two machines whose first in-window responses straddle a catalogue change
  inside the same 15-minute window hold different S_k for that window (OpenRouter changes rarely land in that quarter
  hour; the next window re-converges); (2) a machine that missed the window adopts its first later response, which can
  differ from S_k until the next boundary; (3) a backdated row (`created` more than 30 minutes before it appears) can
  enter at different windows for (2)-type machines; (4) a post-pack row withdrawn before the next pack leaves the
  lineup at a boundary (for everyone at once) and its id falls back to id-only placement; the next pack keeps it as a
  tombstone only if it was in an earlier pack.
- **Owner trade-off (O8, R2-A8).** A new stronger model re-spaces its family at the next boundary + 15 min, up to about
  6.5 h after it is listed (the summary says when). Immediate re-spacing on Refresh would make the lineup depend on who
  pressed Refresh; a shorter window (1 h: about 18 MB a day of catalogue traffic instead of about 3 MB) is the other
  lever. Default: 6 h until the owner answers.

### 6.3 Store robustness (R10, R2-A12)

- **Validation on load, for every file:** every value finite and inside its band and grid; sources known; evidence hash
  present; schema known. Invalid `seeds.json` entries are quarantined one by one (the rest kept) and the file is copied
  aside as `seeds.corrupt-<n>.json`. A `market.json` that fails validation (truncated, unparsable, a boundary index in
  the future) is quarantined as `market.corrupt-<n>.json` and **`market.prev.json` is used instead**, so a local
  failure never regresses the lineup to an older boundary. `snapshot.json` carries a hash of its own content; a
  mismatch means "recompute", never "publish as snapshot 0".
- **Newer schema** (a downgrade): read-only; never rewritten.
- **Locked file** (antivirus, OneDrive): retried with back-off, never treated as corrupt, never rebuilt.
- **Atomic writes:** a temp file, then `File.Move` for the first write and `File.Replace` afterwards (`File.Replace`
  throws when the destination does not exist); `File.Replace` keeps the previous `market.json` as `market.prev.json`.
- **Convergence after a rebuild:** pack seeds and the boundary lineup do not depend on the store, so a lost store
  re-converges on the same palettes (a quarantined local seed re-runs its chain).

---

## 7. Offline, failure and performance

### 7.1 Offline and failure

Every failure degrades to a calm neutral. Nothing throws to the UI. **No failure ever produces a decisive outcome.**

| Case | Shown | Engine |
|---|---|---|
| Offline at launch | Fixed, pack and stored makers in full (Known). A post-pack maker without a seed shows clear water, then its neutral at 3 s. | Market and seeds back off; the lineup stays at the last boundary snapshot (or the pack's, if newer). |
| Offline at a boundary | The previous window's lineup | The first response after coming online becomes the window's snapshot (section 6.2 residual 2). |
| A seed's key changes while its new chain is failing | The pending neutral (glide), as a fresh install shows | Undecided under the new key; the old seed is never shown (R2-4). |
| A source above the winner is Unavailable | Neutral | Undecided; retried. |
| Proxy block page (HTTP 200 HTML), captive portal | Neutral | Unavailable (content validation); retried. |
| HF 429 or shared-NAT limit, for hours | Neutral for longer | Unavailable; budget and `Retry-After` honoured; never converted. |
| Validated placeholder, tiny image, 404 from the origin, aggregator | (none) | Decisive Miss; the chain moves on. |
| All decisive, none chromatic | Neutral, final | Mono. |
| Market fetch fails for ever | The last boundary's lineup (pack snapshot at worst) | Retries. |
| OpenRouter pulls a row, or lists a row with a bad `created` | Unchanged until the next boundary, then the same for everyone | Full-snapshot replacement; `created` clamp; no response is ever rejected for its content (R2-1, R2-A1). |
| Corrupt, newer-schema or locked store | Pack and boundary colours; a corrupt `market.json` falls back to `market.prev.json`, never to an older boundary | Quarantine, read-only or retry (section 6.3). |
| Worker exception | Last good snapshot | Caught per job, logged, backed off. |
| Wall clock jumps (VM resume, manual change) | Unchanged | In-process scheduling is monotonic; windows and B come from the server `Date`. |
| Short process lifetimes, reboots | Unchanged | Window indices persist; ticks never do (R2-A3). |
| Unknown provider-local word | Provider's own palette | No lookup. |

### 7.2 Performance budget

| Where | Budget |
|---|---|
| UI thread `Resolve` | under 10 µs, no allocation once warm, no I/O, lock or await; **zero shade computations** (a read of the snapshot's frozen tables; tested by counting `Shade` calls on the dispatcher thread) |
| Trigger post | about 1 µs (a reference post; the list diff runs on the actor); 250 ms debounce |
| Snapshot change and progress | one coalesced dispatcher post, at most 10 per second; only cards whose `TrackLook` changed redraw |
| Launch | `Prime()`: the pack-only snapshot synchronously (about 5 ms, no file I/O), then `snapshot.json` (one file read and a hash check) within 50 ms; nothing snaps if it is late |
| Market parse | about 15 ms, streaming, on the pool; O(n) diff on the actor |
| Decode | at most 2 images of 256 px or less per maker, under 10 ms; resample and Dominant under 1 ms |
| Derivation | under 50 ms per maker, independent of other post-pack makers, chunked so the actor yields every few ms; the actor thread runs at `BelowNormal` priority so it never competes with the render thread on a 2-core machine |
| Lineup | under 1 ms over 464 rows; levels per palette cached |
| Shade tables | precomputed on the actor before publication, per (palette, level, effort word, theme): at most about 100 palettes x 9 levels x 8 words x 2 themes = 14,400 small entries (stops, title, glow, dials, two gradient rows), shared across snapshots for unchanged palettes; models map to (maker, level) |
| Network per new maker | 4 s per request, 8 s per maker, at most 4 requests in flight, at most 2 makers at once |
| Visible bound | 3 s, the card's neutral settle |
| Rendering | unchanged from OD-0052; a glide adds one blend of two cached rows, no per-frame allocation, 60 fps (native frame tests) |
| Refresh over a 5,000-row fixture | no UI-thread work above 1 ms per frame, measured against the **real `ProviderRegistry`** with stand-in CLIs (`ProviderCliGuard`) and a 5,000-row OpenRouter fixture |
| Refresh summary | counts and the top 5 per category; the details list is virtualised and refreshes only at stage ends |
| Traffic | one catalogue per boundary (763 KB), a Refresh fetch at most once per 120 s, about 3 to 5 small requests per new maker; one re-verification per post-pack maker per UTC day window |

---

## 8. Network inventory entries (create `porting/network_inventory.md`)

All requests are HTTPS GETs through the engine's own injected `URLSession` (so `URLSession.Transport` and the test
handler cover them), send the User-Agent `Marrow/<version>`, follow at most 3 redirects, and send no key, cookie or user
data. Every response is validated (section 4.2) before it is classified. The port's `URLSession` today reads
`Transport.Current` on every request and caps redirects at 20 with no final-host hook (`Http.cs:644-659`, `:715-724`);
U12 adds a **PORT-ONLY constructor** taking a handler, a redirect limit and a final-URL callback, which the engine uses
(R2-A9).

| # | Host | Request | When | Sends | Size / timeout |
|---|---|---|---|---|---|
| N1 | openrouter.ai | `/api/v1/models` (keyless) | after each 6 h UTC boundary (retried inside its 15-minute window); launch, resume, network-available when the window has no snapshot yet; Refresh at most once per 120 s | UA only | 763 KB / 10 s |
| N2 | openrouter.ai | `/api/v1/providers` | a new maker, at most daily | UA only | 26 KB / 4 s |
| N3 | huggingface.co | `/api/organizations/<org>/overview`, `/api/users/<name>/overview`, the exact-slug probe `/api/organizations/<slug>/overview`, `/<org>` (HTML, 1 MB read cap) | a new maker or a re-verification, at most 3 calls | HF org, user or maker slug | small / 4 s |
| N4 | cdn-avatars.huggingface.co | the avatar from `avatarUrl` | as N3 | none | 512 KB cap / 4 s |
| N5 | t0.gstatic.com | `/faviconV2?client=SOCIAL&type=FAVICON&fallback_opts=TYPE,SIZE,URL&url=https://<domain>&size=256` | only if higher sources gave no chromatic hit | maker domain | 512 KB cap / 4 s |
| N6 | (OWNER, O7) | the colour data pack through the app's update feed | with update checks | UA only | small |

Not contacted: registry.npmjs.org, unpkg, DuckDuckGo, maker sites, Wikidata, Commons, OpenRouter author pages and
`/images/icons`, icon.horse, Clearbit, logo.dev, Brandfetch, faviconkit, Google `s2`, HF `quicksearch`.

Third-party notices: the lobehub `toc.json` snapshot (MIT) and the PSL ICANN section (MPL-2.0).

---

## 9. What waits for the owner, and what can be built now

### 9.1 Built now (OD-0064: "the engine can be built before that")

Everything in units U00 to U21 plus U15b and U19b (section 11): the engine, the network chain, the store, the pack
format, the triggers, the refresh engine API, the determinism suite, the slider carrying a `BrandPalette`, the glide and
the locked v22 loading water and ink arrival.

- **The engine gate.** Once wired, the engine runs and learns seeds in Debug and dev builds. Nothing is released
  without the owner's word (OD-0058).
- **The UI gate (`ModelShades.ApplyToUi`, a build constant, off).** Until it opens, every card draws exactly what it
  draws today. Every existing look test still holds.

### 9.2 Waits for the canvases (OD-0063, OD-0061, OD-0067)

| # | Item | What approval unlocks |
|---|---|---|
| W1 | **The intensity rule.** IR-3 is the draft (`../intensity/intensity.py`). Measured limits to show on the canvas: (a) visible strength levels at max effort are 6 to 9 for chromatic palettes and **4 for Silver**, so OpenAI's lineup shows four levels (IR-3: Astra and Astra Pro; the Sols; Terra with the generic Pros; Luna and everything below) unless Silver's range is widened; (b) the global effort ladder has 6 steps, so adjacent efforts differ by about 0.009 OKLab (median palette minimum), under H3's 0.012, though motion also changes; (c) with more groups than levels, a weaker release can move older members up one level, and small lineups re-spread when a member joins (OD66-7 residual); (d) the anchor: centre (a one-model maker shows the lock at max) or ceiling; (e) IR-3's choices: Astra Pro and Astra share the top shade under Silver; the flagship-alone step is dropped for a modifier-only difference. | U22: `IR3Rule : IIntensityRule`; H2 and H3 live; `DeriveVersion` bump. Seeds stay valid. |
| W2 | **Applying maker palettes in the UI** (opening the UI gate). | U23. |
| W3 | **The NVIDIA darker green's exact stops** (IR-3 uses #54800C / #699510 / #87B516). | An owner seed in the pack and a reserved entry. |
| W4 | **The premium hue bands, the mono floor and H1** (section 5.2): 0.060 by default (capacity 97 hearts) against AC-1's 0.080 (51 hearts, under today's need of about 75); or a hue-sector rule; or a second visual dimension for crowded makers. | The constants, versioned D2, confirmed or retuned. |
| W5 | **Whether Plain (non-Max) levels show the liquid.** | How far U23 reaches. |
| W6 | **The OD-0067 Settings control** (proposed: Settings > Appearance, a "Colours" row with Refresh and "last checked"; progress and summary in the glass language). | U24. |

### 9.3 Other owner questions (the build does not wait for them)

| # | Question | Default until answered |
|---|---|---|
| O1 | Keep the always-on keyless OpenRouter market fetch (N1)? OD-0066 names the keyless public catalogue as the lineup source. | On, listed. |
| O2 | `ZaiAPI.Option` and `MetaAPI.Option` filter ids by prefix. Pass unknown ids through? | Unchanged. |
| O3 | An override file and a Settings "reset colour" control. | Not built; `Reset` is an engine API only. |
| O4 | Optional extra sources (DuckDuckGo, maker sites, Wikidata). | Off. |
| O5 | Router ids: clear water until the answering model is known, then its maker's ink, or a fixed neutral. | Engine returns `Router`. |
| O6 | Harness providers running another maker's model: the maker's palette or their own? | Their own. |
| O7 | Deliver the colour data pack through the update feed (N6), so packs move forward without a binary update? OD-0065 across builds rests on it (section 5.6). | Embedded per build only. |
| O8 | A stronger new model re-spaces its family at the next shared boundary (up to about 6.5 h after listing, the same instant for everyone), or sooner with a 1-hour window (about 18 MB a day instead of 3 MB)? Re-spacing at the moment of a Refresh would break "same lineup, same shades" (R2-A8). | 6-hour windows; the new model is placed by name at once and the summary names the re-spacing time. |

---

## 10. Test plan (no test touches the network)

### 10.1 Seams (R6)
- **Injected transport.** The engine receives its `URLSession` (or `HttpMessageHandler`) at construction and never reads
  the ambient seam per request, so work posted from an OS event thread (`SerialExecutor` captures the enqueuer's
  `ExecutionContext`, `Executors.cs:50-56`) cannot reach the process-default real handler (`Http.cs:654`).
- **Fail-closed by default (R2-A9).** A `ModuleInitializer` in `Marrow.Testing`, linked into every test assembly, sets
  `URLSession.Transport.SetProcessDefault(new FailClosedHandler())`. The handler **passes loopback addresses**
  (127.0.0.1, ::1, localhost) to the real handler and fails every other request, so the existing test files that
  use `URLSession.Shared` against loopback stand-in servers (47 by the round-2 grep; `HttpProcessStateTests`, `UpdateCheckerSecondTests` with
  `LoopbackFeedServer`, ...) keep passing without weakening the guard. A build check fails if a test project does not
  reference `Marrow.Testing`.
- **Engine transport.** The engine uses the PORT-ONLY `URLSession(handler, maxRedirects: 3, onFinalUrl)` constructor
  (section 8), so "at most 3 redirects" and the final-host check are real API, tested against the fixture handler.
- **Provider CLIs.** Tests that touch `ProviderRegistry` keep using the existing `ProviderCliGuard` fixtures
  (`tests/Marrow.Services.Tests/Fixtures/ProviderCliGuard.cs`, `tests/Marrow.App.Tests/Fixtures/ProviderCliGuard.cs`)
  and `AllowedForTesting` (`ProviderRegistry.cs:365`), which already fail real CLIs closed.
- **`INetworkStatus`** replaces the direct `NetworkChange.NetworkAvailabilityChanged` subscription; tests use
  `FakeNetworkStatus`.
- **App fixtures.** `AppModelFixtures.Bootstrap` (`:174-185`) gets a `FakeColourEngine` (or the engine with a disabled
  network), and a guard test asserts that `Bootstrap` under `FailClosedHandler` issues zero requests.
- `InProcessHttpHandler` maps a URL to scripted responses (status, headers including `Date` and `Content-Type`, body,
  delay); any unknown URL fails the test, which also enforces the network inventory.
- `SwiftClock.Provider.Override(ManualTimeProvider)` drives timeouts, the 8 s cap, the 3 s settle, back-off, the
  boundary polls, the publication instant and the debounce; scripted `Date` and `Age` headers drive server time, day
  windows and boundary windows; a separate settable wall clock proves wall time is never read. A "restart" helper
  disposes an engine and builds a new one on the same store with a **new monotonic origin**.
- `StoragePathsLocations.Create(tempRoot)` per instance; `ManualMainLoop` for the dispatcher and actor steps.
- `ILogoDecoder`: the real `SkiaLogoDecoder` in `Marrow.UI.Tests`; a by-SHA RGBA fake elsewhere.
- The engine takes an explicit `ColourPack`, so a test can ship a tiny one (and an "older" one for upgrade tests).
- Culture: a helper runs a test body under a given `CultureInfo.CurrentCulture` and `CurrentUICulture`.

### 10.2 Fixtures (U00 copies them into `tests/fixtures/colour/`)
- The OpenRouter `/models` payload, re-captured by one keyless GET at design time (`raw/models.json` no longer exists;
  `../intensity/openrouter-models.json` lacks `created`, `canonical_slug`, `hugging_face_id`, `alias_target`), committed
  with a trimmed copy and two later variants for the boundary tests (v1 adds a new maker and a stronger family model).
- `stability/prov.json`, `raw/toc.json`, `stability/s7_toc_versions.json` (toc 5.18 vs 5.19 for the upgrade tests).
- HF overview JSON and org-page HTML snippets; logos from `samples/`; the faviconV2 404 image; the HF placeholder.
- Failure fixtures: timeout; 404 with and without HF's JSON body; 403; 407; 429 with `RateLimit` and `Retry-After`;
  503; **HTTP 200 with an HTML block page** where JSON or an image is expected; a redirect to a foreign host; a
  truncated image; an undecodable ICO; a 1x1 image.
- Goldens from the oracle (U00) and from `../intensity/intensity.py` IR-3 (`intensity-table.json`, MD5 in section 14.3).

### 10.3 Test suites by component
1. **OKLab:** round trips; byte-identical gradient rows before and after the move.
2. **Makers:** a table over all 15 providers and the routing cases; under tr-TR, de-DE and ar-SA the same results.
3. **Dominant:** goldens for the 25 makers; seeds as integer indices; recomputation from a stored resample equals the
   live result.
4. **DomainVote:** the 25-maker matrix; denylist; single-vote rule; `edu.cn`; never `<slug>.com`.
5. **PaletteDeriver:** goldens; total-order tie-break; property test (bounds, gamut, contrast, H1, H2 with a stub
   cloud); a post-pack maker's palette is identical whatever other post-pack makers exist (all subsets of three).
6. **LineupRanker** (the IR-2 and IR-3 cases, mirrored from `intensity.py metrics.hardening`):
   - Astra > Sol > Terra > Luna; Opus 5.5 > Opus 5; before Astra a GPT-6 Sol member is the flagship;
   - permutation invariance over rows, including same-price dated snapshots (`grok-4.20-0309`, `gpt-5.2-2026-02-01`);
   - a speed variant with no base row never ranks first (`grok-4.8-fast`);
   - X-mini < X and X-nano < X for every family word;
   - `o1-pro`, `gpt-4`, `gpt-3.5-turbo` below every GPT-6 member; `gpt-6.2-mini` above `gpt-4-turbo`;
   - equal (class, version, day, price) share one level; `muse-1.4` tops Meta;
   - every adjacent level pair >= 0.02 at max effort, also with 120 extra synthetic members;
   - off-catalogue placement (`claude-fable-6`, withdrawn `claude-opus-4.1`);
   - alias resolution with modifiers and prices; the boundary B cut and expiry;
   - the same (canonical model, effort word) through every provider gives an identical shade;
   - agreement with the Artificial Analysis index of at least 85 % over same-maker pairs (regression floor);
   - **IR-3 (round 2), mirrored from `metrics.hardening.R2-*`:** a weaker model added 600 days after the newest member
     of **every** lineup never takes level 1 and the flagship keeps it, plus `claude-haiku-6`, `gemma-5-4b`,
     `gpt-6.5-luna-nano`, `muse-glimmer-2-8b` and `trinity-large-mini`; `mistral-large-2512` is Mistral's level 1 and
     a newer `mistral-large-2611` re-spaces the lineup above it; `-fast`, `-highspeed`, `-ultraspeed` with no base row
     (also on ids carrying pro, opus, astra or fable) rank below the old flagship, which keeps level 1; every catalogue
     member withdrawn in turn and kept as a tombstone keeps its (s, level) (314 of 314); id-only placement never sits
     above a member it is listed below (0 of 314 outside the stated legacy residual); 17 harness spellings give their
     member's exact s; the owner's example: Sol > Terra > Luna distinct before Astra and Astra > Sol > Terra > Luna
     after; a plain `gpt-7` is level 1 and `gpt-7-mini` is not lifted; every effort word any provider emits maps to a
     ladder word and every `ACPSession.EffortRanks` key has the same rank on the pack ladder.
7. **ColourPack:** the fixed palette equals the former `LiquidStopsHex`; baked palettes equal `Derive(baked seeds)`;
   pack seeds outrank local seeds; a newer pack is adopted, an older one ignored; a pack needing a newer engine is
   ignored.
8. **MarketCatalogue:** parses every field (invariant culture); keyless; single-flight; generation time `Date - Age`;
   the first in-window response is the boundary snapshot and later ones only prefetch; a body generated before the
   boundary is refused for the window; publication at boundary + 15 min in server time; full-snapshot replacement,
   never a per-row union; tombstones keep pack rows; `created` clamp. **Fixtures (R2-1, R2-6, R2-A1):** a pulled newest
   row (the real `gpt-6.1-sol-pro`, 4 s newer than `gpt-6.1-sol`, pulled after it joined); a mid-window removal
   (`qwen/qwen-plus` and its dated row) under four fetch schedules (install time, one Refresh, two poll jitters); a
   mid-window Refresh; a withdrawn newest row (`apodex-1.1-mini:free`); a row whose `created` is in milliseconds; an
   upgrade where a row was removed between the old `market.json` and the new pack. Each asserts identical lineups at
   every publication instant.
9. **SkiaLogoDecoder:** each format; largest ICO frame; caps; placeholders rejected before decoding; garbage gives a
   classified failure, never an exception.
10. **SeedChain**, one test per rule:
    - a chromatic toc wins with zero requests; a pack seed wins with zero requests;
    - the avatar is used when the toc is mono;
    - avatar 503 plus favicon Hit gives Undecided;
    - **HTTP 200 HTML on the avatar or overview gives Unavailable, never Miss**; a foreign final host gives Unavailable;
    - **HF 429 for 31 h of manual clock never becomes a Miss**; neither does 20 consecutive failures;
    - placeholders and a validated 404 are Misses;
    - inputs only from catalogue rows: the same maker met first through a CLI list and first through the catalogue
      ends with the same seed once the catalogue row exists; **while the new key's chain is failing (HF 429) the card
      shows the pending neutral, never the old key's seed** (R2-4);
    - all mono gives decisive mono; a request times out at 4 s; delaying a higher-priority response changes nothing;
    - the HF budget holds over a 40-maker cold run; URLs carry only the slug, org or domain.
11. **ColourStore** (R10, R2-A12): atomic writes (first `File.Move`, later `File.Replace`); a NaN seed, a truncated
    file, a schema+1 file and a locked file each leave the other seeds intact and give a deterministic digest; a
    truncated `market.json` falls back to `market.prev.json` (no backwards re-spacing); a parseable `snapshot.json`
    whose content hash does not match is recomputed, never published.
12. **ColourEngine:**
    - Pending then decisive publishes exactly 2 snapshots; a refresh's stage 1 publishes one;
    - offline: Pending with a neutral, nothing decisive persisted, later success recolours;
    - back-off on the monotonic clock within a process and by window index across restarts; a wall-clock jump of +3
      days changes nothing;
    - re-verification: one differing observation changes nothing; two agreeing ones in two distinct day windows replace
      the seed; a 10 h process lifetime with a new monotonic origin each day re-verifies in the same windows as a
      process that never restarts; a Refresh adds no second observation to a window;
    - a key change with a failing chain glides to the neutral; an alias merge drops both local seeds to Undecided;
    - post-pack palettes are identical under pack N and pack N+1 of the same `DeriveVersion` (one maker baked, one
      retired); a newly baked maker never re-colours an existing pack maker;
    - `CatalogChanged` (reference post) and network-available trigger work; debounce groups 40 rows into one re-spacing;
    - `Reset` re-fetches; a worker exception keeps the last snapshot;
    - the refresh tests of section 3A.4.
13. **OD-0065 determinism (U18):** section 10.4.
14. **UI contract:**
    - `Resolve` under 10 µs, no allocation after warm-up; no transport call on the dispatcher thread;
    - gate off: every existing `EffortBrand` and look assertion unchanged;
    - gate forced on: a late colour reaches the card through `UpdateLook` and starts the arrival;
    - **a relaunch with stored seeds shows no Pending frame, no arrival and no glide** (R4);
    - **launch after a `DeriveVersion` bump, and launch with `Prime()` step 2 delayed to 200 ms, show zero snapped
      frames**: every card's colour is frame-to-frame continuous (R2-A7, R2-7);
    - **zero `Shade` computations on the dispatcher thread** while a re-spacing snapshot lands with 100 rows on
      screen (R2-A6);
    - **a re-spacing interpolates the title, the glow and every s-driven dial with the stops' `t`** (continuity of each
      value per frame; R2-OD66-G); a retarget's first frame equals the previous frame's RGB blend (R2-A15);
    - **a re-spacing keeps the same `TrackEffect`** (no `startedAt` reset, bubble positions continuous); a retarget
      mid-glide starts from the current colour; a glide waits for a running arrival (R5);
    - native frame tests: 30 tubes gliding at 60 fps with zero allocations per frame; an arrival at 60 fps;
    - a refresh over a 5,000-row fixture: no UI-thread work above 1 ms per frame (R11).
15. **Guard:** `Bootstrap` under `FailClosedHandler` issues zero requests (R6).

### 10.4 The automated 10-instance determinism test (U18; OD-0065, OD-0066, OD-0067)

`tests/Marrow.Services.Tests/Colour/TenInstanceDeterminismTests.cs`. Written test-first against U17's public contract.
No network: every instance runs on its own `InProcessHttpHandler` script under `FailClosedHandler`.

**World.** One scripted public world, shared by all instances, on a timeline of 6-hour boundaries with scripted
`Date` and `Age` headers:
- pack N (with tombstones and its frozen reserved list) and a later pack N+1 of the same `DeriveVersion` that bakes
  `acme`, bakes one more existing post-pack maker, retires an inactive maker, adds a toc entry for a maker that instances
  first resolved by avatar, adds a tombstone, and merges two maker keys by alias;
- catalogue versions W0 and W1. **W1 adds a new maker `acme`** (chromatic HF avatar) **and stronger family members**:
  `gpt-6.2-astra-pro` (OpenAI re-spaces), `muse-spark-1.4` (Meta re-spaces), `mistral-large-2611` (a dated successor);
  also a same-price dated snapshot, a weaker model 600 days after a flagship, a `-highspeed` variant with no base;
- **mid-window events (R2-1, R2-A1, R2-A2):** the real pulled-newest-row case (`gpt-6.1-sol-pro` removed 2 h after the
  boundary it joined), `qwen/qwen-plus` and its dated row removed inside a window, a row listed with `created` in
  milliseconds, a newest row withdrawn an hour after listing, a 50 % price cut on `perplexity/sonar-pro`, and a
  CDN-cached body generated before a boundary served just after it;
- HF, avatar and faviconV2 responses, including an avatar re-uploaded on a given day.

**Ten instances**, each a separate `ColourEngine` with its own temp `StoragePaths` root, `ManualTimeProvider`, wall
clock, transport script, `FakeNetworkStatus`, provider set and culture:

| # | History |
|---|---|
| 1 | Reference: clean network, all providers, en-US, automatic polls only, never restarts. |
| 2 | Catalogue rows shuffled; market fetched after the provider lists; maker queue in reverse order; poll jitter at the other end of the 1-10 min range; tr-TR. |
| 3 | Provider subset: Claude CLI only, so it meets `claude-fable-6` and harness spellings (`claude-4.5-opus-high-thinking`, `opus-4.5`) before the catalogue lists them; effort words from an ACP agent (`extra high`, `ultra`); de-DE. |
| 4 | Provider subset: Command Code only, so it meets `acme`'s model first through a CLI list (slug-only chain), **and HF answers 429 for 31 h starting when `acme`'s catalogue row appears** (R2-4: the key changes while the new chain fails). |
| 5 | HF 503 on the first three attempts of every request; **frequent launches, resumes and Refresh presses inside every window** (R2-A2: fetch count must not matter); ar-SA. |
| 6 | A proxy answering **HTTP 200 HTML** for `cdn-avatars.huggingface.co` for 48 h of manual clock, then clean; **a process lifetime of 10 h a day, restarted with a new monotonic origin each day** (R2-A3). |
| 7 | **Installed after the pulled-newest-row and the qwen-plus removal** (a fresh install mid-world), then online; one Refresh mid-window (R2-1). |
| 8 | Offline for 48 h with 20 Refresh presses, then online; one double press 200 ms apart; a press after stage 1. |
| 9 | Store written under pack N-1 and `SeedVersion` S1 (a seed locked from the avatar where pack N's toc has an entry; an epoch-era layout; a `market.json` that still lists the later-removed row), then upgraded; restart mid-way (killed between the seed write and the snapshot); later holds both keys of the alias merge. |
| 10 | Truncated `seeds.json`, a corrupt `market.json` and a hash-mismatched `snapshot.json` at launch; wall clock jumped +3 days; responses delivered in reverse priority order with long delays; a stale CDN response delivered after a fresh one; **runs build N-1's pack until pack N+1 lands** (R2-5: post-pack makers in neither pack must already agree). |

**Checkpoints and assertions.**
1. **During failures:** at every step, every Ready palette in any instance equals the reference instance's palette for
   that maker **under the same seed key**, or the maker is Pending or Known with the neutral or an earlier agreed
   colour **of the same key**. No instance ever shows a different decisive colour, and none shows a seed computed for
   an older key (OD-0065: "never locks a fallback colour from a different source"; R2-4).
2. **At every boundary publication instant** (boundary + 15 min), every instance that was online in the window holds
   the same boundary snapshot and the same lineup digest, whatever its fetch count, jitter, Refresh presses or install
   time; instance 7 equals instance 1 from the first window it was online (R2-1, R2-A2). Between publications no
   instance's lineup moves.
3. **Settled at W0** (manual clocks advanced past the boundary plus two day windows, so every retry and
   re-verification has run): all 10 `ColourSnapshot` digests byte-identical; `Shade` identical for every (model,
   effort word, theme) in the union of all instances' lists, **every provider spelling and every effort synonym
   included**; the digest records `PackVersion` and `EngineVersion`.
4. **Settled at W1** (stronger models joined): again 10 identical digests; the digest differs from W0's; the expected
   re-spacing holds (`gpt-6.2-astra-pro` level 1, `gpt-6-astra-pro` below it; `muse-spark-1.4` level 1,
   `muse-spark-1.3` level 2; `mistral-large-2611` level 1; the weaker newcomer and the `-highspeed` variant never level
   1); the re-spacing reached each instance as one snapshot at the same publication instant, and each open track glided.
5. **Refresh equals automatic:** for instances 1, 5 and 8, the state reached through Refresh presses equals the state
   reached by waiting for the ticks, at every boundary.
6. **Re-verification:** after the avatar re-upload, instances 1, 5 and 6 replace the seed in the same day window;
   instance 7 (fresh) shows the new evidence at once; nobody shows a third colour.
7. **Pack upgrade:** after pack N+1 is adopted, 10 identical digests, equal to a fresh install of N+1 (an 11th
   instance created at that point), including instance 9's upgraded store (the removed row stays removed) and instance
   10's switch from pack N-1; every post-pack maker in neither pack has the same palette before and after.
8. **Unchanged makers:** the palette of every maker not touched by W1 or the pack step is identical before and after.

The intensity part of this test already runs in `../intensity/intensity.py` (`metrics.hardening.OD-0065_ten_histories`:
10 shuffled and batch-merged histories give one digest before and one after `gpt-6.2-astra-pro` and `muse-spark-1.4`
join; `metrics.hardening.R2-*`: the IR-3 cases; all pass, MD5 in section 14.3).

### 10.5 The one-off real-network evidence run (U21; OD-0065)

Evidence, not CI. Through the bake tool's entry point (C22), on the owner's machine, recorded in
`porting/evidence/OD-0065-colour-<date>.md`:
1. **Ten data folders**, each a separate `MARROW_DEV_DATA_ROOT` (as OD-0055), all on one machine behind one NAT, so the
   shared HF budget (10 x 60 requests per 300 s against HF's published 500 per IP) is exercised for real.
2. **One new model**: the newest catalogue model at run time. For a maker lookup, the run uses a test pack with that
   maker's seed and toc entry removed, so all ten folders look it up live.
3. **Different histories:** folders start in a shuffled order over about 30 minutes, some before and some after a
   6-hour boundary; folders 1-3 run with a provider subset; folder 4 starts offline (network off for the process
   through the dev-only fault-injection handler, never through system settings) and comes online later; folder 5 has
   `cdn-avatars.huggingface.co` failing through the same dev-only handler for its first hour; folder 6 is pre-seeded
   from an older pack; folder 7 presses Refresh every few minutes; folder 8 is quit and relaunched every 20 minutes;
   folder 9 runs the previous build's pack; folder 10 is the reference.
4. **Same boundary:** all ten are online across one boundary publication (boundary + 15 min) and settle within that
   window.
5. **Record:** per folder the snapshot digest, `PackVersion`, `EngineVersion`, `DeriveVersion`, boundary index and the
   boundary snapshot's generation time, the chosen model's shade at every effort word in both themes, the maker's seed,
   its key and evidence SHAs, and a hosts-only request log; the run passes when all ten digests are identical (folder
   9's pack-only differences listed separately and explained). If a real stronger model joins a family during the
   evidence window, a second record after the next boundary publication shows ten identical re-spaced digests;
   otherwise that half is covered by the pack N to N+1 step.

---

## 11. Build sequence: small test-first units for parallel agents

Each unit follows `porting/IMPLEMENTING.md`: tests first, failing for the right reason; adversarial review of the
tests; the code; the full suite passes. Each unit has its own worktree and branch `unit/colour-<id>`.

**Wave 0: preparation**

| Unit | Scope | Depends on | Done when |
|---|---|---|---|
| U00 Oracle and fixtures | Oracle `derive_ac2.py` (area-average 64 x 64, integer grid snap, AC-2 bands and floors, H1 0.060, explicit reserved set, total-order tie-break, H2 with a pluggable cloud); goldens; re-capture `models.json` and its W0/W1 variants; failure fixtures (section 10.2); copy `../intensity/intensity.py` IR-3 goldens. | none | Goldens committed; oracle re-runs byte-identical. |
| U00b Fail-closed test infrastructure | C26: `FailClosedHandler` (loopback allowed), the `ModuleInitializer`, the build check that every test project references `Marrow.Testing`, `FakeNetworkStatus`, the culture helper, the restart helper (new monotonic origin); the bootstrap guard test. | none | Guard passes; every existing suite, the loopback-server tests included, still passes unchanged. |
| U01 Network inventory doc | `porting/network_inventory.md`, N1 to N6, notices. | none | Reviewed against section 8. |

**Wave 1: pure Core (all in parallel)**

| Unit | Scope | Depends on | Done when |
|---|---|---|---|
| U02 OKLab move and colour types | C1, C2 (integer seeds, `Evidence`, `ShadeState.Known`). | none | Round trips; gradient-row parity. |
| U03 Makers | C3, invariant culture. | U02 | 10.3.2 under four cultures. |
| U04 DomainVote | C5. | none | 10.3.4. |
| U05 LineupRanker | C8, IR-3 precedence (class-relative legacy cap, next generation, dated successors, baseless speed variants), groups, level allocation order, tombstones, harness rewrites, id-only placement, aliases, boundary cut. | U03, U06 | 10.3.6, including the IR-3 cases. |
| U06 IntensityRule interface and IdentityRule | C6 with `Levels`. | U02 | Identity: one level, the anchor. |
| U07 Loading water and ink arrival | C21. | none | Look tests; native frame tests. |

**Wave 2: data and impure leaves (in parallel)**

| Unit | Scope | Depends on | Done when |
|---|---|---|---|
| U08 DominantColour | C4 (resample kept as evidence). | U00, U02 | 10.3.3. |
| U09 PaletteDeriver | C7, the frozen reserved set. | U00, U02, U06 | 10.3.5. |
| U10 ColourPack | C9: loader, version rules, the fixed palette moved from `LiquidStopsHex`. Baked seeds empty until U20. | U02 | 10.3.7; `EffortBrand` tests unchanged. |
| U11 Logo decoder | C10, C11. | U02 | 10.3.9. |
| U12 MarketCatalogue | C12: boundary snapshot per window, generation time, publication instant, full-snapshot replacement, tombstones, `created` clamp; the PORT-ONLY `URLSession(handler, maxRedirects, onFinalUrl)` constructor. | U02 | 10.3.8 with the round-2 fixtures. |
| U13 ColourStore | C14. | U02 | 10.3.11. |
| U14 Catalogue hooks | C17 (reference post, `EnumerateCatalogs`). | none | Diff tests on the actor; existing `ProviderRegistry` tests unchanged. |
| U15 Slider carries BrandPalette | C20 with the gate off. | U02, U10 | About 75 `EffortBrand` assertions and look goldens unchanged. |
| U15b Re-spacing glide | C25: same `TrackEffect`, two cached rows, uniform `t`, retarget and arrival rules. | U15, U07 | 10.3.14 glide and frame tests. |

**Wave 3: the engine**

| Unit | Scope | Depends on | Done when |
|---|---|---|---|
| U16 SeedChain | C13: inputs, validation, outcomes, priority, budget, timeouts. | U04, U08, U10, U11, U12 | 10.3.10, with an unknown URL failing the test. |
| U17 ColourEngine and snapshot | C15, C16: triggers, debounce, seed authority (current key only), re-verification on day windows, back-off (monotonic in process, window indices across restarts), boundary lineup and publication instant, two-step `Prime`, precomputed shade tables and rows, one settled snapshot per batch, `BelowNormal` actor thread, `Reset`. | U03, U05, U06, U09, U10, U12, U13, U16 | 10.3.12. |
| U18 Determinism suite | Section 10.4, test-first against U17's contract. | contract of U17 | 10 instances identical at every boundary publication, at W0, at W1 and after the pack upgrade. |
| U19b Refresh engine API | C24: `RefreshNow`, phases (no provider reload), prefetch versus window snapshot, staged publication, cooldown, progress, summary (section 3A). Test-first. | U17, U14 | Section 3A.4. |

**Wave 4: wiring**

| Unit | Scope | Depends on | Done when |
|---|---|---|---|
| U19 ModelShades and App wiring | C18, C19: construction in the `AppModel` constructor, `Prime` before `ShowMain`, triggers after bootstrap, `INetworkStatus`, shutdown; Known state; coalesced posts. | U07, U14, U15, U15b, U17 | 10.3.14 and 10.3.15. |

**Wave 5: pack and evidence**

| Unit | Scope | Depends on | Done when |
|---|---|---|---|
| U20 Bake tool | C22: writes `pack.v<N+1>.json` (seeds append-only, with evidence; append-only bake epochs; the frozen reserved list of the `DeriveVersion`; market snapshot, boundary and tombstones; synonyms and harness rewrites; the precomputed pack-only snapshot; toc pin). | U17, U11 | Unit tests against the fixture handler (a newly baked maker never re-colours an existing one); the first real bake by hand at release preparation. |
| U21 OD-0065 evidence run | Section 10.5. | U20 | Ten identical digests recorded in `porting/evidence/`. |

**After the owner's approval** (section 9)

| Unit | Scope | Unlocked by |
|---|---|---|
| U22 Approved intensity rule | `IR3Rule` (or the chosen rule), H2 and H3 live, `DeriveVersion` bump, goldens from `intensity.py`. | W1 |
| U23 Open the UI gate | Maker palettes in the UI; NVIDIA owner seed; plain-level reach; router look; look goldens re-baselined. | W1, W2, W3, W5 |
| U24 Refresh control UI | The OD-0067 control, progress and summary, behind the UI gate. | W6 |

Critical path: U02 -> U09 -> U17 -> U19, with U16 feeding U17 and U18 running alongside U17.

---

## 12. Risks (carried, with mitigations; residuals after AC-3)

- **Evidence drift.** A maker's avatar or favicon changes between two users' lookups. AC-3: local seeds are re-verified
  once per UTC day window with two observations from distinct windows, so long-running users replace a seed within two
  day windows (at most 48 h online) of the change, whatever their process lifetime; a fresh install shows the new
  evidence at once; the next pack freezes it. Residuals: for up to 48 h a fresh and a long-running user can show the
  old and the new evidence; a source that flaps between two images can keep users apart until the next pack.
- **Build and pack skew.** Two builds of the same `DeriveVersion` differ only in what the newer pack adds or changes
  (new baked makers, tombstones, its market snapshot until the next boundary snapshot); post-pack makers in neither pack
  agree. A `DeriveVersion` bump differs for everyone until they update. OD-0065 across builds rests on O7 or prompt
  updates. Within one pack, no skew.
- **Lineup timing.** All online users agree at each boundary publication (boundary + 15 min), whatever their fetch
  history. Residuals (section 6.2): a catalogue change inside a 15-minute window can split that window's snapshot; a
  machine that missed the window adopts its first later response until the next boundary; a backdated row can enter
  at a different window for such a machine; an offline user shows the last boundary it held; a new stronger model
  re-spaces up to about 6.5 h after listing (O8).
- **Id-only placement.** A model never seen in any catalogue snapshot or pack tombstone, with no (class, version) peer,
  is treated as current; for an old model that is wrong (9 of 314 legacy members measured would sit above a stronger
  neighbour). Pack tombstones close it for every model the catalogue ever listed since the first pack.
- **Re-spacing granularity.** Small lineups re-spread when any member joins (G <= V), and under Silver's four levels
  Astra Pro and Astra share the top shade (W1).
- **Post-pack collisions.** Two post-pack makers can sit closer than H1 until the next pack (reported diagnostic).
- **Crowding.** At H1 0.060 the band grid holds about 97 hearts against about 75 needed today (W4).
- **Intensity resolution (W1).** Silver has 4 visible levels; under IR-3 OpenAI shows Astra | Sol | Terra | Luna and
  below. The global effort ladder's steps are under H3's 0.012. Both are canvas decisions, not engine defects.
- **Undocumented sources.** faviconV2 and the HF org-page link could change; the chain degrades to the toc, the avatar or
  mono, with no hard failure; a changed answer goes through re-verification.
- **Float stability across x64 and ARM64.** Integer storage and quantisation; CI goldens on both architectures before
  release. A wide-gamut screen without colour management shows the same hex more saturated (not a determinism issue).
- **First launch without a baked pack.** About 56 makers at 3 HF calls each is about 15 minutes under the HF budget;
  visible makers go first, and the U20 bake before any release removes this.
- **Native decoding of untrusted bytes.** Magic-byte checks, caps before decoding, no SVG, a pinned Skia.

---

## 13. Hardening (round 1) changelog: AC-1 -> AC-2, IR-1 -> IR-2

### 13.1 Every finding and its resolution

| Id | Severity | Resolution | Where |
|---|---|---|---|
| F1-first-lock-wins-forever | blocker | Fixed. Authority reversed: owner and baked pack seeds always outrank local seeds (glide). Local seeds keyed by (`SeedVersion`, toc digest, input set) and re-run on a mismatch; 64 x 64 resamples stored so a Dominant change re-derives from the same evidence; daily re-verification with two-observation hysteresis converges evidence drift; the pack freezes it. Upgrade-vs-fresh-install scenario in the determinism test. | 1.1, 5.5, 5.6, 6.1, 10.4 (#9, checkpoint 5) |
| F2-per-network-decisive-miss | blocker | Fixed. Response validation: a 200 with the wrong content type or magic bytes, a foreign final host, undecodable bytes are Unavailable. The 5-over-24-h conversion is removed; no failure count ever becomes decisive. Monotonic clock for scheduling. One-NAT evidence run; proxy-HTML and 429-for-31-h fixtures. | 1.2, 4.2, 7.1, 10.2, 10.3.10, 10.4 (#6, #7), 10.5 |
| F3-seed-depends-on-discovery-hints | major | Fixed. Inputs only from global facts: catalogue rows at B (`hugging_face_id` by a fixed rule), the exact-slug HF probe (replacing fuzzy `quicksearch`), `/providers`. CLI-only makers run a slug-only chain that is the same for everyone and re-run when a catalogue row appears. A missing input is Unavailable. | 4.2, 10.4 (#4) |
| F4-epoch-and-predecessor-state | major | Fixed. Post-pack makers derive only against the pack's frozen reserved set; local epochs and the predecessor cascade are removed; the bake resolves post-pack collisions. | 1.3(c), 5.3 |
| F5-effort-from-local-list | major | Fixed in IR-2: one global effort word ladder; the list only picks stops. Test: same (model, word) through every provider gives the same shade (`F5_same_word_same_shade`, pass; DeepSeek vs Command Code dE 0.045 -> 0). | 4.4, `intensity.py` |
| F6-catalogue-age-window | major | Fixed. Union with the pack snapshot (newer `fetchedAt` per row); lineup cut at the shared 6 h boundary from the server `Date`; polls after each boundary. Residual (offline users, backdated rows) stated. | 6.2, 12 |
| F7-culture-sensitive-parsing | major | Fixed. Ordinal and invariant operations, invariant default culture on the actor, CA1304/CA1305/CA1307/CA1309/CA1310 as errors in Core and Services, determinism under tr-TR, de-DE and ar-SA. (`intensity.py` is culture-free; `h8_locale_price.py` now shows the parse path as the only exposure.) | 4.1, 5.5, 10.3.2, 10.4 |
| F8-build-skew-same-moment | major | Fixed by design, delivery OWNER: colour-defining data moved into a versioned, forward-only data pack outranking local state; engine changes ship with a pack; `PackVersion` in the digest. Delivery without a binary update waits for O7; until then same-build users agree and cross-build skew is stated. | 5.6, 9.3 O7, 12 |
| OD66-1 | blocker | Fixed in IR-2: a speed variant collapses only onto an existing base; otherwise its speed word ranks it (`grok-4.8-fast` level 5 of 6, `grok-4.7` stays first). | 4.4, `intensity.py` |
| OD66-2 | major | Fixed in IR-2: down-words apply inside a family (`astra-mini`, `sol-mini`, `luna-nano` all below their base in order). | 4.4 |
| OD66-3 | major | Fixed in IR-2: the family ladder on the generic scale plus a lineup-relative legacy cap (548 days). Before Astra the flagship is GPT-6.1 Sol Pro; `o1-pro`, `gpt-4`, `gpt-3.5-turbo` sit below every GPT-6 member; `gpt-6.2-mini` above `gpt-4-turbo`. | 4.4 |
| OD66-4 | major | Fixed in IR-2: per-palette visible levels (perceptual spacing, >= 0.02 at max effort), steps to the flagship, class boundaries and class heads first. Gate: 86 adjacent level pairs, minimum 0.0205; 120 extra members: 7 levels, minimum 0.0239. Silver's 4 levels go to the canvas (W1). | 4.4, 5.2 H3, 9.2 W1 |
| OD66-5 | major | Fixed in IR-2: order-free collapse (base row, earliest created, price, context, id). 30 shuffles plus reversal with same-price dated snapshots: 0 differences. | 4.4 |
| OD66-6 | major | Fixed in IR-2: virtual placement by the same precedence (`claude-fable-6` takes the top level; withdrawn `claude-opus-4.1` keeps its level). | 4.4 |
| R1-no-OD0067-design | blocker | Fixed: section 3A, C24, U19b (engine, now) and U24 (UI, after W6); single-flight, phases, throttled progress, summary as a snapshot diff, never touches a pack seed, tests. | 3A, 11 |
| R2-transient-to-miss-locks-user-specific | blocker | Fixed with F2: no conversion of any kind; content validation; refresh brings work forward without changing outcomes; convergence from the pack. Offline-48-h-with-20-presses instance in the determinism test. | 4.2, 3A.1, 10.4 (#8) |
| R3-engine-version-migration-diverges | major | Fixed with F1: `SeedVersion` / `DeriveVersion` split, stored resamples, pack seeds override older local seeds, key remap on alias renames, upgrade scenario tested against a fresh install. | 5.5, 10.4 checkpoint 5 |
| R4-startup-delay-replays-arrival | major | Fixed: engine constructed in the `AppModel` constructor, `Prime()` (warm snapshot) before `ShowMain`, `ShadeState.Known`, network triggers after bootstrap, a refresh before start joins the first run. | 3 step 1, 4.5, 10.3.14 |
| R5-glide-unowned-conflicts-with-crossfade | major | Fixed: C25 and U15b own the glide inside the same `TrackEffect`, cached rows and a uniform `t`; retarget, arrival and batching rules; frame and continuity tests. | 4.6, 10.3.14 |
| R6-tests-reach-network | major | Fixed: injected transport, fail-closed `ModuleInitializer`, `INetworkStatus`, fake engine in App fixtures, guard test (U00b). | 10.1 |
| R7-concurrent-and-stale-catalogue | major | Fixed: single-flight with a generation number, staleness check, removal hysteresis, no-cache forced fetch on a cache-less session, awaiting in-flight provider loads, per-source status. | 3A.2, 6.2, 10.3.8 |
| R8-huge-catalogue-crowding-and-cost | major | Fixed in part, rest OWNER: no O(M^2) cascade (independent post-pack derivation); active-maker reserved set; H1 default 0.060 with the measured capacity table for W4; the refresh's UI step ends after visible makers and lists crowded and still-pending makers. | 3A.2, 5.2, 5.3, 9.2 W4 |
| R9-provisional-palettes-are-user-specific | major | Fixed with F4: no palette depends on another post-pack maker's state. | 5.3 |
| F9-collapse-first-seen | minor | Fixed with OD66-5. | 4.4 |
| F10-float-gamut-display | minor | Fixed: integer storage and digests, CI on x64 and ARM64, hex and digest comparison. Not done: tagging Skia surfaces as sRGB (a display property, not a determinism issue; optional polish). | 5.5, 12 |
| OD66-7 | minor | Partly fixed: with more groups than levels the scale is top-anchored and the flagship and the upper class heads never move for a weaker release. Residual (measured: `claude-haiku-5` moves older Opus versions and the Sonnets up one level, because a new class head takes a step) and the re-spread of small lineups go to the canvas (W1(c)). | 4.4, 9.2 |
| OD66-8 | minor | Fixed: `alias_target` or `canonical_slug` first; IR-2 heuristic skips unnamed modifier siblings and prefers an exact price. Residual in the snapshot (no `alias_target` in it): 4 aliases priced differently from every candidate, resolved to the closest-worded member. | 4.4 |
| OD66-9 | minor | Fixed in IR-2: equal (class, version, day, price) is one group, one level. | 4.4 |
| OD66-10 | minor | Fixed in IR-2: `muse` is a Meta class word. | 4.4 |
| OD66-11 | minor | Fixed: the glide interpolates final stops (section 4.6), and IR-2 makes the never-mud lift (bisection), the never-neon clamp and the dark-keep chroma floor (0.01 L fades) continuous. The round-1 probe (baidu e=0, s 0.7415, ds 0.0005) drops from 0.0097 to 0.0002; worst over all palettes for ds 0.002 is 0.0032 (Grok's near-black 8-bit step excluded). | 4.6, `intensity.py` |
| R10-corrupt-store-handling | minor | Fixed: validation, per-entry quarantine, read-only newer schema, retry on a locked file, `File.Move` then `File.Replace`, convergence through the pack. | 6.3, 10.3.11 |
| R11-ui-thread-work-on-triggers | minor | Fixed: reference posts with the diff on the actor, throttled and merged progress, lazy structurally shared shade memo, 5,000-row refresh frame test. | C17, C18, 7.2, 10.3.14 |

### 13.2 Not fixed, and why

- **Pack delivery without a binary update (F8, part):** it needs a publishing channel, which OD-0058 and OD-0062 keep
  local until the owner says otherwise (O7). The pack format, the authority rules and the version gating are built now.
- **Skia sRGB surface tagging (F10, part):** the same hex reaches every screen; an unmanaged wide-gamut panel is a
  display property, not an engine divergence.
- **OD66-7 residual and Silver's four levels:** no rule can add visible levels a palette's range does not have, and
  keeping class heads distinct is worth more than keeping older versions still; both are put to the owner (W1).
- **The global effort ladder's step size:** a consequence of F5's fix (same word, same shade everywhere); widening the
  effort range is a canvas decision (W1(b)).

### 13.3 IR-2 evidence (superseded by IR-3, section 14.3)

`../intensity/intensity.py` (rule `IR-2`) regenerates `../intensity/intensity-table.json` deterministically: two
consecutive runs give MD5 `8316f4c5a3def2451fe4dca381fdd033` both times (Python 3, Windows). `metrics.hardening`
re-runs the round-1 cases (F5, OD66-1 to OD66-11, F9, the ten-history lineup test before and after stronger models
join); `all_pass` is true. Re-running the round-1 probes against IR-2: `h1_effort_lists.py` 0.0000 in all five cases;
`h2_lineup_inputs.py` row-order shuffles 0, older build 0; `h3_stale_shades.py` collapse keys identical under reversal;
`h8_locale_price.py` 0 of 301. The stale-catalogue difference that `h2`/`h3` still show is closed in the engine by the
boundary rule (6.2), not by the rule's maths.

---

## 14. Hardening (round 2) changelog: AC-2 -> AC-3, IR-2 -> IR-3

Round 2 attacked AC-2 and IR-2 with 32 findings (probes in `hardening/r2/`). Every blocker and major is fixed; every
minor is fixed except where 14.2 says why not. Round-1 resolutions that AC-3 replaces: F6 and R7's union, staleness
check and removal hysteresis (now section 6.2's boundary snapshots); R4's "Known: no glide" (now section 3 step 1);
R11's lazy shade memo (now precomputed tables); OD66-6's virtual placement facts (now id-only plus tombstones).

### 14.1 Every finding and its resolution

| Id | Severity | Resolution | Where |
|---|---|---|---|
| R2-1-lineup-depends-on-fetch-history | blocker | Fixed. The lineup (membership and facts) is a pure function of the pack and **one boundary snapshot per 6-hour window**: the first response generated (`Date - Age`) inside `[b_k, b_k + 15 min]`. Later responses only prefetch. The "maximum `created` is not lower" test is gone; removal is full-snapshot replacement (pack rows stay as tombstones), so no fetch count matters; facts come from the boundary snapshot; publication at b_k + 15 min in server time hides the poll jitter. The adversary's "absent from two consecutive boundary snapshots" removal rule was not taken: it depends on whether a machine observed window k-1. Residuals stated (a change inside the 15-minute window; a machine that missed the window adopts its first later response). Fixtures: pulled newest row, mid-window removal under four fetch schedules, mid-window Refresh. | 3 (T4), 6.2, 10.3.8, 10.4 |
| R2-2-virtual-placement-not-equal-to-listed-level | major | Fixed. Withdrawn pack rows stay in the lineup as **tombstones** with their last facts (bake tool carries them for 24 months), so withdrawal never moves a level between packs: all 314 catalogue members withdrawn in turn keep their exact (s, level) (`R2-OD66-D_withdrawn_and_spellings`). Virtual placement uses the **id only**, never provider-list price or `created`, so one id has one placement from every provider; an unknown model inherits the facts of its earliest (class, version) peer and can never outrank its peers (0 of 314 above a stronger listed neighbour outside the legacy residual). | 4.4 items 7-8, 5.6, 6.2, `intensity.py` `place`, `build_lineups(tombstones=)` |
| R2-3-effort-synonyms-reopen-F5 | major | Fixed. The pack's synonym table maps `extra-high`, `extra_high`, `extra high` -> `xhigh` and `ultra` -> `max` before the ladder (the same keys and ranks as `ACPSession.EffortRanks`, asserted by a test rather than shared code, because the colour ladder is versioned with the pack and includes `off`). An enumeration test covers every word any provider emits; none is unknown. The probe's dE 0.0396 to 0.0554 is now 0. | 4.4, C6, C9, `intensity.py` `EFFORT_SYNONYMS` |
| R2-4-stale-local-seed-shown-after-key-change | major | Fixed. Only a seed computed for the current key is ever shown; a key change glides to the new decisive seed or to the pending neutral (what a fresh install with the same failure shows). Checkpoint 1 of 10.4 now requires "the same key". Instance 4 combines the CLI-first maker with HF 429 when its catalogue row appears. | 5.5, 7.1, 10.3.10, 10.4 #4 |
| R2-5-post-pack-palettes-differ-across-builds | major | Fixed. Post-pack makers are derived against a reserved set **frozen per `DeriveVersion`** (locked, fast, the frozen pack-maker list), so a pack that bakes or retires a maker cannot move a post-pack palette; bake epochs are append-only and a newly baked maker never re-colours an existing one. Residual (different `DeriveVersion`s, new baked makers) and O7's role stated. Instance 10 runs the previous build's pack. | 5.3, 5.5, 5.6, 12, 10.4 #10 |
| R2-6-upgrade-union-resurrects-removed-rows | major | Fixed. Catalogues are full snapshots: the lineup uses whichever of the pack snapshot and `market.json`'s boundary snapshot has the newer boundary, never a per-row union. Upgrade fixture with a row removed between the old `market.json` and the new pack; instance 9 asserts upgraded equals fresh. | 6.2, 10.3.8, 10.4 #9 |
| R2-OD66-A-legacy-cap-lets-weak-newcomer-take-top | blocker | Fixed in IR-3: a member is legacy only if a member of the **same or a higher class** is more than 548 days newer. `claude-haiku-6` level 7, `gemma-5-4b` 6, `gpt-6.5-luna-nano` 4, `muse-glimmer-2-8b` 5, `trinity-large-mini` 2; every flagship keeps level 1. Test adds a weaker model 600 days after the newest member to all 54 dated lineups: 0 failures. | 4.4 item 4, `R2-OD66-A_weaker_newcomer_never_takes_top` |
| R2-OD66-B-dated-successor-collapses-into-2024-member | major | Fixed in IR-3: a dated row more than 30 days newer than its base is its own member (base class and version, its own day). `mistral-large-2512` is Mistral's level 1 (was 5 of 7), the 2024 row is legacy, its release changes the lineup, and `mistral-large-2611` re-spaces above it; `qwen-plus-2025-07-28` likewise. Price is deliberately not a split criterion (older dated snapshots priced unlike their alias would outrank it). | 4.4 item 2, `R2-OD66-B_dated_successor` |
| R2-OD66-C-speed-variant-fix-incomplete | blocker | Fixed in IR-3: a speed variant with no base ranks just below its base's class (tier - 0.05) whatever up-words it carries; `highspeed` and `ultraspeed` join `fast`. Grok `-fast/-highspeed/-ultraspeed` level 5 of 6, `kimi-k3.1-highspeed` 7 of 7, `claude-fable-5.2-fast` 3 of 8; every old flagship keeps level 1, also when the variant is first met by id. `gpt-6.2-astra-pro-fast` ranks below `gpt-6-astra-pro` but shares Silver's level 1, because IR-3 merges Astra Pro and Astra there (R2-OD66-E). | 4.4 item 2, `R2-OD66-C_speed_variants` |
| R2-OD66-D-off-catalogue-placement-loses-facts | major | Fixed with R2-2 (tombstones; id-only placement with peer facts) plus **harness spellings** from the pack: word-order rewrites (`claude-4.5-opus` -> `claude-opus-4.5`, `opus-4.5` -> `claude-opus-4.5`) and effort or thinking suffixes stripped when the rest is a member. 17 spellings checked, 0 mismatches (`claude-5.5-opus-high-thinking`, `grok-4.7-high`, `muse-spark-1.2-high`, `gemini-3.1-pro-high` ...). | 4.4 items 7-8, `intensity.py` `lookup`, `HARNESS_REWRITES` |
| R2-OD66-E-owner-example-sol-terra-luna-merge | major | Fixed in IR-3: visible steps go to the flagship alone only for a version/day/price difference, then family-word boundaries, then other class boundaries, then modifier boundaries, then class heads. OpenAI under Silver: before Astra, Sol Pro 6.1 / the Sols / Terra / Luna and below; after Astra, Astra (+Pro) / Sol (+Pro) / Terra with the generic Pros / Luna and below. Sol, Terra and Luna are distinct before and after; the owner's literal example is a test. | 4.4 item 6, 9.2 W1, `R2-OD66-E_owner_example_astra_sol_terra_luna` |
| R2-OD66-F-plain-named-next-generation-ranks-near-bottom | major | Fixed in IR-3: a member with no family word and no down-word, sharing the family's stem, whose major version exceeds every family member's, ranks above the family. `gpt-7` is level 1, rank 1; `gpt-7-mini` is not lifted. The adversary's "generation first whenever majors differ" was not taken: it would put `gpt-6-luna` above `gpt-5.6-terra`, against the owner's Terra > Luna. | 4.4 item 3, `R2-OD66-F_plain_next_generation` |
| R2-OD66-G-glide-scope-omits-title-and-s-driven-dials | minor | Fixed: the title, glow and every s-driven dial glide with the stops' `t` over 1.2 s; the 0.6 s ease is for effort changes only; frame test for continuity. `intensity-table.json` `transitions` says the same. | 4.6, C6, C25, 10.3.14 |
| R2-A1-catalogue-staleness-freeze | blocker | Fixed with R2-1: no max-created test; staleness judged only by generation time `Date - Age` (a body generated before the boundary is refused for the window and retried after 120 s); `created` later than generation + 1 day is ineligible. Fixtures: withdrawn newest row, `created` in milliseconds. The Refresh summary says whether its fetch was the window's snapshot or a prefetch. | 6.2, 3A.3, 10.3.8 |
| R2-A2-removal-depends-on-fetch-history | major | Fixed with R2-1: removal is a property of the window's boundary snapshot, never of fetch count. Instance 5 launches, resumes and presses Refresh inside every window and must match instance 1 at every publication. | 6.2, 10.4 #5, checkpoint 2 |
| R2-A3-reverification-monotonic-across-restarts | major | Fixed: re-verification and back-off across restarts use UTC day-window indices from server `Date` (persisted), never ticks; one observation per window at the first online moment; replacement needs two distinct windows; a Refresh adds no second observation. Instance 6 lives 10 h a day with a new monotonic origin and must converge in the same window as instance 1. | 4.2, 5.5, 6.1, 10.3.12, 10.4 #6 |
| R2-A4-one-snapshot-vs-background-phase | major | Fixed: staged publication. Stage 1 (catalogue plus visible makers, bounded at 8 s) publishes one snapshot with one glide start; background results publish per batch; automatic publications are never held; a press after stage 1 joins the background remainder; new-maker lookups go ahead of re-verifications. 3A.4 and 10.3.12 updated. | 3A.1, 3A.2, 3A.4 |
| R2-A5-phase2-runs-on-ui-thread | major | Fixed by removing phase 2: the refresh no longer force-reloads provider lists (colour depends on the global catalogue and the lists as loaded), so no CLI start, JSON parse, catalogue write or `Catalogs` notification happens on the main actor because of a colour refresh. The 5,000-row frame test runs against the real `ProviderRegistry` with stand-in CLIs. | 3A.1, 3A.2, C17, 7.2 |
| R2-A6-shade-memo-miss-on-ui-thread | major | Fixed: the engine precomputes, before publishing, a frozen shade table per (palette, level, effort word, theme) with the gradient rows (about 14,400 entries at most); `Resolve` only reads; a miss returns the previous value or Pending and posts a request. Test: zero `Shade` calls on the dispatcher thread. | C16, C18, 7.2, 10.3.14 |
| R2-A7-app-update-prime-fallback-snaps | major | Fixed: `Prime()` publishes the old `snapshot.json` (content-hash verified) as snapshot 0 even when digests no longer match, then recomputes and glides; only a maker absent from it is Pending; any later change of a first-drawn colour glides. Tests: launch after a `DeriveVersion` bump; `Prime()` step 2 delayed to 200 ms; zero snapped frames. | 3 step 1, 4.5, 10.3.14 |
| R2-A8-refresh-cannot-place-right-away | major | Fixed in part, rest OWNER (O8): a Refresh places newly seen models at once by name (id-only placement) and starts their makers' lookups; family re-spacing waits for the next boundary publication, which the summary names in local time; a body generated before the boundary never counts for the window. Immediate re-spacing on a press would break "same lineup, same shades"; a 1-hour window is the owner's other lever. | 3A.2, 3A.3, 6.2, 9.3 O8 |
| R2-7-prime-timeout-snaps | minor | Fixed with R2-A7: the pack-only snapshot is built synchronously from the embedded pack before `ShowMain`, so no frame ever uses the compiled fallback; a late stored snapshot reaches post-pack makers through the normal Pending arrival and everything else by glide. | 3 step 1, 4.5 |
| R2-8-reverification-window-wrong-and-refresh-dependent | minor | Fixed with R2-A3: the true bound is stated (two day windows, at most 48 h online; a fresh install at once) and Refresh no longer changes it. The suggested "confirm exactly 6 h later on the monotonic clock" was not taken: it does not survive a restart (R2-A3). | 5.5, 12 |
| R2-9-reference-and-plan-disagree-on-eligibility | minor | Fixed by dropping "with tools" from PLAN 4.4 (the reference never tested it; a row without tools is still pickable and would otherwise fall to virtual placement). `supported_parameters` is no longer a lineup input, so a mid-window change of it cannot move membership; other facts come from the boundary snapshot. | 4.4 item 1 |
| R2-10-maker-resolution-depends-on-catalogue-state | minor | Fixed: the catalogue match uses only the boundary lineup's rows (ordinal tie-break on maker slug); family words are anchored to the start of the last id segment. The five probe ids become Makers tests. | 4.1, C3, 10.3.2 |
| R2-A9-fail-closed-breaks-loopback-and-no-injection-seam | minor | Fixed: `FailClosedHandler` passes loopback and fails everything else; a PORT-ONLY `URLSession(handler, maxRedirects, onFinalUrl)` constructor carries the 3-redirect limit and the final-host check; `ProviderCliGuard` and `AllowedForTesting` are cited for registry tests; a build check enforces the `Marrow.Testing` reference. | 8, 10.1, U00b, U12 |
| R2-A10-provider-status-and-hung-cli | minor | Not applicable to the colour engine any more: the refresh no longer loads provider lists (R2-A5), so it never waits on a hung CLI or needs a load status. The summary shows each list's last-loaded time. The underlying `loadingCatalogs` hang is a `ProviderRegistry` issue outside this plan (14.2). | 3A.2, 14.2 |
| R2-A11-refresh-cooldown-and-backoff | minor | Fixed: a Refresh fetch at most once per 120 s; re-verification on Refresh only for seeds with no observation in the current day window; Refresh-triggered attempts never advance a back-off ladder (instance 8's 20 offline presses assert it). | 3A.1, 3A.2, 6.1, 3A.4 |
| R2-A12-corrupt-market-and-snapshot | minor | Fixed: every store file is validated and quarantined; a corrupt `market.json` falls back to `market.prev.json`, never to an older boundary; `snapshot.json` carries its own content hash. Instance 10 starts with all three damaged. | 6.1, 6.3, 10.3.11, 10.4 #10 |
| R2-A13-alias-merge-migration | minor | Fixed: a key merge drops both local seeds to Undecided and re-runs (a global rule, independent of what each machine stored). Instance 9 holds both keys of a merge in pack N+1. | 5.5, 10.4 #9 |
| R2-A14-huge-catalogue-ui-and-cpu | minor | Fixed: the summary shows counts and the top 5 per category with a virtualised details list refreshed at stage ends; the engine actor runs on a dedicated `BelowNormal` thread and derivation yields every few ms. | 3A.3, C15, 7.2 |
| R2-A15-glide-start-rows | minor | Fixed: rows are precomputed with the snapshot (R2-A6), so every changed track starts on the first frame that sees it, at that frame's time; a retarget's "from" row is exactly what the shader drew (the RGB blend at the current `t`). | 4.6, C16, C25, 10.3.14 |

### 14.2 Not fixed, or fixed differently, and why

- **R2-1's two-consecutive-boundary removal rule** and **R2-8's 6 h monotonic confirmation**: both depend on what one
  machine observed or on a process clock that restarts; AC-3 uses full-snapshot replacement and day windows instead.
- **R2-OD66-F's generation-first rule** for every member: it contradicts the owner's Terra > Luna across GPT-5.6 and
  GPT-6; IR-3 lifts only a plain-named next generation.
- **R2-OD66-B's price criterion** for splitting dated rows: it would let older, pricier dated snapshots outrank their
  alias; IR-3 splits on date only.
- **R2-3's literal reuse of `ACPSession.EffortRanks`**: a private ported table without `off` and outside the pack's
  versioning; the pack holds the colour ladder and a test pins the two to each other.
- **R2-A8's immediate re-spacing**: it would make the lineup depend on who pressed Refresh, against OD-0066's "same
  lineup, same shades"; put to the owner as O8 with the 1-hour window as the alternative.
- **R2-A10's `CatalogLoadResult` and cancellable handshake**: no longer needed by the colour engine. A hung CLI leaving
  `ProviderRegistry.loadingCatalogs` set is real, but it is a provider-registry defect, not a colour one; it should be
  its own unit.
- **Residuals that remain** (section 12): a catalogue change inside one 15-minute window; a machine that missed the
  window; post-pack rows withdrawn before the next pack (consistent across users, not level-preserving); id-only
  placement of a legacy model never seen in any catalogue (9 of 314 members measured); Astra Pro and Astra sharing
  Silver's top shade; small lineups re-spreading when a member joins (OD66-7, W1); cross-build differences until O7.

### 14.3 IR-3 evidence

`../intensity/intensity.py` (rule `IR-3`) regenerates `../intensity/intensity-table.json` deterministically: two
consecutive runs give MD5 `928c51d7346072b5dfc76fdfa26b1ee7` both times, and the same under `PYTHONHASHSEED` 1 and 777
(Python 3.14, Windows). `metrics.hardening` re-runs every round-1 case (all still pass) and adds `R2-OD66-A` to
`R2-OD66-F` and `R2-3`; `all_pass` is true. Re-running the round-2 probes against IR-3 (`hardening/r2/`, with
`intensity` in place of `intensity_r2`): `b_legacy_flip.py` every old flagship stays at level 1 and the 548/549-day
cliff is gone; `d_dated_collapse.py` `mistral-large-2512` level 1 and both releases change the lineup;
`p_speed_words.py` every old flagship keeps level 1; `j_provider_ids.py` every spelling equals its member;
`r2_effort_synonyms.py` dE 0 for every pair; `f_owner_examples.py` 3 distinct shades among Sol, Terra and Luna before
and after Astra, `gpt-7` rank 1; `h_stronger_all.py` and `i_big_random.py` unchanged (no stronger join raises an older
member, 0 shuffle differences); `c_real_flips.py` 0. `r2_withdrawn.py` and `e_withdrawn.py` still report changes
because they withdraw rows without tombstones (the pre-AC-3 model); with tombstones the same sweep gives 0 of 314
(`R2-OD66-D_withdrawn_and_spellings`). `l_weaker_all.py` still shows the OD66-7 re-spread of small lineups (W1(c)).
`r2_placement_facts.py` no longer applies: `place()` takes no provider facts. The engine-level findings (R2-1, R2-4 to
R2-6, R2-A*) are design changes verified by the 10-instance test (10.4), not by `intensity.py`.
