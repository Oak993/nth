using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;
using Marrow.UI.Port;

// Source: DroppyCode/App/Views/Composer/EffortSlider.swift (B2 1fbf81e), :1-1347.
// SKELETON (cycle c100, group 3). Every member with behaviour throws MARROW_STUB until its unit ports it.
// Forms (ED-095): a SwiftUI View is a Decorator whose constructor builds `body`; a parameter without a default that
// follows one with a default moves before it and callers name their arguments; `@Binding` is a Binding<T> parameter
// (R-VIEW-2). A Hashable struct is a sealed record (R-VAL-1); `[Color]` is ValueList<SwiftColor>. EffortBrand is an
// enum without raw values (a plain C# enum); its init and members are a C# 14 extension block (R-NAME-4, ED-164), so
// `EffortBrand(provider:)` is `EffortBrand.Create(provider)`. A member named like its nested type takes the owner's
// name (R-NAME-3): TrackLook.kind is TrackLookKind. GraphicsContext is Avalonia's DrawingContext, CGSize Avalonia.Size.
// NEEDS DECISION (group report): `AnyShapeStyle`/`LinearGradient` have no port type, so EffortBrand.title (:885-890),
// EffortBrand.fill (:893-908), TrackLook.fill (:985-998) and EffortSliderCard.titleStyle (:543-555) are not declared
// yet; SwiftColor has no value equality, so the records holding colours (EffortPairBrand, TrackLook, EffortPairLook)
// compare them by reference until that is settled.
// Not declared yet, private in Swift and read by no other file: the @Environment/@State storage of every view,
// ModelEffortButton.helpText (:142-149), ModelEffortPanel.slider/remember/choose (:191-258), ModelList.detail
// (:364-367), EffortSliderCard's computed properties (:521-555), EffortSlider.look/nearestStop (:729-738, :833-836),
// which the implementing unit adds with the bodies. The Swift-private view types are public, as the App views
// ported so far do (ShortcutsSettingsPage.cs).

namespace Marrow.App.Views.Composer;

/// <summary>:4-31 <c>@MainActor enum ModelCatalog</c>.</summary>
public static class ModelCatalog
{
    /// <summary>:7-12 <c>struct Entry: Identifiable, Hashable</c>.</summary>
    public sealed record Entry(ProviderKind Provider, ModelOption Option) // :8-9
    {
        // :11 var id: String
        public string Id =>
            throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Composer/EffortSlider.swift:11");
    }

    // :14-30 static func entries(_:including:) -> [Entry]
    public static ValueList<Entry> Entries(AppModel model, ChatThread? including) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Composer/EffortSlider.swift:14");
}

/// <summary>:36-150 <c>struct ModelEffortButton: View</c>.</summary>
public sealed class ModelEffortButton : Decorator
{
    /// <param name="thread">:38 <c>let thread: ChatThread</c>.</param>
    /// <param name="hasHistory">:39 <c>let hasHistory: Bool</c>.</param>
    /// <param name="compact">:42 <c>var compact: Bool = false</c>.</param>
    // :59-138 body
    public ModelEffortButton(ChatThread thread, bool hasHistory, bool compact = false) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Composer/EffortSlider.swift:59");
}

/// <summary>:153-259 <c>private struct ModelEffortPanel: View</c>.</summary>
public sealed class ModelEffortPanel : Decorator
{
    /// <param name="threadID">:156 <c>let threadID: UUID</c>.</param>
    /// <param name="hasHistory">:157 <c>let hasHistory: Bool</c>.</param>
    // :161-189 body
    public ModelEffortPanel(Guid threadID, bool hasHistory) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Composer/EffortSlider.swift:161");
}

/// <summary>:261-368 <c>struct ModelList: View</c>.</summary>
public sealed class ModelList : Decorator
{
    // :271 private static let pairRowHeight: CGFloat = 46
    private const double PairRowHeight = 46;

    // :273 private static let sectionHeaderHeight: CGFloat = 22
    private const double SectionHeaderHeight = 22;

    /// <param name="thread">:263 <c>let thread: ChatThread</c>.</param>
    /// <param name="hasHistory">:264 <c>let hasHistory: Bool</c>.</param>
    /// <param name="onChoose">:265 <c>let onChoose: (ModelCatalog.Entry) -> Void</c>.</param>
    /// <param name="onBack">:268 <c>let onBack: () -> Void</c>; no default, so it moves before onEnterPair (ED-095).</param>
    /// <param name="onEnterPair">:267 <c>var onEnterPair: (HydraPair) -> Void = { _ in }</c>; null stands for the
    /// default that does nothing.</param>
    // :275-359 body
    public ModelList(ChatThread thread, bool hasHistory, Action<ModelCatalog.Entry> onChoose, Action onBack, Action<HydraPair>? onEnterPair = null) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Composer/EffortSlider.swift:275");
}

