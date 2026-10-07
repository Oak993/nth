# Keyless online sources for a model maker's identity, website, logo and brand colour

Research for OD-0050 / OD-0064 (Marrow, the Windows port of Droppy Code). Tested live on 2026-10-02 from one Windows machine, no API key anywhere, descriptive User-Agent `MarrowColourResearch/0.1`. The app's tests must never do this; they use the HTTP seam (`URLSession.Transport`, OD-0057) with the fixtures listed in section 8.

All numbers below come from `probe.py`, `or_authors.py`, `resolve_eval.py`, `guess_eval.py`, `latency.py`, `chain_timing.py` in this folder (raw output in `results/*.json`, `or_authors.json`, `latency.json`, `chain_timing.json`). Fetched logos are in `samples/<maker>/` (file name = source). `contact.png` is a side-by-side sheet of the logos for 25 makers.

The 25 makers (real OpenRouter ids, 464 models / 65 id prefixes in the live catalogue): mistralai, qwen, moonshotai, nousresearch, liquid, arcee-ai, inception, thudm, anthropic, openai, google, deepseek, x-ai, z-ai, nvidia, minimax, meta-llama, tencent, aion-labs, cohere, bytedance-seed, xiaomi, perplexity, sakana, ibm-granite. (thudm is no longer on OpenRouter today; kept as the hard case.)

---

## 1. Verdict

1. **No single source is enough, but a small chain is.** Median wall time from "unknown maker" to "logo bytes plus dominant colour" was **0.5 s** (p95 1.2 s, max 2.0 s) over the 25 makers with the chain in section 5, well inside the 3 s bound of OD-0050. Every maker that had a domain or a Hugging Face org ended with at least one logo image (24 of 25; thudm only has the Tsinghua seal), and for most it is the real brand mark.
2. **The best AI-specific source is one 105 KB file:** `@lobehub/icons` `es/toc.json` (MIT, on unpkg/jsDelivr, cacheable for a year per version). It has 340 AI brands with id, title, **official brand colour, homepage URL**, and a matching SVG set (368 brands). It matched 23 of 25 makers (miss: bytedance-seed, thudm), gave a usable chromatic colour for 12 and a homepage for all 23 (21 correct, 2 wrong). Ship a snapshot in the app and refresh it.
3. **OpenRouter's documented public API gives the maker's display name and its Hugging Face org, but no logo, URL or colour** (fields listed in 2.1). Its `/api/v1/providers` list gives a website domain for 17 of 25.
4. **Hugging Face is the best generic fallback**: org overview JSON (keyless, `RateLimit: 500 per 300 s`) gives `avatarUrl` (200x200 webp) for 22 of 25, and the org page names the official website for 17 of 25.
5. **Favicon service `t0.gstatic.com/faviconV2`** is the fastest logo source once a domain is known: 24 of 25 hit, median 0.10 s, 180 to 256 px for most, and a clean 404 for unknown domains (no fake globe). Its weakness is that it returns whatever favicon the site has (often 32 to 48 px, often a black or white tile).
6. **Skip**: Clearbit (dead, DNS fails), icon.horse (always returns 200 with a generated letter icon, one 16 s stall), logo.dev and Brandfetch (need a key), faviconkit (returns a 1x1 pixel), Google `s2/favicons` (same data as faviconV2 but upscaled and blurry), Wikidata as a primary (wrong entity for 2 of 25, wordmarks only, no colour property filled anywhere).
7. **Nine of the 25 makers have a monochrome logo** (moonshotai, nousresearch, liquid, inception, aion-labs plus the already-locked anthropic, openai, x-ai, z-ai). Only 5 of those are genuinely new to the app. For them the dominant-colour step returns "mono", which is exactly the prototype's neutral-shade path (derive.py `mono`). The chain must therefore tolerate "no hue" as a normal outcome, not an error.
8. **One legal flag for the owner:** OpenRouter's author web pages (`openrouter.ai/<slug>`) hold the best curated icons (hosted SVG/PNG for 22 of 56 authors) and the real website for every author, but the page is HTML scraping. As summarised from OpenRouter's terms (section 7.5 to 6) automated copying of site information is prohibited. The documented JSON API is fine. I recommend the chain does **not** depend on author pages (it does not), and that they stay a research reference only unless the owner decides otherwise.
9. **Stability for OD-0065 (section 10).** Byte-identical for every client and pinnable: version-pinned `@lobehub/icons` files and Hugging Face avatars at their content-addressed URL. Mostly identical with a known split: Google `faviconV2` (the four cache shards disagreed for 1 of 25 makers, nousresearch; 23 of 24 logos fetched 8.5 h earlier were byte-identical) and DuckDuckGo. Not the same for everyone: `www.google.com/s2` (random shard), the maker's own site (AVIF vs PNG negotiation, bot walls, locale redirects), Wikimedia thumbnails (WebP vs PNG by Accept), and OpenRouter's newer hosted icons (about a third of requests 404). `@lobehub/icons@latest` moves (11 releases in 30 days). The chain in section 5 needs the amendments in 10.4 (no first-finisher race, definitive miss versus transient failure, pinned request, pinned toc version, provenance saved with each palette).

---

## 2. Source by source (25 makers)

Latency is per request, measured sequentially (`latency.py`), so it includes the full body download. "Real brand mark" is my visual judgement from `contact.png`.

### 2.1 OpenRouter documented API (keyless)

