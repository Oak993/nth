// PORT-ONLY: Dextra (DESIGN.md rev 5, 8.2, 12.4; unit AS-12; ED-NEW-dextra-as12-10). No Swift twin.
// Writing a note. The checks run in a fixed order and every route (a tool call, the background capture, an import, the Memory
// view) goes through them: the note is cleaned to one line; a secret refuses it whole; a note about someone else is kept only
// when the owner's own words hold all of it; a note the owner deleted is not brought back by anything automatic; untrusted
// content makes it quarantined (TaintGates.AdmitNote, AS-7's gate); the same note met again is joined, never doubled.
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;

namespace Marrow.Services.Assistant;

public sealed partial class MemoryStore
{
    /// <summary>A near-duplicate must share this much of its words to be the same note (and have at least <see cref="NearMinWords"/> of them).</summary>
    public const double NearSame = 0.9;

    public const int NearMinWords = 4;

    public NoteOutcome Propose(NoteProposal proposal) => ProposeMany([proposal])[0];

    /// <summary>
    /// Proposes notes together: each is checked in turn against the notes before it, and every scope the batch touches is written
    /// once. When a scope's files cannot be written, its notes are <see cref="NoteResult.Failed"/> and nothing in it changed.
    /// </summary>
    public IReadOnlyList<NoteOutcome> ProposeMany(IReadOnlyList<NoteProposal> proposals)
    {
        ArgumentNullException.ThrowIfNull(proposals);
        var outcomes = new NoteOutcome?[proposals.Count];
        var changes = new List<MemoryChange>();
        var faults = new List<string>();
        lock (gate)
        {
            var now = clock.GetUtcNow();
            foreach (var group in Enumerable.Range(0, proposals.Count).GroupBy(i => proposals[i].Scope, StringComparer.Ordinal))
            {
                if (!ValidScope(group.Key))
                {
                    foreach (var i in group) outcomes[i] = Refuse(NoteRefusal.Scope, "Not kept: that is not an assistant's memory.");
                    continue;
                }
                var data = Scope(group.Key);
                var work = data.Notes.ToList();
                var touched = new HashSet<string>(StringComparer.Ordinal);
                var added = new List<string>();
                var indexes = group.ToList();
                var seen = new BatchIndex(work);
                foreach (var i in indexes) outcomes[i] = Apply(proposals[i], data.NewerFiles, work, seen, touched, added, now);
                if (touched.Count == 0) continue;
                try
                {
                    WriteScope(data, work, touched);
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException)
                {
                    faults.Add("A memory note could not be saved: " + error.Message);
                    foreach (var i in indexes.Where(i => outcomes[i]!.Written))
                        outcomes[i] = new NoteOutcome(NoteResult.Failed, NoteRefusal.None, null, "Not kept: the memory files could not be written. Nothing changed.");
                    continue;
                }
                Commit(data, work, textChanged: true);
                changes.Add(new MemoryChange(MemoryChangeKind.Added, added));
            }
        }
        foreach (var fault in faults) Fault(fault);
        foreach (var change in changes.Where(c => c.Ids.Count > 0)) Changed?.Invoke(change);
        return outcomes!;
    }

    private static NoteOutcome Refuse(NoteRefusal why, string message) => new(NoteResult.Refused, why, null, message);

