// PORT-ONLY: Dextra (DESIGN.md rev 5, 8.1, 15.3; unit AS-12; ED-NEW-dextra-as12-6). No Swift twin.
// The markdown a scope's notes are kept in (memory/<assistant-id>/<topic>.md). One bullet per note: the note's text, then its
// record as a comment at the end of the line, so the owner can read the file and the store can read it back exactly:
//
//     <!-- nth memory 1 -->
//     # Writing style
//
//     - The owner prefers short answers. <!--n {"id":"n3f9a1c27b4e","kind":"preference", ...}-->
//
// The text is one clean line (MemoryRules.Clean), so no note can end its own line, open a heading or forge another note.
// A file with another version number is newer than this build: it is not read and never rewritten. A bullet with no record
// (the owner typed it in an editor) is read as a note of his own. Fields this build does not know stay in the record.
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;

namespace Marrow.Services.Assistant;

/// <summary>A note as the store holds it: the note, the file it lives in, and the record fields this build does not know.</summary>
internal sealed record StoredNote(MemoryNote Note, string File, JsonObject Extra);

/// <summary>What reading one memory file found.</summary>
internal sealed record ParsedMemoryFile(bool Newer, IReadOnlyList<StoredNote> Notes);

internal static partial class MemoryFile
{
    public const int Version = 1;
    public const string RecentFile = "recent";
    public const string QuarantineFile = "quarantine";
    public const string IndexFile = "MEMORY";
    public const int MaxSources = 8;

    private const string RecordOpen = "<!--n ";
    private const string RecordClose = "-->";

    // The JSON of a record never holds a literal "-->" or "<": the default encoder writes both as > and <.
    private static readonly JsonSerializerOptions Compact = new() { WriteIndented = false };

    [GeneratedRegex(@"\A<!-- nth memory (\d{1,6}) -->\z")] private static partial Regex HeaderShape();

    [GeneratedRegex(@"\A[a-z0-9]{6,32}\z")] private static partial Regex IdShape();

    private static readonly HashSet<string> Known = new(StringComparer.Ordinal)
    {
        "id", "topic", "kind", "mayChange", "author", "authorName", "sources", "created", "lastUsed", "quarantine",
    };

    public static bool IsId(string? id) => id is not null && IdShape().IsMatch(id);

    /// <summary>The title line of a file.</summary>
    public static string Title(string file) => file switch
    {
        RecentFile => "Recent notes (not yet filed)",
        QuarantineFile => "Waiting for the owner (not used until he keeps them)",
        _ => MemoryRules.TopicTitle(file),
    };

    // ---- writing ------------------------------------------------------------------------------------------------------

    public static string Render(string file, IEnumerable<StoredNote> notes)
    {
        var text = new StringBuilder();
        text.Append("<!-- nth memory ").Append(Version.ToString(CultureInfo.InvariantCulture)).Append(" -->\n");
        text.Append("# ").Append(Title(file)).Append("\n\n");
        foreach (var stored in notes)
            text.Append("- ").Append(stored.Note.Text).Append(' ').Append(RecordOpen).Append(Record(stored).ToJsonString(Compact)).Append(RecordClose).Append('\n');
        return text.ToString();
    }

    public static JsonObject Record(StoredNote stored)
    {
        var note = stored.Note;
        var record = (JsonObject)stored.Extra.DeepClone();
        record["id"] = note.Id;
        record["topic"] = note.Topic;
        record["kind"] = KindName(note.Kind);
        if (note.MayChange) record["mayChange"] = true; else record.Remove("mayChange");
        record["author"] = AuthorName(note.Author);
        if (note.AuthorName is { Length: > 0 } name) record["authorName"] = name; else record.Remove("authorName");
        var sources = new JsonArray();
        foreach (var s in note.Sources)
        {
            var source = new JsonObject { ["kind"] = SourceKindName(s.Kind), ["target"] = s.Target };
            if (s.Account is { Length: > 0 } account) source["account"] = account;
            if (s.Detail is { Length: > 0 } detail) source["detail"] = detail;
            sources.Add(source);
        }
        record["sources"] = sources;
        record["created"] = Time(note.Created);
        if (note.LastUsed is { } used) record["lastUsed"] = Time(used); else record.Remove("lastUsed");
        if (note.Quarantine is { } quarantine)
        {
            var from = new JsonArray();
            foreach (var source in quarantine.From) from.Add(new JsonObject { ["origin"] = source.Origin.ToString(), ["detail"] = source.Detail });
            record["quarantine"] = new JsonObject { ["why"] = quarantine.Why, ["from"] = from };
        }
        else
        {
            record.Remove("quarantine");
        }
        return record;
    }

