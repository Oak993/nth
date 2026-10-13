// PORT-ONLY: Dextra (DESIGN.md rev 5, 8.1 to 8.4; unit AS-12; ED-NEW-dextra-as12-7). No Swift twin.
// What goes into and comes out of the memory store: a proposed note and the store's answer, a recalled note, the reports of a
// fold and a delete, and the changes the store announces.
using Marrow.Core.Port.Assistant;

namespace Marrow.Services.Assistant;

public enum NoteResult
{
    /// <summary>Written and used right away (the owner's own words and trusted work have no review gate).</summary>
    Kept,

    /// <summary>Written, but waiting for the owner's Keep: it came from untrusted content (8.2).</summary>
    Quarantined,

    /// <summary>The same note was already there; its sources were joined.</summary>
    Merged,

    /// <summary>Not written; <see cref="NoteOutcome.Refusal"/> says why.</summary>
    Refused,

    /// <summary>Not written because the files could not be: nothing changed.</summary>
    Failed,
}

public enum NoteRefusal { None, Empty, TooLong, Secret, ThirdParty, Deleted, Full, NotFound, Scope }

/// <summary>The store's answer to a proposed note, edit or release. <see cref="Message"/> is plain English for the model or the owner.</summary>
public sealed record NoteOutcome(NoteResult Result, NoteRefusal Refusal, MemoryNote? Note, string Message)
{
    public bool Written => Result is NoteResult.Kept or NoteResult.Quarantined or NoteResult.Merged;
}

/// <summary>
/// A note someone wants kept. Everything the rules need travels with it: the scope it is for, the taint of the turn it was drawn in
/// and of its source, and the owner's own words it was drawn from (the third-party rule's evidence, 8.2).
/// </summary>
public sealed record NoteProposal(string Text, NoteKind Kind, string Scope, NoteSource Source)
{
    /// <summary>The topic hint ("writing style"); empty files the note under its kind's default.</summary>
    public string? Topic { get; init; }

    public bool MayChange { get; init; }

    public NoteAuthor Author { get; init; } = NoteAuthor.Assistant;

    /// <summary>The assistant's id or the import's account.</summary>
    public string? AuthorName { get; init; }

    /// <summary>The context the note was drawn in (12.4): a tainted turn makes the note quarantined.</summary>
    public TaintContext Context { get; init; } = TaintContext.Clean;

    /// <summary>The taint of the text the note came from (a web page it summarises).</summary>
    public Taint SourceTaint { get; init; } = Taint.Clean;

    /// <summary>What the owner said that this note comes from (his recent messages); the third-party rule compares against it.</summary>
    public string? OwnerWords { get; init; }

    /// <summary>The proposer says the note is about someone other than the owner.</summary>
    public bool AboutPerson { get; init; }

    /// <summary>The owner typed it himself in the Memory view: his own words, and a note he deleted earlier may be written again.</summary>
    public bool OwnerTyped { get; init; }

    /// <summary>Quarantine it whatever its taint says, with this reason (an import the scanner flagged, or one found after the owner's own).</summary>
    public string? ForceQuarantine { get; init; }
}

/// <summary>A note a search found, and how well it matched (0 to 1 and a little more for recent ones).</summary>
public sealed record RecalledNote(MemoryNote Note, double Score);

/// <summary>What a night's folding did: notes filed under their topics, and duplicates joined into the older note.</summary>
public sealed record FoldReport(int Filed, int Merged)
{
    public bool Changed => Filed + Merged > 0;
}

/// <summary>What a delete did. <see cref="Failed"/>: the files could not be written and nothing was removed.</summary>
public sealed record DeleteReport(int Removed, bool Failed = false);

public enum MemoryChangeKind { Added, Edited, Kept, Deleted, Folded }

/// <summary>A change the store announces after it is written, so the Memory view, the digest and anything that cached a summary can follow.</summary>
public sealed record MemoryChange(MemoryChangeKind Kind, IReadOnlyList<string> Ids);

/// <summary>How many notes a reader can use, and how many wait for the owner.</summary>
public sealed record MemoryCounts(int Used, int Waiting);
