using Marrow.Core.Models;
using Marrow.Core.Port.MultiAccount.Settings;
using Marrow.Testing.MultiAccountSettings;

namespace Marrow.Core.Tests.Port.MultiAccount.Settings;

// MA-8 (step 2, the pieces the first report listed as missing): the OpenRouter "Keys and order" row, the Local block with "Add a local server" and its Connect, and where "Usage details" and "Keys and order" lead.
[Trait("Feature", "MA-8")]
[Trait("Rule", "R-TEST-4")]
public sealed class ServiceBlocksModelTests
{
    [Fact]
    public void The_local_block_is_always_there_so_the_first_server_can_be_added_and_never_offers_another_account()
    {
        var rig = new AccountsRig();
        var block = rig.Model.LocalBlock;
        Assert.Equal((ProviderKind.Local, "Local", MultiAccountWords.LocalNone), (block.Service, block.Title, block.Summary));
        Assert.True(block.ServerBox);
        Assert.False(block.CanAdd);
        Assert.Empty(block.Rows);
        Assert.DoesNotContain(rig.Model.Blocks, b => b.Service == ProviderKind.Local);
    }

    [Fact]
    public void Found_local_models_are_listed_as_rows_that_say_no_limit()
    {
        var rig = new AccountsRig();
        rig.Connect(ProviderKind.Local);
        rig.ConnectAnother(ProviderKind.Local);
        var block = rig.Model.LocalBlock;
        Assert.Equal(MultiAccountWords.LocalCount(2), block.Summary);
        Assert.True(block.Several);
        Assert.All(block.Rows, r => Assert.Equal(MultiAccountWords.NoLimit, r.UsageText));
        Assert.True(rig.Model.ShowRunOut);
    }

    [Fact]
    public async Task Connect_says_what_was_found_where_and_how_many_models_were_added()
    {
        var rig = new AccountsRig();
        rig.Actions.Local = new LocalConnectResult(LocalConnectOutcome.Found, 1, "this PC");
        Assert.Equal(MultiAccountWords.LocalFoundOn("this PC", 1), await rig.Model.ConnectLocalAsync("http://localhost:1234"));
        rig.Actions.Local = new LocalConnectResult(LocalConnectOutcome.Found, 3, "192.168.1.20");
        Assert.Equal(MultiAccountWords.LocalFoundOn("192.168.1.20", 3), await rig.Model.ConnectLocalAsync("192.168.1.20:11434"));
        Assert.Equal(["http://localhost:1234", "192.168.1.20:11434"], rig.Actions.LocalAddresses);
    }

    [Fact]
    public async Task Connect_that_finds_nothing_or_gets_a_bad_address_says_so_in_plain_words()
    {
        var rig = new AccountsRig();
        rig.Actions.Local = new LocalConnectResult(LocalConnectOutcome.NotFound, 0, "10.0.0.9");
        Assert.Equal(MultiAccountWords.LocalNotFound("10.0.0.9"), await rig.Model.ConnectLocalAsync("10.0.0.9"));
        rig.Actions.Local = new LocalConnectResult(LocalConnectOutcome.BadAddress, 0, "");
        Assert.Equal(MultiAccountWords.LocalBadAddress, await rig.Model.ConnectLocalAsync("not an address"));
        Assert.Equal(MultiAccountWords.LocalBadAddress, await rig.Model.ConnectLocalAsync("   "));
        Assert.Single(rig.Actions.LocalAddresses, a => a == "10.0.0.9");
    }

    [Fact]
    public void An_api_key_service_has_keys_and_order_not_add_another_account_and_counts_its_keys()
    {
        var rig = new AccountsRig();
        rig.Connect(ProviderKind.OpenRouter);
        var block = Assert.Single(rig.Model.Blocks);
        Assert.False(block.CanAdd);
        Assert.Equal(MultiAccountWords.KeysAndOrder, block.ActionLabel);
        Assert.Equal(MultiAccountWords.Keys(1, null), block.Summary);
        rig.ConnectAnother(ProviderKind.OpenRouter);
        block = Assert.Single(rig.Model.Blocks);
        Assert.Equal(MultiAccountWords.Keys(2, null), block.Summary);
        Assert.True(block.Several);
        Assert.DoesNotContain(rig.Model.BlockMenu(ProviderKind.OpenRouter), e => e.Key == "add");
    }

    [Fact]
    public void Credits_left_are_added_up_only_when_every_key_reports_them()
    {
        var rig = new AccountsRig();
        var a = rig.Connect(ProviderKind.OpenRouter);
        var b = rig.ConnectAnother(ProviderKind.OpenRouter);
        rig.Credits[a.Id] = 12.5;
        Assert.Equal(MultiAccountWords.Keys(2, null), rig.Model.Blocks[0].Summary);
        rig.Credits[b.Id] = 7.25;
        Assert.Equal(MultiAccountWords.Keys(2, 19.75), rig.Model.Blocks[0].Summary);
        Assert.Equal("2 keys · $19.75 of credits left", rig.Model.Blocks[0].Summary);
    }

    [Fact]
    public void A_service_that_signs_in_has_no_keys_button()
    {
        var rig = new AccountsRig();
        rig.Connect(ProviderKind.Claude, "Max");
        Assert.Null(Assert.Single(rig.Model.Blocks).ActionLabel);
    }

    [Fact]
    public void A_local_model_has_no_handshake_entry_in_its_menu()
    {
        var rig = new AccountsRig();
        var local = rig.Connect(ProviderKind.Local);
        Assert.DoesNotContain(rig.Model.Menu(local.Id), e => e.Key == "handshake");
        var claude = rig.Connect(ProviderKind.Claude, "Max");
        Assert.Contains(rig.Model.Menu(claude.Id), e => e.Key == "handshake");
    }

    [Fact]
    public void The_example_names_the_next_account_in_the_order_follows_the_bring_setting_and_the_handshake_switch()
    {
        var rig = new AccountsRig();
        rig.Connect(ProviderKind.Claude, "Max");
        rig.ConnectAnother(ProviderKind.Claude, "Max");
        var plain = rig.Model.Example();
        Assert.Equal(("to Claude Max 2", MultiAccountWords.HandedSummaryIntro("Claude Max 2")), (plain.Sub, plain.Intro));
        rig.Model.Choose("bring", Marrow.Core.Accounts.DecisionValues.Full);
        rig.Model.SetSwitch("hs", true);
        var full = rig.Model.Example();
        Assert.Equal((MultiAccountWords.HandedFullIntro, "to Claude Max 2 \u00b7 with a Handshake"), (full.Intro, full.Sub));
    }

    [Fact]
    public void With_no_second_account_the_example_says_your_next_account()
    {
        var rig = new AccountsRig();
        rig.Connect(ProviderKind.Claude, "Max");
        Assert.Equal("to " + MultiAccountWords.HandedNext, rig.Model.Example().Sub);
    }
}
