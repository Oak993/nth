using System.Text;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;
using Xunit;

namespace Marrow.Services.Tests.Providers;

// Source: DroppyCode/Services/Providers/ProviderRegistry.swift (B2 1fbf81e), refreshPlanLimits :604-641 and
// drainedForcedRead :643-647.
//
// Test-first proofs for the plan-limits read's queue, gate, watchdog, own-task and keep-previous rules
// (PR-PROVREG-164, 165, 167, 168, 170). Z.ai is the one limits provider whose reader is an HTTP call, so every
// read below goes to a loopback server through ZaiAPI.Endpoint (R-TEST-10); the CLI readers are never reached.
// The Z.ai quota call carries a 15 second idle limit on the deterministic clock, so a read held longer than the
// 25 second watchdog is kept alive by writing a response head and a body byte before each limit falls due.
public sealed class ProviderRegistryPlanLimitsReadTests
{
    private const ProviderKind Zai = ProviderKind.Zai;

    // Synchronous on purpose: the rig scopes its runtime and endpoints to the calling async flow.
    private static ProviderApiRig ZaiRig()
    {
        var rig = new ProviderApiRig();
        rig.Settings.Keys[Zai] = "zai-key";
        rig.Loopback.Route(ProviderApiRig.ZaiResetList, 404, "{}");
        return rig;
    }

    private static void Ask(ProviderApiRig rig, bool force = false) => rig.OnMain(() => rig.Registry.RefreshPlanLimits(Zai, force));

    // Plain reads: the loop thread evaluates these inside Until, and a getter only records an observation access.
    private static bool Loading(ProviderApiRig rig) => rig.Registry.LoadingLimits.Contains(Zai);

    private static double? Percent(ProviderApiRig rig) =>
        rig.Registry.PlanLimits.TryGetValue(Zai, out var limits) ? limits.Windows[0].Percent : (double?)null;

    /// <summary>Asks for a read and waits until its quota request has reached the loopback and is held there.</summary>
    private static async Task<LoopbackExchange> AskAndHold(ProviderApiRig rig, bool force = false)
    {
        var arrived = rig.Loopback.Count(ProviderApiRig.ZaiQuota) + 1;
        Ask(rig, force);
        var pump = rig.Until(() => rig.Loopback.Count(ProviderApiRig.ZaiQuota) >= arrived);
        var held = await rig.Loopback.NextIntercepted(ProviderApiRig.ZaiQuota);
        await pump;
        return held;
    }

    private static Task Answer(LoopbackExchange held, double percent) =>
        held.Respond(200, "OK", Encoding.UTF8.GetBytes(ProviderApiRig.ZaiQuotaBody(percent)));

    /// <summary>Lets anything the registry would still start get going and reach the loopback: pump, wait, pump, wait.</summary>
    private static async Task Quiet(ProviderApiRig rig)
    {
        for (var round = 0; round < 2; round++)
        {
            rig.Runtime.RunUntilIdle();
            await Task.Delay(300);
        }
    }

    // ---- PR-PROVREG-164: a forced read arriving while a read is out is queued and runs once afterwards ----------

