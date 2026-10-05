# Wallpaper library design sources (OD-0110 and its amendment)

Snapshot of the two design pages' sources, taken on 2026-10-05 from E:\Droppy\work\design\decisions-2026-10-05\wallpaper-library.

- **static/** holds 30 wallpapers in three tiers (vibrant, balanced, minimal), each with 5 colour packs, a grain control and one option. `wallpapers.json` is the data and holds each shader's `src`.
- **animated/** holds 24 loopable wallpapers in the same three tiers, each with 5 colour packs, a speed control and options. `common.glsl` is the shared math, `walls/` holds one shader per wallpaper, and `wallpapers.json` holds the data and the inlined GLSL.

The shaders are written in a GLSL ES 3.00 subset that ports to Skia SkSL as written (see the header of `common.glsl`). The pack arrives as six OKLab colours in `u_pal`, the theme as `u_dark`, and the options as `u_opt`. Animated time enters only as `TAU * t / T`.

The design pages are still being polished. When they change, this snapshot is refreshed, and the app's data-driven tests pick up the new shaders.
