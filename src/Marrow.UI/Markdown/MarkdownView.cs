using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Controls.Shapes;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.UI.Chrome;
using Marrow.UI.Common;
using Marrow.UI.Port;

// Source: DroppyCode/UI/Markdown/MarkdownView.swift (B3 52bc792), :1-1425. Link clicks, mention scanning and the
// continuous hover now go through UI.Markdown.MarkdownLinkOpener, MentionScanner (N10) and LinkHover (N9); see
// each member's own citation.
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
// the hook MarkdownDelegationCard.Render (:501-504) that the App layer sets at start-up; B3 renamed this hook from
// MarkdownBlockView.HydraDelegationBlock to its own enum, matching the source, and made "no renderer registered
// yet" fall back to CodeBlock instead of throwing (PR-THEME-108).
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

    internal const int StreamingSlots = 48; // :115 nonisolated fileprivate at B3, so RichLink (:811) can read it

    /// <param name="text">:10 <c>let text: String</c>.</param>
    public string Text { get; private set; }

    /// <param name="isStreaming">:16 <c>var isStreaming = false</c>.</param>
    public bool IsStreaming { get; }

    // :21-23 nonisolated static func ==.
    public static bool Equatable(MarkdownView lhs, MarkdownView rhs) => lhs.Text == rhs.Text && lhs.IsStreaming == rhs.IsStreaming;

    // :63-103 body. The prose-run branch (PR-THEME-105) builds ProseRunView for each prose run (part 2's sibling
    // ProseRunView unit; MARROW_STUB there is expected and left alone). The block branch is fully implemented,
    // via MarkdownBlockView.
    // PORT-ONLY `veils` (UI-THEME-047): the identity a row that builds this view again for every flush keeps for the tree, so the veil of each
    // place survives the rebuild as SwiftUI's @State does (StreamVeil.cs, MarkdownVeils).
    public MarkdownView(string text, bool isStreaming = false, MarkdownVeilScope? veils = null)
    {
        Text = text;
        IsStreaming = isStreaming;
        var parse = isStreaming ? StreamingParse(text) : (Blocks: Blocks(text), Stable: 0); // :67
        var blocks = parse.Blocks;
        var stable = parse.Stable;
        var proseRuns = GetMarkdownProseRuns(this); // :71 @Environment(\.markdownProseRuns)
        Control? root = null;
        if (!isStreaming && proseRuns && veils is { HoldsText: true } or { TreeKnown: true })
        {
            // PORT-ONLY, owner-ruled (OD-0076, UI-THEME-047): the reply has just stopped streaming on screen. The Swift draws it as prose runs at once
            // (:71-83), a different view, so the words still dissolving jump to the full colour and words that arrive with the completion
            // (ThreadRuntime.swift:3248-3265) never dissolve: a cut, and "a token that popped in whole read as a glitch" (StreamVeil.swift:4-8). The
            // reply is drawn as its veiled blocks a little longer instead, at the same places, still veiled so the last batch registers as a chunk;
            // the paragraphs feed their veils as they are built (the inherited \.markdownVeiled reaches them here), so whether anything dissolves
            // is known now. A block that the last batch OPENS arrives with the soft appear all the same (MarkdownView.swift:92), so it is on its way in for as long
            // as that takes. When nothing does (and no link paragraph, which feeds its veil only once it is in the tree, may still take a last
            // batch), the prose runs are drawn at once, as before.
            var veiled = VeiledBlocks(blocks, stable, isStreaming: false, veils);
            StreamVeil.SetMarkdownVeiled(veiled, true);
            if (veils.FadingUntil(SwiftDate.Now) is not null || veils.HoldsLinkText)
            {
                closingVeils = veils;
                root = veiled;
            }
        }

        if (closingVeils is not null)
        {
            closingBlocks = blocks;
            AttachedToVisualTree += (_, _) => closingClock ??= new Tracked<DateTimeOffset>(() => VeilClock.Shared.Now, _ => MainActor.Post(CloseWhenSettled));
            DetachedFromVisualTree += (_, _) => StopClosingClock();
        }
        else if (!isStreaming && proseRuns) // :71-83
        {
            // PORT-ONLY (UI-THEME-047): the reply is drawn as prose runs now, so the veiled views that kept its places have left (StreamVeil.swift:291-295:
            // "once the reply has finished the veil is emptied too, so a settled paragraph keeps no copy of itself"): the places are released.
            veils?.Release();
            root = ProseRuns(blocks);
            StreamVeil.SetMarkdownVeiled(root, isStreaming); // :80 .environment(\.markdownVeiled, isStreaming)
        }
        else // :84-98
        {
            List<PlacedBlock>? placed = isStreaming && veils is not null ? [] : null; // lean L11: the blocks a later flush may keep (Stream)
            var stack = VeiledBlocks(blocks, stable, isStreaming, veils, placed);
            root = stack;
            StreamVeil.SetMarkdownVeiled(root, isStreaming); // :96 .environment(\.markdownVeiled, isStreaming)
            if (placed is not null)
            {
                streamStack = stack;
                streamVeils = veils;
                placedBlocks = placed;
            }
        }

        Child = root;
    }

    // :72-82 the prose-run branch. PR-THEME-105: ProseRunView for each prose run; code, tables and rules stand alone.
    private static StackPanel ProseRuns(IReadOnlyList<MarkdownBlock> blocks)
    {
        var stack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 12 }; // :72
        foreach (var segment in Segments(blocks)) // :73-80
        {
            Control child = segment switch
            {
                MarkdownSegment.Prose prose => new ProseRunView(prose.Value), // :75-76
                MarkdownSegment.Block block => new MarkdownBlockView(block.Value).Transition(Transition(block.Value)), // :77-79
                _ => throw new UnreachableException(),
            };
            stack.Children.Add(child);
        }
        stack.HorizontalAlignment = HorizontalAlignment.Stretch; // :82 frame(maxWidth: .infinity, alignment: .leading)
        return stack;
    }

    // :86-97 the block branch, each block at its place in the veils (b{n}). `placed` (lean L11) collects what Stream needs to keep the settled blocks.
    private static StackPanel VeiledBlocks(IReadOnlyList<MarkdownBlock> blocks, int stable, bool isStreaming, MarkdownVeilScope? veils, List<PlacedBlock>? placed = null)
    {
        var now = SwiftDate.Now;
        var last = blocks.Count - 1;
        var known = veils?.TreeKnown ?? false; // PORT-ONLY (UI-THEME-047): a tree that has been built before, so a block that is new in it is an insertion
        var stack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 12 }; // :86
        for (var index = 0; index < blocks.Count; index++) // :87
        {
            var block = blocks[index];
            var place = veils?.Child("b" + index);
            var view = new MarkdownBlockView(block, settled: index < stable, veils: place); // :88
            SetMarkdownStreaming(view, isStreaming && index == last); // :89-91
            if (place?.Arrival(known) is { } elapsed)
            {
                // PORT-ONLY (UI-THEME-047, OD-0076): a block a flush opens is an insertion in the Swift's ForEach (:87, id: \.offset), and :92's soft appear runs on
                // it (opacity and a 4 point offset, 0.32 s); it runs on through the flushes after, which each build the tree again, from where it was.
                view.Transition(Arrival(block));
                SwiftTransition.Insert(stack, stack.Children.Count, view, elapsed);
                placed?.Add(new PlacedBlock(view, index < stable ? now : null, Plain: false));
            }
            else
            {
                view.Transition(Transition(block)); // :92
                stack.Children.Add(view);
                placed?.Add(new PlacedBlock(view, index < stable ? now : null, Plain: true));
            }
        }
        veils?.TreeBuilt(blocks.Count);
        stack.HorizontalAlignment = HorizontalAlignment.Stretch; // :97 frame(maxWidth: .infinity, alignment: .leading)
        return stack;
    }

    // PORT-ONLY (lean L11, ED-NEW-lean-11). A block of a streaming tree as it stands in the stack: its view, since when the parser has called it settled
    // (null while it is not), and whether it was placed plainly (not by an arrival's insertion).
    private readonly record struct PlacedBlock(MarkdownBlockView View, DateTimeOffset? SettledSince, bool Plain);

    // PORT-ONLY (lean L11). The streaming tree this view built (its stack, the places of its veils, its blocks in order): what Stream updates in place.
    private StackPanel? streamStack;
    private MarkdownVeilScope? streamVeils;
    private List<PlacedBlock> placedBlocks = [];

    /// <summary>PORT-ONLY (lean L11, ED-NEW-lean-11). How long a settled block rests before a flush keeps its view: every chunk of its veil has settled by then
    /// (no chunk fades longer than <see cref="StreamVeil.LongestFade"/> from the flush that fed it, and a settled block's text is fed no more) and its arrival has
    /// run (<see cref="MarkdownVeils.ArrivalLength"/>), with a margin for the frame that paints the last of either. A block in that window is built again
    /// for every flush, exactly as before.</summary>
    internal static TimeSpan SettleQuiet { get; } = TimeSpan.FromSeconds(Math.Max(1.0, StreamVeil.LongestFade + MarkdownVeils.ArrivalLength.TotalSeconds + 0.25));

    /// <summary>
    /// PORT-ONLY (lean L11, ED-NEW-lean-11): the next flush of the streaming reply this view shows, applied to this view instead of building a new one.
    /// The Swift's ForEach keeps every block view and `.equatable()` skips a settled block (MarkdownView.swift:86-97, :295-298); the port built the whole
    /// tree again for every flush, so a flush cost grew with the reply. Here the leading blocks the parser has settled, that are the same block, that are not
    /// the last block, that were placed plainly and that have rested <see cref="SettleQuiet"/> keep their views where they are (never taken out of the
    /// tree); every block from the first that does not is built again and placed exactly as the constructor places it (the same veil places b{n}, the same
    /// arrival and transition). <paramref name="keep"/> is told, before the change, the views that leave and the views just built (none in the tree yet),
    /// as a row that rebuilds the whole tree hands its delegation cards over (TimelineRows' KeepCards). Returns false, changing nothing, when this view
    /// did not build a streaming tree or its veils have forgotten the tree (the row left the screen): the caller builds a new view then.
    /// </summary>
    public bool Stream(string text, Action<IReadOnlyList<Control>, IReadOnlyList<Control>>? keep = null)
    {
        if (streamStack is not { } stack || streamVeils is not { TreeKnown: true } veils) return false;
        var (blocks, stable) = StreamingParse(text); // :67
        var now = SwiftDate.Now;
        var last = blocks.Count - 1;
        var kept = 0;
        while (kept < placedBlocks.Count && kept < blocks.Count && Keeps(placedBlocks[kept], blocks[kept], kept < stable, kept == last, now)) kept++;
        var made = new List<Control>(blocks.Count - kept);
        var arrivals = new TimeSpan?[blocks.Count - kept];
        var settledSince = new DateTimeOffset?[blocks.Count - kept];
        for (var index = kept; index < blocks.Count; index++) // :87-92, as VeiledBlocks
        {
            var block = blocks[index];
            var place = veils.Child("b" + index);
            var view = new MarkdownBlockView(block, settled: index < stable, veils: place); // :88
            SetMarkdownStreaming(view, index == last); // :89-91
            var arrival = place.Arrival(true);
            view.Transition(arrival is null ? Transition(block) : Arrival(block)); // :92
            made.Add(view);
            arrivals[index - kept] = arrival;
            settledSince[index - kept] = index >= stable ? null
                : index < placedBlocks.Count && placedBlocks[index].SettledSince is { } since && MarkdownBlockView.Keeps(placedBlocks[index].View, block, settled: true) ? since : now;
        }
        if (keep is not null)
        {
            var leaving = new List<Control>(stack.Children.Count - kept);
            for (var index = kept; index < stack.Children.Count; index++) leaving.Add(stack.Children[index]);
            keep(leaving, made);
        }
        stack.Children.RemoveRange(kept, stack.Children.Count - kept);
        placedBlocks.RemoveRange(kept, placedBlocks.Count - kept);
        for (var index = 0; index < made.Count; index++)
        {
            if (arrivals[index] is { } elapsed) SwiftTransition.Insert(stack, stack.Children.Count, made[index], elapsed);
            else stack.Children.Add(made[index]);
            placedBlocks.Add(new PlacedBlock((MarkdownBlockView)made[index], settledSince[index], Plain: arrivals[index] is null));
        }
        veils.TreeBuilt(blocks.Count);
        Text = text;
        return true;
    }

    // lean L11: whether the block at a place keeps the view it has. MarkdownView.swift:295-298's settled rule (a settled pair is the same block), made stricter:
    // the parse of a text that does not extend the last one renumbers its blocks, so the block must also be the same block by content.
    private static bool Keeps(PlacedBlock placed, MarkdownBlock block, bool settled, bool isLast, DateTimeOffset now) =>
        placed.Plain && !isLast && placed.SettledSince is { } since && now - since >= SettleQuiet
        && !GetMarkdownStreaming(placed.View) && MarkdownBlockView.Keeps(placed.View, block, settled);

    // PORT-ONLY (UI-THEME-047). :51-56's transition as a block ARRIVES, which the soft appear of Chrome.swift:949-955 does: opacity 0 -> 1 with a 4 point offset -> 0
    // over Animation.softAppear. Chrome.SoftAppear's modifier carries a Bool alone, which the transition mechanics apply at its identity at once (SwiftAnimation.cs
    // Gather, the reason ThreadTimeline.RowTransition and HydraPanel give), so the insertion is decomposed into the modifier's layer effects (Chrome.swift:940-944).
    // The removal half is :51-56's own.
    private static SwiftTransition Arrival(MarkdownBlock @for) =>
        SwiftTransition.Asymmetric(
            insertion: SwiftTransition.Opacity.Combined(SwiftTransition.Offset(y: 4)).Animation(Animation.SoftAppear),
            removal: @for is MarkdownBlock.Code { Language: { } language } && IsDelegationInfo(SwiftString.Lowercased(language))
                ? SwiftTransition.Identity
                : SwiftTransition.Opacity.Animation(Animation.EaseOut(duration: 0.12)));

    // PORT-ONLY (OD-0076, UI-THEME-047): the places of a reply that stopped streaming while words were still dissolving (or with a link paragraph that may
    // take a last batch once it is in the tree), the finished reply's blocks, and the shared veil clock watched until they settle.
    private MarkdownVeilScope? closingVeils;
    private IReadOnlyList<MarkdownBlock> closingBlocks = [];
    private Tracked<DateTimeOffset>? closingClock;

    private void StopClosingClock()
    {
        closingClock?.Dispose();
        closingClock = null;
    }

    // PORT-ONLY (OD-0076). Runs after the pass that put this view in the tree (posted: the link paragraphs have fed their veils by then) and on every
    // tick of the shared veil clock (StreamVeil.swift:161-194). While something under this view still dissolves, the clock is kept running to its
    // settle (StreamVeil.swift:171-181); once nothing does, the reply is drawn as the Swift's prose runs (:71-83) and its places are released
    // (StreamVeil.swift:291-295). A view that is not in the tree does nothing: a newer view may have taken its places, and one that comes back
    // looks again.
    private void CloseWhenSettled()
    {
        if (closingVeils is not { } veils || !Avalonia.VisualTree.VisualExtensions.IsAttachedToVisualTree(this)) return;
        if (veils.FadingUntil(SwiftDate.Now) is { } settlesAt)
        {
            VeilClock.Shared.Run(until: settlesAt);
            return;
        }

        closingVeils = null;
        StopClosingClock();
        veils.Release();
        var root = ProseRuns(closingBlocks);
        StreamVeil.SetMarkdownVeiled(root, false); // :80 .environment(\.markdownVeiled, isStreaming)
        closingBlocks = [];
        Child = root;
    }

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
    // ED-NEW-perf-memory-4 (PORT-ONLY): the count limit alone let a few very long messages fill the cache, and the key is the whole text. Each entry now costs
    // its text length in characters and a generation holds at most BlockCacheCostLimit of them, so short messages cache exactly as before (800 of them cost far
    // less) and only very large ones roll the generation earlier. A single text over the limit still caches, at the limit's cost.
    public const long BlockCacheCostLimit = 4_000_000;
    public const long PrettyCacheCostLimit = 1_000_000;
    public const long CellCacheCostLimit = 500_000;
    internal static long CacheCost(string text, long limit) => Math.Min(text.Length, limit);
    private static RecentCache<string, ValueList<MarkdownBlock>> blockCache = new(800, BlockCacheCostLimit); // PR-THEME-123

    // PORT-ONLY (no Swift twin; the Ambient seam of ED-065). Told, on the thread that is about to parse, each text the markdown parser is asked to parse for the block
    // cache: the cold parse of Blocks(for:) (the caller's thread) and parseWarm's (:237-255, a worker). Nothing acts on it; a test reads it to prove where and when a
    // report's markdown is parsed (PR-CHAT-145: off the main thread, as the pill appears, one text per head, none twice). The default does nothing.
    public static Ambient<Action<string>> ParseObserver { get; } = new(static _ => { });

    // :111 @MainActor private static var streamingParses: [(text: String, parse: MarkdownParser.Parse)] = [].
    // lean L11: each kept text carries its UTF-8 length, measured once when it is kept rather than again for every flush.
    private static readonly List<(string Text, int Utf8Count, MarkdownParser.Parse Parse)> streamingParses = []; // PR-THEME-123

    // :123-133 static func streamingParse(for text: String) -> (blocks: [MarkdownBlock], stable: Int).
    // PR-THEME-123/125.
    public static (ValueList<MarkdownBlock> Blocks, int Stable) StreamingParse(string @for)
    {
        var text = @for;
        var count = SwiftString.Utf8Count(text); // lean L11: once per flush
        for (var i = streamingParses.Count - 1; i >= 0; i--)
        {
            var candidate = streamingParses[i];
            if (candidate.Utf8Count == count && candidate.Text == text)
            {
                return (candidate.Parse.Blocks, candidate.Parse.Stable);
            }
        }
        var slot = -1;
        for (var i = streamingParses.Count - 1; i >= 0; i--)
        {
            if (ExtendsCounted(streamingParses[i].Text, streamingParses[i].Utf8Count, text, count)) { slot = i; break; }
        }
        var parsed = MarkdownParser.MarkdownParserParse(text, slot >= 0 ? (streamingParses[slot].Text, streamingParses[slot].Parse) : null);
        if (slot >= 0) streamingParses.RemoveAt(slot);
        else if (streamingParses.Count == StreamingSlots) streamingParses.RemoveAt(0);
        streamingParses.Add((text, count, parsed));
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
            ParseObserver.Current(text);
            parsed = MarkdownParser.MarkdownParserParse(text);
        }
        blockCache.Insert(parsed, text, CacheCost(text, BlockCacheCostLimit));
        return parsed;
    }

    // :154-167 private static func extends(_ earlier: String, to text: String) -> Bool. PR-THEME-125: byte-probe
    // extension test, not a whole-text comparison.
    private static bool Extends(string earlier, string to) => ExtendsCounted(earlier, SwiftString.Utf8Count(earlier), to, SwiftString.Utf8Count(to));

    // lean L11 (ED-NEW-lean-11): the same test with the two UTF-8 lengths known, reading only the probed bytes. It used to encode both whole texts to
    // compare 128 of their bytes, twice a reply's size for every flush.
    private static bool ExtendsCounted(string earlier, int earlierCount, string text, int count)
    {
        if (earlierCount <= 0 || earlierCount >= count) return false;
        const int probe = 64;
        if (earlierCount <= probe * 2)
        {
            Span<byte> whole = stackalloc byte[earlierCount];
            Span<byte> start = stackalloc byte[earlierCount];
            Utf8Window(earlier, 0, earlierCount, whole);
            Utf8Window(text, 0, earlierCount, start);
            return start.SequenceEqual(whole);
        }
        Span<byte> earlierProbe = stackalloc byte[probe];
        Span<byte> textProbe = stackalloc byte[probe];
        Utf8Window(earlier, 0, probe, earlierProbe);
        Utf8Window(text, 0, probe, textProbe);
        if (!textProbe.SequenceEqual(earlierProbe)) return false;
        var tailStart = earlierCount - probe;
        Utf8Window(earlier, tailStart, earlierCount, earlierProbe);
        Utf8Window(text, tailStart, earlierCount, textProbe);
        return textProbe.SequenceEqual(earlierProbe);
    }

    // lean L11: bytes [from, to) of Encoding.UTF8.GetBytes(text) (a lone surrogate as U+FFFD's three bytes, a pair as four), into `into`, encoding only
    // the characters that hold them. `to` is at most the text's UTF-8 length.
    private static void Utf8Window(string text, int from, int to, Span<byte> into)
    {
        int index = 0, offset = 0;
        while (index < text.Length)
        {
            var width = Utf8Width(text, index, out var units);
            if (offset + width > from) break;
            offset += width;
            index += units;
        }
        var first = index;
        var firstOffset = offset;
        while (index < text.Length && offset < to)
        {
            offset += Utf8Width(text, index, out var units);
            index += units;
        }
        Span<byte> bytes = stackalloc byte[offset - firstOffset];
        Encoding.UTF8.GetBytes(text.AsSpan(first, index - first), bytes);
        bytes.Slice(from - firstOffset, to - from).CopyTo(into);
    }

    private static int Utf8Width(string text, int index, out int units)
    {
        var unit = text[index];
        units = 1;
        if (unit < 0x80) return 1;
        if (unit < 0x800) return 2;
        if (char.IsHighSurrogate(unit) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1]))
        {
            units = 2;
            return 4;
        }
        return 3;
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
            if (!blockCache.Contains(text)) blockCache.Insert(blocks, text, CacheCost(text, BlockCacheCostLimit));
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
        IReadOnlyList<string> texts) => ParseWarm(texts, ParseObserver.Current); // the observer is read here, on the caller's flow, and told from the worker

    private static Task<List<(string Text, ValueList<MarkdownBlock> Blocks, List<(string Source, AttributedText Pretty)> Inline)>> ParseWarm(
        IReadOnlyList<string> texts, Action<string> observer) => Task.Run(() =>
    {
        var parsed = new List<(string, ValueList<MarkdownBlock>, List<(string, AttributedText)>)>();
        var seenInline = new ValueSet<string>([], SwiftStringComparer.Instance);
        foreach (var text in texts)
        {
            SwiftTask.CheckCancellation();
            observer(text);
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

    // :1110 @Environment(\.openURL): the OpenURLAction an ancestor supplies, `.handled` when it took the URL (true) and `.systemAction` when it did not (false).
    // The timeline supplies one that selects a head's chat for a head mention's URL (ThreadTimeline.swift:211-217); anything it declines, and every view
    // outside a timeline, goes to the system's handling (the App's hook, InlineText.OpenUrl).
    public static readonly AttachedProperty<Func<SwiftURL, bool>?> OpenUrlActionProperty =
        AvaloniaProperty.RegisterAttached<Control, Func<SwiftURL, bool>?>("OpenUrlAction", typeof(MarkdownView), null, inherits: true);

    public static Func<SwiftURL, bool>? GetOpenUrlAction(Control control) => control.GetValue(OpenUrlActionProperty);

    public static void SetOpenUrlAction(Control control, Func<SwiftURL, bool>? value) => control.SetValue(OpenUrlActionProperty, value);

    /// <summary>:1148 <c>{ url in openURL(url) }</c> read from <paramref name="control"/>'s environment.</summary>
    public static void OpenUrl(Control control, SwiftURL url)
    {
        if (GetOpenUrlAction(control)?.Invoke(url) == true) return;
        InlineText.OpenUrl?.Invoke(url);
    }

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

    // PORT-ONLY (PR-THEME-126, UI-UPDATE-032): SwiftUI's own `.textSelection(.enabled)` as an environment value an ANCESTOR of the markdown view sets (the licences popover's
    // documents, SettingsView.swift:1033). SwiftUI applies it to every Text below, which includes the caption of a code block (MarkdownView.swift:1286-1289), a Text that
    // MarkdownView's own MarkdownBlockSelection (:1131, :1229, :1308) does not cover; in a chat message nothing above the view sets it and the caption is not selectable.
    public static readonly AttachedProperty<bool> TextSelectionEnabledProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>(
            "TextSelectionEnabled", typeof(MarkdownView), false, inherits: true);

    public static bool GetTextSelectionEnabled(Control control) => control.GetValue(TextSelectionEnabledProperty);

    public static void SetTextSelectionEnabled(Control control, bool value) => control.SetValue(TextSelectionEnabledProperty, value);

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
    /// <param name="block">:279 <c>let block: MarkdownBlock</c>.</param>
    /// <param name="settled">:284 <c>var settled = false</c>.</param>
    public MarkdownBlock Block { get; }

    public bool Settled { get; }

    // :300-374 body. PR-THEME-110 (paragraph path selection), PR-THEME-111/112/113 (marker, below),
    // PR-THEME-114 (list spacing), PR-THEME-115 (quote), PR-THEME-116 (rule), PR-THEME-119 (table, via
    // TableBlock). The .paragraph InlineText/VeiledText paths and the .code CodeBlock path call into this
    // file's own InlineText/CodeBlock and into part 2's mention helpers above, which stay MARROW_STUB until
    // part 2 and the sibling StreamVeil/ProseRunView/LinkParagraphView units land (see the unit's dependency
    // cycle note); this body is otherwise complete and does not stand in for any of them.
    public MarkdownBlockView(MarkdownBlock block, bool settled = false, MarkdownVeilScope? veils = null)
    {
        Block = block;
        Settled = settled;
        this.veils = veils;
        Child = Build(block);
    }

    // PORT-ONLY (UI-THEME-047): where this block's veils live (see MarkdownView); null for a view that is not rebuilt flush after flush.
    private readonly MarkdownVeilScope? veils;

    // Each list row's marker and each nested block of a list, kept so the markers follow the environment.
    private readonly List<(StackPanel Row, bool Ordered, int Number, bool? Checked)> markerRows = [];
    private readonly List<MarkdownBlockView> listChildren = [];

    // :285-286, :482, :495-497 @Environment(\.markdownDimmed) and @Environment(\.markdownListDepth): SwiftUI runs `body`
    // again whenever either changes, so a list inside a quote (dimmed) or inside another list (depth) draws its
    // markers in the dimmed accent and as rings. An inherited Avalonia property reaches a block only after its parent
    // has built it (the parent sets the depth and the quote the dimmed flag on the finished child), so the markers
    // built in the constructor are rebuilt here, and the depth passes on to the blocks nested under this list.
    // UI-THEME-029, UI-THEME-030, UI-THEME-032.
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        var isDepth = change.Property == MarkdownView.MarkdownListDepthProperty;
        if (!isDepth && change.Property != MarkdownView.MarkdownDimmedProperty) return;
        foreach (var (row, ordered, number, @checked) in markerRows)
            row.Children[0] = Marker(ordered, number, @checked);
        if (!isDepth) return;
        foreach (var child in listChildren)
            MarkdownView.SetMarkdownListDepth(child, MarkdownView.GetMarkdownListDepth(this) + 1); // :350
    }

    // :295-298 nonisolated static func ==. A settled pair is the same block by position and the parser's own
    // word alone; anything else compares by content.
    public static bool Equatable(MarkdownBlockView lhs, MarkdownBlockView rhs) =>
        (lhs.Settled && rhs.Settled) || lhs.Block == rhs.Block;

    /// <summary>PORT-ONLY (lean L11, ED-NEW-lean-11). Whether <paramref name="view"/> may stand for <paramref name="block"/> at its place in a flush that calls it
    /// <paramref name="settled"/>: <see cref="Equatable"/>'s settled pair, and the same block by content as well (MarkdownView.Stream).</summary>
    internal static bool Keeps(MarkdownBlockView view, MarkdownBlock block, bool settled) =>
        view.Settled && settled && (ReferenceEquals(view.Block, block) || view.Block == block);

    private Control Build(MarkdownBlock block)
    {
        switch (block)
        {
            case MarkdownBlock.Heading heading: // :302-304
            {
                var text = new InlineText(heading.Text, veils: veils).Font(HeadingFont(heading.Level));
                text.Margin = new Thickness(0, heading.Level <= 2 ? 6 : 2, 0, 0);
                return text;
            }
            case MarkdownBlock.Paragraph paragraph: // :306-320
            {
                var text = paragraph.Text;
                var streaming = MarkdownView.GetMarkdownStreaming(this);
                var listDepth = MarkdownView.GetMarkdownListDepth(this);
                if (RichLink.ContainsLinks(text, streaming)) return new InlineText(text, hasLinks: true, veils: veils); // :313
                if (!streaming && listDepth == 0 && MentionsTarget(text)) return new InlineText(text, hasLinks: true, veils: veils); // :314
                if (listDepth == 0 && MentionSegments(text) is { } segments) // :315-319
                {
                    var veiled = new VeiledText(segments, veils); // .modifier(MarkdownBlockSelection()) (:318): VeiledText draws a SelectableTextBlock, see MarkdownBlockSelection.Body
                    veiled.HorizontalAlignment = HorizontalAlignment.Stretch; // :318 frame(maxWidth: .infinity, alignment: .leading)
                    return veiled;
                }
                return new InlineText(text, hasLinks: false, veils: veils); // :320
            }
            case MarkdownBlock.Code code: // :321-333
            {
                var info = code.Language is { } language ? SwiftString.Lowercased(language) : null;
                // B3 :323-329: the renderer-registered check moves inline (`let delegationRenderer = ...`), and
                // falling through to CodeBlock now also covers "no renderer registered yet" (PR-THEME-108),
                // rather than throwing.
                if (info is not null && MarkdownView.IsDelegationInfo(info) && MarkdownDelegationCard.Render is { } delegationRenderer)
                {
                    var card = delegationRenderer(code.CodeText, info != "hydra"); // :326
                    var leaving = info == "hydra-leaving";
                    card.Opacity = leaving ? 0 : 1; // :327
                    card.RenderTransform = new ScaleTransform(leaving ? 0.98 : 1, leaving ? 0.98 : 1); // :328 anchor: .top
                    card.RenderTransformOrigin = new RelativePoint(0.5, 0, RelativeUnit.Relative);
                    card.IsHitTestVisible = !leaving; // :329
                    return card;
                }
                return new CodeBlock(code.Language, code.CodeText, veils); // :331, and B3's new no-renderer-yet fallback
            }
            case MarkdownBlock.List list: // :334-353
            {
                var stack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 6 }; // :335
                for (var index = 0; index < list.Items.Count; index++)
                {
                    var item = list.Items[index];
                    // :337 HStack(alignment: .firstTextBaseline, spacing: 8). True first-text-baseline alignment
                    // is PR-THEME-151's baseline guides (part 2, InlineText); this row aligns the marker to the
                    // content's top, an ENGINEERING DECISION UNVERIFIED under OD-0001 until that unit lands.
                    var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Top };
                    row.Children.Add(Marker(list.Ordered, list.Start + index, item.Checked)); // :338
                    markerRows.Add((row, list.Ordered, list.Start + index, item.Checked));
                    var content = new StackPanel { Orientation = Orientation.Vertical, Spacing = 6 }; // :339
                    if (item.Text.Length > 0) // :340
                        content.Children.Add(new InlineText(item.Text, hasLinks: RichLink.ContainsLinks(item.Text, MarkdownView.GetMarkdownStreaming(this)), veils: veils?.Child("i" + index)));
                    var childPlace = 0;
                    foreach (var child in item.Children) // :341-343
                    {
                        var childView = new MarkdownBlockView(child, veils: veils?.Child("i" + index + "." + childPlace++));
                        MarkdownView.SetMarkdownListDepth(childView, MarkdownView.GetMarkdownListDepth(this) + 1); // :345-346
                        listChildren.Add(childView);
                        content.Children.Add(childView);
                    }
                    row.Children.Add(content);
                    stack.Children.Add(row);
                }
                return stack;
            }
            case MarkdownBlock.Quote quote: // :354-364
            {
                var capsule = new Border // :356 Capsule().fill(.quaternary).frame(width: 3)
                {
                    Width = 3,
                    HorizontalAlignment = HorizontalAlignment.Left,
                }.RoundedRectangle(1.5, RoundedCornerStyle.Continuous);
                capsule.Background = SwiftColor.Quaternary.Brush(capsule);
                var content = new StackPanel { Orientation = Orientation.Vertical, Spacing = 8 }; // :358
                var quotePlace = 0;
                foreach (var child in quote.Blocks) // :359-361
                {
                    var childView = new MarkdownBlockView(child, veils: veils?.Child("q" + quotePlace++));
                    MarkdownView.SetMarkdownDimmed(childView, true); // :364 .environment(\.markdownDimmed, true)
                    content.Children.Add(childView);
                }
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,10,*") }; // :355 HStack(alignment: .top, spacing: 10)
                Grid.SetColumn(capsule, 0);
                Grid.SetColumn(content, 2);
                row.Children.Add(capsule);
                row.Children.Add(content);
                var foreground = Chrome.Chrome.SecondaryText.Brush(row); // :363 .foregroundStyle(Chrome.secondaryText)
                TextElement.SetForeground(row, foreground);
                return row;
            }
            case MarkdownBlock.Table table: // :365-366
                return new TableBlock(table.Header, table.Rows);
            case MarkdownBlock.Rule: // :367-371
            {
                var rule = new Rectangle { Height = 1, Margin = new Thickness(0, 4, 0, 4) };
                rule.Fill = SwiftColor.Quaternary.Brush(rule);
                return rule;
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(block));
        }
    }

    // :376-383 private func headingFont(_ level: Int) -> Font. PR-THEME-109.
    private static SwiftFont HeadingFont(int level) => level switch
    {
        1 => ChatFont.Chat(SwiftFont.TextStyle.Title2, SwiftFont.Weight.Semibold), // :378
        2 => ChatFont.Chat(SwiftFont.TextStyle.Title3, SwiftFont.Weight.Semibold), // :379
        3 => ChatFont.Chat(SwiftFont.TextStyle.Headline), // :380: ChatFont.Chat's own default resolves headline to semibold (ED-186 (1)).
        _ => ChatFont.Chat(SwiftFont.TextStyle.Subheadline, SwiftFont.Weight.Semibold), // :381
    };

    // :386 private static func mentionGlyphOffset(size: CGFloat) -> CGFloat. PR-THEME-177: the comment at :385
    // says "lifts"; the value is negative, so it lowers the glyph -- the port keeps the value, not the comment.
    private static double MentionGlyphOffset(double size) => -(size * 0.2);

    // :406 @MainActor private static var glyphCache: [String: Image] = [:]. PR-THEME-177.
    private static readonly Dictionary<string, IImage> glyphCache = new(StringComparer.Ordinal);

    // :390-404 @MainActor static func mentionGlyph(_ persona: HydraPersona, size: CGFloat) -> Image; a template image
    // for a Text run (ED-158). PR-THEME-177.
    public static IImage MentionGlyph(HydraPersona persona, double size)
    {
        var key = persona.Asset + "@" + SwiftNumber.Description(size); // :392
        if (glyphCache.TryGetValue(key, out var cached)) return cached; // :393
        // Drawn on demand rather than into fixed pixels, so it stays crisp on any display. (:394-395)
        // ENGINEERING DECISION: NSImage(size:flipped:drawingHandler:) draws the named asset into a fresh
        // canvas of the requested size; the closest Avalonia equivalent is rasterizing the asset's own
        // SwiftImage control into a RenderTargetBitmap of that size (precedent: FaviconCache.Cleaned, below,
        // and GeniePlatform.cs), which implements IImageBrushSource for StyledText's template-image path
        // (ED-158 (1)).
        var source = SwiftImage.Named(persona.Asset); // :395
        source.Width = size;
        source.Height = size;
        source.Measure(new Size(size, size));
        source.Arrange(new Rect(0, 0, size, size));
        var pixelSize = new PixelSize(Math.Max(1, (int)Math.Ceiling(size)), Math.Max(1, (int)Math.Ceiling(size)));
        var bitmap = new RenderTargetBitmap(pixelSize, new Vector(96, 96)); // :396-399
        bitmap.Render(source); // :397 source?.draw(in: rect, ...)
        IImage image = bitmap; // :400-401 out.isTemplate = true; Image(nsImage: out).renderingMode(.template)
        glyphCache[key] = image; // :402
        return image; // :403
    }

    // :411-414 private func mentionsTarget(in text: String) -> Bool (reads the environment's targets). Needed by
    // PR-THEME-110's paragraph-path dispatch (this part); mentionText's own rendering is part 2 (PR-THEME-177/178).
    private bool MentionsTarget(string @in)
    {
        var targets = MarkdownView.GetHydraMentionTargets(this); // :412
        if (targets.Count == 0) return false;
        return MentionRanges(@in, [.. targets.Keys]).Count > 0; // :413
    }

    // :418-419 nonisolated static func mentionRanges(in text: String, names: [String]) -> [Range<String.Index>].
    // B3 moves the word-bounded, longest-name-first scan into the shared UI.Markdown.MentionScanner (N10); the
    // UTF-16 IsMentionBoundaryChar this file's own copy needed is dropped along with it.
    public static ValueList<(int LowerBound, int UpperBound)> MentionRanges(string @in, IReadOnlyList<string> names) =>
        new(MentionScanner.Matches(@in, names, name => name).Select(m => (m.LowerBound, m.UpperBound)).ToList());

    // :421-446 private func mentionSegments(in text: String) -> [VeiledText.Segment]?. The empty-personas guard
    // (:424) is this part's own PR-THEME-110 routing. B3 :425 moves the matching loop into MentionScanner too;
    // building the matched/prose segments now completes, since VeiledText.Segment.Prose landed with the sibling
    // StreamVeil unit.
    private ValueList<VeiledText.Segment>? MentionSegments(string @in)
    {
        var personas = MarkdownView.GetHydraMentionPersonas(this); // :424
        if (personas.Count == 0) return null;

        var streaming = MarkdownView.GetMarkdownStreaming(this);
        var pretty = RichLink.PrettyAttributed(@in, streaming); // :422
        var chars = pretty.PlainText; // :423
        var matches = MentionScanner.Matches(chars, personas, persona => persona.Name); // :425
        if (matches.Count == 0) return null; // :426

        var segments = new List<VeiledText.Segment>();
        var cursor = 0;
        foreach (var match in matches) // :435
        {
            if (cursor < match.LowerBound) // :436-437
                segments.Add(new VeiledText.Segment.Prose(SlicePretty(pretty, cursor, match.LowerBound)));
            segments.Add(new VeiledText.Segment.Fixed(MentionText(match.Value))); // :439
            cursor = match.UpperBound; // :440
        }
        if (cursor < chars.Length) // :442-443
            segments.Add(new VeiledText.Segment.Prose(SlicePretty(pretty, cursor, chars.Length)));

        return new ValueList<VeiledText.Segment>(segments); // :445
    }

    // :427-431 the local prettyRange(_:) helper: pretty[lo..<hi] in the C# port's run-based AttributedText, where
    // Foundation's AttributedString.Index range has no equivalent - split each run at the plain-text offsets it
    // spans, keeping every run's own attributes.
    private static AttributedText SlicePretty(AttributedText pretty, int lowerBound, int upperBound)
    {
        var slices = new List<AttributedRun>();
        var offset = 0;
        foreach (var run in pretty.Runs)
        {
            var runStart = offset;
            var runEnd = offset + run.Text.Length;
            offset = runEnd;
            if (runEnd <= lowerBound || runStart >= upperBound) continue;
            var sliceStart = Math.Max(lowerBound, runStart) - runStart;
            var sliceEnd = Math.Min(upperBound, runEnd) - runStart;
            if (sliceStart == sliceEnd) continue;
            slices.Add(run with { Text = run.Text[sliceStart..sliceEnd] });
        }
        return new AttributedText(slices);
    }

    // :489-504 private func mentionText(_ persona: HydraPersona) -> Text. PR-THEME-177/178.
    private StyledText MentionText(HydraPersona persona)
    {
        const double size = 16; // :490
        var color = HydraGlyph.PersonaColor(persona); // :218-224 (Hydra.swift)
        var glyph = new StyledText(MentionGlyph(persona, size), isTemplate: true) // :491
            .ForegroundColor(color) // :492
            .BaselineOffset(MentionGlyphOffset(size)); // :493
        var targets = MarkdownView.GetHydraMentionTargets(this); // :495
        if (targets.TryGetValue(persona.Name, out var target)) // :495-500
        {
            // A link in a Text keeps the colour the attribute sets and opens through the environment. (:494)
            var name = new StyledText(persona.Name) // :496 AttributedString(persona.name)
                .Link(target.Url) // :497 name.link = target.url
                .ForegroundColor(color) // :498 name.foregroundColor = persona.color
                .FontWeight(SwiftFont.Weight.Bold); // :499 name.inlinePresentationIntent = .stronglyEmphasized
            return glyph + new StyledText(" ") + name; // :500 "\(glyph) \(Text(name))"
        }
        var plainName = new StyledText(persona.Name) // :502 Text(verbatim: persona.name)
            .FontWeight(SwiftFont.Weight.Bold) // :502 .fontWeight(.bold)
            .ForegroundColor(color); // :502 .foregroundColor(persona.color)
        return glyph + new StyledText(" ") + plainName; // :503
    }

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
            // :513-515 no .font: SwiftUI's implicit default with the two modifiers, in the face's own line box (ED-290 (1)).
            var font = SwiftFont.Default.MonospacedDigit().FontWeight(SwiftFont.Weight.Medium);
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

