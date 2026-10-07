# Shipping icon D Aurora (T21, OD-0112)

The generator executes the **unmodified** locked `src/iconkit.js` and
`src/icons-a-j/D-aurora.js`, whose approved hashes are pinned in the generator.
The twenty-direction decision page and its module contract are also hashed.
No C# drawing, screenshots, redesign, extra hinting, or PNG downsampling is used.
Each canvas starts in a fresh page with fresh caches and uses the still state
`{launch:1,idle:0,still:true}` and the source's dark surface variant. Windows
uses one icon across themes; the source's light variant only changes edge/shadow
contrast and is not a separately installed identity.

From the repository root, using Python 3 and a local Chrome/Chromium/Edge:

```sh
python -m pip install playwright==1.62.0
python porting/tools/generate_app_icon_d.py --browser /path/to/chromium
python porting/tools/generate_app_icon_d.py --browser /path/to/chromium --check
```

`AURORA_BROWSER` can supply the executable instead. Playwright drives a single
disposable browser over its debugging pipe. It does not download a browser or
serve a site. CSP and routing deny resource requests; browser background
networking is disabled. Playwright's pinned defaults disable the Chromium
sandbox for this local rendering process. The manifest records the browser
version, Playwright version, custom flags, launch configuration, source/tooling
SHA-256 hashes, render state, and every generated file's hash and byte length.
The checked-in outputs were generated with Chromium **151.0.7922.173** on Linux.
For byte-identical regeneration use that browser version and rendering platform;
`--check` fails on any changed byte, including provenance. It never rewrites a
baseline. Installing Playwright does not install its browsers automatically.
Generation and checking also require the seven available dark still exports
to equal the accepted `tests/fixtures/aurora/chrome/png/*-dark-still-0.png`
references byte for byte. Those reference files are read, never rewritten.

Outputs:

| File | Render |
| --- | --- |
| `icon-{size}.png` here | 16, 20, 24, 32, 40, 48, 64, 96, 128, 256 true Windows pixels, DPR 1 |
| `src/Marrow.App/Assets/icon.ico` | All ten PNGs verbatim in a 32-bit RGBA Windows ICO directory |
| `src/Marrow.App/Assets/icon.png` | 512 px, whole source box with its safe margin |
| `src/Marrow.UI/Assets/Images/marrow-icon.png` | 256 px, source safe box filling the attribution slot |
| `src/Marrow.UI/Assets/Images/marrow-icon@2x.png` | 512 px, source safe box filling the attribution slot |

For the attribution PNGs, the canvas transform maps `IconKit.box(size)` into the
output frame before calling the source's `draw`. This retains ED-276's full-bleed
slot while using Aurora's own geometry. All other exports use
`MarrowIcons.render(..., size, 'dark', still, 1)`. The source selects its tiny
band at 16/20, small at 24/32, medium at 40 through 96, and large at 128 and above.
The PNG-compressed ICO frames target Windows 11; no legacy BMP compatibility
layer is required. There are no additional installer PNG assets.

Consumers: both App and Setup `ApplicationIcon` properties and Velopack's
`--icon` argument refer to the same ICO. Avalonia 12.1.2 automatically embeds
`ApplicationIcon` as `!__AvaloniaDefaultWindowIcon`, which `Window.LoadDefaultIcon`
uses on show; it supplies the window/taskbar/Alt-Tab icon without an extra copy.
`AppIconImage`, the About card/pill, Updates UI, Welcome and new-thread prompt
use the App PNG. Toast startup materializes that PNG as `notification-icon.png`;
`ToastIcon.Install` overwrites a differing prior copy. The Swift `droppy-logo`
lookup key loads the two UI PNGs. The unused OD-0015 SVG assets are removed;
their source and pixel assertions remain available in Git history.

The old packaged-icon test expectations have **not** been regenerated. Their
own documentation supplies no generator or regeneration policy. This tool
deliberately writes no test file or existing Aurora renderer reference.
OD-0112 supersedes their OD-0015 artwork assumptions, but changing those locked
expectations requires an authorized, documented test-baseline workflow. T21
does not weaken their assertions or tolerances. See `porting/first-release/T21-REPORT.md`
for test evidence and remaining Windows verification.
