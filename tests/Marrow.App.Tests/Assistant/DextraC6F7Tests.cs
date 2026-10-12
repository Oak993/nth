using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Providers;
using System.Reflection;

namespace Marrow.App.Tests.Assistant;

// Regression for the accepted Dextra C6-F7 finding. Offline only.
public sealed class DextraC6F7Tests
{
    [Fact]
    [Trait("Decision", "ED-NEW-dextra-cx-7")]
    public void Saving_before_quit_disposes_the_assistant_bus()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("P", h.NewFolder("c6-quit"));
        h.WriteLibrary([project], []);
        var model = h.MakeModel();
        using var bus = model.AssistantEvents;
        using var subscription = bus.Subscribe(_ => { });
        h.Main.OnMain(model.SaveBeforeQuit);
        Assert.False(bus.HasSubscribers);
        Assert.Throws<ObjectDisposedException>(() => bus.Subscribe(_ => { }));
    }
    [Fact]
    [Trait("Decision", "ED-NEW-dextra-cx-7")]
    public void Quit_retires_existing_taps_and_their_watcher_and_late_taps_stay_inert()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("P", h.NewFolder("c6-taps-quit"));
        var thread = AppModelSamples.Thread(project);
        h.WriteLibrary([project], [thread]);
        var model = h.MakeModel();
        using var subscription = model.AssistantEvents.Subscribe(_ => { });
        h.Main.OnMain(() =>
        {
            var taps = model.AssistantTaps;
            taps.IsPrivateAccount = _ => true;
            var watcher = (AccountWindowWatcher)typeof(Marrow.App.Assistant.AssistantFeedTaps)
                .GetField("accounts", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(taps)!;
            watcher.Observe([new("a", "A", "short", "short", 10, model.AssistantEvents.Now.AddHours(1))], model.AssistantEvents.Now);
            model.SaveBeforeQuit();
            model.SaveBeforeQuit();
            Assert.Null(taps.IsPrivateAccount);
            Assert.Throws<ObjectDisposedException>(() => watcher.Observe([], model.AssistantEvents.Now));
            Assert.Throws<ObjectDisposedException>(() => watcher.NoteLimit("a", "A", null, model.AssistantEvents.Now));
            taps.TapProvider(thread.Id, new ProviderEvent.Notice(new Notice { NoticeLevel = Notice.Level.Error, Message = "late" }));
            model.AssistantTaps.TapNeedsAttention(thread.Id);
            Assert.Equal(0, model.AssistantEvents.Pending);
        });
    }

    [Fact]
    [Trait("Decision", "ED-NEW-dextra-cx-7")]
    public void Taps_first_requested_after_quit_are_already_retired()
    {
        using var h = new AppModelHarness();
        h.WriteLibrary([], []);
        var model = h.MakeModel();
        h.Main.OnMain(() =>
        {
            model.SaveBeforeQuit();
            var taps = model.AssistantTaps;
            var watcher = (AccountWindowWatcher)typeof(Marrow.App.Assistant.AssistantFeedTaps)
                .GetField("accounts", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(taps)!;
            Assert.Throws<ObjectDisposedException>(() => watcher.Observe([], model.AssistantEvents.Now));
        });
    }

}
