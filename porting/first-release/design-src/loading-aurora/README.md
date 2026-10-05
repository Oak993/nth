# Loading Animation (LOADING-AURORA, revision 2)

Review page: `loading-aurora.html` (one self-contained file, about 73 KB, no network, system fonts). The icon on the page is
the locked `D-aurora.js` with `iconkit.js`, inlined unchanged, used for the boot only. Nothing in `src` changes.
Picks bar: `loading: boot=B, loader=1..5` (plus `, history=N` / `diff=N` / `attachment=N` when a place gets its own loader),
with Copy (clipboard in try/catch, fallback selects the text).

Labels: SOURCE FACT = locked files, OD-0016/0018/0108/0112, ED-097/102/103/154, the main window layout in
`BootStageWindow.Layout` (38 DIP title bar, 260 sidebar, 96 composer) and its 1320 x 860 default size. ENGINEERING
DECISION = every motion below (proposals). UNRESOLVED = at the end.

## 1. Boot B: the tile grows into the window frame

Variants A, C and the per-turn pulse are gone (the pulse of OD-0018 was one turn of the old spiral; nothing animates in the
chat while the model works, the composer's working dots stay). Shown for a 1280 x 800 and a 900 x 700 window, dark and light,
one scrubber per size, plus a Reduce Motion version. A live readout under each pair compares the tile's rectangle with the
window frame; at the end it reads "tile is the frame, difference 0.0".

Timeline (ms; R = app ready, H = max(R, 1800)): desktop dims 0-260 (new scrim); D launch 120-1320; D idle loop 1320 to H;
warm-up and bootstrap 0 to R with the main window hidden (OD-0108); at H the main window is uncloaked at its exact frame with
content opacity 0, and ONE progress `p` (cubic-bezier .32,.72,0,1, 1000 ms) runs H to H+1000:
- the tile rectangle (centred on the frame's centre) goes from the icon square (0.88 x 440 DIP) to the frame: x, y, width,
  height and corner radius (0.225 x side to 8 DIP, Windows 11's corner, platform value; the source's 26 pt is the rectangle
  the OS then rounds, ED-338);
- the window's clip rectangle is that same rectangle every frame, so glass and content (opacity from p = .35 to 1) can only
  appear inside it; the 38 DIP title-bar band is drawn inside the growing rectangle and hands over to the real one;
- the aurora image is cover-cropped to the rectangle and, from p = .5, blurs into a glow behind the glass, inside the frame only.
H+1000: p = 1, the last tile frame is the window frame. Glow fades H+1000 to H+2200; stage closes H+2200.
Reduce Motion: still tile fades in 300 ms, holds to max(R, 1200), fades out 300 ms while the window fades in over 400 ms.

### What it changes
- `LoadingMark.cs`: drops the Comet arms, trails, gather particles, turn timeline, pulse ring and final-rest rule
  (`CometMotion`, `CometPainter`, `GatherParticle`, the turn parts of `BootSequence`, `WindowParticle`, the disintegration);
  the boot mark is the D icon rendered by the port (launch 0..1, then idle loop). New: a grow rectangle and glow painter.
- `BootStageWindow.cs`: keeps the stage window, cloaking (ED-236) and `Layout`; hand-over starts at `H` instead of
  `FinalRest + 0.1`; instead of root opacity/scale `.985` by particle share, `Apply` sets the root content opacity and a
  clip rectangle from the one progress `p` (no scale: the window never resizes). The stage must sit BELOW the main window for
  the glow to show through its Acrylic backdrop: UNRESOLVED, needs a probe after P12.

## 2. Loaders for empty areas (ED-097: only after 400 ms; fade in 200 ms)

The icon is not a loader. Five loaders borrow only the Aurora's emerald, teal, violet and its slow sway, each in the three
places (older chat history 48 px, diff pane 44 px above "Reading the diff...", large attachment preview 40 px), side by side:
1 Aurora Ring (thin glass ring, a colour-flowing comet round it, 1.8 s); 2 Curtain Fold (24 thin rays that fold and sway, 2.4 s);
3 Drift Lights (three soft points trade colours, 2.4 s); 4 Flow Arc (hairline arc, colour flows along it, 1.5 s turn, 2.8 s colour
cycle); 5 Aurora Sheen (no mark: an aurora sweep across a glass placeholder, 1.8 s). All loop seamlessly, canvas or transform/opacity.
Reduce Motion: calm still pose, opacity breath 55 % to 100 % over 2.4 s. Dark and light via the page theme.
Change: `LoadingMark.cs`'s in-app sites (`LoadingMark` control, ED-097 (4)) take the chosen loader instead of the Comet tile;
the 400 ms and 200 ms rules (ED-097 (5)) stay.

## Not done / limits
Not measured on Windows: the warm-up progress is illustrative; whether the warm-up fits without a stutter is untested.
No C# changed or built. Headless Chromium screenshots checked the grow frames and the rectangle readout, not 60 fps.
