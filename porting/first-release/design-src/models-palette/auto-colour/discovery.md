# Auto-colour: how the port learns about models, and how a new one is first seen

Written 2026-10-02 for OD-0064, OD-0065 (same colours for every user) and OD-0066 (re-spacing when a maker's lineup
changes). Read: port `src/Marrow.Services/Providers/*`, `src/Marrow.App/AppModel.cs`, `EffortSlider.cs`; Swift
`reference/droppy-code-v1.7.0/DroppyCode/Services/Providers/*`. Data used for the measurements: the real OpenRouter
`/models` snapshot in `work/design/auto-colour/raw/models.json` (464 rows, newest `created` 2026-10-01 17:22 UTC),
scratch scripts `rank_test*.py` in the session scratchpad. Where something is not visible in the code it says so.

## 1. Headline findings

1. Every provider's list passes through one chokepoint, `ProviderRegistry.UpdateCatalog(list, provider)`
   (ProviderRegistry.cs:1435). It is the right place for a "list refresh diff". But the constructor fills
   `catalogs` from the cached blob and the seeds WITHOUT calling it (:228-266), so a launch-time reconcile pass is also needed.
2. `ModelOption` (Provider.cs:282) carries only id, name, detail, efforts, defaultEffort, isDefault, fastTier. No price,
   context, date, maker. The persisted catalog blob (`EncodeCatalogs`, :1450) holds only those. Price/context/image
   facts for OpenRouter live in a static in-memory dictionary (`OpenRouterAPI.facts`) that is lost at exit.
3. OpenRouter's own list is by far the richest source (section 3) and the port throws most of it away: `ModelEntry`
   reads id, name, context_length, prompt/completion price, reasoning (as a bool from `supported_parameters`) and image
   input. NOT read: `created`, `canonical_slug`, `reasoning.supported_efforts` / `default_effort`, `knowledge_cutoff`,
   `expiration_date`, `alias_target`, `benchmarks`.
4. The port lists OpenRouter models only with an API key (`ListModels` returns empty for an empty key, :86-88) although
   the endpoint is public (the port's own header comment says it answers 200 keyless). OD-0064 needs a keyless
   fetch, independent of whether the user has any OpenRouter account.
5. A running app never re-reads a list on its own. `LoadCatalog` has a 6 h freshness test (:477) but it runs only
   when something asks, and `attemptedCatalogs` makes every non-forced ask a no-op for the rest of the launch (:461).
   There is no timer. New models appear only on restart, a forced load (Settings "Load models" button, key save,
   "Check providers again", CLI update) or a session-start event.
6. Three first-party API providers silently drop unknown ids (Meta `muse-spark*` only, Z.ai `glm-5.3*` only, DeepSeek
   drops four legacy ids, passes the rest with name = raw id). A `glm-5.4` or `muse-spark-1.4` never reaches the
   catalogue until code changes, so no engine can colour it. Needs a decision (see 8).
7. A user's provider list is account-specific (Copilot filters by policy; Command Code, Cursor, OpenCode by plan;
   CLIs by installed version). OD-0065/0066 need the SAME lineup on every machine, so the lineup that ranks strength
   must come from one global reference (the keyless OpenRouter catalogue + a shipped baseline), never from the
   user's own provider list. Provider lists supply identity and the trigger only.

## 2. Per provider: where the list comes from, when it refreshes, what fields exist

Swift and port are the same for every row except OpenRouter (port-only). "Refresh" = what actually triggers a re-read.

