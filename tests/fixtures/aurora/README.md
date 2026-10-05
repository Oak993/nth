# T10 READY renderer regression

`chrome/` is imported unchanged from `origin/codex/aurora-render` at
`182634dcc38c0574c560a3b682b51e1b1235abbb`. The renderer's own tests and READY report
remain on its branch. `AuroraReadyRendererTests` independently reruns all 210 frames
on T10's byte-identical production renderer, checking source, inputs and every PNG hash.

T10 applies the lead's current bound to every pixel: premultiplied RGBA peak at most
24/255 and mean at most 0.5/255 **for each channel independently**. Straight RGBA
numbers remain diagnostic. All 210 frames pass. The oracle manifest's older strict
2/255 value is retained unchanged as provenance; it does not replace the current bound.

This proves the renderer on N32/BGRA CPU canvases. The native boot tests separately
check real window geometry, cloaking, warm-up, hand-over and frame timing. GPU pixel
readback is not asserted by this CPU reference comparison.
