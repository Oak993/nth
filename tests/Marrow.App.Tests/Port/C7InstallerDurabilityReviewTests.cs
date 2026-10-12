using Marrow.App.Port;
using Marrow.App.Port.Skills;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Port.Skills.Generations;
using Marrow.Services.Port.MultiAccount;
using Marrow.Services.Providers;
using Marrow.Services.Store;
using Xunit;

namespace Marrow.App.Tests.Port;

[Collection("MCPHub processes")]
public sealed class C7InstallerDurabilityReviewTests
{
    // ED-NEW-inst-c7-5: a sent closure has no persisted UserItem until its posted body can run.
    [Fact]
    [Trait("Decision", "ED-247")]
    public void Stop_refuses_a_captured_prompt_that_has_not_reached_the_saved_turn()
    {
        using var shell = new C7OfflineShell();
        using var rig = new NthControlRig("Captured exact prompt");
        rig.H.Main.OnMain(() =>
        {
            using var captured = new CapturedInstallerExecutor(MainActor.Executor.Current);
            using var scope = MainActor.Executor.Override(captured);
            var runtime = rig.Runtimes[0];
            runtime.Draft = new ComposerDraft { Text = "Keep this captured prompt" };
            runtime.Send();
            Assert.Equal(1, captured.QueuedCount);
            Assert.Empty(runtime.Turns);
            Assert.True(runtime.Draft.IsEmpty);

            var answer = rig.Host.Answer(NthControlRequest.QuitAndStop);

            Assert.Equal(NthControlReply.Busy, answer.Response.Reply);
            Assert.Equal("Captured exact prompt", Assert.Single(answer.Response.Work).Name);
            Assert.Empty(rig.Recorder.Calls);
        });
    }

    // Existing warmup task pauses actual StartTurn after its exact persisted payload, without a new production seam.
    [Fact]
    [Trait("Decision", "ED-247")]
    public void Stop_saves_a_durable_prompt_and_prevents_late_preparation_from_launching_a_provider()
    {
        using var shell = new C7OfflineShell();
        using var skills = ProviderSkills.Prepare.Override((_, _, _, _) => Task.FromResult<Generation?>(null));
        using var reader = MultiAccountRouting.LimitsReader.Override((_, _, _) => Task.FromResult<PlanLimits?>(null));
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Deepseek, install: false,
            configure: thread => thread with { HasCustomTitle = true },
            settings: app => app.Settings.DeepseekAPIKeyInput = "offline-source");
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sessions = new List<ThreadRuntimeScriptedSession>();
        using var launch = AccountSessionFactory.Launcher.Override((_, configuration, _, _) =>
        {
            var scripted = new ThreadRuntimeScriptedSession();
            sessions.Add(scripted);
            return new(configuration, scripted);
        });
        TaskHandle warmup = null!;
        Task? pending = null;
        try
        {
            rig.H.Main.OnMain(() =>
            {
                warmup = MainActor.Spawn(async () => await release.Task);
                ThreadRuntimeReflection.Set(rig.Runtime, "warmup", warmup);
            });
            var attachment = new Attachment { Name = "offline.txt", Path = Path.Combine(rig.H.Scratch, "offline.txt"), MimeType = "text/plain" };
            pending = rig.StartTurn("Keep this durable exact prompt", [attachment], [7]);
            rig.H.Main.OnMain(() =>
            {
                Assert.Equal(RuntimePhase.Starting, rig.Runtime.Phase);
                Assert.False(pending.IsCompleted);
                Assert.Empty(sessions);
                var recorder = new ShellRecorder();
                var control = new NthControlHost(new AppShell(new AppDelegate { Model = rig.App },
                    () => { recorder.Exit(); rig.App.SaveBeforeQuit(); }, recorder.AfterWillTerminate),
                    processId: 4242, executable: @"C:\Apps\nth\current\nth.exe");
                var answer = control.Answer(NthControlRequest.QuitAndStop);
                Assert.Equal(NthControlReply.Ok, answer.Response.Reply);
                // ED-NEW-inst-c7-5: exercise both actual ACK stages before temp SaveBeforeQuit.
                Assert.True(answer.Response.CommitRequired);
                var prepared = answer.PrepareCommit!(CancellationToken.None).GetAwaiter().GetResult();
                Assert.Equal(NthControlReply.Ok, prepared.Response.Reply);
                Assert.Null(prepared.Commit!(CancellationToken.None).GetAwaiter().GetResult());
            });
            rig.H.Main.Loop.RunUntilIdle();
            rig.H.Main.OnMain(() =>
            {
                var saved = Assert.IsType<ThreadDocument>(Storage.DecodeDocument(rig.InitialThread.Id));
                Assert.Single(saved.Turns);
                var user = Assert.IsType<TimelineItem.Content.User>(Assert.Single(saved.Items).TimelineItemContent).Value;
                Assert.Equal("Keep this durable exact prompt", user.Text);
                Assert.Equal(attachment, Assert.Single(user.Attachments));
                Assert.Equal(7L, Assert.Single(user.HydraHeads!));
            });
        }
        finally
        {
            release.TrySetResult();
            if (pending is not null) rig.Finish(pending);
            if (warmup is not null) rig.H.Main.Loop.Run(() => warmup.Completion, AppModelHarness.Bound);
        }
        Assert.Empty(sessions);
    }
}
