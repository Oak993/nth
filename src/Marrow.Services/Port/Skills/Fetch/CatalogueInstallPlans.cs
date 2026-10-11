// PORT-ONLY: first-release feature unit SK-16 (DL-19, ONDEMAND-PLAN.md 3.2 and 3.4). There is no Swift twin. What the catalogue alone can say about how to fetch and install an item.
using Marrow.Core.Port.Skills.Catalogue;
using Marrow.Core.Port.Skills.State;
using Marrow.Services.Port.Downloads;
using Marrow.Services.Port.Skills.Install;
using Marrow.Services.Port.Skills.Pages;
using Marrow.Services.Port.Skills.Pipelines;
using Marrow.Services.Port.Skills.Shims;

namespace Marrow.Services.Port.Skills.Fetch;

/// <summary>
/// Skill sets and plugins are fetched "file by file from https://raw.githubusercontent.com/&lt;owner&gt;/&lt;repo&gt;/&lt;pin&gt;/&lt;path&gt;" (ONDEMAND 3.2), each file checked against its pinned length and hash
/// (3.3) and laid out into the store (3.4). ENGINEERING DECISIONS (ED-368): (1) the verified inputs wait under <c>addons\downloads\items\&lt;id&gt;</c> until the install has checked the tree,
/// and are deleted when the binding is made (they are copies of what the store now holds); (2) the licence file is fetched from the item's own pinned licence URL (the catalogue says it is the same
/// raw file); (3) no spare projections are given (SK-9 builds each chat's generation itself, so a spare is only a speed-up nothing supplies yet); (4) the store folder is named after the item id and pin
/// (<c>&lt;id&gt;@&lt;pin8&gt;</c>).
/// ED-NEW-dl-7 (RR-14) retires ED-368's decision (5), "an item the catalogue holds no install parameters for, or one at per-skill pins, fails closed": (5a) a set at per-skill pins (O3) fetches each
/// file from its own skill's commit; (5b) a tool or package set is fetched artifact by artifact from the catalogue's pinned URLs (and its licence file from its pinned licence URL) and installed by the kind
/// its catalogue <c>install</c> parameters name, with the shims, Python package folder and exports they name bound into its route. An item without those parameters still fails closed with the one
/// unavailable message, and <see cref="CanPlan"/> keeps it from being offered.
/// </summary>
public sealed class CatalogueInstallPlans(string dataRoot, Uri? rawBase = null, SkillsCatalogue? catalogue = null)
{
    private readonly string root = Path.GetFullPath(dataRoot);
    private readonly Uri raw = rawBase ?? new Uri("https://raw.githubusercontent.com/");

    private string Inputs(CatalogueItem item) => Path.Combine(root, "addons", "downloads", "items", item.Id);

    private string StateFile => Path.Combine(root, "addons", "state.json");