/// <summary>
/// :501-504 <c>enum MarkdownDelegationCard { @MainActor static var render: ((String, Bool) -&gt; AnyView)? }</c>
/// (B3, replacing the direct <c>HydraDelegationBlock(json:sent:)</c> call this port's C# had named the same
/// way). Draws a <c>hydra</c> delegation block; the card is an App-level view UI cannot name (layering, see the
/// file header), so the App layer registers this hook at launch. Until it is set, the block reads as code
/// (PR-THEME-108's new fallback).
/// </summary>
public static class MarkdownDelegationCard
{
    /// <summary>:326 <c>delegationRenderer(code, info != "hydra")</c>.</summary>
    public delegate Control RenderDelegate(string json, bool sent);

    /// <summary>:502 <c>static var render</c>. Set once by the App layer at start-up (HANDOFF section 9).</summary>
    public static RenderDelegate? Render { get; set; }
}

/// <summary>:542-544 <c>private struct MarkdownListDepthKey: EnvironmentKey</c>.</summary>
file static class MarkdownListDepthKey
{
    public const int DefaultValue = 0; // :543
}

/// <summary>:562-835 <c>enum RichLink</c>: pretty link titles plus favicons for chat markdown.</summary>
public static partial class RichLink
{
    // :568 private static let streamingSlots = MarkdownView.streamingSlots (PR-THEME-123: no longer its own
    // independently-tunable 4; twelve times as many in-flight streaming links are kept before evicting).
    private const int StreamingSlots = MarkdownView.StreamingSlots;

