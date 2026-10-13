// PORT-ONLY: Dextra (DESIGN.md rev 5, 8.2 "Folding", 8.3; unit AS-12; ED-NEW-dextra-as12-12). No Swift twin.
// What the owner does to a note (edit it, keep one that waits, delete it for real) and the night's folding. Delete is real: the
// note's line leaves its file (a temp-then-rename write), the index is rewritten without it, the search index and every built
// prompt block that quoted it are dropped, and only a salted hash of its words is kept so nothing automatic writes it back (8.3).
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;

namespace Marrow.Services.Assistant;

public sealed partial class MemoryStore
{
    private (ScopeData Data, StoredNote Stored)? Locate(string id)
    {
        foreach (var data in scopes.Values)
            if (data.ById.TryGetValue(id, out var stored)) return (data, stored);
        return null;
    }

    // ---- edit -------------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// The owner changes a note's words (and, when given, its kind, its "may change" flag or its topic). The note keeps its id and
    /// its sources, gains "edited by you" as one more, and becomes the owner's own. The new words pass the secret check; they are
    /// his words, so the third-party rule does not apply. A note that waits in quarantine keeps waiting: only Keep releases it.
    /// </summary>
    public NoteOutcome Edit(string id, string text, NoteKind? kind = null, bool? mayChange = null, string? topic = null)
    {
        string? failed = null;
        NoteOutcome outcome;
        lock (gate)
        {
            if (Locate(id) is not { } found) return Refuse(NoteRefusal.NotFound, "That note is not there.");
            var clean = MemoryRules.Clean(text);
            if (clean.Length < MemoryRules.MinNoteChars) return Refuse(NoteRefusal.Empty, "A note needs some words.");
            if (clean.Length > MemoryRules.MaxNoteChars) return Refuse(NoteRefusal.TooLong, $"A note is a sentence or two, up to {MemoryRules.MaxNoteChars} characters.");
            if (MemoryGuard.HoldsSecret(clean)) return Refuse(NoteRefusal.Secret, "That holds a secret (a key, a password, a card number or a code). Secrets are never stored.");
            var (data, stored) = found;
            var note = stored.Note;
            var newKind = kind is { } k && Enum.IsDefined(k) ? k : note.Kind;
            var newTopic = topic is null ? note.Topic : MemoryRules.TopicSlug(topic) is { Length: > 0 } slug ? slug : MemoryRules.DefaultTopic(note.Scope, newKind);
            var sources = note.Sources.ToList();
            var owner = new NoteSource(NoteSourceKind.Owner, "memory view", Detail: "edited by you");
            if (!sources.Contains(owner) && sources.Count < MemoryFile.MaxSources) sources.Add(owner);
            var edited = note with
            {
                Text = clean,
                Kind = newKind,
                Topic = newTopic,
                MayChange = newKind == NoteKind.Fact && (mayChange ?? note.MayChange),
                Author = NoteAuthor.Owner,
                AuthorName = null,
                Sources = new ValueList<NoteSource>(sources),
            };
            var file = stored.File;
            if (edited.Used && newTopic != note.Topic && file != MemoryFile.RecentFile && !data.NewerFiles.Contains(MemoryFile.RecentFile)) file = MemoryFile.RecentFile; // filed again tonight
            var work = data.Notes.ToList();
            work[work.IndexOf(stored)] = stored with { Note = edited, File = file };
            try
            {
                WriteScope(data, work, [stored.File, file]);
                Commit(data, work, textChanged: true);
                outcome = new NoteOutcome(edited.Quarantined ? NoteResult.Quarantined : NoteResult.Kept, NoteRefusal.None, edited, "Saved.");
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                failed = "A memory note could not be saved: " + error.Message;
                outcome = new NoteOutcome(NoteResult.Failed, NoteRefusal.None, null, "Not saved: the memory files could not be written. Nothing changed.");
            }
        }
        if (failed is not null) Fault(failed);
        else Changed?.Invoke(new MemoryChange(MemoryChangeKind.Edited, [id]));
        return outcome;
    }

    // ---- keep -------------------------------------------------------------------------------------------------------------------

