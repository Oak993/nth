// PORT-ONLY: SK-5 / DL-5. ONDEMAND 3.5/3.7/3.8 and PLAN 2.1; generation composition and app wiring are SK-9/SK-13.
using System.Text;
using System.Text.Json;
using Marrow.Core.Port;
using Marrow.Core.Port.Skills.State;

namespace Marrow.Services.Port.Skills.Store;

/// <summary>
/// One owner per data root. Call on the pool: hashing/deletion never run on a UI executor here.
/// Installed-pin descriptors and state come from the caller; neither an installed disk manifest nor a new catalogue pin overrides them.
/// </summary>
public sealed class SkillsStore
{
    private const string ChecksumFailure = "The file didn't match its checksum."; // ONDEMAND 3.9.
    private readonly object gate = new();
    private readonly StorePaths paths;
    private readonly StoreTrees trees;
    private readonly StoreVerifier verifier;
    private readonly IStoreDeletion deletion;
    private readonly IStoreRepairDownloader? downloader;
    private readonly IStoreRepairInstaller? installer;
    private readonly Dictionary<string, StoreItem> items;
    private readonly Dictionary<string, int> leases = new(StorePaths.Comparer);
    private readonly HashSet<string> condemned = new(StorePaths.Comparer);
    private readonly HashSet<string> reserved = new(StorePaths.Comparer);
    private readonly HashSet<string> repairing = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PendingStoreRepair> pending = new(StringComparer.Ordinal);
    private AddonsState state;
    private bool tornDown;

    public SkillsStore(StorePaths paths, IEnumerable<StoreItem> items, AddonsState state, IStoreProtection? protection = null,
        IStoreDeletion? deletion = null, IStoreRepairDownloader? downloader = null, IStoreRepairInstaller? installer = null)
    {
        this.paths = paths;
        this.state = state;
        var acl = protection ?? new WindowsStoreProtection();
        trees = new StoreTrees(acl);
        verifier = new StoreVerifier(acl);
        this.deletion = deletion ?? StoreDeletion.Instance;
        this.downloader = downloader;
        this.installer = installer;
        this.items = items.ToDictionary(i => i.Catalogue.Id, StringComparer.Ordinal);
        var folders = new HashSet<string>(StorePaths.Comparer);
        foreach (var item in this.items.Values)
        {
            var folder = paths.RequireManaged(item.Folder);
            if (!StorePaths.Under(folder, paths.Tools) && !StorePaths.Under(folder, paths.Store))
                throw new ArgumentException("An installed item must live in addons/tools or skills/store.", nameof(items));
            if (!folders.Add(folder) || folders.Any(other => other != folder && (StorePaths.Under(other, folder) || StorePaths.Under(folder, other))))
                throw new ArgumentException("Installed trees cannot overlap.", nameof(items));
            if (state.Get(item.Catalogue.Id).InstalledPin != item.Pin)
                throw new ArgumentException("The descriptor does not match the state's installed pin.", nameof(items));
        }
        LoadCondemned();
    }

    public AddonsState State { get { lock (gate) return state; } }

    private void SaveState(AddonsState next)
    {
        StorePaths.EnsureAncestors(paths.StateFile);
        Directory.CreateDirectory(paths.Addons);
        AddonsStateStore.Save(paths.StateFile, next);
        state = next;
    }

    private void LoadCondemned()
    {
        StorePaths.EnsureAncestors(paths.CondemnedFile);
        if (!File.Exists(paths.CondemnedFile)) return;
        // Disk paths only name candidates for removal. Strict confinement prevents that file becoming authority to delete user data.
        var list = JsonSerializer.Deserialize<string[]>(File.ReadAllBytes(paths.CondemnedFile))
            ?? throw new InvalidDataException("The condemnation list is not an array.");
        foreach (var path in list)
        {
            if (path is null || !condemned.Add(paths.RequireManaged(path)))
                throw new InvalidDataException("The condemnation list has an invalid or duplicate path.");
        }
    }

    private void SaveCondemned()
    {
        StorePaths.EnsureAncestors(paths.CondemnedFile);
        if (tornDown && condemned.Count == 0)
        {
            File.Delete(paths.CondemnedFile);
            return;
        }
        Directory.CreateDirectory(paths.Skills);
        FileOps.WriteAtomic(paths.CondemnedFile, JsonSerializer.SerializeToUtf8Bytes(condemned.OrderBy(p => p, StringComparer.Ordinal).ToArray()));
    }

