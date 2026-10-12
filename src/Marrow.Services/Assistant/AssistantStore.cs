// PORT-ONLY: Dextra, nth's assistant (DESIGN.md revision 5, section 15.3; unit AS-3). No Swift counterpart.
// ED-NEW-dextra-as3-2: the store's layout, its temp-then-rename writes, the mission journal and the store lock.
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Marrow.Services.Assistant;

/// <summary>A mission as the store holds it: its id, the number of its last save and its body (a copy).</summary>
public sealed record StoredMission(string Id, long Sequence, JsonObject Body);

/// <summary>What <see cref="AssistantStore.Open"/> found and repaired.</summary>
public sealed record AssistantStoreOpenReport(
    IReadOnlyList<string> ReplayedFromJournal,
    IReadOnlyList<string> Interrupted,
    IReadOnlyList<string> Damaged,
    int TempFilesRemoved);

/// <summary>The store's options. <see cref="InterruptedMission"/> is the mission model's own rule (AS-5).</summary>
public sealed record AssistantStoreOptions
{
    /// <summary>
    /// Applied to every mission at open, when nothing of this process can be running yet: the mission's body as it
    /// was saved, and the body to save instead when the mission was live (15.3: "an interrupted mission comes back
    /// as Paused with Resume?"), or null to keep it as it is. The store does not know mission states (AS-1 owns them).
    /// </summary>
    public Func<JsonObject, JsonObject?>? InterruptedMission { get; init; }

    /// <summary>A mission's journal is folded into its snapshot after this many saves (and at open and close).</summary>
    public int CompactEvery { get; init; } = 16;

    /// <summary>The feed keeps this many days, today included (15.3: 30 days).</summary>
    public int FeedDays { get; init; } = 30;

    public TimeProvider Time { get; init; } = TimeProvider.System;
}

/// <summary>A second store on the same folder: refused, so two writers never interleave one journal.</summary>
public sealed class AssistantStoreBusyException(string root, Exception inner)
    : IOException($"The assistant's store at \"{root}\" is already open.", inner);

/// <summary>The points a test stops a write at, as a crash would (ED-NEW-dextra-as3-2).</summary>
internal enum AssistantStoreFault
{
    /// <summary>Half of a temporary file's bytes are on disk.</summary>
    TempPartlyWritten,

    /// <summary>The temporary file is complete and flushed; the rename has not happened.</summary>
    TempWritten,

    /// <summary>A mission's journal line is on disk; its snapshot is not written.</summary>
    JournalAppended,

    /// <summary>A compaction wrote the snapshot; the journal is not yet emptied.</summary>
    SnapshotWritten,
}

