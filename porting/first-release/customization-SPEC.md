# SPEC 1: Settings > Customization (build specification)

Status: ready for test-first build. Written 2026-10-03 for the Marrow Windows port (C# / .NET 10 / Avalonia 12.1.2).
Owner-ruled addition beyond the pinned Droppy Code 1.7.0 source: every visible element here is a DEVIATION recorded under
OD-0085, OD-0089 (and its amendment), OD-0091 (sixth amendment), OD-0097 (and its amendment) and OD-0076. Nothing
here may be widened without a new owner ruling.

Amended 2026-10-03 for OD-0097 (the Voice Options alternatives become Customization choices): CZ-1.3, CZ-1.4, CZ-4.3,
CZ-4.7.2, CZ-4.7.5 to CZ-4.7.9, CZ-4.8, CZ-9 (order) and CZ-9.8 to CZ-9.11, CZ-11.1, CZ-12.12, CZ-13.4, CZ-13.6, CZ-14.1,
CZ-15, CZ-17, CZ-18, tests CZ-T90 to CZ-T98, Q-CZ-6. In those clauses **OWNER** is the ruling, **SOURCE** the Voice
Options decision page (`E:\Droppy\work\design\decisions-2026-10-03\voice-options\`, voice-options.html Q1-Q3,
voice-options.js), **ED** an engineering decision; every placement and wording the owner did not set is an ED.

Clause ids (`CZ-n.m`) are cited by the tests in section 19 and by code comments (`// CZ-4.3`).

Evidence labels used below:
- **DESIGN**: read from the locked design source (file and line given). The design is the authority for this page.
- **OWNER**: an owner decision (OD-nnnn) or its amendment.
- **PORT**: a fact of the port's code at staging `ad25b0b14` (file given).
- **ED**: an engineering decision proposed by this spec; the build records it as the next free `ED-nnn` at merge.
- **UNRESOLVED**: needs the owner (section 20) or a measurement; never guessed in code.

---

## 0. Sources and authority

| What | Where |
|---|---|
| Locked design (layout B "Stage") | `E:\Droppy\work\design\customization\src\gen.py` (CSS, templates), `src\cz.js` (rows, labels, ranges, preview logic), `src\voicecmp.js` (voice rows, geometry, orb springs), `src\voice.js` (Voice typing page, see SPEC 2), generated `customization.html`; publish copy `E:\Droppy\work\design\published\czpub\` (v5, https://claude.ai/artifact/AbgSdYZoaZi9zBzN7HWo6Q) |
| Composer light (already built, ED-251) | playground `E:\Droppy\work\design\composer-lights\playground\src\logic.js`, `beam.js`; port `src\Marrow.Core\Models\ComposerLights\*`, `src\Marrow.UI\Port\ComposerLights\*`, `src\Marrow.App\Views\Composer\ComposerLightSlot.cs` |
| Voice composer design (orb, pill, cross, glow) | `E:\Droppy\work\voice\design\voice-composer.html/.js`, `voice-orbs.js`; built by SPEC 2 |
| Accent palette (22 colours) | `E:\Droppy\work\design\accent-palette-locked.json`; port `src\Marrow.UI\Port\AccentPalette.cs` (unit accent-picker, ED-253) |
| Maker liquid palettes (30 makers) | `E:\Droppy\work\design\orb\liquid-palettes.json`, sha256 `a600baefbcf22aca861392456ff4d9c05088c37627fe30e95cd5142d5c441f15` |
| Owner rulings | `porting\owner_decisions.md`: OD-0076, OD-0083, OD-0085, OD-0089 (+ amendment), OD-0090, OD-0091 (+ six amendments), OD-0092 (+ amendment), OD-0095, OD-0097 (+ amendment) |
| Voice options (OD-0097) | decision page `E:\Droppy\work\design\decisions-2026-10-03\voice-options\` (https://claude.ai/artifact/8ZC213hrTRPKzSkudYJtH4): the words L1-L6, the light B1-B3, the language default A-D, their texts and motion; the rows on this page: `src\voiceopts.js` + `gen.py` of the Customization design (shots `shots\od0097\`) |

Authority order: PORT_CONTRACT > amendments > owner_decisions > this spec's EDs. Where the design page draws a mock of a
port component (rows, cards, switches, the window chrome) and the port already has that component, the **port component
wins** for its own metrics (CZ-3.1). New components (stage, style cards, segmented control, sliders with Auto, swatches,
orb tiles, model switch) take the design's numbers given here.

Implementation conventions: `porting\IMPLEMENTING.md` (R-FILE-2: port-only code in `Port\` folders with a `// PORT-ONLY`
header; R-TEST-1..10). Tests run ONLY through `E:\Droppy\work\claude-verify\one.sh`, `one-full.sh`, `one-names.sh`;
never a bare `dotnet test`.

---

## 1. Scope, dependencies, file placement

### CZ-1.1 In scope
1. A new Settings sidebar page **Customization** (and the sidebar entry for **Voice typing**, whose content is SPEC 2 V-16.3).
2. Layout B "Stage": the pinned live preview and every section listed in CZ-3.3.
3. Persisted settings: `composerLight` (exists, ED-251), `voiceOrb` (record from SPEC 2 V-16.1, edited here) and the new
   `workingDots` (CZ-13.3).
4. The working dots colour applied to every composer's `ComposerWorkingDots` (CZ-10.4).

### CZ-1.2 Out of scope (owned elsewhere)
- The composer light engine and shaders (ED-251, done).
- Voice capture, recognition, the composer's orb/pill/cross/glow, live words (SPEC 2).
- The Voice typing page's content (SPEC 2 V-16.3).
- Settings-wide page turnover between sidebar pages (CZ-14.2 note).

### CZ-1.3 Dependencies (must be on staging before the phase that needs them)

| Needed by | Dependency | Why |
|---|---|---|
| C1 | composer-lights (merged, `ad25b0b14`) | `ComposerLightView`, `ComposerLightSettings`, `BorderBeamResolved.AutoValues`, `BorderBeamTables` |
| C1, C2 | accent-picker (ED-253, round 4 merged) | `AccentPalette.All`, `AccentChoice`, `Chrome.Accent`, `DotFieldControl.Retarget` (eased dot colour), `WindowTransients` (Esc closes the newest menu first) |
| C1 | jewel-knob (ED-252) | `JewelPalettes` (brand to maker name), `GpuWarmup.AddActivity` for the preview's shaders |
| C4 | SPEC 2 phases V3 (orb renderer) and V5 (composer voice controls + scripted session) | the preview's orb, pill, cross, glow and voice demo |
| C4 | SPEC 2 phases V1 (`voiceOrb` keys `labels`, `lightVoice`, `langMode`; `VoiceLanguage.Resolve` and the reset rule) and V4 (`ComposerLightView.VoiceLight`) | the three OD-0097 rows and their preview (CZ-9.8 to CZ-9.11) |
| C5 | SPEC 2 phase V6 (Voice typing page, `VoiceTypingSettings`) | the Speech section's summaries and links |

### CZ-1.4 Files (ED)

| File | Holds |
|---|---|
| `src\Marrow.App\Port\Settings\CustomizationSettingsPage.cs` | the page (sections, rows, wiring) |
| `src\Marrow.App\Port\Settings\CustomizationStage.cs` | the pinned block, stage, compaction, clip (CZ-3.5) |
| `src\Marrow.App\Port\Settings\ComposerPreview.cs` | the preview composer (CZ-4.3) |
| `src\Marrow.App\Port\Settings\PreviewModelSwitch.cs` | steppers, chip, menu (CZ-4.4, CZ-4.5) |
| `src\Marrow.UI\Port\Settings\CzControls.cs` | segmented control, slider + Auto, swatch row, capsule button, fold row, crossfade text, style card, orb tile, menu button (CZ-12) |
| `src\Marrow.Core\Port\Settings\WorkingDotsSettings.cs` | the `workingDots` record and JSON (CZ-13.3) |
| `src\Marrow.Core\Port\Settings\MakerLiquids.cs` | the 30-maker table generated from liquid-palettes.json (CZ-4.5.4) |
| `src\Marrow.Core\Port\Settings\CzText.cs` | every label, detail and format function of this page as constants (CZ-6..CZ-11), so tests assert bytes |
| `src\Marrow.App\Views\Settings\SettingsView.cs` | edit: two `SettingsPage` cases, titles, symbols, tints, keywords, page factory, `hasControls` |
| `src\Marrow.App\AppSettings.cs` | edit: `WorkingDots`, `VoiceOrb` properties and keys; write coalescing (CZ-13.5) |
| `src\Marrow.App\Views\Composer\ComposerView.cs` | edit: `ComposerWorkingDotsSlot` passes the dots colour (CZ-10.4) |

---

## 2. Placement in the Settings window

### CZ-2.1 Sidebar order (DESIGN cz.js:20-32, voice.js:195-196)
`SettingsPage` gets two cases; `SwiftEnum<SettingsPage>.AllCases` order is the sidebar order:

General, Hydra, Appearance, **Customization**, **Voice typing**, Providers, MCP, Models, Source control, Shortcuts, Archive, About.

| Case | `[RawValue]` | Title | Symbol | Keywords |
|---|---|---|---|---|
| `Customization` | `"customization"` | `Customization` | `sparkles` (DESIGN gen.py:611 "SF Symbols sparkles") | DESIGN proposal: `light`, `glow`, `beam`, `composer`, `effects`, `customization` (gen.py:611); UNRESOLVED additions, see Q-CZ-4 |
| `VoiceTyping` | `"voiceTyping"` | `Voice typing` | `waveform` (DESIGN voice.js:27 draws a waveform; ED: the catalogue name `waveform`) | UNRESOLVED, see Q-CZ-4 |

- CZ-2.1.1 `SymbolCatalogue.Mapping("waveform")` must resolve; if it does not, add the mapping to the Lucide icon
  `audio-waveform` (ED), with a test that the sidebar row draws a non-empty icon.
- CZ-2.1.2 `Tint` arms (ED): the port's sidebar never draws `SettingsPage.Tint` (PORT Chrome.cs:1042-1053
  `SidebarIconBadge` draws in the label colour); both new arms return `Chrome.Gray` with a comment that the value is
  unused. A test asserts no sidebar code path reads `Tint`.
- CZ-2.1.3 Search: `VisiblePages` (SettingsView.cs:158-165) matches titles and keywords exactly as for other pages.

### CZ-2.2 Page factory and chrome (PORT SettingsView.cs:242-262)
- `SettingsPage.Customization => new CustomizationSettingsPage()`; `SettingsPage.VoiceTyping => new VoiceTypingSettingsPage()` (SPEC 2).
- `hasControls` is true for Customization (its chrome row carries the Preview state control, CZ-3.2) and false for
  Voice typing (DESIGN gen.py:623: the accessory shows only on the Customization page).
- The chrome row's compact title is the existing `ChromeCompactTitle(state.Page.Title, scrollChrome)`.

---

## 3. Page layout (layout B, "Stage", OWNER OD-0089 amendment)

