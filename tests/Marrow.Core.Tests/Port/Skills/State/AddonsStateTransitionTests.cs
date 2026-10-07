using Marrow.Core.Port.Skills.State;

namespace Marrow.Core.Tests.Port.Skills.State;

// Unit SK-7 (DL-7 of porting/first-release/skills-ONDEMAND-PLAN.md section 7): "item states (available, queued, downloading, checking, installed,
// update available, failed, damaged, removing) and their legal transitions". The edges are the plan's flows: 5.4 "asked -> downloading -> checking
// -> done; downloading or checking -> failed -> (Retry) downloading; a cancel returns to asked"; 3.2 "Cancel keeps the .part for a later resume";
// 3.5 "a mismatch marks the item damaged ... Repair (a fresh download of that version)"; 3.7 "Update available with old -> new"; 3.8 removing;
// 3.9 failure and Retry; the OD-0099 review item 3 (a failed item may be checked after a manual file placement). Where the plan is silent the
// edge is an ENGINEERING DECISION (ED-295, listed there). The oracle below is written out by hand and never calls the code under test.
[Trait("Spec", "DL-7")]
public sealed class AddonsStateTransitionTests
{
    private static readonly ItemState[] All = Enum.GetValues<ItemState>();

    private static bool Cancel(ItemRecord from, ItemState to) =>
        (to == ItemState.Available && from.InstalledPin is null)
        || (to == ItemState.UpdateAvailable && from.InstalledPin is not null && from.TargetPin != from.InstalledPin)
        || (to == ItemState.Damaged && from.InstalledPin is not null && from.TargetPin == from.InstalledPin);

    private static bool Expected(ItemRecord from, ItemState to) => from.State switch
    {
        ItemState.Available => to is ItemState.Queued or ItemState.Downloading,
        ItemState.Queued => to is ItemState.Downloading or ItemState.Failed || Cancel(from, to),
        ItemState.Downloading => to is ItemState.Checking or ItemState.Failed || Cancel(from, to),
        ItemState.Checking => to is ItemState.Installed or ItemState.Failed,
        ItemState.Installed => to is ItemState.UpdateAvailable or ItemState.Damaged or ItemState.Removing,
        ItemState.UpdateAvailable => to is ItemState.UpdateAvailable or ItemState.Installed or ItemState.Queued or ItemState.Downloading or ItemState.Damaged or ItemState.Removing,
        ItemState.Failed => to is ItemState.Queued or ItemState.Downloading or ItemState.Checking || Cancel(from, to) || (to == ItemState.Removing && from.InstalledPin is not null),
        ItemState.Damaged => to is ItemState.Queued or ItemState.Downloading or ItemState.Removing,
        ItemState.Removing => to == ItemState.Available,
    };

    // The extra arguments a legal edge needs (the rules of Apply, tested on their own below).
    private static (string? Target, string? Failure) Args(ItemRecord from, ItemState to)
    {
        if (to == ItemState.Failed) return (null, "boom");
        if (to is ItemState.Queued or ItemState.Downloading && from.State == ItemState.Available) return ("9.9", null);
        if (to == ItemState.UpdateAvailable && from.State is ItemState.Installed or ItemState.UpdateAvailable) return ("9.9", null);
        return (null, null);
    }

    [Fact]
    [Trait("Feature", "SK-7")]
    [Trait("Rule", "R-TEST-4")]
    public void The_nine_states_are_the_plan_s_in_the_plan_s_order()
    {
        Assert.Equal(["Available", "Queued", "Downloading", "Checking", "Installed", "UpdateAvailable", "Failed", "Damaged", "Removing"], Enum.GetNames<ItemState>());
    }

    [Fact]
    [Trait("Feature", "SK-7")]
    [Trait("Rule", "R-TEST-4")]
    public void Every_state_pair_is_legal_exactly_when_the_hand_written_table_says_so_for_every_flavour_of_record()
    {
        var checkedPairs = 0;
        foreach (var (name, record) in StateFixtures.ValidRecords())
        {
            Assert.Null(record.Problem());
            foreach (var to in All)
            {
                Assert.True(Expected(record, to) == ItemStateMachine.IsLegal(record, to), $"{name} -> {to}: expected {Expected(record, to)}");
                checkedPairs++;
            }
        }
        Assert.Equal(StateFixtures.ValidRecords().Count() * 9, checkedPairs);
    }