    // :563 @MainActor private static var prettyCache = RecentCache<String, AttributedString>(limit: 1200).
    // PR-THEME-123.
    private static RecentCache<string, AttributedText> prettyCache = new(1200, MarkdownView.PrettyCacheCostLimit); // ED-NEW-perf-memory-4: costed by source length

    // :567-568 @MainActor private static var streamingPretty: [(source: String, value: AttributedString)] = [];
    // private static let streamingSlots = 4. PR-THEME-148: a small ring of in-flight (not-yet-settled) builds,
    // searched most-recent-first (:576 last(where:)), so a still-streaming paragraph's latest build is found
    // without going through the stable prettyCache.
    private static readonly List<(string Source, AttributedText Value)> streamingPretty = [];

    // :571-587 static func prettyAttributed(_ source: String, streaming: Bool = false) -> AttributedString.
    // PR-THEME-148 (via linkHosts/videoIDs/linkAssetKey, below) and InlineText's body: the streaming path keeps a
    // small ring of the last StreamingSlots builds and never touches the stable cache; the settled path promotes a
    // matching streaming entry into the stable cache instead of rebuilding.
    public static AttributedText PrettyAttributed(string source, bool streaming = false)
    {
        if (streaming)
        {
            for (var i = streamingPretty.Count - 1; i >= 0; i--) // :572 last(where:)
            {
                if (SwiftStringComparer.Instance.Equals(streamingPretty[i].Source, source)) return streamingPretty[i].Value;
            }
            var built = Build(source); // :573
            if (streamingPretty.Count == StreamingSlots) streamingPretty.RemoveAt(0); // :574 removeFirst()
            streamingPretty.Add((source, built)); // :575
            return built;
        }
        if (prettyCache.TryGetValue(source, out var hit)) return hit; // :578
        AttributedText value;
        var slot = -1;
        for (var i = streamingPretty.Count - 1; i >= 0; i--) // :580 lastIndex(where:)
        {
            if (SwiftStringComparer.Instance.Equals(streamingPretty[i].Source, source)) { slot = i; break; }
        }
        if (slot >= 0)
        {
            value = streamingPretty[slot].Value; // :581 remove(at:).value
            streamingPretty.RemoveAt(slot);
        }
        else
        {
            value = Build(source); // :583
        }
        prettyCache.Insert(value, source, MarkdownView.CacheCost(source, MarkdownView.PrettyCacheCostLimit)); // :585
        return value;
    }

