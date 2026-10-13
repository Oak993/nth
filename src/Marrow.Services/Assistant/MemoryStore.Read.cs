// PORT-ONLY: Dextra (DESIGN.md rev 5, 8.2 "Scope", 8.3; unit AS-12; ED-NEW-dextra-as12-11). No Swift twin.
// Reading memory. Each Astur reads its own notes plus the shared set; Dextra and the owner's own view read all (8.2). Only used
// notes are ever recalled or put into a prompt: a quarantined note is listed for the owner and nothing else reads it.
using System.Text;
using Marrow.Core.Port.Assistant;

namespace Marrow.Services.Assistant;

public sealed partial class MemoryStore
{
    /// <summary>
    /// The scopes <paramref name="reader"/> reads, own set first, then the shared set, then (for Dextra and the owner's view, a null
    /// reader) every other. A reader that is not a valid assistant id reads only the shared set.
    /// </summary>
    private List<ScopeData> ReadableScopes(string? reader)
    {
        var main = options.MainAssistantId();
        var all = reader is null || (main is not null && string.Equals(reader, main, StringComparison.Ordinal));
        var list = new List<ScopeData>();
        if (reader is not null && AssistantStore.IsId(reader)) list.Add(Scope(reader));
        list.Add(Scope(MemoryScope.Shared));
        if (all)
            foreach (var other in scopes.Values.Where(s => !list.Contains(s)).OrderBy(s => s.Scope, StringComparer.Ordinal).ToList()) list.Add(other);
        return list;
    }

    /// <summary>One note by id, or null; the owner's view finds any.</summary>
    public MemoryNote? Find(string id)
    {
        lock (gate)
        {
            foreach (var data in scopes.Values)
                if (data.ById.TryGetValue(id, out var stored)) return stored.Note;
            return null;
        }
    }

    /// <summary>All the notes <paramref name="reader"/> can see in its scopes, used and waiting: own set, shared set, others; by topic then age. A null reader is the owner's view.</summary>
    public IReadOnlyList<MemoryNote> Notes(string? reader)
    {
        lock (gate)
        {
            return [.. ReadableScopes(reader).SelectMany(s => s.Notes.OrderBy(n => n.Note.Topic, StringComparer.Ordinal).ThenBy(n => n.Note.Created).Select(n => n.Note))];
        }
    }

    /// <summary>The notes waiting for the owner's Keep, in the scopes <paramref name="reader"/> can see (the Memory view and the digest list them, 8.2).</summary>
    public IReadOnlyList<MemoryNote> Quarantined(string? reader) => [.. Notes(reader).Where(n => n.Quarantined)];

    public MemoryCounts Counts(string? reader)
    {
        var notes = Notes(reader);
        return new MemoryCounts(notes.Count(n => n.Used), notes.Count(n => n.Quarantined));
    }

    // ---- recall -------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// The used notes that match <paramref name="query"/>, best first (the share of the query's words a note holds, then the newer).
    /// An empty query returns the newest. Quarantined notes are never returned. Each note returned is marked used (to the hour).
    /// </summary>
    public IReadOnlyList<RecalledNote> Recall(string? reader, string? query, int limit = 10)
    {
        limit = Math.Clamp(limit, 1, 50);
        List<RecalledNote> found;
        lock (gate)
        {
            var readable = ReadableScopes(reader);
            var words = MemoryRules.Tokens(query);
            var candidates = new List<(StoredNote Stored, double Score)>();
            if (words.Count == 0)
            {
                candidates.AddRange(readable.SelectMany(s => s.Notes).Where(n => n.Note.Used).Select(n => (n, 0.0)));
            }
            else
            {
                var ids = new HashSet<string>(StringComparer.Ordinal);
                foreach (var word in words)
                    if (postings.TryGetValue(word, out var hit)) ids.UnionWith(hit);
                foreach (var data in readable)
                {
                    foreach (var id in ids)
                    {
                        if (!data.ById.TryGetValue(id, out var stored) || !stored.Note.Used || !data.Tokens.TryGetValue(id, out var tokens)) continue;
                        var matched = words.Count(tokens.Contains);
                        if (matched > 0) candidates.Add((stored, (double)matched / words.Count));
                    }
                }
            }
            found = [.. candidates
                .OrderByDescending(c => c.Score)
                .ThenByDescending(c => c.Stored.Note.LastUsed ?? c.Stored.Note.Created)
                .ThenBy(c => c.Stored.Note.Id, StringComparer.Ordinal)
                .Take(limit)
                .Select(c => new RecalledNote(c.Stored.Note, c.Score))];
            Touch(found.Select(f => f.Note));
        }
        return found;
    }

    // ---- the prompt block ------------------------------------------------------------------------------------------------

