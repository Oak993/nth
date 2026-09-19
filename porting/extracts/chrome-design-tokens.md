# Chrome.swift design tokens, verbatim

Pinned reference: `E:/Droppy/reference/droppy-code`, commit `dd2b86efef9e77168f304a86e8a8411877578bbe` (read-only; nothing under it was modified).

Files read completely, reconciled against `wc -l`:

| File | Lines (`wc -l`) | Lines read |
| --- | --- | --- |
| `DroppyCode/UI/Chrome/Chrome.swift` | 1107 | 1-1107 |
| `DroppyCode/UI/Chrome/WindowChrome.swift` | 571 | 1-571 |
| `DroppyCode/UI/Theme/ShortcutTint.swift` | 12 | 1-12 |

Two supporting definitions are referenced by `Chrome.swift` but declared elsewhere; both are
reproduced verbatim in section 15, because the capsule/circle surface and every theme colour cannot
be ported without them: `DroppyCode/UI/Common/RenderBudget.swift` lines 79-100 (`isOnGlassPanel`,
`Chrome.panelControlFill`) and `DroppyCode/UI/Theme/AppTheme.swift` lines 202-233 (`ThemeSpec`,
`ThemeManager.spec`).

Labels: **SOURCE FACT** (read off the code), **ENGINEERING DECISION** (a porting judgement),
**UNRESOLVED** (not settled by these files).

---

## 1. `enum Chrome` member census

**SOURCE FACT.** `enum Chrome` spans `Chrome.swift` lines 5-118. It has **57 members**, counted:

- **43** `static let` (one of which is `private static let systemAccent`)
- **13** `static var` (all computed; there are no stored static vars)
- **1** `static func` (`overlay(_:)`)

Full member list in declaration order, 57 entries:

1. `windowCornerRadius`
2. `sheetInset`
3. `sheetCornerRadius`
4. `trafficLightDiameter`
5. `trafficLightSpacing`
6. `trafficLightPitch`
7. `trafficLightLeading`
8. `trafficLightTop`
9. `trafficLightsWidth`
10. `trafficLightClearance`
11. `iconFont`
12. `inlineIconFont`
13. `chevronFont`
14. `chromeHorizontalPadding`
15. `chromeTopPadding`
16. `capsuleContentHeight`
17. `capsuleVerticalPadding`
18. `capsuleHorizontalPadding`
19. `iconCapsuleInnerPadding`
20. `dividerHeight`
21. `capsuleHeight`
22. `contentBreathBelowChrome`
23. `contentTopInset`
24. `veilHeight`
25. `titleStartOffset`
26. `titleEndOffset`
27. `progressQuantum`
28. `rowHeight`
29. `rowCornerRadius`
30. `rowHorizontalPadding`
31. `iconSize`
32. `symbolSize`
33. `groupGap`
34. `listInset`
35. `cardCornerRadius`
36. `sectionSpacing`
37. `sectionHeaderSpacing`
38. `contentHorizontalPadding`
39. `rowControlTrailingPadding`
40. `rowButtonLabelHeight`
41. `overlay(_:)`
42. `primaryText`
43. `secondaryText`
44. `systemAccent` (private)
45. `accent`
46. `accentNSColor`
47. `success`
48. `warning`
49. `danger`
50. `glassTint`
51. `hover`
52. `panelSlide`
53. `glideSpring`
54. `settleFlight`
55. `gray`
56. `blue`
57. `orange`

The enum's doc comment, verbatim (Chrome.swift:4-5):

```swift
/// Tokens and building blocks shared with Droppy's settings panel, so the two apps read as one family.
enum Chrome {
```

Top-level declarations in `Chrome.swift`, **38 in total**, with their line numbers:
`enum Chrome` (5), `extension View` (123), `ChromeGlassSurface` (151, private), `DetailSheetModifier`
(185, private), `WindowBackdrop` (200), `ChromeCapsule` (275), `ChromeDivider` (291),
`ChromeIconLabel` (300), `ChromeIconButton` (328), `ChromeMenuButton` (352), `ChromeCircleButton`
(380), `ChromeCircleMenu` (407), `ChromeTextMenu` (440), `ChromeTextButton` (485),
`ChromeScrollModel` (535), `PaneTopVeil` (548), `ChromeCompactTitle` (598), `SidebarSymbol` (618),
`SidebarIconBadge` (638), `SidebarRow` (648), `extension SidebarRow where Accessory == EmptyView`
(715), `RowAccessoryIcon` (726), `SidebarSearchField` (742), `ChromeSearchField` (794),
`GlassPickerButton` (839), `SoftAppearModifier` (904), `extension AnyTransition` (914),
`extension Animation` (925), `SettleGlideModifier` (934, private), `extension AnyTransition` (957),
`ChromeCard` (973), `ChromeSection` (990), `ChromeRow` (1016), `ChromeRowDivider` (1043),
`WindowDragArea` (1053), `WindowDragView` (1061), `NoWindowDragArea` (1081), `NoWindowDragView` (1089).

---

## 2. `// MARK: Window` — 10 tokens (Chrome.swift:7-22)

Exact declarations, in order:

```swift
    static let windowCornerRadius: CGFloat = 26
    /// The detail sheet runs to the window's edge: inset, it drew a second, tighter corner
    /// inside the window's own, a clipped double border wherever the sidebar left it bare.
    static let sheetInset: CGFloat = 0
    /// The rounding of surfaces that sit on the sheet, like the terminal's top corners.
    static let sheetCornerRadius: CGFloat = 16

    static let trafficLightDiameter: CGFloat = 14
    static let trafficLightSpacing: CGFloat = 9
    static var trafficLightPitch: CGFloat { trafficLightDiameter + trafficLightSpacing }
    static let trafficLightLeading: CGFloat = 18
    static let trafficLightTop: CGFloat = 16
    static var trafficLightsWidth: CGFloat { trafficLightDiameter * 3 + trafficLightSpacing * 2 }
    static let trafficLightClearance: CGFloat = 10
```

Resolved values (**SOURCE FACT**, by substitution):

| Token | Declaration | Value |
| --- | --- | --- |
| `windowCornerRadius` | literal | `26` |
| `sheetInset` | literal | `0` |
| `sheetCornerRadius` | literal | `16` |
| `trafficLightDiameter` | literal | `14` |
| `trafficLightSpacing` | literal | `9` |
| `trafficLightPitch` | `trafficLightDiameter + trafficLightSpacing` | `23` |
| `trafficLightLeading` | literal | `18` |
| `trafficLightTop` | literal | `16` |
| `trafficLightsWidth` | `trafficLightDiameter * 3 + trafficLightSpacing * 2` | `60` |
| `trafficLightClearance` | literal | `10` |

**SOURCE FACT.** `sheetInset` is `0`, so every expression that adds it (`WindowChrome.titlebarHeight`,
`WindowChrome.collapsedTrafficLightTop`, the collapsed traffic-light leading) collapses to the other
terms. The token is still read in 8 places repo-wide; the comment above it records that a nonzero
inset drew a clipped double border. **ENGINEERING DECISION:** keep the token in the port as a real
constant equal to `0` rather than inlining it, so the arithmetic stays comparable to upstream.

---

## 3. `// MARK: Sticky chrome row` — 17 tokens (Chrome.swift:24-42)

Exact declarations, in order, including the shared doc comment:

```swift
    /// One weight and size for every symbol that is a control on its own: chrome buttons,
    /// the composer's paperclip and send, the terminal strip. Symbols beside text step down
    /// a point so they sit level with the type; menu chevrons are small and bold.
    static let iconFont: Font = .system(size: 12, weight: .semibold)
    static let inlineIconFont: Font = .system(size: 11, weight: .semibold)
    static let chevronFont: Font = .system(size: 8, weight: .bold)
    static let chromeHorizontalPadding: CGFloat = 14
    static let chromeTopPadding: CGFloat = 12
    static let capsuleContentHeight: CGFloat = 28
    static let capsuleVerticalPadding: CGFloat = 2
    static let capsuleHorizontalPadding: CGFloat = 12
    static let iconCapsuleInnerPadding: CGFloat = 3
    static let dividerHeight: CGFloat = 14
    static var capsuleHeight: CGFloat { capsuleContentHeight + capsuleVerticalPadding * 2 }
    static let contentBreathBelowChrome: CGFloat = 28
    static var contentTopInset: CGFloat { chromeTopPadding + capsuleHeight + contentBreathBelowChrome }
    static let veilHeight: CGFloat = 72
    static let titleStartOffset: CGFloat = 10
    static let titleEndOffset: CGFloat = 44
    static let progressQuantum: CGFloat = 0.04
```

Resolved values (**SOURCE FACT**):

| Token | Declaration | Value |
| --- | --- | --- |
| `iconFont` | `.system(size: 12, weight: .semibold)` | size 12, semibold |
| `inlineIconFont` | `.system(size: 11, weight: .semibold)` | size 11, semibold |
| `chevronFont` | `.system(size: 8, weight: .bold)` | size 8, bold |
| `chromeHorizontalPadding` | literal | `14` |
| `chromeTopPadding` | literal | `12` |
| `capsuleContentHeight` | literal | `28` |
| `capsuleVerticalPadding` | literal | `2` |
| `capsuleHorizontalPadding` | literal | `12` |
| `iconCapsuleInnerPadding` | literal | `3` |
| `dividerHeight` | literal | `14` |
| `capsuleHeight` | `capsuleContentHeight + capsuleVerticalPadding * 2` | `32` |
| `contentBreathBelowChrome` | literal | `28` |
| `contentTopInset` | `chromeTopPadding + capsuleHeight + contentBreathBelowChrome` | `72` |
| `veilHeight` | literal | `72` |
| `titleStartOffset` | literal | `10` |
| `titleEndOffset` | literal | `44` |
| `progressQuantum` | literal | `0.04` |

**SOURCE FACT.** `contentTopInset` (72) and `veilHeight` (72) are numerically equal but are
independent declarations; do not fold them into one constant.

**Ordering dependency.** `capsuleHeight` must be resolvable before `contentTopInset`,
`WindowChrome.titlebarHeight` and `WindowChrome.collapsedTrafficLightTop`. In Swift these are lazy
computed properties, so file order does not matter; in a port with eager constant evaluation
(a TypeScript module, a CSS custom-property cascade) the declaration order does.

---

## 4. `// MARK: Sidebar` — 7 tokens (Chrome.swift:44-52)

```swift
    static let rowHeight: CGFloat = 28
    static let rowCornerRadius: CGFloat = 7
    static let rowHorizontalPadding: CGFloat = 8
    static let iconSize: CGFloat = 20
    static let symbolSize: CGFloat = 14
    static let groupGap: CGFloat = 10
    static let listInset: CGFloat = 10
```

All seven are literals: `28`, `7`, `8`, `20`, `14`, `10`, `10`.

---

## 5. `// MARK: Cards` — 6 tokens (Chrome.swift:54-67)

```swift
    static let cardCornerRadius: CGFloat = 16
    static let sectionSpacing: CGFloat = 20
    static let sectionHeaderSpacing: CGFloat = 10
    static let contentHorizontalPadding: CGFloat = 16
    /// The gap between a row's trailing control and the card edge.
    static let rowControlTrailingPadding: CGFloat = 10
    /// The height a settings row's trailing button label is held to. A monospaced `> agy`
    /// copy button drew a taller capsule than the plain "Sign in" beside it, so the two
    /// read as two sizes; one clamped label height keeps the controls in a row even.
    static let rowButtonLabelHeight: CGFloat = 16
```

