using System.Collections.Immutable;
using System.Diagnostics;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Marrow.Core.Port;
using Marrow.UI.Common;
using Marrow.UI.Port;
using Marrow.UI.Theme;
using Marrow.UI.Theme.Wallpapers;

// Source: DroppyCode/UI/Chrome/Chrome.swift (B3 52bc792), :1-1137. B3 delta (lane A25, ED-200, PR-WIN-202,
// UI-WIN-005, UI-WIN-052): ChromeCard gains `lazy: Bool = false` (:1006-1027, was B2 :1006-1021, +6); WindowDragArea
// and WindowDragView (B2 :1085-1102, an NSViewRepresentable/NSView pair) collapse into one SwiftUI View,
// WindowDragArea (:1091-1099); NoWindowDragArea/NoWindowDragView are unchanged apart from their citation moving
// from B2 :1104-1140 to B3 :1101-1137. Net -3 lines.
// Parts 1 to 3 of 4 are ported: the window backdrop (:201-273), the surfaces (:125-199), the chrome controls
// (:306-560), the scroll model (:564-577), the search fields and the picker (:775-932), the veil and compact title
// (:579-647), the sidebar row family (:648-773), the settings card/section/row (:1006-1080), the drag regions
// (:1089-1137) and the tokens they read. Part 4 of 4 finishes the traffic-light geometry (TrafficLightPitch :17,
// TrafficLightsWidth :20) and proves UI-WIN-055 (the ChromeRowDivider composition rule, :1082-1087).
// Forms (ED-095, ED-098): a SwiftUI View is a Decorator whose constructor builds `body`; a ViewModifier is a sealed
// record with `Control Body(Control content)`, an IAnimatableModifier where a transition animates it; a Layout is a
// Panel; an NSView is a Control; NSImage is Avalonia.Media.IImage; AnyView is Control; a @ViewBuilder parameter is a
// Func<Control>. A parameter without a default that follows one with a default moves before it (ED-095).
// ENGINEERING DECISION (skeleton, no rule names the form): ButtonStyle (:276-280) is a sealed record with
// `Control MakeBody(Control label)`, `configuration.label` being the label; NoWindowDragArea's NSViewRepresentable
// (:1111-1117) is a Decorator exposing MakeNSView()/UpdateNSView(_) without the SwiftUI Context (B3/ED-200:
// WindowDragArea is no longer NSViewRepresentable - it collapses into one plain Control, :1091-1099); an NSEvent
// handler is the matching Avalonia pointer override, NSEvent is PointerEventArgs.
// The private top-level structs (:153, :187, :967) are `file` types, C#'s nearest form of a file-private type.
// View mechanics without an Avalonia counterpart of the same shape (a plain Button and TextField, `.background`,
// `.onHover`, `.popover`, `.fixedSize`, a font on any view, an animated colour) are in Port/PlainControls.cs.
// Settled by the orchestrator's run4 ruling on this unit's stops: Image(systemName:) and Image(_:) are SwiftImage
// (ED-179, ED-182; OD-0010 Lucide icons); SwiftGlass draws the app-owned material with the source's shape, tint and
// interaction data; `.continuous` shapes are SwiftShapes' (ED-181: Apple's continuous corner, closed by ED-275).

namespace Marrow.UI.Chrome
{
    /// <summary>
    /// :5-121 <c>enum Chrome</c>: tokens and building blocks. Also holds the file's extensions of library types
    /// (R-NAME-4): <c>extension View</c> (:125-149) and <c>extension AnyTransition</c> (:947-956, :990-1002);
    /// <c>extension Animation</c> (:958-961) is at the end of this file. RenderBudget.swift:89-99 adds
    /// <c>panelControlFill</c> as a partial.
    /// </summary>
    public static partial class Chrome
    {
        // MARK: Window

        public const double WindowCornerRadius = 26; // :8
        public const double SheetInset = 0; // :11
        public const double SheetCornerRadius = 16; // :13

        public const double TrafficLightDiameter = 14; // :15
        public const double TrafficLightSpacing = 9; // :16

        // :17 `static var trafficLightPitch: CGFloat { trafficLightDiameter + trafficLightSpacing }`
        public static double TrafficLightPitch => TrafficLightDiameter + TrafficLightSpacing;

        public const double TrafficLightLeading = 18; // :18
        public const double TrafficLightTop = 16; // :19

        // :20 `static var trafficLightsWidth: CGFloat { trafficLightDiameter * 3 + trafficLightSpacing * 2 }`
        public static double TrafficLightsWidth => TrafficLightDiameter * 3 + TrafficLightSpacing * 2;

        public const double TrafficLightClearance = 10; // :21

        // MARK: Sticky chrome row

        public static SwiftFont IconFont { get; } = SwiftFont.System(SwiftFont.TextStyle.Callout, weight: SwiftFont.Weight.Semibold); // :28
        public static SwiftFont InlineIconFont { get; } = SwiftFont.System(SwiftFont.TextStyle.Subheadline, weight: SwiftFont.Weight.Semibold); // :29
        public static SwiftFont ChevronFont { get; } = SwiftFont.System(8, SwiftFont.Weight.Bold); // :30
        public const double ChromeHorizontalPadding = 14; // :31
        public const double ChromeTopPadding = 12; // :32
        public const double CapsuleContentHeight = 28; // :33
        public const double CapsuleVerticalPadding = 2; // :34
        public const double CapsuleHorizontalPadding = 12; // :35
        public const double IconCapsuleInnerPadding = 3; // :36
        public const double DividerHeight = 14; // :37

        // :38
        public static double CapsuleHeight => CapsuleContentHeight + CapsuleVerticalPadding * 2;

        public const double ContentBreathBelowChrome = 28; // :39

        // :40 PR-WIN-212: numerically 72, equal to VeilHeight (:41) but an independent declaration; do not fold them.
        public static double ContentTopInset => ChromeTopPadding + CapsuleHeight + ContentBreathBelowChrome;

        public const double VeilHeight = 72; // :41
        public const double TitleStartOffset = 10; // :42
        public const double TitleEndOffset = 44; // :43
        public const double ProgressQuantum = 0.04; // :44

        // MARK: Sidebar

        public const double RowHeight = 28; // :48
        public const double RowCornerRadius = 7; // :49
        public const double RowHorizontalPadding = 8; // :50
        public const double IconSize = 20; // :51
        public const double SymbolSize = 14; // :52
        public const double GroupGap = 10; // :53
        public const double ListInset = 10; // :54

        // MARK: Cards

        public const double CardCornerRadius = 16; // :58
        public const double SectionSpacing = 20; // :59
        public const double SectionHeaderSpacing = 10; // :60
        public const double ContentHorizontalPadding = 16; // :61
        public const double RowControlTrailingPadding = 10; // :63
        public const double RowButtonLabelHeight = 16; // :67

        // MARK: Color and motion

        // :71-73
        public static SwiftColor Overlay(double opacity) => PrimaryText.Opacity(opacity);

        public static SwiftColor PrimaryText { get; } = SwiftColor.LabelColor; // :76
        public static SwiftColor SecondaryText { get; } = PrimaryText.Opacity(0.7); // :79
        private static SwiftColor SystemAccent { get; } = SwiftColor.ControlAccentColor; // :80

        // :85 PR-WIN-209: the theme's accent, or the system control accent for System/Light/Dark. DEVIATION OD-0088
        // (ED-250): the spec's accent is the user's pick when it is not the default; the value is ThemeManager.Accent, the
        // spec's accent ?? SystemAccent read whenever it is resolved, so what was drawn with it follows a later change.
        public static SwiftColor Accent => ThemeManager.Accent;

        // :87 accentNSColor: NSColor is SwiftColor too (ED-092); the AppKit twin falls back to the control accent
        // directly rather than to a wrapped copy (PR-WIN-209), so it is the same as Accent under ED-092 (and ED-250).
        public static SwiftColor AccentNSColor => ThemeManager.Accent;

        // :91 PR-WIN-209: re-read on every access, never cached.
        public static SwiftColor Success => ThemeManager.Spec.Success;

        // :92
        public static SwiftColor Warning => ThemeManager.Spec.Warning;

        // :93
        public static SwiftColor Danger => ThemeManager.Spec.Danger;

        // :95-96 The theme's glass tint, washed over capsules, sheets and the window.
        public static SwiftColor GlassTint => ThemeManager.Spec.Surface;

        // :98
        public static Animation Hover => Animation.EaseOut(duration: 0.1);

        // ED-NEW-segmenthover-1 (owner 2026-10-11): an icon in a glass pill or a round glass button lights from the secondary colour toward
        // the primary one under the pointer, on Hover; a segment of a pill also lifts a soft plate (an overlay wash, like RoundIconButton's)
        // behind just itself, a little stronger while it is pressed. Opacity only (no movement), so Reduce Motion leaves it as it is.
        public static SwiftColor IconForeground(bool lit) => lit ? PrimaryText : SecondaryText;

        public const double SegmentPlateHover = 0.1;

        public const double SegmentPlatePressed = 0.18;

        // The seam behind `NSWorkspace.shared.accessibilityDisplayShouldReduceMotion` (ED-084, port_impacts.md X4);
        // read live at each call, never cached, as the nine sites PR-WIN-208 names do.
        public static Ambient<IGeniePlatform> ReduceMotionPlatform { get; } = new(() => new WindowsGeniePlatform());

        // :100-104 PR-WIN-208: a spring, or a 0.18-second ease-out with Reduce Motion; read live at each query point.
        public static Animation PanelSlide => ReduceMotionPlatform.Current.AccessibilityDisplayShouldReduceMotion
            ? Animation.EaseOut(duration: 0.18)
            : Animation.Spring(response: 0.32, dampingFraction: 0.9);

        public static Spring GlideSpring { get; } = new(response: 0.6, dampingRatio: 0.86); // :108

        // :112-116 PR-WIN-208: the glide spring, or a 0.2-second ease-out with Reduce Motion.
        public static Animation SettleFlight => ReduceMotionPlatform.Current.AccessibilityDisplayShouldReduceMotion
            ? Animation.EaseOut(duration: 0.2)
            : Animation.Spring(GlideSpring);

        public static SwiftColor Gray { get; } = new(red: 0.556, green: 0.557, blue: 0.576); // :118
        public static SwiftColor Blue { get; } = new(red: 0.040, green: 0.478, blue: 1.000); // :119
        public static SwiftColor Orange { get; } = new(red: 1.000, green: 0.584, blue: 0.000); // :120

        // MARK: - Surfaces (extension View, :125-149)

        // :126-131 Capsule buttons stay glass, washed with a tad of the theme; flat on a glass panel.
        public static Control ChromeGlassCapsule(this Control content) =>
            new ChromeGlassSurface(ChromeGlassSurface.Kind.Capsule).Body(content);

        // :133-139 The capsule for a field: the same glass, without the press response.
        public static Control ChromeGlassFieldCapsule(this Control content) =>
            new ChromeGlassSurface(ChromeGlassSurface.Kind.Capsule, IsInteractive: false).Body(content);

        // :141-143
        public static Control ChromeGlassCircle(this Control content) =>
            new ChromeGlassSurface(ChromeGlassSurface.Kind.Circle).Body(content);

        // :145-148 The inset content sheet the detail pane floats on.
        public static Control DetailSheet(this Control content) => new DetailSheetModifier().Body(content);

        // extension AnyTransition (:947-956, :990-1002): `.softAppear` and `.settleGlide` read as SwiftTransition statics.
        extension(SwiftTransition)
        {
            // :949-955 PR-WIN-206: arrives softly, leaves with a quick 0.12s ease-out fade so removals never drag.
            public static SwiftTransition SoftAppear => SwiftTransition.Asymmetric(
                insertion: SwiftTransition.Modifier(active: new SoftAppearModifier(false), identity: new SoftAppearModifier(true)).Animation(Animation.SoftAppear),
                removal: SwiftTransition.Opacity.Animation(Animation.EaseOut(duration: 0.12)));

            // :994-1001 PR-WIN-207: arrives softly; leaves on the glide spring, gone before it would land.
            public static SwiftTransition SettleGlide => SwiftTransition.Asymmetric(
                insertion: SwiftTransition.Modifier(active: new SoftAppearModifier(false), identity: new SoftAppearModifier(true)).Animation(Animation.SoftAppear),
                removal: SwiftTransition.Modifier(active: new SettleGlideModifier(1), identity: new SettleGlideModifier(0)).Animation(Animation.Spring(GlideSpring)));
        }
    }

    // Image(systemName:) at a chrome site: SwiftImage (ED-179, ED-182) with the site's `.font`. It fills the frame a site
    // gives it and draws its square centred there (SwiftImage.Destination), as SwiftUI centres an image in a larger
    // frame, without layout rounding moving the glyph off the centre.
    file static class Symbols
    {
        public static SwiftImage Image(string systemName, SwiftFont font) => SwiftImage.SystemName(systemName).Font(font);
    }

