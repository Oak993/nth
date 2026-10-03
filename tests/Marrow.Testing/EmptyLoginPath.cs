using System.Reflection;
using Marrow.Core.Support;

namespace Marrow.Testing;

/// <summary>
/// PORT-ONLY test seam reach (R-TEST-10, PR-GIT-051): a lookup over the login PATH (LoginEnvironment.Which, Shell.RunTool) finds nothing for the
/// calling async flow until the result is disposed, so a test of a missing tool is not defeated by a real gh, glab or tea on the owner's
/// machine or by a scripted stand-in another test put first on the process PATH. The seam is internal to Marrow.Core, so projects that cannot see
/// it reach it here by reflection (as AppModelHarness does for HomeForTesting); Marrow.Core.Tests sees it directly.
/// </summary>
public static class EmptyLoginPath
{
    public static IDisposable Begin()
    {
        var property = typeof(LoginEnvironment).GetProperty("PathForTesting", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("LoginEnvironment.PathForTesting is missing.");
        var ambient = property.GetValue(null)!;
        return (IDisposable)ambient.GetType().GetMethod("Override")!.Invoke(ambient, [""])!;
    }
}
