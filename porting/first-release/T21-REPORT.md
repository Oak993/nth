# T21 — App icon D Aurora

Verdict: **NOT READY**. The assets and executable icon resources pass generation
and packaging checks. Existing icon tests still require OD-0015 artwork, and their
documentation provides no authorized regeneration workflow. Windows runtime and
installer verification remain outstanding.

Base: `origin/claude/verify-terminal-20261001` at
`1291606cbac734da0b2afe125ad9c01227eed83b`. Work branch: `codex/app-icon-d`.
Date: 2026-10-07. Decisions read: OD-0112, OD-0015/0016, ED-097 and ED-276.
Engineering record: **ED-401**, appended without changing any of the
ledger's original 1,786,423 bytes. No owner decision or test file is edited.

## Assets and consumers

Replaced:

- `src/Marrow.App/Assets/icon.ico`: ten 32-bit RGBA PNG frames, **16, 20, 24,
  32, 40, 48, 64, 96, 128, 256 px**.
- `src/Marrow.App/Assets/icon.png`: **512 px**, source safe margin retained.
- `src/Marrow.UI/Assets/Images/marrow-icon.png` and `marrow-icon@2x.png`:
  **256/512 px**, Aurora's `IconKit.box(size)` mapped to the full attribution frame
  before drawing, retaining ED-276's full-bleed placement.

Removed the unused old `icon.svg`, `icon-small.svg`, and `icon-mono.svg`; OD-0112
keeps the superseded artwork in history. No production code loads those files.
The locked decision page and JavaScript files are unchanged.

The App and Setup projects' `ApplicationIcon` and `scripts/package-windows.ps1`'s
Velopack `--icon` all use the same replaced ICO. Avalonia 12.1.2's
`AvaloniaBuildTasks.targets` includes that file as `!__AvaloniaDefaultWindowIcon`
by default; `Window.LoadDefaultIcon` loads it on show for window, taskbar and
Alt-Tab. No separately copied window icon is needed.

`AppIconImage` serves Welcome, the new-thread prompt, the About identity pill,
the About card and Updates UI from the App PNG. Toast startup writes that PNG
through `ToastIcon.Install` to `notification-icon.png`, replacing differing old
bytes. `SwiftImage.Named("droppy-logo")` retains the source lookup key while
loading the two replaced UI PNGs for the attribution row. Installer scripts and
the Setup manifest specify no additional icon PNG sizes. Source geometry,
view sizes, interpolation, accessibility and the existing Aurora motion are
unchanged.

## Reproduction

Generator: `porting/tools/generate_app_icon_d.py`.
Instructions: `porting/first-release/design-src/app-icon/generated/README.md`.
SHA-256 manifest: `porting/first-release/design-src/app-icon/generated/sha256.json`.
The ten individual Windows PNG exports are checked in alongside that manifest.

Python 3, Playwright **1.62.0**, local Chromium **151.0.7922.173** on Linux:

```sh
python3 porting/tools/generate_app_icon_d.py
python3 porting/tools/generate_app_icon_d.py --check
```

The script checks the approved hashes of `iconkit.js` and `D-aurora.js`, executes
them directly in a fresh page per canvas, uses dark/still/DPR 1, and exports
Canvas PNGs. Source size bands supply all simplification through 32 px. There
is no added artwork or hinting. Seven still exports must byte-match the accepted
Chrome fixtures before any output is written. Existing renderer references are
never regenerated. A new browser provenance changes the manifest and fails
`--check` until deliberately regenerated; no tolerance is used.

## Validation

- Generator and `--check`: **PASS**, all **15** generated output files identical
  on regeneration, including the SHA-256 manifest.
- Accepted Aurora Chrome still references: **PASS**, all seven available sizes
  (16/24/32/48/64/256/512) match **PNG bytes**, not just rendered pixels.
- ICO structure: **PASS**, ten correct dimensions and 32-bit entries; every
  embedded PNG equals its checked-in source export byte for byte.
