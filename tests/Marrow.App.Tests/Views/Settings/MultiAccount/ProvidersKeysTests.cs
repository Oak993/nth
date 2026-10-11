using Avalonia.Controls;
using Avalonia.Input;
using Marrow.App.Port.MultiAccount.Settings;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port.Accounts;
using Marrow.Core.Port.MultiAccount.Settings;
using Marrow.Testing.MultiAccountSettings;

namespace Marrow.App.Tests.Views.Settings.MultiAccount;

// OD-0139 (ED-NEW-providersmerge-1), the owner's "I can't even add a z.ai key?": an API-key service's card on Settings > Providers takes further keys. "Add another key" folds a box open; a kept key is the next row of
// "Keys and order" (masked, its eye, its switch, ⋯ with Remove this key), saved in the account store; the order is the order the engine sends on (drag, Alt+arrows, the menu).
[Collection("Headless")]
public sealed class ProvidersKeysTests(HeadlessSession headless)
{
    private const string First = "zai-first-key-aaaa";
    private const string Second = "zai-second-key-bbbb";
    private static readonly string Zai = ProviderKind.Zai.Id;

    /// <summary>Z.ai with its first key (the card's "API key" box) and, when <paramref name="two"/>, a second key already added.</summary>
    private void Run(Action<MultiAccountHarness, ServiceBlockControl, Account> check, bool two = false)
    {
        var rig = new AccountsRig();
        var original = rig.Accounts.ForService(ProviderKind.Zai)[0];
        rig.Actions.Vault[original.Id] = First;
        if (two) rig.Actions.Vault[rig.Accounts.Add(ProviderKind.Zai).Id] = Second;
        MultiAccountHarness.Run(headless, rig, () => new ProvidersAccountsPreview(rig.Model, ProviderKind.Zai), h => check(h, ((ProvidersAccountsPreview)h.Page).Block(ProviderKind.Zai), original));
    }

    [Fact]
    [Trait("Decision", "ED-NEW-providersmerge-1")]
    public void Add_another_key_folds_a_masked_box_open_and_a_kept_key_becomes_the_second_row_saved_in_the_store() => Run((h, block, original) =>
    {
        Assert.False(block.ListOpen); // one key: the card's "API key" box, no list
        Assert.True(block.AddOpen);
        Assert.Contains(MultiAccountWords.AddKey, h.Words());
        Assert.False(block.KeyBox!.Open);
        Assert.True(h.Press("service-add-key-" + Zai));
        Assert.True(block.KeyBox.Open);
        var input = h.Find<TextBox>("key-input-" + Zai);
        Assert.Equal('•', input.PasswordChar);
        Assert.Equal(MultiAccountWords.KeyPlaceholder, input.PlaceholderText);
        Assert.Contains(MultiAccountWords.KeyNote, h.Words());
        Assert.True(h.Press("key-input-eye-" + Zai));
        Assert.Equal('\0', input.PasswordChar);
        Assert.True(h.Press("key-input-eye-" + Zai));
        Assert.Equal('•', input.PasswordChar);
        input.Text = Second;
        Assert.True(h.Press("key-save-" + Zai));
        h.Until(() => block.Rows.Count == 2);
        Assert.True(block.ListOpen);
        Assert.False(block.KeyBox.Open);
        Assert.Equal("", input.Text);
        var added = h.Rig.Accounts.ForService(ProviderKind.Zai)[1];
        Assert.Equal(Second, h.Rig.Actions.Vault[added.Id]);
        Assert.Equal([original.Id, added.Id], new AccountStore(h.Rig.Settings).ForService(ProviderKind.Zai).Select(a => a.Id));
        Assert.Equal([original.Id, added.Id], block.Rows.Values.OrderBy(r => r.View.Number).Select(r => r.Id));
        var words = h.Words();
        foreach (var expected in new[] { MultiAccountWords.KeysAndOrder, MultiAccountWords.Keys(2, null), MultiAccountWords.KeyMasked("aaaa"), MultiAccountWords.KeyMasked("bbbb"), MultiAccountWords.KeysFoot })
            Assert.Contains(expected, words);
        Assert.DoesNotContain(Second, words); // masked until its eye shows it
    });

