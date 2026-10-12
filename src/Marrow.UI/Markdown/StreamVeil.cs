using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Marrow.Core.Port;
using Marrow.UI.Common;
using Marrow.UI.Port;

// Source: DroppyCode/UI/Markdown/StreamVeil.swift (B2 1fbf81e), :1-431.

namespace Marrow.UI.Markdown;

/// <summary>DroppyCode/UI/Markdown/StreamVeil.swift:25-140.</summary>
public sealed class StreamVeil
{
    /// <summary>DroppyCode/UI/Markdown/StreamVeil.swift:27-31.</summary>
    public sealed record Span
    {
        public required (int LowerBound, int UpperBound) Range { get; init; } // StreamVeil.swift:28
        public required double Alpha { get; init; } // StreamVeil.swift:30
    }

    /// <summary>DroppyCode/UI/Markdown/StreamVeil.swift:33-38.</summary>
    private sealed record Chunk
    {
        public required (int LowerBound, int UpperBound) Range { get; init; } // StreamVeil.swift:34
        public required DateTimeOffset Started { get; init; } // StreamVeil.swift:35
        public required double Duration { get; init; } // StreamVeil.swift:37
    }

    public const double ShortestFade = 0.12; // StreamVeil.swift:40
    public const double LongestFade = 0.40; // StreamVeil.swift:41
    private const double Curve = 1.6; // StreamVeil.swift:42
    private const double CadenceSeed = 0.16; // StreamVeil.swift:43
    private const double GapCeiling = 1.0; // StreamVeil.swift:44

    private IReadOnlyList<string>? previous; // StreamVeil.swift:47
    // ENGINEERING DECISION (ED-NEW-lean-12): the string `previous` was cut from. Every veil-clock tick feeds the paragraph's text again
    // (VeiledText's redraw, :273-279); a text equal to it unit for unit has the same Characters, so the tick skips cutting and comparing them.
    private string? previousText;
    private List<Chunk> chunks = []; // StreamVeil.swift:48
    private double cadence = CadenceSeed; // StreamVeil.swift:49
    private DateTimeOffset? lastAppend; // StreamVeil.swift:50

    // StreamVeil.swift:55-85.
    public void Advance(string text, DateTimeOffset at)
    {
        if (previous is not null && string.Equals(text, previousText, StringComparison.Ordinal))
        {
            Prune(at); // :56 the same characters: nothing to register
            return;
        }

        var characters = SwiftString.Characters(text).ToArray();
        if (previous is null)
        {
            previous = characters;
            previousText = text;
            return;
        }

        if (!EqualCharacters(characters, previous))
        {
            var prefix = CommonPrefix(previous, characters);
            chunks = chunks
                .Select(chunk =>
                {
                    var upper = Math.Min(chunk.Range.UpperBound, prefix);
                    return upper > chunk.Range.LowerBound
                        ? chunk with { Range = (chunk.Range.LowerBound, upper) }
                        : null;
                })
                .Where(chunk => chunk is not null)
                .Select(chunk => chunk!)
                .ToList();

            if (characters.Length > prefix)
            {
                if (lastAppend is { } previousAppend)
                {
                    var gap = Math.Min(SwiftDate.TimeIntervalSince(at, previousAppend), GapCeiling);
                    cadence = cadence * 0.7 + gap * 0.3;
                }

                lastAppend = at;
                var duration = Math.Min(Math.Max(cadence * 3, ShortestFade), LongestFade);
                chunks.Add(new Chunk
                {
                    Range = (prefix, characters.Length),
                    Started = at,
                    Duration = duration,
                });
            }

            previous = characters;
            previousText = text;
        }

        Prune(at);
    }

    // StreamVeil.swift:88-95.
    public ValueList<Span> Spans(DateTimeOffset at)
    {
        Prune(at);
        var boost = Boost(chunks.Count);
        var spans = new List<Span>(chunks.Count);
        foreach (var chunk in chunks)
        {
            var progress = Math.Min(1, Math.Max(
                0,
                SwiftDate.TimeIntervalSince(at, chunk.Started) * boost / chunk.Duration));
            spans.Add(new Span
            {
                Range = chunk.Range,
                Alpha = 1 - Math.Pow(1 - progress, Curve),
            });
        }

        return new ValueList<Span>(spans);
    }

    // StreamVeil.swift:97-100.
    public bool IsFading(DateTimeOffset at)
    {
        Prune(at);
        return chunks.Count > 0;
    }

    // StreamVeil.swift:102-107.
    public bool Step(Func<string> text, bool active, DateTimeOffset at)
    {
        if (active) Advance(text(), at);
        return IsFading(at);
    }

    // StreamVeil.swift:110-113.
    public DateTimeOffset? SettlesAt
    {
        get
        {
            var boost = Boost(chunks.Count);
            DateTimeOffset? latest = null;
            foreach (var chunk in chunks)
            {
                var settles = SwiftDate.AddingTimeInterval(chunk.Started, chunk.Duration / boost);
                if (latest is null || settles > latest.Value) latest = settles;
            }

            return latest;
        }
    }

    /// <summary>PORT-ONLY (UI-THEME-047, observed by the tests). Whether the veil keeps nothing: no copy of the text it was fed, no chunk in flight.
    /// StreamVeil.swift:291-295 "a settled paragraph keeps no copy of itself".</summary>
    public bool IsEmpty => previous is null && chunks.Count == 0;

    // StreamVeil.swift:117-122.
    public void Reset()
    {
        previous = null;
        previousText = null;
        chunks = [];
        lastAppend = null;
        cadence = CadenceSeed;
    }

    // StreamVeil.swift:124-127.
    private void Prune(DateTimeOffset at)
    {
        var boost = Boost(chunks.Count);
        chunks.RemoveAll(chunk =>
            SwiftDate.TimeIntervalSince(at, chunk.Started) * boost >= chunk.Duration);
    }

    // StreamVeil.swift:130-132.
    private static double Boost(int active) =>
        1 + 0.3 * Math.Max(0, active - 2);

    // StreamVeil.swift:134-139.
    private static int CommonPrefix(IReadOnlyList<string> a, IReadOnlyList<string> b)
    {
        var limit = Math.Min(a.Count, b.Count);
        var index = 0;
        while (index < limit && SwiftString.Equal(a[index], b[index])) index++;
        return index;
    }

    // StreamVeil.swift:56,61.
    private static bool EqualCharacters(IReadOnlyList<string> a, IReadOnlyList<string> b)
    {
        if (a.Count != b.Count) return false;
        for (var index = 0; index < a.Count; index++)
        {
            if (!SwiftString.Equal(a[index], b[index])) return false;
        }

        return true;
    }

    // StreamVeil.swift:149-154.
    public static readonly AttachedProperty<bool> MarkdownVeiledProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>(
            "MarkdownVeiled", typeof(StreamVeil), MarkdownVeiledKey.DefaultValue, inherits: true);

    // StreamVeil.swift:151.
    public static bool GetMarkdownVeiled(Control control) => control.GetValue(MarkdownVeiledProperty);

    // StreamVeil.swift:152.
    public static void SetMarkdownVeiled(Control control, bool value) =>
        control.SetValue(MarkdownVeiledProperty, value);
}

/// <summary>DroppyCode/UI/Markdown/StreamVeil.swift:145-147.</summary>
file static class MarkdownVeiledKey
{
    public const bool DefaultValue = false; // StreamVeil.swift:146
}

