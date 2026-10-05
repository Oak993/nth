# Premium options: the data shape (animated wallpapers)

Written first by the options agent. The tier agents write `walls/<id>.glsl` and `catalog_<tier>_2.py`; the options agent owns
`catalog.py`, `common.glsl`, `lib-gl.js`, `lib-page.js`, `lib-page.css`, `build_page.py`, `check.cjs` and `wallpaper_kit.py`.
Current `common.glsl`, `lib-gl.js` and `wallpaper_kit.py` already carry everything below (updated 2026-10-05); copy them again
into a scratch preview. The page controls (panel) arrive later; in a scratch preview only the shaders are exercised.

## 1. The catalogue file

`src/catalog_<tier>_2.py` (tier = vibrant | balanced | minimal):

```python
from wallpaper_kit import wall, dyn
WALLS = [
    wall('id', 'Name', 'vibrant', 60, 'One sentence.',
         roles,                       # six (Ld, Cd, Ll, Cl) tuples, exactly as in catalog.py
         packs,                       # five tuples (name, hues, ck, lk, dh, x), exactly as in catalog.py
         ('glow', 'Glow', 'Soft', 'Bright', .55),     # the wallpaper's own second option
         motion=.5,
         dynamic=dyn('sky'),          # section 2
         perspective='layers',        # section 3: 'layers' or 'pan'
         focus=0.0,                   # section 4
         match=dict(...)),            # section 5, or None
]
```
`wall()` returns a plain dict: id, name, tier, period, desc, roles, packs, options (two dicts), dynamic, perspective, focus, match.
`catalog.py` imports each present `catalog_<tier>_2.py` and appends its `WALLS`. The shader is `walls/<id>.glsl`, whose
`const float T` must equal the period. Ids must be unique across the library. Packs stay five named packs; the sixth,
Match accent, is built at run time by the host (section 5). Do not add it to `packs`.

## 2. Dynamic: follow the time of day (on or off, default off)

Four looks at four key times on the local clock: dawn 06:00, day 12:00, dusk 18:00, night 24:00 (= 00:00). The look at any
time is the smoothstep blend of the two neighbouring keys, cyclic (night 00:00 -> dawn -> day -> dusk -> night).

A look changes the six palette colours in OKLCH: `dL` (lightness added), `kC` (chroma multiplied), `dh` (hue rotated, degrees),
`tint = (hue, amount)` (each colour is mixed that fraction of the way toward a colour of that hue and chroma 0.12, in the
OKLab a/b plane, as a photo filter does, then 60% of the chroma the mix took away is given back; a near-grey ground gains a
tint). Each of `dL`, `kC`, `dh` is a number or a list of six (one per palette slot). Order of operations in the host: tint,
chroma, hue turn, lightness.

There are two sets of looks, so that dark mode leans night and light mode leans day: in the `light` set the day look is
empty (the light theme's own palette); in the `dark` set the night look is empty (the dark theme's own palette). The host
mixes the two sets by the eased theme value, applies the result to the palette, brings it into sRGB gamut by lowering chroma
only, and eases the palette (0.25 s) like any other change. Dynamic off eases back to the plain pack.

Light direction: a track of four (dx, dy) offsets in p units, one per key, the same in both themes, which the host passes as
`u_sun` (eased). A wallpaper that has a light (a sun, a shaft, a beam) adds `u_sun` to its own light position or direction
(`vec2 sun = vec2(0.65, 0.28) + u_sun;`), so that offset (0, 0) is its designed look. A wallpaper with no light direction
passes `sun=None` (all zero) and never reads `u_sun`.

Normalised data written to `wallpapers.json` (`dynamic`):
```
{ "light": {"dawn": {"dL": [6], "kC": [6], "dh": [6], "tint": [hue, amount]}, "day": ..., "dusk": ..., "night": ...},
  "dark":  { same four keys },
  "sun":   {"dawn": [dx, dy], "day": [dx, dy], "dusk": [dx, dy], "night": [dx, dy]} }
```
Author it with `dyn(preset, scale, sun, light, dark)` from `wallpaper_kit.py`:
- `dyn('sky')` pink dawn, clear day, violet dusk, blue night; `dyn('warm')` film and desert warmth; `dyn('glow')` for
  self-luminous pictures, which change less. `scale` 0.6 for the minimal tier (calmer), 1.0 balanced, up to 1.3 vibrant.
- `sun='track'` the standard track (dawn left and low, day at the designed place, dusk right and low, night below), or a dict
  of the four keys to (dx, dy).
- `light={'dusk': {'tint': (20, .3), 'dL': -.04}}` and `dark={...}` replace single fields of a look.
Keep every look inside "quiet to rich" taste: no look may turn a wallpaper muddy or brown, and the night look of a light
wallpaper must still carry its idea. The checks render each wallpaper at the four keys, dark and light, and report the
lightness and chroma range and the loop proof.