    [Fact]
    [Trait("Decision", "ED-NEW-providersmerge-1")]
    public void Return_adds_too_and_an_empty_or_repeated_key_says_why_and_keeps_the_box_open() => Run((h, block, _) =>
    {
        Assert.True(h.Press("service-add-key-" + Zai));
        var input = h.Find<TextBox>("key-input-" + Zai);
        input.Text = "   ";
        input.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
        h.Until(() => block.KeyBox!.StatusShown == MultiAccountWords.KeyEmpty);
        Assert.Contains(MultiAccountWords.KeyEmpty, h.Words());
        input.Text = First;
        input.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
        h.Until(() => block.KeyBox!.StatusShown == MultiAccountWords.KeyTwice);
        Assert.True(block.KeyBox!.Open);
        Assert.Single(h.Rig.Accounts.ForService(ProviderKind.Zai));
        input.Text = Second;
        input.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
        h.Until(() => block.Rows.Count == 2);
        Assert.Equal("", block.KeyBox.StatusShown);
    });

    [Fact]
    [Trait("Decision", "ED-NEW-providersmerge-1")]
    public void A_keys_eye_shows_the_whole_key_and_hides_it_again() => Run((h, block, original) =>
    {
        var row = block.Rows[original.Id];
        Assert.Equal(MultiAccountWords.KeyMasked("aaaa"), row.SubtitleShown);
        Assert.NotNull(row.Eye);
        Assert.True(h.Press("key-eye-" + original.Id.ToString("N")));
        Assert.True(row.Revealed);
        Assert.Equal(First, row.SubtitleShown);
        Assert.Contains(First, h.Words());
        Assert.Equal(MultiAccountWords.HideKey, Avalonia.Automation.AutomationProperties.GetName(row.Eye!));
        Assert.True(h.Press("key-eye-" + original.Id.ToString("N")));
        Assert.Equal(MultiAccountWords.KeyMasked("aaaa"), row.SubtitleShown);
        Assert.DoesNotContain(First, h.Words());
    }, two: true);

    [Fact]
    [Trait("Decision", "ED-NEW-providersmerge-1")]
    public void Reordering_the_keys_changes_which_key_is_used_first() => Run((h, block, original) =>
    {
        var second = h.Rig.Accounts.ForService(ProviderKind.Zai)[1].Id;
        Assert.Equal(original.Id, h.Rig.Accounts.Choose(ProviderKind.Zai)!.Id);
        Assert.Equal(2, h.Find(id => id.StartsWith("account-grip-", StringComparison.Ordinal)).Count()); // drag to reorder
        block.Rows[second].RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Up, KeyModifiers = KeyModifiers.Alt });
        h.Settle();
        Assert.Equal(second, h.Rig.Accounts.Choose(ProviderKind.Zai)!.Id);
        Assert.Equal([second, original.Id], new AccountStore(h.Rig.Settings).ForService(ProviderKind.Zai).Select(a => a.Id));
        Assert.Equal([second, original.Id], block.Rows.Values.OrderBy(r => r.View.Number).Select(r => r.Id));
        block.Rows[original.Id].Pick("top");
        h.Settle();
        Assert.Equal(original.Id, h.Rig.Accounts.Choose(ProviderKind.Zai)!.Id);
    }, two: true);

    [Fact]
    [Trait("Decision", "ED-NEW-providersmerge-1")]
    public void Remove_this_key_takes_its_row_account_and_secret_away_and_one_key_folds_the_list_shut() => Run((h, block, original) =>
    {
        var second = h.Rig.Accounts.ForService(ProviderKind.Zai)[1].Id;
        Assert.Contains(h.Rig.Model.Menu(second), e => e.Key == "remove" && e.Label == MultiAccountWords.RemoveKey);
        block.Rows[second].Pick("remove");
        h.Until(() => block.Rows.Count == 0);
        Assert.False(block.ListOpen);
        Assert.Equal([second], h.Rig.Actions.Forgotten);
        Assert.False(h.Rig.Actions.Vault.ContainsKey(second));
        Assert.Equal([original.Id], new AccountStore(h.Rig.Settings).ForService(ProviderKind.Zai).Select(a => a.Id));
        Assert.True(block.AddOpen); // the add row stays: one add action for the service
        Assert.DoesNotContain(MultiAccountWords.KeyMasked("bbbb"), h.Words());
    }, two: true);
}
