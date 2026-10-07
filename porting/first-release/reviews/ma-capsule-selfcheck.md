# ma-capsule self-check (cloud/ma-capsule 01a88dd6)

Read-only review against `porting/first-release/multi-account-CHECKLIST.md` section 3 and 11 and OD-0119 (from origin/claude/verify-terminal-20261001). Nothing run. Only gaps.

## Strings and words
1. Checklist: style 2 shows "<pill name> · step <n> of <m>"; style 3 shows its name on hover or focus. Built: style 2 shows the name alone under the capsule (the page's `set()` code does `st === '2' ? name : name + ' · step …'`; the checklist and the code disagree and I followed the code); style 3's name is a tooltip, so hover only, not focus. Fix: decide style 2's text with the lead; show the style 3 name on keyboard focus too.
2. The plain capsule's accessible name has no "linked with … to check its work" part (link-up is not built) and the "weekly limit" wording is mine, not the page's.

## States missing
3. Handshake tick / "In sync" chip (checklist section 3, NOTES:33): not built (Handshake animation is Codex's). The slot has `SetHandshake`, but nothing calls it, so the capsule never shows a Handshake today.
4. Link-up marks beside the capsule (OD-0115 link=A): not built (needs the popover's link state).
5. Hover name is a plain tooltip, not the page's small glass tip.
6. One-account composer: only the plain capsule with two connected accounts, or the pill capsule on a pill relay; a single account with no pill shows nothing. This is the checklist's open conflict (page line 2112 against 1550); not resolved by the owner.

## Styles and the light
7. Customization preview composer is the port's 46 px pill (radius 23); checklist says the page's composer is 52 px with 22 px corners. Not matched.
8. Styles 4 and 9 use the app's glass capsule like the rest; the page gives them a rim-less ground (`.s4/.s9 .mc-bg`). Windows check.
9. The light scale itself matches: 101 colours per theme, anchors 100/50/25/5, equal to the page's ramp code for all 202 values. The colour eases per change over 0.6 s in sRGB per channel (the page's CSS does the same); theme change recolours in place.

## Reduce Motion
10. Covered: no loops (orbit, aurora, wave, pulses, breathing dot, scrubber run), glow a steady 7 px blur, swap and step change crossfade, lights land at once. Gap: style 7's card and peek slide (`translateX`) is crossfaded only for the mark, not for the peek; and on a step change the page's per-style motion is simplified to one pop curve (mark scale, light scale) rather than each style's own.

## Not verified
11. Nothing was seen on Windows. The page-level Customization tests need the Windows rig and were not run here.
