using Avalonia.Controls;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Usage;
using Marrow.Core.Models;
using Marrow.Services.Providers;
using Marrow.UI.Chrome;
using static Marrow.App.Tests.Fixtures.UsageViewsProbe;

namespace Marrow.App.Tests.Views.Usage;

// Source: DroppyCode/App/Views/Usage/UsageViews.swift (B3 52bc792), :204-308 BankedResetsView.spend and the settle task: Reset now
// goes through the registry, the row shows Resetting, the outcome (or the error) is held for four seconds, then the block reads
// the count again. The provider side is a loopback Z.ai (its endpoint boundary) or Codex with no executable; nothing real is called.
[Collection("Headless")]
public sealed class UsageViewsBankedResetSpendTests(HeadlessSession headless)
{
    private const string ZaiKey = "fixture-key-never-sent";

    private static PlanLimits.ResetCredits ZaiCards() =>
        Resets(2, Credit("FIVE_HOUR:1", title: "5-hour reset", detail: "Resets the 5-hour limit"), Credit("WEEK:3", title: "Weekly reset", detail: "Resets the weekly limit"));

    private static BankedResetsView Block(UsageViewsRig rig, ProviderKind provider, PlanLimits.ResetCredits credits) =>
        rig.Show(new BankedResetsView(provider, credits));

    private static BankedResetRow[] Rows(BankedResetsView block) => All<BankedResetRow>(block);

    private static void Spend(BankedResetsView block, int row)
    {
        Click(ButtonTitled(Rows(block)[row], "Use reset"));
        Click(ButtonTitled(Rows(block)[row], "Reset now"));
    }

    private static string?[] Titles(BankedResetsView block) => Buttons(block).Select(button => button.Content as string).ToArray();

    // ------------------------------------------------------------------ UI-PROVREG-031, UI-USAGE-028 Reset now, end to end

    [Fact]
    [Trait("Req", "UI-PROVREG-031")] // :350 Button("Reset now", action: onUse); :293-307 spend: applying, the registry call, done, then idle after four seconds
    [Trait("Req", "UI-USAGE-028")] // "click: spends the banked reset through the registry"
    [Trait("Req", "PR-USAGE-040")] // "Reset now starts the spend."
    public void Reset_now_spends_through_the_registry_shows_Resetting_then_the_outcome_for_four_seconds_and_goes_idle() => headless.OnUiThread(() =>
    {
        using var rig = new UsageViewsRig();
        var release = new TaskCompletionSource();
        using var server = new SettingsProviderProofServer(async request =>
        {
            if (!request.Target.Contains("/customer-package-reset/use", StringComparison.Ordinal)) return CannedResponse.Plain(503, "Unavailable");
            await release.Task;
            return CannedResponse.Json("""{"code":200,"success":true}""");
        });
        using var endpoints = ZaiLoopback(server);
        rig.Model.Settings.SetAPIKeyInput(ZaiKey, ProviderKind.Zai);
        var block = Block(rig, ProviderKind.Zai, ZaiCards());

        Spend(block, 0);

        // Applying: the row has the spinner and Resetting, every other Use reset is off, and the registry is out with the credit id.
        Assert.Equal(new BankedResetsView.Phase.Applying("FIVE_HOUR:1"), PhaseOf(block));
        Assert.True(HasText(Rows(block)[0], "Resetting…"));
        Assert.False(ButtonTitled(Rows(block)[1], "Use reset").IsEnabled);
        WaitUntil(() => server.Requests.Any(request => request.Method == "POST"), "the reset request");
        Assert.Equal("FIVE_HOUR:1", rig.Registry.RedeemingReset);
        Assert.Equal("/api/biz/customer-package-reset/use", server.Requests.Single(request => request.Method == "POST").Target);
        Assert.Equal(["Bearer " + ZaiKey], server.Requests.Single(request => request.Method == "POST").HeaderValues("Authorization"));

        // The provider answers; Z.ai then lets the meters settle for two seconds before the call returns, so the row is still
        // Resetting after 1.9 seconds, however the sleep lines up with them.
        release.SetResult();
        rig.AdvanceInSteps(1.9);
        Assert.IsType<BankedResetsView.Phase.Applying>(PhaseOf(block));
        Assert.True(HasText(Rows(block)[0], "Resetting…"));
        rig.AdvanceUntil(() => PhaseOf(block) is BankedResetsView.Phase.Done, "the outcome", maxSeconds: 5);

        // Done: the outcome line, the registry released, the buttons back.
        Assert.Equal(new BankedResetsView.Phase.Done(PlanLimits.ResetOutcome.Reset), PhaseOf(block));
        Assert.True(HasText(block, "Usage was reset."));
        Assert.Null(rig.Registry.RedeemingReset);
        Assert.True(ButtonTitled(Rows(block)[1], "Use reset").IsEnabled);

        // Held for four seconds, then idle.
        rig.Advance(TimeSpan.FromSeconds(3.9));
        Assert.IsType<BankedResetsView.Phase.Done>(PhaseOf(block));
        Assert.True(HasText(block, "Usage was reset."));
        rig.Advance(TimeSpan.FromSeconds(0.2));
        Assert.IsType<BankedResetsView.Phase.Idle>(PhaseOf(block));
        Assert.False(HasText(block, "Usage was reset."));
    });

