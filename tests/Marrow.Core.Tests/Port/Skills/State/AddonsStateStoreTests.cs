using Marrow.Core.Port.Skills.State;

namespace Marrow.Core.Tests.Port.Skills.State;

// Unit SK-7: state.json on disk (ONDEMAND-PLAN.md 3.1 names the file under addons\). The store takes the folder it is given and has no default, so a
// test can never reach the real data folder (R-TEST-9); a corrupt file fails closed and is never replaced by an empty state. ONDEMAND-PLAN.md DL-19:
// "app restart during a download resumes from the .part on the next Download press, never on its own", so a state read from disk settles every
// in-flight item (ED-295).
[Trait("Spec", "DL-7")]
public sealed class AddonsStateStoreTests
{
    [Fact]
    [Trait("Feature", "SK-7")]
    [Trait("Rule", "R-TEST-9")]
    public void Saving_then_loading_gives_the_same_state_and_the_file_is_state_json()
    {
        var folder = StateFixtures.ScratchFolder();
        var path = AddonsStateStore.PathIn(folder);
        Assert.Equal(Path.Combine(folder, "state.json"), path);
        var state = StateFixtures.Full();
        AddonsStateStore.Save(path, state);
        Assert.Equal(StateFixtures.Text(state), File.ReadAllText(path));
        Assert.Equal(StateFixtures.Text(state), StateFixtures.Text(AddonsStateStore.Load(path)));
        Assert.Equal(["state.json"], Directory.GetFiles(folder).Select(f => Path.GetFileName(f)!).ToArray());
    }

    [Fact]
    [Trait("Feature", "SK-7")]
    [Trait("Rule", "R-TEST-9")]
    public void A_missing_file_is_a_first_run_and_loads_as_the_empty_state_without_creating_it()
    {
        var folder = StateFixtures.ScratchFolder();
        var path = AddonsStateStore.PathIn(folder);
        Assert.Equal(StateFixtures.Text(AddonsState.Empty), StateFixtures.Text(AddonsStateStore.Load(path)));
        Assert.False(File.Exists(path));
    }

    [Fact]
    [Trait("Feature", "SK-7")]
    [Trait("Rule", "R-TEST-9")]
    public void A_corrupt_or_foreign_file_fails_closed_and_is_left_exactly_as_it_was()
    {
        var folder = StateFixtures.ScratchFolder();
        var path = AddonsStateStore.PathIn(folder);
        foreach (var bad in new[] { "", "not json", "{\"schema\":2,\"items\":[],\"acceptedTerms\":[],\"declined\":[],\"found\":[]}", "{\"schema\":1}", "\0\0\0" })
        {
            File.WriteAllText(path, bad);
            Assert.Throws<StateFormatException>(() => AddonsStateStore.Load(path));
            Assert.Equal(bad, File.ReadAllText(path));
        }
        File.WriteAllBytes(path, [0xFF, 0xFE, 0x00, 0x80]);
        Assert.Throws<StateFormatException>(() => AddonsStateStore.Load(path));
        Assert.Equal([0xFF, 0xFE, 0x00, 0x80], File.ReadAllBytes(path));
    }

    [Fact]
    [Trait("Feature", "SK-7")]
    [Trait("Rule", "R-TEST-9")]
    public void A_directory_in_the_way_or_a_missing_parent_is_an_error_and_a_relative_path_is_refused()
    {
        var folder = StateFixtures.ScratchFolder();
        Assert.Throws<DirectoryNotFoundException>(() => AddonsStateStore.Save(Path.Combine(folder, "nope", "state.json"), AddonsState.Empty));
        Assert.False(Directory.Exists(Path.Combine(folder, "nope")));
        Assert.Throws<ArgumentException>(() => AddonsStateStore.Save("state.json", AddonsState.Empty));
        Assert.Throws<ArgumentException>(() => AddonsStateStore.Load("state.json"));
        Assert.Throws<ArgumentException>(() => AddonsStateStore.PathIn("addons"));
        Assert.Throws<ArgumentException>(() => AddonsStateStore.PathIn(""));
    }

    [Fact]
    [Trait("Feature", "SK-7")]
    [Trait("Rule", "R-TEST-9")]
    public void The_store_has_no_member_that_picks_a_folder_for_the_caller()
    {
        foreach (var method in typeof(AddonsStateStore).GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.DeclaredOnly))
            Assert.Contains(method.GetParameters(), p => p.ParameterType == typeof(string));
        Assert.Empty(typeof(AddonsStateStore).GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static));
    }

    [Fact]
    [Trait("Feature", "SK-7")]
    [Trait("Rule", "R-TEST-4")]
    public void After_a_restart_every_in_flight_item_settles_by_the_cancel_rule_and_nothing_resumes_by_itself()
    {
        var state = AddonsState.Restore(
        [
            StateFixtures.Rec("T1", ItemState.Downloading, null, "3.14.8"),
            StateFixtures.Rec("T2", ItemState.Queued, "24.20.0", "24.21.0"),
            StateFixtures.Rec("T3", ItemState.Checking, "26.2.6", "26.2.6"),
            StateFixtures.Rec("T4", ItemState.Failed, null, "26.09.0", "Needs 1.2 GB free on C:."),
            StateFixtures.Rec("T5", ItemState.Installed, "12.4.2"),
            StateFixtures.Rec("T6", ItemState.Removing, "3.12"),
            StateFixtures.Rec("T7", ItemState.Damaged, "2.56.0"),
            StateFixtures.Rec("T8", ItemState.UpdateAvailable, "2.101.0", "2.102.0"),
        ], [], [], []);
        var settled = state.AfterRestart();
        Assert.Equal(ItemState.Available, settled.Get("T1").State);
        Assert.Equal(StateFixtures.Rec("T2", ItemState.UpdateAvailable, "24.20.0", "24.21.0"), settled.Get("T2"));
        Assert.Equal(StateFixtures.Rec("T3", ItemState.Damaged, "26.2.6"), settled.Get("T3"));
        // A failure stays on screen with its Retry (3.9); a removal stays pending for the next GC (3.8); the rest is untouched.
        Assert.Equal(state.Get("T4"), settled.Get("T4"));
        Assert.Equal(state.Get("T5"), settled.Get("T5"));
        Assert.Equal(state.Get("T6"), settled.Get("T6"));
        Assert.Equal(state.Get("T7"), settled.Get("T7"));
        Assert.Equal(state.Get("T8"), settled.Get("T8"));
        Assert.Equal(StateFixtures.Text(settled), StateFixtures.Text(settled.AfterRestart()));
    }

    [Fact]
    [Trait("Feature", "SK-7")]
    [Trait("Rule", "R-TEST-9")]
    public void Load_settles_what_it_reads_and_Save_writes_the_state_as_given()
    {
        var folder = StateFixtures.ScratchFolder();
        var path = AddonsStateStore.PathIn(folder);
        AddonsStateStore.Save(path, AddonsState.Empty.Transition("T1", ItemState.Downloading, "3.14.8"));
        Assert.Contains("\"downloading\"", File.ReadAllText(path), StringComparison.Ordinal);
        Assert.Equal(ItemState.Available, AddonsStateStore.Load(path).Get("T1").State);
        Assert.Contains("\"downloading\"", File.ReadAllText(path), StringComparison.Ordinal);
    }
}
