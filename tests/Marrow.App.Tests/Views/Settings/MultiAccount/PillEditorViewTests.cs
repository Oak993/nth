using Avalonia.Controls;
using Marrow.App.Port.MultiAccount.Settings;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Accounts;
using Marrow.Core.Accounts.Pills;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.MultiAccount.Settings;
using Marrow.Testing.MultiAccountSettings;
using Account = Marrow.Core.Port.Accounts.Account;

namespace Marrow.App.Tests.Views.Settings.MultiAccount;

// MA-8 (T11 step 2): the pill editor: name, "Same for every step / Set each step on its own", steps with their model line, the yellow bolt for Fast, the Handshake slider, Add a step, the loop line and the earlier-account rule.
[Collection("Headless")]
public sealed class PillEditorViewTests(HeadlessSession headless)
{
    private static IReadOnlyList<ModelOption> Models(ProviderKind service) =>
        [new ModelOption("opus", "Opus 5.5", efforts: new ValueList<string>(["low", "medium", "high", "max"]), defaultEffort: "medium", fastTier: "fast"), new ModelOption("sonnet", "Sonnet 5.5", efforts: new ValueList<string>(["low", "medium", "high"]), defaultEffort: "medium")];

    private (AccountsRig Rig, Pill Pill, Account[] Accounts) Setup(bool includeLocal = false)
    {
        var rig = new AccountsRig();
        rig.Connect(ProviderKind.Claude, "Max");
        var second = rig.ConnectAnother(ProviderKind.Claude, "Max");
        var codex = rig.Connect(rig.Accounts.ForService(ProviderKind.Codex)[0], "Plus");
        var claude = rig.Accounts.ForService(ProviderKind.Claude);
        var steps = new List<Account> { claude[0], claude[1], codex };
        if (includeLocal) steps.Add(rig.Connect(rig.Accounts.ForService(ProviderKind.Local)[0]));
        var pill = rig.MakePill("Work", [.. steps]);
        rig.Pills.SetHandshakeMode(pill.Id, PillHandshakeMode.SameForEvery, HandshakePoint.Smart);
        _ = second;
        return (rig, rig.Pills.Find(pill.Id)!, [.. steps]);
    }

    private void Open(AccountsRig rig, Pill pill, Action<MultiAccountHarness, PillEditorView> check, bool motion = false, bool reduceMotion = false) =>
        MultiAccountHarness.Run(headless, rig, () => new MultiAccountHost<PillEditorView>(context => new PillEditorView(context, context.Editor(pill.Id), () => { })),
            h => check(h, ((MultiAccountHost<PillEditorView>)h.Page).View!), motion, reduceMotion, Models);

    [Fact]
    [Trait("Feature", "MA-8")]
    [Trait("Rule", "R-TEST-4")]
    public void The_editor_shows_the_page_words_the_steps_in_order_and_the_count()
    {
        var (rig, pill, _) = Setup();
        Open(rig, pill, (h, view) =>
        {
            var words = h.Words();
            foreach (var expected in new[] { MultiAccountWords.EditorTitle, MultiAccountWords.EditorSub, MultiAccountWords.HandshakePoint, MultiAccountWords.SameForEvery, MultiAccountWords.EachOnItsOwn, MultiAccountWords.AddStep, MultiAccountWords.LoopLine, MultiAccountWords.EarlierTitle, MultiAccountWords.StepCount(3, 2), MultiAccountWords.EveryStep })
                Assert.Contains(expected, words);
            Assert.Equal(["Claude Max 1", "Claude Max 2", "Codex"], view.StepList.Select(r => r.Step.AccountName));
            Assert.Equal("Work", view.NameBox.Text);
            Assert.Equal(3, view.Shape.MarkCount);
        });
    }

    [Fact]
    [Trait("Feature", "MA-8")]
    [Trait("Rule", "R-TEST-4")]
    public void The_name_is_edited_in_the_big_pill_and_a_taken_or_empty_name_goes_back_when_leaving_the_box()
    {
        var (rig, pill, accounts) = Setup();
        rig.MakePill("Play", accounts[0], accounts[2]);
        Open(rig, pill, (h, view) =>
        {
            Assert.Equal(24, view.NameBox.MaxLength);
            view.NameBox.Text = "Night";
            h.Settle();
            Assert.Equal("Night", rig.Pills.Find(pill.Id)!.Name);
            view.NameBox.Text = "play";
            h.Settle();
            Assert.Equal("Night", rig.Pills.Find(pill.Id)!.Name);
            view.NameBox.RaiseEvent(new Avalonia.Input.KeyEventArgs { RoutedEvent = Avalonia.Input.InputElement.KeyDownEvent, Key = Avalonia.Input.Key.Enter });
            h.Settle();
            Assert.Equal("Night", view.NameBox.Text);
        });
    }

