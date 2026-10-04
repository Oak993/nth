using Marrow.Core.Port.Skills.Catalogue;
using Marrow.Core.Port.Skills.State;
using Marrow.Core.Port.Updates;
using Marrow.Services.Port.Skills.Install;
using Marrow.Services.Port.Skills.Store;
namespace Marrow.Services.Port.Skills.Pipelines;

// PORT-ONLY: SK-8, ONDEMAND 3.7/3.8 + DL-8, PLAN 2.1. One owner per root, including leases and GC.
// Uses SK-5's verifier/unlock/deletion primitives; do not run a separate SkillsStore owner on this root.
public sealed class SkillsPipeline
{
    private readonly object gate = new();
    private readonly SemaphoreSlim operation = new(1, 1);
    private readonly StorePaths paths;
    private readonly SkillsCatalogue catalogue;
    private readonly ItemInstaller installer;
    private readonly StoreTrees trees;
    private readonly StoreVerifier verifier;
    private readonly PipelineVerifier inventoryVerifier;
    private readonly Dictionary<(string Id, string Pin), IReadOnlyList<string>> directories = [];
    private readonly IStoreDeletion deletion;
    private readonly Func<CatalogueItem, ItemRecord, IProgress<double?>, CancellationToken, Task<InstallPlan>>? downloads;
    private readonly Func<CatalogueItem, InstallResult, PipelineRoute>? bindings;
    private readonly Action<PipelineStep>? checkpoint;
    private readonly Dictionary<(string Id, string Pin), StoreItem> versions = [];
    private string ResetMarker => Path.Combine(paths.Skills, "reset-pending");
    private readonly Dictionary<string, int> leases = new(StorePaths.Comparer);
    private AddonsState state;

    public SkillsPipeline(StorePaths paths, SkillsCatalogue catalogue, IEnumerable<StoreItem> trustedVersions, AddonsState initial,
        ItemInstaller installer, IStoreProtection protection, IStoreDeletion? deletion = null,
        Func<CatalogueItem, ItemRecord, IProgress<double?>, CancellationToken, Task<InstallPlan>>? downloads = null,
        Func<CatalogueItem, InstallResult, PipelineRoute>? bindings = null, Action<PipelineStep>? checkpoint = null, IReadOnlyDictionary<(string Id, string Pin), IReadOnlyList<string>>? trustedDirectories = null)
    {
        this.paths = paths; this.catalogue = catalogue; this.installer = installer;
        trees = new(protection); verifier = new(protection); inventoryVerifier = new(protection); this.deletion = deletion ?? StoreDeletion.Instance;
        this.downloads = downloads; this.bindings = bindings; this.checkpoint = checkpoint;
        foreach (var item in trustedVersions)
        {
            var folder = paths.RequireManaged(item.Folder);
            if (!StorePaths.Under(folder, paths.Tools) && !StorePaths.Under(folder, paths.Store)) throw new ArgumentException("Installed descriptors must be under tools or store.", nameof(trustedVersions));
            if (versions.Values.Any(v => StorePaths.Comparer.Equals(v.Folder, folder) || StorePaths.Under(v.Folder, folder) || StorePaths.Under(folder, v.Folder)))
                throw new ArgumentException("Installed descriptors cannot overlap.", nameof(trustedVersions));
            var key = (item.Catalogue.Id, item.Pin);
            versions.Add(key, item);
            directories[key] = trustedDirectories?.GetValueOrDefault(key)?.ToArray() ?? item.Manifest.Directories.ToArray();
        }
        StorePaths.EnsureAncestors(paths.StateFile);
        Directory.CreateDirectory(paths.Addons);
        state = File.Exists(paths.StateFile) ? AddonsStateStore.Load(paths.StateFile) : initial.AfterRestart();
        foreach (var record in state.Items.Where(r => r.InstalledPin != null && r.State != ItemState.Removing))
            if (!versions.ContainsKey((record.Id, record.InstalledPin!))) throw new InvalidDataException("An installed pin has no trusted descriptor.");
        state = UpdateCheck.Compute(state, catalogue).State;
        if (state.Items.Any(r => r.InstalledPin != null && r.State is not (ItemState.Removing or ItemState.Damaged))) Snapshot();
        AddonsStateStore.Save(paths.StateFile, state);
    }

