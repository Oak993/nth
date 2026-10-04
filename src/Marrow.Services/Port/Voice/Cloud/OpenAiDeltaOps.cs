// PORT-ONLY, owner-ruled addition (OD-0092; first-release voice typing, VT-16): voice-SPEC V-18.1 "transcript.text.delta events written
// into the composer as they arrive during Processing". Deltas become the same word operations the local engine's passes make (V-7.5).
using Marrow.Core.Port.Voice.Transcript;

namespace Marrow.Services.Port.Voice.Cloud;

/// <summary>Turns a growing transcript into word operations, one batch per delta.</summary>
public sealed class OpenAiDeltaOps
{
    private static readonly char[] Whitespace = [' ', '\t', '\r', '\n'];

    private readonly System.Text.StringBuilder text = new();
    private IReadOnlyList<TranscriptWord> shown = [];

    /// <summary>The words as the composer holds them now.</summary>
    public IReadOnlyList<TranscriptWord> Words => shown;

    /// <summary>The text so far.</summary>
    public string Text => text.ToString();

    /// <summary>A delta arrived: its text joins; the word it may have left unfinished is tentative, the rest are solid (V-8.4).</summary>
    public IReadOnlyList<WordOp> Delta(string delta)
    {
        text.Append(delta);
        return Apply(Split(text.ToString(), finished: false));
    }

    /// <summary>The end: <paramref name="whole"/> is the text; every word is solid.</summary>
    public IReadOnlyList<WordOp> Done(string whole)
    {
        text.Clear().Append(whole);
        return Apply(Split(whole, finished: true));
    }

    /// <summary>The words of a text: split at whitespace; unless finished, a text that does not end in whitespace leaves its last word tentative.</summary>
    public static List<TranscriptWord> Split(string text, bool finished)
    {
        var parts = text.Split(Whitespace, StringSplitOptions.RemoveEmptyEntries);
        var open = !finished && text.Length > 0 && Array.IndexOf(Whitespace, text[^1]) < 0;
        return parts.Select((w, i) => new TranscriptWord(w, open && i == parts.Length - 1)).ToList();
    }

    private IReadOnlyList<WordOp> Apply(List<TranscriptWord> wanted)
    {
        var ops = TranscriptOps.Diff(shown, wanted);
        shown = wanted;
        return ops;
    }
}