    [Fact]
    [Trait("Feature", "MA-8")]
    [Trait("Rule", "R-TEST-4")]
    public void Each_step_says_its_service_where_a_new_service_starts_and_when_its_account_is_off()
    {
        var (rig, pill, accounts) = Setup();
        rig.Accounts.SetEnabled(accounts[1].Id, false);
        Open(rig, pill, (h, view) =>
        {
            var rows = view.StepList;
            Assert.Equal(["Claude Code", "Claude Code", "Codex"], rows.Select(r => r.SubShown));
            Assert.Equal([false, false, true], rows.Select(r => r.NewServiceShown));
            Assert.Equal([false, true, false], rows.Select(r => r.SkippedShown));
            Assert.Contains(MultiAccountWords.NewService, h.Words());
            Assert.Contains(MultiAccountWords.SkippedOff, h.Words());
        });
    }

    [Fact]
    [Trait("Feature", "MA-8")]
    [Trait("Rule", "R-TEST-4")]
    public void A_step_starts_as_the_same_as_the_account_and_shows_Fast_only_as_the_yellow_bolt()
    {
        var (rig, pill, accounts) = Setup();
        rig.Accounts.SetDefaults(accounts[0].Id, new Marrow.Core.Port.Accounts.AccountDefaults { Model = "opus", Effort = "high", FastMode = false });
        Open(rig, pill, (h, view) =>
        {
            var row = view.StepList[0];
            Assert.Equal(MultiAccountWords.SameAsAccount + "claude:opus · High", row.ConfigShown);
            Assert.False(row.BoltShown);
            view.Model.SetStepSettings(0, new StepSettings("opus", "max", true));
            h.Settle();
            Assert.Equal("claude:opus · Max", row.ConfigShown);
            Assert.True(row.BoltShown);
            Assert.DoesNotContain("Fast", row.ConfigShown, StringComparison.Ordinal);
            view.Model.ResetStepSettings(0);
            h.Settle();
            Assert.StartsWith(MultiAccountWords.SameAsAccount, row.ConfigShown, StringComparison.Ordinal);
            Assert.False(row.BoltShown);
        });
    }

    [Fact]
    [Trait("Feature", "MA-8")]
    [Trait("Rule", "R-TEST-4")]
    public void Pressing_the_model_line_opens_the_apps_effort_slider_card_for_that_step_and_a_change_is_saved_with_the_pill()
    {
        var (rig, pill, accounts) = Setup();
        rig.Accounts.SetDefaults(accounts[0].Id, new Marrow.Core.Port.Accounts.AccountDefaults { Model = "opus", Effort = "high" });
        Open(rig, pill, (h, view) =>
        {
            var row = view.StepList[0];
            Assert.False(row.SettingsOpen);
            Assert.True(h.Press("step-config-" + accounts[0].Id.ToString("D")));
            Assert.True(row.SettingsOpen);
            Assert.NotNull(row.Panel!.Card);
            Assert.Contains(MultiAccountWords.StepTitle("Claude Max 1"), h.Words());
            Assert.Contains(MultiAccountWords.StepSub, h.Words());
            Assert.Equal("Opus 5.5", row.Panel.ModelShown);
            // the card's own bindings write the step: effort through the slider, fast through its button
            view.Model.SetStepSettings(0, new StepSettings("opus", "low", true));
            h.Settle();
            var saved = rig.Pills.Find(pill.Id)!.Steps[0];
            Assert.Equal(("opus", "low", true), (saved.Model, saved.Effort, saved.Fast));
            Assert.True(h.Press("step-config-" + accounts[0].Id.ToString("D")));
            Assert.False(row.SettingsOpen);
        });
    }

    [Fact]
    [Trait("Feature", "MA-8")]
    [Trait("Rule", "R-TEST-4")]
    public void The_handshake_point_is_one_slider_for_every_step_or_one_each_and_the_step_sliders_lock_in_the_first_case()
    {
        var (rig, pill, _) = Setup();
        Open(rig, pill, (h, view) =>
        {
            Assert.True(view.EveryOpen);
            Assert.All(view.StepList, r => Assert.True(r.Slider!.Locked));
            Assert.Equal(MultiAccountWords.HandshakeText("Smart"), view.EveryStep.ValueShown);
            view.Model.SetSame(HandshakePoint.At(95));
            h.Settle();
            Assert.Equal(MultiAccountWords.HandshakeText("95%"), view.EveryStep.ValueShown);
            Assert.Equal(MultiAccountWords.HandshakeText("95%"), view.StepList[1].Slider!.ValueShown);
            view.Model.SetMode(PillHandshakeMode.PerStep);
            h.Settle();
            Assert.False(view.EveryOpen);
            Assert.All(view.StepList, r => Assert.False(r.Slider!.Locked));
            Assert.Contains(MultiAccountWords.EachDescription, h.Words());
            view.Model.SetStepHandshake(2, HandshakePoint.At(99));
            h.Settle();
            Assert.Equal(MultiAccountWords.HandshakeText("99%"), view.StepList[2].Slider!.ValueShown);
            Assert.Equal(MultiAccountWords.HandshakeText("95%"), view.StepList[0].Slider!.ValueShown);
        });
    }

