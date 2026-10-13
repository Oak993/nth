// PORT-ONLY: web surfaces W12, Tabula (DESIGN.md 6.1-6.3; ED-NEW-web-w12-1; owner 2026-10-11 "Visus and Tabula
// approved"). Every thread's artifacts on disk, under the thread's data folder (the folder beside its history file, as the
// handoff journal's): threads/<THREAD>/artifacts/<id>/meta.json, one v<n>.<ext> file per version of a single-file
// artifact and one v<n>/ folder per version of a project. Each call that changes content writes a new version; nothing is
// ever rewritten in place except meta.json, which is replaced atomically after the version's files are on disk, so a crash
// leaves the last good version. No Swift counterpart.
using System.Text;
using Marrow.Core.Localisation;
using Marrow.Core.Port;

namespace Marrow.Core.Models;

/// <summary>What artifact_create carries: a single-file artifact's content, or a project's files and entry.</summary>
public sealed record ArtifactDraft
{
    public required ArtifactKind Kind { get; init; }
    public required string Title { get; init; }
    /// <summary>The id the model asked for; made from the title when null.</summary>
    public string? Id { get; init; }
    /// <summary>A code artifact's language, or "tsx" for a TypeScript React component.</summary>
    public string? Language { get; init; }
    public string? Content { get; init; }
    public IReadOnlyDictionary<string, string>? Files { get; init; }
    public string? Entry { get; init; }
}

/// <summary>One find-and-replace of artifact_update's edits; <paramref name="Path"/> names a project's file.</summary>
public sealed record ArtifactTextEdit(string Find, string Replace, string? Path = null);

/// <summary>What artifact_update carries: exactly one of full content, edits or project files (6.2), and an optional new title.</summary>
public sealed record ArtifactRevision
{
    public string? Content { get; init; }
    public IReadOnlyList<ArtifactTextEdit>? Edits { get; init; }
    /// <summary>A project's changed files: a path to its new content, or to null to delete the file.</summary>
    public IReadOnlyDictionary<string, string?>? Files { get; init; }
    public string? Entry { get; init; }
    public string? Title { get; init; }
}

public sealed class ArtifactStore
{
    /// <summary>A project version holds at most 512 files (owner C8 amendment to 6.1).</summary>
    public const int MaxProjectFiles = 512;
    /// <summary>A version holds at most 5 MB, its files together (6.1).</summary>
    public const long MaxVersionBytes = 5L * 1024 * 1024;
    /// <summary>Short enough that "a-" plus the id and a disambiguating suffix stays one DNS label (6.5's synthetic origins).</summary>
    public const int MaxIdLength = 48;
    public const int MaxTitleLength = 120;
    public const int MaxPromptLength = 600;
    public const int MaxPathLength = 160;
    public const string DefaultEntry = "index.html";
    private const string FolderName = "artifacts";
    private const string MetaName = "meta.json";

    private readonly Func<string> threadsDirectory;
    private readonly Func<DateTimeOffset> clock;
    private readonly Lock gate = new();

