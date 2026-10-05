# Locked Aurora loader browser references

`render.py` executes the unmodified PAL, prep and LOADERS JavaScript from
`porting/first-release/design-src/loading-aurora/loading-aurora.html`. Loader 5 uses its
actual CSS and the verbatim `PLACES[1].sk` HTML. No C# drawing participates in the oracle.

72 frames cover loaders 2/4/5, both themes, scales 1/1.25/1.5/2 and times
0.6/0.84/1.2 seconds. The canvas backing DPR equals each sample's scale; a CSS transform
scales the sample box to the same pixel dimensions. For sheen this tests transformed CSS
layout at browser DPR 1, rather than four separate operating-system DPI settings.
Chrome's screenshot is cropped at the recorded integer sample coordinates, without
resampling. The backgrounds are opaque black/white. These are drawing references, not
complete application screenshots or proof of the 400 ms site delay.

The manifest binds the locked source, generator, browser version, inputs and every PNG
SHA256. Tests compare every pixel with a tolerance declared before the first comparison:
peak RGB channel error at most 24/255 and mean RGB channel error at most 0.5/255.
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
