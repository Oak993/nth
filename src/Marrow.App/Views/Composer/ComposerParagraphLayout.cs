// PORT-ONLY: OD-0091, voice-SPEC V-8.3/V-8.9. The editor and live words share native Avalonia lines with a natural bidi base for each paragraph.
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Media.TextFormatting.Unicode;
using Avalonia.Utilities;
using System.Reflection;

namespace Marrow.App.Views.Composer;

internal static class ComposerParagraphLayout
{
    internal static TextLayout Create(string text, Typeface face, double size, IBrush? foreground, TextAlignment alignment,
        TextWrapping wrapping, FlowDirection fallback, double width, double height = double.PositiveInfinity,
        double lineHeight = double.NaN, double letterSpacing = 0, FontFeatureCollection? features = null,
        IReadOnlyList<ValueSpan<TextRunProperties>>? spans = null)
    {
        // TextLayout has no per-paragraph factory hook. Only our marker source takes this adapter path; all other formatting is delegated unchanged.
        var formatter = TextFormatter.Current;
        if (formatter is not ComposerParagraphFormatter) Install(new ComposerParagraphFormatter(formatter));
        var defaults = new GenericTextRunProperties(face, size, null, foreground, fontFeatures: features);
        var source = new ComposerParagraphSource(text, defaults, fallback, spans);
        var properties = new GenericTextParagraphProperties(source.DirectionAt(0), alignment, true, false, defaults, wrapping, lineHeight, 0, letterSpacing);
        return new TextLayout(source, properties, TextTrimming.None, width, height, 0);
    }

    private static void Install(TextFormatter formatter)
    {
        // Avalonia 12 omits its public runtime locator from the reference assembly (as in GpuBootPreparation).
        // This version-pinned adapter fails visibly if that API changes; it never silently falls back to incorrect bidi layout.
        var locator = typeof(AvaloniaLocator).GetProperty("CurrentMutable", BindingFlags.Public | BindingFlags.Static)?.GetValue(null)
            ?? throw new InvalidOperationException("Composer paragraph layout: Avalonia's mutable locator API changed.");
        var bind = locator.GetType().GetMethods().SingleOrDefault(method => method.Name == "Bind" && method.IsGenericMethodDefinition && method.GetParameters().Length == 0)
            ?? throw new InvalidOperationException("Composer paragraph layout: Avalonia's formatter binding API changed.");
        var binding = bind.MakeGenericMethod(typeof(TextFormatter)).Invoke(locator, null)
            ?? throw new InvalidOperationException("Composer paragraph layout: no formatter binding.");
        var constant = binding.GetType().GetMethods().SingleOrDefault(method => method.Name == "ToConstant" && method.IsGenericMethodDefinition && method.GetParameters().Length == 1)
            ?? throw new InvalidOperationException("Composer paragraph layout: Avalonia's constant binding API changed.");
        constant.MakeGenericMethod(typeof(TextFormatter)).Invoke(binding, [formatter]);
    }

    // UAX #9 P2/P3: first strong outside isolates; digits, punctuation and combining marks do not choose the base.
    internal static FlowDirection Direction(string text, FlowDirection fallback) => Direction(text.AsSpan(), fallback);

    internal static FlowDirection Direction(ReadOnlySpan<char> text, FlowDirection fallback)
    {
        var isolates = 0;
        for (var at = 0; at < text.Length;)
        {
            var cp = Codepoint.ReadAt(text, at, out var count); at += count;
            var kind = cp.BiDiClass;
            if (kind is BidiClass.LeftToRightIsolate or BidiClass.RightToLeftIsolate or BidiClass.FirstStrongIsolate) { isolates++; continue; }
            if (kind == BidiClass.PopDirectionalIsolate) { if (isolates > 0) isolates--; continue; }
            if (isolates != 0) continue;
            if (kind == BidiClass.LeftToRight) return FlowDirection.LeftToRight;
            if (kind is BidiClass.RightToLeft or BidiClass.ArabicLetter) return FlowDirection.RightToLeft;
        }
        return fallback;
    }
}

internal sealed class ComposerParagraphFormatter(TextFormatter native) : TextFormatter
{
    public override TextLine? FormatLine(ITextSource source, int start, double width, TextParagraphProperties properties, TextLineBreak? previous = null) =>
        native.FormatLine(source, start, width, ForParagraph(source, start, properties), previous);

