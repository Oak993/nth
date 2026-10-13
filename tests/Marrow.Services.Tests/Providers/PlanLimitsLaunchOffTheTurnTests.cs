using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Services.Tests.Providers;

// PORT-ONLY (ED-NEW-perf-ui-thread-7): PlanLimitsReader.ReadClaudeFromCLI made its short-lived `claude -p` child with CreateProcessW on the caller's turn. It now awaits StdioProcess.StartAsync. The held
// launch (HeldLaunch) shows the read wait for the child with nothing blocked and nothing asked of it before it exists. A launch that fails still reads as null: that mapping is held by the existing
// PlanLimitsTests.ReadClaudeFromCLI_returns_null_when_the_executable_does_not_exist, which now runs through the awaited launch. The child is the compiled claude stand-in (R-TEST-8, R-TEST-10).
public sealed class PlanLimitsLaunchOffTheTurnTests
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(60);

    private const string UsageResponse =
        "{\"type\":\"control_response\",\"response\":{\"request_id\":\"__REQUEST_ID__\",\"response\":{" +
        "\"subscription_type\":\"max\",\"rate_limits\":{\"limits\":[{\"kind\":\"session\",\"percent\":42.5}]}}}}";

    private static SwiftURL FileUrl(string windowsPath) => SwiftURL.Create("marrow-native-path:" + windowsPath.Replace('\\', '/'))!;

    [Fact]
    [Trait("Decision", "ED-127")] // the launcher it keeps off the turn (ProcessLauncher.Run -> CreateProcessW); ED-NEW-perf-ui-thread-7
    public void The_read_waits_for_the_launch_instead_of_making_it_and_reads_the_limits_when_the_child_exists()
    {
        using var runtime = new TestRuntime();
        using var held = new HeldLaunch(runtime.Loop);
        var script = new ClaudeScript("cli-held");
        script.SetResponse(UsageResponse);
        var environment = new Dictionary<string, string> { [script.EnvironmentEntry.Key] = script.EnvironmentEntry.Value };
        Task<PlanLimits?>? read = null;

        runtime.OnMain(() => read = PlanLimitsReader.ReadClaudeFromCLI(FileUrl(script.ExecutablePath), environment));
        Assert.Equal(1, held.Requested);        // the read asked for the launch
        Assert.Equal(0, held.Launched);         // and did not make it itself
        Assert.False(read!.IsCompleted);        // it waits for the child with the loop idle: nothing is blocked
        Assert.Empty(script.Calls());           // none was run

        held.Release();
        var result = runtime.Loop.Run(() => read, Bound);
        Assert.NotNull(result);
        Assert.Equal(42.5, result!.Windows[0].Percent);
        Assert.Equal(0, held.LaunchedOnTheLoop);
    }
}