| Provider | Source of list | Refresh triggers (port = Swift) | Fields in ModelOption | Fields visible on the wire but dropped / absent | How a NEW model is first seen |
|---|---|---|---|---|---|
| Claude | CLI stream-json `initialize` handshake (`ClaudeSession.Handshake`, :278; also each session start) merged over a 5-row seed (`MergedClaudeCatalog`, :1225): default, opus, `claude-fable-5-1[1m]`, sonnet, haiku | Launch (Claude is "seeded" so every launch handshakes, :1072); session start emits `ProviderEvent.Models` -> `UpdateCatalog` (ThreadRuntime.cs:3871); CLI auto-update every 12 h but only at launch (`UpdateCLIIfDue`, AppModel.cs:329) | id (alias like `opus`, or full id), displayName ("Opus 5.5"), description, `supportedEffortLevels`, `supportsFastMode` | No price, context, date. The id is an alias; the version lives only in displayName/description | Only when the user's installed CLI is new enough to list it. Lag = CLI release lag + up to 12 h updater + restart |
| Codex | `codex app-server` JSON-RPC `model/list`, paged up to 10x (CodexSession.cs:655) | Launch (seeded, :1072); forced loads | id, displayName, description, `supportedReasoningEfforts`, `defaultReasoningEffort`, isDefault, `serviceTiers` (-> fastTier). `hidden` rows skipped | Nothing else is read; whether the raw rows carry more is not visible here | Whatever the installed Codex returns; whether server- or version-driven is not visible in the port code |
| Antigravity | `agy models` TSV `slug<TAB>Name`, effort variants (-high/-medium/-low) collapsed (AntigravitySession.cs:480) over a 7-row seed | On ask when stale (>6 h) or forced; stale seed replaced | id (base slug), name, efforts low/medium/high, isDefault | None. Seed mixes makers (Gemini, "Claude Sonnet 4.6 (Thinking)", GPT-OSS) so provider is not the maker | `agy` CLI list; first seen on the next stale/forced load |
| Copilot | `copilot` JSON-RPC `models.list` (CopilotSession.cs:484) behind an "Auto" row | On first use (seed is only Auto) and when stale/forced | id, name, efforts, defaultEffort; detail text holds `billing.multiplier` ("3x premium requests") and "Enable it in settings" | Multiplier exists only inside the detail string, not as a field. `policy.state` "disabled" rows removed, so the list is account-specific | Account list at next load. Not stable across users |
| Command Code | `cmd --list-models --no-auto-update`, text rows `id  detail` under section headers (CommandCodeAPI.cs:179-234) over an 11-row seed | On first use (seed) and when stale/forced | id (often `maker/model` or bare `claude-*`/`gpt-*`), name (derived by `DisplayName`), detail = "section . text", isDefault | Efforts and context windows come from two hard-coded tables (:289-341): an id not in `Efforts(id)` gets NO effort scale, not in `ContextWindow` no window. Section header is sometimes a maker ("Anthropic") and sometimes not ("Open Source") | CLI text list. A new model shows up with no efforts until the table is edited |
| Pi | `pi --mode rpc` `get_available_models` + `get_state` (PiCLI.cs:87) | On first use (seed is one "Default" row) and when stale/forced | id = `provider/id`, name, detail = Pi's provider, reasoning bool -> efforts off..max, isDefault | Only name/provider/reasoning. Pi's provider is the HOST (can be `openrouter`, giving ids like `openrouter/anthropic/...`), so the maker is inside the id | Pi's registry (version-bound to the installed Pi plus user config) |
| Cursor, Grok, Devin | ACP `session/new` -> `models.availableModels` (+ `_meta.reasoningEfforts` for Grok); Cursor/OpenCode report effort of the current model only so the probe walks every model (ACPSession.ProbeModels :332) | On ask when stale/forced; also each ACP session start emits `Models` (ACPSession.cs:449,529) | modelId (Cursor: bracket parameters stripped by `BaseModelID`), name, description, efforts, defaultEffort, fastTier | Nothing price- or date-like | Next probe or session start. The probe spawns the CLI and is slow for big catalogues |
| OpenCode (Zen) / OpenCode Go | One shared ACP probe, split by `opencode-go/` id prefix (`OpenCodeModels` :868); names arrive as "OpenCode Go/DeepSeek V4 Flash" and the prefix is stripped | Once per launch (`openCodeCatalogTask` memo), re-probed if the cached list has <=1 effort scale | as ACP | none | Next probe |
| DeepSeek | `GET api.deepseek.com/models` with the key, ids only; `DeepSeekAPI.Option(id)` table (:70-95) | Launch (Bootstrap :321), key save (forced) | id, name ("V4 Pro", else raw id), efforts low/high/max, defaultEffort | Standard OpenAI-shape rows may carry `created`/`owned_by`; the port reads only `id` (OpenAICompatibleAPI.ListModels :31-36). Not verified against the live endpoint | Live and fast, but only visible after the next launch or key save. Unknown ids appear with name = id |
| Meta | `GET api.meta.ai/v1/models`, ids only; `MetaAPI.Option` accepts only ids starting `muse-spark` | Launch, key save | id, name ("Spark 1.3"), efforts, detail text | as above | A `muse-spark-*` id appears after next load (named by raw id if not in the 5-row table). A new family name is dropped |
| Z.ai | `GET api.z.ai/.../models`, ids only; `ZaiAPI.Option` accepts only ids starting `glm-5.3` and not `flashx` | Launch (`LoadCatalog(Zai)` :326), key save | id, name, efforts low/high/max (default max) | as above | `glm-5.3-*` only. `glm-5.4`, `glm-6` are never listed |
| OpenRouter (port only) | `GET openrouter.ai/api/v1/models` WITH key (`OpenRouterAPI.ListModels`), whole item parsed into `ModelEntry`; one row `openrouter/auto` as the offline seed | Launch if key set (Bootstrap :321), key save, Settings load; 6 h stale rule, once per launch | id, name (vendor lead removed), detail "vendor . 1M context . $in / $out per 1M", efforts fixed `low/medium/high` when reasoning, default medium | Kept in memory only: context, image input. Dropped: created, canonical_slug, reasoning.supported_efforts/default, benchmarks, knowledge_cutoff, expiration_date, alias_target | Cheapest and quickest source (section 3), but gated on a key and a launch/force |

