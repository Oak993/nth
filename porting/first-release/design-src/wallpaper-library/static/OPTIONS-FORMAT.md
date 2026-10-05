# Static wallpapers: the data for the four premium options (OD-0110, second amendment)

Written by the options agent. Tier agents add these fields to each `SW.add({...})` entry in `walls-<tier>-2.js`. Nothing else in the entry changes: `id, name, tier, grain, desc, opt, roles, packs, src` stay as today. Never edit shared files (sw-core.js, sw-gl.body.js, sw-page.*, make_json.cjs, build_page.py, check.cjs).

Per wallpaper you add ONE required field (`dyn`) and three optional ones (`light`, `persp`, `focus`). Everything is data; the engine does the work.

```js
SW.add({
  id: 'example', name: 'Example', tier: 'balanced', grain: .3,
  desc: '...', opt: { label: 'Softness', def: .5 }, roles: [...], packs: [...], src: `...`,

  // 1. Dynamic: REQUIRED. Four looks, one per key time of the local clock.
  dyn: {
    dawn:  { l:  .03, c: 1.10, h: -10, light: [-.90, .25] },
    day:   { l:  .00, c: 1.00, h:   0, light: [-.35, .90] },
    dusk:  { l: -.03, c: 1.15, h:  14, light: [ .90, .20] },
    night: { l: -.10, c:  .75, h:  -8, light: [ .20, .85] },
  },
  // OPTIONAL. The wallpaper's authored light direction, and whether its src reads u_light itself.
  light: { def: [-.35, .90], own: false },
  // 3. Perspective. OPTIONAL (default { def: 0, depth: .3, own: false }).
  persp: { def: 1, depth: .8, own: false },
  // 4. Focus. OPTIONAL (default { def: 0, own: false }).
  focus: { def: 0, own: false },
});
```

## 1. Dynamic (`dyn`, required)

Four keys, each `{ l, c, h, light }`, at fixed clock times: `night` 00:00 (and 24:00), `dawn` 06:00, `day` 12:00, `dusk` 18:00. The engine eases between them around the clock (smooth, cyclic, no cut).

| field | meaning | range |
|---|---|---|
| `l` | lightness shift, added to the OKLab L of every role colour | -0.18 .. +0.12 |
| `c` | chroma multiplier on every role colour | 0.5 .. 1.5 |
| `h` | hue shift in degrees, added to every role colour (warm toward dawn and dusk, cool toward night) | -40 .. 40 |
| `light` | optional: direction TOWARD the light in picture space, x right, y up, roughly unit length; omit to keep `light.def` | each component -1.2 .. 1.2 |

