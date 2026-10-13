using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Services.Tests.Assistant;

// Dextra AS-15 (DESIGN.md rev 5, 13.2, 15.3; ED-NEW-dextra-as15-3, -5): the day's spend that caps are checked against is written at most every 30
// seconds and when the store is let go, so a restart does not give the day back; the duties the breaker paused are written as soon as they change,
// so a restart does not let one run; a file from a newer version is never written over; a write that fails is reported and the app goes on.
[Trait("Feature", "AS-15")]
[Trait("Decision", "ED-NEW-dextra-as15-3")]
public sealed class CostPersistenceTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 13, 9, 0, 0, TimeSpan.Zero);

    private readonly AssistantTempRoot temp = new();

    public void Dispose() => temp.Dispose();

    private static CostCap Day() => new("c", CostCapScope.AssistantDay) { Percent = 15, AssistantId = "dextra" };

    private static ReservePersistence Persist(AssistantStore store, ManualTimeProvider clock, CostLedger costs, DutyHealthBook duties) =>
        new(store, new ReserveSettings(), new UsageCalibrator(clock), new TurnCostHistory(clock), clock, costs, duties);

    [Fact]
    public void The_days_spend_is_written_at_most_every_30_seconds_and_when_the_store_is_let_go()
    {
        var clock = new ManualTimeProvider(T0);
        var costs = new CostLedger(clock);
        using var store = AssistantStore.Open(temp.Roots);
        using var persistence = Persist(store, clock, costs, new DutyHealthBook());

        costs.AddPoints("dextra", "claude:20x", "Claude 20x", false, ReserveWindowKind.FiveHour, 2);
        costs.AddPoints("dextra", "claude:20x", "Claude 20x", false, ReserveWindowKind.FiveHour, 1);
        Assert.Null(store.Costs());                                         // not yet
        Assert.Equal(1, clock.ActiveTimers);                                // one timer for any number of changes
        clock.Advance(TimeSpan.FromSeconds(29));
        Assert.Null(store.Costs());
        clock.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(3, Reload(store, clock).UseOf(Day()).Points[("claude:20x", ReserveWindowKind.FiveHour)]);
        Assert.Equal(0, clock.ActiveTimers);

        costs.AddPoints("dextra", "claude:20x", "Claude 20x", false, ReserveWindowKind.FiveHour, 4);
        persistence.Flush();                                                // Flush writes at once
        Assert.Equal(7, Reload(store, clock).UseOf(Day()).Points[("claude:20x", ReserveWindowKind.FiveHour)]);
    }

    private static CostLedger Reload(AssistantStore store, ManualTimeProvider clock)
    {
        var ledger = new CostLedger(clock);
        ledger.Load(store.Costs());
        return ledger;
    }

    [Fact]
    public void A_restart_keeps_the_day_and_the_duties_the_breaker_paused()
    {
        var clock = new ManualTimeProvider(T0);
        using (var store = AssistantStore.Open(temp.Roots))
        {
            var costs = new CostLedger(clock);
            var duties = new DutyHealthBook();
            using var persistence = Persist(store, clock, costs, duties);
            costs.AddDollars("dextra", "openrouter:key", "OpenRouter", 1.25m);
            duties.Set(new DutyHealth("digest") { ConsecutiveFailures = 3, Paused = true, PausedAt = T0 });
        }                                                                   // letting go writes what is pending

        using var reopened = AssistantStore.Open(temp.Roots);
        var costs2 = new CostLedger(clock);
        var duties2 = new DutyHealthBook();
        using var again = Persist(reopened, clock, costs2, duties2);
        Assert.Equal(1.25m, costs2.UseOf(new CostCap("k", CostCapScope.ApiDay) { Dollars = 5m }).Dollars);
        Assert.True(duties2.Of("digest").Paused);
        Assert.Equal(["digest"], duties2.Paused.Select(h => h.DutyId));
    }

    [Fact]
    public void A_duty_the_breaker_paused_is_written_the_moment_it_changes()
    {
        var clock = new ManualTimeProvider(T0);
        var duties = new DutyHealthBook();
        using var store = AssistantStore.Open(temp.Roots);
        using var persistence = Persist(store, clock, new CostLedger(clock), duties);
        Assert.Null(store.DutyHealth());                                    // nothing was written for nothing
        duties.Set(new DutyHealth("digest") { ConsecutiveFailures = 3, Paused = true, PausedAt = T0 });
        persistence.Flush();                                                // the write was handed to the pool; Flush waits for it
        Assert.Single(store.DutyHealth()!["duties"]!.AsArray());
        duties.Set(DutyBreaker.Resume(duties.Of("digest")));
        persistence.Flush();
        Assert.Empty(store.DutyHealth()!["duties"]!.AsArray());
    }

    [Fact]
    public void A_file_from_a_newer_version_is_left_as_it_is()
    {
        var clock = new ManualTimeProvider(T0);
        using var store = AssistantStore.Open(temp.Roots);
        store.SaveCosts(new System.Text.Json.Nodes.JsonObject { ["version"] = CostLedger.FormatVersion + 1, ["days"] = new System.Text.Json.Nodes.JsonArray() });
        store.SaveDutyHealth(new System.Text.Json.Nodes.JsonObject { ["version"] = DutyHealthBook.FormatVersion + 1, ["duties"] = new System.Text.Json.Nodes.JsonArray() });
        var before = (File.ReadAllBytes(Path.Combine(store.Root, AssistantStore.CostsFile)), File.ReadAllBytes(Path.Combine(store.Root, AssistantStore.DutyHealthFile)));
        var costs = new CostLedger(clock);
        var duties = new DutyHealthBook();
        using (var persistence = Persist(store, clock, costs, duties))
        {
            costs.AddPoints("dextra", "claude:20x", "Claude 20x", false, ReserveWindowKind.FiveHour, 2);
            duties.Set(new DutyHealth("digest") { ConsecutiveFailures = 3, Paused = true, PausedAt = T0 });
            persistence.Flush();
        }
        Assert.Equal(before.Item1, File.ReadAllBytes(Path.Combine(store.Root, AssistantStore.CostsFile)));
        Assert.Equal(before.Item2, File.ReadAllBytes(Path.Combine(store.Root, AssistantStore.DutyHealthFile)));
    }

    [Fact]
    public void A_write_that_fails_is_reported_and_the_next_change_tries_again()
    {
        var clock = new ManualTimeProvider(T0);
        var costs = new CostLedger(clock);
        using var store = AssistantStore.Open(temp.Roots);
        using var persistence = Persist(store, clock, costs, new DutyHealthBook());
        var faults = new List<string>();
        persistence.Faulted += faults.Add;
        var target = Path.Combine(store.Root, AssistantStore.CostsFile);
        Directory.CreateDirectory(target);                                   // a folder where the file goes: the write cannot succeed
        costs.AddPoints("dextra", "claude:20x", "Claude 20x", false, ReserveWindowKind.FiveHour, 2);
        persistence.Flush();
        Assert.Contains(faults, line => line.StartsWith("What each assistant spent today could not be saved", StringComparison.Ordinal));
        Directory.Delete(target);
        persistence.Flush();                                                 // the failed write is still owed: it is made now
        Assert.Equal(2, Reload(store, clock).UseOf(Day()).Points[("claude:20x", ReserveWindowKind.FiveHour)]);
    }
}
