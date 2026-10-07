// PORT-ONLY: DL-6 launch root. R-TEST-9's explicit disposable root preserves offline callers of an unbound storage seam.
using Marrow.Core.Port;
using Marrow.Services.Store;

namespace Marrow.Services.Port.Skills.Providers;

public static class SkillsProviderDataRoot
{
    public static string Resolve(IStorageLocations locations, string? testRoot)
    {
        try { return locations.Root; }
        // This sentinel is Storage.cs's existing unbound seam, not an arbitrary storage failure.
        catch (SwiftTrapException error) when (error.Message == "Storage.Locations is not bound: Marrow.App sets it at start-up; tests override it." &&
            testRoot is not null && Path.IsPathFullyQualified(testRoot) && Directory.Exists(testRoot))
        {
            return Path.GetFullPath(testRoot);
        }
    }
}