All six are literals: `16`, `20`, `10`, `16`, `10`, `16`.

The `rowButtonLabelHeight` comment quotes two exact user-visible strings that shaped the token:
`> agy` (a monospaced copy button) and `Sign in`.

---

## 6. `// MARK: Color and motion` — 17 members (Chrome.swift:69-117)

Exact declarations, in order, with their comments:

```swift
    static func overlay(_ opacity: Double) -> Color {
        primaryText.opacity(opacity)
    }

    // Dynamic system colors resolve at draw time, so one wrapped value serves every appearance.
    static let primaryText = Color(nsColor: .labelColor)
    static let secondaryText = Color(nsColor: .secondaryLabelColor)
    private static let systemAccent = Color(nsColor: .controlAccentColor)

    /// The theme's accent, or the system control accent for System/Light/Dark.
    /// The window root also applies it as the view tint, so prominent glass
    /// buttons, toggles and progress indicators follow the theme.
    static var accent: Color { ThemeManager.spec.accent ?? systemAccent }
    /// The same accent for AppKit: text view link attributes and layer colours.
    static var accentNSColor: NSColor { ThemeManager.spec.accent.map { NSColor($0) } ?? .controlAccentColor }

    /// Status hues: system green/orange/red for System/Light/Dark, the
    /// palette's own hues for every named theme.
    static var success: Color { ThemeManager.spec.success }
    static var warning: Color { ThemeManager.spec.warning }
    static var danger: Color { ThemeManager.spec.danger }

    /// The theme's glass tint, washed over capsules, sheets and the window.
    static var glassTint: Color { ThemeManager.spec.surface }

    static var hover: Animation { .easeOut(duration: 0.1) }

    static var panelSlide: Animation {
        NSWorkspace.shared.accessibilityDisplayShouldReduceMotion
            ? .easeOut(duration: 0.18)
            : .spring(response: 0.32, dampingFraction: 0.9)
    }

    /// The motion of a settling row's glide and of the rows making room for it, so they
    /// land together.
    static let glideSpring = Spring(response: 0.6, dampingRatio: 0.86)

    /// The rows making room as a thread settles to the bottom of the sidebar or comes back
    /// up: the glide's own spring, so the ghost and the rows land together.
    static var settleFlight: Animation {
        NSWorkspace.shared.accessibilityDisplayShouldReduceMotion
            ? .easeOut(duration: 0.2)
            : .spring(glideSpring)
    }

    static let gray = Color(red: 0.556, green: 0.557, blue: 0.576)
    static let blue = Color(red: 0.040, green: 0.478, blue: 1.000)
    static let orange = Color(red: 1.000, green: 0.584, blue: 0.000)
```

### 6.1 How each colour resolves

| Token | Resolution | Note (**SOURCE FACT**) |
| --- | --- | --- |
| `overlay(o)` | `primaryText.opacity(o)` | A function, not a constant. Every "overlay" fill in the family is `labelColor` at an opacity. |
| `primaryText` | `Color(nsColor: .labelColor)` | AppKit dynamic colour; resolves per appearance at draw time. |
| `secondaryText` | `Color(nsColor: .secondaryLabelColor)` | Same. |
| `systemAccent` | `Color(nsColor: .controlAccentColor)` | `private`; read only by `accent`. |
| `accent` | `ThemeManager.spec.accent ?? systemAccent` | `ThemeSpec.accent` is `Color?`; nil for System/Light/Dark. |
| `accentNSColor` | `ThemeManager.spec.accent.map { NSColor($0) } ?? .controlAccentColor` | The AppKit twin; the fallback is `.controlAccentColor` directly, not `NSColor(systemAccent)`. |
| `success` | `ThemeManager.spec.success` | Non-optional on the spec. |
| `warning` | `ThemeManager.spec.warning` | Non-optional. |
| `danger` | `ThemeManager.spec.danger` | Non-optional. |
| `glassTint` | `ThemeManager.spec.surface` | The single tint washed over capsules, the detail sheet, the window backdrop and the veil. |
| `gray` | `Color(red: 0.556, green: 0.557, blue: 0.576)` | Fixed literal; no theme, no dark variant. |
| `blue` | `Color(red: 0.040, green: 0.478, blue: 1.000)` | Fixed literal. |
| `orange` | `Color(red: 1.000, green: 0.584, blue: 0.000)` | Fixed literal. |

**SOURCE FACT.** `Color(red:green:blue:)` with no `opacity:` defaults to opacity 1 and, in SwiftUI,
to the sRGB colour space. The three literals carry three decimal places exactly as written; `0.040`
and `1.000` are numerically `0.04` and `1`.

The three fixed hues in 8-bit sRGB, rounded (**ENGINEERING DECISION**, for a CSS port):
`gray` = `rgb(142, 142, 147)` = `#8E8E93`; `blue` = `rgb(10, 122, 255)` = `#0A7AFF`;
`orange` = `rgb(255, 149, 0)` = `#FF9500`. Keep the float triples as the source of truth.

**SOURCE FACT.** Status hues, the accent and the glass tint are *not* constants: they re-read
`ThemeManager.spec` on every access. `ThemeManager.spec` is
`nonisolated(unsafe) private(set) static var spec: ThemeSpec = AppTheme.system.spec`, rebuilt once
per theme change in `ThemeManager.current.didSet`, which also posts
`Notification.Name("ThemeManager.didChange")` for layer-backed views that cache colours.

### 6.2 Motion tokens, exactly

| Token | Reduce Motion ON | Reduce Motion OFF |
| --- | --- | --- |
| `Chrome.hover` | `.easeOut(duration: 0.1)` (no branch) | `.easeOut(duration: 0.1)` |
| `Chrome.panelSlide` | `.easeOut(duration: 0.18)` | `.spring(response: 0.32, dampingFraction: 0.9)` |
| `Chrome.glideSpring` | `Spring(response: 0.6, dampingRatio: 0.86)` — a `Spring`, not an `Animation` | same |
| `Chrome.settleFlight` | `.easeOut(duration: 0.2)` | `.spring(glideSpring)` |
| `Animation.softAppear` (Chrome.swift:927) | `.smooth(duration: 0.32)` (no branch) | same |
| `AnyTransition.softAppear` removal | `.opacity.animation(.easeOut(duration: 0.12))` | same |
| `AnyTransition.settleGlide` removal | `.animation(.spring(Chrome.glideSpring))` | same |
| `WindowBackdrop` wallpaper crossfade | `.easeInOut(duration: 0.3)` (no branch) | same |
| `WindowChrome.placeTrafficLights` | `context.duration = 0.18`, `CAMediaTimingFunction(name: .easeOut)` | `context.duration = 0.32`, same ease-out |
| `SidebarLayout.slideRun` | `220` ms | `420` ms |
| `SidebarResizeHandleView.Grip.duration` | `0.18` (no branch) | same |
| `SettleGlideModifier` fade curve | linear `progress` | `smoothstep(0.45, 0.92, progress)` |

The Reduce Motion predicate used everywhere is verbatim:

```swift
NSWorkspace.shared.accessibilityDisplayShouldReduceMotion
```

It appears **4 times** in these files: `Chrome.panelSlide`, `Chrome.settleFlight`,
`SettleGlideModifier.body`, `WindowChrome.placeTrafficLights`, plus once more in
`SidebarLayout.slideRun` — **5 occurrences in total** across `Chrome.swift` (3) and
`WindowChrome.swift` (2).

---

## 7. Surface modifiers (Chrome.swift:121-198)

### 7.1 `extension View` entry points, verbatim

```swift
    /// Capsule buttons stay Liquid Glass, washed with a tad of the theme. On a glass panel
    /// they draw flat instead (see `isOnGlassPanel`): glass on glass samples the same
    /// pixels twice, once for the panel and once for the button.
    func chromeGlassCapsule() -> some View {
        modifier(ChromeGlassSurface(shape: .capsule))
    }

    /// The capsule for a field rather than a button: the same glass, without the press
    /// response. A field is clicked to place the caret, and interactive glass answered
    /// the click with its press bounce at the same moment the field took focus, so the
    /// capsule flickered on every tap.
    func chromeGlassFieldCapsule() -> some View {
        modifier(ChromeGlassSurface(shape: .capsule, isInteractive: false))
    }

    func chromeGlassCircle() -> some View {
        modifier(ChromeGlassSurface(shape: .circle))
    }

    /// The inset content sheet the detail pane floats on.
    func detailSheet() -> some View {
        modifier(DetailSheetModifier())
    }
```

### 7.2 `ChromeGlassSurface` (private, Chrome.swift:151-183)

```swift
private struct ChromeGlassSurface: ViewModifier {
    enum Kind { case capsule, circle }
    let shape: Kind
    /// Whether the glass answers a press; off for a field that takes the caret instead.
    var isInteractive = true
    @Environment(\.isOnGlassPanel) private var isOnGlassPanel
    @Environment(\.colorScheme) private var colorScheme
    @State private var isHovered = false

    func body(content: Content) -> some View {
        switch shape {
        case .capsule:
            surface(content, in: Capsule(style: .continuous))
        case .circle:
            surface(content, in: Circle())
        }
    }

    @ViewBuilder
    private func surface<S: InsettableShape>(_ content: Content, in shape: S) -> some View {
        if isOnGlassPanel {
            content
                .background(shape.fill(Chrome.panelControlFill(isDark: colorScheme == .dark, hovered: isHovered)))
                .contentShape(shape)
                .onHover { isHovered = $0 }
                .animation(Chrome.hover, value: isHovered)
        } else if isInteractive {
            content.glassEffect(.regular.tint(Chrome.glassTint.opacity(0.3)).interactive(), in: shape)
        } else {
            content.glassEffect(.regular.tint(Chrome.glassTint.opacity(0.3)), in: shape)
        }
    }
}
```

**Tokens here (SOURCE FACT):**

- Glass tint opacity for *both* glass branches: `0.3` (`Chrome.glassTint.opacity(0.3)`).
- Capsule shape is always `Capsule(style: .continuous)`; circle is a plain `Circle()`.
- The flat-panel branch animates hover with `Chrome.hover` (`.easeOut(duration: 0.1)`).
- `Kind` has exactly **2** cases: `.capsule`, `.circle`.
- Default `isInteractive = true`.

**Conditional inclusion, exact precedence.** `isOnGlassPanel` wins over `isInteractive`. On a glass
panel a field and a button get the *same* flat fill and the same hover brightening; `isInteractive`
is consulted only off-panel.

**SOURCE FACT / upstream quirk to reproduce.** Off a glass panel, neither glass branch has an
`onHover`; `isHovered` is written only in the on-panel branch. Hover response off-panel comes from
AppKit's `.interactive()` glass, so the non-interactive field capsule has **no hover response at all**.

### 7.3 `DetailSheetModifier` (private, Chrome.swift:185-198)

```swift
private struct DetailSheetModifier: ViewModifier {
    @Environment(\.colorScheme) private var colorScheme

    func body(content: Content) -> some View {
        // Square: the window's own clip rounds the sheet's outer corners with everything
        // else, so the sheet never draws a corner of its own inside the window's.
        content
            .background {
                Rectangle().fill(colorScheme == .dark ? Color.black.opacity(0.22) : Color.white.opacity(0.3))
                Rectangle().fill(Chrome.glassTint.opacity(colorScheme == .dark ? 0.22 : 0.16))
            }
    }
}
```

