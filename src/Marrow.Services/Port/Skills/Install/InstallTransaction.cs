// PORT-ONLY: SK-4 / DL-4, OD-0099 amendments; skills-ONDEMAND-PLAN 3.3-3.4 and skills-PLAN 2.1.
using Marrow.Core.Port.Skills.Catalogue;

namespace Marrow.Services.Port.Skills.Install;

/// <summary>
/// A provisional rename stays tmp-prefixed until its lock and manifest verify. This prevents abrupt process
/// termination during build/rename/lock/verify from leaving a usable incomplete item (ED-322).
/// Publication is the final atomic rename. Fault injection after publication rolls that rename back.
/// </summary>
internal sealed class InstallTransaction
{
    private readonly IInstallLock seal;
    private bool lockAttempted;
    public string Path { get; private set; }
    public string Destination { get; }
    public IReadOnlyList<string> Directories { get; private set; } = [];
    public IReadOnlyList<CatalogueFile> Manifest { get; private set; } = [];
    private readonly Func<string, IDisposable>? reserve;
    private readonly List<(string Path, IDisposable Hold)> holds = [];
    public InstallTransaction(string parent, string destination, IInstallLock seal, Func<string, IDisposable>? reserve = null)
    {
        this.seal = seal;
        this.reserve = reserve;
        InstallPaths.NoReparseAncestors(parent);
        Destination = InstallPaths.Under(parent, destination);
        Path = System.IO.Path.Combine(parent, "tmp-" + Guid.NewGuid().ToString("N"));
        Hold(Path); // integ-sk8-sk9: every name this build will occupy is reserved before it exists, so the store's GC never sees it unreserved.
        Directory.CreateDirectory(Path);
    }
    private void Hold(string path) { if (reserve is not null) holds.Add((path, reserve(path))); }
    /// <summary>Releases the reservations of the intermediate names and returns the published name's, for the owner to release.</summary>
    public IDisposable? KeepPublished()
    {
        IDisposable? kept = null;
        foreach (var (path, hold) in holds)
            if (kept is null && System.IO.Path.GetFullPath(path).Equals(System.IO.Path.GetFullPath(Path), StringComparison.OrdinalIgnoreCase)) kept = hold;
            else hold.Dispose();
        holds.Clear();
        return kept;
    }
    public void Release()
    {
        foreach (var (_, hold) in holds) hold.Dispose();
        holds.Clear();
    }
    public void Prepare(Action<InstallStep, string>? checkpoint, IReadOnlyList<CatalogueFile>? expected = null)
    {
        Manifest = expected ?? InstallTree.Snapshot(Path);
        Directories = expected is null ? Directory.EnumerateDirectories(Path, "*", SearchOption.AllDirectories).Select(p => InstallPaths.Relative(System.IO.Path.GetRelativePath(Path, p))).ToArray() : InstallTree.Directories(expected);
        checkpoint?.Invoke(InstallStep.Built, Path);
        Move(Path + "-ready");
        checkpoint?.Invoke(InstallStep.Renamed, Path);
        lockAttempted = true;
        seal.Lock(Path);
        checkpoint?.Invoke(InstallStep.Locked, Path);
        Verify();
        checkpoint?.Invoke(InstallStep.Verified, Path);
    }
    public void Verify() => InstallTree.Verify(Path, Manifest, seal, Directories);
    public void Publish()
    {
        // Directory.Move never overwrites a previous installation, including an update at the same pin.
        Move(Destination);
    }
    private void Move(string target)
    {
        InstallPaths.NoReparseAncestors(Path);
        InstallPaths.NoReparseAncestors(target);
        var old = Path;
        Hold(target);
        Directory.Move(old, target);
        Path = target;
        seal.Renamed(old, target);
    }
    public void Rollback()
    {
        // Quarantine first: even an unlock failure must leave a tmp name. Never recursively traverse a reparse point here.
        if (!Directory.Exists(Path)) return;
        if (!System.IO.Path.GetFileName(Path).StartsWith("tmp-", StringComparison.Ordinal))
            Move(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Path)!, "tmp-" + Guid.NewGuid().ToString("N")));
        if (lockAttempted) seal.Unlock(Path);
    }
}
