# Locked design sources (read-only copies)

Byte-exact copies of the owner-locked design sources that the first-release specs cite as `DESIGN <file>:<line>`, so
cloud workers can read them. The originals are outside the repository; these copies are never edited here.

| Copy | Original |
| --- | --- |
| `customization/cz.js`, `gen.py`, `runtime.js`, `voice.js`, `voicecmp.js`, `voiceopts.js` | `E:\Droppy\work\design\customization\src\` |
| `customization/voice-glow.js`, `voice-orbs.js` | `E:\Droppy\work\design\customization\` |
| `orb/liquid-palettes.json` (the 30 makers that gen.py:59 reads into cz.js) | `E:\Droppy\work\design\orb\` |
| `look-picks/lp-pickers.js`, `lp-page.css` (the picker's stylesheet: sizes, gaps, radii), `lp-page.js`, `build_page.py` (the Q-CZ-3 page text, the 260 px card) | `E:\Droppy\work\design\decisions-2026-10-04\look-picks\src\` |
| `composer-lights/logic.js`, `gen.py` (the playground: the typing string and pace, the composer's css and the ring's dash arrays; `customization/gen.py` runs the latter for its composer; read by `tests/fixtures/customization/stage.generate.cjs`) | `E:\Droppy\work\design\composer-lights\playground\src\` |
| `app-icon/app-icon.html` (the picks page, as published), `app-icon/src/` (`icons-a-j/`, `icons-k-t/`, `iconkit.js`, `ICON-MODULE.md`, `harness.html`, `ic-page.*`, `build_page.py`, `check.cjs`); OD-0112 locks `src/icons-a-j/D-aurora.js` | `E:\Droppy\work\design\decisions-2026-10-05\app-icon\` |

Line numbers in the specs refer to these files. A fixture "from a node run of cz.js" runs `node` on these copies.