/// <summary>:373-420 <c>private struct HydraPairListRow: View</c>.</summary>
public sealed class HydraPairListRow : Decorator
{
    /// <param name="pair">:374 <c>let pair: HydraPair</c>.</param>
    /// <param name="title">:375 <c>let title: String</c>.</param>
    /// <param name="detail">:376 <c>let detail: String</c>.</param>
    /// <param name="rowHeight">:377 <c>let rowHeight: CGFloat</c>.</param>
    /// <param name="isSelected">:378 <c>let isSelected: Bool</c>.</param>
    /// <param name="isEnabled">:379 <c>let isEnabled: Bool</c>.</param>
    /// <param name="action">:380 <c>let action: () -> Void</c>.</param>
    // :384-419 body
    public HydraPairListRow(HydraPair pair, string title, string detail, double rowHeight, bool isSelected, bool isEnabled, Action action) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Composer/EffortSlider.swift:384");
}

/// <summary>:422-477 <c>private struct ModelListRow: View</c>.</summary>
public sealed class ModelListRow : Decorator
{
    /// <param name="entry">:423 <c>let entry: ModelCatalog.Entry</c>.</param>
    /// <param name="detail">:424 <c>let detail: String?</c>.</param>
    /// <param name="showsIcon">:425 <c>let showsIcon: Bool</c>.</param>
    /// <param name="rowHeight">:426 <c>let rowHeight: CGFloat</c>.</param>
    /// <param name="isSelected">:427 <c>let isSelected: Bool</c>.</param>
    /// <param name="isEnabled">:428 <c>let isEnabled: Bool</c>.</param>
    /// <param name="action">:429 <c>let action: () -> Void</c>.</param>
    // :433-476 body
    public ModelListRow(ModelCatalog.Entry entry, string? detail, bool showsIcon, double rowHeight, bool isSelected, bool isEnabled, Action action) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Composer/EffortSlider.swift:433");
}

/// <summary>:485-504 <c>struct EffortPairLook: Hashable</c>.</summary>
public sealed record EffortPairLook
{
    public string Title { get; init; }           // :487
    public EffortPairBrand Brand { get; init; }  // :488
    public string? HeadsEffort { get; init; }    // :490

    // :492-497 @MainActor init(_:registry:resolvedHeadsEffort:)
    public EffortPairLook(HydraPair pair, ProviderRegistry registry, string? resolvedHeadsEffort) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Composer/EffortSlider.swift:493");

    // :500-503 var caption: String
    public string Caption =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Composer/EffortSlider.swift:500");
}

/// <summary>:506-657 <c>struct EffortSliderCard: View</c>.</summary>
public sealed class EffortSliderCard : Decorator
{
    /// <param name="modelName">:507 <c>let modelName: String</c>.</param>
    /// <param name="provider">:508 <c>let provider: ProviderKind?</c>.</param>
    /// <param name="efforts">:511 <c>let efforts: [String]</c>; moves before pair (ED-095).</param>
    /// <param name="defaultEffort">:512 <c>let defaultEffort: String?</c>.</param>
    /// <param name="supportsFast">:513 <c>let supportsFast: Bool</c>.</param>
    /// <param name="effort">:514 <c>@Binding var effort: String?</c>.</param>
    /// <param name="fastMode">:515 <c>@Binding var fastMode: Bool</c>.</param>
    /// <param name="onReset">:517 <c>let onReset: () -> Void</c>; moves before onTitleTap (ED-095).</param>
    /// <param name="pair">:510 <c>var pair: EffortPairLook? = nil</c>.</param>
    /// <param name="onTitleTap">:516 <c>var onTitleTap: (() -> Void)?</c>.</param>
    // :557-656 body
    public EffortSliderCard(
        string modelName,
        ProviderKind? provider,
        ValueList<string> efforts,
        string? defaultEffort,
        bool supportsFast,
        Binding<string?> effort,
        Binding<bool> fastMode,
        Action onReset,
        EffortPairLook? pair = null,
        Action? onTitleTap = null) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Composer/EffortSlider.swift:557");
}

/// <summary>:659-683 <c>private struct FastModeButton: View</c>.</summary>
public sealed class FastModeButton : Decorator
{
    /// <param name="isOn">:660 <c>@Binding var isOn: Bool</c>.</param>
    // :663-682 body
    public FastModeButton(Binding<bool> isOn) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Composer/EffortSlider.swift:663");
}

