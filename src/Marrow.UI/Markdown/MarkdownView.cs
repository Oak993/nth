using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.UI.Port;

// Source: DroppyCode/UI/Markdown/MarkdownView.swift (B2 1fbf81e), :1-1498.
// Skeleton (c100 group 5): every type the file declares, the views' inputs, the constants and data tables, the
// environment values and the members the other cycle files use. Every member with behaviour is left to the
// implementing unit (MARROW_STUB).
// Forms (ED-095): a SwiftUI View is a Decorator whose constructor builds `body`; a ViewModifier is a sealed record with
// `Control Body(Control content)`; a caseless enum is a static class; an enum with associated values is an abstract
// record (R-ENUM-2), an unlabelled payload named Value; Font is SwiftFont, Color SwiftColor, Text StyledText,
// AnyTransition SwiftTransition, NSImage and SwiftUI's Image IImage (ED-158), URL SwiftURL, Data byte[], a Task
// stored or awaited Task/TaskHandle. A Swift Int that counts characters, lines or blocks stays int, as MarkdownParser's
// do; a Range<String.Index> is a (int LowerBound, int UpperBound) pair of offsets into the string. The
// `extension EnvironmentValues` blocks (:546-555, :866-887) cannot be a static class named after the file
// (R-NAME-4): that name is the view's, so their attached properties live on MarkdownView (ThreadTimeline precedent);
// the private EnvironmentKey structs (:542-544, :840-864) hold the defaults.
// Layering (IMPLEMENTING 2.1, HANDOFF section 9): the `hydra` fenced block renders HydraDelegationBlock
// (App/Views/Chat/TimelineRows.swift:648), an App view Marrow.UI cannot name, so MarkdownBlockView reaches it through
// the hook MarkdownBlockView.HydraDelegationBlock that the App layer sets at start-up.
// Not declared yet (read only by the bodies that the implementing unit writes): the views' @State/@Environment
// storage, the private static caches (blockCache :107, streamingParses :111, glyphCache :406, metricsBySize :1212),
// FaviconCache's private tables (:903, :911-912) and RichLink's bareDomainPattern (:753-755).
// The views' custom `==` (MarkdownView :21-23, MarkdownBlockView :295-298) stand for SwiftUI's `.equatable()`; a
// Control keeps reference equality, so they are not declared as Equals (NEEDS DECISION, as group 3 reports).
// NEEDS DECISION (group 5 report): Foundation's AttributedString has no port type (ED-092 (3)), so parseWarm (:237-255),
// RichLink's prettyCache/streamingPretty (:563-568), prettyAttributed (:570-588), warm(_:with:) (:591-594), build
// (:596-621), autolinkedBareDomains (:772-826) and baseAttributed (:828-834), InlineText.attributed (:1239-1242) and
// TableBlock's cellCache/cell (:1347-1355) are not declared yet.

namespace Marrow.UI.Markdown;

/// <summary>:4-7 <c>enum MarkdownSegment: Equatable</c> (R-ENUM-2).</summary>
public abstract record MarkdownSegment
{
    private MarkdownSegment() { }

    public sealed record Prose(ValueList<MarkdownBlock> Value) : MarkdownSegment; // :5

    public sealed record Block(MarkdownBlock Value) : MarkdownSegment; // :6
}

/// <summary>:9-276 <c>struct MarkdownView: View, Equatable</c>.</summary>
public sealed class MarkdownView : Decorator
{
    public const int WarmCandidateLimit = 96; // :11

    private const int StreamingSlots = 48; // :115

    /// <param name="text">:10 <c>let text: String</c>.</param>
    /// <param name="isStreaming">:16 <c>var isStreaming = false</c>.</param>
    // :63-103 body
    public MarkdownView(string text, bool isStreaming = false) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Markdown/MarkdownView.swift:63");

    // :25-45 static func segments(_ blocks: [MarkdownBlock]) -> [MarkdownSegment]
    public static ValueList<MarkdownSegment> Segments(IReadOnlyList<MarkdownBlock> blocks) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Markdown/MarkdownView.swift:25");

