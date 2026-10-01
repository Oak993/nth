using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;
using Xunit;

namespace Marrow.Services.Tests.Providers;

// Source: DroppyCode/Services/Providers/ProviderRegistry.swift (B2 1fbf81e), refreshPlanLimits :606-641 (the CLI branch,
// :627-640) and drainedForcedRead :643-647.
//
// The CLI half of the plan-limits read: the same queue, gate, watchdog and keep-previous rules as the API half proved in
// ProviderRegistryPlanLimitsReadTests (PR-PROVREG-164, 165, 167, 170), driven through Codex's app-server, whose reader is
// the compiled Codex stand-in (R-TEST-8) reached only through a custom binary path inside this run's folders (R-TEST-10).
// A reply file left out makes the stand-in stay silent, which is the hung reader the 25 second watchdog exists for.
// Process-heavy, so it runs with no other class in parallel (like PlanLimitsTests, ED-190).
[Collection(PlanLimitsProcessCollection.Name)]
public sealed class ProviderRegistryPlanLimitsCliReadTests
{
    private const ProviderKind Codex = ProviderKind.Codex;

    private static string Limits(double percent) =>
        "{\"rateLimits\":{\"planType\":\"pro\",\"primary\":{\"usedPercent\":" +
        percent.ToString(System.Globalization.CultureInfo.InvariantCulture) + ",\"windowDurationMins\":300}}}";

    // Synchronous on purpose: the runtime and its clock are scoped to the calling async flow.
    private static (TestRuntime Runtime, ProviderRegistry Registry, CodexScript Script) Arrange(bool answerLimits = true)
    {
        var runtime = new TestRuntime();
        var script = new CodexScript("limits-cli");
        script.SetResponse("initialize", "{}");
        if (answerLimits) script.SetResponse("account/rateLimits/read", Limits(25));
        var settings = new FakeProviderSettings();
        settings.Paths[Codex] = script.ExecutablePath.Replace('\\', '/');
        return (runtime, new ProviderRegistry(settings), script);
    }

    private static void Ask(TestRuntime runtime, ProviderRegistry registry, bool force = false) =>
        runtime.Loop.Run(() => MainActor.Run(() => registry.RefreshPlanLimits(Codex, force)));

    private static bool Loading(ProviderRegistry registry) => registry.LoadingLimits.Contains(Codex);

    private static double? Percent(ProviderRegistry registry) =>
        registry.PlanLimits.TryGetValue(Codex, out var limits) && limits.Windows.Count > 0 ? limits.Windows[0].Percent : null;

    private static Task Until(TestRuntime runtime, Func<bool> condition) =>
        Task.Run(() => runtime.Loop.RunUntil(condition, TimeSpan.FromSeconds(30)));

    /// <summary>Waits until launch <paramref name="launch"/> of the stand-in has received the rate-limits request (its third line).</summary>
    private static Task UntilRequested(TestRuntime runtime, CodexScript script, int launch) =>
        Until(runtime, () => script.MessageCount(launch) >= 3);

    private static async Task Quiet(TestRuntime runtime)
    {
        for (var round = 0; round < 2; round++)
        {
            runtime.RunUntilIdle();
            await Task.Delay(300);
        }
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-165")] // ProviderRegistry.swift:609, :638-639 (CLI branch: sixty second gate, stamped on success only)
    [Trait("Req", "PR-PROVREG-170")] // ProviderRegistry.swift:637-639 (CLI branch: a nil read keeps the previous limits and leaves the gate where it was)
    public async Task a_cli_read_is_gated_for_sixty_seconds_and_a_failed_one_keeps_the_previous_limits_and_the_gate()
    {
        var (runtime, registry, script) = Arrange();
        using var _ = runtime;
        Ask(runtime, registry);
        await Until(runtime, () => Percent(registry) == 25 && !Loading(registry));
        Assert.Single(script.Calls());

        runtime.Advance(TimeSpan.FromSeconds(59));
        Ask(runtime, registry); // 59 seconds old: served from what is held
        Assert.False(Loading(registry));
        await Quiet(runtime);
        Assert.Single(script.Calls());

        // The initialize reply fails, so the reader throws and the read returns nil.
        script.SetError("initialize", "app-server unavailable");
        runtime.Advance(TimeSpan.FromSeconds(1)); // exactly 60 seconds: allowed
        Ask(runtime, registry);
        await Until(runtime, () => script.Calls().Count >= 2 && !Loading(registry));
        Assert.Equal(2, script.Calls().Count);
        Assert.Equal(25, Percent(registry)); // the stale numbers stay

        // The failure did not stamp the read at 60 s: 61 seconds after the success an unforced ask is allowed again.
        File.Delete(Path.Combine(script.ControlDirectory, "error-initialize.json")); // the app-server answers again
        script.SetResponse("account/rateLimits/read", Limits(80));
        runtime.Advance(TimeSpan.FromSeconds(1));
        Ask(runtime, registry);
        await Until(runtime, () => Percent(registry) == 80 && !Loading(registry));
        Assert.Equal(3, script.Calls().Count);
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-164")] // ProviderRegistry.swift:608, :629, :643-647 (CLI branch: a forced ask during a hung read is queued and starts when the watchdog fires)
    [Trait("Req", "PR-PROVREG-167")] // ProviderRegistry.swift:629-631 (CLI branch: the watchdog is due at exactly 25 seconds and clears the loading flag)
    public async Task a_hung_cli_read_stops_loading_at_twenty_five_seconds_and_starts_the_queued_forced_read()
    {
        var (runtime, registry, script) = Arrange(answerLimits: false); // the rate-limits request is never answered
        using var _ = runtime;
        Ask(runtime, registry);
        await UntilRequested(runtime, script, 0);
        Assert.True(Loading(registry));

        Ask(runtime, registry, force: true); // queued behind the hung read
        runtime.Advance(TimeSpan.FromSeconds(24));
        await Quiet(runtime);
        Assert.True(Loading(registry)); // 24 seconds: the watchdog has not fired
        Assert.Single(script.Calls());

        runtime.Advance(TimeSpan.FromSeconds(1)); // 25 seconds: it fires, clears the flag and drains the queue
        await UntilRequested(runtime, script, 1);
        Assert.Equal(2, script.Calls().Count); // the queued forced read started although the first is still out
        Assert.True(Loading(registry)); // and it is marked loading in its turn
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-167")] // ProviderRegistry.swift:631 (CLI branch: the read's defer cancels its watchdog)
    public async Task a_cli_read_that_finishes_first_cancels_its_watchdog_so_a_later_read_is_not_cut_short()
    {
        var (runtime, registry, script) = Arrange();
        using var _ = runtime;
        Ask(runtime, registry); // read 1 finishes at once; its watchdog is due at 25 seconds
        await Until(runtime, () => Percent(registry) == 25 && !Loading(registry));

        runtime.Advance(TimeSpan.FromSeconds(10));
        File.Delete(Path.Combine(script.ControlDirectory, "response-account_rateLimits_read.json")); // read 2 will hang
        Ask(runtime, registry, force: true); // started at 10 s, its own watchdog due at 35 s
        await UntilRequested(runtime, script, 1);

        runtime.Advance(TimeSpan.FromSeconds(15)); // 25 seconds: read 1's watchdog would be due
        await Quiet(runtime);
        Assert.True(Loading(registry)); // cancelled, so read 2 is still marked loading

        runtime.Advance(TimeSpan.FromSeconds(10)); // 35 seconds: read 2's own watchdog
        Assert.False(Loading(registry));
    }
}
