// PORT-ONLY: SK-4 / DL-4, OD-0099 amendments; skills-ONDEMAND-PLAN 3.3-3.4 and skills-PLAN 2.1.
using Marrow.Core.Port.Skills.Catalogue;
using Marrow.Core.Port.Skills.State;

namespace Marrow.Services.Port.Skills.Install;

/// <summary>Offline installation of checked SK-3 files. Only a completed lock and full verify can return SK-7 Installed.</summary>
public sealed class ItemInstaller(IInstallLock? installLock = null, IInstallProcess? process = null, Action<InstallStep, string>? checkpoint = null, Func<string, IDisposable>? reserve = null)
{
    private readonly IInstallLock seal = installLock ?? new FailClosedInstallLock();
    private readonly IInstallProcess runner = process ?? new FailClosedInstallProcess();

    public async Task<InstallResult> InstallAsync(InstallPlan plan, CancellationToken cancellationToken = default)
    {
        if (installLock is null) throw new InvalidOperationException("Installer lock seam is not bound.");
        if (process is null && plan.Kind is InstallerKind.Pyset or InstallerKind.MsiAdmin or InstallerKind.Sfx7z) throw new InvalidOperationException("Installer process seam is not bound.");
        if (plan.Record.Problem() is not null || plan.Record.State != ItemState.Checking || plan.Record.Id != plan.Item.Id || plan.Record.TargetPin != plan.Item.Pin || plan.Item.PinState != PinState.Pinned)
            throw new InvalidDataException("Only a pinned, checking catalogue item can be installed.");
        if (!Path.IsPathFullyQualified(plan.DataRoot)) throw new InvalidDataException("An absolute data root is required.");
        InstallPaths.NoReparseAncestors(plan.DataRoot);
        var pin = InstallPaths.Relative(plan.Item.Pin!);
        if (pin.Contains('/', StringComparison.Ordinal)) throw new InvalidDataException("The pin must be a path component.");
        foreach (var file in plan.Item.Files) RequireInput(file.Path, file.Size, file.Sha256);
        foreach (var artifact in plan.Item.Artifacts)
            RequireInput(artifact.Name, artifact.Size ?? throw new InvalidDataException("Artifact size is not pinned."), artifact.Sha256 ?? throw new InvalidDataException("Artifact digest is not pinned."));
        var supplemental = (plan.Pyset?.Wheels ?? []).Concat(plan.Python?.PipWheel is { } pip ? [pip] : []);
        foreach (var wheel in supplemental) RequireSupplemental(wheel.Path, wheel.Size, wheel.Sha256);
        foreach (var package in plan.NodePackages ?? []) RequireSupplemental(package.Path, package.Size, package.Sha256);
        // ED-NEW-dl-13: a plugin item whose licences are recorded per plugin has no single licence file; each of its licence files must be pinned and supplied instead.
        IReadOnlyList<string> licences = plan.Item.LicenceFile is { } one ? [one]
            : PluginLicences.PerPlugin(plan.Item) ? PluginLicences.Files(plan.Item) : throw new InvalidDataException("A licence file path is required.");
        CatalogueFile? licencePin = null;
        foreach (var licence in licences)
        {
            var pinned = plan.Item.Files.SingleOrDefault(f => f.Path == licence) ?? throw new InvalidDataException("The licence file must have a content pin.");
            RequireInput(pinned.Path, pinned.Size, pinned.Sha256);
            licencePin ??= pinned;
        }
        if (licencePin is null) throw new InvalidDataException("A licence file path is required.");
        string parent;
        string name;
        if (plan.Kind == InstallerKind.Skillset)
        {
            var source = InstallPaths.Relative(plan.Source ?? throw new InvalidDataException("Skill source is required."));
            if (source.Contains('/', StringComparison.Ordinal) || pin.Length < 8) throw new InvalidDataException("Skill source and pin are invalid.");
            parent = InstallPaths.Under(plan.DataRoot, "skills/store");
            name = source + "@" + pin[..8];
        }
        else
        {
            var id = InstallPaths.Relative(plan.Item.Id);
            if (id.Contains('/', StringComparison.Ordinal)) throw new InvalidDataException("Item id must be a path component.");
            var digest = plan.Item.Artifacts.FirstOrDefault()?.Sha256 ?? licencePin.Sha256;
            parent = InstallPaths.Under(plan.DataRoot, "addons/tools");
            name = id + "-" + pin + "-" + digest[..8].ToLowerInvariant();
        }
        var transactions = new List<InstallTransaction>();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var transaction = new InstallTransaction(parent, name, seal, reserve);
            transactions.Add(transaction);
            checkpoint?.Invoke(InstallStep.Created, transaction.Path);
            string Archive()
            {
                if (plan.PrimaryArtifact is { } primary) return plan.LocalFiles[plan.Item.Artifacts.Single(a => a.Name == primary).Name];
                if (plan.Item.Artifacts.Count != 1) throw new InvalidDataException("This installer requires one explicit artifact; split multi-archive layouts into explicit installs.");
                return plan.LocalFiles[plan.Item.Artifacts[0].Name];
            }
            if (Directory.EnumerateFileSystemEntries(transaction.Path).Any()) throw new InvalidDataException("New build folder was modified.");
            switch (plan.Kind)
            {
                case InstallerKind.Zip:
                    if (plan.ZipArchives is null) ZipInstaller.Extract(Archive(), transaction.Path);
                    else foreach (var projection in plan.ZipArchives)
                    {
                        if (!plan.Item.Artifacts.Any(a => a.Name == projection.Artifact)) throw new InvalidDataException("Archive projection is not in the catalogue.");
                        ZipInstaller.Extract(plan.LocalFiles[projection.Artifact], projection.Prefix is null ? transaction.Path : InstallPaths.Under(transaction.Path, projection.Prefix));
                    }
                    break;
                case InstallerKind.PythonEmbed: PythonEmbedInstaller.Build(plan, transaction.Path, Archive()); break;
                case InstallerKind.Pyset: await PysetInstaller.BuildAsync(plan, transaction.Path, runner, cancellationToken); break;
                case InstallerKind.Nodeset: NodesetInstaller.Build(plan, transaction.Path); break;
                case InstallerKind.MsiAdmin: await MsiAdminInstaller.BuildAsync(plan, transaction.Path, Archive(), runner, cancellationToken); break;
                case InstallerKind.Sfx7z: await Sfx7zInstaller.BuildAsync(plan, transaction.Path, Archive(), runner, cancellationToken); break;
                case InstallerKind.Skillset: SkillsetInstaller.Build(plan, transaction.Path); break;
                default: throw new InvalidDataException("Unknown installer kind.");
            }
            foreach (var marker in plan.Markers ?? [])
            {
                var path = InstallPaths.Under(transaction.Path, InstallPaths.Relative(marker));
                if (File.Exists(path) && new FileInfo(path).Length == 0) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                using (new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
            }
            foreach (var licence in licences)
            {
                var pinned = plan.Item.Files.Single(f => f.Path == licence);
                var licenceTarget = InstallPaths.Under(transaction.Path, licence);
                if (!File.Exists(licenceTarget)) InstallTree.Copy(plan.LocalFiles[licence], licenceTarget);
                InstallHash.Verify(licenceTarget, pinned.Size, pinned.Sha256);
            }
            cancellationToken.ThrowIfCancellationRequested();
            transaction.Prepare(checkpoint, plan.Kind == InstallerKind.Skillset ? plan.Item.Files : null);
            var spares = plan.Kind == InstallerKind.Skillset ? SpareGenerationBuilder.Prepare(plan, transaction.Path, seal, checkpoint, transactions, reserve) : [];
            cancellationToken.ThrowIfCancellationRequested();
            transaction.Verify();
            foreach (var spare in spares) { spare.Verify(); spare.Publish(); checkpoint?.Invoke(InstallStep.Published, spare.Path); spare.Verify(); }
            transaction.Publish();
            checkpoint?.Invoke(InstallStep.Published, transaction.Path);
            transaction.Verify();
            var kept = transactions.Select(t => t.KeepPublished()).OfType<IDisposable>().ToArray();
            return new InstallResult(transaction.Path, ItemStateMachine.Apply(plan.Record, ItemState.Installed), spares.Select(s => s.Path).ToArray(), transaction.Manifest)
                { Directories = transaction.Directories, Reservation = reserve is null ? null : new InstallReservation(kept) };
        }
        catch (Exception original)
        {
            var failures = new List<Exception>();
            foreach (var transaction in transactions.AsEnumerable().Reverse())
                try { transaction.Rollback(); } catch (Exception error) { failures.Add(error); }
            // Rolled-back tmp- names are crash residue for the next GC; only their reservations end here.
            foreach (var transaction in transactions) transaction.Release();
            if (failures.Count > 0) throw new AggregateException("Install failed and quarantine cleanup needs retry.", new[] { original }.Concat(failures));
            throw;
        }
        void RequireSupplemental(string path, long size, string hash)
        {
            var artifact = plan.Item.Artifacts.SingleOrDefault(a => a.Size == size && string.Equals(a.Sha256, hash, StringComparison.OrdinalIgnoreCase) &&
                plan.LocalFiles.TryGetValue(a.Name, out var local) && Path.GetFullPath(local).Equals(Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase));
            if (artifact is null) throw new InvalidDataException("Supplemental package is not a catalogue artifact.");
        }
        void RequireInput(string key, long size, string hash)
        {
            InstallPaths.Relative(key);
            if (!plan.LocalFiles.TryGetValue(key, out var file)) throw new InvalidDataException("A pinned install input is missing.");
            InstallHash.Verify(file, size, hash);
        }
    }
}
