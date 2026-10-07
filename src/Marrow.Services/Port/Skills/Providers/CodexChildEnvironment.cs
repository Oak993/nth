// PORT-ONLY: SK-11b / DL-11, ONDEMAND-PLAN.md 3.6 "PATH rule" and 4.2: addons\bin goes into the PATH of every Codex app-server child, and nothing else in its environment changes.
using Marrow.Core.Port;
using Marrow.Services.Port.Skills.Shims;
using Marrow.Services.Store;

namespace Marrow.Services.Port.Skills.Providers;

public static class CodexChildEnvironment
{
    // The default asks the bound Storage (the app binds it at start-up); an unbound Storage (a test, or a tool that never bound it) is "not known", so the seam fails closed:
    // no folder is made and the environment is returned as it came.
    public static Ambient<CodexChildRoot> Root { get; } = new(new CodexChildRoot(StorageRoot));

    public static IReadOnlyDictionary<string, string> Apply(IReadOnlyDictionary<string, string> environment) =>
        Root.Current.Resolve() is { Length: > 0 } root ? ProviderCustomCommands.ForChild(environment, root) : environment;

    private static string? StorageRoot()
    {
        try { return Storage.Root; }
        catch (SwiftTrapException) { return null; }
    }
}
