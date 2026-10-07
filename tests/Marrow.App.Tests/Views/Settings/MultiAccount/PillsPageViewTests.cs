using Avalonia.Controls;
using Marrow.App.Port.MultiAccount.Settings;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Accounts;
using Marrow.Core.Models;
using Marrow.Core.Port.MultiAccount.Settings;
using Marrow.Testing.MultiAccountSettings;

namespace Marrow.App.Tests.Views.Settings.MultiAccount;

// MA-8 (T11 step 2, OD-0117): Settings > Pills. Always there; below two connected accounts its controls are present but dimmed and inert under one plain message with a button to Models, and they enable live, with a transition.
[Collection("Headless")]
public sealed class PillsPageViewTests(HeadlessSession headless)
{
    private void Run(AccountsRig rig, Action<MultiAccountHarness, PillsPageView> check, bool motion = false, bool reduceMotion = false) =>
        MultiAccountHarness.Run(headless, rig, () => new MultiAccountHost<PillsPageView>(context => new PillsPageView(context)), h => check(h, ((MultiAccountHost<PillsPageView>)h.Page).View!), motion, reduceMotion);

    private static AccountsRig Alone()
    {
        var rig = new AccountsRig();
        rig.Connect(ProviderKind.Claude, "Max");
        return rig;
    }

    private static AccountsRig Full()
    {
        var rig = Alone();
        rig.ConnectAnother(ProviderKind.Claude, "Max");
        rig.Connect(rig.Accounts.ForService(ProviderKind.Codex)[0], "Plus");
        return rig;
    }

    [Fact]
    [Trait("Feature", "MA-8")]
    [Trait("Rule", "R-TEST-4")]
    public void With_one_account_the_page_is_there_dimmed_and_inert_under_a_plain_message_and_a_Models_button() => Run(Alone(), (h, view) =>
    {
        Assert.True(view.ExplainOpen);
        Assert.False(view.ControlsEnabled);
        Assert.Equal(0.45, view.Dimness, 6);
        var words = h.Words();
        Assert.Contains(MultiAccountWords.DisabledWhat, words);
        Assert.Contains(MultiAccountWords.DisabledNeed, words);
        Assert.Contains(MultiAccountWords.PillsHeading, words);
        Assert.Contains(MultiAccountWords.NewPill, words);
        Assert.Contains(MultiAccountWords.UseForNew, words);
        Assert.False(h.Press("pills-new"));
        Assert.Empty(h.Rig.Pills.Pills);
        Assert.True(h.Press("pills-models"));
        Assert.Equal(1, h.OpenedModels);
    });

    [Fact]
    [Trait("Feature", "MA-8")]
    [Trait("Rule", "R-TEST-4")]
    public void With_two_accounts_everything_is_enabled_and_the_message_is_gone() => Run(Full(), (h, view) =>
    {
        Assert.False(view.ExplainOpen);
        Assert.True(view.ControlsEnabled);
        Assert.Equal(1, view.Dimness, 6);
    });

    [Fact]
    [Trait("Feature", "MA-8")]
    [Trait("Rule", "R-TEST-4")]
    public void Connecting_a_second_account_enables_the_page_live_over_frames() => Run(Alone(), (h, view) =>
    {
        h.Rig.ConnectAnother(ProviderKind.Claude, "Max");
        h.Rig.PillsModel.Refresh();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.True(view.ControlsEnabled);
        var folds = new List<double> { view.ExplainFraction };
        var dim = new List<double> { view.Dimness };
        for (var spent = 0; spent < 700; spent += 16) { h.Step(16); folds.Add(view.ExplainFraction); dim.Add(view.Dimness); }
        Assert.Equal(0, folds[^1], 6);
        Assert.Equal(1, dim[^1], 6);
        Assert.Contains(folds, f => f is > 0.02 and < 0.98);
        Assert.Contains(dim, d => d is > 0.47 and < 0.98);
        Assert.True(h.Press("pills-new"));
    }, motion: true);

