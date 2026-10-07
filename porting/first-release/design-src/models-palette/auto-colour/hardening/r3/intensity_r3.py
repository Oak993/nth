"""OD-0063 intensity rule IR-3 (synthesis, draft for the owner's canvas; IR-1 hardened twice for OD-0065/OD-0066).

IR-3 changes against IR-2 (adversarial round 2; ids as in PLAN.md section 14):
  * R2-OD66-A  the legacy cap is measured against the newest member of the SAME OR A HIGHER class only, so a weaker
               newcomer released 18+ months after the flagship never makes the flagship legacy (class-first kept).
  * R2-OD66-B  a dated successor (a -2512 / -2025-07-28 row more than SPLIT_DAYS newer than its base row) is its own
               member (same class and version as the base, its own day), so Mistral Large 3 re-spaces Mistral.
  * R2-OD66-C  a speed variant with no base row ranks just below its base's class (tier - SPEED_DOWN), whatever up-words
               it carries; 'highspeed' and 'ultraspeed' join 'fast' on the speed ladder.
  * R2-OD66-D / R2-2  withdrawn rows stay in the lineup as tombstones with their last facts (`tombstones=`); virtual
               placement never takes price or created from a provider list: an unknown model inherits the facts of its
               (class, version) peers, so it can never outrank them; harness spellings (claude-4.5-opus-high-thinking,
               grok-4.7-high, opus-4.5) are canonicalised to the catalogue member before placement (HARNESS_REWRITES).
  * R2-OD66-E  visible-level steps go to family-word boundaries (Astra | Sol | Terra | Luna) before generic class
               boundaries and before modifier (pro/max) boundaries; the flagship-alone step is dropped when the flagship
               differs from the next group only by a modifier.
  * R2-OD66-F  a plain-named next generation (gpt-7, with no family word and no down-word, sharing the family's stem,
               major version above every family member's) ranks above the family.
  * R2-3       effort synonyms (extra-high, extra_high, 'extra high' -> xhigh; ultra -> max) map before the ladder, so no
               word any provider emits today falls to UNKNOWN_WORD_E.
  * R2-9       eligibility stays 'text chat' (no tools test); PLAN 4.4 now says the same, so goldens and rule agree.

IR-2 changes against IR-1 (each closes an adversarial finding; ids as in PLAN.md "Hardening"):
  * F5      effort e comes from ONE global word ladder (off 0 .. max 1), never from the model's own list, so the same
            (model, effort word) has the same shade through every provider. The list only decides which stops show.
  * OD66-1  a speed variant (-fast, -highspeed, -ultraspeed) collapses onto its base only if the base row exists in
            the same lineup; otherwise it is its own member and its speed word ranks it (fast = 3).
  * OD66-2  down-words (mini, nano, lite ...) apply inside a family ladder too: X-mini < X for every family word.
  * OD66-3  the OpenAI family ladder sits on the generic scale (Luna above plain, Sol above pro); a lineup-relative
            legacy cap puts members released more than 18 months before the maker's newest below every current one.
  * OD66-4  per-palette visible levels: strength positions are spaced by measured perceptual distance, the number of
            levels is capped so adjacent levels differ by >= 0.02 OKLab at e = 1, and when a lineup has more groups
            than levels, steps go to class boundaries first, then to each class's head (merging from the bottom).
  * OD66-5 / F9  collapse is order-free: the base row wins, then the earliest created, then the id (ordinal).
  * OD66-6  a model missing from the lineup (CLI-only, withdrawn) is placed virtually with the same precedence.
  * OD66-7  with more groups than levels the scale is top-anchored, so a weaker release moves only the bottom.
  * OD66-8  aliases skip modifier siblings they do not name and prefer an exact price match.
  * OD66-9  members with equal (class, version, day, price) form one group with one shade.
  * OD66-10 'muse' is a Meta class word (Muse above Llama whatever the version numbers).
  * OD66-11 the never-mud lift, the never-neon clamp and the dark-keep chroma floor are continuous (bisection;
            0.01 L fades), so a tiny change of s or e never jumps a visible step.
  The colour maths (apart from the continuity fixes), the anchor and the motion dials are unchanged.
  Measured limits the owner should see on the canvas (W1), printed under metrics.hardening:
    * visible strength levels per palette at max effort (>= 0.02 OKLab apart): 6-9 for chromatic palettes, 4 for
      Silver, so OpenAI's lineup shows 4 levels (IR-3: Astra and Astra Pro; the Sols; Terra with the generic Pros;
      Luna and everything below);
    * a 6-step global effort ladder gives adjacent effort steps of about 0.009 (median palette minimum), below H3's
      0.012; motion also changes with effort.

One rule, taken from the three proposals (perceptual, material "pigment and energy", brand-anchored BA-1):

  * Palette = the MAKER that created the model (OpenRouter vendor prefix, seed maker), never the router/harness.
  * Model strength s in [0,1] sets DEPTH (ink concentration: lightness first, chroma with it).
  * Effort e in [0,1] sets ENERGY (chroma at held lightness, light-contrast between the stops, and all motion).
    The two cues move in near-orthogonal directions of OKLab, so a diagonal neighbour (stronger model one stop
    lower) does not cancel out.
  * Hue never moves. Nothing is ever both lighter AND more chromatic than the locked colour (never neon); a stop
    darker than its locked self keeps at least 75 % of its chroma, and where the sRGB gamut would cut it below 85 %
    of what was asked it gets lightness back instead (never mud).
  * ANCHOR (owner choice, see ANCHOR_S): with the recommended centre anchor the locked LiquidStopsHex (or a derived
    maker's canvas stops) show EXACTLY for a model at s = 0.5 (a one-model maker; the middle of the perceptual
    strength scale) at max effort (e = 1 on the global ladder), with exactly the locked v22 motion. Stronger models go deeper/richer than the lock (the
    "vicious" end, bounded), weaker ones thinner. With ANCHOR_S = 1.0 (the ceiling alternative) the flagship at
    its top stop shows the lock and everything else is a softening of it.

Inputs: builtin-models.json, openrouter-models.json (+ openrouter-catalogue-snapshot.json: reasoning support and
`created` from the same fetch), the locked stops (EffortSlider.cs LiquidStopsHex) and the derived canvas stops (palette.json), embedded
below so the script is self-contained. Output: intensity-table.json.

Run:  python intensity.py            (writes intensity-table.json, prints the metrics)
      python intensity.py --swatch   (also writes intensity-swatch.png, a grid for eyeballing)
"""
from __future__ import annotations

import json
import math
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
RAW_OR = os.path.join(HERE, "openrouter-catalogue-snapshot.json")  # id -> created, reasoning support
RULE_VERSION = "IR-3"

# --------------------------------------------------------------------------------------------- colour math (OKLab)

def _s2l(c):
    c = c / 255
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4

def _l2s(c):
    return 12.92 * c if c <= 0.0031308 else 1.055 * c ** (1 / 2.4) - 0.055

def unhex(h):
    return tuple(int(h[i:i + 2], 16) for i in (1, 3, 5))

def hexc(rgb):
    return "#%02X%02X%02X" % rgb

def to_oklab(rgb):
    r, g, b = (_s2l(v) for v in rgb)
    l = 0.4122214708 * r + 0.5363325363 * g + 0.0514459929 * b
    m = 0.2119034982 * r + 0.6806995451 * g + 0.1073969566 * b
    s = 0.0883024619 * r + 0.2817188376 * g + 0.6299787005 * b
    l, m, s = (math.copysign(abs(v) ** (1 / 3), v) for v in (l, m, s))
    return (0.2104542553 * l + 0.7936177850 * m - 0.0040720468 * s,
            1.9779984951 * l - 2.4285922050 * m + 0.4505937099 * s,
            0.0259040371 * l + 0.7827717662 * m - 0.8086757660 * s)

def _lab_to_lin(L, a, b):
    l = (L + 0.3963377774 * a + 0.2158037573 * b) ** 3
    m = (L - 0.1055613458 * a - 0.0638541728 * b) ** 3
    s = (L - 0.0894841775 * a - 1.2914855480 * b) ** 3
    return (4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s,
            -1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s,
            -0.0041960863 * l - 0.7034186147 * m + 1.7076147010 * s)

def lch(hx):
    L, a, b = to_oklab(unhex(hx))
    return L, math.hypot(a, b), math.degrees(math.atan2(b, a)) % 360

def _in_gamut(L, C, h):
    a, b = C * math.cos(math.radians(h)), C * math.sin(math.radians(h))
    return all(-1e-4 <= v <= 1 + 1e-4 for v in _lab_to_lin(L, a, b))

def cmax(L, h):
    lo, hi = 0.0, 0.45
    for _ in range(32):
        mid = (lo + hi) / 2
        lo, hi = (mid, hi) if _in_gamut(L, mid, h) else (lo, mid)
    return lo

def from_lch(L, C, h):
    """Gamut map by lowering chroma at constant L and hue (the hue never moves). Returns (hex, C actually used)."""
    L = max(0.0, min(1.0, L))
    if not _in_gamut(L, C, h):
        C = cmax(L, h)
    a, b = C * math.cos(math.radians(h)), C * math.sin(math.radians(h))
    rgb = tuple(round(255 * max(0.0, min(1.0, _l2s(max(0.0, min(1.0, v)))))) for v in _lab_to_lin(L, a, b))
    return hexc(rgb), C

def dE(a, b):
    """Mean OKLab distance over the three stops (the measure all three proposals used; JND ~ 0.02)."""
    return sum(math.dist(to_oklab(unhex(x)), to_oklab(unhex(y))) for x, y in zip(a, b)) / len(a)

# --------------------------------------------------------------------------------------------- palettes

# OD-0047/0051/0052 locked (EffortSlider.cs LiquidStopsHex), dark, heart, light. Purple is withdrawn (OD-0063).
LOCKED = {
    "Silver": ["#FFFFFF", "#EEF0F5", "#D9DBE3"],
    "Claude": ["#B85A38", "#CC6B47", "#E0875F"],
    "Deepseek": ["#2A45E0", "#3D5CFA", "#6A82FF"],
    "Meta": ["#0054C4", "#0070EC", "#2B95FF"],
    "Antigravity": ["#4285F5", "#9C73CC", "#D96670"],
    "Cursor": ["#D93F00", "#F54E00", "#FF8040"],
    "Grok": ["#030304", "#121218", "#26262E"],
    "Opencode": ["#168A3A", "#25B34F", "#45DC6C"],
    "OpencodeGo": ["#007EA3", "#00A4CD", "#3CCBEC"],
    "Zai": ["#0A1630", "#14306E", "#2F6BFF"],
    "Devin": ["#239377", "#3CBC9B", "#6FDCBF"],
    "Copilot": ["#8534F3", "#D25AB4", "#F4A876"],
    "Commandcode": ["#24157E", "#2E1B9C", "#6145DC"],
    "Pi": ["#A30C25", "#C8102E", "#EC5560"],
}
# Locked title colours (EffortSlider.cs TitleColor / LiquidTitles). Silver's title is the theme's primary text.
TITLES = {
    "Claude": "#ED9470", "Deepseek": "#7D94FF", "Meta": "#4FA3FF", "Antigravity": "#9C73CC",
    "Cursor": "#FF8A4C", "Grok": "#D4D6DE", "Opencode": "#5BE07F", "OpencodeGo": "#4FD0EE", "Zai": "#6E98FF",
    "Devin": "#7FE3C9", "Copilot": "#C898FD", "Commandcode": "#A98BFF", "Pi": "#FF6B7A",
}
# OD-0050/0061 palette canvas, derived makers (palette.json "auto" rows). The OpenRouter lime is withdrawn (OD-0063).
DERIVED = {
    "mistral": ["#C10058", "#D91E6C", "#F3598B"],
    "qwenlm": ["#4900C6", "#592EDC", "#715DF0"],
    "moonshot": ["#4D637A", "#61778E", "#8095AA"],
    "cohere": ["#BA77CB", "#D08DE1", "#EDB1FC"],
    "nousresearch": ["#5C6547", "#70795B", "#8E967B"],
    "perplexity": ["#7B5266", "#90667A", "#AC8597"],
    "microsoft": ["#EE0041", "#FF2857", "#FF687C"],
    "aws": ["#637A92", "#788FA7", "#99ADC3"],
    "minimax": ["#E32586", "#FC479B", "#FF78BA"],
    "ai21": ["#B4008A", "#CB269F", "#E65BBA"],
    "inflection": ["#374C63", "#4A6076", "#697D91"],
    "liquid": ["#886A5A", "#9D7F6F", "#BA9E8F"],
    "baidu": ["#001EC4", "#083EDA", "#3067EE"],
    "tencent": ["#2F3FAF", "#4056C5", "#5E78DB"],
    "arcee": ["#00BABF", "#08D1D5", "#6AEFF2"],
    # NVIDIA, OD-0063 feedback: darker, more premium, more intense green (replaces #74A100 #8AB700 #ABD64C).
    "nvidia": ["#54800C", "#699510", "#87B516"],
}
NVIDIA_OLD = ["#74A100", "#8AB700", "#ABD64C"]

