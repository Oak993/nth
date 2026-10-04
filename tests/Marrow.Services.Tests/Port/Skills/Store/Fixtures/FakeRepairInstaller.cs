// PORT-ONLY: no real archive installer or process. SK-4 will implement the materialization boundary for tools.
using Marrow.Core.Port.Skills.Catalogue;
using Marrow.Services.Port.Skills.Store;

namespace Marrow.Services.Tests.Port.Skills.Store.Fixtures;

public sealed class FakeRepairInstaller : IStoreRepairInstaller
{
    public Task InstallAsync(CatalogueItem pinnedItem, string downloadsFolder, string preparedFolder, CancellationToken cancellationToken)
    {
        foreach (var path in Directory.EnumerateFiles(downloadsFolder, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var target = Path.Combine(preparedFolder, Path.GetRelativePath(downloadsFolder, path));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(path, target);
        }
        return Task.CompletedTask;
    }
}