| Endpoint | Result |
|---|---|
| `GET https://openrouter.ai/api/v1/models` | 200, 763 KB, 0.85 s, `Cache-Control: max-age=120`, `Access-Control-Allow-Origin: *`. 464 models. Per-model fields: id, canonical_slug, hugging_face_id, name, created, description, context_length, architecture, pricing, top_provider, supported_parameters, links, reasoning, **benchmarks** (`artificial_analysis.intelligence_index`, coding and agentic index on 255 of 464 models), alias_target, expiration_date, knowledge_cutoff. **No author object, no icon, no URL, no colour.** |
| Maker display name | Derivable: the text before ": " in `name` ("Mistral: ...", "Anthropic: ..."). Missing for models with no prefix (e.g. "Pareto 26.10 Preview"). Not always the official name (x-ai models say "xAI:", the author page says "SpaceXAI"). |
| HF org | `hugging_face_id` on 178 of 464 models; first path part is the HF org (`deepseek-ai`, `XiaomiMiMo`, `meituan-longcat`). Available for 22 of the 25 makers. |
| Description URLs | Free text; the maker's own domain appears for only 3 of 25. Not a method. |
| `GET /api/v1/providers` | 200, 26 KB, 0.52 s, 112 providers: name, slug, privacy_policy_url, terms_of_service_url, status_page_url, headquarters. A domain for 17 of 25 (cohere returns the legacy `cohere.ai`; meta-llama returns `meta.com`/`facebook.com`). These are inference providers, so some makers are absent (qwen, nousresearch, thudm, ibm-granite, bytedance-seed, google). |
| Slug id quirks | Leading `~` aliases (`~anthropic/...` 4, `~openai`, `~deepseek`, `~z-ai`, `~google`, `~x-ai`, `~moonshotai`) must be stripped; `openrouter/` and `stealth/` are routers, not makers; `fireworks/` is a provider acting as a maker; `bytedance` and `bytedance-seed` and `meta` and `meta-llama` are separate prefixes of one maker. |
| `/api/v1/authors`, `/api/frontend/*`, `/api/v1/models/user` | 404 / 404 / 401. There is no public author endpoint. |
| Rate limit | None published for keyless `/models`; docs only describe key-based limits. Cloudflare-cached 120 s with stale-while-revalidate. The app already uses this call for the picker (OD-0049), so it adds no new traffic. |

### 2.2 OpenRouter author web pages (HTML, not an API)

`GET https://openrouter.ai/<slug>` (1 MB, median 0.31 s, p95 0.39 s, 25 of 25). The Next.js payload has `author_display_name`, `author_icon_uri` (null for most, a file name or a site URL for a few) and an `<img alt="Favicon for <slug>">`. Over all 56 current authors: 22 point at a hosted file `https://openrouter.ai/images/icons/<Name>.(svg|png)` (13 of the 25 makers: mistralai, qwen, moonshotai, inception, anthropic, openai, google, deepseek, meta-llama, tencent, cohere, perplexity, ibm-granite; 0.09 s each; 191 to 961 px or SVG; the real mark, curated). 34 point at a Google faviconV2 URL whose `url=` parameter is the maker's site (this gives a domain for every author, including makers with no other source). 9 of those 34 are the Hugging Face favicon (placeholder: anthracite-org, cognitivecomputations, gryphe, mancer, meituan, prism-ml, rekaai, sao10k, undi95) and must be rejected; HF avatars cover 8 of the 9.
Concerns: HTML scraping against OpenRouter's terms (see verdict 8), fragile markup, 1 MB per maker. Fresh-maker proof: apodex, dots-studio and poolside (not in any icon pack) all had a hosted icon.

### 2.3 Hugging Face

| Call | Result |
|---|---|
| `GET https://huggingface.co/api/organizations/<org>/overview` | 22 of 25 hit, median 0.18 s, p95 0.29 s. JSON: `avatarUrl`, `fullname`, `isVerified`, `numFollowers`, plan. Case-insensitive (`qwen` = `Qwen`). 404 for the 3 whose HF org differs from the OpenRouter slug (inception = `inception-labs-ai`, perplexity = `perplexity-ai`, sakana = `SakanaAI`). Individual users: `/api/users/<name>/overview` (works for gryphe, sao10k, undi95). |
| Org name lookup | `GET /api/quicksearch?q=<name>&type=org` (0.18 to 0.5 s) returns candidates with `fullname`; pick the one whose normalised fullname equals the normalised maker display name (`perplexity` finds `perplexity-ai`, `sakana` finds `SakanaAI`, `liquid` finds `LiquidAI`, `ibm-granite` finds itself; `inception` lists the wrong `inception42` first, so exact-name match is required). |
| Avatar | `cdn-avatars.huggingface.co/...` webp, 200x200, 2 to 6 KB, 22 of 25 (0 below 48 px). Real brand mark for most. Exceptions: cohere = Cohere Labs star, not the Cohere logo; thudm = Tsinghua seal; tencent/minimax/nvidia = logo plus wordmark tile. |
| Org page `GET https://huggingface.co/<org>` | 300 KB HTML, median 0.33 s, p95 1.2 s. The profile block holds the maker's website (`href=... rel="nofollow" target="_blank"`) for 17 of 25 and `orgEmailDomains` for 10 of 25. |
| Rate limit | Published in response headers: `RateLimit-Policy: "fixed window";"api";q=500;w=300` (anonymous, per IP). A new maker costs at most 3 calls. |
| Terms | Avatars are uploaded by the org; fine for a local colour read, not for redistribution. |

### 2.4 Wikidata and Commons