### CZ-3.1 Geometry rule
The design draws the Settings window at 782 x 581 with a 213 px sidebar (gen.py:148-164); the port's window is 780 x 580
with a 212 px sidebar and page margins `Chrome.ContentHorizontalPadding + 8 = 24` (PORT SettingsView.cs:170, 249). The
page's content column is therefore **520 px** wide. Rows, cards, section headings, dividers and switches are the port's
`ChromeSection`, `ChromeCard`, `ChromeRow`, `ChromeRowDivider`, `SettingsSwitch` with their own metrics (card radius 16,
row padding 16/11/10/11, detail in subheadline). The design's 12 px card radius and 9/11 row padding are mock values and
are not carried (ED).

### CZ-3.2 Chrome row accessory (DESIGN gen.py:623, cz.js:704-706)
A segmented control (CZ-12.1, small) labelled for accessibility `Preview state`, options:
`Rest` | `Typing` | `Running a turn`. Default `Rest`. Session state only (not persisted). Picking an option cancels any
voice demo in the preview (CZ-4.7.5) and sets the preview's phase (CZ-4.6).

### CZ-3.3 Section order (DESIGN gen.py:555-559, cz.js:658-677, 644-654)
Inside the **sticky container** (CZ-3.5):
1. the pinned block: section heading `Composer light`, then the preview card (stage + foot);
2. `Style` (three cards, CZ-5);
3. the style's sections, each folding open or shut as the style changes (CZ-6, CZ-7, CZ-8):
   Border beam: `Look`, `Pace and strength`, Advanced card, Reset card;
   Uiverse light: `Look`, `Motion`, `Brightness by moment`, Advanced card, Reset card;
   Off: one note card;
4. `Voice orb` (CZ-9);
5. `Working dots` (CZ-10).

After the sticky container:
6. `Speech` (CZ-11), the first section not about the composer: the stage lets go here.

Sections are separated by `Chrome.SectionSpacing` (20) as on every page (PORT SettingsView.cs:249).

### CZ-3.4 The pinned block (DESIGN gen.py:178-183, 468-475, 556)
- Top inset 60 px (the port's page top for pages with chrome controls: `ChromeTopPadding + CapsuleHeight + 16`;
  the design's 59).
- Heading `Composer light` in the `ChromeSection` heading style, 10 px above the preview card.
- Preview card: `ChromeCard` fill, padding 12 / 12 / 0 (left, top, right, bottom 0); the stage (CZ-4.1) 496 px wide;
  the foot (CZ-4.4) 46 px minimum height.
- Bottom padding 14 px; 2 px gap to the `Style` section.
- Heights: at rest 60 + 18 + 10 + 12 + 212 + 46 + 14 = **372 px**; fully compacted (CZ-3.5.2) **280 px**.

### CZ-3.5 Sticky stage: pin, compaction, release (OWNER OD-0089 amendment; DESIGN cz.js:250-268, gen.py:468-476)

CZ-3.5.1 **Pin.** Let `y` be the Settings page ScrollViewer's vertical offset (the page finds its ancestor
`ScrollViewer` on attach and listens to `ScrollChanged`; it must not change `SettingsView`'s scroll machinery). The pinned block's visual
top, in the page's content coordinates, is

`pinTop = clamp(y, 0, H_c - h_pin)`

where `H_c` is the laid-out height of the sticky container (the block's rest height 372 + 2 + every section up to and
including `Working dots`) and `h_pin` is the block's current visual height (372 - `col`). This is CSS `position: sticky;
top: 0` inside the container `.czstk`. Applied as a `RenderTransform` translate on the block (no re-layout per frame);
the block has the highest `ZIndex` in the page.

CZ-3.5.2 **Compaction.** `target = min(92, y)`. The applied compaction `col` follows the target exactly when
`|target - col| <= 72` (wheel, touchpad and drag steps), and otherwise (a focus scroll, Home, End, a jump) eases toward it
with `col += (target - col) * (1 - exp(-dt / 0.06 s))` per frame; under reduce motion `col = target` at once. With
`k = col / 92`:
- stage height = 212 - `col` (212 down to 120);
- the conversation text above the composer (CZ-4.2): opacity `clamp(1 - 1.8 k, 0, 1)`, translate Y `-14 k` px;
- the composer keeps its 12 px bottom inset, so it never moves relative to the stage's bottom edge;
- the room given back: the layout keeps the block's rest height (the sections below never move faster than the scroll);
  only the block's visual height shrinks.

CZ-3.5.3 **Release.** Once `y > H_c - h_pin` the block scrolls away with the page, so it lets go exactly when the bottom
of `Working dots` reaches the block's bottom; `Speech` and everything after it scroll normally under nothing.

CZ-3.5.4 **What scrolls under it (ED).** The design paints the window's own gradient behind the block (gen.py:178-179).
The port's Settings window is translucent (`WindowBackdrop`, `BackdropOpacity`), so a painted copy cannot match. Instead
every section of the page is **clipped** to the region below the block's visual bottom edge (a clip rectangle from
`pinTop + h_pin` downward, in content coordinates, updated with the transform). Clipped content is not hit-testable.
The block itself has no fill beyond the preview card.

CZ-3.5.5 **Hairline.** While `y > 4` a 1 px line in the divider colour (`ChromeRowDivider`'s brush) shows along the
block's bottom edge across the 520 px column; it fades in and out over 0.25 s ease (DESIGN gen.py:180-181).

CZ-3.5.6 **Keyboard focus stays clear (DESIGN cz.js:15, gen.py:470 `scroll-padding-top: 290px`).** When a control inside
the sticky container takes keyboard focus and its top is above `pinTop + h_pin + 8` in viewport terms (or below the
viewport), the page scrolls so the control's top sits 8 px below the block's bottom (computed with the compaction the
new offset will produce). The scroll is animated over 0.3 s ease (OD-0076) unless reduce motion; the compaction follows
by CZ-3.5.2. Pointer focus does not scroll.

CZ-3.5.7 **Frame cost.** Scrolling changes only the block's transform, the clip rectangle, `col`-driven values and the
stage's height; no section re-measures. Budget in CZ-16.

---

## 4. The preview

### CZ-4.1 Stage frame (DESIGN gen.py:304-305, 313-319)
- 496 px wide (the card's 520 - 2 x 12), 212 px high at rest, radius 10, clipped; a 1 px inset hairline in the divider
  colour.
- Background (ED): its own `WindowBackdrop` sized to the stage, at the app's `BackdropOpacity`, so the stage reads as a
  small chat window (the design fills it with the window gradient, `.pvw.wbg`).

### CZ-4.2 Conversation text (DESIGN gen.py:547-548)
Static text, left and right insets 24, top 18, in the window's text colour, drawn above the stage background and below
the composer:
- heading (14 / 20, semibold): `Dark mode toggle`
- paragraph (13 / 19): `I added a ` **`Theme`** ` switch to the settings page. It reuses the existing ` `` `Switch` `` `
  ` row, so it picks up the same spacing and focus ring as the other options.` with `Theme` bold and `Switch` in the
  app's code font at 12.
Fonts are the app's (OD-0009, Inter) at these sizes. It compacts by CZ-3.5.2.

### CZ-4.3 The preview composer (ED: `ComposerPreview`)
A runtime-free composer assembled from the app composer's own visual parts, so the preview never starts a thread, a
provider or a microphone. It must look exactly like a real `ComposerArea` of the same width (test CZ-T45).

| Part | Rule |
|---|---|
| Frame | width 472 (attachment inside) or 428 (outside); left inset 12 or 56; bottom inset 12; height 46 (the real pill's, 46 from rim to rim on the reference frame: 2-chat-use.mov at 19.7 s, rows 1299-1344; the playground's 52 is a web mock; amended by ED-335 item 2); radius 22; the composer's glass plate (`SwiftGlass`, as the real composer). The width and left inset ease over 0.42 s `cubic-bezier(.32,.72,0,1)` when the attachment place changes (DESIGN gen.py:463, voicecmp.js:224-238) |
| Placeholder | the app's composer placeholder for a Claude thread: `Ask Claude for anything; big jobs go out to a team of heads` (playground gen.py:385; must equal the real composer's string, test CZ-T46); text left inset 48 (attachment inside) or 14 (outside), eased 0.34 s ez |
| Typing demo | state Typing types `Add a keyboard shortcut for the theme switch` one character every 0.055 s from empty, with a caret (playground logic.js:37, 304-307); leaving Typing clears it with the placeholder crossfade |
| Model chip | the preview maker's display name (CZ-4.5.4) in the real chip's style; clicking it steps to the next maker (DESIGN cz.js:776) |
| Context ring, send button | the real controls' visuals; Running shows the stop square and the ring spins (330 deg/s, eased in and out with time constant 0.35 s, voice-composer.js:785-786) |
| Working dots | the real `ComposerWorkingDots` in the colour of CZ-10.4 for the preview maker; shown in Running |
| Composer light | the real `ComposerLightView`: `Settings` = `AppSettings.ComposerLight` (live, CZ-13.5), `Phase` = the preview phase (CZ-4.6), `Palette` = the preview maker's palette (CZ-4.5.5), `ComposerRect` = the composer's rectangle in the stage, `FadeBottom` = the stage's bottom edge, `VoiceLight` = the level SPEC 2 V-13.4 gives for `voiceOrb.lightVoice` while the demo listens, finishes and lands (OWNER OD-0097, CZ-9.9) |
| Attachment button | inside at the composer's left (34 x 28, radius 14, left 8, top 12) or outside in the stage margin (36 x 36 circle, left 10, centred on the composer's line, bottom 20) per `voiceOrb.attach` (DESIGN gen.py:331, voice-composer.html:139-148); inert in the preview |
| Voice orb, pill, cross, glow | SPEC 2's controls (V-11 orb, V-12 pill and cross, V-13 glow) driven by the scripted demo (CZ-4.7), with the pill's words (V-12.9), the language name (V-12.10) and the language question (V-12.11) per `voiceOrb` |

### CZ-4.4 The foot (DESIGN gen.py:306-307, 556; cz.js:726-731)
Left: the preview model switch (CZ-4.5). Right: the caption, 12 px secondary text, crossfading (CZ-12.9) on change:
- Border beam: `Border beam · ` + shape + ` · ` + palette, shape = `Large` | `Line` | `Small` | `Pulse inside` | `Pulse outside`, palette names per CZ-6.2;
- Uiverse light: `Uiverse light · ` + (`Original` | `{maker} colours` | `Accent` | `Custom`) + ` · ` + `fmt(speed, 1)` + ` s a turn`;
- Off: `No light`.
(` · ` is space, U+00B7, space. `fmt` is CZ-12.10.)

### CZ-4.5 Preview model switch and menu (DESIGN gen.py:343-371, 459-461; cz.js:155-190; OWNER OD-0089 amendment)
CZ-4.5.1 **Controls.** `‹` stepper (22 x 22, radius 11, accessible name `Previous model`), the chip (28 high, radius 14,
padding 0 10, gap 7: a 10 px maker dot, the maker name at 13, a 7 x 5 chevron that turns 180 deg over 0.3 s ez when the
menu is open), `›` stepper (`Next model`). Steppers wrap around the list. The chip's width eases to the new name over
0.3 s ez while the name crossfades. Chip accessible name: `Preview model: {name}` + ` ({model})` when the maker has a model.

