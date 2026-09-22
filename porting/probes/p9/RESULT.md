# P9 RESULT: FAIL

Task packet T-M0-008. Probe P9, text metrics that feed layout, on Avalonia 12.1.2 / .NET 10, headless.
Run 2026-09-22, final run 18:45:00.83Z to 18:45:05.93Z UTC (first run 18:42:00Z, see Deviations).

The PASS condition has two clauses and needs both. Measured:

| Clause | Result |
| --- | --- |
| (A) measured wrap points and line counts match the tour-*@2x.png oracle at the recorded sizes | **FAIL.** 3 of 10 fixtures match with the PA-0013 candidate face (Segoe UI Variable). All 3 matches are single-line strings; **0 of 7 multi-line fixtures match.** No face installed on this PC matches all 10 (best: Inter, 6/10). |
| (B) the 50 monospaced sites hold column alignment under Cascadia Mono with tabular figures | **PASS**, with caveats named below: 50/50 sites aligned (spread 0.0000 DIP), and every control fails as required. |

What this proves and what it does not:

- **Proven:** Avalonia's text measurement is sound and self-consistent. On every line of every fixture the line fits the
  width, adding the next word overflows it, the line width equals the sum of the ShapedTextRun glyph advances, that sum
  equals an independent `TextShaper.ShapeText` pass, and `TextFormatter.FormatLine` over the probe's own `ITextSource`
  gives the same lines as `TextLayout` (10/10). The source's type scale is implemented and printed.
- **Disproven:** that this measurement reproduces the macOS wrap points with the Windows candidate face. The cause is
  the face, not the machinery: Segoe UI Variable runs **3 to 5 percent narrower** than the SF Pro in the oracle for the
  same words (ratio 0.945 to 0.984 on plain lines; 0.945 to 0.968 on every line longer than 100 pt). So it fits more
  words per line and breaks later. Inter runs 1 to 4 percent wider and mostly breaks earlier.
- Glyph rasterisation stays **UNVERIFIED** under OD-0001. Every number taken from the PNGs is **APPROXIMATE** under
  OD-0004: they are hand-regenerated assets, not captures tied to `dd2b86ef`.

---

## 1. The type scale, implemented (SOURCE FACT)

Source: `DroppyCode/UI/Common/ChatZoom.swift` at `dd2b86ef` (`percentages` :10, `defaultIndex` :13, `clamped` :16-18,
`chatBaseSize` :45-57, `chat(...)` :62-70). Also `MarkdownView.swift:1129` (markdown point size rounded to 0.5),
`LinkParagraphView.swift:117` (inline code `max(9, pointSize - 1)`), `MarkdownView.swift:839` (markdown default 13).
The probe prints this table from its own `TypeScale` class. Swift `.rounded()` is ties-away-from-zero:

```
style            80%    90%    95%   100%   110%   125%   135%
LargeTitle        21     23     25     26     29     33     35   weight@default=Normal
Title             18     20     21     22     24     28     30   weight@default=Normal
Title2            14     15     16     17     19     21     23   weight@default=Normal
Title3            12     14     14     15     17     19     20   weight@default=Normal
Headline          10     12     12     13     14     16     18   weight@default=DemiBold
Body              10     12     12     13     14     16     18   weight@default=Normal
Callout           10     11     11     12     13     15     16   weight@default=Normal
Subheadline        9     10     10     11     12     14     15   weight@default=Normal
Footnote           8      9     10     10     11     13     14   weight@default=Normal
Caption            8      9     10     10     11     13     14   weight@default=Normal
Caption2           8      9     10     10     11     13     14   weight@default=Normal
default index 3 -> 100%; clamped(-4)=0 clamped(99)=6
markdown 13pt at zoom 1.25 -> 16.5; inline code at 13 -> 12; at 9.5 -> 9
```

Upstream drift, for information only (the pinned source wins): in live upstream `1fbf81e6` (v1.6.2),
`ChatZoom.swift` is deleted. `ChatFont.swift` makes `Font.chat(style)` always `Font.system(style)`, and
`AppZoom.swift` adds a whole-window zoom (75 to 200 percent). The 100 percent column above is unchanged by that.

