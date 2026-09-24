using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Controls.Shapes;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.UI.Chrome;
using Marrow.UI.Common;
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
    // :63-103 body. The prose-run/streaming split (PR-THEME-105) and the two block ForEach arms wait on
    // MarkdownBlockView's own body (part 2's InlineText/VeiledText dependency), so the view's constructor is
    // left to that unit; the pure functions below (Segments, Transition, IsDelegationInfo, the caches) are this
    // part's PR-THEME-105/106/123/124/125 records and are implemented so part 2's constructor can call them.
    public MarkdownView(string text, bool isStreaming = false) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Markdown/MarkdownView.swift:63");

    // :25-45 static func segments(_ blocks: [MarkdownBlock]) -> [MarkdownSegment]. PR-THEME-105: headings,
    // paragraphs, lists and quotes accumulate into a prose run; code, tables and rules close the run and stand
    // alone.
    public static ValueList<MarkdownSegment> Segments(IReadOnlyList<MarkdownBlock> blocks)
    {
        var outSegments = new List<MarkdownSegment>();
        var run = new List<MarkdownBlock>();
        void Close()
        {
            if (run.Count > 0)
            {
                outSegments.Add(new MarkdownSegment.Prose(new ValueList<MarkdownBlock>([.. run])));
                run = [];
            }
        }
        foreach (var block in blocks)
        {
            switch (block)
            {
                case MarkdownBlock.Heading or MarkdownBlock.Paragraph or MarkdownBlock.List or MarkdownBlock.Quote:
                    run.Add(block);
                    break;
                case MarkdownBlock.Code or MarkdownBlock.Table or MarkdownBlock.Rule:
                    Close();
                    outSegments.Add(new MarkdownSegment.Block(block));
                    break;
                default:
                    throw new UnreachableException();
            }
        }
        Close();
        return new ValueList<MarkdownSegment>([.. outSegments]);
    }

    // :51-56 private static func transition(for block: MarkdownBlock) -> AnyTransition. PR-THEME-107: every
    // block gets softAppear, except a delegation code block, which leaves with .identity (no transition) so its
    // already-faded card is never seen to ghost.
    private static SwiftTransition Transition(MarkdownBlock @for)
    {
        if (@for is MarkdownBlock.Code { Language: { } language } && IsDelegationInfo(SwiftString.Lowercased(language)))
        {
            return SwiftTransition.Asymmetric(insertion: SwiftTransition.SoftAppear, removal: SwiftTransition.Identity);
        }
        return SwiftTransition.SoftAppear;
    }

    // :59-61 static func isDelegationInfo(_ info: String) -> Bool. PR-THEME-107/108.
    public static bool IsDelegationInfo(string info) => info is "hydra" or "hydra-sent" or "hydra-leaving";

    // :107 @MainActor private static var blockCache = RecentCache<String, [MarkdownBlock]>(limit: 800). Plain
    // static field (R-ISO-5): confined to the main actor by convention, as every other MainActor static in the
    // tree.
    private static RecentCache<string, ValueList<MarkdownBlock>> blockCache = new(800); // PR-THEME-123

    // :111 @MainActor private static var streamingParses: [(text: String, parse: MarkdownParser.Parse)] = [].
    private static readonly List<(string Text, MarkdownParser.Parse Parse)> streamingParses = []; // PR-THEME-123

    // :123-133 static func streamingParse(for text: String) -> (blocks: [MarkdownBlock], stable: Int).
    // PR-THEME-123/125.
    public static (ValueList<MarkdownBlock> Blocks, int Stable) StreamingParse(string @for)
    {
        var text = @for;
        for (var i = streamingParses.Count - 1; i >= 0; i--)
        {
            var candidate = streamingParses[i];
            if (SwiftString.Utf8Count(candidate.Text) == SwiftString.Utf8Count(text) && candidate.Text == text)
            {
                return (candidate.Parse.Blocks, candidate.Parse.Stable);
            }
        }
        var slot = -1;
        for (var i = streamingParses.Count - 1; i >= 0; i--)
        {
            if (Extends(streamingParses[i].Text, text)) { slot = i; break; }
        }
        var parsed = MarkdownParser.MarkdownParserParse(text, slot >= 0 ? (streamingParses[slot].Text, streamingParses[slot].Parse) : null);
        if (slot >= 0) streamingParses.RemoveAt(slot);
        else if (streamingParses.Count == StreamingSlots) streamingParses.RemoveAt(0);
        streamingParses.Add((text, parsed));
        return (parsed.Blocks, parsed.Stable);
    }

    // :135-148 static func blocks(for text: String, streaming: Bool = false) -> [MarkdownBlock]. PR-THEME-123.
    public static ValueList<MarkdownBlock> Blocks(string @for, bool streaming = false)
    {
        var text = @for;
        if (streaming) return StreamingParse(text).Blocks;
        if (blockCache.TryGetValue(text, out var cached)) return cached;
        ValueList<MarkdownBlock> parsed;
        var slot = -1;
        for (var i = streamingParses.Count - 1; i >= 0; i--)
        {
            if (streamingParses[i].Text == text) { slot = i; break; }
        }
        if (slot >= 0)
        {
            parsed = streamingParses[slot].Parse.Blocks;
            streamingParses.RemoveAt(slot);
        }
        else
        {
            parsed = MarkdownParser.MarkdownParserParse(text);
        }
        blockCache.Insert(parsed, text);
        return parsed;
    }

    // :154-167 private static func extends(_ earlier: String, to text: String) -> Bool. PR-THEME-125: byte-probe
    // extension test, not a whole-text comparison.
    private static bool Extends(string earlier, string to)
    {
        var text = to;
        var earlierCount = SwiftString.Utf8Count(earlier);
        var count = SwiftString.Utf8Count(text);
        if (earlierCount <= 0 || earlierCount >= count) return false;
        const int probe = 64;
        var earlierBytes = Encoding.UTF8.GetBytes(earlier);
        var bytes = Encoding.UTF8.GetBytes(text);
        if (earlierCount <= probe * 2)
        {
            return bytes.Length >= earlierBytes.Length && bytes.AsSpan(0, earlierBytes.Length).SequenceEqual(earlierBytes);
        }
        if (!bytes.AsSpan(0, probe).SequenceEqual(earlierBytes.AsSpan(0, probe))) return false;
        var earlierTail = earlierBytes.AsSpan(earlierBytes.Length - probe, probe);
        var tailStart = earlierCount - probe;
        return bytes.AsSpan(tailStart, probe).SequenceEqual(earlierTail);
    }

    // :170-176 private static func fenceMarker(in trimmed: String) -> String?. Not cited by any record in this
    // part (no call site reaches it within :1-1499 either); ported anyway since it is trivial and fully
    // specified, rather than left as a permanent stub with no owner.
    private static string? FenceMarker(string @in)
    {
        var trimmed = @in;
        foreach (var marker in new[] { "```", "~~~" })
        {
            if (SwiftString.HasPrefix(trimmed, marker))
            {
                var first = marker[0];
                var count = 0;
                foreach (var ch in trimmed)
                {
                    if (ch != first) break;
                    count++;
                }
                return new string(first, count);
            }
        }
        return null;
    }

    // :180-182 private static func approximateSourceLength(_ blocks: ArraySlice<MarkdownBlock>) -> Int. Not
    // cited by any record in this part; ported for the same reason as FenceMarker above.
    private static int ApproximateSourceLength(IReadOnlyList<MarkdownBlock> blocks)
    {
        var total = 0;
        foreach (var block in blocks) total += ApproximateSourceLength(block) + 4;
        return total;
    }

    // :184-199 private static func approximateSourceLength(_ block: MarkdownBlock) -> Int.
    private static int ApproximateSourceLength(MarkdownBlock block) => block switch
    {
        MarkdownBlock.Paragraph p => SwiftString.Count(p.Text) + 2,
        MarkdownBlock.Heading h => SwiftString.Count(h.Text) + 2,
        MarkdownBlock.Code c => SwiftString.Count(c.CodeText) + 8,
        MarkdownBlock.Quote q => ApproximateSourceLength(q.Blocks) + 2,
        MarkdownBlock.Table t => SwiftString.Count(string.Join("|", t.Header)) + SumTableRows(t.Rows) + 8,
        MarkdownBlock.List l => SumListItems(l.Items),
        MarkdownBlock.Rule => 4,
        _ => throw new UnreachableException(),
    };

    private static int SumTableRows(ValueList<ValueList<string>> rows)
    {
        var total = 0;
        foreach (var row in rows) total += SwiftString.Count(string.Join("|", row)) + 2;
        return total;
    }

    private static int SumListItems(ValueList<MarkdownListItem> items)
    {
        var total = 0;
        foreach (var item in items) total += SwiftString.Count(item.Text) + ApproximateSourceLength(item.Children) + 4;
        return total;
    }

    // :204-219 @MainActor static func warm(_ texts: some Sequence<String>) async throws. PR-THEME-124.
    public static async Task Warm(IEnumerable<string> texts)
    {
        SwiftTask.CheckCancellation();
        var missing = WarmTexts(texts);
        if (missing.Count == 0) return;
        var parsed = await ParseWarm(missing);
        SwiftTask.CheckCancellation();
        foreach (var (text, blocks, inline) in parsed)
        {
            SwiftTask.CheckCancellation();
            if (!blockCache.Contains(text)) blockCache.Insert(blocks, text);
            foreach (var (source, pretty) in inline)
            {
                SwiftTask.CheckCancellation();
                RichLink.Warm(source, pretty);
            }
        }
    }

    // :221-235 @MainActor static func warmTexts(_ texts: some Sequence<String>) -> [String]. PR-THEME-124: the
    // four hard limits (96 candidates, 262144 bytes per text, 1048576 byte budget, 24 warmed texts).
    public static ValueList<string> WarmTexts(IEnumerable<string> texts)
    {
        var missing = new List<string>();
        var seen = new ValueSet<string>([], SwiftStringComparer.Instance);
        var remainingBytes = 1_048_576;
        var candidateCount = 0;
        foreach (var text in texts)
        {
            if (candidateCount >= WarmCandidateLimit) break;
            candidateCount++;
            var bytes = Math.Min(SwiftString.Utf8Count(text), 262_145);
            if (bytes > 262_144 || bytes > remainingBytes || blockCache.Contains(text) || seen.Contains(text)) continue;
            seen = seen.Mutating(s => s.Add(text));
            missing.Add(text);
            remainingBytes -= bytes;
            if (missing.Count == 24) break;
        }
        return new ValueList<string>([.. missing]);
    }

    // :237-255 @concurrent private nonisolated static func parseWarm(_ texts: [String]) async throws
    // -> [(String, [MarkdownBlock], [(String, AttributedString)])]. R-ISO-2: @concurrent -> Task.Run at the
    // boundary. PR-THEME-124.
    private static Task<List<(string Text, ValueList<MarkdownBlock> Blocks, List<(string Source, AttributedText Pretty)> Inline)>> ParseWarm(
        IReadOnlyList<string> texts) => Task.Run(() =>
    {
        var parsed = new List<(string, ValueList<MarkdownBlock>, List<(string, AttributedText)>)>();
        var seenInline = new ValueSet<string>([], SwiftStringComparer.Instance);
        foreach (var text in texts)
        {
            SwiftTask.CheckCancellation();
            var blocks = MarkdownParser.MarkdownParserParse(text);
            var sources = InlineSources(blocks, new ValueSet<string>([], SwiftStringComparer.Instance));
            var inline = new List<(string, AttributedText)>();
            foreach (var source in sources)
            {
                if (seenInline.Contains(source)) continue;
                seenInline = seenInline.Mutating(s => s.Add(source));
                SwiftTask.CheckCancellation();
                inline.Add((source, RichLink.Build(source)));
            }
            parsed.Add((text, blocks, inline));
        }
        SwiftTask.CheckCancellation();
        return parsed;
    });

    // :257-275 nonisolated private static func inlineSources(of blocks: [MarkdownBlock], into sources: inout
    // Set<String>) throws; the inout set comes back changed (R-VAL-1). PR-THEME-124: paragraphs and headings
    // contribute their text, list items their text and their children recursively, quotes their children; code,
    // tables and rules contribute nothing.
    private static ValueSet<string> InlineSources(IReadOnlyList<MarkdownBlock> of, ValueSet<string> into)
    {
        var sources = into;
        foreach (var block in of)
        {
            SwiftTask.CheckCancellation();
            switch (block)
            {
                case MarkdownBlock.Paragraph p:
                    sources = sources.Mutating(s => s.Add(p.Text));
                    break;
                case MarkdownBlock.Heading h:
                    sources = sources.Mutating(s => s.Add(h.Text));
                    break;
                case MarkdownBlock.List l:
                    foreach (var item in l.Items)
                    {
                        SwiftTask.CheckCancellation();
                        if (item.Text.Length > 0) sources = sources.Mutating(s => s.Add(item.Text));
                        sources = InlineSources(item.Children, sources);
                    }
                    break;
                case MarkdownBlock.Quote q:
                    sources = InlineSources(q.Blocks, sources);
                    break;
                case MarkdownBlock.Code or MarkdownBlock.Table or MarkdownBlock.Rule:
                    break;
                default:
                    throw new UnreachableException();
            }
        }
        return sources;
    }

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

    // :376-383 private func headingFont(_ level: Int) -> Font. PR-THEME-109.
    private static SwiftFont HeadingFont(int level) => level switch
    {
        1 => ChatFont.Chat(SwiftFont.TextStyle.Title2, SwiftFont.Weight.Semibold), // :378
        2 => ChatFont.Chat(SwiftFont.TextStyle.Title3, SwiftFont.Weight.Semibold), // :379
        3 => ChatFont.Chat(SwiftFont.TextStyle.Headline), // :380: ChatFont.Chat's own default resolves headline to semibold (ED-186 (1)).
        _ => ChatFont.Chat(SwiftFont.TextStyle.Subheadline, SwiftFont.Weight.Semibold), // :381
    };

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

    // :506-534 @ViewBuilder private func marker(ordered: Bool, number: Int, checked: Bool?) -> some View.
    // PR-THEME-111/112/113: a checkbox marker (:508-511), an ordered number (:512-516), or an unordered dot/ring
    // (:517-533).
    private Control Marker(bool ordered, int number, bool? @checked)
    {
        if (@checked is { } isChecked)
        {
            // :508-511
            return SwiftImage.SystemName(isChecked ? "checkmark.circle.fill" : "circle")
                .ForegroundStyle(isChecked ? Chrome.Chrome.Accent : SwiftColor.Tertiary)
                .ImageScale(SwiftImage.Scale.Small);
        }
        if (ordered)
        {
            // :512-516
            var text = new TextBlock { Text = number + "." };
            var font = SwiftFont.Body.MonospacedDigit().FontWeight(SwiftFont.Weight.Medium);
            font.Apply(text);
            text.Foreground = MarkerColor.Brush(text);
            return text;
        }
        // :517-533: a hidden bullet glyph would only reserve baseline space in a real text layout; the marker
        // itself is the overlaid circle/ring below (:519-531), which is what every record here specifies.
        var circle = new Ellipse();
        if (MarkdownView.GetMarkdownListDepth(this) == 0)
        {
            circle.Width = 5; // :521
            circle.Height = 5;
            circle.Fill = MarkerColor.Brush(circle);
        }
        else
        {
            circle.Width = 5.5; // :526
            circle.Height = 5.5;
            circle.Stroke = MarkerColor.Brush(circle);
            circle.StrokeThickness = 1.2;
        }
        return circle;
    }

    // :536-538 private var markerColor: Color. PR-THEME-111/112.
    private SwiftColor MarkerColor => Chrome.Chrome.Accent.Opacity(MarkdownView.GetMarkdownDimmed(this) ? 0.55 : 0.9);
}