The composer's chip and the model picker read `registry.Models(provider)` (EffortSlider.cs:53,151). The slider
asks `LoadCatalog(thread.Provider)` once per provider per launch (:151) and `LoadCatalog` for Hydra partners (:322).
`EffortBrand.Create(provider)` (EffortSlider.cs:1099) decides colour from the PROVIDER only; OpenRouter is a Purple placeholder.

## 3. What the real OpenRouter catalogue contains (measured, 464 rows)

| Field | Coverage | Notes |
|---|---|---|
| id, name, canonical_slug, context_length, pricing.prompt/completion, architecture.input_modalities, supported_parameters | 464/464 | prices are USD per token as strings; "-1" on 6 router rows (auto, fusion, pareto-code, ...); 21 rows are free (0/0) |
| `created` (unix) | 464/464, none zero | OpenRouter's add time, not the maker's announcement. Rows added in one batch share a second-level timestamp (the 12 `gpt-5.6-*` rows 2026-07-09 09:54:10-21), so it cannot order siblings |
| `canonical_slug` with a `-YYYYMMDD` suffix | 250/464 | the dated release; 219 of 250 are within 3 days of `created` (median 0). Better release date where present |
| `reasoning` object | 331/464 | `supported_efforts` present on 192 rows, values among `max, xhigh, high, medium, low, minimal, none`; `default_effort`; `mandatory`. The port's fixed `low/medium/high` and its `xhigh`/`max` -> `high` mapping (OpenRouterSession.cs:48-57) discard that |
| `benchmarks.artificial_analysis.intelligence_index` | 146/464 (31%) | 20/29 Anthropic, 26/102 OpenAI, 19/41 Google, 0/8 Meta-Llama. A third-party number that Artificial Analysis revises; unsafe as a determinism input |
| `knowledge_cutoff` 180, `expiration_date` 28, `alias_target` 18 | partial | aliases are the `~vendor/...-latest` pointer rows (~openai/gpt-astra-latest -> openai/gpt-6-astra). They must not enter a lineup |
| Variant suffixes | 73 `:batch`, 17 `:free` | same model, different billing. Collapse to the base id |
| Makers (id prefix) | 65 prefixes, 20 with a single model | `meta` vs `meta-llama`, `z-ai`, `x-ai`, `mistralai`, `moonshotai`, `bytedance`/`bytedance-seed`, `~vendor` all need an alias table to name the maker |
| Cadence | 2 rows in the last day, 8 in 7 days, 62 in 30 days, 133 in 90 days | about two new rows a day, including variants |

