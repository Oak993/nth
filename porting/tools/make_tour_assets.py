"""RR-22: turns the native gallery's tour captures into the shipped tour assets.

Input: the gallery folder (MARROW_GALLERY_DIR) after GalleryNativeTests.Gallery_welcome_tour_pictures and
Gallery_welcome_tour_pictures_continued ran (GalleryTourScenes): tour-<page>-dark.png captures of a 1320 x 824 pixel window
drawn at the app's 200 % zoom (1720 x 1074), and tour-theme-<name>-dark.png for the four theme shots.
Output: tour-<page>@2x.png (exactly 1320 x 824, the window's own pixels, centre-cropped if the capture carries a frame pixel or
two more) and tour-<page>.png (660 x 412, a Lanczos reduction of the 2x file) for each of the eight Tour.Pages images.
tour-themes is the four theme shots tiled two by two, as the Mac asset is.

Usage: python porting/tools/make_tour_assets.py <gallery folder> <output folder>
"""
import sys
from pathlib import Path

from PIL import Image

W2, H2 = 1320, 824
PAGES = ["welcome", "hydra", "pairs", "slider", "threads", "recipes", "panels"]
THEMES = ["tokyoNight", "claude", "catppuccinLatte", "rosePine"]


CW, CH = 1720, 1074  # the capture: an 860 x 537 point window at the app's 200 % zoom (GalleryTourScenes)


def fit(image: Image.Image) -> Image.Image:
    """Exactly W2 x H2: the capture centre-cropped to CW x CH (a frame pixel or two), then reduced with Lanczos."""
    w, h = image.size
    if w < CW or h < CH:
        raise SystemExit(f"capture {w}x{h} is smaller than {CW}x{CH}")
    left, top = (w - CW) // 2, (h - CH) // 2
    return image.crop((left, top, left + CW, top + CH)).convert("RGB").resize((W2, H2), Image.Resampling.LANCZOS)


def settings_page(image: Image.Image) -> Image.Image:
    """The Settings window (Hydra page) at 100 %: its 780 x 580 content inside the native shell's 180 px shadow space, the top of it
    at the tour's 1.6 aspect, enlarged to W2 x H2 (softer than the 2x captures; the 1x file is a reduction)."""
    w, h = image.size
    inset = 180
    content = image.crop((inset, inset, w - inset, h - inset)).convert("RGB")
    cw, ch = content.size
    top = content.crop((0, 0, cw, min(ch, round(cw / (W2 / H2)))))
    return top.resize((W2, H2), Image.Resampling.LANCZOS)


def save(two_x: Image.Image, out: Path, name: str) -> None:
    two_x.save(out / f"tour-{name}@2x.png", optimize=True)
    two_x.resize((W2 // 2, H2 // 2), Image.Resampling.LANCZOS).save(out / f"tour-{name}.png", optimize=True)


def main() -> None:
    gallery, out = Path(sys.argv[1]), Path(sys.argv[2])
    out.mkdir(parents=True, exist_ok=True)
    for page in PAGES:
        if page == "recipes":  # Settings > Hydra from the gallery's settings Facts (settings-hydra-dark.png), at 1x
            save(settings_page(Image.open(gallery / "settings-hydra-dark.png")), out, page)
        else:
            save(fit(Image.open(gallery / f"tour-{page}-dark.png")), out, page)
    grid = Image.new("RGB", (W2, H2))
    for index, theme in enumerate(THEMES):
        tile = fit(Image.open(gallery / f"tour-theme-{theme}-dark.png")).resize((W2 // 2, H2 // 2), Image.Resampling.LANCZOS)
        grid.paste(tile, ((index % 2) * (W2 // 2), (index // 2) * (H2 // 2)))
    save(grid, out, "themes")
    print(f"wrote {len(PAGES) + 1} tour images (1x and 2x) to {out}")


if __name__ == "__main__":
    main()