**Tokens (SOURCE FACT).** Two stacked rectangles in this order (base first, tint over it):

| Layer | Dark | Light |
| --- | --- | --- |
| base `Rectangle` | `Color.black.opacity(0.22)` | `Color.white.opacity(0.3)` |
| tint `Rectangle` | `Chrome.glassTint.opacity(0.22)` | `Chrome.glassTint.opacity(0.16)` |

Shape is a plain `Rectangle()` (square corners) by design. `Chrome.sheetCornerRadius` (16) is **not**
used here; it is for surfaces sitting *on* the sheet, e.g. the terminal's top corners.

---

## 8. `WindowBackdrop` (Chrome.swift:200-273)

### 8.1 The scrim formula, verbatim

```swift
    /// The scrim over the glass for a backdrop setting: the stock scrim at the
    /// midpoint, none at 0, a solid base at 1, linear either side.
    static func scrim(for opacity: Double, isDark: Bool) -> Double {
        let stock = isDark ? 0.26 : 0.18
        let t = min(max(opacity, 0), 1)
        return t <= 0.5 ? stock * (t / 0.5) : stock + (1 - stock) * ((t - 0.5) / 0.5)
    }
```

**SOURCE FACT, exact computation:**
- `stock` = `0.26` dark, `0.18` light.
- `t` = `opacity` clamped to `[0, 1]`.
- For `t <= 0.5`: `stock * (t / 0.5)` — linear 0 → stock.
- For `t > 0.5`: `stock + (1 - stock) * ((t - 0.5) / 0.5)` — linear stock → 1.
- The branch boundary is `t <= 0.5` (the midpoint belongs to the lower branch; both give `stock`).

### 8.2 Declared properties

```swift
struct WindowBackdrop: View {
    @Environment(\.colorScheme) private var colorScheme
    /// The backdrop setting, 0 for bare glass and 1 for a solid base.
    let opacity: Double
    /// The user's wallpaper, painted edge to edge at the transparency setting's own opacity; nil for the stock glass.
    var wallpaper: NSImage? = nil
```

### 8.3 Other literals and the layer order

```swift
        let shape = RoundedRectangle(cornerRadius: Chrome.windowCornerRadius, style: .continuous)
        let isDark = colorScheme == .dark
        let isSolid = opacity >= 0.98
```

- Window shape: `RoundedRectangle(cornerRadius: 26, style: .continuous)`.
- **Solid threshold: `opacity >= 0.98`.** At or above it the glass is replaced by a plain fill
  (`shape.fill(isDark ? Color.black : Color.white)`), and in the wallpaper branch the overlay glass
  layer is dropped.
- Wallpaper branch, verbatim image chain:
  `Image(nsImage: wallpaper).resizable().interpolation(.high).aspectRatio(contentMode: .fill).opacity(opacity)`,
  over a `shape.fill(.clear)` base, `.clipShape(shape)`, then `if !isSolid { shape.fill(.clear).glassEffect(in: shape) }`, then `.transition(.opacity)`.
  **No scrim in this branch** — the comment states the picture *is* the base colour.
- Glass branch, verbatim: `shape.fill(.clear).glassEffect(in: shape).overlay { shape.fill(isDark ? Color.black : Color.white).opacity(Self.scrim(for: opacity, isDark: isDark)) }`.
- Crossfade: `.animation(.easeInOut(duration: 0.3), value: wallpaper != nil)`.
- Legibility tint overlay, applied in every branch: `shape.fill(Chrome.glassTint.opacity(0.12))`.
- Hairline, applied in every branch, after the tint:
  `shape.strokeBorder(Chrome.overlay(0.14), lineWidth: 1)`.

**Ordering dependency (SOURCE FACT).** The layer order is fixed and load-bearing:
1. the `Group` (wallpaper / solid / glass+scrim),
2. `.overlay { shape.fill(Chrome.glassTint.opacity(0.12)) }`,
3. `.overlay { shape.strokeBorder(Chrome.overlay(0.14), lineWidth: 1) }`.

The tint at `0.12` is applied on top of a solid fill too, so a "solid" window is not pure
black/white: it is black/white with 12% of the theme surface over it and a 14%-`labelColor` hairline.

**Branch selection, exactly three, in this order:** `if let wallpaper` → `else if isSolid` →
`else` (glass). A wallpaper wins over the solid branch.

---

## 9. Control components — every inline literal

### 9.1 `ChromeCapsule` (Chrome.swift:275-289)

```swift
        HStack(spacing: 0) {
            content
        }
        .padding(.horizontal, Chrome.iconCapsuleInnerPadding)   // 3
        .padding(.vertical, Chrome.capsuleVerticalPadding)      // 2
        .chromeGlassCapsule()
        // The capsule is a control wherever it sits: presses on it work the control and
        // never drag the window.
        .background { NoWindowDragArea() }
```

`HStack(spacing: 0)`. Horizontal padding is `iconCapsuleInnerPadding` (**3**), **not**
`capsuleHorizontalPadding` (12) — that token is for *text* capsules.

### 9.2 `ChromeDivider` (Chrome.swift:291-298)

```swift
        RoundedRectangle(cornerRadius: 0.5, style: .continuous)
            .fill(Chrome.primaryText.opacity(0.22))
            .frame(width: 1, height: Chrome.dividerHeight)
            .accessibilityHidden(true)
```

Corner radius `0.5`, fill `labelColor @ 0.22`, size `1 x 14`.

### 9.3 `ChromeIconLabel` (Chrome.swift:300-326)

```swift
        Image(systemName: symbol)
            .font(Chrome.iconFont)                                     // size 12, semibold
            .foregroundStyle(foreground)
            .frame(width: Chrome.capsuleContentHeight, height: Chrome.capsuleContentHeight)   // 28 x 28
            .background {
                if isActive {
                    Circle()
                        .fill(Chrome.overlay(0.12))
                        .padding(1)
                }
            }
            .contentShape(.rect)
```

```swift
    private var foreground: Color {
        guard isEnabled else { return Chrome.primaryText.opacity(0.32) }
        if isActive { return Chrome.primaryText }
        return Chrome.primaryText.opacity(isHovering ? 1 : 0.92)
    }
```

**Foreground ladder (SOURCE FACT), checked in this order, 4 outcomes:** disabled `0.32` → active
`Chrome.primaryText` (full, no `.opacity` call) → hovering `1` → rest `0.92`.
Active background: `Circle().fill(Chrome.overlay(0.12)).padding(1)` inside a 28x28 frame, so the
circle is effectively **26pt**.
Defaults: `var isEnabled = true`, `var isActive = false`, `var isHovering = false`.

### 9.4 `ChromeIconButton` (Chrome.swift:328-350)

`.buttonStyle(.plain)`, `.disabled(!isEnabled)`, hover wrapped in `withAnimation(Chrome.hover)`,
`.help(help)`, `.accessibilityLabel(Text(help))`, `.background { NoWindowDragArea() }`.
No literals of its own. Defaults `isEnabled = true`, `isActive = false`.

### 9.5 `ChromeMenuButton` (Chrome.swift:352-378)

Same as `ChromeIconButton` plus
`.popover(isPresented: $isPresented, arrowEdge: .bottom) { PopoverMenu { content } }`.
The label is `ChromeIconLabel(symbol: symbol, isActive: isPresented, isHovering: isHovering)` — the
popover being open *is* the active state. It has no `isEnabled`.

### 9.6 `ChromeCircleButton` (Chrome.swift:380-405)

Doc comment: `/// A round glass button the height of a chrome capsule.`

```swift
            Image(systemName: symbol)
                .font(Chrome.iconFont)
                .foregroundStyle(Chrome.primaryText.opacity(isHovering ? 1 : 0.92))
                .frame(width: Chrome.capsuleHeight, height: Chrome.capsuleHeight)   // 32 x 32
                .contentShape(Circle())
```

then `.buttonStyle(.plain)`, `.chromeGlassCircle()`, hover in `withAnimation(Chrome.hover)`,
`.help(help)`, `.accessibilityLabel(Text(help))`, `.background { NoWindowDragArea() }`.

**SOURCE FACT.** The circle is `capsuleHeight` (**32**), not `capsuleContentHeight` (28): a circle
button is a full 32pt where an icon label inside a capsule is 28pt.

### 9.7 `ChromeCircleMenu` (Chrome.swift:407-438)

Doc comment: `/// A round glass button that opens a popover menu, such as the permissions picker.`
Identical geometry to `ChromeCircleButton`; foreground
`Chrome.primaryText.opacity(isHovering || isPresented ? 1 : 0.92)`; popover `arrowEdge: .bottom`
wrapping `PopoverMenu { content }`.

### 9.8 `ChromeTextMenu` (Chrome.swift:440-481)

Doc comment: `/// A text button shaped like a chrome capsule that opens a popover, such as the branch picker.`

```swift
            HStack(spacing: 6) {
                Image(systemName: symbol)
                    .font(Chrome.inlineIconFont)                          // 11, semibold
                Text(verbatim: title)
                    .font(.system(size: 12.5, weight: .medium))
                    .lineLimit(1)
                Image(systemName: "chevron.down")
                    .font(Chrome.chevronFont)                             // 8, bold
                    .foregroundStyle(Chrome.secondaryText)
            }
            .foregroundStyle(Chrome.primaryText.opacity(isHovering || isPresented ? 1 : 0.92))
            .padding(.horizontal, Chrome.capsuleHorizontalPadding)        // 12   (line 464)
            .frame(height: Chrome.capsuleContentHeight)                   // 28   (line 465)
            .padding(.vertical, Chrome.capsuleVerticalPadding)            // 2    (line 466)
            .contentShape(Capsule(style: .continuous))
```

then (lines 469-479):

```swift
        .buttonStyle(.plain)
        .fixedSize()
        .chromeGlassCapsule()
        .onHover { hovering in
            withAnimation(Chrome.hover) { isHovering = hovering }
        }
        .help(help)
        .popover(isPresented: $isPresented, arrowEdge: .bottom) {
            PopoverMenu { content }
        }
        .background { NoWindowDragArea() }
```

Exact SF Symbol string: `"chevron.down"`.
**Ordering dependency:** `.padding(.horizontal, 12)` → `.frame(height: 28)` → `.padding(.vertical, 2)`.
The vertical padding is applied *outside* the height frame, giving **32** total.

**SOURCE FACT / upstream inconsistency to reproduce.** `ChromeTextMenu` has `.help(help)` at line 475
but **no** `.accessibilityLabel(Text(help))`, unlike `ChromeIconButton`, `ChromeMenuButton`,
`ChromeCircleButton`, `ChromeCircleMenu` and `ChromeTextButton`, which all set both.

### 9.9 `ChromeTextButton` (Chrome.swift:483-527)

Doc comment, verbatim (lines 483-484):

```swift
/// A text button shaped like a chrome capsule, such as the archive page's delete all. A
/// destructive one reads as an ordinary chrome control at rest and turns red under the pointer.
```