    public override TextLine? FormatLine(ITextSource source, int start, double width, TextParagraphProperties properties, TextLineBreak? previous, TextRunCache? cache) =>
        native.FormatLine(source, start, width, ForParagraph(source, start, properties), previous, cache);

    private static TextParagraphProperties ForParagraph(ITextSource source, int start, TextParagraphProperties original)
    {
        if (source is not ComposerParagraphSource paragraph) return original;
        // Native TextLineImpl retains this object and reads it during later hit tests. Never mutate properties shared with another paragraph.
        return new GenericTextParagraphProperties(paragraph.DirectionAt(start), original.TextAlignment, original.FirstLineInParagraph,
            original.AlwaysCollapsible, original.DefaultTextRunProperties, original.TextWrapping, original.LineHeight, original.Indent, original.LetterSpacing);
    }
}

internal sealed class ComposerParagraphSource : ITextSource
{
    private readonly string text;
    private readonly TextRunProperties defaults;
    private readonly IReadOnlyList<ValueSpan<TextRunProperties>> spans;
    private readonly (int Start, FlowDirection Direction)[] paragraphs;

    // The walk depends only on the text and the fallback base, and one keystroke rebuilds layouts of the same text several times (the
    // editor at each text, caret, selection and measure change, the height probe): the last few walks on this thread are kept, each
    // immutable, and reused for an equal text and fallback (ED-NEW-lean-20).
    private sealed class Walk(string text, FlowDirection fallback, (int Start, FlowDirection Direction)[] paragraphs)
    {
        internal readonly string Text = text;
        internal readonly FlowDirection Fallback = fallback;
        internal readonly (int Start, FlowDirection Direction)[] Paragraphs = paragraphs;
    }

    [ThreadStatic] private static Walk?[]? recentWalks;
    [ThreadStatic] private static int nextWalk;

    internal ComposerParagraphSource(string text, TextRunProperties defaults, FlowDirection fallback, IReadOnlyList<ValueSpan<TextRunProperties>>? spans)
    {
        this.text = text; this.defaults = defaults;
        this.spans = spans is null || spans.Count == 0 ? [] : spans.Count == 1 ? [spans[0]] : spans.OrderBy(span => span.Start).ToArray();
        paragraphs = ParagraphsOf(text, fallback);
    }

    private static (int Start, FlowDirection Direction)[] ParagraphsOf(string text, FlowDirection fallback)
    {
        var recent = recentWalks ??= new Walk?[4];
        foreach (var walk in recent)
            if (walk is not null && walk.Fallback == fallback && string.Equals(walk.Text, text, StringComparison.Ordinal)) return walk.Paragraphs;
        var found = new List<(int Start, FlowDirection Direction)>(1);
        var start = 0;
        for (var at = 0; at < text.Length;)
        {
            var cp = Codepoint.ReadAt(text.AsSpan(), at, out var count);
            if (cp.BiDiClass == BidiClass.ParagraphSeparator)
            {
                found.Add((start, ComposerParagraphLayout.Direction(text.AsSpan(start, at - start), fallback)));
                at += count;
                if (cp.Value == '\r' && at < text.Length && text[at] == '\n') at++;
                start = at;
            }
            else at += count;
        }
        found.Add((start, ComposerParagraphLayout.Direction(text.AsSpan(start), fallback)));
        var paragraphs = found.ToArray();
        recent[nextWalk] = new Walk(text, fallback, paragraphs);
        nextWalk = (nextWalk + 1) % recent.Length;
        return paragraphs;
    }

    internal FlowDirection DirectionAt(int position)
    {
        for (var i = paragraphs.Length - 1; i >= 0; i--)
            if (position >= paragraphs[i].Start) return paragraphs[i].Direction;
        return paragraphs[0].Direction;
    }

    public TextRun? GetTextRun(int at)
    {
        if (at >= text.Length) return null;
        var properties = defaults;
        var length = text.Length - at;
        foreach (var span in spans)
        {
            if (span.Start + span.Length <= at) continue;
            if (span.Start > at) { length = Math.Min(length, span.Start - at); break; }
            properties = span.Value; length = Math.Min(length, span.Start + span.Length - at); break;
        }
        // Match Avalonia's FormattedTextSource: style boundaries never split a grapheme (preedit, selection, combining marks and supplementary characters).
        var graphemes = new GraphemeEnumerator(text.AsSpan(at));
        var coerced = 0;
        while (graphemes.MoveNext(out var grapheme)) { coerced += grapheme.Length; if (coerced >= length) break; }
        return new TextCharacters(text.AsMemory(at, coerced), properties);
    }
}
