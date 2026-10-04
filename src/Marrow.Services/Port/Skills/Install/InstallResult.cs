// PORT-ONLY: SK-4 / DL-4, OD-0099 amendments; skills-ONDEMAND-PLAN 3.3-3.4 and skills-PLAN 2.1.
using Marrow.Core.Port.Skills.State;

namespace Marrow.Services.Port.Skills.Install;

public sealed record InstallResult(string Path, ItemRecord Record, IReadOnlyList<string> SparePaths, IReadOnlyList<Marrow.Core.Port.Skills.Catalogue.CatalogueFile> Manifest)
{
    public IReadOnlyList<string> Directories { get; init; } = [];
    /// <summary>integ-sk8-sk9: the GC exclusion of the published folder and its spares (null without a reservation seam). The owner disposes it once it registers or claims them.</summary>
    public IDisposable? Reservation { get; init; }
}