## 4. Strength signals per source, and how reliable each is

Reliability measured as pairwise agreement with Artificial Analysis' index among models of the same maker that carry
one (343 pairs over nine makers; AA only as a yardstick, not as an input). Tier ladder used was generic words only, so
the tier numbers are a floor.

| Signal | Where it exists | Deterministic for all users? | Reliability and known traps |
|---|---|---|---|
| Completion/prompt price | OpenRouter (all rows); Copilot via multiplier in detail; Command Code/Pi/ACP/CLIs/first-party /models: none | Yes if read from one catalogue; per-token prices can change over time, so snapshot-dependent | Alone: 75% agreement (Anthropic 71%, DeepSeek 54%). Traps: a newer, stronger model can be cheaper (Opus 5.5 $20 vs Opus 5 $25 vs Opus 4.8 $25); Terra costs more than Sol ($12 vs $10) against the Luna<Terra<Sol<Astra order; routers are -1; free variants are 0 |
| Tier words | every source carries a name; Claude displayName, OpenCode/Command Code names, Copilot name, OpenRouter name | Yes (a shipped, versioned word ladder) | Strong inside one generation (Luna<Terra<Sol<Astra; Haiku<Sonnet<Opus<Fable; flash-lite<flash<pro; nano<mini<standard<pro<max<ultra). Fails across generations (Gemini 3.8 Flash beats 3.1 Pro) and for unseen words. Needs a maker-scoped table for the cases that break the generic ladder |
| Version number | name or id of all sources | Yes | Highest standalone signal: version tuple first, then tier, price, date = 91% agreement. Trap: numeric compare is wrong for non-decimal schemes: `grok-4.20` parses above `grok-4.7` but was released before `grok-4.3`. Parameter counts (70b, a4b) and dates in ids (`-0731`, `-20251001`) must be stripped before parsing. Different families under one maker (Gemma vs Gemini, Muse Glimmer vs Muse Spark, o-series vs GPT) do not share a version axis |
| Release date | OpenRouter `created`/slug date; none elsewhere | Yes per catalogue | Date alone 86%. `created` ties inside a batch and is OpenRouter's add time. Slug date is better but on 54% of rows. A later date is "newer", not "stronger", hence never above tier |
| Context length | OpenRouter all rows; port tables for Command Code; Meta/Z.ai/DeepSeek constants | Yes | Weak: most frontier models are 1M; correlates by generation (Anthropic 0.52, OpenAI 0.89 rank correlation) but ties constantly. Use only as a late tie-break |
| Reasoning support / efforts | all sources | Yes | Not a strength order (every new model reasons). Useful as a class hint only; the effort scale itself is for the effort shade (OD-0063), not for the model's strength |
| Benchmarks (AA index) | OpenRouter 31% of rows | No (third-party, revised) | Best correlate, but partial and not stable; keep out of the rule, use as an offline validation yardstick |
| Provider's own ordering/default flag | `isDefault`, list order | No (differs per user/account) | Never an input |

## 5. Proposed fixed precedence for ranking inside a maker's lineup

A pure function of (engine version, catalogue rows). Lexicographic, each key compared ascending, strongest = greatest.