    // ------------------------------------------------------------------ PR-USAGE-046, UI-PROVREG-026 a failed spend

    [Fact]
    [Trait("Req", "PR-USAGE-046")] // :301 catch { phase = .failed(error.localizedDescription) }; PlanLimits.swift:49-61 "Codex CLI not found."
    [Trait("Req", "UI-PROVREG-026")] // "shown for about four seconds after a failed spend, then cleared"
    public void A_spend_that_cannot_start_shows_the_errors_own_text_for_four_seconds() => headless.OnUiThread(() =>
    {
        using var rig = new UsageViewsRig(); // Codex has no executable here: the registry throws before anything is launched
        var block = Block(rig, ProviderKind.Codex, Resets(1, Credit("c1", title: "Card")));

        Spend(block, 0);
        WaitUntil(() => PhaseOf(block) is BankedResetsView.Phase.Failed, "the failure");

        Assert.Equal(new BankedResetsView.Phase.Failed("Codex CLI not found."), PhaseOf(block));
        var text = Text(block, "Codex CLI not found.");
        AssertColor(text, Chrome.Danger);
        Assert.Empty(rig.Launches);
        Assert.Null(rig.Registry.RedeemingReset);
        Assert.True(ButtonTitled(Rows(block)[0], "Use reset").IsEnabled); // the row is back to its button while the line shows

        WaitUntil(() => rig.Time.ActiveTimers > 0, "the four-second hold");
        rig.Advance(TimeSpan.FromSeconds(3.9));
        Assert.True(HasText(block, "Codex CLI not found."));
        rig.Advance(TimeSpan.FromSeconds(0.2));
        Assert.False(HasText(block, "Codex CLI not found."));
        Assert.IsType<BankedResetsView.Phase.Idle>(PhaseOf(block));
    });

    [Fact]
    [Trait("Req", "PR-USAGE-046")] // PlanLimits.swift:49-61: unknownOutcome gives "Codex returned an unknown reset result."
    public void The_unknown_outcome_error_reads_its_own_description_with_no_prefix_or_full_stop_added() => headless.OnUiThread(() =>
    {
        using var rig = new UsageViewsRig(); // Z.ai with no key: the registry answers unknownOutcome before any request
        using var server = new SettingsProviderProofServer(_ => CannedResponse.Plain(503, "Unavailable"));
        using var endpoints = ZaiLoopback(server);
        Assert.Equal("", rig.Model.Settings.ApiKey(ProviderKind.Zai));
        var block = Block(rig, ProviderKind.Zai, ZaiCards());

        Spend(block, 1);
        WaitUntil(() => PhaseOf(block) is BankedResetsView.Phase.Failed, "the failure");

        Assert.Equal(new BankedResetsView.Phase.Failed("Codex returned an unknown reset result."), PhaseOf(block));
        Assert.True(HasText(block, "Codex returned an unknown reset result."));
        Assert.Empty(server.Requests);
    });