    private static string Time(DateTimeOffset at) => at.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    public static string KindName(NoteKind kind) => kind switch
    {
        NoteKind.Preference => "preference",
        NoteKind.Procedure => "procedure",
        NoteKind.Project => "project",
        _ => "fact",
    };

    private static NoteKind ReadKind(string? name) => name switch
    {
        "preference" => NoteKind.Preference,
        "procedure" => NoteKind.Procedure,
        "project" => NoteKind.Project,
        _ => NoteKind.Fact,
    };

    private static string AuthorName(NoteAuthor author) => author switch
    {
        NoteAuthor.Owner => "owner",
        NoteAuthor.Import => "import",
        _ => "assistant",
    };

    private static NoteAuthor ReadAuthor(string? name) => name switch
    {
        "owner" => NoteAuthor.Owner,
        "import" => NoteAuthor.Import,
        _ => NoteAuthor.Assistant,
    };

    private static string SourceKindName(NoteSourceKind kind) => kind switch
    {
        NoteSourceKind.Mission => "mission",
        NoteSourceKind.Import => "import",
        NoteSourceKind.Owner => "owner",
        _ => "chat",
    };

    private static NoteSourceKind ReadSourceKind(string? name) => name switch
    {
        "mission" => NoteSourceKind.Mission,
        "import" => NoteSourceKind.Import,
        "owner" => NoteSourceKind.Owner,
        _ => NoteSourceKind.Chat,
    };