    [Fact]
    [Trait("Req", "PR-PROVREG-164")] // ProviderRegistry.swift:608 (forced call inserts into forcedLimitReads), :643-647 drainedForcedRead
    public async Task a_forced_read_that_arrives_while_a_read_is_out_runs_exactly_once_when_that_read_finishes()
    {
        await using var rig = ZaiRig();
        rig.Loopback.Intercept(ProviderApiRig.ZaiQuota);
        var first = await AskAndHold(rig);
        Assert.True(Loading(rig));

        Ask(rig, force: true); // queued, not started: only one quota request is out
        Ask(rig, force: true); // the queue holds one entry per provider, so a second forced ask changes nothing
        await Quiet(rig);
        Assert.Equal(1, rig.Loopback.Count(ProviderApiRig.ZaiQuota));

        var pump = rig.Until(() => rig.Loopback.Count(ProviderApiRig.ZaiQuota) >= 2);
        await Answer(first, 10);
        var second = await rig.Loopback.NextIntercepted(ProviderApiRig.ZaiQuota); // started when the first one finished
        await pump;
        Assert.Equal(10, Percent(rig));
        await Answer(second, 20);
        await rig.Until(() => Percent(rig) == 20 && !Loading(rig));

        await Quiet(rig); // a third read would have been started by now
        Assert.Equal(2, rig.Loopback.Count(ProviderApiRig.ZaiQuota));
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-164")] // ProviderRegistry.swift:608 (`if force { forcedLimitReads.insert }` only for a forced call)
    public async Task an_unforced_read_that_arrives_while_a_read_is_out_is_dropped_not_queued()
    {
        await using var rig = ZaiRig();
        rig.Loopback.Intercept(ProviderApiRig.ZaiQuota);
        var first = await AskAndHold(rig);

        Ask(rig); // dropped
        await Answer(first, 10);
        await rig.Until(() => Percent(rig) == 10);
        await Quiet(rig); // a queued read would have started by now

        Assert.Equal(1, rig.Loopback.Count(ProviderApiRig.ZaiQuota));
        Assert.False(Loading(rig));
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-164")] // ProviderRegistry.swift:617 (watchdog calls drainedForcedRead), :643-647 (removal makes the defer's second call a no-op)
    public async Task a_queued_forced_read_also_starts_when_the_watchdog_fires_and_is_not_started_twice()
    {
        await using var rig = ZaiRig();
        rig.Loopback.Intercept(ProviderApiRig.ZaiQuota);
        var body = Encoding.UTF8.GetBytes(ProviderApiRig.ZaiQuotaBody(11));
        var first = await AskAndHold(rig);
        Ask(rig, force: true); // queued behind the read that is out

        // Keep the first request alive past the watchdog (see the class comment).
        rig.Runtime.Advance(TimeSpan.FromSeconds(10));
        await first.WriteHead(200, "OK", ("Content-Length", body.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        await Task.Delay(300);
        rig.Runtime.RunUntilIdle();
        rig.Runtime.Advance(TimeSpan.FromSeconds(10));
        await first.Write(body[..1]);
        await Task.Delay(300);
        rig.Runtime.RunUntilIdle();
        rig.Runtime.Advance(TimeSpan.FromSeconds(4));
        await Quiet(rig);
        Assert.Equal(1, rig.Loopback.Count(ProviderApiRig.ZaiQuota)); // still only the first read

        var pump = rig.Until(() => rig.Loopback.Count(ProviderApiRig.ZaiQuota) >= 2);
        rig.Runtime.Advance(TimeSpan.FromSeconds(1)); // 25 seconds: the watchdog fires and drains the queue
        var second = await rig.Loopback.NextIntercepted(ProviderApiRig.ZaiQuota);
        await pump;
        await Answer(second, 77);
        await rig.Until(() => Percent(rig) == 77 && !Loading(rig));

        // The first read finishes late: its defer drains again, but the entry is already gone.
        await first.Write(body[1..]);
        first.Finish();
        await rig.Until(() => Percent(rig) == 11);
        await Quiet(rig);
        Assert.Equal(2, rig.Loopback.Count(ProviderApiRig.ZaiQuota));
    }

    // ---- PR-PROVREG-165: an unforced read within sixty seconds of the last success is suppressed ----------------

    [Fact]
    [Trait("Req", "PR-PROVREG-165")] // ProviderRegistry.swift:609 (`Date.now.timeIntervalSince(fetched) < 60`), :622-623 (stamped on success)
    public async Task an_unforced_read_is_suppressed_until_sixty_seconds_after_the_last_successful_read()
    {
        await using var rig = ZaiRig();
        rig.Loopback.Route(ProviderApiRig.ZaiQuota, 200, ProviderApiRig.ZaiQuotaBody(10));
        Ask(rig);
        await rig.Until(() => Percent(rig) == 10 && !Loading(rig));
        Assert.Equal(1, rig.Loopback.Count(ProviderApiRig.ZaiQuota));

        rig.Loopback.Route(ProviderApiRig.ZaiQuota, 200, ProviderApiRig.ZaiQuotaBody(50));
        rig.Runtime.Advance(TimeSpan.FromSeconds(59));
        Ask(rig); // 59 seconds old: the cached limits are served, no read starts
        Assert.False(Loading(rig));
        await Quiet(rig);
        Assert.Equal(1, rig.Loopback.Count(ProviderApiRig.ZaiQuota));
        Assert.Equal(10, Percent(rig));

        rig.Runtime.Advance(TimeSpan.FromSeconds(1));
        Ask(rig); // exactly 60 seconds old is no longer "less than 60"
        await rig.Until(() => Percent(rig) == 50 && !Loading(rig));
        Assert.Equal(2, rig.Loopback.Count(ProviderApiRig.ZaiQuota));
    }

    // ---- PR-PROVREG-167: a 25 second watchdog clears the loading flag but does not cancel the read -------------

    [Fact]
    [Trait("Req", "PR-PROVREG-167")] // ProviderRegistry.swift:617 and :619-623 (watchdog at 25 s; the read is not cancelled, a late result still lands)
    [Trait("Req", "PR-PROVREG-168")] // ProviderRegistry.swift:618 (the read keeps running on its own task after the watchdog has fired)
    public async Task a_read_held_past_twenty_five_seconds_stops_loading_but_still_delivers_its_late_result()
    {
        await using var rig = ZaiRig();
        rig.Loopback.Intercept(ProviderApiRig.ZaiQuota);
        var body = Encoding.UTF8.GetBytes(ProviderApiRig.ZaiQuotaBody(11));
        var first = await AskAndHold(rig);

        // Keep the request alive on the deterministic clock: a head at 10 s and a body byte at 20 s each re-arm
        // the 15 second idle limit past the watchdog's 25 seconds.
        rig.Runtime.Advance(TimeSpan.FromSeconds(10));
        await first.WriteHead(200, "OK", ("Content-Length", body.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        await Task.Delay(300);
        rig.Runtime.RunUntilIdle(); // the client's continuation runs on the main loop: only now does it see the head
        rig.Runtime.Advance(TimeSpan.FromSeconds(10));
        await first.Write(body[..1]);
        await Task.Delay(300);
        rig.Runtime.RunUntilIdle();

        rig.Runtime.Advance(TimeSpan.FromSeconds(4)); // 24 seconds
        Assert.True(Loading(rig));
        rig.Runtime.Advance(TimeSpan.FromSeconds(1)); // 25 seconds: the watchdog fires
        Assert.False(Loading(rig));
        Assert.Null(Percent(rig)); // the read itself has not delivered anything yet

        // A later read is allowed although the first is still running.
        var second = await AskAndHold(rig);
        Assert.True(Loading(rig));
        await Answer(second, 99);
        await rig.Until(() => Percent(rig) == 99 && !Loading(rig));

        // The first read was never cancelled: its late result still writes the limits (and the timestamp).
        await first.Write(body[1..]);
        first.Finish();
        await rig.Until(() => Percent(rig) == 11);
        Ask(rig); // the late success stamped the read, so this unforced ask is inside the sixty second gate
        Assert.False(Loading(rig));
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-167")] // ProviderRegistry.swift:619 (the read's defer cancels its watchdog)
    public async Task a_read_that_finishes_first_cancels_its_watchdog_so_a_later_read_is_not_cut_short()
    {
        await using var rig = ZaiRig();
        rig.Loopback.Route(ProviderApiRig.ZaiQuota, 200, ProviderApiRig.ZaiQuotaBody(10));
        Ask(rig); // read 1 finishes at once; its watchdog is due at 25 seconds
        await rig.Until(() => Percent(rig) == 10 && !Loading(rig));

        rig.Loopback.Intercept(ProviderApiRig.ZaiQuota);
        rig.Runtime.Advance(TimeSpan.FromSeconds(10));
        var body = Encoding.UTF8.GetBytes(ProviderApiRig.ZaiQuotaBody(30));
        var second = await AskAndHold(rig, force: true); // started at 10 s, its own watchdog due at 35 s
        await second.WriteHead(200, "OK", ("Content-Length", body.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        await Task.Delay(300);
        rig.Runtime.RunUntilIdle();
        rig.Runtime.RunUntilIdle(); // the client's continuation runs on the main loop: only now does it see the head
        rig.Runtime.Advance(TimeSpan.FromSeconds(10));
        await second.Write(body[..1]);
        await Task.Delay(300);
        rig.Runtime.RunUntilIdle();

        rig.Runtime.Advance(TimeSpan.FromSeconds(5)); // 25 seconds: read 1's watchdog would be due
        Assert.True(Loading(rig)); // cancelled, so read 2 is still marked loading

        await second.Write(body[1..]);
        second.Finish();
        await rig.Until(() => Percent(rig) == 30 && !Loading(rig));
    }

    // ---- PR-PROVREG-168: the read runs on its own task, so the requester going away cannot cancel it -----------

    [Fact]
    [Trait("Req", "PR-PROVREG-168")] // ProviderRegistry.swift:604-605 (doc comment), :618 and :630 (an unstructured Task around the read)
    public async Task a_read_survives_the_cancellation_of_the_task_that_asked_for_it_and_stores_its_result()
    {
        await using var rig = ZaiRig();
        rig.Loopback.Intercept(ProviderApiRig.ZaiQuota);

        // The asking view is a task of its own; it asks for the read and is then cancelled (the popover closing).
        TaskHandle? asker = null;
        rig.OnMain(() => asker = MainActor.Spawn(async () =>
        {
            rig.Registry.RefreshPlanLimits(Zai);
            await SwiftTask.TrySleep(TimeSpan.FromHours(1));
        }));
        var pump = rig.Until(() => rig.Loopback.Count(ProviderApiRig.ZaiQuota) >= 1);
        var held = await rig.Loopback.NextIntercepted(ProviderApiRig.ZaiQuota);
        await pump;
        asker!.Cancel();
        rig.Runtime.RunUntilIdle();
        await Task.Delay(200);

        Assert.True(Loading(rig)); // the read is still out
        await Answer(held, 42);
        await rig.Until(() => Percent(rig) == 42 && !Loading(rig));
    }

    // ---- PR-PROVREG-170: a read that returns nothing leaves the previous limits and timestamp untouched --------

    [Fact]
    [Trait("Req", "PR-PROVREG-170")] // ProviderRegistry.swift:621-623 and :637-639 (guard non-nil before planLimits and limitsFetchedAt are written)
    public async Task a_failed_read_keeps_the_previous_limits_and_does_not_advance_the_sixty_second_gate()
    {
        await using var rig = ZaiRig();
        rig.Loopback.Route(ProviderApiRig.ZaiQuota, 200, ProviderApiRig.ZaiQuotaBody(25));
        Ask(rig);
        await rig.Until(() => Percent(rig) == 25 && !Loading(rig));
        var before = rig.Registry.PlanLimits[Zai];

        rig.Runtime.Advance(TimeSpan.FromSeconds(30));
        rig.Loopback.Route(ProviderApiRig.ZaiQuota, 500, "{}");
        Ask(rig, force: true); // fails: the reader returns nil
        await rig.Until(() => rig.Loopback.Count(ProviderApiRig.ZaiQuota) >= 2 && !Loading(rig));
        Assert.Equal(before, rig.Registry.PlanLimits[Zai]); // the stale numbers stay on screen

        // The failure did not stamp the read at 30 s: 61 seconds after the success an unforced ask is allowed.
        // (Had it stamped, that ask would be 31 seconds old and suppressed.)
        rig.Loopback.Route(ProviderApiRig.ZaiQuota, 200, ProviderApiRig.ZaiQuotaBody(80));
        rig.Runtime.Advance(TimeSpan.FromSeconds(31));
        Ask(rig);
        await rig.Until(() => Percent(rig) == 80 && !Loading(rig));
        Assert.Equal(3, rig.Loopback.Count(ProviderApiRig.ZaiQuota));
    }
}
