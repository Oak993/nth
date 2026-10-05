# Independent AURORA-RENDER review, 2026-10-05

The independent reviewer used standalone Python calls to SkiaSharp's C ABI,
plus a Skia 144 probe, outside the repository. No production or locked source
was changed by that reviewer. The source formulas, seeded RNG, timing, bands,
blur scratch pooling and color interpolation were reviewed against IconKit/D.

Findings and resolutions:

- Native N32/BGRA canvas storage selects Chrome-equivalent raster rounding;
  RGBA storage selects different Skia 119 gradient/image-alpha blitters.
- Skia 119's `SkScan_AntiPath.cpp::ShouldUseAAA` chooses the legacy 4x4 scanner
  for small point-dense paths; newer Skia always uses analytic AA. A zero-edge
  move, or an offscreen closed zero-length contour before stroking, bypasses
  that heuristic without adding visible geometry. The original device bounds,
  rounded outward, retain the confinement supplied by Chrome's small mask
  blitter. Comparisons include every pixel; there is no test mask.
- Independent C-ABI probes passed all 90 small fixtures with these adaptations:
  16 peak 7/mean .253906/99.609375% within 4;
  24 peak 5/mean .256944/99.652778% within 4;
  32 peak 6/mean .169434/99.609375% within 4.
- Independent medium probes confirmed the filled-stroke-outline correction
  removes the 64px rim defect, passing all 60 medium fixtures.
- Rotation/skew bounds must transform the actual path controls, not only the
  original enclosing rectangle. Production now reuses `deviceBoundsPath`.
- Hairline classification must use SkDraw's conservative `fast_len` on both
  transformed width vectors and reject perspective. Production now does so.
- Warm-up must include both a still and early launch opacity. Production draws
  Still and Launch(.3 seconds), whose tile/image alpha is 249/255.
- The native save/restore/clip signatures, bool I1 marshaling, matrix44 ABI,
  shader/filter/snapshot ownership, reusable paths and disposal were checked.
  No unresolved ABI/lifetime/allocation issue was identified.
- PNG export is decoded to straight RGBA symmetrically for the strict
  diagnostic; nearest-rounded 8-bit premultiplied RGBA is explicit for acceptance,
  with all four channels and all pixels retained.

Final production measurements and checks are recorded in ../parity-summary.json,
../validation-build.txt and ../validation-tests.txt. Windows GPU compilation and
native boot behavior remain T10's integration verification.