    // :51-56 private static func transition(for block: MarkdownBlock) -> AnyTransition
    private static SwiftTransition Transition(MarkdownBlock @for) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Markdown/MarkdownView.swift:51");

    // :59-61 static func isDelegationInfo(_ info: String) -> Bool
    public static bool IsDelegationInfo(string info) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Markdown/MarkdownView.swift:59");

    // :123-133 static func streamingParse(for text: String) -> (blocks: [MarkdownBlock], stable: Int)
    public static (ValueList<MarkdownBlock> Blocks, int Stable) StreamingParse(string @for) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Markdown/MarkdownView.swift:123");

    // :135-148 static func blocks(for text: String, streaming: Bool = false) -> [MarkdownBlock]
    public static ValueList<MarkdownBlock> Blocks(string @for, bool streaming = false) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Markdown/MarkdownView.swift:135");

    // :154-167 private static func extends(_ earlier: String, to text: String) -> Bool
    private static bool Extends(string earlier, string to) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Markdown/MarkdownView.swift:154");

    // :170-176 private static func fenceMarker(in trimmed: String) -> String?
    private static string? FenceMarker(string @in) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Markdown/MarkdownView.swift:170");

    // :180-182 private static func approximateSourceLength(_ blocks: ArraySlice<MarkdownBlock>) -> Int
    private static int ApproximateSourceLength(IReadOnlyList<MarkdownBlock> blocks) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Markdown/MarkdownView.swift:180");

    // :184-199 private static func approximateSourceLength(_ block: MarkdownBlock) -> Int
    private static int ApproximateSourceLength(MarkdownBlock block) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Markdown/MarkdownView.swift:184");

    // :204-219 @MainActor static func warm(_ texts: some Sequence<String>) async throws
    public static Task Warm(IEnumerable<string> texts) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Markdown/MarkdownView.swift:204");

    // :221-235 @MainActor static func warmTexts(_ texts: some Sequence<String>) -> [String]
    public static ValueList<string> WarmTexts(IEnumerable<string> texts) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Markdown/MarkdownView.swift:221");

    // :257-275 nonisolated private static func inlineSources(of blocks: [MarkdownBlock], into sources: inout Set<String>)
    // throws; the inout set comes back changed (R-VAL-1).
    private static ValueSet<string> InlineSources(IReadOnlyList<MarkdownBlock> of, ValueSet<string> into) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Markdown/MarkdownView.swift:257");

    // :547 @Entry var hydraMentionPersonas: [HydraPersona] = [], an inherited environment value (R-VIEW-6).
    public static readonly AttachedProperty<ValueList<HydraPersona>> HydraMentionPersonasProperty =
        AvaloniaProperty.RegisterAttached<Control, ValueList<HydraPersona>>(
            "HydraMentionPersonas", typeof(MarkdownView), ValueList<HydraPersona>.Empty, inherits: true);

    public static ValueList<HydraPersona> GetHydraMentionPersonas(Control control) => control.GetValue(HydraMentionPersonasProperty);

    public static void SetHydraMentionPersonas(Control control, ValueList<HydraPersona> value) =>
        control.SetValue(HydraMentionPersonasProperty, value);

    // :549 @Entry var hydraMentionTargets: [String: HydraMentionTarget] = [:]
    public static readonly AttachedProperty<ValueMap<string, HydraMentionTarget>> HydraMentionTargetsProperty =
        AvaloniaProperty.RegisterAttached<Control, ValueMap<string, HydraMentionTarget>>(
            "HydraMentionTargets", typeof(MarkdownView), ValueMap<string, HydraMentionTarget>.Empty, inherits: true);

    public static ValueMap<string, HydraMentionTarget> GetHydraMentionTargets(Control control) => control.GetValue(HydraMentionTargetsProperty);

    public static void SetHydraMentionTargets(Control control, ValueMap<string, HydraMentionTarget> value) =>
        control.SetValue(HydraMentionTargetsProperty, value);

    // :551-554 var markdownListDepth: Int
    public static readonly AttachedProperty<int> MarkdownListDepthProperty =
        AvaloniaProperty.RegisterAttached<Control, int>(
            "MarkdownListDepth", typeof(MarkdownView), MarkdownListDepthKey.DefaultValue, inherits: true);

