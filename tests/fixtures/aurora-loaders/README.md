# Locked Aurora loader browser references

`render.py` executes the unmodified PAL, prep and LOADERS JavaScript from
`porting/first-release/design-src/loading-aurora/loading-aurora.html`. Loader 5 uses its
actual CSS and the verbatim `PLACES[1].sk` HTML. No C# drawing participates in the oracle.

72 frames cover loaders 2/4/5, both themes, scales 1/1.25/1.5/2 and times
0.6/0.84/1.2 seconds. The canvas backing DPR equals each sample's scale; a CSS transform
scales the sample box to the same pixel dimensions. For sheen this tests transformed CSS
layout at browser DPR 1, rather than four separate operating-system DPI settings.
Chrome's screenshot is cropped at the recorded integer sample coordinates, without
resampling. The backgrounds are transparent and all PNGs retain RGBA. These are drawing references, not
complete application screenshots or proof of the 400 ms site delay.

The manifest binds the locked source, generator, browser version, inputs and every PNG
SHA256. Tests compare every pixel with a tolerance declared before the first comparison:
peak premultiplied RGBA channel error at most 24/255 and mean error at most 0.5/255
for each channel independently (lead ruling, 2026-10-05). Straight RGBA peak and mean
remain diagnostic output. The alpha channel participates in acceptance.
No masks or omitted pixels. Failed comparisons remain failures.

Regeneration requires a heavy slot and the native lock. Run the existing Python with
Pillow, setting MARROW_CHROME_VERSION to the installed Chrome ProductVersion. The script
uses an isolated scratch profile, local files, deny-network CSP, disabled browser
background networking and blocked DNS; it downloads nothing.

Harness correction: the extracted `.sk` must be positioned, as the locked `.ld .sk`
selector positions it. The initial harness omitted that property, which let its absolute
gradient escape the rounded overflow clip. Current PNGs include the corrected property.

Drawing observations for the implementation: Chrome paints the 92% bar of a 180 CSS-pixel
box to 166 logical pixels before the sample scale (at scale 2, pixels 330/331 contain the
bubble and pixel 332 is empty). CSS gradient colours interpolate premultiplied sRGB;
Skia's public fixed-gradient API interpolates unpremultiplied colours. The port caches a
dense table of the premultiplied function once. These adaptations do not waive the pixel
comparison or establish parity while comparisons fail.

T10 continuation: all 72 frames now pass the unchanged per-channel bounds. Curtain uses
Ganesh's device-space quad coverage with float, unpremultiplied sRGB Canvas gradients and
dithering. Flow uses the Canvas single-arc fast path's circle coverage, clip planes and
summed round caps. Source ray/stroke geometry, draw order and blend modes are unchanged.

Fractional-scale CSS painting has two sampling stages. Blink promotes a scaled ancestor
with multiple composited descendants to a render surface; EffectTree rounds its backing
scale upward. The picture layers raster at the ideal scale, then bilinear sampling into
and out of that surface produces the reference's straight-edge alpha spill. At 1.25,
the first bar's bottom rows are 221/34 alpha; at 1.5 they are 228/27. The port uses cached
N32 rasters and an identity SkSL colour filter to keep CPU sampling/blending in floating
point, matching the GPU pipeline without changing intermediate 8-bit quantization.
No blur, radius adjustment, overlap adjustment or comparison mask was introduced.

Primary implementation evidence:
- [QuadPerEdgeAA](https://skia.googlesource.com/skia.git/+/d2b9e48baf1697760afc1dc8ea3ad40110b8cacc/src/gpu/ganesh/ops/QuadPerEdgeAA.cpp)
- [Circle/ring/round-cap coverage](https://skia.googlesource.com/skia.git/+/d2b9e48baf1697760afc1dc8ea3ad40110b8cacc/src/gpu/ganesh/ops/GrOvalOpFactory.cpp)
- [Blink surface selection](https://raw.githubusercontent.com/chromium/chromium/main/third_party/blink/renderer/platform/graphics/compositing/property_tree_manager.cc)
- [EffectTree surface scale](https://raw.githubusercontent.com/chromium/chromium/main/cc/trees/property_tree.cc)
- [Compositor bilinear sampling](https://raw.githubusercontent.com/chromium/chromium/main/components/viz/service/display/skia_renderer.cc)
- [Skia float/lowp pipelines](https://raw.githubusercontent.com/google/skia/chrome/m119/src/opts/SkRasterPipeline_opts.h)

The fixture's actual surface selection is inferred from those rules and its DOM; the
independent all-pixel comparison is the acceptance evidence. Native capture and full-suite
results are in the T10 handoff, not implied by these drawing references.