## 2. Fonts: what was installed, and what Avalonia actually used

- **Cascadia Mono is not installed.** It is not in `C:\Windows\Fonts` and not in `%LOCALAPPDATA%\Microsoft\Windows\Fonts`.
  The HKLM and HKCU `Fonts` registry keys have no Cascadia entry. The file exists only inside the Windows Terminal
  package, `C:\Program Files\WindowsApps\Microsoft.WindowsTerminal_1.24.11911.0_x64__8wekyb3d8bbwe\CascadiaMono.ttf`:
  714,804 bytes, sha256 `08026f617eea315caca59898784e999f6cdb2d0c34d2040deeb042bcdc359fa2`.
- The Windows Terminal `AppxManifest.xml` declares `<uap4:Font File="CascadiaMono.ttf" />` (shared fonts). So while
  Windows Terminal is installed, DirectWrite, and therefore Avalonia, resolves "Cascadia Mono" **by name**. The app cannot
  rely on that on another PC.
- **Font actually used for (B):** Cascadia Mono, loaded read-only from that file through a `FontCollectionBase` subclass
  (`fonts:P9Cascadia#Cascadia Mono`). It is not installed and nothing was copied. Resolved as `family='Cascadia Mono'`,
  `fixedPitch=True`, 4,319 glyphs. The file provides weight 400 only. Weights 600 and 700 come back as weight 700 with
  `sim=Bold`, and 500 comes back as 400. A by-name request gave the same result:
  `used Cascadia Mono[w400,None]@12; row spread 0`.
- **Font actually used for (A) and the digit sites:** "Segoe UI Variable" (the PA-0013 / UNK-0007 candidate), reached
  through the name **"Segoe UI Variable Text"**. The table below comes from the probe's own resolution check. It reads
  `GlyphTypeface` and the `ShapedTextRun` a `TextLayout` really produced:

```
request 'SF Pro',                   TryGetGlyphTypeface=True  -> family='Segoe UI' ... | TextLayout used: Segoe UI[w400,None]@13
request '.AppleSystemUIFont',       TryGetGlyphTypeface=True  -> family='Segoe UI' ... | TextLayout used: Segoe UI[w400,None]@13
request 'SF Mono',                  TryGetGlyphTypeface=True  -> family='Segoe UI' ... | TextLayout used: Segoe UI[w400,None]@13
request 'Segoe UI Variable',        TryGetGlyphTypeface=True  -> family='Segoe UI' ... glyphs=5394 | TextLayout used: Segoe UI[w400,None]@13
request 'Segoe UI Variable Text',   TryGetGlyphTypeface=True  -> family='Segoe UI Variable' ... glyphs=2530 | TextLayout used: Segoe UI Variable[w400,None]@13
request 'Segoe UI Variable Small',  TryGetGlyphTypeface=True  -> family='Segoe UI Variable' ... glyphs=2530
request 'Segoe UI Variable Display',TryGetGlyphTypeface=True  -> family='Segoe UI Variable' ... glyphs=2530
request 'Inter',                    TryGetGlyphTypeface=True  -> family='Inter' ... glyphs=2926   (user-installed, not a Windows font)
weight request 'Segoe UI Variable Text' Medium   (500): True -> family='Segoe UI Variable' weight=400 sim=None
weight request 'Segoe UI Variable Text' DemiBold (600): True -> family='Segoe UI Variable' weight=400 sim=None
weight request 'Segoe UI Variable Text' Bold     (700): True -> family='Segoe UI Variable' weight=400 sim=None
weight request 'Segoe UI' Medium   (500): True -> family='Segoe UI' weight=400 sim=None
weight request 'Segoe UI' DemiBold (600): True -> family='Segoe UI Semibold' weight=600 sim=None
weight request 'Segoe UI' Bold     (700): True -> family='Segoe UI' weight=700 sim=None
```

These are findings the port must act on:

