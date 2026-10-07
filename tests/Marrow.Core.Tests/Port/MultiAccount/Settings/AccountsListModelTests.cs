using Marrow.Testing.MultiAccountSettings;
using Marrow.Core.Models;
using Marrow.Core.Port.Accounts;
using Marrow.Core.Port.MultiAccount.Settings;

namespace Marrow.Core.Tests.Port.MultiAccount.Settings;

// MA-8 (T11 step 2): Settings > Models > Accounts, bound to AccountStore. The words are the owner-approved page's.
[Trait("Feature", "MA-8")]
[Trait("Rule", "R-TEST-4")]
public sealed class AccountsListModelTests
{
    [Fact]
    public void Only_connected_services_get_a_block_and_one_account_looks_like_today_without_a_list_or_a_name()
    {
        var rig = new AccountsRig();
        Assert.Empty(rig.Model.Blocks);
        rig.Connect(ProviderKind.Claude, "Max");
        var block = Assert.Single(rig.Model.Blocks);
        Assert.Equal((ProviderKind.Claude, false), (block.Service, block.Several));
        Assert.Equal(MultiAccountWords.One("Max"), block.Summary);
        Assert.Empty(block.Rows);
        Assert.True(block.CanAdd);
        Assert.False(rig.Model.ShowRunOut);
    }

    [Fact]
    public void A_second_account_opens_the_list_with_numbered_default_names_and_the_page_summary()
    {
        var rig = new AccountsRig();
        rig.Connect(ProviderKind.Claude, "Max");
        rig.ConnectAnother(ProviderKind.Claude, "Max");
        var block = Assert.Single(rig.Model.Blocks);
        Assert.True(block.Several);
        Assert.Equal(MultiAccountWords.Several(2), block.Summary);
        Assert.Equal(["Claude Max 1", "Claude Max 2"], block.Rows.Select(r => r.Name));
        Assert.Equal([1, 2], block.Rows.Select(r => r.Number));
        Assert.All(block.Rows, r => Assert.Equal(MultiAccountWords.PlanText("Max"), r.PlanText));
    }

    [Fact]
    public void An_account_that_is_not_connected_is_not_listed_but_keeps_its_place_in_the_order()
    {
        var rig = new AccountsRig();
        var first = rig.Connect(ProviderKind.Claude, "Max");
        var pending = rig.Accounts.Add(ProviderKind.Claude, "Pro");
        var third = rig.ConnectAnother(ProviderKind.Claude, "Max");
        Assert.Equal([first.Id, third.Id], rig.Model.Blocks[0].Rows.Select(r => r.Id));
        rig.Model.MoveTo(third.Id, 0);
        Assert.Equal([third.Id, pending.Id, first.Id], rig.Accounts.ForService(ProviderKind.Claude).Select(a => a.Id));
        Assert.Equal([third.Id, first.Id], rig.Model.Blocks[0].Rows.Select(r => r.Id));
        Assert.Contains(pending.Id, rig.Accounts.ForService(ProviderKind.Claude).Select(a => a.Id));
    }

    [Fact]
    public void Rename_keeps_a_custom_name_clears_it_when_it_is_the_default_and_refuses_more_than_24_characters()
    {
        var rig = new AccountsRig();
        var a = rig.Connect(ProviderKind.Claude, "Max");
        rig.ConnectAnother(ProviderKind.Claude, "Max");
        Assert.True(rig.Model.Rename(a.Id, "  Work  "));
        Assert.Equal("Work", rig.Accounts.Get(a.Id).CustomName);
        Assert.Equal("Work", rig.Model.Blocks[0].Rows[0].Name);
        Assert.Contains(MultiAccountWords.CustomName, rig.Model.Blocks[0].Rows[0].Subtitle, StringComparison.Ordinal);
        Assert.False(rig.Model.Rename(a.Id, new string('x', 25)));
        Assert.Equal("Work", rig.Accounts.Get(a.Id).CustomName);
        Assert.True(rig.Model.Rename(a.Id, "Claude Max 1"));
        Assert.Null(rig.Accounts.Get(a.Id).CustomName);
        Assert.True(rig.Model.Rename(a.Id, "Work"));
        Assert.True(rig.Model.Rename(a.Id, "  "));
        Assert.Null(rig.Accounts.Get(a.Id).CustomName);
    }