CZ-4.5.2 **Menu.** Opens **above** the chip (OWNER amendment; the source's composer popover), 270 px wide, padding 6,
heading `Preview with` (11, semibold, secondary), list max height `clamp(room, 112, 236)` where room = chip top - window
top bar clearance (cz.js:150: chip top - viewport top - 46 - 44). Items: 28 high, radius 7, check (12 px, shown on the
current maker), maker dot (10 px), name (13) and model id (11, secondary, ellipsised). Open: opacity 0.2 s ease and
transform from translateY(4) scale(.96) over 0.22 s `cubic-bezier(.2,.9,.25,1)`, origin at the chip's centre bottom;
close 0.15 s. On open the current item scrolls to the middle and takes focus. Keys: Up/Down move (wrapping), Home/End,
Enter/Space pick, Esc closes the menu only and returns focus to the chip; a click outside closes it; picking closes it
and focuses the chip. Presented through the port's popover presenter so Esc and light dismiss follow `WindowTransients`
(ED-253). Whether it carries the OD-0095 tip: Q-CZ-5.

CZ-4.5.3 **Scope.** The switch changes the preview only: its chip, the Uiverse light's Model colours, the dots' Model
colour. Session state; not persisted (ED; the design stored it in localStorage for review convenience only). Initial
maker: the maker of `AppSettings.DefaultProvider`'s brand (ED; the design opens on Claude); when that brand has no maker
(Purple), Claude.

CZ-4.5.4 **The makers (DESIGN gen.py:51-61).** All 30 makers of liquid-palettes.json in this order: Claude, OpenAI,
Gemini, DeepSeek, Grok, NVIDIA, then the file's order for the rest (Meta, Mistral, Qwen, Kimi, Cursor, OpenCode,
OpenCodeGo, Zai, Devin, Copilot, CommandCode, Pi, MiniMax, Tencent, Cohere, Microsoft, Perplexity, NousResearch, AWS, AI21,
Inflection, LiquidAI, Baidu, Arcee). Display names: the key, except `OpenCodeGo` -> `OpenCode Go`, `CommandCode` ->
`Command Code`, `NousResearch` -> `Nous Research`, `LiquidAI` -> `Liquid AI`, `Zai` -> `Z.ai`, `MiniMax` -> `MiniMax`.
Model id: `_meta.model`, or empty when it starts with `(`. Per maker the table holds (dark and light, effort `high`):
`deep`, `mid`, `light`, `highlight`, `title`. `MakerLiquids` is generated from the file and pinned by its sha256 (test).

CZ-4.5.5 **Colours.**
- Menu and chip dots: `czTone(deep, dark, titleOf(theme))` (CZ-12.11).
- The preview composer light's `ComposerLightPalette` (ED, so the preview matches a real composer of that maker): the
  same rule as `ComposerLightSlot.UpdatePalette` (PORT ComposerLightSlot.cs:52-60) applied to the maker's stops:
  dark = (mid, light), light = (deep, mid), plus `Chrome.Accent` resolved per theme. (The design toned deep and light
  stops for the preview; superseded so preview and app agree.)

### CZ-4.6 Preview phase (DESIGN cz.js:271-276, 704-706)
`Phase` given to the preview's light and controls:
- the chrome control's pick (Rest, Typing, Running a turn), except while the voice demo writes or sends (CZ-4.7):
  inserting or shown text -> Typing, sent -> Running;
- touching any Working dots control switches the pick to `Running a turn` (DESIGN cz.js:626-630, 639).

### CZ-4.7 The voice demo (OWNER OD-0091 sixth amendment "each previewed live in the stage"; DESIGN voicecmp.js)
CZ-4.7.1 The preview never opens the microphone, the network or a model (ED; Q-CZ-2 asks the owner to confirm). It drives
SPEC 2's real composer voice controls through the scripted session source (SPEC 2 V-14.8): the voice design's simulated
speaker and live words (voice-composer.js:45-83, 226-240), the phrase `Add a shortcut for the theme switch, and show it in
the tooltip too.`, its revisions (`team` -> `theme`, `switch` -> `switch,`, `tool` -> `tooltip`, `too` -> `too.`), the
Listening/Transcribing label rule and the final pass. (The design's Customization page typed a fixed sentence after the
final pass, cz.js:46, 86-90; superseded by the voice design, the authority on voice behaviour: ED.)

