// PORT-ONLY: SK-4 / DL-4, OD-0099 amendments; skills-ONDEMAND-PLAN 3.3-3.4 and skills-PLAN 2.1.
using Marrow.Core.Port.Skills.Catalogue;
using Marrow.Core.Port.Skills.State;
using Marrow.Services.Port.Downloads;

namespace Marrow.Services.Port.Skills.Install;

/// <summary>
/// Explicit installer parameters bridge SK-1 (which has no install parameter field yet) to DL-4.
/// LocalFiles are SK-3's successful, downloaded files, keyed by catalogue artifact name or file path.
/// Every byte is rechecked against those pins before use. No network or state.json mutation occurs here.
/// </summary>
public sealed record InstallPlan(CatalogueItem Item, ItemRecord Record, string DataRoot, InstallerKind Kind, IReadOnlyDictionary<string, string> LocalFiles)
{
    public IReadOnlyList<ZipProjection>? ZipArchives { get; init; }
    public string? PrimaryArtifact { get; init; }
    public string? Source { get; init; }
    public PythonEmbedOptions? Python { get; init; }
    public PysetOptions? Pyset { get; init; }
    public IReadOnlyList<LocalNodePackage>? NodePackages { get; init; }
    public string? Extractor { get; init; }
    public IReadOnlyList<SpareProjection>? Spares { get; init; }
    /// <summary>ED-NEW-dl-7: empty files the tool's runtime looks for, added to the built tree before it is locked (Playwright's INSTALLATION_COMPLETE).</summary>
    public IReadOnlyList<string>? Markers { get; init; }

    public InstallPlan WithDownloads(DownloadOutcome outcome)
    {
        if (!outcome.Succeeded) throw new InvalidDataException("Only successful downloads can be installed.");
        var paths = outcome.Files.Select(f => Path.GetFullPath(f.DestinationPath)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (LocalFiles.Values.Any(p => !paths.Contains(Path.GetFullPath(p)))) throw new InvalidDataException("An install input is absent from the download result.");
        return this;
    }
}
