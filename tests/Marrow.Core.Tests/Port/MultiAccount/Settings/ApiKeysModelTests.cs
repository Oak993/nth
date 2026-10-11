using Marrow.Core.Accounts.Keys;
using Marrow.Core.Models;
using Marrow.Core.Port.Accounts;
using Marrow.Core.Port.MultiAccount.Settings;
using Marrow.Testing;
using Marrow.Testing.MultiAccountSettings;

namespace Marrow.Core.Tests.Port.MultiAccount.Settings;

// OD-0139 (ED-NEW-providersmerge-1): several API keys of one service on Settings > Providers. Each key is one of the service's accounts (the multi-account store); its secret is kept by the app (the fake vault here,
// the Credential Manager there); the engine's ApiKeyPool reads them in the store's order and falls back down the list.
[Trait("Decision", "ED-NEW-providersmerge-1")]
[Trait("Rule", "R-TEST-4")]
public sealed class ApiKeysModelTests
{
    private const string First = "zai-first-key-aaaa";
    private const string Second = "zai-second-key-bbbb";

    /// <summary>Z.ai with its first key typed in the card's "API key" box (the original account).</summary>
    private static (AccountsRig Rig, Account Original) OneKey()
    {
        var rig = new AccountsRig();
        var original = rig.Accounts.ForService(ProviderKind.Zai)[0];
        rig.Actions.Vault[original.Id] = First;
        return (rig, original);
    }

    [Fact]
    public async Task Adding_a_second_Zai_key_makes_a_second_row_after_the_first_and_both_persist_in_the_account_store()
    {
        var (rig, original) = OneKey();
        var block = rig.Model.BlockFor(ProviderKind.Zai)!;
        Assert.Empty(block.Rows); // one key looks as the card did: the "API key" box, no list
        Assert.True(block.CanAddKey);
        Assert.True(block.Keys);
        Assert.Null(await rig.Model.AddKeyAsync(ProviderKind.Zai, "  " + Second + "\n"));
        block = rig.Model.BlockFor(ProviderKind.Zai)!;
        Assert.Equal(2, block.Rows.Count);
        Assert.Equal(MultiAccountWords.Keys(2, null), block.Summary);
        Assert.Equal(original.Id, block.Rows[0].Id);
        Assert.Equal([1, 2], block.Rows.Select(r => r.Number));
        Assert.Equal(["Z.ai 1", "Z.ai 2"], block.Rows.Select(r => r.Name));
        Assert.Equal([MultiAccountWords.KeyMasked("aaaa"), MultiAccountWords.KeyMasked("bbbb")], block.Rows.Select(r => r.Subtitle));
        Assert.All(block.Rows, r => Assert.True(r.IsKey));
        var added = block.Rows[1].Id;
        Assert.Equal(Second, rig.Actions.Vault[added]); // trimmed, and kept by the app
        Assert.Equal(Second, rig.Model.RevealKey(added));
        var reread = new AccountStore(rig.Settings).ForService(ProviderKind.Zai); // a fresh read of the saved settings
        Assert.Equal([original.Id, added], reread.Select(a => a.Id));
        Assert.True(reread[0].UsesLegacyCredentials);
        Assert.False(reread[1].UsesLegacyCredentials);
    }

    [Fact]
    public async Task An_empty_or_repeated_key_or_one_the_app_could_not_keep_says_why_and_leaves_no_account()
    {
        var (rig, _) = OneKey();
        var before = rig.Accounts.ForService(ProviderKind.Zai).Select(a => a.Id).ToArray();
        Assert.Equal(MultiAccountWords.KeyEmpty, await rig.Model.AddKeyAsync(ProviderKind.Zai, "   "));
        Assert.Equal(MultiAccountWords.KeyTwice, await rig.Model.AddKeyAsync(ProviderKind.Zai, First));
        rig.Actions.StoreFails = "Credential Manager did not accept this key, so it was not saved.";
        Assert.Equal(rig.Actions.StoreFails, await rig.Model.AddKeyAsync(ProviderKind.Zai, Second));
        Assert.Equal(before, rig.Accounts.ForService(ProviderKind.Zai).Select(a => a.Id));
        Assert.Equal(before, new AccountStore(rig.Settings).ForService(ProviderKind.Zai).Select(a => a.Id));
        Assert.Single(rig.Actions.Vault);
    }

