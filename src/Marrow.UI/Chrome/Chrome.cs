using System.Collections.Immutable;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Marrow.Core.Port;
using Marrow.UI.Port;

// Source: DroppyCode/UI/Chrome/Chrome.swift (B2 1fbf81e), :1-1140.
// SKELETON (cycle c35). Every member with behaviour throws MARROW_STUB until its unit ports it.
// Stored `static let` tokens are complete (a throwing static initializer would break the whole type); computed
// `static var` tokens are stub getters.
// Forms (ED-095, ED-098): a SwiftUI View is a Decorator whose constructor builds `body`; a ViewModifier is a sealed
// record with `Control Body(Control content)`, an IAnimatableModifier where a transition animates it; a Layout is a
// Panel; an NSView is a Control; NSImage is Avalonia.Media.IImage; AnyView is Control; a @ViewBuilder parameter is a
// Func<Control>. A parameter without a default that follows one with a default moves before it (ED-095).
// ENGINEERING DECISION (skeleton, no rule names the form): ButtonStyle (:276-280) is a sealed record with
// `Control MakeBody(Control label)`, `configuration.label` being the label; NSViewRepresentable (:1086-1092,
// :1114-1120) is a Decorator exposing MakeNSView()/UpdateNSView(_) without the SwiftUI Context; an NSEvent handler
// is the matching Avalonia pointer override, NSEvent is PointerEventArgs.
// The private top-level structs (:153, :187, :967) are `file` types, C#'s nearest form of a file-private type.
// Not declared yet, private in Swift and read by no other file: the @State and @Environment storage of every view
// (:158-160, :188, :203, :368, :390-391, :418, :445-446, :479-480, :526, :583, :690, :881-882) and
// PaneTopVeil.fade(_:) (:616-627), which the implementing unit adds with the bodies.

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
        public static double CapsuleHeight =>
            throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Chrome/Chrome.swift:38");

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
        public static SwiftColor Overlay(double opacity) =>
            throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Chrome/Chrome.swift:71");

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
        public static SwiftColor Danger =>
            throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Chrome/Chrome.swift:93");

        // :96
        public static SwiftColor GlassTint =>
            throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Chrome/Chrome.swift:96");

        // :98
        public static Animation Hover =>
            throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Chrome/Chrome.swift:98");

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

        // :129-131
        public static Control ChromeGlassCapsule(this Control content) =>
            throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Chrome/Chrome.swift:129");

        // :137-139
        public static Control ChromeGlassFieldCapsule(this Control content) =>
            throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Chrome/Chrome.swift:137");

        // :141-143
        public static Control ChromeGlassCircle(this Control content) =>
            throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Chrome/Chrome.swift:141");

        // :146-148
        public static Control DetailSheet(this Control content) =>
            throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Chrome/Chrome.swift:146");

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

    // :151-185 private struct ChromeGlassSurface: ViewModifier
    file sealed record ChromeGlassSurface(ChromeGlassSurface.Kind Shape, bool IsInteractive = true) // :155, :157
    {
        public enum Kind { Capsule, Circle } // :154

        // :162-169
        public Control Body(Control content) =>
            throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Chrome/Chrome.swift:162");

        // :171-184
        private Control Surface(Control content, Kind shape) =>
            throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Chrome/Chrome.swift:172");
    }

    // :187-199 private struct DetailSheetModifier: ViewModifier
    file sealed record DetailSheetModifier
    {
        // :190-198
        public Control Body(Control content) =>
            throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Chrome/Chrome.swift:190");
    }

    /// <summary>:201-273. One shaped surface for the whole window: glass or a solid fill, a legibility tint and a hairline.</summary>
    public sealed class WindowBackdrop : Decorator
    {
        /// <param name="opacity">:205 <c>let opacity: Double</c>.</param>
        /// <param name="wallpaper">:207 <c>var wallpaper: NSImage? = nil</c>.</param>
        // :217-272 body
        public WindowBackdrop(double opacity, IImage? wallpaper = null) =>
            throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Chrome/Chrome.swift:217");

        // :211-215 static func scrim(for:isDark:)
        public static double Scrim(double @for, bool isDark) =>
            throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Chrome/Chrome.swift:211");
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
        public ChromeCapsule(Func<Control> content) =>
            throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Chrome/Chrome.swift:311");
    }

    // :324-331 struct ChromeDivider: View
    public sealed class ChromeDivider : Decorator
    {
        // :325-330 body
        public ChromeDivider() =>
            throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Chrome/Chrome.swift:325");
    }

    // :333-359 struct ChromeIconLabel: View
    public sealed class ChromeIconLabel : Decorator
    {
        /// <param name="symbol">:334 <c>let symbol: String</c>.</param>
        /// <param name="isEnabled">:335 <c>var isEnabled = true</c>.</param>
        /// <param name="isActive">:336 <c>var isActive = false</c>.</param>
        /// <param name="isHovering">:337 <c>var isHovering = false</c>.</param>
        // :339-352 body
        public ChromeIconLabel(string symbol, bool isEnabled = true, bool isActive = false, bool isHovering = false) =>
            throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Chrome/Chrome.swift:339");

        // :354-358
        private SwiftColor Foreground =>
            throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Chrome/Chrome.swift:354");
    }

    // :361-383 struct ChromeIconButton: View
    public sealed class ChromeIconButton : Decorator
    {
        /// <summary>Swift members symbol, isEnabled, isActive, help, action (:362-366); help and action have no
        /// default, so they move before the defaulted ones (ED-095) and callers name the labels.</summary>
        // :370-382 body
        public ChromeIconButton(string symbol, string help, Action action, bool isEnabled = true, bool isActive = false) =>
            throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Chrome/Chrome.swift:370");
    }

    // :385-410 struct ChromeMenuButton<Content: View>: View
    public sealed class ChromeMenuButton : Decorator
    {
        /// <param name="symbol">:386 <c>let symbol: String</c>.</param>
        /// <param name="help">:387 <c>let help: String</c>.</param>
        /// <param name="content">:388 <c>@ViewBuilder var content: Content</c>.</param>
        // :393-409 body
        public ChromeMenuButton(string symbol, string help, Func<Control> content) =>
            throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Chrome/Chrome.swift:393");
    }

    // :412-437 struct ChromeCircleButton: View
    public sealed class ChromeCircleButton : Decorator
    {
        /// <param name="symbol">:414 <c>let symbol: String</c>.</param>
        /// <param name="help">:415 <c>let help: String</c>.</param>
        /// <param name="action">:416 <c>let action: () -&gt; Void</c>.</param>
        // :420-436 body
        public ChromeCircleButton(string symbol, string help, Action action) =>
            throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Chrome/Chrome.swift:420");
    }

    // :439-470 struct ChromeCircleMenu<Content: View>: View
    public sealed class ChromeCircleMenu : Decorator
    {
        /// <param name="symbol">:441 <c>let symbol: String</c>.</param>
        /// <param name="help">:442 <c>let help: String</c>.</param>
        /// <param name="content">:443 <c>@ViewBuilder var content: Content</c>.</param>
        // :448-469 body
        public ChromeCircleMenu(string symbol, string help, Func<Control> content) =>
            throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Chrome/Chrome.swift:448");
    }

    // :472-514 struct ChromeTextMenu<Content: View>: View
    public sealed class ChromeTextMenu : Decorator
    {
        /// <param name="symbol">:474 <c>let symbol: String</c>.</param>
        /// <param name="title">:475 <c>let title: String</c>.</param>
        /// <param name="help">:476 <c>let help: String</c>.</param>
        /// <param name="content">:477 <c>@ViewBuilder var content: Content</c>.</param>
        // :482-513 body
        public ChromeTextMenu(string symbol, string title, string help, Func<Control> content) =>
            throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Chrome/Chrome.swift:482");
    }

    // :516-560 struct ChromeTextButton: View
    public sealed class ChromeTextButton : Decorator
    {
        /// <summary>Swift members symbol, title, help, isEnabled, isDestructive, action (:519-524); action has no
        /// default, so it moves before the defaulted ones (ED-095) and callers name the labels.</summary>
        // :528-553 body
        public ChromeTextButton(string symbol, string title, string help, Action action, bool isEnabled = true, bool isDestructive = false) =>
            throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Chrome/Chrome.swift:528");

        // :555-559
        private SwiftColor Foreground =>
            throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Chrome/Chrome.swift:555");
    }

    // MARK: - Scroll morph

    /// <summary>:564-577 <c>@MainActor @Observable final class ChromeScrollModel</c> (R-OBS-6).</summary>
    public sealed class ChromeScrollModel : MainActorModel
    {
        private double progress; // :569 = 0

        // :569 private(set) var progress: CGFloat = 0
        public double Progress { get => Read(progress); private set => Set(ref progress, value); }

        // :571-576 func update(travel:)
        public void Update(double travel) =>
            throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Chrome/Chrome.swift:571");
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
        /// <param name="text">:776 <c>@Binding var text: String</c>.</param>
        /// <param name="prompt">:777 <c>var prompt = "Search"</c>.</param>
        /// <param name="onSubmit">:778 <c>var onSubmit: () -&gt; Void = {}</c>; null stands for the empty closure.</param>
        /// <param name="onMove">:782 <c>var onMove: ((Int) -&gt; Void)?</c>.</param>
        // :784-816 body
        public SidebarSearchField(Binding<string> text, string prompt = "Search", Action? onSubmit = null, Action<int>? onMove = null) =>
            throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Chrome/Chrome.swift:784");

        // :818-822 private func move(_:) -> KeyPress.Result; true is .handled, false .ignored.
        private bool Move(int offset) =>
            throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Chrome/Chrome.swift:818");
    }

    // :825-869 struct ChromeSearchField: View
    public sealed class ChromeSearchField : Decorator
    {
        // :828 static let width: CGFloat = 200. Swift name kept; `new` because Decorator inherits Layoutable.Width.
        public new const double Width = 200;

        /// <param name="query">:830 <c>@Binding var query: String</c>.</param>
        /// <param name="prompt">:831 <c>var prompt = "Search"</c>.</param>
        // :833-868 body
        public ChromeSearchField(Binding<string> query, string prompt = "Search") =>
            throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Chrome/Chrome.swift:833");
    }

    // :871-932 struct GlassPickerButton<Value: Hashable>: View. Callers pass optional values (HydraSettingsPage.swift:412)
    // and enums, so TValue is unconstrained and compared with EqualityComparer<TValue>.Default.
    public sealed class GlassPickerButton<TValue> : Decorator
    {
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
            double? maxWidth = null) =>
            throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Chrome/Chrome.swift:884");
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

    // :1085-1092 struct WindowDragArea: NSViewRepresentable
    public sealed class WindowDragArea : Decorator
    {
        public WindowDragArea() =>
            throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Chrome/Chrome.swift:1087");

        // :1087-1089 makeNSView(context:)
        public WindowDragView MakeNSView() =>
            throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Chrome/Chrome.swift:1087");

        // :1091 updateNSView(_:context:)
        public void UpdateNSView(WindowDragView nsView) =>
            throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Chrome/Chrome.swift:1091");
    }

    // :1094-1102 final class WindowDragView: NSView
    public sealed class WindowDragView : Control
    {
        // :1095 acceptsFirstMouse(for:)
        public bool AcceptsFirstMouse(PointerEventArgs? @for) =>
            throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Chrome/Chrome.swift:1095");

        // :1097-1099 mouseDown(with:)
        protected override void OnPointerPressed(PointerPressedEventArgs e) =>
            throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Chrome/Chrome.swift:1097");

        // :1101 mouseUp(with:)
        protected override void OnPointerReleased(PointerReleasedEventArgs e) =>
            throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Chrome/Chrome.swift:1101");
    }

    // :1104-1120 struct NoWindowDragArea: NSViewRepresentable
    public sealed class NoWindowDragArea : Decorator
    {
        public NoWindowDragArea() =>
            throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Chrome/Chrome.swift:1115");

        // :1115-1117 makeNSView(context:)
        public NoWindowDragView MakeNSView() =>
            throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Chrome/Chrome.swift:1115");

        // :1119 updateNSView(_:context:)
        public void UpdateNSView(NoWindowDragView nsView) =>
            throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Chrome/Chrome.swift:1119");
    }

    // :1122-1140 final class NoWindowDragView: NSView
    public sealed class NoWindowDragView : Control
    {
        // :1123 acceptsFirstMouse(for:)
        public bool AcceptsFirstMouse(PointerEventArgs? @for) =>
            throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Chrome/Chrome.swift:1123");

        // :1126 mouseDownCanMoveWindow
        public bool MouseDownCanMoveWindow =>
            throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Chrome/Chrome.swift:1126");

        // :1131 mouseDown(with:) and :1137 rightMouseDown(with:)
        protected override void OnPointerPressed(PointerPressedEventArgs e) =>
            throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Chrome/Chrome.swift:1131");

        // :1133 mouseDragged(with:)
        protected override void OnPointerMoved(PointerEventArgs e) =>
            throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Chrome/Chrome.swift:1133");

        // :1135 mouseUp(with:) and :1139 rightMouseUp(with:)
        protected override void OnPointerReleased(PointerReleasedEventArgs e) =>
            throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Chrome/Chrome.swift:1135");
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