Geometry identical to `ChromeTextMenu` minus the chevron: `HStack(spacing: 6)`, inline icon font,
title `.system(size: 12.5, weight: .medium)` with `.lineLimit(1)`, then
`.padding(.horizontal, Chrome.capsuleHorizontalPadding)` (line 505),
`.frame(height: Chrome.capsuleContentHeight)` (506),
`.padding(.vertical, Chrome.capsuleVerticalPadding)` (507),
`.contentShape(Capsule(style: .continuous))`. Then `.buttonStyle(.plain)`, `.disabled(!isEnabled)`,
`.fixedSize()`, `.chromeGlassCapsule()`, hover in `withAnimation(Chrome.hover)`, `.help(help)`,
`.accessibilityLabel(Text(help))` (518), `.background { NoWindowDragArea() }` (519).

```swift
    private var foreground: Color {
        guard isEnabled else { return Chrome.primaryText.opacity(0.32) }
        if isDestructive { return Chrome.danger.opacity(isHovering ? 1 : 0.92) }
        return Chrome.primaryText.opacity(isHovering ? 1 : 0.92)
    }
```

**SOURCE FACT.** A destructive button is `Chrome.danger` at `0.92` at rest and `1` on hover. The doc
comment says it "reads as an ordinary chrome control at rest and turns red under the pointer", but
lines 522-526 make it red at rest too, differing only in opacity. **Record as an upstream
comment/code mismatch; port the code, not the comment.**
Defaults: `isEnabled = true`, `isDestructive = false`.

### 9.10 `ChromeSearchField` (Chrome.swift:792-837)

Doc comment: `/// A glass search capsule for a pane's chrome row. Fixed size so it sits aligned with the`
`/// other capsules instead of pushing them or clipping the sheet edge.`

```swift
struct ChromeSearchField: View {
    static let width: CGFloat = 200
```

```swift
        HStack(spacing: 6) {
            Image(systemName: "magnifyingglass")
                .font(.system(size: 12, weight: .semibold))
                .foregroundStyle(Chrome.secondaryText)
                .accessibilityHidden(true)
            TextField(prompt, text: $query)
                .textFieldStyle(.plain)
                // The capsule is the field's edge; the focus ring flashed around the text
                // alone as the caret landed.
                .focusEffectDisabled()
                .font(.system(size: 12.5))
                .foregroundStyle(Chrome.primaryText)
            Button {
                query = ""
            } label: {
                Image(systemName: "xmark.circle.fill")
                    .font(.system(size: 12))
                    .foregroundStyle(Chrome.secondaryText.opacity(0.8))
            }
            .buttonStyle(.plain)
            // Always laid out, so the field's text never shifts when the button comes;
            // invisible and out of the accessibility tree while there is nothing to clear.
            .opacity(query.isEmpty ? 0 : 1)
            .disabled(query.isEmpty)
            .accessibilityHidden(query.isEmpty)
            .accessibilityLabel(Text("Clear search"))
        }
        .padding(.horizontal, 10)
        .frame(width: Self.width, height: Chrome.capsuleContentHeight)
        .padding(.vertical, Chrome.capsuleVerticalPadding)
        .chromeGlassFieldCapsule()
        .fixedSize()
        .onExitCommand { query = "" }
        .background { NoWindowDragArea() }
```

Default prompt: `var prompt = "Search"`.
Exact strings, 4: `"magnifyingglass"`, `"xmark.circle.fill"`, `"Clear search"`, `"Search"`.
Sizes: fixed width **200**, content height **28**, `+2` vertical padding each side (total 32),
horizontal padding **10**.
The clear button is always laid out (`.opacity(0)` when empty), never conditionally inserted.
Escape clears: `.onExitCommand { query = "" }`.

### 9.11 `SidebarSearchField` (Chrome.swift:742-790)

```swift
        HStack(spacing: 6) {
            Image(systemName: "magnifyingglass")
                .font(.system(size: 12, weight: .medium))
                .foregroundStyle(Chrome.secondaryText.opacity(0.85))
            TextField(prompt, text: $text)
                .textFieldStyle(.plain)
                .font(.system(size: 13))
                .frame(maxWidth: .infinity)
                .onSubmit(onSubmit)
                .onKeyPress(.upArrow) { move(-1) }
                .onKeyPress(.downArrow) { move(1) }
            if !text.isEmpty {
                Button {
                    text = ""
                } label: {
                    Image(systemName: "xmark.circle.fill")
                        .font(.system(size: 12))
                        .foregroundStyle(Chrome.secondaryText.opacity(0.75))
                }
                .buttonStyle(.plain)
                .accessibilityLabel(Text("Clear search"))
            }
        }
        .padding(.horizontal, 9)
        .frame(height: 30)
        .background {
            Capsule(style: .continuous).fill(Chrome.overlay(0.07))
        }
        .contentShape(Capsule(style: .continuous))
        // Escape empties the field, the way the chrome's search capsule does.
        .onExitCommand { text = "" }
```

```swift
    private func move(_ offset: Int) -> KeyPress.Result {
        guard let onMove else { return .ignored }
        onMove(offset)
        return .handled
    }
```

**Deliberate contrast with `ChromeSearchField` (SOURCE FACT), 12 differences:**

| | `SidebarSearchField` | `ChromeSearchField` |
| --- | --- | --- |
| magnifier font | `size: 12, weight: .medium` | `size: 12, weight: .semibold` |
| magnifier colour | `secondaryText.opacity(0.85)` | `secondaryText` (full) |
| magnifier a11y | visible to a11y | `.accessibilityHidden(true)` |
| text font | `.system(size: 13)` | `.system(size: 12.5)` |
| text colour | inherited | `Chrome.primaryText` |
| clear icon colour | `secondaryText.opacity(0.75)` | `secondaryText.opacity(0.8)` |
| clear button | conditionally inserted (`if !text.isEmpty`) | always laid out, `.opacity(0)` |
| horizontal padding | `9` | `10` |
| height | `30` (no extra vertical padding) | `28` + `2` top/bottom = 32 |
| width | fills (`maxWidth: .infinity` on the field) | fixed `200` |
| background | `Capsule(style: .continuous).fill(Chrome.overlay(0.07))` | glass, `chromeGlassFieldCapsule()` |
| arrow keys | `.onKeyPress(.upArrow/.downArrow)` | none |
| focus ring | left on | `.focusEffectDisabled()` |
| window drag | none | `.background { NoWindowDragArea() }` |

Defaults: `var prompt = "Search"`, `var onSubmit: () -> Void = {}`, `var onMove: ((Int) -> Void)?`.
`onMove` doc, verbatim: "Up and down walk the results, one step per press. Last, so a bare trailing
closure still means `onSubmit`. Left out where there is no list under the field, and the arrows are
then the field's own again."
`move(_:)` returns `.ignored` when `onMove` is nil, `.handled` otherwise — the offsets passed are
exactly `-1` (up) and `1` (down).

### 9.12 `GlassPickerButton` (Chrome.swift:839-902)

Doc comment: `/// A glass capsule showing the current choice; clicking it opens the choices in a native popover.`

```swift
        let title = options.first { $0.value == selection }?.title ?? ""
```

```swift
            HStack(spacing: 6) {
                if let name = asset(selection) {
                    Image(name)
                        .resizable()
                        .aspectRatio(contentMode: .fit)
                        .frame(width: 14, height: 14)
                }
                if let symbol = symbol(selection) {
                    Image(systemName: symbol)
                        .font(.system(size: 12, weight: .medium))
                        .foregroundStyle(Chrome.secondaryText)
                }
                Text(verbatim: title)
                    .font(.system(size: 12.5, weight: .medium))
                    .lineLimit(1)
                    .truncationMode(.tail)
                Image(systemName: "chevron.down")
                    .font(Chrome.chevronFont)
                    .foregroundStyle(Chrome.secondaryText)
            }
            .foregroundStyle(Chrome.primaryText.opacity(isHovering || isPresented ? 1 : 0.92))
            .padding(.horizontal, Chrome.capsuleHorizontalPadding)   // line 877
            .frame(height: Chrome.capsuleContentHeight)              // line 878
            .contentShape(Capsule(style: .continuous))               // line 879
```

then:

```swift
        .buttonStyle(.plain)
        .fixedSize(horizontal: maxWidth == nil, vertical: true)
        .chromeGlassCapsule()
        .frame(maxWidth: maxWidth, alignment: .trailing)
        .onHover { hovering in
            withAnimation(Chrome.hover) { isHovering = hovering }
        }
        .popover(isPresented: $isPresented, arrowEdge: .bottom) {
            PopoverMenu {
                ForEach(Array(options.enumerated()), id: \.offset) { _, option in
                    PopoverItem(option.title, symbol: symbol(option.value), asset: asset(option.value), isChecked: option.value == selection) {
                        selection = option.value
                    }
                }
            }
        }
        .background { NoWindowDragArea() }
```

Asset image is **14 x 14**; the picker's leading symbol font is `.system(size: 12, weight: .medium)`
(**not** `Chrome.inlineIconFont`, which is 11/semibold). Title resolution falls back to the empty
string `""` when nothing matches.
Defaults: `var asset: (Value) -> String? = { _ in nil }`, `var symbol: (Value) -> String? = { _ in nil }`,
`var maxWidth: CGFloat? = nil`.
`ForEach` identity is `\.offset` from `Array(options.enumerated())` — index identity, not value
identity, so reordering `options` reuses rows.

**SOURCE FACT / upstream defect to reproduce.** `GlassPickerButton` applies
`.padding(.horizontal, 12)` (877) and `.frame(height: 28)` (878) but **no**
`.padding(.vertical, Chrome.capsuleVerticalPadding)`, unlike `ChromeTextMenu` (466) and
`ChromeTextButton` (507). Its glass capsule is therefore **28pt tall, 4pt shorter** than every other
text capsule in the chrome row. Reproduce it.

---

## 10. Scroll morph (Chrome.swift:529-616)

### 10.1 `ChromeScrollModel` (533-546), verbatim

```swift
@MainActor
@Observable
final class ChromeScrollModel {
    private(set) var progress: CGFloat = 0

    func update(travel: CGFloat) {
        let raw = (travel - Chrome.titleStartOffset) / (Chrome.titleEndOffset - Chrome.titleStartOffset)
        let clamped = min(max(raw, 0), 1)
        let quantized = (clamped / Chrome.progressQuantum).rounded() * Chrome.progressQuantum
        if quantized != progress { progress = quantized }
    }
}
```

**Exact computation (SOURCE FACT):**
- `raw = (travel - 10) / (44 - 10) = (travel - 10) / 34`
- clamp to `[0, 1]`
- quantize: `(clamped / 0.04).rounded() * 0.04`. Swift's `.rounded()` is
  `.toNearestOrAwayFromZero`, giving **26 distinct steps**: 0.00, 0.04, 0.08, … 1.00.
- write only on change, so an unchanged frame does not invalidate observers.

Doc comment, verbatim: "Scroll progress for the sticky chrome. Only the veil and the compact title
observe it, so scrolling never re-renders the page underneath."

### 10.2 `PaneTopVeil` (548-596)

```swift
        let progress = Double(model.progress)
        let isDark = colorScheme == .dark
        let scrim = isDark ? 0.14 + 0.18 * progress : 0.30 + 0.26 * progress
        let base = isDark ? Color.black : Color.white
        let tint = Chrome.glassTint.opacity(isDark ? 0.30 : 0.18)
        ZStack {
            // Only once content has scrolled under the chrome; at rest there is nothing to cover.
            if progress > 0 {
                Self.fade(base.opacity(scrim))
                Self.fade(tint)
            }
        }
        .opacity(0.08 + 0.92 * progress)
        .frame(height: Chrome.veilHeight)
        .allowsHitTesting(false)
```