    // :151-185 private struct ChromeGlassSurface: ViewModifier
    file sealed record ChromeGlassSurface(ChromeGlassSurface.Kind Shape, bool IsInteractive = true) // :155, :157
    {
        public enum Kind { Capsule, Circle } // :154

        // :160 @State private var isHovered = false
        private sealed class ViewState : MainActorModel
        {
            private bool isHovered;

            public bool IsHovered { get => Read(isHovered); set => Set(ref isHovered, value); }
        }

        // :162-169
        public Control Body(Control content) => Shape switch
        {
            Kind.Capsule => Surface(content, Kind.Capsule), // :164-165 Capsule(style: .continuous)
            Kind.Circle => Surface(content, Kind.Circle), // :166-167 Circle()
            _ => throw new UnreachableException(),
        };

        // :171-184
        private Control Surface(Control content, Kind shape)
        {
            // The shape filled behind the content. It takes no presses: those belong to the control on it, or to the
            // no-drag region behind the whole control.
            var fill = new Border { IsHitTestVisible = false };
            var host = new Panel { Children = { fill, content } };
            GlassShape glassShape;
            if (shape == Kind.Circle)
            {
                // :166-167 Circle(): the largest circle centred in the frame.
                glassShape = GlassShape.Circle;
                fill.HorizontalAlignment = HorizontalAlignment.Center;
                fill.VerticalAlignment = VerticalAlignment.Center;
                host.SizeChanged += (_, e) =>
                {
                    var side = Math.Min(e.NewSize.Width, e.NewSize.Height);
                    fill.CornerRadius = new CornerRadius(side / 2);
                    fill.Width = side;
                    fill.Height = side;
                };
            }
            else
            {
                glassShape = GlassShape.Capsule(RoundedCornerStyle.Continuous); // :164-165 Capsule(style: .continuous)
                fill.Capsule(RoundedCornerStyle.Continuous);
            }
            var state = new ViewState();
            var paint = new AnimatedColor(fill);
            fill.Background = paint.Brush;

            void Draw(Animation? animation)
            {
                // :158 @Environment(\.isOnGlassPanel), :159 @Environment(\.colorScheme)
                if (RenderBudget.GetIsOnGlassPanel(host))
                {
                    // :173-178 the panel's flat control fill, a shade brighter under the pointer: no glass.
                    SwiftGlass.SetEffect(fill, null);
                    paint.Set(
                        SwiftColor.Dynamic(
                            light: Chrome.PanelControlFill(isDark: false, hovered: state.IsHovered),
                            dark: Chrome.PanelControlFill(isDark: true, hovered: state.IsHovered)),
                        animation);
                }
                else
                {
                    // :179-182 `.glassEffect(.regular.tint(Chrome.glassTint.opacity(0.3))[.interactive()], in: shape)`.
                    // SwiftGlass draws the material before this source tint layer (OD-0005).
                    var glass = GlassEffect.Regular(glassShape).Tint(Chrome.GlassTint, 0.3);
                    SwiftGlass.SetEffect(fill, IsInteractive ? glass.Interactive() : glass); // :180 interactive, :182 not
                    paint.Set(Chrome.GlassTint.Opacity(0.3), animation);
                }
            }

            // :177-178 `.onHover { isHovered = $0 }` with `.animation(Chrome.hover, value: isHovered)`.
            host.OnHover(hovering =>
            {
                if (state.IsHovered == hovering) return;
                state.IsHovered = hovering;
                if (RenderBudget.GetIsOnGlassPanel(host)) Draw(Chrome.Hover);
            });
            host.PropertyChanged += (_, e) =>
            {
                if (e.Property == RenderBudget.IsOnGlassPanelProperty) Draw(Transaction.Current);
            };
            Draw(null);
            return host;
        }
    }

    // :187-199 private struct DetailSheetModifier: ViewModifier
    file sealed record DetailSheetModifier
    {
        // :190-198
        public Control Body(Control content)
        {
            // :191-192 Square: the window's own clip rounds the sheet's outer corners with everything else.
            var wash = new Rectangle();
            wash.Fill = SwiftColor.Dynamic(light: SwiftColor.White.Opacity(0.3), dark: SwiftColor.Black.Opacity(0.22)).Brush(wash); // :195
            var tint = new Rectangle();
            // ED-254 (OD-0088): Chrome.glassTint read whenever it is drawn (ThemeManager.LiveSurface), so the wash follows the theme.
            tint.Fill = SwiftColor.Dynamic(light: ThemeManager.LiveSurface.Opacity(0.16), dark: ThemeManager.LiveSurface.Opacity(0.22)).Brush(tint); // :196
            return new Panel { Children = { wash, tint, content } }; // :194 .background { ... }
        }
    }

    /// <summary>
    /// :201-273. One shaped surface for the whole window: glass or a solid fill, a legibility tint and a hairline.
    /// <para>
    /// PORT-ONLY (OD-0075, owner 2026-10-02: "Window corner: the wedge is filled with the window's scrim"; the owner's intent of 2026-10-05: Windows rounds a
    /// window's corner less than the app's glass shape, so the glass is EXTENDED to the window's edge and reads as one glass surface out to the OS edge;
    /// ED-338). The source draws every layer in the window's own shape (:218 <c>RoundedRectangle(cornerRadius: Chrome.windowCornerRadius, style:
    /// .continuous)</c>), and a Windows window is the system's Acrylic in a rectangle that DWM rounds by 8 px at most. So the layers that FILL (the scrim, the solid
    /// fill, the tint, the wallpaper and its glass) fill the rectangle of the backdrop, and the window (RootView, SettingsView, the guide panels) clips them with
    /// its own rectangle, which DWM then rounds: the corner outside the 26 pt curve is the same stack of layers as the body inside it, not a second paint that
    /// has to match. The HAIRLINE (:266-271) is the glass's edge, and the glass now ends at the window's edge: it is the rectangle's inner stroke, which DWM's
    /// rounding cuts at the corner. (At 26 pt it ran as an arc through the extended glass, reading as a second edge a few pixels inside the window's.)
    /// </para>
    /// </summary>
    public sealed class WindowBackdrop : Decorator
    {
        private enum Base { Wallpaper, LibraryWallpaper, Solid, Glass }

        private readonly Panel group = new(); // :225 Group
        private double opacity;
        private IImage? wallpaper;
        private WallpaperSelection library = new();
        private WallpaperView? libraryView;
        private readonly bool overAppContent;
        private Base? drawn;
        private Control? baseView;
        private Image? picture;
        private SwiftShapeBorder? scrim;
        private Panel? wallpaperOverlay;
        private AnimatedScalar? scrimGlide;

        /// <param name="opacity">:205 <c>let opacity: Double</c>.</param>
        /// <param name="wallpaper">:207 <c>var wallpaper: NSImage? = nil</c>.</param>
        /// <param name="overAppContent">PORT-ONLY (OD-0005 (b), ED-333): the backdrop lies over the app's own window, not the desktop
        /// (the scripts sheet's <c>.presentationBackground</c>, ChatView.swift:1459-1463), so its glass is the app's material, which frosts what
        /// the window drew under it, instead of the window's Acrylic.</param>
        /// <param name="roundedHairline">PORT-ONLY (ED-338): for a backdrop that is NOT a window's whole content but is clipped by its own 26 pt shape (the scripts sheet,
        /// ChatView.cs): the hairline is the source's inner stroke of that shape (:218, :266-271), so it follows the curve. A window's backdrop leaves it false: its hairline is the
        /// rectangle's, because the glass reaches the window's edge and the OS rounds it.</param>
        // :217-272 body
        public WindowBackdrop(double opacity, IImage? wallpaper = null, bool overAppContent = false, bool roundedHairline = false)
        {
            this.opacity = opacity;
            this.wallpaper = wallpaper;
            this.overAppContent = overAppContent;
            var tint = Fill();
            tint.Background = ThemeManager.LiveSurface.Opacity(0.12).Brush(tint); // :263-265, Chrome.glassTint as drawn now (ED-254)
            // :266-271 The hairline sits on the glass as well as on the solid fill; strokeBorder is an inner stroke,
            // which is how an Avalonia Border draws its border.
            var hairline = roundedHairline ? Shape() : Fill();
            hairline.BorderThickness = new Thickness(1);
            hairline.BorderBrush = Chrome.Overlay(0.14).Brush(hairline);
            Child = new Panel { Children = { group, tint, hairline } };
            ActualThemeVariantChanged += (_, _) => Turn(); // :203 @Environment(\.colorScheme)
            Draw();
        }

        // ED-254 (OD-0088, OD-0076): the scrim's strength follows the appearance (:258, 0.26 dark, 0.18 light); in a tree
        // that turns over (AppearanceTurn, the Settings window) it eases there with the window's colours instead of
        // changing in one frame.
        private Action stopTurn = static () => { };

        private void Turn()
        {
            var from = scrim?.Opacity;
            Draw();
            if (scrim is not { } layer || from is not { } start || layer.Opacity == start || !AppearanceTurn.Eases(this)) return;
            var end = layer.Opacity;
            layer.Opacity = start;
            stopTurn = AppearanceTurn.Run(this, progress => layer.Opacity = start + (end - start) * progress);
        }

        /// <summary>OD-0110: a library backdrop shares the custom picture's glass and opacity, preserving the picture underneath.</summary>
        public void UpdateLibrary(WallpaperSelection selection)
        {
            var hadLibrary = library.Kind != WallpaperKind.None;
            library = WallpaperLibraryStore.Normalize(selection);
            if (hadLibrary != (library.Kind != WallpaperKind.None))
                Transaction.WithAnimation(Animation.EaseInOut(duration: .3), Draw);
            else Draw();
        }

        /// <summary>
        /// The parent passing new inputs (R-VIEW-1): <c>WindowBackdrop(opacity:wallpaper:)</c> built again from
        /// RootView.swift:64. Setting or clearing the wallpaper animates with <c>.easeInOut(duration: 0.3)</c> (:262).
        /// </summary>
        public void Update(double opacity, IImage? wallpaper)
        {
            var hadWallpaper = this.wallpaper is not null;
            var before = scrim; var shown = scrim?.Opacity;
            this.opacity = opacity;
            this.wallpaper = wallpaper;
            if (hadWallpaper != wallpaper is not null)
                Transaction.WithAnimation(Animation.EaseInOut(duration: 0.3), Draw); // :262 .animation(_, value: wallpaper != nil)
            else
            {
                Draw();
                // DEVIATION OD-0076 (ED-280): `withAnimation(.snappy(duration: 0.2))` on the transparency (SettingsView.swift:546) reaches the
                // scrim through this input; the scrim glides to its new strength in that transaction instead of cutting there.
                if (Transaction.Current is { } animation && shown is { } start && ReferenceEquals(before, scrim) && scrim is { } layer && layer.Opacity != start)
                {
                    var end = layer.Opacity; scrimGlide!.Set(start, null); scrimGlide.Set(end, animation);
                }
            }
        }

        // :220-224 `opacity >= 0.98`: from here up the window is a solid fill, not glass under a scrim (Draw, and the corner wedge's fill).
        public static bool IsSolid(double opacity) => opacity >= 0.98;

        // :211-215 static func scrim(for:isDark:)
        public static double Scrim(double @for, bool isDark)
        {
            var stock = isDark ? 0.26 : 0.18; // :212
            var floored = 0 >= @for ? 0 : @for; // :213 max(opacity, 0)
            var t = 1 < floored ? 1 : floored; // :213 min(_, 1)
            return t <= 0.5 ? stock * (t / 0.5) : stock + (1 - stock) * ((t - 0.5) / 0.5); // :214
        }

        // :218 RoundedRectangle(cornerRadius: Chrome.windowCornerRadius, style: .continuous), the shape of a backdrop clipped to its own curve (roundedHairline).
        private static SwiftShapeBorder Shape() => new SwiftShapeBorder().RoundedRectangle(Chrome.WindowCornerRadius, RoundedCornerStyle.Continuous);

        // PORT-ONLY (OD-0075, ED-338): every layer of the backdrop is the window's rectangle, out to the window's edge; see the class summary.
        // :218 `RoundedRectangle(cornerRadius: Chrome.windowCornerRadius, style: .continuous)` is the rectangle the OS then rounds.
        private static SwiftShapeBorder Fill() => new();

        // `shape.fill(.clear).glassEffect(in: shape)` (:243-245, :252-254): the default glass, no tint, no .interactive()
        // and no container, drawn over the app-owned backdrop (OD-0005, ADR-0002 Glass (b)).
        private static SwiftShapeBorder GlassLayer()
        {
            var layer = Fill();
            SwiftGlass.SetEffect(layer, GlassEffect.Regular(GlassShape.RoundedRectangle(0, RoundedCornerStyle.Continuous))); // the rectangle: see Fill
            return layer;
        }

        private void Draw()
        {
            stopTurn();
            stopTurn = static () => { };
            var isDark = PlainControls.IsDark(ActualThemeVariant); // :219
            var isSolid = IsSolid(opacity); // :220-224
            var next = library.Kind != WallpaperKind.None ? Base.LibraryWallpaper : wallpaper is not null ? Base.Wallpaper : isSolid ? Base.Solid : Base.Glass; // :226, :249, :251
            if (next != drawn)
            {
                if (baseView is { } old) SwiftTransition.Remove(group, old);
                baseView = next switch
                {
                    Base.Wallpaper => WallpaperBase(),
                    Base.LibraryWallpaper => WallpaperBase(true),
                    Base.Solid => SolidBase(),
                    Base.Glass => GlassBase(),
                    _ => throw new UnreachableException(),
                };
                drawn = next;
                SwiftTransition.Insert(group, group.Children.Count, baseView);
            }
            if (picture is not null && wallpaper is { } image)
            {
                picture.Source = image; // :234
                picture.Opacity = opacity; // :238
            }
            if (libraryView is not null) { libraryView.Selection = library; libraryView.Opacity = opacity; }
            if (scrim is not null) { scrimGlide ??= new AnimatedScalar(this, 0, value => { if (scrim is { } layer) layer.Opacity = value; }); scrimGlide.Set(Scrim(opacity, isDark), null); } // :258 (a running glide stops here)
            if (wallpaperOverlay is { } overlay)
            {
                // :241-247 `.overlay { if !isSolid { ... } }`
                if (!isSolid && overlay.Children.Count == 0) overlay.Children.Add(GlassLayer());
                else if (isSolid && overlay.Children.Count != 0) overlay.Children.Clear();
            }
        }

        // :226-248 The picture rides over the desktop: the setting is its own opacity, and no scrim is drawn here.
        private Control WallpaperBase(bool builtIn = false)
        {
            libraryView = builtIn ? new WallpaperView { Selection = library } : null;
            picture = builtIn ? null : new Image { Stretch = Stretch.UniformToFill }; // :235, :237 .resizable().aspectRatio(contentMode: .fill)
            if (picture is not null) RenderOptions.SetBitmapInterpolationMode(picture, BitmapInterpolationMode.HighQuality); // :236 .interpolation(.high)
            scrim = null;
            var clip = Fill(); // :232 shape.fill(.clear)
            clip.ClipToBounds = true; // :240 .clipShape(shape)
            clip.Child = (Control?)libraryView ?? picture; // :233 .overlay
            // :241-247 `.overlay { if !isSolid { shape.fill(.clear).glassEffect(in: shape) } }`: over the picture the app
            // draws its own backdrop, where OD-0005 calls for the app-drawn material (ADR-0002 Glass (b)); Draw lays the
            // glass layer (GlassLayer) in this overlay while the setting is below 0.98.
            wallpaperOverlay = new Panel();
            return new Panel { Children = { clip, wallpaperOverlay } }.Transition(SwiftTransition.Opacity); // :248 .transition(.opacity)
        }

        // :249-250 `shape.fill(isDark ? Color.black : Color.white)`
        private Control SolidBase()
        {
            picture = null;
            libraryView = null;
            scrim = null;
            wallpaperOverlay = null;
            var solid = Fill();
            solid.Background = SwiftColor.Dynamic(light: SwiftColor.White, dark: SwiftColor.Black).Brush(solid);
            return solid;
        }

