using System.Reflection;
using Marrow.App.Assistant;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Port.Usage;
using Marrow.Services.Providers;

namespace Marrow.App.Tests.Assistant;

public sealed class DextraC6cD5Tests
{
    [Fact, Trait("Decision", "ED-NEW-dextra-c6c-5")]
    public Task Disposed_Provider_tap_stays_inert_on_a_live_subscribed_bus() => CheckTap(0);

    [Fact, Trait("Decision", "ED-NEW-dextra-c6c-5")]
    public Task Disposed_TurnFinished_tap_stays_inert_on_a_live_subscribed_bus() => CheckTap(1);

    [Fact, Trait("Decision", "ED-NEW-dextra-c6c-5")]
    public Task Disposed_NeedsAttention_tap_stays_inert_on_a_live_subscribed_bus() => CheckTap(2);

    [Fact, Trait("Decision", "ED-NEW-dextra-c6c-5")]
    public Task Disposed_HeadFinished_tap_stays_inert_on_a_live_subscribed_bus() => CheckTap(3);

    [Fact, Trait("Decision", "ED-NEW-dextra-c6c-5")]
    public Task Disposed_AccountSnapshot_tap_stays_inert_on_a_live_subscribed_bus() => CheckTap(4);

    [Fact, Trait("Decision", "ED-NEW-dextra-c6c-5")]
    public Task Disposed_AccountLimit_tap_stays_inert_on_a_live_subscribed_bus() => CheckTap(5);

    private static async Task CheckTap(int kind)
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("P", h.NewFolder("c6c-tap"));
        var thread = AppModelSamples.Thread(project);
        h.WriteLibrary([project], [thread]);
        var model = h.MakeModel();
        var clock = new C6cTapTimeProvider(h.Main.Loop.CheckAccess);
        using var bus = new AssistantEventBus(clock) { Brain = new FeedBrain(ProviderKind.Claude) };
        using var taps = new AssistantFeedTaps(model, bus);
        using var feed = new FeedCollector(bus);
        var key = UsageAccountKey.Default(ProviderKind.Claude);
        var info = new HydraHeadInfo(0, "task", HydraHeadInfo.Kind.Native, HydraHeadInfo.Origin.Delegated);
        void Tap()
        {
            switch (kind)
            {
                case 0: taps.TapProvider(thread.Id, new ProviderEvent.Notice(new Notice { NoticeLevel = Notice.Level.Error, Message = "diagnostic" })); break;
                case 1: taps.TapTurnFinished(thread.Id, TurnStatus.Completed, false); break;
                case 2: taps.TapNeedsAttention(thread.Id); break;
                case 3: taps.TapHeadFinished(thread.Id, Guid.NewGuid(), info, HydraHeadInfo.Status.Completed, "done"); break;
                case 4: taps.TapAccountSnapshot(new AccountPlanLimitsSnapshot(key,
                    new PlanLimits { Windows = new ValueList<PlanLimits.Window>([new PlanLimits.Window { Id = "short", Title = "short", Percent = 100 }]) },
                    new DateTimeOffset(2026, 10, 12, 9, 0, 0, TimeSpan.Zero))); break;
                case 5: taps.TapAccountLimit(null!, key, null); break; // Explicit account avoids any runtime/provider seam.
                default: throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }
        h.Main.OnMain(() =>
        {
            var before = clock.TapReads;
            Tap();
            Assert.True(clock.TapReads > before, "The positive control did not reach the tap body.");
        });
        await bus.FlushAsync().WaitAsync(TimeSpan.FromSeconds(10));
        var expected = new[] { FeedEventKind.Error, FeedEventKind.TurnFinished, FeedEventKind.NeedsAttention,
            FeedEventKind.HeadFinished, FeedEventKind.AccountLimitReached, FeedEventKind.AccountLimitReached };
        Assert.Equal(expected[kind], Assert.Single(feed.Lines).Kind);
        h.Main.OnMain(() =>
        {
            taps.Dispose(); // Retire ONLY the taps; HasSubscribers remains true.
            Assert.True(bus.HasSubscribers);
            var before = clock.TapReads;
            Tap();
            Assert.Equal(before, clock.TapReads); // Account watcher faults are swallowed, so output alone is insufficient.
        });
        await bus.FlushAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Single(feed.Lines);
    }

    [Fact, Trait("Decision", "ED-NEW-dextra-c6c-5")]
    public void First_quit_with_persistence_disabled_still_retires_bus_taps_and_watcher()
    {
        using var h = new AppModelHarness();
        h.WriteLibrary([], []);
        var model = h.MakeModel();
        using var bus = model.AssistantEvents;
        using var subscription = bus.Subscribe(_ => { });
        h.Main.OnMain(() =>
        {
            var taps = model.AssistantTaps;
            taps.IsPrivateAccount = _ => true;
            var watcher = (AccountWindowWatcher)typeof(AssistantFeedTaps)
                .GetField("accounts", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(taps)!;
            typeof(AppModel).GetField("persistenceEnabled", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(model, false);
            model.SaveBeforeQuit(); // First call: previous shutdown cannot make this pass vacuously.
            Assert.False(bus.HasSubscribers);
            Assert.Throws<ObjectDisposedException>(() => bus.Subscribe(_ => { }));
            Assert.Null(taps.IsPrivateAccount);
            Assert.Throws<ObjectDisposedException>(() => watcher.Observe([], DateTimeOffset.UtcNow));
            Assert.Throws<ObjectDisposedException>(() => watcher.NoteLimit("a", "A", null, DateTimeOffset.UtcNow));
        });
    }
}
