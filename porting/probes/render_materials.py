"""
Probe: render the SAME glass surface two ways over the SAME real Windows desktop, so the
owner can see the difference in character before ruling on ADR-0001.

WHAT IS REAL AND WHAT IS NOT, stated up front because it decides how much this is worth:

  * The backdrop is a REAL screen capture from this Windows 11 machine (2560x1440).
  * The geometry and tint are SOURCE FACTS read out of the pinned Droppy Code tree:
        window size      1320 x 860      WindowManager.swift:19
        corner radius    26              Chrome.swift:8   windowCornerRadius
        tint colour      #1E1E20         AppTheme.swift:116  dark theme surface
        tint opacity     0.30            Chrome.swift:178, :180
  * Panel A reproduces Microsoft's DOCUMENTED Acrylic recipe in software
    (backdrop -> gaussian blur -> saturation -> tint over luminosity -> fine noise).
    It is NOT live DWM output. Live DWM output could not be captured because this
    machine's session is LOCKED, so no window composites and screen capture returns a
    frozen lock-screen frame. porting/probes/acrylic_window.ps1 produces the real thing
    and aborts loudly if its control case does not render; run it on an unlocked session.
  * Panel B reproduces the CHARACTER of macOS Liquid Glass: the same blur, plus edge
    refraction that bends the backdrop near the border, per-channel offsets that produce
    chromatic fringing, and a specular rim. It is a faithful illustration of the
    mechanism, NOT a reproduction of Apple's shader, which is not public.

So this answers "what is the difference in kind between blur-and-tint and lensing over
the same content", which is the question the ruling turns on. It does not and cannot
establish pixel parity with macOS.
"""

import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont
from pathlib import Path

OUT = Path(__file__).parent / "out"

# ---- SOURCE FACTS -----------------------------------------------------------------
WIN_W, WIN_H = 1320, 860          # WindowManager.swift:19
CORNER = 26                       # Chrome.swift:8
TINT = (0x1E, 0x1E, 0x20)         # AppTheme.swift:116, dark theme surface
TINT_ALPHA = 0.30                 # Chrome.swift:178 / :180
# -----------------------------------------------------------------------------------

BLUR = 30.0        # Acrylic's documented gaussian radius
NOISE = 0.02       # Acrylic's documented noise amount


def rounded_mask(w, h, r):
    m = Image.new("L", (w, h), 0)
    ImageDraw.Draw(m).rounded_rectangle([0, 0, w - 1, h - 1], radius=r, fill=255)
    return m


def edge_distance(w, h, r):
    """Distance in px from each pixel to the rounded-rect border, negative outside."""
    ys, xs = np.mgrid[0:h, 0:w].astype(np.float32)
    # distance to the rounded rectangle, via the standard rounded-box SDF
    cx, cy = (w - 1) / 2.0, (h - 1) / 2.0
    qx = np.abs(xs - cx) - (w / 2.0 - r)
    qy = np.abs(ys - cy) - (h / 2.0 - r)
    qx_c = np.clip(qx, 0, None)
    qy_c = np.clip(qy, 0, None)
    outside = np.sqrt(qx_c ** 2 + qy_c ** 2)
    inside = np.minimum(np.maximum(qx, qy), 0.0)
    sdf = outside + inside - r + r  # keep r term explicit for readability
    return -(outside + inside)      # positive inside, 0 at the border


def sample(arr, xs, ys):
    """Bilinear sample of an HxWx3 float array at fractional coords."""
    h, w = arr.shape[:2]
    xs = np.clip(xs, 0, w - 1.001)
    ys = np.clip(ys, 0, h - 1.001)
    x0 = np.floor(xs).astype(np.int32); x1 = x0 + 1
    y0 = np.floor(ys).astype(np.int32); y1 = y0 + 1
    fx = (xs - x0)[..., None]; fy = (ys - y0)[..., None]
    a = arr[y0, x0]; b = arr[y0, x1]; c = arr[y0 + 0, x0]; d = arr[y1, x1]
    top = a * (1 - fx) + b * fx
    bot = arr[y1, x0] * (1 - fx) + d * fx
    return top * (1 - fy) + bot * fy


