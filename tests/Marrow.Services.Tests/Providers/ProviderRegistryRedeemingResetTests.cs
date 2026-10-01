using System.Text;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;
using Xunit;

namespace Marrow.Services.Tests.Providers;

// Source: DroppyCode/Services/Providers/ProviderRegistry.swift (B2 1fbf81e), redeemingReset :58-60 and
// consumeResetCredit :649-676.
//
// PR-PROVREG-236: the property is published, written when a spend starts and cleared by a defer when it ends. The
// pinned checkout has no reader of it (the banked-resets view keeps its own phase), so what is proved here is the
// maintenance the record asks to keep: set to the credit id (or the provider's raw value) while the spend is out,
// cleared on every exit. The Z.ai spend is driven against a loopback server (R-TEST-10).
public sealed class ProviderRegistryRedeemingResetTests
{
    private static ProviderApiRig ZaiRig()
    {
        var rig = new ProviderApiRig();
        rig.Settings.Keys[ProviderKind.Zai] = "zai-key";
        rig.Loopback.Route(ProviderApiRig.ZaiQuota, 200, ProviderApiRig.ZaiQuotaBody(10));
        rig.Loopback.Route(ProviderApiRig.ZaiResetList, 404, "{}");
        return rig;
    }

    private static List<string?> WatchRedeemingReset(ProviderRegistry registry)
    {
        var seen = new List<string?>();
        registry.PropertyChanged += (_, change) =>
        {
            if (change.PropertyName == nameof(ProviderRegistry.RedeemingReset)) seen.Add(registry.RedeemingReset);
        };
        return seen;
    }

    /// <summary>Starts a spend as a task of its own and waits until its reset-use request is held at the loopback.</summary>
    private static async Task<(TaskHandle<PlanLimits.ResetOutcome> Spend, LoopbackExchange Held)> StartHeldSpend(ProviderApiRig rig, string creditId)
    {
        rig.Loopback.Intercept(ProviderApiRig.ZaiResetUse);
        TaskHandle<PlanLimits.ResetOutcome>? spend = null;
        rig.OnMain(() => spend = MainActor.SpawnThrowing(() => rig.Registry.ConsumeResetCredit(ProviderKind.Zai, creditId)));
        var pump = rig.Until(() => rig.Loopback.Count(ProviderApiRig.ZaiResetUse) >= 1);
        var held = await rig.Loopback.NextIntercepted(ProviderApiRig.ZaiResetUse);
        await pump;
        return (spend!, held);
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-236")] // ProviderRegistry.swift:651-652 (redeemingReset = creditID ?? rawValue; defer clears it) over a successful Z.ai spend
    public async Task redeeming_reset_holds_the_credit_id_while_the_spend_is_out_and_is_cleared_when_it_succeeds()
    {
        await using var rig = ZaiRig();
        var seen = WatchRedeemingReset(rig.Registry);
        Assert.Null(rig.Registry.RedeemingReset);

        var (spend, held) = await StartHeldSpend(rig, "WEEK:7");
        Assert.Equal("WEEK:7", rig.Registry.RedeemingReset);

        await held.Respond(200, "OK", Encoding.UTF8.GetBytes("{\"code\":200,\"success\":true}"));
        // The spend waits two seconds on the deterministic clock before re-reading the limits.
        for (var round = 0; round < 400 && !spend.Value.IsCompleted; round++)
        {
            rig.Runtime.Advance(TimeSpan.FromMilliseconds(500));
            await Task.Delay(10);
        }
        Assert.Equal(PlanLimits.ResetOutcome.Reset, await spend.Value);
        Assert.Null(rig.Registry.RedeemingReset);
        Assert.Equal(["WEEK:7", null], seen);
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-236")] // ProviderRegistry.swift:651-652 (nil credit id: the provider's raw value), defer on a throwing exit
    public async Task redeeming_reset_is_the_provider_raw_value_for_a_spend_with_no_credit_id_and_is_cleared_when_it_throws()
    {
        await using var rig = ZaiRig();
        var seen = WatchRedeemingReset(rig.Registry);

        // Z.ai needs a credit id (:666), so this spend throws at once; the property still went "zai" and back.
        await Assert.ThrowsAsync<ResetCreditError>(() => rig.Run(() => rig.Registry.ConsumeResetCredit(ProviderKind.Zai, null)));

        Assert.Equal(["zai", null], seen);
        Assert.Null(rig.Registry.RedeemingReset);
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-236")] // ProviderRegistry.swift:652 (the defer clears it on cancellation: PR-PROVREG-236 cancellation)
    public async Task redeeming_reset_is_cleared_when_the_spend_is_cancelled_while_it_is_out()
    {
        await using var rig = ZaiRig();
        var (spend, held) = await StartHeldSpend(rig, "FIVE_HOUR:3");
        Assert.Equal("FIVE_HOUR:3", rig.Registry.RedeemingReset);

        spend.Cancel();
        for (var round = 0; round < 400 && !spend.Value.IsCompleted; round++)
        {
            rig.Runtime.RunUntilIdle();
            await Task.Delay(10);
        }
        Assert.True(spend.Value.IsCompleted);
        await Assert.ThrowsAnyAsync<Exception>(() => spend.Value); // the cancelled request ends the spend with an error
        Assert.Null(rig.Registry.RedeemingReset);
        GC.KeepAlive(held);
    }
}
