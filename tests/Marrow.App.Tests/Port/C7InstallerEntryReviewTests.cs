using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Xunit;

namespace Marrow.App.Tests.Port;

[Collection("MCPHub processes")]
public sealed class C7InstallerEntryReviewTests
{
    // ED-NEW-inst-c7-5: reserve the real Send intent before its actor post can run.
    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_real_send_captured_before_its_actor_post_is_busy_for_an_idle_quit()
    {
        using var rig = new NthControlRig("Captured typed prompt");
        rig.H.Main.OnMain(() =>
        {
            using var captured = new CapturedInstallerExecutor(MainActor.Executor.Current);
            using var executorScope = MainActor.Executor.Override(captured);
            var runtime = rig.Runtimes[0];
            runtime.Draft = new ComposerDraft { Text = "Captured prompt" };
            runtime.Send();
            Assert.Equal(1, captured.QueuedCount);
            Assert.False(runtime.IsRunning); // No turn/provider body was allowed to execute.

            var answer = rig.Host.Answer(NthControlRequest.QuitIfIdle);

            Assert.Equal(NthControlReply.Busy, answer.Response.Reply);
            Assert.Equal("Captured typed prompt", Assert.Single(answer.Response.Work).Name);
            Assert.Empty(rig.Recorder.Calls);
        }); // Both the ambient executor and its unstarted posts are disposed even when an assertion fails.
    }

    // ED-NEW-inst-c7-5: use a provider-free local command to check the actual Send draft/mode mutations.
    [Fact]
    [Trait("Decision", "ED-247")]
    public void An_idle_quit_preserves_the_actual_send_draft_and_local_command_mode()
    {
        using var rig = new NthControlRig("Keep my draft");
        rig.H.Main.OnMain(() =>
        {
            var runtime = rig.Runtimes[0];
            var draft = new ComposerDraft { Text = "/plan" };
            runtime.Draft = draft;
            var mode = runtime.Thread!.InteractionMode;
            var answer = rig.Host.Answer(NthControlRequest.QuitIfIdle);
            Assert.Equal(NthControlReply.Ok, answer.Response.Reply);

            runtime.Send(); // /plan toggles a local field; it starts no provider, actor task or process.

            Assert.Same(draft, runtime.Draft);
            Assert.Equal(mode, runtime.Thread!.InteractionMode);
            Assert.False(runtime.IsRunning);
            Assert.Empty(rig.Recorder.Calls);
        });
    }
}