/// <summary>DroppyCode/UI/Markdown/StreamVeil.swift:161-194.</summary>
public sealed class VeilClock : MainActorModel
{
    private static readonly VeilClock shared = new(); // StreamVeil.swift:164
    private DateTimeOffset now = SwiftDate.Now; // StreamVeil.swift:166
    private DisplayLink? link; // StreamVeil.swift:167
    private DateTimeOffset? deadline; // StreamVeil.swift:168

    // StreamVeil.swift:164.
    public static VeilClock Shared => shared;

    // StreamVeil.swift:166.
    public DateTimeOffset Now => Read(now);

    // StreamVeil.swift:171-181.
    public void Run(DateTimeOffset until)
    {
        if (deadline is null || until > deadline.Value) deadline = until;
        if (link is not null) return;

        var displayLink = FrameClock.DisplayLink(Tick);
        displayLink.PreferredFrameRateRange = new FrameRateRange(24, 60, 30);
        displayLink.Add();
        link = displayLink;
    }

    // StreamVeil.swift:183-193.
    private void Tick()
    {
        if (ScrollActivity.Shared.Current.IsScrolling) return;

        // `now` is the observable property (:166 `private(set) var now`): Set takes the CALLER's name by default, which here is Tick, so without
        // the property named the paragraphs that read Now (VeiledText's Track, :287) were never told it moved and no frame after the first was drawn.
        Set(ref now, SwiftDate.Now, property: nameof(Now));
        if (deadline is not { } end || now < end) return;

        link?.Invalidate();
        link = null;
        deadline = null;
    }
}

/// <summary>
/// PORT-ONLY. ENGINEERING DECISION (proposed ED; UI-THEME-047). SwiftUI keeps <c>@State private var veil</c> of every VeiledText
/// (StreamVeil.swift:245) and <c>let streamVeil</c> of every link paragraph's text view (LinkParagraphView.swift:290) through all the
/// evaluations of a streaming reply's body: a view's identity is its place in the tree (<c>ForEach(..., id: \.offset)</c>,
/// MarkdownView.swift:87), so each flush re-evaluates the body and the SAME veil sees the longer text and registers what was appended
/// (LinkParagraphView.swift:42, :55-57 feeds it after every render). The port builds a message's whole markdown tree again for every
/// flush (AssistantMessageRow, Narration, the thinking steps), so every VeiledText and LinkTextView was born new, adopted its text as the
/// baseline and could never see an append: the fade never ran in the app. A MarkdownVeils is the identity such a row keeps for its tree: one
/// StreamVeil per VeiledText place and one LinkTextVeil per link paragraph place, named by the builders (<see cref="MarkdownVeilScope"/>), alive
/// as long as the row is on screen. When the row leaves the screen <see cref="Reset"/> forgets them all, as SwiftUI drops the state of a view
/// that leaves the hierarchy: what is on screen when the row comes back is adopted as the baseline again (StreamVeil.swift:18-19). When the
/// reply stops streaming MarkdownView draws prose runs, a different view, and <see cref="Release"/> drops the places of the views that went; words still
/// dissolving at that moment (or arriving with the completion) first finish in the veiled views (OD-0076, MarkdownView.CloseWhenSettled).
/// </summary>
public sealed class MarkdownVeils
{
    private sealed class TextPlace
    {
        public StreamVeil Veil { get; } = new();
        public VeiledText? Owner { get; set; }
    }

    private readonly Dictionary<string, TextPlace> text = new(StringComparer.Ordinal);
    private readonly Dictionary<string, LinkTextVeil> links = new(StringComparer.Ordinal);

    // PORT-ONLY (UI-THEME-047, OD-0076). The block places of the markdown views this keeps identity for, and when each arrived: null for a block that was on screen
    // when its tree first attached (the baseline, StreamVeil.swift:18-19), else the time a flush opened it. SwiftUI inserts a block that a flush opens
    // (MarkdownView.swift:86-97, `ForEach(..., id: \.offset)` with `.transition(softAppear)`), and the insertion runs on through the flushes after it; the port builds the
    // whole tree again for every flush, so the arrival is kept here and each new tree continues it (MarkdownView.VeiledBlocks).
    private readonly Dictionary<string, DateTimeOffset?> arrivals = new(StringComparer.Ordinal);
    // The paths of the markdown views whose tree has been built before: a block that appears under one of them is an insertion, under any other the baseline.
    private readonly HashSet<string> builtTrees = new(StringComparer.Ordinal);

    /// <summary>The scope of the whole tree; the builders extend it place by place.</summary>
    public MarkdownVeilScope Root => new(this, "");

    // The newest VeiledText of a place owns its veil: the one a flush replaced must feed it nothing more.
    internal StreamVeil ClaimText(string place, VeiledText owner)
    {
        if (!text.TryGetValue(place, out var known)) text[place] = known = new TextPlace();
        known.Owner = owner;
        return known.Veil;
    }

    internal bool OwnsText(string place, VeiledText owner) => text.TryGetValue(place, out var known) && ReferenceEquals(known.Owner, owner);

    internal LinkTextVeil Link(string place)
    {
        if (!links.TryGetValue(place, out var known)) links[place] = known = new LinkTextVeil().Retain();
        return known;
    }

    /// <summary>The row is leaving the screen: forgets every text and chunk, stops the paint clocks.</summary>
    public void Reset()
    {
        foreach (var place in text.Values) place.Veil.Reset();
        foreach (var veil in links.Values) veil.Reset();
        arrivals.Clear(); // what is on screen when the row comes back is the baseline again: nothing arrives
        builtTrees.Clear();
    }

    /// <summary>
    /// The reply drawn under <paramref name="path"/> has stopped streaming: MarkdownView draws prose runs now (MarkdownView.swift:69-83), a different view, so the
    /// VeiledText that kept these veils has left the hierarchy and its <c>@State</c> with it, "so a settled paragraph keeps no copy of itself"
    /// (StreamVeil.swift:291-295). Every place at or under the path forgets its text and chunks and is dropped with the owner view it held; the
    /// places of the other thinking steps, whose names this one's may begin with ("s1", "s10"), stay as they are.
    /// </summary>
    internal void Release(string path)
    {
        foreach (var place in text.Keys.Where(place => IsUnder(place, path)).ToList())
        {
            text[place].Veil.Reset();
            text.Remove(place);
        }

        foreach (var place in links.Keys.Where(place => IsUnder(place, path)).ToList())
        {
            links[place].Reset();
            links.Remove(place);
        }

        foreach (var place in arrivals.Keys.Where(place => IsUnder(place, path)).ToList()) arrivals.Remove(place);
        builtTrees.RemoveWhere(tree => IsUnder(tree, path));
    }

    // The root's path is "", the child named n of a place p is p + "/" + n: a place is under a path when it is that path or goes on with a "/".
    private static bool IsUnder(string place, string path) =>
        path.Length == 0 || place == path || place.StartsWith(path + "/", StringComparison.Ordinal);

    /// <summary>PORT-ONLY (UI-THEME-047, OD-0076). Whether a place at or under <paramref name="path"/> keeps the text it was fed or a chunk in flight:
    /// the reply drawn there was streaming on screen, so the end of its stream may still carry words in flight or a last batch.</summary>
    internal bool HoldsTextUnder(string path) =>
        text.Any(pair => IsUnder(pair.Key, path) && !pair.Value.Veil.IsEmpty) || HoldsLinkTextUnder(path);

    /// <summary>PORT-ONLY (OD-0076). Whether a link paragraph's place at or under <paramref name="path"/> keeps its text: its view feeds the veil only
    /// once it is in the tree (LinkTextView.OnAttachedToVisualTree), so a batch that ends the stream there is known only after that.</summary>
    internal bool HoldsLinkTextUnder(string path) => links.Any(pair => IsUnder(pair.Key, path) && !pair.Value.IsEmpty);