```swift
    /// A colour held over the top 28% of the veil and eased out by its bottom edge, the
    /// mask the glass used to wear: a shorter plateau and a mid stop, so the veil reads
    /// as a soft falloff rather than a band with an edge.
    private static func fade(_ color: Color) -> LinearGradient {
        LinearGradient(
            stops: [
                .init(color: color, location: 0),
                .init(color: color, location: 0.28),
                .init(color: color.opacity(0.45), location: 0.62),
                .init(color: color.opacity(0), location: 1),
            ],
            startPoint: .top,
            endPoint: .bottom
        )
    }
```

**Exact token table (SOURCE FACT):**

| Quantity | Dark | Light |
| --- | --- | --- |
| scrim opacity | `0.14 + 0.18 * progress` (0.14 → 0.32) | `0.30 + 0.26 * progress` (0.30 → 0.56) |
| base colour | `Color.black` | `Color.white` |
| tint | `Chrome.glassTint.opacity(0.30)` | `Chrome.glassTint.opacity(0.18)` |
| whole-veil opacity | `0.08 + 0.92 * progress` | same |
| height | `Chrome.veilHeight` = `72` | same |

**Gradient stops: exactly 4**, in order — `(color, 0)`, `(color, 0.28)`, `(color.opacity(0.45), 0.62)`,
`(color.opacity(0), 1)`; `startPoint: .top`, `endPoint: .bottom`.
Layer order: scrim gradient first, tint gradient over it.
`.allowsHitTesting(false)`.

**Conditional inclusion, precise.** The two gradients live inside `if progress > 0`, while
`.opacity(0.08 + 0.92 * progress)` is applied to the `ZStack` unconditionally. So the `0.08` floor
only ever multiplies an empty stack: it is **unreachable as a visible value**. Reproduce the
expression as written.

The comment records the performance history: the veil used to be liquid glass under a gradient mask,
"a backdrop sample and an offscreen mask pass on every scrolled frame of every chat"; it is now two
gradient fills and nothing else.

### 10.3 `ChromeCompactTitle` (598-616)

```swift
        let progress = model.progress
        Text(verbatim: title)
            .font(.system(size: 15, weight: .semibold))
            .foregroundStyle(Chrome.primaryText)
            .lineLimit(1)
            .opacity(Double(progress))
            .offset(y: (1 - progress) * 5)
            .frame(maxWidth: .infinity, alignment: .leading)
            .allowsHitTesting(false)
            .accessibilityHidden(progress < 0.5)
```

Font **15 semibold**; rise **5pt** (`(1 - progress) * 5`); accessibility cutoff **`progress < 0.5`**.

---

## 11. Sidebar pieces (Chrome.swift:616-740)

### 11.1 `SidebarSymbol` (618-636)

```swift
    let name: String
    var scale: CGFloat = 1

    init(_ name: String, scale: CGFloat = 1) {
        self.name = name
        self.scale = scale
    }

        Image(systemName: name)
            .resizable()
            .interpolation(.high)
            .aspectRatio(contentMode: .fit)
            .frame(width: Chrome.symbolSize * scale, height: Chrome.symbolSize * scale)
            .symbolRenderingMode(.monochrome)
```

Base size `Chrome.symbolSize` = **14**, multiplied by `scale` (default `1`). Rendering mode is
explicitly `.monochrome`.

### 11.2 `SidebarIconBadge` (638-646)

```swift
        glyph
            .foregroundStyle(Chrome.primaryText.opacity(0.85))
            .frame(width: Chrome.iconSize, height: Chrome.iconSize)
```

Opacity **0.85**, frame **20 x 20**. Doc: "A sidebar glyph on its own: a provider logo or an SF
Symbol, with nothing drawn behind it."

### 11.3 `SidebarRow` (648-713)

```swift
        let shape = RoundedRectangle(cornerRadius: Chrome.rowCornerRadius, style: .continuous)   // 7
        Button(action: action) {
            HStack(spacing: 8) {
                icon
                    .frame(width: Chrome.iconSize, alignment: .center)                            // 20
                Text(verbatim: title)
                    .font(.system(size: 13, weight: isSelected || isEmphasized ? .medium : .regular))
                    .foregroundStyle(Chrome.primaryText.opacity(isSelected ? 1 : 0.92))
                    .lineLimit(1)
                    .truncationMode(.tail)
                Spacer(minLength: 4)
            }
            .padding(.leading, Chrome.rowHorizontalPadding)                                       // 8
            .padding(.trailing, Chrome.rowHorizontalPadding + accessoryWidth)                     // 8 + accessoryWidth
            .frame(height: Chrome.rowHeight)                                                      // 28
            .frame(maxWidth: .infinity, alignment: .leading)
            .background { shape.fill(fill).animation(Chrome.hover, value: isSelected) }
            .contentShape(shape)
        }
        .buttonStyle(.plain)
        .overlay(alignment: .trailing) {
            accessory(isHovering)
                .padding(.trailing, 6)
        }
        .onHover { hovering in
            withAnimation(Chrome.hover) { isHovering = hovering }
        }
        .accessibilityAddTraits(isSelected ? [.isSelected, .isButton] : .isButton)
```

```swift
    private var fill: Color {
        if isSelected { return Chrome.overlay(0.12) }
        if isHovering { return Chrome.overlay(0.06) }
        return .clear
    }
```

**Row fill ladder, exactly 3 states in this order:** selected `Chrome.overlay(0.12)` → hovering
`Chrome.overlay(0.06)` → `.clear`.
`HStack(spacing: 8)`, `Spacer(minLength: 4)`, accessory `.padding(.trailing, 6)`.
Title weight is `.medium` when `isSelected || isEmphasized`, else `.regular`; title opacity is `1`
when selected, `0.92` otherwise. **Emphasis changes weight only, not opacity.**
Init defaults: `isSelected: Bool = false`, `isEmphasized: Bool = false`, `accessoryWidth: CGFloat = 0`.
`accessory` is `(Bool) -> Accessory`, taking `isHovering`.

Convenience overload (715-724) for `Accessory == EmptyView`, verbatim:

```swift
extension SidebarRow where Accessory == EmptyView {
    init(
        title: String,
        isSelected: Bool = false,
        action: @escaping () -> Void,
        @ViewBuilder icon: () -> Icon
    ) {
        self.init(title: title, isSelected: isSelected, action: action, icon: icon) { _ in EmptyView() }
    }
}
```

It drops `isEmphasized` and `accessoryWidth`, so they take their defaults (`false`, `0`).

### 11.4 `RowAccessoryIcon` (726-740)

```swift
        Image(systemName: symbol)
            .font(Chrome.inlineIconFont)                 // 11, semibold
            .foregroundStyle(Chrome.secondaryText)
            .frame(width: 20, height: 20)                // line 737, literal
            .contentShape(.rect)
```

Frame is the **literal `20 x 20`**, not `Chrome.iconSize` (which is also 20).
`init(_ symbol: String)` — the label is unnamed.

---

## 12. Transitions and animatable modifiers (Chrome.swift:904-971)

### 12.1 `SoftAppearModifier` (904-912)

```swift
/// A soft appearance: fades in with a short rise. Opacity and offset are plain layer
/// properties, so a view in its settled state costs nothing extra; a blur here would leave
/// every timeline row and markdown block with a filter of its own, an offscreen pass each.
struct SoftAppearModifier: ViewModifier {
    let isVisible: Bool

    func body(content: Content) -> some View {
        content
            .opacity(isVisible ? 1 : 0)
            .offset(y: isVisible ? 0 : 4)
    }
}
```

Rise distance **4pt**.

### 12.2 `AnyTransition.softAppear` (914-923)

```swift
extension AnyTransition {
    /// Arrives softly; leaves with a quick fade so removals never drag.
    static var softAppear: AnyTransition {
        .asymmetric(
            insertion: .modifier(active: SoftAppearModifier(isVisible: false), identity: SoftAppearModifier(isVisible: true))
                .animation(.softAppear),
            removal: .opacity.animation(.easeOut(duration: 0.12))
        )
    }
}
```

### 12.3 `Animation.softAppear` (925-928)

```swift
extension Animation {
    /// The timing every soft appearance shares.
    static var softAppear: Animation { .smooth(duration: 0.32) }
}
```

### 12.4 `SettleGlideModifier` (private, 930-955)

```swift
/// A departing badge's face along its glide: `progress` runs from 0, in place, to 1, gone.
/// The face holds through the first half of the flight and fades over the second, the way
/// the sidebar's ghost slips out of the list's edge (see `RowGlideAnimator`); with Reduce
/// Motion on it fades where it stands.
private struct SettleGlideModifier: ViewModifier, Animatable {
    var progress: Double

    var animatableData: Double {
        get { progress }
        set { progress = newValue }
    }

    func body(content: Content) -> some View {
        let reduceMotion = NSWorkspace.shared.accessibilityDisplayShouldReduceMotion
        let fade = reduceMotion ? progress : Self.smoothstep(0.45, 0.92, progress)
        content
            .opacity(1 - fade)
            .offset(y: reduceMotion ? 0 : 22 * progress)
            .scaleEffect(reduceMotion ? 1 : 1 - 0.04 * progress, anchor: .top)
    }

    private static func smoothstep(_ edge0: Double, _ edge1: Double, _ x: Double) -> Double {
        let t = min(max((x - edge0) / (edge1 - edge0), 0), 1)
        return t * t * (3 - 2 * t)
    }
}
```

**Exact values (SOURCE FACT):**
- fade curve: `smoothstep(0.45, 0.92, progress)`, i.e. `t = clamp((p - 0.45) / 0.47, 0, 1)` then
  `t * t * (3 - 2 * t)`. Reduce Motion replaces it with linear `progress`.
- glide distance: `22 * progress` points down (0 under Reduce Motion).
- scale: `1 - 0.04 * progress`, anchor `.top` (1 under Reduce Motion).
- `progress` runs **0 = in place, 1 = gone**.

### 12.5 `AnyTransition.settleGlide` (957-971)

```swift
extension AnyTransition {
    /// How a badge whose work is done leaves the timeline: the way a settled chat's row
    /// leaves the sidebar. It glides down a little on the glide spring and fades out on the
    /// way, gone before it would land. It arrives softly, like everything else.
    static var settleGlide: AnyTransition {
        .asymmetric(
            insertion: .modifier(active: SoftAppearModifier(isVisible: false), identity: SoftAppearModifier(isVisible: true))
                .animation(.softAppear),
            removal: .modifier(active: SettleGlideModifier(progress: 1), identity: SettleGlideModifier(progress: 0))
                .animation(.spring(Chrome.glideSpring))
        )
    }
}
```

Insertion is byte-identical to `softAppear`'s; only the removal differs.

---

## 13. Cards, sections, rows (Chrome.swift:971-1048)

### 13.1 `ChromeCard` (973-988)

```swift
        // Rows are built as they scroll into view, and the card is a filled shape rather than a
        // clip, so a long page costs neither every row up front nor a mask per card while scrolling.
        LazyVStack(alignment: .leading, spacing: 0) {
            content
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(
            RoundedRectangle(cornerRadius: Chrome.cardCornerRadius, style: .continuous)
                .fill(Chrome.overlay(0.03))
        )
```