    // :591-594 @MainActor static func warm(_ source: String, with pretty: AttributedString). PR-THEME-124: only
    // the units this part does not cover (part 2's InlineText/prettyAttributed) call this, but it is declared
    // here since it lives beside prettyCache and MarkdownView.Warm (PR-THEME-124, above) calls it directly.
    public static void Warm(string source, AttributedText pretty)
    {
        if (!prettyCache.Contains(source)) prettyCache.Insert(pretty, source, MarkdownView.CacheCost(source, MarkdownView.PrettyCacheCostLimit));
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
                            // AttributedString(markdown:) reads a link's text as inline content (CommonMark): a code
                            // span or emphasis inside it keeps its style, and every piece carries the link.
                            foreach (var piece in ParseInline(label).Runs) runs.Add(piece with { Link = url });
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

    // :623-634 @MainActor static func linkHosts(for source: String, streaming: Bool = false) -> [String].
    // PR-THEME-148: first-seen order, lower-cased, each host once.
    public static ValueList<string> LinkHosts(string @for, bool streaming = false)
    {
        var pretty = PrettyAttributed(@for, streaming); // :625
        var hosts = new List<string>();
        var seen = new HashSet<string>(SwiftStringComparer.Instance);
        foreach (var run in pretty.Runs)
        {
            if (run.Link?.Host is { Length: > 0 } host)
            {
                host = SwiftString.Lowercased(host); // :630
                if (seen.Add(host)) hosts.Add(host); // :630-631
            }
        }
        return new ValueList<string>(hosts);
    }

    // :637-647 @MainActor static func videoIDs(for source: String, streaming: Bool = false) -> [String].
    // PR-THEME-148: first-seen order, each id once.
    public static ValueList<string> VideoIDs(string @for, bool streaming = false)
    {
        var pretty = PrettyAttributed(@for, streaming); // :639
        var ids = new List<string>();
        var seen = new HashSet<string>(SwiftStringComparer.Instance);
        foreach (var run in pretty.Runs)
        {
            if (run.Link is not { } link) continue;
            if (LinkTitles.VideoID(link) is not { } id) continue; // :644
            if (seen.Add(id)) ids.Add(id); // :644-645
        }
        return new ValueList<string>(ids);
    }

    // :651-657 @MainActor static func linkAssetKey(for source: String, streaming: Bool = false) -> String.
    // PR-THEME-148: hosts and video ids joined, a vertical bar between; empty when there is neither.
    public static string LinkAssetKey(string @for, bool streaming = false)
    {
        var hosts = LinkHosts(@for, streaming); // :653
        var videos = VideoIDs(@for, streaming); // :654
        if (hosts.Count == 0 && videos.Count == 0) return ""; // :655
        return string.Join(",", hosts) + "|" + string.Join(",", videos); // :656
    }

    // :660-664 @MainActor static func invalidate(_ source: String). PR-THEME-148.
    public static void Invalidate(string source)
    {
        prettyCache.RemoveValue(source, out _); // :662
        streamingPretty.RemoveAll(entry => SwiftStringComparer.Instance.Equals(entry.Source, source)); // :663
    }

    // :670-689 static func prettyTitle(for url: URL) -> String. PR-THEME-132: a strict cascade -- forge reference,
    // then a cached video title, then host plus last path segment.
    public static string PrettyTitle(SwiftURL @for)
    {
        var url = @for;
        if (ForgeReference(url) is { } reference) return reference; // :671
        if (LinkTitles.Cached(url) is { } title) return Capped(title, 48); // :672
        var host = url.Host;
        if (string.IsNullOrEmpty(host)) return CompactFallback(url.AbsoluteString); // :673-674
        host = SwiftString.Lowercased(host);
        if (SwiftString.HasPrefix(host, "www.")) host = host[4..]; // :676
        // :677-680 (url.path.removingPercentEncoding ?? url.path): SwiftURL.Path (F.URL) already returns the
        // percent-decoded path, matching Swift's own url.path, so a further decode here would be redundant.
        var segments = url.Path.Split('/').Where(s => s.Length > 0).ToList(); // :677-681
        if (segments.Count == 0) return host; // :682
        var last = segments[^1];
        if (NamelessSegments.Contains(SwiftString.Lowercased(last))) return host; // :683
        var tail = SwiftString.Count(last) > 24
            ? SwiftString.Prefix(last, 21) + "…"
            : last; // :684
        var built = segments.Count == 1 ? host + "/" + tail : host + "/…/" + tail; // :685
        if (SwiftString.Count(built) > 40) // :686
        {
            return SwiftString.Prefix(built, 19) + "…" + SwiftString.Suffix(built, 18); // :687
        }
        return built; // :688
    }

    // :692-694 private static let namelessSegments: Set<String>
    private static ValueSet<string> NamelessSegments { get; } = new(
    [
        "watch", "view", "index", "index.html", "index.php", "home", "default", "page",
    ], SwiftStringComparer.Instance); // R-VAL-2: matched against URL text

    // :697-700 private static func capped(_ title: String, at limit: Int) -> String. PR-THEME-132.
    private static string Capped(string title, int at)
    {
        if (SwiftString.Count(title) <= at) return title; // :698
        return SwiftString.Prefix(title, at - 3) + "…"; // :699
    }

    // :705-723 static func forgeReference(for url: URL) -> String?. PR-THEME-133: a GitLab "-" segment followed by
    // merge_requests/issues and a number, else a GitHub-style pull/issues segment followed by a number.
    public static string? ForgeReference(SwiftURL @for)
    {
        var segments = @for.Path.Split('/').Where(s => s.Length > 0).ToList(); // :706
        var dash = segments.IndexOf("-"); // :709
        if (dash >= 0 && dash + 2 < segments.Count && SwiftNumber.ParseInt(segments[dash + 2]) is { } gitlabId) // :709-710
        {
            switch (segments[dash + 1]) // :711
            {
                case "merge_requests": return "!" + gitlabId; // :712
                case "issues": return "#" + gitlabId; // :713
            }
        }
        if (segments.Count >= 2 && SwiftNumber.ParseInt(segments[^1]) is { } id) // :719
        {
            var kind = segments[^2]; // :720
            if (kind == "pull" || kind == "issues") return "#" + id; // :721
        }
        return null; // :723
    }

    // :727-730 static func compactFallback(_ absolute: String) -> String. PR-THEME-132.
    public static string CompactFallback(string absolute)
    {
        if (SwiftString.Count(absolute) <= 40) return absolute; // :728
        return SwiftString.Prefix(absolute, 19) + "…" + SwiftString.Suffix(absolute, 18); // :729
    }

    // :732-735 @MainActor static func containsLinks(in source: String, streaming: Bool = false) -> Bool
    public static bool ContainsLinks(string @in, bool streaming = false) =>
        PrettyAttributed(@in, streaming).Runs.Any(run => run.Link is not null); // :733-734

    // :737-748 static func isBareDisplay(_ display: String, url: URL) -> Bool. PR-THEME-131: bare when the display
    // text, after trimming whitespace, one pair of angle brackets and ten trailing punctuation characters, equals
    // the URL itself, or starts with an http(s) scheme with no whitespace.
    public static bool IsBareDisplay(string display, SwiftURL url)
    {
        var d = FoundationText.Trim(display, includeNewlines: true); // :738 display.trimmed == whitespacesAndNewlines
        if (SwiftString.HasPrefix(d, "<") && SwiftString.HasSuffix(d, ">") && SwiftString.Count(d) >= 2) // :739
        {
            d = d[1..^1]; // :740 dropFirst().dropLast()
        }
        while (d.Length > 0 && ".,;:!?)]}>".IndexOf(d[^1], StringComparison.Ordinal) >= 0) // :742
        {
            d = d[..^1]; // :743 removeLast()
        }
        if (d == url.AbsoluteString) return true; // :744
        if ((SwiftString.HasPrefix(d, "http://") || SwiftString.HasPrefix(d, "https://")) // :745
            && !d.Contains(' ', StringComparison.Ordinal) && !d.Contains('\n', StringComparison.Ordinal))
        {
            return true; // :746
        }
        return false; // :747
    }

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

/// <summary>:854-863 <c>struct MarkdownBlockSelection: ViewModifier</c>: <c>.textSelection(.enabled)</c> only while
/// the environment allows blocks their own selection.</summary>
public sealed record MarkdownBlockSelection
{
    // :856-862 func body(content: Content) -> some View. PR-THEME-126: the environment's value is always true
    // (dead_code.md entry 30: markdownBlockSelection has no writer, so the else branch -- .textSelection(.disabled)
    // -- is dead and, per ED-061, is not implemented). Avalonia has no per-subtree "enable text selection"
    // modifier the way SwiftUI's `.textSelection` is, so this is the identity transform and the selection is the
    // content's own: VeiledText (which InlineText's paragraphs and headings, the mention paragraphs and the code
    // lines draw through) and the table's cells draw their words in a SelectableTextBlock painted with the
    // system's selection colour. That is proven by dragging and copying in VeiledTextSelectionTests; before
    // the defects-priority review this comment said the content "renders its own selectable text" while it drew a
    // plain TextBlock, which cannot be selected.
    public Control Body(Control content) => content;
}

/// <summary>PORT-ONLY (PR-THEME-126). The selection highlight of the selectable text a markdown view draws (VeiledText, a table cell, a code block's caption): the system's
/// selection colour, as ParagraphTextPresenter paints it (SelectableTextBlock's own brush is null with no Fluent theme loaded). It follows the system accent, which needs a running
/// application, so it is made when the text is on screen, the only time a selection is seen (a view built outside an application, in a unit test of the control tree, has none).</summary>
internal static class MarkdownSelection
{
    public static T Painted<T>(T text) where T : SelectableTextBlock
    {
        text.AttachedToVisualTree += (_, _) => text.SelectionBrush ??= SwiftColor.SelectedTextBackgroundColor.Brush(text);
        return text;
    }
}

/// <summary>:901-1099 <c>@MainActor enum FaviconCache</c>.</summary>
public static class FaviconCache
{
    // :903 private static var memory: [String: NSImage] = [:]. PR-THEME-139: flushed wholesale at 300 entries.
    private static readonly Dictionary<string, IImage> memory = new(SwiftStringComparer.Instance);

    // :911-912: hosts already known to have no icon, and one shared in-flight task per host. PR-THEME-137.
    private static readonly HashSet<string> failed = new(SwiftStringComparer.Instance);
    private static readonly Dictionary<string, Task<IImage?>> inFlight = new(SwiftStringComparer.Instance);

    // :905-907 static func cached(host: String) -> NSImage?
    public static IImage? Cached(string host) =>
        memory.TryGetValue(SwiftString.Lowercased(host), out var hit) ? hit : null; // :906

    // :914-939 static func image(for host: String) async -> NSImage?. PR-THEME-137/139.
    public static async Task<IImage?> Image(string @for)
    {
        var key = SwiftString.Lowercased(@for); // :915
        if (memory.TryGetValue(key, out var hit)) return hit; // :916
        if (failed.Contains(key)) return null; // :917
        if (!IsPublic(key)) // :919
        {
            failed.Add(key); // :920
            return null;
        }
        if (inFlight.TryGetValue(key, out var running)) return await running; // :923-924
        var task = FetchAndPrepare(key); // :925-929
        inFlight[key] = task;
        var image = await task; // :930
        inFlight.Remove(key); // :931
        Remember(key, image); // :932-937
        return image; // :938
    }

    // :932-937 the store step of image(for:), a method of its own so the 300-entry flush can be driven without a
    // fetch (PR-THEME-139, upstream defect 49): at 300 entries the next icon discards every cached icon and becomes
    // the only entry; a host with no icon is recorded as failed, and nothing ever clears that set.
    private static void Remember(string key, IImage? image)
    {
        if (image is not null) // :932
        {
            if (memory.Count >= 300) memory.Clear(); // :933 removeAll(keepingCapacity:)
            memory[key] = image; // :934
        }
        else
        {
            failed.Add(key); // :936
        }
    }

    private static async Task<IImage?> FetchAndPrepare(string key)
    {
        var data = await IconData(key); // :926
        if (data is null) return null;
        var clean = Cleaned(data); // :926
        return clean is null ? null : ResizedIcon(clean); // :927
    }

    // :945-953 private static func iconData(for host: String) async -> Data?. ED-168: the host's own
    // /favicon.ico, over URLSession.Shared, 5 second timeout, accepted only on 2xx with a decodable image body.
    private static async Task<byte[]?> IconData(string @for)
    {
        if (!Uri.TryCreate("https://" + @for + "/favicon.ico", UriKind.Absolute, out var uri)) return null; // :946
        var request = new URLRequest(uri, timeoutInterval: 5); // :947-948
        byte[] data;
        URLResponse response;
        try
        {
            (data, response) = await URLSession.Shared.Data(request); // :949
        }
        catch
        {
            return null; // :949 try?
        }
        if (response is not HTTPURLResponse http || http.StatusCode is < 200 or > 299) return null; // :950
        if (!TryDecode(data, out _)) return null; // :951 NSImage(data:) != nil
        return data; // :952
    }

    // :955-975 private static func cleaned(_ data: Data) -> NSImage?. PR-THEME-138: downscale to at most 64 on
    // the long side, then flood-fill away a white background plate.
    private static IImage? Cleaned(byte[] data)
    {
        if (!TryDecode(data, out var image) || image.PixelSize.Width <= 0 || image.PixelSize.Height <= 0) return null; // :956
        double sourceWidth = image.PixelSize.Width, sourceHeight = image.PixelSize.Height;
        var long_ = Math.Max(sourceWidth, sourceHeight); // :957
        var scale = long_ > 64 ? 64 / long_ : 1; // :958
        var width = Math.Max(1, (int)Math.Round(sourceWidth * scale, MidpointRounding.AwayFromZero)); // :959
        var height = Math.Max(1, (int)Math.Round(sourceHeight * scale, MidpointRounding.AwayFromZero)); // :960
        var rep = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96),
            PixelFormat.Bgra8888, AlphaFormat.Unpremul); // :962-964 samplesPerPixel 4, hasAlpha true
        DrawScaled(image, rep, width, height); // :965-971 high-interpolation draw into the bitmap
        RemoveWhitePlate(rep); // :973
        return rep;
    }