    /// <summary>PORT-ONLY (OD-0076). When the last chunk still dissolving at <paramref name="at"/> at or under <paramref name="path"/> settles, or null when
    /// nothing there dissolves any more (StreamVeil.swift:97-100 isFading, :110-113 settlesAt).</summary>
    internal DateTimeOffset? FadingUntil(string path, DateTimeOffset at)
    {
        DateTimeOffset? latest = null;
        void Consider(DateTimeOffset? settles)
        {
            if (settles is { } value && (latest is null || value > latest.Value)) latest = value;
        }

        foreach (var (place, known) in text)
        {
            if (IsUnder(place, path) && known.Veil.IsFading(at)) Consider(known.Veil.SettlesAt);
        }

        foreach (var (place, veil) in links)
        {
            if (IsUnder(place, path) && veil.IsFading(at)) Consider(veil.SettlesAt);
        }

        // A block that arrived by its insertion is still on its way in until the soft appear has run (Chrome.swift:958-961).
        foreach (var (place, arrived) in arrivals)
        {
            if (IsUnder(place, path) && arrived is { } started && started + ArrivalLength > at) Consider(started + ArrivalLength);
        }

        return latest;
    }

    /// <summary>PORT-ONLY (UI-THEME-047). How long a block takes to arrive: the soft appear every block comes in with (MarkdownView.swift:92, Chrome.swift:949-961).</summary>
    internal static TimeSpan ArrivalLength { get; } = TimeSpan.FromSeconds(Animation.SoftAppear.Duration);

    /// <summary>PORT-ONLY (UI-THEME-047). Whether the markdown view at <paramref name="path"/> has had its tree built before: its next new block is an insertion.</summary>
    internal bool TreeKnown(string path) => builtTrees.Contains(path);

    /// <summary>
    /// PORT-ONLY (UI-THEME-047). Whether the block at <paramref name="place"/> arrives now, and how far into its arrival it is: null when it is not arriving (it was
    /// on screen when its tree attached, or it has arrived already), else the time since a flush opened it (zero the first time it is asked, so the soft appear
    /// starts from nothing, and later the same arrival carried on by the next tree that is built for the next flush).
    /// </summary>
    internal TimeSpan? Arrival(string place, bool treeKnown)
    {
        if (arrivals.TryGetValue(place, out var started))
        {
            if (started is not { } at) return null;
            var elapsed = SwiftDate.Now - at;
            return elapsed < ArrivalLength ? elapsed < TimeSpan.Zero ? TimeSpan.Zero : elapsed : null;
        }

        arrivals[place] = treeKnown ? SwiftDate.Now : null;
        return treeKnown ? TimeSpan.Zero : null;
    }

    /// <summary>
    /// PORT-ONLY (UI-THEME-047). The tree of the markdown view at <paramref name="path"/> was built with <paramref name="blocks"/> blocks: it is known from now on, and
    /// a block that is gone (the parser re-deriving the reply shorter) arrives afresh if it comes back, as a view that left a ForEach and returned would.
    /// </summary>
    internal void TreeBuilt(string path, int blocks)
    {
        builtTrees.Add(path);
        var prefix = path + "/b";
        foreach (var place in arrivals.Keys.Where(place => place.StartsWith(prefix, StringComparison.Ordinal)).ToList())
        {
            if (int.TryParse(place.AsSpan(prefix.Length), out var index) && index >= blocks) arrivals.Remove(place);
        }
    }

    /// <summary>PORT-ONLY, observed by the tests: how many places hold a veil (each with its owner view).</summary>
    public int Places => text.Count + links.Count;

    /// <summary>PORT-ONLY, observed by the tests: how many block places hold a record of their arrival, and how many trees are known to have been built before.</summary>
    public int Arrivals => arrivals.Count + builtTrees.Count;

    /// <summary>PORT-ONLY, observed by the tests: whether any place still keeps a copy of its text or a chunk in flight.</summary>
    public bool HoldsText => text.Values.Any(place => !place.Veil.IsEmpty) || links.Values.Any(veil => !veil.IsEmpty);
}

/// <summary>PORT-ONLY. A place in a <see cref="MarkdownVeils"/>: the path of block, item and quote indices from the markdown view down to it.</summary>
public sealed class MarkdownVeilScope
{
    private readonly MarkdownVeils store;
    private readonly string path;

    internal MarkdownVeilScope(MarkdownVeils store, string path)
    {
        this.store = store;
        this.path = path;
    }

    /// <summary>The place named <paramref name="name"/> under this one.</summary>
    public MarkdownVeilScope Child(string name) => new(store, path + "/" + name);

    internal StreamVeil ClaimText(VeiledText owner) => store.ClaimText(path, owner);

    internal bool OwnsText(VeiledText owner) => store.OwnsText(path, owner);

    /// <summary>The reply under this place has stopped streaming and its views have left: see <see cref="MarkdownVeils.Release"/>.</summary>
    internal void Release() => store.Release(path);

    /// <summary>See <see cref="MarkdownVeils.HoldsTextUnder(string)"/>.</summary>
    internal bool HoldsText => store.HoldsTextUnder(path);

    /// <summary>See <see cref="MarkdownVeils.HoldsLinkTextUnder(string)"/>.</summary>
    internal bool HoldsLinkText => store.HoldsLinkTextUnder(path);

    /// <summary>See <see cref="MarkdownVeils.FadingUntil(string, DateTimeOffset)"/>.</summary>
    internal DateTimeOffset? FadingUntil(DateTimeOffset at) => store.FadingUntil(path, at);

    /// <summary>See <see cref="MarkdownVeils.TreeKnown(string)"/>.</summary>
    internal bool TreeKnown => store.TreeKnown(path);

    /// <summary>See <see cref="MarkdownVeils.Arrival(string, bool)"/>.</summary>
    internal TimeSpan? Arrival(bool treeKnown) => store.Arrival(path, treeKnown);

    /// <summary>See <see cref="MarkdownVeils.TreeBuilt(string, int)"/>.</summary>
    internal void TreeBuilt(int blocks) => store.TreeBuilt(path, blocks);

    internal LinkTextVeil Link() => store.Link(path);
}

/// <summary>DroppyCode/UI/Markdown/StreamVeil.swift:200-339.</summary>
public sealed class VeiledText : Decorator
{
    /// <summary>DroppyCode/UI/Markdown/StreamVeil.swift:203-227.</summary>
    private abstract record Source
    {
        private Source() { }

        public sealed record Plain(string String) : Source; // StreamVeil.swift:205
        public sealed record Attributed(AttributedText Value) : Source; // StreamVeil.swift:204

        // StreamVeil.swift:207-212.
        public string Characters => this switch
        {
            Plain plain => plain.String,
            Attributed attributed => attributed.Value.PlainText,
            _ => throw new UnreachableException(),
        };

        // StreamVeil.swift:221-226.
        public StyledText Text => this switch
        {
            Plain plain => new StyledText(plain.String),
            Attributed attributed => Styled(
                attributed.Value,
                attributed.Value.PlainText,
                ValueList<StreamVeil.Span>.Empty,
                SwiftColor.Primary),
            _ => throw new UnreachableException(),
        };
    }

    /// <summary>DroppyCode/UI/Markdown/StreamVeil.swift:230-233.</summary>
    public abstract record Segment
    {
        private Segment() { }