def neutral_stops():
    """The pending neutral (PLAN 5.1 mono start: L 0.56, C 0.045, h 250) as tube stops (PLAN 5.4 offsets). IR-2 ranks
    a maker that has no palette yet against this palette's levels; it re-spaces (with a glide) when the palette lands."""
    L, C, h = 0.56, 0.045, 250.0
    return [from_lch(L - 0.07, C * 1.02, h)[0], from_lch(L, C, h)[0], from_lch(min(0.88, L + 0.10), C * 0.88, h)[0]]

def derive_nvidia(L_heart=0.615, hue=128.0, gamut_share=0.97):
    """Reproduces DERIVED['nvidia']: heart lowered from OKLCH L 0.72 to 0.615 (hue 125 -> 128, a step from lime toward
    true green), each stop at 97 % of the sRGB chroma limit for its lightness, the old stops' lightness offsets kept."""
    old = [lch(h) for h in NVIDIA_OLD]
    out = []
    for i, dl in enumerate((old[0][0] - old[1][0], 0.0, old[2][0] - old[1][0])):
        L = L_heart + dl
        h = hue + (2, 0, -2)[i]
        out.append(from_lch(L, gamut_share * cmax(L, h), h)[0])
    return out

# maker (OpenRouter vendor / seed maker) -> palette key
MAKER_PALETTE = {
    "openai": "Silver", "anthropic": "Claude", "google": "Antigravity", "meta": "Meta", "meta-llama": "Meta",
    "deepseek": "Deepseek", "x-ai": "Grok", "z-ai": "Zai", "mistralai": "mistral", "qwen": "qwenlm",
    "moonshotai": "moonshot", "cohere": "cohere", "nousresearch": "nousresearch", "perplexity": "perplexity",
    "microsoft": "microsoft", "amazon": "aws", "minimax": "minimax", "ai21": "ai21", "inflection": "inflection",
    "liquid": "liquid", "baidu": "baidu", "tencent": "tencent", "arcee-ai": "arcee", "nvidia": "nvidia",
}
SEED_MAKER = {
    "Anthropic": "anthropic", "Google": "google", "OpenAI": "openai", "DeepSeek": "deepseek", "Meta": "meta",
    "Z.ai": "z-ai", "Moonshot (Kimi)": "moonshotai", "Qwen": "qwen", "MiniMax": "minimax", "xAI": "x-ai",
    "Tencent": "tencent", "Xiaomi": "xiaomi", "StepFun": "stepfun", "Thinking Machines": "thinkingmachines",
    "Sakana": "sakana", "Meituan": "meituan", "Poolside": "poolside", "InclusionAI": "inclusionai", "NVIDIA": "nvidia",
}
LINEUP_KEY = {"meta-llama": "meta"}  # one maker, one lineup (Muse and Llama are both Meta's)

def palette_for(maker):
    key = MAKER_PALETTE.get(maker)
    if key is None:
        return None, None
    return key, (LOCKED.get(key) or DERIVED.get(key))

# --------------------------------------------------------------------------------------------- the colour rule

ANCHOR_S = 0.5          # recommended centre anchor; 1.0 = ceiling alternative (flagship at top = lock)

# per stop (dark, heart, light)
DEEP_L = (0.085, 0.065, 0.035)   # stronger than the anchor: denser ink, deeper (dark stop most)
DEEP_C = 0.12                    #   ... and up to 12 % richer (gamut permitting)
THIN_L = (0.12, 0.095, 0.06)     # weaker than the anchor: thinner ink, lighter
THIN_C = 0.12                    #   ... and up to 12 % less chroma
CALM_C = 0.40                    # lower effort: chroma calms at held lightness (energy)
CALM_L = (0.05, 0.045, 0.03)     #   ... with a touch of air so a calm deep shade never greys
CALM_SPREAD = 0.55               # lower effort: outer stops' lightness pulled toward the heart (flatter, resting)
DEEP_SPREAD = 0.15               # stronger model: a little more lightness contrast (polished)
GRADIENT_SPREAD_FLOOR = 0.75     # gradient brands (hue span > 60 deg) keep three distinct hues at low effort
C_FLOOR = 0.55                   # chroma never below 55 % of the locked stop
DARK_KEEP_C = 0.75               # a stop darker than its locked self keeps >= 75 % of its chroma (never mud)
L_MAX = 0.94
DARK_FADE_L = 0.01               # IR-2: the dark-keep chroma floor phases in over this much darkening
NEON_FADE_L = 0.01               # IR-2: lighter-and-richer chroma fades to the lock's over this much lightness
MUD_KEEP = 0.85                  # never mud: a darkened stop keeps >= 85 % of the chroma asked (gamut permitting)
MUD_BISECT = 30                  # IR-2: bisection steps for the never-mud lift (fixed count: identical on every machine)
# near-colourless brands (Silver light pole, Grok dark pole)
ACH_DEPTH_L = 0.08                # stronger: toward the pole (whiter white, blacker black), asymptotic
ACH_THIN_L = 0.05                 # weaker: away from the pole (satin pewter, smoky graphite)
ACH_CALM_L = 0.035
ACH_DEEP_SPREAD = 0.15
ACH_CALM_SPREAD = 0.75
ACH_CALM_TINT = 0.60             # calm: the cool steel/blue cast fades to satin neutral
ACH_DEEP_TINT = 0.25

def _lam(Lh):
    """Dark-hearted brands (Z.ai navy, Command Code indigo) are diluted toward white more gently, so navy stays navy."""
    return max(0.55, min(1.0, (Lh - 0.25) / 0.30))

def uv(s, e, anchor_s=None):
    a = ANCHOR_S if anchor_s is None else anchor_s
    if s >= a:
        u = 0.0 if a >= 1 else (s - a) / (1 - a)
    else:
        u = -(a - s) / a if a > 0 else 0.0
    return u, 1.0 - e

def shade(stops, s, e, anchor_s=None):
    """(locked stops, strength s, effort e) -> the three stops of this (model, effort)."""
    u, v = uv(s, e, anchor_s)
    base = [lch(h) for h in stops]
    Lh, Ch = base[1][0], base[1][1]
    if Ch < 0.03:
        return _shade_achromatic(base, u, v)
    hues = [b[2] for b in base]
    span = max(abs(((hues[i] - hues[j] + 180) % 360) - 180) for i in range(3) for j in range(3))
    lam = _lam(Lh)
    Ls, Cs = [], []
    for i, (L, C, h) in enumerate(base):
        if u >= 0:
            dL, kC = -DEEP_L[i] * u, 1 + DEEP_C * u
        else:
            dL, kC = THIN_L[i] * -u * lam, 1 - THIN_C * -u
        kC *= 1 - CALM_C * v
        dL += CALM_L[i] * v * lam
        kC = max(C_FLOOR, kC)
        if dL < 0:
            # IR-2 (OD66-11): the 75 % floor phases in over the first DARK_FADE_L of darkening (IR-1 switched it on at
            # once, a chroma jump of up to 15 % as a stop crossed its locked lightness)
            kC = max(kC, kC + (DARK_KEEP_C - kC) * min(1.0, -dL / DARK_FADE_L))
        Ls.append(L + dL)
        Cs.append(C * kC)
    spread = (1 - CALM_SPREAD * v) * (1 + DEEP_SPREAD * u)
    if span > 60:
        spread = max(spread, GRADIENT_SPREAD_FLOOR + (1 - GRADIENT_SPREAD_FLOOR) * (1 - v))
    for i in (0, 2):
        Ls[i] = Ls[1] + (Ls[i] - Ls[1]) * spread
    out = []
    for i, (L, C, h) in enumerate(base):
        Ln = max(0.04, min(L_MAX if L < L_MAX else L, Ls[i]))
        Cn = Cs[i]
        if Ln > L and Cn > C:
            # never neon: lighter AND richer than the lock is not allowed. IR-2 (OD66-11): the extra chroma fades out
            # over the first NEON_FADE_L of lightness above the lock instead of being cut at once (continuous in s, e).
            Cn = C + (Cn - C) * max(0.0, 1.0 - (Ln - L) / NEON_FADE_L)
        hx, Cg = from_lch(Ln, Cn, h)
        # never mud: if the gamut cut the chroma of a darkened stop below 85 % of what was asked, give back light.
        # IR-2 (OD66-11): the smallest lightness that keeps 85 % is found by bisection, not in 0.005 steps, so a tiny
        # change of s or e can never jump a whole step.
        target = MUD_KEEP * min(Cn, C)
        if Ln < L and Cg < target:
            lo, hi = Ln, min(L, L_MAX)
            for _ in range(MUD_BISECT):
                mid = (lo + hi) / 2
                if min(Cn, cmax(mid, h)) >= target:
                    hi = mid
                else:
                    lo = mid
            Ln = hi
            hx, Cg = from_lch(Ln, Cn, h)
        out.append(hx)
    return out

def _shade_achromatic(base, u, v):
    """Silver (light pole) and Grok (dark pole): intensity is depth and polish, never added colour.
    Model strength moves the heart toward its pole (whiter white, blacker black) and polishes the contrast a little;
    effort mainly sets the contrast between the stops (flat and satin when calm, polished when energetic), plus a
    touch of light. The heart approaches its pole asymptotically, so the strongest heart is never pure white/black
    (an outer stop may reach it, which only widens the gloss)."""
    Lh = base[1][0]
    p = 1 if Lh > 0.5 else -1
    pole = 1.0 if p > 0 else 0.0
    d = abs(pole - Lh)
    shift = (-ACH_DEPTH_L * u if u >= 0 else ACH_THIN_L * -u) + ACH_CALM_L * v
    dh = d + shift if shift >= 0 else d * math.exp(shift / d)
    Lh2 = pole - p * dh
    spread = (1 + ACH_DEEP_SPREAD * u) * (1 - ACH_CALM_SPREAD * v)
    out = []
    for L, C, h in base:
        Ln = max(0.0, min(1.0, Lh2 + (L - Lh) * spread))
        out.append(from_lch(Ln, C * (1 - ACH_CALM_TINT * v) * (1 + ACH_DEEP_TINT * u), h)[0])
    return out

def title_for(key, stops, shaded):
    """The title follows the heart: the locked title moved by the same OKLab offset the heart moved."""
    t = TITLES.get(key)
    if key == "Silver":
        return None  # Silver's title is the theme's primary text (unchanged)
    h0, h1 = to_oklab(unhex(stops[1])), to_oklab(unhex(shaded[1]))
    if t is None:  # derived makers: heart lifted 22 % toward white, as EffortPairBrand does for unknown colours
        L, a, b = h1
        return from_lch(L + 0.22 * (1 - L), math.hypot(a, b) * 0.9, math.degrees(math.atan2(b, a)) % 360)[0]
    tl = to_oklab(unhex(t))
    L, a, b = (tl[k] + h1[k] - h0[k] for k in range(3))
    return from_lch(L, math.hypot(a, b), math.degrees(math.atan2(b, a)) % 360)[0]

# --------------------------------------------------------------------------------------------- motion

def _geo(t_calm, t_full, e):
    return t_calm * (t_full / t_calm) ** e

def _lin(a, b, e):
    return a + (b - a) * e

