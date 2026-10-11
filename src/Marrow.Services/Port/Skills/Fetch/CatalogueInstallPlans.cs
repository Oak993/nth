// PORT-ONLY: first-release feature unit SK-16 (DL-19, ONDEMAND-PLAN.md 3.2 and 3.4). There is no Swift twin. What the catalogue alone can say about how to fetch and install an item.
using Marrow.Core.Port.Skills.Catalogue;
using Marrow.Core.Port.Skills.State;
using Marrow.Services.Port.Downloads;
using Marrow.Services.Port.Skills.Install;
using Marrow.Services.Port.Skills.Pages;
using Marrow.Services.Port.Skills.Pipelines;

namespace Marrow.Services.Port.Skills.Fetch;

/// <summary>
/// Skill sets and plugins at one commit are fetched "file by file from https://raw.githubusercontent.com/&lt;owner&gt;/&lt;repo&gt;/&lt;pin&gt;/&lt;path&gt;" (ONDEMAND 3.2), each file checked against its pinned length and hash
/// (3.3) and laid out into the store (3.4). ENGINEERING DECISIONS (ED-368): (1) the verified inputs wait under <c>addons\downloads\items\&lt;id&gt;</c> until the install has checked the tree,
/// and are deleted when the binding is made (they are copies of what the store now holds); (2) the licence file is fetched from the item's own pinned licence URL (the catalogue says it is the same
/// raw file); (3) no spare projections are given (SK-9 builds each chat's generation itself, so a spare is only a speed-up nothing supplies yet); (4) the store folder is named after the item id and pin
/// (<c>&lt;id&gt;@&lt;pin8&gt;</c>); (5) UNRESOLVED: an item the catalogue holds no install parameters for (a tool or package set: Python embed options, wheels, node packages, msi layout), or one at per-skill pins
/// (no single commit), fails closed with the one unavailable message: nothing is guessed, and SK-2's pin run and its installer parameters are what would fill them.
/// </summary>
public sealed class CatalogueInstallPlans(string dataRoot, Uri? rawBase = null)
{
    private readonly string root = Path.GetFullPath(dataRoot);
    private readonly Uri raw = rawBase ?? new Uri("https://raw.githubusercontent.com/");

    private string Inputs(CatalogueItem item) => Path.Combine(root, "addons", "downloads", "items", item.Id);

    private static (string Owner, string Repository) Repository(CatalogueItem item)
    {
        if (item.SourceUrl is null || !Uri.TryCreate(item.SourceUrl, UriKind.Absolute, out var source) || !source.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)) throw new FirstInstallUnavailableException();
        var parts = source.AbsolutePath.Trim('/').Split('/');
        if (parts.Length < 2 || parts[0].Length == 0 || parts[1].Length == 0) throw new FirstInstallUnavailableException();
        return (parts[0], parts[1].EndsWith(".git", StringComparison.OrdinalIgnoreCase) ? parts[1][..^4] : parts[1]);
    }

    /// <summary>
    /// Whether <see cref="Plan"/> can say how to fetch and install the item: a skill set or plugin at one commit of a GitHub repository. Everything else (a tool, a package set, a set at per-skill pins) fails closed
    /// in <see cref="Plan"/> (decision (5) above), so a page or an offer must not offer to download it (ED-NEW-dlhonest-1).
    /// </summary>
    public static bool CanPlan(CatalogueItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.Kind is not (ItemKind.Skillset or ItemKind.Plugin) || string.IsNullOrEmpty(item.Pin)) return false;
        try { _ = Repository(item); return true; }
        catch (FirstInstallUnavailableException) { return false; }
    }

    /// <summary>The raw-file address of one file of the item at its pin.</summary>
    public Uri Url(CatalogueItem item, string path)
    {
        var (owner, repository) = Repository(item);
        if (string.IsNullOrEmpty(item.Pin)) throw new FirstInstallUnavailableException();
        var escaped = string.Join('/', InstallPaths.Relative(path).Split('/').Select(Uri.EscapeDataString));
        return new Uri(raw, $"{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(repository)}/{item.Pin}/{escaped}");
    }

    public InstallPlan Plan(CatalogueItem item, ItemRecord record)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.Kind is not (ItemKind.Skillset or ItemKind.Plugin) || string.IsNullOrEmpty(item.Pin)) throw new FirstInstallUnavailableException();
        _ = Repository(item);
        var folder = Inputs(item);
        var locals = item.Files.ToDictionary(file => file.Path, file => Path.GetFullPath(InstallPaths.Under(folder, file.Path)), StringComparer.Ordinal);
        return new InstallPlan(item, record, root, InstallerKind.Skillset, locals) { Source = item.Id, Spares = [] };
    }

    public IReadOnlyList<DownloadRequest> Requests(InstallPlan plan) => plan.Item.Files.Select(file =>
    {
        var url = file.Path == plan.Item.LicenceFile && plan.Item.LicenceUrl is not null ? new Uri(plan.Item.LicenceUrl) : Url(plan.Item, file.Path);
        return new DownloadRequest(url, plan.LocalFiles[file.Path], file.Size, file.Sha256, url.Host, file.Size);
    }).ToArray();

    /// <summary>The route a verified install is published with: no shims (a set provides no executable) and the spares the install made. The downloaded inputs are not needed any more.</summary>
    public PipelineRoute Bind(CatalogueItem item, InstallResult result)
    {
        try { if (Directory.Exists(Inputs(item))) Directory.Delete(Inputs(item), recursive: true); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { /* a held file: the next reset or install replaces it */ }
        return new PipelineRoute(item.Id, UpdateCheck.CataloguePin(item) ?? throw new InvalidDataException("Missing target pin."), result.Path, result.SparePaths.ToArray(), new(StringComparer.Ordinal));
    }
}