Card fill: `Chrome.overlay(0.03)` (labelColor at 3%), radius **16**, `.continuous`.
`LazyVStack` with `spacing: 0`. It is a **background, not a clip** — deliberate, per the comment.

### 13.2 `ChromeSection` (990-1014)

```swift
    let title: String
    /// Sits right after the title: an info button, a count.
    var accessory: AnyView? = nil
    /// Sits at the header's far right: a control for the whole section, out of its cards.
    var trailing: AnyView? = nil

        VStack(alignment: .leading, spacing: Chrome.sectionHeaderSpacing) {   // 10
            HStack(spacing: 4) {
                Text(verbatim: title)
                    .font(.system(size: 13, weight: .semibold))
                    .foregroundStyle(.secondary)
                if let accessory { accessory }
                if let trailing {
                    Spacer(minLength: 8)
                    trailing
                }
            }
            content
        }
        .frame(maxWidth: .infinity, alignment: .leading)
```

Header font **13 semibold**, `.foregroundStyle(.secondary)` — SwiftUI's `.secondary` hierarchical
style, **not** `Chrome.secondaryText`. `HStack(spacing: 4)`; `Spacer(minLength: 8)` only when
`trailing` is present, so a header with only an `accessory` has no spacer at all.

**SOURCE FACT.** `Chrome.sectionSpacing` (20) is *not* used inside `ChromeSection`; it is the token
callers use *between* sections (7 reads repo-wide).

### 13.3 `ChromeRow` (1016-1041)

```swift
    let title: String
    var detail: String?

        HStack(alignment: .center, spacing: 16) {
            VStack(alignment: .leading, spacing: 2) {
                Text(verbatim: title)
                    .font(.system(size: 13))
                    .foregroundStyle(Chrome.primaryText)
                if let detail, !detail.isEmpty {
                    Text(verbatim: detail)
                        .font(.system(size: 11))
                        .foregroundStyle(Chrome.secondaryText)
                        .fixedSize(horizontal: false, vertical: true)
                }
            }
            Spacer(minLength: 12)
            control
        }
        .padding(.leading, 16)
        .padding(.trailing, Chrome.rowControlTrailingPadding)   // 10
        .padding(.vertical, 11)
```

Title **13 regular**, detail **11 regular** in `secondaryText`, inner `VStack(spacing: 2)`,
`HStack(alignment: .center, spacing: 16)`, `Spacer(minLength: 12)`, leading padding literal **16**,
trailing `Chrome.rowControlTrailingPadding` (**10**), vertical **11**.
Detail is shown only when non-nil **and** non-empty.

### 13.4 `ChromeRowDivider` (1043-1048)

```swift
        Divider()
            .padding(.leading, 16)
```

Leading inset literal **16** (matches `ChromeRow`'s leading padding and the value of
`Chrome.contentHorizontalPadding`, but written as a literal in both places).

---

## 14. Window drag helpers (Chrome.swift:1050-1107)

No design tokens; behaviour only. Recorded because the chrome row's geometry depends on it.

```swift
/// Lets a stretch of chrome move the window, the way the sidebar does in Droppy.
struct WindowDragArea: NSViewRepresentable {
    func makeNSView(context: Context) -> WindowDragView { WindowDragView() }
    func updateNSView(_ nsView: WindowDragView, context: Context) {}
}

final class WindowDragView: NSView {
    override func acceptsFirstMouse(for event: NSEvent?) -> Bool { true }
    override func mouseDown(with event: NSEvent) { window?.performDrag(with: event) }
    override func mouseUp(with event: NSEvent) {}
}
```

```swift
final class NoWindowDragView: NSView {
    override func acceptsFirstMouse(for event: NSEvent?) -> Bool { true }
    /// A press here works the control, never the window.
    override var mouseDownCanMoveWindow: Bool { false }
    override func mouseDown(with event: NSEvent) { superview?.mouseDown(with: event) }
    override func mouseDragged(with event: NSEvent) { superview?.mouseDragged(with: event) }
    override func mouseUp(with event: NSEvent) { superview?.mouseUp(with: event) }
    override func rightMouseDown(with event: NSEvent) { superview?.rightMouseDown(with: event) }
    override func rightMouseUp(with event: NSEvent) { superview?.rightMouseUp(with: event) }
}
```

The `NoWindowDragArea` doc comment explicitly names the coupling to `WindowChrome.titlebarHeight`:
"The chrome row sits inside the title bar's reach (see `WindowChrome.titlebarHeight`), where AppKit
moves the window for any view that lets it."

`.background { NoWindowDragArea() }` appears on **9** chrome components: `ChromeCapsule`,
`ChromeIconButton`, `ChromeMenuButton`, `ChromeCircleButton`, `ChromeCircleMenu`, `ChromeTextMenu`,
`ChromeTextButton`, `ChromeSearchField`, `GlassPickerButton`.
It is **absent** from `SidebarRow`, `SidebarSearchField`, `ChromeDivider`, `ChromeIconLabel` (the bare
label; its enclosing buttons carry it) and the whole card/section/row family.

---

## 15. Supporting declarations outside Chrome.swift

### 15.1 `panelControlFill` — `DroppyCode/UI/Common/RenderBudget.swift:79-100`, verbatim

```swift
extension EnvironmentValues {
    /// True for everything inside a floating glass panel: a helper's, the team's, the
    /// usage panel. Liquid Glass samples what lies behind it on every frame that content
    /// moves, and a panel already does that for its whole surface; a capsule or a circle
    /// drawn as glass on top of it is a second sampling pass for the same pixels, and with
    /// a few panels open those added up to twenty-odd passes over a scrolling timeline.
    /// Controls read this and draw as a flat tinted fill on a panel instead.
    @Entry var isOnGlassPanel = false
}

extension Chrome {
    /// The fill of a control drawn flat on a glass panel (see `isOnGlassPanel`): the
    /// theme's tint over the panel's scrim, lifted a touch, and brighter under the
    /// pointer the way interactive glass brightens.
    static func panelControlFill(isDark: Bool, hovered: Bool = false) -> Color {
        let lift = hovered ? (isDark ? 0.24 : 0.12) : (isDark ? 0.12 : 0.04)
        return Chrome.glassTint
            .mix(with: isDark ? .white : .black, by: lift)
            .opacity(isDark ? 0.55 : 0.42)
    }
}
```

**Exact `lift` table, 4 values:**

| | dark | light |
| --- | --- | --- |
| hovered | `0.24` | `0.12` |
| at rest | `0.12` | `0.04` |

Mix partner: `.white` in dark, `.black` in light. Final opacity: `0.55` dark, `0.42` light.
Environment default: `isOnGlassPanel = false`.
Call sites of `Chrome.panelControlFill`, **5** repo-wide:
`Chrome.swift:173`, `App/Views/Chat/HydraPanel.swift:469`, `App/Views/Chat/HydraProgressBar.swift:574`,
`App/Views/Chat/HydraRunMetricsView.swift:274`, `App/Views/Composer/ComposerView.swift:1301`.
Of these, only `Chrome.swift:173` passes `hovered:`; the other four take the default `false`.

### 15.2 `ThemeSpec` / `ThemeManager` — `DroppyCode/UI/Theme/AppTheme.swift:202-233`, verbatim

```swift
/// What a theme paints: an optional fixed color scheme, an optional accent
/// (nil keeps the system control accent), the glass tint, and status hues.
struct ThemeSpec {
    var scheme: ColorScheme?
    var accent: Color?
    var surface: Color
    var success: Color
    var warning: Color
    var danger: Color
}
```

**`ThemeSpec` has exactly 6 fields**, 2 optional (`scheme`, `accent`) and 4 required
(`surface`, `success`, `warning`, `danger`).

```swift
/// The theme every static `Chrome` color reads. Mirrored from
/// `AppSettings.theme`, which is the source of truth; views re-render
/// through the settings they already observe, so the static read is always
/// fresh on the main thread.
enum ThemeManager {
    /// Posted after the theme changes, for layer-backed views whose colours are set once
    /// rather than resolved on every draw (the spinners' cells), so they follow the theme
    /// even when nothing re-renders them.
    static let didChange = Notification.Name("ThemeManager.didChange")

    nonisolated(unsafe) static var current: AppTheme = .system {
        didSet {
            spec = current.spec
            NotificationCenter.default.post(name: didChange, object: nil)
        }
    }

    /// The current theme's colors, built once per theme change. `Chrome.accent` and friends
    /// are read in hundreds of view bodies per frame, and building a spec parses hex colors,
    /// so the parse happens here rather than on every read.
    nonisolated(unsafe) private(set) static var spec: ThemeSpec = AppTheme.system.spec
}
```

Exact notification-name string: `"ThemeManager.didChange"`.
Theme hex parsing is `Color(hex:)` in the same file: trims whitespace/newlines, drops a leading `#`,
`Scanner.scanHexInt64`, then `red = ((value >> 16) & 0xFF) / 255`, `green = ((value >> 8) & 0xFF) / 255`,
`blue = (value & 0xFF) / 255`. Documented example strings: `"CBA6F7"` and `"#CBA6F7"`, in sRGB.

---

## 16. `WindowChrome.swift` tokens

### 16.1 Window configuration, verbatim (WindowChrome.swift:106-129)

```swift
    static func configure(_ window: NSWindow) {
        // Idempotent: viewDidMoveToWindow fires on every reorder, so the setup pays once.
        if configuredWindows.contains(window) { return }
        configuredWindows.add(window)
        window.styleMask.insert(.fullSizeContentView)
        window.titlebarAppearsTransparent = true
        window.titleVisibility = .hidden
        // A focused window draws a separator under the title bar that cuts through the rounded corner.
        window.titlebarSeparatorStyle = .none
        window.isOpaque = false
        window.backgroundColor = .clear
        window.hasShadow = true
        window.isMovableByWindowBackground = false
        window.invalidateShadow()
        window.autorecalculatesKeyViewLoop = true
    }
```

**Exactly 10 window mutations, in this order:** `styleMask.insert(.fullSizeContentView)`,
`titlebarAppearsTransparent = true`, `titleVisibility = .hidden`, `titlebarSeparatorStyle = .none`,
`isOpaque = false`, `backgroundColor = .clear`, `hasShadow = true`,
`isMovableByWindowBackground = false`, `invalidateShadow()`, `autorecalculatesKeyViewLoop = true`.

Idempotency store: `private static let configuredWindows = NSHashTable<NSWindow>.weakObjects()`.

`autorecalculatesKeyViewLoop = true` carries a long upstream incident comment naming
"the Sept 15 2026 freezes" and `FocusBridge.updateDefaultKeyViewLoop`. Record as a
**behaviour-critical flag, not a style token**.

**Exact constant strings (2):**

```swift
    private static let leadingIdentifier = "droppycode.trafficLight.leading"
    private static let topIdentifier = "droppycode.trafficLight.top"
```

### 16.2 Derived geometry, verbatim (WindowChrome.swift:131-160)

```swift
    /// Where the buttons sit with the sidebar hidden: level with the chat's chrome capsules,
    /// which then step aside for them, and inset like the chrome is from the sheet's edge.
    static var collapsedTrafficLightTop: CGFloat {
        Chrome.sheetInset + Chrome.chromeTopPadding + (Chrome.capsuleHeight - Chrome.trafficLightDiameter) / 2
    }
```

`= 0 + 12 + (32 - 14) / 2 = 12 + 9 = ` **21**

