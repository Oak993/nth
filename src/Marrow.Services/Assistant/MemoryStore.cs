// PORT-ONLY: Dextra (DESIGN.md rev 5, 8.1 to 8.3, 12.4; unit AS-12; ED-NEW-dextra-as12-9). No Swift twin.
// Dextra's memory: markdown topic files and an index per assistant, plus the shared "About you" set, kept on the assistant store.
// Notes are used right away (the owner's 2026-10-08 choice: no review gate), with three exceptions: a secret is never stored, a
// note about someone else is kept only in the owner's own words, and a note drawn from untrusted content waits in quarantine,
// listed for the owner and never used until he keeps it. Delete is real: the note leaves its file, the index, the search index
// and every cached summary that quoted it. Thread-safe: every call runs under one lock; changes are announced after it is released.
using System.Security.Cryptography;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;

namespace Marrow.Services.Assistant;

/// <summary>The memory store's options.</summary>
public sealed record MemoryStoreOptions
{
    public TimeProvider Time { get; init; } = TimeProvider.System;

    /// <summary>Dextra's own id: the assistant that reads every scope (8.2). Read at every call; null: no assistant reads all, only the owner's view does.</summary>
    public Func<string?> MainAssistantId { get; init; } = () => null;

    /// <summary>The most notes a scope holds (a full memory refuses more rather than grow without end).</summary>
    public int MaxNotesPerScope { get; init; } = 2000;

    /// <summary>The most notes a scope keeps waiting for the owner, so untrusted content cannot flood the view.</summary>
    public int MaxQuarantinedPerScope { get; init; } = 100;

    /// <summary>The longest prompt block, in characters.</summary>
    public int MaxPromptChars { get; init; } = 3500;

    /// <summary>The most prompt blocks kept built.</summary>
    public int MaxCachedBlocks { get; init; } = 32;
}

public sealed partial class MemoryStore
{
    private readonly AssistantStore store;
    private readonly MemoryStoreOptions options;
    private readonly TimeProvider clock;
    private readonly Lock gate = new();
    private readonly Dictionary<string, ScopeData> scopes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> postings = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CachedBlock> blocks = new(StringComparer.Ordinal);
    private MemoryState state;

    private sealed class ScopeData(string scope)
    {
        public string Scope { get; } = scope;

        public List<StoredNote> Notes { get; set; } = [];

        public Dictionary<string, StoredNote> ById { get; } = new(StringComparer.Ordinal);

        /// <summary>The key of each note's words, for finding the scope that already holds a note (an import routes by it).</summary>
        public HashSet<string> Keys { get; } = new(StringComparer.Ordinal);

        /// <summary>The words each used note is found by.</summary>
        public Dictionary<string, IReadOnlyList<string>> Tokens { get; } = new(StringComparer.Ordinal);

        /// <summary>Files written by a newer build: not read, never rewritten.</summary>
        public HashSet<string> NewerFiles { get; } = new(StringComparer.Ordinal);

        public List<string> Problems { get; } = [];

        public long Version { get; set; }
    }

    private sealed record CachedBlock(string Text, IReadOnlySet<string> Quoted);

    /// <summary>A test's crash points: called after each memory file is written ("file:recent", "index"); a throw stands for the process dying there.</summary>
    internal Action<string>? AfterWrite { get; set; }

    public MemoryStore(AssistantStore store, MemoryStoreOptions? options = null)
    {
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        this.options = options ?? new MemoryStoreOptions();
        clock = this.options.Time;
        if (this.options.MaxNotesPerScope < 1 || this.options.MaxQuarantinedPerScope < 0 || this.options.MaxPromptChars < 200 || this.options.MaxCachedBlocks < 1)
            throw new ArgumentOutOfRangeException(nameof(options), "The memory limits must be positive.");
        store.RemoveMemoryTempFiles();
        state = MemoryState.Open(store.MemoryState());
        foreach (var scope in store.MemoryScopes()) LoadInto(scope);
    }

    /// <summary>A note changed (written, edited, released, deleted or filed). Raised on the thread that made the change, after it is on disk.</summary>
    public event Action<MemoryChange>? Changed;

