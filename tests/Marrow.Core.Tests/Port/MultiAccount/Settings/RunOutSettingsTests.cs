using Marrow.Testing.MultiAccountSettings;
using Marrow.Core.Accounts;
using Marrow.Core.Models;
using Marrow.Core.Port.MultiAccount.Settings;

namespace Marrow.Core.Tests.Port.MultiAccount.Settings;

// MA-8 (T11 step 2, OD-0116): "When an account runs out" with its indented rows, as grouped rows with the current value on the right. Automatic switching starts unset: nothing is chosen for the user.
[Trait("Feature", "MA-8")]
[Trait("Rule", "R-TEST-4")]
public sealed class RunOutSettingsTests
{
    private static AccountsRig Two()
    {
        var rig = new AccountsRig();
        rig.Connect(ProviderKind.Claude, "Max");
        rig.ConnectAnother(ProviderKind.Claude, "Max");
        return rig;
    }

    [Fact]
    public void The_group_and_the_handshake_group_appear_with_two_accounts_or_a_pill_and_not_for_one_account()
    {
        var rig = new AccountsRig();
        rig.Connect(ProviderKind.Claude, "Max");
        Assert.False(rig.Model.ShowRunOut);
        rig.PillsExist = true;
        Assert.True(rig.Model.ShowRunOut);
        rig.PillsExist = false;
        rig.ConnectAnother(ProviderKind.Claude, "Max");
        Assert.True(rig.Model.ShowRunOut);
    }

    [Fact]
    public void The_run_out_row_starts_unset_as_ask_me_the_first_time_and_nothing_is_stored_until_the_user_chooses()
    {
        var rig = Two();
        var run = rig.Model.Row("run");
        Assert.Equal(MultiAccountWords.RunTitle, run.Title);
        Assert.True(run.Unset);
        Assert.Equal(MultiAccountWords.RunNone, run.ValueLabel);
        Assert.Equal(MultiAccountWords.RunNoneDescription, run.Description);
        Assert.Equal([MultiAccountWords.RunGo, MultiAccountWords.RunAsk, MultiAccountWords.RunWait], run.Options.Select(o => o.Label));
        Assert.DoesNotContain(run.Options, o => o.Recommended); // OD-0116: switching is off by default, so Keep me going is not recommended
        Assert.DoesNotContain(run.Options, o => o.Chosen);
        Assert.Null(rig.Decisions.GetSetting(DecisionKind.OnLimit));
        Assert.Null(rig.Decisions.GetSetting(DecisionKind.MidReply));
    }

    [Fact]
    public void The_indented_rows_are_hidden_until_the_run_out_choice_makes_them_matter()
    {
        var rig = Two();
        Assert.All(new[] { "mid", "back", "other", "bring", "handed" }, key => Assert.False(rig.Model.Row(key).Visible, key));
        rig.Model.Choose("run", DecisionValues.Ask);
        Assert.True(rig.Model.Row("other").Visible);
        Assert.False(rig.Model.Row("mid").Visible);
        Assert.False(rig.Model.Row("back").Visible);
        rig.Model.Choose("run", DecisionValues.Switch);
        Assert.All(new[] { "mid", "back", "other", "bring", "handed" }, key => Assert.True(rig.Model.Row(key).Visible, key));
        Assert.All(new[] { "mid", "back", "other", "bring", "handed" }, key => Assert.True(rig.Model.Row(key).Indented, key));
        rig.Model.Choose("run", DecisionValues.Wait);
        Assert.All(new[] { "mid", "back", "other", "bring", "handed" }, key => Assert.False(rig.Model.Row(key).Visible, key));
    }

    [Fact]
    public void Choosing_writes_the_decision_store_and_the_value_and_its_sentence_follow()
    {
        var rig = Two();
        rig.Model.Choose("run", DecisionValues.Switch);
        Assert.Equal(DecisionValues.Switch, rig.Decisions.GetSetting(DecisionKind.OnLimit));
        var run = rig.Model.Row("run");
        Assert.Equal((false, MultiAccountWords.RunGo, MultiAccountWords.RunGoDescription), (run.Unset, run.ValueLabel, run.Description));
        Assert.Equal([true], run.Options.Where(o => o.Chosen).Select(o => o.Value == DecisionValues.Switch));
        rig.Model.Choose("mid", DecisionValues.Stop);
        rig.Model.Choose("back", DecisionValues.Stay);
        rig.Model.Choose("other", DecisionValues.Switch);
        rig.Model.Choose("bring", DecisionValues.AiSummary);
        Assert.Equal(DecisionValues.Stop, rig.Decisions.GetSetting(DecisionKind.MidReply));
        Assert.Equal(DecisionValues.Stay, rig.Decisions.GetSetting(DecisionKind.EarlierAccount));
        Assert.Equal(DecisionValues.Switch, rig.Decisions.GetSetting(DecisionKind.OtherService));
        Assert.Equal(DecisionValues.AiSummary, rig.Decisions.GetSetting(DecisionKind.Bring));
        Assert.Equal((MultiAccountWords.MidStop, MultiAccountWords.MidStopDescription), (rig.Model.Row("mid").ValueLabel, rig.Model.Row("mid").Description));
        Assert.Equal(MultiAccountWords.BringAi, rig.Model.Row("bring").ValueLabel);
        Assert.Equal(MultiAccountWords.OtherSwitchDescription, rig.Model.Row("other").Description);
        Assert.Equal(MultiAccountWords.BackStayDescription, rig.Model.Row("back").Description);
    }

    [Fact]
    public void A_dependent_row_shows_its_page_default_without_writing_it()
    {
        var rig = Two();
        rig.Model.Choose("run", DecisionValues.Switch);
        Assert.Equal(MultiAccountWords.MidFinish, rig.Model.Row("mid").ValueLabel);
        Assert.Equal(MultiAccountWords.BackGo, rig.Model.Row("back").ValueLabel);
        Assert.Equal(MultiAccountWords.OtherAsk, rig.Model.Row("other").ValueLabel);
        Assert.Equal(MultiAccountWords.BringCompact, rig.Model.Row("bring").ValueLabel);
        Assert.Null(rig.Decisions.GetSetting(DecisionKind.MidReply));
        Assert.Null(rig.Decisions.GetSetting(DecisionKind.EarlierAccount));
        Assert.Null(rig.Decisions.GetSetting(DecisionKind.OtherService));
        Assert.Null(rig.Decisions.GetSetting(DecisionKind.Bring));
    }

    [Fact]
    public void The_option_lists_are_the_pages_in_the_pages_order()
    {
        var rig = Two();
        Assert.Equal([MultiAccountWords.MidFinish, MultiAccountWords.MidStop], rig.Model.Row("mid").Options.Select(o => o.Label));
        Assert.Equal([MultiAccountWords.BackGo, MultiAccountWords.BackStay], rig.Model.Row("back").Options.Select(o => o.Label));
        Assert.Equal([MultiAccountWords.OtherAsk, MultiAccountWords.OtherSwitch, MultiAccountWords.OtherWait], rig.Model.Row("other").Options.Select(o => o.Label));
        Assert.Equal([MultiAccountWords.BringCompact, MultiAccountWords.BringFull, MultiAccountWords.BringAi], rig.Model.Row("bring").Options.Select(o => o.Label));
        Assert.Equal(SettingKind.Button, rig.Model.Row("handed").Kind);
        Assert.Equal(MultiAccountWords.HandedButton, rig.Model.Row("handed").ButtonText);
        Assert.Equal(MultiAccountWords.HandedDescription, rig.Model.Row("handed").Description);
    }
}