    /// <summary>The owner keeps notes that wait in quarantine: they are used from now on (8.2). Returns the ids released.</summary>
    public IReadOnlyList<string> Keep(IReadOnlyCollection<string> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);
        var released = new List<string>();
        string? failed = null;
        lock (gate)
        {
            foreach (var group in ids.Distinct(StringComparer.Ordinal).Select(i => (Id: i, Found: Locate(i))).Where(x => x.Found is { Stored.Note.Quarantined: true }).GroupBy(x => x.Found!.Value.Data))
            {
                var data = group.Key;
                if (data.NewerFiles.Contains(MemoryFile.RecentFile) || data.NewerFiles.Contains(MemoryFile.QuarantineFile)) continue;
                var work = data.Notes.ToList();
                var touched = new HashSet<string>(StringComparer.Ordinal) { MemoryFile.QuarantineFile, MemoryFile.RecentFile };
                var room = options.MaxNotesPerScope - work.Count(n => n.Note.Used);
                var these = new List<string>();
                foreach (var (id, found) in group)
                {
                    if (these.Count >= room) break;
                    var index = work.FindIndex(w => w.Note.Id == id);
                    work[index] = work[index] with { Note = work[index].Note with { Quarantine = null }, File = MemoryFile.RecentFile };
                    these.Add(id);
                }
                if (these.Count == 0) continue;
                try
                {
                    WriteScope(data, work, touched);
                    Commit(data, work, textChanged: true);
                    released.AddRange(these);
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException)
                {
                    failed = "Notes could not be kept: " + error.Message;
                }
            }
        }
        if (failed is not null) Fault(failed);
        if (released.Count > 0) Changed?.Invoke(new MemoryChange(MemoryChangeKind.Kept, released));
        return released;
    }

    public bool Keep(string id) => Keep([id]).Count == 1;

    // ---- delete -----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Deletes notes for real (8.3). The state's hash of each note's words is written first (a crash after it loses nothing), then
    /// the files and the index without the notes; the search index and the built prompt blocks drop them. A reader other than the
    /// owner's view (<paramref name="reader"/> set) deletes only notes it can read. Ids that name no note are ignored.
    /// </summary>
    public DeleteReport Delete(IReadOnlyCollection<string> ids, string? reader = null)
    {
        ArgumentNullException.ThrowIfNull(ids);
        var removed = new List<string>();
        string? failed = null;
        lock (gate)
        {
            var allowed = reader is null ? null : ReadableScopes(reader);
            var targets = ids.Distinct(StringComparer.Ordinal).Select(i => (Id: i, Found: Locate(i)))
                .Where(x => x.Found is not null && (allowed is null || allowed.Contains(x.Found.Value.Data))).ToList();
            if (targets.Count == 0) return new DeleteReport(0);
            var now = clock.GetUtcNow();
            var added = targets.Where(t => !state.IsDeleted(t.Found!.Value.Stored.Note.Text, now)).ToList();
            foreach (var (_, found) in added) state.AddDeleted(found!.Value.Stored.Note.Text, now);
            try { SaveState(); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                // The record that the owner deleted these could not be written, so nothing is deleted: the hashes added are taken back.
                foreach (var (_, found) in added) state.RemoveDeleted(found!.Value.Stored.Note.Text);
                failed = "The memory could not be updated: " + error.Message;
                targets.Clear();
            }
            var undone = false;
            foreach (var group in targets.GroupBy(t => t.Found!.Value.Data))
            {
                var data = group.Key;
                var drop = group.Select(g => g.Id).ToHashSet(StringComparer.Ordinal);
                var work = data.Notes.Where(n => !drop.Contains(n.Note.Id)).ToList();
                var touched = data.Notes.Where(n => drop.Contains(n.Note.Id)).Select(n => n.File).ToHashSet(StringComparer.Ordinal);
                try
                {
                    WriteScope(data, work, touched);
                    Commit(data, work, textChanged: true);
                    removed.AddRange(drop);
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException)
                {
                    failed = "Notes could not be deleted: " + error.Message;
                    foreach (var (_, found) in group.Where(g => added.Contains(g))) state.RemoveDeleted(found!.Value.Stored.Note.Text);
                    undone = true;
                }
            }
            if (undone)
            {
                try { SaveState(); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException) { Fault("The memory state could not be saved: " + error.Message); }
            }
        }
        if (failed is not null) Fault(failed);
        if (removed.Count > 0) Changed?.Invoke(new MemoryChange(MemoryChangeKind.Deleted, removed));
        return new DeleteReport(removed.Count, Failed: failed is not null && removed.Count == 0);
    }

    // ---- folding ----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// The night's folding (8.2): every used note in the staging file is filed under its topic, and notes that say the same thing are
    /// joined into the older one (same words, or at least 90% of four or more words alike, and the same kind). A used note is never
    /// joined with one that waits in quarantine. Running it again changes nothing.
    /// </summary>
    public FoldReport Fold()
    {
        var filed = 0;
        var merged = 0;
        var changed = new List<string>();
        var failed = new List<string>();
        lock (gate)
        {
            state.Prune(clock.GetUtcNow());
            foreach (var data in scopes.Values.OrderBy(s => s.Scope, StringComparer.Ordinal).ToList())
            {
                var work = data.Notes.ToList();
                var touched = new HashSet<string>(StringComparer.Ordinal);
                var scopeFiled = 0;
                var scopeMerged = 0;
                // 1. File the staged notes under their topics.
                for (var i = 0; i < work.Count; i++)
                {
                    var stored = work[i];
                    if (stored.File != MemoryFile.RecentFile || !stored.Note.Used) continue;
                    var topic = MemoryRules.IsTopic(stored.Note.Topic) ? stored.Note.Topic : MemoryRules.DefaultTopic(data.Scope, stored.Note.Kind);
                    if (data.NewerFiles.Contains(topic) || data.NewerFiles.Contains(MemoryFile.RecentFile)) continue;
                    work[i] = stored with { File = topic, Note = stored.Note with { Topic = topic } };
                    changed.Add(stored.Note.Id);
                    touched.Add(MemoryFile.RecentFile);
                    touched.Add(topic);
                    scopeFiled++;
                }
                // 2. Join notes that say the same thing, the older one staying.
                work = [.. work.OrderBy(n => n.Note.Created)];
                for (var i = 0; i < work.Count; i++)
                {
                    var tokens = MemoryRules.Tokens(work[i].Note.Text);
                    var key = MemoryRules.NormalKey(work[i].Note.Text);
                    for (var j = i + 1; j < work.Count; j++)
                    {
                        var later = work[j];
                        if (later.Note.Quarantined != work[i].Note.Quarantined) continue;
                        var same = MemoryRules.NormalKey(later.Note.Text) == key
                            || (!later.Note.Quarantined && later.Note.Kind == work[i].Note.Kind && tokens.Count >= NearMinWords
                                && MemoryRules.Similarity(tokens, MemoryRules.Tokens(later.Note.Text)) >= NearSame);
                        if (!same) continue;
                        work[i] = work[i] with { Note = Join(work[i].Note, later.Note) };
                        touched.Add(work[i].File);
                        touched.Add(later.File);
                        work.RemoveAt(j);
                        j--;
                        scopeMerged++;
                    }
                }
                if (touched.Count == 0) continue;
                try
                {
                    WriteScope(data, work, touched);
                    Commit(data, work, textChanged: true);
                    filed += scopeFiled;
                    merged += scopeMerged;
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException)
                {
                    failed.Add("The memory could not be filed: " + error.Message);
                }
            }
        }
        foreach (var line in failed) Fault(line);
        if (filed + merged > 0) Changed?.Invoke(new MemoryChange(MemoryChangeKind.Folded, changed));
        return new FoldReport(filed, merged);
    }

    // The older note takes the newer one's sources and use time; its words and standing stay.
    private static MemoryNote Join(MemoryNote older, MemoryNote newer)
    {
        var sources = older.Sources.ToList();
        foreach (var s in newer.Sources)
            if (!sources.Contains(s) && sources.Count < MemoryFile.MaxSources) sources.Add(s);
        var used = older.LastUsed is { } a && newer.LastUsed is { } b ? (a > b ? a : b) : older.LastUsed ?? newer.LastUsed;
        return older with { Sources = new ValueList<NoteSource>(sources), LastUsed = used, MayChange = older.MayChange || newer.MayChange };
    }

    /// <summary>Folds once a local day: does nothing when it already ran today. Returns the report, or null when it did not run.</summary>
    public FoldReport? FoldIfDue()
    {
        var today = clock.GetLocalNow().ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        lock (gate)
        {
            if (state.Folded == today) return null;
        }
        var report = Fold();
        lock (gate)
        {
            state.Folded = today;
            try { SaveState(); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { Fault("The memory state could not be saved: " + error.Message); }
        }
        return report;
    }

    // ---- state for the import ---------------------------------------------------------------------------------------------------

    internal ImportedFile? ImportedFileOf(string sourceKey, string path)
    {
        lock (gate) return state.File(sourceKey, path);
    }

    internal void RememberImportedFiles(string sourceKey, IReadOnlyDictionary<string, ImportedFile> files)
    {
        if (files.Count == 0) return;
        lock (gate)
        {
            foreach (var (path, file) in files) state.SetFile(sourceKey, path, file);
            try { SaveState(); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { Fault("The import's record could not be saved: " + error.Message); }
        }
    }
}