1. Avalonia substitutes Segoe UI for any family it cannot find, and `TryGetGlyphTypeface` still returns `True`. Asking for
   "Segoe UI Variable" by its family name silently gives **classic Segoe UI**. The resolved `FamilyName` has to be checked,
   because a boolean success means nothing here.
2. The weight axis of the variable font is never applied, and no bold simulation is added either. Every Segoe UI Variable
   weight comes out at 400. The Text, Small and Display instance names give identical metrics (identical results in
   section 3), so the optical-size axis is not applied either.
3. The same weight loss hit the bold run in fixture F1 (`**ComposerView.swift**`). It was laid out at w400.

## 3. Clause (A): wrap points against the oracle

### Oracle and fixtures

- Oracle images, read-only, at the pinned SHA:
  `DroppyCode/Resources/Assets.xcassets/tour-{welcome,hydra,panels,recipes,themes}.imageset/*@2x.png`, each 1320x824.
  There is no PROVENANCE file for them in the pinned tree. `porting/reference_captures/README.md` and
  `porting/owner_decisions.md` OD-0004 describe them, and this probe carries their caveat.
- **Pixel-to-point scale, from source rather than guessed.** `scripts/tour_captures.py` (`interior`/`frame` then `fit`)
  cuts the window's inside at an 8 pt inset and centre-crops the width to 1320:824, so the height is preserved and
  `px_per_pt = 824 / (window_h - 16)`. Window sizes come from `TourCaptures.swift` and `WINDOW_SIZES`:
  welcome and panels 1280x800 give 1.05102; hydra 960x600 gives 1.41096; the themes tiles give 409/584 = 0.70034.
  **Calibration check:** the rule under "Worked for" measures 820.16 pt on tour-welcome and 821.03 pt on the tour-themes
  tile. Source says 820 (`ThreadTimeline.swift:395`, `.frame(maxWidth: 820)`, "the same column as the composer").
- Fixture strings are verbatim from `App/Captures/WebsiteCaptures.swift` and `TourCaptures+Hydra.swift`, as rendered:
  markdown markers removed, `code` as inline-code runs, `**bold**` as a bold run. Font sizes come from source: body 13
  (`MarkdownPointSizeKey` default, `LinkParagraphView` `NSFont.systemFont(ofSize: 13)`), inline code 12, cookbook caption
  11 (`HydraCookbookPanel.swift:23`). `lineFragmentPadding = 0` and `textContainerInset = .zero`
  (`LinkParagraphView.swift:305,319`), so the text wraps at the full column. User bubble width = column - 96 - 14 - (14 + 4)
  (`TimelineRows.swift:124,126,164`, `UserBubble.tail` :1175).
- Widths: F1 to F5 use 820 or 692 (SOURCE). F11 uses 428 = 460 - 2x16 (SOURCE, `HydraCookbookPanel.swift:27,60`). F7 to
  F10 are **APPROXIMATE**: oracle rule length divided by the source-derived scale, +-1.5 px (hydra 508.87 +-1.06; panels
  416.74 +-1.43; panels bubble 288.74 +-1.43).
- The oracle's line breaks were read from the images (crops zoomed 2x) and cross-checked against the ink bands that
  `oracle_measure.py` found (same line count for every fixture).

### Results, primary face Segoe UI Variable (via "Segoe UI Variable Text")

"Avalonia reproduces the oracle breaks for W in" is the range of max widths (scanned in 0.125 DIP steps, +-150 around W)
over which the layout gives exactly the oracle's lines. "Ratio" is this face's advance width for the oracle's own words on
that line, divided by the oracle's ink width. It shows how much narrower the face is.

