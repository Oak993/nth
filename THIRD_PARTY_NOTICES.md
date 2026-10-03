# Third-party notices (the Windows port)

This file lists third-party work the Windows port itself draws on. The upstream Droppy Code `THIRD_PARTY_NOTICES.md`,
`LICENSE` and `TRADEMARK.md` ship with the port unchanged (OD-0002); this file adds to them, it does not replace them.

## Composer lights

The light around the composer (owner decisions OD-0083, OD-0085, OD-0089; ED-251) carries code and values from the two
works below. Border beam: the port re-implements border-beam 1.4.1's stylesheet generator and keeps its tables (palette
colours, layer opacities, presets, mask stops, window profiles, oscillator tables, blur radii, durations) and its timing
curves (`src/Marrow.Core/Models/ComposerLights/`, `border-beam-tables.json` embedded unchanged, and the shaders in
`src/Marrow.UI/Port/ComposerLights/`). The Uiverse light: the port draws SelfMadeSystem's button glow with its gradient
stops and colours (#ff5500 / #0055ff and their lighter rims), the eased turn (cubic-bezier(.56, .15, .28, .86)), the
breathing to .75 over 4 s, the 3 px halftone and the reference's opacities. No paid ("Studio" or "Pro") content of
libraries.dev was used.

### border-beam (Jakub Antalik)

- Source: the npm package `border-beam`, version 1.4.1 (https://www.npmjs.com/package/border-beam), playground at
  https://libraries.dev/beam
- License: MIT

```
MIT License

Copyright (c) 2026 Jakub Antalik

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

### Uiverse.io button (SelfMadeSystem)

- Source: a button by SelfMadeSystem on Uiverse.io (https://uiverse.io), shared by the owner on 2026-10-02
- License: MIT (all Uiverse.io content is MIT-licensed)

```
MIT License

Copyright (c) SelfMadeSystem (Uiverse.io)

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## Liquid Orb Editor (LerSent001)

The effort slider's knob (`src/Marrow.App/Views/Composer/JewelOrb.cs`, OD-0090) carries code ported from Liquid Orb Editor,
by way of the owner's locked design (`knob-orbs-1.js` in `work/design/jewel-knob-locked/`, a GLSL transcription of the
project's WGSL). What was taken, transcribed into SkSL: from `effect.wgsl` the noise bank (`lqHash`, `lqNoise`, the
pre-blurred `lqFbm`, `lqRidge` / `lqRidgeS`, `lqRamp`), `glsFinishPresetFluid`, `glsRotate`, the Violet Ember fluid program,
the contour functions and the glass shell of `orbGlassLiquidAnim` (refraction profile, three-sample dispersion, inner rim,
cool and warm splits, edge shadow, key and fill highlights); from `src/presets.ts` the Violet Ember preset's values; from
`src/orb-states.ts` its Idle profile, the linear-light colour mixing between states, the 0.65 s settle and the integrated
motion phase. Marrow's own additions in the same file are not covered by this notice: the knob framing and pixel-aware
edge, the 2x render below 128 device pixels, the flare's highlight roll-off, the charge and flare light toward the exit
angle, the glass hairline and contact line, the Jewel material's colours (computed from each maker's liquid), and the
knob's page layers. The project's Toolcraft UI code (MIT, Pixel Point) was not used.

- Source: https://github.com/LerSent001/orb (live playground: https://lersent001.github.io/orb/)
- License: MIT

```
MIT License

Copyright (c) 2026 LerSent001

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```