    [Fact]
    public async Task Reordering_the_keys_changes_the_key_the_engine_sends_on_first_and_it_falls_back_down_the_list()
    {
        var (rig, original) = OneKey();
        Assert.Null(await rig.Model.AddKeyAsync(ProviderKind.Zai, Second));
        var added = rig.Model.BlockFor(ProviderKind.Zai)!.Rows[1].Id;
        var keys = new AccountKeyStore(rig.Accounts, rig.Settings, id => rig.Actions.KeySecret(id));
        var pool = new ApiKeyPool(ProviderKind.Zai.Id, keys, new InMemoryApiKeyCredits(), keys, new ManualTimeProvider(), moves: new InMemoryKeyMovePolicy { Default = KeyMoveSetting.Allow });
        Assert.Equal(original.Id.ToString("D"), pool.ForThread(Guid.NewGuid()).Current()!.Id);
        Assert.Equal(original.Id, rig.Accounts.Choose(ProviderKind.Zai)!.Id);

        rig.Model.MoveTo(added, 0); // the drag, or Move to top
        Assert.Equal([added, original.Id], rig.Model.BlockFor(ProviderKind.Zai)!.Rows.Select(r => r.Id));
        Assert.Equal(added, rig.Accounts.Choose(ProviderKind.Zai)!.Id);
        Assert.Equal([added, original.Id], new AccountStore(rig.Settings).ForService(ProviderKind.Zai).Select(a => a.Id));
        var request = pool.ForThread(Guid.NewGuid());
        Assert.Equal(added.ToString("D"), request.Current()!.Id);
        Assert.Equal(original.Id.ToString("D"), request.OnFailure(429, null)!.Id); // rate limited: the next key down takes the same request

        rig.Model.SetEnabled(added, false); // a key switched off is skipped
        Assert.Equal(original.Id.ToString("D"), pool.ForThread(Guid.NewGuid()).Current()!.Id);
    }

    [Fact]
    public async Task Removing_a_further_key_drops_its_row_its_account_and_its_secret_while_removing_the_first_keeps_its_account_and_lists_the_one_left()
    {
        var (rig, original) = OneKey();
        Assert.Null(await rig.Model.AddKeyAsync(ProviderKind.Zai, Second));
        Assert.Null(await rig.Model.AddKeyAsync(ProviderKind.Zai, "zai-third-key-cccc"));
        var rows = rig.Model.BlockFor(ProviderKind.Zai)!.Rows;
        Assert.Equal(3, rows.Count);
        await rig.Model.RemoveKeyAsync(rows[1].Id);
        Assert.Equal([original.Id, rows[2].Id], rig.Model.BlockFor(ProviderKind.Zai)!.Rows.Select(r => r.Id));
        Assert.Equal([original.Id, rows[2].Id], new AccountStore(rig.Settings).ForService(ProviderKind.Zai).Select(a => a.Id));
        Assert.False(rig.Actions.Vault.ContainsKey(rows[1].Id));

        await rig.Model.RemoveKeyAsync(original.Id); // the first key: its box empties, its account stays for the next key typed there
        Assert.Equal([rows[1].Id, original.Id], rig.Actions.Forgotten);
        Assert.Contains(rig.Accounts.ForService(ProviderKind.Zai), a => a.Id == original.Id);
        var left = Assert.Single(rig.Model.BlockFor(ProviderKind.Zai)!.Rows); // a lone further key stays in sight, with its own eye and Remove
        Assert.Equal(rows[2].Id, left.Id);
    }

    [Fact]
    public void A_keys_menu_moves_and_removes_it_and_has_no_Handshake_or_sign_out()
    {
        var (rig, original) = OneKey();
        var menu = rig.Model.Menu(original.Id);
        Assert.Equal(["rename", "up", "top", "down", "remove"], menu.Where(e => !e.Separator).Select(e => e.Key));
        Assert.Equal(MultiAccountWords.RemoveKey, menu.Single(e => e.Key == "remove").Label);
        Assert.DoesNotContain(menu, e => e.Key is "handshake" or "signout");
    }

    [Fact]
    public void A_service_that_signs_in_takes_no_key_and_a_key_service_takes_no_account()
    {
        Assert.False(AccountsSettingsModel.CanAddKey(ProviderKind.Claude));
        Assert.False(AccountsSettingsModel.CanAddKey(ProviderKind.Commandcode)); // its key box is the source's optional one
        Assert.True(AccountsSettingsModel.CanAddKey(ProviderKind.OpenRouter));
        Assert.False(AccountsSettingsModel.CanAdd(ProviderKind.Zai));
        Assert.True(AccountsSettingsModel.CanAdd(ProviderKind.Codex));
    }

    [Fact]
    public async Task Credits_a_key_reports_are_its_usage_line()
    {
        var (rig, original) = OneKey();
        Assert.Null(await rig.Model.AddKeyAsync(ProviderKind.Zai, Second));
        rig.Credits[original.Id] = 4.2;
        var rows = rig.Model.BlockFor(ProviderKind.Zai)!.Rows;
        Assert.Equal(MultiAccountWords.KeyCredits(4.2), rows[0].UsageText);
        Assert.Equal("$4.20 of credits left", rows[0].UsageText);
        Assert.Equal("", rows[1].UsageText);
    }
}