`wbsearchentities` (median 0.26 s), entity JSON via `Special:EntityData/<Q>.json` (0.23 s), logo via `https://commons.wikimedia.org/wiki/Special:FilePath/<P154 file>?width=256` (1.0 to 1.6 s, 330 px PNG thumbnail).
- Entity resolution is the weak point: searching the plain name gives wrong entities (`liquid` = the state of matter, `minimax` = a German fire-protection company (P856 `minimax.de`, red wordmark), `mistral` = software). Using the author page's display name plus an exact-label and description-keyword filter fixed most, but 11 of 25 still had no usable entity.
- P856 official website: 13 of 25 correct, 1 wrong (minimax). P154 logo: 14 of 25; **always a wordmark or lockup, small, and for x-ai it is the old SpaceXAI wordmark**. P6364 (official colour) is filled for none of the 25; P8972 (small logo) only for Google.
- Licence: Wikidata data is CC0. Commons logo files are mostly public-domain text logos or non-free, trademark-encumbered; fine to read a colour from, not to redistribute.
- Rate limit: no hard number, User-Agent with contact is mandatory. Use only as a late fallback.

### 2.5 The maker's own site

| Item | Result (25 makers) |
|---|---|
| `GET https://<domain>/` | 21 of 25 reachable (median 0.28 s, p95 1.4 s). Failures: tencent.com, ibm.com (bot walls, timeouts), mi.com (403), thudm.github.io (404). |
| `<link rel="icon" / apple-touch-icon>` | 17 of 25 had a declared icon; best one a png (mostly 180 px apple-touch) for 11, ico for 5, svg for 1 (z.ai). 4 are 48 px or smaller. |
| `/favicon.ico` | 17 of 25 answered 200 but only 15 are images (median 0.9 s, p95 1.9 s, the slowest of all); 13 of those 15 are 48 px or smaller; one (nousresearch) did not decode. |
| `manifest.json` icons | Only 3 of 25 (aion-labs, cohere, liquid) and 512 px; rare. |
| `og:image` | 15 of 25, 1200x630 jpg/png social cards. Not a logo (photographic or full-page art), useless for colour, do not use. |
| `theme-color` meta | Only 6 of 25, mostly white or near-black (z.ai `#F4F6F8`, cohere `#ffffff`, perplexity `#FCFCF9`); not a brand colour. |
| Cost | A site fetch is the slowest and least reliable step; only needed when the favicon service lacks the domain. |

### 2.6 Favicon services

| Service | Hits | Median | Size / format | Notes |
|---|---|---|---|---|
| `https://t0.gstatic.com/faviconV2?client=SOCIAL&type=FAVICON&fallback_opts=TYPE,SIZE,URL&url=https://<domain>&size=256` | 24 of 25 | 0.10 s (p95 0.18) | PNG; 6 are 48 px or smaller, others 144 to 256 | Real favicon of the site; 404 + 726-byte image for unknown domains (a clean miss, same SHA each time). Undocumented but it is the service OpenRouter's own pages use (256 px). No published rate limit; ~100 calls in a minute were never throttled. Terms: no stated terms for third-party use; treat as best effort. |
| `https://www.google.com/s2/favicons?domain=<d>&sz=128` | 23 of 25 | 0.7 s | PNG/JPG 128 | Redirects to the same service; **upscales** small icons (blurry). Prefer faviconV2. |
| `https://icons.duckduckgo.com/ip3/<d>.ico` | 20 of 25 | 0.10 s (p95 0.49) | ico/png, 13 of 20 are 48 px or smaller | Own crawl, often the older/smaller icon; clean 404 on unknown; undocumented, no terms. Good second opinion. |
| `https://icon.horse/icon/<d>` | 25 of 25 | 0.5 s, one 16.5 s stall | ico/png/svg | Never misses: 200 + generated letter tile for unknown domains. Unusable without a placeholder check. |
| `https://logo.clearbit.com/<d>` | 0 | n/a | n/a | Host no longer resolves. |
| `favicon.im`, `api.faviconkit.com` | n/a | n/a | n/a | favicon.im returns the same ico; faviconkit returned a 1x1 pixel. Not worth a dependency. |
| `cdn.brandfetch.io`, `img.logo.dev` | n/a | n/a | n/a | Require a key / client id (401). Excluded by "no API key". |

### 2.7 AI icon packs (static CDN files)