```swift
    static var titlebarHeight: CGFloat {
        Chrome.sheetInset + Chrome.chromeTopPadding + Chrome.capsuleHeight
    }
```

`= 0 + 12 + 32 = ` **44**

```swift
    /// The view AppKit sizes the title bar with: the buttons' bar and its accessories together.
    static func titlebarContainer(of window: NSWindow) -> NSView? {
        window.standardWindowButton(.closeButton)?.superview?.superview
    }
```

Note the **two** `superview` hops: button → buttons' bar → the container AppKit sizes.

### 16.3 `fitTitlebar(of:)`, verbatim (162-174)

```swift
    static func fitTitlebar(of window: NSWindow) {
        guard let container = titlebarContainer(of: window), let themeFrame = container.superview else { return }
        var fitted = container.frame
        guard fitted.height < titlebarHeight else { return }
        if themeFrame.isFlipped {
            fitted.size.height = titlebarHeight
        } else {
            fitted.origin.y = fitted.maxY - titlebarHeight
            fitted.size.height = titlebarHeight
        }
        container.frame = fitted
    }
```

Only ever grows the container (`guard fitted.height < titlebarHeight`); keeps the top edge fixed in
both coordinate conventions.

### 16.4 `placeTrafficLights(on:sidebarVisible:animated:)` — placement math (176-220)

```swift
        let leading = sidebarVisible ? Chrome.trafficLightLeading : Chrome.sheetInset + Chrome.chromeHorizontalPadding
        let top = sidebarVisible ? Chrome.trafficLightTop : collapsedTrafficLightTop
```

| State | leading | top |
| --- | --- | --- |
| sidebar visible | `Chrome.trafficLightLeading` = **18** | `Chrome.trafficLightTop` = **16** |
| sidebar hidden | `Chrome.sheetInset + Chrome.chromeHorizontalPadding` = `0 + 14` = **14** | `collapsedTrafficLightTop` = **21** |

Button order and x-spacing, verbatim:

```swift
        let types: [NSWindow.ButtonType] = [.closeButton, .miniaturizeButton, .zoomButton]
        for (index, type) in types.enumerated() {
            guard let button = window.standardWindowButton(type) else { continue }
            button.isHidden = false
            let x = leading + CGFloat(index) * Chrome.trafficLightPitch
```

**Exactly 3 buttons**, in this order: `.closeButton`, `.miniaturizeButton`, `.zoomButton`.
Pitch `Chrome.trafficLightPitch` = **23**, so x = `leading`, `leading + 23`, `leading + 46`.
Size constraints: `widthAnchor.constraint(equalToConstant: Chrome.trafficLightDiameter)` and
`heightAnchor.constraint(equalToConstant: Chrome.trafficLightDiameter)` — **14 x 14**.
The first pass creates 4 constraints per button (leading, top, width, height) after setting
`translatesAutoresizingMaskIntoConstraints = false`; later passes mutate only the two identified
constants, and only when they differ.

Animation block, verbatim:

```swift
        guard changed else { return }
        // Never animated mid-resize: a fresh group every frame is what lagged the buttons.
        guard animated, !window.inLiveResize else {
            titlebar.layoutSubtreeIfNeeded()
            return
        }
        // The same run as Chrome.panelSlide, so the buttons and the chrome row that makes
        // room for them arrive together. A plain ease-out spends the run gliding; a curve
        // that is nearly done after its first few frames reads as the buttons popping over.
        let reduceMotion = NSWorkspace.shared.accessibilityDisplayShouldReduceMotion
        NSAnimationContext.runAnimationGroup { context in
            context.duration = reduceMotion ? 0.18 : 0.32
            context.timingFunction = CAMediaTimingFunction(name: .easeOut)
            context.allowsImplicitAnimation = true
            titlebar.layoutSubtreeIfNeeded()
        }
```

**0.18 / 0.32** — deliberately the same numbers as `Chrome.panelSlide`, but the AppKit side is an
**ease-out over that duration, not a spring**.

### 16.5 `SidebarLayout` constants (226-232) and persisted keys

```swift
    static let minimumWidth: CGFloat = 210
    static let maximumWidth: CGFloat = 440
    static let collapseThreshold: CGFloat = 110
    static let defaultWidth: CGFloat = 268

    private enum Key {
        static let width = "sidebarWidth"
        static let startsOpen = "sidebarStartsOpen"
    }
```

**Exact persisted key strings, 2:** `"sidebarWidth"` (stored as `Double`), `"sidebarStartsOpen"`
(stored as `Bool`).

Load, verbatim (367-376):

```swift
        let defaults = CaptureRun.defaults ?? .standard
        let stored = (defaults.object(forKey: Key.width) as? Double).map { CGFloat($0) } ?? Self.defaultWidth
        restingWidth = min(Self.maximumWidth, max(Self.minimumWidth, stored))
        width = restingWidth
        let visible = defaults.object(forKey: Key.startsOpen) as? Bool ?? false
        startsOpen = visible
        isVisible = visible
        holdsTrafficLights = visible
```

