using Marrow.App.Assistant;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Accounts;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.App.Tests.Assistant;

// Dextra AS-14 (DESIGN.md rev 5, 11.6, 15.4; ED-NEW-dextra-as14-8): the router as the app holds it. StartDextra gives the controller a
// router over the owner's connected accounts, in his pill order, and a mission the router placed is held to the floor of the account it
// was placed on, not of the provider's first account.
[Trait("Decision", "ED-NEW-dextra-as14-8")]
[Trait("Feature", "AS-14")]
public sealed class AssistantRouterWiringTests
{
    private static (Guid Legacy, Guid Second, Guid Codex) Accounts(ThreadRuntimeTurnRig rig)
    {
        return rig.H.Main.OnMain(() =>
        {
            var accounts = rig.App.Settings.Accounts;
            var legacy = accounts.ForService(ProviderKind.Claude)[0].Id;
            accounts.MarkSignedIn(legacy);
            var second = accounts.Add(ProviderKind.Claude).Id;
            accounts.MarkSignedIn(second);
            var codex = accounts.Add(ProviderKind.Codex).Id;
            accounts.MarkSignedIn(codex);
            return (legacy, second, codex);
        });
    }

    [Fact]
    public void Start_dextra_binds_a_router_over_the_owners_connected_accounts_in_his_pill_order()
    {
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Claude);
        using var temp = new AssistantTempRoot();
        using var store = AssistantStore.Open(temp.Roots);
        var (_, second, codex) = Accounts(rig);
        rig.H.Main.OnMain(() =>
        {
            var pill = rig.App.Settings.Pills.Create("Mine", [new PillStep(codex.ToString("D")), new PillStep(second.ToString("D"))]);
            rig.App.Settings.Pills.SetUseForNewThreads(pill.Id);
        });
        var controller = rig.H.Main.OnMain(() => rig.App.StartDextra(store, _ => { }, () => []));
        try
        {
            var router = Assert.IsType<AssistantRouter>(controller.Router);
            var decision = rig.H.Main.OnMain(() => router.Decide(new RouteRequest(new RouteStep(StepWork.Edit), "dextra")));
            // Nobody has read the accounts yet, so the owner's order decides: the pill's two accounts, then the one in no pill.
            Assert.Equal(["codex:" + codex.ToString("D"), "claude:" + second.ToString("D"), "claude:default"], decision.Ranked.Select(r => r.Option.AccountKey).Distinct());
            Assert.All(decision.Ranked, r => Assert.True(r.Option.Unread));
            Assert.Empty(decision.Excluded);
            Assert.Same(router, controller.Runner.ReviewerRouter);
        }
        finally
        {
            rig.H.Main.OnMain(rig.App.UnbindDextra);
            controller.Dispose();
        }
    }

    [Fact]
    public void A_mission_the_router_placed_is_held_to_the_floor_of_its_own_account_not_the_providers_first()
    {
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Claude);
        using var temp = new AssistantTempRoot();
        using var store = AssistantStore.Open(temp.Roots);
        var (_, second, _) = Accounts(rig);
        var controller = rig.H.Main.OnMain(() => rig.App.StartDextra(store, _ => { }, () => []));
        try
        {
            var reserve = controller.Reserve!;
            Assert.Null(reserve.Settings.Add(new ReserveRule("", ReserveScope.Every, ReserveLifetime.Standing) { KeepPercent = 30 }));
            var now = DateTimeOffset.UtcNow;
            var firstAccount = rig.App.AssistantTaps.AccountRef(UsageAccountKey.Default(ProviderKind.Claude));
            var placed = rig.App.AssistantTaps.AccountRef(ProviderKind.Claude, second);
            reserve.OnReadings(firstAccount, [new ReserveWindow("five", ReserveWindowKind.FiveHour, 10, now, now.AddHours(3))]);
            reserve.OnReadings(placed, [new ReserveWindow("five", ReserveWindowKind.FiveHour, 69, now, now.AddHours(3))]);
            var mission = new MissionRecord(new Mission(Guid.NewGuid(), "Fix it", "dextra", MissionOrigin.Owner, MissionState.Planning)) { Provider = ProviderKind.Claude, Created = now };

            Assert.True(reserve.BeforeTurn(mission, null, ProviderKind.Claude, null, ReserveTurns.Start));                          // the provider's first account has room
            Assert.False(reserve.BeforeTurn(mission with { AccountId = second }, null, ProviderKind.Claude, null, ReserveTurns.Start));   // the one it was placed on does not
        }
        finally
        {
            rig.H.Main.OnMain(rig.App.UnbindDextra);
            controller.Dispose();
        }
    }
}