def motion(s, e, key, anchor_s=None):
    """Effort drives every motion dial from calm (e=0) to exactly the locked v22 values (e=1). Strength adds light
    (caustic, glow, breathe saturation, Grok's oil sheen, Silver's specular), so achromatic brands read stronger too.
    At (anchor, e=1) every value equals v22. Loops advance by phase (phi += dt/T): a change never restarts a loop."""
    u, _ = uv(s, e, anchor_s)
    light = 1 + 0.12 * u
    m = {
        "current_fast_period_s": _geo(12.0, 7.0, e), "current_fast_opacity": _lin(0.30, 0.55, e),
        "current_fast_amplitude": _lin(18, 30, e),
        "current_slow_period_s": _geo(16.0, 11.0, e), "current_slow_opacity": _lin(0.22, 0.35, e),
        "current_slow_amplitude": 7,
        "caustic_period_s": _geo(9.0, 6.0, e), "caustic_alpha": min(0.92, _lin(0.50, 0.80, e) * light),
        "bubble_px_per_lane": _geo(64.0, 32.0, e), "bubble_rise_scale": _lin(1.5, 1.0, e),
        "bubble_pause_max_s": _lin(2.4, 1.4, e), "bubble_large_share": _lin(0.04, 0.10, e),
        "breathe_period_s": _geo(6.4, 4.8, e), "breathe_saturate_amp": _lin(0.05, 0.12, e) * (1 + 0.25 * u),
        "breathe_bright_amp": _lin(0.03, 0.06, e),
        "glow_alpha": min(0.70, 0.6 * _lin(0.60, 1.0, e) * (1 + 0.15 * u)), "glow_blur": _lin(4, 8, e),
    }
    if key == "Grok":
        m["oil_sheen_period_s"] = _geo(9.0, 6.5, e)
        m["oil_sheen_alpha"] = min(1.0, 0.9 * _lin(0.6, 1.0, e) * (1 + 0.15 * u))
    if key == "Silver":
        m["specular_scale"] = min(1.15, (0.75 + 0.25 * e) * (1 + 0.15 * u))
    return {k: (round(x, 3) if isinstance(x, float) else x) for k, x in m.items()}

# --------------------------------------------------------------------------------------------- effort e

# IR-2 (F5): ONE global effort scale, versioned with the rule. IR-1 used index/(n-1) in the model's own list, so the
# same model at the same word got a different shade through DeepSeek [low, high, max] and Command Code [high, max],
# or through Claude Code with and without xhigh. Now the word alone sets e; the model's own list only decides which
# stops its knob offers. Every word any provider uses today is on the ladder (builtin-models.json: off, minimal, low,
# medium, high, xhigh, max). Comparison is ordinal on the lower-cased ASCII word (C#: ToLowerInvariant, Ordinal).
EFFORT_LADDER = {"off": 0, "none": 0, "minimal": 1, "low": 2, "medium": 3, "high": 4, "xhigh": 5, "max": 6}
EFFORT_TOP = 6
NO_KNOB_E = 1.0        # a model with no effort knob shows its full look (as IR-1)
UNKNOWN_WORD_E = 0.5   # a word the ladder does not know yet: mid scale, flagged; the next data pack adds the word
# IR-3 (R2-3): spellings of ladder words that providers emit today, applied BEFORE the ladder. The same table as the
# port's ACPSession.EffortRanks (extra-high/extra_high/'extra high' = 5 = xhigh, ultra = 6 = max); a test in the port
# asserts that every EffortRanks key maps to the same rank here, so the two can never drift.
EFFORT_SYNONYMS = {"extra-high": "xhigh", "extra_high": "xhigh", "extra high": "xhigh", "ultra": "max"}
# Every effort word a provider can emit today (the R2-3 enumeration test): builtin-models.json; ACPSession.cs:539 and
# :551-552 (Grok, Cursor, Devin, OpenCode via ACP); DeepSeekSession.cs:55-56; CommandCodeAPI.cs:452-453; MetaAPI.cs:59-63;
# CodexSession supportedReasoningEfforts and Copilot supportedReasoningEfforts (minimal..xhigh); OpenRouter reasoning
# (OpenRouterAPI.cs:147: low, medium, high); OpenRouterSession.cs:52.
PROVIDER_EFFORT_WORDS = ("off", "none", "minimal", "low", "medium", "high", "xhigh", "max", "extra-high", "extra_high",
                         "extra high", "ultra")

def effort_word(level):
    """The ladder word of an effort word as a provider spells it (ordinal, lower-cased ASCII, synonyms applied)."""
    w = level.strip().lower()
    return EFFORT_SYNONYMS.get(w, w)

def effort_e(level, levels=None):
    """Global effort position of an effort WORD (IR-2). `levels` is accepted for the old call shape and ignored:
    the same word is the same e in every list."""
    if level is None:
        return NO_KNOB_E
    w = effort_word(level)
    return EFFORT_LADDER[w] / EFFORT_TOP if w in EFFORT_LADDER else UNKNOWN_WORD_E

def effort_word_known(level):
    return level is None or effort_word(level) in EFFORT_LADDER

# --------------------------------------------------------------------------------------------- strength s

# OD-0066 / discovery.md section 5: class (tier word) first, then version, release date, price, context, id.
GENERIC_DOWN = {"nano": 0, "tiny": 0, "micro": 0, "lite": 1, "xs": 1, "oss": 1, "gemma": 1, "glimmer": 1,
                "edge": 1, "ministral": 1, "nemo": 1, "mini": 2, "small": 2, "haiku": 2}
GENERIC_UP = {"flash": 3, "flashx": 3, "fast": 3, "highspeed": 3, "ultraspeed": 3, "air": 3, "turbo": 3,
              "lightning": 3, "scout": 4,
              "sonnet": 4, "medium": 4, "plus": 5, "super": 5, "maverick": 5, "large": 6, "pro": 6,
              "opus": 6, "premier": 6, "max": 7, "ultra": 7, "prime": 8, "fable": 9, "astra": 9}
MAKER_LADDER = {  # maker-scoped words that break the generic ladder
    "openai": {"max": 4.5},
    "x-ai": {"build": 1},
    "deepseek": {"flash": 4},            # V4 Flash is DeepSeek's mainline model, not a speed tier
    # IR-2 (OD66-10): 'muse' is Meta's flagship family word, so a future 'muse-1.4' (no 'spark') ranks above every
    # Llama whatever the version numbers, and Muse versions compare only with Muse versions (one class).
    "meta": {"muse": 6, "spark": 6, "glimmer": 1},
}
# Maker families with their own ladder. IR-2 (OD66-3): the family values sit ON the generic scale instead of beside
# it: Luna between plain (4) and plus (5), Terra between plus and pro (6), Sol between pro and max (7), Astra at the
# top tier. So a family's tiers outrank the previous generation's generic tiers (Sol above gpt-5.5-pro and o1-pro,
# Luna above a plain gpt-4), which is the owner's OD-0066 example ("before Astra the range was spread over Sol, Terra
# and Luna").
FAMILY = {"openai": {"luna": 4.5, "terra": 5.5, "sol": 6.5, "astra": 9}}
MODIFIER = ("pro", "max", "plus", "ultra", "prime")
FAMILY_UP = 0.4                              # a modifier word lifts a family member
FAMILY_DOWN = {0: 0.4, 1: 0.35, 2: 0.3}      # IR-2 (OD66-2): nano-like / lite-like / mini-like below the plain member
# IR-2 (OD66-1): suffixes that never change the model (always collapse) vs speed variants (collapse only onto a base
# row that exists in the same lineup; otherwise the variant is its own member and its speed word ranks it).
SAME_MODEL_SUFFIX = re.compile(r"-(free|paid|preview|exp|contributor|thinking|customtools|latest|instruct|it|sante)$")
SPEED_SUFFIX = re.compile(r"-(fast|highspeed|ultraspeed)$")
PARAM_TOKEN = re.compile(r"^(\d+x)?\d+(\.\d+)?[bmt]$|^a\d+(\.\d+)?b$|^r\d+b$")
# IR-2 (OD66-3): lineup-relative legacy cap. A member released more than LEGACY_DAYS before the newest member of its
# maker's lineup ranks below every current member (its own class order kept). Lineup-relative, never the clock.
LEGACY_DAYS = 548
# IR-3 (R2-OD66-C): a speed variant with no base row in the lineup ranks just below its base's class, whatever
# up-words (pro, astra, fable) it carries, so it can never take the flagship's shade.
SPEED_DOWN = 0.05
# IR-3 (R2-OD66-B): a dated row (-2512, -2025-07-28) created more than SPLIT_DAYS after its base row (or after the
# earliest dated row when there is no base) is a successor, its own member, not a snapshot of the base. Price is not a
# criterion: older dated snapshots are often priced differently from their alias and must not outrank it.
SPLIT_DAYS = 30
# IR-3 (R2-OD66-D): harness spellings of catalogue members, in the pack. Word-order rewrites always apply; effort or
# thinking suffixes are stripped only when what remains is a lineup member ('-max' is also a model modifier).
HARNESS_REWRITES = ((re.compile(r"^claude-(\d+(?:\.\d+)*)-(opus|sonnet|haiku|fable)(?=-|$)"), r"claude-\2-\1"),
                    (re.compile(r"^(opus|sonnet|haiku|fable)-(?=\d)"), r"claude-\1-"))
EFFORT_SUFFIX = re.compile(r"-(none|minimal|low|medium|high|xhigh|extra-high|max|ultra|thinking|reasoning)$")
EFFORT_SUFFIX_SAFE = re.compile(r"-(none|minimal|low|medium|high|xhigh|extra-high|thinking|reasoning)$")
# IR-2 (OD66-4): visible levels.
MIN_LEVEL_STEP = 0.02     # adjacent strength levels differ by at least this (mean OKLab over the 3 stops) at e = 1
MAX_LEVELS = 16
ARC_SAMPLES = 200

_DATE_SUFFIXES = (re.compile(r"-\d{4}-\d{2}-\d{2}$"), re.compile(r"-\d{8}$"), re.compile(r"-\d{2}-\d{4}$"),
                  re.compile(r"-\d{2}-\d{2}$"),
                  re.compile(r"-(0\d{3}|1[0-2]\d{2}|2[0-9]{3})$"))  # -0731, -2407, -2512 snapshot dates

def _canon(model_id, strip_speed):
    """-> (canonical id, whether a variant label, a ':' or '[...]' suffix or a snapshot date was removed, the first
    snapshot date removed as digits or None). IR-3 (R2-OD66-B) uses the date to name a dated successor."""
    x = model_id.lower().lstrip("~")
    x = x.split("/")[-1]
    raw = x
    x = re.sub(r"\[.*?\]", "", x)
    x = x.split(":")[0]
    date = None
    for _ in range(4):  # variant words and snapshot dates, in any order
        for rx in _DATE_SUFFIXES:
            m = rx.search(x)
            if m:
                if date is None:
                    date = re.sub(r"\D", "", m.group(0))
                x = x[:m.start()]
        x = SAME_MODEL_SUFFIX.sub("", x)
        if strip_speed:
            x = SPEED_SUFFIX.sub("", x)
    stripped = x != raw
    # claude-opus-4-6 -> claude-opus-4.6, mistral-medium-3-5 -> 3.5; never qwen3-8b or llama-3.3-70b (sizes)
    x = re.sub(r"(?<=[a-z-]\d)-(?=\d{1,2}(?:-|$))", ".", x)
    return x, stripped, date

def canonical(model_id, keep_speed=False):
    """The lineup name of a model id. keep_speed=False (the IR-1 behaviour) also strips speed words; the lineup keeps
    them unless the base row exists (OD66-1), so lookups try keep_speed=True first."""
    return _canon(model_id, not keep_speed)[0]

SEED_CANON = {  # seed ids whose name differs from the public catalogue's
    "deepseek-flash": "deepseek-v4.1-flash", "hy3-paid": "hy3", "gemini-3.1-pro": "gemini-3.1-pro",
    "minimax-m3-free": "minimax-m3", "laguna-s-2.1-free": "laguna-s-2.1",
}

def _toks(canon):
    return re.split(r"[-_.\s/]+", canon)

def tier(canon, maker):
    toks = _toks(canon)
    famtab = FAMILY.get(maker, {})
    fam = [famtab[t] for t in toks if t in famtab]
    downs = [GENERIC_DOWN[t] for t in toks if t in GENERIC_DOWN]
    if fam:
        base = max(fam)
        if any(t in MODIFIER for t in toks):
            return base + FAMILY_UP
        if downs:                                   # IR-2 (OD66-2): X-mini < X inside a family too
            return base - FAMILY_DOWN[min(downs)]
        return base
    ladder = dict(GENERIC_UP)
    ladder.update(MAKER_LADDER.get(maker, {}))
    if downs:
        return min(downs)
    ups = [ladder[t] for t in toks if t in ladder]
    return max(ups) if ups else 4  # no word = the plain class

