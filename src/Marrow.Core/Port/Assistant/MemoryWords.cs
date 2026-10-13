// PORT-ONLY: Dextra (DESIGN.md rev 5, 8.1 "why do you know this", 8.3; unit AS-12; ED-NEW-dextra-as12-18). No Swift twin.
// The words that say where a note came from, who wrote it and why it waits. The recall tool and the Memory view both use them,
// so the model and the owner read the same account of a note.
using System.Text;

namespace Marrow.Core.Port.Assistant;

public static class MemoryWords
{
    /// <summary>One source in a few words: "from your chat “Release plan”", "imported from Claude 2 · CLAUDE.md", "you wrote it".</summary>
    public static string Source(NoteSource source) => source.Kind switch
    {
        NoteSourceKind.Chat => "from " + (string.IsNullOrWhiteSpace(source.Detail) ? "a chat" : $"your chat “{source.Detail}”"),
        NoteSourceKind.Mission => "from a mission" + (string.IsNullOrWhiteSpace(source.Detail) ? "" : $" (“{source.Detail}”)"),
        NoteSourceKind.Import => "imported from " + (string.IsNullOrWhiteSpace(source.Detail) ? source.Account ?? "another agent" : source.Detail),
        _ => string.Equals(source.Detail, "edited by you", StringComparison.Ordinal) ? "edited by you" : "you wrote it",
    };

    /// <summary>The note's first source in a few words, and how many more there are ("imported from Claude 1 · CLAUDE.md and 1 more").</summary>
    public static string Source(MemoryNote note)
    {
        if (note.Sources.Count == 0) return note.Author == NoteAuthor.Owner ? "you wrote it" : "source not recorded";
        var first = Source(note.Sources[0]);
        return note.Sources.Count == 1 ? first : $"{first} and {note.Sources.Count - 1} more";
    }

    /// <summary>One line for each source, for the Source button ("why do you know this").</summary>
    public static IReadOnlyList<string> SourceLines(MemoryNote note)
    {
        var lines = note.Sources.Select(Source).ToList();
        if (lines.Count == 0) lines.Add(note.Author == NoteAuthor.Owner ? "you wrote it" : "source not recorded");
        return lines;
    }

    /// <summary>Who wrote the note, as the owner reads it.</summary>
    public static string Author(MemoryNote note) => note.Author switch
    {
        NoteAuthor.Owner => "You",
        NoteAuthor.Import => string.IsNullOrWhiteSpace(note.AuthorName) ? "An import" : "Import from " + note.AuthorName,
        _ => "An assistant",
    };

    /// <summary>Why a quarantined note waits, in the owner's words.</summary>
    public static string WhyWaiting(MemoryNote note)
    {
        if (note.Quarantine is not { } quarantine) return "";
        var text = new StringBuilder(string.IsNullOrWhiteSpace(quarantine.Why) ? "It came from content nth does not trust." : quarantine.Why);
        if (!text.ToString().Contains("until you keep", StringComparison.OrdinalIgnoreCase)) text.Append(" It is not used until you keep it.");
        return text.ToString();
    }
}