/// <summary>:685-708 <c>private struct RoundIconButton: View</c>.</summary>
public sealed class RoundIconButton : Decorator
{
    /// <param name="symbol">:686 <c>let symbol: String</c>.</param>
    /// <param name="help">:687 <c>let help: String</c>.</param>
    /// <param name="action">:688 <c>let action: () -> Void</c>.</param>
    // :692-707 body
    public RoundIconButton(string symbol, string help, Action action) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Composer/EffortSlider.swift:692");
}

/// <summary>:711-837 <c>struct EffortSlider: View</c>.</summary>
public sealed class EffortSlider : Decorator
{
    // :712 static let trackHeight: CGFloat = 34
    public const double TrackHeight = 34;

    // :713 static let thumbSize: CGFloat = 42
    public const double ThumbSize = 42;

    /// <param name="count">:715 <c>let count: Int</c>.</param>
    /// <param name="index">:716 <c>@Binding var index: Int</c>.</param>
    /// <param name="accessibilityTitle">:725 <c>let accessibilityTitle: String</c>; moves before the defaulted ones
    /// (ED-095).</param>
    /// <param name="fastMode">:719 <c>var fastMode = false</c>.</param>
    /// <param name="brand">:721 <c>var brand: EffortBrand = .purple</c>.</param>
    /// <param name="pair">:724 <c>var pair: EffortPairBrand? = nil</c>.</param>
    // :740-831 body
    public EffortSlider(int count, Binding<int> index, string accessibilityTitle, bool fastMode = false, EffortBrand brand = EffortBrand.Purple, EffortPairBrand? pair = null) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Composer/EffortSlider.swift:740");
}

/// <summary>:839-847 <c>enum EffortPalette</c>.</summary>
public static class EffortPalette
{
    // :841 static var title: Color { Chrome.accent.mix(with: .white, by: 0.22) }
    public static SwiftColor Title =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Composer/EffortSlider.swift:841");

    public static SwiftColor Supercharged { get; } = new(red: 0.74, green: 0.55, blue: 1.0);      // :842
    public static SwiftColor SuperchargedFill { get; } = new(red: 0.55, green: 0.34, blue: 0.97); // :843
    public static SwiftColor Fast { get; } = new(red: 1.0, green: 0.84, blue: 0.36);              // :845
    public static SwiftColor FastFill { get; } = new(red: 0.93, green: 0.66, blue: 0.13);         // :846
}

/// <summary>:853-936 <c>enum EffortBrand: Hashable</c>, no raw values.</summary>
public enum EffortBrand
{
    Claude,      // :854
    Deepseek,    // :855
    Meta,        // :856
    Antigravity, // :857
    Silver,      // :858
    Purple,      // :860
}

/// <summary>The members of <c>EffortBrand</c> (:862-935) as a C# 14 extension block (R-NAME-4, ED-164).</summary>
public static class EffortBrandExtensions
{
    extension(EffortBrand)
    {
        // :862-871 init(provider:)
        public static EffortBrand Create(ProviderKind? provider) =>
            throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Composer/EffortSlider.swift:862");

        // :931-935 static let gemini
        public static ValueList<SwiftColor> Gemini =>
            throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Composer/EffortSlider.swift:931");
    }

    extension(EffortBrand brand)
    {
        // :874-883 var titleColor: Color
        public SwiftColor TitleColor =>
            throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Composer/EffortSlider.swift:874");

        // :911-920 var fillColor: Color
        public SwiftColor FillColor =>
            throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Composer/EffortSlider.swift:911");

        // :923 var spark: Color
        public SwiftColor Spark =>
            throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Composer/EffortSlider.swift:923");

        // :926 var isLight: Bool
        public bool IsLight =>
            throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Composer/EffortSlider.swift:926");

        // :928 var glow: Color
        public SwiftColor Glow =>
            throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Composer/EffortSlider.swift:928");
    }
}

/// <summary>:942-965 <c>struct EffortPairBrand: Hashable</c>.</summary>
public sealed record EffortPairBrand
{
    public ValueList<SwiftColor> Colors { get; init; } // :944
    public bool IsLight { get; init; }                 // :946

    // :948-961 init(_ pair: HydraPair)
    public EffortPairBrand(HydraPair pair) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Composer/EffortSlider.swift:948");

    // :964 var titleColors: [Color]
    public ValueList<SwiftColor> TitleColors =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Composer/EffortSlider.swift:964");
}

