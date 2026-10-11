using Avalonia.Automation;
using Avalonia.Controls;
using Marrow.App.Port.MultiAccount.Settings;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Accounts;
using Marrow.Core.Models;
using Marrow.Core.Port.MultiAccount.Settings;
using Marrow.Testing.MultiAccountSettings;

namespace Marrow.App.Tests.Views.Settings.MultiAccount;

// MA-8 (step 2, the pieces the first report listed as missing), in the service's own card on Settings > Providers since OD-0139 (ED-NEW-providersmerge-1): an API-key service's "Keys and order" (its keys listed in its
// card, no jump to anywhere), the Local models with "Add a local server" and Connect, and the "Show an example" sheet. The Models page's "Usage details" and "Keys and order" jumps are gone: the card shows the usage and
// the keys itself.
[Collection("Headless")]
public sealed class ServiceBlockPiecesTests(HeadlessSession headless)
{
    private void Run(AccountsRig rig, Action<MultiAccountHarness, ProvidersAccountsPreview> check, params ProviderKind[] services) =>
        MultiAccountHarness.Run(headless, rig, () => new MultiAccountHost<ProvidersAccountsPreview>(context => new ProvidersAccountsPreview(context.Accounts, services)),
            h => check(h, ((MultiAccountHost<ProvidersAccountsPreview>)h.Page).View!));

    private static AccountsRig Keys()
    {
        var rig = new AccountsRig();
        rig.Connect(ProviderKind.OpenRouter);
        return rig;
    }

    [Fact]
    [Trait("Feature", "MA-8")]
    [Trait("Rule", "R-TEST-4")]
    public void The_openrouter_card_counts_its_keys_lists_them_under_Keys_and_order_and_offers_another_key_not_another_account() => Run(Keys(), (h, view) =>
    {
        var block = view.Block(ProviderKind.OpenRouter);
        Assert.Equal(MultiAccountWords.Keys(1, null), block.SummaryShown);
        Assert.Equal(MultiAccountWords.KeysAndOrder, block.TitleShown);
        Assert.False(block.ListOpen); // one key is the card's "API key" box
        Assert.NotNull(h.TryFind("service-add-key-" + ProviderKind.OpenRouter.Id));
        Assert.Null(h.TryFind("service-add-" + ProviderKind.OpenRouter.Id));
        Assert.Null(h.TryFind("service-keys-" + ProviderKind.OpenRouter.Id)); // the jump to Providers is gone: this is Providers
        h.Rig.ConnectAnother(ProviderKind.OpenRouter);
        h.Rig.Model.Refresh();
        h.Settle();
        Assert.True(block.ListOpen);
        Assert.Equal(MultiAccountWords.Keys(2, null), block.SummaryShown);
        Assert.Equal(2, block.Rows.Count);
        var words = h.Words();
        foreach (var expected in new[] { MultiAccountWords.KeysAndOrder, MultiAccountWords.Keys(2, null), MultiAccountWords.KeysFoot, MultiAccountWords.AddKey })
            Assert.Contains(expected, words);
        Assert.DoesNotContain(MultiAccountWords.AddAnother, words);
    }, ProviderKind.OpenRouter);

    [Fact]
    [Trait("Feature", "MA-8")]
    [Trait("Rule", "R-TEST-4")]
    public void A_service_that_signs_in_has_no_keys_button_and_no_key_row() => Run(new AccountsRig(), (h, view) =>
    {
        h.Rig.Connect(ProviderKind.Claude, "Max");
        h.Rig.Model.Refresh();
        h.Settle();
        Assert.Null(h.TryFind("service-keys-" + ProviderKind.Claude.Id));
        Assert.Null(h.TryFind("service-add-key-" + ProviderKind.Claude.Id));
        Assert.NotNull(h.TryFind("service-add-" + ProviderKind.Claude.Id));
    }, ProviderKind.Claude);

