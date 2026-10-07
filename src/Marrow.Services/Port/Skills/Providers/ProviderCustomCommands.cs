// PORT-ONLY: RR-18. Service composition stays out of the BCL-only standalone shim assembly.
using Marrow.Services.Port.Skills.Host;
using Marrow.Services.Port.Skills.Shims;
using Marrow.Services.Port.Skills.Store;

namespace Marrow.Services.Port.Skills.Providers;

public static class ProviderCustomCommands
{
    public static Dictionary<string, string> ForChild(IReadOnlyDictionary<string, string> environment, string dataRoot)
    {
        var child = ShimPath.ForChild(environment, dataRoot);
        if (SkillsHost.Find(dataRoot) is { } host)
            foreach (var bin in host.CustomSources.Commands().Select(c => Path.GetDirectoryName(c.Entry)!).Distinct(StorePaths.Comparer))
                child["PATH"] = ShimPath.Insert(child["PATH"], bin, Path.PathSeparator);
        return child;
    }
}