    [Fact]
    [Trait("Feature", "SK-7")]
    [Trait("Rule", "R-TEST-4")]
    public void Every_legal_edge_applies_and_gives_a_record_that_satisfies_the_invariants_and_every_other_edge_is_refused()
    {
        var legal = 0;
        var refused = 0;
        foreach (var (name, record) in StateFixtures.ValidRecords())
            foreach (var to in All)
            {
                var (target, failure) = Args(record, to);
                if (Expected(record, to))
                {
                    var next = ItemStateMachine.Apply(record, to, target, failure);
                    Assert.Equal(to, next.State);
                    Assert.Null(next.Problem());
                    Assert.Equal(record.Id, next.Id);
                    legal++;
                }
                else
                {
                    Assert.Throws<IllegalTransitionException>(() => ItemStateMachine.Apply(record, to, target, failure));
                    refused++;
                }
            }
        // Counted by hand from the table above: 17 valid records x 9 targets = 153 pairs, 53 of them legal.
        Assert.Equal(53, legal);
        Assert.Equal(100, refused);
    }

    [Fact]
    [Trait("Feature", "SK-7")]
    [Trait("Rule", "R-TEST-4")]
    public void A_refused_transition_changes_nothing_in_the_state()
    {
        var state = AddonsState.Restore([StateFixtures.Rec("T1", ItemState.Installed, "3.14.8")], [], [], []);
        var before = StateFixtures.Text(state);
        Assert.Throws<IllegalTransitionException>(() => state.Transition("T1", ItemState.Checking));
        Assert.Throws<IllegalTransitionException>(() => state.Transition("T1", ItemState.Installed));
        Assert.Throws<IllegalTransitionException>(() => state.Transition("T1", ItemState.Available));
        Assert.Throws<IllegalTransitionException>(() => state.Transition("T9", ItemState.Installed));
        Assert.Equal(before, StateFixtures.Text(state));
        Assert.Equal(ItemState.Installed, state.Get("T1").State);
        Assert.Equal(ItemState.Available, state.Get("T9").State);
    }

    [Fact]
    [Trait("Feature", "SK-7")]
    [Trait("Rule", "R-TEST-4")]
    public void The_happy_path_of_5_4_runs_available_downloading_checking_installed_and_installs_the_target_pin()
    {
        var state = AddonsState.Empty;
        Assert.Equal(ItemState.Available, state.Get("T1").State);
        state = state.Transition("T1", ItemState.Queued, "3.14.8");
        state = state.Transition("T1", ItemState.Downloading);
        state = state.Transition("T1", ItemState.Checking);
        Assert.Null(state.Get("T1").InstalledPin);
        state = state.Transition("T1", ItemState.Installed);
        var done = state.Get("T1");
        Assert.Equal("3.14.8", done.InstalledPin);
        Assert.Null(done.TargetPin);
        Assert.Null(done.Failure);
    }

    [Fact]
    [Trait("Feature", "SK-7")]
    [Trait("Rule", "R-TEST-4")]
    public void A_failure_keeps_the_target_so_Retry_can_resume_and_Retry_clears_the_message()
    {
        var state = AddonsState.Empty.Transition("T3", ItemState.Downloading, "26.2.6");
        state = state.Transition("T3", ItemState.Failed, failure: "Couldn't reach libreoffice.org.");
        var failed = state.Get("T3");
        Assert.Equal("26.2.6", failed.TargetPin);
        Assert.Equal("Couldn't reach libreoffice.org.", failed.Failure);
        var retried = state.Transition("T3", ItemState.Downloading).Get("T3");
        Assert.Equal("26.2.6", retried.TargetPin);
        Assert.Null(retried.Failure);
        Assert.Equal(ItemState.Checking, state.Transition("T3", ItemState.Checking).Get("T3").State);
    }

    [Fact]
    [Trait("Feature", "SK-7")]
    [Trait("Rule", "R-TEST-4")]
    public void A_cancel_returns_a_first_install_to_available_an_update_to_update_available_and_a_repair_to_damaged()
    {
        var fresh = AddonsState.Empty.Transition("T2", ItemState.Downloading, "24.21.0").Transition("T2", ItemState.Available);
        Assert.Equal(AddonsState.Empty.Get("T2"), fresh.Get("T2"));
        var update = AddonsState.Restore([StateFixtures.Rec("T2", ItemState.UpdateAvailable, "24.20.0", "24.21.0")], [], [], []);
        var cancelledUpdate = update.Transition("T2", ItemState.Downloading).Transition("T2", ItemState.UpdateAvailable).Get("T2");
        Assert.Equal(StateFixtures.Rec("T2", ItemState.UpdateAvailable, "24.20.0", "24.21.0"), cancelledUpdate);
        var damaged = AddonsState.Restore([StateFixtures.Rec("T2", ItemState.Damaged, "24.20.0")], [], [], []);
        var cancelledRepair = damaged.Transition("T2", ItemState.Queued).Transition("T2", ItemState.Damaged).Get("T2");
        Assert.Equal(StateFixtures.Rec("T2", ItemState.Damaged, "24.20.0"), cancelledRepair);
        Assert.Throws<IllegalTransitionException>(() => damaged.Transition("T2", ItemState.Queued).Transition("T2", ItemState.Available));
        Assert.Throws<IllegalTransitionException>(() => update.Transition("T2", ItemState.Downloading).Transition("T2", ItemState.Damaged));
    }