    public static int GetMarkdownListDepth(Control control) => control.GetValue(MarkdownListDepthProperty);

    public static void SetMarkdownListDepth(Control control, int value) => control.SetValue(MarkdownListDepthProperty, value);

    // :867-870 var markdownPointSize: CGFloat
    public static readonly AttachedProperty<double> MarkdownPointSizeProperty =
        AvaloniaProperty.RegisterAttached<Control, double>(
            "MarkdownPointSize", typeof(MarkdownView), MarkdownPointSizeKey.DefaultValue, inherits: true);

    public static double GetMarkdownPointSize(Control control) => control.GetValue(MarkdownPointSizeProperty);

    public static void SetMarkdownPointSize(Control control, double value) => control.SetValue(MarkdownPointSizeProperty, value);

    // :871-874 var markdownDimmed: Bool
    public static readonly AttachedProperty<bool> MarkdownDimmedProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>(
            "MarkdownDimmed", typeof(MarkdownView), MarkdownDimmedKey.DefaultValue, inherits: true);

    public static bool GetMarkdownDimmed(Control control) => control.GetValue(MarkdownDimmedProperty);

    public static void SetMarkdownDimmed(Control control, bool value) => control.SetValue(MarkdownDimmedProperty, value);

    // :875-878 var markdownStreaming: Bool
    public static readonly AttachedProperty<bool> MarkdownStreamingProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>(
            "MarkdownStreaming", typeof(MarkdownView), MarkdownStreamingKey.DefaultValue, inherits: true);

    public static bool GetMarkdownStreaming(Control control) => control.GetValue(MarkdownStreamingProperty);

    public static void SetMarkdownStreaming(Control control, bool value) => control.SetValue(MarkdownStreamingProperty, value);

    // :879-882 var markdownBlockSelection: Bool
    public static readonly AttachedProperty<bool> MarkdownBlockSelectionProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>(
            "MarkdownBlockSelection", typeof(MarkdownView), MarkdownBlockSelectionKey.DefaultValue, inherits: true);

    public static bool GetMarkdownBlockSelection(Control control) => control.GetValue(MarkdownBlockSelectionProperty);

    public static void SetMarkdownBlockSelection(Control control, bool value) => control.SetValue(MarkdownBlockSelectionProperty, value);

    // :883-886 var markdownProseRuns: Bool
    public static readonly AttachedProperty<bool> MarkdownProseRunsProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>(
            "MarkdownProseRuns", typeof(MarkdownView), MarkdownProseRunsKey.DefaultValue, inherits: true);

    public static bool GetMarkdownProseRuns(Control control) => control.GetValue(MarkdownProseRunsProperty);

    public static void SetMarkdownProseRuns(Control control, bool value) => control.SetValue(MarkdownProseRunsProperty, value);
}

/// <summary>:278-539 <c>struct MarkdownBlockView: View, Equatable</c>.</summary>
public sealed class MarkdownBlockView : Decorator
{
    /// <summary>The App hook for a delegation block (layering, see the header): builds
    /// <c>HydraDelegationBlock(json:sent:)</c> (:330).</summary>
    public delegate Control HydraDelegationBlockFactory(string json, bool sent);

    /// <summary>Set once by the App layer at start-up (HANDOFF section 9); MarkdownBlockView calls it where the Swift
    /// names HydraDelegationBlock (:330).</summary>
    public static HydraDelegationBlockFactory? HydraDelegationBlock { get; set; }

    /// <param name="block">:279 <c>let block: MarkdownBlock</c>.</param>
    /// <param name="settled">:284 <c>var settled = false</c>.</param>
    // :300-374 body
    public MarkdownBlockView(MarkdownBlock block, bool settled = false) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Markdown/MarkdownView.swift:300");

    // :376-383 private func headingFont(_ level: Int) -> Font
    private static SwiftFont HeadingFont(int level) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Markdown/MarkdownView.swift:376");