0. Eligibility (decides who is in the lineup). Row must be text-chat with tools (`tools` in `supported_parameters`),
   not an alias (`~...`, `alias_target`), not expired, not image/audio/embedding/router. Variants `:free`, `:batch`,
   `:thinking`, `:nitro`, `-preview` collapse onto the base row (the base row's price and date win). A `-pro` sibling of
   the same class is its own member of the lineup (kept; ranks just above its base through key 1's tie-break).
1. Class. The maker-scoped tier ladder if the name has a ladder word (Luna 0 < Terra 1 < Sol 2 < Astra 3; Haiku < Sonnet <
   Opus < Fable; Spark has one class); else the generic ladder; else a price band from completion $/M
   (<1, 1-4, 4-16, >=16 -> 0..3). The ladder is shipped inside the app, versioned, and stored in the engine file.
2. Version tuple: numeric component compare after stripping size/date tokens, with a per-maker scheme override (Grok
   `4.20` is read as 4.2.0 beyond the point release; tested against `created`). If version order contradicts `created` order
   by more than 30 days the maker is flagged and key 3 decides.
3. Release date: dated slug if present, else `created`, truncated to the day.
4. Completion price (higher stronger), then prompt price.
5. Context length (larger stronger).
6. Final tie-break: the canonical id compared ordinally (UTF-8 bytes, culture-free). So the order is total and identical on every machine.

Measured agreement with AA on the snapshot (343 pairs): class > version > price > date 88%; class > date 87%;
version > tier > price > date 91%; OD-0066's literal order price > tier > version > date 75%. I recommend class
first (it keeps the owner's invariant, "a newer but weaker variant does not take the top shade"), version second, price
only as tie-break and as the class fallback for unknown words. This moves price from first to fourth, which
OD-0066 lists first; needs the owner's word. Known misses to accept or special-case: Sol 6.1 (version 6.1) vs Astra 6 (class decides, correct);
Opus 5 vs Fable 5 (AA says Opus, class says Fable); Gemma sits below the Gemini ladder by price band only.

Strength to intensity: members sort into G distinct groups (members with identical keys 1-4 share a group; a
lineup of one has G = 1). Position `p = g / (G-1)` and the strongest takes p = 1 (the most intense shade of OD-0063),
the rest step down in equal ordinal steps (OD-0066: "the strongest takes the most intense shade, the rest step down").
A one-model maker or G = 1 gets the fixed mid value p = 0.6 (final constant on the canvas, with the
intensity range). A very wide lineup (OpenAI ~40) would give steps too fine to see: cap by merging groups whose p
differs by less than a minimum visible delta (a shipped constant), merging from the bottom. Results are cached per
(engine version, maker, lineup digest) and recomputed when the digest changes; they are never frozen per model, because
OD-0066 makes intensity a function of the lineup. The maker's HUE palette is what is "locked" (OD-0050/0065).

## 6. Maker resolution per source (needed before any ranking)

| Source | Maker comes from |
|---|---|
| OpenRouter | id prefix via an alias table (`meta-llama`, `meta` -> Meta; `x-ai` -> xAI; `z-ai` -> Z.ai; `mistralai`; `moonshotai`; `bytedance`, `bytedance-seed`; strip `~`) |
| DeepSeek, Meta, Z.ai | the provider itself (first-party APIs) |
| Claude, Codex | the provider (Anthropic, OpenAI), except Codex rows that are not OpenAI (not seen) |
| Antigravity, Copilot, Cursor, Grok, Devin, OpenCode, Command Code | leading family token of the id/name (`claude-` Anthropic, `gpt-`, `o3` OpenAI, `gemini` Google, `grok` xAI, `glm` Z.ai, `kimi` Moonshot, `muse` Meta, `deepseek`, `qwen`, ...) via the shipped family table; Command Code ids with `maker/` use the prefix. Command Code's section header is not reliable ("Open Source") |
| Pi | the id after Pi's provider; when Pi's provider is a router (`openrouter`), the second segment |
| Unknown family token | resolved by looking the model up in the global catalogue (slug match); if still unknown, treated as a NEW maker (online lookup of OD-0050), single-model lineup, neutral mid intensity |

Matching a provider-local id to a catalogue row: normalise (lowercase, strip `[...]` and `:variant`, `claude-opus-5-5` ->
`claude-opus-5.5`, remove vendor prefix) and compare to the catalogue slug and name. Misses fall back to rules over
the local name alone.

## 7. How fast does each source show a new model (how long two users can disagree)

| Source | Lag after the maker releases | Then the app side |
|---|---|---|
| OpenRouter catalogue | Hours to ~3 days: 219 of 250 dated slugs were listed within 3 days of the slug date, median same day; the newest rows are under a day old (2026-10-01) | Not before: launch (if keyed) or a forced load; no timer. Proposed keyless fetch every 6 h plus on launch and on resume |
| Claude CLI | The CLI release lag, then up to 12 h for the updater, and only at launch | A new Claude model reaches users in a CLI version, so two users disagree until both update. The global catalogue closes this gap for colouring |
| Codex, Antigravity, Pi, ACP | Version- or server-driven, not visible in the port code | Per launch, per 6 h stale ask, or session start |
| Copilot, Command Code, Cursor, OpenCode | Account/plan gated and CLI-bound | Same; the account list may never show a model another user sees |
| DeepSeek / Meta / Z.ai /models | Live server list, but tables drop unknown names | Launch or key save |

Two instances can disagree on a given model for: (a) up to one poll interval (6 h) plus clock skew, (b) a launch gap of
any length if the app stays closed, (c) forever for models only some accounts see, unless the lineup comes from the
global catalogue. To limit (a) and (b): ship a dated catalogue baseline in every build (instances on the same build
agree from first run offline); poll the keyless catalogue on launch and every 6 h; apply a lineup change only once a
fetch succeeded (a failed fetch changes nothing and retries with back-off); a model absent from the baseline is
neutral and "provisional" until the fetch lands. State this honestly in the evidence for OD-0065: the guarantee is
"same inputs -> same shades", and the inputs converge within one poll interval of a model appearing in the catalogue.

## 8. Proposed triggers for the engine

The engine owns a local store (maker palettes, model facts, lineup digests, engine version) and a single-flight queue.
All paths call one entry, `Colours.Resolve(provider, modelId)`, which returns at once (stored shade, else the neutral
free shade) and schedules the work. 3 s budget per OD-0050; recolour smoothly when the work lands.

- T1 List-refresh diff. In `UpdateCatalog` after the guard at :727: compare the new list's ids with the previous list's
  (and with the engine store). New ids enqueue (provider, id). Covers every CLI, ACP session start and API list.
- T2 Launch reconcile. After `Bootstrap`'s loads (AppModel.cs:313-327) walk every `Catalogs` entry (the constructor's cached
  lists and seeds bypass T1) and enqueue anything without a stored record or whose engine version is older.
- T3 First selection of an uncoloured model. Where the effort card or composer chip gets a model (`EffortSliderCard`, the
  picker, `EnsureModel` at ThreadRuntime.cs:1401, Hydra pair pickers): `Resolve` is the hook, the loading animation of
  OD-0050 shows while pending. Also catches models restored from a thread file that no list has produced yet.
- T4 Market watcher. A keyless fetch of the public catalogue at launch, on resume and every 6 h, independent of the
  user's providers and keys; its diff is what makes "any new model in the market" coloured before the user has it in a
  provider list. Listed in the network inventory; goes through `URLSession.Transport` (OD-0057) so tests are offline.
- T5 Maker lookup. For a maker with no palette (OD-0050): one logo/site-icon download, dominant colour, nearest free
  shade by the order-independent rule of OD-0065, saved locally. A failure shows neutral, retries later, locks nothing.

Debounce triggers by 250 ms and group per maker so a 40-row catalogue lands as one re-spacing, animated (OD-0066).

## 9. Prerequisites the research found (not built here)

1. Capture in `OpenRouterAPI.Parse`: `created`, dated `canonical_slug`, `reasoning.supported_efforts/default_effort`,
   `alias_target`, `expiration_date`, tools support; keep them in an engine-side `ModelFacts` record (persisted by
   the engine), not by widening `ModelOption` and its cached blob.
2. A keyless public-catalogue fetch next to the keyed `ListModels`.
3. Decide whether `MetaAPI.Option` / `ZaiAPI.Option` may pass unknown ids through like `DeepSeekAPI.Option`, otherwise
   a new Z.ai or Meta family cannot be seen. Same for Command Code's `Efforts`/`ContextWindow` tables for unknown ids.
4. Optional: a periodic catalogue refresh in `ProviderRegistry` (the engine's 6 h poll could drive it).
5. Test fixture: `raw/models.json` is a ready real fixture for the seam; it holds 20 single-model makers and 18 alias rows.
6. Owner questions: price first (OD-0066's wording) or class first (the numbers prefer class/version); which constant for a
   one-model maker; whether a lineup is limited to recent models (older models dilute the ladder).
