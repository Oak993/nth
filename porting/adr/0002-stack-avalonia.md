# ADR-0002: The Windows stack is Avalonia, conditional on probe P1

Status: **DECIDED, CONDITIONAL.** Binding once P1 passes. If P1 fails, the decision flips to the
runner-up and this record is amended rather than rewritten.
Date: 2026-09-20. Authority: PORT_CONTRACT section 9; owner rulings OD-0003 and OD-0005.

## Decision

**Avalonia UI 12.1.2 on .NET 10 (C#)**, rendering through SkiaSharp 3.119.4 with HarfBuzzSharp
8.3.1.3 shaping and the Avalonia composition renderer.

- Glass (b), the 22 per-site materials: an app-owned two-pass compositor driving SKSL runtime
  effects (`SKRuntimeEffect`, `SKRuntimeEffectUniforms`, `SKRuntimeEffectChildren`) through
  `ISkiaSharpApiLeaseFeature`.
- Glass (a), the acrylic edge the owner chose: `Window.TransparencyLevelHint =
  WindowTransparencyLevel.AcrylicBlur`, per window.
- Win32 interop: P/Invoke plus `Win32Properties` (`AddWindowStylesCallback`,
  `AddWndProcHookCallback`, `SetNonClientHitTestResult`).
- Processes: ConPTY and Job Object in **one** `PROC_THREAD_ATTRIBUTE` list via hand-rolled
  `CreateProcessW`.
- Packaging and update: Velopack, with a hand-written check against the public GitLab Releases API.

**Runner-up: WinUI 3 / Windows App SDK** (C#/.NET 10, `SystemBackdropElement` + `AcrylicBrush`,
Win2D / ComputeSharp.D2D1.WinUI, unpackaged and self-contained).

## The reason it is Avalonia and not WinUI 3

The judge did not take either scorecard's word for where the 22 glass sites sit. It grepped the
derived specification and found them **predominantly over live app-rendered content**, not over a
static wallpaper layer:

- `TimelineMinimap.swift:306-344`, a preview card over the transcript
- `ComposerView.swift:1293-1307`, the composer surface over the chat
- `CommandPalette.swift:96-98`, over the whole window
- `RequestCards.swift:62-64`, `FollowUpQueueTab.swift:150-155`, `ThreadChangesTab.swift:36-44`
- `FloatingSidebarPanel.swift:87-102`, `UsageFloatingPanel.swift:53-63`, over panes
- `EffortSlider.swift:781-796`, over an app-drawn track

That is decisive. WinUI 3's own scorecard concedes that a Direct2D pixel shader cannot read
XAML-composed pixels and that `PixelShaderEffect` carries `[NoComposition]`;
`CompositionVisualSurface` keeps its result inside composition and `RenderTargetBitmap` is an async
one-shot, not a per-frame source. So on WinUI 3, full material over those sites means re-rendering
the transcript, composer and panes in immediate-mode Win2D: discarding the framework exactly where a
framework earns its keep, in text editing, IME, accessibility and virtualised lists, while still
paying the XAML dependency. A structural fork of unbounded blast radius.

Avalonia already renders everything through Skia, so one explicit two-pass compositor covers all 22
sites uniformly with no fork.

Two further parity points, both favouring Avalonia:

- `.interactive()` appears at `Chrome.swift:178`, on the EffortSlider knob and on ThreadChangesTab.
  `AcrylicBrush` has no pointer-tracked deformation, so on WinUI 3 those become flat
  approximations. A visible product deviation, which the contract ranks worse than developer effort.
- Avalonia can draw true `.continuous` squircle corners, because a superellipse SDF in SKSL
  reproduces them exactly. WinUI 3's `CornerRadius` is a circular arc at every one of the 22 radii.

## Three facts verified here, correcting both scorecards

1. **.NET SDK 10.0.401 is already installed** on this machine (`dotnet --list-sdks`), so Avalonia
   needs zero toolchain installs. The SDK was installed earlier in this session for probe work.
2. **Avalonia.Skia 12.1.2's nuspec declares a `net10.0` dependency group** pinning SkiaSharp
   3.119.4 and HarfBuzzSharp 8.3.1.3. The original eight-stack evaluation said 4.152.x, which was
   wrong. WinAppSDK's `net10.0-windows` support remains an unresolved probe by contrast.
3. **Symbol inspection of the shipped `net10.0` assemblies** confirms `ISkiaSharpApiLeaseFeature`,
   `ISkiaSharpApiLease`, `SkSurface`, `SkCanvas`, `GrContext` in `Avalonia.Skia.dll`, and
   `SKRuntimeEffect`, `SKRuntimeEffectUniforms`, `SKRuntimeEffectChildren`,
   `SKRuntimeShaderBuilder`, `SKRoundRect` in `SkiaSharp.dll`. Evidence from the binaries, not the
   documentation.

## Constraint 11, semantic translation

Avalonia is joint-best. One process, one UI thread: `Dispatcher.UIThread` plus
`AvaloniaSynchronizationContext` carry the source's 256 `@MainActor` annotations and its
`sessionEpoch` / `finishingTurnID` / `unsentSessionID` ordering rules as written, rather than by
re-expression. It also clears constraint 5's ordering problem by construction, because
`PROC_THREAD_ATTRIBUTE_JOB_LIST` sits beside `PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE` in one attribute
list; Electron needs a native helper for the same thing.

## Deviations this choice forces, all named

1. Avalonia's acrylic recipe is hardcoded (`WinUiCompositionUtils.CreateAcrylicBlurBackdropBrush`),
   so `DesktopAcrylicController`'s tint knobs are not exposed. The source's tint and its 0.26 dark /
   0.18 light scrim are drawn by the app as a layer over the system material.
2. The **outer** window corner is DWM's circular-arc corner preference, not a `.continuous`
   squircle. The 22 **inner** sites do not inherit this, because their geometry is app-drawn.
3. The glass subsystem depends on `[Unstable]` lease APIs with no cross-minor compatibility promise.
   The Avalonia version is pinned and every upgrade is gated behind a screenshot regression suite.
4. AvaloniaUI/Avalonia#21212: `WindowDecorations='BorderOnly'` with `ExtendClientAreaToDecorationsHint`
   strips the window styles DWM needs for minimize/maximize/restore animation and Snap Layouts,
   while `'Full'` draws OS caption glyphs over the app's own controls. The source has neither
   problem.
5. AppKit shapes the window shadow to the window's alpha; DWM does not for a
   `WS_EX_NOREDIRECTIONBITMAP` window, so the shadow is app-drawn inside reserved transparent
   margins, and every frame and hit-test constant shifts accordingly.
6. Skia is not CoreText: glyph rasterisation, hinting and subpixel behaviour differ. Under OD-0001
   this is unverifiable for every candidate, so it costs nothing relative to the field, but it is a
   named deviation here as anywhere.
7. Neither .NET nor Avalonia ships an audio API, so the runtime-synthesised chimes need NAudio over
   WASAPI or XAudio2 P/Invoke. An added dependency, not a behaviour change.
8. Platform ceilings identical across all four contenders, recorded for completeness rather than as
   discriminators: `NSPanel [.nonactivatingPanel]` semantics have no Windows equivalent;
   `kSecAttrAccessibleWhenUnlocked` maps to DPAPI CurrentUser or Credential Manager with different
   lock-state behaviour.

## Why this record is conditional

Probe **P1** is the whole basis for preferring Avalonia over WinUI 3: an app-owned two-pass glass
compositor over live app content, through `ISkiaSharpApiLease`. If P1 fails, neither stack can
shader over live content, and WinUI 3's cleaner packaging story and stronger `OverlappedPresenter`
window API decide it instead. P1 runs before any production code.

Eleven probes were specified with concrete pass conditions; they are listed in
`porting/critiques/stack_decision.json` and tracked in `porting/probes/`. The most load-bearing
after P1 are P2 (snapshot budget at 22 sites on this integrated GPU), P4 (live resize with acrylic
and extended client area together, the least-verified constraint on this stack) and P5 (ConPTY and
Job Object in one attribute list).

## Open question this choice elevates

UNK-0023 becomes the highest-value open question. If the app always draws its own backdrop, every
one of the 22 sites gets the full shader material and the acrylic becomes purely the window edge the
owner named. If it does not, sites over a bare transparent window degrade to tint-over-acrylic with
no lensing. That is a product decision, not an engineering one, and it is not assumed here.

---

## P1 RESULT: PASS (2026-09-20)

The condition this record was blocked on is met. `porting/probes/p1-glass/` on Avalonia 12.1.2 /
.NET 10, rendered headless through `RenderTargetBitmap` so it runs on this locked session.

| Condition | Result |
| --- | --- |
| `ISkiaSharpApiLeaseFeature` obtainable from the drawing context | **True** |
| App-owned `SKSurface` created and drawn into (not a snapshot of the ambient surface) | **True** |
| `SKRuntimeEffect` compiles and runs with the backdrop bound as a shader child | **True** |
| The effect is edge-concentrated, i.e. it is lensing and not a flat overlay | **True** |

The fourth condition needed its test redesigned before it meant anything. Comparing the glassed
render against the plain one showed 99.9% of panel pixels differing and only a 1.4x edge-to-interior
ratio, which reads like a failure but is not: the 0.30 tint applies across the whole panel by
design, so it swamps the displacement signal. The probe now renders a third control image with the
same shader at `strength = 0` and `rimGain = 0`, which isolates the displacement:

- edge band, under 46 px from the border: mean 60.9/765, 19.8% of pixels differing by more than 30
- interior, 120 px or further in: mean **0.0/765**, exactly zero
- edge/interior ratio: **6089x**

Zero in the interior is the right answer, not a suspicious one: the shader's displacement term is
gated on `lens = pow(clamp(1 - d/band, 0, 1), 2.2)`, which is identically zero beyond the band.

Visually confirmed in `porting/probes/out/p1_glass.png`: the high-contrast bands compress and bend
where they cross the panel's top edge, the transcript text is visibly squeezed and refracted along
the bottom border, a specular rim runs along the lit edge, and the corners are superellipse
`.continuous`, not circular arcs.

**Consequence:** ADR-0002 is no longer conditional. The stack is Avalonia. The reason it beat
WinUI 3 was that most of the 22 glass sites sit over live app-rendered content rather than a static
wallpaper, and this probe demonstrates the mechanism working over exactly that.

What P1 does NOT establish, recorded so it is not over-read: it says nothing about frame budget at
22 sites on this integrated GPU (P2), nothing about live resize with acrylic and extended client
area together (P4, the least-verified constraint on this stack), and nothing about whether the
result matches macOS, which OD-0001 leaves permanently unverifiable. It proves the mechanism exists
and is reachable, which is what the decision turned on.