    /// <summary>Draws <paramref name="source"/> into <paramref name="target"/> at <paramref name="width"/> by
    /// <paramref name="height"/>, replacing whatever the target held (:965-971 <c>image.draw(in:from:...)</c>).</summary>
    private static void DrawScaled(Bitmap source, WriteableBitmap target, int width, int height)
    {
        using var rtb = new RenderTargetBitmap(new PixelSize(width, height), new Vector(96, 96));
        using (var ctx = rtb.CreateDrawingContext())
        {
            ctx.DrawImage(source, new Rect(0, 0, source.PixelSize.Width, source.PixelSize.Height),
                new Rect(0, 0, width, height));
        }
        using var targetLock = target.Lock();
        rtb.CopyPixels(new PixelRect(0, 0, width, height), targetLock.Address, targetLock.RowBytes * height, targetLock.RowBytes);
    }

    private static bool TryDecode(byte[] data, out Bitmap image)
    {
        try
        {
            using var stream = new MemoryStream(data);
            image = new Bitmap(stream);
            return true;
        }
        catch
        {
            image = null!;
            return false;
        }
    }

    // :977-1048 private static func removeWhitePlate(in rep: NSBitmapImageRep). PR-THEME-138: flood-fills a
    // white background plate connected to any border to full transparency, and un-premultiplies the one-pixel
    // fringe next to it against white.
    private static void RemoveWhitePlate(WriteableBitmap rep)
    {
        var width = rep.PixelSize.Width;
        var height = rep.PixelSize.Height;
        if (width <= 0 || height <= 0) return; // :980
        using var buffer = rep.Lock();
        var stride = buffer.RowBytes;
        var pixels = new byte[stride * height];
        System.Runtime.InteropServices.Marshal.Copy(buffer.Address, pixels, 0, pixels.Length);
        (int R, int G, int B, int A) At(int x, int y)
        {
            var i = y * stride + x * 4; // Bgra8888
            return (pixels[i + 2], pixels[i + 1], pixels[i], pixels[i + 3]); // :981-984 (r, g, b, a) 0-255
        }
        void Set(int x, int y, byte r, byte g, byte b, byte a)
        {
            var i = y * stride + x * 4;
            pixels[i] = b; pixels[i + 1] = g; pixels[i + 2] = r; pixels[i + 3] = a;
        }
        bool IsWhite((int R, int G, int B, int A) p) // :986-990
        {
            if (p.A <= 200) return false;
            var lo = Math.Min(p.R, Math.Min(p.G, p.B));
            var hi = Math.Max(p.R, Math.Max(p.G, p.B));
            return lo >= 236 && hi - lo <= 20;
        }
        var plate = new bool[width * height]; // :992
        var stack = new Stack<int>(); // :993
        for (var x = 0; x < width; x++) // :994-1000
        {
            foreach (var y in new[] { 0, height - 1 })
            {
                var idx = y * width + x;
                if (!plate[idx] && IsWhite(At(x, y))) { plate[idx] = true; stack.Push(idx); }
            }
        }
        for (var y = 0; y < height; y++) // :1002-1008
        {
            foreach (var x in new[] { 0, width - 1 })
            {
                var idx = y * width + x;
                if (!plate[idx] && IsWhite(At(x, y))) { plate[idx] = true; stack.Push(idx); }
            }
        }
        while (stack.Count > 0) // :1010-1019
        {
            var idx = stack.Pop();
            var x = idx % width;
            var y = idx / width;
            foreach (var (nx, ny) in new[] { (x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1) })
            {
                if (nx < 0 || nx >= width || ny < 0 || ny >= height) continue;
                var nidx = ny * width + nx;
                if (plate[nidx]) continue;
                if (!IsWhite(At(nx, ny))) continue;
                plate[nidx] = true;
                stack.Push(nidx);
            }
        }
        for (var y = 0; y < height; y++) // :1021-1047
        {
            for (var x = 0; x < width; x++)
            {
                var (r, g, b, _) = At(x, y);
                var lo = Math.Min(r, Math.Min(g, b));
                if (plate[y * width + x]) // :1030-1032
                {
                    Set(x, y, (byte)r, (byte)g, (byte)b, 0);
                    continue;
                }
                var hasPlateNeighbour = // :1034-1037
                    (x + 1 < width && plate[y * width + x + 1]) ||
                    (x - 1 >= 0 && plate[y * width + x - 1]) ||
                    (y + 1 < height && plate[(y + 1) * width + x]) ||
                    (y - 1 >= 0 && plate[(y - 1) * width + x]);
                if (!hasPlateNeighbour || lo < 160) continue; // :1038
                var alpha = 1 - lo / 255.0; // :1039
                if (alpha <= 0.02) // :1040-1042
                {
                    Set(x, y, (byte)r, (byte)g, (byte)b, 0);
                    continue;
                }
                double Unpremult(double v) => Math.Min(1, Math.Max(0, (v / 255 - (1 - alpha)) / alpha)); // :1044-1046
                Set(x, y,
                    (byte)Math.Round(Unpremult(r) * 255, MidpointRounding.AwayFromZero),
                    (byte)Math.Round(Unpremult(g) * 255, MidpointRounding.AwayFromZero),
                    (byte)Math.Round(Unpremult(b) * 255, MidpointRounding.AwayFromZero),
                    (byte)Math.Round(alpha * 255, MidpointRounding.AwayFromZero)); // :1047
            }
        }
        System.Runtime.InteropServices.Marshal.Copy(pixels, 0, buffer.Address, pixels.Length);
    }