CZ-4.7.2 Start: a click (or Enter/Space) on the preview's orb, or picking a **Listening style** (CZ-9.2), which starts
listening at once and sets the preview phase to Rest (voicecmp.js:56-62). Picking any option of **Pill words**,
**Composer light while you speak** or **Spoken language** (CZ-9.8 to CZ-9.10) replays the demo the same way, from the
start even when it is running (SOURCE voice-options-q.js: every pick plays the flow): 0.2 s after the pick for Pill
words (the pill's relabel runs first, SPEC 2 V-12.9) and at once for the other two (OWNER OD-0097 "every change of
these previews in the Customization stage").

CZ-4.7.3 Stop: a second click, or the script's end (the voice design ends listening at speaker end + 0.35 s), starts the
final pass (`Finishing…`).

CZ-4.7.4 After the final pass: the text stays (`afterVoice = leave`) or, with `send`, the preview sends 0.35 s after
landing: the words lift away (opacity 0.22 s, translate Y -7 over 0.3 s ez), the composer runs for 3.0 s
(`sendHold`), then returns to Rest (voice-composer.js:463-472, 704-709).

CZ-4.7.5 Cancel: the cross, or Esc while the preview listens or finishes; picking a preview state; touching Working dots.
Words withdraw as in SPEC 2 V-8.6. An open language question (CZ-4.7.8) is closed by the same events, with nothing
started.

CZ-4.7.6 **Words in the preview** (OWNER OD-0097): the preview's pill draws the picked set (SPEC 2 V-12.9), relabelled
in place when the set changes while it is open. With Listening style `Cross beside the orb` there is no pill and the
row is dimmed (CZ-9.8).

CZ-4.7.7 **Light in the preview** (OWNER OD-0097; resolves Q-CZ-6): the preview composer's light goes to the picked
level while the demo listens, finishes and lands, and back to full after, exactly as SPEC 2 V-13.4 (same follower,
0.17 s down, 0.2 s up), and the voice glow and its sweep are drawn under it as in the app (SPEC 2 V-13). With Style
`Off` there is no light and the row is dimmed (CZ-9.9).

CZ-4.7.8 **Language in the preview** (OWNER OD-0097; SOURCE voice-options.js `askLang`, `answerLang`, `playFlow`):
- `auto` (A) and `windows` (C): nothing in the pill (C's language shows in the Speech section, CZ-11.1).
- `show` (B): the language name appears in the pill as SPEC 2 V-12.10; the scripted engine reports `en` with its first
  live pass, so the preview shows `Transcribing… English` (no separator dot: OD-0097 amendment).
- `ask` (D): the demo opens the language question first (SPEC 2 V-12.11), presented through the popover presenter, so
  the stage's clip never cuts it; above the orb when the window has room, else below it with the tip up (the stage is
  pinned and compacted while the Voice orb rows are on screen, so it usually opens below; ED, OD-0095's flip). A demo
  started by a pick answers `English` by itself 1.6 s after the question opens unless the user answers first (SOURCE
  `playFlow`: `[1.5 + ask, answerLang('English')]`), and it does not take keyboard focus from the row (ED); a demo
  started by a click on the preview's orb waits for the user's answer and gives the question focus as in the app.
  After an answer the preview is "asked": later clicks on its orb listen directly until a Spoken language option is
  picked again (SOURCE: the page's `asked`, re-armed by `setLang`).

CZ-4.7.9 The preview's answer and its "asked" state are session state of the page: the answer is never written to
`voiceTyping` (CZ-4.8).

### CZ-4.8 Never in the preview
No microphone, no network, no model load, no real send, no write to any thread's draft, no persisted state other than
the settings the page edits. The preview's answer to the language question (CZ-4.7.8) is not stored; picking a Spoken
language option does store its reset of `voiceTyping.lang` (CZ-9.10), which is a setting this page edits.

---

## 5. Style (DESIGN gen.py:373-388, 557-558; cz.js:34-35, 742-745)

CZ-5.1 Section heading `Style`. Three cards in a 3-column grid, gap 8, as one radio group named `Light`:

| Card | Title | Description | Glyph (56 high tile, radius 8, a 104 x 24 capsule inside) |
|---|---|---|---|
| `beam` | `Border beam` | `A beam of colour travels the edge` | a conic beam (#4F8BFF, #B46BFF, #FF6F7D, #FFB45C over 60-92 % of the turn) riding the capsule's 1.5 px edge, with a 6 px blurred copy at 0.55 opacity, one turn every 3.4 s |
| `aura` | `Uiverse light` | `A soft two-tone glow turns around it` | a conic two-tone glow (#FF5500 0-16 %, #0055FF 84-100 %) blurred 8 px at 0.55 opacity behind the capsule, one turn every 9 s |
| `off` | `Off` | `No light around the composer` | the capsule alone |

CZ-5.2 Card: padding 8 / 8 / 11, radius 12, fill `cardfill`; hover `tile`; selected `tilesel` with a 2 px inset ring
in `sel`; press scale 0.98 (0.12 s). Title 13 / 18 semibold (padding 5 4 0); description 11.5 / 15 secondary.

CZ-5.3 Picking sets `composerLight.style` (`beam` | `aura` | `off`); the other styles' settings are kept (DESIGN gen.py:612).
The glyphs animate only while the card is on screen and stop under reduce motion (still frame).

---

## 6. Border beam settings (DESIGN cz.js:506-546, 658-667; ranges are the page's, CZ-13.2)

Value formats (cz.js:509): `x2(v) = fmt(v, 2) + '×'`, `deg(v) = round(v) + '°'`, `s(v) = fmt(v, 2) + ' s'`,
`px(v) = fmt(v, 1) + ' px'`, `pc(v) = round(v * 100) + '%'`. `round` is JavaScript `Math.round` (half up).
"Auto" values come from `BorderBeamResolved.AutoValues(beam, appDark, w, 46, 22)` with `w` = the preview composer's
width (472 or 428) and `h` = 46, the real pill's height (the playground's 52 is a web mock; amended by ED-335
item 2): the page shows the preview's Auto values (DESIGN gen.py:43-49).

### CZ-6.1 Section `Look`
| Row | Control | Values -> stored | Detail text |
|---|---|---|---|
| `Motion` | segmented sm: `Rotate` \| `Pulse` | `family` rotate \| pulse; picking a new family also sets `size` to `md` (Rotate) or `pulse-inner` (Pulse) (= `BorderBeamOptions.WithFamily`) | `Travels around the edge` (rotate) / `Breathes in place` (pulse) |
| `Shape` | segmented sm, two stacked sets that crossfade (CZ-12.1.4): Rotate `Large` \| `Line` \| `Small`; Pulse `Inside` \| `Outside` | `size` md \| line \| sm; pulse-inner \| pulse-outside | md `A wide beam with an inner glow`; line `A short streak glides along the edge`; sm `A slimmer beam, a tighter glow`; pulse-inner `Colour pools just inside the edge`; pulse-outside `A glow spills out past the edge` |
| `Colours` | swatch row (CZ-12.5), 8 palettes | `color` | the palette's name |

### CZ-6.2 Palettes (DESIGN cz.js:33, 515-518)
`colorful` `Colourful`, `mono` `Mono`, `ocean` `Ocean`, `sunset` `Sunset`, `forest` `Forest`, `candy` `Candy`, `ice` `Ice`,
`gold` `Gold`. Each swatch fill: a 135 deg gradient of the palette's border colours (`BorderBeamTables` palette `border`
list) at indices 0, 4, 6, 8 placed at 0 %, 40 %, 70 %, 100 %.

### CZ-6.3 Section `Pace and strength`
| Row | Key | Min | Max | Step | Inverted | Auto | Format | Detail |
|---|---|---|---|---|---|---|---|---|
| `Speed` | `duration` | 0.5 | 14 | 0.05 | yes (left slow, right fast) | yes: Auto duration | `s` | Auto: `Auto: the designed pace`; else `Seconds for one lap` (rotate), `Seconds for one sweep` (Line), `Seconds for one breath` (pulse) |
| `Brightness` | `brightness` | 0.2 | 3 | 0.05 | no | yes: Auto brightness | `x2` | Auto: `Auto: set for the theme`; else `Lighter or deeper colours` |
| `Intensity` | `strength` | 0 | 1 | 0.01 | no | no | `pc` | `How strongly the light shows` |
| `Follow the composer` | `respond` | switch | | | | | | `Softer at rest, fuller as you type and while a turn runs` |

### CZ-6.4 Advanced card (folded by default)
Row `Advanced`, detail `Colour recipe, glow layers and corner`, capsule button `Show` / `Hide` (CZ-12.7). The open state
is shared with the Uiverse light's Advanced card and is session state (DESIGN cz.js:124, 586-593). When open, a nested
card folds in with:

| Row | Key | Min | Max | Step | Auto | Format | Detail / rule |
|---|---|---|---|---|---|---|---|
| `Colour shift` | `hueShift` | -180 | 180 | 1 | no | `deg` | `Turns every colour around the wheel` |
| `Saturation` | `saturation` | 0 | 2.5 | 0.05 | yes | `x2` | Auto: `Auto: set for the theme`; else `Vivid or muted colours` |
| `Colour drift` | `hueRange` | 0 | 180 | 1 | no | `'±' + round(line ? min(13, v) : v) + '°'` | dimmed and inert when Pulse, Mono or Hold colours still; detail: pulse `Pulse always runs through every hue`; mono or still `Off while the colours hold still`; line `How far the colours wander (Line keeps it under 13°)`; else `How far the colours wander` |
| `Hold colours still` | `staticColors` | switch | | | | | Mono: shown on, dimmed, inert, detail `Mono always holds still`; else `The palette stops drifting` |
| `Glow size` | `glowSize` | 0.25 | 3 | 0.05 | no | `x2` | `Softness of every glow` |
| `Edge line` | `strokeX` | 0 | 2 | 0.05 | no | `x2` | `The thin line on the edge` |
| `Inner glow` | `innerX` | 0 | 2 | 0.05 | no | `x2` | `The wash inside the edge` |
| `Outer glow` | `bloomX` | 0 | 2 | 0.05 | no | `x2` | `The bloom beyond the edge` |
| `Corner` | `radius` | 0 | 26 | 1 | yes: 22 | `v + ' px'` | Auto: `Auto: follows the composer`; else `Rounder or squarer than the composer` |
| `Tuned for` | `theme` | segmented sm `Auto` \| `Dark` \| `Light` | | | | | auto `Matches the app’s theme`; else `Always the dark recipe` / `Always the light recipe` |
| caption `Outside glow` | | | | | | | shown only for Pulse outside |
| `Core softness` | `coreBlur` | 0 | 20 | 0.5 | yes | `px` | `The bright glow near the edge` (Pulse outside only) |
| `Bloom softness` | `bloomBlur` | 0 | 60 | 0.5 | yes | `px` | `The wide glow around it` (Pulse outside only) |
| `Glow width` | `glowX` | 0.35 | 4 | 0.01 | yes: `clamp(w / 350, 0.35, 4)` to 3 places | `x2` | `How far it spreads sideways` (Pulse outside only) |
| `Glow height` | `glowY` | 0.35 | 4 | 0.01 | yes: `clamp(46 / 140, 0.35, 4)` to 3 places (0.35 at the preview's 46 high; the design's 52 gave 0.371; amended by ED-335 item 2) | `x2` | `How far it spreads up and down` (Pulse outside only) |
| `Glow boost` | `boost` | 0.5 | 2 | 0.01 | no | `x2` | `Extra brightness in the glow` (Pulse outside only) |
| caption `Line` | | | | | | | shown only for Line |
| `Flare` | `spike` | 0 | 3 | 0.05 | no | `x2` | `How much the streak flares as it goes` (Line only) |

"Shown only" rows fold open and shut (CZ-12.8); a caption is followed by no divider (cz.js:608-610).

### CZ-6.5 Reset card
Row `Defaults`, detail `Border beam as Nth ships it` when every beam key equals `BorderBeamOptions.Default` (keys:
all except `fit`, `faithful`, `active`; DESIGN cz.js:38, 571-575), else `Return every Border beam setting to how it ships`.
Capsule button `Reset to defaults`, dimmed to 0.42 opacity and inert (not focusable) when already default. It sets
`Beam = BorderBeamOptions.Default` (family, shape and palette included).

### CZ-6.6 Not carried (DESIGN gen.py:613)
Geometry (always fitted), Active (Off replaces it), the playground's Extra CSS, the 1.4.1 defect switch (`faithful`), the
code readout, and the two preset buttons (`Library defaults`, `Calm for the composer`).

---

## 7. Uiverse light settings (DESIGN cz.js:548-569, 668-672)

Formats: `px(v) = fmt(v, 1) + ' px'`, `pc(v) = round(v) + '%'`.

### CZ-7.1 Section `Look`
| Row | Control | Stored | Detail |
|---|---|---|---|
| `Colours` | segmented sm `Original` \| `Model` \| `Accent` \| `Custom` | `scheme` original \| maker \| accent \| custom | original `Orange and blue`; maker `The model in use: {maker name}`; accent `Your accent: {accent name}`; custom `Two colours you pick` |
| `Your colours` (folds open only for Custom) | two colour wells (CZ-12.6) labelled `One side`, `The other`; accessible names `First colour`, `Second colour` | `c1`, `c2` (#RRGGBB) | `One for each side of the glow` |
| `Rim light` | switch | `rim` | `A bright line hugging the edge` |
| `Halftone` | switch | `dots` | `The glow seen through fine dots` |

(`{maker name}` is the preview maker; `{accent name}` the user's accent name from `AccentPalette`.)

### CZ-7.2 Section `Motion`
| Row | Key | Min | Max | Step | Inverted | Format | Detail |
|---|---|---|---|---|---|---|---|
| `Speed` | `speed` | 3 | 40 | 0.5 | yes | `fmt(v, 1) + ' s'` | `Seconds for one turn` |
| `Breathing` | `breathing` | switch | | | | | `The glow slowly swells and settles` |

### CZ-7.3 Section `Brightness by moment`
| Row | Key | Range / step | Format | Detail |
|---|---|---|---|---|
| `At rest` | `rest` | 0..100 / 1 | `pc` | `While the composer waits` |
| `While typing` | `typing` | 0..100 / 1 | `pc` | `As you write a message` |
| `While a turn runs` | `running` | 0..100 / 1 | `pc` | `As the agent works` |

### CZ-7.4 Advanced card
Detail `Halo, rim and dot sizes`. Nested rows:
| Row | Key | Range / step | Format | Dim when | Detail |
|---|---|---|---|---|---|
| `Halo size` | `haloSize` | 0..40 / 1 | `px` | | `How far the glow reaches` |
| `Halo softness` | `haloBlur` | 4..64 / 1 | `px` | | `How soft its outer edge is` |
| `Rim width` | `rimW` | 0.5..5 / 0.25 | `px` | Rim light off | on: `Thickness of the rim light`; off: `Turn on Rim light to use it` |
| `Dot spacing` | `dotCell` | 2..8 / 0.5 | `px` | Halftone off | on: `Finer or coarser dots`; off: `Turn on Halftone to use it` |
| `Dot strength` | `dotStr` | 0..100 / 1 | `pc` | Halftone off | on: `How visible the dots are`; off: `Turn on Halftone to use it` |

### CZ-7.5 Reset card
Detail `The Uiverse light as Nth ships it` when default (every `UiverseAuraOptions` key equal to `Default`), else
`Return every Uiverse light setting to how it ships`; button `Reset to defaults` sets `Aura = UiverseAuraOptions.Default`.

---

## 8. Off (DESIGN cz.js:673)
One card, no heading, a note: `The composer has no light. Your Border beam and Uiverse light settings are kept for when you
turn one back on.`

---

## 9. Voice orb (OWNER OD-0091 amendments 1 and 6; DESIGN cz.js:343-360, voicecmp.js:207-222)

Section heading `Voice orb`. One card, rows in this order (OWNER OD-0097 adds three; their places are an ED: the words
right after the pill's style, the light and the language before After voice input, so the card reads look, then
behaviour while you speak, then what happens to the words): `Orb` (CZ-9.1), `Listening style` (CZ-9.2), `Pill words`
(CZ-9.8), `Orb size` (CZ-9.3), `Attachment button` (CZ-9.4), `Composer light while you speak` (CZ-9.9), `Spoken
language` (CZ-9.10), `After voice input` (CZ-9.5), the note (CZ-9.6). All values are `voiceOrb` (CZ-13.4).

CZ-9.1 **`Orb`**, top-aligned row (title and detail on the left, tiles on the right): a radio group of two tiles, 12 px
apart. Tile: 96 x 56, radius 8, `tile` fill, a 52 px orb view showing the ball at 30 px (SPEC 2 `VoiceOrbView`,
frame state: activation 0, speed 1, scale 1, glow 0.25 for the selected tile and 0 for the other, reduce motion
respected), the name below in 12 / 16 (`Siri Wave`, `Voice Membrane`), 6 px gap. Selected: `tilesel` fill and a 2 px
inset `sel` ring, the name in primary text; hover `tilesel`; press scale 0.97. Arrow keys move the choice (wrapping) and
focus. Detail: `Siri Wave, the default` / `Voice Membrane`. Stored `orb`: `siri` | `membrane`.

CZ-9.2 **`Listening style`**, stacked row (title and detail above, the control below, gap 8, bottom padding 11):
segmented sm `Pill in the composer` | `Cross beside the orb` | `Floating pill`; stored `lstyle` `pill` | `cross` |
`float`. Detail: pill `The orb opens into a glass pill that says what it hears; the cross at its far end cancels`;
cross `A cross slides out beside the orb to cancel; the text field shows the words`; float `The orb lifts into a glass
pill above the composer, the text keeps its full width`. Picking starts the preview's voice demo (CZ-4.7.2).

CZ-9.3 **`Orb size`**: segmented sm `32 px` | `26 px`; stored `orbSize` 32 | 26; detail `32 px, the default` /
`26 px, a quieter orb`. The preview's orb springs to the new size (SPEC 2 V-11.4).

CZ-9.4 **`Attachment button`**: segmented sm `Inside the composer` | `Outside`; stored `attach` `in` | `out`; detail
`At the composer’s left, inside it` / `In the margin beside the composer`. The preview composer's width and inset ease
(CZ-4.3).

CZ-9.5 **`After voice input`**, stacked row: segmented sm `Leave the text in the composer` | `Send it as the prompt`;
stored `afterVoice` `leave` | `send`; detail leave `The words wait in the composer until you press send`; send
`The words go out as your prompt as soon as they arrive`.

CZ-9.6 **Note**: `Click the orb in the preview to try it: it listens until you click it again (the cross or Esc cancels),
then the words arrive.`

CZ-9.7 Floating pill in the preview: the composer here is narrow, so while the floating pill is open any line drawn
above the composer that the pill's rectangle crosses fades out (0.16 s) and back (0.25 s, 0.05 s delay) (DESIGN
gen.py:335-336, voicecmp.js:185-187). The same rule applies in the app (SPEC 2 V-12.7).

CZ-9.8 **`Pill words`** (OWNER OD-0097 "the pill's words (L1 to L6 as the page words them)", default L1; stored
`labels`). Stacked row (as CZ-9.2: title and detail above, the control below, gap 8, bottom padding 11), the control a
menu button (CZ-12.12) left-aligned under the detail. ED: six sets of three words do not fit a segmented control in the
520 px column (one option is about 250 px wide), so they take the page's own menu pattern (the Voice typing page's
Language menu); stacked because the button's text is as wide as the words.
- Items and the button's text (SOURCE the page's option titles): the set's three words joined by ` · ` (space, U+00B7,
  space; a menu's text, not the pill: the pill never draws a dot, OD-0097 amendment): `Listening… · Transcribing… ·
  Finishing…` (L1), `Listening… · Writing… · Finishing…` (L2), `Hearing you… · Writing… · Tidying up…` (L3), `Speak now ·
  Transcribing… · Almost done…` (L4), `Listening… · Typing… · Wrapping up…` (L5), `Listening · Transcribing · Finishing`
  (L6); the words exactly as SPEC 2 V-12.9.
- Detail per set (ED, the page's option notes made user-facing: the page's references to L1, the proposal and the owner
  are dropped, no final full stop as the page's other details): L1 `What the app is doing, plainly`; L2 `Writing says
  what you see: the words appearing in the composer`; L3 `Warmer and personal`; L4 `An invitation in silence, then
  what the app is doing`; L5 `Names the composer’s own state: it is typing`; L6 `Without the ellipses: quieter, and the
  pill stays narrower`.
- Dimmed (CZ-12.2.7: 0.5 opacity, inert, not focusable) while Listening style is `Cross beside the orb`, which draws no
  pill (ED), with the detail `Cross beside the orb has no pill, so no words are shown`; the stored value is kept.
- A pick replays the demo after 0.2 s (CZ-4.7.2) and relabels an open pill (CZ-4.7.6).

CZ-9.9 **`Composer light while you speak`** (OWNER OD-0097 "the composer light while you speak (B1 Steps aside, B2 Dims
to 35 %, B3 Stays on)", default B1; stored `lightVoice`). Stacked row with a segmented sm control (CZ-12.1): `Steps
aside` | `Dims to 35 %` | `Stays on` (`away` | `dim` | `stay`; the owner's words; `35 %` with a space as the ruling
writes it, the button never wraps). ED: stacked as the other long-label rows (CZ-9.2, CZ-9.5): beside its 30-character
title the control would leave the detail three lines.
- Detail per option (ED, from the page's notes; "light" for "beam", since the setting applies to whichever light is on,
  SPEC 2 V-13.4): away `The light fades out as you start speaking and returns when the text is done: one light on the
  composer at a time`; dim `The light keeps running at 35 % under the voice glow`; stay `The light keeps running at full
  strength under the voice glow`.
- Dimmed while Style is `Off` (ED), with the detail `No effect while the composer light is Off`; the value is kept.
- A pick replays the demo at once; the preview shows the light's behaviour with the voice glow (CZ-4.7.7).

CZ-9.10 **`Spoken language`** (OWNER OD-0097 "the language behaviour (A Auto-detect, silent; B Auto-detect, named in the
pill once heard; C the Windows display language; D asked on the first click of the orb ...)", default A; stored
`langMode`). Title (ED): `Spoken language` (the page's question was "The language default"; the Speech section already
has a `Language` row, CZ-11.1). Inline row with a menu button (CZ-12.12) on the right, as the Voice typing page's
`Language` row draws its menu (ED: four options of up to 30 characters do not fit a segmented control).
- Items and the button's text (SOURCE the page's option titles): `Auto-detect, silent` (`auto`), `Auto-detect, named in
  the pill` (`show`), `Windows display language` (`windows`), `Asked on first use` (`ask`).
- Detail per option (ED, from the page's notes): auto `Whisper detects the language each time; nothing is shown`; show
  `Whisper detects the language, and the pill names it once it is heard, so a wrong guess shows at once`; windows
  `{Language}, Windows’ display language, from the start; Auto-detect stays one click away on the Voice typing page`
  (when Windows' language is not in the list: `Windows’ display language is not in the list, so Whisper detects the
  language`); ask `The first click on the orb asks which language you will speak; the answer is kept on the Voice typing
  page`. While `voiceTyping.lang` holds a language picked on the Voice typing page (or a past answer) the detail is
  instead `{Language or Auto-detect} is set on the Voice typing page and comes first; picking here clears it` (ED).
- Picking any option, the current one included, stores `langMode` and sets `voiceTyping.lang` to `null` (SPEC 2 V-7.14:
  the latest choice wins; D asks again), then replays the demo at once (CZ-4.7.8).

CZ-9.11 **Not in the Voice orb card**: the explicit language list (English, Hindi, Urdu and the rest) stays on the
Voice typing page (OWNER OD-0097, SPEC 2 V-16.3.1); the Speech section summarises it (CZ-11.1).

---

## 10. Working dots (OWNER OD-0089 amendment; DESIGN cz.js:619-641)

CZ-10.1 Section heading `Working dots`. Rows:
1. `Dot colour`: segmented sm `Accent` | `Model colour` | `Custom`; detail: Accent `Your accent colour, as the rest of
   Nth`; Model colour `The colour of the model in use: {maker name}`; Custom: the picked swatch's name.
2. `Custom dot colour` (folds open only for Custom; no visible title, the accessible names below): two labelled swatch
   lines, label column 62 px (11.5, secondary): `Classic` (the 10 Classic accents) and `Signature` (the 12 Signature
   accents), each a radio group (`Classic dot colours`, `Signature dot colours`), swatches drawn in the theme's hex
   (`AccentSwatch.Hex(dark)`). The Windows accent swatch draws the lifted system accent as the accent picker does.
3. Note: `The dots run along the bottom of the composer while a turn runs; touching this switches the preview to Running a
   turn.`

CZ-10.2 Any pick in this section switches the preview to Running (CZ-4.6) and cancels the voice demo.

CZ-10.3 Defaults: mode `accent`; the custom pick `blue` (DESIGN cz.js:126) so the first switch to Custom shows Blue
selected.

CZ-10.4 **The dots' colour** (`WorkingDotsColor.Resolve(settings, maker, theme)`):
- `accent`: `Chrome.Accent` (the user's accent, as the source draws the dots: ComposerWorkingDots.swift:13-19);
- `model`: the maker's `mid` stop toned for the window: `czTone(mid, dark, title)` (CZ-12.11), the maker being the
  thread's provider brand mapped through `JewelPalettes.For(brand).Maker` into `MakerLiquids` (in the preview: the
  preview maker); the Purple brand (no maker) uses `JewelPalettes` Purple's mid `#8C57F7` with no title colour;
- `custom`: the picked accent swatch's hex for the theme.
Every composer's `ComposerWorkingDotsSlot` passes this colour to `ComposerWorkingDots`; a change (setting, theme, model,
accent) reaches the running field through `DotFieldControl.Retarget(ease: true)`: one eased blend on the render thread
(ED: the field's existing 0.32 s ease, ED-253 SPEC 6.4; the design's preview used 0.4 s ease, gen.py:463; one curve is
kept for all dot colour changes). Under reduce motion the still dots change at once (existing rule).

---

## 11. Speech (DESIGN cz.js:642-654)

CZ-11.1 Section heading `Speech`, after the sticky container. Three link rows, each with a capsule button `Change` and a
small trailing chevron, each opening the Voice typing page (`SettingsNavigation` page change):

| Row | Detail |
|---|---|
| `Speech model` | engine local: the selected model's name (SPEC 2 V-9.6 `ModelName`, e.g. `Large v3 Turbo, balanced 874 MB`); cloud: `Not used while the cloud engine is on` |
| `Language` | Auto: `Auto-detect: Whisper works out the language`; else the language's name. With nothing picked (`voiceTyping.lang` null, OD-0097) it follows the language default (ED wording from the page's `Settings > Voice typing` values): A `Auto-detect: Whisper works out the language`; B `Auto-detect, named in the pill while you speak`; C `{Language} (from Windows)` (A's text when Windows' language is not in the list); D `Asked on first use` |
| `Engine` | local `On this PC: your voice never leaves this computer`; cloud `OpenAI cloud, with your own key` |