| Fixture | Page | Size | W (pt/DIP) | Oracle lines | Measured | Result | Avalonia reproduces the oracle breaks for W in | Width ratio per oracle line |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| F1 "In DraftAttachments, inside..." | welcome | 13 (+12 code, bold) | 820 | 2 | 2 | **MISMATCH**: L1 ends "leaves the top on the", oracle "leaves the" | [724.375, 747.5] | 0.9085 (bold lost), 0.9514 |
| F2 "Want me to give the top the same 14pt?" | welcome | 13 | 820 | 1 | 1 | MATCH | >= [670, 970] | 0.9592 |
| F3 "Done. The attachment strip..." | welcome | 13 | 820 | 2 | 2 | **MISMATCH**: L1 ends "The build is", oracle "The" | [765.875, 798.25] | 0.9447, 0.9683 |
| F4 user bubble "Where does the composer..." | welcome | 13 | 692 | 1 | 1 | MATCH | >= [542, 842] | 0.9513 |
| F5 user bubble "The draft photo in the composer..." | welcome | 13 | 692 | 1 | 1 | MATCH | >= [542, 842] | 0.9543 |
| F7 "Three heads are out..." | hydra | 13 | 508.87 APPROX | 2 | 2 | **MISMATCH**: "wires the", oracle "wires" | [479.0, 483.75] | 0.9523, 0.9572 |
| F8 "Three heads are out..." | panels | 13 | 416.74 APPROX | 3 | 3 | **MISMATCH** at both breaks | [394.36, 403.49] | 0.9562, 0.9528, 0.9637 |
| F9 user bubble "Use the same pictures..." | panels | 13 | 288.74 APPROX | 2 | 2 | **MISMATCH**: "zoomed in", oracle "zoomed" | [267.11, 281.11] | 0.9515, 0.9840 |
| F10 "The site now shows only..." | panels | 13 | 416.74 APPROX | 2 | 2 | **MISMATCH**: "what it is", oracle "what it" | [383.11, 395.24] | 0.9472, 0.9636 |
| F11 Cookbook caption | recipes | 11 | 428 | 2 | 2 | **MISMATCH**: "and which", oracle "and" | [378.625, 409.625] | n/a (page scale is a popover focus zoom, not source-derivable) |

Result: **3/10.** For every mismatch the recorded width is outside the reproducing range, including for the APPROXIMATE
widths with their whole +-uncertainty band. So the failure does not depend on how the width was measured. The line
counts agree 10/10; the break positions do not.

Sensitivity, the same corpus in every other face on this PC:

| Face requested | Resolved as | Fixtures matching |
| --- | --- | --- |
| Segoe UI Variable Small | Segoe UI Variable (identical numbers to Text) | 3/10 |
| Segoe UI Variable Display | Segoe UI Variable (identical numbers to Text) | 3/10 |
| Segoe UI | Segoe UI | 3/10 |
| Inter (user-installed, not shipped with Windows) | Inter | 6/10: F2, F4, F5, F9, F10, F11 match. F3, F7, F8 break earlier; F1 breaks later (its bold run is laid out at w400, see section 2, and its code runs are in Cascadia Mono). Plain-line ratios 1.013 to 1.042. |

### Measurement self-consistency (primary face, every line of every fixture)

From `p9_output.txt`:

- `fits=True` on every line.
- The next word overflows on every wrapped line, for example F3 L1 `with next word 'running' = 858.863 > W: True` and
  F8 L2 `with next word 'design' = 420.374 > W: True`.
- `runs' advance sum` equals `WidthIncludingTrailingWhitespace` on every line, for example F7 L1
  `487.322 vs wTrail 487.322; TextShaper.ShapeText sum 487.322`.
- `TextFormatter.FormatLine` with `previousLineBreak` chained gives lines identical to `TextLayout` for 10/10 fixtures.
  In `TextLayout`, wrapped lines carry a `TextLineBreak` with `IsSplit=True` and no `TextEndOfLine`. In the `FormatLine`
  loop, the last line's `TextLineBreak` is `TextEndOfParagraph`. `GetTextBounds` returns one bound per line starting at x=0.

### Controls for (A), each required to FAIL

| Control | What it removes | Fixtures matching | Required outcome |
| --- | --- | --- | --- |
| CA1 | the source type scale: one step up (13 to 15, 11 to 12; inline code follows as max(9, size - 1) = 14) | 4/10 | fails, as required |
| CA2 | shaping: fixed 0.5 em per character, greedy word wrap | 4/10 | fails, as required |

