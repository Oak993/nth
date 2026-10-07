using System.Text;
using System.Text.Json;
using Marrow.Core.Port.Skills.Catalogue;
using Marrow.Services.Port.Skills.Generations;
using Marrow.Services.Port.Skills.Import;

namespace Marrow.Services.Port.Skills.Custom;

/// <summary>RR-18: check first, then install only that frozen snapshot. Opening/listing does no network work. Registry publication is the commit point for command registration.</summary>
public sealed class CustomSources : ICustomAddBackend, IImportedSkillSource, IOwnPluginSource
{
    private readonly string root;
    private readonly ICustomSourceFiles files;
    private readonly ICustomGitHubSource github;
    private readonly TimeProvider clock;
    private readonly object gate = new();
    private readonly Dictionary<string, CheckedCustomItem> pending = [];
    private OwnItem[] items;

    public CustomSources(string root, ICustomSourceFiles files, ICustomGitHubSource github, TimeProvider clock)
    {
        this.root = Path.GetFullPath(root);
        this.files = files;
        this.github = github;
        this.clock = clock;
        var bytes = files.ReadRegistry(this.root);
        items = bytes is null ? [] : JsonSerializer.Deserialize(bytes, CustomSourceJsonContext.Default.OwnItemArray) ?? throw new InvalidDataException("Invalid own-items registry.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            if (!Guid.TryParseExact(item.Id, "N", out _) || !ids.Add(item.Id) || !Enum.IsDefined(item.Kind) || !Enum.IsDefined(item.Provenance.Source)) throw new InvalidDataException("Invalid own-items registry entry.");
            foreach (var pin in item.Files) CustomSourcePaths.Require(pin.Path);
        }
    }

