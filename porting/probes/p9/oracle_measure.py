# Probe P9: pixel measurements taken from the tour-*@2x.png approximate oracle (OD-0004).
# Every number this script prints is APPROXIMATE under the OD-0004 caveat: the tour images are
# hand-regenerated assets, not captures tied to commit dd2b86ef.
#
# Pixel-to-point scale is taken from SOURCE, not guessed: scripts/tour_captures.py cuts the
# window's inside (8 pt inset on every edge, interior()/frame()), then fit() centre-crops the
# width to the 1320x824 aspect and resizes, so the height is preserved and
#     px_per_pt = 824 / (window_height - 16)            (tour pages)
#     px_per_pt = 409 / (window_height - 16)            (tour-themes tiles, tile_h = (824-6)//2)
# with window sizes from TourCaptures.swift / WINDOW_SIZES (welcome, panels 1280x800;
# hydra, themes 960x600).
#
# Usage: py oracle_measure.py <Assets.xcassets dir> <out json>
import json, sys
import numpy as np
from PIL import Image

assets, out = sys.argv[1], sys.argv[2]

def img(name):
    return np.asarray(Image.open(f"{assets}/{name}.imageset/{name}@2x.png").convert("L")).astype(int)

def divider(a, y, x0, x1):
    """A 1-px rule brighter than the rows 3 px above and below: its first and last column."""
    d = a[y, x0:x1] - (a[y - 3, x0:x1] + a[y + 3, x0:x1]) / 2
    idx = np.where(d >= 4)[0]
    return int(x0 + idx.min()), int(x0 + idx.max())

def lines(a, x0, x1, y0, y1, thr):
    """Text line bands (rows holding pixels brighter than thr) and each band's ink extent."""
    m = a[y0:y1, x0:x1] > thr
    rows = m.sum(1)
    bands, inb, s = [], False, 0
    for i, c in enumerate(rows):
        if c > 0 and not inb: s, inb = i, True
        if c == 0 and inb: bands.append((s, i)); inb = False
    if inb: bands.append((s, len(rows)))
    res = []
    for s, e in bands:
        cols = np.where(m[s:e].any(0))[0]
        res.append({"y": [y0 + s, y0 + e], "ink_x": [int(x0 + cols.min()), int(x0 + cols.max())]})
    return res

S_1280 = 824 / (800 - 16)
S_960 = 824 / (600 - 16)
S_TILE = ((824 - 6) // 2) / (600 - 16)

W, H, P, T, R = img("tour-welcome"), img("tour-hydra"), img("tour-panels"), img("tour-themes"), img("tour-recipes")
dw = divider(W, 305, 150, 1200)
dh = divider(H, 143, 0, 760)
dp = divider(P, 100, 400, 900)
dt = divider(T, 100, 0, 660)

def col_pt(d, s): return (d[1] - d[0] + 1) / s

result = {
    "scales_px_per_pt": {"tour-welcome": S_1280, "tour-panels": S_1280, "tour-hydra": S_960, "tour-themes-tile": S_TILE},
    "dividers_px": {"tour-welcome": dw, "tour-hydra": dh, "tour-panels": dp, "tour-themes-tile-dark": dt},
    "column_pt": {
        "tour-welcome (calibration check, source says 820)": col_pt(dw, S_1280),
        "tour-themes tile (calibration check, source says 820)": col_pt(dt, S_TILE),
        "tour-hydra": col_pt(dh, S_960),
        "tour-panels": col_pt(dp, S_1280),
    },
    "lines": {
        "F1+F2 welcome": lines(W, 200, 1110, 312, 385, 170),
        "F3 welcome": lines(W, 200, 1110, 684, 722, 170),
        "F4 welcome bubble": lines(W, 540, 1080, 228, 252, 170),
        "F5 welcome bubble": lines(W, 500, 1080, 428, 452, 170),
        "F7 hydra": lines(H, 0, 740, 158, 204, 170),
        "F8 panels": lines(P, 430, 890, 110, 164, 170),
        "F9 panels bubble": lines(P, 540, 870, 480, 518, 170),
        "F10 panels": lines(P, 430, 890, 714, 752, 170),
        "F11 recipes header": lines(R, 675, 1180, 137, 170, 140),
    },
}
with open(out, "w", encoding="utf-8") as f:
    json.dump(result, f, indent=1)
print(json.dumps({k: result[k] for k in ("scales_px_per_pt", "dividers_px", "column_pt")}, indent=1))
for k, v in result["lines"].items():
    print(k, [(l["y"], l["ink_x"]) for l in v])