/// <summary>:542-544 <c>private struct MarkdownListDepthKey: EnvironmentKey</c>.</summary>
file static class MarkdownListDepthKey
{
    public const int DefaultValue = 0; // :543
}

/// <summary>:562-835 <c>enum RichLink</c>: pretty link titles plus favicons for chat markdown.</summary>
public static partial class RichLink
{
    private const int StreamingSlots = 4; // :568

    // :563 @MainActor private static var prettyCache = RecentCache<String, AttributedString>(limit: 1200).
    // PR-THEME-123.
    private static RecentCache<string, AttributedText> prettyCache = new(1200);

    // :591-594 @MainActor static func warm(_ source: String, with pretty: AttributedString). PR-THEME-124: only
    // the units this part does not cover (part 2's InlineText/prettyAttributed) call this, but it is declared
    // here since it lives beside prettyCache and MarkdownView.Warm (PR-THEME-124, above) calls it directly.
    public static void Warm(string source, AttributedText pretty)
    {
        if (!prettyCache.Contains(source)) prettyCache.Insert(pretty, source);
    }

    // :596-621 nonisolated static func build(_ source: String) -> AttributedString: baseAttributed, then every
    // link run whose display text is bare gets the pretty title in its place, and every link renders bold.
    // Referenced by MarkdownView.ParseWarm (PR-THEME-124) and this file's own prettyAttributed/linkHosts family
    // (part 2's records); IsBareDisplay and PrettyTitle stay MARROW_STUB below (not among this part's records).
    public static AttributedText Build(string source)
    {
        var baseText = BaseAttributed(source);
        var result = new List<AttributedRun>();
        foreach (var run in baseText.Runs)
        {
            if (run.Link is not { } url)
            {
                result.Add(run);
                continue;
            }
            var display = run.Text;
            var linkRun = IsBareDisplay(display, url)
                ? new AttributedRun(PrettyTitle(url), Link: url, IsBold: true)
                : run with { IsBold = true };
            result.Add(linkRun);
        }
        return new AttributedText(result);
    }