        // :251-259 glass under the scrim
        private Control GlassBase()
        {
            picture = null;
            libraryView = null;
            wallpaperOverlay = null;
            // :252-254 `shape.fill(.clear).glassEffect(in: shape)`: with no wallpaper the window shows the desktop, and
            // there the glass is the window's own Acrylic (OD-0005, ADR-0002 Glass (a), OD-0014), so the app paints
            // the source scrim over native Acrylic, with no desktop sampling or app frost on the window base. A backdrop over
            // the app's own content (the scripts sheet's) has the app's window under it, where OD-0005 (b) calls for the
            // app-drawn material: its glass layer is the material's.
            scrim = Fill();
            scrim.Background = SwiftColor.Dynamic(light: SwiftColor.White, dark: SwiftColor.Black).Brush(scrim); // :256-257
            var glass = GlassLayer();
            if (!overAppContent)
            {
                SwiftGlass.SetUsesDesktopBackdrop(glass, true); // OD-0014 keeps upstream's clear default.
                // ED-358: the window's edge under the scrim; sheets keep their app-content material.
                glass.Child = new WindowRim();
            }
            return new Panel { Children = { glass, scrim } }; // :255 .overlay
        }
    }

    /// <summary>
    /// PORT-ONLY (ED-358, OD-0107 and its amendment): the edge the Mac's window glass draws (<c>shape.fill(.clear).glassEffect(in: shape)</c>,
    /// Chrome.swift:252-254) where the port's window glass is the system's Acrylic, which draws none (OD-0005: no screen capture). Only the
    /// app's own light, no sampling: a specular rim on the window's first point and a light inner edge falling over the next three, brightest
    /// along the top. Measured on Mac frame 01 (the main window at rest, the transparency at its least, so no scrim; 1 px = 1 pt), the step over
    /// the window's body: the first point +44 at the top, +41 at the bottom, +40 and +32 at the sides, of which the hairline over it
    /// (Chrome.overlay(0.14), :266-271) draws about +22; the next points +8, +3, +2 at the top, +4 at the bottom, +2 at the sides.
    /// <para>
    /// The edge is the window's real one (ED-338): the glass fills the window's rectangle and DWM rounds it, so the rim is the rectangle's
    /// inner rings, as the hairline is, and DWM's rounding cuts them at the corner, where it draws its own border on its curve. The light inner edge
    /// (the rings past the first) runs along the straight edges and fades out over <see cref="Fade"/> before the 26 pt corners (ENGINEERING DECISION):
    /// within a corner the window shows the body out to its edge (OD-0075, CornerSeam's proof); the Mac draws it round its 26 pt corner.
    /// </para>
    /// </summary>
    public sealed class WindowRim : Control
    {
        /// <summary>
        /// White over the glass, per point inside the edge (0 the first, the specular rim; 1 to 3 the light inner edge), along the top, the sides
        /// and the bottom: the frame-01 steps as alpha (a step s over a body b is s / (255 - b): first points 0.235, 0.21, 0.21; then 0.042, 0.016,
        /// 0.009 at the top, 0.023 at the bottom, 0.008 at the sides), less the hairline's 0.119 over them, through the tint's 0.88 over them.
        /// </summary>
        internal static readonly (double Top, double Side, double Bottom)[] Rings =
        [
            (0.150, 0.110, 0.117),
            (0.048, 0.010, 0.026),
            (0.018, 0.004, 0.009),
            (0.010, 0.0, 0.0045),
        ];

        /// <summary>The hairline's white (Chrome.overlay(0.14): the label colour's 0.85 at 0.14) and the tint's share left over the rim (1 - 0.12).</summary>
        internal const double Hairline = 0.85 * 0.14, UnderTint = 0.88;

        /// <summary>
        /// The first point along the top where the system covers the window's first row (<see cref="TopCover"/>): the rim there also carries the hairline's
        /// light the system hides, so the first row it leaves reads as the Mac's first point (0.235).
        /// </summary>
        internal static double CoveredTop => (1 - (1 - UnderTint * Rings[0].Top) * (1 - Hairline)) / UnderTint;

        /// <summary>How far the light inner edge fades out before the 26 pt corner, in points.</summary>
        internal const double Fade = 12;

        public WindowRim() => IsHitTestVisible = false;

        /// <summary>
        /// How much of the window's top the system covers: on Windows a window that extends its client area into the frame (WindowChrome,
        /// ExtendClientAreaToDecorationsHint) keeps the system's 1 px frame line over the client's first row while it is not maximised; at the
        /// sides and the bottom that line lies outside the client. Measured on the real main window (GlassPanelAndRimNativeTests): the first row
        /// reads 0 with and without this edge, and the hairline under it does not show. The Mac's own dark outline lies outside its rim on every
        /// side, so along the top the rim starts on the first row the system leaves (ENGINEERING DECISION).
        /// </summary>
        internal double TopCover() =>
            OperatingSystem.IsWindows() && TopLevel.GetTopLevel(this) is Window { ExtendClientAreaToDecorationsHint: true, WindowState: WindowState.Normal } window &&
            window.TryGetPlatformHandle() is { HandleDescriptor: "HWND" } // a real Win32 window, which has the frame line (a headless one has none)
                ? 1 / window.RenderScaling
                : 0;

        private static Color White(double a) => Color.FromArgb((byte)Math.Round(255 * Math.Clamp(a, 0, 1)), 255, 255, 255);

        // A 1 pt line from `from` to `to`, `alpha` along it; with `fade` it fades in over Fade from each end (the light inner edge).
        private static void Line(DrawingContext context, Point from, Point to, double alpha, bool fade)
        {
            if (alpha <= 0) return;
            var length = Math.Sqrt((to.X - from.X) * (to.X - from.X) + (to.Y - from.Y) * (to.Y - from.Y));
            if (length <= 0) return;
            IBrush brush = new SolidColorBrush(White(alpha));
            if (fade)
            {
                var f = Math.Clamp(Fade / length, 0, 0.5);
                brush = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(from, RelativeUnit.Absolute),
                    EndPoint = new RelativePoint(to, RelativeUnit.Absolute),
                    GradientStops =
                    {
                        new GradientStop(White(0), 0), new GradientStop(White(alpha), f), new GradientStop(White(alpha), 1 - f), new GradientStop(White(0), 1),
                    },
                };
            }
            context.DrawLine(new Pen(brush, 1, lineCap: PenLineCap.Flat), from, to);
        }

