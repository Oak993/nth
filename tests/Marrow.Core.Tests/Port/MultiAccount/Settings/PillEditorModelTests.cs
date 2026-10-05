using Marrow.Testing.MultiAccountSettings;
using Marrow.Core.Accounts;
using Marrow.Core.Models;
using Marrow.Core.Port.MultiAccount.Settings;

namespace Marrow.Core.Tests.Port.MultiAccount.Settings;

// MA-8 (T11 step 2): the pill editor: steps (account, model, effort, fast, Handshake point), "Same for every step / Set each step on its own", the loop line and the earlier-account rule.
[Trait("Feature", "MA-8")]
[Trait("Rule", "R-TEST-4")]
public sealed class PillEditorModelTests
{
    [Fact]
    public void The_steps_are_numbered_named_and_say_where_a_new_service_starts_and_how_many_there_are()
    {
        var rig = new PillsRig();
        var editor = rig.Editor(rig.Make("Work", rig.Claude1, rig.Claude2, rig.Codex1).Id);
        Assert.Equal(["Claude Max 1", "Claude Max 2", "Codex"], editor.Steps.Select(s => s.AccountName));
        Assert.Equal([1, 2, 3], editor.Steps.Select(s => s.Number));
        Assert.Equal([false, false, true], editor.Steps.Select(s => s.NewService));
        Assert.Equal(MultiAccountWords.StepCount(3, 2), editor.CountText);
        Assert.Equal("Claude Code", editor.Steps[0].ServiceText);
    }

    [Fact]
    public void A_step_on_an_account_that_is_off_is_marked_skipped_and_stays_in_the_pill()
    {
        var rig = new PillsRig();
        var editor = rig.Editor(rig.Make("Work", rig.Claude1, rig.Claude2).Id);
        rig.Accounts.SetEnabled(rig.Claude2.Id, false);
        Assert.Equal([false, true], editor.Steps.Select(s => s.Skipped));
        Assert.Equal(2, editor.Steps.Count);
    }

    [Fact]
    public void Add_a_step_offers_only_connected_accounts_that_are_not_in_the_pill_yet()
    {
        var rig = new PillsRig();
        var editor = rig.Editor(rig.Make("Work", rig.Claude1, rig.Claude2).Id);
        var offer = editor.Available;
        Assert.Equal([PillsRig.Id(rig.Codex1)], offer.Select(o => o.AccountId));
        editor.AddStep(PillsRig.Id(rig.Codex1));
        Assert.Equal(3, rig.Pills.Pills[0].Steps.Count);
        Assert.Empty(editor.Available);
    }

    [Fact]
    public void A_pill_keeps_at_least_two_steps()
    {
        var rig = new PillsRig();
        var pill = rig.Make("Work", rig.Claude1, rig.Claude2, rig.Codex1);
        var editor = rig.Editor(pill.Id);
        Assert.True(editor.RemoveStep(2));
        Assert.False(editor.RemoveStep(1));
        Assert.Equal(2, rig.Pills.Find(pill.Id)!.Steps.Count);
    }

    [Fact]
    public void Steps_move_up_and_down_and_to_the_top()
    {
        var rig = new PillsRig();
        var pill = rig.Make("Work", rig.Claude1, rig.Claude2, rig.Codex1);
        var editor = rig.Editor(pill.Id);
        editor.MoveStep(2, 0);
        Assert.Equal([PillsRig.Id(rig.Codex1), PillsRig.Id(rig.Claude1), PillsRig.Id(rig.Claude2)], rig.Pills.Find(pill.Id)!.Steps.Select(s => s.AccountId));
        Assert.True(editor.MenuFor(0).Single(e => e.Key == "up").Disabled);
        Assert.True(editor.MenuFor(2).Single(e => e.Key == "down").Disabled);
    }

    [Fact]
    public void A_step_starts_as_the_same_as_the_account_and_a_change_is_saved_with_the_pill_and_can_be_reset()
    {
        var rig = new PillsRig();
        rig.Accounts.SetDefaults(rig.Claude1.Id, new Marrow.Core.Port.Accounts.AccountDefaults { Model = "opus", Effort = "high", FastMode = false });
        var pill = rig.Make("Work", rig.Claude1, rig.Claude2);
        var editor = rig.Editor(pill.Id);
        Assert.True(editor.Steps[0].SameAsAccount);
        Assert.Equal(("claude:opus", "high", false), (editor.Steps[0].Model, editor.Steps[0].Effort, editor.Steps[0].Fast));
        editor.SetStepSettings(0, new StepSettings("sonnet", "max", true));
        var step = rig.Pills.Find(pill.Id)!.Steps[0];
        Assert.Equal(("sonnet", "max", true), (step.Model, step.Effort, step.Fast));
        Assert.False(editor.Steps[0].SameAsAccount);
        Assert.Equal(("claude:sonnet", "max", true), (editor.Steps[0].Model, editor.Steps[0].Effort, editor.Steps[0].Fast));
        editor.ResetStepSettings(0);
        step = rig.Pills.Find(pill.Id)!.Steps[0];
        Assert.Equal((null, null, null), (step.Model, step.Effort, step.Fast));
        Assert.True(editor.Steps[0].SameAsAccount);
    }

