using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Providers;
using System.Reflection;

namespace Marrow.App.Tests.Assistant;

// Regression for the accepted Dextra C6-F3 finding. Offline only.
public sealed class DextraC6F3Tests
{
    [Fact]
    [Trait("Decision", "ED-NEW-dextra-cx-3")]
    public async Task Hiding_a_parent_redacts_its_heads_already_queued_words()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("P", h.NewFolder("c6-queued"));
        var parent = AppModelSamples.Thread(project);
        var head = AppModelSamples.Thread(project) with { ParentThreadID = parent.Id };
        h.WriteLibrary([project], [parent, head]);
        var model = h.MakeModel();
        using var bus = model.AssistantEvents;
        bus.Brain = new FeedBrain(head.Provider);
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var feed = new FeedCollector(bus) { Hook = _ => { entered.TrySetResult(); release.Wait(TimeSpan.FromSeconds(10)); } };
        bus.Publish(FeedDraft.About(FeedEventKind.MissionState, FeedSeverity.Info, bus.Now, "primer"));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            h.Main.OnMain(() =>
            {
                model.AssistantTaps.TapProvider(head.Id, new ProviderEvent.Notice(new Notice { NoticeLevel = Notice.Level.Error, Message = "PRIVATE HEAD CONTENT" }));
                model.UpdateThread(parent.Id, t => t with { HiddenFromAssistant = true });
                bus.Hide(parent.Id);
                Assert.True(FeedChatFacts.From(model.Thread(head.Id)!, model.Thread).Hidden);
            });
        }
        finally { release.Set(); }
        await bus.FlushAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.DoesNotContain(feed.Lines, line => line.Summary.Contains("PRIVATE HEAD CONTENT", StringComparison.Ordinal));
    }
}
