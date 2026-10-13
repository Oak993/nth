using System.Runtime.CompilerServices;
using Marrow.Core.Port;
using Marrow.Services.Providers;
using Marrow.Testing;

namespace Marrow.Services.Tests.Fixtures;

// PORT-ONLY: match the dispatch test's idle manual runtime, with no test-owned strong reference to its task/loop.
public static class UnrootedPlanLimitsProbe
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static WeakReference Start(ClaudeScript script, TaskCompletionSource<PlanLimits?> completed) =>
        new(Observe(script, completed));

    private static async Task Observe(ClaudeScript script, TaskCompletionSource<PlanLimits?> completed)
    {
        using var runtime = new TestRuntime();
        var environment = new Dictionary<string, string> { [script.EnvironmentEntry.Key] = script.EnvironmentEntry.Value };
        var result = await PlanLimitsReader.ReadClaudeFromCLI(
            SwiftURL.Create("marrow-native-path:" + script.ExecutablePath.Replace('\\', '/'))!, environment);
        completed.TrySetResult(result);
    }
}