    [Fact]
    [Trait("Feature", "SK-7")]
    [Trait("Rule", "R-TEST-4")]
    public void Starting_a_download_needs_a_pin_from_available_and_a_pending_pin_may_not_be_swapped()
    {
        var available = StateFixtures.Rec("X", ItemState.Available);
        Assert.Throws<IllegalTransitionException>(() => ItemStateMachine.Apply(available, ItemState.Downloading));
        Assert.Throws<IllegalTransitionException>(() => ItemStateMachine.Apply(available, ItemState.Queued, " "));
        Assert.Throws<IllegalTransitionException>(() => ItemStateMachine.Apply(available, ItemState.Queued, ""));
        var update = StateFixtures.Rec("X", ItemState.UpdateAvailable, "1.0", "2.0");
        Assert.Equal("2.0", ItemStateMachine.Apply(update, ItemState.Downloading).TargetPin);
        Assert.Equal("2.0", ItemStateMachine.Apply(update, ItemState.Downloading, "2.0").TargetPin);
        Assert.Throws<IllegalTransitionException>(() => ItemStateMachine.Apply(update, ItemState.Downloading, "3.0"));
        var damaged = StateFixtures.Rec("X", ItemState.Damaged, "1.0");
        Assert.Equal("1.0", ItemStateMachine.Apply(damaged, ItemState.Downloading).TargetPin);
        Assert.Throws<IllegalTransitionException>(() => ItemStateMachine.Apply(damaged, ItemState.Downloading, "2.0"));
        var queued = StateFixtures.Rec("X", ItemState.Queued, null, "2.0");
        Assert.Throws<IllegalTransitionException>(() => ItemStateMachine.Apply(queued, ItemState.Downloading, "3.0"));
        Assert.Equal("2.0", ItemStateMachine.Apply(queued, ItemState.Downloading, "2.0").TargetPin);
    }

    [Fact]
    [Trait("Feature", "SK-7")]
    [Trait("Rule", "R-TEST-4")]
    public void Update_available_needs_a_new_pin_that_differs_from_the_installed_and_from_the_one_already_offered()
    {
        var installed = StateFixtures.Rec("X", ItemState.Installed, "1.0");
        Assert.Throws<IllegalTransitionException>(() => ItemStateMachine.Apply(installed, ItemState.UpdateAvailable));
        Assert.Throws<IllegalTransitionException>(() => ItemStateMachine.Apply(installed, ItemState.UpdateAvailable, "1.0"));
        Assert.Throws<IllegalTransitionException>(() => ItemStateMachine.Apply(installed, ItemState.UpdateAvailable, " "));
        Assert.Equal("2.0", ItemStateMachine.Apply(installed, ItemState.UpdateAvailable, "2.0").TargetPin);
        var offered = StateFixtures.Rec("X", ItemState.UpdateAvailable, "1.0", "2.0");
        Assert.Throws<IllegalTransitionException>(() => ItemStateMachine.Apply(offered, ItemState.UpdateAvailable, "2.0"));
        Assert.Throws<IllegalTransitionException>(() => ItemStateMachine.Apply(offered, ItemState.UpdateAvailable, "1.0"));
        Assert.Equal("3.0", ItemStateMachine.Apply(offered, ItemState.UpdateAvailable, "3.0").TargetPin);
        Assert.Null(ItemStateMachine.Apply(offered, ItemState.Installed).TargetPin);
    }

