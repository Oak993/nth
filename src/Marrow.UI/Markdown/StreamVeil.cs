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
    private List<Chunk> chunks = []; // StreamVeil.swift:48
    private double cadence = CadenceSeed; // StreamVeil.swift:49
    private DateTimeOffset? lastAppend; // StreamVeil.swift:50

    // StreamVeil.swift:55-85.
    public void Advance(string text, DateTimeOffset at)
    {
        var characters = SwiftString.Characters(text).ToArray();
        if (previous is null)
        {
            previous = characters;
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
/// reply stops streaming MarkdownView draws prose runs, a different view, and <see cref="Release"/> drops the places of the views that went.
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
    }

    // The root's path is "", the child named n of a place p is p + "/" + n: a place is under a path when it is that path or goes on with a "/".
    private static bool IsUnder(string place, string path) =>
        path.Length == 0 || place == path || place.StartsWith(path + "/", StringComparison.Ordinal);

    /// <summary>PORT-ONLY, observed by the tests: how many places hold a veil (each with its owner view).</summary>
    public int Places => text.Count + links.Count;

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
                0,
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
    private readonly TextBlock textBlock = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private readonly bool hasSegments;
    private bool isRedrawing;
    private bool wasFading;

    // StreamVeil.swift:241.
    public ValueList<int> ProseEnds { get; } = ValueList<int>.Empty;

    // StreamVeil.swift:253-265,273-279.
    public VeiledText(IReadOnlyList<Segment> segments, MarkdownVeilScope? veils = null)
    {
        this.segments = segments;
        hasSegments = true;
        source = new Source.Attributed(JoinProse(segments, out var ends));
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
            var fading = veil.Step(() => source.Characters, veiled, at);
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

            result.Render(textBlock);
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
                    0,
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

        if (!hasSegments)
        {
            if (source is Source.Plain plain) return Plain(plain.String, spans, baseColor);
            if (source is Source.Attributed attributed)
                return Styled(attributed.Value, 0, source.Characters, spans, baseColor);
            throw new UnreachableException();
        }

        var joinedText = source.Characters;
        var joinedStyled = Styled(
            ((Source.Attributed)source).Value,
            0,
            joinedText,
            spans,
            baseColor);
        var output = new StyledText("");
        var cursor = 0;
        var prose = 0;
        foreach (var segment in segments!)
        {
            switch (segment)
            {
                case Segment.Fixed fixedRun:
                    output += fixedRun.Text;
                    break;
                case Segment.Prose:
                    var end = ProseEnds[prose++];
                    output += SliceStyled(joinedStyled, joinedText, cursor, end);
                    cursor = end;
                    break;
                default:
                    throw new UnreachableException();
            }
        }

        return output;
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

    // StreamVeil.swift:326-331. Slice at the joined string's grapheme boundaries, not at each prose run's count.
    private static StyledText SliceStyled(StyledText styled, string joinedText, int lowerCharacter, int upperCharacter)
    {
        var characters = SwiftString.Characters(joinedText).ToArray();
        var lower = Utf16Offset(characters, lowerCharacter);
        var upper = Utf16Offset(characters, upperCharacter);
        var output = new StyledText("");
        var offset = 0;

        foreach (var run in styled.Runs)
        {
            var runEnd = offset + run.Text.Length;
            var start = Math.Max(offset, lower);
            var end = Math.Min(runEnd, upper);
            if (end > start)
            {
                var text = run.Text.Substring(start - offset, end - start);
                output += StyledRun(run, text);
            }
            offset = runEnd;
        }

        return output;
    }

    // StreamVeil.swift:326-331.
    private static int Utf16Offset(IReadOnlyList<string> characters, int characterIndex)
    {
        var offset = 0;
        for (var index = 0; index < characterIndex; index++) offset += characters[index].Length;
        return offset;
    }

    // StreamVeil.swift:326-331.
    private static StyledText StyledRun(StyledText.Run run, string text)
    {
        var result = new StyledText(text);
        if (run.Font is { } font) result = result.Font(font);
        if (run.Color is { } color) result = result.ForegroundColor(color);
        if (run.Weight is { } weight) result = result.FontWeight(weight);
        if (run.IsItalic is true) result = result.Italic();
        if (run.IsUnderlined is true) result = result.Underline();
        if (run.IsStrikethrough is true) result = result.Strikethrough();
        if (run.HasMonospacedDigits is true) result = result.MonospacedDigit();
        if (run.BaselineOffset is { } baseline) result = result.BaselineOffset(baseline);
        if (run.Link is { } link) result = result.Link(link);
        return result;
    }

    // StreamVeil.swift:309-320.
    private static StyledText Plain(string text, ValueList<StreamVeil.Span> spans, SwiftColor baseColor)
    {
        var characters = SwiftString.Characters(text).ToArray();
        var output = new StyledText("");
        var start = 0;
        while (start < characters.Length)
        {
            var alpha = AlphaAt(start, spans);
            var end = start + 1;
            while (end < characters.Length && AlphaAt(end, spans) == alpha) end++;
            var value = string.Concat(characters.Skip(start).Take(end - start));
            var run = new StyledText(value);
            if (alpha is { } opacity) run = run.ForegroundColor(baseColor.Opacity(opacity));
            output += run;
            start = end;
        }

        return output;
    }

    // StreamVeil.swift:309-338.
    private static StyledText Styled(
        AttributedText attributed,
        int utf16Offset,
        string joinedText,
        ValueList<StreamVeil.Span> spans,
        SwiftColor baseColor)
    {
        var output = new StyledText("");
        var characterRanges = new List<(int Start, int End, double? Alpha)>();
        var globalOffset = 0;
        foreach (var character in SwiftString.Characters(joinedText))
        {
            var end = globalOffset + character.Length;
            characterRanges.Add((globalOffset, end, AlphaAt(characterRanges.Count, spans)));
            globalOffset = end;
        }

        var runOffset = utf16Offset;
        foreach (var attributedRun in attributed.Runs)
        {
            var localCharacters = SwiftString.Characters(attributedRun.Text).ToArray();
            if (localCharacters.Length == 0)
            {
                output += StyledRun(attributedRun, null, baseColor);
                continue;
            }

            var alphas = new double?[localCharacters.Length];
            var localOffset = runOffset;
            for (var index = 0; index < localCharacters.Length; index++)
            {
                var end = localOffset + localCharacters[index].Length;
                alphas[index] = AlphaForRange(localOffset, end, characterRanges);
                localOffset = end;
            }

            var start = 0;
            while (start < localCharacters.Length)
            {
                var alpha = alphas[start];
                var end = start + 1;
                while (end < localCharacters.Length && alphas[end] == alpha) end++;

                var text = string.Concat(localCharacters.Skip(start).Take(end - start));
                output += StyledRun(attributedRun with { Text = text }, alpha, baseColor);
                start = end;
            }

            runOffset += attributedRun.Text.Length;
        }

        return output;
    }

    // StreamVeil.swift:309-338.
    private static double? AlphaForRange(
        int lower,
        int upper,
        IReadOnlyList<(int Start, int End, double? Alpha)> characters)
    {
        foreach (var character in characters)
        {
            if (character.Start < upper && character.End > lower) return character.Alpha;
        }

        return null;
    }

    // StreamVeil.swift:309-338.
    private static StyledText StyledRun(AttributedRun run, double? alpha, SwiftColor baseColor)
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

        return text;
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

    /// <summary>PORT-ONLY. Forgets the text and every chunk, stops the clock and lets go of the view: the owner of a retained veil
    /// (<see cref="MarkdownVeils.Reset"/>) calls this when its row leaves the screen.</summary>
    public void Reset()
    {
        displayLink?.Invalidate();
        displayLink = null;
        painted.Clear();
        tickTarget = null;
        veil.Reset();
    }

    // StreamVeil.swift:354-367.
    public void Update(LinkTextView view, bool active)
    {
        var now = SwiftDate.Now;
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

        var joinedText = TextOf(view);
        var perCharacter = new List<(int Start, int End, double Alpha)>();
        var globalOffset = 0;
        var characterIndex = 0;
        foreach (var character in SwiftString.Characters(joinedText))
        {
            var end = globalOffset + character.Length;
            var alpha = AlphaAt(characterIndex, spans);
            if (alpha is { } opacity) perCharacter.Add((globalOffset, end, opacity));
            globalOffset = end;
            characterIndex++;
        }

        // Coalesce adjacent same-alpha characters into one range, so a Run is split at most at a real chunk
        // boundary, never at every character of a multi-character chunk.
        var characterRanges = new List<(int Start, int End, double Alpha)>();
        foreach (var character in perCharacter)
        {
            if (characterRanges.Count > 0)
            {
                var last = characterRanges[^1];
                if (last.End == character.Start && last.Alpha == character.Alpha)
                {
                    characterRanges[^1] = (last.Start, character.End, last.Alpha);
                    continue;
                }
            }
            characterRanges.Add(character);
        }

        foreach (var textBlock in textBlocks)
        {
            if (!painted.TryGetValue(textBlock, out var clean))
            {
                clean = textBlock.Inlines is { } current ? [.. current] : [];
                painted.Add(textBlock, clean);
            }

            var rebuilt = new List<Inline>();
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

            textBlock.Inlines!.Clear();
            foreach (var piece in rebuilt) textBlock.Inlines!.Add(piece);
        }
    }

    /// <summary>Appends `run`'s pieces to `into`, splitting at any span boundary that falls inside it so only
    /// the overlapping characters carry the veil colour; the rest keep the run's own, unpainted colour.</summary>
    private static void PaintRun(
        Run run,
        string runText,
        int runOffset,
        List<(int Start, int End, double Alpha)> characterRanges,
        TextBlock textBlock,
        List<Inline> into)
    {
        var length = runText.Length;
        var end = runOffset + length;
        var overlapping = characterRanges
            .Where(range => range.Start < end && range.End > runOffset)
            .OrderBy(range => range.Start)
            .ToList();
        if (overlapping.Count == 0)
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
        foreach (var range in overlapping)
        {
            var segStart = Math.Max(range.Start, runOffset);
            var segEnd = Math.Min(range.End, end);
            if (segStart > cursor) into.Add(CloneRun(run, runText[(cursor - runOffset)..(segStart - runOffset)], run.Foreground));
            if (range.Alpha < 1)
            {
                into.Add(CloneRun(
                    run,
                    runText[(segStart - runOffset)..(segEnd - runOffset)],
                    new SolidColorBrush(opaqueColor) { Opacity = range.Alpha }));
            }
            else
            {
                into.Add(CloneRun(run, runText[(segStart - runOffset)..(segEnd - runOffset)], run.Foreground));
            }
            cursor = segEnd;
        }
        if (cursor < end) into.Add(CloneRun(run, runText[(cursor - runOffset)..], run.Foreground));
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