    private NoteOutcome Apply(NoteProposal p, HashSet<string> newer, List<StoredNote> work, BatchIndex seen, HashSet<string> touched, List<string> added, DateTimeOffset now)
    {
        if (!Enum.IsDefined(p.Kind)) return Refuse(NoteRefusal.Empty, "Not kept: the kind of note is not one of Preference, Fact, Procedure or Project.");
        var text = MemoryRules.Clean(p.Text);
        if (text.Length < MemoryRules.MinNoteChars) return Refuse(NoteRefusal.Empty, "Not kept: the note says nothing.");
        if (text.Length > MemoryRules.MaxNoteChars)
            return Refuse(NoteRefusal.TooLong, $"Not kept: a note is a sentence or two, up to {MemoryRules.MaxNoteChars} characters. Say it shorter.");
        // 8.2 "Never stored": a secret is refused whole, whoever wrote it and wherever it came from, even for quarantine.
        if (MemoryGuard.HoldsSecret(text))
            return Refuse(NoteRefusal.Secret, "Not kept: it holds a secret (a key, a password, a card number or a code). Secrets are never stored.");
        var admission = TaintGates.AdmitNote(p.Context, new TaintedText(text, p.SourceTaint));
        // 8.2 "Never stored": notes about a third party are kept only when the owner states them, and only what he said.
        if (!p.OwnerTyped && (p.AboutPerson || ThirdPartyNotes.Find(text, p.OwnerWords) is not null)
            && (admission.Quarantined || !ThirdPartyNotes.StatedBy(p.OwnerWords, text)))
            return Refuse(NoteRefusal.ThirdParty, "Not kept: it is about another person. Notes about people are kept only when the owner states them, in his own words.");
        if (!p.OwnerTyped && state.IsDeleted(text, now))
            return Refuse(NoteRefusal.Deleted, "Not kept: the owner deleted this note. It is written again only when he writes it himself.");

        var quarantine = admission.Quarantined || p.ForceQuarantine is not null;
        var key = MemoryRules.NormalKey(text);
        var tokens = MemoryRules.Tokens(text);
        var source = Clip(p.Source);

        var existing = seen.FindSame(key, tokens, p.Kind, quarantine);
        if (existing is { } hit)
            return Merge(hit, work, seen, touched, added, source, quarantine, admission, p, text);

        var scopeCount = work.Count(n => n.Note.Used == !quarantine);
        if (quarantine ? scopeCount >= options.MaxQuarantinedPerScope : scopeCount >= options.MaxNotesPerScope)
            return Refuse(NoteRefusal.Full, quarantine
                ? "Not kept: too many notes are waiting for the owner. He must keep or delete some first."
                : "Not kept: the memory is full. The owner can delete notes he no longer needs.");

        var file = quarantine ? MemoryFile.QuarantineFile : MemoryFile.RecentFile;
        if (newer.Contains(file))
            return new NoteOutcome(NoteResult.Failed, NoteRefusal.None, null, $"Not kept: {file}.md was written by a newer version of nth, which this version leaves as it is.");
        var id = NewId(seen.Ids);
        var topic = MemoryRules.TopicSlug(p.Topic);
        if (topic.Length == 0) topic = MemoryRules.DefaultTopic(p.Scope, p.Kind);
        var note = new MemoryNote(id, p.Scope, topic, p.Kind, text)
        {
            MayChange = p.Kind == NoteKind.Fact && p.MayChange,
            Author = p.Author,
            AuthorName = Clip(p.AuthorName, 120),
            Sources = new ValueList<NoteSource>([source]),
            Created = now,
            Quarantine = quarantine ? new NoteQuarantine(new ValueList<TaintSource>(admission.Sources.Sources), WhyQuarantined(admission, p.ForceQuarantine)) : null,
        };
        var created = new StoredNote(note, file, new System.Text.Json.Nodes.JsonObject());
        work.Add(created);
        seen.Add(created, key, tokens);
        touched.Add(file);
        added.Add(id);
        return new NoteOutcome(quarantine ? NoteResult.Quarantined : NoteResult.Kept, NoteRefusal.None, note,
            quarantine ? "Kept aside: it came from untrusted content, so it is not used until the owner keeps it." : "Kept.");
    }