    // :750-755 private static let bareDomainPattern. .NET's Regex supports the look-behind Swift's Regex
    // cannot, so this needs no NSRegularExpression-only workaround (R-RE-1). PR-THEME-128.
    [GeneratedRegex(
        @"(?<![A-Za-z0-9@._/-])((?:[A-Za-z0-9](?:[A-Za-z0-9-]*[A-Za-z0-9])?\.)+[A-Za-z]{2,24})(/[^\s<>()]*)?",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BareDomainPattern();

    // :790 let trimSet = ".,;:!?'\"" -- trailing punctuation belongs to the sentence, not the link.
    private const string TrimSet = ".,;:!?'\"";

    // :772-826 private static func autolinkedBareDomains(_ parsed: AttributedString) -> AttributedString.
    // PR-THEME-128/129: skip a run already a link or code; otherwise scan it for bare-domain matches, trim
    // trailing sentence punctuation, and link only when the domain's last label is a curated TLD and
    // "https://" + the match parses as a URL.
    private static AttributedText AutolinkedBareDomains(AttributedText parsed)
    {
        var outRuns = new List<AttributedRun>();
        foreach (var run in parsed.Runs)
        {
            var text = run.Text;
            if (run.Link is not null || run.IsCode) // :778-780
            {
                outRuns.Add(run);
                continue;
            }
            var matches = BareDomainPattern().Matches(text);
            if (matches.Count == 0) // :792-795 no pattern match
            {
                outRuns.Add(run);
                continue;
            }
            var cursor = 0;
            foreach (Match match in matches)
            {
                var start = match.Index;
                if (start < cursor) continue; // :801 guard start >= cursor
                var matchText = match.Value;
                while (matchText.Length > 0 && TrimSet.IndexOf(matchText[^1], StringComparison.Ordinal) >= 0) matchText = matchText[..^1]; // :803-804
                var end = start + matchText.Length; // :805
                if (start > cursor) outRuns.Add(run with { Text = text[cursor..start] }); // :806
                var slash = matchText.IndexOf('/', StringComparison.Ordinal); // :808
                var domainPart = slash >= 0 ? matchText[..slash] : matchText; // :809
                var labels = domainPart.Split('.');
                var lastLabel = labels.Length > 0 ? SwiftString.Lowercased(labels[^1]) : ""; // :810
                if (BareDomainTLDs.Contains(lastLabel) && SwiftURL.Create("https://" + matchText) is { } url) // :811
                {
                    outRuns.Add(run with { Text = matchText, Link = url }); // :812-814
                }
                else if (end > cursor) // :815-816
                {
                    outRuns.Add(run with { Text = matchText });
                }
                cursor = end; // :818
            }
            if (cursor < text.Length) outRuns.Add(run with { Text = text[cursor..] }); // :820
        }
        return new AttributedText(outRuns);
    }

    // ENGINEERING DECISION (ED-158 (7): "Links stay with the MarkdownView unit"; ED-092 (3) leaves AttributedString
    // undecided otherwise): the port's inline-only markdown scan for baseAttributed (:828-834, PR-THEME-127). No
    // port type or macOS reference (OD-0001) exists for Foundation's undocumented AttributedString(markdown:)
    // grammar, so this implements exactly what PR-THEME-127 names -- inline code spans, emphasis, strong
    // emphasis, `[text](url)` links and `<url>` autolinks -- with one left-to-right scan. An unmatched or
    // malformed marker is left as literal text; every character the scan does not consume as a marker,
    // including newlines and runs of spaces, is preserved verbatim in a plain run (PR-THEME-127's whitespace
    // requirement); the scan never throws. Fidelity beyond what PR-THEME-127 states (nested emphasis, escaped
    // markers, CommonMark's flanking-delimiter rules) is UNVERIFIED under OD-0001.
    private static AttributedText ParseInline(string source)
    {
        var runs = new List<AttributedRun>();
        var textStart = 0;
        var i = 0;
        var n = source.Length;
        void FlushText(int end)
        {
            if (end > textStart) runs.Add(new AttributedRun(source[textStart..end]));
        }
        while (i < n)
        {
            var c = source[i];
            if (c == '`')
            {
                var fenceLen = 1;
                while (i + fenceLen < n && source[i + fenceLen] == '`') fenceLen++;
                var fence = new string('`', fenceLen);
                var close = source.IndexOf(fence, i + fenceLen, StringComparison.Ordinal);
                if (close >= 0)
                {
                    FlushText(i);
                    runs.Add(new AttributedRun(source[(i + fenceLen)..close], IsCode: true));
                    i = close + fenceLen;
                    textStart = i;
                    continue;
                }
            }
            else if ((c == '*' || c == '_') && i + 1 < n && source[i + 1] == c)
            {
                var marker = new string(c, 2);
                var close = source.IndexOf(marker, i + 2, StringComparison.Ordinal);
                if (close > i + 2)
                {
                    FlushText(i);
                    runs.Add(new AttributedRun(source[(i + 2)..close], IsBold: true));
                    i = close + 2;
                    textStart = i;
                    continue;
                }
            }
            else if (c == '*' || c == '_')
            {
                var close = source.IndexOf(c, i + 1);
                if (close > i + 1)
                {
                    FlushText(i);
                    runs.Add(new AttributedRun(source[(i + 1)..close], IsItalic: true));
                    i = close + 1;
                    textStart = i;
                    continue;
                }
            }
            else if (c == '[')
            {
                var closeBracket = source.IndexOf(']', i + 1);
                if (closeBracket > i && closeBracket + 1 < n && source[closeBracket + 1] == '(')
                {
                    var closeParen = source.IndexOf(')', closeBracket + 2);
                    if (closeParen > closeBracket + 1)
                    {
                        var label = source[(i + 1)..closeBracket];
                        var urlText = source[(closeBracket + 2)..closeParen];
                        if (SwiftURL.Create(urlText) is { } url)
                        {
                            FlushText(i);
                            runs.Add(new AttributedRun(label, Link: url));
                            i = closeParen + 1;
                            textStart = i;
                            continue;
                        }
                    }
                }
            }
            else if (c == '<')
            {
                var close = source.IndexOf('>', i + 1);
                if (close > i + 1)
                {
                    var candidate = source[(i + 1)..close];
                    if (candidate.Contains("://", StringComparison.Ordinal) && SwiftURL.Create(candidate) is { } url)
                    {
                        FlushText(i);
                        runs.Add(new AttributedRun(candidate, Link: url));
                        i = close + 1;
                        textStart = i;
                        continue;
                    }
                }
            }
            i++;
        }
        FlushText(n);
        return new AttributedText(runs);
    }

    // :828-834 private static func baseAttributed(_ source: String) -> AttributedString. PR-THEME-127: the parse
    // is wrapped so it never fails, falling back to plain text, and always passes through the bare-domain
    // autolinker.
    private static AttributedText BaseAttributed(string source)
    {
        AttributedText parsed;
        try
        {
            parsed = ParseInline(source);
        }
        catch
        {
            parsed = AttributedText.Plain(source);
        }
        return AutolinkedBareDomains(parsed);
    }

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
    // :892-898 func body(content: Content) -> some View. PR-THEME-126: the environment's value is always true
    // (dead_code.md entry 30: markdownBlockSelection has no writer, so the else branch -- .textSelection(.disabled)
    // -- is dead and, per ED-061, is not implemented). Avalonia has no per-subtree "enable text selection"
    // modifier the way SwiftUI's `.textSelection` is: the content this wraps (InlineText, VeiledText, the code
    // and table text) renders its own selectable text directly, so the modifier's one live effect is already
    // true of its content, and this is the identity transform.
    public Control Body(Control content) => content;
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
    // :1278-1321 body: waits on VeiledText (part 2's StreamVeil.cs) for the code text itself; the line-counting
    // and collapse math (PR-THEME-118) is Head, below, which part 2's body calls.
    public CodeBlock(string? language, string code) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Markdown/MarkdownView.swift:1278");

    // :1324-1335 private static func head(of code: String, lines: Int) -> String. PR-THEME-118: counts UTF-8
    // line feeds without splitting the text into an array, and returns everything up to (not including) the
    // Nth line feed, or the whole text when there are fewer than N line feeds.
    private static string Head(string of, int lines)
    {
        var code = of;
        var remaining = lines;
        var utf8 = Encoding.UTF8.GetBytes(code);
        var end = -1;
        for (var index = 0; index < utf8.Length; index++)
        {
            if (utf8[index] != 0x0A) continue;
            remaining--;
            if (remaining == 0) { end = index; break; }
        }
        return remaining == 0 && end >= 0 ? Encoding.UTF8.GetString(utf8, 0, end) : code;
    }
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
public static partial class ProseReflow
{
    // :1436 .range(of: #"[.!?] "#, options: .regularExpression) != nil
    [GeneratedRegex(@"[.!?] ", RegexOptions.CultureInvariant)]
    private static partial Regex SentencePunctuationPattern();

    private const string ListMarkerFirstChars = "#>-*+|"; // :1414
    private const string NoTrailingChars = "{};:=([\\|"; // :1432

    // :1402-1468 static func reflowed(_ text: String) -> String. PR-THEME-121: joins a hard-wrapped paragraph
    // into one line only when all eight conditions (:1409-1437) hold; a fenced region, and its own fence lines,
    // are never reflowed.
    public static string Reflowed(string text)
    {
        var lines = text.Split('\n'); // :1403 components(separatedBy: "\n"), O-13 (R-STR-3)
        var output = new List<string>();
        var paragraph = new List<string>();
        var fenceOpen = false;
        var fenceLanguage = "";

        void FlushParagraph()
        {
            if (paragraph.Count == 0) return; // :1410
            var trimmed = paragraph.Select(p => FoundationText.Trim(p)).ToList(); // :1411
            var canReflow = trimmed.Count >= 2 // :1412
                && fenceLanguage.Length == 0 // :1413
                && paragraph.Zip(trimmed).All(pair => // :1414-1416
                    pair.Second.Length > 0 && !(pair.First.Length > 0 && FoundationText.IsWhitespace(pair.First[0])))
                && trimmed.All(line => // :1417-1425
                {
                    if (line.Length == 0) return false;
                    var first = line[0];
                    if (ListMarkerFirstChars.IndexOf(first, StringComparison.Ordinal) >= 0) return false;
                    if (SwiftCharacter.IsNumber(first.ToString()) && line.Length > 1 && (line[1] == '.' || line[1] == ')'))
                    {
                        return false;
                    }
                    return true;
                })
                && trimmed.Take(trimmed.Count - 1).All(line => SwiftString.Count(line) >= 45) // :1426
                && trimmed.Take(trimmed.Count - 1).All(line => // :1427-1432
                {
                    if (line.Length == 0) return true;
                    var last1 = line[^1];
                    if (NoTrailingChars.IndexOf(last1, StringComparison.Ordinal) >= 0) return false;
                    if (SwiftString.HasSuffix(line, "->") || SwiftString.HasSuffix(line, "=>")) return false;
                    return true;
                })
                && trimmed.All(line => !line.Contains("  ", StringComparison.Ordinal) && !line.Contains('\t', StringComparison.Ordinal)) // :1433
                && SentencePunctuationPattern().IsMatch(string.Join(" ", trimmed)); // :1436
            if (canReflow)
            {
                output.Add(string.Join(" ", trimmed)); // :1438
            }
            else
            {
                output.AddRange(paragraph); // :1440
            }
            paragraph.Clear();
        }

        foreach (var line in lines)
        {
            var trimmedLine = FoundationText.Trim(line);
            if (SwiftString.HasPrefix(trimmedLine, "```")) // :1446
            {
                FlushParagraph();
                output.Add(line);
                if (fenceOpen)
                {
                    fenceOpen = false;
                    fenceLanguage = "";
                }
                else
                {
                    fenceOpen = true;
                    fenceLanguage = SwiftString.Lowercased(trimmedLine.Length > 3 ? trimmedLine[3..] : "");
                }
            }
            else if (trimmedLine.Length == 0) // :1454
            {
                FlushParagraph();
                output.Add(line);
            }
            else if (fenceOpen) // :1457
            {
                FlushParagraph();
                output.Add(line);
            }
            else
            {
                paragraph.Add(line); // :1460
            }
        }
        FlushParagraph();
        return string.Join("\n", output);
    }
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
