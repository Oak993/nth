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

        Set(ref now, SwiftDate.Now);
        if (deadline is not { } end || now < end) return;

        link?.Invalidate();
        link = null;
        deadline = null;
    }
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

    private readonly StreamVeil veil = new(); // StreamVeil.swift:245
    private readonly IReadOnlyList<Segment>? segments; // StreamVeil.swift:237
    private readonly Source source; // StreamVeil.swift:229
    private readonly TextBlock textBlock = new(); // StreamVeil.swift:245
    private readonly bool hasSegments;
    private bool isRedrawing;
    private bool wasFading;

    // StreamVeil.swift:241.
    public ValueList<int> ProseEnds { get; } = ValueList<int>.Empty;

    // StreamVeil.swift:253-265,273-279.
    public VeiledText(IReadOnlyList<Segment> segments)
    {
        this.segments = segments;
        hasSegments = true;
        source = new Source.Attributed(JoinProse(segments, out var ends));
        ProseEnds = new ValueList<int>(ends);
        Build();
    }

    // StreamVeil.swift:247-251.
    public VeiledText(AttributedText attributed) : this([new Segment.Prose(attributed)])
    {
    }

    // StreamVeil.swift:267-271,273-279.
    public VeiledText(string verbatim)
    {
        source = new Source.Plain(verbatim);
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
        Redraw();
    }

    // StreamVeil.swift:243-244.
    private void OnEnvironmentChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == StreamVeil.MarkdownVeiledProperty ||
            e.Property == MarkdownView.MarkdownDimmedProperty)
            Redraw();
    }

    // StreamVeil.swift:273-279.
    private void Redraw()
    {
        if (isRedrawing) return;
        isRedrawing = true;
        try
        {
            var at = SwiftDate.Now;
            var fading = veil.Step(() => source.Characters, StreamVeil.GetMarkdownVeiled(this), at);
            wasFading = fading;
            StyledText result;
            if (fading)
            {
                if (veil.SettlesAt is { } settlesAt) VeilClock.Shared.Run(until: settlesAt);
                result = Text(at);
            }
            else
            {
                if (!StreamVeil.GetMarkdownVeiled(this)) veil.Reset();
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
    private readonly Dictionary<Run, IBrush?> painted = new(); // StreamVeil.swift:350
    private WeakReference<LinkTextView>? tickTarget; // StreamVeil.swift:374

    // StreamVeil.swift:354-367.
    public void Update(LinkTextView view, bool active)
    {
        var now = SwiftDate.Now;
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

    // StreamVeil.swift:370-372.
    public void Detach(LinkTextView view) => Settle(view, false);

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
        foreach (var (run, foreground) in painted)
        {
            if (run.Parent is TextBlock) run.Foreground = foreground;
        }

        painted.Clear();
    }

    // StreamVeil.swift:409-430.
    private void Paint(LinkTextView view, DateTimeOffset at)
    {
        var textBlocks = TextBlocks(view).ToArray();
        if (textBlocks.Length == 0) return;
        Clear(view);
        var spans = veil.Spans(at);
        if (spans.Count == 0) return;

        var joinedText = TextOf(view);
        var characterRanges = new List<(int Start, int End, double Alpha)>();
        var globalOffset = 0;
        var characterIndex = 0;
        foreach (var character in SwiftString.Characters(joinedText))
        {
            var end = globalOffset + character.Length;
            var alpha = AlphaAt(characterIndex, spans);
            if (alpha is { } opacity) characterRanges.Add((globalOffset, end, opacity));
            globalOffset = end;
            characterIndex++;
        }

        var runOffset = 0;
        foreach (var textBlock in textBlocks)
        {
            if (textBlock.Inlines is not { } inlines)
            {
                runOffset += (textBlock.Text ?? "").Length;
                continue;
            }

            foreach (var inline in inlines)
            {
                if (inline is Run run)
                {
                    var runText = run.Text ?? "";
                    var end = runOffset + runText.Length;
                    var alpha = AlphaForPaintRange(runOffset, end, characterRanges);
                    if (alpha is { } opacity && opacity < 1)
                    {
                        if (!painted.ContainsKey(run)) painted.Add(run, run.Foreground);
                        var existing = run.Foreground as SolidColorBrush;
                        var accentColor = Marrow.UI.Chrome.Chrome.Accent.Resolve(textBlock.ActualThemeVariant);
                        var isLink = existing is not null && existing.Color == accentColor;
                        var color = isLink
                            ? accentColor
                            : existing?.Color ?? SwiftColor.LabelColor.Resolve(textBlock.ActualThemeVariant);
                        var opaqueColor = Color.FromArgb(255, color.R, color.G, color.B);
                        run.Foreground = new SolidColorBrush(opaqueColor) { Opacity = opacity };
                    }

                    runOffset = end;
                }
                else if (inline is LineBreak)
                {
                    runOffset++;
                }
            }
        }
    }

    // StreamVeil.swift:409-430.
    private static double? AlphaForPaintRange(
        int lower,
        int upper,
        IReadOnlyList<(int Start, int End, double Alpha)> characters)
    {
        foreach (var character in characters)
        {
            if (character.Start < upper && character.End > lower) return character.Alpha;
        }

        return null;
    }

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
