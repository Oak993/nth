using Marrow.App;
using Marrow.App.Assistant;
using Marrow.Core.Models;
using Marrow.Core.Port.Accounts;
using Marrow.Core.Port.Assistant;

namespace Marrow.App.Tests.Assistant;

[Trait("Decision", "ED-NEW-dextra-open-3")]
public sealed class AssistantTextBindingTests
{
    [Theory]
    [InlineData("", false, "fake-default")]
    [InlineData(" ", true, "fake-default")]
    [InlineData("chosen model with spaces", true, "chosen model with spaces")]
    public async Task A_default_brain_model_reaches_the_provider_default_without_losing_a_named_account(string model, bool named, string expected)
    {
        var account = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var chosen = new BrainModel("codex", model, "codex:" + (named ? account.ToString("D") : "default"), "Fake account");
        var legacy = Guid.Parse("bbbbbbbb-cccc-dddd-eeee-ffffffffffff");
        Account[] accounts = [
            new() { Id = account, Service = ProviderKind.Codex },
            new() { Id = legacy, Service = ProviderKind.Codex, UsesLegacyCredentials = true },
        ];
        Guid? usedAccount = null;
        var call = AppModel.PrepareSuggestionText(chosen, (who, onAccount) =>
        {
            Assert.Equal(ProviderKind.Codex, who.Provider);
            usedAccount = onAccount;
            // Recorded transport's default resolution matches the existing production text factory's null-model contract.
            var selected = who.Model ?? "fake-default";
            return new DelegateCall(ProviderKind.Codex, (_, _) => Task.FromResult<string?>("used:" + selected));
        }, accounts);
        Assert.NotNull(call);
        Assert.Equal(named ? account : legacy, usedAccount);
        Assert.Equal("used:" + expected, await call.Ask("fake", CancellationToken.None));
    }

    [Theory]
    [InlineData("claude:default")]
    [InlineData("codex:not-a-guid")]
    public void An_unknown_account_key_cannot_fall_back_to_a_different_account(string key)
    {
        var called = false;
        var chosen = new BrainModel("codex", "chosen", key, "Fake account");
        Assert.Null(AppModel.PrepareSuggestionText(chosen, (_, _) => { called = true; return null; }, []));
        Assert.False(called);
    }

    [Fact]
    public void A_missing_legacy_account_cannot_use_the_first_named_account()
    {
        var named = new Account { Id = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"), Service = ProviderKind.Codex };
        var called = false;
        var chosen = new BrainModel("codex", "", "codex:default", "Fake legacy account");
        Assert.Null(AppModel.PrepareSuggestionText(chosen, (_, _) => { called = true; return null; }, [named]));
        Assert.False(called);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void An_unusable_brain_pick_cannot_reinterpret_a_missing_pin_as_a_legacy_account(bool named)
    {
        var id = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        Account[] accounts = [new() { Id = id, Service = ProviderKind.Codex, UsesLegacyCredentials = !named }];
        var chosen = new BrainModel("codex", "", "codex:" + (named ? id.ToString("D") : "default"), "Unusable fake brain") { Usable = false };
        var called = false;
        Assert.Null(AppModel.PrepareSuggestionText(chosen, (_, _) => { called = true; return null; }, accounts));
        Assert.False(called);
    }
}
