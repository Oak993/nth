// PORT-ONLY: web surfaces W13 (DESIGN.md 6.5: "nth.storage ... per artifact, saved with the thread, 20 MB cap"; grants
// "listed and removable in the artifact's menu"; "Allow network"; ED-NEW-web-w13-2, -4). What the bridge keeps for one
// artifact, in that artifact's own folder beside its versions: storage.json (the page's keys and values) and
// grants.json (model use allowed, the tools allowed, the network sites allowed). Each file is written atomically; both
// go when the artifact is removed or the thread's artifacts are cleared, and they count toward "Artifact storage used".
// No Swift counterpart.
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Marrow.Core.Models;
using Marrow.Core.Port;

namespace Marrow.Services.Web;

/// <summary>A tool the user allowed for one artifact.</summary>
public sealed record ArtifactToolGrant(string Server, string Tool);

/// <summary>An artifact's grants: model use, the tools, and the exact network sites (null while network is off).</summary>
public sealed record ArtifactGrants(bool Model, IReadOnlyList<ArtifactToolGrant> Tools, IReadOnlyList<string>? NetworkHosts)
{
    public static ArtifactGrants None { get; } = new(false, [], null);

    public bool AllowsTool(string server, string tool) => Tools.Any(t => t.Server == server && t.Tool == tool);
}

public sealed class ArtifactData
{
    /// <summary>DESIGN 6.5: the storage cap per artifact (keys and values, as UTF-8).</summary>
    public const long MaxStorageBytes = 20L * 1024 * 1024;

    public const int MaxKeyLength = 1024;
    public const string StorageFile = "storage.json";
    public const string GrantsFile = "grants.json";

    private readonly ArtifactStore store;
    private readonly object gate = new();
    private readonly Dictionary<string, SortedDictionary<string, string>> storage = new(StringComparer.OrdinalIgnoreCase);
    private readonly Logger log = new(Logger.Subsystem, "web");

    public ArtifactData(ArtifactStore store)
    {
        this.store = store;
        store.Changed += change =>
        {
            if (change.Change != ArtifactChangeKind.Removed) return;
            lock (gate) storage.Remove(store.Folder(change.Thread, change.Id));
        };
    }

    /// <summary>Raised after an artifact's grants change (the ⋮ menu lists them).</summary>
    public event Action<ArtifactAddress>? GrantsChanged;

    // ---- storage ----

    public string? Get(ArtifactAddress artifact, string key)
    {
        CheckKey(key);
        lock (gate) return Load(artifact).TryGetValue(key, out var value) ? value : null;
    }

    /// <summary>Sets one key; refused with quota_exceeded when the artifact's storage would pass 20 MB.</summary>
    public void Set(ArtifactAddress artifact, string key, string value)
    {
        CheckKey(key);
        ArgumentNullException.ThrowIfNull(value);
        lock (gate)
        {
            var map = new SortedDictionary<string, string>(Load(artifact), StringComparer.Ordinal);
            var used = Bytes(map) - (map.TryGetValue(key, out var old) ? Entry(key, old) : 0) + Entry(key, value);
            if (used > MaxStorageBytes)
                throw new ArtifactBridgeException(ArtifactBridgeErrors.QuotaExceeded, "This artifact's storage is full (20 MB).");
            map[key] = value;
            Save(artifact, map);
            storage[Folder(artifact)] = map;
        }
    }

    public bool Remove(ArtifactAddress artifact, string key)
    {
        CheckKey(key);
        lock (gate)
        {
            var map = new SortedDictionary<string, string>(Load(artifact), StringComparer.Ordinal);
            if (!map.Remove(key)) return false;
            Save(artifact, map);
            storage[Folder(artifact)] = map;
            return true;
        }
    }

    /// <summary>The keys that start with <paramref name="prefix"/>, in ordinal order.</summary>
    public IReadOnlyList<string> Keys(ArtifactAddress artifact, string prefix)
    {
        lock (gate) return Load(artifact).Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToList();
    }

    /// <summary>The bytes the artifact's storage holds against its cap.</summary>
    public long StorageBytes(ArtifactAddress artifact)
    {
        lock (gate) return Bytes(Load(artifact));
    }

    // ---- grants ----