    [Fact]
    [Trait("Feature", "MA-8")]
    [Trait("Rule", "R-TEST-4")]
    public void Under_Reduce_Motion_the_page_enables_at_once() => Run(Alone(), (h, view) =>
    {
        h.Rig.ConnectAnother(ProviderKind.Claude, "Max");
        h.Rig.PillsModel.Refresh();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, view.Dimness, 6);
        Assert.Equal(0, view.ExplainFraction, 6);
    }, motion: true, reduceMotion: true);

    [Fact]
    [Trait("Feature", "MA-8")]
    [Trait("Rule", "R-TEST-4")]
    public void Pills_are_drawn_as_pills_with_their_steps_in_words_under_them() => Run(Full(), (h, view) =>
    {
        var rig = h.Rig;
        var claude = rig.Accounts.ForService(ProviderKind.Claude);
        rig.MakePill("Work", claude[0], claude[1], rig.Accounts.ForService(ProviderKind.Codex)[0]);
        h.Settle();
        var row = Assert.Single(view.Rows).Value;
        Assert.Equal("Work", row.NameShown);
        Assert.Equal(3, row.Shape.MarkCount);
        Assert.Equal("Claude Max 1 → Claude Max 2 → Codex", row.SummaryShown);
        Assert.Contains(MultiAccountWords.PillsFoot, h.Words());
    });

    [Fact]
    [Trait("Feature", "MA-8")]
    [Trait("Rule", "R-TEST-4")]
    public void New_pill_makes_one_and_opens_it_in_the_editor() => Run(Full(), (h, view) =>
    {
        Assert.True(h.Press("pills-new"));
        Assert.Single(h.Rig.Pills.Pills);
        Assert.NotNull(view.Editor);
        Assert.Contains(MultiAccountWords.EditorTitle, h.Words());
        Assert.True(h.Press("pill-back"));
        Assert.Null(view.Editor);
        Assert.Contains(MultiAccountWords.PillsHeading, h.Words());
    });

    [Fact]
    [Trait("Feature", "MA-8")]
    [Trait("Rule", "R-TEST-4")]
    public void The_row_menu_edits_duplicates_renames_and_deletes_after_asking() => Run(Full(), (h, view) =>
    {
        var rig = h.Rig;
        var claude = rig.Accounts.ForService(ProviderKind.Claude);
        var pill = rig.MakePill("Work", claude[0], claude[1]);
        h.Settle();
        var row = view.Rows[pill.Id];
        row.Pick("duplicate");
        h.Settle();
        Assert.Equal(["Work", "Work copy"], rig.Pills.Pills.Select(p => p.Name));
        row.Pick("rename");
        var key = pill.Id.ToString("N");
        var box = h.Find<TextBox>("pill-rename-" + key);
        Assert.Equal(24, box.MaxLength);
        box.Text = "Deep work";
        box.RaiseEvent(new Avalonia.Input.KeyEventArgs { RoutedEvent = Avalonia.Input.InputElement.KeyDownEvent, Key = Avalonia.Input.Key.Enter });
        h.Settle();
        Assert.Equal("Deep work", rig.Pills.Find(pill.Id)!.Name);
        Assert.Equal("Deep work", row.NameShown);
        row.Pick("delete");
        h.Settle();
        Assert.True(row.ConfirmOpen);
        Assert.Contains(MultiAccountWords.ConfirmDelete("Deep work"), h.Words());
        Assert.True(h.Press("pill-delete-cancel-" + key));
        Assert.False(row.ConfirmOpen);
        Assert.NotNull(rig.Pills.Find(pill.Id));
        row.Pick("delete");
        h.Settle();
        Assert.True(h.Press("pill-delete-confirm-" + key));
        Assert.Null(rig.Pills.Find(pill.Id));
        Assert.DoesNotContain(pill.Id, view.Rows.Keys);
    });

    [Fact]
    [Trait("Feature", "MA-8")]
    [Trait("Rule", "R-TEST-4")]
    public void Escape_in_the_rename_box_keeps_the_name() => Run(Full(), (h, view) =>
    {
        var claude = h.Rig.Accounts.ForService(ProviderKind.Claude);
        var pill = h.Rig.MakePill("Work", claude[0], claude[1]);
        h.Settle();
        view.Rows[pill.Id].Pick("rename");
        var box = h.Find<TextBox>("pill-rename-" + pill.Id.ToString("N"));
        box.Text = "Nope";
        box.RaiseEvent(new Avalonia.Input.KeyEventArgs { RoutedEvent = Avalonia.Input.InputElement.KeyDownEvent, Key = Avalonia.Input.Key.Escape });
        h.Settle();
        Assert.Equal("Work", h.Rig.Pills.Find(pill.Id)!.Name);
    });

    [Fact]
    [Trait("Feature", "MA-8")]
    [Trait("Rule", "R-TEST-4")]
    public void Use_for_new_threads_follows_the_store() => Run(Full(), (h, view) =>
    {
        var claude = h.Rig.Accounts.ForService(ProviderKind.Claude);
        var pill = h.Rig.MakePill("Work", claude[0], claude[1]);
        h.Settle();
        Assert.Equal(MultiAccountWords.UseNone, view.Use.ValueShown);
        view.Use.Choose(pill.Id.ToString("D"));
        h.Settle();
        Assert.Equal(pill.Id, h.Rig.Pills.UseForNewThreads);
        Assert.Equal("Work", view.Use.ValueShown);
        Assert.Contains(MultiAccountWords.UsePillDescription, h.Words());
    });

    [Fact]
    [Trait("Feature", "MA-8")]
    [Trait("Rule", "R-TEST-4")]
    public void A_pill_deleted_while_it_is_open_returns_to_the_list() => Run(Full(), (h, view) =>
    {
        Assert.True(h.Press("pills-new"));
        var id = h.Rig.Pills.Pills[0].Id;
        h.Rig.PillsModel.Delete(id);
        h.Settle();
        Assert.Null(view.Editor);
        Assert.Empty(view.Rows);
    });
}