        public sealed record Fixed(StyledText Text) : Segment; // StreamVeil.swift:232
        public sealed record Prose(AttributedText Value) : Segment; // StreamVeil.swift:231
    }

    // StreamVeil.swift:245 @State private var veil = StreamVeil(). In SwiftUI it lives as long as the view's place in the tree, through every
    // evaluation of the body; here a new VeiledText is built for every flush of a streaming reply, so a row that rebuilds its markdown passes
    // the place's veil in (MarkdownVeils, UI-THEME-047). Without one, the veil is this control's own.
    private readonly StreamVeil veil;
    private readonly MarkdownVeilScope? veils;
    private readonly IReadOnlyList<Segment>? segments; // StreamVeil.swift:237
    private readonly Source source; // StreamVeil.swift:229
    // StreamVeil.swift:245. A SwiftUI Text wraps at the width it is offered (no lineLimit anywhere in :200-339), and an
    // Avalonia TextBlock does not unless asked: without Wrap a long message ran on one line and clipped at the bubble's
    // edge (the user bubble, MarkdownView.swift:1129-1133, and every link-free inline paragraph). The code block hosts
    // this in a horizontal ScrollView with fixedSize (:1223-1229), where the width offered is unbounded, so it stays on its lines.
    // MarkdownView.swift:318, :1131, :1229: every VeiledText (a prose block, a link-free inline paragraph such as the user's
    // bubble, a code block's text) sits under MarkdownBlockSelection, whose live branch is .textSelection(.enabled)
    // (B3 :854-863, the live branch :858; the environment value has no writer, dead_code.md 30; the licences popover's documents
    // also :1033): its text is selectable in its own block, so it is drawn by a SelectableTextBlock (an Avalonia TextBlock cannot be
    // selected or copied), with the system's selected-text colour as ParagraphTextPresenter uses (PR-CHAT-166, PR-THEME-126,
    // UI-UPDATE-032, UI-SETTINGS-060).
    // The I-beam a selectable text shows (a SelectableTextBlock has no cursor of its own with no control theme loaded):
    // the reference video shows it over selectable timeline text, 2-chat-use.mov 14.74 s (PR-CHAT-166 windows_path).
    private static readonly Avalonia.Input.Cursor TextCursor = new(Avalonia.Input.StandardCursorType.Ibeam);

    /// <summary>The text of a block that sits under MarkdownBlockSelection (MarkdownView.swift:854-863 at B3, live branch
    /// <c>.textSelection(.enabled)</c> at :858), or of any other text the source marks <c>.textSelection(.enabled)</c>: a
    /// SelectableTextBlock with the I-beam over it and the system's selected-text colour, the one recipe the veiled text, a
    /// table's cells (MarkdownView.swift:1287-1308) and the timeline's own selectable lines (TimelineRows.swift:1900, :1922,
    /// :1982, :1989, :2000, :2124, :2152, :2394) share (PR-CHAT-166; ED-256 (5)).</summary>
    public static SelectableTextBlock SelectableText(bool wraps) => SelectableText(new SelectableTextBlock(), wraps);

    /// <summary>The same recipe on a text block the caller made: a SelectableTextBlock subclass that lays its text out its own way
    /// (TimelineRows' tool detail, whose line limit also cuts at a hard line break; ED-256 (7)).</summary>
    public static T SelectableText<T>(T block, bool wraps) where T : SelectableTextBlock
    {
        block.Cursor = TextCursor;
        if (wraps) block.TextWrapping = Avalonia.Media.TextWrapping.Wrap;
        // The selection colour is the one MarkdownSelection paints, resolved once the text is in a window (the system accent needs the running application).
        return MarkdownSelection.Painted(block);
    }

    private readonly SelectableTextBlock textBlock = SelectableText(wraps: true);
    private readonly bool hasSegments;
    private bool isRedrawing;
    private bool wasFading;
    // ENGINEERING DECISION (ED-NEW-lean-12): a VeiledText's text never changes (a flush builds a new one), so its Characters are read and cut
    // once: `characters` is the source's text (an attributed source builds its plain text anew on every read), `pieces` keeps where each
    // Character starts and the pieces of the text no chunk touches, so a veil-clock tick rebuilds only the runs of the tail still dissolving.
    private readonly string characters;
    private readonly Func<string> readCharacters;
    private VeilPieces? pieces;
    private readonly StyledText.Painted shown = new(); // what the last redraw put in the text block, so a tick only recolours (StyledText.Render)

    // StreamVeil.swift:241.
    public ValueList<int> ProseEnds { get; } = ValueList<int>.Empty;

    // StreamVeil.swift:253-265,273-279.
    public VeiledText(IReadOnlyList<Segment> segments, MarkdownVeilScope? veils = null)
    {
        this.segments = segments;
        hasSegments = true;
        source = new Source.Attributed(JoinProse(segments, out var ends));
        characters = source.Characters;
        readCharacters = () => characters;
        ProseEnds = new ValueList<int>(ends);
        this.veils = veils;
        veil = veils?.ClaimText(this) ?? new StreamVeil();
        Build();
    }

    // StreamVeil.swift:247-251.
    public VeiledText(AttributedText attributed, MarkdownVeilScope? veils = null) : this([new Segment.Prose(attributed)], veils)
    {
    }

    // StreamVeil.swift:267-271,273-279.
    public VeiledText(string verbatim, MarkdownVeilScope? veils = null)
    {
        source = new Source.Plain(verbatim);
        characters = source.Characters;
        readCharacters = () => characters;
        this.veils = veils;
        veil = veils?.ClaimText(this) ?? new StreamVeil();
        Build();
    }

    // StreamVeil.swift:273-279.
    private void Build()
    {
        Child = textBlock;
        PropertyChanged += OnEnvironmentChanged;
        this.Track(
            () => VeilClock.Shared.Now,
            _ =>
            {
                if (wasFading) Redraw();
            });
        Redraw(environmentKnown: false);
    }