    /// <param name="threadsDirectory">The app's threads folder (Storage.ThreadsDirectory); each thread's data folder is inside it.</param>
    /// <param name="clock">The version timestamps' clock; the wall clock when null.</param>
    public ArtifactStore(Func<string> threadsDirectory, Func<DateTimeOffset>? clock = null)
    {
        this.threadsDirectory = threadsDirectory;
        this.clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>Raised after every committed change, outside the store's lock, on the thread that made it.</summary>
    public event Action<ArtifactChange>? Changed;

    /// <summary>The thread's artifacts folder (it may not exist yet).</summary>
    public string ArtifactsFolder(Guid thread) => Path.Combine(threadsDirectory(), SwiftUUID.UuidString(thread), FolderName);

    /// <summary>One artifact's folder: meta.json and its versions.</summary>
    public string Folder(Guid thread, string id) => Path.Combine(ArtifactsFolder(thread), id);

    // ---- create, update, restore, fork ----

    public ArtifactSnapshot Create(Guid thread, ArtifactDraft draft, ArtifactAuthor author, string? prompt)
    {
        var title = CleanTitle(draft.Title);
        var language = CleanLanguage(draft.Kind, draft.Language);
        var (text, files, entry) = Validated(draft.Kind, draft.Content, draft.Files, draft.Entry, "artifact_create");
        ArtifactSnapshot snapshot;
        lock (gate)
        {
            string id;
            if (draft.Id is { } wanted && wanted.Trim().Length > 0)
            {
                id = NormalizeId(wanted) ?? throw new ArtifactException($"\"{wanted.Trim()}\" cannot be an artifact id: use letters, digits and hyphens.");
                if (IsTaken(thread, id))
                    throw new ArtifactException($"This chat already has an artifact \"{id}\". Change it with artifact_update and id \"{id}\", or pick another id.");
            }
            else id = UniqueId(thread, title);
            var folder = PrepareFolder(thread, id);
            var version = WriteVersion(folder, 1, draft.Kind, language, text, files, entry, author, prompt, null, null);
            var artifact = new Artifact { Thread = thread, Id = id, Title = title, Kind = draft.Kind, Language = language, Created = version.Timestamp, Versions = [version] };
            WriteMeta(folder, artifact);
            snapshot = new ArtifactSnapshot(artifact, version, text, files);
        }
        Raise(snapshot, ArtifactChangeKind.Created);
        return snapshot;
    }

    public ArtifactSnapshot Update(Guid thread, string id, ArtifactRevision revision, ArtifactAuthor author, string? prompt)
    {
        ArtifactSnapshot snapshot;
        lock (gate)
        {
            var artifact = Load(thread, id) ?? throw NotFound(id);
            var latest = Content(artifact, artifact.Latest);
            var modes = (revision.Content is not null ? 1 : 0) + (revision.Edits is not null ? 1 : 0) + (revision.Files is not null ? 1 : 0);
            if (modes == 0) throw new ArtifactException("Pass the new content, edits or (for a project) files.");
            if (modes > 1) throw new ArtifactException("Pass only one of content, edits or files.");
            string? text = null;
            IReadOnlyDictionary<string, string>? files = null;
            string? entry = null;
            if (artifact.Kind == ArtifactKind.Project)
            {
                if (revision.Content is not null) throw new ArtifactException($"\"{artifact.Id}\" is a project: change it with files or edits.");
                var current = new Dictionary<string, string>(latest.Files!, StringComparer.Ordinal);
                if (revision.Edits is { } edits) ApplyEdits(current, edits, artifact.Latest.Number);
                else
                {
                    foreach (var (path, content) in revision.Files!)
                    {
                        var clean = ProjectPath(path);
                        var existing = current.Keys.FirstOrDefault(key => string.Equals(key, clean, StringComparison.OrdinalIgnoreCase));
                        if (content is null)
                        {
                            if (existing is null) throw new ArtifactException($"\"{clean}\" is not a file of \"{artifact.Id}\", so it cannot be deleted.");
                            current.Remove(existing);
                        }
                        else
                        {
                            if (existing is not null) current.Remove(existing);
                            current[clean] = content;
                        }
                    }
                }
                var wantedEntry = revision.Entry ?? (current.ContainsKey(artifact.Latest.Entry!) ? artifact.Latest.Entry : null);
                (_, files, entry) = Validated(ArtifactKind.Project, null, current, wantedEntry, "artifact_update");
            }
            else
            {
                if (revision.Files is not null) throw new ArtifactException($"\"{artifact.Id}\" is one file, not a project: pass content or edits.");
                text = revision.Edits is { } edits ? ApplyEdits(latest.Text!, edits, artifact.Latest.Number) : revision.Content;
                (text, _, _) = Validated(artifact.Kind, text, null, null, "artifact_update");
            }
            var title = revision.Title is { } renamed && renamed.Trim().Length > 0 ? CleanTitle(renamed) : artifact.Title;
            snapshot = Append(artifact with { Title = title }, text, files, entry, author, prompt, null, null);
        }
        Raise(snapshot, ArtifactChangeKind.Updated);
        return snapshot;
    }

    /// <summary>Restore: the old version's content becomes the new latest version (6.3); nothing is deleted.</summary>
    public ArtifactSnapshot Restore(Guid thread, string id, int version, ArtifactAuthor author)
    {
        ArtifactSnapshot snapshot;
        lock (gate)
        {
            var artifact = Load(thread, id) ?? throw NotFound(id);
            var old = artifact.Version(version) ?? throw NoVersion(artifact, version);
            var content = Content(artifact, old);
            snapshot = Append(artifact, content.Text, content.Files, old.Entry, author, null, version, null);
        }
        Raise(snapshot, ArtifactChangeKind.Updated);
        return snapshot;
    }

    /// <summary>
    /// Fork: a version copied into a new artifact, in the same thread ("Remix") or another one ("Copy into this thread",
    /// 6.1), under a fresh id made from the source's. Its first version remembers where it came from.
    /// </summary>
    public ArtifactSnapshot Fork(Guid sourceThread, string id, int version, Guid targetThread, ArtifactAuthor author, string? title = null)
    {
        ArtifactSnapshot snapshot;
        lock (gate)
        {
            var source = Load(sourceThread, id) ?? throw NotFound(id);
            var old = source.Version(version) ?? throw NoVersion(source, version);
            var content = Content(source, old);
            var name = title is { } wanted && wanted.Trim().Length > 0 ? CleanTitle(wanted) : source.Title;
            var newId = UniqueId(targetThread, source.Id);
            var folder = PrepareFolder(targetThread, newId);
            var first = WriteVersion(folder, 1, source.Kind, source.Language, content.Text, content.Files, old.Entry, author, null, null, new ArtifactOrigin(sourceThread, source.Id, version));
            var artifact = new Artifact { Thread = targetThread, Id = newId, Title = name, Kind = source.Kind, Language = source.Language, Created = first.Timestamp, Versions = [first] };
            WriteMeta(folder, artifact);
            snapshot = new ArtifactSnapshot(artifact, first, content.Text, content.Files);
        }
        Raise(snapshot, ArtifactChangeKind.Created);
        return snapshot;
    }

    // ---- read and list ----

    /// <summary>A version's content and the artifact's metadata; the latest version when <paramref name="version"/> is null.</summary>
    public ArtifactSnapshot Read(Guid thread, string id, int? version = null)
    {
        lock (gate)
        {
            var artifact = Load(thread, id) ?? throw NotFound(id);
            var chosen = version is { } number ? artifact.Version(number) ?? throw NoVersion(artifact, number) : artifact.Latest;
            return Content(artifact, chosen);
        }
    }

    /// <summary>The artifact's metadata, or null when the thread has no artifact with that id.</summary>
    public Artifact? Find(Guid thread, string id)
    {
        lock (gate) return Load(thread, id);
    }

    /// <summary>True when the thread has a readable artifact with this id (after the id's normal form).</summary>
    public bool Exists(Guid thread, string id) => Find(thread, id) is not null;

    /// <summary>The thread's artifacts, newest change first. An artifact whose meta.json cannot be read is skipped, never fatal.</summary>
    public IReadOnlyList<Artifact> List(Guid thread)
    {
        lock (gate) return Newest(ListUnlocked(thread));
    }

    /// <summary>The threads that have an artifacts folder (the gallery's "All threads").</summary>
    public IReadOnlyList<Guid> Threads()
    {
        var root = threadsDirectory();
        if (!Directory.Exists(root)) return [];
        var result = new List<Guid>();
        foreach (var folder in SafeEntries(() => Directory.GetDirectories(root)))
            if (SwiftUUID.Parse(Path.GetFileName(folder)) is { } thread && Directory.Exists(Path.Combine(folder, FolderName))) result.Add(thread);
        result.Sort();
        return result;
    }

    /// <summary>
    /// The gallery across threads (6.1): every artifact of the given threads, newest first, filtered by a search over the
    /// title and the kind's names (case-insensitive) and by kind.
    /// </summary>
    public IReadOnlyList<Artifact> Gallery(IEnumerable<Guid> threads, string? search = null, ArtifactKind? kind = null)
    {
        var words = (search ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var all = new List<Artifact>();
        lock (gate)
        {
            foreach (var thread in threads.Distinct())
                foreach (var artifact in ListUnlocked(thread))
                {
                    if (kind is { } wanted && artifact.Kind != wanted) continue;
                    var haystack = artifact.Title + " " + artifact.Kind.WireName() + " " + ArtifactKinds.DisplayName(artifact.Kind, artifact.Language) + " " + artifact.Id;
                    if (words.All(word => haystack.Contains(word, StringComparison.OrdinalIgnoreCase))) all.Add(artifact);
                }
        }
        return Newest(all);
    }

    // ---- storage used, remove ----

    /// <summary>The bytes the thread's artifacts take on disk (Settings, Tabula, "Artifact storage used").</summary>
    public long StorageUsed(Guid thread)
    {
        lock (gate)
        {
            var folder = ArtifactsFolder(thread);
            if (!Directory.Exists(folder)) return 0;
            return SafeEntries(() => Directory.GetFiles(folder, "*", SearchOption.AllDirectories)).Sum(file => SwiftTry.OptionalValue(() => new FileInfo(file).Length) ?? 0);
        }
    }

    /// <summary>Removes one artifact with all its versions.</summary>
    public void Remove(Guid thread, string id)
    {
        Artifact artifact;
        lock (gate)
        {
            artifact = Load(thread, id) ?? throw NotFound(id);
            Directory.Delete(Folder(thread, artifact.Id), recursive: true);
        }
        Changed?.Invoke(new ArtifactChange(thread, artifact.Id, artifact.Title, artifact.Kind, artifact.Current, ArtifactChangeKind.Removed));
    }

    /// <summary>Removes every artifact of the thread (Settings, "Clear").</summary>
    public void Clear(Guid thread)
    {
        IReadOnlyList<Artifact> removed;
        lock (gate)
        {
            removed = ListUnlocked(thread);
            var folder = ArtifactsFolder(thread);
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
        foreach (var artifact in removed)
            Changed?.Invoke(new ArtifactChange(thread, artifact.Id, artifact.Title, artifact.Kind, artifact.Current, ArtifactChangeKind.Removed));
    }

    // ---- ids ----

    /// <summary>
    /// An id's normal form: lower case ASCII letters, digits and single hyphens, at most <see cref="MaxIdLength"/>
    /// characters; spaces, underscores and dots become hyphens. Null when nothing usable is left.
    /// </summary>
    public static string? NormalizeId(string? text)
    {
        if (text is null) return null;
        var builder = new StringBuilder();
        foreach (var raw in text.Trim().ToLowerInvariant())
        {
            var c = raw is ' ' or '_' or '.' or '-' or '\t' ? '-' : raw;
            if (c is >= 'a' and <= 'z' or >= '0' and <= '9') builder.Append(c);
            else if (c == '-' && builder.Length > 0 && builder[^1] != '-') builder.Append('-');
        }
        var id = builder.ToString().Trim('-');
        if (id.Length > MaxIdLength) id = id[..MaxIdLength].TrimEnd('-');
        return id.Length == 0 ? null : id;
    }

    /// <summary>The id a new artifact gets when none is asked for: the title's normal form, made unique in the thread.</summary>
    public string NewId(Guid thread, string title)
    {
        lock (gate) return UniqueId(thread, CleanTitle(title));
    }

    private string UniqueId(Guid thread, string seed)
    {
        var stem = NormalizeId(seed) ?? "artifact";
        if (stem.Length > MaxIdLength - 4) stem = stem[..(MaxIdLength - 4)].TrimEnd('-');
        if (!IsTaken(thread, stem)) return stem;
        for (var n = 2; ; n++)
        {
            var suffix = "-" + n.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var available = MaxIdLength - suffix.Length;
            var candidate = (stem.Length > available ? stem[..available].TrimEnd('-') : stem) + suffix;
            if (!IsTaken(thread, candidate)) return candidate;
        }
    }

    /// <summary>An id is taken when its folder holds a meta.json, readable or not (a newer nth's format is never overwritten).</summary>
    private bool IsTaken(Guid thread, string id) => File.Exists(Path.Combine(Folder(thread, id), MetaName));

    // ---- content rules ----

    public static string CleanTitle(string? title)
    {
        var words = (title ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var clean = string.Join(' ', words);
        if (clean.Length > MaxTitleLength) clean = clean[..MaxTitleLength].TrimEnd();
        return clean.Length == 0 ? UiText.Get("tabula.artifact.untitled") : clean;
    }

    private static string? CleanLanguage(ArtifactKind kind, string? language)
    {
        var name = language?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(name) || name.Length > 24 || !name.All(c => char.IsAsciiLetterOrDigit(c) || c is '#' or '+' or '-' or '_')) return null;
        return kind switch
        {
            ArtifactKind.Code => name,
            ArtifactKind.React => name is "tsx" or "ts" or "typescript" ? "tsx" : null,
            _ => null,
        };
    }

    private static string? CleanPrompt(string? prompt)
    {
        var text = prompt?.Trim();
        if (string.IsNullOrEmpty(text)) return null;
        return text.Length <= MaxPromptLength ? text : text[..(MaxPromptLength - 1)].TrimEnd() + "…";
    }

    /// <summary>
    /// A project file's path in its normal form: forward slashes, relative, no "." or ".." segments, no characters or
    /// names Windows cannot store, at most <see cref="MaxPathLength"/> characters and 8 folders deep.
    /// </summary>
    public static string ProjectPath(string? path)
    {
        var text = (path ?? "").Trim().Replace('\\', '/');
        while (text.StartsWith("./", StringComparison.Ordinal)) text = text[2..];
        string Fail(string why) => throw new ArtifactException($"\"{path}\" cannot be a project file path: {why}.");
        if (text.Length == 0) return Fail("it is empty");
        if (text.StartsWith('/') || text.Contains(':', StringComparison.Ordinal)) return Fail("use a path relative to the project, like src/app.js");
        if (text.Length > MaxPathLength) return Fail($"it is longer than {MaxPathLength} characters");
        var segments = text.Split('/');
        if (segments.Length > 9) return Fail("it is more than 8 folders deep");
        foreach (var segment in segments)
        {
            if (segment.Length == 0 || segment is "." or "..") return Fail("it has an empty, \".\" or \"..\" part");
            if (segment.EndsWith('.') || segment.EndsWith(' ') || segment.StartsWith(' ')) return Fail("a name cannot start with a space or end with a dot or a space");
            if (segment.Any(c => c < ' ' || c is '<' or '>' or '"' or '|' or '?' or '*')) return Fail("it has a character file names cannot hold");
            var stem = segment.Split('.')[0].ToUpperInvariant();
            if (stem is "CON" or "PRN" or "AUX" or "NUL" || (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal)) && stem[3] is >= '0' and <= '9'))
                return Fail($"\"{segment}\" is a name Windows reserves");
        }
        return text;
    }

    /// <summary>Checks a version's content against its kind and the limits; a project's paths come back in normal form.</summary>
    private static (string? Text, IReadOnlyDictionary<string, string>? Files, string? Entry) Validated(ArtifactKind kind, string? content, IReadOnlyDictionary<string, string>? files, string? entry, string tool)
    {
        if (kind != ArtifactKind.Project)
        {
            if (files is not null) throw new ArtifactException($"Only a project takes files: pass content to {tool}.");
            if (content is null || content.Trim().Length == 0) throw new ArtifactException("The content is empty.");
            if (ArtifactMetaJson.Utf8Length(content) > MaxVersionBytes) throw new ArtifactException("The content is larger than 5 MB.");
            return (content, null, null);
        }
        if (content is not null && files is null) throw new ArtifactException($"A project is made of files: pass files ({{\"index.html\": \"...\"}}) to {tool}.");
        if (files is null || files.Count == 0) throw new ArtifactException("A project needs at least one file.");
        if (files.Count > MaxProjectFiles) throw new ArtifactException($"A project holds at most {MaxProjectFiles} files; this one has {files.Count}.");
        var clean = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long bytes = 0;
        foreach (var (path, text) in files)
        {
            var normal = ProjectPath(path);
            if (!seen.Add(normal)) throw new ArtifactException($"Two files have the path \"{normal}\" (paths are not case-sensitive).");
            clean[normal] = text ?? "";
            bytes += ArtifactMetaJson.Utf8Length(text ?? "");
        }
        if (bytes > MaxVersionBytes) throw new ArtifactException("The project's files are larger than 5 MB together.");
        string chosen;
        if (entry is { } wanted && wanted.Trim().Length > 0)
        {
            var normal = ProjectPath(wanted);
            chosen = clean.Keys.FirstOrDefault(key => string.Equals(key, normal, StringComparison.OrdinalIgnoreCase))
                ?? throw new ArtifactException($"The entry \"{normal}\" is not one of the project's files.");
        }
        else
        {
            var pages = clean.Keys.Where(key => key.EndsWith(".html", StringComparison.OrdinalIgnoreCase) || key.EndsWith(".htm", StringComparison.OrdinalIgnoreCase)).ToList();
            chosen = clean.Keys.FirstOrDefault(key => string.Equals(key, DefaultEntry, StringComparison.OrdinalIgnoreCase))
                ?? (pages.Count == 1 ? pages[0] : throw new ArtifactException("A project needs an entry file: add index.html, or name the entry."));
        }
        return (null, clean, chosen);
    }

    /// <summary>Applies edits in order to one text; each find must match exactly once in the text as the earlier edits left it.</summary>
    private static string ApplyEdits(string text, IReadOnlyList<ArtifactTextEdit> edits, int version)
    {
        if (edits.Count == 0) throw new ArtifactException("edits is empty.");
        for (var i = 0; i < edits.Count; i++)
        {
            if (edits[i].Path is { Length: > 0 }) throw new ArtifactException($"Edit {i + 1} of {edits.Count} names a path, but this artifact is one file.");
            text = ApplyEdit(text, edits[i], i, edits.Count, version, null);
        }
        return text;
    }

    private static void ApplyEdits(Dictionary<string, string> files, IReadOnlyList<ArtifactTextEdit> edits, int version)
    {
        if (edits.Count == 0) throw new ArtifactException("edits is empty.");
        for (var i = 0; i < edits.Count; i++)
        {
            var edit = edits[i];
            string path;
            if (edit.Path is { Length: > 0 } named)
            {
                var normal = ProjectPath(named);
                path = files.Keys.FirstOrDefault(key => string.Equals(key, normal, StringComparison.OrdinalIgnoreCase))
                    ?? throw new ArtifactException($"Edit {i + 1} of {edits.Count}: \"{normal}\" is not a file of v{version}.");
            }
            else
            {
                // No path: the find text must be in exactly one file, once.
                var holders = files.Where(file => Count(file.Value, edit.Find) > 0 || (Crlf(file.Value, edit.Find) is { } crlf && Count(file.Value, crlf) > 0)).Select(file => file.Key).ToList();
                if (edit.Find.Length > 0 && holders.Count > 1)
                    throw new ArtifactException($"Edit {i + 1} of {edits.Count}: the find text is in {holders.Count} files ({string.Join(", ", holders)}); add a path. Nothing was changed.");
                path = holders.Count == 1 ? holders[0] : files.Count == 1 ? files.Keys.First() : "";
                if (path.Length == 0)
                {
                    if (edit.Find.Length == 0) throw new ArtifactException($"Edit {i + 1} of {edits.Count}: the find text is empty. Nothing was changed.");
                    throw new ArtifactException($"Edit {i + 1} of {edits.Count}: the find text is not in any file of v{version}. Nothing was changed.");
                }
            }
            files[path] = ApplyEdit(files[path], edit, i, edits.Count, version, path);
        }
    }

    private static string ApplyEdit(string text, ArtifactTextEdit edit, int index, int total, int version, string? path)
    {
        var where = path is null ? $"v{version}" : $"{path} (v{version})";
        if (edit.Find.Length == 0) throw new ArtifactException($"Edit {index + 1} of {total}: the find text is empty. Nothing was changed.");
        var (find, replace) = (edit.Find, edit.Replace);
        var count = Count(text, find);
        // A model writes "\n" for a file saved with "\r\n": the same lines, so they match.
        if (count == 0 && Crlf(text, find) is { } crlf)
        {
            (find, replace) = (crlf, edit.Replace.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\n", "\r\n", StringComparison.Ordinal));
            count = Count(text, find);
        }
        if (count == 0) throw new ArtifactException($"Edit {index + 1} of {total}: the find text is not in {where}. Read the artifact and copy the text exactly. Nothing was changed.");
        if (count > 1) throw new ArtifactException($"Edit {index + 1} of {total}: the find text matches {count} places in {where}; include more of the text around it so it matches once. Nothing was changed.");
        var at = text.IndexOf(find, StringComparison.Ordinal);
        return string.Concat(text.AsSpan(0, at), replace, text.AsSpan(at + find.Length));
    }

    /// <summary>The find text with "\n" line ends as "\r\n", when the text uses "\r\n" and the find text has bare "\n"; else null.</summary>
    private static string? Crlf(string text, string find) =>
        find.Contains('\n', StringComparison.Ordinal) && !find.Contains('\r', StringComparison.Ordinal) && text.Contains("\r\n", StringComparison.Ordinal)
            ? find.Replace("\n", "\r\n", StringComparison.Ordinal) : null;

    /// <summary>Occurrences of <paramref name="find"/>, overlapping ones included (so "aa" in "aaa" is ambiguous, not one).</summary>
    private static int Count(string text, string find)
    {
        if (find.Length == 0) return 0;
        var count = 0;
        for (var at = text.IndexOf(find, StringComparison.Ordinal); at >= 0; at = at + 1 < text.Length ? text.IndexOf(find, at + 1, StringComparison.Ordinal) : -1) count++;
        return count;
    }

    // ---- disk ----

    private ArtifactSnapshot Append(Artifact artifact, string? text, IReadOnlyDictionary<string, string>? files, string? entry, ArtifactAuthor author, string? prompt, int? restoredFrom, ArtifactOrigin? forkedFrom)
    {
        var folder = Folder(artifact.Thread, artifact.Id);
        var version = WriteVersion(folder, artifact.Latest.Number + 1, artifact.Kind, artifact.Language, text, files, entry, author, prompt, restoredFrom, forkedFrom);
        var updated = artifact with { Versions = [.. artifact.Versions, version] };
        WriteMeta(folder, updated);
        return new ArtifactSnapshot(updated, version, text, files);
    }

    private ArtifactVersion WriteVersion(string folder, int number, ArtifactKind kind, string? language, string? text, IReadOnlyDictionary<string, string>? files, string? entry,
        ArtifactAuthor author, string? prompt, int? restoredFrom, ArtifactOrigin? forkedFrom)
    {
        var name = "v" + number.ToString(System.Globalization.CultureInfo.InvariantCulture);
        long bytes;
        string? file = null;
        IReadOnlyList<string> paths = [];
        if (files is null)
        {
            file = name + "." + ArtifactKinds.Extension(kind, language);
            var data = new UTF8Encoding(false).GetBytes(text!);
            FileOps.WriteAtomic(Path.Combine(folder, file), data);
            bytes = data.Length;
        }
        else
        {
            // The folder is filled under a temporary name and moved into place whole, so a v<n>/ folder is always complete.
            var final = Path.Combine(folder, name);
            var staging = Path.Combine(folder, name + ".partial-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(staging);
            bytes = 0;
            try
            {
                foreach (var (path, content) in files)
                {
                    var target = Path.Combine(staging, path.Replace('/', Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    var data = new UTF8Encoding(false).GetBytes(content);
                    File.WriteAllBytes(target, data);
                    bytes += data.Length;
                }
                if (Directory.Exists(final)) Directory.Delete(final, recursive: true); // a leftover of a version whose meta.json never landed
                Directory.Move(staging, final);
            }
            catch
            {
                SwiftTry.Ignore(() => { if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true); });
                throw;
            }
            paths = [.. files.Keys.Order(StringComparer.Ordinal)];
        }
        return new ArtifactVersion
        {
            Number = number,
            Timestamp = clock(),
            Author = author,
            Prompt = CleanPrompt(prompt),
            File = file,
            Entry = files is null ? null : entry,
            Files = paths,
            Bytes = bytes,
            RestoredFrom = restoredFrom,
            ForkedFrom = forkedFrom,
        };
    }

    private string PrepareFolder(Guid thread, string id)
    {
        var folder = Folder(thread, id);
        // A folder with no meta.json is what a create that crashed before its meta.json left: nothing refers to it.
        if (Directory.Exists(folder) && !File.Exists(Path.Combine(folder, MetaName))) Directory.Delete(folder, recursive: true);
        Directory.CreateDirectory(folder);
        return folder;
    }

    private static void WriteMeta(string folder, Artifact artifact) => FileOps.WriteAtomic(Path.Combine(folder, MetaName), ArtifactMetaJson.Write(artifact));

    private Artifact? Load(Guid thread, string id)
    {
        if (NormalizeId(id) is not { } normal) return null;
        var meta = Path.Combine(Folder(thread, normal), MetaName);
        if (!File.Exists(meta)) return null;
        var data = SwiftTry.Optional(() => File.ReadAllBytes(meta));
        return data is null ? null : ArtifactMetaJson.Read(data, thread) is { } artifact && string.Equals(artifact.Id, normal, StringComparison.Ordinal) ? artifact : null;
    }

    private List<Artifact> ListUnlocked(Guid thread)
    {
        var folder = ArtifactsFolder(thread);
        var result = new List<Artifact>();
        if (!Directory.Exists(folder)) return result;
        foreach (var child in SafeEntries(() => Directory.GetDirectories(folder)))
            if (Load(thread, Path.GetFileName(child)) is { } artifact) result.Add(artifact);
        return result;
    }

    private ArtifactSnapshot Content(Artifact artifact, ArtifactVersion version)
    {
        var folder = Folder(artifact.Thread, artifact.Id);
        try
        {
            if (version.File is { } file) return new ArtifactSnapshot(artifact, version, File.ReadAllText(Path.Combine(folder, file), Encoding.UTF8), null);
            var root = Path.Combine(folder, "v" + version.Number.ToString(System.Globalization.CultureInfo.InvariantCulture));
            var files = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (var path in version.Files) files[path] = File.ReadAllText(Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar)), Encoding.UTF8);
            return new ArtifactSnapshot(artifact, version, null, files);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            throw new ArtifactException($"v{version.Number} of \"{artifact.Id}\" cannot be read from disk: {error.Message}");
        }
    }

    private static List<Artifact> Newest(List<Artifact> artifacts)
    {
        artifacts.Sort((a, b) =>
        {
            var byTime = b.Updated.CompareTo(a.Updated);
            if (byTime != 0) return byTime;
            var byThread = a.Thread.CompareTo(b.Thread);
            return byThread != 0 ? byThread : string.CompareOrdinal(a.Id, b.Id);
        });
        return artifacts;
    }

    private static string[] SafeEntries(Func<string[]> list) => SwiftTry.Optional(list) ?? [];

    private void Raise(ArtifactSnapshot snapshot, ArtifactChangeKind change) =>
        Changed?.Invoke(new ArtifactChange(snapshot.Artifact.Thread, snapshot.Artifact.Id, snapshot.Artifact.Title, snapshot.Artifact.Kind, snapshot.Version.Number, change));

    private static ArtifactException NotFound(string id) =>
        new($"This chat has no artifact \"{(NormalizeId(id) ?? id.Trim())}\". artifact_list shows the ids it has.");

    private static ArtifactException NoVersion(Artifact artifact, int version) =>
        new($"\"{artifact.Id}\" has no v{version}; its versions are v{artifact.Versions[0].Number} to v{artifact.Current}.");
}