Honest note on the controls: they were built to guard against a false PASS. Because the primary also fails (3/10), their
4/10 does not separate them from the primary. The evidence that the FAIL is real, and not a broken comparator, is:

- the self-consistency checks above;
- the calibration check (820.16 and 821.03 against 820);
- the fact that the same comparator reports MATCH on three multi-line fixtures (F9, F10, F11) when a face with closer
  widths (Inter) is used;
- the consistent direction of the error: every Segoe UI Variable reproducing range sits below the recorded width.

## 4. Clause (B): the 50 monospaced sites

**Site census (grep over `DroppyCode/**/*.swift` at `dd2b86ef`):** 22 `.monospaced` design sites plus 28
`.monospacedDigit()` sites = 50. This matches the 50 in PA-0013 and `critiques/stack_eval.json`. Two AppKit
`NSFont.monospacedSystemFont` sites are outside that count (`TerminalStore.swift:22`, `LinkParagraphView.swift:117`); the
second one is the inline code in F1. The probe's `Sites()` table lists all 50 with the size and weight read from each
line, or from the enclosing `.font` it applies to (cited line numbers checked with `sed -n`).

**Method:** columns hold when rows of equal length put character i at the same x. For each site, 3 rows (mono) or 4 rows
(digits) were laid out with `TextLayout` at the site's size and weight, and x was read with `HitTestTextPosition(i)` for
every i, plus the trailing edge. Tolerance 0.01 DIP. Advances per character come from `TextShaper.ShapeText` with the
resolved `GlyphTypeface`. A missing glyph (`CharacterToGlyphMap` = 0) or a second face in the layout (font fallback)
counts as NOT ALIGNED.

| Group | Face and features | Result | Control, required to fail | Control result |
| --- | --- | --- | --- | --- |
| 22 `.monospaced` sites (10, 10.5, 11, 11.5, 12 pt, all w400) | Cascadia Mono; 99-char set = ASCII 0x20-0x7E plus U+2212, U+00B7, U+2026, U+2192 | **22/22 aligned.** Missing 0; one advance per size (11 pt 6.4453, 12 pt 7.0313, 10 pt 5.8594, 10.5 pt 6.1523, 11.5 pt 6.7383, i.e. 0.5859 em); row spread 0.0000 | CB1: the same rows in the proportional face | spread 121.7 to 146.1 DIP at all 22: **fails** |
| 27 `.monospacedDigit()` sites in the system face | Segoe UI Variable +tnum | **27/27 aligned** (digit advances equal, row spread 0) | CB2: +pnum | spread 28.8 to 54.5 DIP at all 27: **fails**. Without any feature the digits are proportional too (for example 11 pt 4.168 to 5.9297, spread 35.23), so tnum is what aligns them. |
| 1 `.monospacedDigit()` site inside a `.monospaced` run (`HydraReportsPopover.swift:612`, enclosing `.font` :615) | Cascadia Mono +tnum | aligned, spread 0 | CB1: proportional face +pnum | spread 35.234: **fails** |
| Alternative literal reading: all 50 sites in Cascadia Mono +tnum | Cascadia Mono | **50/50 aligned** | | |

Caveats that travel with the (B) PASS:

- **Weights.** In Segoe UI Variable the requested weight is not honoured at 10 of the 28 digit sites: six at w500, three
  at w600, one at w700 all come out at w400 with no simulation (section 2). Alignment holds, but at the wrong weight at
  those sites.
- **With classic Segoe UI instead: 47/50.** Its real Semibold face keeps the weights, but its `tnum` leaves "7" narrower,
  so 3 sites misalign:
  - `HydraMergePopover.swift:273` (17 pt w600): `7:9.114`, other digits `9.438`, spread 0.6475
  - `HydraPanel.swift:235` (12.5 pt w600): `7:6.702` against `6.94`, spread 0.4761
  - `UsageViews.swift:114` (13 pt w600): `7:6.97` against `7.217`, spread 0.4951

  Segoe UI Regular digits are already tabular without the feature.
