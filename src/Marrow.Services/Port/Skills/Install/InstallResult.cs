// PORT-ONLY: SK-4 / DL-4, OD-0099 amendments; skills-ONDEMAND-PLAN 3.3-3.4 and skills-PLAN 2.1.
using Marrow.Core.Port.Skills.State;

namespace Marrow.Services.Port.Skills.Install;

public sealed record InstallResult(string Path, ItemRecord Record, IReadOnlyList<string> SparePaths, IReadOnlyList<Marrow.Core.Port.Skills.Catalogue.CatalogueFile> Manifest)
{
    public IReadOnlyList<string> Directories { get; init; } = [];
}