**Defaults when nothing is stored:** width `268`, clamped into `[210, 440]`; `startsOpen` **`false`**
(the sidebar is collapsed on first run — the doc comment says so explicitly: "Collapsed by default:
the list button in the toolbar shows and hides it any time, and that choice lasts for the session
alone.").

Save, verbatim:

```swift
    private func persist() {
        let defaults = CaptureRun.defaults ?? .standard
        defaults.set(Double(restingWidth), forKey: Key.width)
        defaults.set(startsOpen, forKey: Key.startsOpen)
    }
```

**SOURCE FACT.** `persist()` writes `restingWidth`, **not** `width` — a width mid-drag is never saved.

Derived width and run:

```swift
    /// The width from which the buttons fit beside the sidebar's edge with their usual clearance.
    static var trafficLightsFitWidth: CGFloat {
        Chrome.trafficLightLeading + Chrome.trafficLightsWidth + Chrome.trafficLightClearance
    }
```

`= 18 + 60 + 10 = ` **88**

```swift
    /// The spring of Chrome.panelSlide is visually settled by then.
    private static var slideRun: Int { NSWorkspace.shared.accessibilityDisplayShouldReduceMotion ? 220 : 420 }
```

**220 / 420 milliseconds**, consumed as `try? await Task.sleep(for: .milliseconds(Self.slideRun))`.

Observable state, with exact semantics:
`renderedWidth` = `isVisible ? width : 0`.
`noteLaidOutWidth(_:)` flips `holdsTrafficLights` at `laidOut >= Self.trafficLightsFitWidth` (88) and
writes nothing when the laid-out width is unchanged.

Drag and collapse math, verbatim (322-361):

```swift
    func beginDrag() {
        slideTask?.cancel()
        isSliding = false
        slideDelta = 0
        isDragging = true
        if isVisible {
            anchorWidth = width
            return
        }
        anchorWidth = 0
        var transaction = Transaction()
        transaction.disablesAnimations = true
        withTransaction(transaction) {
            width = 0
            isVisible = true
        }
    }

    func drag(by translation: CGFloat) {
        width = min(Self.maximumWidth, max(0, anchorWidth + translation))
    }

    func endDrag() {
        if width < Self.collapseThreshold {
            beginSlide(delta: width)
            withAnimation(Chrome.panelSlide) { isVisible = false }
            width = restingWidth
        } else {
            let settled = max(Self.minimumWidth, width)
            restingWidth = settled
            withAnimation(Chrome.panelSlide) { width = settled }
        }
        persist()
        isDragging = false
    }

    func toggle() {
        let opening = !isVisible
        if opening { width = restingWidth }
        beginSlide(delta: opening ? -restingWidth : width)
        withAnimation(Chrome.panelSlide) { isVisible.toggle() }
        persist()
    }
```

**Ordering dependency (SOURCE FACT).** While dragging, the lower clamp is **0**, not `minimumWidth`;
the `minimumWidth` clamp is applied only on release, and only when the width is at or above
`collapseThreshold` (**110**). Dragging to 50 hides the sidebar; dragging to 150 snaps up to 210.
`slideDelta` is **negative while opening** (`-restingWidth`) and **positive while closing** (`width`).

### 16.6 `SidebarResizeHandle` and `Grip` constants

```swift
/// The grab strip on the sidebar edge. AppKit gives it a real resize cursor and first-click tracking.
struct SidebarResizeHandle: NSViewRepresentable {
    static let hitWidth: CGFloat = 9
```

```swift
    private enum Grip {
        static let width: CGFloat = 2.5
        static let restHeight: CGFloat = 10
        static let shownHeight: CGFloat = 22
        static let alpha: Float = 0.6
        static let hoverGlow: Float = 0.05
        static let dragGlow: Float = 0.10
        static let duration: CFTimeInterval = 0.18
        static let verticalInset = Chrome.trafficLightTop + Chrome.trafficLightDiameter
    }
```

**`Grip` has exactly 8 members.** `verticalInset` = `Chrome.trafficLightTop + Chrome.trafficLightDiameter`
= `16 + 14` = **30**. Types matter: `alpha`, `hoverGlow`, `dragGlow` are `Float` (CALayer opacity),
`duration` is `CFTimeInterval`, the rest `CGFloat`.

Layer setup, verbatim:

```swift
    private func configureLayers() {
        wantsLayer = true
        gripLayer.cornerRadius = Grip.width / 2
        gripLayer.opacity = 0
        glowLayer.cornerRadius = 4
        glowLayer.opacity = 0
        applyColors()
        layer?.addSublayer(glowLayer)
        layer?.addSublayer(gripLayer)
    }
```

Grip corner radius `Grip.width / 2` = **1.25**; glow corner radius literal **4**.
Sublayer order: glow first, grip over it.

Colours, verbatim:

```swift
    private func applyColors() {
        effectiveAppearance.performAsCurrentDrawingAppearance {
            let color = NSColor.labelColor.cgColor
            gripLayer.backgroundColor = color
            glowLayer.backgroundColor = color
        }
    }
```

Both layers are `NSColor.labelColor` resolved for the current appearance, re-resolved in
`viewDidChangeEffectiveAppearance()`.

Frames and opacity, verbatim:

```swift
    private var showsGrip: Bool { isActive && (isHovering || isDragging) }

    private func updateGrip(animated: Bool) {
        let height = showsGrip ? Grip.shownHeight : Grip.restHeight
        CATransaction.begin()
        if animated {
            CATransaction.setAnimationDuration(Grip.duration)
            CATransaction.setAnimationTimingFunction(CAMediaTimingFunction(name: .easeOut))
        } else {
            CATransaction.setDisableActions(true)
        }
        gripLayer.frame = CGRect(x: (bounds.width - Grip.width) / 2, y: (bounds.height - height) / 2, width: Grip.width, height: height)
        gripLayer.opacity = showsGrip ? Grip.alpha : 0
        glowLayer.frame = CGRect(
            x: bounds.minX + 0.5,
            y: Grip.verticalInset,
            width: max(0, bounds.width - 1),
            height: max(0, bounds.height - Grip.verticalInset)
        )
        glowLayer.opacity = showsGrip ? (isDragging ? Grip.dragGlow : Grip.hoverGlow) : 0
        CATransaction.commit()
    }
```

Glow inset literals: `x = bounds.minX + 0.5`, `width = max(0, bounds.width - 1)` — a half-point inset
each side. The glow starts at y **30** and runs to the bottom (in the view's own flipping).
Grip opacity ladder: `0.6` when shown, `0` otherwise.
Glow opacity ladder, 3 states: dragging `0.10` → hovering `0.05` → `0`.

Behaviour tokens:
`addCursorRect(bounds, cursor: .columnResize)` only when `isActive`;
`override var mouseDownCanMoveWindow: Bool { false }`;
`acceptsFirstMouse` returns `true`;
`hitTest` returns `nil` when inactive;
tracking area, verbatim:
`NSTrackingArea(rect: .zero, options: [.mouseEnteredAndExited, .activeAlways, .inVisibleRect], owner: self, userInfo: nil)`.
`mouseEntered` sets `isHovering = isActive` (not unconditionally `true`).
`refreshHover()` reads `window.mouseLocationOutsideOfEventStream` converted into the view and tests
`bounds.contains(point)`, guarded by `isActive`, a non-nil `window` and `!isHiddenOrHasHiddenAncestor`.

### 16.7 `WindowChromeConfigurator` / `WindowChromeProbeView` defaults

```swift
struct WindowChromeConfigurator: NSViewRepresentable {
    var sidebarVisible = true
    var sidebarDragging = false
```

```swift
final class WindowChromeProbeView: NSView {
    var sidebarDragging = false
    var sidebarVisible = true { ... }
```

`override func hitTest(_ point: NSPoint) -> NSView? { nil }` — the probe never takes a click.
Re-fit trigger, verbatim:

```swift
        guard let container = notification.object as? NSView,
              container.frame.height < WindowChrome.titlebarHeight,
              !titlebarFitPending else { return }
```

The animation predicate when the sidebar's visibility changes, verbatim:

```swift
            WindowChrome.placeTrafficLights(on: window, sidebarVisible: sidebarVisible, animated: !window.inLiveResize && !sidebarDragging)
```

Two notification observers per window, both removed in `viewDidMoveToWindow` before re-registering
and in `deinit`: `NSWindow.didEndLiveResizeNotification` (on the window) and
`NSView.frameDidChangeNotification` (on the title bar container, with
`container.postsFrameChangedNotifications = true`).

---

## 17. `ShortcutTint.swift` — complete file, 12 lines

```swift
import SwiftUI

/// The colour each shortcut section wears in Settings; kept with the theme so the
/// shortcut model stays free of chrome colours.
extension AppShortcut.Section {
    var tint: Color {
        switch self {
        case .threads: Chrome.blue
        case .window: Color(red: 0.686, green: 0.322, blue: 0.871)
        }
    }
}
```

**The table has exactly 2 entries:**

| `AppShortcut.Section` case | tint |
| --- | --- |
| `.threads` | `Chrome.blue` = `Color(red: 0.040, green: 0.478, blue: 1.000)` |
| `.window` | `Color(red: 0.686, green: 0.322, blue: 0.871)` — an inline purple, **not** a `Chrome` token |

**SOURCE FACT.** The purple is declared nowhere else; it is the only colour literal in the file and
has no `Chrome` name. In 8-bit sRGB it rounds to `rgb(175, 82, 222)` = `#AF52DE`.
**ENGINEERING DECISION:** give it a name in the port (a shortcut-section palette), keeping the exact
sRGB triple.

**UNRESOLVED (out of scope for this pass).** The enumeration of `AppShortcut.Section` itself lives in
the shortcut model, not in these files. This switch is exhaustive without a `default`, so the type
has exactly these 2 cases as of this commit; a separate extraction should confirm.

---

## 18. Token usage census (repo-wide)

**SOURCE FACT.** Counted with
`grep -rhoE "(^|[^A-Za-z])Chrome\.[A-Za-z]+" --include=*.swift DroppyCode | grep -oE "Chrome\.[A-Za-z]+" | sort | uniq -c | sort -rn`
over the whole `DroppyCode` tree. Counts include uses inside `Chrome.swift` itself; they exclude the
declaration lines, which are unqualified. **56 distinct `Chrome.` members are referenced.**

| Token | References |
| --- | --- |
| `Chrome.secondaryText` | 263 |
| `Chrome.primaryText` | 160 |
| `Chrome.panelSlide` | 93 |
| `Chrome.overlay` | 67 |
| `Chrome.accent` | 47 |
| `Chrome.hover` | 42 |
| `Chrome.danger` | 34 |
| `Chrome.warning` | 22 |
| `Chrome.success` | 20 |
| `Chrome.chromeTopPadding` | 17 |
| `Chrome.capsuleContentHeight` | 16 |
| `Chrome.capsuleHorizontalPadding` | 13 |
| `Chrome.capsuleHeight` | 13 |
| `Chrome.accentNSColor` | 13 |
| `Chrome.glassTint` | 12 |
| `Chrome.capsuleVerticalPadding` | 12 |
| `Chrome.rowControlTrailingPadding` | 11 |
| `Chrome.inlineIconFont` | 11 |
| `Chrome.chromeHorizontalPadding` | 11 |
| `Chrome.iconFont` | 10 |
| `Chrome.trafficLightDiameter` | 8 |
| `Chrome.sheetInset` | 8 |
| `Chrome.rowHorizontalPadding` | 8 |
| `Chrome.rowCornerRadius` | 8 |
| `Chrome.sectionSpacing` | 7 |
| `Chrome.windowCornerRadius` | 6 |
| `Chrome.trafficLightTop` | 6 |
| `Chrome.sectionHeaderSpacing` | 5 |
| `Chrome.rowHeight` | 5 |
| `Chrome.panelControlFill` | 5 |
| `Chrome.listInset` | 5 |
| `Chrome.blue` | 5 |
| `Chrome.symbolSize` | 4 |
| `Chrome.rowButtonLabelHeight` | 4 |
| `Chrome.iconSize` | 4 |
| `Chrome.contentTopInset` | 4 |
| `Chrome.chevronFont` | 4 |
| `Chrome.orange` | 3 |
| `Chrome.glideSpring` | 3 |
| `Chrome.cardCornerRadius` | 3 |
| `Chrome.trafficLightsWidth` | 2 |
| `Chrome.trafficLightLeading` | 2 |
| `Chrome.trafficLightClearance` | 2 |
| `Chrome.titleStartOffset` | 2 |
| `Chrome.sheetCornerRadius` | 2 |
| `Chrome.settleFlight` | 2 |
| `Chrome.progressQuantum` | 2 |
| `Chrome.groupGap` | 2 |
| `Chrome.veilHeight` | 1 |
| `Chrome.trafficLightPitch` | 1 |
| `Chrome.titleEndOffset` | 1 |
| `Chrome.iconCapsuleInnerPadding` | 1 |
| `Chrome.gray` | 1 |
| `Chrome.dividerHeight` | 1 |
| `Chrome.contentHorizontalPadding` | 1 |

Plus `systemAccent`, read unqualified once inside `accent`.

**`contentBreathBelowChrome` never appears as `Chrome.contentBreathBelowChrome`** — it is read
unqualified, inside `contentTopInset` only. It is the one member with no external reader.

`WindowChrome.` members, **5 distinct**: `titlebarHeight` (4), `placeTrafficLights` (3),
`fitTitlebar` (3), `configure` (2), `titlebarContainer` (1).

---

## 19. Shipped vs. dead code

**SOURCE FACT.** Nothing in these three files is behind a compile condition, a feature flag or an
environment variable. There is no `#if DEBUG`, no `ProcessInfo.processInfo.environment` read and no
`@available` gate anywhere in `Chrome.swift`, `WindowChrome.swift` or `ShortcutTint.swift`.
Everything here is shipped, reachable code.

Two items are *effectively* inert rather than dead:

1. `Chrome.sheetInset = 0` — every arithmetic use adds zero. Shipped and reachable; no visual effect
   at the current value.
2. `PaneTopVeil`'s `.opacity(0.08 + 0.92 * progress)` floor of `0.08` — unreachable as a visible
   value, because the gradients only exist when `progress > 0`. Shipped, harmless.

**UNRESOLVED.** `CaptureRun.defaults` (read twice by `SidebarLayout`) is not defined in these files.
It is an optional `UserDefaults` with a `?? .standard` fallback, apparently a capture/test harness
suite. It changes *where* `"sidebarWidth"` and `"sidebarStartsOpen"` are stored, not any value above.
`PopoverMenu`, `PopoverItem`, `RowGlideAnimator` and `AppShortcut.Section` are likewise referenced
and defined elsewhere.

---

## 20. Upstream defects and awkward interactions to reproduce

**Record, do not fix** — a parity port copies behaviour.

1. **`GlassPickerButton` is 4pt shorter than its siblings.** It omits
   `.padding(.vertical, Chrome.capsuleVerticalPadding)`, so its capsule is 28pt tall while
   `ChromeTextMenu` and `ChromeTextButton` are 32pt. `Chrome.swift:877-879` vs `464-466` / `505-507`.
2. **`ChromeTextMenu` has no `accessibilityLabel`.** Every other chrome control with a `help` string
   also sets `.accessibilityLabel(Text(help))`; `ChromeTextMenu` sets only `.help(help)`
   (`Chrome.swift:475`).
3. **`ChromeTextButton`'s doc comment contradicts its code.** The comment (483-484) says a
   destructive button "reads as an ordinary chrome control at rest and turns red under the pointer";
   `foreground` (522-526) returns `Chrome.danger` at rest too, only at `0.92` instead of `1`.
4. **`PaneTopVeil`'s `0.08` opacity floor is unreachable** (section 10.2).
5. **Three animation systems describe one slide.** `Chrome.panelSlide` (spring 0.32/0.9, or ease-out
   0.18 under Reduce Motion), `WindowChrome.placeTrafficLights` (`NSAnimationContext` ease-out
   0.32 / 0.18) and `SidebarLayout.slideRun` (420 / 220 ms). They must be kept in step by hand;
   changing one leaves the traffic lights or the `isSliding` flag out of phase with the pane.
6. **`RowAccessoryIcon` hardcodes `20` instead of `Chrome.iconSize`** (`Chrome.swift:737`), as do
   `ChromeRow`'s leading `16` and `ChromeRowDivider`'s `16` where `Chrome.contentHorizontalPadding`
   (16) exists. The numbers agree today by coincidence, not by token.
7. **`contentTopInset` (72) and `veilHeight` (72) are equal by coincidence**, declared independently.
8. **`accentNSColor`'s fallback is `.controlAccentColor` directly**, not `NSColor(systemAccent)` — a
   second source for the same value.
9. **Two different icon-button diameters by design.** `ChromeCircleButton` / `ChromeCircleMenu` use
   `capsuleHeight` (32); `ChromeIconLabel` uses `capsuleContentHeight` (28). Not a defect, but a trap
   for a porter who assumes one icon-button size.
10. **`SidebarLayout` clamps to 0 during a drag but to 210 on release** (section 16.5), and
    `persist()` saves `restingWidth`, never the live `width`.
11. **Off a glass panel, `chromeGlassFieldCapsule()` has no hover response at all** — `isHovered` is
    written only in the `isOnGlassPanel` branch (section 7.2).
12. **`ChromeSection` uses SwiftUI's `.secondary` for its header, not `Chrome.secondaryText`**
    (`Chrome.swift:1003`), while every other secondary label in the file uses the token. The two
    resolve to the same `secondaryLabelColor` today, but through different mechanisms (hierarchical
    style vs. wrapped `NSColor`).
