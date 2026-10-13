// PORT-ONLY: web surfaces W12, Tabula (DESIGN.md 6.1; ED-NEW-web-W12-1). What an artifact is: a named, versioned
// creation of one thread, with each version's author (a model and its turn, or "you"), the prompt that caused it and
// its time. meta.json is written and read by hand (no reflection), so a trimmed publish keeps it. No Swift counterpart.
using System.Globalization;
using System.Text;
using System.Text.Json;
using Marrow.Core.Port;

namespace Marrow.Core.Models;

/// <summary>Who made a version: a model (its provider's raw name, the model id and the turn), or the user ("you", all null).</summary>
public sealed record ArtifactAuthor(string? Provider, string? Model, Guid? Turn)
{
    public static ArtifactAuthor You { get; } = new(null, null, null);
    public bool IsYou => Provider is null && Model is null;
}

/// <summary>Where a forked version came from: an artifact's version, in this thread or another one.</summary>
public sealed record ArtifactOrigin(Guid Thread, string Id, int Version);

public sealed record ArtifactVersion
{
    public required int Number { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
    public required ArtifactAuthor Author { get; init; }
    /// <summary>The prompt that caused the version (capped), or null when there was none (a restore, an edit by you).</summary>
    public string? Prompt { get; init; }
    /// <summary>A single-file version's file name inside the artifact's folder ("v3.html"); null for a project version.</summary>
    public string? File { get; init; }
    /// <summary>A project version's entry file; null for a single-file version.</summary>
    public string? Entry { get; init; }
    /// <summary>A project version's file paths, sorted ordinally; empty for a single-file version.</summary>
    public IReadOnlyList<string> Files { get; init; } = [];
    public long Bytes { get; init; }
    public int? RestoredFrom { get; init; }
    public ArtifactOrigin? ForkedFrom { get; init; }
}

public sealed record Artifact
{
    public required Guid Thread { get; init; }
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required ArtifactKind Kind { get; init; }
    /// <summary>The language of a code artifact (or "tsx" for a TypeScript React one); null otherwise.</summary>
    public string? Language { get; init; }
    public required DateTimeOffset Created { get; init; }
    public required IReadOnlyList<ArtifactVersion> Versions { get; init; }

    /// <summary>The current version is always the latest: a restore makes the old one the new latest (6.3).</summary>
    public int Current => Versions[^1].Number;
    public ArtifactVersion Latest => Versions[^1];
    public DateTimeOffset Updated => Versions[^1].Timestamp;
    public ArtifactVersion? Version(int number) => Versions.FirstOrDefault(v => v.Number == number);
}

/// <summary>One version's content: the text of a single-file artifact, or a project's files and entry.</summary>
public sealed record ArtifactSnapshot(Artifact Artifact, ArtifactVersion Version, string? Text, IReadOnlyDictionary<string, string>? Files)
{
    public bool IsProject => Files is not null;
}

/// <summary>What happened to an artifact.</summary>
public enum ArtifactChangeKind
{
    /// <summary>A new artifact: the Tabula pane opens on it (6.2, artifact_create "opens the Tabula pane").</summary>
    Created,
    /// <summary>A new version of an existing artifact (an update, an edit by you, a restore).</summary>
    Updated,
    /// <summary>The artifact and all its versions were removed from the thread.</summary>
    Removed,
}

/// <summary>
/// A change the store committed; the Tabula pane listens for these (<see cref="ArtifactStore.Changed"/>). Raised on the
/// thread that made the change (a tool call arrives on a pipe or session thread), after the files are on disk.
/// </summary>
public sealed record ArtifactChange(Guid Thread, string Id, string Title, ArtifactKind Kind, int Version, ArtifactChangeKind Change)
{
    public bool Created => Change == ArtifactChangeKind.Created;
}

/// <summary>A refusal the model or the user can act on: its message is said as it is.</summary>
public sealed class ArtifactException(string message) : Exception(message);

internal static class ArtifactMetaJson
{
    public const int Format = 1;

    public static byte[] Write(Artifact artifact)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("format", Format);
            writer.WriteString("id", artifact.Id);
            writer.WriteString("title", artifact.Title);
            writer.WriteString("kind", artifact.Kind.WireName());
            if (artifact.Language is { } language) writer.WriteString("language", language);
            writer.WriteString("created", Date(artifact.Created));
            writer.WriteNumber("current", artifact.Current);
            writer.WriteStartArray("versions");
            foreach (var version in artifact.Versions) WriteVersion(writer, version);
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        return stream.ToArray();
    }