- `dotnet build Marrow.sln -p:EnableWindowsTargeting=true`: **PASS**, zero
  warnings/errors with .NET SDK **10.0.401**.
- Both App and Setup `dotnet build <project> -r win-x64 --self-contained false
  -p:EnableWindowsTargeting=true`: **PASS**, zero warnings/errors. Their Windows
  EXEs' `RT_GROUP_ICON` directories contain all ten sizes, and every `RT_ICON`
  payload equals the corresponding generated PNG bytes. No Windows binary was
  executed and no installation or release publication was performed.
- Focused UI packaged-icon tests: **35 passed, 4 failed, 0 skipped**. Failures:
  the two old PNG hashes in `SwiftImageTests`, the old core/arm samples in
  `MarrowLogoArtworkTests`, and the old file hashes in `AssetCatalogueTests`.
- Focused App icon/toast tests with an isolated `MARROW_PORT_DATA_DIR`:
  **11 passed, 5 failed, 0 skipped**. Failures: `AboutIdentityIconTests`, both
  `MarrowLogoPlacesTests`, the icon assertion in `NewThreadPromptGeometryTests`,
  and the old core sample in `MarrowLogoSettingsRowTests`. Toast materialization
  checks pass. The new App PNG hash starts `2833c700`; the four old hash assertions
  expect `52d605d7`.

The first broad/focused attempts omitted the required scratch-data variable.
Those results are superseded by the isolated focused run and the corrected
bounded broad run below. No production data path was used for any successful
data-root-dependent test.

Corrected broad command (disposable `/tmp/marrow-t21-data`):

```sh
MARROW_PORT_DATA_DIR=/tmp/marrow-t21-data PATH=/tmp/marrow-dotnet:$PATH \
  timeout --kill-after=10s 180s dotnet test Marrow.sln --no-build \
  -p:EnableWindowsTargeting=true --filter 'FullyQualifiedName!~Native' \
  --nologo -v minimal --logger 'trx;LogFilePrefix=T21-isolated' \
  --results-directory /tmp/marrow-t21-isolated
```

The requested filter still reaches Windows P/Invokes on Linux. The corrected
run hit its **180-second limit (exit 124)**. Core's partial result was **2,851
passed, 306 failed, 0 skipped** before a host crashed loading `kernel32.dll`;
other failures load `icu.dll` and additional Windows libraries. All four test
projects started, but the remaining hosts did not complete or produce final
TRX summaries before timeout. Incomplete/aborted coverage is never a pass.
The initial UI host was terminated after over four minutes of failed Windows
calls and a spinning test; the corrected run has the explicit 180-second limit.

Gate B: **8/8 PASS**. Gate A: 7 pass, 5 fail because the pristine Windows reference
checkout is absent here. Gate C: quarantine guard passes; existing stub markers
and the unimplemented gate portion fail. The protected-path checker flags the
new generator, csproj comment and engineering ledger as orchestrator-owned;
they are intentional lead edits required by T21, not worker changes. No guard
or gate is weakened. `git diff --check` passes.

## Before READY

The existing packaged-icon tests have no documented reference generator or
regeneration permission. In compliance with T21, their locked hashes, artwork
samples, test manifest and all tolerances/assertions remain byte-identical.
An authorized test-baseline workflow must derive new expectations from the
locked OD-0112 source and cite OD-0112 plus an engineering entry. This report
does not supply retroactive permission by adding documentation to those tests.

On Windows, set `MARROW_PORT_DATA_DIR` to a new throwaway directory and run the
requested filtered solution tests; run native desktop tests separately. Check
Explorer/executable and Setup icons, window/taskbar/Alt-Tab, Start shortcuts,
toast PNG refresh, and the in-app icon sites at 100/125/150/200% DPI. Build the
local Velopack package when installer work is due (OD-0105) and inspect its
Setup/shortcut icon resources. Existing installed shell icon caches may require
a fresh test install/session. T21 itself does not install or publish anything.
