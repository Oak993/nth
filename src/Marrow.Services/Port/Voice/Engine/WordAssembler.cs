// PORT-ONLY, owner-ruled addition (OD-0091, OD-0092, OD-0099 Q-V-9 C; first-release voice typing, VT-6): words from segments (V-7.2).
using Marrow.Core.Port.Voice.Seams;

namespace Marrow.Services.Port.Voice.Engine;

/// <summary>
/// V-7.2: a token beginning with a space starts a word; a word's start and end are its first and last token's timestamps; punctuation stays attached. Scripts written
/// without spaces give one word per token run: a segment starts a new run (ENGINEERING DECISION), and special tokens belong to no word.
/// </summary>
public static class WordAssembler
{
    public static IReadOnlyList<HypothesisWord> Assemble(IReadOnlyList<BackendSegment> segments)
    {
        var words = new List<HypothesisWord>();
        foreach (var segment in segments)
        {
            var text = new System.Text.StringBuilder();
            double start = 0, end = 0;
            void Flush()
            {
                if (text.Length > 0) words.Add(new HypothesisWord(text.ToString(), start, Math.Max(start, end)));
                text.Clear();
            }

            foreach (var token in segment.Tokens)
            {
                if (token.IsSpecial) continue;
                var startsWord = token.Text.Length > 0 && token.Text[0] == ' ';
                if (startsWord) Flush();
                var piece = token.Text.TrimStart(' ');
                if (piece.Length == 0) continue;
                if (text.Length == 0) start = token.Start;
                text.Append(piece);
                end = token.End;
            }
            Flush();
        }
        return words;
    }
}