CZ-11.2 Before SPEC 2 V6 is merged the section is not built (phase C5).

---

## 12. Controls (new components in `CzControls.cs`)

Colour tokens (DESIGN gen.py:91-92, D3 dark / light). Where the Look card (ED-253) already defines a token of the same
name, reuse it.

| Token | Dark | Light |
|---|---|---|
| `tile` | rgba(255,255,255,.07) | rgba(0,0,0,.05) |
| `tilesel` | rgba(255,255,255,.1) | rgba(0,0,0,.07) |
| `sel` | rgba(255,255,255,.24) | rgba(0,0,0,.72) |
| `cap` | rgba(0,0,0,.2) | rgba(255,255,255,.55) |
| `caph` | rgba(0,0,0,.3) | rgba(255,255,255,.82) |
| `pill` | rgba(255,255,255,.16) | #FFFFFF |
| `rim` | rgba(255,255,255,.22) | rgba(0,0,0,.22) |
| `trk` | rgba(255,255,255,.1) | rgba(0,0,0,.1) |
| `trkfill` | rgba(255,255,255,.24) | rgba(0,0,0,.72) |
| `ring` (swatch) | rgba(255,255,255,.92) | rgba(0,0,0,.78) |
| `hov` | rgba(255,255,255,.08) | rgba(0,0,0,.05) |
| `prs` | rgba(255,255,255,.14) | rgba(0,0,0,.1) |
| `popbg` / `popedge` | #1E1E1E / rgba(255,255,255,.2) | #FFFFFF / rgba(0,0,0,.08) |
| focus ring | the user's accent (`Chrome.Accent`) | same |

Easing names: `ez` = cubic-bezier(.32,.72,0,1); `ease` = cubic-bezier(.25,.1,.25,1).

CZ-12.1 **Segmented control** (gen.py:228-240; cz.js:420-436, 375-392).
1. Track: height 28 (sm 26), radius 14 (sm 13), padding 2, `cap` fill. Buttons: height 24 (sm 22), padding 0 11 (sm 0 9),
   radius 12 (sm 11), font 12 (sm 11.5) medium, secondary text, the picked one primary.