        public override void Render(DrawingContext context)
        {
            var (w, h) = (Bounds.Width, Bounds.Height);
            if (w <= 8 || h <= 8) return;
            var cover = TopCover();
            var corner = Chrome.WindowCornerRadius;
            for (var i = 0; i < Rings.Length; i++)
            {
                var (top, side, bottom) = Rings[i];
                var d = i + 0.5; // a 1 pt line centred on the point
                if (i == 0)
                {
                    // The specular rim: the rectangle's first ring, out to the corners (DWM's rounding cuts it there).
                    Line(context, new Point(0, cover + d), new Point(w, cover + d), cover > 0 ? CoveredTop : top, fade: false);
                    Line(context, new Point(0, h - d), new Point(w, h - d), bottom, fade: false);
                    Line(context, new Point(d, cover + 1), new Point(d, h - 1), side, fade: false);
                    Line(context, new Point(w - d, cover + 1), new Point(w - d, h - 1), side, fade: false);
                    continue;
                }
                // The light inner edge, along the straight edges between the 26 pt corners.
                Line(context, new Point(corner, cover + d), new Point(w - corner, cover + d), top, fade: true);
                Line(context, new Point(corner, h - d), new Point(w - corner, h - d), bottom, fade: true);
                Line(context, new Point(d, corner), new Point(d, h - corner), side, fade: true);
                Line(context, new Point(w - d, corner), new Point(w - d, h - corner), side, fade: true);
            }
        }
    }

    // :275-280 struct ChromeQuietButtonStyle: ButtonStyle
    public sealed record ChromeQuietButtonStyle
    {
        // :277-279 makeBody(configuration:); label is configuration.label. PR-WIN-227: the label unchanged, so
        // pressing shows no dimming, highlight, scale or opacity change; keyboard activation and the button
        // automation peer are the label's Button, unaffected by this style.
        public Control MakeBody(Control label) => label;
    }

    /// <summary>:282-304 <c>struct ChromeRevealLayout: Layout</c>. Reveals its single child by height.</summary>
    public sealed class ChromeRevealLayout : Panel
    {
        private double fraction;
        private Size natural;

        // Memberwise init ChromeRevealLayout(fraction:) (ModelsSettingsPage.swift:370, SettingsView.swift:671).
        public ChromeRevealLayout(double fraction) => this.fraction = fraction;

        // :287 var fraction: CGFloat
        public double Fraction
        {
            get => fraction;
            set { fraction = value; InvalidateMeasure(); }
        }

        // :289-292 var animatableData: CGFloat; PR-WIN-229 the layout's animatable value, so a panelSlide transaction
        // interpolates the slot height frame by frame while the child stays still.
        public double AnimatableData
        {
            get => fraction;
            set => Fraction = value;
        }

        // :294-298 sizeThatFits(proposal:subviews:cache:). PR-WIN-228: the slot is the child's natural width for the
        // proposed width, and its natural height (measured with no height proposed) times fraction clamped to 0...1.
        protected override Size MeasureOverride(Size availableSize)
        {
            if (Children.Count == 0) return default;
            var child = Children[0];
            child.Measure(new Size(availableSize.Width, double.PositiveInfinity));
            natural = child.DesiredSize;
            var clamped = Math.Clamp(fraction, 0, 1);
            return new Size(natural.Width, natural.Height * clamped);
        }

        // :300-303 placeSubviews(in:proposal:subviews:cache:). PR-WIN-229: the child is always placed at the slot's
        // origin, anchored top-leading, at its full natural height, so it never moves and stays mounted at fraction 0.
        protected override Size ArrangeOverride(Size finalSize)
        {
            if (Children.Count == 0) return finalSize;
            var child = Children[0];
            child.Arrange(new Rect(new Point(0, 0), new Size(finalSize.Width, natural.Height)));
            return finalSize;
        }
    }

    // MARK: - Chrome controls

    // :308-322 struct ChromeCapsule<Content: View>: View
    public sealed class ChromeCapsule : Decorator
    {
        /// <param name="content">:309 <c>@ViewBuilder var content: Content</c>.</param>
        // :311-321 body
        public ChromeCapsule(Func<Control> content)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 0, Children = { content() } }; // :312-314
            var padded = new Border
            {
                Padding = new Thickness(Chrome.IconCapsuleInnerPadding, Chrome.CapsuleVerticalPadding), // :315-316
                Child = row,
            };
            // :317 .chromeGlassCapsule(); :318-320 the capsule is a control wherever it sits: presses on it work the
            // control and never drag the window.
            Child = PlainControls.Background(padded.ChromeGlassCapsule(), new NoWindowDragArea());
        }
    }

    // :324-331 struct ChromeDivider: View
    public sealed class ChromeDivider : Decorator
    {
        // :325-330 body
        public ChromeDivider()
        {
            var bar = new Border { Width = 1, Height = Chrome.DividerHeight }.RoundedRectangle(0.5, RoundedCornerStyle.Continuous); // :326, :328
            bar.Background = Chrome.PrimaryText.Opacity(0.22).Brush(bar); // :327
            AutomationProperties.SetAccessibilityView(this, AccessibilityView.Raw); // :329 .accessibilityHidden(true)
            Child = bar;
        }
    }

    // :333-359 struct ChromeIconLabel: View
    public sealed class ChromeIconLabel : Decorator
    {
        private readonly Panel frame;
        private readonly AnimatedColor foreground;
        private bool isEnabled;
        private bool isActive;
        private bool isHovering;
        private bool isPressed;
        private Ellipse? activeCircle;
        private readonly AnimatedColor plate;

        /// <param name="symbol">:334 <c>let symbol: String</c>.</param>
        /// <param name="isEnabled">:335 <c>var isEnabled = true</c>.</param>
        /// <param name="isActive">:336 <c>var isActive = false</c>.</param>
        /// <param name="isHovering">:337 <c>var isHovering = false</c>.</param>
        // :339-352 body
        public ChromeIconLabel(string symbol, bool isEnabled = true, bool isActive = false, bool isHovering = false)
        {
            var image = Symbols.Image(symbol, Chrome.IconFont); // :340-341
            foreground = new AnimatedColor(image);
            image.SetValue(TextElement.ForegroundProperty, foreground.Brush); // :342
            // PORT-ONLY (ED-NEW-segmenthover-1): the hover plate of this segment, a rounded wash behind the glyph that is always mounted
            // and only changes its alpha, so moving the pointer from one segment to the next crossfades the two. Its corner radius is half
            // the frame, so in a capsule it is concentric with the pill's own curve at the ends; the pill's padding (3 pt at the sides, 2 pt
            // above and below) is its inset from the pill's edge.
            var plateShape = new Border { CornerRadius = new CornerRadius(Chrome.CapsuleContentHeight / 2), IsHitTestVisible = false, Name = "SegmentPlate" };
            plate = new AnimatedColor(plateShape);
            plateShape.Background = plate.Brush;
            AutomationProperties.SetAccessibilityView(plateShape, AccessibilityView.Raw);
            frame = new Panel
            {
                Width = Chrome.CapsuleContentHeight, // :343
                Height = Chrome.CapsuleContentHeight,
                Background = Brushes.Transparent, // :351 .contentShape(.rect)
                Children = { plateShape, image },
            };
            Child = frame;
            Update(isEnabled, isActive, isHovering);
        }

        /// <summary>The parent passing new inputs (R-VIEW-1): the owning button's hover and presentation state.</summary>
        public void Update(bool isEnabled, bool isActive, bool isHovering, bool isPressed = false)
        {
            this.isEnabled = isEnabled;
            this.isActive = isActive;
            this.isHovering = isHovering;
            this.isPressed = isPressed;
            foreground.Set(Foreground, Transaction.Current);
            plate.Set(Chrome.Overlay(Plate), Transaction.Current);
            // :344-350 `.background { if isActive { Circle().fill(Chrome.overlay(0.12)).padding(1) } }`
            if (isActive && activeCircle is null)
            {
                activeCircle = new Ellipse { Margin = new Thickness(1) };
                activeCircle.Fill = Chrome.Overlay(0.12).Brush(activeCircle);
                SwiftTransition.Insert(frame, 0, activeCircle);
            }
            else if (!isActive && activeCircle is { } circle)
            {
                activeCircle = null;
                SwiftTransition.Remove(frame, circle);
            }
        }

        // :354-358
        private SwiftColor Foreground
        {
            get
            {
                if (!isEnabled) return Chrome.PrimaryText.Opacity(0.32); // :355
                if (isActive) return Chrome.PrimaryText; // :356
                return Chrome.IconForeground(isHovering || isPressed); // :357, ED-NEW-segmenthover-1: secondary at rest, primary under the pointer
            }
        }

        // The plate's alpha (of the overlay colour, so a fade never passes through another hue): none at rest or when disabled.
        private double Plate => !isEnabled ? 0 : isPressed ? Chrome.SegmentPlatePressed : isHovering ? Chrome.SegmentPlateHover : 0;
    }

    // :361-383 struct ChromeIconButton: View
    public sealed class ChromeIconButton : Decorator
    {
        // :368 @State private var isHovering = false
        private sealed class ViewState : MainActorModel
        {
            private bool isHovering;
            private bool isPressed;

            public bool IsHovering { get => Read(isHovering); set => Set(ref isHovering, value); }

            public bool IsPressed { get => Read(isPressed); set => Set(ref isPressed, value); }
        }

        /// <summary>Swift members symbol, isEnabled, isActive, help, action (:362-366); help and action have no
        /// default, so they move before the defaulted ones (ED-095) and callers name the labels.</summary>
        // :370-382 body
        public ChromeIconButton(string symbol, string help, Action action, bool isEnabled = true, bool isActive = false)
        {
            state = new ViewState();
            this.isEnabled = isEnabled;
            this.isActive = isActive;
            label = new ChromeIconLabel(symbol, isEnabled, isActive, isHovering: false); // :372
            label.Track(() => (state.IsHovering, state.IsPressed), current => label.Update(this.isEnabled, this.isActive, current.IsHovering, current.IsPressed));
            var button = PlainControls.Button(label, action); // :371, :374 .buttonStyle(.plain)
            button.IsEnabled = isEnabled; // :375 .disabled(!isEnabled)
            button.OnHover(hovering => Transaction.WithAnimation(Chrome.Hover, () => state.IsHovering = hovering)); // :376-378
            button.OnPressed(pressed => Transaction.WithAnimation(Chrome.Hover, () => state.IsPressed = pressed)); // ED-NEW-segmenthover-1
            ToolTip.SetTip(button, help); // :379 .help(help)
            AutomationProperties.SetName(button, help); // :380 .accessibilityLabel(Text(help))
            Child = PlainControls.Background(button, new NoWindowDragArea()); // :381
        }

        private readonly ViewState state;
        private readonly ChromeIconLabel label;
        private readonly bool isEnabled;
        private bool isActive;

        /// <summary>PORT-ONLY (ED-278 (5)). SwiftUI re-evaluates a parent's body and the same button keeps its identity,
        /// its hover state and its place; the new <c>isActive</c> changes its fill and its glyph's colour under the transaction in force
        /// (a column's `.animation(Chrome.panelSlide, value:)`, ChatView.swift:290), so the "on" circle fades in and out.</summary>
        public void Update(bool isActive)
        {
            if (this.isActive == isActive) return;
            this.isActive = isActive;
            label.Update(isEnabled, isActive, state.IsHovering, state.IsPressed);
        }
    }

    // :385-410 struct ChromeMenuButton<Content: View>: View
    public sealed class ChromeMenuButton : Decorator
    {
        // :390-391 @State private var isHovering = false, isPresented = false
        private sealed class ViewState : MainActorModel
        {
            private bool isHovering;
            private bool isPresented;
            private bool isPressed;

            public bool IsHovering { get => Read(isHovering); set => Set(ref isHovering, value); }

            public bool IsPresented { get => Read(isPresented); set => Set(ref isPresented, value); }

            public bool IsPressed { get => Read(isPressed); set => Set(ref isPressed, value); }
        }

        /// <param name="symbol">:386 <c>let symbol: String</c>.</param>
        /// <param name="help">:387 <c>let help: String</c>.</param>
        /// <param name="content">:388 <c>@ViewBuilder var content: Content</c>.</param>
        // :393-409 body
        public ChromeMenuButton(string symbol, string help, Func<Control> content)
        {
            var state = new ViewState();
            var label = new ChromeIconLabel(symbol, isActive: false, isHovering: false); // :397
            label.Track(() => (state.IsPresented, state.IsHovering, state.IsPressed), shown => label.Update(isEnabled: true, shown.IsPresented, shown.IsHovering, shown.IsPressed));
            var button = PlainControls.Button(label, () => state.IsPresented = !state.IsPresented); // :394-396, :399
            button.OnHover(hovering => Transaction.WithAnimation(Chrome.Hover, () => state.IsHovering = hovering)); // :400-402
            button.OnPressed(pressed => Transaction.WithAnimation(Chrome.Hover, () => state.IsPressed = pressed)); // ED-NEW-segmenthover-1
            ToolTip.SetTip(button, help); // :403
            AutomationProperties.SetName(button, help); // :404
            var popover = PlainControls.Popover(
                button,
                new Binding<bool>(() => state.IsPresented, value => state.IsPresented = value),
                () => new PopoverMenu(content)); // :405-407
            Child = PlainControls.Background(new Panel { Children = { button, popover } }, new NoWindowDragArea()); // :408
        }
    }

    // :412-437 struct ChromeCircleButton: View. A round glass button the height of a chrome capsule.
    public sealed class ChromeCircleButton : Decorator
    {
        // :418 @State private var isHovering = false
        private sealed class ViewState : MainActorModel
        {
            private bool isHovering;

            public bool IsHovering { get => Read(isHovering); set => Set(ref isHovering, value); }
        }

        /// <param name="symbol">:414 <c>let symbol: String</c>.</param>
        /// <param name="help">:415 <c>let help: String</c>.</param>
        /// <param name="action">:416 <c>let action: () -&gt; Void</c>.</param>
        // :420-436 body
        public ChromeCircleButton(string symbol, string help, Action action)
        {
            var state = new ViewState();
            var image = Symbols.Image(symbol, Chrome.IconFont); // :422-423
            var foreground = new AnimatedColor(image);
            image.SetValue(TextElement.ForegroundProperty, foreground.Brush);
            image.Track(() => state.IsHovering, hovering =>
                foreground.Set(Chrome.IconForeground(hovering), Transaction.Current)); // :424, ED-NEW-segmenthover-1: secondary at rest, primary lit
            var label = new Panel
            {
                Width = Chrome.CapsuleHeight, // :425
                Height = Chrome.CapsuleHeight,
                Children = { new Ellipse { Fill = Brushes.Transparent }, image }, // :426 .contentShape(Circle())
            };
            var button = PlainControls.Button(label, action); // :421, :428
            button.Clip = new EllipseGeometry(new Rect(0, 0, Chrome.CapsuleHeight, Chrome.CapsuleHeight)); // :426 .contentShape(Circle())
            var surface = button.ChromeGlassCircle(); // :429
            surface.OnHover(hovering => Transaction.WithAnimation(Chrome.Hover, () => state.IsHovering = hovering)); // :430-432
            ToolTip.SetTip(button, help); // :433
            AutomationProperties.SetName(button, help); // :434
            Child = PlainControls.Background(surface, new NoWindowDragArea()); // :435
        }
    }

    // :439-470 struct ChromeCircleMenu<Content: View>: View. A round glass button that opens a popover menu.
    public sealed class ChromeCircleMenu : Decorator
    {
        // :445-446 @State private var isHovering = false, isPresented = false
        private sealed class ViewState : MainActorModel
        {
            private bool isHovering;
            private bool isPresented;

            public bool IsHovering { get => Read(isHovering); set => Set(ref isHovering, value); }

            public bool IsPresented { get => Read(isPresented); set => Set(ref isPresented, value); }
        }

        /// <param name="symbol">:441 <c>let symbol: String</c>.</param>
        /// <param name="help">:442 <c>let help: String</c>.</param>
        /// <param name="content">:443 <c>@ViewBuilder var content: Content</c>.</param>
        // :448-469 body
        public ChromeCircleMenu(string symbol, string help, Func<Control> content)
        {
            var state = new ViewState();
            var image = Symbols.Image(symbol, Chrome.IconFont); // :452-453
            var foreground = new AnimatedColor(image);
            image.SetValue(TextElement.ForegroundProperty, foreground.Brush);
            image.Track(() => state.IsHovering || state.IsPresented, full =>
                foreground.Set(Chrome.IconForeground(full), Transaction.Current)); // :454, ED-NEW-segmenthover-1
            var label = new Panel
            {
                Width = Chrome.CapsuleHeight, // :455
                Height = Chrome.CapsuleHeight,
                Children = { new Ellipse { Fill = Brushes.Transparent }, image }, // :456 .contentShape(Circle())
            };
            var button = PlainControls.Button(label, () => state.IsPresented = !state.IsPresented); // :449-451, :458
            button.Clip = new EllipseGeometry(new Rect(0, 0, Chrome.CapsuleHeight, Chrome.CapsuleHeight)); // :456 .contentShape(Circle())
            var surface = button.ChromeGlassCircle(); // :459
            surface.OnHover(hovering => Transaction.WithAnimation(Chrome.Hover, () => state.IsHovering = hovering)); // :460-462
            ToolTip.SetTip(button, help); // :463
            AutomationProperties.SetName(button, help); // :464
            var popover = PlainControls.Popover(
                button,
                new Binding<bool>(() => state.IsPresented, value => state.IsPresented = value),
                () => new PopoverMenu(content)); // :465-467
            Child = PlainControls.Background(new Panel { Children = { surface, popover } }, new NoWindowDragArea()); // :468
        }
    }

    // :472-514 struct ChromeTextMenu<Content: View>: View. A text button shaped like a chrome capsule that opens a popover.
    public sealed class ChromeTextMenu : Decorator
    {
        // :479-480 @State private var isHovering = false, isPresented = false
        private sealed class ViewState : MainActorModel
        {
            private bool isHovering;
            private bool isPresented;

            public bool IsHovering { get => Read(isHovering); set => Set(ref isHovering, value); }

            public bool IsPresented { get => Read(isPresented); set => Set(ref isPresented, value); }
        }

        /// <param name="symbol">:474 <c>let symbol: String</c>.</param>
        /// <param name="title">:475 <c>let title: String</c>.</param>
        /// <param name="help">:476 <c>let help: String</c>.</param>
        /// <param name="content">:477 <c>@ViewBuilder var content: Content</c>.</param>
        // :482-513 body
        public ChromeTextMenu(string symbol, string title, string help, Func<Control> content)
        {
            var state = new ViewState();
            var text = new TextBlock { Text = title, MaxLines = 1, TextWrapping = TextWrapping.NoWrap }; // :489, :491 .lineLimit(1)
            SwiftFont.System(12.5, SwiftFont.Weight.Medium).Apply(text); // :490
            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6, // :486 HStack(spacing: 6)
                Children =
                {
                    Symbols.Image(symbol, Chrome.InlineIconFont), // :487-488
                    text,
                    Symbols.Image("chevron.down", Chrome.ChevronFont).ForegroundStyle(Chrome.SecondaryText), // :492-494
                },
            };
            foreach (var part in row.Children) part.VerticalAlignment = VerticalAlignment.Center;
            var foreground = new AnimatedColor(row);
            row.SetValue(TextElement.ForegroundProperty, foreground.Brush);
            row.Track(() => state.IsHovering || state.IsPresented, full =>
                foreground.Set(Chrome.PrimaryText.Opacity(full ? 1 : 0.92), Transaction.Current)); // :496
            var framed = new Border
            {
                Padding = new Thickness(Chrome.CapsuleHorizontalPadding, 0), // :497
                Height = Chrome.CapsuleContentHeight, // :498
                Child = row,
            };
            var label = new Border
            {
                Padding = new Thickness(0, Chrome.CapsuleVerticalPadding), // :499
                Background = Brushes.Transparent, // :500 .contentShape(Capsule(style: .continuous))
                Child = framed,
            }.Capsule(RoundedCornerStyle.Continuous);
            var button = PlainControls.Button(label, () => state.IsPresented = !state.IsPresented); // :483-485, :502
            var surface = button.ChromeGlassCapsule(); // :503 .fixedSize() (MeasureOverride), :504
            surface.OnHover(hovering => Transaction.WithAnimation(Chrome.Hover, () => state.IsHovering = hovering)); // :505-507
            ToolTip.SetTip(button, help); // :508 .help(help), and no accessibility label
            var popover = PlainControls.Popover(
                button,
                new Binding<bool>(() => state.IsPresented, value => state.IsPresented = value),
                () => new PopoverMenu(content)); // :509-511
            Child = PlainControls.Background(new Panel { Children = { surface, popover } }, new NoWindowDragArea()); // :512
        }

        // :503 .fixedSize(): the modifiers after it do not change the size, so the whole control holds it.
        protected override Size MeasureOverride(Size availableSize) =>
            PlainControls.MeasureFixed(this, Child, availableSize);
    }

    // :516-560 struct ChromeTextButton: View. A destructive one reads as an ordinary chrome control at rest and turns
    // red under the pointer.
    public sealed class ChromeTextButton : Decorator
    {
        private readonly bool isEnabled;
        private readonly bool isDestructive;

        // :526 @State private var isHovering = false
        private sealed class ViewState : MainActorModel
        {
            private bool isHovering;

            public bool IsHovering { get => Read(isHovering); set => Set(ref isHovering, value); }
        }

        /// <summary>Swift members symbol, title, help, isEnabled, isDestructive, action (:519-524); action has no
        /// default, so it moves before the defaulted ones (ED-095) and callers name the labels.</summary>
        // :528-553 body
        public ChromeTextButton(string symbol, string title, string help, Action action, bool isEnabled = true, bool isDestructive = false)
        {
            this.isEnabled = isEnabled;
            this.isDestructive = isDestructive;
            var state = new ViewState();
            var text = new TextBlock { Text = title, MaxLines = 1, TextWrapping = TextWrapping.NoWrap }; // :533, :535
            SwiftFont.System(12.5, SwiftFont.Weight.Medium).Apply(text); // :534
            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6, // :530
                Children = { Symbols.Image(symbol, Chrome.InlineIconFont), text }, // :531-532
            };
            foreach (var part in row.Children) part.VerticalAlignment = VerticalAlignment.Center;
            var foreground = new AnimatedColor(row);
            row.SetValue(TextElement.ForegroundProperty, foreground.Brush);
            row.Track(() => state.IsHovering, hovering => foreground.Set(Foreground(hovering), Transaction.Current)); // :537
            var framed = new Border
            {
                Padding = new Thickness(Chrome.CapsuleHorizontalPadding, 0), // :538
                Height = Chrome.CapsuleContentHeight, // :539
                Child = row,
            };
            var label = new Border
            {
                Padding = new Thickness(0, Chrome.CapsuleVerticalPadding), // :540
                Background = Brushes.Transparent, // :541 .contentShape(Capsule(style: .continuous))
                Child = framed,
            }.Capsule(RoundedCornerStyle.Continuous);
            var button = PlainControls.Button(label, action); // :529, :543
            button.IsEnabled = isEnabled; // :544
            var surface = button.ChromeGlassCapsule(); // :545 .fixedSize() (MeasureOverride), :546
            surface.OnHover(hovering => Transaction.WithAnimation(Chrome.Hover, () => state.IsHovering = hovering)); // :547-549
            ToolTip.SetTip(button, help); // :550
            AutomationProperties.SetName(button, help); // :551
            Child = PlainControls.Background(surface, new NoWindowDragArea()); // :552
        }

        // :545 .fixedSize(): the modifiers after it do not change the size, so the whole control holds it.
        protected override Size MeasureOverride(Size availableSize) =>
            PlainControls.MeasureFixed(this, Child, availableSize);

        // :555-559, with the hover state it reads.
        private SwiftColor Foreground(bool isHovering)
        {
            if (!isEnabled) return Chrome.PrimaryText.Opacity(0.32); // :556
            if (isDestructive) return Chrome.Danger.Opacity(isHovering ? 1 : 0.92); // :557
            return Chrome.PrimaryText.Opacity(isHovering ? 1 : 0.92); // :558
        }
    }

    // MARK: - Scroll morph

    /// <summary>:564-577 <c>@MainActor @Observable final class ChromeScrollModel</c> (R-OBS-6). Only the veil and the
    /// compact title observe it, so scrolling never re-renders the page underneath.</summary>
    public sealed class ChromeScrollModel : MainActorModel
    {
        private double progress; // :569 = 0

        // :569 private(set) var progress: CGFloat = 0
        public double Progress { get => Read(progress); private set => Set(ref progress, value); }

        // :571-576 func update(travel:)
        public void Update(double travel)
        {
            var raw = (travel - Chrome.TitleStartOffset) / (Chrome.TitleEndOffset - Chrome.TitleStartOffset); // :572
            var floored = 0 >= raw ? 0 : raw; // :573 max(raw, 0)
            var clamped = 1 < floored ? 1 : floored; // :573 min(_, 1)
            var quantized = SwiftNumber.Rounded(clamped / Chrome.ProgressQuantum) * Chrome.ProgressQuantum; // :574
            if (quantized != Progress) Progress = quantized; // :575
        }
    }

    // :579-628 struct PaneTopVeil: View
    public sealed class PaneTopVeil : Decorator
    {
        private readonly Panel host = new() { Height = Chrome.VeilHeight }; // :623 .frame(height:)
        private Border? baseFade;
        private Border? tintFade;
        private Color baseShown;
        private Color tintShown;
        private Action stopTurn = static () => { };
        // ED-253 (round 4): the running turnover, from what the fades showed to the colours it is heading for.
        private bool turning;
        private (Color Base, Color Tint) turnFrom;
        private (Color Base, Color Tint) turnTo;

        /// <param name="model">:582 <c>let model: ChromeScrollModel</c>.</param>
        // :585-611 body. PR-WIN-196, UI-CHAT-058: two 72-point gradients, never hit-testable, drawn only once
        // progress exceeds zero.
        public PaneTopVeil(ChromeScrollModel model)
        {
            IsHitTestVisible = false; // :624 .allowsHitTesting(false)
            Child = host;
            this.Track(() => model.Progress, _ => Draw(turn: false));
            ActualThemeVariantChanged += (_, _) => Draw(turn: true); // :585 model.progress via colorScheme
            // :589 Chrome.glassTint is the theme's surface, read as the body is drawn: a theme of the same appearance redraws
            // it too (ED-253).
            AttachedToVisualTree += (_, _) => ThemeManager.DidChange += OnThemeChanged;
            DetachedFromVisualTree += (_, _) => ThemeManager.DidChange -= OnThemeChanged;
            Draw(turn: false);

            void OnThemeChanged()
            {
                if (CheckAccess()) Draw(turn: true);
                else Dispatcher.Post(() => Draw(turn: true));
            }

            // ED-253 (OD-0076, SPEC 6.5): in a tree that turns over (AppearanceTurn, the Settings window) a change of
            // appearance or theme eases both fades from what they show to the new colours over 0.32 s, as every other
            // colour of the window does, instead of redrawing them in the click's frame. A scroll redraws at once, but one
            // during that turnover keeps it running, retargeted at the colours of the new position (round 4: "nothing cuts").
            void Draw(bool turn)
            {
                var progress = model.Progress; // :585
                var isDark = PlainControls.IsDark(ActualThemeVariant); // :586
                host.Opacity = 0.08 + 0.92 * progress; // :622
                if (progress > 0) // :590 `if progress > 0`
                {
                    var scrim = isDark ? 0.14 + 0.18 * progress : 0.30 + 0.26 * progress; // :587
                    var baseColor = (isDark ? SwiftColor.Black : SwiftColor.White).Opacity(scrim).Resolve(ActualThemeVariant); // :588, :596
                    var tintColor = Chrome.GlassTint.Opacity(isDark ? 0.30 : 0.18).Resolve(ActualThemeVariant); // :589, :597
                    var drawn = baseFade is not null;
                    if (baseFade is null)
                    {
                        baseFade = new Border { IsHitTestVisible = false, Background = Fade() };
                        tintFade = new Border { IsHitTestVisible = false, Background = Fade() };
                        host.Children.Add(baseFade);
                        host.Children.Add(tintFade!);
                    }
                    if (!turn && turning && drawn)
                    {
                        turnTo = (baseColor, tintColor); // the next frame of the turn heads there
                        return;
                    }
                    StopTurn();
                    if (turn && drawn && (baseShown != baseColor || tintShown != tintColor) && AppearanceTurn.Eases(this))
                    {
                        turnFrom = (baseShown, tintShown);
                        turnTo = (baseColor, tintColor);
                        turning = true;
                        stopTurn = AppearanceTurn.Run(this, p =>
                        {
                            Paint(SwiftColor.PremultipliedMix(turnFrom.Base, turnTo.Base, p), SwiftColor.PremultipliedMix(turnFrom.Tint, turnTo.Tint, p));
                            if (p >= 1) turning = false;
                        });
                    }
                    else Paint(baseColor, tintColor);
                }
                else
                {
                    StopTurn();
                    if (baseFade is not null)
                    {
                        host.Children.Clear();
                        baseFade = null;
                        tintFade = null;
                    }
                }
            }
        }

        private void StopTurn()
        {
            stopTurn();
            stopTurn = static () => { };
            turning = false;
        }

        private void Paint(Color baseColor, Color tintColor)
        {
            baseShown = baseColor;
            tintShown = tintColor;
            Fade((LinearGradientBrush)baseFade!.Background!, baseColor);
            Fade((LinearGradientBrush)tintFade!.Background!, tintColor);
        }

        // :613-627 the fade held over the top 28%, eased out by 62%, clear at the bottom.
        private static LinearGradientBrush Fade() => new()
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(default, 0), // :618
                new GradientStop(default, 0.28), // :619
                new GradientStop(default, 0.62), // :620
                new GradientStop(default, 1), // :621
            },
        };

        private static void Fade(LinearGradientBrush brush, Color resolved)
        {
            brush.GradientStops[0].Color = resolved; // :618
            brush.GradientStops[1].Color = resolved; // :619
            brush.GradientStops[2].Color = WithAlphaFactor(resolved, 0.45); // :620
            brush.GradientStops[3].Color = WithAlphaFactor(resolved, 0); // :621
        }

        private static Color WithAlphaFactor(Color color, double factor) =>
            Color.FromArgb((byte)Math.Round(color.A * factor), color.R, color.G, color.B);
    }

    // :630-647 struct ChromeCompactTitle: View
    public sealed class ChromeCompactTitle : Decorator
    {
        /// <param name="title">:632 <c>let title: String</c>.</param>
        /// <param name="model">:633 <c>let model: ChromeScrollModel</c>.</param>
        // :635-646 body. PR-WIN-197: opacity equal to progress, a 5-point rise, hidden from assistive technology
        // below 0.5.
        public ChromeCompactTitle(string title, ChromeScrollModel model)
        {
            var text = new TextBlock { Text = title, MaxLines = 1, TextWrapping = TextWrapping.NoWrap }; // :636, :638-639
            SwiftFont.System(SwiftFont.TextStyle.Title3, weight: SwiftFont.Weight.Semibold).Apply(text); // :638
            text.Foreground = Chrome.PrimaryText.Brush(text); // :639
            text.HorizontalAlignment = HorizontalAlignment.Left;
            HorizontalAlignment = HorizontalAlignment.Stretch; // :642 .frame(maxWidth: .infinity, alignment: .leading)
            IsHitTestVisible = false; // :643 .allowsHitTesting(false)
            Child = text;
            this.Track(() => model.Progress, progress =>
            {
                text.Opacity = progress; // :640
                text.RenderTransform = new TranslateTransform(0, (1 - progress) * 5); // :641
                AutomationProperties.SetAccessibilityView(this, progress < 0.5 ? AccessibilityView.Raw : AccessibilityView.Content); // :644
            });
        }
    }

    // MARK: - Sidebar pieces

    // :651-668 struct SidebarSymbol: View
    public sealed class SidebarSymbol : Decorator
    {
        // :655-658 init(_ name: String, scale: CGFloat = 1); :660-667 body. PR-WIN-198: fitted into a
        // symbolSize * scale box, monochrome (ED-179, ED-182; OD-0010 Lucide).
        public SidebarSymbol(string name, double scale = 1)
        {
            var side = Chrome.SymbolSize * scale; // :664
            var image = SwiftImage.SystemName(name) // :661
                .Resizable() // :662
                .Interpolation(BitmapInterpolationMode.HighQuality) // :663
                .AspectRatio(ContentMode.Fit) // :665
                .SymbolRenderingMode(Marrow.UI.Port.SymbolRenderingMode.Monochrome); // :667
            image.Width = side; // :664
            image.Height = side;
            Child = image;
        }
    }

    // :670-679 struct SidebarIconBadge<Glyph: View>: View
    public sealed class SidebarIconBadge : Decorator
    {
        /// <param name="glyph">:672 <c>@ViewBuilder var glyph: Glyph</c>.</param>
        // :674-678 body. PR-WIN-199: a 20-point box at 0.85 of the label colour, nothing drawn behind it.
        public SidebarIconBadge(Func<Control> glyph)
        {
            var g = glyph(); // :675
            g.SetValue(TextElement.ForegroundProperty, Chrome.PrimaryText.Opacity(0.85).Brush(g)); // :676
            g.HorizontalAlignment = HorizontalAlignment.Center;
            g.VerticalAlignment = VerticalAlignment.Center;
            Child = new Panel { Width = Chrome.IconSize, Height = Chrome.IconSize, Children = { g } }; // :677
        }
    }

    // :681-757 struct SidebarRow<Icon: View, Accessory: View>: View
    public sealed class SidebarRow : Decorator
    {
        // :690 @State private var isHovering = false
        private sealed class ViewState : MainActorModel
        {
            private bool isHovering;

            public bool IsHovering { get => Read(isHovering); set => Set(ref isHovering, value); }
        }

        /// <summary>:692-708 <c>init(title:isSelected:isEmphasized:accessoryWidth:action:icon:accessory:)</c>; action,
        /// icon and accessory have no default, so they move before the defaulted ones (ED-095) and callers name the
        /// labels. <paramref name="icon"/> is the @ViewBuilder icon, <paramref name="accessory"/> the @ViewBuilder
        /// <c>(Bool) -&gt; Accessory</c> given the hover state.</summary>
        // :710-739 body. PR-WIN-200: a 28-point row, radius 7, three fill states, its trailing padding reserving
        // room for the accessory.
        public SidebarRow(
            string title,
            Action action,
            Func<Control> icon,
            Func<bool, Control> accessory,
            bool isSelected = false,
            bool isEmphasized = false,
            double accessoryWidth = 0,
            bool pressLook = false)
        {
            selected = isSelected;
            var state = new ViewState();
            var iconSlot = icon(); // :712
            iconSlot.Width = Chrome.IconSize; // :713 .frame(width:, alignment: .center)
            iconSlot.HorizontalAlignment = HorizontalAlignment.Center;

            var text = new TextBlock { Text = title, MaxLines = 1, TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis }; // :714, :718-719 .lineLimit(1).truncationMode(.tail)
            void ApplyText()
            {
                SwiftFont.System(SwiftFont.TextStyle.Body, weight: selected || isEmphasized ? SwiftFont.Weight.Medium : SwiftFont.Weight.Regular).Apply(text); // :717
                text.Foreground = Chrome.PrimaryText.Opacity(selected ? 1 : 0.92).Brush(text); // :718
            }
            ApplyText();

            var row = new TitleLine
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8, // :711 HStack(spacing: 8)
                Children = { iconSlot, text }, // :712-720 Spacer(minLength: 4) is TitleLine's trailing 4 pt
            };
            foreach (var part in row.Children) part.VerticalAlignment = VerticalAlignment.Center;

            var shape = new Border // :722 RoundedRectangle(cornerRadius: rowCornerRadius, style: .continuous).fill(fill)
            {
                Padding = new Thickness(Chrome.RowHorizontalPadding, 0, Chrome.RowHorizontalPadding + accessoryWidth, 0), // :725-726
                Height = Chrome.RowHeight, // :727
                HorizontalAlignment = HorizontalAlignment.Stretch, // :728
                Background = Brushes.Transparent,
                Child = row,
            }.RoundedRectangle(Chrome.RowCornerRadius, RoundedCornerStyle.Continuous);
            var fill = new AnimatedColor(shape);
            shape.Background = fill.Brush;
            fill.Set(FillFor(selected, false), null);

            var button = new SelectableButton(() => selected) { Template = PlainButtonTemplate, Content = shape, Padding = default }; // :709, :730
            button.Click += (_, _) => action();
            // The pressed look (PORT-ONLY, ED-280; SOURCE FACT: the reference runtime, CATALOGUE press.row-tile-dim):
            // a plain button dims its content to 0.8 and its wash to x0.7 while the pointer holds it down. The catalogue's own
            // ramp, 0.05 s in and 0.1 s out, ease-out (transition-audit sidebar-panels-015). Only the rows that ask for it draw it.
            var pressed = false;
            var dim = new AnimatedScalar(button, 0, value => row.Opacity = 1 - 0.2 * value);
            void Restyle(Animation? animation) => fill.Set(FillFor(selected, state.IsHovering).Opacity(pressed ? 0.7 : 1), animation);
            if (pressLook)
            {
                button.PropertyChanged += (_, change) =>
                {
                    if (change.Property != Button.IsPressedProperty || button.IsPressed == pressed) return;
                    pressed = button.IsPressed;
                    var ramp = pressed ? Animation.EaseOut(0.05) : Animation.EaseOut(0.1);
                    dim.Set(pressed ? 1 : 0, ramp);
                    Restyle(ramp);
                };
            }
            button.Track(() => state.IsHovering, hovering =>
            {
                Transaction.WithAnimation(Chrome.Hover, () => Restyle(Transaction.Current)); // :729
            });
            // :729 `.background { shape.fill(fill).animation(Chrome.hover, value: isSelected) }`: a new isSelected eases the
            // fill on Chrome.hover whatever transaction carried it (the modifier's own curve for a change of its value), from
            // where the fill is drawn now. The title's weight and opacity (:717-718) and the trait (:738) have no animation
            // of their own and take the new value under the change's transaction; every caller that changes a row's
            // selection (SettingsView.swift:149, `page = item` and its other writes) carries none, so they change at once.
            // A row held down keeps its pressed wash (x0.7, ED-280) through the change.
            applySelection = () =>
            {
                ApplyText();
                Restyle(Chrome.Hover);
            };
            var overlay = new Panel { Children = { button } };
            // :731-737 `.overlay(alignment: .trailing) { accessory }` comes BEFORE `.onHover`, so the hover region is the
            // button together with its overlaid accessory: the pointer resting on the ellipsis or the plus is still on the
            // row and keeps them (the hover is on the host, as SidebarThreadRow's overlays host carries its own).
            overlay.OnHover(hovering => Transaction.WithAnimation(Chrome.Hover, () => state.IsHovering = hovering)); // :735-737
            // :731-734 `.overlay(alignment: .trailing) { accessory(isHovering).padding(.trailing, 6) }`. SwiftUI keeps the
            // accessory's views under their own identity while the closure keeps returning them, and a view that a
            // conditional inside it adds or drops runs the default .opacity transition under the transaction in force: the
            // hover's withAnimation(Chrome.hover) (:735-737), so the project row's count and its ellipsis and plus
            // (SidebarView.swift:1115-1133) crossfade (OD-0076). The closure says which it is by the control it returns: the
            // control it returned last time is the same view and stays where it is, with its own state (an open popover's
            // anchor, a hover); another control is another branch, and the two crossfade. The first body places it.
            Control? shownAccessory = null;
            void ShowAccessory(bool hovering)
            {
                var built = accessory(hovering); // :732-733
                if (ReferenceEquals(built, shownAccessory)) return;
                built.Margin = new Thickness(0, 0, 6, 0); // :734
                built.HorizontalAlignment = HorizontalAlignment.Right;
                built.VerticalAlignment = VerticalAlignment.Center;
                if (shownAccessory is { } previous)
                {
                    SwiftTransition.Remove(overlay, previous);
                    SwiftTransition.Insert(overlay, overlay.Children.Count, built);
                }
                else SwiftTransition.Place(overlay, overlay.Children.Count, built);
                shownAccessory = built;
            }
            refreshAccessory = () => ShowAccessory(state.IsHovering);
            button.Track(() => state.IsHovering, ShowAccessory);

            AutomationProperties.SetName(button, title); // :738 AddTraits(isSelected ? [.isSelected, .isButton] : .isButton)

            Child = overlay;
        }

        private readonly Action refreshAccessory;
        private readonly Action applySelection;
        private bool selected;

        /// <summary>:683 <c>let isSelected: Bool</c>, handed in anew when the view that owns the row renders it again under
        /// the same identity (SettingsView.swift:148-149, a ForEach over the pages, so a page keeps its row): the fill eases
        /// to the new state on Chrome.hover (:729), the title and the trait take it at once (:717-718, :738).</summary>
        public bool IsSelected
        {
            get => selected;
            set
            {
                if (selected == value) return;
                selected = value;
                applySelection();
            }
        }

        /// <summary>The accessory closure asked again with the row's own hover state: SwiftUI re-reads
        /// <c>accessory(isHovering)</c> whenever the view that owns the row renders, so a caller whose accessory reads its
        /// own state (the project row's isMenuPresented, its count) asks through this when that state changes.</summary>
        public void RefreshAccessory() => refreshAccessory();

        // :711-720 HStack(spacing: 8) { icon; title; Spacer(minLength: 4) } under a width the row's padding fixes. A plain
        // horizontal StackPanel measures every child at infinite width, so the title would never be cut at its tail
        // (:718-719 `.lineLimit(1).truncationMode(.tail)`) and would run under the reserved accessory space; this one
        // measures the icon at its own size and gives the title the rest of the width, less the Spacer's minimum 4 pt and
        // the stack's spacing before the Spacer (SwiftUI puts the HStack's spacing between every adjacent pair of subviews,
        // the Spacer included: 8 + 4 = 12 pt after the title, ED-329), so the title truncates at the same place
        // whether or not the accessory shows (UI-SIDEBAR-008).
        private sealed class TitleLine : StackPanel
        {
            private const double SpacerMinLength = 4; // :720

            protected override Size MeasureOverride(Size availableSize)
            {
                if (Children.Count != 2 || double.IsInfinity(availableSize.Width)) return base.MeasureOverride(availableSize);
                var icon = Children[0];
                var title = Children[1];
                icon.Measure(new Size(double.PositiveInfinity, availableSize.Height));
                title.Measure(new Size(Math.Max(0, availableSize.Width - icon.DesiredSize.Width - Spacing - Spacing - SpacerMinLength), availableSize.Height));
                return new Size(
                    icon.DesiredSize.Width + Spacing + title.DesiredSize.Width,
                    Math.Max(icon.DesiredSize.Height, title.DesiredSize.Height));
            }
        }

        // ENGINEERING DECISION (skeleton, no rule names the form): Avalonia's Button peer carries no selection
        // state; :738 `.accessibilityAddTraits(isSelected ? [.isSelected, .isButton] : .isButton)` is exposed through
        // ISelectionItemProvider, the platform's own selectable-item automation pattern, alongside the button's
        // invoke behaviour.
        private sealed class SelectableButton(Func<bool> isSelected) : Button
        {
            protected override AutomationPeer OnCreateAutomationPeer() => new Peer(this, isSelected);

            // :730 the row is a Button: accessibility activation runs its action, as a click does.
            internal void Activate()
            {
                if (IsEffectivelyEnabled) OnClick();
            }

            private sealed class Peer(Control owner, Func<bool> isSelected) : ControlAutomationPeer(owner), ISelectionItemProvider, IInvokeProvider
            {
                protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Button;
                bool ISelectionItemProvider.IsSelected => isSelected();
                ISelectionProvider? ISelectionItemProvider.SelectionContainer => null;
                void ISelectionItemProvider.AddToSelection() { }
                void ISelectionItemProvider.RemoveFromSelection() { }
                void ISelectionItemProvider.Select() { }
                void IInvokeProvider.Invoke() => ((SelectableButton)Owner).Activate();
            }
        }

        // Same template PlainControls uses for `.buttonStyle(.plain)` (PlainControls.cs); duplicated here because
        // SelectableButton needs its own automation peer, not PlainControls.Button's.
        private static readonly Avalonia.Controls.Templates.FuncControlTemplate<Button> PlainButtonTemplate = new((btn, scope) =>
            new ContentPresenter
            {
                Name = "PART_ContentPresenter",
                [!ContentPresenter.ContentProperty] = btn[!ContentControl.ContentProperty],
            }.RegisterInNameScope(scope));

        /// <summary>:748-757 <c>extension SidebarRow where Accessory == EmptyView</c>,
        /// <c>init(title:isSelected:action:icon:)</c>.</summary>
        public SidebarRow(string title, Action action, Func<Control> icon, bool isSelected = false)
            : this(title, action, icon, EmptyView(), isSelected)
        {
        }

        // :756 `{ _ in EmptyView() }`: one empty view, the same whatever the hover, so a hover changes nothing in it.
        private static Func<bool, Control> EmptyView()
        {
            var none = new Panel();
            return _ => none;
        }

        // :741-745. PR-WIN-200: the primary label colour at 0.12 selected, 0.06 hovered, clear otherwise.
        private static SwiftColor FillFor(bool isSelected, bool isHovering)
        {
            if (isSelected) return Chrome.Overlay(0.12); // :742
            if (isHovering) return Chrome.Overlay(0.06); // :743
            return SwiftColor.Clear; // :744
        }
    }

    // :759-773 struct RowAccessoryIcon: View
    public sealed class RowAccessoryIcon : Decorator
    {
        // :762-764 init(_ symbol: String); :766-772 body. PR-WIN-201: a semibold subheadline glyph in
        // Chrome.secondaryText, centred in a 20 by 20 rectangular hit box.
        public RowAccessoryIcon(string symbol)
        {
            var image = Symbols.Image(symbol, Chrome.InlineIconFont) // :767-768
                .ForegroundStyle(Chrome.SecondaryText); // :769
            image.HorizontalAlignment = HorizontalAlignment.Center;
            image.VerticalAlignment = VerticalAlignment.Center;
            Child = new Panel
            {
                Width = 20, // :770
                Height = 20,
                Background = Brushes.Transparent, // :771 .contentShape(.rect)
                Children = { image },
            };
        }
    }

    // :775-823 struct SidebarSearchField: View
    public sealed class SidebarSearchField : Decorator
    {
        private readonly Action<int>? onMove;

        /// <param name="text">:776 <c>@Binding var text: String</c>.</param>
        /// <param name="prompt">:777 <c>var prompt = "Search"</c>.</param>
        /// <param name="onSubmit">:778 <c>var onSubmit: () -&gt; Void = {}</c>; null stands for the empty closure.</param>
        /// <param name="onMove">:782 <c>var onMove: ((Int) -&gt; Void)?</c>.</param>
        // :784-816 body
        public SidebarSearchField(Binding<string> text, string prompt = "Search", Action? onSubmit = null, Action<int>? onMove = null)
        {
            this.onMove = onMove;
            var magnifier = Symbols.Image("magnifyingglass", SwiftFont.System(SwiftFont.TextStyle.Callout, weight: SwiftFont.Weight.Medium)) // :786-787
                .ForegroundStyle(Chrome.SecondaryText.Opacity(0.85)); // :788
            magnifier.Margin = new Thickness(0, 0, 6, 0); // :785 HStack(spacing: 6)
            var field = PlainControls.TextField(prompt, text); // :789-790
            SwiftFont.System(SwiftFont.TextStyle.Body).Apply(field); // :791
            field.HorizontalAlignment = HorizontalAlignment.Stretch; // :792 .frame(maxWidth: .infinity)
            field.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(magnifier, 0);
            Grid.SetColumn(field, 1);
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Children = { magnifier, field } }; // :785
            // :793-795 `.onSubmit(onSubmit)`, `.onKeyPress(.upArrow) { move(-1) }`, `.onKeyPress(.downArrow) { move(1) }`:
            // SwiftUI answers these before the field sees the key, so they are taken on the way down to the field, ahead
            // of every handler the field itself carries.
            row.AddHandler(KeyDownEvent, (_, e) =>
            {
                if (!ReferenceEquals(e.Source, field)) return;
                switch (e.Key)
                {
                    case Key.Enter:
                        onSubmit?.Invoke();
                        e.Handled = true;
                        break;
                    case Key.Up:
                        if (Move(-1)) e.Handled = true;
                        break;
                    case Key.Down:
                        if (Move(1)) e.Handled = true;
                        break;
                }
            }, RoutingStrategies.Tunnel);
            Button? clear = null;
            row.Track(() => text.Value.Length != 0, hasText => // :796 `if !text.isEmpty`
            {
                if (hasText && clear is null)
                {
                    var glyph = Symbols.Image("xmark.circle.fill", SwiftFont.System(SwiftFont.TextStyle.Callout)) // :800-801
                        .ForegroundStyle(Chrome.SecondaryText.Opacity(0.75)); // :802
                    clear = PlainControls.Button(glyph, () => text.Value = ""); // :797-799, :804
                    AutomationProperties.SetName(clear, "Clear search"); // :805
                    clear.Margin = new Thickness(6, 0, 0, 0);
                    clear.VerticalAlignment = VerticalAlignment.Center;
                    Grid.SetColumn(clear, 2);
                    SwiftTransition.Insert(row, row.Children.Count, clear);
                }
                else if (!hasText && clear is { } gone)
                {
                    clear = null;
                    SwiftTransition.Remove(row, gone);
                }
            });
            var capsule = new Border
            {
                Padding = new Thickness(9, 0), // :808
                Height = 30, // :809
                Child = row,
            }.Capsule(RoundedCornerStyle.Continuous);
            capsule.Background = Chrome.Overlay(0.07).Brush(capsule); // :810-813 Capsule(style: .continuous).fill(Chrome.overlay(0.07)), also the content shape
            // :814-815 Escape empties the field, the way the chrome's search capsule does, and goes no further.
            AddHandler(KeyDownEvent, (_, e) =>
            {
                if (e.Key != Key.Escape) return;
                text.Value = "";
                e.Handled = true;
            }, RoutingStrategies.Tunnel);
            Child = capsule;
        }

        // :818-822 private func move(_:) -> KeyPress.Result; true is .handled, false .ignored.
        private bool Move(int offset)
        {
            if (onMove is null) return false; // :819
            onMove(offset); // :820
            return true; // :821
        }
    }

    // :825-869 struct ChromeSearchField: View. A glass search capsule for a pane's chrome row. Fixed size so it sits
    // aligned with the other capsules instead of pushing them or clipping the sheet edge.
    public sealed class ChromeSearchField : Decorator
    {
        // :828 static let width: CGFloat = 200. Swift name kept; `new` because Decorator inherits Layoutable.Width.
        public new const double Width = 200;

        /// <param name="query">:830 <c>@Binding var query: String</c>.</param>
        /// <param name="prompt">:831 <c>var prompt = "Search"</c>.</param>
        // :833-868 body
        public ChromeSearchField(Binding<string> query, string prompt = "Search")
        {
            var magnifier = Symbols.Image("magnifyingglass", SwiftFont.System(SwiftFont.TextStyle.Callout, weight: SwiftFont.Weight.Semibold)) // :835-836
                .ForegroundStyle(Chrome.SecondaryText); // :837
            AutomationProperties.SetAccessibilityView(magnifier, AccessibilityView.Raw); // :838
            magnifier.Margin = new Thickness(0, 0, 6, 0); // :834 HStack(spacing: 6)
            var field = PlainControls.TextField(prompt, query); // :839-840
            field.FocusAdorner = null; // :841-843 .focusEffectDisabled()
            SwiftFont.System(12.5).Apply(field); // :844
            field.Foreground = Chrome.PrimaryText.Brush(field); // :845
            field.VerticalAlignment = VerticalAlignment.Center;
            var glyph = Symbols.Image("xmark.circle.fill", SwiftFont.System(SwiftFont.TextStyle.Callout)) // :849-850
                .ForegroundStyle(Chrome.SecondaryText.Opacity(0.8)); // :851
            var clear = PlainControls.Button(glyph, () => query.Value = ""); // :846-848, :853
            clear.Margin = new Thickness(6, 0, 0, 0);
            clear.VerticalAlignment = VerticalAlignment.Center;
            AutomationProperties.SetName(clear, "Clear search"); // :859
            // :854-858 Always laid out, so the field's text never shifts when the button comes; invisible and out of the
            // accessibility tree while there is nothing to clear.
            // DEVIATION OD-0076 (ED-280): the source cuts the button in and out (:856, no animation); the port
            // fades it and scales it from 0.8 over Chrome.hover, the way every control that appears does.
            clear.RenderTransformOrigin = RelativePoint.Center;
            var shown = new AnimatedScalar(clear, 0, value => { clear.Opacity = value; var scale = value == 0 ? 1 : 0.8 + 0.2 * value; clear.RenderTransform = new ScaleTransform(scale, scale); });
            var primed = false;
            clear.Track(() => query.Value.Length == 0, isEmpty =>
            {
                shown.Set(isEmpty ? 0 : 1, primed ? Transaction.Current ?? Chrome.Hover : null); primed = true; // :856
                clear.IsEnabled = !isEmpty; // :857
                if (isEmpty) AutomationProperties.SetAccessibilityView(clear, AccessibilityView.Raw); // :858
                else clear.ClearValue(AutomationProperties.AccessibilityViewProperty);
            });
            Grid.SetColumn(magnifier, 0);
            Grid.SetColumn(field, 1);
            Grid.SetColumn(clear, 2);
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Children = { magnifier, field, clear } }; // :834
            var framed = new Border
            {
                Padding = new Thickness(10, 0), // :861
                Width = ChromeSearchField.Width, // :862
                Height = Chrome.CapsuleContentHeight,
                Child = row,
            };
            var padded = new Border { Padding = new Thickness(0, Chrome.CapsuleVerticalPadding), Child = framed }; // :863
            // :866 `.onExitCommand { query = "" }`: Escape empties the field and dismisses nothing.
            AddHandler(KeyDownEvent, (_, e) =>
            {
                if (e.Key != Key.Escape) return;
                query.Value = "";
                e.Handled = true;
            }, RoutingStrategies.Tunnel);
            Child = PlainControls.Background(padded.ChromeGlassFieldCapsule(), new NoWindowDragArea()); // :864, :867
        }

        // :865 .fixedSize(): the modifiers after it do not change the size, so the whole control holds it.
        protected override Size MeasureOverride(Size availableSize) =>
            PlainControls.MeasureFixed(this, Child, availableSize);
    }

    // :871-932 struct GlassPickerButton<Value: Hashable>: View. Callers pass optional values (HydraSettingsPage.swift:412)
    // and enums, so TValue is unconstrained and compared with EqualityComparer<TValue>.Default.
    public sealed class GlassPickerButton<TValue> : Decorator
    {
        private readonly bool fixesWidth;

        // :881-882 @State private var isPresented = false, isHovering = false
        private sealed class ViewState : MainActorModel
        {
            private bool isPresented;
            private bool isHovering;

            public bool IsPresented { get => Read(isPresented); set => Set(ref isPresented, value); }

            public bool IsHovering { get => Read(isHovering); set => Set(ref isHovering, value); }
        }

        /// <param name="options">:873 <c>let options: [(value: Value, title: String)]</c>.</param>
        /// <param name="selection">:874 <c>@Binding var selection: Value</c>.</param>
        /// <param name="asset">:875 <c>var asset: (Value) -&gt; String? = { _ in nil }</c>; null stands for that default.</param>
        /// <param name="symbol">:878 <c>var symbol: (Value) -&gt; String? = { _ in nil }</c>; null stands for that default.</param>
        /// <param name="maxWidth">:879 <c>var maxWidth: CGFloat? = nil</c>.</param>
        // :884-931 body
        public GlassPickerButton(
            IReadOnlyList<(TValue Value, string Title)> options,
            Binding<TValue> selection,
            Func<TValue, string?>? asset = null,
            Func<TValue, string?>? symbol = null,
            double? maxWidth = null)
        {
            var assetOf = asset ?? (_ => null);
            var symbolOf = symbol ?? (_ => null);
            var state = new ViewState();
            var text = new TextBlock
            {
                MaxLines = 1, // :903 .lineLimit(1)
                TextWrapping = TextWrapping.NoWrap,
                TextTrimming = TextTrimming.CharacterEllipsis, // :904 .truncationMode(.tail)
                VerticalAlignment = VerticalAlignment.Center,
            };
            SwiftFont.System(12.5, SwiftFont.Weight.Medium).Apply(text); // :902
            var chevron = Symbols.Image("chevron.down", Chrome.ChevronFont).ForegroundStyle(Chrome.SecondaryText); // :905-907
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { text, chevron } }; // :889
            var foreground = new AnimatedColor(row);
            row.SetValue(TextElement.ForegroundProperty, foreground.Brush);
            row.Track(() => state.IsHovering || state.IsPresented, full =>
                foreground.Set(Chrome.PrimaryText.Opacity(full ? 1 : 0.92), Transaction.Current)); // :909
            Border? assetView = null;
            SwiftImage? symbolView = null;
            row.Track(() =>
            {
                var value = selection.Value;
                return (Asset: assetOf(value), Symbol: symbolOf(value), Title: Title(options, value)); // :885, :890, :896
            }, shown =>
            {
                text.Text = shown.Title; // :901 Text(verbatim: title)
                // :890-895 `if let name = asset(selection) { Image(name).resizable().aspectRatio(contentMode: .fit).frame(width: 14, height: 14) }`
                if (shown.Asset is { } name)
                {
                    if (assetView is null)
                    {
                        assetView = new Border { Width = 14, Height = 14, VerticalAlignment = VerticalAlignment.Center }; // :894
                        SwiftTransition.Insert(row, 0, assetView);
                    }
                    // :891-893 Image(name).resizable().aspectRatio(contentMode: .fit): the same view with a new name.
                    if (assetView.Child is not SwiftImage { } drawn || drawn.AssetName != name)
                        assetView.Child = SwiftImage.Named(name).Resizable().AspectRatio(ContentMode.Fit);
                }
                else if (assetView is { } goneAsset)
                {
                    assetView = null;
                    SwiftTransition.Remove(row, goneAsset);
                }
                // :896-900 `if let symbol = symbol(selection) { Image(systemName: symbol).font(.system(.callout, weight: .medium)).foregroundStyle(Chrome.secondaryText) }`
                if (shown.Symbol is { } glyph)
                {
                    var image = symbolView is null || symbolView.SymbolName != glyph
                        ? Symbols.Image(glyph, SwiftFont.System(SwiftFont.TextStyle.Callout, weight: SwiftFont.Weight.Medium)) // :897-898
                            .ForegroundStyle(Chrome.SecondaryText) // :899
                        : symbolView;
                    // The same view with a new name draws the new symbol in place; only its appearing is a transition.
                    if (symbolView is null) SwiftTransition.Insert(row, row.Children.IndexOf(text), image);
                    else if (!ReferenceEquals(image, symbolView)) row.Children[row.Children.IndexOf(symbolView)] = image;
                    symbolView = image;
                }
                else if (symbolView is { } goneSymbol)
                {
                    symbolView = null;
                    SwiftTransition.Remove(row, goneSymbol);
                }
            });
            var label = new Border
            {
                Padding = new Thickness(Chrome.CapsuleHorizontalPadding, 0), // :910
                Height = Chrome.CapsuleContentHeight, // :911
                Background = Brushes.Transparent, // :912 .contentShape(Capsule(style: .continuous))
                Child = row,
            }.Capsule(RoundedCornerStyle.Continuous);
            var button = PlainControls.Button(label, () => state.IsPresented = !state.IsPresented); // :886-888, :914
            fixesWidth = maxWidth is null; // :915 .fixedSize(horizontal: maxWidth == nil, vertical: true) (MeasureOverride)
            var surface = button.ChromeGlassCapsule(); // :916
            if (maxWidth is { } cap)
            {
                MaxWidth = cap; // :917 .frame(maxWidth: maxWidth, alignment: .trailing)
                surface.HorizontalAlignment = HorizontalAlignment.Right;
            }
            var popover = PlainControls.Popover(
                button,
                new Binding<bool>(() => state.IsPresented, value => state.IsPresented = value),
                () => new PopoverMenu(() =>
                {
                    // :922-928 every option in the supplied order, checked where it is the selection; choosing one assigns it.
                    var items = new StackPanel();
                    foreach (var option in options)
                    {
                        items.Children.Add(new PopoverItem(
                            option.Title,
                            () => selection.Value = option.Value,
                            symbol: symbolOf(option.Value),
                            asset: assetOf(option.Value),
                            isChecked: EqualityComparer<TValue>.Default.Equals(option.Value, selection.Value)));
                    }
                    return items;
                })); // :921
            var framed = new Panel { Children = { surface, popover } };
            framed.OnHover(hovering => Transaction.WithAnimation(Chrome.Hover, () => state.IsHovering = hovering)); // :918-920
            Child = PlainControls.Background(framed, new NoWindowDragArea()); // :930
        }

        // :915 .fixedSize(horizontal: maxWidth == nil, vertical: true): the modifiers after it keep the size on the fixed
        // axes, so the whole control holds it there.
        protected override Size MeasureOverride(Size availableSize) =>
            PlainControls.MeasureFixed(this, Child, availableSize, horizontal: fixesWidth, vertical: true);

        // :885 `options.first { $0.value == selection }?.title ?? ""`
        private static string Title(IReadOnlyList<(TValue Value, string Title)> options, TValue selection)
        {
            foreach (var option in options)
            {
                if (EqualityComparer<TValue>.Default.Equals(option.Value, selection)) return option.Title;
            }
            return "";
        }
    }

    /// <summary>:934-945 <c>struct SoftAppearModifier: ViewModifier</c>. Used by <c>.modifier(active:identity:)</c>
    /// transitions (:951, :996, ThreadTimeline.swift:318), so it is an IAnimatableModifier (ED-098). The Swift's only property
    /// is a Bool, so the struct has EmptyAnimatableData, yet SwiftUI still animates its arrival: the body is the built-in
    /// <c>.opacity(isVisible ? 1 : 0).offset(y: isVisible ? 0 : 4)</c>, whose own values are animatable, and the transition
    /// carries <c>.animation(.softAppear)</c> (:951). ENGINEERING DECISION (UI-CHAT-052, unit rec-tlr): the port's modifier
    /// applies opacity and offset itself, so it carries that one value for SwiftUI's two built-in effects: a progress from 0
    /// (not visible) to 1 (visible), which the transition interpolates, and from which Update derives the opacity and the
    /// 4 point rise. Before this the Bool was taken at its identity at once and every soft arrival was a cut.</summary>
    public sealed record SoftAppearModifier(double Progress) : IAnimatableModifier // :938
    {
        /// <param name="isVisible">:938 <c>let isVisible: Bool</c>.</param>
        public SoftAppearModifier(bool isVisible) : this(isVisible ? 1.0 : 0.0) { }

        /// <summary>:938 isVisible, as the transition's ends read it.</summary>
        public bool IsVisible => Progress >= 1;

        // The one value the transition interpolates (see the type's remark).
        public ImmutableArray<double> AnimatableData => ImmutableArray.Create(Progress);

        public IAnimatableModifier WithAnimatableData(ImmutableArray<double> data) => this with { Progress = data[0] };

        // :940-944. PR-WIN-206: opacity and offset are plain layer properties, so no wrapper is needed.
        public Control Body(Control content) => content;

        // :940-944, applied per frame (ED-098)
        public void Update(Control body)
        {
            body.Opacity = Progress; // :942 isVisible ? 1 : 0, at the progress the transition has got to
            body.RenderTransform = new TranslateTransform(0, 4 * (1 - Progress)); // :943 isVisible ? 0 : 4
        }
    }

    // :963-988 private struct SettleGlideModifier: ViewModifier, Animatable (ED-098)
    file sealed record SettleGlideModifier(double Progress) : IAnimatableModifier // :968
    {
        // :970-973 var animatableData: Double
        public ImmutableArray<double> AnimatableData => ImmutableArray.Create(Progress);

        // :972
        public IAnimatableModifier WithAnimatableData(ImmutableArray<double> data) => this with { Progress = data[0] };

        // :975-982. PR-WIN-207: opacity, offset and scale are plain layer properties, so no wrapper is needed.
        public Control Body(Control content) => content;

        // :975-982, applied per frame (ED-098)
        public void Update(Control body)
        {
            var reduceMotion = Chrome.ReduceMotionPlatform.Current.AccessibilityDisplayShouldReduceMotion; // :976
            var fade = reduceMotion ? Progress : Smoothstep(0.45, 0.92, Progress); // :977
            body.Opacity = 1 - fade; // :979
            var offsetY = reduceMotion ? 0 : 22 * Progress; // :980
            var scale = reduceMotion ? 1 : 1 - 0.04 * Progress; // :981
            body.RenderTransformOrigin = new RelativePoint(0.5, 0, RelativeUnit.Relative); // :981 anchor: .top
            var group = new TransformGroup();
            group.Children.Add(new TranslateTransform(0, offsetY)); // :980, applied before the anchored scale
            group.Children.Add(new ScaleTransform(scale, scale)); // :981
            body.RenderTransform = group;
        }

        // :984-987
        private static double Smoothstep(double edge0, double edge1, double x)
        {
            var t = Math.Clamp((x - edge0) / (edge1 - edge0), 0, 1);
            return t * t * (3 - 2 * t);
        }
    }

    /// <summary>PORT-ONLY: the ChromeSection header <c>HStack(spacing: 4)</c> (:1039-1048) with its <c>Spacer(minLength: 8)</c>
    /// before the trailing control. Still a horizontal StackPanel to every caller: the heading and accessory keep their
    /// natural widths from the leading edge, the trailing control is placed at the far edge, and the heading (a Text
    /// with no lineLimit) is given what is left, so it wraps instead of running under the trailing control.</summary>
    internal sealed class ChromeSectionHeaderPanel : StackPanel
    {
        /// <summary>:1046 the trailing control, when the section has one.</summary>
        public Control? Trailing { get; set; }

        protected override Size MeasureOverride(Size availableSize)
        {
            var visible = Children.Where(c => c.IsVisible).ToList();
            var others = visible.Where(c => !ReferenceEquals(c, visible[0])).ToList();
            double used = 0, height = 0;
            foreach (var child in others)
            {
                child.Measure(new Size(double.PositiveInfinity, availableSize.Height));
                used += child.DesiredSize.Width;
                height = Math.Max(height, child.DesiredSize.Height);
            }
            if (visible.Count == 0) return default;
            var lead = visible[0];
            var gaps = Spacing * (visible.Count - 1);
            var room = double.IsInfinity(availableSize.Width) ? double.PositiveInfinity : Math.Max(0, availableSize.Width - used - gaps);
            lead.Measure(new Size(room, availableSize.Height));
            height = Math.Max(height, lead.DesiredSize.Height);
            return new Size(lead.DesiredSize.Width + used + gaps, height);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            var visible = Children.Where(c => c.IsVisible).ToList();
            double x = 0;
            foreach (var child in visible)
            {
                var size = child.DesiredSize;
                var left = ReferenceEquals(child, Trailing) ? Math.Max(x, finalSize.Width - size.Width) : x;
                child.Arrange(new Rect(left, Math.Max(0, (finalSize.Height - size.Height) / 2), size.Width, size.Height));
                x = left + size.Width + Spacing;
            }
            return finalSize;
        }
    }

    // MARK: - Cards

    // :1006-1027 struct ChromeCard<Content: View>: View. B3 (PR-WIN-202, UI-WIN-052) adds `lazy: Bool = false`
    // (:1009). ENGINEERING DECISION on how `lazy` renders in Avalonia: `content` (:1010) is a single already-built
    // `Func<Control>` subtree - the caller's @ViewBuilder rows are composed before ChromeCard ever sees them - so
    // there is nothing left for a virtualizing host to generate lazily; `lazy` is a recorded eager fallback rather
    // than a virtualizing host, stored and exposed so the default and the three named call sites
    // (MCPSettingsPage.swift:40, :395; SettingsView.swift:898) are inspectable, while both of :1015-1019's branches
    // build the same eager StackPanel. The fill-not-clip property (UI-WIN-052) is reproduced either way: :1021-1025.
    public sealed class ChromeCard : Decorator
    {
        /// <param name="content">:1010 <c>@ViewBuilder var content: Content</c>.</param>
        /// <param name="lazy">:1009 <c>var lazy = false</c>. Recorded, not a virtualizing host; see the class remark.</param>
        // :1012-1026 body. PR-WIN-202: rows stacked with no spacing on a continuous rounded-16 fill at 0.03; a fill
        // rather than a clip, so scrolling costs no offscreen mask pass.
        public ChromeCard(Func<Control> content, bool lazy = false)
        {
            Lazy = lazy; // :1009
            var stack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 0, HorizontalAlignment = HorizontalAlignment.Stretch }; // :1015-1019, both branches (see the class remark)
            stack.Children.Add(content());
            var border = new Border { Child = stack, HorizontalAlignment = HorizontalAlignment.Stretch } // :1021
                .RoundedRectangle(Chrome.CardCornerRadius, RoundedCornerStyle.Continuous) // :1023-1024
                .ContentBeyondSides(); // Chrome.swift:1012 "a filled shape instead of a clip": a held row's lift, shadow and 1.02 scale spill past the sides (ModelsSettingsPage.swift:255-262)
            border.Background = Chrome.Overlay(0.03).Brush(border); // :1025
            Child = border;
        }

        /// :1009 <c>var lazy = false</c>. Default false, matching the Swift default; recorded rather than
        /// realised as a virtualizing host (see the class remark on <see cref="ChromeCard"/>).
        public bool Lazy { get; }
    }

    // :1029-1053 struct ChromeSection<Content: View>: View
    public sealed class ChromeSection : Decorator
    {
        /// <param name="title">:1030 <c>let title: String</c>.</param>
        /// <param name="content">:1035 <c>@ViewBuilder var content: Content</c>.</param>
        /// <param name="accessory">:1032 <c>var accessory: AnyView? = nil</c>.</param>
        /// <param name="trailing">:1034 <c>var trailing: AnyView? = nil</c>.</param>
        // :1037-1052 body. PR-WIN-203: a semibold body-style heading in the secondary text colour, an optional
        // accessory 4 points after it, an optional trailing control pushed to the far right, content 10 points below.
        public ChromeSection(string title, Func<Control> content, Control? accessory = null, Control? trailing = null)
        {
            var heading = new TextBlock { Text = title, TextWrapping = TextWrapping.Wrap }; // :1040 no lineLimit: a long title wraps
            SwiftFont.System(SwiftFont.TextStyle.Body, weight: SwiftFont.Weight.Semibold).Apply(heading); // :1041
            heading.Foreground = Chrome.SecondaryText.Brush(heading); // :1042
            var header = new ChromeSectionHeaderPanel { Orientation = Orientation.Horizontal, Spacing = 4 }; // :1039 HStack(spacing: 4)
            header.Children.Add(heading);
            if (accessory is not null) header.Children.Add(accessory); // :1043
            if (trailing is not null)
            {
                trailing.Margin = new Thickness(8, 0, 0, 0); // :1045 Spacer(minLength: 8)
                header.Children.Add(trailing); // :1046
                header.Trailing = trailing;
            }
            foreach (var part in header.Children) part.VerticalAlignment = VerticalAlignment.Center;
            var stack = new StackPanel { Orientation = Orientation.Vertical, Spacing = Chrome.SectionHeaderSpacing, HorizontalAlignment = HorizontalAlignment.Stretch }; // :1038, :1050
            stack.Children.Add(header);
            stack.Children.Add(content()); // :1049
            Child = stack;
            HorizontalAlignment = HorizontalAlignment.Stretch; // :1051
        }
    }

    // :1055-1080 struct ChromeRow<Control: View>: View
    public sealed class ChromeRow : Decorator
    {
        /// <param name="title">:1056 <c>let title: String</c>.</param>
        /// <param name="control">:1058 <c>@ViewBuilder var control: Control</c>.</param>
        /// <param name="detail">:1057 <c>var detail: String?</c>.</param>
        // :1060-1079 body. PR-WIN-204: a body-style title over an optional non-empty subheadline detail, control
        // at the trailing edge.
        public ChromeRow(string title, Func<Control> control, string? detail = null)
        {
            var titleText = new TextBlock { Text = title, TextWrapping = TextWrapping.Wrap }; // :1064-1065 normal Text wrapping
            SwiftFont.System(SwiftFont.TextStyle.Body).Apply(titleText); // :1064
            titleText.Foreground = Chrome.PrimaryText.Brush(titleText); // :1065
            var textStack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 2, HorizontalAlignment = HorizontalAlignment.Left }; // :1063, :1066
            textStack.Children.Add(titleText);
            if (!string.IsNullOrEmpty(detail)) // :1067 `if let detail, !detail.isEmpty`
            {
                var detailText = new TextBlock { Text = detail, TextWrapping = TextWrapping.Wrap }; // :1068-1070
                SwiftFont.System(SwiftFont.TextStyle.Subheadline).Apply(detailText); // :1068
                detailText.Foreground = Chrome.SecondaryText.Brush(detailText); // :1069
                textStack.Children.Add(detailText);
            }
            var controlView = control(); // :1073
            controlView.HorizontalAlignment = HorizontalAlignment.Right;
            controlView.VerticalAlignment = VerticalAlignment.Center;
            // :1062, :1072 HStack has two 16-point gaps around Spacer(minLength: 12).
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,16,12,16,Auto") };
            textStack.HorizontalAlignment = HorizontalAlignment.Stretch;
            Grid.SetColumn(textStack, 0);
            Grid.SetColumn(controlView, 4);
            row.Children.Add(textStack);
            row.Children.Add(controlView);
            row.VerticalAlignment = VerticalAlignment.Center;
            var border = new Border
            {
                Padding = new Thickness(16, 11, Chrome.RowControlTrailingPadding, 11), // :1074-1076
                Child = row,
            };
            Child = border;
        }
    }

    // :1082-1087 struct ChromeRowDivider: View
    public sealed class ChromeRowDivider : Decorator
    {
        // :1083-1086 body. PR-WIN-205: a hairline inset 16 points from the leading edge only.
        public ChromeRowDivider()
        {
            var line = new Border { BorderThickness = new Thickness(0, 0, 0, 1), Height = 1 };
            // :1084 `Divider()`: SwiftUI's own separator, drawn in the system divider colour, which the framework paints
            // (B2 gives it no literal). SOURCE FACT (the reference runtime, work/reference-video): measured on
            // the Mac as a 1 px line of 0.10 opacity (the pixel's own alpha): dark card S 199.5 (frames/w-1-settings-tour-198.0/stills/win_199.5.png,
            // y402: (22,66,72) over (43,86,92), white at 0.09-0.11) and light card S 141.5 (frames/gap-R2-05, black at 0.10);
            // CATALOGUE static.settings-light-theme says the same (row separators 10 % light, 9-10 % dark). The label colour is 0.85 opaque,
            // so Overlay(0.12) is the line at 0.102 (alpha 26).
            var ink = new AnimatedColor(line); ink.Set(Chrome.Overlay(0.12), null); line.BorderBrush = ink.Brush; // a colour that follows an appearance turnover (ED-254)
            Child = line;
            Margin = new Thickness(16, 0, 0, 0); // :1085 .padding(.leading, 16)
        }
    }

    // MARK: - Window drag

    // :1091-1099 struct WindowDragArea: View (doc comment :1091). ED-200 (oracle O-68): B3 replaces the B2
    // NSViewRepresentable/NSView pair (WindowDragArea wrapping WindowDragView) with one SwiftUI View,
    // `Color.clear.contentShape(.rect).gesture(WindowDragGesture()).allowsWindowActivationEvents(true)`; this
    // collapses onto a single Avalonia Control the same way, keeping the ED-063 caption hit test (probe P7) with a
    // BeginMoveDrag fallback for windows where the caption result does not apply. WindowDragGesture's own
    // press-start and double-click semantics are UNDOCUMENTED and UNRESOLVED (6.1 E6, oracle O-68; OD-0001 rules
    // out a Mac run to settle it): the press-and-move-drags, activating-click-drags, release-to-system,
    // double-click-maximizes and right-click-shows-system-menu behaviour below is ED-063's own (already measured
    // by probe P7), not a transcription of any documented WindowDragGesture behaviour, since none exists to
    // transcribe.
    public sealed class WindowDragArea : Control
    {
        // ED-063 (probe P7): the region is a caption to the Windows hit test, so the system move loop drags the
        // window; :1097 `allowsWindowActivationEvents(true)` - a caption hit test's own click already activates
        // an inactive window and drags it in the same gesture, matching Apple's stated intent.
        public WindowDragArea() => Win32Properties.SetNonClientHitTestResult(this, Win32Properties.Win32HitTestValue.Caption);

        // :1094 `Color.clear` (B2's acceptsFirstMouse-equivalent): the region takes the hit over its whole frame.
        public bool AcceptsFirstMouse(PointerEventArgs? @for) => true;

        // :1095 `.contentShape(.rect)`: the region draws nothing, as B2's NSView subclass did.
        public override void Render(DrawingContext context) => context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));

        // ED-200's BeginMoveDrag fallback, for windows the caption hit-test result does not reach; the press goes
        // no further, the way B2's `window?.performDrag(with: event)` let no further handler see it.
        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            if (e.GetCurrentPoint(this).Properties.PointerUpdateKind != PointerUpdateKind.LeftButtonPressed) return;
            (TopLevel.GetTopLevel(this) as Window)?.BeginMoveDrag(e);
            e.Handled = true;
        }

        // The release is swallowed here exactly as B2's empty mouseUp did, and never reaches the UI behind the
        // region; WindowDragGesture documents nothing about the release either way (6.1 E6, oracle O-68).
        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            if (e.GetCurrentPoint(this).Properties.PointerUpdateKind != PointerUpdateKind.LeftButtonReleased) return;
            e.Handled = true;
        }
    }

    // :1101-1117 struct NoWindowDragArea: NSViewRepresentable (doc comment :1101-1110, was B2 :1104-1120). Marks a
    // chrome control's hit area as never dragging the window; empty chrome around the controls has no such view
    // behind it and still drags the window like a title bar. Unchanged from B2 apart from this citation move.
    public sealed class NoWindowDragArea : Decorator
    {
        public NoWindowDragArea() => Child = MakeNSView();

        // :1112-1114 makeNSView(context:)
        public NoWindowDragView MakeNSView() => new();

        // :1116 updateNSView(_:context:) {}
        public void UpdateNSView(NoWindowDragView nsView)
        {
        }
    }

    // :1119-1137 final class NoWindowDragView: NSView (was B2 :1122-1140). Unchanged from B2 apart from this
    // citation move.
    public sealed class NoWindowDragView : Control
    {
        // ED-063 (probe P7): a client hit, so the Windows hit test never moves the window from here.
        public NoWindowDragView() => Win32Properties.SetNonClientHitTestResult(this, Win32Properties.Win32HitTestValue.Client);

        // :1120 acceptsFirstMouse(for:)
        public bool AcceptsFirstMouse(PointerEventArgs? @for) => true;

        // :1122-1123 A press here works the control, never the window.
        public bool MouseDownCanMoveWindow => false;

        // :1125-1127 The view draws nothing and keeps nothing: it only takes the hit so the window does not drag.
        public override void Render(DrawingContext context) => context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));

        // :1128 mouseDown(with:) and :1134 rightMouseDown(with:): the press travels on to the superview, left unhandled
        // so it bubbles to the parent the way `superview?.mouseDown(with:)` hands it on.
        protected override void OnPointerPressed(PointerPressedEventArgs e) => base.OnPointerPressed(e);

        // :1130 mouseDragged(with:): travels on to the superview.
        protected override void OnPointerMoved(PointerEventArgs e) => base.OnPointerMoved(e);

        // :1132 mouseUp(with:) and :1136 rightMouseUp(with:): travel on to the superview.
        protected override void OnPointerReleased(PointerReleasedEventArgs e) => base.OnPointerReleased(e);
    }
}

// :958-961 extension Animation. ENGINEERING DECISION (skeleton): a C# 14 `extension(Animation)` block in Chrome beside
// `extension(SwiftTransition)` fails with CS0111 (both static `SoftAppear` getters lower to `get_SoftAppear()` on
// Chrome), so `.softAppear` is a member of the port's partial Animation record, declared here in the twin file the way
// R-FILE-1 places an extension. Call sites read `Animation.SoftAppear` as they would with the extension block.
namespace Marrow.UI.Port
{
    public abstract partial record Animation
    {
        // :960 PR-WIN-206, PR-WIN-208: the timing every soft appearance shares, a 0.32-second smooth curve.
        public static Animation SoftAppear => Smooth(duration: 0.32);
    }
}
