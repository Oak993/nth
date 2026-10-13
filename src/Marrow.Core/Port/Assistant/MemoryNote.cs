// PORT-ONLY: Dextra (DESIGN.md rev 5, 8.1 "Store", 8.2; unit AS-12; ED-NEW-dextra-as12-1). No Swift twin.
// What a memory note is: its text, kind, source ("why do you know this"), author, dates, and whether it waits in
// quarantine for the owner's Keep. Pure data: the store that holds notes and the files they live in are AS-12's services.
using Marrow.Core.Port;

namespace Marrow.Core.Port.Assistant;

/// <summary>The four kinds of note (8.1). A Fact carries a "may change" flag: when the assistant relies on one, it checks it again.</summary>
public enum NoteKind { Preference, Fact, Procedure, Project }

/// <summary>Who wrote a note (8.1): an assistant, the owner, or an import (with the account it came from).</summary>
public enum NoteAuthor { Assistant, Owner, Import }

/// <summary>What a note's source points at: a chat turn, a mission, an import file, or the owner himself.</summary>
public enum NoteSourceKind { Chat, Mission, Import, Owner }

/// <summary>
/// A link from a note to where it came from (8.1: "the source answers why do you know this"). <see cref="Target"/> is the chat
/// (and turn), mission, file path or place; <see cref="Account"/> the account an import came from; <see cref="Detail"/> a short
/// human label (a chat's title, a file's heading).
/// </summary>
public sealed record NoteSource(NoteSourceKind Kind, string Target, string? Account = null, string? Detail = null)
{
    /// <summary>The longest target and detail a source keeps (a path is the longest).</summary>
    public const int MaxTarget = 400;

    public const int MaxDetail = 160;
}

/// <summary>Why a note waits: the untrusted sources it came from (12.4), and the line shown beside Keep and Delete.</summary>
public sealed record NoteQuarantine(ValueList<TaintSource> From, string Why);

/// <summary>The scopes notes live in: an assistant's id for its own set, and the one set every assistant reads.</summary>
public static class MemoryScope
{
    /// <summary>The shared "About you" set (<c>memory/shared</c>).</summary>
    public const string Shared = "shared";
}

/// <summary>
/// One note. <see cref="Id"/> is stable for the note's life (an edit keeps it). <see cref="Scope"/> is the assistant that owns it
/// or <see cref="MemoryScope.Shared"/>. A quarantined note (<see cref="Quarantine"/> set) is listed for the owner and never used
/// in a prompt, a recall or a summary until his Keep releases it (8.2).
/// </summary>
public sealed record MemoryNote(string Id, string Scope, string Topic, NoteKind Kind, string Text)
{
    /// <summary>A Fact that can go stale: the assistant checks it before relying on it (8.1).</summary>
    public bool MayChange { get; init; }

    public NoteAuthor Author { get; init; }

    /// <summary>The assistant's id or name, or the account an import came from.</summary>
    public string? AuthorName { get; init; }

    /// <summary>Where the note came from; more than one when the same note was met again (an import from two accounts).</summary>
    public ValueList<NoteSource> Sources { get; init; } = ValueList<NoteSource>.Empty;

    public DateTimeOffset Created { get; init; }

    /// <summary>When it was last read into a prompt or a recall (kept to the hour, so a recall does not write a file each time).</summary>
    public DateTimeOffset? LastUsed { get; init; }

    public NoteQuarantine? Quarantine { get; init; }

    public bool Quarantined => Quarantine is not null;

    /// <summary>Whether the note may be used: read into a prompt, recalled, quoted in a summary.</summary>
    public bool Used => Quarantine is null;
}
