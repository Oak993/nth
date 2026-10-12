using Marrow.Core.Port.Skills.Catalogue;
using Marrow.Core.Port.Skills.State;
using Marrow.Core.Port.Updates;
using Marrow.Services.Port.Skills.Install;
using Marrow.Services.Port.Skills.Store;
namespace Marrow.Services.Port.Skills.Pipelines;

// PORT-ONLY: SK-8, ONDEMAND 3.7/3.8 + DL-8, PLAN 2.1. One owner per root, including leases and GC.
// Uses SK-5's verifier/unlock/deletion primitives; do not run a separate SkillsStore owner on this root.
public sealed class SkillsPipeline : IGenerationStore
{
    private readonly object gate = new();
    private readonly SemaphoreSlim operation = new(1, 1);
    private int resetSerial; // ED-372: a completed download cannot undo a reset.
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
    private readonly HashSet<string> retired = new(StorePaths.Comparer); // integ-sk8-sk9: generations never handed out again (SK-9 binding).
    private AddonsState state;
    private string OrphanFile => Path.Combine(paths.Addons, "orphans.json");
    private List<PipelineOrphan> orphans = [];
    private bool orphansUnreadable; // a corrupt orphans.json retains every tool and store tree (fail closed)

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
        LoadOrphans();
        state = UpdateCheck.Compute(state, catalogue).State;
        if (state.Items.Any(r => r.InstalledPin != null && r.State is not (ItemState.Removing or ItemState.Damaged))) Snapshot();
        AddonsStateStore.Save(paths.StateFile, state);
    }

    public AddonsState State { get { lock (gate) return state; } }
    public string CatalogueVersion => catalogue.ReadOn;

    // PORT-ONLY: SK-17. Frozen descriptors of the currently selected installed pins, never disk manifests or orphan trees.
    // The launch composer still acquires/fully verifies each pin before reading its disclosure metadata.
    public IReadOnlyList<StoreItem> InstalledSkillItems()
    {
        lock (gate)
            return state.Items.Where(r => r.InstalledPin is not null && r.State is not (ItemState.Removing or ItemState.Damaged))
                .Select(r => versions[(r.Id, r.InstalledPin!)])
                .Where(i => i.Catalogue.Kind is ItemKind.Skillset or ItemKind.Plugin).ToArray();
    }

    /// <summary>SK-16: raised after a first install, update, repair, remove or reset has finished (never inside the pipeline's locks), so a surface that lists the installed items can read them again.</summary>
    public event Action? Changed;

    /// <summary>SK-16: true when both the download seam and the binding seam are bound, so a first install can be tried at all (an item may still be refused: unpinned, terms owed).</summary>
    public bool FetchingBound => downloads is not null && bindings is not null;

    /// <summary>SK-16: the folder an installed item is routed to now, or null when it is not installed (a damaged copy and a removal in progress are not installed here).</summary>
    public string? InstalledFolder(string id)
    {
        lock (gate)
        {
            var record = state.Get(id);
            return record.InstalledPin is { } pin && record.State is ItemState.Installed or ItemState.UpdateAvailable && versions.TryGetValue((id, pin), out var item) ? item.Folder : null;
        }
    }

    private void Raise()
    {
        try { Changed?.Invoke(); }
        catch (Exception) { /* an observer cannot undo what was committed */ }
    }

    /// <summary>
    /// SK-16 (DL-19, ED-326 (6)(a)): the first install of a catalogue item, SK-3's download seam then SK-4's installer then the binding seam, committed to state.json as Installed. Returns the folder it is
    /// installed in. Refuses, before any byte is requested and with nothing written, an item that is not in the catalogue, is installed already (update or repair it), has no complete pin, owes terms
    /// not accepted, needs an item that is not installed, or whose seams are not bound. A cancel or a failure leaves the item not installed (the download keeps what it fetched for a later try).
    /// Nothing about a download in flight is written to state.json: a restart finds the item not installed and fetches only when it is asked to.
    /// </summary>
    public async Task<string> InstallAsync(string id, IProgress<double?> progress, CancellationToken cancellation = default)
    {
        await operation.WaitAsync(cancellation).ConfigureAwait(false);
        InstallResult? prepared = null;
        var committed = false;
        string folder;
        try
        {
            CatalogueItem item; ItemRecord checking; string selectedPin;
            lock (gate)
            {
                item = catalogue.Items.FirstOrDefault(i => i.Id == id) ?? throw new InvalidOperationException("The item is not in the catalogue.");
                if (state.Get(id).InstalledPin is not null) throw new InvalidOperationException("Install requires an item that is not installed; update or repair it instead.");
                if (item.PinState != PinState.Pinned || CatalogueGate.Missing(item).Count != 0) throw new InvalidOperationException("Install requires a complete pin.");
                if (ItemTerms.Required(item, state)) throw new PipelineTermsRequiredException(id);
                if (downloads is null || bindings is null) throw new InvalidOperationException("Pipeline seams are not bound.");
                if (MissingNeeds(id) is { Count: > 0 } missing) throw new InvalidOperationException("Install requires " + string.Join(", ", missing) + " first.");
                selectedPin = UpdateCheck.CataloguePin(item) ?? throw new InvalidDataException("Missing target pin.");
                CollectCore(); // Recover unreferenced completed installs and tmp trees before the try.
                checking = state.Transition(id, ItemState.Downloading, selectedPin).Transition(id, ItemState.Checking).Get(id);
            }
            var plan = await DownloadOutsideOperation(item, checking, progress, cancellation).ConfigureAwait(false);
            if (plan.Item != item || plan.Record != checking || Path.GetFullPath(plan.DataRoot) != paths.Root)
                throw new InvalidDataException("Download plan disagrees with the install.");
            checkpoint?.Invoke(PipelineStep.Downloaded);
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
            PipelineManifests.Prepare(paths.StateFile, selectedPin, item, result.Manifest, result.Directories);
            checkpoint?.Invoke(PipelineStep.RoutesPrepared);
            cancellation.ThrowIfCancellationRequested();
            lock (gate)
            {
                var next = AddonsState.Restore(state.Items.Where(r => r.Id != id).Append(result.Record), state.AcceptedTerms, state.Declined, state.Found);
                PipelineRoutes.Read(paths.StateFile, next);
                checkpoint?.Invoke(PipelineStep.BeforeCommit);
                versions[(id, trusted.Pin)] = trusted;
                directories[(id, trusted.Pin)] = result.Directories.ToArray();
                try { Commit(next); committed = true; } catch { versions.Remove((id, trusted.Pin)); directories.Remove((id, trusted.Pin)); throw; }
            }
            folder = result.Path;
            result.Reservation?.Dispose();
            try { progress.Report(1); } catch (Exception) { }
        }
        catch
        {
            if (!committed && prepared is not null)
                foreach (var path in prepared.SparePaths.Prepend(prepared.Path)) TryDelete(path);
            throw;
        }
        finally { operation.Release(); }
        Raise();
        return folder;
    }

    /// <summary>SK-13 review fix: the ids of installed items whose trust could not be rebuilt at open; their trees are retained until the item is removed or Reset runs.</summary>
    public IReadOnlyList<string> Orphans { get { lock (gate) return orphans.Select(o => o.Id).ToArray(); } }

    /// <summary>
    /// Marks installed trees as orphans BEFORE their records leave state.json: persisted in addons/orphans.json (merged with the earlier ones), retained by every GC of every launch until
    /// <see cref="RemoveAsync"/> of that id or <see cref="ResetAsync"/>. Folders are the item's folder and spares; empty means unknown, which retains every tool and store tree.
    /// Call before a pipeline is built over the root: the pipeline reads the file once, at construction.
    /// </summary>
    public static void MarkOrphans(StorePaths paths, IEnumerable<PipelineOrphan> marked)
    {
        var (existing, unreadable) = ReadOrphans(paths);
        if (unreadable) existing.Add(new PipelineOrphan("?unreadable", [])); // a corrupt file keeps retaining every tool and store tree once rewritten
        var merged = existing.Where(o => !marked.Any(m => m.Id == o.Id)).Concat(marked.Select(m => new PipelineOrphan(m.Id, m.Folders.Select(paths.RequireManaged).Distinct(StorePaths.Comparer).ToArray())))
            .OrderBy(o => o.Id, StringComparer.Ordinal).ToArray();
        StorePaths.EnsureAncestors(paths.Addons);
        Directory.CreateDirectory(paths.Addons);
        Marrow.Core.Port.FileOps.WriteAtomic(Path.Combine(paths.Addons, "orphans.json"), System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(merged, PipelineOrphanJsonContext.Default.PipelineOrphanArray));
    }

    private static (List<PipelineOrphan> Orphans, bool Unreadable) ReadOrphans(StorePaths paths)
    {
        var file = Path.Combine(paths.Addons, "orphans.json");
        if (!File.Exists(file)) return ([], false);
        try
        {
            var loaded = System.Text.Json.JsonSerializer.Deserialize(File.ReadAllBytes(file), PipelineOrphanJsonContext.Default.PipelineOrphanArray) ?? throw new InvalidDataException("null");
            return (loaded.Select(o => new PipelineOrphan(o.Id ?? throw new InvalidDataException("id"), (o.Folders ?? throw new InvalidDataException("folders")).Select(paths.RequireManaged).ToArray())).ToList(), false);
        }
        catch (Exception error) when (error is System.Text.Json.JsonException or InvalidDataException or ArgumentException or IOException)
        {
            return ([], true);
        }
    }

    private void LoadOrphans() => (orphans, orphansUnreadable) = ReadOrphans(paths);

    private void SaveOrphans()
    {
        StorePaths.EnsureAncestors(OrphanFile);
        Directory.CreateDirectory(paths.Addons);
        if (orphans.Count == 0 && !orphansUnreadable) { File.Delete(OrphanFile); return; }
        Marrow.Core.Port.FileOps.WriteAtomic(OrphanFile, System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(orphans.ToArray(), PipelineOrphanJsonContext.Default.PipelineOrphanArray));
        orphansUnreadable = false;
    }
    /// <summary>
    /// SK-14 (ONDEMAND 5.4 and 5.5): writes the per-chat declined offers of <paramref name="declined"/> to state.json, before the provider is answered. Only that list is taken: the items, the accepted terms and
    /// the found tools stay as the pipeline holds them, so an offer can never change an installed record.
    /// </summary>
    public void SetDeclined(AddonsState declined)
    {
        ArgumentNullException.ThrowIfNull(declined);
        // Not under the operation lock: that is held for a whole download or install, and a decline answers a provider on the main actor. Every commit builds the next state from the state it finds under
        // the gate, so this one cannot be lost to, or lose, an install's: each keeps the other's lists.
        lock (gate) Commit(AddonsState.Restore(state.Items, state.AcceptedTerms, declined.Declined, state.Found));
    }

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
        // ED-372: like SK-14 SetDeclined, merge the current lists under gate without waiting for an item's operation.
        lock (gate) Commit(state.AcceptTerms(ItemTerms.LicenceHash(catalogue.Items.Single(i => i.Id == id)) ?? throw new InvalidOperationException("Unpinned licence."), id, at));
    }

    // ED-372: only downloading runs without the operation slot; install, verification, routing and GC stay serialized.
    // The caller owns the slot on entry and again on every exit, including a failed/cancelled download.
    private async Task<InstallPlan> DownloadOutsideOperation(CatalogueItem item, ItemRecord checking, IProgress<double?> progress, CancellationToken cancellation)
    {
        ItemRecord before;
        int reset;
        ItemRecord[] needs;
        lock (gate) { before = state.Get(item.Id); reset = resetSerial; needs = item.Needs.Select(state.Get).ToArray(); }
        operation.Release();
        InstallPlan plan;
        try { plan = await downloads!(item, checking, progress, cancellation).ConfigureAwait(false); }
        finally { await operation.WaitAsync().ConfigureAwait(false); }
        cancellation.ThrowIfCancellationRequested();
        lock (gate)
        {
            if (reset != resetSerial || state.Get(item.Id) != before || !needs.SequenceEqual(item.Needs.Select(state.Get)))
                throw new InvalidOperationException("The item changed while it was downloading.");
            if (ItemTerms.Required(item, state)) throw new PipelineTermsRequiredException(item.Id);
        }
        return plan;
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
            if (!StorePaths.Under(folder, paths.Views) || Leased(folder) || IsRetired(folder) || !verifier.Verify(folder, manifest).IsValid) return null;
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

    /// <summary>integ-sk8-sk9: SK-9's generation lease. A generation is a copy, so it holds no source lease once composed.</summary>
    public StoreLease? AcquireGeneration(string folder, StoreManifest manifest) => AcquireGeneration(folder, manifest, []);

    /// <summary>integ-sk8-sk9: SK-9 retires a damaged or replaced generation; it is deleted at once when unleased, else by a later GC.</summary>
    public void Retire(string folder)
    {
        lock (gate)
        {
            folder = paths.RequireManaged(folder);
            if (!StorePaths.Under(folder, paths.Views)) throw new ArgumentException("Only a generation under skills/views can be retired.", nameof(folder));
            if (Snapshot().SelectMany(r => r.SparePaths).Any(p => StorePaths.Comparer.Equals(p, folder))) return; // A selected pin's spare stays with its route.
            retired.Add(folder);
            TryDelete(folder);
        }
    }
    private bool IsRetired(string folder) => retired.Any(p => StorePaths.Comparer.Equals(p, folder) || StorePaths.Under(folder, p));

    public async Task UpdateAsync(string id, IProgress<double?> progress, CancellationToken cancellation = default)
    {
        await UpdateCoreAsync(id, progress, cancellation).ConfigureAwait(false);
        Raise();
    }

    private async Task UpdateCoreAsync(string id, IProgress<double?> progress, CancellationToken cancellation)
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
            var plan = await DownloadOutsideOperation(item, checking, progress, cancellation).ConfigureAwait(false);
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
            PipelineManifests.Prepare(paths.StateFile, selectedPin, item, result.Manifest, result.Directories);
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

    /// <summary>
    /// integ-sk8-sk9 (ED-323 (5)(b)): Repair of a damaged item, archive tools included. The damaged folder is renamed aside to a tmp- name (so its
    /// path is free even when deleting it must wait) and collected; the orchestrator's download seam supplies the installer parameters for the installed pin (never
    /// latest); SK-4's installer reinstalls that pin at its own path; the tree is verified against the trusted descriptor and committed. False, with nothing changed,
    /// while a running chat leases the damaged folder or a handle holds it. A failure or an abrupt exit leaves the item Damaged (Installed once committed); Repair runs again.
    /// </summary>
    public async Task<bool> RepairAsync(string id, IProgress<double?> progress, CancellationToken cancellation = default)
    {
        var repaired = await RepairCoreAsync(id, progress, cancellation).ConfigureAwait(false);
        if (repaired) Raise();
        return repaired;
    }

    private async Task<bool> RepairCoreAsync(string id, IProgress<double?> progress, CancellationToken cancellation)
    {
        await operation.WaitAsync(cancellation).ConfigureAwait(false);
        var published = false;
        var committed = false;
        StoreItem? trusted = null;
        try
        {
            ItemRecord checking;
            lock (gate)
            {
                var record = state.Get(id);
                if (record.State != ItemState.Damaged || record.InstalledPin is null) throw new InvalidOperationException("Repair requires a damaged installed pin.");
                if (downloads is null) throw new InvalidOperationException("Pipeline seams are not bound.");
                trusted = versions[(id, record.InstalledPin)];
                if (Leased(trusted.Folder)) return false; // A running chat keeps its folder; Repair runs again when the lease ends.
                if (Directory.Exists(trusted.Folder))
                {
                    var aside = Path.Combine(Path.GetDirectoryName(trusted.Folder)!, "tmp-" + Guid.NewGuid().ToString("N"));
                    paths.RequireManaged(aside);
                    try { Directory.Move(trusted.Folder, aside); } // The parent grants delete-child, so a locked folder renames (PLAN 2.1).
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return false; } // A held handle: retried later.
                }
                checkpoint?.Invoke(PipelineStep.SetAside);
                CollectCore(); // The aside copy and any crash residue of an earlier attempt: unselected tmp- trees.
                checking = state.Transition(id, ItemState.Downloading).Transition(id, ItemState.Checking).Get(id);
            }
            var item = trusted.Catalogue;
            var plan = await DownloadOutsideOperation(item, checking, progress, cancellation).ConfigureAwait(false);
            if (plan.Item != item || plan.Record != checking || Path.GetFullPath(plan.DataRoot) != paths.Root)
                throw new InvalidDataException("Download plan disagrees with the repair.");
            checkpoint?.Invoke(PipelineStep.Downloaded);
            cancellation.ThrowIfCancellationRequested();
            var installPin = item.Pin ?? Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(trusted.Pin)));
            var result = await installer.InstallAsync(plan with { Item = item with { Pin = installPin }, Record = checking with { TargetPin = installPin } }, cancellation).ConfigureAwait(false);
            result.Reservation?.Dispose(); // This owner's GC never runs during an operation; after the commit the folder is the selected route.
            published = StorePaths.Comparer.Equals(result.Path, trusted.Folder);
            foreach (var spare in result.SparePaths) TryDelete(spare); // The pin's routing is immutable and keeps its own spares.
            if (!published) { TryDelete(result.Path); throw new InvalidDataException("The reinstalled pin is not at its installed path."); }
            checkpoint?.Invoke(PipelineStep.Installed);
            if (!inventoryVerifier.Verify(trusted, directories[(id, trusted.Pin)])) throw new IOException("The file didn't match its checksum.");
            cancellation.ThrowIfCancellationRequested();
            lock (gate)
            {
                checkpoint?.Invoke(PipelineStep.BeforeCommit);
                var installed = new ItemRecord(id, ItemState.Installed, trusted.Pin, null, null);
                Commit(UpdateCheck.Compute(AddonsState.Restore(state.Items.Where(r => r.Id != id).Append(installed), state.AcceptedTerms, state.Declined, state.Found), catalogue).State);
                committed = true;
            }
            try { progress.Report(1); } catch (Exception) { }
            return true;
        }
        catch
        {
            // An uncommitted reinstall is not trusted: the item stays Damaged and its path is collected like any damaged tree.
            if (!committed && published && trusted is not null) lock (gate) TryDelete(trusted.Folder);
            throw;
        }
        finally { operation.Release(); }
    }

    public IReadOnlyList<string> MissingNeeds(string id) => catalogue.Items.Single(i => i.Id == id).Needs
        .Where(n => State.Get(n).InstalledPin is null || State.Get(n).State is ItemState.Removing or ItemState.Damaged).ToArray();

    public async Task RemoveAsync(string id, CancellationToken cancellation = default)
    {
        await RemoveCoreAsync(id, cancellation).ConfigureAwait(false);
        Raise();
    }

    private async Task RemoveCoreAsync(string id, CancellationToken cancellation)
    {
        await operation.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            lock (gate)
            {
                if (orphans.RemoveAll(o => o.Id == id) > 0) SaveOrphans(); // an explicit removal releases an orphan's trees to the GC
                var removed = ItemDependents.Installed(catalogue, state, id).Where(d => catalogue.Items.Single(i => i.Id == d).Kind is ItemKind.Pyset or ItemKind.Nodeset).Append(id).ToHashSet(StringComparer.Ordinal);
                Commit(AddonsState.Restore(state.Items.Where(r => !removed.Contains(r.Id)), state.AcceptedTerms, state.Declined, state.Found));
                CollectCore();
            }
        }
        finally { operation.Release(); }
    }

    public async Task ResetAsync(CancellationToken cancellation = default)
    {
        await ResetCoreAsync(cancellation).ConfigureAwait(false);
        Raise();
    }

    private async Task ResetCoreAsync(CancellationToken cancellation)
    {
        await operation.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            lock (gate)
            {
                StorePaths.EnsureAncestors(ResetMarker);
                Directory.CreateDirectory(paths.Skills);
                Marrow.Core.Port.FileOps.WriteAtomic(ResetMarker, []);
                resetSerial++;
                orphans = []; orphansUnreadable = false; // Reset releases every orphan's trees
                if (File.Exists(OrphanFile)) File.Delete(OrphanFile);
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
        var retained = Snapshot().SelectMany(r => r.SparePaths.Prepend(r.Folder)).Concat(orphans.SelectMany(o => o.Folders)).ToHashSet(StorePaths.Comparer);
        var retainAll = orphansUnreadable || orphans.Any(o => o.Folders.Length == 0);
        foreach (var root in new[] { paths.Tools, paths.Store, paths.Views })
        {
            StorePaths.EnsureAncestors(root);
            if (!Directory.Exists(root) || retainAll && root != paths.Views) continue;
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
        try { trees.Unlock(folder); deletion.Delete(folder); if (!Path.Exists(folder)) retired.RemoveWhere(p => StorePaths.Comparer.Equals(p, folder) || StorePaths.Under(p, folder)); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { /* Retry at next GC, per SK-5. */ }
    }
}
