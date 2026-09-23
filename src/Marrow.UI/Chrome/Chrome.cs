using System.Collections.Immutable;
using System.Diagnostics;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Marrow.Core.Port;
using Marrow.UI.Common;
using Marrow.UI.Port;
using Marrow.UI.Theme;

// Source: DroppyCode/UI/Chrome/Chrome.swift (B2 1fbf81e), :1-1140.
// Part 1 of 4 is ported: the window backdrop (:201-273), the surfaces (:125-199), the chrome controls (:306-560), the
// scroll model (:564-577), the search fields and the picker (:775-932), the drag regions (:1083-1140) and the tokens they
// read. Members that still throw MARROW_STUB belong to parts 2 to 4 of this unit.
// Forms (ED-095, ED-098): a SwiftUI View is a Decorator whose constructor builds `body`; a ViewModifier is a sealed
// record with `Control Body(Control content)`, an IAnimatableModifier where a transition animates it; a Layout is a
// Panel; an NSView is a Control; NSImage is Avalonia.Media.IImage; AnyView is Control; a @ViewBuilder parameter is a
// Func<Control>. A parameter without a default that follows one with a default moves before it (ED-095).
// ENGINEERING DECISION (skeleton, no rule names the form): ButtonStyle (:276-280) is a sealed record with
// `Control MakeBody(Control label)`, `configuration.label` being the label; NSViewRepresentable (:1086-1092,
// :1114-1120) is a Decorator exposing MakeNSView()/UpdateNSView(_) without the SwiftUI Context; an NSEvent handler
// is the matching Avalonia pointer override, NSEvent is PointerEventArgs.
// The private top-level structs (:153, :187, :967) are `file` types, C#'s nearest form of a file-private type.
// View mechanics without an Avalonia counterpart of the same shape (a plain Button and TextField, `.background`,
// `.onHover`, `.popover`, `.fixedSize`, a font on any view, an animated colour) are in Port/PlainControls.cs.
// Not drawn, each a missing dependency reported by the unit (see Symbols and WindowBackdrop below): SF Symbol glyphs
// (OD-0010: no unit renders the Lucide table yet), asset-catalogue images, and the app-drawn glass material with its
// .interactive() press response (OD-0005, ADR-0002 Glass (b)); `.continuous` corners are drawn with circular arcs.

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

        // :17
        public static double TrafficLightPitch =>
            throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Chrome/Chrome.swift:17");

        public const double TrafficLightLeading = 18; // :18
        public const double TrafficLightTop = 16; // :19

        // :20
        public static double TrafficLightsWidth =>
            throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Chrome/Chrome.swift:20");

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

        // :40
        public static double ContentTopInset =>
            throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Chrome/Chrome.swift:40");

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

        // :85
        public static SwiftColor Accent =>
            throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Chrome/Chrome.swift:85");

        // :87 accentNSColor: NSColor is SwiftColor too (ED-092).
        public static SwiftColor AccentNSColor =>
            throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Chrome/Chrome.swift:87");

        // :91
        public static SwiftColor Success =>
            throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Chrome/Chrome.swift:91");

        // :92
        public static SwiftColor Warning =>
            throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Chrome/Chrome.swift:92");

        // :93
        public static SwiftColor Danger => ThemeManager.Spec.Danger;

        // :95-96 The theme's glass tint, washed over capsules, sheets and the window.
        public static SwiftColor GlassTint => ThemeManager.Spec.Surface;

        // :98
        public static Animation Hover => Animation.EaseOut(duration: 0.1);

        // :100-104
        public static Animation PanelSlide =>
            throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Chrome/Chrome.swift:100");

        public static Spring GlideSpring { get; } = new(response: 0.6, dampingRatio: 0.86); // :108

        // :112-116
        public static Animation SettleFlight =>
            throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Chrome/Chrome.swift:112");

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
            // :949-955
            public static SwiftTransition SoftAppear =>
                throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Chrome/Chrome.swift:949");

            // :994-1001
            public static SwiftTransition SettleGlide =>
                throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Chrome/Chrome.swift:994");
        }
    }

    // Image(systemName:) and Image(_:) for an asset. OD-0010 draws each SF Symbol as its Lucide counterpart through
    // porting/icons/sf-symbols-to-lucide.json, and the asset catalogue's first-party art ships with the app, but no unit in
    // the tree renders either yet (missing dependency, reported by UI.Chrome.Chrome part 1). The view keeps its place in
    // the layout, its font and its foreground, carries the name it will draw, and draws nothing until that unit lands.
    file static class Symbols
    {
        public static Border Image(string systemName, SwiftFont font) =>
            new Border { Tag = systemName, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }.Font(font);

        /// <summary>Sets the view's <c>.foregroundStyle</c> to a fixed colour that follows the appearance.</summary>
        public static T ForegroundStyle<T>(this T view, SwiftColor color) where T : Control
        {
            view.SetValue(TextElement.ForegroundProperty, color.Brush(view));
            return view;
        }
    }

    // Capsule(style: .continuous): a capsule's corner radius is half its shorter side. The `.continuous` curvature has no
    // unit in the tree (missing dependency, reported); the corner is a circular arc.
    file static class CapsuleShape
    {
        public static T Apply<T>(T border) where T : Border
        {
            border.SizeChanged += (_, e) => border.CornerRadius = new CornerRadius(Math.Min(e.NewSize.Width, e.NewSize.Height) / 2);
            return border;
        }
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
            if (shape == Kind.Circle)
            {
                fill.HorizontalAlignment = HorizontalAlignment.Center;
                fill.VerticalAlignment = VerticalAlignment.Center;
            }
            host.SizeChanged += (_, e) =>
            {
                var side = Math.Min(e.NewSize.Width, e.NewSize.Height);
                fill.CornerRadius = new CornerRadius(side / 2);
                if (shape != Kind.Circle) return;
                fill.Width = side;
                fill.Height = side;
            };
            var state = new ViewState();
            var paint = new AnimatedColor(fill);
            fill.Background = paint.Brush;

            void Draw(Animation? animation)
            {
                // :158 @Environment(\.isOnGlassPanel), :159 @Environment(\.colorScheme)
                if (RenderBudget.GetIsOnGlassPanel(host))
                {
                    // :173-178 the panel's flat control fill, a shade brighter under the pointer.
                    paint.Set(
                        SwiftColor.Dynamic(
                            light: Chrome.PanelControlFill(isDark: false, hovered: state.IsHovered),
                            dark: Chrome.PanelControlFill(isDark: true, hovered: state.IsHovered)),
                        animation);
                }
                else
                {
                    // :179-182 `.glassEffect(.regular.tint(Chrome.glassTint.opacity(0.3))[.interactive()], in: shape)`.
                    // The tint is drawn here over whatever material lies below: over the desktop that is the window's
                    // Acrylic (OD-0005, ADR-0002 Glass (a)). The app-drawn material over a wallpaper or the solid base,
                    // and the press response of IsInteractive, have no unit in the tree (missing dependency, reported).
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
            tint.Fill = SwiftColor.Dynamic(light: Chrome.GlassTint.Opacity(0.16), dark: Chrome.GlassTint.Opacity(0.22)).Brush(tint); // :196
            return new Panel { Children = { wash, tint, content } }; // :194 .background { ... }
        }
    }

    /// <summary>:201-273. One shaped surface for the whole window: glass or a solid fill, a legibility tint and a hairline.</summary>
    public sealed class WindowBackdrop : Decorator
    {
        private enum Base { Wallpaper, Solid, Glass }

        private readonly Panel group = new(); // :225 Group
        private double opacity;
        private IImage? wallpaper;
        private Base? drawn;
        private Control? baseView;
        private Image? picture;
        private Border? scrim;

        /// <param name="opacity">:205 <c>let opacity: Double</c>.</param>
        /// <param name="wallpaper">:207 <c>var wallpaper: NSImage? = nil</c>.</param>
        // :217-272 body
        public WindowBackdrop(double opacity, IImage? wallpaper = null)
        {
            this.opacity = opacity;
            this.wallpaper = wallpaper;
            var tint = Shape();
            tint.Background = Chrome.GlassTint.Opacity(0.12).Brush(tint); // :263-265
            // :266-271 The hairline sits on the glass as well as on the solid fill; strokeBorder is an inner stroke,
            // which is how an Avalonia Border draws its border.
            var hairline = Shape();
            hairline.BorderThickness = new Thickness(1);
            hairline.BorderBrush = Chrome.Overlay(0.14).Brush(hairline);
            Child = new Panel { Children = { group, tint, hairline } };
            ActualThemeVariantChanged += (_, _) => Draw(); // :203 @Environment(\.colorScheme)
            Draw();
        }

        /// <summary>
        /// The parent passing new inputs (R-VIEW-1): <c>WindowBackdrop(opacity:wallpaper:)</c> built again from
        /// RootView.swift:64. Setting or clearing the wallpaper animates with <c>.easeInOut(duration: 0.3)</c> (:262).
        /// </summary>
        public void Update(double opacity, IImage? wallpaper)
        {
            var hadWallpaper = this.wallpaper is not null;
            this.opacity = opacity;
            this.wallpaper = wallpaper;
            if (hadWallpaper != wallpaper is not null)
                Transaction.WithAnimation(Animation.EaseInOut(duration: 0.3), Draw); // :262 .animation(_, value: wallpaper != nil)
            else
                Draw();
        }

        // :211-215 static func scrim(for:isDark:)
        public static double Scrim(double @for, bool isDark)
        {
            var stock = isDark ? 0.26 : 0.18; // :212
            var floored = 0 >= @for ? 0 : @for; // :213 max(opacity, 0)
            var t = 1 < floored ? 1 : floored; // :213 min(_, 1)
            return t <= 0.5 ? stock * (t / 0.5) : stock + (1 - stock) * ((t - 0.5) / 0.5); // :214
        }

        // :218 RoundedRectangle(cornerRadius: Chrome.windowCornerRadius, style: .continuous), the size of the backdrop.
        private static Border Shape() => new() { CornerRadius = new CornerRadius(Chrome.WindowCornerRadius) };

        private void Draw()
        {
            var isDark = PlainControls.IsDark(ActualThemeVariant); // :219
            var isSolid = opacity >= 0.98; // :220-224
            var next = wallpaper is not null ? Base.Wallpaper : isSolid ? Base.Solid : Base.Glass; // :226, :249, :251
            if (next != drawn)
            {
                if (baseView is { } old) SwiftTransition.Remove(group, old);
                baseView = next switch
                {
                    Base.Wallpaper => WallpaperBase(),
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
            if (scrim is not null) scrim.Opacity = Scrim(opacity, isDark); // :258
        }

        // :226-248 The picture rides over the desktop: the setting is its own opacity, and no scrim is drawn here.
        private Control WallpaperBase()
        {
            picture = new Image { Stretch = Stretch.UniformToFill }; // :235, :237 .resizable().aspectRatio(contentMode: .fill)
            RenderOptions.SetBitmapInterpolationMode(picture, BitmapInterpolationMode.HighQuality); // :236 .interpolation(.high)
            scrim = null;
            var clip = Shape(); // :232 shape.fill(.clear)
            clip.ClipToBounds = true; // :240 .clipShape(shape)
            clip.Child = picture; // :233 .overlay
            // :241-247 `.overlay { if !isSolid { shape.fill(.clear).glassEffect(in: shape) } }`: over the picture the app
            // draws its own backdrop, where OD-0005 calls for the app-drawn material (ADR-0002 Glass (b)). No unit in the
            // tree renders that material (missing dependency, reported), so nothing is drawn over the picture yet.
            return clip.Transition(SwiftTransition.Opacity); // :248 .transition(.opacity)
        }

        // :249-250 `shape.fill(isDark ? Color.black : Color.white)`
        private Control SolidBase()
        {
            picture = null;
            scrim = null;
            var solid = Shape();
            solid.Background = SwiftColor.Dynamic(light: SwiftColor.White, dark: SwiftColor.Black).Brush(solid);
            return solid;
        }

        // :251-259 glass under the scrim
        private Control GlassBase()
        {
            picture = null;
            // :252-254 `shape.fill(.clear).glassEffect(in: shape)`: with no wallpaper the window shows the desktop, and
            // there the glass is the window's own Acrylic (OD-0005, ADR-0002 Glass (a), OD-0014), so the app draws
            // nothing under the scrim.
            scrim = Shape();
            scrim.Background = SwiftColor.Dynamic(light: SwiftColor.White, dark: SwiftColor.Black).Brush(scrim); // :256-257
            return new Panel { Children = { scrim } }; // :255 .overlay
        }
    }

    // :275-280 struct ChromeQuietButtonStyle: ButtonStyle
    public sealed record ChromeQuietButtonStyle
    {
        // :277-279 makeBody(configuration:); label is configuration.label.
        public Control MakeBody(Control label) =>
            throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Chrome/Chrome.swift:277");
    }

    /// <summary>:282-304 <c>struct ChromeRevealLayout: Layout</c>. Reveals its single child by height.</summary>
    public sealed class ChromeRevealLayout : Panel
    {
        // Memberwise init ChromeRevealLayout(fraction:) (ModelsSettingsPage.swift:370, SettingsView.swift:671).
        public ChromeRevealLayout(double fraction) =>
            throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Chrome/Chrome.swift:287");

        // :287 var fraction: CGFloat
        public double Fraction
        {
            get => throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Chrome/Chrome.swift:287");
            set => throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Chrome/Chrome.swift:287");
        }

        // :289-292 var animatableData: CGFloat
        public double AnimatableData
        {
            get => throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Chrome/Chrome.swift:290");
            set => throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Chrome/Chrome.swift:291");
        }

        // :294-298 sizeThatFits(proposal:subviews:cache:)
        protected override Size MeasureOverride(Size availableSize) =>
            throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Chrome/Chrome.swift:294");

        // :300-303 placeSubviews(in:proposal:subviews:cache:)
        protected override Size ArrangeOverride(Size finalSize) =>
            throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Chrome/Chrome.swift:300");
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
            var bar = new Border { CornerRadius = new CornerRadius(0.5), Width = 1, Height = Chrome.DividerHeight }; // :326, :328
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
        private Ellipse? activeCircle;

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
            frame = new Panel
            {
                Width = Chrome.CapsuleContentHeight, // :343
                Height = Chrome.CapsuleContentHeight,
                Background = Brushes.Transparent, // :351 .contentShape(.rect)
                Children = { image },
            };
            Child = frame;
            Update(isEnabled, isActive, isHovering);
        }

        /// <summary>The parent passing new inputs (R-VIEW-1): the owning button's hover and presentation state.</summary>
        public void Update(bool isEnabled, bool isActive, bool isHovering)
        {
            this.isEnabled = isEnabled;
            this.isActive = isActive;
            this.isHovering = isHovering;
            foreground.Set(Foreground, Transaction.Current);
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
                return Chrome.PrimaryText.Opacity(isHovering ? 1 : 0.92); // :357
            }
        }
    }

    // :361-383 struct ChromeIconButton: View
    public sealed class ChromeIconButton : Decorator
    {
        // :368 @State private var isHovering = false
        private sealed class ViewState : MainActorModel
        {
            private bool isHovering;

            public bool IsHovering { get => Read(isHovering); set => Set(ref isHovering, value); }
        }

        /// <summary>Swift members symbol, isEnabled, isActive, help, action (:362-366); help and action have no
        /// default, so they move before the defaulted ones (ED-095) and callers name the labels.</summary>
        // :370-382 body
        public ChromeIconButton(string symbol, string help, Action action, bool isEnabled = true, bool isActive = false)
        {
            var state = new ViewState();
            var label = new ChromeIconLabel(symbol, isEnabled, isActive, isHovering: false); // :372
            label.Track(() => state.IsHovering, hovering => label.Update(isEnabled, isActive, hovering));
            var button = PlainControls.Button(label, action); // :371, :374 .buttonStyle(.plain)
            button.IsEnabled = isEnabled; // :375 .disabled(!isEnabled)
            button.OnHover(hovering => Transaction.WithAnimation(Chrome.Hover, () => state.IsHovering = hovering)); // :376-378
            ToolTip.SetTip(button, help); // :379 .help(help)
            AutomationProperties.SetName(button, help); // :380 .accessibilityLabel(Text(help))
            Child = PlainControls.Background(button, new NoWindowDragArea()); // :381
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

            public bool IsHovering { get => Read(isHovering); set => Set(ref isHovering, value); }

            public bool IsPresented { get => Read(isPresented); set => Set(ref isPresented, value); }
        }

        /// <param name="symbol">:386 <c>let symbol: String</c>.</param>
        /// <param name="help">:387 <c>let help: String</c>.</param>
        /// <param name="content">:388 <c>@ViewBuilder var content: Content</c>.</param>
        // :393-409 body
        public ChromeMenuButton(string symbol, string help, Func<Control> content)
        {
            var state = new ViewState();
            var label = new ChromeIconLabel(symbol, isActive: false, isHovering: false); // :397
            label.Track(() => (state.IsPresented, state.IsHovering), shown => label.Update(isEnabled: true, shown.IsPresented, shown.IsHovering));
            var button = PlainControls.Button(label, () => state.IsPresented = !state.IsPresented); // :394-396, :399
            button.OnHover(hovering => Transaction.WithAnimation(Chrome.Hover, () => state.IsHovering = hovering)); // :400-402
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
                foreground.Set(Chrome.PrimaryText.Opacity(hovering ? 1 : 0.92), Transaction.Current)); // :424
            var label = new Panel
            {
                Width = Chrome.CapsuleHeight, // :425
                Height = Chrome.CapsuleHeight,
                Children = { new Ellipse { Fill = Brushes.Transparent }, image }, // :426 .contentShape(Circle())
            };
            var button = PlainControls.Button(label, action); // :421, :428
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
                foreground.Set(Chrome.PrimaryText.Opacity(full ? 1 : 0.92), Transaction.Current)); // :454
            var label = new Panel
            {
                Width = Chrome.CapsuleHeight, // :455
                Height = Chrome.CapsuleHeight,
                Children = { new Ellipse { Fill = Brushes.Transparent }, image }, // :456 .contentShape(Circle())
            };
            var button = PlainControls.Button(label, () => state.IsPresented = !state.IsPresented); // :449-451, :458
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
            var label = CapsuleShape.Apply(new Border
            {
                Padding = new Thickness(0, Chrome.CapsuleVerticalPadding), // :499
                Background = Brushes.Transparent, // :500 .contentShape(Capsule(style: .continuous))
                Child = framed,
            });
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
            var label = CapsuleShape.Apply(new Border
            {
                Padding = new Thickness(0, Chrome.CapsuleVerticalPadding), // :540
                Background = Brushes.Transparent, // :541 .contentShape(Capsule(style: .continuous))
                Child = framed,
            });
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
        /// <param name="model">:582 <c>let model: ChromeScrollModel</c>.</param>
        // :585-611 body
        public PaneTopVeil(ChromeScrollModel model) =>
            throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Chrome/Chrome.swift:585");
    }

    // :630-647 struct ChromeCompactTitle: View
    public sealed class ChromeCompactTitle : Decorator
    {
        /// <param name="title">:632 <c>let title: String</c>.</param>
        /// <param name="model">:633 <c>let model: ChromeScrollModel</c>.</param>
        // :635-646 body
        public ChromeCompactTitle(string title, ChromeScrollModel model) =>
            throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Chrome/Chrome.swift:635");
    }

    // MARK: - Sidebar pieces

    // :651-668 struct SidebarSymbol: View
    public sealed class SidebarSymbol : Decorator
    {
        // :655-658 init(_ name: String, scale: CGFloat = 1); :660-667 body
        public SidebarSymbol(string name, double scale = 1) =>
            throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Chrome/Chrome.swift:655");
    }

    // :670-679 struct SidebarIconBadge<Glyph: View>: View
    public sealed class SidebarIconBadge : Decorator
    {
        /// <param name="glyph">:672 <c>@ViewBuilder var glyph: Glyph</c>.</param>
        // :674-678 body
        public SidebarIconBadge(Func<Control> glyph) =>
            throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Chrome/Chrome.swift:674");
    }

    // :681-757 struct SidebarRow<Icon: View, Accessory: View>: View
    public sealed class SidebarRow : Decorator
    {
        /// <summary>:692-708 <c>init(title:isSelected:isEmphasized:accessoryWidth:action:icon:accessory:)</c>; action,
        /// icon and accessory have no default, so they move before the defaulted ones (ED-095) and callers name the
        /// labels. <paramref name="icon"/> is the @ViewBuilder icon, <paramref name="accessory"/> the @ViewBuilder
        /// <c>(Bool) -&gt; Accessory</c> given the hover state.</summary>
        // :710-739 body
        public SidebarRow(
            string title,
            Action action,
            Func<Control> icon,
            Func<bool, Control> accessory,
            bool isSelected = false,
            bool isEmphasized = false,
            double accessoryWidth = 0) =>
            throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Chrome/Chrome.swift:692");

        /// <summary>:748-757 <c>extension SidebarRow where Accessory == EmptyView</c>,
        /// <c>init(title:isSelected:action:icon:)</c>.</summary>
        public SidebarRow(string title, Action action, Func<Control> icon, bool isSelected = false) =>
            throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Chrome/Chrome.swift:749");

        // :741-745
        private SwiftColor Fill =>
            throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Chrome/Chrome.swift:741");
    }

    // :759-773 struct RowAccessoryIcon: View
    public sealed class RowAccessoryIcon : Decorator
    {
        // :762-764 init(_ symbol: String); :766-772 body
        public RowAccessoryIcon(string symbol) =>
            throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Chrome/Chrome.swift:762");
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
            var capsule = CapsuleShape.Apply(new Border
            {
                Padding = new Thickness(9, 0), // :808
                Height = 30, // :809
                Child = row,
            });
            capsule.Background = Chrome.Overlay(0.07).Brush(capsule); // :810-813 the capsule background, also the content shape
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
            clear.Track(() => query.Value.Length == 0, isEmpty =>
            {
                clear.Opacity = isEmpty ? 0 : 1; // :856
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
            Border? symbolView = null;
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
                        assetView = new Border { Width = 14, Height = 14, VerticalAlignment = VerticalAlignment.Center };
                        SwiftTransition.Insert(row, 0, assetView);
                    }
                    assetView.Tag = name; // the asset the missing image unit will draw (see Symbols)
                }
                else if (assetView is { } goneAsset)
                {
                    assetView = null;
                    SwiftTransition.Remove(row, goneAsset);
                }
                // :896-900 `if let symbol = symbol(selection) { Image(systemName: symbol).font(.system(.callout, weight: .medium)).foregroundStyle(Chrome.secondaryText) }`
                if (shown.Symbol is { } glyph)
                {
                    if (symbolView is null)
                    {
                        symbolView = Symbols.Image(glyph, SwiftFont.System(SwiftFont.TextStyle.Callout, weight: SwiftFont.Weight.Medium))
                            .ForegroundStyle(Chrome.SecondaryText);
                        SwiftTransition.Insert(row, row.Children.IndexOf(text), symbolView);
                    }
                    symbolView.Tag = glyph;
                }
                else if (symbolView is { } goneSymbol)
                {
                    symbolView = null;
                    SwiftTransition.Remove(row, goneSymbol);
                }
            });
            var label = CapsuleShape.Apply(new Border
            {
                Padding = new Thickness(Chrome.CapsuleHorizontalPadding, 0), // :910
                Height = Chrome.CapsuleContentHeight, // :911
                Background = Brushes.Transparent, // :912 .contentShape(Capsule(style: .continuous))
                Child = row,
            });
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
    /// transitions (:951, :996, ThreadTimeline.swift:318), so it is an IAnimatableModifier (ED-098); its only property is
    /// a Bool, so SwiftUI gives it EmptyAnimatableData.</summary>
    public sealed record SoftAppearModifier(bool IsVisible) : IAnimatableModifier // :938
    {
        // :937 (no animatableData: EmptyAnimatableData)
        public ImmutableArray<double> AnimatableData =>
            throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Chrome/Chrome.swift:937");

        // :937
        public IAnimatableModifier WithAnimatableData(ImmutableArray<double> data) =>
            throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Chrome/Chrome.swift:937");

        // :940-944
        public Control Body(Control content) =>
            throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Chrome/Chrome.swift:940");

        // :940-944, applied per frame (ED-098)
        public void Update(Control body) =>
            throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Chrome/Chrome.swift:940");
    }

    // :963-988 private struct SettleGlideModifier: ViewModifier, Animatable (ED-098)
    file sealed record SettleGlideModifier(double Progress) : IAnimatableModifier // :968
    {
        // :970-973 var animatableData: Double
        public ImmutableArray<double> AnimatableData =>
            throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Chrome/Chrome.swift:971");

        // :972
        public IAnimatableModifier WithAnimatableData(ImmutableArray<double> data) =>
            throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Chrome/Chrome.swift:972");

        // :975-982
        public Control Body(Control content) =>
            throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Chrome/Chrome.swift:975");

        // :975-982, applied per frame (ED-098)
        public void Update(Control body) =>
            throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Chrome/Chrome.swift:975");

        // :984-987
        private static double Smoothstep(double edge0, double edge1, double x) =>
            throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Chrome/Chrome.swift:984");
    }

    // MARK: - Cards

    // :1006-1021 struct ChromeCard<Content: View>: View
    public sealed class ChromeCard : Decorator
    {
        /// <param name="content">:1007 <c>@ViewBuilder var content: Content</c>.</param>
        // :1009-1020 body
        public ChromeCard(Func<Control> content) =>
            throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Chrome/Chrome.swift:1009");
    }

    // :1023-1047 struct ChromeSection<Content: View>: View
    public sealed class ChromeSection : Decorator
    {
        /// <param name="title">:1024 <c>let title: String</c>.</param>
        /// <param name="content">:1029 <c>@ViewBuilder var content: Content</c>.</param>
        /// <param name="accessory">:1026 <c>var accessory: AnyView? = nil</c>.</param>
        /// <param name="trailing">:1028 <c>var trailing: AnyView? = nil</c>.</param>
        // :1031-1046 body
        public ChromeSection(string title, Func<Control> content, Control? accessory = null, Control? trailing = null) =>
            throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Chrome/Chrome.swift:1031");
    }

    // :1049-1074 struct ChromeRow<Control: View>: View
    public sealed class ChromeRow : Decorator
    {
        /// <param name="title">:1050 <c>let title: String</c>.</param>
        /// <param name="control">:1052 <c>@ViewBuilder var control: Control</c>.</param>
        /// <param name="detail">:1051 <c>var detail: String?</c>.</param>
        // :1054-1073 body
        public ChromeRow(string title, Func<Control> control, string? detail = null) =>
            throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Chrome/Chrome.swift:1054");
    }

    // :1076-1081 struct ChromeRowDivider: View
    public sealed class ChromeRowDivider : Decorator
    {
        // :1077-1080 body
        public ChromeRowDivider() =>
            throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Chrome/Chrome.swift:1077");
    }

    // MARK: - Window drag

    // :1085-1092 struct WindowDragArea: NSViewRepresentable. Lets a stretch of chrome move the window.
    public sealed class WindowDragArea : Decorator
    {
        public WindowDragArea() => Child = MakeNSView();

        // :1087-1089 makeNSView(context:)
        public WindowDragView MakeNSView() => new();

        // :1091 updateNSView(_:context:) {}
        public void UpdateNSView(WindowDragView nsView)
        {
        }
    }

    // :1094-1102 final class WindowDragView: NSView
    public sealed class WindowDragView : Control
    {
        // ED-063 (probe P7): the region is a caption to the Windows hit test, so the system moves the window.
        public WindowDragView() => Win32Properties.SetNonClientHitTestResult(this, Win32Properties.Win32HitTestValue.Caption);

        // :1095 acceptsFirstMouse(for:)
        public bool AcceptsFirstMouse(PointerEventArgs? @for) => true;

        // The view takes the hit over its whole frame, as an NSView does, and draws nothing.
        public override void Render(DrawingContext context) => context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));

        // :1097-1099 mouseDown(with:): `window?.performDrag(with: event)`; the press goes no further.
        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            if (e.GetCurrentPoint(this).Properties.PointerUpdateKind != PointerUpdateKind.LeftButtonPressed) return;
            (TopLevel.GetTopLevel(this) as Window)?.BeginMoveDrag(e);
            e.Handled = true;
        }

        // :1101 mouseUp(with:) {}: the release is swallowed and never reaches the UI behind the region.
        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            if (e.GetCurrentPoint(this).Properties.PointerUpdateKind != PointerUpdateKind.LeftButtonReleased) return;
            e.Handled = true;
        }
    }

    // :1104-1120 struct NoWindowDragArea: NSViewRepresentable. Marks a chrome control's hit area as never dragging the
    // window; empty chrome around the controls has no such view behind it and still drags the window like a title bar.
    public sealed class NoWindowDragArea : Decorator
    {
        public NoWindowDragArea() => Child = MakeNSView();

        // :1115-1117 makeNSView(context:)
        public NoWindowDragView MakeNSView() => new();

        // :1119 updateNSView(_:context:) {}
        public void UpdateNSView(NoWindowDragView nsView)
        {
        }
    }

    // :1122-1140 final class NoWindowDragView: NSView
    public sealed class NoWindowDragView : Control
    {
        // ED-063 (probe P7): a client hit, so the Windows hit test never moves the window from here.
        public NoWindowDragView() => Win32Properties.SetNonClientHitTestResult(this, Win32Properties.Win32HitTestValue.Client);

        // :1123 acceptsFirstMouse(for:)
        public bool AcceptsFirstMouse(PointerEventArgs? @for) => true;

        // :1125-1126 A press here works the control, never the window.
        public bool MouseDownCanMoveWindow => false;

        // :1128-1130 The view draws nothing and keeps nothing: it only takes the hit so the window does not drag.
        public override void Render(DrawingContext context) => context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));

        // :1131 mouseDown(with:) and :1137 rightMouseDown(with:): the press travels on to the superview, left unhandled
        // so it bubbles to the parent the way `superview?.mouseDown(with:)` hands it on.
        protected override void OnPointerPressed(PointerPressedEventArgs e) => base.OnPointerPressed(e);

        // :1133 mouseDragged(with:): travels on to the superview.
        protected override void OnPointerMoved(PointerEventArgs e) => base.OnPointerMoved(e);

        // :1135 mouseUp(with:) and :1139 rightMouseUp(with:): travel on to the superview.
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
        // :960
        public static Animation SoftAppear =>
            throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Chrome/Chrome.swift:960");
    }
}