def acrylic(backdrop: Image.Image) -> Image.Image:
    """Microsoft's documented Acrylic recipe, in software."""
    blurred = backdrop.filter(ImageFilter.GaussianBlur(BLUR))
    a = np.asarray(blurred).astype(np.float32) / 255.0

    # saturation boost, as the recipe's exclusion/saturation stage approximates
    lum = a @ np.array([0.2126, 0.7152, 0.0722], dtype=np.float32)
    a = np.clip(lum[..., None] + (a - lum[..., None]) * 1.25, 0, 1)

    tint = np.array(TINT, dtype=np.float32) / 255.0
    a = a * (1 - TINT_ALPHA) + tint * TINT_ALPHA

    rng = np.random.default_rng(7)
    a = np.clip(a + rng.normal(0, NOISE, a.shape).astype(np.float32), 0, 1)
    return Image.fromarray((a * 255).astype(np.uint8))


def liquid(backdrop: Image.Image) -> Image.Image:
    """The Liquid Glass mechanism: blur, plus edge refraction, chromatic fringing, rim."""
    w, h = backdrop.size
    blurred = np.asarray(backdrop.filter(ImageFilter.GaussianBlur(BLUR))).astype(np.float32) / 255.0
    crisp = np.asarray(backdrop).astype(np.float32) / 255.0

    d = edge_distance(w, h, CORNER)          # px inside the border
    band = 46.0                              # how far the lens reaches inward
    t = np.clip(1.0 - d / band, 0.0, 1.0)    # 1 at the border, 0 deep inside
    lens = t ** 2.2                          # the bend concentrates near the edge

    # inward normal, from the gradient of the distance field
    gy, gx = np.gradient(d.astype(np.float32))
    n = np.sqrt(gx ** 2 + gy ** 2) + 1e-6
    nx, ny = gx / n, gy / n

    ys, xs = np.mgrid[0:h, 0:w].astype(np.float32)
    strength = 26.0                          # peak displacement in px at the border

    # Per-channel displacement produces the chromatic fringing the field capture shows.
    disp = lens * strength
    out = np.empty_like(crisp)
    for ch, k in ((0, 1.10), (1, 1.00), (2, 0.90)):
        sx = xs - nx * disp * k
        sy = ys - ny * disp * k
        # near the edge the lens shows refracted CRISP content; the interior stays blurred
        refr = sample(crisp, sx, sy)[..., ch]
        out[..., ch] = blurred[..., ch] * (1 - lens) + refr * lens

    tint = np.array(TINT, dtype=np.float32) / 255.0
    out = out * (1 - TINT_ALPHA) + tint * TINT_ALPHA

    # specular rim: a bright hairline just inside the border, strongest top-left
    rim = np.exp(-((d - 1.5) ** 2) / 6.0)
    light = np.clip((-nx * 0.7 - ny * 0.7), 0, 1) ** 1.5
    out = np.clip(out + (rim * light * 0.55)[..., None], 0, 1)

    # a soft inner shadow just inside the rim gives the material thickness
    inner = np.exp(-((d - 10.0) ** 2) / 120.0) * 0.16
    out = np.clip(out - inner[..., None], 0, 1)

    rng = np.random.default_rng(7)
    out = np.clip(out + rng.normal(0, NOISE * 0.5, out.shape).astype(np.float32), 0, 1)
    return Image.fromarray((out * 255).astype(np.uint8))


def label(img, text, sub=""):
    d = ImageDraw.Draw(img)
    try:
        f = ImageFont.truetype("segoeui.ttf", 30)
        fs = ImageFont.truetype("segoeui.ttf", 19)
    except Exception:
        f = ImageFont.load_default(); fs = ImageFont.load_default()
    d.rectangle([0, 0, img.width, 78], fill=(12, 12, 14))
    d.text((18, 12), text, font=f, fill=(255, 255, 255))
    if sub:
        d.text((18, 48), sub, font=fs, fill=(165, 165, 172))
    return img


