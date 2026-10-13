using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.MCP;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Services.Tests.MCP;

// PORT-ONLY (ED-NEW-perf-ui-thread-7): MCPProbe.Probe is started from the main actor (the connection probe of Settings > MCP), and its stdio path made the server's child with CreateProcessW on that
// turn. It now awaits MCPStdioConnection.LaunchAsync (StdioProcess.StartAsync). The held launch (HeldLaunch) shows the probe wait for the child with nothing blocked and nothing asked of it before it
// exists; a launch that fails reaches the caller as the launcher's own error, as before. The server is a small scripted PowerShell process (system binary, R-TEST-10), never a real AI CLI.
public sealed class MCPProbeLaunchOffTheTurnTests
{
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(60);

    [Fact]
    [Trait("Decision", "ED-127")] // the launcher it keeps off the turn (ProcessLauncher.Run -> CreateProcessW); ED-NEW-perf-ui-thread-7
    public async Task The_probe_waits_for_the_launch_instead_of_making_it_and_finishes_when_the_child_exists()
    {
        using var runtime = new TestRuntime();
        using var held = new HeldLaunch(runtime.Loop);
        var server = new MCPResolvedServer("s", "S")
        {
            Command = ProbeProcessScripts.PowerShell,
            Args = new ValueList<string>(ProbeProcessScripts.Args(ProbeProcessScripts.EchoArgsAndEnvAndCwd(), ["held"])),
            Env = new ValueMap<string, string>(new Dictionary<string, string> { ["MCP_PROBE_TEST_VAR"] = "value" }),
        };

        var probe = MCPProbe.Probe(server);

        Assert.Equal(1, held.Requested);   // the probe asked for the launch
        Assert.Equal(0, held.Launched);    // and did not make it itself
        Assert.False(probe.IsCompleted);   // it waits for the child, with nothing blocked on the caller's turn
        held.Release();
        var result = await probe.WaitAsync(Guard);
        Assert.Equal("held|value", result.ServerName);
        Assert.Equal(1, held.Launched);
        Assert.Equal(0, held.LaunchedOnTheLoop);
    }

    [Fact]
    [Trait("Decision", "ED-127")]
    public async Task A_launch_that_cannot_start_reaches_the_caller_as_the_launchers_own_error()
    {
        var folder = Path.Combine(Path.GetTempPath(), "marrow-mcpprobe-launch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var notAProgram = Path.Combine(folder, "server.exe");
        File.WriteAllText(notAProgram, "this is not a Win32 program"); // found by the lookup, refused by CreateProcessW
        var server = new MCPResolvedServer("s", "S") { Command = notAProgram };
        // Ask the same launch directly what it says.
        var direct = Assert.ThrowsAny<Exception>(() => new StdioProcess(notAProgram, [], LoginEnvironment.HomeDirectory, new Dictionary<string, string>()).Start());

        var error = await Assert.ThrowsAnyAsync<Exception>(() => MCPProbe.Probe(server));

        Assert.Equal(direct.GetType(), error.GetType());
        Assert.Equal(direct.Message, error.Message);
    }
}
