using Marrow.Core.Accounts;
using Marrow.Core.Accounts.Pills;

namespace Marrow.Core.Tests.Accounts.Pills;

// NOTES "Pills", "Pills get their own Settings category": create, rename, duplicate, delete, reorder steps, "Use for new threads".
public sealed class PillStoreTests
{
    private static PillStep Step(string account, string? model = null) => new(account, model);

    private static (PillStore Store, List<PillStoreChange> Changes) Make()
    {
        var store = new PillStore();
        var changes = new List<PillStoreChange>();
        store.Changed += (_, e) => changes.Add(e);
        return (store, changes);
    }

    [Fact]
    [Trait("Feature", "MA-7")]
    [Trait("Spec", "MA-T201")]
    [Trait("Rule", "R-TEST-4")]
    public void Create_trims_the_name_gives_an_id_and_keeps_the_steps_in_order()
    {
        var (store, changes) = Make();
        var pill = store.Create("  Long haul  ", [Step("c1"), Step("c2"), Step("x1")]);
        Assert.Equal("Long haul", pill.Name);
        Assert.NotEqual(Guid.Empty, pill.Id);
        Assert.Equal(["c1", "c2", "x1"], pill.Steps.Select(s => s.AccountId).ToList());
        Assert.Same(pill, store.Find(pill.Id));
        Assert.Equal([new PillStoreChange(PillChangeKind.Created, pill.Id)], changes);
    }

    [Fact]
    [Trait("Feature", "MA-7")]
    [Trait("Spec", "MA-T202")]
    [Trait("Rule", "R-TEST-4")]
    public void A_name_must_not_be_empty_or_taken_ignoring_case_and_the_pill_itself_may_keep_its_name()
    {
        var (store, changes) = Make();
        var pill = store.Create("Night shift");
        Assert.Equal(PillNameProblem.Empty, store.NameProblem("   "));
        Assert.Equal(PillNameProblem.Taken, store.NameProblem("night SHIFT"));
        Assert.Equal(PillNameProblem.None, store.NameProblem("night shift", except: pill.Id));
        Assert.Throws<ArgumentException>(() => store.Create(" "));
        Assert.Throws<ArgumentException>(() => store.Create("NIGHT shift"));
        Assert.Single(changes);
    }

    [Fact]
    [Trait("Feature", "MA-7")]
    [Trait("Spec", "MA-T203")]
    [Trait("Rule", "R-TEST-4")]
    public void Rename_changes_only_the_name()
    {
        var (store, changes) = Make();
        var a = store.Create("A", [Step("c1")]);
        store.Create("B");
        store.Rename(a.Id, " Alpha ");
        Assert.Equal("Alpha", store.Find(a.Id)!.Name);
        Assert.Equal(a.Steps, store.Find(a.Id)!.Steps);
        Assert.Throws<ArgumentException>(() => store.Rename(a.Id, "b"));
        Assert.Equal(PillChangeKind.Renamed, changes[^1].Kind);
    }

    [Fact]
    [Trait("Feature", "MA-7")]
    [Trait("Spec", "MA-T204")]
    [Trait("Rule", "R-TEST-4")]
    public void Duplicate_copies_steps_and_rules_under_a_new_id_and_a_free_copy_name_right_after_the_original()
    {
        var (store, _) = Make();
        var a = store.Create("A", [Step("c1", "opus"), Step("x1")]);
        store.Create("B");
        store.SetEarlierAccount(a.Id, DecisionValues.GoBack);
        store.SetHandshakeMode(a.Id, PillHandshakeMode.SameForEvery, HandshakePoint.At(90));
        store.SetUseForNewThreads(a.Id);

        var copy = store.Duplicate(a.Id);
        var second = store.Duplicate(a.Id);
        Assert.Equal(("A copy", "A copy 2"), (copy.Name, second.Name));
        Assert.NotEqual(a.Id, copy.Id);
        Assert.Equal(a.Steps, copy.Steps);
        Assert.Equal((DecisionValues.GoBack, PillHandshakeMode.SameForEvery, HandshakePoint.At(90)), (copy.EarlierAccount, copy.HandshakeMode, copy.SameHandshake));
        Assert.Equal(["A", "A copy 2", "A copy", "B"], store.Pills.Select(p => p.Name).ToList());
        Assert.Equal(a.Id, store.UseForNewThreads);
    }

