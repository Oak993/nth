using System.Text;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Port.Skills.Providers;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Port.Skills.Providers;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;
using Xunit;

namespace Marrow.Services.Tests.Providers;

// PORT-ONLY (ED-NEW-perf-ui-thread-2): ClaudeSession.StartCore launched the child with CreateProcessW on the caller's turn, which for a send is the UI thread. It now awaits
// IClaudeProcess.StartAsync, which StdioProcess runs on the pool. The offline transport holds that launch, so the test sees the session wait for it without a thread blocked,
// and a Stop that comes while the child is being created ends the child once it exists.
public sealed class ClaudeSessionLaunchOffTheTurnTests
{
    private static SessionConfiguration Config() => new()
    {
        Provider = ProviderKind.Claude,
        Executable = SwiftURL.Create("/offline/claude-" + Guid.NewGuid().ToString("N"))!,
        WorkingDirectory = SwiftURL.Create("/tmp")!,
        Environment = new ValueMap<string, string>(new Dictionary<string, string> { ["PATH"] = "user-bin" }),
        Model = "claude-opus-5-5",
        RuntimeMode = RuntimeMode.Supervised,
        InteractionMode = InteractionMode.Build,
    };

    private static IDisposable Help() => ClaudeProviderRuntime.Help.Override((_, _) => Task.FromResult(new ShellResult(0, Encoding.UTF8.GetBytes("--agents"), [])));

    [Fact]
    [Trait("Decision", "ED-127")] // the launcher it keeps off the turn (ProcessLauncher.Run -> CreateProcessW); ED-NEW-perf-ui-thread-2
    public void The_session_waits_for_the_launch_instead_of_making_it_and_goes_on_when_the_child_exists()
    {
        using var runtime = new TestRuntime();
        using var scratch = new StorageScratchFolder();
        using var help = Help();
        GatedStartClaudeProcess? process = null;
        using var transport = ClaudeProviderRuntime.Process.Override((_, args, _, env) => process = new GatedStartClaudeProcess(args, env));
        var session = new ClaudeSession(Config(), () => null);
        Task<string>? start = null;

        runtime.OnMain(() => start = session.Start());
        runtime.Loop.RunUntilIdle();
        Assert.NotNull(process);
        Assert.Equal(1, process!.StartAsyncCalls);   // the session asked for the launch
        Assert.Equal(0, process.StartCalls);          // and did not make it itself
        Assert.False(start!.IsCompleted);             // it is waiting for the child, with nothing blocked: the loop has run idle

        process.Launch();
        runtime.Loop.Run(() => start, TimeSpan.FromSeconds(5));
        Assert.True(start.IsCompletedSuccessfully);
        Assert.Equal(0, process.StartCalls);
        Assert.Equal(0, process.TerminatesBeforeTheChildExisted + process.TerminatesOfTheRunningChild);
        session.Stop();
    }

    [Fact]
    [Trait("Decision", "ED-127")]
    public void A_stop_that_comes_while_the_child_is_being_created_ends_it_once_it_exists()
    {
        using var runtime = new TestRuntime();
        using var scratch = new StorageScratchFolder();
        using var help = Help();
        GatedStartClaudeProcess? process = null;
        using var transport = ClaudeProviderRuntime.Process.Override((_, args, _, env) => process = new GatedStartClaudeProcess(args, env));
        var session = new ClaudeSession(Config(), () => null);
        Task<string>? start = null;

        runtime.OnMain(() => start = session.Start());
        runtime.Loop.RunUntilIdle();
        runtime.OnMain(session.Stop);                  // the child does not exist yet: this Stop ends nothing
        Assert.Equal(1, process!.TerminatesBeforeTheChildExisted);
        Assert.Equal(0, process.TerminatesOfTheRunningChild);

        process.Launch();
        runtime.Loop.Run(() => start!.ContinueWith(_ => { }), TimeSpan.FromSeconds(5));
        Assert.Equal(1, process.TerminatesOfTheRunningChild); // the session ends the child as soon as it exists
    }

    [Fact]
    [Trait("Decision", "ED-127")]
    public void A_stop_before_the_launch_began_does_not_end_a_child_the_session_then_makes()
    {
        using var runtime = new TestRuntime();
        using var scratch = new StorageScratchFolder();
        using var help = Help();
        var processes = new List<RecordingClaudeProcess>();
        using var transport = ClaudeProviderRuntime.Process.Override((_, args, _, env) => { var p = new RecordingClaudeProcess(args, env); processes.Add(p); return p; });
        var session = new ClaudeSession(Config(), () => null);

        runtime.OnMain(session.Stop);
        runtime.Loop.Run(() => session.Start(), TimeSpan.FromSeconds(5)); // as before: a Stop that found no child is not carried into a later launch
        Assert.False(Assert.Single(processes).TerminateRequested);
    }
}
