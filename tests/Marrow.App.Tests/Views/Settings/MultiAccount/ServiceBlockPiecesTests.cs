using Avalonia.Controls;
using Marrow.App.Port.MultiAccount.Settings;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Accounts;
using Marrow.Core.Models;
using Marrow.Core.Port.MultiAccount.Settings;
using Marrow.Testing.MultiAccountSettings;

namespace Marrow.App.Tests.Views.Settings.MultiAccount;

// MA-8 (step 2, the pieces the first report listed as missing): the OpenRouter "Keys and order" row, the Local block with "Add a local server" and Connect, "Usage details" for the right service and the "Show an example" sheet.
[Collection("Headless")]
public sealed class ServiceBlockPiecesTests(HeadlessSession headless)
{
    private void Run(AccountsRig rig, Action<MultiAccountHarness, ModelsAccountsView> check, bool motion = false, bool reduceMotion = false) =>
        MultiAccountHarness.Run(headless, rig, () => new MultiAccountHost<ModelsAccountsView>(context => new ModelsAccountsView(context.Accounts, context.OpenUsage, context.OpenKeys)),
            h => check(h, ((MultiAccountHost<ModelsAccountsView>)h.Page).View!), motion, reduceMotion);

    private static AccountsRig Keys()
    {
        var rig = new AccountsRig();
        rig.Connect(ProviderKind.OpenRouter);
        return rig;
    }

    [Fact]
    [Trait("Feature", "MA-8")]
    [Trait("Rule", "R-TEST-4")]
    public void The_openrouter_row_counts_its_keys_and_Keys_and_order_opens_the_place_where_keys_are_kept() => Run(Keys(), (h, view) =>
    {
        var block = view.Blocks[ProviderKind.OpenRouter];
        Assert.Equal(MultiAccountWords.Keys(1, null), block.SummaryShown);
        Assert.Contains(MultiAccountWords.KeysAndOrder, h.Words());
        Assert.Null(h.TryFind("service-add-" + ProviderKind.OpenRouter.Id));
        Assert.True(h.Press("service-keys-" + ProviderKind.OpenRouter.Id));
        Assert.Equal([ProviderKind.OpenRouter], h.KeysOpened);
        h.Rig.ConnectAnother(ProviderKind.OpenRouter);
        h.Rig.Model.Refresh();
        h.Settle();
        Assert.True(block.ListOpen);
        Assert.Equal(MultiAccountWords.Keys(2, null), block.SummaryShown);
    });

    [Fact]
    [Trait("Feature", "MA-8")]
    [Trait("Rule", "R-TEST-4")]
    public void A_service_that_signs_in_has_no_keys_button() => Run(new AccountsRig(), (h, view) =>
    {
        h.Rig.Connect(ProviderKind.Claude, "Max");
        h.Rig.Model.Refresh();
        h.Settle();
        Assert.Null(h.TryFind("service-keys-" + ProviderKind.Claude.Id));
    });

    [Fact]
    [Trait("Feature", "MA-8")]
    [Trait("Rule", "R-TEST-4")]
    public void Usage_details_opens_the_service_the_menu_belongs_to() => Run(Keys(), (h, view) =>
    {
        h.Rig.Connect(ProviderKind.Claude, "Max");
        h.Rig.Model.Refresh();
        h.Settle();
        view.Blocks[ProviderKind.Claude].Pick("usage");
        view.Blocks[ProviderKind.OpenRouter].Pick("usage");
        Assert.Equal([ProviderKind.Claude, ProviderKind.OpenRouter], h.UsageOpened);
    });

    [Fact]
    [Trait("Feature", "MA-8")]
    [Trait("Rule", "R-TEST-4")]
    public void The_local_block_is_there_with_nothing_found_and_its_box_folds_open() => Run(new AccountsRig(), (h, view) =>
    {
        Assert.Contains(MultiAccountWords.LocalTitle, h.Words());
        Assert.Contains(MultiAccountWords.LocalNone, h.Words());
        Assert.Contains(MultiAccountWords.AddLocal, h.Words());
        Assert.False(view.LocalBox.Open);
        Assert.True(h.Press("local-add"));
        Assert.True(view.LocalBox.Open);
        var address = h.Find<TextBox>("local-address");
        Assert.Equal(MultiAccountWords.LocalPlaceholder, address.PlaceholderText);
        Assert.Contains(MultiAccountWords.LocalNote, h.Words());
        Assert.True(h.Press("local-add"));
        Assert.False(view.LocalBox.Open);
    });