    /// <summary>A write failed: the line for the board. Nothing changed; the next change tries again.</summary>
    public event Action<string>? Faulted;

    /// <summary>The notes' files that could not be read or that a newer build wrote, as lines for the Memory view.</summary>
    public IReadOnlyList<string> Problems
    {
        get
        {
            lock (gate)
            {
                return [.. scopes.Values.OrderBy(s => s.Scope, StringComparer.Ordinal).SelectMany(s =>
                    s.Problems.Concat(s.NewerFiles.Order(StringComparer.Ordinal).Select(f => $"{f}.md was written by a newer version of nth. It is left as it is and its notes are not used.")))];
            }
        }
    }

    // ---- loading ------------------------------------------------------------------------------------------------------

    private ScopeData Scope(string scope)
    {
        if (scopes.TryGetValue(scope, out var data)) return data;
        return LoadInto(scope);
    }

    private ScopeData LoadInto(string scope)
    {
        var data = new ScopeData(scope);
        var now = clock.GetUtcNow();
        var found = new List<(StoredNote Stored, string Physical)>();
        foreach (var name in store.MemoryFileNames(scope))
        {
            var file = name[..^3];
            if (file == MemoryFile.IndexFile) continue;
            string? text;
            try { text = store.ReadMemoryFile(scope, name); }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                data.Problems.Add($"{name} could not be read: {error.Message}");
                continue;
            }
            if (text is null) continue;
            var parsed = MemoryFile.Parse(scope, file, text, now);
            if (parsed.Newer) data.NewerFiles.Add(file);
            else found.AddRange(parsed.Notes.Select(n => (n, file)));
        }
        var kept = new List<StoredNote>();
        var droppedFiles = new HashSet<string>(StringComparer.Ordinal);
        foreach (var group in found.GroupBy(n => n.Stored.Note.Id, StringComparer.Ordinal))
        {
            var copies = group.ToList();
            // A fold the process died in the middle of leaves a note in two files, and a hand-edit may move one: the copy that
            // waits in quarantine wins (a note is never released by editing a file), then the filed copy over the staged one.
            var chosen = copies.FirstOrDefault(c => c.Stored.Note.Quarantined) is { Stored: not null } q ? q
                : copies.FirstOrDefault(c => c.Stored.File != MemoryFile.RecentFile) is { Stored: not null } filed ? filed : copies[0];
            kept.Add(chosen.Stored);
            // Every file a copy was found in that is not where the note belongs is rewritten without it.
            foreach (var copy in copies)
                if (!ReferenceEquals(copy.Stored, chosen.Stored) || copy.Physical != chosen.Stored.File) droppedFiles.Add(copy.Physical);
            if (copies.Count > 1 || chosen.Physical != chosen.Stored.File) droppedFiles.Add(chosen.Stored.File);
        }
        kept = [.. kept.OrderBy(n => n.Note.Created)];
        data.Notes = kept;
        foreach (var stored in kept)
        {
            data.ById[stored.Note.Id] = stored;
            data.Keys.Add(MemoryRules.NormalKey(stored.Note.Text));
        }
        scopes[scope] = data;
        Reindex(data);
        // A crash between a file and the index, or between two files of a fold, is repaired here; failing to repair is not fatal.
        try
        {
            if (droppedFiles.Count > 0) WriteScope(data, kept, droppedFiles);
            else if (data.Notes.Count > 0 && store.ReadMemoryFile(scope, MemoryFile.IndexFile + ".md") != MemoryFile.RenderIndex(kept))
                store.WriteMemoryFile(scope, MemoryFile.IndexFile + ".md", MemoryFile.RenderIndex(kept));
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            data.Problems.Add("The memory index could not be repaired: " + error.Message);
        }
        return data;
    }

    // ---- the search index -----------------------------------------------------------------------------------------------

    private void Reindex(ScopeData data)
    {
        foreach (var (id, tokens) in data.Tokens)
            foreach (var token in tokens)
                if (postings.TryGetValue(token, out var ids) && ids.Remove(id) && ids.Count == 0) postings.Remove(token);
        data.Tokens.Clear();
        foreach (var stored in data.Notes.Where(n => n.Note.Used))
        {
            var tokens = MemoryRules.Tokens(stored.Note.Text + " " + stored.Note.Topic.Replace('-', ' '));
            data.Tokens[stored.Note.Id] = tokens;
            foreach (var token in tokens)
            {
                if (!postings.TryGetValue(token, out var ids)) postings[token] = ids = new HashSet<string>(StringComparer.Ordinal);
                ids.Add(stored.Note.Id);
            }
        }
        data.Version++;
    }

    /// <summary>How many used notes the search index finds a word in (a test of "removed from the search index").</summary>
    internal int SearchHits(string word)
    {
        lock (gate) return MemoryRules.Tokens(word).Sum(t => postings.TryGetValue(t, out var ids) ? ids.Count : 0);
    }

    /// <summary>How many built prompt blocks quote the note.</summary>
    internal int CachedBlocksQuoting(string id)
    {
        lock (gate) return blocks.Values.Count(b => b.Quoted.Contains(id));
    }

    internal int CachedBlockCount
    {
        get { lock (gate) return blocks.Count; }
    }

    // ---- writing a scope --------------------------------------------------------------------------------------------------

    private void WriteScope(ScopeData data, IReadOnlyList<StoredNote> work, IEnumerable<string> touched)
    {
        // The filed topics first, then the staging file, then quarantine: a note moved from staging to a topic is in both for a
        // moment, never in neither (a duplicate id is resolved when the store opens).
        var order = touched.Distinct(StringComparer.Ordinal)
            .OrderBy(f => f == MemoryFile.RecentFile ? 2 : f == MemoryFile.QuarantineFile ? 3 : 1)
            .ThenBy(f => f, StringComparer.Ordinal)
            .ToList();
        foreach (var file in order)
        {
            if (data.NewerFiles.Contains(file)) continue;
            var notes = work.Where(n => n.File == file).ToList();
            var name = file + ".md";
            if (notes.Count == 0) store.DeleteMemoryFile(data.Scope, name);
            else store.WriteMemoryFile(data.Scope, name, MemoryFile.Render(file, notes));
            AfterWrite?.Invoke("file:" + file);
        }
        AfterWrite?.Invoke("index");
        if (work.Count == 0) store.DeleteMemoryFile(data.Scope, MemoryFile.IndexFile + ".md");
        else store.WriteMemoryFile(data.Scope, MemoryFile.IndexFile + ".md", MemoryFile.RenderIndex(work));
    }

    private void Commit(ScopeData data, List<StoredNote> work, bool textChanged)
    {
        // Oldest first, and notes written at the same moment in the order they were written (a stable sort, never by id).
        var ordered = work.OrderBy(n => n.Note.Created).ToList();
        work.Clear();
        work.AddRange(ordered);
        data.Notes = work;
        data.ById.Clear();
        data.Keys.Clear();
        foreach (var stored in work)
        {
            data.ById[stored.Note.Id] = stored;
            data.Keys.Add(MemoryRules.NormalKey(stored.Note.Text));
        }
        if (textChanged)
        {
            Reindex(data);
            blocks.Clear();
        }
    }

    /// <summary>The scope that already holds a note with these words (so the same note met again goes where it is), or null.</summary>
    internal string? ScopeHolding(string text)
    {
        var key = MemoryRules.NormalKey(MemoryRules.Clean(text));
        lock (gate) return scopes.Values.OrderBy(s => s.Scope, StringComparer.Ordinal).FirstOrDefault(s => s.Keys.Contains(key))?.Scope;
    }

    private void SaveState()
    {
        if (state.ReadOnly) return;
        store.SaveMemoryState(state.ToJson());
    }

    private void Fault(string text) => Faulted?.Invoke(text);

    private static string NewId(ISet<string> taken)
    {
        while (true)
        {
            var id = "n" + Convert.ToHexString(RandomNumberGenerator.GetBytes(6)).ToLowerInvariant();
            if (!taken.Contains(id)) return id;
        }
    }

    private static bool ValidScope(string scope) => scope == MemoryScope.Shared || AssistantStore.IsId(scope);

    private static DateTimeOffset Hour(DateTimeOffset at) => new(at.Year, at.Month, at.Day, at.Hour, 0, 0, TimeSpan.Zero);
}