    [Fact]
    [Trait("Feature", "MA-8")]
    [Trait("Rule", "R-TEST-4")]
    public void No_usage_details_or_keys_jump_or_service_menu_is_left_in_the_cards() => Run(Keys(), (h, view) =>
    {
        h.Rig.Connect(ProviderKind.Claude, "Max");
        h.Rig.ConnectAnother(ProviderKind.Claude, "Max");
        h.Rig.ConnectAnother(ProviderKind.OpenRouter);
        h.Rig.Model.Refresh();
        h.Settle();
        Assert.Empty(h.Find(id => id.StartsWith("service-more-", StringComparison.Ordinal) || id.StartsWith("service-keys-", StringComparison.Ordinal)));
        Assert.DoesNotContain(h.All().OfType<Button>(), b => AutomationProperties.GetName(b) is MultiAccountWords.UsageDetails or MultiAccountWords.KeysAndOrder);
        Assert.DoesNotContain(MultiAccountWords.UsageDetails, h.Words());
        Assert.True(view.Block(ProviderKind.Claude).ListOpen);
        Assert.True(view.Block(ProviderKind.OpenRouter).ListOpen);
    }, ProviderKind.Claude, ProviderKind.OpenRouter);

    [Fact]
    [Trait("Feature", "MA-8")]
    [Trait("Rule", "R-TEST-4")]
    public void The_local_block_is_there_with_nothing_found_and_its_box_folds_open() => Run(new AccountsRig(), (h, view) =>
    {
        Assert.Contains(MultiAccountWords.LocalModelsHeading, h.Words());
        Assert.Contains(MultiAccountWords.LocalNone, h.Words());
        Assert.Contains(MultiAccountWords.AddLocal, h.Words());
        var box = view.Block(ProviderKind.Local).ServerBox!;
        Assert.False(box.Open);
        Assert.True(h.Press("local-add"));
        Assert.True(box.Open);
        var address = h.Find<TextBox>("local-address");
        Assert.Equal(MultiAccountWords.LocalPlaceholder, address.PlaceholderText);
        Assert.Contains(MultiAccountWords.LocalNote, h.Words());
        Assert.True(h.Press("local-add"));
        Assert.False(box.Open);
    }, ProviderKind.Local);

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
    }, ProviderKind.Local);

    [Fact]
    [Trait("Feature", "MA-8")]
    [Trait("Rule", "R-TEST-4")]
    public void Connect_with_an_empty_box_asks_for_an_address_and_asks_nothing_of_the_actions() => Run(new AccountsRig(), (h, view) =>
    {
        Assert.True(h.Press("local-add"));
        Assert.True(h.Press("local-connect"));
        h.Until(() => h.Words().Contains(MultiAccountWords.LocalBadAddress));
        Assert.Empty(h.Rig.Actions.LocalAddresses);
    }, ProviderKind.Local);

    [Fact]
    [Trait("Feature", "MA-8")]
    [Trait("Rule", "R-TEST-4")]
    public void Found_local_models_are_rows_and_the_block_counts_them() => Run(new AccountsRig(), (h, view) =>
    {
        h.Rig.Connect(ProviderKind.Local);
        h.Rig.ConnectAnother(ProviderKind.Local);
        h.Rig.Model.Refresh();
        h.Settle();
        var block = view.Block(ProviderKind.Local);
        Assert.Equal(MultiAccountWords.LocalCount(2), block.SummaryShown);
        Assert.True(block.ListOpen);
        Assert.Equal(2, block.Rows.Count);
        Assert.Null(h.TryFind("service-add-" + ProviderKind.Local.Id));
        Assert.Contains(MultiAccountWords.LocalCount(2), h.Words());
    }, ProviderKind.Local);

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
        foreach (var expected in new[] { MultiAccountWords.HandedTitle, "to Claude Max 2", MultiAccountWords.HandedSummaryIntro("Claude Max 2"), "Your messages", "File reads", "handoff-thread-0412.md", "· " + MultiAccountWords.HandedFileLine })
            Assert.Contains(expected, words);
        Assert.True(h.Press("handed-close"));
        h.Until(() => h.TryFind("handed-sheet") is null);
    }, ProviderKind.Claude);

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
    }, ProviderKind.Claude);

    private static AccountsRig Two()
    {
        var rig = new AccountsRig();
        rig.Connect(ProviderKind.Claude, "Max");
        rig.ConnectAnother(ProviderKind.Claude, "Max");
        return rig;
    }
}
