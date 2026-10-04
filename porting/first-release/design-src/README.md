# Locked design sources (read-only copies)

Byte-exact copies of the owner-locked design sources that the first-release specs cite as `DESIGN <file>:<line>`, so
cloud workers can read them. The originals are outside the repository; these copies are never edited here.

| Copy | Original |
| --- | --- |
| `customization/cz.js`, `gen.py`, `runtime.js`, `voice.js`, `voicecmp.js`, `voiceopts.js` | `E:\Droppy\work\design\customization\src\` |
| `customization/voice-glow.js`, `voice-orbs.js` | `E:\Droppy\work\design\customization\` |
| `orb/liquid-palettes.json` (the 30 makers that gen.py:59 reads into cz.js) | `E:\Droppy\work\design\orb\` |
| `look-picks/lp-pickers.js`, `lp-page.css` (the picker's stylesheet: sizes, gaps, radii), `lp-page.js`, `build_page.py` (the Q-CZ-3 page text, the 260 px card) | `E:\Droppy\work\design\decisions-2026-10-04\look-picks\src\` |

Line numbers in the specs refer to these files. A fixture "from a node run of cz.js" runs `node` on these copies.