2. The pill (`pill` fill, a 0.5 px inset `rim`, shadow 0 1 2 rgba(0,0,0,.18)) is measured from the picked button's own
   box, so a label never leaves its capsule; it moves (left and width) over 0.3 s ez; the first placement does not
   animate.
3. Radio group semantics: one tab stop (the picked button); Left/Up and Right/Down move the pick with wrap-around and
   keep focus on the new pick; press scale 0.96 (0.12 s); label colour 0.15 s.
4. Two sets in one place (Shape, CZ-6.1): the outgoing set fades 0.16 s and moves 3 px down over 0.3 s ez; the incoming
   set fades in 0.28 s after 0.08 s and rises over 0.4 s ez.

CZ-12.2 **Slider with value and Auto** (gen.py:243-260; cz.js:448-489).
1. Track 150 x 22 with a 9 px inner inset; rail 4 px high radius 2 `trk`; fill `trkfill`; knob 18 x 14 radius 7 white,
   shadow 0 1 3 rgba(0,0,0,.3) and a 0.5 px edge. The fill width and knob position ease 0.32 s ez except while dragging.
   Hover: knob scale 1.06 (0.15 s); drag: scale 1.1.
2. Value text: 48 px right-aligned, 11.5, tabular figures, secondary. Auto capsule: 40 x 20 radius 10, 11 medium, text
   `Auto`; on: `pill` fill with rim and shadow, primary text; off: transparent with a 1 px divider ring. Absent (space
   kept) for sliders without Auto. Accessible name `Automatic {row title}`.
3. Mapping: `f = clamp((v - min) / (max - min), 0, 1)`; the knob sits at `f` (inverted sliders at `1 - f`).
4. Setting: `snap(v) = round(clamp(v, min, max) / step) * step` rounded to 4 decimals (JavaScript half-up `round`).
5. Pointer: press anywhere jumps to the pointer and captures it; drag follows. Keys: Left/Down `-step`, Right/Up `+step`
   (sign flipped for inverted sliders), PageDown/PageUp `-(max - min) / 10` / `+`, Home `min` (inverted: `max`), End
   `max` (inverted: `min`).
6. Auto: while the stored value is null the slider shows the Auto value and the capsule is on; moving the slider stores
   a value (Auto off); clicking the capsule toggles between null and the current shown value.
7. Dimmed rows (`na`): the row at 0.5 opacity (0.3 s); the slider, its capsule and the switch are inert and not focusable
   (ED; the design only changed the cursor).
8. Accessibility: role slider; value range; value text = the formatted value.

CZ-12.3 **Switch**: the port's `SettingsSwitch` (small). Forced-on dimmed switches (Mono's Hold colours still) are inert.

CZ-12.4 **Capsule button** (gen.py:288-299): height 26, radius 13, 12 medium, `cap` fill, hover `caph`, press 0.96;
padding 0 12. Link buttons (`Change`) carry a 5 x 8 chevron after the label.

CZ-12.5 **Swatch row** (gen.py:273-281): swatches 24 x 24 hit area, a 16 px dot with a 0.5 px `rim` inset, gap 2. The
selection ring (1.5 px `ring`) fades in 0.18 s and scales from 0.8 over 0.24 s ez; hover dot scale 1.12; press 0.86
(0.08 s). Radio group; tooltip and accessible name = the swatch name.

CZ-12.6 **Colour wells** (gen.py:282-286): a 20 px round well in the colour with a label after it (12, secondary), the
pair 14 px apart. Clicking opens a colour picker: UNRESOLVED which (Q-CZ-3). A picked colour is stored as `#RRGGBB`
upper case (`UiverseAuraOptions.NormalizedHex`); the well's fill eases 0.3 s.

CZ-12.7 **Advanced capsule** (gen.py:289-298): padding 0 9 0 12; a 30 px label slot whose `Show` / `Hide` crossfade;
the chevron turns 180 deg over 0.3 s ez when open; `aria-expanded` = open.

CZ-12.8 **Fold rows and sections** (gen.py:189-193): a row or section that appears or disappears folds its height from
0 to its natural height over 0.34 s `ease` while fading in over 0.24 s after 0.08 s; folding shut fades over 0.14 s with
the same height ease; content is clipped while folding. Dividers between rows follow the rows that are shown (no divider
above the first shown row; none after a caption) and fade 0.3 s.

CZ-12.9 **Crossfading text** (gen.py:220-225): the outgoing text fades over 0.1 s; the incoming text fades in over 0.2 s
after 0.08 s; both occupy one grid cell, so a detail's height eases with the fold rules when line counts differ.

CZ-12.10 **`fmt(v, n)`**: JavaScript `(Math.round(v * 10^n) / 10^n).toString()` (shortest round-trip, no trailing zeros,
invariant culture; `-0` prints `0`).

CZ-12.12 **Menu button** (the Voice typing page's Language menu made a shared control, DESIGN voice.js:155-176 and
gen.py `.czms`, `.czmc`, `.czpop`, `.pitem`; used by CZ-9.8, CZ-9.10 and SPEC 2 V-16.3.1).
1. Button: 28 high, radius 14, padding 0 10, gap 7, `cap` fill, hover and open `caph`, press scale 0.97; the value at 13
   regular in a crossfading text (CZ-12.9) whose width eases 0.3 s ez; a 7 x 5 chevron at 0.7 opacity turning 180 deg
   over 0.3 s ez while open. Accessible: menu button pattern (`aria-haspopup` menu, expanded state), name = the row
   title.
2. Menu: padding 6, radius 12, `popbg` with a 1 px `popedge` ring and the shadows 0 16 40 rgba(0,0,0,.32) and 0 2 8
   rgba(0,0,0,.16); items 28 high, radius 7, padding 0 8, gap 8: a 12 px check (shown on the current item, fading in
   0.16 s and scaling from 0.6 over 0.24 s ez), the item's text at 13; hover `hov`, press `prs`. Width (ED): `max(220,
   ceil(widest item text) + 48)` (the Language menu's 220, widened so no item is cut: Pill words about 300).
3. Placement: below the button, 6 px gap; right-aligned under an inline row's button, left-aligned under a stacked
   row's; flips above when the window has no room below (ED). Open: opacity 0.2 s ease and from translate Y -4, scale
   0.96 over 0.22 s cubic-bezier(.2,.9,.25,1), origin at the button; close 0.15 s.
4. Keys and focus (as CZ-4.5.2): opening focuses the current item; Up/Down move (wrapping), Home/End; Enter/Space pick;
   Esc closes the menu only and returns focus to the button; a click outside closes it; a pick closes it and focuses the
   button. Items are `menuitemradio` with the checked state. Dimmed rows: the button is inert and not focusable.
5. A menu is not a popover: no OD-0095 tip (ED; the model menu's tip is Q-CZ-5).

CZ-12.11 **`czTone(hex, dark, title)`** (DESIGN cz.js:107-116): relative luminance `L` (sRGB, WCAG); a colour is legible
when `L >= 0.12` (dark) or `L <= 0.42` (light). If `hex` is legible, return it; else target = `title` if legible,
otherwise `#FFFFFF` (dark) / `#1C1C1E` (light); for `t = 0.1; t < 1; t += 0.05` (same floating-point accumulation)
return the first legible `mixHex(hex, target, t)`; else return the target. `mixHex` mixes 8-bit sRGB channels linearly
and rounds half up.

---

## 13. Persistence and wiring

CZ-13.1 **`composerLight`** (exists; PORT ComposerLightSettings.cs). The page edits `AppSettings.ComposerLight` through
`with` copies; every change applies to every composer at once (PORT ComposerLightSlot.cs:33).