    public AddonsState State { get { lock (gate) return state; } }
    public string? LatestPin(string id) => catalogue.Items.FirstOrDefault(i => i.Id == id) is { } item ? UpdateCheck.CataloguePin(item) : null;
    public IReadOnlyList<PipelineRoute> Snapshot()
    {
        lock (gate)
        {
            var routes = PipelineRoutes.Read(paths.StateFile, state);
            foreach (var route in routes)
                if (!versions.TryGetValue((route.Id, route.Pin), out var trusted) || !StorePaths.Comparer.Equals(route.Folder, trusted.Folder))
                    throw new InvalidDataException("Routing disagrees with the trusted installed descriptor.");
            return routes;
        }
    }
    public IReadOnlyList<IUpdateItem> UpdateItems() => State.Items.Where(r => r.InstalledPin != null && r.State != ItemState.Removing)
        .Select(r => (IUpdateItem)new SkillUpdateItem(this, r.Id, catalogue.Items.FirstOrDefault(i => i.Id == r.Id)?.Title ?? versions[(r.Id, r.InstalledPin!)].Catalogue.Title)).ToArray();

    public void AcceptTerms(string id, DateTimeOffset at)
    {
        operation.Wait();
        try { lock (gate) Commit(state.AcceptTerms(ItemTerms.LicenceHash(catalogue.Items.Single(i => i.Id == id)) ?? throw new InvalidOperationException("Unpinned licence."), id, at)); }
        finally { operation.Release(); }
    }

    private void Commit(AddonsState next)
    {
        StorePaths.EnsureAncestors(paths.StateFile);
        AddonsStateStore.Save(paths.StateFile, next); // The only commit point; readers select every route from this one file.
        state = next;
    }

    public StoreLease? Acquire(string id) => AcquireItem(id, sealOnly: true);
    public StoreLease? AcquireForViewBuild(string id) => AcquireItem(id, sealOnly: false);
    private StoreLease? AcquireItem(string id, bool sealOnly)
    {
        lock (gate)
        {
            var record = state.Get(id);
            if (record.InstalledPin is null || record.State is ItemState.Damaged or ItemState.Removing) return null;
            var item = versions[(id, record.InstalledPin)];
            if (!inventoryVerifier.Verify(item, directories[(id, item.Pin)], sealOnly))
            {
                var damaged = new ItemRecord(id, ItemState.Damaged, record.InstalledPin, null, null);
                Commit(AddonsState.Restore(state.Items.Where(r => r.Id != id).Append(damaged), state.AcceptedTerms, state.Declined, state.Found));
                return null;
            }
            leases[item.Folder] = leases.GetValueOrDefault(item.Folder) + 1;
            return new StoreLease(item.Folder, () => { lock (gate) { if (--leases[item.Folder] == 0) leases.Remove(item.Folder); } });
        }
    }

    public StoreLease? AcquireGeneration(string folder, StoreManifest manifest, IReadOnlyList<(string Id, string Pin)> sources)
    {
        lock (gate)
        {
            folder = paths.RequireManaged(folder);
            if (!StorePaths.Under(folder, paths.Views) || Leased(folder) || !verifier.Verify(folder, manifest).IsValid) return null;
            var held = new List<StoreLease>();
            foreach (var (id, pin) in sources.Distinct())
            {
                var source = state.Get(id).InstalledPin == pin ? AcquireForViewBuild(id) : null;
                if (source is null) { foreach (var lease in held) lease.Dispose(); return null; }
                held.Add(source);
            }
            leases[folder] = 1;
            return new StoreLease(folder, () => { lock (gate) { leases.Remove(folder); foreach (var lease in held) lease.Dispose(); } });
        }
    }

