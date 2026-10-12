// PORT-ONLY, owner-ruled addition (OD-0091, OD-0092; first-release voice typing, VT-6, V-7.2): the token texts of a segment made whole again (ED-NEW-voicefix-2).
using System.Text;

namespace Marrow.Services.Port.Voice.Engine;

/// <summary>
/// Whisper's tokens are byte pieces: a Devanagari, Urdu or emoji character is often split across two or three of them (1476 of the 50257 text tokens of the shipped
/// large-v3-turbo vocabulary are not whole UTF-8 on their own). Whisper.net decodes every token's bytes by itself, so each piece of a split character comes out as U+FFFD,
/// and the words assembled from the tokens read "������". The segment's own text is decoded from all its bytes at once and is whole; whisper.cpp builds it from the same
/// text tokens in the same order. <see cref="Repair"/> lays the tokens over it: the whole parts of each token must match it where they fall, and every run of broken pieces
/// takes the characters between its neighbours (a run of n replacement characters stands for n to 3n bytes), given to the token the run starts in, so a word keeps the
/// time of its first token. A text that cannot be laid over loses only its replacement characters: a U+FFFD is never what anyone said.
/// </summary>
public static class WhisperTokenText
{
    private const char Replacement = '�';

    public static IReadOnlyList<BackendToken> Repair(IReadOnlyList<BackendToken> tokens, string? segmentText)
    {
        var broken = false;
        foreach (var token in tokens)
            if (!token.IsSpecial && token.Text.Contains(Replacement, StringComparison.Ordinal)) { broken = true; break; }
        if (!broken) return tokens;
        return Align(tokens, segmentText) ?? Stripped(tokens);
    }

    private static BackendToken[] Stripped(IReadOnlyList<BackendToken> tokens) =>
        [.. tokens.Select(t => t.IsSpecial ? t : t with { Text = t.Text.Replace(Replacement.ToString(), "", StringComparison.Ordinal) })];

    // one piece of the token stream: a whole run of text, or a run of replacement characters (Gap > 0), and the token it starts in
    private readonly record struct Piece(int Owner, string? Known, int Gap);

    private static BackendToken[]? Align(IReadOnlyList<BackendToken> tokens, string? text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        var pieces = new List<Piece>();
        for (var i = 0; i < tokens.Count; i++)
        {
            if (tokens[i].IsSpecial) continue;
            var t = tokens[i].Text;
            var at = 0;
            while (at < t.Length)
            {
                var gap = 0;
                while (at < t.Length && t[at] == Replacement) { gap++; at++; }
                if (gap > 0)
                {
                    // replacement runs that meet across tokens are one broken stretch of bytes
                    if (pieces.Count > 0 && pieces[^1].Gap > 0) pieces[^1] = pieces[^1] with { Gap = pieces[^1].Gap + gap };
                    else pieces.Add(new Piece(i, null, gap));
                    continue;
                }
                var end = t.IndexOf(Replacement, at);
                if (end < 0) end = t.Length;
                pieces.Add(new Piece(i, t[at..end], 0));
                at = end;
            }
        }

        var built = new StringBuilder?[tokens.Count];
        var p = 0;
        for (var k = 0; k < pieces.Count; k++)
        {
            var piece = pieces[k];
            string part;
            if (piece.Known is { } known)
            {
                if (p + known.Length > text.Length || string.CompareOrdinal(text, p, known, 0, known.Length) != 0) return null;
                part = known;
            }
            else
            {
                var next = k + 1 < pieces.Count ? pieces[k + 1].Known : null;
                var end = next is null ? text.Length : GapEnd(text, p, next, piece.Gap);
                if (end <= p) return null;
                part = text[p..end];
            }
            p += part.Length;
            (built[piece.Owner] ??= new StringBuilder()).Append(part);
        }

        var result = new BackendToken[tokens.Count];
        for (var i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            if (token.IsSpecial) { result[i] = token; continue; }
            var whole = built[i]?.ToString() ?? "";
            // a character the segment text itself could not decode (split across two segments) is dropped, never shown
            result[i] = token with { Text = whole.Replace(Replacement.ToString(), "", StringComparison.Ordinal) };
        }
        return result;
    }

    // where a broken stretch of `gap` replacement characters starting at `from` ends: the first place the next whole text follows after 1 to 3 bytes per replacement character
    private static int GapEnd(string text, int from, string next, int gap)
    {
        var search = from + 1;
        while (search <= text.Length - next.Length)
        {
            var at = text.IndexOf(next, search, StringComparison.Ordinal);
            if (at < 0) return -1;
            if (!char.IsLowSurrogate(text[at]))
            {
                var bytes = Encoding.UTF8.GetByteCount(text.AsSpan(from, at - from));
                if (bytes > 3 * gap) return -1;
                if (bytes >= gap) return at;
            }
            search = at + 1;
        }
        return -1;
    }
}