    [Fact]
    [Trait("Feature", "SK-7")]
    [Trait("Rule", "R-TEST-4")]
    public void Failed_needs_a_message_and_no_other_transition_may_carry_one_or_carry_a_stray_pin()
    {
        var downloading = StateFixtures.Rec("X", ItemState.Downloading, null, "2.0");
        Assert.Throws<IllegalTransitionException>(() => ItemStateMachine.Apply(downloading, ItemState.Failed));
        Assert.Throws<IllegalTransitionException>(() => ItemStateMachine.Apply(downloading, ItemState.Failed, null, "  "));
        Assert.Equal("boom", ItemStateMachine.Apply(downloading, ItemState.Failed, null, "boom").Failure);
        Assert.Throws<IllegalTransitionException>(() => ItemStateMachine.Apply(downloading, ItemState.Checking, null, "boom"));
        Assert.Throws<IllegalTransitionException>(() => ItemStateMachine.Apply(downloading, ItemState.Checking, "2.0"));
        Assert.Throws<IllegalTransitionException>(() => ItemStateMachine.Apply(downloading, ItemState.Failed, "2.0", "boom"));
        var installed = StateFixtures.Rec("X", ItemState.Installed, "1.0");
        Assert.Throws<IllegalTransitionException>(() => ItemStateMachine.Apply(installed, ItemState.Removing, "1.0"));
    }

    [Fact]
    [Trait("Feature", "SK-7")]
    [Trait("Rule", "R-TEST-4")]
    public void Damage_and_removal_clear_a_pending_update_and_removal_ends_in_available_with_nothing_left()
    {
        var offered = StateFixtures.Rec("X", ItemState.UpdateAvailable, "1.0", "2.0");
        Assert.Equal(StateFixtures.Rec("X", ItemState.Damaged, "1.0"), ItemStateMachine.Apply(offered, ItemState.Damaged));
        var removing = ItemStateMachine.Apply(offered, ItemState.Removing);
        Assert.Equal(StateFixtures.Rec("X", ItemState.Removing, "1.0"), removing);
        Assert.Equal(StateFixtures.Rec("X", ItemState.Available), ItemStateMachine.Apply(removing, ItemState.Available));
        var failedUpdate = StateFixtures.Rec("X", ItemState.Failed, "1.0", "2.0", "boom");
        Assert.Equal(StateFixtures.Rec("X", ItemState.Removing, "1.0"), ItemStateMachine.Apply(failedUpdate, ItemState.Removing));
        Assert.False(ItemStateMachine.IsLegal(StateFixtures.Rec("X", ItemState.Failed, null, "2.0", "boom"), ItemState.Removing));
    }

    [Fact]
    [Trait("Feature", "SK-7")]
    [Trait("Rule", "R-TEST-4")]
    public void An_invalid_record_cannot_enter_a_transition_and_the_invariants_are_these()
    {
        var bad = new[]
        {
            StateFixtures.Rec("X", ItemState.Available, "1.0"),
            StateFixtures.Rec("X", ItemState.Available, null, "2.0"),
            StateFixtures.Rec("X", ItemState.Available, null, null, "boom"),
            StateFixtures.Rec("X", ItemState.Queued),
            StateFixtures.Rec("X", ItemState.Downloading, null, null),
            StateFixtures.Rec("X", ItemState.Checking, null, "2.0", "boom"),
            StateFixtures.Rec("X", ItemState.Installed),
            StateFixtures.Rec("X", ItemState.Installed, "1.0", "2.0"),
            StateFixtures.Rec("X", ItemState.Installed, "1.0", null, "boom"),
            StateFixtures.Rec("X", ItemState.UpdateAvailable, "1.0"),
            StateFixtures.Rec("X", ItemState.UpdateAvailable, "1.0", "1.0"),
            StateFixtures.Rec("X", ItemState.UpdateAvailable, null, "2.0"),
            StateFixtures.Rec("X", ItemState.Failed, null, "2.0"),
            StateFixtures.Rec("X", ItemState.Failed, null, "2.0", " "),
            StateFixtures.Rec("X", ItemState.Failed, null, null, "boom"),
            StateFixtures.Rec("X", ItemState.Damaged),
            StateFixtures.Rec("X", ItemState.Damaged, "1.0", "1.0"),
            StateFixtures.Rec("X", ItemState.Removing),
            StateFixtures.Rec("X", ItemState.Removing, "1.0", "2.0"),
            StateFixtures.Rec("", ItemState.Available),
            StateFixtures.Rec("a b", ItemState.Available),
            StateFixtures.Rec("X", ItemState.Installed, " "),
        };
        foreach (var invalid in bad)
        {
            Assert.NotNull(invalid.Problem());
            Assert.Throws<IllegalTransitionException>(() => ItemStateMachine.Apply(invalid, ItemState.Removing));
            Assert.False(ItemStateMachine.IsLegal(invalid, ItemState.Removing));
        }
    }
}