How the engine uses them (so you can pick numbers):
- The values are looks, not deltas from the pack: the engine subtracts the theme's home look, so the authored palette is shown EXACTLY at the home time (light mode at 12:00 = `day`, dark mode at 00:00 = `night`). Dark mode therefore leans night and light mode leans day. In light mode at night the palette is darker and cooler by `night - day`; in dark mode at noon it is lighter and warmer by `day - night`.
- So write the four looks as the same wallpaper seen at four hours: `day` is the brightest, cleanest, `night` the darkest and calmest (lower `c`), `dawn` and `dusk` the warm, rich ones (higher `c`, hue pushed toward warm: for cool packs the engine still moves the hue by the same degrees, so keep `h` small, at most about 15 for dawn/dusk; the sign is yours to choose per wallpaper: dawn a little pink or amber, dusk a little magenta or violet).
- `night.l - day.l` should be between -0.06 and -0.18 (the check requires the night render to be darker than the day one, in both themes). Keep every pair of looks visibly different (the check requires it) but never muddy: no brown.
- The light direction moves a soft brightness wash across the picture, left-to-right through the day, for wallpapers that do not read `u_light` themselves (`light.own` false). Typical: dawn from the low left `[-.9, .25]`, day from high `[-.35, .9]`, dusk from the low right `[.9, .2]`, night `[.2, .85]` (moon, high and faint). A flat wallpaper (a gradient, a field of grain) may omit `light` in every key.
- `light: { def: [x, y], own: true }`: set this when your `src` has a light/sun/key direction. Then READ the uniform `u_light` (vec2, the current direction toward the light, it equals `def` when Dynamic is off and at the theme's home time) instead of a literal vector in `wall()`. Normalise it where you need unit length: `vec2 L2 = normalize(u_light);`. The engine then does no wash of its own. If `own` is false or `light` is absent, the engine does the wash.
- Also available: `u_dark` is eased by the engine from the theme and the clock (dark mode at noon gives a lower `u_dark`, so stars fade), you need nothing for that.

## 2. Match accent (no data)

A sixth pack, built at run time from a system accent colour (the page: #005FB8 by default, with a picker). You write nothing; this is how your roles map, so write roles that survive it:
- `roles[0..4]` are the colour ramp and `roles[5]` is the ground. The accent's lightness picks the ramp role whose L is closest to it (dark and light separately) as the anchor: that role takes the accent's exact hue.
- The other roles keep the hue OFFSETS of your FIRST pack (`packs[0]`), compressed to half and capped at +/-60 degrees, so a wide wallpaper stays wide and a restrained one stays restrained: the accent's hue family plus harmonious analogous companions in OKLCH.
- Every role keeps its own L and its own chroma (`roles[i]`), the chroma scaled by accent chroma / 0.16 (clamped 0.3 .. 1.15; a grey accent gives a quiet wallpaper). The dark-mode brown rules of `resolvePack` still apply.
- Therefore: make `packs[0]` a faithful, colourful standard pack whose hues span the wallpaper's real colour roles in order; keep roles' L ordering meaningful (ramp rises from role 0 to role 4); never rely on an explicit-hex pack (`x`) as `packs[0]`, or Match accent has no hue offsets to compress and falls back to even steps of 14 degrees.

## 3. Perspective (`persp`, optional)

`{ def, depth, own }`
- `def`: 1 = on by default (wallpapers with depth: layers, ridges, panes, spheres, sky over land), 0 = off by default (flat things: grain fields, plain washes, paper, a plain gradient). The viewer can always switch it. Under Reduce Motion it is off and the control is disabled.
- `depth`: 0 .. 1, the strength. The whole picture moves by at most `0.015 * depth` height units (about 6 px at 800 px high for depth 1), following the pointer. Keep depth between 0.3 and 1. The check bounds the displacement.
- `own`: false (default) = the engine shifts the whole picture (the wallpaper plane moves relative to the glass in front of it: a true parallax against the window). true = your `src` does its own layered parallax, reading the uniform `u_tilt` (vec2, height units, already scaled by depth, the viewer's switch, the pointer and the 0.015 cap, so |u_tilt| <= 0.015) and the engine does NOT shift the picture. Do it per layer by a factor f: far layer 0.0, near layer 1.0, `vec2 pk = p + u_tilt*f;`, then evaluate that layer at `pk`. Never multiply `u_tilt` by more than 1.5 in total (the check renders at the corners and bounds the change).
- `u_tilt` is always (0, 0) when perspective is off, so a shader written with `own: true` costs nothing and looks identical at rest.

## 4. Focus (`focus`, optional)

`{ def, own }`
- A slider from sharp (0) to dreamy (1). `def` is the starting value for this wallpaper, 0 for nearly all (sharp as authored); give a dreamy wallpaper such as a mist or a glow at most 0.15.
- `own: false` (default): the engine blurs the rendered wallpaper with a cheap two-pass gaussian (sigma up to 2.2 percent of the height) plus a faint soft-screen glow (so even a smooth gradient visibly softens), and re-applies the film grain on top of it. You write nothing.
- `own: true`: your `src` reads the uniform `u_focus` (float 0..1) and softens itself (wider falloffs, larger feather, fewer fine details) and the engine adds no blur. Only for wallpapers where a post blur would look wrong (hard fine texture such as linen weave or fibre where a blur would turn it to mud; a mathematical softness is better). The check renders the extreme and compares it to a blurred sharp render; an `own` wallpaper must still change visibly and stay in budget.
- `u_focus` exists for every wallpaper (it is 0 whenever you do not use it).

## Uniforms the engine adds to the head (declared for you; never redeclare)

`uniform vec2 u_tilt; uniform float u_tg; uniform vec2 u_light; uniform vec2 u_light0; uniform float u_lw; uniform float u_focus;`
`u_tg` (1 when the engine shifts the picture, 0 when `persp.own`), `u_light0` (= `light.def`) and `u_lw` (the engine's wash strength) are used by main(); a wallpaper only ever reads `u_tilt`, `u_light` and `u_focus`.

## What the checks require of a new wallpaper (so you can pass them)

- `dyn` has all four keys with finite values in the ranges above; four key renders are pairwise different; night is darker than day (mean luminance, dark and light theme); no render goes flat or black.
- Match accent: renders in all ten preset accents, never flat or black, its role hues land within the accent's family.
- Perspective: |u_tilt| <= 0.015; the render at the four corner tilts differs from the rest render by a small bounded amount (and by exactly 0 when `depth` is 0).
- Cost: the single render at 2560 x 1600 stays within the 30 ms budget with Dynamic on, Match accent, tilt at a corner and Focus at 1 (the blur is a separate cheap pass).