    // StreamVeil.swift:243-244.
    private void OnEnvironmentChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == StreamVeil.MarkdownVeiledProperty)
            Redraw();
        else if (e.Property == MarkdownView.MarkdownDimmedProperty)
            // PORT-ONLY (UI-THEME-047): a change of \.markdownDimmed says nothing about whether the reply is still arriving, so it only repaints:
            // it neither feeds the veil nor makes it forget. (1) A block that is dimmed on its own (a quote's blocks, MarkdownView.swift:364) gets
            // the value while it is still outside the markdown view that sets \.markdownVeiled (:84, :100), so the veiled value read then is the
            // default; taking it for the reply's answer made the settled branch forget a kept veil's text before every flush, and a quoted paragraph
            // never faded. (2) A view the thinking steps replace is told it is no longer dimmed as it leaves its panel; feeding the veil its old text
            // then re-adopted that text after the veil was forgotten for a row that had left the screen, so what arrived while it was away dissolved
            // instead of being the baseline.
            Redraw(environmentKnown: false);
    }

    // StreamVeil.swift:273-279. `environmentKnown` is PORT-ONLY: the first draw runs in the constructor, before this control has a parent, so
    // \.markdownVeiled still reads its default (false) and says nothing about the reply; that draw only paints (a veil kept from the last flush
    // may still be dissolving) and never feeds the veil or forgets what it holds. The environment arrives with the property change that follows.
    private void Redraw(bool environmentKnown = true)
    {
        if (isRedrawing) return;
        // A later flush built a newer VeiledText for this place and took the veil over; this one is on its way out of the tree and feeds it nothing.
        if (veils is not null && !veils.OwnsText(this)) return;
        isRedrawing = true;
        try
        {
            var at = SwiftDate.Now;
            var veiled = environmentKnown && StreamVeil.GetMarkdownVeiled(this);
            var fading = veil.Step(readCharacters, veiled, at);
            wasFading = fading;
            StyledText result;
            if (fading)
            {
                if (veil.SettlesAt is { } settlesAt) VeilClock.Shared.Run(until: settlesAt);
                result = Text(at);
            }
            else
            {
                if (environmentKnown && !veiled) veil.Reset();
                result = SettledText();
            }

            result.Render(textBlock, shown);
        }
        finally
        {
            isRedrawing = false;
        }
    }

    // StreamVeil.swift:284-289.
    private StyledText FadingText() => Text(SwiftDate.Now);

    // StreamVeil.swift:294-307.
    private StyledText SettledText()
    {
        if (!hasSegments) return source.Text;

        var output = new StyledText("");
        foreach (var segment in segments!)
        {
            output = segment switch
            {
                Segment.Fixed fixedRun => output + fixedRun.Text,
                Segment.Prose prose => output + Styled(
                    prose.Value,
                    prose.Value.PlainText,
                    ValueList<StreamVeil.Span>.Empty,
                    SwiftColor.Primary),
                _ => throw new UnreachableException(),
            };
        }

        return output;
    }

    // StreamVeil.swift:309-338.
    private StyledText Text(DateTimeOffset at)
    {
        var spans = veil.Spans(at);
        var baseColor = MarkdownView.GetMarkdownDimmed(this)
            ? SwiftColor.Primary.Opacity(0.7)
            : SwiftColor.Primary;
        var cut = pieces ??= new VeilPieces(characters, source is Source.Attributed attributedSource ? attributedSource.Value : null);

        if (!hasSegments)
        {
            if (source is Source.Plain) return cut.Plain(spans, baseColor);
            if (source is Source.Attributed)
            {
                var whole = StyledText.FromRuns(cut.Styled(spans, baseColor));
                cut.Turn();
                return whole;
            }

            throw new UnreachableException();
        }

        // :326-331 the joined prose styled once, then sliced at the joined string's grapheme boundaries between the fixed runs.
        var joinedStyled = cut.Styled(spans, baseColor);
        var output = cut.Output;
        output.Clear();
        output.Add(VeilPieces.EmptyRun); // the empty Text the joined value starts from
        var cursor = 0;
        var prose = 0;
        foreach (var segment in segments!)
        {
            switch (segment)
            {
                case Segment.Fixed fixedRun:
                    output.AddRange(fixedRun.Text.Runs);
                    break;
                case Segment.Prose:
                    var end = ProseEnds[prose++];
                    cut.Slice(joinedStyled, cursor, end, output);
                    cursor = end;
                    break;
                default:
                    throw new UnreachableException();
            }
        }

        cut.Turn();
        return StyledText.FromRuns(output);
    }

    // StreamVeil.swift:253-265.
    private static AttributedText JoinProse(IReadOnlyList<Segment> segments, out List<int> ends)
    {
        ends = [];
        var runs = new List<AttributedRun>();
        var joined = "";
        foreach (var segment in segments)
        {
            if (segment is not Segment.Prose prose) continue;
            runs.AddRange(prose.Value.Runs);
            joined += prose.Value.PlainText;
            ends.Add(SwiftString.Count(joined));
        }

        return new AttributedText(runs);
    }

    // StreamVeil.swift:309-338: a whole attributed text in the veil's colours (the settled text, :294-307, with no spans).
    private static StyledText Styled(
        AttributedText attributed,
        string joinedText,
        ValueList<StreamVeil.Span> spans,
        SwiftColor baseColor) =>
        StyledText.FromRuns(new VeilPieces(joinedText, attributed).Styled(spans, baseColor));

    // StreamVeil.swift:309-338.
    private static StyledText.Run StyledRun(AttributedRun run, double? alpha, SwiftColor baseColor)
    {
        var text = new StyledText(run.Text);
        if (run.IsBold == true) text = text.FontWeight(SwiftFont.Weight.Bold);
        if (run.IsItalic == true) text = text.Italic();
        if (run.IsCode)
            text = text.Font(SwiftFont.System(SwiftFont.TextStyle.Body, design: SwiftFont.Design.Monospaced));
        if (run.Link is { } link) text = text.Link(link);

        if (alpha is { } opacity)
            text = text.ForegroundColor(baseColor.Opacity(opacity));
        else if (run.Link is not null)
            text = text.ForegroundColor(Chrome.Chrome.Accent);

        return text.Runs[0]; // a Text of one run keeps one run through its modifiers
    }

    // StreamVeil.swift:314-320.
    private static double? AlphaAt(int character, ValueList<StreamVeil.Span> spans)
    {
        foreach (var span in spans)
        {
            if (character >= span.Range.LowerBound && character < span.Range.UpperBound)
                return span.Alpha;
        }

        return null;
    }

    /// <summary>
    /// ENGINEERING DECISION (ED-NEW-lean-12). The Characters of one veiled text, cut once, and the runs StreamVeil.swift:309-338 paints them
    /// in. A Character's alpha is that of the first chunk holding it (:314-320); between two chunk bounds every Character has the same alpha,
    /// so a run is split only where a bound falls (found by binary search) instead of by asking every Character. An attributed run's own
    /// Character takes the alpha of the first Character of the whole text it overlaps (:321-325), which is the one holding its first UTF-16
    /// unit. The pieces no chunk touches do not change from tick to tick and are kept for the next one; only the dissolving tail is rebuilt.
    /// The runs come out exactly as the per-Character form built them: an empty Text first, equal neighbours merged within a run.
    /// </summary>
    private sealed class VeilPieces
    {
        public static readonly StyledText.Run EmptyRun = new("");

        private readonly string text;
        private readonly int[] starts; // where each Character of the text starts (UTF-16), and the text's length last
        private readonly AttributedText? attributed;
        private readonly int[][] runStarts = []; // per attributed run: where each of its own Characters starts in it, and its length last
        private readonly int[][] runFirst = []; // per attributed run: per own Character, the text's Character it overlaps first (int.MaxValue: none)
        private readonly List<int> bounds = [];
        private readonly List<StyledText.Run> runs = [];
        private Dictionary<(int Run, int Lower, int Upper), StyledText.Run> kept = [];
        private Dictionary<(int Run, int Lower, int Upper), StyledText.Run> used = [];

        public VeilPieces(string text, AttributedText? attributed)
        {
            this.text = text;
            starts = Starts(text);
            this.attributed = attributed;
            if (attributed is null) return;

            runStarts = new int[attributed.Runs.Count][];
            runFirst = new int[attributed.Runs.Count][];
            var runOffset = 0;
            for (var index = 0; index < attributed.Runs.Count; index++)
            {
                var run = attributed.Runs[index];
                var local = Starts(run.Text);
                var first = new int[local.Length - 1];
                for (var character = 0; character < first.Length; character++)
                {
                    var lower = runOffset + local[character];
                    first[character] = lower < text.Length ? Holding(lower) : int.MaxValue;
                }

                runStarts[index] = local;
                runFirst[index] = first;
                runOffset += run.Text.Length;
            }
        }

        /// <summary>The list the segmented text is assembled in, kept between ticks.</summary>
        public List<StyledText.Run> Output { get; } = [];

        private int Count => starts.Length - 1;

        // StreamVeil.swift:309-320: a plain string, split where the alpha changes.
        public StyledText Plain(ValueList<StreamVeil.Span> spans, SwiftColor baseColor)
        {
            Bounds(spans);
            Output.Clear();
            Output.Add(EmptyRun);
            var character = 0;
            while (character < Count)
            {
                var alpha = AlphaAt(character, spans);
                var end = Math.Min(NextBound(character), Count);
                while (end < Count && AlphaAt(end, spans) == alpha) end = Math.Min(NextBound(end), Count);
                if (alpha is { } opacity)
                {
                    Output.Add(new StyledText(Substring(text, starts, character, end)).ForegroundColor(baseColor.Opacity(opacity)).Runs[0]);
                }
                else
                {
                    var key = (-1, character, end);
                    if (!Reuse(key, out var piece)) used[key] = piece = new StyledText.Run(Substring(text, starts, character, end));
                    Output.Add(piece);
                }

                character = end;
            }

            Turn();
            return StyledText.FromRuns(Output);
        }

        // StreamVeil.swift:309-338: every attributed run, split where the alpha of the Characters it overlaps changes. The list is kept
        // between ticks (a caller that keeps the runs copies them) and the caller ends the build with Turn.
        public List<StyledText.Run> Styled(ValueList<StreamVeil.Span> spans, SwiftColor baseColor)
        {
            Bounds(spans);
            runs.Clear();
            runs.Add(EmptyRun);
            for (var index = 0; index < runFirst.Length; index++)
            {
                var run = attributed!.Runs[index];
                var first = runFirst[index];
                if (first.Length == 0)
                {
                    runs.Add(Piece(index, 0, 0, run, null, baseColor));
                    continue;
                }

                var character = 0;
                while (character < first.Length)
                {
                    var alpha = Alpha(first[character], spans);
                    var end = NextChange(first, character);
                    while (end < first.Length && Alpha(first[end], spans) == alpha) end = NextChange(first, end);
                    runs.Add(Piece(index, character, end, run, alpha, baseColor));
                    character = end;
                }
            }

            return runs;
        }

        // StreamVeil.swift:326-331: the runs of the joined value between two of its Characters, after an empty Text.
        public void Slice(List<StyledText.Run> styled, int lowerCharacter, int upperCharacter, List<StyledText.Run> into)
        {
            var lower = starts[lowerCharacter];
            var upper = starts[upperCharacter];
            into.Add(EmptyRun);
            var offset = 0;
            foreach (var run in styled)
            {
                var runEnd = offset + run.Text.Length;
                var start = Math.Max(offset, lower);
                var end = Math.Min(runEnd, upper);
                if (end > start)
                    into.Add(start == offset && end == runEnd ? run : run with { Text = run.Text.Substring(start - offset, end - start) });
                offset = runEnd;
            }
        }

        /// <summary>The build is done: the pieces it used are the ones kept for the next tick.</summary>
        public void Turn()
        {
            kept.Clear();
            (kept, used) = (used, kept);
        }

        private StyledText.Run Piece(int index, int lower, int upper, AttributedRun run, double? alpha, SwiftColor baseColor)
        {
            var local = runStarts[index];
            string Text() => lower == 0 && upper == local.Length - 1 ? run.Text : Substring(run.Text, local, lower, upper);
            if (alpha is not null) return StyledRun(run with { Text = Text() }, alpha, baseColor);

            var key = (index, lower, upper);
            if (!Reuse(key, out var piece)) used[key] = piece = StyledRun(run with { Text = Text() }, null, baseColor);
            return piece;
        }

        private bool Reuse((int, int, int) key, out StyledText.Run piece)
        {
            if (used.TryGetValue(key, out piece!)) return true;
            if (!kept.Remove(key, out piece!)) return false;
            used[key] = piece;
            return true;
        }

        private static double? Alpha(int character, ValueList<StreamVeil.Span> spans) =>
            character == int.MaxValue ? null : AlphaAt(character, spans);

        // The chunk bounds, in order: the alpha of a Character changes only where one falls.
        private void Bounds(ValueList<StreamVeil.Span> spans)
        {
            bounds.Clear();
            foreach (var span in spans)
            {
                bounds.Add(span.Range.LowerBound);
                bounds.Add(span.Range.UpperBound);
            }

            bounds.Sort();
        }

        private int NextBound(int character)
        {
            foreach (var bound in bounds)
            {
                if (bound > character) return bound;
            }

            return int.MaxValue;
        }

        // The first own Character after `character` whose text Character lies at or past the next bound (they never go down).
        private int NextChange(int[] first, int character)
        {
            if (first[character] == int.MaxValue) return first.Length;
            var bound = NextBound(first[character]);
            var lower = character + 1;
            var upper = first.Length;
            while (lower < upper)
            {
                var middle = (lower + upper) >>> 1;
                if (first[middle] >= bound) upper = middle;
                else lower = middle + 1;
            }

            return lower;
        }

        // The Character of the text that holds this UTF-16 offset.
        private int Holding(int offset)
        {
            var found = Array.BinarySearch(starts, offset);
            return found >= 0 ? found : ~found - 1;
        }

        private static string Substring(string of, int[] at, int lower, int upper) => of.Substring(at[lower], at[upper] - at[lower]);

        private static int[] Starts(string of) => CharacterStarts.Of(of);
    }
}

