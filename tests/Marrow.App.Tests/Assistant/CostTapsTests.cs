using Marrow.App.Assistant;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.App.Tests.Assistant;

// Dextra AS-15 (DESIGN.md rev 5, 13.2, 15.3; ED-NEW-dextra-as15-3, -4, -5, -6): the cost controls as the app holds them. StartDextra reads the
// owner's standing caps from the store, the one-line taps feed the day's spend to the guard whether or not anyone watches the feed (a chat of an
// assistant that is no mission's counts toward the assistant's day), the duty watch keeps its book, and the controller writes the day and the
// book back when it lets go.
[Trait("Decision", "ED-NEW-dextra-as15-3")]
[Trait("Feature", "AS-15")]
public sealed class CostTapsTests
{
    [Fact]
    [Trait("Decision", "ED-NEW-dextra-open-16")]
    public void Start_dextra_binds_the_caps_the_taps_feed_the_day_and_the_store_keeps_it_with_the_breaker_s_book()
    {
        var assistant = Guid.NewGuid();
        // ED-NEW-dextra-open-16: triage needs an authorized, connected account and selection on MainActor.
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Claude, thread => thread with { AssistantID = assistant }, settings: app =>
        {
            var account = app.Settings.Accounts.Choose(ProviderKind.Claude) ?? app.Settings.Accounts.Add(ProviderKind.Claude);
            app.Settings.Accounts.MarkSignedIn(account.Id);
            Assert.True(app.IsAccountConnected(app.Settings.Accounts.Get(account.Id)));
            Assert.True(app.CanUseAccount(account.Id));
        });
        using var temp = new AssistantTempRoot();
        using var store = AssistantStore.Open(temp.Roots);
        var stored = new ReserveSettings();
        Assert.Null(stored.AddCap(new CostCap("", CostCapScope.AssistantDay) { Percent = 10, AssistantId = assistant.ToString("N") }));
        store.SaveReserves(stored.ToJson()!);

        var controller = rig.H.Main.OnMain(() => rig.App.StartDextra(store, _ => { }, () => [new AssistantProfile(assistant.ToString("N"), "Channel A")]));
        try
        {
            var costs = Assert.IsType<CostGuard>(controller.Costs);
            Assert.Same(costs, rig.App.Costs);
            Assert.Same(costs, controller.Reserve!.Costs);                           // the runner has one gate, the reserve, and it asks the caps
            Assert.Same(controller.Reserve, controller.Runner.Reserve);
            Assert.Equal(["Channel A: at most 10% of any account a day"], costs.Settings.Caps.Select(c => CostCapWords.Title(c, id => id == assistant.ToString("N") ? "Channel A" : null)));
            Assert.NotNull(controller.Duties);

            // The assistant's own thread spends: the tap feeds the day, nobody watches the feed. Nothing is calibrated yet, so a token costs the
            // conservative default: 150,000 tokens to a point of the 5-hour window and 1,200,000 to a point of the weekly one.
            rig.H.Main.OnMain(() => rig.App.AssistantTaps.TapProvider(rig.InitialThread.Id,
                new ProviderEvent.TokenSpend(new TokenSpend(ProviderKind.Claude, 600_000, model: "offline-model"))));

            // A duty fails three times in a row: it is paused and the book keeps that.
            for (var i = 0; i < 3; i++) controller.Duties!.RunEnded(new DutyRun("digest", "Daily digest", assistant.ToString("N"), false, "the site is down"));
            Assert.True(controller.Duties!.IsPaused("digest"));

            // The cheap model: nothing priced to compare with, so the brain's own model reads the feed.
            var pick = rig.H.Main.OnMain(() => controller.TriageModel());
            Assert.True(pick.IsBrain);
            Assert.Equal("claude", pick.Model!.ProviderId);
            Assert.All(rig.H.Main.OnMain(() => rig.App.TriageModels()), model => Assert.NotEmpty(model.ProviderId));      // whatever this PC offers is listed, priced or not
        }
        finally
        {
            rig.H.Main.OnMain(rig.App.UnbindDextra);
            controller.Dispose();                                                     // the day and the book are written when the controller lets go
        }

        var day = Assert.Single(store.Costs()!["days"]!.AsArray());
        var rows = day!["rows"]!.AsArray().Select(r => r!).ToList();
        Assert.All(rows, row => Assert.Equal(assistant.ToString("N"), row["assistant"]!.GetValue<string>()));
        Assert.Equal(4.0, rows.Single(r => r["window"]!.GetValue<string>() == "FiveHour")["amount"]!.GetValue<double>(), 6);
        Assert.Equal(0.5, rows.Single(r => r["window"]!.GetValue<string>() == "Weekly")["amount"]!.GetValue<double>(), 6);
        var duty = Assert.Single(store.DutyHealth()!["duties"]!.AsArray());
        Assert.Equal("digest", duty!["duty"]!.GetValue<string>());
        Assert.True(duty["paused"]!.GetValue<bool>());
    }

    [Fact]
    public void Without_dextra_the_taps_leave_the_caps_alone()
    {
        using var rig = new ThreadRuntimeTurnRig(ProviderKind.Claude);
        Assert.Null(rig.App.Costs);
        rig.H.Main.OnMain(() => rig.App.AssistantTaps.TapProvider(rig.InitialThread.Id,
            new ProviderEvent.TokenSpend(new TokenSpend(ProviderKind.Claude, 100_000, model: "offline-model"))));
        Assert.Null(rig.App.Costs);
    }
}
