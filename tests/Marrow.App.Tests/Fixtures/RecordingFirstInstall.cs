using Marrow.Core.Port.Skills.Catalogue;
using Marrow.Services.Port.Skills.Pages;

namespace Marrow.App.Tests.Fixtures;

internal sealed class RecordingFirstInstall(List<ItemKind> seen) : IFirstInstall
{
    public Task InstallAsync(CatalogueItem item, IProgress<double?> progress, CancellationToken cancellation)
    {
        seen.Add(item.Kind);
        return Task.CompletedTask;
    }
}