/// <summary>DroppyCode/UI/Markdown/StreamVeil.swift:344-431.</summary>
public sealed class LinkTextVeil
{
    private readonly StreamVeil veil = new(); // StreamVeil.swift:346
    private DisplayLink? displayLink; // StreamVeil.swift:347
    // StreamVeil.swift:350 painted: here, the pristine (un-split, un-coloured) Inlines of each TextBlock as
    // they stood before this fade's first Paint(), so every Paint() rebuilds from the same clean basis instead
    // of accumulating splits, and Clear() can restore them exactly.
    private readonly Dictionary<TextBlock, List<Inline>> painted = new();
    private WeakReference<LinkTextView>? tickTarget; // StreamVeil.swift:374
    // ENGINEERING DECISION (ED-NEW-lean-12): what the last Paint() cut and set, so the next tick does not join and cut the paragraph's text
    // again while it has not changed. `paintedText` is the text of the views as it read then and `paintedStarts` where each of its
    // Characters starts; `shownBlocks` and `shownInlines` are the text blocks and the inlines (with each run's text) that Paint() set. The
    // text is reused only while every text block still holds exactly those inlines with those texts; anything else reads it afresh.
    private string? paintedText;
    private int[] paintedStarts = [];
    private readonly List<TextBlock> shownBlocks = [];
    private readonly List<List<(Inline Inline, string? Text)>> shownInlines = [];
    private readonly List<(int Start, int End, double Alpha)> characterRanges = [];
    private readonly List<int> bounds = [];
    private readonly List<Inline> rebuilt = [];
    private Dictionary<(Run Run, int Lower, int Upper), (string Source, string Slice)> keptSlices = [];
    private Dictionary<(Run Run, int Lower, int Upper), (string Source, string Slice)> usedSlices = [];
    // PORT-ONLY (UI-THEME-047): a veil kept by a MarkdownVeils outlives the text views that draw it, since the port builds a new one for every
    // flush; see Detach and Reset.
    private bool retained;

    internal LinkTextVeil Retain()
    {
        retained = true;
        return this;
    }

    /// <summary>PORT-ONLY (observed by the tests): whether the veil keeps nothing, see <see cref="StreamVeil.IsEmpty"/>.</summary>
    internal bool IsEmpty => veil.IsEmpty;

    /// <summary>PORT-ONLY (OD-0076, MarkdownVeils.FadingUntil): StreamVeil.swift:97-100 isFading of this veil's chunks.</summary>
    internal bool IsFading(DateTimeOffset at) => veil.IsFading(at);

