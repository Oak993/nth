using System.Collections.Generic;
using System.Linq;
using Marrow.Core.Port;
using Marrow.UI.Markdown;
using Marrow.UI.Port;

namespace Marrow.UI.Tests.Fixtures;

/// <summary>ED-NEW-lean-12: VeiledText's painting as it stood on the base, frozen (StreamVeil.cs at f8be2e20d, :679-909), the oracle of
/// StreamVeilOracleTests; the dimmed colour is left out (the rig is not dimmed).</summary>
internal static class StreamVeilFrozenPainting
{
    public static StyledText Settled(IReadOnlyList<VeiledText.Segment>? segments, string text)
    {
        if (segments is null) return new StyledText(text);
        var output = new StyledText("");
        foreach (var segment in segments)
        {
            output = segment switch
            {
                VeiledText.Segment.Fixed fixedRun => output + fixedRun.Text,
                VeiledText.Segment.Prose prose => output + Styled(prose.Value, 0, prose.Value.PlainText, ValueList<StreamVeil.Span>.Empty, SwiftColor.Primary),
                _ => throw new InvalidOperationException(),
            };
        }

        return output;
    }

    public static StyledText Text(ValueList<StreamVeil.Span> spans, IReadOnlyList<VeiledText.Segment>? segments, string text)
    {
        var baseColor = SwiftColor.Primary;
        if (segments is null) return Plain(text, spans, baseColor);

        var runs = new List<AttributedRun>();
        var ends = new List<int>();
        var joined = "";
        foreach (var segment in segments)
        {
            if (segment is not VeiledText.Segment.Prose prose) continue;
            runs.AddRange(prose.Value.Runs);
            joined += prose.Value.PlainText;
            ends.Add(SwiftString.Count(joined));
        }

        var attributed = new AttributedText(runs);
        var joinedText = attributed.PlainText;
        var joinedStyled = Styled(attributed, 0, joinedText, spans, baseColor);
        var output = new StyledText("");
        var cursor = 0;
        var index = 0;
        foreach (var segment in segments)
        {
            switch (segment)
            {
                case VeiledText.Segment.Fixed fixedRun:
                    output += fixedRun.Text;
                    break;
                case VeiledText.Segment.Prose:
                    var end = ends[index++];
                    output += SliceStyled(joinedStyled, joinedText, cursor, end);
                    cursor = end;
                    break;
            }
        }

        return output;
    }

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
            if (end > start) output += StyledRun(run, run.Text.Substring(start - offset, end - start));
            offset = runEnd;
        }

        return output;
    }

    private static int Utf16Offset(IReadOnlyList<string> characters, int characterIndex)
    {
        var offset = 0;
        for (var index = 0; index < characterIndex; index++) offset += characters[index].Length;
        return offset;
    }

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
            var run = new StyledText(string.Concat(characters.Skip(start).Take(end - start)));
            if (alpha is { } opacity) run = run.ForegroundColor(baseColor.Opacity(opacity));
            output += run;
            start = end;
        }

        return output;
    }

    private static StyledText Styled(AttributedText attributed, int utf16Offset, string joinedText, ValueList<StreamVeil.Span> spans, SwiftColor baseColor)
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
                output += StyledRun(attributedRun with { Text = string.Concat(localCharacters.Skip(start).Take(end - start)) }, alpha, baseColor);
                start = end;
            }

            runOffset += attributedRun.Text.Length;
        }

        return output;
    }

    private static double? AlphaForRange(int lower, int upper, IReadOnlyList<(int Start, int End, double? Alpha)> characters)
    {
        foreach (var character in characters)
        {
            if (character.Start < upper && character.End > lower) return character.Alpha;
        }

        return null;
    }

    private static StyledText StyledRun(AttributedRun run, double? alpha, SwiftColor baseColor)
    {
        var text = new StyledText(run.Text);
        if (run.IsBold == true) text = text.FontWeight(SwiftFont.Weight.Bold);
        if (run.IsItalic == true) text = text.Italic();
        if (run.IsCode) text = text.Font(SwiftFont.System(SwiftFont.TextStyle.Body, design: SwiftFont.Design.Monospaced));
        if (run.Link is { } link) text = text.Link(link);
        if (alpha is { } opacity) text = text.ForegroundColor(baseColor.Opacity(opacity));
        else if (run.Link is not null) text = text.ForegroundColor(Marrow.UI.Chrome.Chrome.Accent);
        return text;
    }

    private static double? AlphaAt(int character, ValueList<StreamVeil.Span> spans)
    {
        foreach (var span in spans)
        {
            if (character >= span.Range.LowerBound && character < span.Range.UpperBound) return span.Alpha;
        }

        return null;
    }
}