def main():
    screen = Image.open(OUT / "screen_full.png").convert("RGB")
    # Take the window-sized region from the most demanding part of the backdrop:
    # the high-contrast Earth limb, where refraction and fringing are most visible.
    left = (screen.width - WIN_W) // 2
    top = int(screen.height * 0.28)
    back = screen.crop((left, top, left + WIN_W, top + WIN_H))
    back.save(OUT / "backdrop_region.png")

    mask = rounded_mask(WIN_W, WIN_H, CORNER)

    panels = {}
    for name, fn in (("acrylic", acrylic), ("liquid", liquid)):
        body = fn(back)
        panel = Image.composite(body, back, mask)
        panel.save(OUT / f"panel_{name}.png")
        panels[name] = panel

    # Side by side, full size.
    gap = 26
    sbs = Image.new("RGB", (WIN_W * 2 + gap * 3, WIN_H + 78 + gap * 2), (18, 18, 20))
    for i, (name, title, sub) in enumerate((
        ("acrylic", "A  Windows Acrylic",
         "documented DWM recipe: blur + saturation + tint. No lensing. What Windows can do without screen capture."),
        ("liquid", "B  Liquid Glass character",
         "same blur, plus edge refraction, chromatic fringing and a specular rim. Needs desktop pixels in a shader."),
    )):
        tile = Image.new("RGB", (WIN_W, WIN_H + 78), (12, 12, 14))
        tile.paste(panels[name], (0, 78))
        label(tile, title, sub)
        sbs.paste(tile, (gap + i * (WIN_W + gap), gap))
    sbs.save(OUT / "comparison_full.png")

    # Zoom where it actually matters: the stretch of border the backdrop's highest-contrast
    # feature crosses. Chosen by measurement, not by eye, so the crop cannot flatter either
    # panel. The first attempt cropped the top-left corner, which on this wallpaper is black
    # space, so both panels looked identically dark and the comparison showed nothing.
    cw, ch = 460, 300
    edge_col = np.asarray(back.convert("L")).astype(np.float32)[:, :90]
    row_contrast = edge_col.std(axis=1)
    band = np.convolve(row_contrast, np.ones(ch) / ch, mode="valid")
    top_y = int(np.argmax(band))
    print(f"zoom crop chosen at y={top_y} (highest-contrast band on the left border)")
    zoom = Image.new("RGB", (cw * 2 + gap * 3, ch + 78 + gap * 2), (18, 18, 20))
    for i, (name, title) in enumerate((("acrylic", "A  Acrylic edge"), ("liquid", "B  Liquid Glass edge"))):
        c = panels[name].crop((0, top_y, cw, top_y + ch)).resize((cw, ch), Image.LANCZOS)
        tile = Image.new("RGB", (cw, ch + 78), (12, 12, 14))
        tile.paste(c, (0, 78))
        label(tile, title, "left border, 1:1, highest-contrast band")
        zoom.paste(tile, (gap + i * (cw + gap), gap))
    zoom.save(OUT / "comparison_corner.png")

    # Difference map, to show it is not a subtle repaint.
    a = np.asarray(panels["acrylic"]).astype(np.int16)
    b = np.asarray(panels["liquid"]).astype(np.int16)
    diff = np.abs(a - b).sum(axis=2)
    print(f"mean abs difference: {diff.mean():.1f}/765   max: {diff.max()}/765")
    print(f"pixels differing by >30: {(diff > 30).mean() * 100:.1f}%")
    Image.fromarray(np.clip(diff * 2, 0, 255).astype(np.uint8)).save(OUT / "difference_map.png")

    for f in ("comparison_full.png", "comparison_corner.png", "panel_acrylic.png",
              "panel_liquid.png", "difference_map.png"):
        print("wrote", OUT / f)


if __name__ == "__main__":
    main()
