// PORT-ONLY: Dextra, nth's assistant (DESIGN.md revision 5, sections 10 and 15.3; unit AS-19; ED-NEW-dextra-as19-1, -7). No Swift counterpart.
// The teach recordings as the store keeps them: teach/<assistant>/<recording>/ with a fixed set of named files, the events and the
// narration as JSON lines, the frames under frames/, the manifest and the draft as JSON documents. The store knows nothing about what
// they hold. It refuses any name outside that set, resolves every path fully and acts only strictly inside the recording's own folder
// (never through a link), writes whole files temp-then-rename and lines by append, and deletes a recording by removing its folder.
using System.Text;
using System.Text.RegularExpressions;

namespace Marrow.Services.Assistant;

public sealed partial class AssistantStore
{
    public const string TeachManifestFile = "recording.json";

    public const string TeachEventsFile = "events.jsonl";

    public const string TeachNarrationFile = "narration.jsonl";

    public const string TeachDraftFile = "draft.json";

    public const string TeachFramesFile = "frames.jsonl";

    /// <summary>The most a recording's single file is read as (a frame is far smaller; a larger file is damaged or planted).</summary>
    public const int MaxTeachFileBytes = 32 * 1024 * 1024;

    /// <summary>The longest line appended to a recording's JSON-lines file.</summary>
    public const int MaxTeachLineBytes = 64 * 1024;

    [GeneratedRegex(@"\A(?:[a-z][a-z-]{0,30}\.(?:json|jsonl)|frames/[0-9]{1,7}\.jpg)\z", RegexOptions.CultureInvariant, 250)] private static partial Regex TeachFileShape();

    [GeneratedRegex(@"\Aframes/[0-9]{1,7}\.jpg\z", RegexOptions.CultureInvariant, 250)] private static partial Regex FrameShape();

    /// <summary>Whether a text is the name of a frame file ("frames/0007.jpg").</summary>
    public static bool IsFrameName(string? name) => name is not null && FrameShape().IsMatch(name);

    /// <summary>A new recording id: "t" and twelve hex digits.</summary>
    public static string NewTeachId() => "t" + Guid.NewGuid().ToString("N")[..12];

    private string TeachRoot => Path.Combine(Root, "teach");

    private static void CheckTeachFile(string relative)
    {
        if (!TeachFileShape().IsMatch(relative)) throw new ArgumentException($"\"{relative}\" is not a teach recording file name.", nameof(relative));
    }