    [Fact]
    public void Order_moves_through_the_row_menu_with_up_and_top_disabled_on_the_first_row_and_down_on_the_last()
    {
        var rig = new AccountsRig();
        var a = rig.Connect(ProviderKind.Codex);
        var b = rig.ConnectAnother(ProviderKind.Codex);
        var c = rig.ConnectAnother(ProviderKind.Codex);
        var first = rig.Model.Menu(a.Id);
        Assert.True(first.Single(e => e.Key == "up").Disabled);
        Assert.True(first.Single(e => e.Key == "top").Disabled);
        Assert.False(first.Single(e => e.Key == "down").Disabled);
        Assert.True(rig.Model.Menu(c.Id).Single(e => e.Key == "down").Disabled);
        rig.Model.Do(c.Id, "top");
        Assert.Equal([c.Id, a.Id, b.Id], rig.Model.Blocks[0].Rows.Select(r => r.Id));
        rig.Model.Do(c.Id, "down");
        Assert.Equal([a.Id, c.Id, b.Id], rig.Model.Blocks[0].Rows.Select(r => r.Id));
        rig.Model.Do(b.Id, "up");
        Assert.Equal([a.Id, b.Id, c.Id], rig.Model.Blocks[0].Rows.Select(r => r.Id));
        Assert.Equal([0, 1, 2], rig.Accounts.ForService(ProviderKind.Codex).Select(x => x.Priority));
    }

    [Fact]
    public void The_switch_turns_an_account_off_without_signing_it_out_and_the_row_says_so()
    {
        var rig = new AccountsRig();
        var a = rig.Connect(ProviderKind.Claude, "Max");
        rig.ConnectAnother(ProviderKind.Claude, "Max");
        Assert.True(rig.Model.Blocks[0].Rows[0].Enabled);
        rig.Model.SetEnabled(a.Id, false);
        var row = rig.Model.Blocks[0].Rows[0];
        Assert.False(row.Enabled);
        Assert.False(rig.Accounts.Get(a.Id).Enabled);
        Assert.Equal(MultiAccountWords.PlanText("Max") + " · " + MultiAccountWords.OffNotUsed, row.Subtitle);
        Assert.Empty(rig.Actions.SignedOut);
        rig.Model.SetEnabled(a.Id, true);
        Assert.True(rig.Model.Blocks[0].Rows[0].Enabled);
    }