## 3. Perspective

`perspective='layers'`: the wallpaper has depth. Its `wall()` shifts each layer by `tiltD(depth)` from `common.glsl`
(depth -1 farthest .. +1 nearest; add the result to `p` before evaluating that layer). The default is ON. At full tilt the
nearest layer moves 0.010 p units (4 px at 800 px high), the farthest the other way. `tiltD` reads the uniform
`vec2 u_tilt` (each component -1..1, eased, (0, 0) when Perspective is off or under Reduce Motion). Declare layers only if
`wall()` really calls `tiltD(`; the build checks that `layers` <=> the source contains `tiltD(`.

`perspective='pan'`: a flat wallpaper (default OFF). It does not read `u_tilt`; the host adds `u_tilt * 0.012` to `p` once
(the uniform `u_pan`), a pan of at most 5 px, only if the viewer turns Perspective on.

Rules: the tilt is applied to `p` only, never to `t`, so loops stay exact; keep the layers' shift continuous (no seams at
the window edge: noise and ridges are unbounded in p, so use them for the moving layers); under Reduce Motion `u_tilt` is
always (0, 0). The page drives `u_tilt` from the pointer over the large view; the app drives it from the window's position
on the screen (window centre minus screen centre, over half the screen size, clamped to -1..1), eased with a 0.35 s time
constant.
Data written to `wallpapers.json`: `perspective: {"mode": "layers" | "pan", "default": true | false}`.

## 4. Focus (sharp to dreamy)

A control from 0 (sharp) to 1 (dreamy), default `focus` (0 unless the wallpaper is meant to sit a little soft; at most 0.6).
The host does it: the picture is drawn, then blurred with a Gaussian of sigma = 0.035 * H * focus^1.4 pixels (a mip-mapped
disc blur here; in the app, a Skia blur of that sigma on the wallpaper layer). It costs nothing for a sharp wallpaper, and
the blur only starts above 0.35 px. The uniform `float u_focus` (0..1, eased) is also given to the shader: the optional hook
is `edgePx()` in `common.glsl` (`u_px * (1.0 + 14.0 * u_focus)`), to be used instead of `u_px` for the width of a hard edge
so that crisp edges soften before the blur and no aliasing shows. No other work is needed in a wallpaper.
Data: `focus: {"default": number}`.

## 5. Match accent (the sixth pack)

Built at run time in OKLCH from the system accent colour (the page uses #005FB8 and an accent picker). The accent gives a hue
`ha` and a chroma `Ca`. Slot i gets hue `ha + off[i]`; the lightness and chroma of each slot are the wallpaper's own
dark and light roles (the same `roles` as the named packs), with the chroma factor `ck * clamp(Ca / 0.15, 0.55, 1.3)` and the
same optional extras as a named pack (`lk`, `dl`, `dc`, `cm`). Colours are brought into gamut by lowering chroma only, exactly
as for the named packs, and the pack crossfades in OKLab like any other. In the dark palette the host also applies the `dark_warm` rule, to every wallpaper, so that a warm accent never gives a brown or
olive dark ground (the defaults are `DARK_WARM` in `catalog.py`; a wallpaper may override single fields in `match['dark_warm']`):
with the hue h in -180..180 and w = smoothstep(band[0], band[1], h) * (1 - smoothstep(band[2], band[3], h)) (red through yellow),
slots whose dark role lightness is under `ground_below` turn toward `ground_hue` (plum) by w, slots under `mid_below` turn
toward `mid_hue` (rose) by `mid_amount` * w, both with chroma x (1 + (kc - 1) w) and lightness + dl * w; brighter slots keep the hue
with lightness + bright_dl * w and chroma x (1 + (bright_kc - 1) w). The light palette is never changed.
Data written to `wallpapers.json`: `match: {"off": [6], "hero": n, "ck": x, "lk": x, "dl": [6], "dc": [6], "cm": [6], "dark_warm": {...}}` and
`roles: [[Ld, Cd, Ll, Cl] x 6]` (the host builds the pack from these).

## 6. What the build checks (run `python catalog.py` in your scratch copy)

The shader against the SkSL subset (as before); the period; unique ids; five packs; `match` offsets six long with offset 0 at the
hero; the look ranges. `perspective` <=> `tiltD(` in the source, and a non-zero `sun` track <=> `u_sun` in the source, are
reconciled with a printed WARNING (the build follows what the shader really reads), so read the build output. The page check
adds: the loop proof (frame 0 against frame T, at most 1/255) for every pack, dark and light, including Match accent, with
the options at both ends, Dynamic at all four keys, Perspective at full tilt and Focus at 1; the frame at T/2 differing; the
frame cost (at most 0.5 ms), also with Focus at 1.
