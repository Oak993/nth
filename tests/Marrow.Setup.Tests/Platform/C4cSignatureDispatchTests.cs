using System.Reflection;
using Marrow.Setup.Flow;
using Marrow.Setup.Platform;
using Marrow.Setup.Screens;
using Marrow.Setup.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Setup.Tests.Platform;

[Collection("SetupHeadless")]
public sealed class C4cSignatureDispatchTests(SetupHeadlessSession headless)
{
    [Fact]
    [Trait("Decision", "ED-247")]
    public void Real_window_signature_check_uses_the_tested_dispatch()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var source = File.ReadAllText(Path.Combine(root, "src", "Marrow.Setup", "Program.cs"));
        Assert.Contains("else _ = CheckSignatureAsync(session, done.Token);", source, StringComparison.Ordinal);
        var start = source.IndexOf("private static Task CheckSignatureAsync(", StringComparison.Ordinal);
        var end = source.IndexOf("private static async Task DispatchSignatureAsync(", StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var wrapper = source[start..end];
        Assert.Contains("DispatchSignatureAsync(session, SignatureCheck.VerifySelfAsync(), TimeProvider.System,", wrapper, StringComparison.Ordinal);
        Assert.Contains("action => Dispatcher.UIThread.Post(action), closed);", wrapper, StringComparison.Ordinal);
        var dispatch = source[end..]; // The extracted dispatch is the final method in Program.
        var answer = dispatch.IndexOf("var result = await SignatureCheck.WaitForAnswerAsync(check, clock, closed).ConfigureAwait(false);", StringComparison.Ordinal);
        var posts = System.Text.RegularExpressions.Regex.Matches(dispatch, @"\bpost\s*\(");
        Assert.Single(posts);
        Assert.True(answer >= 0 && answer < posts[0].Index);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Close_after_post_prevents_the_queued_signature_from_changing_the_session()
    {
        headless.OnUiThread(() =>
        {
            var rig = new WindowRig();
            var session = rig.Session(SetupScreen.Welcome);
            session.Start();
            using var closed = new CancellationTokenSource();
            var posted = new List<Action>();
            Dispatch(session, Task.FromResult(new SignatureResult(SignatureState.Signed, "Publisher", 0)), new ManualTimeProvider(), posted.Add, closed.Token).GetAwaiter().GetResult();
            var callback = Assert.Single(posted);
            closed.Cancel();
            callback();
            Assert.Equal(SignatureState.Unknown, session.Signature.State);
            Assert.False(session.CanChooseInstall);
            rig.Window.CloseNow();
        });
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Timeout_is_dispatched_as_unavailable_and_a_late_answer_posts_no_new_permission()
    {
        headless.OnUiThread(() =>
        {
            var rig = new WindowRig();
            var session = rig.Session(SetupScreen.Welcome);
            session.Start();
            var clock = new ManualTimeProvider();
            var pending = new TaskCompletionSource<SignatureResult>();
            var posted = new List<Action>();
            var dispatch = Dispatch(session, pending.Task, clock, posted.Add, default);
            Assert.Equal(TimeSpan.FromSeconds(30), Assert.Single(clock.TimerDeadlines));
            clock.Advance(TimeSpan.FromSeconds(30));
            dispatch.GetAwaiter().GetResult();
            Assert.Single(posted)();
            Assert.Equal(SetupScreen.Problem, session.Screen);
            Assert.Equal(Marrow.Setup.Text.SetupStrings.ProblemSignature, session.ProblemReason);
            pending.SetResult(new(SignatureState.Signed, "Publisher", 0));
            // Supplemental observation; the structural invariant above rules out a separate asynchronous late post.
            Assert.Single(posted);
            session.Go(SetupScreen.Welcome);
            Assert.False(session.CanChooseInstall);
            Assert.Equal(0, clock.ActiveTimers);
            rig.Window.CloseNow();
        });
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Close_before_post_and_fault_or_unknown_answers_use_the_same_production_dispatch()
    {
        headless.OnUiThread(() =>
        {
            foreach (var check in new[] { Task.FromException<SignatureResult>(new IOException("refused")), Task.FromResult(SignatureResult.Pending) })
            {
                var rig = new WindowRig();
                var session = rig.Session(SetupScreen.Update);
                session.Start();
                var posted = new List<Action>();
                Dispatch(session, check, new ManualTimeProvider(), posted.Add, default).GetAwaiter().GetResult();
                Assert.Single(posted)();
                Assert.Equal(SetupScreen.Problem, session.Screen);
                Assert.False(session.CanChooseInstall);
                using var closed = new CancellationTokenSource();
                closed.Cancel();
                posted.Clear();
                Dispatch(session, Task.FromResult(new SignatureResult(SignatureState.Signed, "Publisher", 0)), new ManualTimeProvider(), posted.Add, closed.Token).GetAwaiter().GetResult();
                Assert.Empty(posted);
                rig.Window.CloseNow();
            }
        });
    }

    private static Task Dispatch(SetupSession session, Task<SignatureResult> check, TimeProvider clock, Action<Action> post, CancellationToken closed)
    {
        var method = typeof(Program).GetMethod("DispatchSignatureAsync", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return (Task)method.Invoke(null, [session, check, clock, post, closed])!;
    }
}