- In Cascadia Mono, w600 and w700 are Bold simulation and w500 is w400. Alignment is unaffected: spread 0 at all 50.
- The rows are synthetic, not each site's live strings. Alignment is a property of face, size, weight and features, so
  the rows cover every character class those sites print: digits, the minus sign, middle dot, ellipsis, arrow, and
  punctuation.

## 5. Environment

- `cmd /c ver`: `Microsoft Windows [Version 10.0.26200.9457]` (Windows 11 Pro).
- .NET SDK 10.0.401 (`dotnet --version`); runtime `.NET 10.0.12` (reported by the probe).
- Avalonia.Base 12.1.2.0; Avalonia, Avalonia.Headless and Avalonia.Skia 12.1.2 from the repository's
  `Directory.Packages.props`. Transitively SkiaSharp 3.119.4, HarfBuzzSharp 8.3.1.3 and Avalonia.HarfBuzz 12.1.2 (nuspec).
- Probe runtime: `TextFormatter` = `Avalonia.Media.TextFormatting.TextFormatterImpl`, `TextShaper` =
  `Avalonia.Media.TextFormatting.TextShaper`, default family 'Segoe UI'. Headless with `UseHeadlessDrawing = false` plus
  `UseSkia()`, so the Skia font manager does font lookup and HarfBuzz does shaping.
- CPU AMD Ryzen 7 9800X3D. The GPU is irrelevant: nothing was rasterised.
- `MARROW_PORT_DATA_DIR` = `C:\Users\omera\AppData\Local\Temp\marrow-p9-1342101762` (fresh, empty, deleted afterwards).
  The probe refuses to run without it.

## 6. Commands run, with their real output (trimmed to the decisive lines)

```
$ cat porting/tasks/T-M0-008.md, porting/adr/0002-stack-avalonia.md, porting/probes/p1-glass/{p1-glass.csproj,Program.cs},
  Directory.Build.props, Directory.Packages.props, WORKER_BRIEF.md, CLAUDE.md          (read)
$ git log --format='%h %ad %s' -- Directory.Build.props Directory.Packages.props porting/probes/p1-glass
90bd97d 2026-09-22 ... solution skeleton; 662 packets      <- the props files arrive here
da77ff7 2026-09-20 ... ADR-0002 ... Probe P1 passes.       <- p1-glass predates them
```
p1-glass puts `Version=` on each `PackageReference`, and the props now turn on central package management and
TreatWarningsAsErrors. p1-glass was not rebuilt to test this. p9's csproj leaves the versions out and takes the central
12.1.2 pins.

