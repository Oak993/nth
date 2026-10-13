using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Services.Tests.Providers;

// PORT-ONLY (ED-NEW-gateprep-4): AntigravitySession.IsRunning was the child's own IsRunning. The end of a child's output reaches the session's reader a few milliseconds before the exit watcher sees the
// child go (measured 4 to 10 ms against the agy stand-in), so a turn that ended with the stream could be followed by "running" for that long, and a Send in the gap wrote to a child on its way out. A session
// whose output has closed is not running, whether or not the child has been seen to exit. The race itself cannot be placed (the child's pipe ends only with the child), so the test ends the stream by calling
// the private StreamEnded the reader calls, with the child still alive. The child is the compiled agy stand-in (R-TEST-8, R-TEST-10), never a real CLI.
public sealed class AntigravitySessionStreamEndedTests
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(60);
    private const string Init = "{\"event\":\"init\",\"conversation_id\":\"conv-1\"}";

    private static SwiftURL FileUrl(string path) => SwiftURL.Create("marrow-native-path:" + path.Replace('\\', '/').Replace(" ", "%20", StringComparison.Ordinal))!;

    private static SessionConfiguration Config(AntigravityScript script) => new()
    {
        Provider = ProviderKind.Antigravity,
        Executable = FileUrl(script.ExecutablePath),
        WorkingDirectory = FileUrl(Path.GetDirectoryName(script.ExecutablePath)!),
        Environment = new ValueMap<string, string>([script.EnvironmentEntry]),
        RuntimeMode = RuntimeMode.Supervised,
        InteractionMode = InteractionMode.Build,
    };

    private static TurnInput Turn(string text) => new()
    {
        Text = text,
        Images = ValueList<Attachment>.Empty,
        RuntimeMode = RuntimeMode.Supervised,
        InteractionMode = InteractionMode.Build,
    };

    private static T Field<T>(AntigravitySession session, string name) =>
        (T)typeof(AntigravitySession).GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(session)!;

    [Fact]
    [Trait("Decision", "ED-NEW-gateprep-4")]
    public void A_session_whose_output_has_closed_is_not_running_though_the_child_has_not_exited_and_a_send_says_so()
    {
        using var runtime = new TestRuntime();
        var script = new AntigravityScript("stream-ended");
        script.SetInit(Init);
        script.IgnoreEndOfInput();     // the child outlives its stdin and its stream's end: only a Terminate ends it
        var events = new List<ProviderEvent>();
        var session = new AntigravitySession(Config(script)) { OnEvent = events.Add };
        runtime.Loop.Run(() => session.Start(), Bound);
        var child = Field<StdioProcess>(session, "process");
        runtime.Loop.Run(() => session.Send(Turn("work")), Bound);
        Assert.True(session.IsRunning);
        events.Clear();

        runtime.OnMain(() => typeof(AntigravitySession).GetMethod("StreamEnded", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(session, [child]));

        Assert.True(child.IsRunning);     // the child is still there
        Assert.False(session.IsRunning);  // but its output has closed: nothing can answer
        var failed = Assert.IsType<ProviderEvent.TurnCompleted>(events[0]);
        Assert.Equal(TurnStatus.Failed, failed.Status);
        var error = Assert.Throws<ProviderError>(() => runtime.Loop.Run(() => session.Send(Turn("again")), Bound));
        Assert.IsType<ProviderError.Case.NotRunning>(error.ErrorCase);

        session.Stop();
        HeldLaunch.AssertEndedByTerminate(runtime, child);
    }
}
