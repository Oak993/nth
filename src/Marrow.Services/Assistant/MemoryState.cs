// PORT-ONLY: Dextra (DESIGN.md rev 5, 8.3, 8.4, 15.3; unit AS-12; ED-NEW-dextra-as12-8). No Swift twin.
// The memory state document (memory/state.json): the salted hashes of notes the owner deleted (so the background capture and the
// import do not bring a deleted note back), the files the import has read (so an unchanged file is not read again), and the day of
// the last fold. It carries a format number; a document with a newer number is read but never written, and fields this build does
// not know stay in it when it is written.
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Marrow.Core.Port.Assistant;

namespace Marrow.Services.Assistant;

/// <summary>What the import last saw of one file: size, modified time and a content hash.</summary>
public sealed record ImportedFile(long Length, long Modified, string Hash);

internal sealed class MemoryState
{
    public const int Format = 1;
    public const int MaxDeleted = 1000;
    public static readonly TimeSpan DeletedKept = TimeSpan.FromDays(180);
    public const int MaxFilesPerSource = 500;
    public const int MaxSources = 40;

    private readonly JsonObject document;
    private readonly byte[] salt;

    private MemoryState(JsonObject document, byte[] salt, bool readOnly)
    {
        this.document = document;
        this.salt = salt;
        ReadOnly = readOnly;
    }

    /// <summary>The document came from a newer build: it is read, never written.</summary>
    public bool ReadOnly { get; }

    /// <summary>The state in a stored document (null or unreadable: a new one with a fresh salt).</summary>
    public static MemoryState Open(JsonObject? stored)
    {
        if (stored is null)
        {
            var fresh = RandomNumberGenerator.GetBytes(16);
            return new MemoryState(new JsonObject { ["format"] = Format, ["salt"] = Convert.ToBase64String(fresh) }, fresh, readOnly: false);
        }
        var format = stored["format"] is JsonValue f && f.TryGetValue<int>(out var number) ? number : 1;
        var bytes = stored["salt"] is JsonValue s && s.TryGetValue<string>(out var text) && TryDecode(text, out var decoded) ? decoded : null;
        if (bytes is null)
        {
            bytes = RandomNumberGenerator.GetBytes(16);
            stored["salt"] = Convert.ToBase64String(bytes);
        }
        stored["format"] = Math.Max(format, Format);
        return new MemoryState(stored, bytes, readOnly: format > Format);
    }

    private static bool TryDecode(string text, out byte[]? bytes)
    {
        try
        {
            bytes = Convert.FromBase64String(text);
            return bytes.Length >= 8;
        }
        catch (FormatException)
        {
            bytes = null;
            return false;
        }
    }

    public JsonObject ToJson() => (JsonObject)document.DeepClone();

    /// <summary>The hash a deleted note is remembered by: salted, so the text is not recoverable from it, and of the note's key, so a rewording of case or punctuation is the same note.</summary>
    public string Hash(string text)
    {
        var key = Encoding.UTF8.GetBytes(MemoryRules.NormalKey(text));
        var all = new byte[salt.Length + key.Length];
        salt.CopyTo(all, 0);
        key.CopyTo(all, salt.Length);
        return Convert.ToHexString(SHA256.HashData(all))[..32].ToLowerInvariant();
    }

    private JsonArray Deleted()
    {
        if (document["deleted"] is JsonArray existing) return existing;
        var created = new JsonArray();
        document["deleted"] = created;
        return created;
    }

    public bool IsDeleted(string text, DateTimeOffset now)
    {
        var hash = Hash(text);
        if (document["deleted"] is not JsonArray list) return false;
        foreach (var item in list.OfType<JsonObject>())
        {
            if (item["h"] is not JsonValue h || !h.TryGetValue<string>(out var value) || value != hash) continue;
            return item["at"] is not JsonValue at || !at.TryGetValue<string>(out var when) || !DateTimeOffset.TryParse(when, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out var parsed)
                || now - parsed < DeletedKept;
        }
        return false;
    }

    /// <summary>Remembers that the owner deleted a note, by its hash only. The oldest are dropped past <see cref="MaxDeleted"/>.</summary>
    public void AddDeleted(string text, DateTimeOffset now)
    {
        if (ReadOnly) return;
        var hash = Hash(text);
        var list = Deleted();
        if (list.OfType<JsonObject>().Any(i => i["h"]?.ToString() == hash)) return;
        list.Add(new JsonObject { ["h"] = hash, ["at"] = now.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture) });
        while (list.Count > MaxDeleted) list.RemoveAt(0);
    }

    /// <summary>Forgets that a note was deleted (a delete whose files could not be written did not happen).</summary>
    public void RemoveDeleted(string text)
    {
        if (ReadOnly || document["deleted"] is not JsonArray list) return;
        var hash = Hash(text);
        for (var i = list.Count - 1; i >= 0; i--)
            if (list[i] is JsonObject item && item["h"]?.ToString() == hash) list.RemoveAt(i);
    }

    public int DeletedCount => document["deleted"] is JsonArray list ? list.Count : 0;

    /// <summary>The day (local, "yyyy-MM-dd") the notes were last folded.</summary>
    public string? Folded
    {
        get => document["folded"] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
        set { if (!ReadOnly) document["folded"] = value; }
    }

    private JsonObject Imports()
    {
        if (document["imports"] is JsonObject existing) return existing;
        var created = new JsonObject();
        document["imports"] = created;
        return created;
    }

    public bool HasImports => document["imports"] is JsonObject imports && imports.Count > 0;

    public ImportedFile? File(string sourceKey, string path)
    {
        if (document["imports"]?[sourceKey]?["files"]?[path] is not JsonObject item) return null;
        return item["length"] is JsonValue l && l.TryGetValue<long>(out var length) && item["modified"] is JsonValue m && m.TryGetValue<long>(out var modified)
            && item["hash"] is JsonValue h && h.TryGetValue<string>(out var hash) ? new ImportedFile(length, modified, hash) : null;
    }

    public void SetFile(string sourceKey, string path, ImportedFile file)
    {
        if (ReadOnly) return;
        var imports = Imports();
        if (imports[sourceKey] is not JsonObject source)
        {
            if (imports.Count >= MaxSources) return;
            source = new JsonObject { ["files"] = new JsonObject() };
            imports[sourceKey] = source;
        }
        var files = source["files"] as JsonObject ?? new JsonObject();
        source["files"] = files;
        if (!files.ContainsKey(path) && files.Count >= MaxFilesPerSource) return;
        files[path] = new JsonObject { ["length"] = file.Length, ["modified"] = file.Modified, ["hash"] = file.Hash };
    }

    /// <summary>Drops deleted-note hashes older than <see cref="DeletedKept"/>.</summary>
    public void Prune(DateTimeOffset now)
    {
        if (ReadOnly || document["deleted"] is not JsonArray list) return;
        for (var i = list.Count - 1; i >= 0; i--)
        {
            if (list[i] is JsonObject item && item["at"] is JsonValue at && at.TryGetValue<string>(out var when)
                && DateTimeOffset.TryParse(when, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out var parsed) && now - parsed >= DeletedKept)
                list.RemoveAt(i);
        }
    }
}
