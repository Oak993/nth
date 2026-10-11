using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Marrow.App.Port.MultiAccount.Settings;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.Core.Accounts;
using Marrow.Core.Models;
using Marrow.Core.Port.MultiAccount.Settings;
using Marrow.Testing.MultiAccountSettings;
using Marrow.UI.Port.Settings;

namespace Marrow.App.Tests.Views.Settings.MultiAccount;

// MA-8 (T11 step 2), moved from Settings > Models to Settings > Providers by OD-0139 (ED-NEW-providersmerge-1): a service's accounts in its own card (one account: only "Add another account"; several: the list in the order
// nth uses them, its heading and the page's line on the order), and "When an account runs out" with its indented rows and the Handshake group at the page's foot, in grouped rows with the port's own switch; the owner-approved
// page's words. These were ModelsAccountsViewTests; each assertion keeps its strength or says more about the new place.
[Collection("Headless")]
public sealed class ProvidersAccountsTests(HeadlessSession headless)
{
    private void Run(AccountsRig rig, Action<MultiAccountHarness, ProvidersAccountsPreview> check, bool motion = false, bool reduceMotion = false) =>
        MultiAccountHarness.Run(headless, rig, () => new ProvidersAccountsPreview(rig.Model, ProviderKind.Claude), h => check(h, (ProvidersAccountsPreview)h.Page), motion, reduceMotion);

    private static AccountsRig One()
    {
        var rig = new AccountsRig();
        rig.Connect(ProviderKind.Claude, "Max");
        return rig;
    }

    private static AccountsRig Two()
    {
        var rig = One();
        rig.ConnectAnother(ProviderKind.Claude, "Max");
        return rig;
    }

    [Fact]
    [Trait("Feature", "MA-8")]
    [Trait("Rule", "R-TEST-4")]
    public void One_account_shows_only_the_add_row_in_its_card_with_no_list_and_none_of_the_multi_account_groups() => Run(One(), (h, view) =>
    {
        var block = Assert.Single(view.Blocks).Value;
        Assert.Equal((MultiAccountWords.AccountsHeading, MultiAccountWords.One("Max")), (block.TitleShown, block.SummaryShown));
        Assert.True(block.Shown);
        Assert.False(block.HeadingOpen); // the card's own status row says it is signed in
        Assert.False(block.ListOpen);
        Assert.True(block.AddOpen);
        Assert.False(block.FootOpen);
        Assert.False(view.RunOpen);
        Assert.False(view.HandshakeOpen);
        var words = h.Words();
        Assert.Contains(MultiAccountWords.AddAnother, words);
        Assert.DoesNotContain(MultiAccountWords.AccountsHeading, words);
        Assert.DoesNotContain(MultiAccountWords.AccountsFoot, words);
        Assert.DoesNotContain(MultiAccountWords.One("Max"), words);
        Assert.DoesNotContain(MultiAccountWords.RunHeading, words);
        Assert.DoesNotContain(MultiAccountWords.HandshakeHeading, words);
    });

    [Fact]
    [Trait("Feature", "MA-8")]
    [Trait("Rule", "R-TEST-4")]
    public void Several_accounts_open_the_list_and_the_run_out_and_handshake_groups_with_the_page_words() => Run(Two(), (h, view) =>
    {
        var block = Assert.Single(view.Blocks).Value;
        Assert.True(block.ListOpen);
        Assert.True(block.HeadingOpen);
        Assert.True(block.FootOpen);
        Assert.Equal(MultiAccountWords.Several(2), block.SummaryShown);
        Assert.Equal(["Claude Max 1", "Claude Max 2"], block.Rows.Values.OrderBy(r => r.View.Number).Select(r => r.NameShown));
        Assert.True(view.RunOpen);
        Assert.True(view.HandshakeOpen);
        var words = h.Words();
        foreach (var expected in new[] { MultiAccountWords.AccountsHeading, MultiAccountWords.Several(2), MultiAccountWords.AccountsFoot, MultiAccountWords.RunHeading, MultiAccountWords.RunFoot, MultiAccountWords.RunTitle, MultiAccountWords.RunNone, MultiAccountWords.HandshakeHeading, MultiAccountWords.HandshakeFoot, MultiAccountWords.UseHandshake, MultiAccountWords.UseHandshakeOff, MultiAccountWords.AddAnother })
            Assert.Contains(expected, words);
        Assert.Equal(2, h.Find(id => id.StartsWith("account-grip-", StringComparison.Ordinal)).Count()); // the order is in reach: a grip on each row
    });