    /// <summary>
    /// The notes <paramref name="reader"/> is told at the start of a chat (8.2 "used in a prompt"): used notes only, one line each, with
    /// a Fact that may change marked so the assistant checks it before relying on it. Built once for the notes as they stand and kept
    /// until a note is written, edited, released, deleted or filed. Empty when there are none.
    /// </summary>
    public string PromptBlock(string? reader)
    {
        lock (gate)
        {
            var readable = ReadableScopes(reader);
            var key = (reader ?? "") + "|" + string.Join(",", readable.Select(s => s.Scope + ":" + s.Version));
            if (blocks.TryGetValue(key, out var cached)) return cached.Text;
            var notes = readable.SelectMany(s => s.Notes).Where(n => n.Note.Used).Select(n => n.Note)
                .OrderBy(n => KindOrder(n.Kind))
                .ThenByDescending(n => n.LastUsed ?? n.Created)
                .ThenBy(n => n.Id, StringComparer.Ordinal)
                .ToList();
            var text = new StringBuilder();
            var quoted = new HashSet<string>(StringComparer.Ordinal);
            if (notes.Count > 0)
            {
                text.Append("What you remember about the owner and his work. The owner can see, edit and delete every note, and whatever he says now beats a note. "
                    + "Use recall for more.");
                var shown = 0;
                foreach (var note in notes)
                {
                    var line = "\n- " + note.Text + (note.Kind == NoteKind.Fact && note.MayChange ? " (may have changed: check before relying on it)" : "");
                    if (text.Length + line.Length > options.MaxPromptChars) break;
                    text.Append(line);
                    quoted.Add(note.Id);
                    shown++;
                }
                if (shown < notes.Count) text.Append("\n(").Append(notes.Count - shown).Append(" more: use recall.)");
            }
            var block = text.ToString();
            if (blocks.Count >= options.MaxCachedBlocks) blocks.Remove(blocks.Keys.First());
            blocks[key] = new CachedBlock(block, quoted);
            Touch(notes.Where(n => quoted.Contains(n.Id)));
            return block;
        }
    }

    private static int KindOrder(NoteKind kind) => kind switch
    {
        NoteKind.Preference => 0,
        NoteKind.Procedure => 1,
        NoteKind.Project => 2,
        _ => 3,
    };

    // The last-used time is kept to the hour, so reading a note rewrites its file at most once an hour. A write that fails is
    // reported and the read still returns: using a note does not depend on recording that it was used.
    private void Touch(IEnumerable<MemoryNote> used)
    {
        var hour = Hour(clock.GetUtcNow());
        foreach (var group in used.Where(n => n.LastUsed is null || n.LastUsed < hour).GroupBy(n => n.Scope, StringComparer.Ordinal))
        {
            var data = Scope(group.Key);
            var work = data.Notes.ToList();
            var touched = new HashSet<string>(StringComparer.Ordinal);
            foreach (var note in group)
            {
                var index = work.FindIndex(w => w.Note.Id == note.Id);
                if (index < 0) continue;
                work[index] = work[index] with { Note = work[index].Note with { LastUsed = hour } };
                touched.Add(work[index].File);
            }
            if (touched.Count == 0) continue;
            try
            {
                foreach (var file in touched.Where(f => !data.NewerFiles.Contains(f)))
                {
                    var notes = work.Where(n => n.File == file).ToList();
                    store.WriteMemoryFile(data.Scope, file + ".md", MemoryFile.Render(file, notes));
                }
                Commit(data, work, textChanged: false);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                Fault("A memory note's last-used time could not be saved: " + error.Message);
            }
        }
    }

    // ---- forget by topic ---------------------------------------------------------------------------------------------------

    /// <summary>
    /// The notes "forget everything about ⟨topic⟩" would remove, for the owner to see first (8.3): those filed under a topic of that
    /// name, and those whose words hold every word of it. Used notes and waiting ones both. At most 200. Nothing is removed.
    /// </summary>
    public IReadOnlyList<MemoryNote> PreviewForget(string? reader, string? subject)
    {
        var about = MemoryRules.ForgetSubject(subject) ?? subject;
        var slug = MemoryRules.TopicSlug(about);
        var words = MemoryRules.Tokens(about);
        if (slug.Length == 0 && words.Count == 0) return [];
        lock (gate)
        {
            var matches = new List<MemoryNote>();
            foreach (var stored in ReadableScopes(reader).SelectMany(s => s.Notes))
            {
                var note = stored.Note;
                var topicHit = slug.Length > 0 && note.Topic == slug;
                var tokens = MemoryRules.Tokens(note.Text + " " + note.Topic.Replace('-', ' '));
                var wordsHit = words.Count > 0 && words.All(tokens.Contains);
                if (topicHit || wordsHit) matches.Add(note);
                if (matches.Count >= 200) break;
            }
            return matches;
        }
    }
}
