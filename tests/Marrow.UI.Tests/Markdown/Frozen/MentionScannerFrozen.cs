using System;
using System.Collections.Generic;
using System.Linq;
using Marrow.Core.Port;

namespace Marrow.UI.Tests.Markdown.Frozen;

// FROZEN (ED-NEW-lean-13): src/Marrow.UI/Markdown/MentionScanner.cs exactly as it stood on claude/release-integration
// 0a946c558, before the lean pass, kept as the oracle for MentionScannerOracleTests. Do not edit or optimise.
// Source: DroppyCode/UI/Markdown/MentionScanner.swift (B3 52bc792), :1-33.

/// <summary>:4-33 <c>enum MentionScanner</c>: the word-bounded mention scan shared by every prose path.</summary>
internal static class MentionScannerFrozen
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

        var graphemes = SwiftString.Characters(text).ToArray();
        var boundaries = new SortedSet<int> { 0, text.Length };
        var offset = 0;
        foreach (var grapheme in graphemes)
        {
            offset += grapheme.Length;
            boundaries.Add(offset);
        }
        var boundaryArray = boundaries.ToArray(); // sorted ascending; every element is a grapheme-cluster boundary

        var cursor = 0; // :10 i = text.startIndex
        while (cursor < text.Length) // :11
        {
            var found = false;
            T hit = default!;
            var hitEnd = 0;
            foreach (var candidate in ordered) // :13
            {
                var candidateName = name(candidate); // :14
                // :15 text[i...].hasPrefix(candidateName): whole Characters, compared canonically (R-STR-2), not a
                // UTF-16 ordinal compare — U+0301-combining and precomposed forms of the same letter must match.
                if (!SwiftString.HasPrefix(text[cursor..], candidateName)) continue;
                // :16 text.index(i, offsetBy: candidateName.count, limitedBy: text.endIndex) ?? text.endIndex: advance
                // by candidateName's Character count, not its UTF-16 length, and clamp at text's end.
                var after = AdvanceGraphemes(boundaryArray, cursor, SwiftString.Count(candidateName));

                var beforeOk = cursor == 0 || !IsWordCharacter(PreviousCharacter(graphemes, cursor)); // :17-18
                var afterOk = after == text.Length || !IsWordCharacter(NextCharacter(graphemes, after)); // :19-20
                if (!beforeOk || !afterOk) continue;
                hit = candidate; // :21
                hitEnd = after; // :24 text.index(i, offsetBy: name(hit).count) is the same grapheme advance as `after`
                found = true;
                break;
            }

            if (found)
            {
                results.Add((cursor, hitEnd, hit)); // :25
                cursor = hitEnd; // :26
            }
            else
            {
                cursor = NextBoundary(boundaries, cursor); // :28 i = text.index(after: i)
            }
        }

        return new ValueList<(int, int, T)>(results); // :31
    }

    // :18, :20 !c.isLetter && !c.isNumber
    private static bool IsWordCharacter(string value) => SwiftCharacter.IsLetter(value) || SwiftCharacter.IsNumber(value);

    private static int NextBoundary(SortedSet<int> boundaries, int offset)
    {
        foreach (var boundary in boundaries)
            if (boundary > offset) return boundary;
        return offset + 1;
    }

    // :16 text.index(_:offsetBy:limitedBy:) ?? text.endIndex: step `count` grapheme-cluster boundaries forward
    // from `cursor` (itself always a boundary), clamped to text.Length when that would run past the end.
    private static int AdvanceGraphemes(int[] boundaryArray, int cursor, int count)
    {
        var index = Array.BinarySearch(boundaryArray, cursor);
        var target = index + count;
        return target < boundaryArray.Length ? boundaryArray[target] : boundaryArray[^1];
    }

    // :18 text[text.index(before: i)], stepping back one grapheme
    private static string PreviousCharacter(IReadOnlyList<string> graphemes, int offset)
    {
        var position = 0;
        foreach (var grapheme in graphemes)
        {
            var next = position + grapheme.Length;
            if (next >= offset) return next == offset ? grapheme : "";
            position = next;
        }
        return "";
    }

    // :20 text[after], the grapheme starting exactly at offset
    private static string NextCharacter(IReadOnlyList<string> graphemes, int offset)
    {
        var position = 0;
        foreach (var grapheme in graphemes)
        {
            var next = position + grapheme.Length;
            if (position == offset) return grapheme;
            if (position < offset && offset < next) return "";
            position = next;
        }
        return "";
    }
}
