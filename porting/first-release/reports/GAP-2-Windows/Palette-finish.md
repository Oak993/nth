# GAP-2 palette Windows finish — READY under the commissioned alone-twice rule

Tested production/tests: `941a86827dc31ea5378fa0ec6223a62f6a314c49`, branch `codex/gap-2-fix`, Windows 11 x64 / .NET SDK 10.0.401. This finish supersedes the historical palette STOP and NOT READY reports; it does not rewrite their evidence. Production changes only the preview palette function. The real composer is unchanged. The final delivery head is recorded in the external T29 handoff.

| Scope | Run 1 pass / raw fail / skip | Run 2 pass / raw fail / skip |
| --- | --- | --- |
| App GAP (`Marrow.App.Tests.Port.Settings.Gap*`) | 8 / 0 / 0 | 8 / 0 / 0 |
| Full Customization (`Marrow.App.Tests.Port.Settings`) | 401 / 1 / 0 | 401 / 1 / 0 |
| Full Composer (`Marrow.App.Tests.Views.Composer`) + Traceability | 1223 / 0 / 0 | 1223 / 0 / 0 |

Both raw Customization failures are retained: `CustomizationAllocationTests.Drawing_a_style_glyph_allocates_nothing`, Aura 16 B/frame in run 1, Off 25 B/frame in run 2. That exact method (all three theory styles) passed alone twice: 3 pass / 0 fail / 0 skip each time. The same method had failed in the earlier T29 engine raw sweep and passed alone twice there. Under CODEX-LOCAL.md / FEATURE-RULES.md, a failure counts only when it fails alone twice. No glyph/test/tolerance is changed, and no raw Customization sweep is described as clean. All 28 new exact maker/appearance cases pass in both full Customization runs; all GAP cases pass twice. No other failure or skipped case remains in this scope.

Final build: 0 warnings/errors. The six sequential combined runs used one unchanged heavy.sh slot and unchanged one-names-long.sh; the exact alone reruns used unchanged one-full.sh, after the active combined batch ended. Every build/test used cleared provider/API variables, isolated disposable data, and Native exclusion. `palette-suite-receipts.json` records log hashes, counts, source head and red baselines; original raw logs remain in `E:/Droppy/work/scratch/codex-gap-2`.

## What changed and what stayed exact

The latest owner ruling says the Customization page previews the real composer; its High swatches are a mock-up approximation, not a second light authority. `PreviewMakers.Palette` maps all 14 locked makers to the unchanged `ComposerLightSlot.PaletteFor`, retaining the preview's passed-in accent values. Purple has no maker and keeps the existing Claude fallback. The 16 other gallery makers have no fixed provider slot and retain their former shared pair rule. No dot/menu swatch, real composer, EffortBrand, shared palette rule, MakerLiquids/Jewel data, motion, plate, rendering code or frozen reference changes.

Exactly one existing test is retired under the new ruling: `PreviewMakersTests.A_makers_palette_is_its_dark_mid_and_light_and_its_light_deep_and_mid_with_the_accent`. One ED, `ED-597`, quotes both conflicting assertions and cites OD-0089 (`owner_decisions.md:787`) and the latest ruling. Its replacement is 28 zero-tolerance cases: independent numeric pairs, exact real-slot equality, preview accent equality, and the mounted preview selected in each actual window appearance. A separate fact preserves the original 16 remaining gallery assertions. The preceding five PreviewMakers test bodies, the complete GAP equality predicate, original GAP plate pixel comparison and all thresholds are byte-exact.

Test-first evidence: original unchanged GAP equality failed alone twice (0 pass / 1 fail); the new 28-case replacement ran against original preview production and failed all 28 cases in both launches. The saved production repair was then applied and committed. The initial `palette-repair-GAP` launch ran original code because a guarded CRLF edit had aborted before any write; its failure is pre-repair evidence. `palette-repair-GAP-applied` subsequently passed. No failing baseline is relabeled as green.

## Exact oracle and source limits

OD-0089 locks the real composer to `E:/Droppy/work/design/variants/project/ComposerLights.dc.html`. `playground/src/gen.py:462` embeds `logic.js`; `logic.js:483-484` selects the appearance array and its first two maker entries, and `:495` assigns `--au-c1/--au-c2`. The literal table at `logic.js:60-63` contains only Claude, OpenAI, Gemini and DeepSeek. It does not contain 14 literal model pairs. OpenAI Light there is #8D94A5/#A9AEBB and Gemini Light #8A5FC0/#D96670, which differ from the unchanged real slot.

To satisfy the explicit instruction **do not change the real composer**, the 14 numeric expectations use the independently approved OD-0047/0051 maker stops at `E:/Droppy/work/design/effort-liquid-approved-v15.dc.html:360-373` and the already shipped slot's Dark mid/light, Light deep/mid rule (`ComposerLightSlot.cs:52-61`). The source limit was raised as an optional clarification; implementation follows the owner's explicit preservation priority, without treating silence as a new approval. No claim is made that all 14 pairs occur literally in gen.py/logic.js. Each InlineData cites its approved canvas line; `palette-oracle.json` records the independent source hashes and table.

| Maker | Dark c1 / c2 | Light c1 / c2 | Approved canvas line |
| --- | --- | --- | ---: |
| OpenAI | #EEF0F5 / #D9DBE3 | #FFFFFF / #EEF0F5 | 360 |
| Claude | #CC6B47 / #E0875F | #B85A38 / #CC6B47 | 361 |
| DeepSeek | #3D5CFA / #6A82FF | #2A45E0 / #3D5CFA | 362 |
| Meta | #0070EC / #2B95FF | #0054C4 / #0070EC | 363 |
| Gemini | #9C73CC / #D96670 | #4285F5 / #9C73CC | 364 |
| Cursor | #F54E00 / #FF8040 | #D93F00 / #F54E00 | 365 |
| Grok | #121218 / #26262E | #030304 / #121218 | 366 |
| OpenCode | #25B34F / #45DC6C | #168A3A / #25B34F | 367 |
| OpenCodeGo | #00A4CD / #3CCBEC | #007EA3 / #00A4CD | 368 |
| Zai | #14306E / #2F6BFF | #0A1630 / #14306E | 369 |
| Devin | #3CBC9B / #6FDCBF | #239377 / #3CBC9B | 370 |
| Copilot | #D25AB4 / #F4A876 | #8534F3 / #D25AB4 | 371 |
| CommandCode | #2E1B9C / #6145DC | #24157E / #2E1B9C | 372 |
| Pi | #C8102E / #EC5560 | #A30C25 / #C8102E | 373 |

These exact numbers are checked independently of the production palette method and then on the mounted preview; both appearances are tested without tolerance. The existing Customization Chrome captures are preserved as evidence of the superseded mock-up, not used as the light oracle. No new capture or reference regeneration is claimed.

## Mechanical checks and scope

`palette-byte-proof.json` verifies the unchanged real composer palette files, GAP predicate, plate/motion dependencies, previous five maker tests and original input ledger prefixes. The ED append proof records its entire byte-exact predecessor prefix. STATE is appended only after final evidence is complete. Gate A 12 PASS, Gate B 8 PASS, protected/reference checks and CR-at-EOL whitespace checks pass. Native tests, owner app launch, global release/packaging and owner visual acceptance are outside this commissioned local finish. The current `node porting/tools/mark.mjs GAP-2 DONE` attempt returned exit 2: "no task GAP-2 in the queue" (palette-mark-GAP-2.log). T29 also has no queue task. No implementation/DONE status is invented; READY is the commissioned Windows verdict for T9.
