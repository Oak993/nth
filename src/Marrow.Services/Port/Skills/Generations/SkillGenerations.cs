// PORT-ONLY: SK-9 / DL-9. Generation composition only; SK-11 owns provider launch arguments.
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Marrow.Core.Port.Skills.Catalogue;
using Marrow.Services.Port.Skills.Store;

namespace Marrow.Services.Port.Skills.Generations;

/// <summary>One coordinator per root owner (SkillsStore, or SK-8's SkillsPipeline through IGenerationStore). Pool-thread work: copying and hashing never run on a UI executor here.</summary>
public sealed class SkillGenerations(StorePaths paths, IGenerationStore store, IStoreProtection? protection = null, Func<string, IReadOnlyList<ExistingSkill>>? clashSource = null,
    IImportedSkillSource? importedSource = null, IOwnPluginSource? ownPluginSource = null) : IDisposable
{
    private readonly object gate = new();
    private readonly ChatGenerationPaths chatPaths = new(paths);
    private readonly IStoreProtection acl = protection ?? new WindowsStoreProtection();
    private readonly Dictionary<string, Generation> spares = new(StringComparer.Ordinal);
    private bool disposed;
    private readonly HashSet<string> pendingBuilds = new(StringComparer.Ordinal);
    private readonly List<Task> workers = [];
    private readonly Dictionary<string, string> globalKeys = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Queue<(string Chat, string Key)>> recent = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Generation> listings = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> listingUsers = new(StringComparer.Ordinal);
    public GenerationLaunch? LastLaunch { get; private set; }
    public Exception? LastSpareFailure { get; private set; }
    private readonly HashSet<string> live = new(StringComparer.Ordinal);

    public void Collect() { lock (gate) store.Collect(); }

    public string Prepare(string provider, IEnumerable<GenerationItem> items, SkillSwitches switches, GenerationBudget budget,
        IEnumerable<ExistingSkill>? existing = null, bool global = true)
    {
        var imported = importedSource?.Snapshot() ?? [];
        var ownPlugins = ownPluginSource?.SnapshotPlugins() ?? [];
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            var plan = Compose(provider, items, switches, budget, existing ?? clashSource?.Invoke(provider) ?? throw new InvalidOperationException("A reliable clash snapshot is required."), imported, ownPlugins);
            var cacheKey = provider + "/" + plan.Key;
            if (global) globalKeys[provider] = plan.Key;
            Evict(provider, plan.Key);
            if (spares.TryGetValue(cacheKey, out var ready) && ready.Verify() && new StoreVerifier(acl).Verify(ready.Folder, plan.Manifest).IsValid) return plan.Key;
            if (ready is not null) { ready.Dispose(); spares.Remove(cacheKey); }
            spares[cacheKey] = Build(provider, "spare-" + plan.Key + "-" + Nonce(), plan);
            return plan.Key;
        }
    }

    public Generation Start(string provider, string chatId, IEnumerable<GenerationItem> items, SkillSwitches switches,
        GenerationBudget budget, IEnumerable<ExistingSkill>? existing = null)
    {
        var imported = importedSource?.Snapshot() ?? [];
        var ownPlugins = ownPluginSource?.SnapshotPlugins() ?? [];
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            StoreManifest.RequireRelative(chatId);
            if (chatId.Contains('/', StringComparison.Ordinal)) throw new ArgumentException("A chat id is one safe path component.", nameof(chatId));
            var liveKey = provider + "/" + chatId;
            if (live.Contains(liveKey)) throw new InvalidOperationException("This chat already has a live generation.");
            var plan = Compose(provider, items, switches, budget, existing ?? clashSource?.Invoke(provider) ?? throw new InvalidOperationException("A reliable clash snapshot is required."), imported, ownPlugins);
            var parent = ProviderRoot(provider);
            var target = paths.RequireManaged(Path.Combine(parent, chatPaths.Get(provider, chatId)));
            var previous = target;
            Generation? result = null;
            var reason = GenerationLaunchReason.MissingSpare;
            if (Directory.Exists(target))
            {
                if (new StoreVerifier(acl).Verify(target, plan.Manifest).IsValid)
                {
                    var lease = store.AcquireGeneration(target, plan.Manifest);
                    if (lease is not null) { result = Create(lease, plan); reason = GenerationLaunchReason.ExistingChat; }
                }
                if (result is null)
                {
                    store.Retire(target);
                    store.Collect();
                    if (Path.Exists(target)) target = paths.RequireManaged(Path.Combine(parent, "chat-" + chatId + "-" + Nonce())); // A leased/held old tree must never be changed underneath its reader.
                }
            }
            if (result is null)
            {
                var cacheKey = provider + "/" + plan.Key;
                if (spares.Remove(cacheKey, out var spare))
                {
                    try
                    {
                        if (spare.Verify() && new StoreVerifier(acl).Verify(spare.Folder, plan.Manifest).IsValid)
                        { result = PublishSpare(target, spare, plan); reason = GenerationLaunchReason.ReadySpare; }
                        else { store.Retire(spare.Folder); reason = GenerationLaunchReason.DamagedSpare; }
                    }
                    finally { spare.Dispose(); }
                }
                result ??= Build(provider, Path.GetFileName(target), plan);
                QueueReplacement(provider, plan);
            }
            try
            {
                chatPaths.Set(provider, chatId, Path.GetFileName(result.Folder));
                var diagnosticFile = Path.Combine(paths.Skills, "generation-launches.jsonl");
                StorePaths.EnsureAncestors(diagnosticFile);
                File.AppendAllText(diagnosticFile, JsonSerializer.Serialize(new { provider, chatId, key = plan.Key, reason = reason.ToString() }) + "\n");
                LastLaunch = new(provider, chatId, plan.Key, reason);
                result.PreviousFolder = result.Folder == previous ? null : previous;
                if (!recent.TryGetValue(provider, out var chats)) recent[provider] = chats = new();
                var previousChats = chats.Where(c => c.Chat != chatId).TakeLast(4).ToArray();
                chats.Clear();
                foreach (var c in previousChats) chats.Enqueue(c);
                chats.Enqueue((chatId, plan.Key));
                Evict(provider, plan.Key);
                live.Add(liveKey);
                result.OnDisposed = () => { lock (gate) live.Remove(liveKey); };
                return result;
            }
            catch
            {
                live.Remove(liveKey);
                result.Dispose();
                store.Retire(result.Folder);
                throw;
            }
        }
    }

    private string ProviderRoot(string provider)
    {
        GenerationFilters.RequireProvider(provider);
        var root = paths.RequireManaged(Path.Combine(paths.Views, provider));
        Directory.CreateDirectory(root);
        return root;
    }

    private GenerationPlan Compose(string provider, IEnumerable<GenerationItem> source, SkillSwitches switches,
        GenerationBudget budget, IEnumerable<ExistingSkill> existing, IReadOnlyList<ImportedGenerationSkill> importedSnapshot, IReadOnlyList<OwnGenerationPlugin> ownPlugins)
    {
        GenerationFilters.RequireProvider(provider);
        if (string.IsNullOrWhiteSpace(switches.Version)) throw new ArgumentException("A reviewed catalogue release version is required.", nameof(switches));
        var items = source.ToArray();
        if (items.Select(i => i.Store.Catalogue.Id).Distinct(StringComparer.Ordinal).Count() != items.Length)
            throw new ArgumentException("An installed item is supplied twice.", nameof(source));
        var user = existing.ToArray();
        var leases = new List<StoreLease>();
        var verified = new List<GenerationItem>();
        try
        {
            foreach (var item in items)
            {
                var lease = store.AcquireForViewBuild(item.Store.Catalogue.Id);
                if (lease is null) continue;
                leases.Add(lease);
                // The SK-5 inventory, not the caller's folder, chooses the actual installed bytes.
                if (!StorePaths.Comparer.Equals(lease.Folder, item.Store.Folder)) throw new ArgumentException("The generation descriptor is not the store's installed pin.");
                verified.Add(item);
            }
            var leftOut = new List<string>();
            var chosen = new List<GenerationSkill>();
            var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var room = budget.Room(user);
            var importedAvailable = importedSnapshot;
            var importedChosen = new List<(ImportedGenerationSkill Skill, GenerationSkill Meta)>();
            void ChooseImported(ImportedGenerationSkill imported)
            {
                if (!SkillName.IsValid(imported.Name) || string.IsNullOrWhiteSpace(imported.Description) || imported.Description.Any(char.IsControl) || !imported.Files.ContainsKey("SKILL.md")) return;
                if (GenerationFilters.Clashes(provider, imported.Name, user) || usedNames.Contains(imported.Name)) return;
                var meta = new GenerationSkill(imported.PluginId ?? (imported.Own ? imported.Id : "import"), imported.Name, imported.Id, imported.Description, SkillGroup.Other, 1_000, false,
                    imported.Own ? imported.ExplicitOnly : null);
                if (imported.Own && imported.ExplicitOnly && provider is not ("claude" or "codex" or "pi" or "native" or "cursor" or "devin" or "agy")) { leftOut.Add(meta.Id); return; }
                var cost = imported.Own && meta.ExplicitOnly ? 0 : imported.Name.Length + imported.Description.Length + 3;
                if (cost > room) { leftOut.Add(meta.Id); return; }
                room -= cost;
                usedNames.Add(imported.Name);
                importedChosen.Add((imported, meta));
            }
            foreach (var own in importedAvailable.Where(i => i.Own).OrderBy(i => i.Name, StringComparer.Ordinal)) ChooseImported(own);
            foreach (var skill in verified.SelectMany(i => i.Skills).OrderBy(s => s.DefaultOn ? 0 : 1).ThenBy(s => s.Priority).ThenBy(s => s.Id, StringComparer.Ordinal))
            {
                if (!switches.Enabled(skill) || !GenerationFilters.Allows(provider, skill) || GenerationFilters.Clashes(provider, skill.Name, user)) continue;
                if (usedNames.Contains(skill.Name)) continue;
                var sourceItem = verified.Single(i => i.Store.Catalogue.Id == skill.ItemId);
                var sourceFile = sourceItem.Store.Manifest.Files.Single(f => f.Path == skill.SourcePath + "/SKILL.md");
                var disclosure = SkillFrontmatter.Disclosure(Encoding.UTF8.GetString(ReadPinned(sourceItem.Store, sourceFile)));
                if (disclosure.Name != skill.Name || disclosure.Description != skill.Description)
                    throw new InvalidDataException("Reviewed disclosure metadata differs from the pinned skill.");
                if (skill.ExplicitOnly && provider is not ("native" or "cursor" or "devin" or "agy"))
                {
                    var item = verified.Single(i => i.Store.Catalogue.Id == skill.ItemId);
                    var pinned = item.Store.Manifest.Files.Single(f => f.Path == skill.SourcePath + "/SKILL.md");
                    if (!SkillFrontmatter.ExplicitOnly(Encoding.UTF8.GetString(ReadPinned(item.Store, pinned))))
                    { leftOut.Add(skill.Id); continue; } // ONDEMAND 1 forbids the old vendor patches. Never weaken explicit-only behavior.
                }
                var cost = skill.ExplicitOnly ? 0 : skill.Name.Length + skill.Description.Length + 3;
                if (cost > room) { leftOut.Add(skill.Id); continue; }
                room -= cost;
                usedNames.Add(skill.Name);
                chosen.Add(skill);
            }
            // SK-18 (OD-0118): skills the user brought in from other apps join the catalogue's, after it: a catalogue skill keeps a name, a provider's own skills still win a clash, the same budget applies.
            // Their on/off is the imported library's own (they are not catalogue items), and an empty source changes nothing: the key below is then exactly what it always was.
            // Provider imports retain OD-0118's catalogue-first rule. Explicit own additions have already reserved their names and budget.
            foreach (var imported in importedAvailable.Where(i => !i.Own).OrderBy(i => i.Name, StringComparer.Ordinal)) ChooseImported(imported);
            var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            var locations = new Dictionary<string, string>(StringComparer.Ordinal);
            var pluginRoots = new List<string>();
            if (GenerationFilters.PluginLoading(provider))
                files[".claude-plugin/plugin.json"] = JsonSerializer.SerializeToUtf8Bytes(new { name = "marrow", version = switches.Version });
            foreach (var item in verified)
            {
                var selected = chosen.Where(s => s.ItemId == item.Store.Catalogue.Id).ToArray();
                var enabledRoots = GenerationFilters.PluginLoading(provider)
                    ? item.PluginRoots.Where(r => switches.PluginEnabled(item.Store.Catalogue.Id, r)).ToArray() : [];
                foreach (var pluginRoot in enabledRoots)
                {
                    var root = "plugins/" + item.Store.Catalogue.Id + (pluginRoot.Length == 0 ? "" : "/" + pluginRoot);
                    StoreManifest.RequireRelative(root);
                    pluginRoots.Add(root);
                    var prefix = pluginRoot.Length == 0 ? "" : pluginRoot + "/";
                    foreach (var file in item.Store.Manifest.Files.Where(f => f.Path.StartsWith(prefix, StringComparison.Ordinal)))
                    {
                        var owningSkill = item.Skills.SingleOrDefault(s => file.Path.StartsWith(s.SourcePath + "/", StringComparison.Ordinal));
                        if (owningSkill is not null && !selected.Contains(owningSkill)) continue;
                        AddFile(root + "/" + file.Path[prefix.Length..], ReadPinned(item.Store, file));
                    }
                    foreach (var skill in selected.Where(s => s.SourcePath.StartsWith(prefix, StringComparison.Ordinal)))
                        locations.Add(skill.Id, root + "/" + skill.SourcePath[prefix.Length..] + "/SKILL.md");
                }
                // Plugin contents may be off while individual skills are on; those go into the ordinary skills-only view.
                foreach (var skill in selected.Where(s => !locations.ContainsKey(s.Id)))
                {
                    // SK-17: agy's added workspace discovers .agents/skills. Explicit-only skills remain outside it,
                    // available to Marrow's named invocation without disclosing them to the CLI's automatic catalogue.
                    var root = (provider == "agy" && !skill.ExplicitOnly ? ".agents/skills/" :
                        GenerationFilters.PluginLoading(provider) ? "skills/" : "") + skill.Name;
                    locations.Add(skill.Id, root + "/SKILL.md");
                    foreach (var file in item.Store.Manifest.Files.Where(f => f.Path.StartsWith(skill.SourcePath + "/", StringComparison.Ordinal)))
                        AddFile(root + "/" + file.Path[(skill.SourcePath.Length + 1)..], ReadPinned(item.Store, file));
                }
            }
            var ownPluginEntries = new Dictionary<string, string>(StringComparer.Ordinal);
            if (GenerationFilters.PluginLoading(provider)) foreach (var plugin in ownPlugins)
            {
                // Codex-only manifests still contribute ordinary skills; do not claim their hooks are Claude-compatible.
                if (!plugin.Files.ContainsKey(".claude-plugin/plugin.json")) continue;
                var root = "plugins/" + plugin.Id;
                pluginRoots.Add(root);
                ownPluginEntries.Add(root, ImportedFingerprint(new(plugin.Id, plugin.Id, "", plugin.Files)));
                foreach (var (path, bytes) in plugin.Files)
                {
                    var owningSkill = importedAvailable.OrderByDescending(s => s.PluginPath?.Length ?? 0).FirstOrDefault(s => s.PluginId == plugin.Id && (s.PluginPath!.Length == 0 || path.StartsWith(s.PluginPath + "/", StringComparison.Ordinal)));
                    if (owningSkill is not null && !importedChosen.Any(c => c.Skill.Id == owningSkill.Id)) continue;
                    // Unreadable skill metadata cannot bypass the budget through an otherwise valid plugin.
                    if (owningSkill is null && (path == "SKILL.md" || path.EndsWith("/SKILL.md", StringComparison.Ordinal))) continue;
                    AddFile(root + "/" + path, bytes);
                }
                foreach (var (skill, meta) in importedChosen.Where(c => c.Skill.PluginId == plugin.Id))
                    locations.Add(meta.Id, root + "/" + (skill.PluginPath!.Length == 0 ? "" : skill.PluginPath + "/") + "SKILL.md");
            }
            foreach (var (imported, meta) in importedChosen)
            {
                if (locations.ContainsKey(meta.Id)) continue;
                var root = (provider == "agy" && imported.Own && !meta.ExplicitOnly ? ".agents/skills/" : GenerationFilters.PluginLoading(provider) ? "skills/" : "") + imported.Name;
                locations.Add(meta.Id, root + "/SKILL.md");
                foreach (var (relative, bytes) in imported.Files.OrderBy(f => f.Key, StringComparer.Ordinal)) AddFile(root + "/" + relative, bytes);
            }
            var entries = chosen.Select(s => s.Id + "@" + verified.Single(i => i.Store.Catalogue.Id == s.ItemId).Store.Pin).Order(StringComparer.Ordinal).ToArray();
            // The switch for non-skill plugin contents also changes the resulting entry list and therefore the content key.
            var pluginEntries = pluginRoots.Order(StringComparer.Ordinal).Select(r => r + "@" + (ownPluginEntries.TryGetValue(r, out var ownPin) ? ownPin : verified.Single(i => r == "plugins/" + i.Store.Catalogue.Id || r.StartsWith("plugins/" + i.Store.Catalogue.Id + "/", StringComparison.Ordinal)).Store.Pin)).ToArray();
            var importedEntries = importedChosen.Select(c => c.Meta.Id + "@" + ImportedFingerprint(c.Skill)).Order(StringComparer.Ordinal).ToArray();
            var identity = importedEntries.Length == 0
                ? JsonSerializer.SerializeToUtf8Bytes(new { version = switches.Version, entries, plugins = pluginEntries })
                : JsonSerializer.SerializeToUtf8Bytes(new { version = switches.Version, entries, plugins = pluginEntries, imported = importedEntries });
            var key = Convert.ToHexStringLower(SHA256.HashData(identity))[..16];
            files["generation.json"] = identity; // trusted generated inventory member, also permits a valid empty generation.
            return new(key, new StoreManifest(files.Select(f => Pin(f.Key, f.Value))), files, importedChosen.Where(c => c.Skill.Own).Select(c => c.Meta).Concat(chosen).Concat(importedChosen.Where(c => !c.Skill.Own).Select(c => c.Meta)).ToArray(), locations, leftOut.ToArray(), pluginRoots.ToArray());
            void AddFile(string path, byte[] bytes)
            {
                StoreManifest.RequireRelative(path);
                if (!files.TryAdd(path, bytes)) throw new InvalidDataException("Two generation files occupy one path.");
            }
        }
        finally { foreach (var lease in leases) lease.Dispose(); }
    }

    // What makes two imported skills the same bytes: the name, the description and every file by path and hash. A change in an original a Link follows changes this, and so the generation's key.
    private static string ImportedFingerprint(ImportedGenerationSkill skill)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes(skill.Name + "\n" + skill.Description + "\n"));
        if (skill.Own || skill.ExplicitOnly) hash.AppendData(Encoding.UTF8.GetBytes("own=" + skill.Own + ";explicit=" + skill.ExplicitOnly + "\n"));
        foreach (var (path, bytes) in skill.Files.OrderBy(f => f.Key, StringComparer.Ordinal))
            hash.AppendData(Encoding.UTF8.GetBytes(path + "=" + Convert.ToHexStringLower(SHA256.HashData(bytes)) + "\n"));
        return Convert.ToHexStringLower(hash.GetHashAndReset())[..32];
    }

    private static byte[] ReadPinned(StoreItem item, CatalogueFile file)
    {
        StorePaths.EnsureAncestors(Path.Combine(item.Folder, file.Path.Replace('/', Path.DirectorySeparatorChar)));
        var bytes = File.ReadAllBytes(Path.Combine(item.Folder, file.Path.Replace('/', Path.DirectorySeparatorChar)));
        if (bytes.LongLength != file.Size || !Convert.ToHexStringLower(SHA256.HashData(bytes)).Equals(file.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Source bytes changed while composing a generation.");
        return bytes;
    }

    // Reserve the final pathname before the rename, so the existing SK-5 lease covers it throughout publication.
    private StoreLease Reserve(string folder, byte[] metadata)
    {
        paths.RequireManaged(folder);
        if (Path.Exists(folder)) throw new IOException("The publication target is occupied.");
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, "generation.json"), metadata);
        new StoreTrees(acl).Lock(folder);
        return store.AcquireGeneration(folder, new StoreManifest([Pin("generation.json", metadata)]))
            ?? throw new IOException("The publication reservation is unavailable.");
    }

    private void RemoveBootstrap(string folder)
    {
        paths.RequireManaged(folder);
        new StoreTrees(acl).Unlock(folder);
        // Only our own leased bootstrap is removed. The lease continues to reserve the same pathname for the final rename.
        Directory.Delete(folder, recursive: true);
    }

    private Generation PublishSpare(string target, Generation spare, GenerationPlan plan)
    {
        StoreLease? targetLease = null;
        try
        {
            targetLease = Reserve(target, plan.Files["generation.json"]);
            RemoveBootstrap(target);
            Directory.Move(spare.Folder, target);
            new StoreTrees(acl).Lock(target);
            if (!new StoreVerifier(acl).Verify(target, plan.Manifest).IsValid) throw new IOException("The claimed generation failed verification.");
            var result = Create(targetLease, plan);
            targetLease = null;
            return result;
        }
        catch { if (targetLease is not null) store.Retire(target); throw; }
        finally { targetLease?.Dispose(); }
    }

    private Generation Build(string provider, string name, GenerationPlan plan)
    {
        var parent = ProviderRoot(provider);
        var tmp = paths.RequireManaged(Path.Combine(parent, "tmp-" + Nonce()));
        var target = paths.RequireManaged(Path.Combine(parent, name));
        var trees = new StoreTrees(acl);
        StoreLease? buildLease = null;
        StoreLease? targetLease = null;
        try
        {
            var metadata = plan.Files["generation.json"];
            targetLease = Reserve(target, metadata);
            buildLease = Reserve(tmp, metadata);
            trees.Unlock(tmp);
            foreach (var (path, bytes) in plan.Files)
            {
                var file = Path.Combine(tmp, path.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                File.WriteAllBytes(file, bytes);
            }
            // Both pathnames have SK-5 leases before the atomic rename. GC can run even through SkillsStore directly.
            RemoveBootstrap(target);
            Directory.Move(tmp, target);
            trees.Lock(target);
            if (!new StoreVerifier(acl).Verify(target, plan.Manifest).IsValid) throw new IOException("The published generation failed verification.");
            var result = Create(targetLease, plan);
            targetLease = null;
            return result;
        }
        catch
        {
            store.Retire(tmp);
            if (targetLease is not null) store.Retire(target);
            throw;
        }
        finally { buildLease?.Dispose(); targetLease?.Dispose(); }
    }

    private void QueueReplacement(string provider, GenerationPlan plan)
    {
        var key = provider + "/" + plan.Key;
        if (spares.ContainsKey(key) || !pendingBuilds.Add(key)) return;
        workers.RemoveAll(t => t.IsCompleted);
        workers.Add(Task.Run(() =>
        {
            Generation? built = null;
            try
            {
                lock (gate) { if (disposed) return; }
                // Copying/hashing runs outside the coordinator gate: ready spares remain claimable during another build.
                var thread = Thread.CurrentThread;
                var oldPriority = OperatingSystem.IsWindows() ? thread.Priority : ThreadPriority.Normal;
                try
                {
                    // ED-NEW-lean-18b: EcoQoS too while the spare builds; the pool thread gets both back before it returns.
                    if (OperatingSystem.IsWindows()) { thread.Priority = ThreadPriority.BelowNormal; ModelsPalette.PaletteExecutor.EcoQoS(true); }
                    built = Build(provider, "spare-" + plan.Key + "-" + Nonce(), plan);
                }
                finally { if (OperatingSystem.IsWindows()) { ModelsPalette.PaletteExecutor.EcoQoS(false); thread.Priority = oldPriority; } }
                lock (gate)
                {
                    if (disposed || spares.ContainsKey(key) || !(globalKeys.GetValueOrDefault(provider) == plan.Key || recent.GetValueOrDefault(provider)?.Any(c => c.Key == plan.Key) == true)) built.Dispose();
                    else spares[key] = built;
                    built = null;
                    LastSpareFailure = null;
                    Evict(provider, "");
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
            { lock (gate) LastSpareFailure = e; }
            finally { built?.Dispose(); lock (gate) pendingBuilds.Remove(key); }
        }));
    }

    private void Evict(string provider, string current)
    {
        var keep = recent.TryGetValue(provider, out var chats) ? chats.Select(c => c.Key).ToHashSet(StringComparer.Ordinal) : new HashSet<string>(StringComparer.Ordinal);
        keep.Add(current);
        if (globalKeys.TryGetValue(provider, out var global)) keep.Add(global);
        foreach (var key in spares.Keys.Where(k => k.StartsWith(provider + "/", StringComparison.Ordinal) && !keep.Contains(k[(provider.Length + 1)..])).ToArray())
        { spares[key].Dispose(); spares.Remove(key); }
    }

    public GenerationListing Listing(string provider, IEnumerable<GenerationItem> items, SkillSwitches switches, GenerationBudget budget,
        IEnumerable<ExistingSkill>? existing = null)
    {
        var imported = importedSource?.Snapshot() ?? [];
        var ownPlugins = ownPluginSource?.SnapshotPlugins() ?? [];
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            var plan = Compose(provider, items, switches, budget, existing ?? clashSource?.Invoke(provider) ?? throw new InvalidOperationException("A reliable clash snapshot is required."), imported, ownPlugins);
            var key = provider + "/" + plan.Key;
            if (!listings.TryGetValue(key, out var generation))
            {
                generation = Build(provider, "list-" + plan.Key + "-" + Nonce(), plan);
                listings[key] = generation;
                listingUsers[key] = 0;
            }
            if (!generation.Verify()) throw new IOException("A listing generation failed verification.");
            listingUsers[key]++;
            return new(generation, () =>
            {
                lock (gate)
                {
                    if (!listingUsers.TryGetValue(key, out var count)) return;
                    if (count > 1) listingUsers[key] = count - 1;
                    else { generation.Dispose(); listings.Remove(key); listingUsers.Remove(key); }
                }
            });
        }
    }

    public void Dispose()
    {
        Task[] outstanding;
        lock (gate)
        {
            disposed = true;
            foreach (var spare in spares.Values) spare.Dispose();
            foreach (var listing in listings.Values) listing.Dispose();
            spares.Clear(); listings.Clear(); listingUsers.Clear();
            outstanding = workers.ToArray();
        }
        Task.WaitAll(outstanding); // Fixture/app teardown cannot race a pending build. Never wait while holding gate.
    }

    private Generation Create(StoreLease lease, GenerationPlan plan) => new(lease, plan.Manifest, acl, store, plan.Key,
        plan.Skills.Select(s => new PublishedSkill(s.Id, s.Name, s.Description, Path.Combine(lease.Folder, plan.Locations[s.Id].Replace('/', Path.DirectorySeparatorChar)), s.ExplicitOnly)),
        plan.LeftOut, plan.PluginRoots.Select(p => Path.Combine(lease.Folder, p.Replace('/', Path.DirectorySeparatorChar))));
    private static CatalogueFile Pin(string path, byte[] bytes) => new() { Path = path, Size = bytes.LongLength, Sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes)) };
    private static string Nonce() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8));
}