    // :386 private static func mentionGlyphOffset(size: CGFloat) -> CGFloat
    private static double MentionGlyphOffset(double size) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Markdown/MarkdownView.swift:386");

    // :390-404 @MainActor static func mentionGlyph(_ persona: HydraPersona, size: CGFloat) -> Image; a template image
    // for a Text run (ED-158).
    public static IImage MentionGlyph(HydraPersona persona, double size) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Markdown/MarkdownView.swift:390");

    // :411-414 private func mentionsTarget(in text: String) -> Bool (reads the environment's targets)
    private bool MentionsTarget(string @in) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Markdown/MarkdownView.swift:411");

    // :418-441 nonisolated static func mentionRanges(in text: String, names: [String]) -> [Range<String.Index>]
    public static ValueList<(int LowerBound, int UpperBound)> MentionRanges(string @in, IReadOnlyList<string> names) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Markdown/MarkdownView.swift:418");

    // :442-487 private func mentionSegments(in text: String) -> [VeiledText.Segment]?
    private ValueList<VeiledText.Segment>? MentionSegments(string @in) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Markdown/MarkdownView.swift:442");

    // :489-504 private func mentionText(_ persona: HydraPersona) -> Text
    private StyledText MentionText(HydraPersona persona) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Markdown/MarkdownView.swift:489");

    // :506-534 @ViewBuilder private func marker(ordered: Bool, number: Int, checked: Bool?) -> some View
    private Control Marker(bool ordered, int number, bool? @checked) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Markdown/MarkdownView.swift:506");

    // :536-538 private var markerColor: Color
    private SwiftColor MarkerColor =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Markdown/MarkdownView.swift:536");
}

/// <summary>:542-544 <c>private struct MarkdownListDepthKey: EnvironmentKey</c>.</summary>
file static class MarkdownListDepthKey
{
    public const int DefaultValue = 0; // :543
}

/// <summary>:562-835 <c>enum RichLink</c>: pretty link titles plus favicons for chat markdown.</summary>
public static class RichLink
{
    private const int StreamingSlots = 4; // :568

    // :623-634 @MainActor static func linkHosts(for source: String, streaming: Bool = false) -> [String]
    public static ValueList<string> LinkHosts(string @for, bool streaming = false) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Markdown/MarkdownView.swift:623");

    // :637-647 @MainActor static func videoIDs(for source: String, streaming: Bool = false) -> [String]
    public static ValueList<string> VideoIDs(string @for, bool streaming = false) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Markdown/MarkdownView.swift:637");

    // :651-657 @MainActor static func linkAssetKey(for source: String, streaming: Bool = false) -> String
    public static string LinkAssetKey(string @for, bool streaming = false) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Markdown/MarkdownView.swift:651");

    // :660-664 @MainActor static func invalidate(_ source: String)
    public static void Invalidate(string source) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Markdown/MarkdownView.swift:660");

    // :670-689 static func prettyTitle(for url: URL) -> String
    public static string PrettyTitle(SwiftURL @for) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Markdown/MarkdownView.swift:670");

    // :692-694 private static let namelessSegments: Set<String>
    private static ValueSet<string> NamelessSegments { get; } = new(
    [
        "watch", "view", "index", "index.html", "index.php", "home", "default", "page",
    ], SwiftStringComparer.Instance); // R-VAL-2: matched against URL text

    // :697-700 private static func capped(_ title: String, at limit: Int) -> String
    private static string Capped(string title, int at) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Markdown/MarkdownView.swift:697");

    // :705-723 static func forgeReference(for url: URL) -> String?
    public static string? ForgeReference(SwiftURL @for) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Markdown/MarkdownView.swift:705");

    // :727-730 static func compactFallback(_ absolute: String) -> String
    public static string CompactFallback(string absolute) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Markdown/MarkdownView.swift:727");

    // :732-735 @MainActor static func containsLinks(in source: String, streaming: Bool = false) -> Bool
    public static bool ContainsLinks(string @in, bool streaming = false) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Markdown/MarkdownView.swift:732");

    // :737-748 static func isBareDisplay(_ display: String, url: URL) -> Bool
    public static bool IsBareDisplay(string display, SwiftURL url) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Markdown/MarkdownView.swift:737");

