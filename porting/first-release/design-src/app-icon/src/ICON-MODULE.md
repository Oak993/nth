# App-icon module contract (decision page D-ICON, 2026-10-05)

The page `app-icon.html` shows twenty app-icon directions for Marrow, letters A to T. Designer 1 owns the page and
A to J; designer 2 writes K to T. Every icon is one self-contained module that the page inlines into its single-file
share copy. This file is the exact contract. If something here is unclear, follow the A to J modules in `icons-a-j\`
as worked examples.

## 1. The file

- Path: `src\icons-k-t\<Letter>-<id>.js`, for example `src\icons-k-t\K-tidepool.js`. `id` is lower-case kebab.
- A classic script (no `import`/`export`, no top-level `await`). Wrap everything in an IIFE; define no globals.
- It makes exactly one call: `MarrowIcons.register({...})` (section 2).
- Allowed globals: `MarrowIcons`, `IconKit` (section 5), `document.createElement('canvas')` for offscreen work,
  `Math`, `Path2D`, the Canvas 2D API.
- Forbidden: images, fonts, `fetch`/XHR, any URL, `eval`/`new Function`, `localStorage`, timers
  (`setTimeout`, `requestAnimationFrame`: the page owns the clock), `Math.random` (use `IconKit.rng(seed)`),
  `ctx.filter` directly (Safari lacks it: use `IconKit.blur`), text glyphs as the mark (no `fillText`).
- Size: under 40 KB per module. ASCII only (write non-ASCII in comments as plain words).

## 2. The registration object

```js
(function () {
  const K = IconKit;
  MarrowIcons.register({
    letter: 'K',                       // A..T
    id: 'tidepool',                    // as in the file name
    name: 'Tidepool',                  // a short name, one or two words
    meaning: 'One line: what the icon says about the app.',
    story: 'Two or three sentences: the metaphor, the material, the form and the colour story.',
    swatch: ['#0b1020', '#6aa8ff', '#9d7bff'],   // 3 colours that summarise it (the comparison row uses them)
    launch: { ms: 1000, note: 'One sentence: how the mark assembles when the app opens.' },  // ms 800..1200
    idle: { ms: 4800, note: 'One sentence: the subtle living loop.' },  // or idle: null for none
    draw(ctx, size, theme, t) { /* section 3 */ },
  });
})();
```

## 3. draw(ctx, size, theme, t)

- `ctx`: a `CanvasRenderingContext2D`. The page has already cleared it and set a transform so that **one unit is one
  pixel of the icon at that size**: draw into the square `0..size` by `0..size`. Never call `setTransform`,
  `resetTransform` or clear the canvas; wrap your work in `ctx.save()`/`ctx.restore()`. You may read
  `ctx.getTransform().a` to know the device scale (1 at true Windows pixels, 2 to 4 on the hero and loupes).
- `size`: the icon's design size in Windows pixels: **16, 24, 32, 48, 64, 96, 256 or 512** (512 is the hero; the
  contexts also ask for 20, 40 and 72). Treat any value by its band (section 4).
- `theme`: `'dark'` or `'light'`, the surface the icon sits on (taskbar, Start, desktop). The identity must not
  change between themes; you may adjust only rim contrast, the cast shadow, or the plate's edge so it reads on both.
- `t`: the animation state, always this object shape:
  - `t.launch`: 0 to 1, linear progress of the launch (apply your own easing). **`t.launch = 1` is the still
    icon**, and launch must end on exactly the still frame (no snap when the page swaps to the still canvas).
    `t.launch = 0` is the first frame: usually nothing or almost nothing.
  - `t.idle`: seconds since the idle loop started (0 when still or during launch). The loop must be seamless: the
    frame at `t.idle = idle.ms/1000` equals the frame at `t.idle = 0`, and both equal the still frame. Use
    `K.loop(t.idle, idle.ms)` to get a 0..1 phase and drive everything with sines of `2*PI*phase` (or whole
    multiples of it).
  - `t.still`: true when the page wants the still icon (launch 1, idle 0); a convenience only.
- Draw must be deterministic: the same arguments always give the same pixels.
- Performance: at size 512 and device scale 2, one `draw` must take under 6 ms on a desktop PC (the hero animates at
  60 fps). Cache expensive static layers in an offscreen canvas keyed by size, theme and device scale
  (`K.cache(key, w, h, fn)` does this for you).

## 4. Size bands: what Windows really shows

| Band | Sizes | Rule |
| --- | --- | --- |
| tiny | 16, 20 | A dedicated simplification. At most 2 or 3 shapes, solid fills and one gradient, strokes at least 1.5 px, no blur, no hairline rims, no shadow. Snap edges to whole pixels (`K.px`). It must read as the same mark at a glance. |
| small | 24, 32 | A lighter simplification: the main forms, one highlight, no fine detail under 1 px, no cast shadow. |
| medium | 40, 48, 64, 72, 96 | The full mark with its material, a reduced specular and a soft contact shadow. |
| large | 256, 512 | Everything: refraction, specular, inner shadows, grain if any. |

`K.band(size)` returns `'tiny' | 'small' | 'medium' | 'large'`. The page shows 16 and 24 at true pixels and as an
8x pixel loupe, so they are judged as Windows draws them.

Silhouette: Windows 11 app icons are free-form (no mandated plate), so a mark may be a plate (squircle, circle) or
a free shape. Keep the art inside the square with a margin: at large and medium about 6% of `size` on each side
(the cast shadow may use the margin); at tiny and small 1 px. `K.box(size)` returns `{x, y, w, h}` of that safe area.

## 5. IconKit helpers (window.IconKit, in src\iconkit.js)

```
K.band(size)                         -> 'tiny'|'small'|'medium'|'large'
K.box(size)                          -> {x, y, w, h}  the safe area for the art
K.px(v)                              -> v rounded to the device pixel grid (for tiny/small edges)
K.squircle(ctx, x, y, w, h, r, n)    -> adds a superellipse-cornered rect path (r = corner radius, n = smoothing 0..1, default 0.6)
K.circle(ctx, cx, cy, r)             -> adds a circle path
K.lin(ctx, x0, y0, x1, y1, stops)    -> a linear gradient; stops = [[0,'#fff'],[1,'#000']]
K.rad(ctx, cx, cy, r, stops, fx, fy, fr) -> a radial gradient (focus fx, fy, fr optional)
K.blur(ctx, radius, drawFn)          -> runs drawFn(ctx) blurred by radius (CSS px); uses ctx.filter where supported, else a shadow trick
K.glow(ctx, color, blur, drawFn)     -> runs drawFn(ctx) with a coloured shadow glow
K.cache(key, w, h, fn)               -> an offscreen canvas of w x h device pixels drawn once by fn(octx); returns the canvas
K.layer(ctx, key, size, fn)          -> draws a cached static layer: fn(octx) draws in icon units exactly like draw(); cached per key, size and device scale
K.scale(ctx)                         -> the current device scale (1 at true Windows pixels)
K.ease.mac(u) / .sine(u)             -> the macOS standard curve cubic-bezier(.32,.72,0,1) / a sine in-out
K.rng(seed)                          -> a seeded random () => 0..1
K.clamp(v, a, b), K.lerp(a, b, u), K.mix(hexA, hexB, u) -> numbers / 'rgb(...)'
K.seg(p, a, b)                       -> local 0..1 progress of p within [a, b], clamped
K.ease.out(u) / .inOut(u) / .back(u) / .expo(u) / .spring(u) -> premium easing curves, all 0 at 0 and exactly 1 at 1
K.loop(seconds, ms)                  -> the idle phase 0..1
K.alpha(color, a)                    -> the colour with alpha a
```

## 6. Quality bar (what the owner will judge)

- Owner, 2026-10-05: no icon is anchored on the old mark. Do not reuse its arcs, its colour set or its bright core, and never mention the old mark or its colours in any text field. Any metaphor, material, palette and form is open.

- Apple-grade, Windows 11 Fluent-grade or Samsung One UI-grade: real material, light and depth. Nothing flat, no
  clip-art, no generic AI sparkle, no robot, no chat bubble, no brain, no `</>` brackets.
- Original: never imitate a real company's mark (Claude's starburst, OpenAI's knot, Apple, Microsoft, Google,
  Samsung, GitHub, VS Code, Cursor and so on).
- One idea per icon, readable at 16 px.
- Launch 0.8 to 1.2 s, premium easing (no linear moves, no bounce for its own sake), nothing pops in on one frame:
  every element fades, grows or travels in. Idle is subtle: a breath, a slow light sweep, a drift; never a spinner.
- Reduce Motion is the page's job (it shows the still or a fade); the module only answers `draw`.

## 7. Testing your module

Open `src\harness.html?m=icons-k-t/K-tidepool.js` in Chrome (file:// is fine). It shows every size, the 16 and 24
loupes, both themes, and buttons to replay the launch and run the idle loop, with a frame-time readout. Fix
anything it reports in red. Then write DONE for that letter in `E:\Droppy\work\scratch\handoff\app-icon-k-t.md`.