    [Fact]
    [Trait("Feature", "MA-8")]
    [Trait("Rule", "R-TEST-4")]
    public void Connect_shows_looking_while_it_looks_then_what_it_found() => Run(new AccountsRig(), (h, view) =>
    {
        var hold = new TaskCompletionSource();
        h.Rig.Actions.LocalHold = hold.Task;
        h.Rig.Actions.Local = new LocalConnectResult(LocalConnectOutcome.Found, 2, "192.168.1.20");
        Assert.True(h.Press("local-add"));
        h.Find<TextBox>("local-address").Text = "192.168.1.20:11434";
        Assert.True(h.Press("local-connect"));
        Assert.Contains(MultiAccountWords.LocalLooking, h.Words());
        hold.SetResult();
        h.Until(() => h.Words().Contains(MultiAccountWords.LocalFoundOn("192.168.1.20", 2)));
        Assert.Equal(["192.168.1.20:11434"], h.Rig.Actions.LocalAddresses);
        Assert.DoesNotContain(MultiAccountWords.LocalLooking, h.Words());
    });

    [Fact]
    [Trait("Feature", "MA-8")]
    [Trait("Rule", "R-TEST-4")]
    public void Connect_with_an_empty_box_asks_for_an_address_and_asks_nothing_of_the_actions() => Run(new AccountsRig(), (h, view) =>
    {
        Assert.True(h.Press("local-add"));
        Assert.True(h.Press("local-connect"));
        h.Until(() => h.Words().Contains(MultiAccountWords.LocalBadAddress));
        Assert.Empty(h.Rig.Actions.LocalAddresses);
    });

    [Fact]
    [Trait("Feature", "MA-8")]
    [Trait("Rule", "R-TEST-4")]
    public void Found_local_models_are_rows_and_the_block_counts_them() => Run(new AccountsRig(), (h, view) =>
    {
        h.Rig.Connect(ProviderKind.Local);
        h.Rig.ConnectAnother(ProviderKind.Local);
        h.Rig.Model.Refresh();
        h.Settle();
        var block = view.LocalBlockView;
        Assert.Equal(MultiAccountWords.LocalCount(2), block.SummaryShown);
        Assert.True(block.ListOpen);
        Assert.Equal(2, block.Rows.Count);
        Assert.Null(h.TryFind("service-add-" + ProviderKind.Local.Id));
    });

    [Fact]
    [Trait("Feature", "MA-8")]
    [Trait("Rule", "R-TEST-4")]
    public void Show_an_example_opens_the_sheet_with_the_pages_words_and_Close_puts_it_away() => Run(Two(), (h, view) =>
    {
        view.RunRows.Rows["run"].View.Choose(DecisionValues.Switch);
        h.Settle();
        Assert.True(h.Press("setting-handed-button"));
        h.Until(() => h.TryFind("handed-sheet") is not null);
        var words = h.Words(h.Find<Control>("handed-sheet"));
        foreach (var expected in new[] { MultiAccountWords.HandedTitle, "to Claude Max 2", MultiAccountWords.HandedSummaryIntro("Claude Max 2"), "Your messages", "File reads", "handoff-thread-0412.md", "\u00b7 " + MultiAccountWords.HandedFileLine })
            Assert.Contains(expected, words);
        Assert.True(h.Press("handed-close"));
        h.Until(() => h.TryFind("handed-sheet") is null);
    });

    [Fact]
    [Trait("Feature", "MA-8")]
    [Trait("Rule", "R-TEST-4")]
    public void The_example_follows_the_setting_and_the_handshake() => Run(Two(), (h, view) =>
    {
        view.RunRows.Rows["run"].View.Choose(DecisionValues.Switch);
        view.RunRows.Rows["bring"].View.Choose(DecisionValues.Full);
        view.HandshakeRows.Rows["hs"].View.Toggle(true);
        h.Settle();
        Assert.True(h.Press("setting-handed-button"));
        h.Until(() => h.TryFind("handed-sheet") is not null);
        var words = h.Words(h.Find<Control>("handed-sheet"));
        Assert.Contains(MultiAccountWords.HandedFullIntro, words);
        Assert.Contains("to Claude Max 2 · with a Handshake", words);
        Assert.Contains(MultiAccountWords.HandedHandshakeLine("Claude Max 2"), words);
        Assert.DoesNotContain("Your messages", words);
    });

    private static AccountsRig Two()
    {
        var rig = new AccountsRig();
        rig.Connect(ProviderKind.Claude, "Max");
        rig.ConnectAnother(ProviderKind.Claude, "Max");
        return rig;
    }
}