/// <summary>
/// The assistant's files under <see cref="IAssistantStorageRoots.AssistantRoot"/> (DESIGN.md 15.3), never under
/// <c>Storage.Root</c> directly:
/// <code>
/// assistants/&lt;id&gt;/profile.json  duties.json  folder/
/// memory/&lt;id&gt;/  memory/shared/
/// missions/&lt;id&gt;.json  missions/&lt;id&gt;.journal
/// feed/yyyy-mm-dd.jsonl            (30 days)
/// teach/&lt;id&gt;/
/// worktrees/&lt;mission-id&gt;/
/// </code>
/// Every whole-file write goes to a temporary file beside its target, is flushed to disk, and is then renamed over the
/// target, so a crash leaves the old file or the new one. A mission save is first appended to that mission's journal
/// (flushed to disk) and folded into the snapshot later, so a crash between the two resumes from the journal. One store
/// holds the folder: <c>.store.lock</c> is opened with no sharing for its life, so a second store is refused.
/// Thread-safe: every call runs under one lock.
/// </summary>
public sealed partial class AssistantStore : IDisposable
{
    public const string LockFileName = ".store.lock";
    private const string TempSuffix = ".tmp";
    private const int SnapshotFormat = 1;

    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };
    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9", "shared",
    };

    private readonly Lock gate = new();
    private readonly AssistantStoreOptions options;
    private readonly Dictionary<string, (long Sequence, JsonObject Body)> missions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> journalLines = new(StringComparer.Ordinal);
    private FileStream? lockHandle;

    [GeneratedRegex(@"\A[a-z0-9][a-z0-9_-]{0,63}\z")] private static partial Regex IdShape();

    [GeneratedRegex(@"\A\d{4}-\d{2}-\d{2}\.jsonl\z")] private static partial Regex FeedFileShape();

    private AssistantStore(string root, AssistantStoreOptions options, FileStream lockHandle)
    {
        Root = root;
        this.options = options;
        this.lockHandle = lockHandle;
    }

    public string Root { get; }

    public AssistantStoreOpenReport OpenReport { get; private set; } = new([], [], [], 0);

    /// <summary>A test's crash points; null in the product (ED-NEW-dextra-as3-2).</summary>
    internal Action<AssistantStoreFault>? Faults { get; set; }

    private string MissionsDirectory => Path.Combine(Root, "missions");

    private string FeedDirectory => Path.Combine(Root, "feed");

    /// <summary>
    /// Opens the store under <paramref name="roots"/>' assistant folder: takes the lock, removes temporary files a
    /// crash left, replays every mission's journal, applies <see cref="AssistantStoreOptions.InterruptedMission"/> and
    /// folds each journal into its snapshot. Throws <see cref="AssistantStoreBusyException"/> while another store holds
    /// the folder.
    /// </summary>
    public static AssistantStore Open(IAssistantStorageRoots roots, AssistantStoreOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(roots);
        options ??= new AssistantStoreOptions();
        if (options.CompactEvery < 1) throw new ArgumentOutOfRangeException(nameof(options), "CompactEvery must be at least 1.");
        if (options.FeedDays < 1) throw new ArgumentOutOfRangeException(nameof(options), "FeedDays must be at least 1.");
        var root = Path.GetFullPath(roots.AssistantRoot);
        Directory.CreateDirectory(root);
        FileStream handle;
        try
        {
            handle = new FileStream(Path.Combine(root, LockFileName), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException error)
        {
            throw new AssistantStoreBusyException(root, error);
        }
        var store = new AssistantStore(root, options, handle);
        try
        {
            store.Load();
            return store;
        }
        catch
        {
            store.Release();
            throw;
        }
    }

    // ---- Missions (journaled) ----

    /// <summary>Every mission, ordered by id.</summary>
    public IReadOnlyList<StoredMission> Missions()
    {
        lock (gate)
        {
            ThrowIfClosed();
            return missions.OrderBy(m => m.Key, StringComparer.Ordinal)
                .Select(m => new StoredMission(m.Key, m.Value.Sequence, (JsonObject)m.Value.Body.DeepClone()))
                .ToList();
        }
    }

    public StoredMission? Mission(string id)
    {
        CheckId(id);
        lock (gate)
        {
            ThrowIfClosed();
            return missions.TryGetValue(id, out var held) ? new StoredMission(id, held.Sequence, (JsonObject)held.Body.DeepClone()) : null;
        }
    }

    /// <summary>
    /// Saves a mission: its new body is appended to its journal and flushed to disk before this returns, and the journal
    /// is folded into the snapshot every <see cref="AssistantStoreOptions.CompactEvery"/> saves. Returns the save's number.
    /// </summary>
    public long SaveMission(string id, JsonObject body)
    {
        CheckId(id);
        ArgumentNullException.ThrowIfNull(body);
        lock (gate)
        {
            ThrowIfClosed();
            var copy = (JsonObject)body.DeepClone();
            var sequence = (missions.TryGetValue(id, out var held) ? held.Sequence : 0) + 1;
            AppendJournal(id, new JsonObject { ["seq"] = sequence, ["mission"] = copy.DeepClone() });
            missions[id] = (sequence, copy);
            Faults?.Invoke(AssistantStoreFault.JournalAppended);
            journalLines[id] = journalLines.GetValueOrDefault(id) + 1;
            if (journalLines[id] >= options.CompactEvery) Compact(id);
            return sequence;
        }
    }

    /// <summary>
    /// Deletes a mission for real: a tombstone goes to its journal first, so a crash part way still ends deleted, then
    /// the snapshot and the journal are removed. Returns false when there was no such mission.
    /// </summary>
    public bool DeleteMission(string id)
    {
        CheckId(id);
        lock (gate)
        {
            ThrowIfClosed();
            if (!missions.TryGetValue(id, out var held)) return false;
            AppendJournal(id, new JsonObject { ["seq"] = held.Sequence + 1, ["deleted"] = true });
            RemoveMissionFiles(id);
            missions.Remove(id);
            journalLines.Remove(id);
            return true;
        }
    }

    // ---- Assistants: profile, duties and folders ----

    /// <summary>The ids that have a profile, ordered.</summary>
    public IReadOnlyList<string> AssistantIds()
    {
        lock (gate)
        {
            ThrowIfClosed();
            var directory = Path.Combine(Root, "assistants");
            if (!Directory.Exists(directory)) return [];
            return Directory.EnumerateDirectories(directory)
                .Select(Path.GetFileName)
                .OfType<string>()
                .Where(name => IsId(name) && File.Exists(Path.Combine(directory, name, "profile.json")))
                .Order(StringComparer.Ordinal)
                .ToList();
        }
    }

    public void SaveProfile(string assistantId, JsonObject profile) => SaveDocument(AssistantFile(assistantId, "profile.json"), profile);

    public JsonObject? Profile(string assistantId) => ReadDocument(AssistantFile(assistantId, "profile.json")) as JsonObject;

    public void SaveDuties(string assistantId, JsonNode duties) => SaveDocument(AssistantFile(assistantId, "duties.json"), duties);

    public JsonNode? Duties(string assistantId) => ReadDocument(AssistantFile(assistantId, "duties.json"));

    /// <summary>The assistant's home folder (<c>assistants/&lt;id&gt;/folder</c>), created.</summary>
    public string AssistantFolder(string assistantId) => Created(Path.Combine(Root, "assistants", Checked(assistantId), "folder"));

    /// <summary>The assistant's memory folder (<c>memory/&lt;id&gt;</c>), created; AS-12's notes live there.</summary>
    public string MemoryFolder(string assistantId) => Created(Path.Combine(Root, "memory", Checked(assistantId)));

    /// <summary>The memory every assistant shares (<c>memory/shared</c>), created.</summary>
    public string SharedMemoryFolder() => Created(Path.Combine(Root, "memory", "shared"));

    /// <summary>The assistant's teach recordings until approved (<c>teach/&lt;id&gt;</c>), created.</summary>
    public string TeachFolder(string assistantId) => Created(Path.Combine(Root, "teach", Checked(assistantId)));

    /// <summary>
    /// Where a mission's worktree or snapshot copy goes (<c>worktrees/&lt;mission-id&gt;</c>, 12.6a). Only the parent is
    /// created: <c>git worktree add</c> wants a folder that does not exist yet.
    /// </summary>
    public string MissionWorktreeFolder(string missionId)
    {
        var parent = Created(Path.Combine(Root, "worktrees"));
        return Path.Combine(parent, Checked(missionId));
    }

    // ---- Feed (feed/yyyy-mm-dd.jsonl, 30 days) ----

    /// <summary>Appends events to the file of <paramref name="at"/>'s local day, one compact JSON line each.</summary>
    public void AppendFeed(DateTimeOffset at, IReadOnlyList<JsonObject> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        if (events.Count == 0) return;
        var builder = new StringBuilder();
        foreach (var item in events) builder.Append(item.ToJsonString()).Append('\n');
        lock (gate)
        {
            ThrowIfClosed();
            Directory.CreateDirectory(FeedDirectory);
            var path = Path.Combine(FeedDirectory, FeedFileName(LocalDay(at)));
            using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
            var bytes = Utf8.GetBytes(builder.ToString());
            stream.Write(bytes);
            stream.Flush();
        }
    }

    /// <summary>The events of one local day, in order; a line that does not parse (a torn last line) is skipped.</summary>
    public IReadOnlyList<JsonObject> ReadFeed(DateOnly day)
    {
        lock (gate)
        {
            ThrowIfClosed();
            var path = Path.Combine(FeedDirectory, FeedFileName(day));
            if (!File.Exists(path)) return [];
            var events = new List<JsonObject>();
            foreach (var line in File.ReadAllText(path, Utf8).Split('\n'))
                if (TryParseObject(line) is { } item) events.Add(item);
            return events;
        }
    }

    /// <summary>Removes the feed files older than <see cref="AssistantStoreOptions.FeedDays"/> days before today.</summary>
    public int PruneFeed()
    {
        lock (gate)
        {
            ThrowIfClosed();
            if (!Directory.Exists(FeedDirectory)) return 0;
            var oldest = LocalDay(options.Time.GetUtcNow()).AddDays(-(options.FeedDays - 1));
            var removed = 0;
            foreach (var path in Directory.EnumerateFiles(FeedDirectory))
            {
                var name = Path.GetFileName(path);
                if (!FeedFileShape().IsMatch(name)) continue;
                if (!DateOnly.TryParseExact(name[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)) continue;
                if (day >= oldest) continue;
                File.Delete(path);
                removed++;
            }
            return removed;
        }
    }

    // ---- Lifetime ----

    /// <summary>Folds every journal into its snapshot and releases the folder.</summary>
    public void Dispose()
    {
        lock (gate)
        {
            if (lockHandle is null) return;
            try
            {
                foreach (var id in journalLines.Where(j => j.Value > 0).Select(j => j.Key).ToList()) Compact(id);
            }
            finally
            {
                Release();
            }
        }
    }

    /// <summary>A test's crash: the folder is released and nothing is folded or flushed beyond what is on disk.</summary>
    internal void AbandonForTests()
    {
        lock (gate) Release();
    }

    private void Release()
    {
        lockHandle?.Dispose();
        lockHandle = null;
    }

    private void ThrowIfClosed() => ObjectDisposedException.ThrowIf(lockHandle is null, this);

    // ---- Open: clean, replay, recover, fold ----

    private void Load()
    {
        Directory.CreateDirectory(MissionsDirectory);
        var removed = RemoveTempFiles();
        var replayed = new List<string>();
        var damaged = new List<string>();
        var ids = Directory.EnumerateFiles(MissionsDirectory)
            .Select(path => Path.GetFileName(path))
            .Select(name => name.EndsWith(".json", StringComparison.Ordinal) ? name[..^5]
                : name.EndsWith(".journal", StringComparison.Ordinal) ? name[..^8] : null)
            .OfType<string>()
            .Where(IsId)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();
        foreach (var id in ids)
        {
            var snapshot = ReadSnapshot(id, damaged);
            var (latest, deleted, appended) = ReplayJournal(id, snapshot, damaged);
            if (deleted)
            {
                RemoveMissionFiles(id);
                continue;
            }
            if (latest is null) continue;
            missions[id] = latest.Value;
            if (appended > 0)
            {
                replayed.Add(id);
                journalLines[id] = appended;
            }
        }
        var interrupted = new List<string>();
        if (options.InterruptedMission is { } rule)
        {
            foreach (var id in missions.Keys.Order(StringComparer.Ordinal).ToList())
            {
                var (sequence, body) = missions[id];
                if (rule((JsonObject)body.DeepClone()) is not { } recovered) continue;
                AppendJournal(id, new JsonObject { ["seq"] = sequence + 1, ["mission"] = recovered.DeepClone() });
                missions[id] = (sequence + 1, (JsonObject)recovered.DeepClone());
                journalLines[id] = journalLines.GetValueOrDefault(id) + 1;
                interrupted.Add(id);
            }
        }
        foreach (var id in journalLines.Where(j => j.Value > 0).Select(j => j.Key).Order(StringComparer.Ordinal).ToList()) Compact(id);
        OpenReport = new AssistantStoreOpenReport(replayed, interrupted, damaged, removed);
    }

    private (long Sequence, JsonObject Body)? ReadSnapshot(string id, List<string> damaged)
    {
        var path = SnapshotPath(id);
        if (!File.Exists(path)) return null;
        var parsed = TryParseObject(File.ReadAllText(path, Utf8));
        if (parsed is not null && parsed["seq"] is JsonValue seqValue && seqValue.TryGetValue<long>(out var sequence)
            && parsed["mission"] is JsonObject body)
            return (sequence, (JsonObject)body.DeepClone());
        // A snapshot that does not parse is kept aside (never overwritten) and reported; the journal may still hold it.
        var aside = path + ".damaged-" + options.Time.GetUtcNow().ToString("yyyyMMddHHmmssfff", CultureInfo.InvariantCulture);
        File.Move(path, aside);
        damaged.Add(Path.GetFileName(aside));
        return null;
    }

    private ((long Sequence, JsonObject Body)? Latest, bool Deleted, int Appended) ReplayJournal(
        string id, (long Sequence, JsonObject Body)? snapshot, List<string> damaged)
    {
        var path = JournalPath(id);
        var latest = snapshot;
        if (!File.Exists(path)) return (latest, false, 0);
        var lines = File.ReadAllText(path, Utf8).Split('\n');
        var appended = 0;
        var deleted = false;
        var broken = false;
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.Length == 0) continue;
            // The last piece has no newline after it: the append was cut by a crash, so it is dropped silently.
            if (i == lines.Length - 1) break;
            var entry = TryParseObject(line);
            if (entry is null || entry["seq"] is not JsonValue seqValue || !seqValue.TryGetValue<long>(out var sequence))
            {
                broken = true;
                damaged.Add(Path.GetFileName(path) + ":" + (i + 1).ToString(CultureInfo.InvariantCulture));
                break;
            }
            if (latest is { } held && sequence <= held.Sequence) continue; // already in the snapshot
            if (entry["deleted"] is JsonValue flag && flag.TryGetValue<bool>(out var gone) && gone)
            {
                deleted = true;
                break;
            }
            if (entry["mission"] is not JsonObject body)
            {
                broken = true;
                damaged.Add(Path.GetFileName(path) + ":" + (i + 1).ToString(CultureInfo.InvariantCulture));
                break;
            }
            latest = (sequence, (JsonObject)body.DeepClone());
            appended++;
        }
        if (deleted) return (latest, true, appended);
        if (broken)
        {
            // Everything before the damaged line is kept; the journal itself is set aside, never deleted.
            File.Move(path, path + ".damaged-" + options.Time.GetUtcNow().ToString("yyyyMMddHHmmssfff", CultureInfo.InvariantCulture));
        }
        else if (appended == 0)
        {
            File.Delete(path); // only lines the snapshot already holds, or a torn piece
        }
        return (latest, false, appended);
    }

    // ---- Writes ----

    private void AppendJournal(string id, JsonObject entry)
    {
        var bytes = Utf8.GetBytes(entry.ToJsonString() + "\n");
        using var stream = new FileStream(JournalPath(id), FileMode.Append, FileAccess.Write, FileShare.Read);
        stream.Write(bytes);
        stream.Flush(flushToDisk: true);
    }

    private void Compact(string id)
    {
        if (missions.TryGetValue(id, out var held))
        {
            var snapshot = new JsonObject
            {
                ["format"] = SnapshotFormat,
                ["seq"] = held.Sequence,
                ["mission"] = held.Body.DeepClone(),
            };
            WriteAtomic(SnapshotPath(id), Utf8.GetBytes(snapshot.ToJsonString(Indented)));
            Faults?.Invoke(AssistantStoreFault.SnapshotWritten);
        }
        var journal = JournalPath(id);
        if (File.Exists(journal)) File.Delete(journal);
        journalLines[id] = 0;
    }

    private void RemoveMissionFiles(string id)
    {
        var snapshot = SnapshotPath(id);
        if (File.Exists(snapshot)) File.Delete(snapshot);
        var journal = JournalPath(id);
        if (File.Exists(journal)) File.Delete(journal);
    }

    private void SaveDocument(string path, JsonNode document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var bytes = Utf8.GetBytes(document.ToJsonString(Indented));
        lock (gate)
        {
            ThrowIfClosed();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            WriteAtomic(path, bytes);
        }
    }

    private JsonNode? ReadDocument(string path)
    {
        lock (gate)
        {
            ThrowIfClosed();
            if (!File.Exists(path)) return null;
            return AssistantJson.Parse(File.ReadAllText(path, Utf8)); // null when it does not parse or repeats a name
        }
    }

    /// <summary>
    /// Temp-then-rename (15.3): the bytes go to <c>.&lt;name&gt;.&lt;guid&gt;.tmp</c> beside the target, are flushed to
    /// disk, and the file is then renamed over the target (MoveFileEx with replace, one step on one volume).
    /// </summary>
    private void WriteAtomic(string path, byte[] bytes)
    {
        var directory = Path.GetDirectoryName(path)!;
        var temporary = Path.Combine(directory, "." + Path.GetFileName(path) + "." + Guid.NewGuid().ToString("N") + TempSuffix);
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                var half = bytes.Length / 2;
                stream.Write(bytes, 0, half);
                if (Faults is { } faults)
                {
                    stream.Flush(flushToDisk: true);
                    faults(AssistantStoreFault.TempPartlyWritten);
                }
                stream.Write(bytes, half, bytes.Length - half);
                stream.Flush(flushToDisk: true);
            }
            Faults?.Invoke(AssistantStoreFault.TempWritten);
            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception error) when (error is not AssistantStoreCrash)
        {
            if (File.Exists(temporary)) File.Delete(temporary);
            throw;
        }
    }

    /// <summary>
    /// Removes the temporary files a crash left beside the store's own documents: in <c>missions</c> and in each
    /// <c>assistants/&lt;id&gt;</c> folder (never inside the assistant's home folder, a worktree or a recording).
    /// </summary>
    private int RemoveTempFiles()
    {
        var folders = new List<string> { MissionsDirectory };
        var assistants = Path.Combine(Root, "assistants");
        if (Directory.Exists(assistants)) folders.AddRange(Directory.EnumerateDirectories(assistants));
        var removed = 0;
        foreach (var folder in folders.Where(Directory.Exists))
        {
            foreach (var path in Directory.EnumerateFiles(folder, ".*" + TempSuffix, SearchOption.TopDirectoryOnly))
            {
                File.Delete(path);
                removed++;
            }
        }
        return removed;
    }

    // ---- Names ----

    private string SnapshotPath(string id) => Path.Combine(MissionsDirectory, id + ".json");

    private string JournalPath(string id) => Path.Combine(MissionsDirectory, id + ".journal");

    private string AssistantFile(string assistantId, string name) => Path.Combine(Root, "assistants", Checked(assistantId), name);

    private DateOnly LocalDay(DateTimeOffset at) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(at, options.Time.LocalTimeZone).DateTime);

    private static string FeedFileName(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".jsonl";

    private static string Created(string path)
    {
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>
    /// An id is 1 to 64 lowercase letters, digits, '-' or '_', starting with a letter or digit, and never a Windows device
    /// name or "shared" (the shared memory folder), so no id can reach outside its folder or another assistant's, and two
    /// ids never name one file (Windows file names compare without case).
    /// </summary>
    public static bool IsId(string? id) => id is not null && IdShape().IsMatch(id) && !ReservedNames.Contains(id);

    private static string Checked(string id)
    {
        CheckId(id);
        return id;
    }

    private static void CheckId(string id)
    {
        if (!IsId(id)) throw new ArgumentException($"\"{id}\" is not an assistant store id.", nameof(id));
    }

    /// <summary>The object, or null when the text does not parse or repeats a property name (ED-NEW-dextra-as3-6).</summary>
    private static JsonObject? TryParseObject(string text) => AssistantJson.ParseObject(text);
}

/// <summary>The exception a test's fault hook throws to stop a write where a crash would.</summary>
internal sealed class AssistantStoreCrash : Exception
{
    public AssistantStoreCrash() : base("Simulated crash.") { }
}