    [Fact]
    [Trait("Feature", "MA-8")]
    [Trait("Rule", "R-TEST-4")]
    public void A_second_account_connected_while_the_page_shows_folds_the_list_and_the_groups_open() => Run(One(), (h, view) =>
    {
        h.Rig.ConnectAnother(ProviderKind.Claude, "Max");
        h.Rig.Model.Refresh();
        h.Settle();
        Assert.True(view.Blocks.Values.Single().ListOpen);
        Assert.True(view.Blocks.Values.Single().HeadingOpen);
        Assert.True(view.RunOpen);
        Assert.Contains(MultiAccountWords.RunHeading, h.Words());
        Assert.Contains(MultiAccountWords.AccountsFoot, h.Words());
    });

    [Fact]
    [Trait("Feature", "MA-8")]
    [Trait("Rule", "R-TEST-4")]
    public void The_list_folds_open_over_frames_and_Reduce_Motion_lands_it_at_once()
    {
        Run(One(), (h, view) =>
        {
            h.Rig.ConnectAnother(ProviderKind.Claude, "Max");
            h.Rig.Model.Refresh();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            var fold = view.Blocks.Values.Single();
            var samples = new List<double> { fold.ListFraction };
            for (var spent = 0; spent < 600; spent += 16) { h.Step(16); samples.Add(fold.ListFraction); }
            Assert.Equal(1, samples[^1], 6);
            Assert.Contains(samples, f => f is > 0.02 and < 0.98);
            Assert.True(samples.Zip(samples.Skip(1), (x, y) => y - x).All(d => d >= -1e-9));
        }, motion: true);
        Run(One(), (h, view) =>
        {
            h.Rig.ConnectAnother(ProviderKind.Claude, "Max");
            h.Rig.Model.Refresh();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, view.Blocks.Values.Single().ListFraction, 6);
        }, motion: true, reduceMotion: true);
    }

    [Fact]
    [Trait("Feature", "MA-8")]
    [Trait("Rule", "R-TEST-4")]
    public void Every_switch_on_the_page_is_the_ports_own_small_switch() => Run(Two(), (h, view) =>
    {
        var switches = h.All().OfType<SettingsSwitch>().ToList();
        Assert.Equal(3, switches.Count); // two accounts and "Use the Handshake"
        Assert.NotNull(h.TryFind("account-switch-" + h.Rig.Accounts.ForService(ProviderKind.Claude)[0].Id.ToString("N")));
    });

    [Fact]
    [Trait("Feature", "MA-8")]
    [Trait("Rule", "R-TEST-4")]
    public void An_account_turned_off_dims_and_says_off_not_used_and_turning_it_on_again_restores_it() => Run(Two(), (h, view) =>
    {
        var id = h.Rig.Accounts.ForService(ProviderKind.Claude)[0].Id;
        h.Rig.Model.SetEnabled(id, false);
        h.Settle();
        var row = view.Blocks.Values.Single().Rows[id];
        Assert.Equal(MultiAccountWords.PlanText("Max") + " · " + MultiAccountWords.OffNotUsed, row.SubtitleShown);
        // Retired Assert.True(row.Opacity < 1): T15-review-rulings.md #7 / ED-506.
        // HTML1459 dims .aname/.awk/.num only, keeping switch/menu bright; MAUI-040 pins the words.
        h.Rig.Model.SetEnabled(id, true);
        h.Settle();
        Assert.Equal(1, row.Opacity);
    });

    [Fact]
    [Trait("Feature", "MA-8")]
    [Trait("Rule", "R-TEST-4")]
    public void Tapping_the_name_renames_in_place_with_a_count_and_Enter_keeps_it() => Run(Two(), (h, view) =>
    {
        var id = h.Rig.Accounts.ForService(ProviderKind.Claude)[0].Id;
        var key = id.ToString("N");
        Assert.True(h.Press("account-name-" + key));
        var box = h.Find<TextBox>("account-rename-" + key);
        Assert.Equal(24, box.MaxLength);
        Assert.Contains("12/24", h.Words());
        box.Text = "Work";
        h.Settle();
        Assert.Contains("4/24", h.Words());
        box.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
        h.Settle();
        Assert.Equal("Work", h.Rig.Accounts.Get(id).CustomName);
        Assert.Equal("Work", view.Blocks.Values.Single().Rows[id].NameShown);
        Assert.False(view.Blocks.Values.Single().Rows[id].Renaming);
    });

    [Fact]
    [Trait("Feature", "MA-8")]
    [Trait("Rule", "R-TEST-4")]
    public void Escape_in_the_rename_box_keeps_the_old_name() => Run(Two(), (h, view) =>
    {
        var id = h.Rig.Accounts.ForService(ProviderKind.Claude)[0].Id;
        var key = id.ToString("N");
        Assert.True(h.Press("account-name-" + key));
        var box = h.Find<TextBox>("account-rename-" + key);
        box.Text = "Nope";
        box.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
        h.Settle();
        Assert.Null(h.Rig.Accounts.Get(id).CustomName);
        Assert.Equal("Claude Max 1", view.Blocks.Values.Single().Rows[id].NameShown);
    });

    [Fact]
    [Trait("Feature", "MA-8")]
    [Trait("Rule", "R-TEST-4")]
    public void The_menu_moves_signs_out_and_opens_handshake_and_alt_arrows_move_the_row() => Run(Two(), (h, view) =>
    {
        var rows = h.Rig.Accounts.ForService(ProviderKind.Claude);
        var block = view.Blocks.Values.Single();
        block.Rows[rows[1].Id].Pick("top");
        h.Settle();
        Assert.Equal([rows[1].Id, rows[0].Id], h.Rig.Accounts.ForService(ProviderKind.Claude).Select(a => a.Id));
        Assert.Equal(["Claude Max 2", "Claude Max 1"], block.Rows.Values.OrderBy(r => r.View.Number).Select(r => r.NameShown));
        var down = block.Rows[rows[1].Id];
        down.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Down, KeyModifiers = KeyModifiers.Alt });
        h.Settle();
        Assert.Equal([rows[0].Id, rows[1].Id], h.Rig.Accounts.ForService(ProviderKind.Claude).Select(a => a.Id));
        block.Rows[rows[0].Id].Pick("signout");
        Assert.Equal([rows[0].Id], h.Rig.Actions.SignedOut);
    });

    [Fact]
    [Trait("Feature", "MA-8")]
    [Trait("Rule", "R-TEST-4")]
    public void The_menu_is_the_apps_popover_menu_with_the_pages_entries() => Run(Two(), (h, view) =>
    {
        var id = h.Rig.Accounts.ForService(ProviderKind.Claude)[0].Id;
        var menu = MenuPopover.Build(h.Rig.Model.Menu(id), _ => { }, () => { });
        var host = new Border { Child = menu };
        var shown = new Avalonia.Controls.Window { Content = host };
        shown.Show();
        var words = shown.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        foreach (var expected in new[] { MultiAccountWords.Rename, MultiAccountWords.MoveUp, MultiAccountWords.MoveToTop, MultiAccountWords.MoveDown, MultiAccountWords.HandshakeAt, MultiAccountWords.SignOutOfThis })
            Assert.Contains(expected, words);
        shown.Close();
    });

    [Fact]
    [Trait("Feature", "MA-8")]
    [Trait("Rule", "R-TEST-4")]
    public void Add_another_account_asks_the_account_actions_for_that_service() => Run(Two(), (h, view) =>
    {
        Assert.True(h.Press("service-add-" + ProviderKind.Claude.Id));
        Assert.Equal([ProviderKind.Claude], h.Rig.Actions.Added);
        Assert.Single(h.Find(id => id.StartsWith("service-add-", StringComparison.Ordinal))); // one add action for the service
    });

    [Fact]
    [Trait("Feature", "MA-8")]
    [Trait("Rule", "R-TEST-4")]
    public void A_sign_in_that_fails_says_why_under_the_add_row() => Run(One(), (h, view) =>
    {
        h.Rig.Actions.AddFails = "Claude Code did not finish signing in.";
        Assert.True(h.Press("service-add-" + ProviderKind.Claude.Id));
        h.Until(() => h.Words().Contains("Claude Code did not finish signing in."));
        Assert.Equal("Claude Code did not finish signing in.", view.Block(ProviderKind.Claude).AddStatusShown);
    });

    [Fact]
    [Trait("Feature", "MA-8")]
    [Trait("Rule", "R-TEST-4")]
    public void A_dragged_row_lands_where_its_distance_points_inside_the_list()
    {
        Assert.Equal(2, AccountRowControl.DragTarget(0, 2 * AccountRowControl.RowHeight + 3, AccountRowControl.RowHeight, 3));
        Assert.Equal(0, AccountRowControl.DragTarget(1, -3 * AccountRowControl.RowHeight, AccountRowControl.RowHeight, 3));
        Assert.Equal(2, AccountRowControl.DragTarget(2, 30, AccountRowControl.RowHeight, 3));
        Assert.Equal(1, AccountRowControl.DragTarget(1, 10, AccountRowControl.RowHeight, 3));
    }

    [Fact]
    [Trait("Feature", "MA-8")]
    [Trait("Rule", "R-TEST-4")]
    public void The_run_out_row_starts_unset_and_choosing_opens_its_indented_rows_with_a_transition() => Run(Two(), (h, view) =>
    {
        var run = view.RunRows.Rows["run"].View;
        Assert.Equal(MultiAccountWords.RunNone, run.ValueShown);
        Assert.All(new[] { "mid", "back", "other", "bring", "handed" }, key => Assert.False(view.RunRows.Rows[key].Slot.Open, key));
        run.Choose(DecisionValues.Switch);
        h.Settle();
        Assert.Equal(DecisionValues.Switch, h.Rig.Decisions.GetSetting(DecisionKind.OnLimit));
        Assert.Equal(MultiAccountWords.RunGo, run.ValueShown);
        Assert.All(new[] { "mid", "back", "other", "bring", "handed" }, key => Assert.True(view.RunRows.Rows[key].Slot.Open, key));
        Assert.Contains(MultiAccountWords.MidTitle, h.Words());
        Assert.Contains(MultiAccountWords.RunGoDescription, h.Words());
        view.RunRows.Rows["bring"].View.Choose(DecisionValues.Full);
        h.Settle();
        Assert.Contains(MultiAccountWords.BringFullDescription, h.Words());
    });

    [Fact]
    [Trait("Feature", "MA-8")]
    [Trait("Rule", "R-TEST-4")]
    public void The_handshake_starts_off_and_its_switch_opens_when_to_start() => Run(Two(), (h, view) =>
    {
        var rows = view.HandshakeRows.Rows;
        Assert.False(h.Rig.Handshake.Enabled);
        Assert.False(rows["hsWhen"].Slot.Open);
        Assert.Contains(MultiAccountWords.UseHandshakeOff, h.Words());
        rows["hs"].View.Toggle(true);
        h.Settle();
        Assert.True(h.Rig.Handshake.Enabled);
        Assert.True(rows["hsWhen"].Slot.Open);
        Assert.Contains(MultiAccountWords.UseHandshakeOn, h.Words());
        rows["hsWhen"].View.Choose("97");
        h.Settle();
        Assert.Equal("At 97%", rows["hsWhen"].View.ValueShown);
        rows["hs"].View.Toggle(false);
        h.Settle();
        Assert.False(rows["hsWhen"].Slot.Open);
    });

    [Fact]
    [Trait("Feature", "MA-8")]
    [Trait("Rule", "R-TEST-4")]
    public void Usage_shows_the_week_bar_and_its_reset_beside_the_switch() => Run(Two(), (h, view) =>
    {
        var id = h.Rig.Accounts.ForService(ProviderKind.Claude)[0].Id;
        var reset = new DateTimeOffset(2026, 10, 11, 16, 0, 0, TimeSpan.Zero);
        h.Rig.Usage[id] = new AccountUsageLine(0.2, reset, 0.12, reset, false, false);
        h.Rig.Model.Refresh();
        h.Settle();
        var row = view.Blocks.Values.Single().Rows[id];
        Assert.Equal(MultiAccountWords.WeekText(12, "Sun 4:00 PM"), row.UsageShown);
        Assert.Equal(0.12, row.Bar.Target, 6);
        Assert.Contains(MultiAccountWords.WeekText(12, "Sun 4:00 PM"), h.Words());
    });

    [Fact]
    [Trait("Decision", "ED-NEW-providersmerge-1")]
    public void A_part_off_screen_does_not_listen_to_the_model_and_catches_up_when_it_is_back() => Run(One(), (h, view) =>
    {
        var block = view.Block(ProviderKind.Claude);
        var stack = (StackPanel)view.Child!;
        stack.Children.Remove(block);
        h.Settle();
        h.Rig.ConnectAnother(ProviderKind.Claude, "Max");
        h.Rig.Model.Refresh();
        h.Settle();
        Assert.False(block.ListOpen); // detached: no redraw (and no handler kept on the app-long model)
        stack.Children.Insert(0, block);
        h.Settle();
        Assert.True(block.ListOpen);
        Assert.Equal(2, block.Rows.Count);
    });
}
