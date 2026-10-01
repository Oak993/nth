using Marrow.Core.Models;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;
using Xunit;

namespace Marrow.Services.Tests.Providers;

// Source: DroppyCode/Services/Providers/ProviderRegistry.swift (B2 1fbf81e), refreshAPIProvider :380-425.
//
// Test-first proofs for the three API-key providers' status checks and the reads a proven key triggers
// (PR-PROVREG-126 to 130). Every request goes to a loopback server through the Endpoint boundaries of
// DeepSeekAPI, MetaAPI and ZaiAPI (R-TEST-10); no key, endpoint or credential outside the test is touched.
public sealed class ProviderRegistryApiRefreshTests
{
    private static ProviderStatus ApiStatus(ProviderStatus.Auth auth) =>
        new() { Version = "API", ProviderStatusAuth = auth, ApiKeyConfigured = true };

    // ---- PR-PROVREG-126: DeepSeek is validated over the network; version is the literal "API" ----------------

    [Fact]
    [Trait("Req", "PR-PROVREG-126")] // ProviderRegistry.swift:387-393 refreshAPIProvider (DeepSeek, accepted key)
    public async Task a_deepseek_key_the_models_endpoint_accepts_is_signed_in_with_no_account_and_the_api_version()
    {
        await using var rig = new ProviderApiRig();
        rig.Loopback.Route(ProviderApiRig.DeepSeekModels, 200, "{\"data\":[]}");
        rig.Loopback.Route(ProviderApiRig.DeepSeekBalance, 200, ProviderApiRig.DeepSeekBalanceBody("4.00"));
        rig.Settings.Keys[ProviderKind.Deepseek] = "ds-key";

        await rig.Run(() => rig.Registry.Refresh(ProviderKind.Deepseek));

        Assert.Equal(ApiStatus(new ProviderStatus.Auth.SignedIn(null)), rig.Registry.Status(ProviderKind.Deepseek));
        Assert.Equal("Signed in", rig.Registry.Status(ProviderKind.Deepseek).Summary);
        var validation = Assert.Single(rig.Loopback.Requests, request => request.Path == ProviderApiRig.DeepSeekModels);
        Assert.Equal("Bearer ds-key", validation.Authorization);
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-126")] // ProviderRegistry.swift:387-393 refreshAPIProvider (DeepSeek, rejected key); PR-PROVREG-126 failure_cases
    public async Task a_deepseek_key_the_models_endpoint_rejects_is_signed_out_but_still_counts_as_installed()
    {
        await using var rig = new ProviderApiRig();
        rig.Loopback.Route(ProviderApiRig.DeepSeekModels, 401, "{}");
        rig.Settings.Keys[ProviderKind.Deepseek] = "ds-key";

        await rig.Run(() => rig.Registry.Refresh(ProviderKind.Deepseek));

        var status = rig.Registry.Status(ProviderKind.Deepseek);
        Assert.Equal(ApiStatus(new ProviderStatus.Auth.SignedOut()), status);
        Assert.True(status.IsInstalled); // apiKeyConfigured is true regardless of validity
        Assert.Equal("Not signed in", status.Summary);
    }

    // ---- PR-PROVREG-127: a valid key forces a balance read; an invalid one clears balance and timestamp -------

    [Fact]
    [Trait("Req", "PR-PROVREG-127")] // ProviderRegistry.swift:394-398 (valid: refreshCredits(.deepseek, force: true))
    public async Task a_valid_deepseek_key_reads_the_balance_at_once_even_inside_the_sixty_second_gate()
    {
        await using var rig = new ProviderApiRig();
        rig.Loopback.Route(ProviderApiRig.DeepSeekModels, 200, "{\"data\":[]}");
        rig.Loopback.Route(ProviderApiRig.DeepSeekBalance, 200, ProviderApiRig.DeepSeekBalanceBody("4.00"));
        rig.Settings.Keys[ProviderKind.Deepseek] = "ds-key";

        await rig.Run(() => rig.Registry.Refresh(ProviderKind.Deepseek));
        await rig.Until(() => rig.Registry.Credits.ContainsKey(ProviderKind.Deepseek));
        Assert.Equal(4.0, rig.Registry.Credits[ProviderKind.Deepseek].Total);
        Assert.Equal(1, rig.Loopback.Count(ProviderApiRig.DeepSeekBalance));

        // The first read just succeeded, so an unforced ask is gated; the key proving itself again is forced.
        rig.Loopback.Route(ProviderApiRig.DeepSeekBalance, 200, ProviderApiRig.DeepSeekBalanceBody("9.00"));
        await rig.Run(() => rig.Registry.Refresh(ProviderKind.Deepseek));
        await rig.Until(() => rig.Registry.Credits[ProviderKind.Deepseek].Total == 9.0);
        Assert.Equal(2, rig.Loopback.Count(ProviderApiRig.DeepSeekBalance));
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-127")] // ProviderRegistry.swift:399-401 (invalid: credits[.deepseek] and creditsFetchedAt[.deepseek] = nil)
    public async Task a_rejected_deepseek_key_clears_the_cached_balance_and_its_timestamp_so_the_next_read_is_not_gated()
    {
        await using var rig = new ProviderApiRig();
        rig.Loopback.Route(ProviderApiRig.DeepSeekModels, 200, "{\"data\":[]}");
        rig.Loopback.Route(ProviderApiRig.DeepSeekBalance, 200, ProviderApiRig.DeepSeekBalanceBody("4.00"));
        rig.Settings.Keys[ProviderKind.Deepseek] = "ds-key";
        await rig.Run(() => rig.Registry.Refresh(ProviderKind.Deepseek));
        await rig.Until(() => rig.Registry.Credits.ContainsKey(ProviderKind.Deepseek));
        Assert.Equal(1, rig.Loopback.Count(ProviderApiRig.DeepSeekBalance));

        // The key is now rejected: the balance behind it is dropped, not left stale.
        rig.Loopback.Route(ProviderApiRig.DeepSeekModels, 401, "{}");
        await rig.Run(() => rig.Registry.Refresh(ProviderKind.Deepseek));
        Assert.DoesNotContain(ProviderKind.Deepseek, rig.Registry.Credits.Keys);
        Assert.Equal(1, rig.Loopback.Count(ProviderApiRig.DeepSeekBalance)); // no read on the invalid path

        // The timestamp went with it: an unforced ask a moment after the success is NOT held by the sixty second gate.
        rig.OnMain(() => rig.Registry.RefreshCredits(ProviderKind.Deepseek));
        await rig.Until(() => rig.Registry.Credits.ContainsKey(ProviderKind.Deepseek));
        Assert.Equal(2, rig.Loopback.Count(ProviderApiRig.DeepSeekBalance));
    }

    // ---- PR-PROVREG-128: Meta is validated the same way, with no credits or limits read --------------------

    [Fact]
    [Trait("Req", "PR-PROVREG-128")] // ProviderRegistry.swift:402-408 refreshAPIProvider (Meta, accepted key)
    public async Task a_meta_key_the_models_endpoint_accepts_is_signed_in_and_nothing_else_is_read()
    {
        await using var rig = new ProviderApiRig();
        rig.Loopback.Route(ProviderApiRig.MetaModels, 200, "{\"data\":[]}");
        rig.Settings.Keys[ProviderKind.Meta] = "meta-key";

        await rig.Run(() => rig.Registry.Refresh(ProviderKind.Meta));
        rig.Runtime.RunUntilIdle();

        Assert.Equal(ApiStatus(new ProviderStatus.Auth.SignedIn(null)), rig.Registry.Status(ProviderKind.Meta));
        var only = Assert.Single(rig.Loopback.Requests); // one validation request, no balance, no limits
        Assert.Equal(ProviderApiRig.MetaModels, only.Path);
        Assert.Equal("Bearer meta-key", only.Authorization);
        Assert.Empty(rig.Registry.Credits);
        Assert.Empty(rig.Registry.PlanLimits);
        Assert.Empty(rig.Registry.LoadingCredits);
        Assert.Empty(rig.Registry.LoadingLimits);
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-128")] // ProviderRegistry.swift:402-408 refreshAPIProvider (Meta, rejected key); PR-PROVREG-128 failure_cases
    public async Task a_meta_key_the_models_endpoint_rejects_is_signed_out_but_still_installed()
    {
        await using var rig = new ProviderApiRig();
        rig.Loopback.Route(ProviderApiRig.MetaModels, 403, "{}");
        rig.Settings.Keys[ProviderKind.Meta] = "meta-key";

        await rig.Run(() => rig.Registry.Refresh(ProviderKind.Meta));

        var status = rig.Registry.Status(ProviderKind.Meta);
        Assert.Equal(ApiStatus(new ProviderStatus.Auth.SignedOut()), status);
        Assert.True(status.IsInstalled);
    }

    // ---- PR-PROVREG-129: a valid Z.ai key forces a limits read; an invalid one clears limits and timestamp ---

    [Fact]
    [Trait("Req", "PR-PROVREG-129")] // ProviderRegistry.swift:409-417 refreshAPIProvider (Z.ai, valid: refreshPlanLimits(.zai, force: true))
    public async Task a_valid_zai_key_is_signed_in_and_forces_a_plan_limits_read_even_inside_the_sixty_second_gate()
    {
        await using var rig = new ProviderApiRig();
        rig.Loopback.Route(ProviderApiRig.ZaiModels, 200, "{\"data\":[]}");
        rig.Loopback.Route(ProviderApiRig.ZaiQuota, 200, ProviderApiRig.ZaiQuotaBody(25));
        rig.Loopback.Route(ProviderApiRig.ZaiResetList, 404, "{}");
        rig.Settings.Keys[ProviderKind.Zai] = "zai-key";

        await rig.Run(() => rig.Registry.Refresh(ProviderKind.Zai));
        Assert.Equal(ApiStatus(new ProviderStatus.Auth.SignedIn(null)), rig.Registry.Status(ProviderKind.Zai));
        await rig.Until(() => rig.Registry.PlanLimits.ContainsKey(ProviderKind.Zai) && rig.Registry.LoadingLimits.Count == 0);
        Assert.Equal(25, rig.Registry.PlanLimits[ProviderKind.Zai].Windows[0].Percent);
        Assert.Equal("Bearer zai-key", rig.Loopback.Requests.First(request => request.Path == ProviderApiRig.ZaiQuota).Authorization);

        // The read just succeeded, so an unforced ask would be gated; the key proving itself again is forced.
        rig.Loopback.Route(ProviderApiRig.ZaiQuota, 200, ProviderApiRig.ZaiQuotaBody(60));
        await rig.Run(() => rig.Registry.Refresh(ProviderKind.Zai));
        await rig.Until(() => rig.Registry.PlanLimits[ProviderKind.Zai].Windows[0].Percent == 60 && rig.Registry.LoadingLimits.Count == 0);
        Assert.Equal(2, rig.Loopback.Count(ProviderApiRig.ZaiQuota));
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-129")] // ProviderRegistry.swift:418-421 refreshAPIProvider (Z.ai, invalid: planLimits[.zai] and limitsFetchedAt[.zai] = nil)
    public async Task a_rejected_zai_key_clears_the_cached_limits_and_their_timestamp_so_the_next_read_is_not_gated()
    {
        await using var rig = new ProviderApiRig();
        rig.Loopback.Route(ProviderApiRig.ZaiModels, 200, "{\"data\":[]}");
        rig.Loopback.Route(ProviderApiRig.ZaiQuota, 200, ProviderApiRig.ZaiQuotaBody(25));
        rig.Loopback.Route(ProviderApiRig.ZaiResetList, 404, "{}");
        rig.Settings.Keys[ProviderKind.Zai] = "zai-key";
        await rig.Run(() => rig.Registry.Refresh(ProviderKind.Zai));
        await rig.Until(() => rig.Registry.PlanLimits.ContainsKey(ProviderKind.Zai) && rig.Registry.LoadingLimits.Count == 0);
        Assert.Equal(1, rig.Loopback.Count(ProviderApiRig.ZaiQuota));

        rig.Loopback.Route(ProviderApiRig.ZaiModels, 401, "{}");
        await rig.Run(() => rig.Registry.Refresh(ProviderKind.Zai));
        Assert.Equal(ApiStatus(new ProviderStatus.Auth.SignedOut()), rig.Registry.Status(ProviderKind.Zai));
        Assert.DoesNotContain(ProviderKind.Zai, rig.Registry.PlanLimits.Keys);
        Assert.Equal(1, rig.Loopback.Count(ProviderApiRig.ZaiQuota)); // no read on the invalid path

        // The timestamp went with the limits: an unforced ask right after the earlier success is not suppressed.
        rig.Loopback.Route(ProviderApiRig.ZaiQuota, 200, ProviderApiRig.ZaiQuotaBody(70));
        rig.OnMain(() => rig.Registry.RefreshPlanLimits(ProviderKind.Zai));
        await rig.Until(() => rig.Registry.PlanLimits.ContainsKey(ProviderKind.Zai) && rig.Registry.LoadingLimits.Count == 0);
        Assert.Equal(2, rig.Loopback.Count(ProviderApiRig.ZaiQuota));
        Assert.Equal(70, rig.Registry.PlanLimits[ProviderKind.Zai].Windows[0].Percent);
    }

    // ---- PR-PROVREG-130: the final else arm marks any other provider signed in, with no validation ------------

    [Fact]
    [Trait("Req", "PR-PROVREG-130")] // ProviderRegistry.swift:422-424 refreshAPIProvider (the else arm, driven directly)
    public async Task the_else_arm_marks_any_other_provider_with_a_key_signed_in_with_no_version_and_no_network_call()
    {
        await using var rig = new ProviderApiRig();
        rig.Settings.Keys[ProviderKind.Codex] = "some-key"; // a stand-in for a fourth API-key provider

        await rig.Run(() => rig.Registry.RefreshAPIProvider(ProviderKind.Codex));

        var status = rig.Registry.Status(ProviderKind.Codex);
        Assert.Equal(new ProviderStatus { ProviderStatusAuth = new ProviderStatus.Auth.SignedIn(null), ApiKeyConfigured = true }, status);
        Assert.Null(status.Version);
        Assert.True(status.IsInstalled);
        Assert.Equal(0, rig.Loopback.Total);
    }
}
