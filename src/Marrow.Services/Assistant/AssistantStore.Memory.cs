// PORT-ONLY: Dextra, nth's assistant (DESIGN.md revision 5, sections 8.1 and 15.3; unit AS-12). No Swift counterpart.
// ED-NEW-dextra-as12-4: the memory files go through the store, so they share its folder lock, its closed check and its
// temp-then-rename write. The store knows nothing about notes: it reads and writes the named markdown files of a scope
// (an assistant's id, or "shared") and the one state document, and refuses any name outside that shape.
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Marrow.Services.Assistant;

public sealed partial class AssistantStore
{
    /// <summary>The memory set every assistant shares (8.1: <c>memory/shared/about-you.md</c>).</summary>
    public const string SharedScope = "shared";

    /// <summary>The memory state document (<c>memory/state.json</c>): deleted-note hashes, import fingerprints, the last fold.</summary>
    public const string MemoryStateFile = "state.json";

    /// <summary>The most a memory file is read as (a larger file is damaged or planted, and is left alone).</summary>
    public const int MaxMemoryFileBytes = 4 * 1024 * 1024;

    [GeneratedRegex(@"\A(?:MEMORY|[a-z0-9][a-z0-9-]{0,45})\.md\z")] private static partial Regex MemoryFileShape();

    private string MemoryRoot => Path.Combine(Root, "memory");

    private string MemoryScopeFolder(string scope) => scope == SharedScope ? SharedMemoryFolder() : MemoryFolder(scope);

    private static void CheckScope(string scope)
    {
        if (scope != SharedScope) CheckId(scope);
    }

    private static void CheckMemoryFile(string fileName)
    {
        if (!MemoryFileShape().IsMatch(fileName)) throw new ArgumentException($"\"{fileName}\" is not a memory file name.", nameof(fileName));
    }

    /// <summary>The scopes that have a folder under <c>memory</c>: "shared" and each assistant that has kept a note.</summary>
    public IReadOnlyList<string> MemoryScopes()
    {
        lock (gate)
        {
            ThrowIfClosed();
            if (!Directory.Exists(MemoryRoot)) return [];
            return [.. Directory.EnumerateDirectories(MemoryRoot).Select(d => Path.GetFileName(d)).Where(n => n == SharedScope || IsId(n)).Order(StringComparer.Ordinal)];
        }
    }

    /// <summary>The names of the scope's memory files (<c>MEMORY.md</c> and the topic files), in name order.</summary>
    public IReadOnlyList<string> MemoryFileNames(string scope)
    {
        CheckScope(scope);
        lock (gate)
        {
            ThrowIfClosed();
            var folder = Path.Combine(MemoryRoot, scope);
            if (!Directory.Exists(folder)) return [];
            return [.. Directory.EnumerateFiles(folder, "*.md").Select(p => Path.GetFileName(p)).Where(n => MemoryFileShape().IsMatch(n)).Order(StringComparer.Ordinal)];
        }
    }

    /// <summary>
    /// A memory file's text, or null when it does not exist. A file over <see cref="MaxMemoryFileBytes"/> is not read:
    /// <see cref="InvalidDataException"/>. A file that cannot be read throws <see cref="IOException"/>.
    /// </summary>
    public string? ReadMemoryFile(string scope, string fileName)
    {
        CheckScope(scope);
        CheckMemoryFile(fileName);
        lock (gate)
        {
            ThrowIfClosed();
            var path = Path.Combine(MemoryRoot, scope, fileName);
            if (!File.Exists(path)) return null;
            if (new FileInfo(path).Length > MaxMemoryFileBytes) throw new InvalidDataException($"The memory file \"{fileName}\" is larger than {MaxMemoryFileBytes / 1024 / 1024} MB.");
            return File.ReadAllText(path, Utf8);
        }
    }

    /// <summary>Writes a memory file whole, temp-then-rename (15.3): a crash leaves the old file or the new one.</summary>
    public void WriteMemoryFile(string scope, string fileName, string text)
    {
        CheckScope(scope);
        CheckMemoryFile(fileName);
        ArgumentNullException.ThrowIfNull(text);
        var bytes = Utf8.GetBytes(text);
        if (bytes.Length > MaxMemoryFileBytes) throw new InvalidDataException("A memory file is not written over " + MaxMemoryFileBytes / 1024 / 1024 + " MB.");
        lock (gate)
        {
            ThrowIfClosed();
            var folder = MemoryScopeFolder(scope);
            WriteAtomic(Path.Combine(folder, fileName), bytes);
        }
    }

    /// <summary>Removes a memory file; false when it was not there.</summary>
    public bool DeleteMemoryFile(string scope, string fileName)
    {
        CheckScope(scope);
        CheckMemoryFile(fileName);
        lock (gate)
        {
            ThrowIfClosed();
            var path = Path.Combine(MemoryRoot, scope, fileName);
            if (!File.Exists(path)) return false;
            File.Delete(path);
            return true;
        }
    }

    /// <summary>The memory state document, or null when there is none or it does not parse.</summary>
    public JsonObject? MemoryState()
    {
        lock (gate)
        {
            ThrowIfClosed();
            return ReadDocument(Path.Combine(MemoryRoot, MemoryStateFile)) as JsonObject;
        }
    }

    public void SaveMemoryState(JsonObject document)
    {
        ArgumentNullException.ThrowIfNull(document);
        lock (gate)
        {
            ThrowIfClosed();
            Directory.CreateDirectory(MemoryRoot);
            SaveDocument(Path.Combine(MemoryRoot, MemoryStateFile), document);
        }
    }

    /// <summary>Removes the temporary files a crash left in the memory folders; returns how many.</summary>
    public int RemoveMemoryTempFiles()
    {
        lock (gate)
        {
            ThrowIfClosed();
            if (!Directory.Exists(MemoryRoot)) return 0;
            var removed = 0;
            foreach (var folder in Directory.EnumerateDirectories(MemoryRoot).Prepend(MemoryRoot))
            {
                foreach (var path in Directory.EnumerateFiles(folder, ".*" + TempSuffix, SearchOption.TopDirectoryOnly))
                {
                    File.Delete(path);
                    removed++;
                }
            }
            return removed;
        }
    }
}