| Pack | Result |
|---|---|
| `@lobehub/icons` toc: `https://unpkg.com/@lobehub/icons@<ver>/es/toc.json` (find `<ver>` with `https://registry.npmjs.org/@lobehub/icons/latest`, 0.7 s) | 340 entries, 105 KB, 0.39 s, `Cache-Control: max-age=31536000` on a pinned version, MIT licensed code. Per entry: `id`, `title`, `fullTitle`, **`color`**, `desc` (homepage for all 23 matches), `group` (model/provider/application), flags. Matched 23 of 25 makers by normalised slug / display name / HF org (miss: bytedance-seed, thudm). Colour is chromatic for 12 (mistralai, qwen, arcee-ai, deepseek, nvidia, minimax, meta-llama, tencent, cohere `#39594D` dark green, perplexity, sakana, ibm-granite) and white/black/near-black ("mono") for 11 (inception, liquid, nousresearch, x-ai, google, openai, z-ai, xiaomi, moonshotai, anthropic `#F1F0E8`, aion-labs `#0f172a`). Two wrong homepages (qwen and ibm resolve to huggingface.co; minimax resolves to `.chat`). |
| `@lobehub/icons-static-svg` `icons/<id>.svg` and `<id>-color.svg` | 0.3 to 1.5 s, 24x24 viewBox; colour SVGs for 13 of 25, mono for 10. Clean vector marks (best for the app's logo tile) but SVG needs a renderer in Avalonia (Svg.Skia). |
| `@lobehub/icons-static-png` (jsDelivr) | Works (640 px) but one fetch took 7.7 s through jsDelivr; use unpkg. |
| Simple Icons `https://cdn.simpleicons.org/<slug>` | 12 of 25, SVG, 0.3 to 2 s, brand hex in the data. CC0, with a trademark disclaimer. Coverage of AI labs is thin (no arcee, aion, cohere, liquid, nousresearch, tencent, inception, thudm, ibm-granite). Optional tie-breaker only. |
| Licence note | Code/data MIT or CC0, but the marks remain trademarks of their owners. Reading a colour from them is harmless; showing the mark as the maker's logo in the app is common practice (OpenRouter and LobeChat do) and the owner has already approved logos on the palette canvas. |

---

## 3. Per-maker matrix

Cell = format and largest side in px (svg = vector, "-" = miss). "lobehub" is the toc brand colour ("mono" = white, black or near-black, no usable hue).

| maker | OR hosted icon (HTML, see 2.2) | HF avatar | faviconV2 256 | site icon (declared) | favicon.ico | DDG ip3 | Wikidata logo | lobehub toc colour | simple-icons |
|---|---|---|---|---|---|---|---|---|---|
| mistralai | png 191 | webp 200 | png 256 | - | ico 256 | ico 256 | png 330 | #FA520F | svg |
| qwen | png 200 | webp 200 | png 80 | png 80 | - | png 80 | png 330 | #615ced | svg |
| moonshotai | png 512 | webp 200 | png 28 | ico 28 | - | ico 28 | - | #16191E (mono) | svg |
| nousresearch | favicon url only | webp 200 | png 256 | png 48 | ico (undecodable) | png 150 | - | #fff (mono) | - |
| liquid | favicon url only | webp 200 | png 180 | png 180 | ico 48 | png 32 | - | #fff (mono) | - |
| arcee-ai | favicon url only | webp 200 | jpg 180 | png 180 | ico 48 | ico 48 | - | #008C8C | - |
| inception | svg | - (HF org differs) | png 180 | png 180 | - | png 64 | - | #fff (mono) | - |
| thudm | not on OR | webp 200 (Tsinghua seal) | - | - | - | - | - | - | - |
| anthropic | svg | webp 200 | png 256 | png 256 | ico 48 | png 32 | png 330 (wordmark) | #F1F0E8 (mono) | svg |
| openai | svg | webp 200 | png 180 | png 180 | ico 48 | ico 48 | png 330 (wordmark) | #000 (mono) | - |
| google | svg | webp 200 | png 144 | - | ico 32 | ico 32 | png 330 | #fff (mono) | svg |
| deepseek | png 200 | webp 200 | png 225 | ico 225 | ico 225 | ico 225 | png 330 | #4D6BFE | svg |
| x-ai | favicon url only | webp 200 | png 256 | png 180 | ico 48 | ico 48 | png 330 (wrong: SpaceX wordmark) | #fff (mono) | svg |
| z-ai | favicon url only | webp 200 | png 256 | svg | - | - | png 330 | #000 (mono) | svg |
| nvidia | favicon url only | webp 200 | png 48 | - | ico 48 | ico 48 | png 330 | #74B71B | svg |
| minimax | favicon url only | webp 200 | png 32 | ico 32 | ico 32 | ico 32 | png 330 (wrong entity) | #F23F5D | svg |
| meta-llama | png 400 | webp 200 | png 64 | ico 64 | - | ico 64 | - | #1d65c1 | svg |
| tencent | png 961 | webp 200 | png 32 | - | - | png 32 | png 330 | #0052D9 | - |
| aion-labs | favicon url only | webp 200 | png 180 | png 180 | - | - | - | #0f172a (mono) | - |
| cohere | png 280 | webp 200 (Cohere Labs star) | png 180 | png 180 | ico 48 | png 32 | png 330 | #39594D | - |
| bytedance-seed | favicon url only | webp 200 | png 48 | ico 48 | - | ico 48 | - | - | svg |
| xiaomi | favicon url only | webp 200 | png 196 | - | ico 32 | png 16 | png 330 | #000 (mono) | svg |
| perplexity | png 512 | - (HF org differs) | png 256 | png 180 | ico 48 | png 192 | png 330 | #22B8CD | svg |
| sakana | favicon url only | - (HF org differs) | png 48 | - | ico 48 | ico 48 | - | #E10600 | - |
| ibm-granite | svg | webp 200 | png 256 | - | ico 32 | png 32 | - | #0F62FE | - |

Real-brand-mark concerns found by eye: every `site icon`/`favicon` of moonshotai, openai, anthropic, x-ai is a black or white tile (the dominant-colour step must ignore the tile); ibm.com's favicon is a grey glyph, not the IBM mark; Wikidata logos are wordmarks and for 2 makers the wrong company; HF avatars are the real mark except cohere/thudm.

### Colour read from each logo (OD-0050 `dominant()` from derive.py, unchanged; `colours.py`)

Chromatic result agreeing across sources for: mistralai (OR icon `#FF4600`, HF `#FF4700`, toc `#FA520F`), deepseek (`#4D6BFE` in five sources), nvidia (`#74B700` / `#7BB900` / `#74B71B`), meta-llama (`#087FFF` / `#0B7CF8`), tencent (`#0053D9`), sakana (`#E10500`), arcee (`#0BCFCF` logo vs `#008C8C` toc), minimax (`#F9396B` vs `#F23F5D`), qwen (`#6252F1` OR icon and toc `#615ced`, but the favicon reads `#082DFF` blue). Sources disagree for cohere (favicon lilac `#D18EE2`, HF avatar blue `#4E6EE9`, toc dark green `#39594D`). Rule that follows: **toc colour first when it is chromatic, else the best logo's dominant, else "mono" which means the neutral free shade.** 16 of 25 gave a chromatic answer; the 9 mono ones are listed in verdict 7.

---

## 4. Slug to domain resolution (scored on 25 makers)

Single methods (correct / wrong / no answer, of 25):

| Method | Correct | Wrong | None | Comment |
|---|---|---|---|---|
| M3 HF org page website link | 17 | 1 (thudm gives `edu.cn`; fix with a public-suffix list) | 7 | Needs the HF org (from `hugging_face_id` or quicksearch). Excellent when present. |
| M2 OR `/providers` privacy/ToS host | 17 | 1 (cohere `cohere.ai`, a legacy alias that redirects) | 7 | One cached 26 KB call for all makers. Slug match needs alias rules (`x-ai` = `xai`, `meta-llama` = `meta`, strip `-ai`). |
| M6 lobehub toc `desc` | 21 | 2 (huggingface.co for qwen, ibm) | 2 | Highest coverage; reject aggregator hosts. |
| M4 Wikidata P856 | 13 | 1 (minimax.de) | 11 | Needs disambiguation; slowest to set up. |
| M3b HF `orgEmailDomains` | 10 | 0 | 15 | Precise but sparse. |
| M1 OR author-page favicon `url=` | 11 | 0 | 14 (the hosted-icon ones) | Scraping, see 2.2. |
| M5 URLs in OR model descriptions | 3 | 0 | 22 | Not useful. |
| M7 guess `<slug>.<tld>` with title check | 12 | 11 (squatters such as `liquid.com`, `inception.com`, `minimax.com`, `arceeai.com`) | 2 | Unsafe: it returned the wrong brand for 11 of 25. Do not use. |

**Recommended method: vote over M3, M2, M6, M4, M3b** after reducing every URL to its registrable domain (public-suffix aware) and discarding an aggregator list (`huggingface.co`, `github.com`, `x.com`, `twitter.com`, `linkedin.com`, `discord.com`, `youtube.com`). Result on the 25: **23 correct, 2 wrong (thudm, ibm-granite, both singletons from an aggregator); every domain with at least two agreeing votes (22 of 22) was correct.** So: accept a domain with two or more agreeing votes, or one vote from HF page/OR providers that is not on the denylist; otherwise declare "no domain" and rely on the HF avatar and the toc colour, or settle on the neutral shade. Aliases (`mi.com` vs `xiaomi.com`, `cohere.ai` vs `cohere.com`) are harmless for favicons because the services follow redirects.

Prefer the registrable domain (`sakana.ai`) over a subdomain taken from a privacy URL (`console.sakana.ai`, `chat.z.ai`, `www.tencentcloud.com`), then try `www.` if the favicon service misses.

---

## 5. Recommended fallback chain

**Amended by section 10.4 for OD-0065 (identical colours for every user): the priority order below must be walked, not raced; the parallel "best finished within 3 s" wording in step 4 is superseded.**

Per new maker slug (cache result forever, negative-cache failures with the back-off already in discovery.md: 1 h, 6 h, 24 h):

0. **Normalise the slug**: strip leading `~`, take the part before `/`, lowercase. Skip routers (`openrouter`, `stealth`). If it is a locked, alias or already learned maker, stop (0 ms).
1. **Identity from data already in memory** (no new request): display name (prefix of `name` before ": "), HF org (`hugging_face_id`), strength (`benchmarks.artificial_analysis`) from the model list the picker holds (OD-0049).
2. **Offline brand hints** (shipped snapshot of lobehub `toc.json`, refreshed in the background): match by normalised slug, slug without `-ai`, display name, HF org. Gives colour (if chromatic) and homepage immediately, before any network call. This is why most makers can have a real colour in under a second even before step 4 finishes.
3. **Domain** (parallel, one 1.5 s deadline): HF org page (resolve org via `hugging_face_id`, else `quicksearch` exact name) website link; OR `/providers` host (one cached call); toc `desc`; Wikidata P856 only if still undecided. Vote as in section 4.
4. **Logo bytes**, first usable by priority, each with a 1.5 s timeout, run in parallel and take the best finished by priority within the 3 s bound:
   1. faviconV2 on the domain (24 of 25, 0.10 s). Reject if smaller than 32 px, or if its source domain is an aggregator.
   2. HF org avatar (22 of 25, 200 px webp), via `overview` then `avatarUrl`; also covers makers with no domain; for users use `/api/users/<name>/overview`.
   3. Site `apple-touch-icon` / declared icon from the home page (17 of 25) and then `/favicon.ico`.
   4. DDG `ip3` (20 of 25).
   5. Wikidata P154 via `Special:FilePath?width=256` (last: wordmark, wrong-entity risk).
   Never: icon.horse, Clearbit, Google s2, og:image, theme-color.
5. **Colour**: toc colour if chromatic; else `dominant()` of the best logo (the prototype, unchanged; it already treats a plain tile as mono); if it returns mono (9 of 25), use the neutral free shade, which OD-0050 already defines; then nearest free shade against the colours in use. When the toc colour and the logo colour disagree strongly (cohere, qwen) prefer the toc colour (it is hand-curated brand colour).
6. **Offline or slow**: at 3 s settle on the neutral free shade and recolour when the lookup completes, as OD-0050 says. HTTP failures, 404, decode failures and bot walls are all "this source missed", never an error.
7. **Housekeeping**: monthly refresh of the toc snapshot; re-resolve a maker that stayed neutral when the app next sees a new model of it.

Measured end to end (`chain_timing.py`, steps 3 to 4 in two parallel stages, 25 makers): median 0.5 s, p95 1.2 s, max 2.0 s (liquid, a slow HF page).

---

## 6. Licence, terms and rate limits (summary)

| Source | Terms / licence | Rate limit | Risk |
|---|---|---|---|
| OpenRouter `/api/v1/models`, `/providers` | Documented public API | None published for keyless; cached 120 s | Low; already in OD-0049 |
| OpenRouter author pages and `/images/icons` | Terms (as summarised) forbid automated scraping of site information | n/a | Medium; not used by the chain |
| Hugging Face API and org pages | Public API | **500 requests per 300 s per IP**, in `RateLimit` header | Low (3 calls per new maker) |
| Wikidata / Commons | CC0 data; logos mostly PD text-logo or non-free; User-Agent with contact required | no hard limit; be gentle | Low if last fallback |
| lobehub icons (unpkg, jsDelivr) | MIT code; marks are trademarks of owners | CDN fair use; versioned files cache a year | Low |
| Simple Icons | CC0 plus trademark disclaimer | CDN | Low |
| Google faviconV2 / s2 | Undocumented, no stated terms | none observed in ~100 calls | Low to medium (could change); several fallbacks exist |
| DuckDuckGo ip3 | Undocumented | none observed | Low to medium |
| Maker sites | Public pages, one GET per maker | n/a | Low; respect robots by fetching only `/` and `/favicon.ico` |

Network inventory entries the port needs for this feature (OD-0064): `openrouter.ai` (models, providers), `huggingface.co` and `cdn-avatars.huggingface.co`, `t0.gstatic.com`, `registry.npmjs.org` and `unpkg.com`, optionally `www.wikidata.org`, `commons.wikimedia.org`, `icons.duckduckgo.com` and the maker's own domain. A fixed descriptive User-Agent should be sent.

---

## 7. Gotchas for the implementation

- **Image formats to decode:** webp (HF avatars), ico (containers with several sizes: take the largest), png, jpg, and SVG (lobehub packs, some OR/z.ai icons). Colour extraction does not need SVG rendering if the toc colour is used; for SVG logos read the `fill`/`stop-color` hexes.
- **Placeholders to reject:** the Hugging Face favicon (identical bytes, 43,889 B) served for makers without a site; the 726-byte faviconV2 404 image; icon.horse's generated tile; 1x1 pixels.
- **Tiles:** many favicons are an opaque black or white rounded square around the mark; the prototype's `dominant()` already classes them mono, which is right.
- **Slug aliasing:** `meta` and `meta-llama` are one maker, `bytedance` and `bytedance-seed` one maker, `z-ai` is Zhipu, `x-ai` is xAI (now "SpaceXAI" on OpenRouter). The app's locked palette already has these brands; the chain should only run for makers not yet known.
- **Strength ranking** (OD-0063) can use `benchmarks.artificial_analysis.intelligence_index` where present (255 of 464 models); that field is on the same `/models` call, so no extra request.
- **No DNS-guessing.** It is tempting to try `<slug>.com`; it picked the wrong site for 11 of 25.

## 8. Test seam fixtures (tests never touch the network)

Capture these as fixed responses behind the HTTP seam: an OpenRouter `/models` excerpt with a `name` prefix and `hugging_face_id`; `/providers` excerpt; HF `overview` JSON and an org HTML snippet with the website link; a 200x200 webp avatar, a 256 px png favicon, a 726-byte 404 image; a toc.json excerpt (chromatic and mono entries, an `#fff` entry); and failure cases (timeout, 404, 403 bot wall, undecodable ico). Real sample files for each shape are already in `samples/`.

## 9. Not tested, and caveats

- One machine, one network, one day: latency and hit rates are indicative. Run-to-run noise of 0.3 s was seen on the same URL.
- Parallel runs used 5 processes for the matrix (per-request ms there is inflated); the clean numbers in sections 2 and 5 are from sequential runs.
- "Real brand mark" and "chromatic" are my visual and computational judgement on 25 makers, not an external truth.
- The OpenRouter terms summary came from a fetch of `openrouter.ai/terms`; the owner should have the clause read before any HTML scraping is considered.

---

## 10. Stability and determinism of every source (OD-0065)

Added after the owner's ruling that every user must get the same colours for a given new model or maker. Question asked of each source: does it return the byte-identical answer to every client, and over days, and can the answer be pinned? Scripts and raw results: `stability/` (`s1_openrouter.py`, `s2_sources.py`, `s3_disc.py`, `s4_stab.py`, `s5.py`, `s7.py`, `s8.py`, `cmp2.py`, plus the `*.json` outputs; they expect `models.json` and `prov.json` from the OpenRouter endpoints in the same folder).

### 10.1 What was measured

| Test | Method | Scale |
|---|---|---|
| Client variation | Same URL fetched under 3 client profiles that differ in User-Agent, Accept-Language and Accept: A = `Marrow/1.0`, en-US, `image/*`; B = Chrome UA, hi-IN, `image/avif,image/webp`; C = curl UA, de-DE, `*/*`. Compare SHA-256 | every image hit of the 25 makers, all sources |
| Accept negotiation | 6 Accept variants (none, `*/*`, png, webp, avif, svg) | 8 sources |
| Repeat and CDN shard | 5 repeats; Google faviconV2 on all four shards `t0`..`t3` | 25 makers (shards), 4 sources (repeat) |
| Over time | The 24 logos the prototype fetched at 07:48 today (`scratchpad/logos/*.png`) compared with a fresh fetch of the same source about 8.5 h later | 24 logos |
| Package drift | `@lobehub/icons` `es/toc.json` over the 8 latest versions; npm publish dates | 340 entries |
| Live data drift | `/api/v1/models` fetched twice 30 min apart | 464 models |
| Page discovery | Maker home page under the 3 profiles: do the declared icon URLs, og:image and final URL agree? | 25 makers |

Not testable here (one machine, one network, one day): geography (regional edges and redirects), A/B experiments over weeks, anything beyond 8.5 h. Where a source is a CDN or search engine whose behaviour by region is unknown, it is flagged below rather than assumed identical.

### 10.2 Scoreboard

"ABC" = byte-identical under all 3 client profiles. "Pin" = what can be fixed in the request or recorded to make the answer repeatable.

| Source | ABC identical | Differs between two users at the same moment? | Over time | Pin | Verdict |
|---|---|---|---|---|---|
| lobehub `es/toc.json` and `icons-static-svg` at an exact version, unpkg or jsDelivr | 3 of 3 (unpkg and jsDelivr give the same SHA `3f452ff4...`) | No | Immutable (`max-age=31536000`; jsDelivr adds `immutable`). **`@latest` is not stable**: it is a moving tag, 11 releases in 30 days (5.22.0 on 2026-10-01), and 18 brands (incl. Sakana, Perceptron, Dots Studio) appeared between 5.18 and 5.19 | the version in the URL | **Same for everyone, only with an exact version.** Across the last 8 versions no existing brand's colour changed; only additions did |
| Hugging Face org avatar, via the `avatarUrl` the overview returns | 22 of 22 (always webp, whatever the Accept header; 6 of 6 Accept variants) | No (CloudFront, `max-age` 1 year, Age 133 days seen) | The URL is content-addressed (`.../uploads/<org-id>/<file-id>.png`): same URL, same bytes. The *overview* JSON is live (follower counts change; `avatarUrl` changes when the org re-uploads) | record `avatarUrl` plus SHA-256 | **Same for everyone for a given `avatarUrl`.** Resolving `avatarUrl` is a live read, so two users could see different ones only in the minutes after an org changes its avatar |
| HF org page website link and `orgEmailDomains` | 5 of 5 orgs same link under 3 profiles | No | Live page, changes when the org edits its profile | none | Same at a given time; slow-moving |
| OpenRouter `/api/v1/models`, `/providers` | 3 of 3 (same SHA, CDN cache 120 s / 300 s) | No at one moment | **Live**: new models arrive; 30 min apart the list had the same 464 ids and identical `intelligence_index` values but differed by 177 bytes | none | Same at one moment, never the same over time: any rule that reads a maker's current model list or benchmark values (strength rank, intensity) changes when the list changes |
| Google faviconV2 (`t0.gstatic.com`, size 256) | 24 of 24 | **Yes, rarely: the four shards `t0`..`t3` are separate caches and can disagree.** 1 of 25 makers (nousresearch.com) returned different bytes by shard (256 px on `t0`, 48 px on `t1..t3`; at size 128: 4,416 B on `t0/t1`, 3,859 B on `t2/t3`). The other 24 were byte-identical on all four shards | Cache `max-age=604800` (7 days), follows the site's own favicon. After 8.5 h, 23 of 24 prototype logos were byte-identical; the 24th is the shard case | host `t0`, size 256, fixed `fallback_opts` | **Mostly same, with a known divergence window**: pin one host and size. In the nousresearch case the derived colour was the same (mono) on every shard, but only because the logo is monochrome |
| `www.google.com/s2/favicons` | 24 of 24 | **Yes**: it redirects to a random shard (`t0..t3`) per request; 12 repeats of nousresearch split 7 / 5 between two byte sets | as above | none | Do not use; same data as faviconV2 minus the ability to pick a shard |
| DuckDuckGo `ip3` | 22 of 22 (also 6 of 6 Accept variants, 5 of 5 repeats) | Not observed | `max-age` 30 days, has an ETag; no multi-hour test | ETag | Same in all tests; one backend as far as seen |
| OpenRouter hosted icons `/images/icons/<Name>.png` or `.svg` | 13 of 13 | **Yes for new assets**: `Apodex.svg` (added 2026-10-01) answered 200 on about 65 % of requests and a 36 KB 404 page on about 35 % (14 consecutive requests, 3 UAs, same minute); `Mistral.png` (added 2026-09-14) answered 200 on 14 of 14. A 404 here is not a miss | ETag, `Last-Modified`, 1-day cache; `Vary: User-Agent` | ETag | Same bytes whenever it answers; availability is not deterministic for newer assets. Already excluded from the chain for terms reasons |
| Maker's own `favicon.ico`, declared icon, apple-touch-icon | favicon.ico 15 of 15, declared icon 18 of 19, apple-touch 12 of 13 | **Yes**: image CDNs (Framer, Sanity) negotiate format by Accept: `image/avif` for Chrome, `image/png` for curl, so the bytes differ (inception, arcee-ai, cohere). Bot walls answer by User-Agent (perplexity.ai 403 to the curl UA, tencent.com and ibm.com time out). Home page redirects by language (zhipuai.cn `/en` vs `/zh`, cohere `/de`; the icon URLs themselves did not change). inception declares a separate dark-mode icon (`media=`) | Changes whenever the maker edits its site | none | **Not same for everyone.** Keep as a late fallback only, with Accept pinned to `image/png` |
| `og:image` | 15 of 18 | **Yes** (AVIF vs PNG negotiation, 3 makers) | any time | none | Excluded already |
| Wikimedia `Special:FilePath?width=256` | **5 of 16** | **Yes, by Accept**: a client sending `image/webp` gets a WebP thumbnail, everyone else a PNG, both rendered from the same SVG (Mistral: PNG 1,035 B vs WebP 362 B) | The Commons file can be re-uploaded (`imageinfo` returns `sha1` and `timestamp`); Wikidata entities are editable by anyone | send `Accept: image/png`; record the Commons `sha1` | Same only with Accept pinned; entity choice is a heuristic |
| icon.horse | 25 of 25 | not observed | n/a | none | Excluded already (generated tiles) |
| Simple Icons: `cdn.simpleicons.org/<slug>` | 14 of 14 | No | **Floating**: serves the current release (`Age` 65 days seen); a version-pinned `npm/simple-icons@<ver>` URL is immutable | version | Use only the pinned form, and only as a tie-breaker |

Domain voting inputs (section 4) inherit these properties: HF page links and `/providers` are live but stable over the minutes tested; lobehub `desc` is pinned with the toc version.

### 10.3 Colour derivation is not the weak link, its inputs are

For nousresearch, the three different byte sets (4,416 B, 3,859 B, 8,136 B) all derive to mono with the prototype `dominant()`; the mono branch of `derive.py` ignores the pixels (it uses fixed L 0.56, C 0.045, h 250), so the neutral shade is identical. For chromatic logos two byte sets that are visually the same mark can still give a hue that differs by a degree or two, and a hue that sits near a 15 degree bin edge (`dominant()` bins in 24 steps) can flip bins. **Quantise the result before use** (hue to 5 degrees, lightness and chroma to 0.02) and prefer a source that is byte-identical for everyone.

### 10.4 Recommended pins and amendments to the chain in section 5

1. **Prefer the pinned, immutable source for the colour itself.** The lobehub toc colour (step 2 of the chain) is the only colour source that is byte-identical for every user. Pin its version in the app build (`unpkg.com/@lobehub/icons@<exact>/es/toc.json`), never `@latest`. All users on one app build then get the same colour for every brand the snapshot lists. The 12 chromatic makers of section 3 are decided at this step with no network at all (the shipped snapshot).
2. **The snapshot version is part of the palette's definition.** A maker that is in toc 5.19 but not in 5.18 (Sakana, Perceptron, Dots Studio were added on 2026-09-21) takes a different chain branch, hence potentially a different colour, depending on the version. Treat "toc version" as an input recorded next to each saved palette, bump it only with an app release, and when it is bumped re-derive deterministically from the same rule (a maker already coloured from logo pixels keeps its colour, or all users migrate together).
3. **Do not race sources and keep the first finisher.** Section 5 step 4 says to run the logo sources in parallel and take "the best finished by priority within the 3 s bound". That outcome depends on each user's latency: a slow network can miss the priority-1 source and lock a priority-3 image, i.e. a different colour. Required instead: walk the priority order; each source returns a definitive HIT, a definitive MISS (a clean 404, or a decoded-and-rejected placeholder) or a TRANSIENT failure (timeout, 5xx, 403/429, undecodable). A transient failure on any higher-priority source means "not decided yet": show the neutral shade at 3 s, retry later, and lock only when every higher-priority source has answered HIT or MISS. This is OD-0065's "never locks a fallback from a different source".
4. **Treat a 404 from a CDN that flaps as transient.** OpenRouter's newer static assets returned 404 about a third of the time (see the scoreboard). For those, require two further 404s a minute apart before calling it a miss. HF `overview` 404, faviconV2 404 (726-byte image) and DDG 404 were stable misses in every test.
5. **Pin the request**: one User-Agent string, `Accept: image/png,image/*;q=0.5` (stops AVIF/WebP negotiation by Sanity, Framer and Wikimedia), no `Accept-Language` dependence, faviconV2 only on host `t0.gstatic.com` with `size=256` and `fallback_opts=TYPE,SIZE,URL` (never `www.google.com/s2`, which randomises the shard).
6. **Record provenance with each saved palette**: source name, final URL, SHA-256 of the image bytes, toc version, derivation-algorithm version. Two machines that disagree can then be diffed in one line; a stored palette is never recomputed from a fresh fetch.
7. **Make domain voting order-free**: apply the vote to the sorted set of candidate registrable domains with a fixed tie-break (HF page, then `/providers`, then toc `desc`, then Wikidata), not first-arrival.
8. **Do not feed live model data into a saved result.** The model list changes minute to minute (new models, benchmark indexes). The maker's palette must not depend on it; intensity (OD-0063) should use frozen inputs (the model's own published numbers captured when first seen, or a versioned table), not "rank among the maker's models today".
9. **Strongest option for the "10 people" case, a design call for the owner**: bundle (or serve from the app's own release feed) a versioned `makers.json` of already-resolved palettes, produced once by the maintainer by running this same chain and checked in with the provenance fields above. Every user then gets identical colours for every maker in the file, whatever their network does; the on-device lookup only handles makers newer than the file and, for those, relies on 3 to 8 above. The one-off real-network run across 10 data folders that OD-0065 asks for should run all 10 instances concurrently with the pinned request profile and compare the recorded provenance, not only the final colours.

### 10.5 What this changes in the recommendation

- Section 1 verdict stands (the chain is quick and covers 24 of 25 makers), with one correction: the `faviconV2` strength "median 0.10 s" is real, but it is a *mostly*-identical source (shard divergence, 1 of 25), so it must be pinned as in 10.4 item 5 and ranked below the version-pinned toc colour and the content-addressed HF avatar for the purpose of locking a colour.
- Sources ranked by "same for everyone" (best first): version-pinned lobehub toc and SVGs; HF avatar at its content-addressed URL; faviconV2 on `t0` at size 256; DuckDuckGo `ip3`; the maker's own site with Accept pinned; Wikimedia with Accept pinned. Never: `www.google.com/s2`, `og:image`, icon.horse, floating `@latest` or `cdn.simpleicons.org`, OpenRouter hosted icons as the only evidence.
- The only A/B or dark-mode risk seen is on the maker's own page (inception's `media=` icon), which a pinned parser avoids by skipping `<link ... media=...>`.