/// <summary>:968-1027 <c>struct TrackLook: Hashable</c>.</summary>
/// <param name="TrackLookKind">:979 <c>var kind: Kind</c> (R-NAME-3, Swift: kind).</param>
/// <param name="Brand">:980 <c>var brand: EffortBrand</c>.</param>
/// <param name="Pair">:983 <c>var pair: EffortPairBrand? = nil</c>.</param>
public sealed record TrackLook(TrackLook.Kind TrackLookKind, EffortBrand Brand, EffortPairBrand? Pair = null)
{
    /// <summary>:969-977 <c>enum Kind: Hashable</c>.</summary>
    public enum Kind
    {
        Plain,        // :970
        Supercharged, // :972
        Fast,         // :974
        Fusion,       // :976
    }

    // :1000-1008 var glow: Color
    public SwiftColor Glow =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Composer/EffortSlider.swift:1000");

    // :1012 var isLightFill: Bool
    public bool IsLightFill =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Composer/EffortSlider.swift:1012");

    // :1015 var stopColor: Color
    public SwiftColor StopColor =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Composer/EffortSlider.swift:1015");

    // :1018 var spark: Color
    public SwiftColor Spark =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Composer/EffortSlider.swift:1018");

    // :1024-1026 var effectIdentity: AnyHashable
    public object EffectIdentity =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Composer/EffortSlider.swift:1024");
}

/// <summary>:1039-1347 <c>struct TrackEffect: View</c>.</summary>
public sealed class TrackEffect : Decorator
{
    /// <param name="look">:1040 <c>let look: TrackLook</c>.</param>
    // :1045-1072 body
    public TrackEffect(TrackLook look) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Composer/EffortSlider.swift:1045");

    /// <summary>:1074-1082 <c>struct Particle: Equatable</c>.</summary>
    public sealed record Particle
    {
        public double Height { get; init; }      // :1075
        public double Speed { get; init; }       // :1076
        public double Radius { get; init; }      // :1077
        public double Start { get; init; }       // :1079
        public double Brightness { get; init; }  // :1080
        public double ShimmerRate { get; init; } // :1081
    }

    /// <summary>:1084-1091 <c>struct Streak: Equatable</c>.</summary>
    public sealed record Streak
    {
        public double Length { get; init; }     // :1085
        public double Speed { get; init; }      // :1086
        public double Thickness { get; init; }  // :1087
        public double Start { get; init; }      // :1088
        public double Height { get; init; }     // :1089
        public double Brightness { get; init; } // :1090
    }

    /// <summary>:1093-1116 <c>struct Traits: Equatable</c>.</summary>
    public sealed record Traits(ValueList<Particle> Particles, ValueList<Streak> Streaks) // :1094-1095
    {
        // :1099-1115 static func build(width:) -> Traits
        public static Traits Build(double width) =>
            throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Composer/EffortSlider.swift:1099");
    }

    /// <summary>:1121-1132 <c>final class TraitCache</c>.</summary>
    public sealed class TraitCache
    {
        // :1125-1131 func traits(width:) -> Traits
        public Traits Traits(double width) =>
            throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Composer/EffortSlider.swift:1125");
    }

    // :1134-1148 private static func drawParticles(_:size:time:color:particles:)
    private static void DrawParticles(DrawingContext context, Size size, double time, SwiftColor color, IReadOnlyList<Particle> particles) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Composer/EffortSlider.swift:1134");

    // :1151-1171 private static func drawStreaks(_:size:time:streaks:)
    private static void DrawStreaks(DrawingContext context, Size size, double time, IReadOnlyList<Streak> streaks) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Composer/EffortSlider.swift:1151");

    // :1175-1201 private static func drawBolts(_:size:time:)
    private static void DrawBolts(DrawingContext context, Size size, double time) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Composer/EffortSlider.swift:1175");

    // :1204-1225 private static func drawSheen(_:size:time:color:)
    private static void DrawSheen(DrawingContext context, Size size, double time, SwiftColor color) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Composer/EffortSlider.swift:1204");

    // :1232-1335 private static func drawFusion(_:size:time:lead:heads:)
    private static void DrawFusion(DrawingContext context, Size size, double time, SwiftColor lead, IReadOnlyList<SwiftColor> heads) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Composer/EffortSlider.swift:1232");

    // :1338-1340 private static func edgeFade(_:width:)
    private static double EdgeFade(double x, double width) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Composer/EffortSlider.swift:1338");

    // :1343-1346 nonisolated static func random(_:_:)
    public static double Random(double index, double trait) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Composer/EffortSlider.swift:1343");
}