    [Fact]
    [Trait("Feature", "MA-8")]
    [Trait("Rule", "R-TEST-4")]
    public void The_step_sliders_dim_and_lock_over_frames_and_Reduce_Motion_does_it_at_once()
    {
        var (rig, pill, _) = Setup();
        rig.Pills.SetHandshakeMode(pill.Id, PillHandshakeMode.PerStep);
        Open(rig, pill, (h, view) =>
        {
            var slider = view.StepList[0].Slider!;
            Assert.False(slider.Locked);
            view.Model.SetMode(PillHandshakeMode.SameForEvery);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            var samples = new List<double> { slider.Dimness };
            for (var spent = 0; spent < 600; spent += 16) { h.Step(16); samples.Add(slider.Dimness); }
            Assert.Equal(0.45, samples[^1], 6);
            Assert.Contains(samples, d => d is > 0.47 and < 0.98);
        }, motion: true);
        Open(rig, pill, (h, view) =>
        {
            var slider = view.StepList[0].Slider!;
            view.Model.SetMode(PillHandshakeMode.PerStep);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, slider.Dimness, 6);
        }, motion: true, reduceMotion: true);
    }

    [Fact]
    [Trait("Feature", "MA-8")]
    [Trait("Rule", "R-TEST-4")]
    public void A_local_step_never_runs_out_and_has_no_slider()
    {
        var (rig, pill, _) = Setup(includeLocal: true);
        Open(rig, pill, (h, view) =>
        {
            var local = view.StepList[3];
            Assert.Null(local.Slider);
            Assert.True(local.NeverShown);
            Assert.Contains(MultiAccountWords.NeverRunsOut, h.Words());
            Assert.Equal("Local · " + MultiAccountWords.PillNoLimit, local.SubShown);
        });
    }

    [Fact]
    [Trait("Feature", "MA-8")]
    [Trait("Rule", "R-TEST-4")]
    public void Steps_can_be_added_removed_down_to_two_and_moved()
    {
        var (rig, pill, accounts) = Setup();
        var local = rig.Connect(rig.Accounts.ForService(ProviderKind.Local)[0]);
        Open(rig, pill, (h, view) =>
        {
            Assert.Equal([local.Id.ToString("D")], view.AddMenu.Select(e => e.Key));
            view.AddStep(local.Id.ToString("D"));
            h.Settle();
            Assert.Equal(4, view.StepList.Count);
            Assert.True(h.Press("step-remove-" + local.Id.ToString("D")));
            Assert.True(h.Press("step-remove-" + accounts[2].Id.ToString("D")));
            Assert.Equal(2, view.StepList.Count);
            Assert.True(h.Press("step-remove-" + accounts[1].Id.ToString("D")));
            Assert.Equal(2, view.StepList.Count);
            view.StepList[1].RaiseEvent(new Avalonia.Input.KeyEventArgs { RoutedEvent = Avalonia.Input.InputElement.KeyDownEvent, Key = Avalonia.Input.Key.Up, KeyModifiers = Avalonia.Input.KeyModifiers.Alt });
            h.Settle();
            Assert.Equal([accounts[1].Id.ToString("D"), accounts[0].Id.ToString("D")], rig.Pills.Find(pill.Id)!.Steps.Select(s => s.AccountId));
            Assert.Equal(["Claude Max 2", "Claude Max 1"], view.StepList.Select(r => r.Step.AccountName));
        });
    }

    [Fact]
    [Trait("Feature", "MA-8")]
    [Trait("Rule", "R-TEST-4")]
    public void The_earlier_account_rule_follows_the_pill_and_says_like_in_Settings_until_it_has_its_own()
    {
        var (rig, pill, _) = Setup();
        Open(rig, pill, (h, view) =>
        {
            Assert.Equal(MultiAccountWords.EarlierSettings, view.Earlier.ValueShown);
            view.Earlier.Choose(DecisionValues.GoBack);
            h.Settle();
            Assert.Equal(DecisionValues.GoBack, rig.Pills.Find(pill.Id)!.EarlierAccount);
            Assert.Equal(MultiAccountWords.EarlierGo, view.Earlier.ValueShown);
            Assert.Contains(MultiAccountWords.EarlierGoDescription, h.Words());
            view.Earlier.Choose("settings");
            h.Settle();
            Assert.Null(rig.Pills.Find(pill.Id)!.EarlierAccount);
        });
    }
}