```
$ find porting/reference_captures reference/droppy-code -iname 'tour-*'
reference/droppy-code/DroppyCode/Resources/Assets.xcassets/tour-{hydra,pairs,panels,recipes,slider,themes,threads,welcome}.imageset/*@2x.png
$ py -c "...struct.unpack('>II', png[16:24])..."      -> all eight (1320, 824)

$ grep -rn "\.monospaced\b\|design: \.monospaced\|\.monospaced()" --include=*.swift DroppyCode | wc -l   -> 22
$ grep -rn "monospacedDigit" --include=*.swift DroppyCode | wc -l                                       -> 28
$ grep -rn "monospacedSystemFont\|monospacedDigitSystemFont\|userFixedPitchFont" ... | wc -l             -> 2

$ Get-ChildItem C:\Windows\Fonts | ? Name -match 'casc|segui|SegUI|var'   -> SegUIVar.ttf, segui*.ttf ... (no Cascadia)
$ Get-ChildItem $env:LOCALAPPDATA\Microsoft\Windows\Fonts                 -> no Cascadia (Inter-VariableFont_opsz,wght.ttf present)
$ (Get-ItemProperty 'HKLM:\...\Fonts') matching Cascadia|Segoe UI Variable -> "Segoe UI Variable (TrueType) = SegUIVar.ttf" only
$ Get-AppxPackage Microsoft.WindowsTerminal   -> 1.24.11911.0, InstallLocation ...\Microsoft.WindowsTerminal_1.24.11911.0_x64__8wekyb3d8bbwe
$ grep -o '<uap4:Font [^>]*>' .../AppxManifest.xml
<uap4:Font File="CascadiaCode.ttf" /> <uap4:Font File="CascadiaCodeItalic.ttf" /> <uap4:Font File="CascadiaMono.ttf" /> <uap4:Font File="CascadiaMonoItalic.ttf" />

$ py oracle_measure.py <pinned Assets.xcassets> oracle_measurements.json
 "column_pt": { "tour-welcome (calibration check, source says 820)": 820.155,
                "tour-themes tile (calibration check, source says 820)": 821.027,
                "tour-hydra": 508.874, "tour-panels": 416.738 }
F1+F2 welcome [([319,332],[230,1067]), ([337,349],[230,747]), ([366,379],[230,480])]
F7 hydra      [([162,180],[12,695]), ([185,202],[12,717])]
F8 panels     [([115,127],[442,843]), ([132,144],[442,876]), ([148,161],[442,634])]
...

PS> $env:MARROW_PORT_DATA_DIR = "$env:TEMP\marrow-p9-$(Get-Random)"; New-Item -ItemType Directory ...
DATA_DIR=C:\Users\omera\AppData\Local\Temp\marrow-p9-1342101762
PS> dotnet build -c Release          (porting/probes/p9)
Build succeeded.    0 Warning(s)    0 Error(s)
PS> $env:P9_CASCADIA_FILE = "C:\Program Files\WindowsApps\Microsoft.WindowsTerminal_1.24.11911.0_x64__8wekyb3d8bbwe\CascadiaMono.ttf"
PS> dotnet bin\Release\net10.0\p9-textmetrics.dll "E:\Droppy\port\porting\probes\p9"
exit=0 elapsed=5.27s
==== SUMMARY ====
(A) wrap points, primary face Segoe UI Variable Text: 3/10 -> MISMATCH
    sensitivity face Segoe UI Variable Small   : 3/10 fixtures match
    sensitivity face Segoe UI Variable Display : 3/10 fixtures match
    sensitivity face Segoe UI                  : 3/10 fixtures match
    sensitivity face Inter                     : 6/10 fixtures match
CA1 control (one type-scale step up) fails        : True
CA2 control (naive fixed advance) fails           : True
(B) 50 sites hold alignment, digits in Segoe UI Variable Text: YES
(B) 50 sites hold alignment, digits in Segoe UI   : NO
    all 50 under Cascadia Mono + tnum (alt read)  : YES
CB1 control (proportional face) fails everywhere  : True
CB2 control (pnum forced) fails everywhere        : True (Segoe UI run: True)
```
The full output of the final run is `p9_output.txt` (71 KB).

```
$ git -C E:\Droppy\reference\droppy-code status --short | Measure-Object   -> 0   (the reference is untouched)
$ git -C E:\Droppy\reference\droppy-code-417ab7d3 show 1fbf81e6:DroppyCode/UI/Common/ChatFont.swift, .../AppZoom.swift   (read only)
$ git status --short (E:\Droppy\port)   -> ?? porting/probes/p9/  (plus p5, p6, p8, p10, p11 from other workers, not touched here)
```

## 7. Deviations from the probe text, with reasons

1. **The system face is a substitute.** SF Pro is not installed and not redistributable (PA-0013, UNK-0007 open). The
   primary face is the PA-0013 candidate, Segoe UI Variable. That is not a decision; the other installed faces are
   reported beside it. No tracking was applied (`letterSpacing` 0). CoreText may apply SF Pro's size-specific tracking;
   whether it did is UNVERIFIED, and adding it would be an invented number.
2. **Corpus scope.** 10 fixtures come from 4 tour pages, and tour-themes is used for scale calibration only (it shows the
   same F1/F3 text wrapping at the same words at 820). Left out:
   - tour-pairs and tour-slider: no wrapped body text.
   - tour-threads: its text is covered by the popover.
   - The recipe rows in tour-recipes: their text width depends on the "Add" button's intrinsic width, which depends on
     the font, so source does not settle it.