    public async Task<IReadOnlyList<CustomAddCandidate>> Check(CustomAddRequest request, CancellationToken cancel)
    {
        cancel.ThrowIfCancellationRequested();
        if (!Enum.IsDefined(request.Kind) || !Enum.IsDefined(request.Source) || string.IsNullOrWhiteSpace(request.Location)) throw new ArgumentException("Choose a custom source.");
        Dictionary<string, byte[]> snapshot;
        string? commit = null, url = null;
        string hash;
        switch (request.Source)
        {
            case CustomAddSource.Folder:
                snapshot = CustomSourcePaths.Validate(await files.ReadFolder(request.Location, cancel).ConfigureAwait(false));
                hash = CustomSourcePaths.Fingerprint(snapshot);
                break;
            case CustomAddSource.Zip:
                var zip = await files.ReadZip(request.Location, cancel).ConfigureAwait(false);
                hash = CustomSourcePaths.Hash(zip);
                snapshot = CustomSourcePaths.Unzip(zip, false);
                break;
            case CustomAddSource.GitHub:
                var repo = CustomSourcePaths.Repository(request.Location);
                var archive = await github.Fetch(repo, cancel).ConfigureAwait(false);
                if (archive.Commit.Length != 40 || archive.Commit.Any(c => !Uri.IsHexDigit(c)) || archive.Url != "https://codeload.github.com/" + repo + "/zip/" + archive.Commit)
                    throw new InvalidDataException("GitHub source did not return a pinned official archive.");
                commit = archive.Commit;
                url = archive.Url;
                hash = CustomSourcePaths.Hash(archive.Bytes);
                snapshot = CustomSourcePaths.Unzip(archive.Bytes, true);
                break;
            default: throw new ArgumentException("Unsupported source.");
        }
        cancel.ThrowIfCancellationRequested();
        var provenance = new CustomProvenance(request.Source, request.Location, commit, url, hash, clock.GetUtcNow());
        var result = new List<CustomAddCandidate>();
        if (request.Kind == CustomAddKind.Tools)
        {
            var executables = snapshot.Where(p => p.Key.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && p.Value.Length >= 2 && p.Value[0] == 'M' && p.Value[1] == 'Z').Select(p => p.Key).ToArray();
            var matching = executables.Where(p => Path.GetFileNameWithoutExtension(p).Equals(request.Command, StringComparison.OrdinalIgnoreCase)).ToArray();
            var executable = matching.Length == 1 ? matching[0] : executables.Length == 1 ? executables[0] : null;
            var problem = executable is null ? "Choose a source with one Windows executable, or enter the primary executable's command." :
                string.IsNullOrWhiteSpace(request.Name) || request.Name.Any(char.IsControl) ? "Enter a tool name." :
                !SkillName.IsValid(request.Command) ? "Enter a command using lowercase letters, digits and hyphens." : null;
            AddCandidate(request.Name, "Custom tool", problem, snapshot, executable, request.Command);
        }
        else
        {
            var markers = snapshot.Keys.Where(p => request.Kind == CustomAddKind.Skills ? p.EndsWith("SKILL.md", StringComparison.Ordinal) && (p == "SKILL.md" || p.EndsWith("/SKILL.md", StringComparison.Ordinal)) :
                p == ".claude-plugin/plugin.json" || p.EndsWith("/.claude-plugin/plugin.json", StringComparison.Ordinal) || p == ".codex-plugin/plugin.json" || p.EndsWith("/.codex-plugin/plugin.json", StringComparison.Ordinal)).Order(StringComparer.Ordinal).ToArray();
            var seenRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var marker in markers)
            {
                var suffix = request.Kind == CustomAddKind.Skills ? "SKILL.md" : marker.Contains(".claude-plugin/", StringComparison.Ordinal) ? ".claude-plugin/plugin.json" : ".codex-plugin/plugin.json";
                var prefix = marker[..^suffix.Length];
                if (!seenRoots.Add(prefix)) continue;
                string? name = null, description = null;
                try
                {
                    if (request.Kind == CustomAddKind.Skills)
                    {
                        var parsed = ForeignFrontmatter.Parse(Encoding.UTF8.GetString(snapshot[marker]));
                        name = parsed.Name; description = parsed.Description;
                    }
                    else
                    {
                        using var json = JsonDocument.Parse(snapshot[marker]);
                        name = json.RootElement.GetProperty("name").GetString();
                        description = json.RootElement.TryGetProperty("description", out var d) ? d.GetString() : "";
                    }
                }
                catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException) { /* listed as invalid below */ }
                var problem = !SkillName.IsValid(name ?? "") ? "The manifest needs a valid name." :
                    request.Kind == CustomAddKind.Skills && (string.IsNullOrWhiteSpace(description) || description.Length > 1024 || description.Any(char.IsControl)) ? "SKILL.md needs a description of at most 1024 characters." : null;
                var subset = snapshot.Where(p => p.Key.StartsWith(prefix, StringComparison.Ordinal)).ToDictionary(p => p.Key[prefix.Length..], p => p.Value);
                AddCandidate(name ?? marker, description ?? "", problem, subset, null, null);
            }
        }
        return result;
        void AddCandidate(string name, string description, string? problem, Dictionary<string, byte[]> content, string? executable, string? command)
        {
            var candidate = new CustomAddCandidate(Guid.NewGuid().ToString("N"), name, description, problem);
            if (problem is null)
                lock (gate)
                {
                    // Bound retained check snapshots; checks themselves never write the source or registry.
                    if (pending.Count >= 4096) throw new InvalidOperationException("Too many checked items; finish or discard the current checks.");
                    pending.Add(candidate.Key, new(candidate, request.Kind, provenance, content, executable, command));
                }
            result.Add(candidate);
        }
    }

    public Task<OwnItem> Add(CustomAddCandidate candidate, IProgress<double> progress, CancellationToken cancel) => Task.Run(() =>
    {
        cancel.ThrowIfCancellationRequested();
        OwnItem installed;
        lock (gate)
        {
            cancel.ThrowIfCancellationRequested();
            if (!pending.TryGetValue(candidate.Key, out var checkedItem) || checkedItem.Candidate != candidate || candidate.Problem is not null)
                throw new InvalidOperationException("Check this item again before adding it.");
            if (items.Any(i => i.Kind == checkedItem.Kind && (i.Name.Equals(candidate.Name, StringComparison.OrdinalIgnoreCase) || checkedItem.Command is not null && i.Command == checkedItem.Command)))
                throw new InvalidOperationException("An own item already uses that name or command.");
            var id = Guid.NewGuid().ToString("N");
            var content = checkedItem.Files.ToDictionary(p => "payload/" + p.Key, p => p.Value);
            if (checkedItem.Command is { } command && checkedItem.Executable is { } executable)
                content.Add("bin/" + command + ".cmd", Encoding.UTF8.GetBytes("@\"%~dp0..\\payload\\" + executable.Replace('/', '\\') + "\" %*\r\n@exit /b %errorlevel%\r\n"));
            installed = new(id, checkedItem.Kind, candidate.Name, candidate.Description, checkedItem.Command, checkedItem.Executable, true, checkedItem.Provenance,
                content.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => new CustomFilePin(p.Key, p.Value.LongLength, CustomSourcePaths.Hash(p.Value))).ToArray());
            var next = items.Append(installed).ToArray();
            files.Publish(root, id, content);
            try
            {
                cancel.ThrowIfCancellationRequested();
                files.SaveRegistry(root, JsonSerializer.SerializeToUtf8Bytes(next, CustomSourceJsonContext.Default.OwnItemArray));
            }
            catch { files.Delete(root, id); throw; }
            items = next;
            pending.Remove(candidate.Key);
        }
        // User code is never invoked under the registry gate or allowed to undo an already committed install.
        try { progress.Report(1); } catch (Exception) { }
        return installed;
    }, cancel);

    public IReadOnlyList<OwnItem> List(CustomAddKind kind) { lock (gate) return items.Where(i => i.Kind == kind).Select(Copy).ToArray(); }
    public void Discard(IEnumerable<CustomAddCandidate> candidates) { lock (gate) foreach (var c in candidates) pending.Remove(c.Key); }
    public void SetEnabled(string id, bool enabled)
    {
        lock (gate)
        {
            if (!items.Any(i => i.Id == id)) throw new KeyNotFoundException(id);
            var next = items.Select(i => i.Id == id ? i with { Enabled = enabled } : i).ToArray();
            files.SaveRegistry(root, JsonSerializer.SerializeToUtf8Bytes(next, CustomSourceJsonContext.Default.OwnItemArray));
            items = next;
        }
    }
    public void Remove(string id)
    {
        lock (gate)
        {
            if (!items.Any(i => i.Id == id)) return;
            var next = items.Where(i => i.Id != id).ToArray();
            files.SaveRegistry(root, JsonSerializer.SerializeToUtf8Bytes(next, CustomSourceJsonContext.Default.OwnItemArray));
            items = next;
            files.Delete(root, id);
        }
    }
    public IReadOnlyList<CustomCommand> Commands()
    {
        lock (gate) return items.Where(i => i.Enabled && i.Kind == CustomAddKind.Tools).Select(i =>
        {
            _ = Verified(i);
            return new CustomCommand(i.Command!, Path.Combine(root, "items", i.Id, "bin", i.Command + ".cmd"), i.Id);
        }).ToArray();
    }
    public IReadOnlyList<ImportedGenerationSkill> Snapshot()
    {
        lock (gate)
        {
            var result = new List<ImportedGenerationSkill>();
            foreach (var item in items.Where(i => i.Enabled && i.Kind is CustomAddKind.Skills or CustomAddKind.Plugins))
            {
                var content = Verified(item).ToDictionary(p => p.Key["payload/".Length..], p => p.Value);
                if (item.Kind == CustomAddKind.Skills) result.Add(new("own-" + item.Id, item.Name, item.Description, content));
                else foreach (var marker in content.Keys.Where(p => p.EndsWith("/SKILL.md", StringComparison.Ordinal) || p == "SKILL.md").Order(StringComparer.Ordinal))
                {
                    var metadata = ForeignFrontmatter.Parse(Encoding.UTF8.GetString(content[marker]));
                    if (!SkillName.IsValid(metadata.Name ?? "") || string.IsNullOrWhiteSpace(metadata.Description) || metadata.Description.Length > 1024) continue;
                    var prefix = marker[..^"SKILL.md".Length];
                    var skillFiles = content.Where(p => p.Key.StartsWith(prefix, StringComparison.Ordinal)).ToDictionary(p => p.Key[prefix.Length..], p => p.Value);
                    result.Add(new("own-" + item.Id + "/" + marker, metadata.Name!, metadata.Description!, skillFiles, "own-" + item.Id, prefix.TrimEnd('/')));
                }
            }
            return result;
        }
    }
    public IReadOnlyList<OwnGenerationPlugin> SnapshotPlugins()
    {
        lock (gate) return items.Where(i => i.Enabled && i.Kind == CustomAddKind.Plugins).Select(i =>
            new OwnGenerationPlugin("own-" + i.Id, Verified(i).ToDictionary(p => p.Key["payload/".Length..], p => p.Value))).ToArray();
    }
    /// <summary>Enabled plugin trees retain both manifests, hooks, MCP configuration and resources. Provider adapters can load these owned paths.</summary>
    public IReadOnlyList<string> PluginDirectories()
    {
        lock (gate) return items.Where(i => i.Enabled && i.Kind == CustomAddKind.Plugins).Select(i =>
        { _ = Verified(i); return Path.Combine(root, "items", i.Id, "payload"); }).ToArray();
    }
    private Dictionary<string, byte[]> Verified(OwnItem item)
    {
        var bytes = CustomSourcePaths.Validate(files.ReadInstalled(root, item.Id));
        if (bytes.Count != item.Files.Length) throw new InvalidDataException("Own item file inventory changed.");
        foreach (var pin in item.Files)
            if (!bytes.TryGetValue(pin.Path, out var value) || value.LongLength != pin.Size || CustomSourcePaths.Hash(value) != pin.Sha256)
                throw new InvalidDataException("Own item bytes changed since installation.");
        return bytes;
    }
    private static OwnItem Copy(OwnItem item) => item with { Files = item.Files.ToArray() };
}