    [Fact]
    [Trait("Feature", "MA-7")]
    [Trait("Spec", "MA-T205")]
    [Trait("Rule", "R-TEST-4")]
    public void Delete_removes_the_pill_and_clears_use_for_new_threads_when_it_was_that_one()
    {
        var (store, changes) = Make();
        var a = store.Create("A");
        var b = store.Create("B");
        store.SetUseForNewThreads(a.Id);
        Assert.True(store.Delete(a.Id));
        Assert.Null(store.UseForNewThreads);
        Assert.Equal([b.Id], store.Pills.Select(p => p.Id).ToList());
        Assert.False(store.Delete(a.Id));
        Assert.Equal(PillChangeKind.Deleted, changes[^1].Kind);
        store.SetUseForNewThreads(b.Id);
        store.Delete(a.Id);
        Assert.Equal(b.Id, store.UseForNewThreads);
    }

    [Fact]
    [Trait("Feature", "MA-7")]
    [Trait("Spec", "MA-T206")]
    [Trait("Rule", "R-TEST-4")]
    public void Steps_are_added_removed_updated_and_reordered()
    {
        var (store, changes) = Make();
        var p = store.Create("P", [Step("a"), Step("b"), Step("c")]);
        store.AddStep(p.Id, Step("d"));
        store.AddStep(p.Id, Step("z"), at: 1);
        Assert.Equal(["a", "z", "b", "c", "d"], store.Find(p.Id)!.Steps.Select(s => s.AccountId).ToList());
        store.RemoveStep(p.Id, 1);
        store.MoveStep(p.Id, 3, 0);
        Assert.Equal(["d", "a", "b", "c"], store.Find(p.Id)!.Steps.Select(s => s.AccountId).ToList());
        store.MoveStep(p.Id, 0, 2);
        Assert.Equal(["a", "b", "d", "c"], store.Find(p.Id)!.Steps.Select(s => s.AccountId).ToList());
        store.UpdateStep(p.Id, 2, s => s with { Model = "opus", Effort = "high", Fast = true, HandshakeAt = HandshakePoint.At(97), KeyId = "k2" });
        var changed = store.Find(p.Id)!.Steps[2];
        Assert.Equal(("d", "opus", "high", true, HandshakePoint.At(97), "k2"), (changed.AccountId, changed.Model, changed.Effort, changed.Fast, changed.HandshakeAt, changed.KeyId));
        Assert.Null(store.Find(p.Id)!.Steps[1].Model);
        Assert.All(changes.Skip(1), c => Assert.Equal(PillChangeKind.StepsChanged, c.Kind));
    }

    [Fact]
    [Trait("Feature", "MA-7")]
    [Trait("Spec", "MA-T207")]
    [Trait("Rule", "R-TEST-4")]
    public void A_step_left_at_same_as_the_account_has_no_model_effort_fast_mode_or_point()
    {
        var (store, _) = Make();
        var step = store.Create("P", [Step("c1")]).Steps[0];
        Assert.Equal((null, null, null, null, null), (step.Model, step.Effort, step.Fast, step.HandshakeAt, step.KeyId));
        Assert.Equal(new StepSettings(), step.Settings);
    }