    private static void WriteVersion(Utf8JsonWriter writer, ArtifactVersion version)
    {
        writer.WriteStartObject();
        writer.WriteNumber("number", version.Number);
        writer.WriteString("timestamp", Date(version.Timestamp));
        writer.WriteStartObject("author");
        if (version.Author.IsYou) writer.WriteBoolean("you", true);
        if (version.Author.Provider is { } provider) writer.WriteString("provider", provider);
        if (version.Author.Model is { } model) writer.WriteString("model", model);
        if (version.Author.Turn is { } turn) writer.WriteString("turn", SwiftUUID.UuidString(turn));
        writer.WriteEndObject();
        if (version.Prompt is { } prompt) writer.WriteString("prompt", prompt);
        if (version.File is { } file) writer.WriteString("file", file);
        if (version.Entry is { } entry) writer.WriteString("entry", entry);
        if (version.Entry is not null)
        {
            writer.WriteStartArray("files");
            foreach (var path in version.Files) writer.WriteStringValue(path);
            writer.WriteEndArray();
        }
        writer.WriteNumber("bytes", version.Bytes);
        if (version.RestoredFrom is { } restored) writer.WriteNumber("restoredFrom", restored);
        if (version.ForkedFrom is { } origin)
        {
            writer.WriteStartObject("forkedFrom");
            writer.WriteString("thread", SwiftUUID.UuidString(origin.Thread));
            writer.WriteString("id", origin.Id);
            writer.WriteNumber("version", origin.Version);
            writer.WriteEndObject();
        }
        writer.WriteEndObject();
    }

    /// <summary>The artifact a meta.json holds; null when it is not one this format can read (a damaged file is skipped, never fatal).</summary>
    public static Artifact? Read(byte[] data, Guid thread)
    {
        try
        {
            using var document = JsonDocument.Parse(data);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            if (Int(root, "format") is not { } format || format > Format) return null;
            if (Text(root, "id") is not { Length: > 0 } id || Text(root, "title") is not { } title
                || ArtifactKinds.Parse(Text(root, "kind")) is not { } kind || ParseDate(Text(root, "created")) is not { } created) return null;
            if (!root.TryGetProperty("versions", out var list) || list.ValueKind != JsonValueKind.Array) return null;
            var versions = new List<ArtifactVersion>();
            foreach (var item in list.EnumerateArray())
            {
                if (ReadVersion(item) is not { } version) return null;
                versions.Add(version);
            }
            if (versions.Count == 0) return null;
            return new Artifact { Thread = thread, Id = id, Title = title, Kind = kind, Language = Text(root, "language"), Created = created, Versions = versions };
        }
        catch (JsonException) { return null; }
    }

    private static ArtifactVersion? ReadVersion(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object || Int(item, "number") is not { } number || ParseDate(Text(item, "timestamp")) is not { } timestamp) return null;
        var author = ArtifactAuthor.You;
        if (item.TryGetProperty("author", out var by) && by.ValueKind == JsonValueKind.Object)
            author = new ArtifactAuthor(Text(by, "provider"), Text(by, "model"), SwiftUUID.Parse(Text(by, "turn")));
        var files = new List<string>();
        if (item.TryGetProperty("files", out var paths) && paths.ValueKind == JsonValueKind.Array)
            foreach (var path in paths.EnumerateArray())
                if (path.ValueKind == JsonValueKind.String && path.GetString() is { } value) files.Add(value);
        ArtifactOrigin? origin = null;
        long size = 0;
        if (item.TryGetProperty("bytes", out var bytes)
            && (bytes.ValueKind != JsonValueKind.Number || !bytes.TryGetInt64(out size) || size < 0)) return null;
        if (item.TryGetProperty("forkedFrom", out var from) && from.ValueKind == JsonValueKind.Object
            && SwiftUUID.Parse(Text(from, "thread")) is { } thread && Text(from, "id") is { } fromId && Int(from, "version") is { } fromVersion)
            origin = new ArtifactOrigin(thread, fromId, fromVersion);
        var version = new ArtifactVersion
        {
            Number = number,
            Timestamp = timestamp,
            Author = author,
            Prompt = Text(item, "prompt"),
            File = Text(item, "file"),
            Entry = Text(item, "entry"),
            Files = files,
            Bytes = size,
            RestoredFrom = Int(item, "restoredFrom"),
            ForkedFrom = origin,
        };
        // A version is either one file or a project folder with its entry among its files.
        return (version.File is null) == (version.Entry is null) || (version.Entry is { } entry && !files.Contains(entry, StringComparer.Ordinal)) ? null : version;
    }

    private static string Date(DateTimeOffset date) => date.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    private static DateTimeOffset? ParseDate(string? text) =>
        DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var date) ? date : null;

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static int? Int(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) ? number : null;

    public static long Utf8Length(string text) => Encoding.UTF8.GetByteCount(text);
}
