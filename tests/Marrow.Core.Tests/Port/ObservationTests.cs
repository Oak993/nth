using Marrow.Core.Port;
using Marrow.Core.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Core.Tests.Port;

// R-OBS rules (IMPLEMENTING.md 3.7). Source: draft C (proto-C ObservationTests) and draft B (isolation, SetAlways).
public sealed class ObservationTests
{
    private static readonly Guid A = Guid.Parse("00000000-0000-0000-0000-00000000000A");
    private static readonly Guid B = Guid.Parse("00000000-0000-0000-0000-00000000000B");

    [Fact]
    [Trait("Rule", "R-OBS-2")]
    [Trait("Decision", "ED-070")]
    public void Loading_settings_writes_nothing_back_because_didSet_is_silent_in_init()
    {
        using var runtime = new TestRuntime();
        var settings = new SettingsSample();
        Assert.True(settings.NotifyWhenFinished);   // absent: object(forKey:) as? Bool ?? true
        Assert.False(settings.SidebarActivityView); // absent: bool(forKey:) is false
        Assert.Empty(runtime.Defaults.Writes);
    }

    [Fact]
    [Trait("Rule", "R-OBS-2")]
    [Trait("Rule", "R-OBS-3")]
    [Trait("Oracle", "O-05")]
    public void DidSet_runs_for_an_equal_value_but_bindings_are_not_told()
    {
        using var runtime = new TestRuntime();
        var settings = new SettingsSample();
        using var log = new ChangeLog(settings);
        settings.NotifyWhenFinished = true; // equal to the loaded value
        Assert.Equal(["notifyWhenFinished"], runtime.Defaults.Writes);
        Assert.Empty(log.Names);
        settings.NotifyWhenFinished = false;
        Assert.Equal(["NotifyWhenFinished"], log.Names);
        Assert.Equal(false, runtime.Defaults.Object("notifyWhenFinished"));
        settings.DefaultProvider = ProviderKindSample.OpencodeGo;
        Assert.Equal("opencodeGo", runtime.Defaults.Object("defaultProvider"));
    }

    [Fact]
    [Trait("Rule", "R-OBS-3")]
    public void A_non_Equatable_value_notifies_on_every_set()
    {
        using var runtime = new TestRuntime();
        var count = runtime.OnMain(() =>
        {
            var cell = new ObservedValueSample<int>(1);
            var notified = 0;
            cell.PropertyChanged += (_, _) => notified++;
            cell.Value = 1;
            cell.Value = 1;
            return notified;
        });
        Assert.Equal(2, count);
    }

    [Fact]
    [Trait("Rule", "R-OBS-6")]
    public async Task A_main_actor_model_mutated_off_main_traps()
    {
        using var runtime = new TestRuntime();
        var model = runtime.OnMain(() => new MainActorCounterSample());
        runtime.OnMain(() => model.Count = 1);
        await Assert.ThrowsAsync<SwiftTrapException>(() => Task.Run(() => model.Count = 2));
    }

    [Fact]
    [Trait("Rule", "R-OBS-4")]
    public void A_computed_property_follows_its_own_cell_and_ignores_sibling_cells()
    {
        using var runtime = new TestRuntime();
        runtime.OnMain(() =>
        {
            var model = new ThreadListModel();
            model.Load([new(A, "Alpha"), new(B, "Beta")]);
            model.SelectedThreadID = A;
            using var log = new ChangeLog(model);
            Assert.Equal("Alpha", model.SelectedTitle); // a binding reads it, which arms tracking

            model.UpdateThread(B, t => t with { Title = "Beta 2" }); // a sibling changes
            Assert.DoesNotContain("SelectedTitle", log.Names);
            Assert.DoesNotContain("SelectedThread", log.Names);

            model.UpdateThread(A, t => t with { Title = "Alpha 2" }); // the selected item changes
            Assert.Contains("SelectedTitle", log.Names);
            Assert.Equal("Alpha 2", model.SelectedTitle);
        });
    }

    [Fact]
    [Trait("Rule", "R-OBS-4")]
    public void WithTracking_fires_once_and_only_for_what_was_read()
    {
        using var runtime = new TestRuntime();
        runtime.OnMain(() =>
        {
            var first = new ObservedValueSample<int>(1);
            var second = new ObservedValueSample<int>(2);
            var fired = 0;
            var value = ObservationTracking.WithTracking(() => first.Value * 10, () => fired++);
            Assert.Equal(10, value);
            second.Value = 3;
            Assert.Equal(0, fired);
            first.Value = 5;
            first.Value = 6;
            Assert.Equal(1, fired);
            Assert.Equal(0, first.TrackerCount("Value"));
        });
    }

    [Fact]
    [Trait("Rule", "R-VAL-1")]
    [Trait("Rule", "R-OBS-1")]
    public void UpdateThread_on_immutable_records_saves_once_and_skips_a_no_op()
    {
        using var runtime = new TestRuntime();
        runtime.OnMain(() =>
        {
            var model = new ThreadListModel();
            model.Load([new(A, "Alpha")]);
            model.UpdateThread(A, t => t with { Title = "Alpha" });
            Assert.Equal(0, model.ScheduledSaves);
            model.UpdateThread(A, t => t with { Title = "Renamed" });
            Assert.Equal(1, model.ScheduledSaves);
            Assert.Equal("Renamed", model.Thread(A)!.Title);
        });
    }

    [Fact]
    [Trait("Rule", "R-VAL-1")]
    public void A_mutable_class_alias_makes_the_same_code_skip_the_save()
    {
        // The pitfall: `var thread = threads[i]` is an alias, so the change is already in the list when
        // `thread != threads[i]` is checked, and scheduleSave() never runs.
        var threads = new List<MutableThread> { new() { Title = "Alpha" } };
        var saves = 0;
        var thread = threads[0];
        thread.Title = "Renamed";
        if (!ReferenceEquals(thread, threads[0]) || thread.Title != threads[0].Title) saves++;
        Assert.Equal(0, saves);
    }
}
