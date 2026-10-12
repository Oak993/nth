using Marrow.App.Port;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Store;
using Xunit;

namespace Marrow.App.Tests.Port;

[Collection("MCPHub processes")]
public sealed class C7InstallerHydraRetryReviewTests
{
    // ED-NEW-inst-c7-5: reserve the actual staggered retry BEFORE its actor post, not after its sleep.
    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_captured_hydra_launch_retry_is_busy_before_its_delayed_actor_post()
    {
        using var shell = new C7OfflineShell();
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("P", h.NewFolder("retry"));
        var lead = AppModelSamples.Thread(project);
        var head = AppModelSamples.Thread(project) with
        {
            ParentThreadID = lead.Id, IsInPanel = true,
            Hydra = AppModelSamples.Head(HydraHeadInfo.Status.Running) with { Index = 0 },
        };
        var turn = new TurnRecord { Index = 0, Status = TurnStatus.Failed, CompletedAt = SwiftDate.Now };
        h.WriteLibrary([project], [lead, head]);
        DiskWriter.Shared.WriteSynchronously(new ThreadDocument(head.Id)
        {
            Items = new([
                new TimelineItem(null, new TimelineItem.Content.User(new UserMessage { Text = "/plan", HydraHeads = new([0]) })),
                new TimelineItem(turn.Id, new TimelineItem.Content.Notice(new Notice { NoticeLevel = Notice.Level.Error, Message = "database is locked" })),
            ]),
            Turns = new([turn]),
        }, Storage.ThreadURL(head.Id));
        var model = h.MakeModel();
        h.PrepareBootstrap(model);
        var runtime = h.Main.OnMain(() => model.Runtime(head.Id));
        h.Main.Loop.Run(() => runtime.EnsureLoaded(), AppModelHarness.Bound);
        h.Main.OnMain(() =>
        {
            model.Settings.HydraAutoClearFinished = false;
            model.UpdateThread(head.Id, value => value with { Hydra = value.Hydra! with { HydraHeadInfoStatus = HydraHeadInfo.Status.Running } });
            var draft = new ComposerDraft { Text = "Unsent head edit" };
            runtime.Draft = draft;
            using var captured = new CapturedInstallerExecutor(MainActor.Executor.Current);
            using var scope = MainActor.Executor.Override(captured);
            model.HydraHeadTurnFinished(model.Thread(head.Id)!, TurnStatus.Failed);
            Assert.Equal(1L, model.HydraHeadRetries[head.Id]);
            Assert.Equal(1, captured.QueuedCount);
            var recorder = new ShellRecorder();
            var host = new NthControlHost(new AppShell(new AppDelegate { Model = model }, recorder.Exit, recorder.AfterWillTerminate),
                processId: 4242, executable: @"C:\Apps\nth\current\nth.exe");

            Assert.Equal(NthControlReply.Busy, host.Answer(NthControlRequest.QuitIfIdle).Response.Reply);

            Assert.Same(draft, runtime.Draft);
            Assert.False(runtime.IsRunning);
            Assert.Empty(recorder.Calls);
        });
    }
}