    public ArtifactGrants Grants(ArtifactAddress artifact)
    {
        lock (gate)
        {
            var path = Path.Combine(Folder(artifact), GrantsFile);
            if (!File.Exists(path)) return ArtifactGrants.None;
            try
            {
                var root = JsonNode.Parse(File.ReadAllBytes(path))?.AsObject();
                if (root is null) return ArtifactGrants.None;
                var tools = root["tools"]?.AsArray().OfType<JsonObject>()
                    .Select(t => (Server: t["server"]?.GetValue<string>(), Tool: t["tool"]?.GetValue<string>()))
                    .Where(t => !string.IsNullOrEmpty(t.Server) && !string.IsNullOrEmpty(t.Tool))
                    .Select(t => new ArtifactToolGrant(t.Server!, t.Tool!)).ToList() ?? [];
                var hosts = root["network"] is JsonArray network ? network.Select(h => h?.GetValue<string>()).OfType<string>().ToList() : null;
                return new ArtifactGrants(root["model"]?.GetValue<bool>() == true, tools, hosts);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or FormatException)
            {
                log.Error($"web: grants of artifact {artifact.Id} could not be read ({error.Message}); nothing is granted");
                return ArtifactGrants.None;
            }
        }
    }

    public void AllowModel(ArtifactAddress artifact, bool allowed) => Change(artifact, g => g with { Model = allowed });

    public void AllowTool(ArtifactAddress artifact, string server, string tool) =>
        Change(artifact, g => g.AllowsTool(server, tool) ? g : g with { Tools = [.. g.Tools, new ArtifactToolGrant(server, tool)] });

    public void RevokeTool(ArtifactAddress artifact, string server, string tool) =>
        Change(artifact, g => g with { Tools = g.Tools.Where(t => !(t.Server == server && t.Tool == tool)).ToList() });

    /// <summary>Allow network for exactly these sites (null turns network off).</summary>
    public void AllowNetwork(ArtifactAddress artifact, IReadOnlyList<string>? hosts) =>
        Change(artifact, g => g with { NetworkHosts = hosts?.Select(h => h.ToLowerInvariant()).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList() });

    private void Change(ArtifactAddress artifact, Func<ArtifactGrants, ArtifactGrants> change)
    {
        lock (gate)
        {
            var grants = change(Grants(artifact));
            var root = new JsonObject
            {
                ["format"] = 1,
                ["model"] = grants.Model,
                ["tools"] = new JsonArray(grants.Tools.Select(t => (JsonNode)new JsonObject { ["server"] = t.Server, ["tool"] = t.Tool }).ToArray()),
                ["network"] = grants.NetworkHosts is { } hosts ? new JsonArray(hosts.Select(h => (JsonNode)JsonValue.Create(h)!).ToArray()) : null,
            };
            Write(artifact, GrantsFile, Encoding.UTF8.GetBytes(root.ToJsonString()));
        }

        GrantsChanged?.Invoke(artifact);
    }

    // ---- files ----

    private string Folder(ArtifactAddress artifact) => store.Folder(artifact.Thread, artifact.Id);

    private SortedDictionary<string, string> Load(ArtifactAddress artifact)
    {
        var folder = Folder(artifact);
        if (storage.TryGetValue(folder, out var cached)) return cached;
        var map = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var path = Path.Combine(folder, StorageFile);
        if (File.Exists(path))
        {
            try
            {
                if (JsonNode.Parse(File.ReadAllBytes(path))?["values"] is JsonObject values)
                    foreach (var (key, value) in values)
                        if (value is JsonValue text && text.TryGetValue<string>(out var s)) map[key] = s;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
            {
                log.Error($"web: storage of artifact {artifact.Id} could not be read ({error.Message}); it starts empty");
            }
        }

        storage[folder] = map;
        return map;
    }

    private void Save(ArtifactAddress artifact, SortedDictionary<string, string> map)
    {
        var values = new JsonObject();
        foreach (var (key, value) in map) values[key] = value;
        Write(artifact, StorageFile, Encoding.UTF8.GetBytes(new JsonObject { ["format"] = 1, ["values"] = values }.ToJsonString()));
    }

    private void Write(ArtifactAddress artifact, string name, byte[] bytes)
    {
        if (store.Find(artifact.Thread, artifact.Id) is null)
            throw new ArtifactBridgeException(ArtifactBridgeErrors.NotFound, "This artifact no longer exists.");
        FileOps.WriteAtomic(Path.Combine(Folder(artifact), name), bytes);
    }

    private static long Entry(string key, string value) => Encoding.UTF8.GetByteCount(key) + Encoding.UTF8.GetByteCount(value);

    private static long Bytes(SortedDictionary<string, string> map) => map.Sum(pair => Entry(pair.Key, pair.Value));

    private static void CheckKey(string key)
    {
        if (string.IsNullOrEmpty(key) || key.Length > MaxKeyLength)
            throw new ArtifactBridgeException(ArtifactBridgeErrors.InvalidRequest, "A storage key is a non-empty string of at most 1,024 characters.");
    }
}