CZ-13.2 **Range mismatch to settle in C1 (ED).** The page's ranges are the locked design's: Speed step 0.05 (the port's
`DurationSlider` steps 0.02) and Corner 0..26 (the port's `RadiusSlider` 0..32). Because 0.05 is not a multiple of 0.02,
`Clamped()` would move a page value (0.55 -> 0.56). Change `BorderBeamOptions.DurationSlider` to step 0.01 (every 0.02
and 0.05 value stays put) and keep `RadiusSlider` at 0..32 for storage; the page's Corner slider runs 0..26 and shows a
stored value above 26 at its right end without rewriting it until the user moves it. Existing ED-251 tests that assert
the 0.02 step are updated by this ED (with the reason), never edited to match output.

CZ-13.3 **`workingDots`** (new). Key `workingDots`, compact JSON `{"mode":"accent","custom":"blue"}`; `mode` in
`accent` | `model` | `custom`; `custom` an `AccentPalette` id (`windows` for the Windows accent). Lenient read: missing or
unknown values keep their defaults, unreadable data is the default (same rules as `ComposerLightSettings.FromJson`).

CZ-13.4 **`voiceOrb`** (record defined in SPEC 2 V-16.1). Key `voiceOrb`, JSON
`{"orb":"siri","lstyle":"pill","orbSize":32,"attach":"in","afterVoice":"leave","labels":"L1","lightVoice":"away","langMode":"auto"}`;
defaults as shown (OWNER OD-0091 sixth amendment: pill, Siri Wave, 32 px, inside; first amendment: leave the text;
OD-0097: L1, Steps aside, Auto-detect silent). Lenient read; `orbSize` other than 26 reads as 32; an unknown `labels`,
`lightVoice` or `langMode` reads as its default. `Spoken language` also writes `voiceTyping.lang` = null (CZ-9.10); both
writes go through one coalesced store write (CZ-13.5).

CZ-13.5 **Write coalescing (ED).** A slider drag must not write the settings store every frame. `AppSettings` keeps the
in-memory value current on every change (so the preview and all composers follow live) and writes the store at most
every 250 ms while values keep changing, plus once when a drag ends and on app exit. Test with the store fake counting
writes.

CZ-13.6 **What drives `ComposerLightView`** (PORT ComposerLightView.cs:182-191): `Settings` (CZ-13.1), `Phase` (app:
`ComposerLightSlot` rule; preview: CZ-4.6), `Palette` (app: the thread's brand; preview: CZ-4.5.5), `ComposerRect`,
`FadeBottom`. SPEC 2 adds `VoiceLight` (V-13.4, a level, replacing the earlier `GiveWay` bool); the preview passes the
same value its voice controls produce for `voiceOrb.lightVoice` (CZ-4.7.7).

---

## 14. Transitions (OWNER OD-0076: nothing cuts)

CZ-14.1 Every transition of this page, with its timing (all on the frame clock, ED-237; reduce motion stills them):

| Change | Transition |
|---|---|
| Style card pick | card fill and ring 0.2 s; the style's sections fold (CZ-12.8); the light changes style through `ComposerLightView`'s own crossfade (ED-251) |
| Shape set swap | CZ-12.1.4 |
| Segmented pick | pill 0.3 s ez; label colour 0.15 s |
| Slider value | fill and knob 0.32 s ez (not while dragging); value text crossfades only when its width class changes (else replaced in place: digits only) |
| Auto toggle | capsule fill, colour and shadow 0.2 s; the knob eases to the new position 0.32 s ez |
| Switch | the port's `SettingsSwitch` motion |
| Dimmed row | opacity to 0.5 over 0.3 s |
| Row and section fold | CZ-12.8 |
| Detail text | CZ-12.9 |
| Advanced open | chevron 0.3 s ez, label crossfade, nested card fold |
| Reset idle | opacity 0.42 over 0.25 s |
| Swatch pick | CZ-12.5 |
| Preview maker | chip name crossfade + width 0.3 s ez; dot colours 0.35 s; the light's palette through its own ease (ED-251) |
| Menu open / close | CZ-4.5.2 |
| Preview state | the composer's own state animations (typing placeholder crossfade, send/stop 0.28-0.32 s, dots fade 0.4 s ease-in-out, ring spin ease 0.35 s) |
| Attachment place | composer width and inset 0.42 s ez; text inset 0.34 s ez; inside button fades 0.28 s and scales to 0.6 over 0.34 s ez while the outside one comes in |
| Orb size | the orb's size spring (response 0.34 s, damping 0.86), SPEC 2 V-11.4 |
| Orb choice | SPEC 2 V-11.5 (crossfade, phase handed over); tiles' glow follows |
| Menu button pick (CZ-12.12) | value crossfade and width 0.3 s ez; chevron 0.3 s ez; menu open 0.2 s / 0.22 s, close 0.15 s; check 0.16 s / 0.24 s ez |
| Pill words | the open pill relabels: out 0.14 s, swap at 0.16 s, the label's crossfade in, width 0.42 s ez (SPEC 2 V-12.9); the demo replays 0.2 s after the pick |
| Composer light while you speak | the preview light's level follows 0.17 s down / 0.2 s up (SPEC 2 V-13.4); the voice glow fades in and out by its package fade |
| Spoken language | B: the name joins the label with its crossfade, the width eases 0.42 s ez (V-12.10); D: the question opens 0.18 s / 0.24 s ez from its tip and closes 0.22 s after an answer (V-12.11) |
| Row dims (Pill words under Cross, the light row under Off) | opacity to 0.5 over 0.3 s; detail crossfade (CZ-12.9) |
| Working dots colour | CZ-10.4 |
| Pinned block | compaction CZ-3.5.2; hairline 0.25 s |
| Chrome accessory | the existing chrome row behaviour |
| Theme change | every token through the Settings turnover of ED-253 |

CZ-14.2 Page switches between sidebar pages are `SettingsView`'s behaviour, unchanged here. The design animates a page in
(opacity 0.3 s after 0.1 s, rise 6 px over 0.45 s ez) and out (0.2 s, 0.3 s ez) (gen.py:165-166); if `SettingsView`
still cuts between pages when this unit lands, that is recorded for the app-wide transitions unit (OD-0076 audit), not
fixed here.

---

## 15. Accessibility and keyboard

- Every control has an accessible name equal to its row title (or the names given above); details are the help text.
- Segmented controls, style cards, orb tiles and swatch rows are radio groups (one tab stop, arrow keys).
- Sliders: role slider, range, value text (CZ-12.2.8). Switches: toggle pattern. Advanced: expand/collapse pattern.
- Esc: closes the preview's menu or a row's menu (CZ-12.12) if open; else the preview's language question
  (CZ-4.7.8); else cancels a running preview voice demo; else falls to the window (Settings Esc behaviour from ED-253
  `WindowTransients`). Never closes the Settings window while a menu, the question or the demo consumed it.
- Menu buttons: the menu button pattern; items `menuitemradio` (CZ-12.12). The preview's language question is a dialog
  named `Voice language` (SPEC 2 V-12.11).
- Focus visuals: a 2 px ring in the accent colour, offset 1-2 px, only for keyboard focus (as the Look card).
- Keyboard focus never lands under the pinned block (CZ-3.5.6).

---

## 16. Performance budgets (ED; measured at the gate on a quiet PC)

- Scrolling the page: no layout pass per frame for sections; the UI thread's work per scroll frame under 1 ms; frame gaps
  within the app's scroll gate.
- The preview light, orb tiles and preview orb render on the composition thread; nothing allocates per frame (allocation
  test as ED-252's REVIEW test).
- The orb tiles and the style glyphs request frames only while visible; the page stops all frame requests when the page
  is not shown or the window is minimised.
- Opening the page: under 150 ms to first frame on the owner's PC with GPU programs warm (`GpuWarmup`).

---

## 17. Records to write at merge

- ED: the page's file placement and the port-component rule (CZ-1.4, CZ-3.1).
- ED: sticky clip instead of painted backdrop (CZ-3.5.4); focus scroll (CZ-3.5.6).
- ED: `ComposerPreview` assembled from the composer's parts (CZ-4.3); the stage backdrop (CZ-4.1).
- ED: preview palette rule (CZ-4.5.5); preview maker initial value and session scope (CZ-4.5.3).
- ED: voice demo through the scripted session (CZ-4.7.1).
- ED: duration step 0.01 in storage (CZ-13.2); write coalescing (CZ-13.5); dimmed rows inert (CZ-12.2.7).
- ED: one dot-colour ease (CZ-10.4); `waveform` symbol mapping (CZ-2.1.1); unused tints (CZ-2.1.2).
- OD-0097 EDs: the three rows' places, titles (`Pill words`, `Spoken language`), stacked/inline choice and details
  (CZ-9.8 to CZ-9.10); dimming under Cross and under Off; the menu button as a shared control and its width rule
  (CZ-12.12); the preview's question placement, self-answer and focus (CZ-4.7.8); the Speech `Language` texts (CZ-11.1).
- `porting\parity_matrix.json` / `ui_contract.json` entries for the two pages as owner-ruled deviations; THIRD_PARTY_NOTICES
  needs nothing new from this spec (border-beam, Uiverse, Liquid Orb Editor are credited by ED-251 and SPEC 2).

---

## 18. Build order (each phase is mergeable on its own)

| Phase | Content | Mergeable because |
|---|---|---|
| **C1** | Sidebar entry (Customization only), page shell, chrome accessory, the pinned block **without** sticky behaviour (it scrolls), stage + `ComposerPreview` (no voice parts: the attachment button stays where the source has it until SPEC 2 V5), model switch and menu, Style, Border beam, Uiverse, Off sections, Reset, Advanced, the range ED (CZ-13.2), write coalescing | a complete page for the composer light; no voice dependency |
| **C2** | Working dots section, `workingDots` setting, app-wide dots colour (CZ-10) | independent of voice |
| **C3** | Sticky stage: pin, compaction, release, clip, hairline, focus scrolling (CZ-3.5) | pure layout behaviour |
| **C4** | Voice orb section, `voiceOrb` editing, the preview's orb, pill, cross, glow, attachment place and voice demo; OD-0097: the menu button (CZ-12.12), Pill words, Composer light while you speak, Spoken language (CZ-9.8 to CZ-9.10) and their preview (CZ-4.7.6 to CZ-4.7.9) | needs SPEC 2 V1 + V3 + V4 + V5 |
| **C5** | Voice typing sidebar entry and the Speech section (CZ-11), its Language row following the language default (OD-0097) | needs SPEC 2 V6 |

Each phase: tests first (section 19), adversarial review, fixes, merge, gate (quiet PC for native frame tests).

---

## 19. Tests, in test-first order

Each test names its clause; traits per R-TEST-1 (`[Trait("Decision", "ED-nnn")]` with the OD in the comment). Headless
UI tests per R-TEST-7. Reference values come from the design files cited, never from running the port (R-TEST-4).
Geometry fixtures: a script under `tests\fixtures\customization\` (as `tests\fixtures\composer-lights\ref.js`) measures
the locked page in headless Chrome (window at 780 x 580, the sidebar at 212) and commits the JSON; tests compare.

### C1
| # | Test | Clause |
|---|---|---|
| CZ-T1 | `SettingsPage` raw values `customization`, `voiceTyping`; `AllCases` order puts them after Appearance | CZ-2.1 |
| CZ-T2 | Titles, symbols, keywords exact strings; search `glow` shows Customization | CZ-2.1, CZ-2.1.3 |
| CZ-T3 | `SymbolCatalogue` resolves `sparkles` and `waveform` to non-empty icons | CZ-2.1.1 |
| CZ-T4 | No code path reads `SettingsPage.Tint` for the sidebar | CZ-2.1.2 |
| CZ-T5 | Customization page has the chrome accessory `Preview state` with `Rest`, `Typing`, `Running a turn`; default Rest | CZ-2.2, CZ-3.2 |
| CZ-T6 | `CzText`: every label, detail and caption string byte-equal to the design (one data-driven test over the tables of CZ-5..CZ-8) | CZ-5..CZ-8 |
| CZ-T7 | `fmt` matches JavaScript for a table of values incl. halves, negatives, `-0`, 3.69, 14 | CZ-12.10 |
| CZ-T8 | Slider snap: `round(clamp/step)*step` to 4 places, half-up, for every row's (min, max, step) | CZ-12.2.4 |
| CZ-T9 | Inverted slider: knob position and every key's direction (Arrow, Page, Home, End) | CZ-12.2.3, CZ-12.2.5 |
| CZ-T10 | Auto: null shows the Auto value from `BorderBeamResolved.AutoValues` at w 472; moving stores a value; capsule toggles | CZ-12.2.6, CZ-6 |
| CZ-T11 | Duration step 0.01 in storage: page values 0.55, 3.69, 13.95 survive `Clamped()` and a JSON round trip; Corner 30 stored is shown at the end and not rewritten | CZ-13.2 |
| CZ-T12 | Motion pick sets the family's first shape; Shape sets swap with the family | CZ-6.1 |
| CZ-T13 | Colours swatches: 8, names, gradient stops from the tables | CZ-6.2 |
| CZ-T14 | Colour drift dimmed and inert for Pulse, Mono, Hold still; detail per case; Line format caps at 13 | CZ-6.4 |
| CZ-T15 | Hold colours still forced on and inert under Mono | CZ-6.4 |
| CZ-T16 | Pulse-outside rows and caption fold open only for Pulse outside; Flare only for Line; no divider after a caption | CZ-6.4, CZ-12.8 |
| CZ-T17 | Reset idle when default (keys except fit, faithful, active), active otherwise; resets family/shape/palette | CZ-6.5 |
| CZ-T18 | Uiverse rows: Colours scheme details incl. `{maker name}` and `{accent name}`; Your colours folds only for Custom | CZ-7.1 |
| CZ-T19 | Uiverse Speed inverted 3..40 step 0.5, format `fmt(v,1) s` | CZ-7.2 |
| CZ-T20 | Rim width and dot rows dim with their switches; details swap | CZ-7.4 |
| CZ-T21 | Uiverse Reset | CZ-7.5 |
| CZ-T22 | Off shows the note card only; switching back keeps both styles' settings | CZ-8, CZ-5.3 |
| CZ-T23 | Style cards: three, radio semantics, picks write `style` | CZ-5 |
| CZ-T24 | Advanced open state shared between styles, session only | CZ-6.4, CZ-7.4 |
| CZ-T25 | Segmented control: pill measured from the picked button (a long label never escapes); arrow keys wrap and refocus | CZ-12.1 |
| CZ-T26 | Every page change persists through `AppSettings` and reaches a second composer's `ComposerLightView.Settings` | CZ-13.1 |
| CZ-T27 | Write coalescing: a 2 s drag at 60 Hz writes at most 9 times plus once at the end; in-memory value current each frame | CZ-13.5 |
| CZ-T28 | Preview foot caption strings for every style and shape | CZ-4.4 |
| CZ-T29 | `MakerLiquids` sha256 pin, 30 makers, order, display names, model ids | CZ-4.5.4 |
| CZ-T30 | `czTone` against the design's function for all 30 makers x 2 themes (fixture from a node run of cz.js) | CZ-12.11 |
| CZ-T31 | Preview palette rule equals `ComposerLightSlot`'s for the 15 brands | CZ-4.5.5 |
| CZ-T32 | Steppers wrap; chip click steps; chip width eases; accessible name | CZ-4.5.1 |
| CZ-T33 | Menu opens above the chip, list height clamp, current item centred and focused, keys, Esc closes the menu only (the window stays) | CZ-4.5.2, CZ-15 |
| CZ-T34 | Preview maker is session state, initial = default provider's maker, Purple -> Claude | CZ-4.5.3 |
| CZ-T35 | Preview phase follows the chrome control; Typing types the playground's string at 0.055 s per char | CZ-4.6, CZ-4.3 |
| CZ-T36 | Preview opens no microphone, network, model or draft (seams throw if touched) | CZ-4.8 |
| CZ-T37 | Stage geometry: 496 x 212, radius 10; composer 472 wide (height 46, the real pill's: ED-335 item 2) at left 12, bottom 12 (fixture) | CZ-4.1, CZ-4.3 |
| CZ-T38 | Pinned block rest height 372 (fixture from the design, adjusted by the port's 60 top) | CZ-3.4 |
| CZ-T39 | Section order and headings exact | CZ-3.3 |
| CZ-T40 | Transition table: each row's duration and curve sampled on the frame clock (monotonic, no single-frame jump over 15 % of the travel) | CZ-14.1 |
| CZ-T41 | Reduce motion: every transition lands at once; glyphs still | CZ-14.1, CZ-5.3 |
| CZ-T42 | Dimmed rows are not focusable | CZ-12.2.7 |
| CZ-T43 | Accessibility names and patterns for each control type | CZ-15 |
| CZ-T44 | No per-frame allocation in the preview's light and glyph frames (allocation probe) | CZ-16 |
| CZ-T45 | `ComposerPreview` at width 472 renders within tolerance of a real `ComposerArea` at 472 (placeholder, chip, ring, send, plate) | CZ-4.3 |
| CZ-T46 | The preview placeholder equals the real composer's placeholder string for a Claude thread | CZ-4.3 |

### C2
| # | Test | Clause |
|---|---|---|
| CZ-T50 | `workingDots` JSON exact bytes for defaults; lenient read cases (missing, unknown mode, unknown id, garbage) | CZ-13.3, CZ-10.3 |
| CZ-T51 | Rows and details exact; Custom row folds open only for Custom; Classic 10 and Signature 12 swatches in palette order | CZ-10.1 |
| CZ-T52 | Picks switch the preview to Running and cancel a voice demo | CZ-10.2, CZ-4.6 |
| CZ-T53 | `Resolve`: accent = `Chrome.Accent`; model = toned mid for each brand x theme (fixture); custom = swatch hex; Purple rule | CZ-10.4 |
| CZ-T54 | A running composer's dots blend to a new colour through `Retarget(ease: true)` over 0.32 s; still dots change at once under reduce motion | CZ-10.4 |
| CZ-T55 | Every composer follows a change (two composers) | CZ-10.4 |

### C3
| # | Test | Clause |
|---|---|---|
| CZ-T60 | Pin: for offsets 0..H_c the block's visual top equals `clamp(y, 0, H_c - h_pin)` | CZ-3.5.1 |
| CZ-T61 | Compaction follows small steps exactly; a 300 px jump eases with tau 0.06 s; reduce motion exact | CZ-3.5.2 |
| CZ-T62 | Stage height, text opacity and offset at k = 0, 0.25, 0.5, 1 | CZ-3.5.2 |
| CZ-T63 | Sections never move faster than the scroll (their content-space positions are offset-independent) | CZ-3.5.2 |
| CZ-T64 | Release exactly when Working dots' bottom meets the block's bottom; Speech scrolls freely | CZ-3.5.3 |
| CZ-T65 | Content under the block is clipped and not hit-testable; content below it is | CZ-3.5.4 |
| CZ-T66 | Hairline fades in past 4 px and out at 0 | CZ-3.5.5 |
| CZ-T67 | Tab to a row hidden under the block scrolls it to 8 px below the block (animated); pointer focus does not scroll | CZ-3.5.6 |
| CZ-T68 | No section measure pass during a scroll (layout counter) | CZ-3.5.7, CZ-16 |
| CZ-T69 | Native frame test on a quiet PC: scrolling the page at 60 Hz keeps the app's frame-gap gate | CZ-16 |

### C4
| # | Test | Clause |
|---|---|---|
| CZ-T70 | Voice orb rows: titles, details, option labels exact; order Orb, Listening style, Orb size, Attachment button, After voice input, note | CZ-9 |
| CZ-T71 | `voiceOrb` writes the documented JSON; defaults pill / siri / 32 / in / leave | CZ-13.4 |
| CZ-T72 | Orb tiles: 96 x 56, 52 px orb with ball 30, selected glow 0.25, arrows wrap | CZ-9.1 |
| CZ-T73 | Listening style pick starts the preview demo in Rest | CZ-9.2, CZ-4.7.2 |
| CZ-T74 | Orb size pick drives the preview orb's size spring; attachment pick eases the composer (fixture positions at t = 0, 0.21, 0.42 s) | CZ-9.3, CZ-9.4, CZ-4.3 |
| CZ-T75 | Demo: script timeline, labels, revisions and final pass equal the voice design's (fixture from voice-composer.js run headless) | CZ-4.7.1, CZ-4.7.3 |
| CZ-T76 | After voice input send: preview sends 0.35 s after landing, runs 3.0 s, returns to Rest; leave keeps the text | CZ-4.7.4 |
| CZ-T77 | Cancel paths (cross, Esc, state pick, dots touch) withdraw the words | CZ-4.7.5 |
| CZ-T78 | Floating pill fades a crossed line above the composer and restores it | CZ-9.7 |
| CZ-T90 | OD-0097 rows: order Orb, Listening style, Pill words, Orb size, Attachment button, Composer light while you speak, Spoken language, After voice input, note; every title, option text and detail byte-equal to `CzText` (incl. the dimmed details and the `{Language}` forms); the six word strings equal SPEC 2 V-12.9's fixture | CZ-9, CZ-9.8 to CZ-9.10 |
| CZ-T91 | Picks write `voiceOrb.labels` / `lightVoice` / `langMode` (defaults L1 / away / auto in the documented JSON); Spoken language also writes `voiceTyping.lang` = null on every pick, the current one included, in one coalesced write | CZ-13.4, CZ-9.10 |
| CZ-T92 | Menu button: value crossfade and width ease; chevron; menu width `max(220, widest + 48)`; below the button, right- or left-aligned by row kind, flips above with no room; check on the current item; Up/Down wrap, Home/End, Enter/Space pick, Esc closes only the menu and refocuses the button; outside click closes; `menuitemradio` checked states | CZ-12.12 |
| CZ-T93 | Pill words dims and is inert (not focusable) under Cross beside the orb with its dimmed detail; the light row under Style Off; both keep their values and undim when the condition ends | CZ-9.8, CZ-9.9, CZ-12.2.7 |
| CZ-T94 | Picks replay the demo from Rest: Pill words after 0.2 s with the pill's relabel (out 0.14 s, swap 0.16 s), the other two at once; a running demo restarts | CZ-4.7.2, CZ-4.7.6 |
| CZ-T95 | Preview light per option during listening, finishing and landing: level 0 / 0.35 / 1 by the follower (fixture from voice-options.js's `beamVis` run headless), back to 1 after; the voice glow drawn | CZ-4.7.7 |
| CZ-T96 | Preview language: A and C draw nothing in the pill; B draws `Transcribing… English` (no U+00B7) from the first live pass; D opens the question (not clipped by the stage; below the orb when the compacted stage leaves no room above), answers `English` by itself after 1.6 s for a pick-started demo without taking focus, waits and takes focus for an orb-click demo; afterwards the orb listens directly until an option is picked again; `voiceTyping` is never written by the preview's answer (store fake) | CZ-4.7.8, CZ-4.7.9, CZ-4.8 |
| CZ-T97 | Spoken language detail: each option's text; the Windows forms with a listed and an unlisted display language (scripted); the override text while `voiceTyping.lang` is set | CZ-9.10 |

### C5
| # | Test | Clause |
|---|---|---|
| CZ-T80 | Voice typing sidebar entry follows Customization; its page has no chrome accessory | CZ-2.1, CZ-2.2 |
| CZ-T81 | Speech rows' details for local/cloud and Auto/forced language; `Change` opens Voice typing | CZ-11.1 |
| CZ-T82 | The pin releases at Speech with the Speech section present | CZ-3.5.3 |
| CZ-T98 | Speech > Language detail with `voiceTyping.lang` null under A, B, C (listed and unlisted Windows language) and D, and with an explicit `auto` and code | CZ-11.1 |

---

## 20. Open questions for the owner (each with the recommended answer)

| # | Question | Recommended answer |
|---|---|---|
| Q-CZ-1 | The Voice typing page was shown inside the Customization design (v5) but never locked on its own. Lock it as shown? | Yes: lock it as drawn (SPEC 2 V-16.3), as its own sidebar page right below Customization. |
| Q-CZ-2 | The preview's orb: a scripted demo (no microphone) or your real microphone? | Scripted demo: Settings never opens the microphone; it shows exactly what the composer does. |
| Q-CZ-3 | The Uiverse light's Custom colour wells need a colour picker. Which? | Avalonia's own ColorPicker (12.1.2, MIT) in a glass popover; the old Windows colour dialog does not match the app. Needs one package pin by the orchestrator. |
| Q-CZ-4 | Search words: the design proposed `light, glow, beam, composer, effects, customization`. Add the voice orb and dots? Words for Voice typing? | Customization: add `orb`, `voice`, `dots`, `uiverse`, `aura`. Voice typing: `voice`, `dictation`, `microphone`, `mic`, `whisper`, `speech`, `transcribe`, `language`, `model`, `hardware`. |
| Q-CZ-5 | The preview's model menu opens above its chip like the composer's popover. Give it the Mac arrow tip of OD-0095 too? | Yes, through the same popover card, so it matches the composer's menu. |
| Q-CZ-6 | ~~Should the preview show the voice glow and the Border beam giving way while the demo listens (the app does; the design page did not show it)?~~ | **RESOLVED by OD-0097 (2026-10-03):** the light's behaviour while you speak is now a setting whose every change previews in the stage, so the stage shows it with the voice glow (CZ-4.7.7); the design page now draws both. |
| Q-CZ-7 | Working dots colour changes: one ease for all (the accent picker's 0.32 s) or the design preview's 0.4 s? | One curve, 0.32 s, for every dot colour change. |