3. **F7 to F10 widths are APPROXIMATE.** They are oracle rule lengths over the source-derived scale, because deriving the
   pane width with docked team panels needs panel geometry this probe did not reduce from source. Their +-1.5 px
   uncertainty band is reported, and no verdict depends on it: each band lies wholly outside the reproducing range.
4. **`TextLineMetrics`.** The struct exists in 12.1.2, but no public member of `TextLayout` or `TextLine` in the dumped
   API returns it. The same fields were read from `TextLine` (`Width`, `WidthIncludingTrailingWhitespace`, `Height`,
   `Baseline`, `TrailingWhitespaceLength`).
5. **Cascadia Mono** was loaded privately and read-only from the Windows Terminal package file (not installed, not copied),
   which is how a bundled font would be loaded. The by-name system lookup was measured too and gives the same result.
6. **(B) has two readings, and both were measured.** The source-faithful reading puts `.monospaced` in Cascadia Mono and
   `.monospacedDigit()` in the system face with tnum. The literal reading puts all 50 sites in Cascadia Mono with tnum.
7. **The first run (18:42:00Z) used the wrong primary face.** It requested "Segoe UI Variable" and measured classic Segoe UI,
   which the probe's own resolution table exposed. The primary was corrected to "Segoe UI Variable Text" and rerun. The
   first run's numbers are identical to the final run's "Segoe UI" sensitivity rows (3/10), and its output file was
   overwritten.
8. The alignment rows are synthetic (see section 4) rather than each site's live strings.

## 8. Design consequence

- **Keep Avalonia's TextFormatting for layout-driving metrics.** It is deterministic and self-consistent, and one
  `FormatLine` loop reproduces `TextLayout` exactly.
- **Line breaks will not match macOS with Segoe UI Variable.** At 13 pt it runs 3 to 5 percent narrower than the oracle's
  SF Pro. Every multi-line fixture broke one or more words later than the oracle. UNK-0007 has to be decided knowing this,
  and "text/line-wrap checks" (contract section 10) will record wrap points as a **named deviation** for whatever face is
  chosen. No installed face reproduced them all (Inter 6/10). Any size or tracking correction to close the gap would be an
  invented number and needs its own decision and measurement.
- **Font resolution needs a guard in production code.** Unknown families silently become Segoe UI even though
  `TryGetGlyphTypeface` returns `True`. Request "Segoe UI Variable Text" (not "Segoe UI Variable") and check the resolved
  `FamilyName`.
- **Weights are an open problem** for the 73 `.chat` sites and the 430+ system-font sites that ask for medium, semibold or
  bold. The Segoe UI Variable weight axis is not applied on this stack, and there is no simulation either. Either static
  weight files, or a way to set the `wght` axis through Avalonia, must be found and measured. Whether the latter exists is
  UNVERIFIED here.
- **Monospaced text is solved.** Ship Cascadia Mono inside the app and load it through a `FontCollectionBase`. It is not
  an inbox font on this PC; it resolves by name only because Windows Terminal registers it. Apply `tnum` explicitly to
  `.monospacedDigit()` text: Segoe UI Variable's default figures are proportional, and classic Segoe UI Semibold's `tnum`
  does not cover "7".

## 9. Files in this folder

- `p9-textmetrics.csproj`: the probe project. No versions; they come from `Directory.Packages.props`.
- `Program.cs`: the probe (type scale, font resolution, wrap suite, controls, FormatLine cross-check, 50-site suite).
- `oracle_measure.py`: pixel measurements from the pinned tour PNGs. The scale derivation is documented in its header.
- `oracle_measurements.json`: that script's output.
- `p9_output.txt`: full output of the final run.
- `RESULT.md`: this file.

Cleanup: `bin/` and `obj/`, the `MARROW_PORT_DATA_DIR` folder, the `%TEMP%\marrow-p9-datadir.txt` helper and the
scratch crops were deleted after the run. Every check came back False (`data=False helper=False bin=False obj=False`).
No process was left running. The probe exits on its own. The Roslyn compiler server the builds started was stopped with
`dotnet build-server shutdown --vbcscompiler`, which printed "VB/C# compiler server shut down successfully.".
