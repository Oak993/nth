// PORT-ONLY: SK-5 Repair for plain-file skillsets/plugins; archives remain SK-4's responsibility.
using Marrow.Core.Port.Skills.Catalogue;

namespace Marrow.Services.Port.Skills.Store;

public sealed class PinnedSkillRepairInstaller : IStoreRepairInstaller
{
    public Task InstallAsync(CatalogueItem pinnedItem, string downloadsFolder, string preparedFolder, CancellationToken cancellationToken)
    {
        if (pinnedItem.Kind is not (ItemKind.Skillset or ItemKind.Plugin))
            throw new InvalidOperationException("Archive repair requires the SK-4 installer boundary.");
        StorePaths.EnsureAncestors(downloadsFolder);
        StorePaths.EnsureAncestors(preparedFolder);
        foreach (var file in new StoreManifest(pinnedItem.Files).Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relative = file.Path.Replace('/', Path.DirectorySeparatorChar);
            var source = Path.Combine(downloadsFolder, relative);
            StorePaths.EnsureAncestors(source);
            var target = Path.Combine(preparedFolder, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(source, target, overwrite: false);
        }
        return Task.CompletedTask;
    }
}