    private void Condemn(string folder)
    {
        if (condemned.Add(paths.RequireManaged(folder))) SaveCondemned();
    }

    private void Damage(StoreItem item)
    {
        Condemn(item.Folder); // The mark survives even a crash between the two atomic files.
        var record = state.Get(item.Catalogue.Id);
        if (record.State is ItemState.Installed or ItemState.UpdateAvailable)
            SaveState(state.Transition(item.Catalogue.Id, ItemState.Damaged));
    }

    public StoreLease? Acquire(string id) => AcquireItem(id, sealOnly: true);

    /// <summary>PLAN 2.1: every view build first verifies its source store whole.</summary>
    public StoreLease? AcquireForViewBuild(string id) => AcquireItem(id, sealOnly: false);

    private bool IsCondemned(string folder) => condemned.Any(p => StorePaths.Comparer.Equals(p, folder) || StorePaths.Under(p, folder) || StorePaths.Under(folder, p));

    private StoreLease? AcquireItem(string id, bool sealOnly)
    {
        lock (gate)
        {
            if (tornDown || !items.TryGetValue(id, out var item)) return null;
            var record = state.Get(id);
            if (record.State is not (ItemState.Installed or ItemState.UpdateAvailable)) return null;
            if (IsCondemned(item.Folder) || !verifier.Verify(item.Folder, item.Manifest, sealOnly).IsValid)
            {
                Damage(item);
                return null;
            }
            return Lease(item.Folder);
        }
    }

    /// <summary>SK-9 must pass a generation's trusted composed manifest. No on-disk manifest is read.</summary>
    public StoreLease? AcquireGeneration(string folder, StoreManifest manifest)
    {
        lock (gate)
        {
            folder = paths.RequireManaged(folder);
            if (!StorePaths.Under(folder, paths.Views)) throw new ArgumentException("A generation must be under skills/views.", nameof(folder));
            if (tornDown || IsCondemned(folder)) return null;
            if (!verifier.Verify(folder, manifest).IsValid)
            {
                Condemn(folder);
                return null;
            }
            // A generation is never handed to a second live session. Probe sharing is a separate SK-9 policy.
            if (HasLease(folder)) return null;
            return Lease(folder);
        }
    }

    private StoreLease Lease(string folder)
    {
        leases[folder] = leases.GetValueOrDefault(folder) + 1;
        return new StoreLease(folder, () =>
        {
            lock (gate)
            {
                if (--leases[folder] == 0) leases.Remove(folder);
            }
        });
    }

    private bool HasLease(string folder) => leases.Keys.Any(p => StorePaths.Comparer.Equals(p, folder) || StorePaths.Under(p, folder) || StorePaths.Under(folder, p));

    public void VerifyAfterAppUpdate()
    {
        lock (gate)
            foreach (var item in items.Values)
                if (state.Get(item.Catalogue.Id).State is ItemState.Installed or ItemState.UpdateAvailable &&
                    (IsCondemned(item.Folder) || !verifier.Verify(item.Folder, item.Manifest).IsValid)) Damage(item);
    }

