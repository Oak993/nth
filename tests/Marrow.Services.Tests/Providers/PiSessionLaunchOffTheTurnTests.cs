using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Services.Tests.Providers;

// PORT-ONLY (ED-NEW-perf-ui-thread-7): PiSession.Start made the pi child with CreateProcessW on the caller's turn. It now awaits StdioProcess.StartAsync. The held launch (HeldLaunch) shows the
// session wait for the child with nothing blocked and nothing read from it; a Stop that comes inside that window ends the child once it exists, announces nothing and leaves the session not
// running; a launch that fails keeps the "Pi could not start: " message built from the launcher's own error. The children are the compiled pi stand-in (R-TEST-8, R-TEST-10), never a real CLI.
public sealed class PiSessionLaunchOffTheTurnTests
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(60);

    private static SwiftURL FileUrl(string windowsPath) => SwiftURL.Create("marrow-native-path:" + windowsPath.Replace('\\', '/'))!;

    private static (PiSession Session, List<ProviderEvent> Events) Make(PiScript script)
    {
        var folder = Path.GetDirectoryName(script.ExecutablePath)!;
        var configuration = new SessionConfiguration
        {
            Provider = ProviderKind.Pi,
            Executable = FileUrl(script.ExecutablePath),
            WorkingDirectory = FileUrl(folder),
            Environment = new ValueMap<string, string>([script.EnvironmentEntry]),
            RuntimeMode = RuntimeMode.Supervised,
            InteractionMode = InteractionMode.Build,
        };
        var events = new List<ProviderEvent>();
        return (new PiSession(configuration, Path.Combine(folder, "local-app-data")) { OnEvent = events.Add }, events);
    }

    private static void Handshake(PiScript script)
    {
        script.Reply(0, "{\"type\":\"response\",\"id\":\"droppy-1\",\"data\":{\"sessionFile\":\"session-1.json\",\"model\":{\"contextWindow\":128000}}}");
        script.Reply(1, "{\"type\":\"response\",\"id\":\"droppy-2\",\"data\":{\"commands\":[]}}");
    }

    private static object? Field(PiSession session, string name) =>
        typeof(PiSession).GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(session);

    [Fact]
    [Trait("Decision", "ED-127")] // the launcher it keeps off the turn (ProcessLauncher.Run -> CreateProcessW); ED-NEW-perf-ui-thread-7
    public void The_session_waits_for_the_launch_instead_of_making_it_and_goes_on_when_the_child_exists()
    {
        using var runtime = new TestRuntime();
        using var storage = new StorageScratchFolder(); // MCPPaths.ClaudeConfigURL reads Storage.Root (R-TEST-9)
        using var held = new HeldLaunch(runtime.Loop);
        var script = new PiScript("launch-held");
        Handshake(script);
        var (session, _) = Make(script);
        Task<string>? start = null;

        runtime.OnMain(() => start = session.Start());
        Assert.Equal(1, held.Requested);        // the session asked for the launch
        Assert.Equal(0, held.Launched);         // and did not make it itself
        Assert.False(start!.IsCompleted);       // it waits for the child with the loop idle: nothing is blocked
        Assert.Null(Field(session, "process")); // and reads from no child yet
        Assert.Null(script.Arguments());        // none was run

        held.Release();
        Assert.Equal("session-1.json", runtime.Loop.Run(() => start, Bound));
        Assert.Equal(1, held.Launched);
        Assert.Equal(0, held.LaunchedOnTheLoop);
        Assert.True(session.IsRunning);
        session.Stop();
    }

    [Fact]
    [Trait("Decision", "ED-127")]
    public void A_stop_that_comes_while_the_child_is_being_created_ends_it_once_it_exists_and_the_session_announces_nothing()
    {
        using var runtime = new TestRuntime();
        using var storage = new StorageScratchFolder();
        using var held = new HeldLaunch(runtime.Loop);
        var script = new PiScript("launch-stop");
        script.IgnoreEndOfInput(); // the Stop closes the child's stdin before it exists; this one then lives on, so only a Terminate can end it
        var (session, events) = Make(script);
        Task<string>? start = null;

        runtime.OnMain(() => start = session.Start());
        var child = held.Child();
        runtime.OnMain(session.Stop);           // the child does not exist yet: this Stop ends nothing
        Assert.False(child.IsRunning);
        Assert.Equal(0, held.Launched);

        held.Release();
        Assert.Equal("", runtime.Loop.Run(() => start!, Bound)); // the start ends without an error and without a session id
        Assert.Equal(1, held.Launched);
        Assert.True(child.ProcessIdentifier > 0);
        HeldLaunch.AssertEndedByTerminate(runtime, child); // the stand-in would run for 60 s: it is the session's Terminate that ends it
        Assert.Empty(events);                   // no SessionReady after a Stop
        Assert.False(session.IsRunning);
        Assert.Null(Field(session, "process"));
    }

    [Fact]
    [Trait("Decision", "ED-127")]
    public void A_launch_that_cannot_start_keeps_the_could_not_start_message_built_from_the_launchers_own_error()
    {
        using var runtime = new TestRuntime();
        using var storage = new StorageScratchFolder();
        var script = new PiScript("launch-fails");
        var events = new List<ProviderEvent>();
        var configuration = new SessionConfiguration
        {
            Provider = ProviderKind.Pi,
            Executable = FileUrl(Path.Combine(Path.GetDirectoryName(script.ExecutablePath)!, "missing-pi.exe")),
            WorkingDirectory = FileUrl(Path.GetDirectoryName(script.ExecutablePath)!),
            Environment = ValueMap<string, string>.Empty,
            RuntimeMode = RuntimeMode.Supervised,
            InteractionMode = InteractionMode.Build,
        };
        var session = new PiSession(configuration, Path.Combine(Path.GetDirectoryName(script.ExecutablePath)!, "local-app-data")) { OnEvent = events.Add };
        // Ask the same launch directly what it says.
        var direct = Assert.ThrowsAny<Exception>(() => new StdioProcess(configuration.Executable!.Path, [], configuration.WorkingDirectory.Path, new Dictionary<string, string>()).Start());

        var error = Assert.Throws<ProviderError>(() => runtime.Loop.Run(() => session.Start(), Bound));

        Assert.Equal(new ProviderError.Case.Failed("Pi could not start: " + direct.Message), error.ErrorCase);
        Assert.False(session.IsRunning);
        Assert.Empty(events);
    }
}