    /// <summary>PORT-ONLY (OD-0076, MarkdownVeils.FadingUntil): StreamVeil.swift:110-113 settlesAt of this veil's chunks.</summary>
    internal DateTimeOffset? SettlesAt => veil.SettlesAt;

    /// <summary>PORT-ONLY. Forgets the text and every chunk, stops the clock and lets go of the view: the owner of a retained veil
    /// (<see cref="MarkdownVeils.Reset"/>) calls this when its row leaves the screen.</summary>
    public void Reset()
    {
        displayLink?.Invalidate();
        displayLink = null;
        painted.Clear();
        Forget();
        tickTarget = null;
        veil.Reset();
    }

    // StreamVeil.swift:354-367.
    public void Update(LinkTextView view, bool active)
    {
        var now = SwiftDate.Now;
        Forget(); // a new render: the view's text is read afresh
        // PORT-ONLY (UI-THEME-047): a view that is leaving is told the environment it no longer has (the reply is not streaming for it) on its way
        // out; it must not take a kept veil away from the view that feeds it. Only a view that feeds the veil takes it over from another.
        if (retained && !active && tickTarget is { } held && held.TryGetTarget(out var holder) && !ReferenceEquals(holder, view)) return;
        if (veil.Step(() => TextOf(view), active, now))
        {
            Paint(view, now);
            if (displayLink is null)
            {
                displayLink = FrameClock.DisplayLink(Tick);
                displayLink.Add();
            }

            tickTarget = new WeakReference<LinkTextView>(view);
        }
        else
        {
            Settle(view, active);
        }
    }

    // StreamVeil.swift:370-372. PORT-ONLY (UI-THEME-047): the view of a retained veil leaves the tree with every flush, as the next flush's view takes
    // its place (the Child swap detaches the old tree before the new one is attached); the veil's text and chunks belong to the place and are
    // forgotten only by Reset when the row leaves the screen, so this lets go of the view alone. A view that is not the veil's current one
    // (a newer view already took it over) touches nothing.
    public void Detach(LinkTextView view)
    {
        if (!retained)
        {
            Settle(view, false);
            return;
        }

        if (tickTarget is { } target && target.TryGetTarget(out var current) && !ReferenceEquals(current, view)) return;
        displayLink?.Invalidate();
        displayLink = null;
        painted.Clear();
        Forget();
        tickTarget = null;
    }

    // StreamVeil.swift:376-392.
    private void Tick()
    {
        if (ScrollActivity.Shared.Current.IsScrolling) return;
        if (tickTarget is null || !tickTarget.TryGetTarget(out var view))
        {
            displayLink?.Invalidate();
            displayLink = null;
            painted.Clear();
            Forget();
            tickTarget = null;
            return;
        }

        var now = SwiftDate.Now;
        if (veil.IsFading(now)) Paint(view, now);
        else Settle(view, keepText: true);
    }

    // StreamVeil.swift:394-399.
    private void Settle(LinkTextView view, bool keepText)
    {
        displayLink?.Invalidate();
        displayLink = null;
        Clear(view);
        if (!keepText) veil.Reset();
        tickTarget = null;
    }

    // StreamVeil.swift:401-407.
    private void Clear(LinkTextView view)
    {
        foreach (var textBlock in TextBlocks(view))
        {
            if (!painted.TryGetValue(textBlock, out var clean)) continue;
            textBlock.Inlines!.Clear();
            foreach (var piece in clean) textBlock.Inlines!.Add(piece);
        }

        painted.Clear();
        Forget();
    }

    // StreamVeil.swift:409-430. AppKit paints by enumerating attribute runs WITHIN the veil's nsRange, so only
    // the characters actually inside a span's range are ever recoloured, whatever run they fall in the middle
    // of; a Run in this port is atomic, so matching that means splitting a Run at the span's boundary rather
    // than recolouring it whole whenever a chunk boundary and a run boundary land on different characters
    // (e.g. a chunk starting mid-run at a shared space before a link).
    private void Paint(LinkTextView view, DateTimeOffset at)
    {
        var textBlocks = TextBlocks(view).ToArray();
        if (textBlocks.Length == 0) return;
        var spans = veil.Spans(at);
        if (spans.Count == 0)
        {
            Clear(view);
            return;
        }

        if (!Unchanged(textBlocks))
        {
            paintedText = TextOf(view);
            paintedStarts = CharacterStarts.Of(paintedText);
        }

        // The Characters with an alpha as UTF-16 ranges, adjacent same-alpha Characters coalesced into one range, so a Run is split at
        // most at a real chunk boundary, never at every character of a multi-character chunk. Between two chunk bounds every Character
        // has the same alpha (:314-320), so the ranges come from the bounds rather than from every Character (ED-NEW-lean-12).
        Ranges(spans);

        shownBlocks.Clear();
        for (var index = 0; index < textBlocks.Length; index++)
        {
            var textBlock = textBlocks[index];
            if (!painted.TryGetValue(textBlock, out var clean))
            {
                clean = textBlock.Inlines is { } current ? [.. current] : [];
                painted.Add(textBlock, clean);
            }

            rebuilt.Clear();
            var runOffset = 0;
            foreach (var inline in clean)
            {
                if (inline is Run run)
                {
                    var runText = run.Text ?? "";
                    var length = runText.Length;
                    PaintRun(run, runText, runOffset, characterRanges, textBlock, rebuilt);
                    runOffset += length;
                }
                else
                {
                    rebuilt.Add(inline);
                    if (inline is LineBreak) runOffset++;
                }
            }

            // ED-NEW-lean-12: every inline added to or removed from the SelectableTextBlock makes it rebuild its whole text twice (Avalonia
            // 12.1.2 coerces the selection against it). When the pieces are the ones it holds and only the colour of a split piece moved,
            // that piece takes the new brush in place: the block ends up holding the same runs either way.
            if (!Recolour(textBlock.Inlines!, clean))
            {
                textBlock.Inlines!.Clear();
                foreach (var piece in rebuilt) textBlock.Inlines!.Add(piece);
            }

            Remember(index, textBlock);
        }

        keptSlices.Clear();
        (keptSlices, usedSlices) = (usedSlices, keptSlices);
    }

    // ED-NEW-lean-12: whether the block's inlines are this paint's pieces but for the brush of split pieces (CloneRun's, from the same run
    // and the same slice of its text), and if so gives them the new brushes.
    private bool Recolour(InlineCollection inlines, List<Inline> clean)
    {
        if (inlines.Count != rebuilt.Count) return false;
        for (var index = 0; index < rebuilt.Count; index++)
        {
            var current = inlines[index];
            var piece = rebuilt[index];
            if (ReferenceEquals(current, piece)) continue;
            if (current is not Run now || piece is not Run fresh || Holds(clean, now) || Holds(clean, fresh)) return false;
            if (!string.Equals(now.Text, fresh.Text, StringComparison.Ordinal) || !now.FontSize.Equals(fresh.FontSize) || !Equals(now.FontFamily, fresh.FontFamily)
                || now.FontWeight != fresh.FontWeight || now.FontStyle != fresh.FontStyle)
                return false;
        }

        for (var index = 0; index < rebuilt.Count; index++)
        {
            if (inlines[index] is Run now && !ReferenceEquals(now, rebuilt[index])) now.Foreground = ((Run)rebuilt[index]).Foreground;
        }

        return true;
    }

    private static bool Holds(List<Inline> clean, Inline inline)
    {
        foreach (var piece in clean)
        {
            if (ReferenceEquals(piece, inline)) return true;
        }

        return false;
    }

