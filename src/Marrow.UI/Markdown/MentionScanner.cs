using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Marrow.Core.Port;

// Source: DroppyCode/UI/Markdown/MentionScanner.swift (B3 52bc792), :1-33. New at B3 (N10): the word-bounded,
// longest-first mention scan shared by LinkParagraphView.swift:197 and MarkdownView.swift:419, :425.

namespace Marrow.UI.Markdown;

/// <summary>:4-33 <c>enum MentionScanner</c>: the word-bounded mention scan shared by every prose path.</summary>
public static class MentionScanner
{
    /// <summary>
    /// :7-32 <c>matches&lt;Value&gt;(in:candidates:name:)</c>: longest name first, so "Hank 2" beats "Hank"; a name
    /// inside a longer word or beside a digit doesn't count. The range is returned as UTF-16 offsets into
    /// <paramref name="text"/> rather than Swift <c>String.Index</c>: every offset this method produces sits on a
    /// grapheme-cluster boundary, so slicing <paramref name="text"/> at them gives the identical substring.
    /// ENGINEERING DECISION (b3_note, PR-THEME-143): an empty candidate name is skipped before the scan. The Swift
    /// loop's <c>i = text.index(i, offsetBy: candidateName.count)</c> does not advance past an empty name
    /// (<c>i == end</c>), so a real empty name would spin forever; no roster name this port renders is ever empty.
    /// </summary>
    /// <param name="text">:7 <c>in text: String</c>.</param>
    /// <param name="candidates">:7 <c>candidates: [Value]</c>.</param>
    /// <param name="name">:7 <c>name: (Value) -> String</c>.</param>
    public static ValueList<(int LowerBound, int UpperBound, T Value)> Matches<T>(
        string text, IReadOnlyList<T> candidates, Func<T, string> name)
    {
        var ordered = candidates
            .Where(candidate => name(candidate).Length > 0)
            .OrderByDescending(candidate => SwiftString.Count(name(candidate))) // :8
            .ToArray();

        var results = new List<(int, int, T)>();
        if (ordered.Length == 0 || text.Length == 0) return new ValueList<(int, int, T)>(results);

        // ENGINEERING DECISION (ED-NEW-lean-13): each name is split into its Characters once, not once per position,
        // and the text's Characters are kept as their boundary offsets rather than as one string each.
        var names = new Name[ordered.Length];
        for (var i = 0; i < ordered.Length; i++) names[i] = new Name(name(ordered[i])); // :14

        var boundaries = Boundaries(text); // ascending; every element is a grapheme-cluster boundary, 0 and text.Length included
        var last = boundaries.Length - 1; // the text's Character count

        var k = 0; // :10 i = text.startIndex, as the index of its boundary
        while (k < last) // :11
        {
            var cursor = boundaries[k];
            var found = false;
            T hit = default!;
            var hitIndex = 0;
            string? character = null; // the Character at the cursor, made only if a compare needs it as a string
            for (var c = 0; c < ordered.Length; c++) // :13
            {
                var candidate = names[c];
                // :15 text[i...].hasPrefix(candidateName): whole Characters, compared canonically (R-STR-2), not a
                // UTF-16 ordinal compare — U+0301-combining and precomposed forms of the same letter must match.
                if (!HasPrefixAt(text, boundaries, k, candidate, ref character)) continue;
                // :16 text.index(i, offsetBy: candidateName.count, limitedBy: text.endIndex) ?? text.endIndex: advance
                // by candidateName's Character count, not its UTF-16 length, and clamp at text's end.
                var afterIndex = Math.Min(k + candidate.Count, last);
                var after = boundaries[afterIndex];

                var beforeOk = cursor == 0 || !IsWordCharacter(text[boundaries[k - 1]..cursor]); // :17-18
                var afterOk = after == text.Length || !IsWordCharacter(text[after..boundaries[afterIndex + 1]]); // :19-20
                if (!beforeOk || !afterOk) continue;
                hit = ordered[c]; // :21
                hitIndex = afterIndex; // :24 text.index(i, offsetBy: name(hit).count) is the same grapheme advance as `after`
                found = true;
                break;
            }

            if (found)
            {
                results.Add((cursor, boundaries[hitIndex], hit)); // :25
                k = hitIndex; // :26
            }
            else
            {
                k += 1; // :28 i = text.index(after: i)
            }
        }

        return new ValueList<(int, int, T)>(results); // :31
    }

    // :18, :20 !c.isLetter && !c.isNumber
    private static bool IsWordCharacter(string value) => SwiftCharacter.IsLetter(value) || SwiftCharacter.IsNumber(value);

    // The UTF-16 offset of every Character boundary of `text`, from 0 to text.Length (SwiftString.Characters' steps).
    private static int[] Boundaries(string text)
    {
        var count = 0;
        for (var index = 0; index < text.Length; count++) index += StringInfo.GetNextTextElementLength(text.AsSpan(index));
        var boundaries = new int[count + 1];
        var offset = 0;
        for (var i = 1; i <= count; i++)
        {
            offset += StringInfo.GetNextTextElementLength(text.AsSpan(offset));
            boundaries[i] = offset;
        }
        return boundaries;
    }

    // ENGINEERING DECISION (ED-NEW-lean-13): SwiftString.HasPrefix(text[cursor..], name) without the substring. The
    // text's Characters from a boundary are the Characters of that suffix, so the two are compared one Character at a
    // time with SwiftString.Equal's rule (ordinal, else NFC). A text Character that is one ASCII character is its own
    // NFC form, so it is compared with the name Character's NFC form, made once per name; every other text Character
    // goes through SwiftString.Equal itself, which also throws where the old compare threw (a lone surrogate reaching
    // NFC). On two ASCII strings this is the old ordinal compare, CRLF being one Character in both.
    private static bool HasPrefixAt(string text, int[] boundaries, int k, Name name, ref string? firstCharacter)
    {
        var characters = name.Characters;
        for (var j = 0; j < characters.Length; j++)
        {
            if (k + j >= boundaries.Length - 1) return false; // the text ran out first
            var start = boundaries[k + j];
            var span = text.AsSpan(start, boundaries[k + j + 1] - start);
            var character = characters[j];
            if (span.SequenceEqual(character)) continue;
            if (span.Length == 1 && span[0] < 0x80)
            {
                if (name.Nfc[j] is { } nfc)
                {
                    if (span.SequenceEqual(nfc)) continue;
                    return false;
                }
            }
            string textCharacter;
            if (j == 0) textCharacter = firstCharacter ??= span.ToString();
            else textCharacter = span.ToString();
            if (!SwiftString.Equal(textCharacter, character)) return false;
        }
        return true;
    }

    // One candidate name: its Characters, each one's NFC form (null when NFC throws, so the compare goes through
    // SwiftString.Equal and throws there exactly as before), and SwiftString.Count, which the advance uses (:16).
    private sealed class Name
    {
        public Name(string text)
        {
            Characters = SwiftString.Characters(text).ToArray();
            Nfc = new string?[Characters.Length];
            for (var i = 0; i < Characters.Length; i++)
            {
                try { Nfc[i] = Characters[i].IsNormalized(NormalizationForm.FormC) ? Characters[i] : Characters[i].Normalize(NormalizationForm.FormC); }
                catch (ArgumentException) { Nfc[i] = null; }
            }
            Count = SwiftString.Count(text);
        }

        public string[] Characters { get; }

        public string?[] Nfc { get; }

        public int Count { get; }
    }
}
