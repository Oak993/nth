namespace Marrow.Core.Port;

// PORT-ONLY (no Swift twin), R-TEST-10: no test may launch a real shell action (a browser, File Explorer, another
// app). The shell seams (ShellWorkspace, Workspace.Open, the usage top-up link) fail closed inside a test host: a test
// that forgets to override one gets a no-op instead of a real window on the owner's desktop. The detection is the
// one WorkspacePlatform.Guard already uses: an xunit, testhost or Marrow.Testing assembly in the process.
public static class ShellGuard
{
    private static readonly Lazy<bool> InTestHostValue = new(() => AppDomain.CurrentDomain.GetAssemblies().Any(a =>
        a.GetName().Name is { } name
        && (name.StartsWith("xunit", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("testhost", StringComparison.OrdinalIgnoreCase)
            || name == "Marrow.Testing")));

    /// <summary>True when this process is a test host: shell actions must not reach the real shell.</summary>
    public static bool InTestHost => InTestHostValue.Value;
}
