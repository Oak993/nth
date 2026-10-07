// PORT-ONLY: SK-11f / DL-11 (skills-ONDEMAND-PLAN.md 3.6, 4.2; ED-309). The environment an API provider's child (run_command's shell) gets: the one it always got, with addons\bin
// put on PATH by the SK-6 rule and nothing else changed.
using Marrow.Core.Port;
using Marrow.Services.Port.Skills.Shims;
using Marrow.Services.Store;

namespace Marrow.Services.Port.Skills.Providers;

public static class ProviderChildEnvironment
{
    /// <summary>
    /// <paramref name="environment"/> with addons\bin of the app's data root on PATH. When no data root is bound (the app binds it at start-up; a test binds its own throwaway folder) the
    /// environment is returned as it is and no folder is made: the seam fails closed rather than reaching for a real user folder.
    /// </summary>
    public static IReadOnlyDictionary<string, string> Apply(IReadOnlyDictionary<string, string> environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        var root = BoundDataRoot();
        return root is null ? environment : Apply(environment, root);
    }

    public static IReadOnlyDictionary<string, string> Apply(IReadOnlyDictionary<string, string> environment, string dataRoot)
    {
        ArgumentNullException.ThrowIfNull(environment);
        return ProviderCustomCommands.ForChild(environment, dataRoot);
    }

    private static string? BoundDataRoot()
    {
        try { return Storage.Root; }
        catch (SwiftTrapException) { return null; } // Storage.Locations is unbound
    }
}