    [Fact]
    public void The_handshake_point_is_one_slider_for_every_step_or_one_per_step_and_switching_seeds_the_steps()
    {
        var rig = new PillsRig();
        var pill = rig.Model.Create()!;
        var editor = rig.Editor(pill.Id);
        Assert.All(editor.Steps, s => Assert.True(s.HandshakeLocked));
        Assert.Equal(MultiAccountWords.SameDescription, editor.HandshakeDescription);
        editor.SetSame(Marrow.Core.Accounts.HandshakePoint.At(95));
        Assert.Equal(Marrow.Core.Accounts.HandshakePoint.At(95), rig.Pills.Find(pill.Id)!.SameHandshake);
        editor.SetMode(PillHandshakeMode.PerStep);
        var seeded = rig.Pills.Find(pill.Id)!;
        Assert.All(seeded.Steps, s => Assert.Equal(Marrow.Core.Accounts.HandshakePoint.At(95), s.HandshakeAt));
        Assert.Equal(MultiAccountWords.EachDescription, editor.HandshakeDescription);
        Assert.All(editor.Steps, s => Assert.False(s.HandshakeLocked));
        editor.SetStepHandshake(1, Marrow.Core.Accounts.HandshakePoint.At(99));
        Assert.Equal(Marrow.Core.Accounts.HandshakePoint.At(99), rig.Pills.Find(pill.Id)!.Steps[1].HandshakeAt);
        editor.SetMode(PillHandshakeMode.SameForEvery);
        Assert.Equal(Marrow.Core.Accounts.HandshakePoint.At(99), rig.Pills.Find(pill.Id)!.Steps[1].HandshakeAt);
        Assert.Equal(Marrow.Core.Accounts.HandshakePoint.At(95), rig.Pills.Find(pill.Id)!.Effective(1).HandshakeAt);
    }

    [Fact]
    public void A_local_step_never_runs_out_and_has_no_handshake_slider()
    {
        var rig = new PillsRig();
        var local = rig.Connect(rig.Accounts.ForService(ProviderKind.Local)[0]);
        var editor = rig.Editor(rig.Make("Work", rig.Claude1, local).Id);
        Assert.Equal([false, true], editor.Steps.Select(s => s.NeverRunsOut));
    }

    [Fact]
    public void The_earlier_account_rule_is_like_in_Settings_until_the_pill_has_its_own()
    {
        var rig = new PillsRig();
        var pill = rig.Make("Work", rig.Claude1, rig.Claude2);
        var editor = rig.Editor(pill.Id);
        var row = editor.EarlierRow;
        Assert.Equal((MultiAccountWords.EarlierTitle, MultiAccountWords.EarlierSettings, MultiAccountWords.EarlierSettingsDescription), (row.Title, row.ValueLabel, row.Description));
        Assert.Equal([MultiAccountWords.EarlierSettings, MultiAccountWords.EarlierGo, MultiAccountWords.EarlierStay], row.Options.Select(o => o.Label));
        editor.ChooseEarlier(DecisionValues.GoBack);
        Assert.Equal(DecisionValues.GoBack, rig.Pills.Find(pill.Id)!.EarlierAccount);
        Assert.Equal((MultiAccountWords.EarlierGo, MultiAccountWords.EarlierGoDescription), (editor.EarlierRow.ValueLabel, editor.EarlierRow.Description));
        editor.ChooseEarlier(DecisionValues.Stay);
        Assert.Equal(MultiAccountWords.EarlierStayDescription, editor.EarlierRow.Description);
        editor.ChooseEarlier("settings");
        Assert.Null(rig.Pills.Find(pill.Id)!.EarlierAccount);
    }

    [Fact]
    public void The_name_is_edited_in_the_editor_with_the_same_checks_as_the_list()
    {
        var rig = new PillsRig();
        var pill = rig.Make("Work", rig.Claude1, rig.Claude2);
        var editor = rig.Editor(pill.Id);
        Assert.Equal("Work", editor.Name);
        Assert.Equal(PillRenameResult.TooLong, editor.Rename(new string('y', 30)));
        Assert.Equal(PillRenameResult.Ok, editor.Rename("Night"));
        Assert.Equal("Night", editor.Name);
        Assert.Equal(MultiAccountWords.LoopLine, editor.LoopLine);
    }
}