    // :1051-1054 private static let privateSuffixes
    private static ValueList<string> PrivateSuffixes { get; } = new(
    [
        ".local", ".localhost", ".localdomain", ".internal", ".intranet",
        ".lan", ".home", ".home.arpa", ".test", ".invalid", ".corp",
    ]);

    // :1059-1082 static func isPublic(host: String) -> Bool. PR-THEME-137: rejects empty/localhost, an IPv6
    // literal, a private suffix, a host with no dot, and the four private/CGNAT IPv4 ranges.
    public static bool IsPublic(string host)
    {
        if (host.Length == 0 || host == "localhost") return false; // :1060
        if (host.Contains(':', StringComparison.Ordinal)) return false; // :1063
        foreach (var suffix in PrivateSuffixes) // :1064
        {
            if (SwiftString.HasSuffix(host, suffix)) return false;
        }
        if (!host.Contains('.', StringComparison.Ordinal)) return false; // :1067
        var labels = host.Split('.'); // :1068 omittingEmptySubsequences: false
        var octets = new List<int>();
        foreach (var label in labels) // :1070-1073
        {
            if (SwiftNumber.ParseInt(label) is not { } value || value < 0 || value > 255) break;
            octets.Add((int)value);
        }
        if (octets.Count != 4 || octets.Count != labels.Length) return true; // :1074
        return (octets[0], octets[1]) switch // :1075-1080
        {
            (0, _) or (10, _) or (127, _) => false,
            (169, 254) or (192, 168) => false,
            (172, >= 16 and <= 31) => false,
            (100, >= 64 and <= 127) => false, // Carrier-grade NAT.
            _ => true,
        };
    }

    // :1084-1098 private static func resizedIcon(_ image: NSImage) -> NSImage. PR-THEME-138: rendered at 28 by
    // 28 pixels, declared at 14 by 14 points -- a two-times bitmap. Avalonia has no separate "declared size"; the
    // 28-pixel bitmap with DPI 192 (2x of 96) reports itself as 14 by 14 device-independent units, matching.
    private static IImage ResizedIcon(IImage image)
    {
        if (image is not Bitmap source) return image; // :1085 guard let rep = NSBitmapImageRep(...) else { return image }
        var rep = new WriteableBitmap(new PixelSize(28, 28), new Vector(192, 192),
            PixelFormat.Bgra8888, AlphaFormat.Unpremul); // :1086-1088
        DrawScaled(source, rep, 28, 28); // :1089-1095
        return rep; // :1096-1098 (declared size 14x14 == 28px / (192/96))
    }
}

/// <summary>:1101-1243 <c>struct InlineText: View</c>: one paragraph of inline markdown, as a link text view when it
/// has links and as veiled Text otherwise.</summary>
public sealed class InlineText : Decorator
{
    /// <summary>The App hook for <c>@Environment(\.openURL)</c> (:1110): set once by the App layer at start-up,
    /// the same pattern as MarkdownDelegationCard.Render (layering, see the file header). There is no
    /// port-wide openURL environment yet; this unit adds the one call site MarkdownView.swift needs (:1148).</summary>
    public static Action<SwiftURL>? OpenUrl { get; set; }

    /// <param name="source">:1102 <c>let source: String</c>.</param>
    /// <param name="hasLinks">:1104 <c>let hasLinks: Bool?</c>.</param>
    /// <param name="hugsContent">:1106 <c>var hugsContent = false</c>.</param>
    public string Source { get; }
    public bool? HasLinks { get; }
    public bool HugsContent { get; }

    // B3 :1077 @State private var faviconRevision = 0, read by body (B3 :1092-1093, :1114; R-VIEW-2), kept as ProseRunView keeps its own.
    private sealed class ViewState : MainActorModel
    {
        private int faviconRevision;

        public int FaviconRevision { get => Read(faviconRevision); set => Set(ref faviconRevision, value); }
    }

    private readonly ViewState state = new();

    // :1121-1125 init(_:hasLinks:hugsContent:); :1127-1208 body. PR-THEME-144, PR-THEME-148, PR-THEME-151.
    // The hasLinkText branch (:1134-1191) builds LinkParagraphView, whose whole constructor is part of the
    // sibling LinkParagraphView unit and stays MARROW_STUB (see the file header's dependency-cycle note); this
    // wiring is otherwise complete and does not stand in for it. The link-free branch (:1193-1198) similarly
    // depends on the sibling StreamVeil unit's VeiledText(AttributedString) constructor.
    public InlineText(string source, bool? hasLinks = null, bool hugsContent = false, MarkdownVeilScope? veils = null)
    {
        Source = source;
        HasLinks = hasLinks;
        HugsContent = hugsContent;
        this.veils = veils;
        Body();
        // body reads faviconRevision (B3 :1092-1093, :1114): a bump runs it again, and SwiftUI hands the paragraph its new revision
        // (LinkParagraphView.updateNSView, LinkParagraphView.swift:36-58), which draws the text again with the icon.
        this.Track(() => state.FaviconRevision, _ => Body());
    }

    // The inputs body last ran with, and what it built from them. The link branch's paragraph is a representable: SwiftUI keeps it while
    // the branch stays (the same view in the same place) and hands it its new values (updateNSView, LinkParagraphView.swift:36-58).
    private bool hasBody;
    private double lastPointSize;
    private bool lastDimmed;
    private bool lastStreaming;
    private int lastRevision;
    private ValueList<HydraPersona> lastPersonas = ValueList<HydraPersona>.Empty;
    private ValueMap<string, HydraMentionTarget> lastTargets = ValueMap<string, HydraMentionTarget>.Empty;
    private LinkParagraphView? linkView;
    private string? linkHostKey;
    private bool builtVeiledStreaming;
    // PORT-ONLY (UI-THEME-047): the place in the row's veils this paragraph's text takes its veil from (MarkdownVeils), for every run of body.
    private readonly MarkdownVeilScope? veils;