    // ED-NEW-lean-12: Paint() set these inlines on this text block.
    private void Remember(int index, TextBlock textBlock)
    {
        shownBlocks.Add(textBlock);
        if (shownInlines.Count <= index) shownInlines.Add([]);
        var shown = shownInlines[index];
        shown.Clear();
        foreach (var piece in textBlock.Inlines!) shown.Add((piece, piece is Run run ? run.Text : null));
    }

    // ED-NEW-lean-12: whether the text blocks are the ones Paint() last set, each still holding exactly the inlines it set, with their texts.
    private bool Unchanged(TextBlock[] textBlocks)
    {
        if (paintedText is null || shownBlocks.Count != textBlocks.Length) return false;
        for (var index = 0; index < textBlocks.Length; index++)
        {
            if (!ReferenceEquals(shownBlocks[index], textBlocks[index])) return false;
            var shown = shownInlines[index];
            var inlines = textBlocks[index].Inlines;
            if (shown.Count == 0 || inlines is null || inlines.Count != shown.Count) return false;
            for (var piece = 0; piece < shown.Count; piece++)
            {
                var inline = inlines[piece];
                if (!ReferenceEquals(inline, shown[piece].Inline)) return false;
                if (inline is Run run && !ReferenceEquals(run.Text, shown[piece].Text)) return false;
            }
        }

        return true;
    }

    // ED-NEW-lean-12: forgets what the last Paint() cut and set.
    private void Forget()
    {
        paintedText = null;
        shownBlocks.Clear();
        keptSlices.Clear();
        usedSlices.Clear();
    }

    // StreamVeil.swift:409-430 (and :314-320): the UTF-16 ranges of the painted text's Characters that a chunk holds, with its alpha, in order.
    private void Ranges(ValueList<StreamVeil.Span> spans)
    {
        characterRanges.Clear();
        bounds.Clear();
        foreach (var span in spans)
        {
            bounds.Add(span.Range.LowerBound);
            bounds.Add(span.Range.UpperBound);
        }

        bounds.Sort();
        var count = paintedStarts.Length - 1;
        var lower = 0;
        foreach (var bound in bounds)
        {
            var upper = Math.Min(bound, count);
            if (upper <= lower) continue;
            Range(lower, upper, spans);
            lower = upper;
        }

        if (lower < count) Range(lower, count, spans);
    }

    private void Range(int lower, int upper, ValueList<StreamVeil.Span> spans)
    {
        if (AlphaAt(lower, spans) is not { } alpha) return;
        var start = paintedStarts[lower];
        var end = paintedStarts[upper];
        if (characterRanges.Count > 0)
        {
            var last = characterRanges[^1];
            if (last.End == start && last.Alpha == alpha)
            {
                characterRanges[^1] = (last.Start, end, last.Alpha);
                return;
            }
        }

        characterRanges.Add((start, end, alpha));
    }

    /// <summary>Appends `run`'s pieces to `into`, splitting at any span boundary that falls inside it so only
    /// the overlapping characters carry the veil colour; the rest keep the run's own, unpainted colour.</summary>
    private void PaintRun(
        Run run,
        string runText,
        int runOffset,
        List<(int Start, int End, double Alpha)> characterRanges,
        TextBlock textBlock,
        List<Inline> into)
    {
        var length = runText.Length;
        var end = runOffset + length;
        // The ranges are in order and apart (ED-NEW-lean-12: no list is built for a run no range touches).
        var overlaps = false;
        foreach (var range in characterRanges)
        {
            if (range.Start < end && range.End > runOffset)
            {
                overlaps = true;
                break;
            }
        }

        if (!overlaps)
        {
            into.Add(run);
            return;
        }

        var existing = run.Foreground as SolidColorBrush;
        var accentColor = Marrow.UI.Chrome.Chrome.Accent.Resolve(textBlock.ActualThemeVariant);
        var isLink = existing is not null && existing.Color == accentColor;
        var color = isLink ? accentColor : existing?.Color ?? SwiftColor.LabelColor.Resolve(textBlock.ActualThemeVariant);
        var opaqueColor = Color.FromArgb(255, color.R, color.G, color.B);

        var cursor = runOffset;
        foreach (var range in characterRanges)
        {
            if (range.Start >= end || range.End <= runOffset) continue;
            var segStart = Math.Max(range.Start, runOffset);
            var segEnd = Math.Min(range.End, end);
            if (segStart > cursor) into.Add(CloneRun(run, Slice(run, runText, cursor - runOffset, segStart - runOffset), run.Foreground));
            if (range.Alpha < 1)
            {
                into.Add(CloneRun(
                    run,
                    Slice(run, runText, segStart - runOffset, segEnd - runOffset),
                    new SolidColorBrush(opaqueColor) { Opacity = range.Alpha }));
            }
            else
            {
                into.Add(CloneRun(run, Slice(run, runText, segStart - runOffset, segEnd - runOffset), run.Foreground));
            }
            cursor = segEnd;
        }
        if (cursor < end) into.Add(CloneRun(run, Slice(run, runText, cursor - runOffset, length), run.Foreground));
    }

    // ED-NEW-lean-12: runText[lower..upper], the same string object as the last paint's for the same run, text and slice (the piece before
    // the dissolving tail is most of a long paragraph and does not change from tick to tick).
    private string Slice(Run run, string runText, int lower, int upper)
    {
        var key = (run, lower, upper);
        if (usedSlices.TryGetValue(key, out var known) || keptSlices.Remove(key, out known))
        {
            if (ReferenceEquals(known.Source, runText))
            {
                usedSlices[key] = known;
                return known.Slice;
            }
        }

        var slice = runText[lower..upper];
        usedSlices[key] = (runText, slice);
        return slice;
    }

    private static Run CloneRun(Run original, string text, IBrush? foreground) => new(text)
    {
        FontSize = original.FontSize,
        FontFamily = original.FontFamily,
        FontWeight = original.FontWeight,
        FontStyle = original.FontStyle,
        Foreground = foreground,
    };

    // StreamVeil.swift:412-430.
    private static string TextOf(LinkTextView view) =>
        string.Concat(TextBlocks(view).Select(textBlock =>
        {
            var inlines = textBlock.Inlines;
            return inlines is { Count: > 0 }
                ? string.Concat(inlines.Select(inline => inline switch
                {
                    Run run => run.Text ?? "",
                    LineBreak => "\n",
                    _ => "",
                }))
                : textBlock.Text ?? "";
        }));

    // StreamVeil.swift:409-430.
    private static IEnumerable<TextBlock> TextBlocks(LinkTextView view)
    {
        foreach (var descendant in Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(view))
        {
            if (descendant is TextBlock block) yield return block;
        }
    }

    // StreamVeil.swift:314-320.
    private static double? AlphaAt(int character, ValueList<StreamVeil.Span> spans)
    {
        foreach (var span in spans)
        {
            if (character >= span.Range.LowerBound && character < span.Range.UpperBound)
                return span.Alpha;
        }

        return null;
    }
}

/// <summary>ED-NEW-lean-12: where each Character (SwiftString.Characters, the `for character in` of StreamVeil.swift:309-338) of a string
/// starts, in UTF-16 units, and the string's length last.</summary>
file static class CharacterStarts
{
    public static int[] Of(string text)
    {
        var result = new List<int>();
        var offset = 0;
        foreach (var character in SwiftString.Characters(text))
        {
            result.Add(offset);
            offset += character.Length;
        }

        result.Add(offset);
        return [.. result];
    }
}