    // The recording's folder, fully resolved and strictly inside teach/<assistant>/. Throws if the id is not an id, if the folder is a link,
    // or if it resolves anywhere else. Not created.
    private string TeachFolderPath(string assistantId, string recordingId)
    {
        CheckId(assistantId);
        CheckId(recordingId);
        var assistant = Path.GetFullPath(Path.Combine(TeachRoot, assistantId));
        var folder = Path.GetFullPath(Path.Combine(assistant, recordingId));
        if (!folder.StartsWith(assistant + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new IOException("A teach recording resolves outside its folder.");
        return folder;
    }

    private static void RefuseLinks(string folder)
    {
        if (!Directory.Exists(folder)) return;
        var info = new DirectoryInfo(folder);
        if (info.Attributes.HasFlag(FileAttributes.ReparsePoint) || info.LinkTarget is not null)
            throw new IOException("A teach recording folder is a link, and nth does not follow it.");
    }

    private static void RefuseFileLink(string path)
    {
        if (File.Exists(path) && File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))
            throw new IOException("A teach recording file is a link, and nth does not follow it.");
    }

    private string TeachFilePath(string assistantId, string recordingId, string relative)
    {
        CheckTeachFile(relative);
        var folder = TeachFolderPath(assistantId, recordingId);
        var path = Path.GetFullPath(Path.Combine(folder, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!path.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new IOException("A teach recording file resolves outside its folder.");
        return path;
    }

    /// <summary>The ids of the assistant's recordings (folders that hold a manifest or any file), in id order.</summary>
    public IReadOnlyList<string> TeachRecordingIds(string assistantId)
    {
        CheckId(assistantId);
        lock (gate)
        {
            ThrowIfClosed();
            var folder = Path.Combine(TeachRoot, assistantId);
            if (!Directory.Exists(folder)) return [];
            return [.. Directory.EnumerateDirectories(folder).Select(d => Path.GetFileName(d)).Where(IsId).Order(StringComparer.Ordinal)];
        }
    }

    /// <summary>Whether a recording has a folder.</summary>
    public bool TeachRecordingExists(string assistantId, string recordingId)
    {
        lock (gate)
        {
            ThrowIfClosed();
            return Directory.Exists(TeachFolderPath(assistantId, recordingId));
        }
    }

    /// <summary>Writes a recording's file whole, temp-then-rename (15.3): a crash leaves the old file or the new one. Creates the folder.</summary>
    public void WriteTeachFile(string assistantId, string recordingId, string relative, byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.LongLength > MaxTeachFileBytes) throw new InvalidDataException("A teach file is not written over " + MaxTeachFileBytes / 1024 / 1024 + " MB.");
        lock (gate)
        {
            ThrowIfClosed();
            var path = TeachFilePath(assistantId, recordingId, relative);
            var folder = TeachFolderPath(assistantId, recordingId);
            RefuseLinks(folder);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            RefuseLinks(folder);
            RefuseLinks(Path.GetDirectoryName(path)!);
            WriteAtomic(path, bytes);
        }
    }

    /// <summary>Appends one line to a JSON-lines file and flushes it to disk, so a crash loses at most the line being written.</summary>
    public void AppendTeachLine(string assistantId, string recordingId, string relative, string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        if (!relative.EndsWith(".jsonl", StringComparison.Ordinal)) throw new ArgumentException("Only a JSON-lines file takes a line.", nameof(relative));
        if (line.Contains('\n', StringComparison.Ordinal) || line.Contains('\r', StringComparison.Ordinal)) throw new ArgumentException("A line has no line break.", nameof(line));
        var bytes = Utf8.GetBytes(line + "\n");
        if (bytes.Length > MaxTeachLineBytes) throw new InvalidDataException("A teach line is longer than " + MaxTeachLineBytes / 1024 + " KB.");
        lock (gate)
        {
            ThrowIfClosed();
            var path = TeachFilePath(assistantId, recordingId, relative);
            var folder = TeachFolderPath(assistantId, recordingId);
            RefuseLinks(folder);
            Directory.CreateDirectory(folder);
            RefuseLinks(folder);
            RefuseFileLink(path);
            using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
            stream.Write(bytes, 0, bytes.Length);
            stream.Flush(flushToDisk: true);
        }
    }

    /// <summary>A recording file's bytes, or null when it does not exist. A file over <see cref="MaxTeachFileBytes"/> is refused.</summary>
    public byte[]? ReadTeachFile(string assistantId, string recordingId, string relative)
    {
        lock (gate)
        {
            ThrowIfClosed();
            var path = TeachFilePath(assistantId, recordingId, relative);
            RefuseLinks(TeachFolderPath(assistantId, recordingId));
            RefuseFileLink(path);
            if (!File.Exists(path)) return null;
            if (new FileInfo(path).Length > MaxTeachFileBytes) throw new InvalidDataException("A teach file is larger than " + MaxTeachFileBytes / 1024 / 1024 + " MB.");
            return File.ReadAllBytes(path);
        }
    }

    /// <summary>
    /// The lines of a JSON-lines file, in order. A last line a crash cut short (no line break after it) is left out; a file that is not
    /// there is no lines.
    /// </summary>
    public IReadOnlyList<string> ReadTeachLines(string assistantId, string recordingId, string relative)
    {
        if (!relative.EndsWith(".jsonl", StringComparison.Ordinal)) throw new ArgumentException("Only a JSON-lines file is read in lines.", nameof(relative));
        var bytes = ReadTeachFile(assistantId, recordingId, relative);
        if (bytes is null || bytes.Length == 0) return [];
        var text = Utf8.GetString(bytes);
        var end = text.LastIndexOf('\n');
        if (end < 0) return [];
        return [.. text[..end].Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0)];
    }

    /// <summary>The names of a recording's files, as <see cref="WriteTeachFile"/> takes them ("recording.json", "frames/0007.jpg").</summary>
    public IReadOnlyList<string> TeachFiles(string assistantId, string recordingId)
    {
        lock (gate)
        {
            ThrowIfClosed();
            var folder = TeachFolderPath(assistantId, recordingId);
            if (!Directory.Exists(folder)) return [];
            RefuseLinks(folder);
            return [.. Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
                .Select(p => Path.GetRelativePath(folder, p).Replace('\\', '/'))
                .Where(n => TeachFileShape().IsMatch(n)).Order(StringComparer.Ordinal)];
        }
    }

    /// <summary>The bytes a recording's files take on disk.</summary>
    public long TeachBytes(string assistantId, string recordingId)
    {
        lock (gate)
        {
            ThrowIfClosed();
            var folder = TeachFolderPath(assistantId, recordingId);
            if (!Directory.Exists(folder)) return 0;
            RefuseLinks(folder);
            return Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Sum(p => new FileInfo(p).Length);
        }
    }

    /// <summary>Removes one file of a recording; false when it was not there.</summary>
    public bool DeleteTeachFile(string assistantId, string recordingId, string relative)
    {
        lock (gate)
        {
            ThrowIfClosed();
            var path = TeachFilePath(assistantId, recordingId, relative);
            RefuseLinks(TeachFolderPath(assistantId, recordingId));
            if (!File.Exists(path)) return false;
            File.Delete(path);
            return true;
        }
    }

    /// <summary>
    /// Removes a recording: its folder and everything in it. False when there was none. A folder that is a link is refused, not followed.
    /// This is a plain delete, not a wipe: on a solid-state drive the freed blocks are Windows' to reuse.
    /// </summary>
    public bool DeleteTeachRecording(string assistantId, string recordingId)
    {
        lock (gate)
        {
            ThrowIfClosed();
            var folder = TeachFolderPath(assistantId, recordingId);
            if (!Directory.Exists(folder)) return false;
            RefuseLinks(folder);
            Directory.Delete(folder, recursive: true);
            return true;
        }
    }

    /// <summary>Removes the temporary files a crash left in the recordings; returns how many.</summary>
    public int RemoveTeachTempFiles()
    {
        lock (gate)
        {
            ThrowIfClosed();
            if (!Directory.Exists(TeachRoot)) return 0;
            var removed = 0;
            foreach (var path in Directory.EnumerateFiles(TeachRoot, ".*" + TempSuffix, SearchOption.AllDirectories))
            {
                File.Delete(path);
                removed++;
            }
            return removed;
        }
    }

    // ---- staging: the folder a skill is written to before the own-skill service reads it ----

    /// <summary>A new, empty folder beside the recordings (<c>teach/&lt;assistant&gt;/.stage-&lt;id&gt;</c>) for a SKILL.md about to be added.</summary>
    public string NewTeachStaging(string assistantId)
    {
        CheckId(assistantId);
        lock (gate)
        {
            ThrowIfClosed();
            var path = Path.Combine(Created(Path.Combine(TeachRoot, assistantId)), ".stage-" + Guid.NewGuid().ToString("N")[..12]);
            Directory.CreateDirectory(path);
            return path;
        }
    }

    /// <summary>Removes a staging folder made by <see cref="NewTeachStaging"/>; a path that is not one is refused.</summary>
    public void DeleteTeachStaging(string assistantId, string path)
    {
        CheckId(assistantId);
        lock (gate)
        {
            ThrowIfClosed();
            var parent = Path.GetFullPath(Path.Combine(TeachRoot, assistantId));
            var full = Path.GetFullPath(path);
            if (!string.Equals(Path.GetDirectoryName(full), parent, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(full).StartsWith(".stage-", StringComparison.Ordinal))
                throw new IOException("That is not a teach staging folder.");
            if (!Directory.Exists(full)) return;
            RefuseLinks(full);
            Directory.Delete(full, recursive: true);
        }
    }

    /// <summary>Removes every staging folder a crash left for the assistant; returns how many.</summary>
    public int RemoveTeachStagings(string assistantId)
    {
        CheckId(assistantId);
        lock (gate)
        {
            ThrowIfClosed();
            var parent = Path.Combine(TeachRoot, assistantId);
            if (!Directory.Exists(parent)) return 0;
            var removed = 0;
            foreach (var folder in Directory.EnumerateDirectories(parent, ".stage-*"))
            {
                RefuseLinks(folder);
                Directory.Delete(folder, recursive: true);
                removed++;
            }
            return removed;
        }
    }
}
