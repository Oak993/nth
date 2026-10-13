// PORT-ONLY: DESIGN.md 9.1; CU-DX-OPEN 5; ED-NEW-dextra-open-4.
// Explicit owner deletion keeps the retired profile and old mission/audit records.
using System.Text.Json.Nodes;
using Marrow.Services.Port.Skills.Store;

namespace Marrow.Services.Assistant;

public sealed partial class AssistantStore
{
    public static Marrow.Core.Port.Ambient<Action<string>> AsturDeleteCheckpoint { get; } = new(_ => { });

    public void DeleteAsturFiles(string id)
    {
        CheckId(id);
        lock (gate)
        {
            ThrowIfClosed();
            var read = AssistantProfileFile.Read(id, Profile(id));
            if (read.Document?.Profile is not { IsRoot: false, Retired: true }) throw new IOException("Only a retired Astur's files can be deleted.");
            var roots = new[]
            {
                Path.Combine(Root, "assistants", id, "folder"), Path.Combine(Root, "assistants", id, "browser"),
                Path.Combine(Root, "assistants", id, "duties.json"), Path.Combine(Root, "assistants", id, "watch"),
                Path.Combine(Root, "memory", id), Path.Combine(Root, "teach", id),
            };
            StorePaths.EnsureAncestors(Root);
            var entries = new List<string>();
            foreach (var path in roots) Inspect(path, entries);
            var intent = Path.Combine(Root, "astur-delete-" + id + ".json");
            RefuseFileLink(intent);
            var record = new JsonObject { ["version"] = 1, ["assistant"] = id };
            if (File.Exists(intent))
            {
                if (ReadDocument(intent) is not JsonObject prior || prior["version"] is not JsonValue v || !v.TryGetValue<int>(out var version) || version != 1
                    || prior["assistant"] is not JsonValue a || !a.TryGetValue<string>(out var owner) || owner != id)
                    throw new IOException("The deletion record cannot safely be continued.");
                record = (JsonObject)prior.DeepClone();
            }
            record["state"] = "pending";
            SaveDocument(intent, record);
            // Each operation is nonrecursive. A link is never followed by a recursive delete.
            foreach (var path in entries)
            {
                StorePaths.EnsureAncestors(path);
                if (Directory.Exists(path)) Directory.Delete(path, recursive: false);
                else if (File.Exists(path)) File.Delete(path);
                AsturDeleteCheckpoint.Current?.Invoke(path);
            }
            record["state"] = "done";
            SaveDocument(intent, record);
        }
    }

    private void Inspect(string path, List<string> entries)
    {
        var full = Path.GetFullPath(path);
        if (!full.StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(Root)) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new IOException("An Astur file resolves outside its storage root.");
        StorePaths.EnsureAncestors(full);
        if (!File.Exists(full) && !Directory.Exists(full)) return;
        if (Directory.Exists(full)) foreach (var child in Directory.EnumerateFileSystemEntries(full)) Inspect(child, entries);
        if (entries.Count >= 100_000) throw new IOException("Too many files to delete safely in one action.");
        entries.Add(full);
    }
}
