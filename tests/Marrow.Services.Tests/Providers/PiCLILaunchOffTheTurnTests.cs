using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Services.Tests.Providers;

// PORT-ONLY (ED-NEW-perf-ui-thread-7): PiCLI.ListModels made its throwaway `pi --mode rpc` child with CreateProcessW on the caller's turn. It now awaits StdioProcess.StartAsync. The held launch
// (HeldLaunch) shows the probe wait for the child with nothing blocked and the replies asked for only once the child exists; a launch that fails keeps the "Pi could not start: " message built from
// the launcher's own error. The child is the compiled pi stand-in (R-TEST-8, R-TEST-10), never a real CLI.
public sealed class PiCLILaunchOffTheTurnTests
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(60);

    private static string Scratch(string label)
    {
        var root = TestDataDirectory.Read();
        if (string.IsNullOrEmpty(root))
            throw new InvalidOperationException("Set MARROW_PORT_DATA_DIR to a throwaway folder before running the tests (R-TEST-9).");
        var folder = Path.Combine(root, "PiCLILaunchOffTheTurn", label + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }

    private static SwiftURL FileUrl(string windowsPath) => SwiftURL.Create("marrow-native-path:" + windowsPath.Replace('\\', '/'))!;

    [Fact]
    [Trait("Decision", "ED-127")] // the launcher it keeps off the turn (ProcessLauncher.Run -> CreateProcessW); ED-NEW-perf-ui-thread-7
    public void The_probe_waits_for_the_launch_instead_of_making_it_and_lists_the_models_when_the_child_exists()
    {
        using var runtime = new TestRuntime();
        using var held = new HeldLaunch(runtime.Loop);
        var script = new PiScript("probe-held");
        script.Reply(0, "{\"type\":\"response\",\"command\":\"get_state\",\"id\":\"state\",\"data\":{\"model\":{\"provider\":\"openai\",\"id\":\"gpt-5\"}}}");
        script.Reply(1, "{\"type\":\"response\",\"id\":\"models\",\"data\":{\"models\":[{\"provider\":\"openai\",\"id\":\"gpt-5\",\"reasoning\":true}]}}");
        var environment = new ValueMap<string, string>([script.EnvironmentEntry, new KeyValuePair<string, string>("HOME", Scratch("home"))]);
        Task<ValueList<ModelOption>>? probe = null;

        runtime.OnMain(() => probe = PiCLI.ListModels(FileUrl(script.ExecutablePath), environment));
        Assert.Equal(1, held.Requested);       // the probe asked for the launch
        Assert.Equal(0, held.Launched);        // and did not make it itself
        Assert.False(probe!.IsCompleted);      // it waits for the child with the loop idle: nothing is blocked
        Assert.Null(script.Arguments());       // none was run
        Assert.Empty(script.StdinLines());     // and nothing was asked yet

        held.Release();
        var options = runtime.Loop.Run(() => probe, Bound);
        Assert.Equal(["openai/gpt-5"], options.Select(o => o.Id).ToList());
        Assert.Equal(0, held.LaunchedOnTheLoop);
        runtime.Loop.RunUntil(() => script.SawEndOfInput, Bound);
    }

    [Fact]
    [Trait("Decision", "ED-127")]
    public void A_launch_that_cannot_start_keeps_the_could_not_start_message_built_from_the_launchers_own_error()
    {
        using var runtime = new TestRuntime();
        var home = Scratch("missing-home");
        var missing = Path.Combine(Scratch("missing"), "pi.exe");
        // Ask the same launch directly what it says.
        var direct = Assert.ThrowsAny<Exception>(() => new StdioProcess(missing, [], home, new Dictionary<string, string>()).Start());

        var error = Assert.Throws<ProviderError>(() =>
            runtime.Loop.Run(() => PiCLI.ListModels(FileUrl(missing), new ValueMap<string, string>([new KeyValuePair<string, string>("HOME", home)])), Bound));

        Assert.Equal(new ProviderError.Case.Failed("Pi could not start: " + direct.Message), error.ErrorCase);
    }
}
