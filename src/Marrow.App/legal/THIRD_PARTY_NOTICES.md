# Third-party notices

## Droppy Code

Droppy Code itself is licensed under the GNU AGPL v3.0 with additional attribution terms (see LICENSE). The notices below are for what it builds on and are preserved as their licenses require.

## T3 Code

Droppy Code began as a native Swift rewrite of T3 Code by T3 Tools Inc. (https://github.com/pingdotgg/t3code),
in T3 Code's own repository: the history of this repository carries T3 Code's code, and `LICENSE` keeps
T3 Tools Inc.'s copyright notice beside Droppy Code's.

MIT License

Copyright (c) 2026 T3 Tools Inc.

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

## Synara

The way Droppy Code keeps a chat responsive under load follows principles studied in Synara
by T3 Tools Inc. and Emanuele Di Pietro (https://github.com/Emanuele-web04/synara), a
TypeScript app whose architecture was read, not copied: nothing synchronous on the click
that opens a chat (its history decodes off the main thread and the selection paints first),
one write to the library for a click rather than one per thread, timeline blocks kept per
thread across switches so coming back to a chat reuses them, the chat column staying put
while only the transcript is swapped, sidebar rows reading their own thread rather than
the whole list, generous parse caches so scrolling back through a long thread never parses
again, and rows built lazily so a popover opens on what is visible. No Synara code is in
this repository; its license is reproduced here in acknowledgement.

MIT License

Copyright (c) 2026 T3 Tools Inc.
Copyright (c) 2026 Emanuele Di Pietro

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

## SwiftTerm

The terminal under every thread is SwiftTerm by Miguel de Icaza, https://github.com/migueldeicaza/SwiftTerm,
fetched by Swift Package Manager and linked into the app.

Copyright (c) 2019-2026 Miguel de Icaza (https://github.com/migueldeicaza)
Copyright (c) 2017-2019, The xterm.js authors (https://github.com/xtermjs/xterm.js)
Copyright (c) 2014-2016, SourceLair Private Company (https://www.sourcelair.com)
Copyright (c) 2012-2013, Christopher Jeffrey (https://github.com/chjj/)

Permission is hereby granted, free of charge, to any person obtaining
a copy of this software and associated documentation files (the
"Software"), to deal in the Software without restriction, including
without limitation the rights to use, copy, modify, merge, publish,
distribute, sublicense, and/or sell copies of the Software, and to
permit persons to whom the Software is furnished to do so, subject to
the following conditions:

The above copyright notice and this permission notice shall be
included in all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

## MonoCode

Spending a Codex account's banked rate-limit resets from the usage popover (`DroppyCode/Services/Providers/CodexSession.swift`,
`DroppyCode/App/Views/Usage/UsageViews.swift`) follows MonoCode by hardbeat920, https://github.com/hardbeat920/monocode,
`src/lib/rateLimitsFetch.ts` and `src/chrome/UsageProviderChip.tsx`: the app-server's `account/rateLimitResetCredit/consume`
call sent with an idempotency key, the reset credits read beside the rate limits, the confirm step before a reset is spent,
and the four outcomes it can answer with. No MonoCode code is in this repository; its license is reproduced here in
acknowledgement.

MIT License

Copyright (c) 2026 Nick

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

## Zeron

Droppy Code's working indicators (`DroppyCode/UI/Common/WorkingIndicators.swift`) are ported from Zeron,
https://github.com/zeronsh/zeron, `apps/ios/Zeron/Views/Loaders.swift` and `apps/ios/Zeron/Theme/Motion.swift`.

MIT License

Copyright (c) 2026 Wing

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

## Octicons

The Copilot provider icon (`DroppyCode/Resources/Assets.xcassets/provider-copilot.imageset/copilot.svg`) is
the `copilot` mark from GitHub's Octicons, https://github.com/primer/octicons, `icons/copilot-24.svg`,
with its artboard resized to match the other provider marks.

MIT License

Copyright (c) 2026 GitHub Inc.

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

## TourKit

The welcome tour (`DroppyCode/App/Views/Tour/`) is a port of TourKit by Ram Patra,
https://github.com/rampatra/TourKit: the slideshow card, its page indicator, its top controls
and its floating window controller, carried over to the app's glass and asset catalog.

MIT License

Copyright (c) 2026 Ram Patra

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

## Provider names and marks

Codex, Claude, Cursor, OpenCode, Grok, DeepSeek, Meta, Devin, Antigravity, GitHub Copilot and Command
Code are the names and marks of their respective owners, used here to name the agents the app drives.
Droppy Code is not affiliated with or endorsed by any of them.

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
