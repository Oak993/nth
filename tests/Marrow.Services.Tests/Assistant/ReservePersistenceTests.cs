using System.Text.Json.Nodes;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Services.Tests.Assistant;

// Dextra AS-13 (DESIGN.md rev 5, 11.7, 15.3; ED-NEW-dextra-as13-7): the owner's rules are written the moment he changes them; what nth
// learned is written at most every 30 seconds and when the store is let go; a file from a newer version is never written over; a
// write that fails is reported and the app goes on.
public sealed class ReservePersistenceTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 12, 9, 0, 0, TimeSpan.Zero);

    private readonly AssistantTempRoot temp = new();

    public void Dispose() => temp.Dispose();

    private static ReserveRule Always(string name, double keep) =>
        new("", ReserveScope.Named(name), ReserveLifetime.Standing) { KeepPercent = keep };

    [Fact]
    [Trait("Feature", "AS-13")]
    [Trait("Decision", "ED-NEW-dextra-as13-7")]
    public void The_rules_load_at_the_start_and_are_written_the_moment_they_change()
    {
        var clock = new ManualTimeProvider(T0);
        using (var store = AssistantStore.Open(temp.Roots))
        {
            using var first = new ReservePersistence(store, new ReserveSettings(), new UsageCalibrator(clock), new TurnCostHistory(clock), clock);
        }
        using var reopened = AssistantStore.Open(temp.Roots);
        var settings = new ReserveSettings();
        using (var persistence = new ReservePersistence(reopened, settings, new UsageCalibrator(clock), new TurnCostHistory(clock), clock))
        {
            Assert.Empty(settings.Rules);
            Assert.Null(reopened.Reserves());                       // nothing was written for nothing
            Assert.Null(settings.Add(Always("Claude 20x", 30)));
            Assert.Single(reopened.Reserves()!["rules"]!.AsArray());           // at once, with no wait
            settings.SetHoldReserves(true);
            Assert.True(reopened.Reserves()!["holdReserves"]!.GetValue<bool>());
        }
        var again = new ReserveSettings();
        using var third = new ReservePersistence(reopened, again, new UsageCalibrator(clock), new TurnCostHistory(clock), clock);
        Assert.Equal(["Always keep 30% on Claude 20x"], again.Rules.Select(ReserveWords.Title));
        Assert.True(again.HoldReserves);
    }

    [Fact]
    [Trait("Feature", "AS-13")]
    [Trait("Decision", "ED-NEW-dextra-as13-7")]
    public void What_nth_learned_is_written_at_most_every_30_seconds_and_when_it_lets_go()
    {
        var clock = new ManualTimeProvider(T0);
        var calibrator = new UsageCalibrator(clock);
        var turns = new TurnCostHistory(clock);
        using var store = AssistantStore.Open(temp.Roots);
        using var persistence = new ReservePersistence(store, new ReserveSettings(), calibrator, turns, clock);

        turns.Record("opus", 100_000, T0);
        turns.Record("opus", 120_000, T0);
        Assert.Null(store.Calibration());                           // not yet
        Assert.Equal(1, clock.ActiveTimers);                        // one timer for any number of changes
        clock.Advance(TimeSpan.FromSeconds(29));
        Assert.Null(store.Calibration());
        clock.Advance(TimeSpan.FromSeconds(2));
        var written = store.Calibration()!;
        Assert.Equal(2, written["turns"]!.AsArray().Count);
        Assert.Equal(0, clock.ActiveTimers);

        // Nothing changed: nothing is written again; a change after it starts the next 30 seconds.
        var before = File.GetLastWriteTimeUtc(Path.Combine(store.Root, AssistantStore.CalibrationFile));
        persistence.Flush();
        Assert.Equal(before, File.GetLastWriteTimeUtc(Path.Combine(store.Root, AssistantStore.CalibrationFile)));
        turns.Record("opus", 90_000, T0);
        persistence.Flush();                                        // Flush writes at once
        Assert.Equal(3, store.Calibration()!["turns"]!.AsArray().Count);
    }

    [Fact]
    [Trait("Feature", "AS-13")]
    [Trait("Decision", "ED-NEW-dextra-as13-7")]
    public void Letting_go_writes_what_is_pending_and_stops_listening()
    {
        var clock = new ManualTimeProvider(T0);
        var turns = new TurnCostHistory(clock);
        var settings = new ReserveSettings();
        using var store = AssistantStore.Open(temp.Roots);
        var persistence = new ReservePersistence(store, settings, new UsageCalibrator(clock), turns, clock);
        turns.Record("opus", 100_000, T0);
        persistence.Dispose();
        Assert.Single(store.Calibration()!["turns"]!.AsArray());
        Assert.Equal(0, clock.ActiveTimers);
        persistence.Dispose();                                      // twice is once

        turns.Record("opus", 200_000, T0);                          // after it let go: not written
        settings.Add(Always("Claude 20x", 30));
        Assert.Single(store.Calibration()!["turns"]!.AsArray());
        Assert.Null(store.Reserves());
    }

    [Fact]
    [Trait("Feature", "AS-13")]
    [Trait("Decision", "ED-NEW-dextra-as13-7")]
    public void A_file_saved_by_a_newer_version_is_never_written_over()
    {
        var newerRules = new JsonObject { ["version"] = ReserveSettings.FormatVersion + 1, ["rules"] = new JsonArray(), ["somethingNew"] = true };
        var newerCalibration = new JsonObject { ["version"] = UsageCalibrator.FormatVersion + 1, ["intervals"] = new JsonArray(), ["somethingNew"] = true };
        var clock = new ManualTimeProvider(T0);
        using var store = AssistantStore.Open(temp.Roots);
        store.SaveReserves(newerRules);
        store.SaveCalibration(newerCalibration);
        var rulesPath = Path.Combine(store.Root, AssistantStore.ReservesFile);
        var calibrationPath = Path.Combine(store.Root, AssistantStore.CalibrationFile);
        var rulesBytes = File.ReadAllBytes(rulesPath);
        var calibrationBytes = File.ReadAllBytes(calibrationPath);

        var settings = new ReserveSettings();
        var turns = new TurnCostHistory(clock);
        using (var persistence = new ReservePersistence(store, settings, new UsageCalibrator(clock), turns, clock))
        {
            Assert.True(settings.ReadOnly);
            Assert.NotNull(settings.Add(Always("Claude 20x", 30)));  // refused: "saved by a newer version"
            turns.Record("opus", 100_000, T0);
            persistence.Flush();
        }
        Assert.Equal(rulesBytes, File.ReadAllBytes(rulesPath));
        Assert.Equal(calibrationBytes, File.ReadAllBytes(calibrationPath));
    }

    [Fact]
    [Trait("Feature", "AS-13")]
    [Trait("Decision", "ED-NEW-dextra-as13-7")]
    public void A_write_that_fails_is_reported_not_thrown_and_the_next_change_tries_again()
    {
        var clock = new ManualTimeProvider(T0);
        var turns = new TurnCostHistory(clock);
        var settings = new ReserveSettings();
        var store = AssistantStore.Open(temp.Roots);
        using var persistence = new ReservePersistence(store, settings, new UsageCalibrator(clock), turns, clock);
        var faults = new List<string>();
        persistence.Faulted += faults.Add;
        store.Dispose();                                            // the store is gone: every write fails

        Assert.Null(settings.Add(Always("Claude 20x", 30)));        // the owner's rule is kept in memory all the same
        Assert.Single(faults);
        Assert.StartsWith("The reserve rules could not be saved: ", faults[0], StringComparison.Ordinal);
        turns.Record("opus", 100_000, T0);
        persistence.Flush();
        Assert.Equal(2, faults.Count);
        Assert.StartsWith("What nth learned about your usage could not be saved: ", faults[1], StringComparison.Ordinal);
        Assert.Single(settings.Rules);
    }
}