def version(canon, maker):
    toks = [t for t in re.split(r"[-_\s]+", canon) if not PARAM_TOKEN.match(t)]
    for t in toks:
        m = re.search(r"(\d+(?:\.\d+)*)", t)
        if m:
            parts = [int(p) for p in m.group(1).split(".")]
            if maker == "x-ai" and len(parts) >= 2 and parts[1] >= 10 and parts[1] % 10 == 0:
                parts = [parts[0], parts[1] // 10, 0] + parts[2:]  # grok-4.20 is 4.2.0, before 4.3
            return tuple(parts)
    return ()

_INF = float("inf")

def _fam_word(canon, lk):
    famtab = FAMILY.get(lk, {})
    fams = [t for t in _toks(canon) if t in famtab]
    return max(fams, key=lambda t: (famtab[t], t)) if fams else None

def make_member(lk, canon, rows, base_canon=None, orphan_speed=False):
    """One lineup member from its collapsed rows. IR-2 (OD66-5, F9): the representative row is chosen by a total
    order that ignores input order: the base row (no variant label) first, then the earliest `created` (unknown last),
    then the higher price, the larger context, the id compared ordinally. Its price, date and context are the member's.
    IR-3: `base_canon` (a dated successor) takes class and version from its base's name; `orphan_speed` (a speed
    variant with no base row) ranks just below its base's class (R2-OD66-C)."""
    rep = min(rows, key=lambda r: (not r["base"], r["created"] or _INF, -(r["price"] or 0.0), -(r["ctx"] or 0),
                                   r["id"].encode()))
    created, price, ctx = rep["created"] or 0, rep["price"] or 0.0, rep["ctx"] or 0
    tc = base_canon or canon
    t = tier(canonical(tc), lk) - SPEED_DOWN if orphan_speed else tier(tc, lk)
    fam = _fam_word(tc, lk)
    toks = _toks(tc)
    return {"canon": canon, "tier": t, "created": created, "price": price, "ctx": ctx,
            "fam": fam, "major_tier": FAMILY[lk][fam] if fam else (t + SPEED_DOWN if orphan_speed else t),
            "stem": toks[0],
            "down": any(x in GENERIC_DOWN for x in toks),
            "rows": sorted({r["id"] for r in rows}, key=lambda i: i.encode()),
            "key": (version(tc, lk), created // 86400, price, ctx, canon.encode())}

def lineup_order(members):
    """Annotate (legacy, class, group) and return the members strongest first. The group key is (class, version,
    release day, price): members equal on all four share one shade (OD66-9); context and id only order the list.
    IR-3:
      * next generation (R2-OD66-F): a member with no family word and no down-word, sharing a family member's stem
        ('gpt'), whose major version is above every family member's, ranks above the family (gpt-7 > gpt-6-astra);
      * legacy (R2-OD66-A): a member is legacy only if a member of the SAME OR A HIGHER rank (next generation, then
        tier) was released more than LEGACY_DAYS after it. A weaker newcomer never makes a stronger member legacy.
    class = (current, next generation, tier); major = the same with the family word's value in place of a modified
    tier (Sol Pro and Sol share a major class), used by the level allocation (R2-OD66-E)."""
    fam_members = [m for m in members if m["fam"]]
    g_fam = max((m["key"][0][0] for m in fam_members if m["key"][0]), default=None)
    stems = {m["stem"] for m in fam_members}
    for m in members:
        m["nextgen"] = int(g_fam is not None and not m["fam"] and not m["down"] and m["stem"] in stems
                           and bool(m["key"][0]) and m["key"][0][0] > g_fam)
    for m in members:
        rank = (m["nextgen"], m["tier"])
        m["legacy"] = bool(m["created"]) and any(
            n["created"] - m["created"] > LEGACY_DAYS * 86400 and (n["nextgen"], n["tier"]) >= rank for n in members)
        m["class"] = (0 if m["legacy"] else 1, m["nextgen"], m["tier"])
        m["major"] = (0 if m["legacy"] else 1, m["nextgen"], m["major_tier"])
        m["group"] = (m["class"], m["key"][0], m["key"][1], m["key"][2])
    return sorted(members, key=lambda m: (m["group"], m["key"][3], m["key"][4]), reverse=True)

_LEVELS = {}

def _raw_positions(n):
    """Evenly spaced positions, strongest first (1 .. 0)."""
    return [1 - i / (n - 1) for i in range(n)]

def level_scale(stops):
    """IR-2 (OD66-4): (V, {n: [s of level 1 (strongest) .. n]}) for one palette, a pure cached function of the palette.
      * Levels are spaced by measured perceptual distance at e = 1: p in [0, 1] is the arc-length fraction of the
        shade path s = 0 .. 1 (ARC_SAMPLES samples), so equal p-steps are equal visible steps.
      * Few levels stay near the anchor (IR-1's spread): n levels use the central fraction w = min(1, (n-1)/3) of
        the range, widened in eighths only as far as needed for every adjacent pair to differ by MIN_LEVEL_STEP.
      * One level is s = 0.5 exactly (the anchor, so a one-model maker shows the canvas-approved stops at max).
      * V is the largest n (<= MAX_LEVELS) for which every n' <= n has a passing spacing."""
    key = tuple(stops)
    if key in _LEVELS:
        return _LEVELS[key]
    sh = [shade(stops, j / ARC_SAMPLES, 1.0) for j in range(ARC_SAMPLES + 1)]
    cum = [0.0]
    for a, b in zip(sh, sh[1:]):
        cum.append(cum[-1] + dE(a, b))
    a_top = cum[-1]

    def s_at(p):
        arc = a_top * p
        if arc <= 0:
            return 0.0
        if arc >= a_top:
            return 1.0
        lo, hi = 0, ARC_SAMPLES          # largest j with cum[j] <= arc
        while hi - lo > 1:
            mid = (lo + hi) // 2
            lo, hi = (mid, hi) if cum[mid] <= arc else (lo, mid)
        span = cum[lo + 1] - cum[lo]
        return (lo + (0.0 if span == 0 else (arc - cum[lo]) / span)) / ARC_SAMPLES

    positions, V = {1: [0.5]}, 1
    for n in range(2, MAX_LEVELS + 1):
        base, found = min(1.0, (n - 1) / 3), None
        for k in range(9):
            w = base + (1 - base) * k / 8
            ss = [round(s_at(0.5 + (r - 0.5) * w), 4) for r in _raw_positions(n)]
            st = [shade(stops, s, 1.0) for s in ss]
            if min(dE(a, b) for a, b in zip(st, st[1:])) >= MIN_LEVEL_STEP:
                found = ss
                break
        if found is None:
            break
        positions[n], V = found, n
    _LEVELS[key] = (V, positions)
    return V, positions

def s_from_lineup(members, stops=None):
    """members: dicts from make_member. Returns {canon: (s, level (1 = strongest), levels used)}.
    G groups, V visible levels of the palette (level_scale):
      G <= V: every group its own level, spread as IR-1 (1 -> 0.5, 2 -> 1/3 and 2/3, 4+ -> the full range);
      G >  V: top-anchored (OD66-7): exactly V-1 steps, given first to the flagship (the strongest group alone takes
              the most intense shade, OD-0066), then to class boundaries (current vs legacy included) top-down, then
              to each class's head boundary (its newest version apart from older ones), classes top-down, then each
              class's second boundary, and so on. The rest merges ("merging from the bottom")."""
    ordered = lineup_order([dict(m) for m in members])
    if not ordered:
        return {}
    groups = []
    for m in ordered:
        if groups and groups[-1][0] == m["group"]:
            groups[-1][1].append(m)
        else:
            groups.append((m["group"], [m]))
    G = len(groups)
    V, positions = level_scale(stops or NEUTRAL)
    if G <= V:
        n, level = G, list(range(G))
    else:
        n = V
        cls = [g[1][0]["class"] for g in groups]
        maj = [g[1][0]["major"] for g in groups]
        fam = [g[1][0]["fam"] for g in groups]
        prio = []

        def add(i):
            if i not in prio:
                prio.append(i)
        # IR-3 (R2-OD66-E): 1. the flagship alone (OD-0066), but only when it differs from the next group by version,
        # day or price, never by a modifier alone (Astra Pro | Astra is not worth a step); 2. family-word boundaries
        # top-down (Astra | Sol | Terra | Luna); 3. every other class boundary top-down (generic tiers, legacy, next
        # generation); 4. modifier boundaries inside a family word (Sol Pro | Sol); 5. class heads, then second
        # members, and so on.
        if cls[0] == cls[1]:
            add(0)
        for i in range(G - 1):
            if fam[i] is not None and fam[i + 1] != fam[i]:
                add(i)
        for i in range(G - 1):
            if maj[i] != maj[i + 1]:
                add(i)
        for i in range(G - 1):
            if cls[i] != cls[i + 1]:
                add(i)
        runs, start = [], 0
        for i in range(1, G + 1):
            if i == G or cls[i] != cls[i - 1]:
                runs.append(list(range(start, i - 1)))
                start = i
        r = 0
        while any(r < len(run) for run in runs):
            prio += [run[r] for run in runs if r < len(run) and run[r] not in prio]
            r += 1
        chosen = set(prio[:V - 1])
        level, lv = [], 0
        for i in range(G):
            level.append(lv)
            if i in chosen:
                lv += 1
    pos = positions[n]
    out = {}
    for i, (_, ms) in enumerate(groups):
        for m in ms:
            out[m["canon"]] = (pos[level[i]], level[i] + 1, n)
    return out

# --------------------------------------------------------------------------------------------- building the table

SPECIALIST = "specialist(non-chat)"
NEUTRAL = neutral_stops()

def load():
    b = json.load(open(os.path.join(HERE, "builtin-models.json"), encoding="utf-8"))
    o = json.load(open(os.path.join(HERE, "openrouter-models.json"), encoding="utf-8"))
    raw = {}
    if os.path.exists(RAW_OR):
        raw = json.load(open(RAW_OR, encoding="utf-8"))["models"]
    return b, o, raw

def or_efforts(raw_row):
    if not raw_row:
        return []
    p = raw_row.get("supported_parameters") or []
    return ["low", "medium", "high"] if ("reasoning" in p or "include_reasoning" in p) else []  # OpenRouterAPI.cs:147

def lineup_stops(lk):
    """The palette a maker's levels are measured on: its own, or the pending neutral while it has none."""
    return palette_for(lk)[1] or NEUTRAL

def _eligible(r):
    return not (r["id"].startswith("~") or SPECIALIST in r["tags"] or any(t.startswith("router") for t in r["tags"]))

def build_lineups(builtin, orm, raw, cutoff=None, tombstones=()):
    """Global lineup per maker (OD-0066): the public catalogue plus the shipped seeds, variants collapsed.
    IR-2: row order never matters (collapse by a total order; speed variants only onto an existing base);
    `cutoff` (F6) drops catalogue rows created at or after the lineup boundary B(t).
    IR-3: `tombstones` are (row, raw) pairs withdrawn from the live catalogue but carried by the pack with their last
    facts (R2-2): they stay members, so a withdrawal never moves anyone's shade between packs. Dated successors split
    from their base (R2-OD66-B); a speed variant with no base ranks below its base's class (R2-OD66-C)."""
    if tombstones:
        live = {r["id"] for r in orm}
        orm = list(orm) + [t for t, _ in tombstones if t["id"] not in live]
        raw = dict(raw)
        raw.update({t["id"]: tr for t, tr in tombstones if t["id"] not in live})
    rows = {}
    for r in orm:
        if not _eligible(r):
            continue
        rr = raw.get(r["id"], {})
        created = rr.get("created")
        if cutoff is not None and created is not None and created >= cutoff:
            continue
        lk = LINEUP_KEY.get(r["maker"], r["maker"])
        c, stripped, date = _canon(r["id"], strip_speed=False)
        rows.setdefault(lk, {}).setdefault(c, []).append(
            {"id": r["id"], "price": r.get("completion_price_per_m"), "created": created, "ctx": r.get("context"),
             "base": not stripped, "date": date})
    for r in builtin:
        mk = SEED_MAKER.get(r["maker"])
        if mk is None or r.get("alias") or r.get("live_only") or r["model_id"] in ("haiku", "sonnet", "opus"):
            continue
        c = canonical(r["model_id"], keep_speed=True)
        c = SEED_CANON.get(c, c)
        lk = LINEUP_KEY.get(mk, mk)
        slot = rows.setdefault(lk, {})
        if c not in slot and canonical(c) not in slot:
            slot[c] = [{"id": r["model_id"], "price": None, "created": None, "ctx": None, "base": True, "date": None}]
    result = {}
    for lk in sorted(rows):
        slot = rows[lk]
        split_base = {}
        for c in sorted(slot):                       # IR-3 (R2-OD66-B): dated successors become their own members
            rs = slot[c]
            undated = [r for r in rs if r["date"] is None and r["created"]]
            dated = [r for r in rs if r["date"] is not None and r["created"]]
            anchor = min(undated or dated, key=lambda r: (r["created"], r["id"].encode()), default=None)
            if anchor is None:
                continue
            for r in dated:
                if r["created"] - anchor["created"] > SPLIT_DAYS * 86400:
                    dc = f"{c}-{r['date']}"
                    rs.remove(r)
                    slot.setdefault(dc, []).append(dict(r, base=True))
                    split_base[dc] = c
        for c in sorted(slot):                       # OD66-1: a speed variant joins its base only if the base exists
            if c in split_base:
                continue
            b = canonical(c)
            if b != c and b in slot and c in slot:
                slot[b].extend(dict(x, base=False) for x in slot.pop(c))
        members = [make_member(lk, c, slot[c], base_canon=split_base.get(c),
                               orphan_speed=c not in split_base and canonical(c) != c)
                   for c in sorted(slot) if slot[c]]
        meta = {m["canon"]: m for m in lineup_order([dict(m) for m in members])}
        for m in members:
            m["legacy"] = meta[m["canon"]]["legacy"]
        result[lk] = (s_from_lineup(members, lineup_stops(lk)), members)
    return result

def place(lk, canon, lineups):
    """IR-2 (OD66-6): a model missing from the lineup (in a provider list before the catalogue has it, CLI-only) is
    placed with the same precedence, as if it were one more member, WITHOUT changing any other member's shade (the
    lineup stays the global one). Same lineup + same id -> same place on every machine. A model stronger than the whole
    lineup takes the top level (ties the current flagship until the catalogue has it).
    IR-3 (R2-OD66-D, R2-2): placement takes NO facts from a provider list (price, created): the id alone. A model with
    (class, version) peers in the lineup inherits the facts of the earliest of them, so it shares that peer's group and
    can never outrank its peers; a model with no peer keeps unknown facts (current, day 0), which only order it among
    members of its own (class, version), of which there are none. Withdrawn catalogue rows never come here: they stay in
    the lineup as tombstones."""
    _, members = lineups.get(lk, ({}, []))
    base = canonical(canon)
    orphan = base != canon and not any(m["canon"] == base for m in members)
    row = {"id": canon, "price": None, "created": None, "ctx": None, "base": True}
    m = make_member(lk, canon, [row], orphan_speed=orphan)
    peers = [p for p in members if p["tier"] == m["tier"] and p["key"][0] == m["key"][0]]
    if peers:
        p = min(peers, key=lambda p: (p["created"] or _INF, p["price"], p["canon"].encode()))
        m = make_member(lk, canon, [dict(row, price=p["price"] or None, created=p["created"] or None)],
                        orphan_speed=orphan)
    return s_from_lineup(members + [m], lineup_stops(lk))[canon]

def harness_canon(c):
    """IR-3 (R2-OD66-D): a harness spelling (Cursor-style claude-4.5-opus, bare opus-4.5) in catalogue word order."""
    for rx, rep in HARNESS_REWRITES:
        c = rx.sub(rep, c)
    return c

def lookup(lk, model_id, lineups):
    """-> (s, level, levels, how): the member itself, its dated successor, its speed base, the member a harness spelling
    names (word order, effort or thinking suffix), or a virtual placement."""
    table = lineups.get(lk, ({}, []))[0]
    c, _, date = _canon(model_id, strip_speed=False)
    c = SEED_CANON.get(c, c)
    if date is not None and f"{c}-{date}" in table:
        return table[f"{c}-{date}"] + ("lineup (dated successor)",)
    if c in table:
        return table[c] + ("lineup",)
    b = canonical(c)
    if b in table:
        return table[b] + ("lineup (speed variant of its base)",)
    h = harness_canon(c)
    x = h
    for _ in range(3):
        for cand in (x, canonical(x)):
            if cand in table:
                return table[cand] + ("lineup (harness spelling)",)
        y = EFFORT_SUFFIX.sub("", x)
        if y == x:
            break
        x = y
    for _ in range(3):
        y = EFFORT_SUFFIX_SAFE.sub("", h)
        if y == h:
            break
        h = y
    return place(lk, h, lineups) + ("placed (not in the lineup)",)

def resolve_alias(canon_stem, lk, lineups, price=None):
    """'~anthropic/claude-opus-latest' or the CLI's version-less 'opus'. IR-2 (OD66-8): the catalogue's
    alias_target / canonical_slug decides when present (engine); otherwise the members carrying all the alias's words
    (minus 'latest' and the brand word), skipping modifier siblings (pro, max, plus, ultra, prime) the alias does not
    name, preferring an exact price match with the alias row, then the strongest; none -> the maker's strongest
    non-modifier member."""
    _, members = lineups[lk]
    words = [w for w in re.split(r"[-_.]+", canon_stem) if w and w not in ("latest", "claude", "gpt", "gemini")]

    def plain(m):
        return not any(t in MODIFIER and t not in words for t in _toks(m["canon"]))
    cands = [m for m in members if all(w in _toks(m["canon"]) for w in words) and plain(m)]
    if price is not None:
        exact = [m for m in cands if abs(m["price"] - price) < 1e-9]
        if exact:
            cands = exact
    pool = cands or [m for m in members if plain(m)] or members
    order = {m["canon"]: i for i, m in enumerate(lineup_order([dict(m) for m in members]))}
    return min(pool, key=lambda m: order[m["canon"]])["canon"]

def cells(key, stops, s, efforts, anchor_alt):
    levels = list(efforts) if efforts else []
    out = []
    for lv in (levels or [None]):
        e = effort_e(lv)
        sh = shade(stops, s, e) if stops else None
        cell = {"effort": lv or "(none)", "e": round(e, 4)}
        if not effort_word_known(lv):
            cell["unknown_effort_word"] = True
        if sh:
            cell["stops"] = sh
            cell["title"] = title_for(key, stops, sh)
            cell["stops_ceiling_alt"] = shade(stops, s, e, anchor_s=anchor_alt)
        else:
            cell["stops"] = None
        cell["motion"] = motion(s, e, key)
        out.append(cell)
    return out

def main(swatch=False):
    builtin, orm, raw = load()
    assert derive_nvidia() == DERIVED["nvidia"], derive_nvidia()
    lineups = build_lineups(builtin, orm, raw)
    ALT = 1.0

    # ---- built-in providers
    brows = []
    for r in builtin:
        row = {"provider": r["provider"], "maker": r["maker"], "model_id": r["model_id"],
               "display_name": r["display_name"]}
        if r.get("live_only"):
            row.update(live_only=True, rule="ranked at runtime by the same lineup rule; e from the global effort "
                                            "ladder (the model's own list only picks the stops)",
                       efforts_source=r["efforts"])
            brows.append(row)
            continue
        mk = SEED_MAKER.get(r["maker"])
        if mk is None:  # OpenRouter router, Copilot Auto, Pi Default
            row.update(alias=True, palette=None,
                       rule="no colour of its own: clear living water (OD-0050) until the answering/resolved model "
                            "is known, then that model's row (its maker's palette at its own s and e)")
            brows.append(row)
            continue
        lk = LINEUP_KEY.get(mk, mk)
        key, stops = palette_for(mk)
        c = canonical(r["model_id"], keep_speed=True)
        c = SEED_CANON.get(c, c)
        resolved = None
        if r.get("alias") or r["model_id"] in ("haiku", "sonnet", "opus"):
            if r["model_id"] == "default":
                row.update(alias=True, palette=key,
                           rule="takes the row of the model it resolves to at runtime (Claude Code's recommended model)")
                brows.append(row)
                continue
            resolved = resolve_alias(c, lk, lineups)
            c = resolved
        s, rank, n, how = lookup(lk, c, lineups)
        row.update(maker_key=mk, palette=key or "pending (OD-0064 derives it at runtime)", canonical=c,
                   resolved_to=resolved, s=s, lineup_level=rank, lineup_levels=n, placement=how,
                   seed_rank=r["strength_rank"], efforts=cells(key, stops, s, r["efforts"], ALT))
        brows.append(row)

    # ---- OpenRouter: every maker's models, the maker's colour (OpenRouter itself has none)
    ormakers = {}
    for r in orm:
        mk = r["maker"]
        lk = LINEUP_KEY.get(mk, mk)
        rec = {"id": r["id"], "name": r["name"], "tags": r["tags"],
               "price_percentile_input": r.get("strength_percentile")}
        if any(t.startswith("router") for t in r["tags"]):
            rec.update(router=True, palette=None, s=None,
                       rule="router: clear living water until the answering model is known, then its maker's ink")
            ormakers.setdefault(mk, {"palette": None, "models": []})["models"].append(rec)
            continue
        key, stops = palette_for(mk)
        c = canonical(r["id"], keep_speed=True)
        if r["id"].startswith("~"):
            c = resolve_alias(canonical(r["id"]), lk, lineups, price=r.get("completion_price_per_m"))
            rec["resolved_to"] = c
        how = None
        if SPECIALIST in r["tags"]:
            s, rank, n = 0.0, None, None
            rec["note"] = "non-chat specialist: weakest shade, outside the ranked lineup"
        else:
            s, rank, n, how = lookup(lk, c, lineups)
        efforts = or_efforts(raw.get(r["id"]))
        rec.update(canonical=c, s=s, lineup_level=rank, lineup_levels=n, placement=how,
                   efforts=cells(key, stops, s, efforts, ALT))
        m = ormakers.setdefault(mk, {"palette": key or "pending (OD-0064 derives it at runtime)", "models": []})
        m["models"].append(rec)
    for mk, m in ormakers.items():
        m["models"].sort(key=lambda x: (x.get("s") is None, -(x.get("s") or 0), x["id"]))

    metrics = measure(lineups, brows)
    metrics["hardening"] = hardening_checks(builtin, orm, raw, lineups, brows, ormakers)
    levels = {k: level_scale(v)[0] for k, v in {**LOCKED, **DERIVED, "(pending neutral)": NEUTRAL}.items()}
    table = {
        "rule": RULE_VERSION,
        "generated": "2026-10-02",
        "anchor": {"recommended": "centre: s = 0.5 at the top of the global effort ladder (max, e = 1) shows the "
                                  "locked stops and v22 motion exactly", "ANCHOR_S": ANCHOR_S,
                   "alternative_in_stops_ceiling_alt": "ceiling: the flagship (s = 1) at max shows the lock"},
        "inputs": {"s": "strength within the maker's global lineup (OD-0066): class first (tier word; a plain-named "
                        "next generation above the family; a member is legacy, below every current one, only when a "
                        "member of the same or a higher class is > 18 months newer), then version, release day, price "
                        "(one group, one shade), then context and id for the list order. Groups take the palette's "
                        "visible levels (>= 0.02 OKLab apart at e = 1): every group its own level when they fit, else "
                        "the flagship alone (unless only a modifier sets it apart), family-word boundaries, other "
                        "class boundaries, modifier boundaries, then each class's head. Variants (:free, :batch, "
                        "-preview, dated snapshots) collapse onto their base by an order-free rule; a dated successor "
                        "more than 30 days newer than its base is its own member; speed variants (-fast, -highspeed, "
                        "-ultraspeed) collapse only onto a base that exists, else rank just below its class. Withdrawn "
                        "rows stay as tombstones. Aliases take their target's s. A model missing from the lineup is "
                        "placed virtually by its id alone (harness spellings canonicalised; facts inherited from its "
                        "(class, version) peers).",
                   "e": "the effort WORD on one global ladder: " + ", ".join(
                       f"{w} {EFFORT_LADDER[w]}/{EFFORT_TOP}" for w in EFFORT_LADDER) +
                        "; synonyms first (" + ", ".join(f"{k} -> {v}" for k, v in EFFORT_SYNONYMS.items()) +
                        "); no knob -> 1; the model's own list only decides which stops its knob offers"},
        "constants": {"DEEP_L": DEEP_L, "DEEP_C": DEEP_C, "THIN_L": THIN_L, "THIN_C": THIN_C, "CALM_C": CALM_C,
                      "CALM_L": CALM_L, "CALM_SPREAD": CALM_SPREAD, "DEEP_SPREAD": DEEP_SPREAD,
                      "GRADIENT_SPREAD_FLOOR": GRADIENT_SPREAD_FLOOR, "C_FLOOR": C_FLOOR,
                      "DARK_KEEP_C": DARK_KEEP_C, "DARK_FADE_L": DARK_FADE_L, "NEON_FADE_L": NEON_FADE_L, "MUD_KEEP": MUD_KEEP,
                      "MUD_BISECT": MUD_BISECT, "EFFORT_LADDER": EFFORT_LADDER, "FAMILY": FAMILY,
                      "FAMILY_UP": FAMILY_UP, "FAMILY_DOWN": FAMILY_DOWN, "LEGACY_DAYS": LEGACY_DAYS,
                      "MIN_LEVEL_STEP": MIN_LEVEL_STEP, "MAX_LEVELS": MAX_LEVELS, "ARC_SAMPLES": ARC_SAMPLES,
                      "EFFORT_SYNONYMS": EFFORT_SYNONYMS, "SPEED_DOWN": SPEED_DOWN, "SPLIT_DAYS": SPLIT_DAYS,
                      "HARNESS_REWRITES": [(rx.pattern, rep) for rx, rep in HARNESS_REWRITES]},
        "palettes": {**{k: {"stops": v, "kind": "locked", "title": TITLES.get(k)} for k, v in LOCKED.items()},
                     **{k: {"stops": v, "kind": "derived (palette canvas)"} for k, v in DERIVED.items()},
                     "(pending neutral)": {"stops": NEUTRAL, "kind": "pending neutral (ranks makers with no palette)"}},
        "visible_levels_per_palette": levels,
        "nvidia": {"old": NVIDIA_OLD, "new": DERIVED["nvidia"],
                   "new_oklch": [tuple(round(x, 3) for x in lch(h)) for h in DERIVED["nvidia"]],
                   "old_oklch": [tuple(round(x, 3) for x in lch(h)) for h in NVIDIA_OLD],
                   "heart_distance_to_opencode": round(math.dist(to_oklab(unhex(DERIVED["nvidia"][1])),
                                                                 to_oklab(unhex(LOCKED["Opencode"][1]))), 3)},
        "fast_mode": "constant (OD-0063): streaks, bolts, amber title and glow independent of s and e, drawn over "
                     "the (s, e) liquid",
        "transitions": {"colour_crossfade_s": 0.3, "motion_ease_s": 0.6, "lineup_respacing_glide_s": 1.2,
                        "rule": "a re-spacing (same provider and kind, new s) glides the FINAL stops in OKLab over "
                                "1.2 s, never s itself, keeping the running effect (no time reset); a retarget "
                                "starts from the current interpolated colour; the 0.3 s cross-fade is only for a "
                                "provider or kind change (OD66-11, R5). IR-3 (R2-OD66-G): the title colour, the glow "
                                "colour and every s-driven dial (caustic_alpha, glow_alpha, breathe_saturate_amp, "
                                "Silver's specular_scale, Grok's oil_sheen_alpha) glide with the SAME t over the same "
                                "1.2 s; motion_ease_s applies to effort changes only"},
        "lineups": {lk: sorted(({"canonical": m["canon"], "s": t[m["canon"]][0], "level": t[m["canon"]][1],
                                 "levels": t[m["canon"]][2], "legacy": m["legacy"], "rows": m["rows"]}
                                for m in members), key=lambda x: (-x["s"], x["canonical"]))
                    for lk, (t, members) in sorted(lineups.items())},
        "builtin": brows,
        "openrouter": ormakers,
        "metrics": metrics,
    }
    with open(os.path.join(HERE, "intensity-table.json"), "w", encoding="utf-8", newline="\n") as f:
        json.dump(table, f, indent=1, ensure_ascii=False)
    print(json.dumps(metrics, indent=1))
    if swatch:
        draw_swatch()

# --------------------------------------------------------------------------------------------- hardening checks

def _row(maker, mid, price, created, ctx=1050000, tags=()):
    return ({"maker": maker, "id": mid, "name": mid, "completion_price_per_m": price, "context": ctx,
             "tags": list(tags)}, {"created": created, "supported_parameters": ["tools", "reasoning"]})

def _with(orm, raw, *rows, drop=()):
    o = [r for r in orm if not any(r["id"] == d or r["id"].startswith(d + ":") for d in drop)]
    rw = dict(raw)
    for r, rr in rows:
        o.append(r)
        rw[r["id"]] = rr
    return o, rw

def _digest(lineups):
    import hashlib
    flat = sorted((lk, c, v[0], v[1], v[2]) for lk, (t, _) in lineups.items() for c, v in t.items())
    return hashlib.md5(json.dumps(flat).encode()).hexdigest()

def hardening_checks(builtin, orm, raw, L0, brows, ormakers):
    """The adversarial cases of round 1, re-run against IR-2. Every entry has `pass` and the measured values."""
    import random
    out = {}

    def st(L, lk):
        return {c: v[0] for c, v in L[lk][0].items()}

    def lv(L, lk):
        return {c: v[1] for c, v in L[lk][0].items()}

    # F5: same (maker, canonical, effort word) -> one shade through every provider
    seen, clash = {}, []
    for r in brows:
        for c in r.get("efforts") or []:
            if c.get("stops"):
                k = (r.get("maker_key"), r.get("canonical"), c["effort"])
                if seen.setdefault(k, c["stops"]) != c["stops"]:
                    clash.append(k)
    for mk, m in ormakers.items():
        for rec in m["models"]:
            for c in rec.get("efforts") or []:
                if c.get("stops"):
                    k = (LINEUP_KEY.get(mk, mk), rec.get("canonical"), c["effort"])
                    if seen.setdefault(k, c["stops"]) != c["stops"]:
                        clash.append(k)
    ds = lambda st_, s, a, b: dE(shade(st_, s, effort_e("high", a)), shade(st_, s, effort_e("high", b)))
    s_dsp = L0["deepseek"][0]["deepseek-v4-pro"][0]
    out["F5_same_word_same_shade"] = {
        "pass": not clash, "clashes": clash[:5],
        "deepseek_v4_pro_high_DeepSeek_vs_CommandCode_dE": round(ds(LOCKED["Deepseek"], s_dsp, ["low", "high", "max"],
                                                                    ["high", "max"]), 4)}

    # OD66-5 / F9: permutation invariance, including same-price dated snapshots
    o1, r1 = _with(orm, raw, _row("x-ai", "x-ai/grok-4.20-0309", 2.5, 1775100000, 2000000),
                   _row("openai", "openai/gpt-5.2-2026-02-01", 14.0, 1769900000, 400000))
    base = _digest(build_lineups(builtin, o1, r1))
    bad = 0
    for seed in range(30):
        o2 = list(o1)
        random.Random(seed).shuffle(o2)
        bad += _digest(build_lineups(builtin, o2, r1)) != base
    rev = _digest(build_lineups(builtin, list(reversed(o1)), r1)) != base
    out["OD66-5_F9_row_order_free"] = {"pass": bad == 0 and not rev, "shuffles": 30, "differing": bad + rev}

    # OD66-1: a speed variant with no base never ranks first
    o, rw = _with(orm, raw, _row("x-ai", "x-ai/grok-4.8-fast", 0.5, 1791000000))
    L = build_lineups(builtin, o, rw)
    t = L["x-ai"][0]
    top = min(t, key=lambda c: (t[c][1], c))
    out["OD66-1_speed_variant_without_base"] = {"pass": "grok-4.8-fast" in t and t["grok-4.8-fast"][1] > 1
                                               and t["grok-4.7"][1] == 1, "member": "grok-4.8-fast",
                                               "s": t.get("grok-4.8-fast"), "top": top}

    # OD66-2: X-mini < X, X-nano < X inside the family
    o, rw = _with(orm, raw, _row("openai", "openai/gpt-6-astra-mini", 4.0, 1791000000),
                  _row("openai", "openai/gpt-6.1-sol-mini", 2.0, 1791000000),
                  _row("openai", "openai/gpt-6-luna-nano", 0.1, 1791000000))
    L = build_lineups(builtin, o, rw)
    s = st(L, "openai")
    pairs = [("gpt-6-astra-mini", "gpt-6-astra"), ("gpt-6.1-sol-mini", "gpt-6.1-sol"), ("gpt-6-luna-nano", "gpt-6-luna")]
    oo = [m["canon"] for m in lineup_order([dict(m) for m in L["openai"][1]])]
    out["OD66-2_down_words_inside_family"] = {
        "pass": all(oo.index(a) > oo.index(b) and s[a] <= s[b] for a, b in pairs),
        "order_and_s (variant, base)": {a: ((oo.index(a), s[a]), (oo.index(b), s[b])) for a, b in pairs},
        "note": "strict in the order; in s the variant may share its base's level when the palette's levels are full"}

    # OD66-3: generation (family on the generic scale + legacy cap)
    o, rw = _with(orm, raw, drop=("openai/gpt-6-astra", "openai/gpt-6-astra-pro", "~openai/gpt-astra-latest"))
    bpre = [x for x in builtin if x["model_id"] != "gpt-6-astra"]
    pre = build_lineups(bpre, o, rw)
    tp = pre["openai"][0]
    top_pre = [m["canon"] for m in lineup_order([dict(m) for m in pre["openai"][1]])][:3]
    post = L0["openai"][0]
    order = [m["canon"] for m in lineup_order([dict(m) for m in L0["openai"][1]])]
    fam6 = [c for c in post if c.startswith("gpt-6")]
    legacy = ["o1-pro", "gpt-4", "gpt-3.5-turbo"]
    # strict in the ORDER; in s a legacy member may share the floor level (levels are capped), never sit above
    below = all(order.index(x) > order.index(f) and post[x][0] <= post[f][0] for x in legacy for f in fam6)
    o, rw = _with(orm, raw, _row("openai", "openai/gpt-6.2-mini", 2.0, 1791000000))
    L62 = build_lineups(builtin, o, rw)
    t = L62["openai"][0]
    o62 = [m["canon"] for m in lineup_order([dict(m) for m in L62["openai"][1]])]
    mini_ok = all(o62.index("gpt-6.2-mini") < o62.index(x) and t["gpt-6.2-mini"][0] >= t[x][0]
                  for x in ("gpt-4-turbo", "gpt-3.5-turbo"))
    out["OD66-3_generation"] = {"pass": "sol" in top_pre[0] and top_pre[0].startswith("gpt-6") and below and mini_ok,
                                "top_before_astra": [(c, tp[c][0]) for c in top_pre],
                                "legacy_below_every_gpt6_member": below,
                                "o1_pro_gpt4_gpt35_s": [post[x][0] for x in legacy],
                                "lowest_gpt6_member_s": min(post[f][0] for f in fam6),
                                "gpt-6.2-mini_above_gpt-4-turbo_and_gpt-3.5-turbo": mini_ok}

    # OD66-4: adjacent distinct levels >= 0.02 at e = 1, for every maker with a palette, and the 120-model stress
    worst, n_pairs = (1.0, None), 0
    for lk, (t, _) in L0.items():
        stops = palette_for(lk)[1]
        if not stops:
            continue
        ss = sorted({v[0] for v in t.values()}, reverse=True)
        for a, b in zip(ss, ss[1:]):
            d = dE(shade(stops, a, 1.0), shade(stops, b, 1.0))
            n_pairs += 1
            if d < worst[0]:
                worst = (round(d, 4), lk)
    rows120 = [_row("mistralai", f"mistralai/codex-{i // 10}.{i % 10}", 2.0, 1700000000 + i * 86400 * 3)
               for i in range(120)]
    o, rw = _with(orm, raw, *rows120)
    t = build_lineups(builtin, o, rw)["mistralai"][0]
    ss = sorted({v[0] for v in t.values()}, reverse=True)
    st_m = palette_for("mistralai")[1]
    d120 = min(dE(shade(st_m, a, 1.0), shade(st_m, b, 1.0)) for a, b in zip(ss, ss[1:]))
    out["OD66-4_visible_levels"] = {"pass": worst[0] >= MIN_LEVEL_STEP and d120 >= MIN_LEVEL_STEP,
                                    "adjacent_level_pairs": n_pairs, "min_adjacent_dE_e1": worst,
                                    "synthetic_mistral_plus_120": {"levels": len(ss), "min_adjacent_dE_e1":
                                                                   round(d120, 4)}}

    # OD66-6: off-catalogue placement (IR-3: a withdrawn row stays as a tombstone with its last facts)
    fable6 = lookup("anthropic", "claude-fable-6", L0)
    gone = [r for r in orm if r["id"] == "anthropic/claude-opus-4.1"]
    o, rw = _with(orm, raw, drop=("anthropic/claude-opus-4.1",))
    Lw = build_lineups(builtin, o, rw, tombstones=[(r, raw[r["id"]]) for r in gone])
    w = lookup("anthropic", "claude-opus-4.1", Lw)
    out["OD66-6_off_catalogue_placement"] = {
        "pass": fable6[0] == max(v[0] for v in L0["anthropic"][0].values())
                and w[:3] == L0["anthropic"][0]["claude-opus-4.1"],
        "claude-fable-6_in_CLI_before_catalogue": fable6, "withdrawn_claude-opus-4.1_tombstone": w,
        "claude-opus-4.1_while_listed": L0["anthropic"][0]["claude-opus-4.1"]}

    # OD66-7: a weaker release does not raise stronger members (top-anchored when groups exceed levels)
    o, rw = _with(orm, raw, _row("anthropic", "anthropic/claude-haiku-5", 5.0, 1791000000))
    a1, a0 = st(build_lineups(builtin, o, rw), "anthropic"), st(L0, "anthropic")
    ups = {c: (a0[c], a1[c]) for c in a0 if a1[c] > a0[c] + 1e-12}
    top_kept = all(a1[c] == a0[c] for c in ("claude-fable-5.1", "claude-fable-5", "claude-opus-5.5", "claude-opus-5"))
    out["OD66-7_weaker_release"] = {
        "pass": top_kept, "flagship_and_class_heads_above_kept": top_kept, "residual_raised": ups,
        "note": "levels full (15 groups > 8 levels): the new Haiku head takes the last step from Opus 5 | 4.8, so "
                "older Opus versions merge into Opus 5's level and the Sonnets move up one level. Canvas item (W1)."}

    # OD66-8: aliases
    al = {}
    for r in orm:
        if r["id"].startswith("~"):
            lk = LINEUP_KEY.get(r["maker"], r["maker"])
            tgt = resolve_alias(canonical(r["id"]), lk, L0, price=r.get("completion_price_per_m"))
            mp = next(m["price"] for m in L0[lk][1] if m["canon"] == tgt)
            al[r["id"]] = (tgt, r.get("completion_price_per_m"), mp)
    mism = {k: v for k, v in al.items() if abs(v[1] - v[2]) > 1e-9}
    out["OD66-8_aliases"] = {"pass": all(not any(t in MODIFIER for t in _toks(v[0])) or any(
        t in MODIFIER for t in _toks(canonical(k))) for k, v in al.items()), "resolved": al,
                             "price_mismatch_left": mism}

    # OD66-9: equal (class, version, day, price) -> one shade
    o, rw = _with(orm, raw, _row("mistralai", "mistralai/zephyr-9", 3.0, 1791000000, 128000),
                  _row("mistralai", "mistralai/aurora-9", 3.0, 1791000000, 128000))
    t = st(build_lineups(builtin, o, rw), "mistralai")
    out["OD66-9_equal_keys_one_group"] = {"pass": t["zephyr-9"] == t["aurora-9"], "s": (t["zephyr-9"], t["aurora-9"])}

    # OD66-10: Muse without 'spark'
    o, rw = _with(orm, raw, _row("meta", "meta/muse-1.4", 4.25, 1791000000))
    Lm = build_lineups(builtin, o, rw)
    t, l = st(Lm, "meta"), lv(Lm, "meta")
    out["OD66-10_muse_family"] = {"pass": l["muse-1.4"] == 1 and all(t["muse-1.4"] >= v for v in t.values()),
                                  "muse-1.4": Lm["meta"][0]["muse-1.4"]}

    # OD66-11: continuity of shade in s and e
    worst, worst_black = (0.0, None), (0.0, None)
    for key, stops in {**LOCKED, **DERIVED}.items():
        for e in (0.0, 0.5, 1.0):
            prev = shade(stops, 0.0, e)
            for i in range(1, 501):
                cur = shade(stops, i / 500, e)
                d = dE(prev, cur)
                if key == "Grok":
                    worst_black = max(worst_black, (round(d, 4), key, e, i / 500))
                elif d > worst[0]:
                    worst = (round(d, 4), key, e, i / 500)
                prev = cur
    sb = [shade(DERIVED["baidu"], 0.7415 + k * 0.0005, 0.0) for k in range(2)]  # the round-1 probe (IR-1: 0.0097)
    out["OD66-11_continuity"] = {"pass": worst[0] < 0.006, "largest_dE_for_ds_0.002": worst,
                                 "baidu_e0_s0.7415_ds0.0005": round(dE(*sb), 4),
                                 "grok_near_black": worst_black,
                                 "note": "Grok excluded from the gate: one 8-bit step near black (#000001 -> "
                                         "#000000) is ~0.012 in OKLab; a glide interpolates stops, so it never shows"}

    # OD-0065 / OD-0066 ten-history determinism of the lineup part (orders, batches, a stale-then-fresh merge),
    # before and after a stronger model joins a family
    def ten(o_, rw_):
        digs = set()
        for i in range(10):
            rnd = random.Random(1000 + i)
            oo = list(o_)
            rnd.shuffle(oo)
            if i % 3 == 1:                      # rows arrive in two batches merged in the other order
                k = rnd.randrange(1, len(oo))
                oo = oo[k:] + oo[:k]
            bb = list(builtin)
            rnd.shuffle(bb)
            digs.add(_digest(build_lineups(bb, oo, rw_)))
        return digs
    before = ten(orm, raw)
    o, rw = _with(orm, raw, _row("openai", "openai/gpt-6.2-astra-pro", 60.0, 1791500000),
                  _row("meta", "meta/muse-spark-1.4", 4.25, 1791500000))
    after = ten(o, rw)
    La = build_lineups(builtin, o, rw)
    ta, tm = La["openai"][0], La["meta"][0]
    out["OD-0065_ten_histories"] = {
        "pass": len(before) == 1 and len(after) == 1 and ta["gpt-6.2-astra-pro"][1] == 1
                and ta["gpt-6-astra-pro"][1] > 1 and tm["muse-spark-1.4"][1] == 1 and tm["muse-spark-1.3"][1] > 1,
        "digest_before": sorted(before), "digest_after_stronger_models_join": sorted(after),
        "gpt-6.2-astra-pro": ta["gpt-6.2-astra-pro"], "gpt-6-astra-pro_after": ta["gpt-6-astra-pro"],
        "muse-spark-1.4": tm["muse-spark-1.4"], "muse-spark-1.3_after": tm["muse-spark-1.3"]}

    out.update(round2_checks(builtin, orm, raw, L0))

    # effort steps on the global ladder (informative; PLAN H3 asks >= 0.012): a global ladder has 6 steps where a
    # 3-stop list had 2 under IR-1, so each step is smaller; motion also changes with e (OD-0063). Canvas item W1.
    eff = {}
    for key, stops in {**LOCKED, **DERIVED}.items():
        m = 1.0
        for s in (0.0, 0.5, 1.0):
            sh = [shade(stops, s, k / EFFORT_TOP) for k in range(1, EFFORT_TOP + 1)]
            m = min([m] + [dE(a, b) for a, b in zip(sh, sh[1:])])
        eff[key] = round(m, 4)
    out["effort_ladder_steps_informative"] = {
        "min_adjacent_dE_minimal_to_max": min(eff.values()), "median_of_palette_minima": sorted(eff.values())[len(eff) // 2],
        "palettes_below_0.012": sorted(k for k, v in eff.items() if v < 0.012), "per_palette": eff}
    out["all_pass"] = all(v["pass"] for v in out.values() if isinstance(v, dict) and "pass" in v)
    return out

def round2_checks(builtin, orm, raw, L0):
    """IR-3: the adversarial cases of round 2 (PLAN.md section 14). Every entry has `pass` and the measured values."""
    out = {}
    D = 86400

    def lk_of(r):
        return LINEUP_KEY.get(r["maker"], r["maker"])

    def only(lk, o):                       # one maker's rows (other makers cannot affect its lineup)
        return [r for r in o if lk_of(r) == lk]

    def order(L, lk):
        return [m["canon"] for m in lineup_order([dict(m) for m in L[lk][1]])]

    def top_group(L, lk):
        od = lineup_order([dict(m) for m in L[lk][1]])
        return [m["canon"] for m in od if m["group"] == od[0]["group"]]

    makers = {}
    for r in orm:
        makers.setdefault(lk_of(r), r["maker"])

    # R2-OD66-A: a weaker model released 600 days after the newest member, added to EVERY lineup: the flagship group
    # keeps level 1 and the newcomer never takes it; plus the adversary's five literal cases.
    bad, n = [], 0
    for lk in sorted(L0):
        ms = L0[lk][1]
        newest = max((m["created"] for m in ms if m["created"]), default=0)
        if lk not in makers or not newest:
            continue
        stem = lineup_order([dict(m) for m in ms])[0]["stem"]
        mid = f"{makers[lk]}/{stem}-nano-99"
        o, rw = _with(only(lk, orm), raw, _row(makers[lk], mid, 0.01, newest + 600 * D))
        Lx = build_lineups(builtin, o, rw)
        t, c = Lx[lk][0], canonical(mid, keep_speed=True)
        n += 1
        if any(t[x][1] != 1 for x in top_group(L0, lk)) or t[c][1] == 1:
            bad.append((lk, c, t[c]))
    lit = {}
    for lk, mid, price, day in (("anthropic", "anthropic/claude-haiku-6", 5.0, 20724 + 549),
                                ("google", "google/gemma-5-4b", 0.1, 20698 + 549),
                                ("openai", "openai/gpt-6.5-luna-nano", 0.2, 20725 + 549),
                                ("meta", "meta/muse-glimmer-2-8b", 0.2, 20698 + 549),
                                ("arcee-ai", "arcee-ai/trinity-large-mini", 0.05, 20000 + 580)):
        o, rw = _with(only(lk, orm), raw, _row(lk, mid, price, day * D))
        Lx = build_lineups(builtin, o, rw)
        c = canonical(mid, keep_speed=True)
        tg = top_group(L0, lk)
        lit[c] = {"new": Lx[lk][0][c], "old_flagship": (tg[0], Lx[lk][0][tg[0]])}
    lit_ok = all(v["new"][1] != 1 and v["old_flagship"][1][1] == 1 for v in lit.values())
    out["R2-OD66-A_weaker_newcomer_never_takes_top"] = {"pass": not bad and lit_ok, "lineups": n, "failing": bad[:5],
                                                        "literal_cases": lit}

    # R2-OD66-B: a dated successor is its own member and re-spaces its lineup
    lk = "mistralai"
    l2512 = lookup(lk, "mistralai/mistral-large-2512", L0)
    pre = build_lineups(builtin, [r for r in only(lk, orm) if not r["id"].startswith("mistralai/mistral-large-2512")],
                        raw)
    o, rw = _with(only(lk, orm), raw, _row(lk, "mistralai/mistral-large-2611", 8.0, (20725 + 30) * D))
    L11 = build_lineups(builtin, o, rw)
    od11 = order(L11, lk)
    out["R2-OD66-B_dated_successor"] = {
        "pass": l2512[1] == 1 and pre[lk][0] != L0[lk][0] and L11[lk][0] != L0[lk][0]
                and L11[lk][0]["mistral-large-2611"][1] == 1
                and od11.index("mistral-large-2611") < od11.index("mistral-large-2512") < od11.index("mistral-large"),
        "mistral-large-2512": l2512, "mistral-large (2024 base)": L0[lk][0]["mistral-large"],
        "ministral-3b": L0[lk][0]["ministral-3b"], "lineup_changes_with_2512": pre[lk][0] != L0[lk][0],
        "mistral-large-2611_added": L11[lk][0]["mistral-large-2611"],
        "qwen-plus-2025-07-28": lookup("qwen", "qwen/qwen-plus-2025-07-28", L0),
        "qwen-plus (base)": L0["qwen"][0].get("qwen-plus")}

    # R2-OD66-C: speed variants with no base never take level 1, whatever words they carry
    sp = {}
    spc = (("x-ai", "x-ai/grok-4.8-fast"), ("x-ai", "x-ai/grok-4.8-highspeed"),
           ("x-ai", "x-ai/grok-4.8-ultraspeed"), ("moonshotai", "moonshotai/kimi-k3.1-highspeed"),
           ("openai", "openai/gpt-6.2-astra-pro-fast"), ("anthropic", "anthropic/claude-fable-5.2-fast"),
           ("anthropic", "anthropic/claude-opus-5.6-fast"), ("anthropic", "anthropic/claude-fable-5.2-ultraspeed"))
    for lk, mid in spc:
        o, rw = _with(only(lk, orm), raw, _row(lk, mid, 5.0, (20725 + 5) * D))
        Lx = build_lineups(builtin, o, rw)
        c = canonical(mid, keep_speed=True)
        tg = top_group(L0, lk)
        od = order(Lx, lk)
        sp[c] = {"new": Lx[lk][0][c], "old_flagship": (tg[0], Lx[lk][0][tg[0]][1]),
                 "ranked_below_old_flagship": od.index(c) > od.index(tg[0]) and Lx[lk][0][c][0] <= Lx[lk][0][tg[0]][0],
                 "placed_id_only_before_catalogue": lookup(lk, mid, L0)[:3]}
    # pass: the old flagship keeps level 1 and the variant ranks below it (it may share level 1 only where the
    # palette's levels already merge modifiers, as Astra Pro | Astra under Silver's four levels)
    out["R2-OD66-C_speed_variants"] = {
        "pass": all(v["old_flagship"][1] == 1 and v["ranked_below_old_flagship"]
                    and v["placed_id_only_before_catalogue"][0] <= L0[lk_][0][v["old_flagship"][0]][0]
                    for (lk_, _), v in zip(spc, sp.values())), "cases": sp}

    # R2-OD66-D / R2-2: (a) every catalogue member withdrawn in turn and kept as a tombstone keeps its (s, level);
    # (b) the same member never in any catalogue (id-only placement) never sits above a member it is listed below,
    # except a legacy member with no (class, version) peer (the residual that pack tombstones close); (c) spellings.
    tomb_bad, idonly_up, legacy_resid, n = [], [], [], 0
    for lk, (t, ms) in sorted(L0.items()):
        o_lk = only(lk, orm)
        od = order(L0, lk)
        for m in ms:
            gone = [r for r in o_lk if r["id"] in m["rows"]]
            if not gone:
                continue
            n += 1
            rest = [r for r in o_lk if r["id"] not in m["rows"]]
            Lt = build_lineups(builtin, rest, raw, tombstones=[(r, raw.get(r["id"], {})) for r in gone])
            if lookup(lk, m["canon"], Lt)[:3] != t[m["canon"]]:
                tomb_bad.append((lk, m["canon"]))
            b2 = [x for x in builtin if SEED_CANON.get(canonical(x["model_id"], keep_speed=True),
                                                       canonical(x["model_id"], keep_speed=True)) != m["canon"]]
            Lw = build_lineups(b2, rest, raw)
            if lk not in Lw:
                continue
            s_new = lookup(lk, m["canon"], Lw)[0]
            tw = Lw[lk][0]
            above = [x for x in od[:od.index(m["canon"])] if x in tw]
            if above and s_new > tw[above[-1]][0] + 1e-9:
                (legacy_resid if m["legacy"] else idonly_up).append((lk, m["canon"], t[m["canon"]][:2], s_new))
    spell = [("anthropic", "claude-opus-4.5", ["anthropic/claude-opus-4.5", "claude-opus-4-5-20251101",
                                              "claude-4.5-opus-high-thinking", "claude-4.5-opus", "opus-4.5",
                                              "claude-opus-4.5-high"]),
             ("anthropic", "claude-sonnet-4.5", ["claude-4.5-sonnet-thinking", "sonnet-4.5"]),
             ("anthropic", "claude-opus-5.5", ["claude-5.5-opus-high-thinking", "claude-opus-5.5-high",
                                              "claude-opus-5.5-max"]),
             ("openai", "gpt-5.1-codex", ["gpt-5.1-codex-high"]),
             ("openai", "gpt-5.1-codex-max", ["gpt-5.1-codex-max-high"]),
             ("openai", "gpt-5", ["gpt-5-high"]), ("x-ai", "grok-4.7", ["grok-4.7-high"]),
             ("meta", "muse-spark-1.2", ["muse-spark-1.2-high"]), ("google", "gemini-3.1-pro", ["gemini-3.1-pro-high"])]
    sp_bad = [(lk, c, x, lookup(lk, x, L0)[:3]) for lk, c, xs in spell for x in xs
              if lookup(lk, x, L0)[:3] != L0[lk][0][c]]
    out["R2-OD66-D_withdrawn_and_spellings"] = {
        "pass": not tomb_bad and not idonly_up and not sp_bad, "members_withdrawn": n,
        "tombstone_level_changes": tomb_bad[:5], "id_only_above_a_listed_stronger_member": idonly_up[:5],
        "residual_legacy_without_tombstone_or_peer": {"count": len(legacy_resid), "examples": legacy_resid[:4]},
        "spellings_checked": sum(len(xs) for _, _, xs in spell), "spelling_mismatches": sp_bad}

    # R2-OD66-E: the owner's literal example (OD-0066): Sol > Terra > Luna before Astra; Astra > Sol > Terra > Luna after
    o, rw = _with(only("openai", orm), raw,
                  drop=("openai/gpt-6-astra", "openai/gpt-6-astra-pro", "~openai/gpt-astra-latest"))
    pre = build_lineups([x for x in builtin if x["model_id"] != "gpt-6-astra"], o, rw)["openai"][0]
    post = L0["openai"][0]
    names = ["gpt-6-astra", "gpt-6.1-sol", "gpt-5.6-terra", "gpt-6-luna"]
    sb, sa = [pre[c][0] for c in names[1:]], [post[c][0] for c in names]
    out["R2-OD66-E_owner_example_astra_sol_terra_luna"] = {
        "pass": all(a > b for a, b in zip(sb, sb[1:])) and all(a > b for a, b in zip(sa, sa[1:])),
        "before_astra": {c: pre[c] for c in ["gpt-6.1-sol-pro"] + names[1:] + ["gpt-3.5-turbo"]},
        "after_astra": {c: post[c] for c in ["gpt-6-astra-pro"] + names + ["gpt-3.5-turbo"]}}

    # R2-OD66-F: a plain-named next generation ranks first; its down-word sibling does not
    o, rw = _with(only("openai", orm), raw, _row("openai", "openai/gpt-7", 80.0, (20725 + 60) * D),
                  _row("openai", "openai/gpt-7-mini", 4.0, (20725 + 60) * D))
    L7 = build_lineups(builtin, o, rw)
    od7 = order(L7, "openai")
    out["R2-OD66-F_plain_next_generation"] = {
        "pass": L7["openai"][0]["gpt-7"][1] == 1
                and all(od7.index("gpt-7") < od7.index(c) for c in od7 if c.startswith("gpt-6"))
                and od7.index("gpt-7-mini") > od7.index("gpt-6-luna"),
        "gpt-7": L7["openai"][0]["gpt-7"], "gpt-7-mini": L7["openai"][0]["gpt-7-mini"],
        "gpt-6-astra-pro_after": L7["openai"][0]["gpt-6-astra-pro"]}

    # R2-3: every effort word a provider emits maps to a ladder word; synonyms give the same e
    unknown = [w for w in PROVIDER_EFFORT_WORDS if not effort_word_known(w)]
    for r in builtin:
        if isinstance(r.get("efforts"), list):    # live-only rows describe their list in prose
            unknown += [w for w in r["efforts"] if not effort_word_known(w)]
    pairs = {f"{a}={b}": (effort_e(a), effort_e(b)) for a, b in (("xhigh", "extra-high"), ("xhigh", "extra_high"),
                                                                  ("xhigh", "Extra High"), ("max", "ultra"))}
    out["R2-3_effort_synonyms"] = {"pass": not unknown and all(a == b for a, b in pairs.values()),
                                   "unknown_words": sorted(set(unknown)), "pairs_e": pairs}
    return out

# --------------------------------------------------------------------------------------------- measurement

def grid(stops, n_s=4, n_e=5):
    S = [i / (n_s - 1) for i in range(n_s)]
    E = [j / (n_e - 1) for j in range(n_e)]
    return {(s, e): shade(stops, s, e) for s in S for e in E}, S, E

def measure(lineups, brows):
    out = {"per_palette_4_models_x_5_efforts": {}}
    clouds = {}
    for key, stops in {**LOCKED, **DERIVED}.items():
        g, S, E = grid(stops)
        model_steps = [dE(g[(S[i], e)], g[(S[i + 1], e)]) for i in range(len(S) - 1) for e in E]
        eff_steps = [dE(g[(s, E[j])], g[(s, E[j + 1])]) for s in S for j in range(len(E) - 1)]
        diag = [dE(g[(S[i + 1], E[j])], g[(S[i], E[j + 1])]) for i in range(len(S) - 1) for j in range(len(E) - 1)]
        allpairs = [dE(g[a], g[b]) for a in g for b in g if a < b]
        out["per_palette_4_models_x_5_efforts"][key] = {
            "model_step_min": round(min(model_steps), 4), "model_step_median": round(sorted(model_steps)[len(model_steps) // 2], 4),
            "effort_step_min": round(min(eff_steps), 4), "effort_step_median": round(sorted(eff_steps)[len(eff_steps) // 2], 4),
            "diagonal_min": round(min(diag), 4), "closest_cells": round(min(allpairs), 4),
            "anchor_exact": shade(stops, ANCHOR_S, 1.0) == stops,
            "ceiling_alt_exact_at_flagship": shade(stops, 1.0, 1.0, anchor_s=1.0) == stops}
        clouds[key] = list(g.values())
    keys = list(clouds)
    cross = []
    for i in range(len(keys)):
        for j in range(i + 1, len(keys)):
            d = min(dE(a, b) for a in clouds[keys[i]] for b in clouds[keys[j]])
            cross.append((round(d, 4), keys[i], keys[j]))
    cross.sort()
    out["cross_maker_closest_cells"] = [{"d": d, "a": a, "b": b} for d, a, b in cross[:14]]
    # the owner's example: Sonnet at High vs Opus at Low (Claude CLI rows)
    cl = {r["model_id"]: r for r in brows if r["provider"] == "Claude" and "efforts" in r}
    def cell(mid, lv):
        return next(c for c in cl[mid]["efforts"] if c["effort"] == lv)["stops"]
    out["owner_example"] = {
        "sonnet_high_vs_opus_low": round(dE(cell("sonnet", "high"), cell("opus", "low")), 4),
        "sonnet_max_vs_opus_low": round(dE(cell("sonnet", "max"), cell("opus", "low")), 4),
        "claude_cli_s": {k: v["s"] for k, v in cl.items()}}
    return out

def draw_swatch():
    from PIL import Image, ImageDraw
    keys = ["Claude", "Silver", "Grok", "Zai", "Antigravity", "Copilot", "Deepseek", "Meta", "Cursor", "Opencode",
            "Pi", "Commandcode", "qwenlm", "mistral", "nvidia", "moonshot"]
    cw, ch, gap = 34, 18, 6
    W = 160 + 4 * (5 * cw + gap)
    H = 30 + len(keys) * (ch + 8)
    for theme, bg in (("dark", (24, 24, 28)), ("light", (246, 246, 248))):
        im = Image.new("RGB", (W, H), bg)
        d = ImageDraw.Draw(im)
        fg = (220, 220, 220) if theme == "dark" else (30, 30, 30)
        for si, s in enumerate((0, 1 / 3, 2 / 3, 1)):
            d.text((160 + si * (5 * cw + gap), 8), f"s={s:.2f}  e: 0 .. 1", fill=fg)
        for ki, k in enumerate(keys):
            y = 30 + ki * (ch + 8)
            d.text((6, y + 3), k, fill=fg)
            stops = LOCKED.get(k) or DERIVED[k]
            for si, s in enumerate((0, 1 / 3, 2 / 3, 1)):
                for ei in range(5):
                    sh = shade(stops, s, ei / 4)
                    x0 = 160 + si * (5 * cw + gap) + ei * cw
                    for xi in range(cw - 2):
                        t = xi / (cw - 3)
                        c = unhex(sh[0]) if t < 0.33 else unhex(sh[1]) if t < 0.66 else unhex(sh[2])
                        d.line([(x0 + xi, y), (x0 + xi, y + ch)], fill=c)
        im.save(os.path.join(HERE, f"intensity-swatch-{theme}.png"))

if __name__ == "__main__":
    main(swatch="--swatch" in sys.argv)
