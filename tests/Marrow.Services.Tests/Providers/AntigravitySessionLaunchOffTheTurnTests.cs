using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Services.Tests.Providers;

// PORT-ONLY (ED-NEW-perf-ui-thread-7): AntigravitySession.Start made the agy child with CreateProcessW on the caller's turn, which for a thread's first message is the UI thread. It now awaits
// StdioProcess.StartAsync. The held launch (HeldLaunch) shows the session wait for the child with nothing blocked; a Stop that comes inside that window ends the child once it exists; a launch
// that fails reaches the caller as the launcher's own error, as before. The children are the compiled agy stand-in (R-TEST-8, R-TEST-10), never a real CLI.
public sealed class AntigravitySessionLaunchOffTheTurnTests
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(60);
    private const string Init = "{\"event\":\"init\",\"conversation_id\":\"conv-1\"}";

    private static SwiftURL FileUrl(string path) => SwiftURL.Create("marrow-native-path:" + path.Replace('\\', '/').Replace(" ", "%20", StringComparison.Ordinal))!;

    private static string Scratch()
    {
        var root = TestDataDirectory.Read();
        if (string.IsNullOrEmpty(root))
            throw new InvalidOperationException("Set MARROW_PORT_DATA_DIR to a throwaway folder before running the tests (R-TEST-9).");
        var folder = Path.Combine(root, "AntigravityLaunchOffTheTurn", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }

    private static SessionConfiguration Config(AntigravityScript script) => new()
    {
        Provider = ProviderKind.Antigravity,
        Executable = FileUrl(script.ExecutablePath),
        WorkingDirectory = FileUrl(Path.GetDirectoryName(script.ExecutablePath)!),
        Environment = new ValueMap<string, string>([script.EnvironmentEntry]),
        RuntimeMode = RuntimeMode.Supervised,
        InteractionMode = InteractionMode.Build,
    };

    private static StdioProcess Process(AntigravitySession session) =>
        (StdioProcess)typeof(AntigravitySession).GetField("process", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(session)!;

    [Fact]
    [Trait("Decision", "ED-127")] // the launcher it keeps off the turn (ProcessLauncher.Run -> CreateProcessW); ED-NEW-perf-ui-thread-7
    public void The_session_waits_for_the_launch_instead_of_making_it_and_goes_on_when_the_child_exists()
    {
        using var runtime = new TestRuntime();
        using var held = new HeldLaunch(runtime.Loop);
        var script = new AntigravityScript("launch-held");
        script.SetInit(Init);
        var session = new AntigravitySession(Config(script));
        Task<string>? start = null;

        runtime.OnMain(() => start = session.Start());
        Assert.Equal(1, held.Requested);        // the session asked for the launch
        Assert.Equal(0, held.Launched);         // and did not make it itself
        Assert.False(start!.IsCompleted);       // it waits for the child with the loop idle: nothing is blocked
        Assert.False(session.IsRunning);        // no child exists yet

        held.Release();
        Assert.Equal("conv-1", runtime.Loop.Run(() => start, Bound));
        Assert.Equal(1, held.Launched);
        Assert.Equal(0, held.LaunchedOnTheLoop);
        Assert.True(session.IsRunning);
        session.Stop();
    }

    [Fact]
    [Trait("Decision", "ED-127")]
    public void A_stop_that_comes_while_the_child_is_being_created_ends_it_once_it_exists()
    {
        using var runtime = new TestRuntime();
        using var held = new HeldLaunch(runtime.Loop);
        var script = new AntigravityScript("launch-stop");
        script.IgnoreEndOfInput(); // the Stop closes the child's stdin before it exists; this one then lives on, so only a Terminate can end it
        var events = new List<ProviderEvent>();
        var session = new AntigravitySession(Config(script)) { OnEvent = events.Add };
        Task<string>? start = null;

        runtime.OnMain(() => start = session.Start());
        var child = Process(session);
        runtime.OnMain(session.Stop);           // the child does not exist yet: this Stop ends nothing
        Assert.False(child.IsRunning);
        Assert.Equal(0, held.Launched);

        held.Release();
        runtime.Loop.RunUntil(() => held.Launched == 1, Bound);
        Assert.True(child.ProcessIdentifier > 0);
        HeldLaunch.AssertEndedByTerminate(runtime, child); // the stand-in would idle for 20 s: it is the session's Terminate that ends it
        var error = Assert.Throws<ProviderError>(() => runtime.Loop.Run(() => start!, Bound)); // the start fails as it does for a Stop while it waits for the init line
        Assert.IsType<ProviderError.Case.Failed>(error.ErrorCase);
        Assert.DoesNotContain(events, e => e is ProviderEvent.Exited); // a stopped session reports no exit
        Assert.False(session.IsRunning);
    }

    [Fact]
    [Trait("Decision", "ED-127")]
    public void A_launch_that_cannot_start_fails_the_start_with_the_launchers_own_error()
    {
        using var runtime = new TestRuntime();
        var folder = Scratch();
        var missing = Path.Combine(folder, "missing-agy.exe");
        var configuration = new SessionConfiguration
        {
            Provider = ProviderKind.Antigravity,
            Executable = FileUrl(missing),
            WorkingDirectory = FileUrl(folder),
            Environment = ValueMap<string, string>.Empty,
            RuntimeMode = RuntimeMode.Supervised,
            InteractionMode = InteractionMode.Build,
        };
        // Ask the same launch directly what it says: the session adds nothing and maps nothing.
        var direct = Assert.ThrowsAny<Exception>(() => new StdioProcess(missing, [], folder, new Dictionary<string, string>()).Start());
        var events = new List<ProviderEvent>();
        var session = new AntigravitySession(configuration) { OnEvent = events.Add };

        var error = Assert.ThrowsAny<Exception>(() => runtime.Loop.Run(() => session.Start(), Bound));

        Assert.Equal(direct.GetType(), error.GetType());
        Assert.Equal(direct.Message, error.Message);
        Assert.False(session.IsRunning);
        Assert.Empty(events);
    }
}