    /// <summary>True when published; false when a verified repair waits for an old lease/handle. The next GC attempts promotion.</summary>
    public async Task<bool> RepairAsync(string id, CancellationToken cancellationToken = default)
    {
        StoreItem item;
        string downloads;
        string prepared;
        IStoreRepairInstaller materializer;
        lock (gate)
        {
            if (tornDown) throw new InvalidOperationException("The store has been torn down.");
            item = items[id];
            if (repairing.Contains(id) || pending.ContainsKey(id)) throw new InvalidOperationException("A repair is already in progress.");
            var record = state.Get(id);
            if (record.State != ItemState.Damaged && !(record.State == ItemState.Failed && record.TargetPin == item.Pin))
                throw new InvalidOperationException("Repair requires a damaged installed pin.");
            if (downloader is null) throw new InvalidOperationException("The SK-3 repair downloader is not configured.");
            materializer = installer ?? (item.Catalogue.Kind is Marrow.Core.Port.Skills.Catalogue.ItemKind.Skillset or Marrow.Core.Port.Skills.Catalogue.ItemKind.Plugin
                ? new PinnedSkillRepairInstaller() : throw new InvalidOperationException("The SK-4 archive repair installer is not configured."));
            var parent = Path.GetDirectoryName(item.Folder)!;
            downloads = Path.Combine(parent, "tmp-" + Guid.NewGuid().ToString("N"));
            prepared = Path.Combine(parent, "tmp-" + Guid.NewGuid().ToString("N"));
            paths.RequireManaged(downloads);
            paths.RequireManaged(prepared);
            Directory.CreateDirectory(downloads);
            Directory.CreateDirectory(prepared);
            reserved.Add(downloads);
            reserved.Add(prepared);
            repairing.Add(id);
            SaveState(state.Transition(id, ItemState.Downloading));
        }
        try
        {
            await downloader.DownloadAsync(item.Catalogue, downloads, cancellationToken).ConfigureAwait(false);
            lock (gate)
            {
                if (tornDown) throw new InvalidOperationException("The store was torn down during repair.");
                SaveState(state.Transition(id, ItemState.Checking));
            }
            await materializer.InstallAsync(item.Catalogue, downloads, prepared, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            // Refuse extra download files/links too for vendor plain-file sets; artifacts are validated by SK-3 for tools.
            if (item.Catalogue.Kind is Marrow.Core.Port.Skills.Catalogue.ItemKind.Skillset or Marrow.Core.Port.Skills.Catalogue.ItemKind.Plugin)
            {
                trees.Lock(downloads);
                if (!verifier.Verify(downloads, item.Manifest).IsValid) throw new IOException(ChecksumFailure);
            }
            trees.Lock(prepared);
            if (!verifier.Verify(prepared, item.Manifest).IsValid) throw new IOException(ChecksumFailure);
            lock (gate)
            {
                if (tornDown) throw new InvalidOperationException("The store was torn down during repair.");
                pending.Add(id, new PendingStoreRepair(item, prepared));
                return Publish(id);
            }
        }
        catch (Exception error)
        {
            lock (gate)
            {
                if (!tornDown)
                {
                    var record = state.Get(id);
                    if (record.State is ItemState.Downloading or ItemState.Checking)
                    {
                        // UI error mapping is the downloader/installer's responsibility; no new product text is invented here.
                        SaveState(state.Transition(id, ItemState.Failed, failure: error.Message));
                        if (error is OperationCanceledException) SaveState(state.Transition(id, ItemState.Damaged));
                    }
                }
            }
            throw;
        }
        finally
        {
            lock (gate)
            {
                repairing.Remove(id);
                reserved.Remove(downloads);
                TryDelete(downloads);
                if (!pending.ContainsKey(id))
                {
                    reserved.Remove(prepared);
                    TryDelete(prepared);
                }
                if (tornDown) Collect();
            }
        }
    }

    private bool Publish(string id)
    {
        var repair = pending[id];
        if (HasLease(repair.Item.Folder)) return false;
        if (!verifier.Verify(repair.PreparedFolder, repair.Item.Manifest).IsValid)
        {
            pending.Remove(id);
            reserved.Remove(repair.PreparedFolder);
            SaveState(state.Transition(id, ItemState.Failed, failure: ChecksumFailure));
            TryDelete(repair.PreparedFolder);
            return false;
        }
        if (!TryDelete(repair.Item.Folder)) return false;
        var moved = false;
        try
        {
            paths.RequireManaged(repair.Item.Folder);
            Directory.Move(repair.PreparedFolder, repair.Item.Folder);
            moved = true;
            trees.Lock(repair.Item.Folder);
            if (!verifier.Verify(repair.Item.Folder, repair.Item.Manifest).IsValid) throw new IOException(ChecksumFailure);
            // Keep condemnation until the final check and installed state both succeed.
            SaveState(state.Transition(id, ItemState.Installed));
            condemned.Remove(repair.Item.Folder);
            SaveCondemned();
            pending.Remove(id);
            reserved.Remove(repair.PreparedFolder);
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Condemn(repair.Item.Folder);
            if (moved)
            {
                // The prepared path no longer exists. Do not retain a pending repair pointing at that vanished path.
                pending.Remove(id);
                reserved.Remove(repair.PreparedFolder);
                if (state.Get(id).State == ItemState.Checking) SaveState(state.Transition(id, ItemState.Failed, failure: error.Message));
                else Damage(repair.Item);
            }
            return false; // A held target is retried at the next GC, with no sleep.
        }
    }

    public void Retire(string folder)
    {
        lock (gate) Condemn(folder);
    }

    private bool TryDelete(string folder)
    {
        paths.RequireManaged(folder);
        if (HasLease(folder)) return false;
        try
        {
            trees.Unlock(folder);
            deletion.Delete(folder);
            if (Path.Exists(folder)) throw new IOException("Deletion left a managed folder.");
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Condemn(folder);
            return false;
        }
    }

    private IEnumerable<string> Candidates()
    {
        foreach (var root in new[] { paths.Tools, paths.Store })
        {
            StorePaths.EnsureAncestors(root);
            if (Directory.Exists(root))
                foreach (var child in Directory.EnumerateFileSystemEntries(root)) yield return child;
        }
        StorePaths.EnsureAncestors(paths.Views);
        if (!Directory.Exists(paths.Views)) yield break;
        foreach (var provider in Directory.EnumerateFileSystemEntries(paths.Views))
        {
            var attributes = File.GetAttributes(provider);
            if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != FileAttributes.Directory) yield return provider;
            else foreach (var child in Directory.EnumerateFileSystemEntries(provider)) yield return child;
        }
    }