    [Fact]
    [Trait("Feature", "MA-7")]
    [Trait("Spec", "MA-T208")]
    [Trait("Rule", "R-TEST-4")]
    public void Step_edits_outside_the_list_or_with_an_empty_account_are_refused_and_change_nothing()
    {
        var (store, changes) = Make();
        var p = store.Create("P", [Step("a"), Step("b")]);
        Assert.Throws<ArgumentOutOfRangeException>(() => store.RemoveStep(p.Id, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => store.MoveStep(p.Id, 0, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => store.MoveStep(p.Id, -1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => store.AddStep(p.Id, Step("c"), at: 3));
        Assert.Throws<ArgumentException>(() => store.AddStep(p.Id, Step(" ")));
        Assert.Throws<ArgumentException>(() => store.UpdateStep(p.Id, 0, s => s with { AccountId = "" }));
        Assert.Throws<KeyNotFoundException>(() => store.AddStep(Guid.NewGuid(), Step("c")));
        Assert.Equal(["a", "b"], store.Find(p.Id)!.Steps.Select(s => s.AccountId).ToList());
        Assert.Single(changes);
    }

    [Fact]
    [Trait("Feature", "MA-7")]
    [Trait("Spec", "MA-T209")]
    [Trait("Rule", "R-TEST-4")]
    public void Moving_a_step_onto_itself_changes_and_saves_nothing()
    {
        var (store, changes) = Make();
        var p = store.Create("P", [Step("a"), Step("b")]);
        store.MoveStep(p.Id, 1, 1);
        Assert.Single(changes);
    }

    [Fact]
    [Trait("Feature", "MA-7")]
    [Trait("Spec", "MA-T210")]
    [Trait("Rule", "R-TEST-4")]
    public void The_pills_earlier_account_rule_takes_go_back_stay_ask_or_null_for_follow_Settings()
    {
        var (store, changes) = Make();
        var p = store.Create("P");
        foreach (var value in new[] { DecisionValues.GoBack, DecisionValues.Stay, DecisionValues.Ask, null })
        {
            store.SetEarlierAccount(p.Id, value);
            Assert.Equal(value, store.Find(p.Id)!.EarlierAccount);
        }
        Assert.Throws<ArgumentException>(() => store.SetEarlierAccount(p.Id, DecisionValues.Switch));
        Assert.Equal(PillChangeKind.RuleChanged, changes[^1].Kind);
    }

    [Fact]
    [Trait("Feature", "MA-7")]
    [Trait("Spec", "MA-T211")]
    [Trait("Rule", "R-TEST-4")]
    public void Same_for_every_step_makes_one_point_serve_every_step_without_losing_the_steps_own_points()
    {
        var (store, _) = Make();
        var p = store.Create("P", [new PillStep("a", HandshakeAt: HandshakePoint.At(85)), new PillStep("b")]);
        store.SetHandshakeMode(p.Id, PillHandshakeMode.SameForEvery, HandshakePoint.At(99));
        var same = store.Find(p.Id)!;
        Assert.Equal(HandshakePoint.At(99), same.Effective(0).HandshakeAt);
        Assert.Equal(HandshakePoint.At(99), same.Effective(1).HandshakeAt);
        store.SetHandshakeMode(p.Id, PillHandshakeMode.PerStep);
        var per = store.Find(p.Id)!;
        Assert.Equal(HandshakePoint.At(85), per.Effective(0).HandshakeAt);
        Assert.Null(per.Effective(1).HandshakeAt);
        store.SetHandshakeMode(p.Id, PillHandshakeMode.SameForEvery);
        Assert.True(store.Find(p.Id)!.Effective(0).HandshakeAt!.Value.IsSmart);
    }

    [Fact]
    [Trait("Feature", "MA-7")]
    [Trait("Spec", "MA-T212")]
    [Trait("Rule", "R-TEST-4")]
    public void Use_for_new_threads_is_one_pill_or_none_and_an_unknown_pill_is_refused()
    {
        var (store, changes) = Make();
        var a = store.Create("A");
        store.SetUseForNewThreads(a.Id);
        Assert.Equal(a.Id, store.UseForNewThreads);
        Assert.Equal(PillChangeKind.UseForNewThreadsChanged, changes[^1].Kind);
        store.SetUseForNewThreads(null);
        Assert.Null(store.UseForNewThreads);
        Assert.Throws<KeyNotFoundException>(() => store.SetUseForNewThreads(Guid.NewGuid()));
    }

    [Fact]
    [Trait("Feature", "MA-7")]
    [Trait("Spec", "MA-T213")]
    [Trait("Rule", "R-TEST-4")]
    public void A_new_thread_starts_on_the_chosen_pills_first_usable_step()
    {
        var (store, _) = Make();
        var p = store.Create("P", [Step("c1"), Step("c2"), Step("x1")]);
        Assert.Null(store.NewThreadRelay(_ => true));
        store.SetUseForNewThreads(p.Id);
        Assert.Equal(new PillThreadState(p.Id, 1, "c2"), store.NewThreadRelay(s => s.AccountId != "c1"));
        Assert.Equal(new PillThreadState(p.Id, 0, "c1"), store.NewThreadRelay(_ => true));
        Assert.Null(store.NewThreadRelay(_ => false));
    }
}