    // :759-768 private static let bareDomainTLDs: Set<String>
    private static ValueSet<string> BareDomainTLDs { get; } = new(
    [
        "com", "org", "net", "edu", "gov", "mil", "info", "biz", "io", "ai", "app", "dev", "co", "me",
        "xyz", "site", "online", "store", "shop", "tech", "cloud", "tools", "blog", "fm", "gg", "so", "tv",
        "live", "news", "studio", "design", "social", "works", "codes", "run", "page", "link", "chat", "bot",
        "art", "fun", "space", "world", "email", "group", "team", "digital", "agency", "solutions", "software",
        "systems", "network", "media", "photo", "video", "games", "zone", "city", "life", "one", "plus", "pro",
        "nl", "de", "be", "uk", "fr", "es", "it", "se", "no", "dk", "fi", "is", "ch", "at", "pl", "ie", "eu",
        "ca", "us", "au", "nz", "jp", "cn", "in", "br", "mx", "za", "pt", "gr", "cz", "ro", "hu", "sk", "kr",
        "sg", "hk", "tw", "th", "my", "id", "ph", "vn", "tr", "il", "ae", "sa", "cl", "ar", "pe",
    ], SwiftStringComparer.Instance); // R-VAL-2: matched against typed text
}

/// <summary>:840-842 <c>private struct MarkdownPointSizeKey: EnvironmentKey</c>.</summary>
file static class MarkdownPointSizeKey
{
    public const double DefaultValue = 13; // :841
}

/// <summary>:846-848 <c>private struct MarkdownDimmedKey: EnvironmentKey</c>.</summary>
file static class MarkdownDimmedKey
{
    public const bool DefaultValue = false; // :847
}

/// <summary>:851-853 <c>private struct MarkdownStreamingKey: EnvironmentKey</c>.</summary>
file static class MarkdownStreamingKey
{
    public const bool DefaultValue = false; // :852
}

/// <summary>:858-860 <c>private struct MarkdownBlockSelectionKey: EnvironmentKey</c>.</summary>
file static class MarkdownBlockSelectionKey
{
    public const bool DefaultValue = true; // :859
}

/// <summary>:862-864 <c>private struct MarkdownProseRunsKey: EnvironmentKey</c>.</summary>
file static class MarkdownProseRunsKey
{
    public const bool DefaultValue = true; // :863
}

/// <summary>:890-899 <c>struct MarkdownBlockSelection: ViewModifier</c>: <c>.textSelection(.enabled)</c> only while
/// the environment allows blocks their own selection.</summary>
public sealed record MarkdownBlockSelection
{
    // :892-898 func body(content: Content) -> some View
    public Control Body(Control content) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Markdown/MarkdownView.swift:892");
}

/// <summary>:901-1099 <c>@MainActor enum FaviconCache</c>.</summary>
public static class FaviconCache
{
    // :905-907 static func cached(host: String) -> NSImage?
    public static IImage? Cached(string host) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Markdown/MarkdownView.swift:905");

    // :914-939 static func image(for host: String) async -> NSImage?
    public static Task<IImage?> Image(string @for) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Markdown/MarkdownView.swift:914");

    // :945-953 private static func iconData(for host: String) async -> Data?
    private static Task<byte[]?> IconData(string @for) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Markdown/MarkdownView.swift:945");

    // :955-975 private static func cleaned(_ data: Data) -> NSImage?
    private static IImage? Cleaned(byte[] data) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Markdown/MarkdownView.swift:955");

    // :1051-1054 private static let privateSuffixes
    private static ValueList<string> PrivateSuffixes { get; } = new(
    [
        ".local", ".localhost", ".localdomain", ".internal", ".intranet",
        ".lan", ".home", ".home.arpa", ".test", ".invalid", ".corp",
    ]);

    // :1059-1082 static func isPublic(host: String) -> Bool
    public static bool IsPublic(string host) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Markdown/MarkdownView.swift:1059");