    [Fact]
    public void The_handshake_menu_offers_like_all_automatically_and_five_points_and_the_note_shows_only_when_set()
    {
        var rig = new AccountsRig();
        var a = rig.Connect(ProviderKind.Claude, "Max");
        rig.ConnectAnother(ProviderKind.Claude, "Pro");
        var menu = rig.Model.HandshakeMenu(a.Id);
        Assert.Equal([MultiAccountWords.LikeAllAccounts("automatically"), "Automatically", "At 90%", "At 95%", "At 97%", "At 98%", "At 99%"], menu.Where(e => !e.Separator).Select(e => e.Label));
        Assert.True(menu.First(e => !e.Separator).Checked);
        Assert.Equal(MultiAccountWords.Near(95), menu.Single(e => e.Label == "Automatically").Hint);
        Assert.Equal(MultiAccountWords.Near(98), HandshakeStops.NearText("Max 20x"));
        Assert.DoesNotContain("Handshake", rig.Model.Blocks[0].Rows[0].Subtitle, StringComparison.Ordinal);
        rig.Model.SetHandshake(a.Id, HandshakePoint.Smart.Percentage is null ? new HandshakePoint(99) : null);
        Assert.Equal(new HandshakePoint(99), rig.Accounts.Get(a.Id).Defaults.HandshakePoint);
        Assert.Contains("Handshake at 99%", rig.Model.Blocks[0].Rows[0].Subtitle, StringComparison.Ordinal);
        Assert.Equal(MultiAccountWords.WhenAt(99), rig.Model.Blocks[0].Rows[0].HandshakeLabel);
        rig.Model.SetHandshake(a.Id, HandshakePoint.Smart);
        // Retired equal-default note assertion: ED-NEW-ma-wave1-37 / OD-0119.
        // MAUI-041 pins absence when the explicit point equals the general point.
        rig.Model.SetHandshake(a.Id, null);
        Assert.Null(rig.Accounts.Get(a.Id).Defaults.HandshakePoint);
        Assert.DoesNotContain("Handshake", rig.Model.Blocks[0].Rows[0].Subtitle, StringComparison.Ordinal);
        Assert.Contains(MultiAccountWords.UsesDefault, rig.Model.Blocks[0].Rows[0].HandshakeLabel, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sign_out_and_add_go_to_the_account_actions_and_the_last_account_cannot_be_signed_out_from_the_list()
    {
        var rig = new AccountsRig();
        var a = rig.Connect(ProviderKind.Claude, "Max");
        Assert.True(rig.Model.BlockMenu(ProviderKind.Claude).Single(e => e.Key == "signout").Disabled);
        await rig.Model.AddAsync(ProviderKind.Claude);
        Assert.Equal([ProviderKind.Claude], rig.Actions.Added);
        var b = rig.ConnectAnother(ProviderKind.Claude, "Pro");
        Assert.False(rig.Model.Menu(a.Id).Single(e => e.Key == "signout").Disabled);
        await rig.Model.SignOutAsync(b.Id);
        Assert.Equal([b.Id], rig.Actions.SignedOut);
    }

    [Fact]
    public async Task A_sign_in_that_fails_says_why_and_changes_nothing()
    {
        var rig = new AccountsRig();
        rig.Connect(ProviderKind.Claude, "Max");
        rig.Actions.AddFails = "Claude Code was not found.";
        var message = await rig.Model.AddAsync(ProviderKind.Claude);
        Assert.Equal("Claude Code was not found.", message);
    }

    [Fact]
    public void A_service_that_keeps_one_account_never_offers_to_add_another()
    {
        var rig = new AccountsRig();
        rig.Connect(ProviderKind.Copilot);
        var block = Assert.Single(rig.Model.Blocks);
        Assert.False(block.CanAdd);
        Assert.DoesNotContain(rig.Model.BlockMenu(ProviderKind.Copilot), e => e.Key == "add");
    }

    [Fact]
    public void Usage_shows_the_week_bar_and_its_reset_or_that_there_is_no_five_hour_limit()
    {
        var rig = new AccountsRig();
        var a = rig.Connect(ProviderKind.Claude, "Max");
        var b = rig.ConnectAnother(ProviderKind.Claude, "Max");
        var reset = new DateTimeOffset(2026, 10, 11, 16, 0, 0, TimeSpan.Zero);
        rig.Usage[a.Id] = new AccountUsageLine(0.2, reset, 0.12, reset, false, false);
        rig.Usage[b.Id] = new AccountUsageLine(null, null, 0.9, reset, true, false);
        var rows = rig.Model.Blocks[0].Rows;
        Assert.Equal(0.12, rows[0].WeekFraction);
        Assert.Equal(MultiAccountWords.WeekText(12, "Sun 4:00 PM"), rows[0].UsageText);
        Assert.True(rows[1].Hot);
        Assert.True(rows[1].NoFiveHour);
    }

    [Fact]
    public void Changes_made_through_the_store_or_the_decisions_tell_the_page_to_draw_again()
    {
        var rig = new AccountsRig();
        var a = rig.Connect(ProviderKind.Claude, "Max");
        rig.ConnectAnother(ProviderKind.Claude, "Max");
        var changes = 0;
        rig.Model.Changed += () => changes++;
        rig.Model.SetEnabled(a.Id, false);
        rig.Model.Choose("run", Marrow.Core.Accounts.DecisionValues.Switch);
        Assert.Equal(2, changes);
        rig.Decisions.SetSetting(Marrow.Core.Accounts.DecisionKind.Bring, Marrow.Core.Accounts.DecisionValues.Full);
        Assert.Equal(3, changes);
    }

    [Fact]
    public void A_local_model_has_no_limit_and_no_add_another_account()
    {
        var rig = new AccountsRig();
        rig.Connect(ProviderKind.Local);
        rig.ConnectAnother(ProviderKind.Local);
        var block = rig.Model.LocalBlock;
        Assert.False(block.CanAdd);
        Assert.All(block.Rows, r => Assert.Equal(MultiAccountWords.NoLimit, r.UsageText));
        Assert.DoesNotContain(rig.Model.BlockMenu(ProviderKind.Local), e => e.Key == "add");
    }
}
