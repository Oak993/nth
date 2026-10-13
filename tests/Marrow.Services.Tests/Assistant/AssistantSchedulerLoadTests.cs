using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Assistant;

// Dextra AS-11 (DESIGN.md rev 5, 7.1, 15.3; ED-NEW-dextra-as11-8): the scheduler saves every duty it holds for an assistant. Start loads only the assistants it is given, so a duty
// added to another assistant must load that assistant's file first: otherwise the save writes the new duty alone and the file's own duties are lost. The Morning digest duty
// is made for Dextra whether or not Start was given Dextra's id, so this is what keeps the owner's duties safe beside it.
[Trait("Feature", "AS-11")]
[Trait("Decision", "ED-NEW-dextra-as11-8")]
public sealed class AssistantSchedulerLoadTests
{
    private static SchedulerRig Unlisted()
    {
        var rig = new SchedulerRig { Assistants = [] };
        rig.Store.Put("dextra", SchedulerRig.Daily("d1", 7));
        rig.Begin();
        return rig;
    }

    [Fact]
    public void A_duty_added_to_an_assistant_start_was_not_given_is_saved_beside_the_duties_its_file_holds()
    {
        using var rig = Unlisted();
        Assert.Empty(rig.Scheduler!.Duties);                                    // Start was given no assistant
        var (saved, problem) = rig.Scheduler.Add(SchedulerRig.Daily("d2", 9));
        Assert.Null(problem);
        Assert.Equal("d2", saved!.Id);
        Assert.Equal(["d1", "d2"], rig.Scheduler.Duties.Select(v => v.Duty.Id).Order(StringComparer.Ordinal));
        Assert.Equal(["d1", "d2"], rig.Store.Saved("dextra").Duties.Select(d => d.Id).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void The_duties_loaded_that_way_are_armed_and_run_on_their_time()
    {
        using var rig = Unlisted();
        Assert.Null(rig.Scheduler!.Add(SchedulerRig.Daily("d2", 9)).Problem);
        rig.Advance(TimeSpan.FromHours(1));                                    // 07:00: the file's own duty
        Assert.Contains(rig.Launcher.Fires, fire => fire.Duty.Id == "d1");
        rig.Advance(TimeSpan.FromHours(2));                                    // 09:00: the new one
        Assert.Contains(rig.Launcher.Fires, fire => fire.Duty.Id == "d2");
    }

    [Fact]
    public void A_duty_with_the_id_of_one_in_the_file_is_refused_and_nothing_is_lost()
    {
        using var rig = Unlisted();
        var (saved, problem) = rig.Scheduler!.Add(SchedulerRig.Daily("d1", 9));
        Assert.Null(saved);
        Assert.Equal("A duty with that id exists already.", problem);
        Assert.Equal(["d1"], rig.Scheduler.Duties.Select(v => v.Duty.Id));
        Assert.Equal(7, ((TimeTrigger)rig.Store.Saved("dextra").Duties.Single().Trigger).Schedule.TimeOfDay.Hour);
    }

    [Fact]
    public void Ensure_loaded_loads_an_assistant_once_and_leaves_one_that_start_loaded_alone()
    {
        using var rig = Unlisted();
        rig.Scheduler!.EnsureLoaded("dextra");
        rig.Scheduler.EnsureLoaded("dextra");
        Assert.Equal(["d1"], rig.Scheduler.Duties.Select(v => v.Duty.Id));
        using var listed = new SchedulerRig();
        listed.Store.Put("dextra", SchedulerRig.Daily("d1", 7));
        listed.Begin();
        var saves = listed.Store.Saves;
        listed.Scheduler!.EnsureLoaded("dextra");
        Assert.Equal(saves, listed.Store.Saves);
        Assert.Single(listed.Scheduler.Duties);
    }
}