    // B3 :1071-1075 @Environment(\.markdownPointSize), (\.markdownDimmed), (\.markdownStreaming), (\.hydraMentionPersonas) and
    // (\.hydraMentionTargets): SwiftUI runs body again whenever one of them changes. A port host often sets one on a control it has
    // already built (HydraReportPopover's markdownPointSize 12, TimelineRows.swift:631; the narration's 12 and dimmed, :1551-1552;
    // MarkdownView's streaming flag on its last block, MarkdownView.swift:89-91), and an inherited value reaches this view only once
    // its ancestors hold it, so a change of any of them runs body again here (review round 5 of TimelineRows: a report paragraph with a
    // link was drawn at the 13-point default instead of the report's 12).
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (!hasBody) return;
        if (change.Property == MarkdownView.MarkdownPointSizeProperty || change.Property == MarkdownView.MarkdownDimmedProperty
            || change.Property == MarkdownView.MarkdownStreamingProperty || change.Property == MarkdownView.HydraMentionPersonasProperty
            || change.Property == MarkdownView.HydraMentionTargetsProperty)
            Body();
    }

    // :1127-1208 body (B3 :1091-1136).
    private void Body()
    {
        var source = Source;
        var hugsContent = HugsContent;
        var pointSize = MarkdownView.GetMarkdownPointSize(this); // :1108 (B3 :1071)
        var dimmed = MarkdownView.GetMarkdownDimmed(this); // :1109 (B3 :1072)
        var streaming = MarkdownView.GetMarkdownStreaming(this); // :1110 (B3 :1073)
        var personas = MarkdownView.GetHydraMentionPersonas(this); // :1111 (B3 :1074)
        var targets = MarkdownView.GetHydraMentionTargets(this); // :1112 (B3 :1075)
        var revision = state.FaviconRevision; // B3 :1092-1093 `let _ = faviconRevision`
        if (hasBody && pointSize == lastPointSize && dimmed == lastDimmed && streaming == lastStreaming && revision == lastRevision
            && personas.Equals(lastPersonas) && targets.Equals(lastTargets))
            return; // nothing body reads has changed
        hasBody = true;
        lastPointSize = pointSize;
        lastDimmed = dimmed;
        lastStreaming = streaming;
        lastRevision = revision;
        lastPersonas = personas;
        lastTargets = targets;

        var scaled = SwiftNumber.Rounded(pointSize * 2) / 2; // :1130 (B3 :1094)
        // B3 :1095-1096: local file links have no host, but still need the native link opener, so a hint-less
        // call site (a heading, or the user's own message bubble) checks ContainsLinks itself rather than
        // whether any host was found (PR-THEME-186).
        var hasLinkText = HasLinks ?? RichLink.ContainsLinks(source, streaming);
        Control child;
        if (hasLinkText) // :1134-1191
        {
            var metrics = Metrics(scaled); // :1141 the baseline numbers (PR-THEME-151), measured once per size
            _ = metrics; // consumed by the alignment-guide wiring once LinkParagraphView exists (:1156-1157)
            // :1189-1191 .task(id: hostKey): stable hosts and video ids key the work (PR-THEME-148), so a
            // streamed token with no new link leaves it alone.
            var hostKey = RichLink.LinkAssetKey(source, streaming);
            // The same paragraph while the branch and the task's id stay: SwiftUI keeps the representable (its NSTextView, its selection)
            // and hands updateNSView the new values, the mentions and targets among them (B3 :1114; LinkParagraphView.swift:36-58); the
            // hover reads the mentions the environment holds when the pointer moves (below), as SwiftUI's modifier is handed the new
            // ones. Only a new task id (hostKey) builds the paragraph again, with its own task.
            if (linkView is { } kept && hostKey == linkHostKey && ReferenceEquals(Child, kept))
            {
                kept.Update(scaled, dimmed, streaming, revision, personas, targets); // B3 :1114
                return;
            }
            LinkTextView? hostTextView = null;
            var paragraph = new LinkParagraphView( // :1148
                source,
                pointSize: scaled,
                dimmed: dimmed,
                streaming: streaming,
                revision: revision, // B3 :1114 revision: faviconRevision
                mentions: personas,
                targets: targets,
                onOpenHead: url => MarkdownView.OpenUrl(this, url), // :1148 { url in openURL(url) }
                onHost: view => hostTextView = view, // :1148 { [weak linkBox] view in linkBox?.value = view }
                veils: veils); // PORT-ONLY (UI-THEME-047)
            paragraph.HorizontalAlignment = hugsContent ? HorizontalAlignment.Left : HorizontalAlignment.Stretch; // :1149
            // :1159-1188 the continuous hover: pushes/pops the pointing hand over a link, shows/hides the head
            // hover card over a mention, only on change (PR-THEME-144). B3 :1159-1160 moves this into the shared
            // UI.Markdown.LinkHover (N9), also used identically by ProseRunView.swift:27-59.
            LinkHover.Attach(paragraph, () => hostTextView, () => MarkdownView.GetHydraMentionPersonas(this), () => MarkdownView.GetHydraMentionTargets(this));
            if (hostKey.Length > 0)
            {
                // B3 :1123-1125 `[source, streaming, revision = $faviconRevision]`: the task bumps the view's own state (:1163).
                var viewTask = new ViewTask<string>(hostKey, async _ =>
                    await FetchLinkAssets(source, streaming, new Binding<int>(() => state.FaviconRevision, v => state.FaviconRevision = v)));
                paragraph.AttachedToVisualTree += (_, _) => viewTask.Appear();
                paragraph.DetachedFromVisualTree += (_, _) => viewTask.Disappear();
            }
            linkView = paragraph;
            linkHostKey = hostKey;
            child = paragraph;
        }
        else // :1193-1198
        {
            // The veiled text reads only the streaming flag of these (:1203); another change leaves it as it is.
            if (linkView is null && Child is not null && streaming == builtVeiledStreaming) return;
            var veiled = new VeiledText(RichLink.PrettyAttributed(source, streaming), veils); // :1203
            var selected = new MarkdownBlockSelection().Body(veiled); // :1204 .modifier(MarkdownBlockSelection())
            selected.HorizontalAlignment = hugsContent ? HorizontalAlignment.Left : HorizontalAlignment.Stretch; // :1206
            linkView = null;
            linkHostKey = null;
            builtVeiledStreaming = streaming;
            child = selected;
        }
        Child = child;
    }

    // :1214-1221 @MainActor private static func metrics(pointSize: CGFloat) -> (ascender: CGFloat, lineHeight: CGFloat).
    // PR-THEME-151: the base font's baseline numbers, measured once per point size and kept.
    private static readonly Dictionary<double, (double Ascender, double LineHeight)> metricsBySize = new(); // :1212

    private static (double Ascender, double LineHeight) Metrics(double pointSize)
    {
        if (metricsBySize.TryGetValue(pointSize, out var known)) return known; // :1216
        var font = SwiftFont.System(pointSize).Resolve(); // :1217 NSFont.systemFont(ofSize:)
        var typeface = new Avalonia.Media.Typeface(font.FontFamily, font.FontStyle, font.FontWeight);
        // NSFont's ascender/descender/leading have no direct Avalonia accessor; IGlyphTypeface's design-unit
        // metrics, scaled to the point size, are the closest equivalent (ENGINEERING DECISION, visual parity
        // UNVERIFIED under OD-0001 like SwiftImage.cs's own cap-height measurement above).
        double ascender, lineHeight;
        if (Avalonia.Media.FontManager.Current.TryGetGlyphTypeface(typeface, out var glyph))
        {
            var scale = pointSize / glyph.Metrics.DesignEmHeight;
            var rawAscender = -glyph.Metrics.Ascent * scale; // Ascent is stored negative (above the baseline)
            var descender = -glyph.Metrics.Descent * scale; // Descent is stored positive (below the baseline)
            var leading = glyph.Metrics.LineGap * scale;
            ascender = Math.Ceiling(rawAscender); // :1218 ceil(font.ascender)
            lineHeight = Math.Ceiling(rawAscender - descender + leading); // :1218 ceil(ascender - descender + leading)
        }
        else
        {
            ascender = Math.Ceiling(pointSize);
            lineHeight = Math.Ceiling(pointSize * 1.2);
        }
        var measured = (ascender, lineHeight);
        metricsBySize[pointSize] = measured; // :1219
        return measured;
    }

    // :1226-1237 static func fetchLinkAssets(source: String, streaming: Bool, revision: Binding<Int>) async. PR-THEME-148.
    public static async Task FetchLinkAssets(string source, bool streaming, Binding<int> revision)
    {
        var hosts = await MainActor.Run(() => RichLink.LinkHosts(source, streaming)); // :1227
        var changed = false;
        foreach (var host in hosts) // :1228-1230
        {
            if (FaviconCache.Cached(host) is not null) continue;
            if (await FaviconCache.Image(host) is not null) changed = true;
        }
        var videos = await MainActor.Run(() => RichLink.VideoIDs(source, streaming)); // :1232
        foreach (var id in videos) // :1233-1235
        {
            if (await LinkTitles.TitleForVideo(id) is not null) changed = true;
        }
        if (changed) // :1236-1237
        {
            await MainActor.Run(() =>
            {
                RichLink.Invalidate(source);
                revision.Value += 1;
            });
        }
    }

    // :1240-1242 static func attributed(_ source: String, streaming: Bool = false) -> AttributedString.
    public static AttributedText Attributed(string source, bool streaming = false) =>
        RichLink.PrettyAttributed(source, streaming); // :1241
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
    public CodeBlockCopyButton(string text, BlockHover hover)
    {
        var button = new CopyButton(text); // :1262
        Child = button;
        this.Track(() => hover.IsHovering, isHovering => Opacity = isHovering ? 1 : 0); // :1263 .opacity(hover.isHovering ? 1 : 0)
    }
}

/// <summary>:1267-1336 <c>struct CodeBlock: View</c>.</summary>
public sealed class CodeBlock : Decorator
{
    private const int CollapsedLineLimit = 120; // :1276

    /// <summary>:1271 <c>@State private var showsAll = false</c>.</summary>
    private sealed class ViewState : MainActorModel
    {
        private bool showsAll;

        public bool ShowsAll { get => Read(showsAll); set => Set(ref showsAll, value); }
    }

    /// <param name="language">:1268 <c>let language: String?</c>.</param>
    /// <param name="code">:1269 <c>let code: String</c>.</param>
    // :1278-1321 body. UI-THEME-034 to 037. The code text itself renders through VeiledText(verbatim:), the
    // sibling StreamVeil unit's own MARROW_STUB (see the file header's dependency-cycle note); this wiring is
    // otherwise complete and does not stand in for it.
    public CodeBlock(string? language, string code, MarkdownVeilScope? veils = null)
    {
        var hover = new BlockHover(); // :1270 @State private var hover = BlockHover()
        var state = new ViewState();

        var stack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 0 }; // :1281
        stack.Track(() => state.ShowsAll, _ => Rebuild(stack, language, code, hover, state, veils));
        Rebuild(stack, language, code, hover, state, veils);

