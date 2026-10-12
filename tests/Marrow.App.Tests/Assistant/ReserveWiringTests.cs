using Marrow.App.Assistant;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;
using Marrow.Services.Port.Usage;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.App.Tests.Assistant;

// Dextra AS-13 (DESIGN.md rev 5, 11.2, 11.5, 15.3; ED-NEW-dextra-as13-7 and -9): the reserve as the app holds it. StartDextra reads
// the owner's rules and what nth learned from the store, the one-line taps feed the guard whether or not anyone watches the feed
// (the plan-limit polls, the ledger's token spend, the turns of mission chats), and the controller writes the learning back when
// it lets go.
[Trait("Decision", "ED-NEW-dextra-as13-9")]
[Trait("Feature", "AS-13")]
public sealed class ReserveWiringTests
{
    private static PlanLimits Limits(double percent, DateTimeOffset resets) => new()
    {
        Windows = new ValueList<PlanLimits.Window>([
            new PlanLimits.Window { Id = "five_hour", Title = "5-hour limit", Percent = percent, DurationMinutes = 300, ResetsAt = resets },
        ]),
    };

    [Fact]
    public void Start_dextra_binds_a_reserve_that_reads_the_stored_rules_the_polls_and_the_ledger()
    {
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Claude);
        using var temp = new AssistantTempRoot();
        using var store = AssistantStore.Open(temp.Roots);
        var rules = new ReserveSettings();
        Assert.Null(rules.Add(new ReserveRule("", ReserveScope.Provider("claude"), ReserveLifetime.Standing) { KeepPercent = 30 }));
        store.SaveReserves(rules.ToJson()!);

        var controller = rig.H.Main.OnMain(() => rig.App.StartDextra(store, _ => { }, () => []));
        try
        {
            var reserve = Assert.IsType<ReserveGuard>(controller.Reserve);
            Assert.Same(reserve, rig.App.Reserve);
            Assert.Same(reserve, controller.Runner.Reserve);
            Assert.Empty(reserve.Bars());

            // A poll reaches the guard through the tap even though nobody listens to the feed.
            var now = DateTimeOffset.UtcNow;
            rig.H.Main.OnMain(() => rig.App.AssistantTaps.TapAccountSnapshot(
                new AccountPlanLimitsSnapshot(UsageAccountKey.Default(ProviderKind.Claude), Limits(60, now.AddHours(3)), now.AddSeconds(-30))));
            var bar = Assert.Single(reserve.Bars());
            Assert.Equal("Claude", bar.Name);
            var window = Assert.Single(bar.Windows);
            Assert.Equal(60, window.Used);
            Assert.Equal(70.0, window.Floor!.Value, 6);                // the stored rule: keep 30% of every Claude account

            // The ledger's spend moves the estimate between polls, for a chat that belongs to no mission as well.
            // Nothing is calibrated yet, so a token costs the conservative default: 150,000 tokens to the point.
            rig.H.Main.OnMain(() => rig.App.AssistantTaps.TapProvider(rig.InitialThread.Id,
                new ProviderEvent.TokenSpend(new TokenSpend(ProviderKind.Claude, 500_000, model: "offline-model"))));
            Assert.Equal(64, Assert.Single(Assert.Single(reserve.Bars()).Windows).Used);
        }
        finally
        {
            rig.H.Main.OnMain(rig.App.UnbindDextra);
            controller.Dispose();
        }
        Assert.Null(rig.App.Reserve);
    }

    [Fact]
    public void A_mission_chat_s_turn_is_counted_when_it_ends_and_the_controller_writes_what_it_learned_back_to_the_store()
    {
        var mission = Guid.NewGuid();
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Claude, thread => thread with { MissionID = mission });
        using var temp = new AssistantTempRoot();
        using var store = AssistantStore.Open(temp.Roots);
        var controller = rig.H.Main.OnMain(() => rig.App.StartDextra(store, _ => { }, () => []));
        try
        {
            var thread = rig.InitialThread.Id;
            rig.H.Main.OnMain(() =>
            {
                rig.App.AssistantTaps.TapProvider(thread, new ProviderEvent.TurnStarted("turn-1"));
                rig.App.AssistantTaps.TapProvider(thread, new ProviderEvent.TokenSpend(new TokenSpend(ProviderKind.Claude, 100_000, model: "offline-model")));
                rig.App.AssistantTaps.TapProvider(thread, new ProviderEvent.TurnCompleted(TurnStatus.Completed, null));
            });
            Assert.Null(store.Calibration());                        // written at most every 30 seconds ...
        }
        finally
        {
            rig.H.Main.OnMain(rig.App.UnbindDextra);
            controller.Dispose();                                    // ... and when the controller lets go
        }
        var document = store.Calibration();
        Assert.NotNull(document);
        var turns = Assert.IsType<System.Text.Json.Nodes.JsonArray>(document["turns"]);
        var turn = Assert.Single(turns);
        Assert.Equal("offline-model", turn!["model"]!.GetValue<string>());
        Assert.Equal(100_000, turn["tokens"]!.GetValue<double>());
        Assert.Equal(1, document["version"]!.GetValue<int>());
    }

    [Fact]
    public void Without_dextra_the_taps_leave_the_reserve_alone_and_a_tap_that_throws_does_not_break_the_app()
    {
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Claude);
        Assert.Null(rig.App.Reserve);
        rig.H.Main.OnMain(() =>
        {
            rig.App.AssistantTaps.TapAccountSnapshot(new AccountPlanLimitsSnapshot(UsageAccountKey.Default(ProviderKind.Claude), Limits(10, DateTimeOffset.UtcNow.AddHours(1)), DateTimeOffset.UtcNow));
            rig.App.AssistantTaps.TapProvider(rig.InitialThread.Id, new ProviderEvent.TokenSpend(new TokenSpend(ProviderKind.Claude, 100)));
            rig.App.AssistantTaps.TapProvider(Guid.NewGuid(), new ProviderEvent.TurnCompleted(TurnStatus.Completed, null));
        });
    }
}