    private NoteOutcome Merge(StoredNote hit, List<StoredNote> work, BatchIndex seen, HashSet<string> touched, List<string> added, NoteSource source, bool incomingQuarantine,
        NoteAdmission admission, NoteProposal p, string text)
    {
        var note = hit.Note;
        var sources = note.Sources.ToList();
        if (!sources.Contains(source) && sources.Count < MemoryFile.MaxSources) sources.Add(source);
        var updated = note with { Sources = new ValueList<NoteSource>(sources) };
        var file = hit.File;
        var exact = MemoryRules.NormalKey(note.Text) == MemoryRules.NormalKey(text);
        string message;
        if (note.Quarantined && p.OwnerTyped && exact)
        {
            // The owner types the same words a quarantined note holds: that is his Keep, by hand.
            updated = updated with { Quarantine = null };
            file = MemoryFile.RecentFile;
            touched.Add(MemoryFile.QuarantineFile);
            added.Add(note.Id);
            message = "Kept: the owner wrote the same words, so the note that was waiting is used now.";
        }
        else if (note.Quarantined)
        {
            // Only the owner's Keep (or his typing the note himself) releases a quarantined note, whatever says it again (8.2).
            if (incomingQuarantine)
            {
                var joined = note.Quarantine!.From.Concat(admission.Sources.Sources).Distinct().Take(MemoryFile.MaxSources);
                updated = updated with { Quarantine = note.Quarantine with { From = new ValueList<TaintSource>(joined) } };
            }
            message = "Already waiting for the owner.";
        }
        else
        {
            message = "Already kept.";
        }
        if (updated == note && file == hit.File) return new NoteOutcome(NoteResult.Merged, NoteRefusal.None, note, message);
        var replaced = hit with { Note = updated, File = file };
        work[work.IndexOf(hit)] = replaced;
        seen.Replace(hit, replaced);
        touched.Add(hit.File);
        touched.Add(file);
        return new NoteOutcome(NoteResult.Merged, NoteRefusal.None, updated, message);
    }

    private static string WhyQuarantined(NoteAdmission admission, string? forced)
    {
        if (forced is not null) return forced;
        var from = string.Join(", ", admission.Sources.Sources.Select(s => TaintGates.OriginWord(s.Origin)).Distinct(StringComparer.Ordinal));
        return "Drawn from " + from + ". Not used until you keep it.";
    }

    private static NoteSource Clip(NoteSource source) => source with
    {
        Target = Clip(source.Target, NoteSource.MaxTarget) ?? "",
        Account = Clip(source.Account, NoteSource.MaxTarget),
        Detail = Clip(MemoryRules.Clean(source.Detail), NoteSource.MaxDetail),
    };

    private static string? Clip(string? text, int max) => text is null ? null : text.Length <= max ? text : text[..max];

    /// <summary>
    /// The notes of one scope as one batch meets them, by key and by words, so a batch of a thousand notes does not read every note a
    /// thousand times. Kept in step as the batch adds and joins notes.
    /// </summary>
    private sealed class BatchIndex
    {
        private readonly Dictionary<string, StoredNote> byKey = new(StringComparer.Ordinal);
        private readonly Dictionary<string, IReadOnlyList<string>> tokens = new(StringComparer.Ordinal);
        private readonly List<StoredNote> all = [];

        public BatchIndex(List<StoredNote> work)
        {
            foreach (var stored in work) Add(stored, MemoryRules.NormalKey(stored.Note.Text), null);
        }

        public HashSet<string> Ids { get; } = new(StringComparer.Ordinal);

        public void Add(StoredNote stored, string key, IReadOnlyList<string>? words)
        {
            byKey.TryAdd(key, stored);
            if (words is not null) tokens[stored.Note.Id] = words;
            all.Add(stored);
            Ids.Add(stored.Note.Id);
        }

        public void Replace(StoredNote old, StoredNote updated)
        {
            var at = all.IndexOf(old);
            if (at >= 0) all[at] = updated;
            var key = MemoryRules.NormalKey(old.Note.Text);
            if (byKey.TryGetValue(key, out var held) && ReferenceEquals(held, old)) byKey[key] = updated;
        }

        /// <summary>
        /// The note that is the same as this one: the same key, or (for a note of at least <see cref="NearMinWords"/> words and the
        /// same kind) a near copy. A near copy is looked for only among notes of the same standing, so a hostile reworded copy is
        /// never folded into a used note.
        /// </summary>
        public StoredNote? FindSame(string key, IReadOnlyList<string> words, NoteKind kind, bool quarantine)
        {
            if (byKey.TryGetValue(key, out var exact)) return exact;
            if (words.Count < NearMinWords) return null;
            foreach (var stored in all)
            {
                if (stored.Note.Kind != kind || stored.Note.Quarantined != quarantine) continue;
                if (!tokens.TryGetValue(stored.Note.Id, out var held)) tokens[stored.Note.Id] = held = MemoryRules.Tokens(stored.Note.Text);
                if (MemoryRules.Similarity(words, held) >= NearSame) return stored;
            }
            return null;
        }
    }
}