    private static (string Owner, string Repository) Repository(CatalogueItem item)
    {
        if (item.SourceUrl is null || !Uri.TryCreate(item.SourceUrl, UriKind.Absolute, out var source) || !source.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)) throw new FirstInstallUnavailableException();
        var parts = source.AbsolutePath.Trim('/').Split('/');
        if (parts.Length < 2 || parts[0].Length == 0 || parts[1].Length == 0) throw new FirstInstallUnavailableException();
        return (parts[0], parts[1].EndsWith(".git", StringComparison.OrdinalIgnoreCase) ? parts[1][..^4] : parts[1]);
    }

    /// <summary>The commit a file of a set is fetched at: the set's one pin, or (5a) the pin of the listed skill whose folder holds it.</summary>
    private static string CommitOf(CatalogueItem item, string path)
    {
        if (!string.IsNullOrEmpty(item.Pin)) return item.Pin;
        if (UpdateCheck.CataloguePin(item) is null) throw new FirstInstallUnavailableException();
        var owners = item.Skills.Where(s => s.Path is not null && path.StartsWith(s.Path + "/", StringComparison.Ordinal)).ToArray();
        return owners.Length == 1 ? owners[0].Pin! : throw new FirstInstallUnavailableException();
    }

    /// <summary>
    /// Whether <see cref="Plan"/> can say how to fetch and install the item: a skill set or plugin of a GitHub repository at one commit or at a commit per listed skill that holds each of its files, or a
    /// tool or package set whose catalogue entry carries its installer parameters and its licence file pin (ED-NEW-dl-7). Everything else fails closed in <see cref="Plan"/>, so a page or an offer must
    /// not offer to download it (ED-NEW-dlhonest-1).
    /// </summary>
    public static bool CanPlan(CatalogueItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        try
        {
            // ED-NEW-dl-11: only a pinned item is offered, by SK-1's own rule (nothing missing); a blocked set keeps its plan's commit but has no files or licence to fetch.
            if (CatalogueGate.Missing(item).Count > 0) return false;
            if (item.Kind is ItemKind.Skillset or ItemKind.Plugin)
            {
                _ = Repository(item);
                if (string.IsNullOrEmpty(item.Pin)) foreach (var file in item.Files) if (file.Path != item.LicenceFile || item.LicenceUrl is null) _ = CommitOf(item, file.Path);
                return UpdateCheck.CataloguePin(item) is not null;
            }
            return item.Install is not null && CatalogueGate.InstallMissing(item) is null && item.LicenceUrl is not null && item.Artifacts.Count > 0 &&
                item.Artifacts.All(a => a.Url is not null && a.Size is not null && a.Sha256 is not null);
        }
        catch (FirstInstallUnavailableException) { return false; }
    }

    /// <summary>The raw-file address of one file of the item at its pin (the file's own skill's pin for a set at per-skill pins).</summary>
    public Uri Url(CatalogueItem item, string path)
    {
        var (owner, repository) = Repository(item);
        var commit = CommitOf(item, path);
        var escaped = string.Join('/', InstallPaths.Relative(path).Split('/').Select(Uri.EscapeDataString));
        return new Uri(raw, $"{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(repository)}/{commit}/{escaped}");
    }

    public InstallPlan Plan(CatalogueItem item, ItemRecord record)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (!CanPlan(item)) throw new FirstInstallUnavailableException();
        var folder = Inputs(item);
        if (item.Kind is ItemKind.Skillset or ItemKind.Plugin)
        {
            var locals = item.Files.ToDictionary(file => file.Path, file => Path.GetFullPath(InstallPaths.Under(folder, file.Path)), StringComparer.Ordinal);
            return new InstallPlan(item, record, root, InstallerKind.Skillset, locals) { Source = item.Id, Spares = [] };
        }
        var install = item.Install!;
        var inputs = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var artifact in item.Artifacts) inputs.Add(artifact.Name, Path.GetFullPath(InstallPaths.Under(folder, "artifacts/" + InstallPaths.Relative(artifact.Name))));
        foreach (var file in item.Files) inputs.Add(file.Path, Path.GetFullPath(InstallPaths.Under(folder, "files/" + file.Path)));
        CatalogueArtifact Artifact(string name) => item.Artifacts.Single(a => a.Name == name);
        var plan = new InstallPlan(item, record, root, Kind(install.Kind), inputs) { Markers = install.Markers.ToArray() };
        switch (install.Kind)
        {
            case InstallKind.Zip:
                return plan with { ZipArchives = install.Archives.Select(a => new ZipProjection(a.Artifact, a.Prefix)).ToArray() };
            case InstallKind.PythonEmbed:
                var python = install.Python!;
                var pip = Artifact(python.PipArtifact);
                return plan with
                {
                    PrimaryArtifact = install.Archives.Single().Artifact,
                    Python = new PythonEmbedOptions(python.PthFile, python.StandardLibraryZip, python.AgentVersion, new LocalWheel(inputs[pip.Name], pip.Size!.Value, pip.Sha256!, Requirement(pip.Name))),
                };
            case InstallKind.Pyset:
                return plan with
                {
                    Pyset = new PysetOptions(InstalledPython(item), item.Artifacts.Select(a => new LocalWheel(inputs[a.Name], a.Size!.Value, a.Sha256!, Requirement(a.Name))).ToArray()),
                };
            case InstallKind.Nodeset:
                return plan with
                {
                    NodePackages = install.NodeTree.Select(p => { var a = Artifact(p.Artifact); return new LocalNodePackage(inputs[a.Name], a.Size!.Value, a.Sha256!, p.Integrity, p.Path); }).ToArray(),
                };
            case InstallKind.MsiAdmin:
                return plan with { PrimaryArtifact = item.Artifacts.Single().Name, Extractor = CatalogueInstallProcess.Msiexec };
            case InstallKind.Sfx7z:
                return plan with { PrimaryArtifact = item.Artifacts.Single().Name, Extractor = inputs[item.Artifacts.Single().Name] };
            default:
                throw new FirstInstallUnavailableException();
        }
    }

    private static InstallerKind Kind(InstallKind kind) => kind switch
    {
        InstallKind.Zip => InstallerKind.Zip,
        InstallKind.PythonEmbed => InstallerKind.PythonEmbed,
        InstallKind.Pyset => InstallerKind.Pyset,
        InstallKind.Nodeset => InstallerKind.Nodeset,
        InstallKind.MsiAdmin => InstallerKind.MsiAdmin,
        InstallKind.Sfx7z => InstallerKind.Sfx7z,
        _ => throw new FirstInstallUnavailableException(),
    };

    /// <summary>A wheel's own pinned requirement, from its file name (PEP 427: <c>{name}-{version}(-{build})?-{python}-{abi}-{platform}.whl</c>).</summary>
    internal static string Requirement(string wheel)
    {
        var parts = wheel.Split('-');
        if (!wheel.EndsWith(".whl", StringComparison.Ordinal) || parts.Length is < 5 or > 6) throw new InvalidDataException("Not a wheel file name: " + wheel);
        return parts[0] + "==" + parts[1];
    }

    /// <summary>The interpreter a package set is installed with: the selected pin of the Python the set needs, at the target of its Python shim (ONDEMAND 3.4 "run Marrow's pip").</summary>
    private string InstalledPython(CatalogueItem item)
    {
        var all = catalogue ?? throw new FirstInstallUnavailableException();
        var python = item.Needs.Select(n => all.Items.FirstOrDefault(i => i.Id == n)).SingleOrDefault(i => i?.Install?.Kind == InstallKind.PythonEmbed) ?? throw new FirstInstallUnavailableException();
        var shim = python.Install!.Shims.First(s => s.Python);
        var route = PipelineRoutes.Read(StateFile).SingleOrDefault(r => r.Id == python.Id) ?? throw new InvalidOperationException("Install requires " + python.Id + " first.");
        return route.Shims.Single(p => p.Key.Equals(shim.Name, StringComparison.OrdinalIgnoreCase)).Value.Target;
    }

    public IReadOnlyList<DownloadRequest> Requests(InstallPlan plan)
    {
        var item = plan.Item;
        var files = item.Files.Select(file =>
        {
            var url = file.Path == item.LicenceFile && item.LicenceUrl is not null ? new Uri(item.LicenceUrl) : Url(item, file.Path);
            return new DownloadRequest(url, plan.LocalFiles[file.Path], file.Size, file.Sha256, url.Host, file.Size);
        });
        var artifacts = item.Artifacts.Select(artifact =>
        {
            var url = new Uri(artifact.Url ?? throw new FirstInstallUnavailableException());
            return new DownloadRequest(url, plan.LocalFiles[artifact.Name], artifact.Size ?? throw new FirstInstallUnavailableException(), artifact.Sha256 ?? throw new FirstInstallUnavailableException(), url.Host, artifact.Size.Value);
        });
        return artifacts.Concat(files).ToArray();
    }

    /// <summary>
    /// The route a verified install is published with: a set provides no executable; a tool's shims (ONDEMAND 3.6), a package set's place on the Python path and an item's exports come from its
    /// catalogue <c>install</c> parameters, every path inside the verified folder (ED-NEW-dl-7). The downloaded inputs are not needed any more.
    /// </summary>
    public PipelineRoute Bind(CatalogueItem item, InstallResult result)
    {
        try { if (Directory.Exists(Inputs(item))) Directory.Delete(Inputs(item), recursive: true); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { /* a held file: the next reset or install replaces it */ }
        var pin = UpdateCheck.CataloguePin(item) ?? throw new InvalidDataException("Missing target pin.");
        var route = new PipelineRoute(item.Id, pin, result.Path, result.SparePaths.ToArray(), new(StringComparer.Ordinal));
        if (item.Install is not { } install) return route;
        var folder = Path.GetFullPath(result.Path);
        var work = Path.Combine(root, "addons", "work");
        // "{folder}" alone is the verified folder (T10's PLAYWRIGHT_BROWSERS_PATH); "{folder}/a/b" a path inside it; anywhere else it is refused.
        string Expand(string value)
        {
            const string Folder = "{folder}";
            if (!value.Contains(Folder, StringComparison.Ordinal)) return value;
            var rest = value.StartsWith(Folder, StringComparison.Ordinal) ? value[Folder.Length..] : throw new InvalidDataException("{folder} only starts a value.");
            if (rest.Length == 0) return folder;
            if (rest[0] != '/' || rest.Contains(Folder, StringComparison.Ordinal)) throw new InvalidDataException("{folder} is followed by a path inside the folder.");
            return Path.GetFullPath(folder + Path.DirectorySeparatorChar + InstallPaths.Relative(rest[1..]));
        }
        var links = new Dictionary<string, PipelineShimLink>(StringComparer.Ordinal);
        foreach (var shim in install.Shims)
        {
            var arguments = shim.Arguments.Select(Expand).ToArray();
            if (shim.Target is null) { links.Add(shim.Name, new PipelineShimLink(shim.Item!, shim.Shim!, arguments)); continue; }
            route.Shims.Add(shim.Name, new ShimDefinition
            {
                Target = InstallPaths.Under(folder, InstallPaths.Relative(shim.Target)),
                PrefixArguments = arguments,
                PythonPathFile = shim.Python ? InstallPaths.Under(folder, install.Python!.PthFile) : null,
                WorkDirectory = shim.Python || shim.LibreOffice ? work : null,
                LibreOffice = shim.LibreOffice,
            });
        }
        return route with
        {
            Links = links.Count == 0 ? null : links,
            PythonPackages = install.PythonPackages,
            Exports = install.Exports.Count == 0 ? null : install.Exports.ToDictionary(e => e.Name, e => Expand(e.Value), StringComparer.Ordinal),
        };
    }
}