    // :1084-1098 private static func resizedIcon(_ image: NSImage) -> NSImage
    private static IImage ResizedIcon(IImage image) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Markdown/MarkdownView.swift:1084");
}

/// <summary>:1101-1243 <c>struct InlineText: View</c>: one paragraph of inline markdown, as a link text view when it
/// has links and as veiled Text otherwise.</summary>
public sealed class InlineText : Decorator
{
    /// <param name="source">:1102 <c>let source: String</c>.</param>
    /// <param name="hasLinks">:1104 <c>let hasLinks: Bool?</c>.</param>
    /// <param name="hugsContent">:1106 <c>var hugsContent = false</c>.</param>
    // :1121-1125 init(_:hasLinks:hugsContent:); :1127-1208 body
    public InlineText(string source, bool? hasLinks = null, bool hugsContent = false) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Markdown/MarkdownView.swift:1127");

    // :1214-1221 @MainActor private static func metrics(pointSize: CGFloat) -> (ascender: CGFloat, lineHeight: CGFloat)
    private static (double Ascender, double LineHeight) Metrics(double pointSize) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Markdown/MarkdownView.swift:1214");

    // :1226-1237 static func fetchLinkAssets(source: String, streaming: Bool, revision: Binding<Int>) async
    public static Task FetchLinkAssets(string source, bool streaming, Binding<int> revision) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Markdown/MarkdownView.swift:1226");
}

/// <summary>:1249-1253 <c>@MainActor @Observable private final class BlockHover</c> (R-OBS-6).</summary>
internal sealed class BlockHover : MainActorModel
{
    private bool isHovering; // :1252

    // :1252 var isHovering = false
    public bool IsHovering
    {
        get => Read(isHovering);
        set => Set(ref isHovering, value);
    }
}

/// <summary>:1257-1265 <c>private struct CodeBlockCopyButton: View</c>.</summary>
internal sealed class CodeBlockCopyButton : Decorator
{
    /// <param name="text">:1258 <c>let text: String</c>.</param>
    /// <param name="hover">:1259 <c>let hover: BlockHover</c>.</param>
    // :1261-1264 body
    public CodeBlockCopyButton(string text, BlockHover hover) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Markdown/MarkdownView.swift:1261");
}

/// <summary>:1267-1336 <c>struct CodeBlock: View</c>.</summary>
public sealed class CodeBlock : Decorator
{
    private const int CollapsedLineLimit = 120; // :1276

    /// <param name="language">:1268 <c>let language: String?</c>.</param>
    /// <param name="code">:1269 <c>let code: String</c>.</param>
    // :1278-1321 body
    public CodeBlock(string? language, string code) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Markdown/MarkdownView.swift:1278");

    // :1324-1335 private static func head(of code: String, lines: Int) -> String
    private static string Head(string of, int lines) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Markdown/MarkdownView.swift:1324");
}

/// <summary>:1338-1397 <c>struct TableBlock: View</c>.</summary>
public sealed class TableBlock : Decorator
{
    private const int CollapsedRowLimit = 30; // :1344

    /// <param name="header">:1339 <c>let header: [String]</c>.</param>
    /// <param name="rows">:1340 <c>let rows: [[String]]</c>.</param>
    // :1357-1396 body
    public TableBlock(IReadOnlyList<string> header, IReadOnlyList<IReadOnlyList<string>> rows) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Markdown/MarkdownView.swift:1357");
}

/// <summary>:1401-1469 <c>enum ProseReflow</c>: unwraps prose the model hard-wrapped before it reaches the
/// pasteboard.</summary>
public static class ProseReflow
{
    // :1402-1468 static func reflowed(_ text: String) -> String
    public static string Reflowed(string text) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Markdown/MarkdownView.swift:1402");
}

/// <summary>:1471-1498 <c>struct CopyButton: View</c>.</summary>
public sealed class CopyButton : Decorator
{
    /// <param name="text">:1472 <c>let text: String</c>.</param>
    /// <param name="label">:1473 <c>var label = "Copy"</c>.</param>
    // :1477-1497 body
    public CopyButton(string text, string label = "Copy") =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Markdown/MarkdownView.swift:1477");
}
