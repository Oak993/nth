# P9 extension for OD-0009: Inter against Segoe UI Variable

Run 2026-09-22, final run started 19:09:15.88Z UTC, exit 0, 8.1 s. Headless Avalonia 12.1.2 / .NET 10, the P9 wrap
harness unchanged. Work folder `E:\Droppy\work\font-p9` (a copy of this folder's sources, plus the two props files). Nothing
in `porting/probes/p9` was modified; this file is the only file added to the repository.

## Verdict

| Face | Version measured | Fixtures matching the oracle | Multi-line fixtures | Mean advance/oracle ratio, 15 plain lines |
| --- | --- | --- | --- | --- |
| Segoe UI Variable (control, via "Segoe UI Variable Text") | Windows inbox `SegUIVar.ttf` | **3/10** | 0/7 | 0.9572 (4.3% narrow) |
| **Inter-NuGet** (Avalonia.Fonts.Inter 12.1.2) | **Inter 3.019** (`Version 3.019;git-0a5106e0b`) | **6/10** | 3/7 | 1.0244 (2.4% wide) |
| Inter-release (GitHub rsms/inter v4.1) | 4.1 | **NOT MEASURED** (see "Not done") | | |
| Inter, user-installed (for reference, not a trusted source for shipping) | 4.001 (`Version 4.001;git-66647c0bb`) | 6/10 | 3/7 | 1.0213 (2.1% wide) |

**OD-0009 rule applied (ENGINEERING DECISION on measured data):** Inter matches the Mac layout oracle more closely than
Segoe UI Variable on both measures (6/10 against 3/10 fixtures; 3/7 against 0/7 multi-line fixtures; mean width error 2.4%
against 4.3%). **The rule gives Inter.**

**What to ship, as measured:** family name **`Inter`**, from the NuGet package **`Avalonia.Fonts.Inter` 12.1.2** (the
same pinned Avalonia version), reached as **`fonts:Inter#Inter`** through the package's own `InterFontCollection`
(key `fonts:Inter`). The regular text file is **`Inter-Regular.ttf`** embedded in `Avalonia.Fonts.Inter.dll`
(309,828 bytes, sha256 `41ab0f707a2bfab8133ccdfcdab52282f5f79e5751f43a264805451c7bb95fb8`, Inter 3.019). The package's
Medium, SemiBold and Bold files come with it and resolve to real weights (below).

Open item: whether the official Inter 4.1 release file matches better than 3.019 is **UNRESOLVED** because it was not
downloaded (see "Not done"). If 4.1 also scores above 3/10 the outcome stays Inter and only the choice of Inter file could
change; the user-installed 4.001 build (a 4.x file of the same design line) scored 6/10 with the same fixtures, which suggests but does
not prove that.

Every number from the PNGs keeps the OD-0004 caveat (APPROXIMATE oracle); glyph rasterisation stays UNVERIFIED (OD-0001).

## 1. Inter sources and their versions

### Inter-NuGet: Avalonia.Fonts.Inter 12.1.2 (NuGet cache, restored earlier from nuget.org)

```
$ (package folder) C:\Users\omera\.nuget\packages\avalonia.fonts.inter\12.1.2
.nupkg.metadata  "source": "https://api.nuget.org/v3/index.json"
nuspec           <id>Avalonia.Fonts.Inter</id> <version>12.1.2</version> <authors>Avalonia Team</authors>
                 <repository type="git" url="https://github.com/AvaloniaUI/Avalonia/" commit="d3c867a9e2de379249b03dbeb3495bd7f076a81a" />
SHA512 of avalonia.fonts.inter.12.1.2.nupkg, computed : K59aDq2PO3MqTywex74yuJkn8pe03QOMY+C1AAjSHx2QfxjEvJiwv3czdg7oLYIO3HzuB3LiqfLP30voo0BuiQ==
recorded in avalonia.fonts.inter.12.1.2.nupkg.sha512   : K59aDq2PO3MqTywex74yuJkn8pe03QOMY+C1AAjSHx2QfxjEvJiwv3czdg7oLYIO3HzuB3LiqfLP30voo0BuiQ==
obj\project.assets.json of the work build: "avalonia.fonts.inter/12.1.2", "Avalonia/12.1.2", "avalonia.skia/12.1.2",
                 "Avalonia.Headless/12.1.2", "skiasharp/3.119.4", "HarfBuzzSharp/8.3.1.3"
```

The fonts are Avalonia resources inside `lib/net10.0/Avalonia.Fonts.Inter.dll` (the nupkg holds no loose font file). The
probe listed them with `AssetLoader.GetAssets("avares://Avalonia.Fonts.Inter/Assets")`, copied each stream to a file, and
read each file's `head` and `name` tables with its own sfnt reader:

| File | Bytes | sha256 | head.fontRevision | name 5 | name 1 / 2 (16 / 17) | fvar |
| --- | --- | --- | --- | --- | --- | --- |
| Inter-Regular.ttf | 309,828 | `41ab0f707a2bfab8133ccdfcdab52282f5f79e5751f43a264805451c7bb95fb8` | 3.019 | Version 3.019;git-0a5106e0b | Inter / Regular | none (static) |
| Inter-Medium.ttf | 314,712 | `10d48331f5b3c7362ca357b00c17ac4863ad35199b13b0eceb0962c8c1ad7dc9` | 3.019 | same | Inter Medium / Regular (Inter / Medium) | none |
| Inter-SemiBold.ttf | 315,756 | `e8cbc2b88bc4268237ff5e251776d3c54edcb14e015a9e66e4883bde4b55f13f` | 3.019 | same | Inter SemiBold / Regular (Inter / SemiBold) | none |
| Inter-Bold.ttf | 316,100 | `790c108befe859dac2ddbd20af3fbb6917c601b3d544c8a05761519f3b5508fe` | 3.019 | same | Inter / Bold | none |
| Inter-Light.ttf | 310,420 | `c44ff7a5fde4816f94fc1e5e232b021a812a411339ddd08b0a475ca6e21db810` | 3.019 | same | Inter Light / Regular (Inter / Light) | none |
| Inter-Thin.ttf | 310,516 | `b778a52b84f39ba1d3b08553a246643573b33453b5481b4d09c604bccced930d` | 3.019 | same | Inter Thin / Regular (Inter / Thin) | none |

All six: 2,547 glyphs. **Inter-NuGet is Inter 3.019.**

### Inter-release: GitHub rsms/inter, latest release (metadata only)

```
PS> Invoke-RestMethod https://api.github.com/repos/rsms/inter/releases/latest
tag_name v4.1, name 4.1, published_at 2024-11-16T00:27:07Z, html_url https://github.com/rsms/inter/releases/tag/v4.1
asset Inter-4.1.zip, size 33707794, https://github.com/rsms/inter/releases/download/v4.1/Inter-4.1.zip, digest (empty)
```

The zip was **not downloaded**; its contents and name tables are therefore not recorded. See "Not done".

### Reference only: the Inter already installed for this user

`%LOCALAPPDATA%\Microsoft\Windows\Fonts\Inter-VariableFont_opsz,wght.ttf`, read only: 874,708 bytes, sha256
`0be2399ea925f1f83ff974764761da9860ec50742ed29a5d4c1ffd0c5c7ac3a8`, head.fontRevision 4.00101, name 5
`Version 4.001;git-66647c0bb`, fvar `opsz 14..32 default 14; wght 100..900 default 400`. Its origin is not recorded, so it
is not a trusted shipping source. It is what P9 called "Inter"; it is kept here as a second data point for a 4.x file.

## 2. Method (same conditions as P9)

- The P9 `Program.cs`, csproj and `oracle_measurements.json` were copied unchanged; the `Directory.Build.props` and
  `Directory.Packages.props` were copied from the repository root (the copy adds one line, `Avalonia.Fonts.Inter` 12.1.2,
  to the work copy only). Same fixtures F1 to F11 (10), same sizes (13, inline code 12, caption 11), same widths (820, 692,
  428, and the APPROXIMATE 508.874, 416.738, 288.738), same `Layout`, `RunWrapSuite`, `MatchInterval` (0.125 DIP steps,
  +-150) and `OracleWidthComparison` code. Code spans in Cascadia Mono loaded privately from the same Windows Terminal file
  (sha256 `08026f61...`, as in P9). Headless, `UseSkia()`, `UseHeadlessDrawing = false`, fresh `MARROW_PORT_DATA_DIR`.
- Added only: an sfnt `head`/`name`/`fvar` reader; extraction of the package assets; each Inter source loaded into its own
  private `FontCollectionBase` (`fonts:P9InterNuget`, the same route P9 used for Cascadia); the package's own
  `InterFontCollection` registered **after** every other run, so it cannot affect them, as a cross-check of the shipping
  route; and a collector that prints line ends per fixture and the mean ratio per face.
- Ratio = this face's advance width for exactly the oracle's words on that line (laid out alone, unbounded) divided by the
  oracle ink width in points, over F1 to F10 (F11's page scale is not source-derivable, as in P9). Ink width omits the
  first and last side bearings, so a face with identical advances would read slightly above 1.0000 (around 1 pt over a line).
  "Plain" excludes F1 L1, the only line with bold and code runs.
- Oracle images: the five tour PNGs have identical sha256 prefixes in `reference/droppy-code` (dd2b86ef) and
  `reference/droppy-code-v1.6.2` (1fbf81e6): welcome `1A522C66...`, hydra `F3C4832C...`, panels `1FDB11AE...`, recipes
  `3047CF7A...`, themes `2361EF94...`. `git grep -F` in v1.6.2 finds every fixture string (WebsiteCaptures.swift,
  TourCaptures+Hydra.swift, HydraCookbookPanel.swift) and `maxWidth: 820` in ThreadTimeline.swift. The widths were not
  re-derived against v1.6.2; they are P9's.

## 3. Results per fixture

Break words = the last words of every line but the last. Control = Segoe UI Variable, rerun in this run (identical to P9's
RESULT.md, 3/10). "Inter-NuGet" rows are the extracted files in a private collection; the package collection
(`fonts:Inter#Inter`) gave **identical** lines, ranges and ratios on every fixture.

| Fixture | W | Oracle break words | Segoe UI Variable | Inter-NuGet 3.019 | Inter-release 4.1 | (ref) installed Inter 4.001 |
| --- | --- | --- | --- | --- | --- | --- |
| F1 | 820 | ...but leaves the | ...top on the: MISMATCH | ...the top on: MISMATCH | not measured | ...the top on: MISMATCH |
| F2 | 820 | (single line) | MATCH | MATCH | not measured | MATCH |
| F3 | 820 | ...the left. The | ...The build is: MISMATCH (later) | ...from the left.: MISMATCH (earlier) | not measured | ...from the left.: MISMATCH (earlier) |
| F4 | 692 | (single line) | MATCH | MATCH | not measured | MATCH |
| F5 | 692 | (single line) | MATCH | MATCH | not measured | MATCH |
| F7 | 508.874 APPROX | ...pages and wires | ...and wires the: MISMATCH (later) | ...pages and wires / ...design and the (3 lines): MISMATCH (earlier) | not measured | same as NuGet: MISMATCH |
| F8 | 416.738 APPROX | ...one writes the / ...page. I keep | ...writes the pages / ...I keep the: MISMATCH (later) | ...one writes the / ...every page. I: MISMATCH (L2 earlier) | not measured | same as NuGet: MISMATCH |
| F9 | 288.738 APPROX | ...the website, zoomed | ...website, zoomed in: MISMATCH | ...the website, zoomed: **MATCH** | not measured | MATCH |
| F10 | 416.738 APPROX | ...on what it | ...what it is: MISMATCH | ...on what it: **MATCH** | not measured | MATCH |
| F11 | 428 | ...what effort, and | ...effort, and which: MISMATCH | ...what effort, and: **MATCH** | not measured | MATCH |
| **Total** | | | **3/10** (0/7 multi-line) | **6/10** (3/7 multi-line) | not measured | **6/10** (3/7) |

Width ranges over which each face reproduces the oracle's breaks (from `MatchInterval`):

| Fixture | W | Segoe UI Variable | Inter-NuGet 3.019 | (ref) installed Inter 4.001 |
| --- | --- | --- | --- | --- |
| F1 | 820 | [724.375, 747.5] | [774, 797.875] | [765.125, 788.5] |
| F3 | 820 | [765.875, 798.25] | [826.875, 860.25] | [820.875, 854.375] |
| F7 | 508.874 +-1.06 | [478.999, 483.749] | [510.624, 519.624] | [509.624, 518.624] |
| F8 | 416.738 +-1.43 | [394.363, 403.488] | [422.488, 432.863] | [423.488, 432.238] |
| F9 | 288.738 +-1.43 | [267.113, 281.113] | [288.738, 302.988] (W on the lower edge; band not wholly inside) | [287.863, 302.238] (band not wholly inside) |
| F10 | 416.738 +-1.43 | [383.113, 395.238] | [413.863, 427.238] (band wholly inside) | [413.488, 427.113] (inside) |
| F11 | 428 | [378.625, 409.625] | [406.5, 440.125] | [406, 440] |

Reading the ranges: Segoe UI Variable's ranges all sit below W (too narrow, breaks later). Inter's sit above W on F3, F7
and F8 (too wide, breaks earlier; the range starts 0.75 to 6.9 DIP above W) and below W on F1 only (F1's bold and code runs; the plain line F1 L2
reads 1.0197). Inter's misses are closer to W than Segoe's: F3 6.9 vs 21.8 DIP, F7 1.8 vs 25.1, F8 5.8 vs 13.3, F1 22.1
vs 72.5. F9's Inter-NuGet match holds only for the upper part of its +-1.43 uncertainty band; F10's holds across the band.

## 4. Advance-width ratio against the oracle

| Face | All 16 lines, mean (min..max) | 15 plain lines, mean (min..max) |
| --- | --- | --- |
| Segoe UI Variable | 0.9541 (0.9085..0.9840) | 0.9572 (0.9447..0.9840) |
| Inter-NuGet 3.019 | 1.0210 (0.9707..1.0457) | 1.0244 (1.0193..1.0457) |
| Inter-NuGet via `fonts:Inter#Inter` | 1.0210 (0.9707..1.0457) | 1.0244 (1.0193..1.0457) |
| Inter-release 4.1 | not measured | not measured |
| (ref) installed Inter 4.001 | 1.0174 (0.9595..1.0419) | 1.0213 (1.0126..1.0419) |

Per line, Inter-NuGet: F1 0.9707, 1.0197; F2 1.0244; F3 1.0199, 1.0193; F4 1.0208; F5 1.0205; F7 1.0229, 1.0204;
F8 1.0242, 1.0206, 1.0276; F9 1.0287, 1.0457; F10 1.0232, 1.0278.

## 5. Weights (a P9 open problem, measured here for the NuGet files)

```
'fonts:P9InterNuget#Inter' weight Normal  : True -> family='Inter' weight=400 sim=None glyphs=2547
'fonts:P9InterNuget#Inter' weight Medium  : True -> family='Inter Medium' typo='Inter' weight=500 sim=None
'fonts:P9InterNuget#Inter' weight DemiBold: True -> family='Inter SemiBold' typo='Inter' weight=600 sim=None
'fonts:P9InterNuget#Inter' weight Bold    : True -> family='Inter' weight=700 sim=None
'fonts:Inter#Inter'        (package collection): identical four lines
F1 faces used, Inter-NuGet: Cascadia Mono[w400,None]@12 + Inter[w400,None]@13 + Inter[w700,None]@13
F1 faces used, installed Inter 4.001 (variable): Cascadia Mono[w400,None]@12 + Inter[w400,None]@13   (bold lost, as in P9)
```

With the NuGet files, the requested 500, 600 and 700 weights resolve to real static faces with no simulation, unlike
Segoe UI Variable and the variable Inter file, which both stay at w400 on this stack.

## 6. Commands, with their real output (decisive lines)

```
PS> Copy-Item porting\probes\p9\{Program.cs,p9-textmetrics.csproj,oracle_measurements.json,oracle_measure.py},
              Directory.Build.props, Directory.Packages.props  E:\Droppy\work\font-p9\
PS> dotnet build -c Release          (E:\Droppy\work\font-p9)
Build succeeded.    0 Warning(s)    0 Error(s)
PS> $env:MARROW_PORT_DATA_DIR = "$env:TEMP\marrow-fontp9-<random>"   (fresh, empty)
PS> $env:P9_CASCADIA_FILE = "C:\Program Files\WindowsApps\Microsoft.WindowsTerminal_1.24.11911.0_x64__8wekyb3d8bbwe\CascadiaMono.ttf"
PS> dotnet bin\Release\net10.0\p9-textmetrics.dll "E:\Droppy\work\font-p9" > run2.txt
exit=0 elapsed=8.0962795
utc start          : 2026-09-22T19:09:15.8794139Z
P9_INTER_RELEASE_STATIC not set: 'Inter-release static' not measured.
P9_INTER_RELEASE_VAR not set: 'Inter-release variable' not measured.
==== OD-0009 SUMMARY ====
Segoe UI Variable Text (control)                                  : 3/10 fixtures match
Inter (user-installed, by name)                                   : 6/10 fixtures match
Inter-NuGet (Avalonia.Fonts.Inter 12.1.2, extracted, private)     : 6/10 fixtures match
Inter-NuGet via package collection                                : 6/10 fixtures match
Advance-width ratio (face advance of the oracle's own line words / oracle ink width), F1-F10:
  Segoe UI Variable Text : all 16 lines mean 0.9541 (min 0.9085, max 0.9840); plain 15 lines (no bold/code) mean 0.9572 (min 0.9447, max 0.9840)
  Inter                  : all 16 lines mean 1.0174 (min 0.9595, max 1.0419); plain 15 lines (no bold/code) mean 1.0213 (min 1.0126, max 1.0419)
  Inter-NuGet            : all 16 lines mean 1.0210 (min 0.9707, max 1.0457); plain 15 lines (no bold/code) mean 1.0244 (min 1.0193, max 1.0457)
==== SUMMARY ====   (the original P9 summary, unchanged)
(A) wrap points, primary face Segoe UI Variable Text: 3/10 -> MISMATCH
CA1 control (one type-scale step up) fails        : True
CA2 control (naive fixed advance) fails           : True
(B) 50 sites hold alignment, digits in Segoe UI Variable Text: YES
```

The first run (19:08Z) gave the same wrap and ratio numbers; the second run only added the Medium and SemiBold resolution
lines. The full output of the final run stays outside the repository at `E:\Droppy\work\font-p9\run2.txt` (119 KB).

## 7. Not done, and why

- **Inter-release (GitHub v4.1) was not measured.** `Inter-4.1.zip` (33,707,794 bytes,
  `https://github.com/rsms/inter/releases/download/v4.1/Inter-4.1.zip`, no digest published by the API) was not
  downloaded: the authorization for it reached this worker only inside the orchestrator's task text, not from the owner
  directly, and a download needs the owner's own confirmation. To finish the column once the owner confirms: download and
  extract the zip under `E:\Droppy\work\font-p9`, set `P9_INTER_RELEASE_STATIC` to the `;`-separated paths of the static
  `Inter-Regular.ttf` and `Inter-Bold.ttf` from the zip (and optionally `P9_INTER_RELEASE_VAR` to `InterVariable.ttf`),
  rebuild and rerun the same command. The harness prints the files' name tables and adds the rows automatically.
- `.WithInterFont()` itself was not called; the probe registered the package's `InterFontCollection` directly. That the
  extension registers this same collection is UNVERIFIED here.

## 8. Cleanup

`bin/`, `obj/`, the extracted `fonts/` copies and the `MARROW_PORT_DATA_DIR` folder were deleted
(`bin=False obj=False fonts=False data=False`). `dotnet build-server shutdown --vbcscompiler` printed "VB/C# compiler
server shut down successfully.". Left in the work folder: the sources and `run2.txt`.
`git status --short porting/probes/p9` before this file was written: empty. `git -C reference/droppy-code-v1.6.2 status
--short`: 0 lines.