    [Fact]
    [Trait("Req", "PR-USAGE-046")] // PlanLimits.swift:49-61: rejected(let message) gives that message unchanged; "An error from a lower layer surfaces its own description verbatim"
    public void A_rejection_shows_the_providers_own_message_unchanged() => headless.OnUiThread(() =>
    {
        using var rig = new UsageViewsRig();
        using var server = new SettingsProviderProofServer(_ => CannedResponse.Json("""{"code":1305,"success":false,"msg":"Reset card already redeemed"}"""));
        using var endpoints = ZaiLoopback(server);
        rig.Model.Settings.SetAPIKeyInput(ZaiKey, ProviderKind.Zai);
        var block = Block(rig, ProviderKind.Zai, ZaiCards());

        Spend(block, 0);
        WaitUntil(() => PhaseOf(block) is BankedResetsView.Phase.Failed, "the failure");

        Assert.Equal(new BankedResetsView.Phase.Failed("Reset card already redeemed"), PhaseOf(block));
        Assert.True(HasText(block, "Reset card already redeemed"));
    });

    // ------------------------------------------------------------------ PR-USAGE-044 the settle task is cancelled when the view goes

    [Fact]
    [Trait("Req", "PR-USAGE-044")] // :256 .onDisappear { settle?.cancel() }; :304 guard !Task.isCancelled else { return }
    public void Closing_the_view_during_the_hold_cancels_the_write_back_to_idle() => headless.OnUiThread(() =>
    {
        using var rig = new UsageViewsRig();
        var block = Block(rig, ProviderKind.Codex, Resets(1, Credit("c1", title: "Card")));
        Spend(block, 0);
        WaitUntil(() => PhaseOf(block) is BankedResetsView.Phase.Failed, "the failure");
        WaitUntil(() => rig.Time.ActiveTimers > 0, "the four-second hold");

        ((Border)block.Parent!).Child = null; // the panel or popover closes
        UsageViewsRig.Drain();
        rig.Advance(TimeSpan.FromSeconds(10));

        Assert.IsType<BankedResetsView.Phase.Failed>(PhaseOf(block)); // never written back to idle
        Assert.Equal(0, rig.Time.ActiveTimers);
    });

    [Fact]
    [Trait("Req", "PR-USAGE-044")] // "Reopening the panel builds a fresh BankedResetsView whose phase starts at idle"
    public void A_fresh_block_starts_idle_whatever_the_old_one_was_doing() => headless.OnUiThread(() =>
    {
        using var rig = new UsageViewsRig();
        var first = Block(rig, ProviderKind.Codex, Resets(1, Credit("c1", title: "Card")));
        Spend(first, 0);
        WaitUntil(() => PhaseOf(first) is BankedResetsView.Phase.Failed, "the failure");

        var reopened = Block(rig, ProviderKind.Codex, Resets(1, Credit("c1", title: "Card")));

        Assert.IsType<BankedResetsView.Phase.Idle>(PhaseOf(reopened));
        Assert.False(HasText(reopened, "Codex CLI not found."));
    });

    [Fact]
    [Trait("Req", "PR-USAGE-044")] // the contrast: left on screen, the same hold does return to idle after four seconds
    public void Left_on_screen_the_same_hold_returns_to_idle() => headless.OnUiThread(() =>
    {
        using var rig = new UsageViewsRig();
        var block = Block(rig, ProviderKind.Codex, Resets(1, Credit("c1", title: "Card")));
        Spend(block, 0);
        WaitUntil(() => PhaseOf(block) is BankedResetsView.Phase.Failed, "the failure");
        WaitUntil(() => rig.Time.ActiveTimers > 0, "the four-second hold");

        rig.Advance(TimeSpan.FromSeconds(4));

        Assert.IsType<BankedResetsView.Phase.Idle>(PhaseOf(block));
    });
}