    public async Task UpdateAsync(string id, IProgress<double?> progress, CancellationToken cancellation = default)
    {
        await operation.WaitAsync(cancellation).ConfigureAwait(false);
        InstallResult? prepared = null;
        var committed = false;
        try
        {
            CatalogueItem item; ItemRecord checking;
            lock (gate)
            {
                item = catalogue.Items.Single(i => i.Id == id);
                if (state.Get(id).InstalledPin is null) throw new InvalidOperationException("Update requires an installed item.");
                if (state.Get(id).State is ItemState.Damaged or ItemState.Removing ||
                    state.Get(id).State == ItemState.Failed && state.Get(id).TargetPin != LatestPin(id))
                    throw new InvalidOperationException("This item requires repair or removal before updating.");
                if (LatestPin(id) == state.Get(id).InstalledPin) return;
                if (item.PinState != PinState.Pinned || CatalogueGate.Missing(item).Count != 0) throw new InvalidOperationException("Update requires a complete pin.");
                if (ItemTerms.Required(item, state)) throw new PipelineTermsRequiredException(id);
                if (downloads is null || bindings is null) throw new InvalidOperationException("Pipeline seams are not bound.");
                CollectCore(); // Recover unreferenced completed installs and tmp trees before retry.
                var local = UpdateCheck.Compute(state, catalogue).State;
                checking = local.Transition(id, ItemState.Downloading).Transition(id, ItemState.Checking).Get(id);
            }
            var plan = await downloads(item, checking, progress, cancellation).ConfigureAwait(false);
            if (plan.Item != item || plan.Record != checking || Path.GetFullPath(plan.DataRoot) != paths.Root)
                throw new InvalidDataException("Download plan disagrees with the update.");
            checkpoint?.Invoke(PipelineStep.Downloaded);
            // Multi-pin sets have no single upstream pin. SK-4's path component is a hash of SK-7's composed pin key.
            var selectedPin = UpdateCheck.CataloguePin(item) ?? throw new InvalidDataException("Missing target pin.");
            var installPin = item.Pin ?? Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(selectedPin)));
            var result = await installer.InstallAsync(plan with { Item = item with { Pin = installPin }, Record = checking with { TargetPin = installPin } }, cancellation).ConfigureAwait(false);
            result = result with { Record = result.Record with { InstalledPin = selectedPin } };
            prepared = result;
            checkpoint?.Invoke(PipelineStep.Installed);
            var trusted = new StoreItem(item, result.Path, new StoreManifest(result.Manifest), selectedPin);
            if (!inventoryVerifier.Verify(trusted, result.Directories)) throw new IOException("The file didn't match its checksum.");
            var route = bindings(item, result);
            if (route.Id != id || route.Pin != selectedPin || route.Folder != result.Path || !route.SparePaths.SequenceEqual(result.SparePaths))
                throw new InvalidDataException("Bindings disagree with the verified install.");
            PipelineRoutes.Prepare(paths.StateFile, route);
            checkpoint?.Invoke(PipelineStep.RoutesPrepared);
            cancellation.ThrowIfCancellationRequested();
            lock (gate)
            {
                // Validate the combined target table before publication (including duplicate shims).
                var next = AddonsState.Restore(state.Items.Where(r => r.Id != id).Append(result.Record), state.AcceptedTerms, state.Declined, state.Found);
                PipelineRoutes.Read(paths.StateFile, next);
                checkpoint?.Invoke(PipelineStep.BeforeCommit);
                versions[(id, trusted.Pin)] = trusted;
                directories[(id, trusted.Pin)] = result.Directories.ToArray();
                try { Commit(next); committed = true; } catch { versions.Remove((id, trusted.Pin)); directories.Remove((id, trusted.Pin)); throw; }
            }
            // User progress observers cannot undo a committed update.
            try { progress.Report(1); } catch (Exception) { }
        }
        catch
        {
            if (!committed && prepared is not null)
                foreach (var folder in prepared.SparePaths.Prepend(prepared.Path)) TryDelete(folder);
            throw;
        }
        finally { operation.Release(); }
    }

    public IReadOnlyList<string> MissingNeeds(string id) => catalogue.Items.Single(i => i.Id == id).Needs
        .Where(n => State.Get(n).InstalledPin is null || State.Get(n).State is ItemState.Removing or ItemState.Damaged).ToArray();

    public async Task RemoveAsync(string id, CancellationToken cancellation = default)
    {
        await operation.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            lock (gate)
            {
                var removed = ItemDependents.Installed(catalogue, state, id).Where(d => catalogue.Items.Single(i => i.Id == d).Kind is ItemKind.Pyset or ItemKind.Nodeset).Append(id).ToHashSet(StringComparer.Ordinal);
                Commit(AddonsState.Restore(state.Items.Where(r => !removed.Contains(r.Id)), state.AcceptedTerms, state.Declined, state.Found));
                CollectCore();
            }
        }
        finally { operation.Release(); }
    }

    public async Task ResetAsync(CancellationToken cancellation = default)
    {
        await operation.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            lock (gate)
            {
                StorePaths.EnsureAncestors(ResetMarker);
                Directory.CreateDirectory(paths.Skills);
                Marrow.Core.Port.FileOps.WriteAtomic(ResetMarker, []);
                Commit(AddonsState.Restore([], state.AcceptedTerms, state.Declined, state.Found));
                foreach (var root in new[] { paths.Tools, paths.Store, paths.Views, Path.Combine(paths.Addons, "bin"), Path.Combine(paths.Addons, "downloads"), Path.Combine(paths.Addons, "work") }) trees.Unlock(root);
                CollectCore();
                foreach (var root in new[] { Path.Combine(paths.Addons, "bin"), Path.Combine(paths.Addons, "downloads"), Path.Combine(paths.Addons, "work") }) TryDelete(root);
            }
        }
        finally { operation.Release(); }
    }

    public void Collect()
    {
        if (!operation.Wait(0)) return; // Never collect a build in flight.
        try { lock (gate) CollectCore(); } finally { operation.Release(); }
    }
    private bool Leased(string folder) => leases.Keys.Any(p => p == folder || StorePaths.Under(p, folder) || StorePaths.Under(folder, p));
    private void CollectCore()
    {
        StorePaths.EnsureAncestors(ResetMarker);
        var retained = Snapshot().SelectMany(r => r.SparePaths.Prepend(r.Folder)).ToHashSet(StorePaths.Comparer);
        foreach (var root in new[] { paths.Tools, paths.Store, paths.Views })
        {
            StorePaths.EnsureAncestors(root);
            if (!Directory.Exists(root)) continue;
            var children = Directory.EnumerateFileSystemEntries(root).ToArray();
            foreach (var child in children)
            {
                var candidates = root == paths.Views && (File.GetAttributes(child) & (FileAttributes.Directory | FileAttributes.ReparsePoint)) == FileAttributes.Directory
                    ? Directory.EnumerateFileSystemEntries(child).ToArray() : [child];
                foreach (var candidate in candidates)
                {
                    if (retained.Any(p => StorePaths.Comparer.Equals(p, candidate) || StorePaths.Under(p, candidate)) || Leased(candidate)) continue;
                    TryDelete(candidate);
                }
                if (root == paths.Views && Directory.Exists(child) && !Directory.EnumerateFileSystemEntries(child).Any()) TryDelete(child);
            }
            if (!Directory.EnumerateFileSystemEntries(root).Any()) TryDelete(root);
        }
        if (File.Exists(ResetMarker) && !state.Items.Any(r => r.InstalledPin != null))
        {
            StorePaths.EnsureAncestors(paths.Addons);
            var targets = Directory.EnumerateFileSystemEntries(paths.Addons).Where(p => !StorePaths.Comparer.Equals(p, paths.StateFile)).ToArray();
            // Reset's unlock-everything-before-delete rule includes bin, work, downloads and route sidecars.
            foreach (var target in targets) trees.Unlock(target);
            foreach (var target in targets) TryDelete(target);
            if (targets.All(p => !Path.Exists(p)) && !Directory.Exists(paths.Tools) && !Directory.Exists(paths.Store) && !Directory.Exists(paths.Views)) File.Delete(ResetMarker);
        }
    }
    private void TryDelete(string folder)
    {
        StorePaths.EnsureAncestors(Path.GetDirectoryName(folder)!);
        if (Leased(folder)) return;
        try { trees.Unlock(folder); deletion.Delete(folder); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { /* Retry at next GC, per SK-5. */ }
    }
}