        var background = new Border { Child = stack }.RoundedRectangle(12, RoundedCornerStyle.Continuous); // :1317
        background.Background = SwiftColor.Quaternary.Opacity(0.45).Brush(background); // :1317
        background.PointerEntered += (_, _) => hover.IsHovering = true; // :1318 .onHover
        background.PointerExited += (_, _) => hover.IsHovering = false; // :1318
        Child = background;
    }

    // PORT-ONLY (TextSelectionEnabledProperty): the caption of the header as it is now, so a change of the inherited value (it arrives once the block has a parent) reaches it.
    private SelectableTextBlock? caption;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == MarkdownView.TextSelectionEnabledProperty && caption is { } text) SetCaptionSelectable(text, MarkdownView.GetTextSelectionEnabled(this));
    }

    // Selectable only where an ancestor enables text selection; otherwise nothing of a plain caption: no pointer, no focus, no selection left over from when it was.
    private static void SetCaptionSelectable(SelectableTextBlock text, bool enabled)
    {
        text.IsHitTestVisible = enabled;
        text.Focusable = enabled;
        if (!enabled) text.ClearSelection();
    }

    private void Rebuild(StackPanel stack, string? language, string code, BlockHover hover, ViewState state, MarkdownVeilScope? veils)
    {
        stack.Children.Clear();
        var newlines = Encoding.UTF8.GetBytes(code).Count(b => b == 0x0A); // :1281 code.utf8.count { $0 == 0x0A }
        var lineCount = 1 + newlines; // :1281
        var truncated = !state.ShowsAll && lineCount > CollapsedLineLimit; // :1282
        var visible = truncated ? Head(code, CollapsedLineLimit) : code; // :1283

        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") }; // :1286 HStack { ... Spacer() ... }
        // :1287 Text(language ?? "code"). A SelectableTextBlock that selects only where an ancestor of the markdown view enables text selection (TextSelectionEnabledProperty:
        // the licences popover's documents, SettingsView.swift:1033, whose environment reaches this Text); everywhere else it is the plain caption it is in a chat message.
        var langText = MarkdownSelection.Painted(new SelectableTextBlock { Text = language ?? "code" });
        SetCaptionSelectable(langText, MarkdownView.GetTextSelectionEnabled(this));
        caption = langText;
        ChatFont.Chat(SwiftFont.TextStyle.Caption).Apply(langText); // :1288
        langText.Foreground = Chrome.Chrome.SecondaryText.Brush(langText); // :1289
        Grid.SetColumn(langText, 0);
        var copyButton = new CodeBlockCopyButton(code, hover); // :1292
        Grid.SetColumn(copyButton, 2);
        header.Children.Add(langText);
        header.Children.Add(copyButton);
        header.Margin = new Thickness(12, 6, 6, 0); // :1293-1295 leading 12, trailing 6, top 6
        stack.Children.Add(header);

        var veiled = new VeiledText(visible, veils).Font(ChatFont.Chat(SwiftFont.TextStyle.Callout, design: SwiftFont.Design.Monospaced)); // :1299-1300
        var selected = new MarkdownBlockSelection().Body(veiled); // :1301
        selected.Margin = new Thickness(12, 4, 12, 12); // :1302-1304 horizontal 12, bottom 12, top 4
        var scroll = new ScrollViewer
        {
            Content = selected,
            // Swift :1225 showsIndicators: false (UI-THEME-034 "no visible indicator"): Hidden scrolls without drawing a bar.
            HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
        }; // :1298 ScrollView(.horizontal, showsIndicators: false)
        stack.Children.Add(scroll);

        if (lineCount > CollapsedLineLimit) // :1310-1316
        {
            var title = state.ShowsAll ? "Show less" : "Show all " + lineCount + " lines"; // :1311
            stack.Children.Add(TableBlock.LinkStyleButton(title, () => state.ShowsAll = !state.ShowsAll)); // :1311-1313
        }
    }

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

    // :1347 @MainActor private static var cellCache = RecentCache<String, AttributedString>(limit: 1200).
    private static readonly RecentCache<string, AttributedText> cellCache = new(1200, MarkdownView.CellCacheCostLimit); // ED-NEW-perf-memory-4: costed by source length

    // :1349-1354 @MainActor private static func cell(_ source: String) -> AttributedString. Renders the plain
    // text of each cell (AttributedText.PlainText): converting a run's bold/link attributes into a control is
    // left to InlineText's own render path (see AttributedText.cs's header), not duplicated here.
    private static AttributedText Cell(string source)
    {
        if (cellCache.TryGetValue(source, out var hit)) return hit; // :1350
        var value = InlineText.Attributed(source); // :1351
        cellCache.Insert(value, source, MarkdownView.CacheCost(source, MarkdownView.CellCacheCostLimit)); // :1352
        return value;
    }

    // :1341 @State private var showsAll = false
    private sealed class ViewState : MainActorModel
    {
        private bool showsAll;

        public bool ShowsAll { get => Read(showsAll); set => Set(ref showsAll, value); }
    }

    /// <param name="header">:1339 <c>let header: [String]</c>.</param>
    /// <param name="rows">:1340 <c>let rows: [[String]]</c>.</param>
    // :1357-1396 body. PR-THEME-119: a horizontally scrolling grid, semibold header, 30 rows at a time, on a
    // rounded quaternary panel.
    public TableBlock(IReadOnlyList<string> header, IReadOnlyList<IReadOnlyList<string>> rows)
    {
        var state = new ViewState();
        var host = new StackPanel { Orientation = Orientation.Vertical, Spacing = 0 }; // :1357
        host.Track(() => state.ShowsAll, showsAll => Rebuild(host, header, rows, showsAll, state));
        Rebuild(host, header, rows, false, state);
        var panel = new Border { Child = host }.RoundedRectangle(12, RoundedCornerStyle.Continuous); // :1394 .background(.quaternary.opacity(0.35), in: .rect(cornerRadius: 12))
        panel.Background = SwiftColor.Quaternary.Opacity(0.35).Brush(panel);
        Child = panel;
    }

    private static void Rebuild(StackPanel host, IReadOnlyList<string> header, IReadOnlyList<IReadOnlyList<string>> rows, bool showsAll, ViewState state)
    {
        host.Children.Clear();
        var visible = showsAll ? rows : rows.Take(CollapsedRowLimit).ToList(); // :1358
        var head = header.Select(Cell).ToList(); // :1359
        var cells = visible.Select(r => r.Select(Cell).ToList()).ToList(); // :1360

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions(string.Join(",", head.Select(_ => "Auto"))) }; // :1363 Grid(alignment: .leading, horizontalSpacing: 18, verticalSpacing: 8)
        for (var row = 0; row <= cells.Count; row++) grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        for (var column = 0; column < head.Count; column++) // :1364-1368 GridRow (header)
        {
            // :1308 .modifier(MarkdownBlockSelection()) on the grid: its live branch is .textSelection(.enabled) (:854-863, :858), so
            // each cell's text is selectable in its own block and shows the I-beam (PR-CHAT-166, PR-THEME-126).
            var text = VeiledText.SelectableText(wraps: false);
            text.Text = head[column].PlainText;
            text.FontWeight = FontWeight.SemiBold;
            text.Margin = new Thickness(0, 0, column == head.Count - 1 ? 0 : 18, cells.Count > 0 ? 8 : 0); // :1292 verticalSpacing 8 sits only between rows
            Grid.SetRow(text, 0);
            Grid.SetColumn(text, column);
            grid.Children.Add(text);
        }
        for (var row = 0; row < cells.Count; row++) // :1370-1376 GridRow (body)
        {
            for (var column = 0; column < head.Count; column++)
            {
                var value = column < cells[row].Count ? cells[row][column].PlainText : ""; // :1373
                var text = VeiledText.SelectableText(wraps: false); // :1308, as the header's cells above
                text.Text = value;
                text.Margin = new Thickness(0, 0, column == head.Count - 1 ? 0 : 18, row == cells.Count - 1 ? 0 : 8);
                text.Foreground = SwiftColor.LabelColor.Opacity(0.9).Brush(text); // :1374 .foregroundStyle(.primary.opacity(0.9))
                Grid.SetRow(text, row + 1);
                Grid.SetColumn(text, column);
                grid.Children.Add(text);
            }
        }
        // Swift :1289 ScrollView(.horizontal, showsIndicators: false) (UI-THEME-038 "no visible indicator"): Hidden scrolls without drawing a bar.
        var scroll = new ScrollViewer { Content = grid, Padding = new Thickness(12), HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled }; // :1361-1362, :1381
        host.Children.Add(scroll);
        if (!showsAll && rows.Count > CollapsedRowLimit) // :1383-1392
        {
            var button = LinkStyleButton("Show all " + rows.Count + " rows", () => state.ShowsAll = true); // :1384-1386
            host.Children.Add(button);
        }
    }

    // Shared with CopyButton/CodeBlock's "Show all"/"Show less" links (:1305-1311, :1387-1391): a caption-sized,
    // accent-coloured plain-text button, the same ENGINEERING DECISION AttachmentPreview.LinkButton already
    // takes for `.buttonStyle(.link)` (no `.link` style precedent exists; UNVERIFIED under OD-0001 for underline
    // and hover state).
    internal static Button LinkStyleButton(string title, Action action)
    {
        var label = new TextBlock { Text = title };
        ChatFont.Chat(SwiftFont.TextStyle.Caption).Apply(label); // :1387-1389 .buttonStyle(.link).font(.chat(.caption))
        label.Foreground = Chrome.Chrome.SecondaryText.Brush(label); // :1390 .foregroundStyle(Chrome.secondaryText)
        var button = PlainControls.Button(label, action);
        button.Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand);
        button.Margin = new Thickness(12, 0, 12, 10); // :1391 .padding(.horizontal, 12).padding(.bottom, 10)
        return button;
    }
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
    // :1475 @State private var didCopy = false
    private sealed class ViewState : MainActorModel
    {
        private bool didCopy;

        public bool DidCopy { get => Read(didCopy); set => Set(ref didCopy, value); }
    }

    /// <param name="text">:1472 <c>let text: String</c>.</param>
    /// <param name="label">:1473 <c>var label = "Copy"</c>.</param>
    // :1477-1497 body. PR-THEME-120: copies the reflowed text, shows a check mark for 1.4 seconds, and carries
    // the tooltip "Copy" (the default label).
    public CopyButton(string text, string label = "Copy")
    {
        var state = new ViewState();
        Button button = null!;
        button = PlainControls.Button(new Panel(), () => // :1477-1490 (its content is the glyph host below)
        {
            var clipboard = TopLevel.GetTopLevel(button)?.Clipboard; // NSPasteboard has no Avalonia port type; TopLevel.Clipboard is the platform's own equivalent (precedent: ComposerTextView.cs).
            _ = clipboard?.SetTextAsync(ProseReflow.Reflowed(text)); // :1478-1479
            state.DidCopy = true; // :1480
            MainActor.Spawn(async () => // :1482-1486 Task { ... }
            {
                await SwiftTask.Sleep(TimeSpan.FromSeconds(1.4)); // :1483
                state.DidCopy = false; // :1484
            });
        }); // :1494 .buttonStyle(.borderless): no distinct borderless template exists yet (ENGINEERING DECISION,
            // same as ChromeIconButton's .plain), so this reuses PlainControls.Button's content-only template.
        // :1488-1489 didCopy ? "checkmark" : "doc.on.doc" with .contentTransition(.symbolEffect(.replace)): the two replace
        // each other in two steps (SymbolReplace) inside one 22-point host.
        Control shown = Icon(false); // :1488 doc.on.doc before any copy
        var host = new Panel { Width = 22, Height = 22, Children = { shown } };
        bool? shownCopied = false;
        button.Content = host;
        button.Track(() => state.DidCopy, didCopy =>
        {
            if (shownCopied == didCopy) return;
            var next = Icon(didCopy);
            var previous = shown;
            host.Children.Add(next);
            host.Children.Remove(previous);
            host.Children.Add(previous); // over the new one while it goes
            SymbolReplace.Play(previous, next, () => host.Children.Remove(previous));
            shown = next;
            shownCopied = didCopy;
        });
        TextElement.SetForeground(button, Chrome.Chrome.SecondaryText.Brush(button)); // :1495 .foregroundStyle(Chrome.secondaryText)
        ToolTip.SetTip(button, label); // :1496 .help(label)
        Child = button;
    }

    private static Control Icon(bool didCopy)
    {
        var image = SwiftImage.SystemName(didCopy ? "checkmark" : "doc.on.doc"); // :1488
        image.Width = 22; // :1489 .frame(width: 22, height: 22).contentShape(.rect)
        image.Height = 22;
        return image;
    }
}