    // ---- reading ------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Reads one file of <paramref name="scope"/>. <paramref name="file"/> is its name without ".md". A note in the quarantine file
    /// is quarantined whatever its record says, and a note whose record says quarantined is quarantined wherever it was found
    /// (a note is never released by a file edit; only the owner's Keep releases it, 8.2). Notes the file cannot read as one clean
    /// line (too long, empty) are left out, and the next write drops them.
    /// </summary>
    public static ParsedMemoryFile Parse(string scope, string file, string text, DateTimeOffset now)
    {
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        var start = 0;
        if (lines.Length > 0 && lines[0].StartsWith("<!-- nth memory ", StringComparison.Ordinal))
        {
            start = 1;
            if (HeaderShape().Match(lines[0]) is not { Success: true } header || !int.TryParse(header.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var version) || version > Version)
                return new ParsedMemoryFile(true, []);
        }
        var notes = new List<StoredNote>();
        var heading = "";
        for (var i = start; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.StartsWith("# ", StringComparison.Ordinal)) { heading = line[2..].Trim(); continue; }
            if (!(line.StartsWith("- ", StringComparison.Ordinal) || line.StartsWith("* ", StringComparison.Ordinal))) continue;
            var body = line[2..];
            var open = body.LastIndexOf(RecordOpen, StringComparison.Ordinal);
            JsonObject? record = null;
            var noteText = body;
            if (open >= 0 && body.EndsWith(RecordClose, StringComparison.Ordinal))
            {
                // A record that does not parse is dropped with its comment: the words before it are still the owner's bullet.
                record = AssistantJson.ParseObject(body[(open + RecordOpen.Length)..^RecordClose.Length]);
                noteText = body[..open];
            }
            var clean = MemoryRules.Clean(noteText);
            if (clean.Length < MemoryRules.MinNoteChars || clean.Length > MemoryRules.MaxNoteChars) continue;
            notes.Add(FromLine(scope, file, clean, record, heading, now));
        }
        return new ParsedMemoryFile(false, notes);
    }

    private static StoredNote FromLine(string scope, string file, string text, JsonObject? record, string heading, DateTimeOffset now)
    {
        var inQuarantine = file == QuarantineFile;
        var kind = ReadKind(Str(record, "kind"));
        var topic = MemoryRules.TopicSlug(Str(record, "topic"));
        if (topic.Length == 0) topic = MemoryRules.TopicSlug(heading);
        if (topic.Length == 0 || topic == MemoryRules.RecentTopic) topic = MemoryRules.DefaultTopic(scope, kind);
        var id = Str(record, "id");
        if (!IsId(id)) id = "h" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(MemoryRules.NormalKey(text))))[..12].ToLowerInvariant();
        var list = new List<NoteSource>();
        if (record?["sources"] is JsonArray array)
        {
            foreach (var item in array.OfType<JsonObject>().Take(MaxSources))
            {
                var target = Clip(Str(item, "target"), NoteSource.MaxTarget) ?? "";
                list.Add(new NoteSource(ReadSourceKind(Str(item, "kind")), target, Clip(Str(item, "account"), NoteSource.MaxTarget), Clip(Str(item, "detail"), NoteSource.MaxDetail)));
            }
        }
        var author = record is null ? NoteAuthor.Owner : ReadAuthor(Str(record, "author"));
        NoteQuarantine? quarantine = null;
        if (record?["quarantine"] is JsonObject || inQuarantine)
        {
            var from = new List<TaintSource>();
            if (record?["quarantine"]?["from"] is JsonArray origins)
            {
                foreach (var item in origins.OfType<JsonObject>().Take(MaxSources))
                {
                    var origin = Enum.TryParse<ContentOrigin>(Str(item, "origin"), out var parsed) && Taint.IsUntrusted(parsed) ? parsed : ContentOrigin.OutsideFile;
                    from.Add(new TaintSource(origin, Clip(Str(item, "detail"), NoteSource.MaxTarget) ?? ""));
                }
            }
            quarantine = new NoteQuarantine(new ValueList<TaintSource>(from), Clip(Str(record?["quarantine"] as JsonObject, "why"), 300) ?? "Kept aside by nth until you keep it.");
        }
        var note = new MemoryNote(id!, scope, topic, kind, text)
        {
            MayChange = record?["mayChange"] is JsonValue may && may.TryGetValue<bool>(out var flag) && flag,
            Author = author,
            AuthorName = Clip(Str(record, "authorName"), 120),
            Sources = new ValueList<NoteSource>(list),
            Created = ReadTime(Str(record, "created")) ?? now,
            LastUsed = ReadTime(Str(record, "lastUsed")),
            Quarantine = quarantine,
        };
        var extra = new JsonObject();
        if (record is not null)
            foreach (var (key, value) in record)
                if (!Known.Contains(key)) extra[key] = value?.DeepClone();
        var placed = note.Quarantined ? QuarantineFile : file;
        return new StoredNote(note, placed, extra);
    }

    private static string? Str(JsonObject? node, string name) => node?[name] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    private static string? Clip(string? text, int max) => text is null ? null : text.Length <= max ? text : text[..max];

    private static DateTimeOffset? ReadTime(string? text) =>
        text is not null && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var at) ? at : null;

    // ---- the index ------------------------------------------------------------------------------------------------------

    /// <summary>MEMORY.md: the used notes by topic with their ids, and a count of what waits for the owner (8.3: the index is rebuilt on a delete).</summary>
    public static string RenderIndex(IReadOnlyCollection<StoredNote> notes)
    {
        var used = notes.Where(n => n.Note.Used).ToList();
        var waiting = notes.Count - used.Count;
        var text = new StringBuilder();
        text.Append("<!-- nth memory index ").Append(Version.ToString(CultureInfo.InvariantCulture)).Append(" -->\n# Memory index\n\n");
        text.Append(used.Count.ToString(CultureInfo.InvariantCulture)).Append(used.Count == 1 ? " note" : " notes");
        if (waiting > 0) text.Append(" · ").Append(waiting.ToString(CultureInfo.InvariantCulture)).Append(" waiting for the owner (not listed)");
        text.Append('\n');
        foreach (var group in used.GroupBy(n => n.Note.Topic).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            text.Append("\n## ").Append(MemoryRules.TopicTitle(group.Key)).Append('\n');
            foreach (var stored in group.OrderBy(n => n.Note.Created).ThenBy(n => n.Note.Id, StringComparer.Ordinal))
            {
                var excerpt = stored.Note.Text.Length > 120 ? stored.Note.Text[..120] + "…" : stored.Note.Text;
                text.Append("- ").Append(stored.Note.Id).Append(" · ").Append(excerpt).Append('\n');
            }
        }
        return text.ToString();
    }
}