    /// <summary>Invoke at startup, after a build and at session/probe end. Does not wait on handles.</summary>
    public void Collect()
    {
        lock (gate)
        {
            foreach (var id in pending.Keys.ToArray()) Publish(id);
            var retained = items.Values.Where(i => !condemned.Contains(i.Folder)).Select(i => i.Folder).ToHashSet(StorePaths.Comparer);
            var candidates = Candidates().Concat(condemned).Distinct(StorePaths.Comparer).ToArray();
            foreach (var folder in candidates)
            {
                var path = paths.RequireManaged(folder);
                if (retained.Any(p => StorePaths.Comparer.Equals(p, path) || StorePaths.Under(p, path) || StorePaths.Under(path, p)) ||
                    reserved.Any(p => StorePaths.Comparer.Equals(p, path) || StorePaths.Under(p, path) || StorePaths.Under(path, p)) || HasLease(path)) continue;
                if (TryDelete(path)) condemned.Remove(path);
            }
            if (tornDown) DeleteTeardownResidue();
            SaveCondemned();
        }
    }

    private bool HasRetainedMetadata => state.AcceptedTerms.Count != 0 || state.Declined.Count != 0 || state.Found.Count != 0;

    private void DeleteTeardownResidue()
    {
        // Repeat whole-root cleanup on later collections, including held bin/work/downloads files outside versioned tools.
        foreach (var root in new[] { paths.Addons, paths.Store, paths.Views })
        {
            StorePaths.EnsureAncestors(root);
            if (!Directory.Exists(root)) continue;
            var keepState = root == paths.Addons && HasRetainedMetadata;
            var targets = HasLease(root) || keepState ? Directory.EnumerateFileSystemEntries(root).ToArray() : [root];
            foreach (var target in targets)
            {
                if (HasLease(target) || (keepState && StorePaths.Comparer.Equals(target, paths.StateFile))) continue;
                try { deletion.Delete(target); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException) { /* Next GC; all locks were already removed. */ }
            }
        }
    }

    /// <summary>Uninstall/reset primitive: all locks and read-only flags removed before the first deletion. Caller owns ending/rebuilding sessions.</summary>
    public void Teardown()
    {
        lock (gate)
        {
            tornDown = true;
            // Uninstall unlocks live trees too; nothing is left undeletable if the 30-second hook times out.
            foreach (var root in new[] { paths.Addons, paths.Store, paths.Views }) trees.Unlock(root);
            foreach (var root in new[] { paths.Addons, paths.Store, paths.Views })
            {
                try
                {
                    if (HasLease(root))
                    {
                        foreach (var candidate in Candidates().Where(p => StorePaths.Under(p, root)).ToArray()) Condemn(candidate);
                        continue;
                    }
                    deletion.Delete(root);
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    foreach (var candidate in Candidates().Where(p => StorePaths.Under(p, root)).ToArray()) Condemn(candidate);
                }
            }
            pending.Clear();
            reserved.Clear();
            foreach (var item in items.Values) Condemn(item.Folder);
            var retained = AddonsState.Restore([], state.AcceptedTerms, state.Declined, state.Found);
            if (retained.AcceptedTerms.Count != 0 || retained.Declined.Count != 0 || retained.Found.Count != 0) SaveState(retained);
            else state = retained; // Do not recreate an empty addons/state.json after removing everything.
            Collect(); // A live lease in one tree must not retain unrelated ended chats or old versions.
        }
    }
}
