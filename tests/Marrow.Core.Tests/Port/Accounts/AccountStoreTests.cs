using System.Text;
using Marrow.Core.Models;
using Marrow.Core.Port.Accounts;
using Marrow.Testing;

namespace Marrow.Core.Tests.Port.Accounts;

[Trait("Feature", "MA-1")]
[Trait("Rule", "R-SET-1")]
public sealed class AccountStoreTests
{
    [Fact]
    public void Migration_creates_one_stable_legacy_account_per_service_without_changing_old_settings()
    {
        var settings = new InMemorySettingsStore();
        settings.Set("lastModels", "old bytes");
        settings.Writes.Clear();
        var store = new AccountStore(settings, p => new AccountDefaults { Model = p.Id, Effort = "high", FastMode = true });
        foreach (var service in Enum.GetValues<ProviderKind>())
        {
            var account = Assert.Single(store.ForService(service));
            Assert.True(account.UsesLegacyCredentials);
            Assert.True(account.Enabled);
            Assert.Equal(0, account.Priority);
            Assert.Equal(service.Id, account.Defaults.Model);
            Assert.Equal("high", account.Defaults.Effort);
            Assert.True(account.Defaults.FastMode);
            Assert.Null(store.DisplayName(account.Id));
        }
        Assert.DoesNotContain(store.Accounts, a => a.Id == Guid.Empty);
        Assert.Equal(store.Accounts.Count, store.Accounts.Select(a => a.Id).Distinct().Count());
        Assert.Equal("old bytes", settings.Object("lastModels"));
        Assert.Equal([AccountStore.SettingsKey], settings.Writes);
        settings.Writes.Clear();
        var reopened = new AccountStore(settings);
        Assert.Equal(store.Accounts, reopened.Accounts);
        Assert.Empty(settings.Writes);
    }

    [Fact]
    public void Plan_names_number_equal_plans_and_hide_names_without_losing_custom_names()
    {
        var store = new AccountStore(new InMemorySettingsStore());
        var first = store.ForService(ProviderKind.Claude)[0];
        store.SetPlan(first.Id, "Max");
        var second = store.Add(ProviderKind.Claude, "Max");
        var pro = store.Add(ProviderKind.Claude, "Pro");
        Assert.Equal("Claude Max 1", store.DisplayName(first.Id));
        Assert.Equal("Claude Max 2", store.DisplayName(second.Id));
        Assert.Equal("Claude Pro", store.DisplayName(pro.Id));
        store.Rename(first.Id, "Work");
        store.Remove(second.Id);
        store.Remove(pro.Id);
        Assert.Null(store.DisplayName(first.Id));
        Assert.Equal("Work", store.Get(first.Id).CustomName);
        store.Add(ProviderKind.Claude, "Pro");
        Assert.Equal("Work", store.DisplayName(first.Id));
    }


    [Fact]
    public void Distinct_plans_are_unnumbered_and_disabled_accounts_still_keep_names_visible()
    {
        var store = new AccountStore(new InMemorySettingsStore());
        var first = store.ForService(ProviderKind.Claude)[0];
        store.SetPlan(first.Id, "Max");
        var second = store.Add(ProviderKind.Claude, "Max");
        var pro = store.Add(ProviderKind.Claude, "Pro");
        store.Remove(second.Id);
        store.SetEnabled(pro.Id, false);
        Assert.Equal("Claude Max", store.DisplayName(first.Id));
        Assert.Equal("Claude Pro", store.DisplayName(pro.Id));
        Assert.Equal(new[] { 0, 1 }, store.ForService(ProviderKind.Claude).Select(a => a.Priority));
    }

    [Fact]
    public void Codex_numbering_stays_bound_to_identity_when_priority_changes()
    {
        var store = new AccountStore(new InMemorySettingsStore());
        var first = store.ForService(ProviderKind.Codex)[0];
        var second = store.Add(ProviderKind.Codex);
        store.Reorder(ProviderKind.Codex, [second.Id, first.Id]);
        Assert.Equal("Codex 1", store.DisplayName(first.Id));
        Assert.Equal("Codex 2", store.DisplayName(second.Id));
        Assert.Equal(new[] { second.Id, first.Id }, store.ForService(ProviderKind.Codex).Select(a => a.Id));
        Assert.Equal(new[] { 0, 1 }, store.ForService(ProviderKind.Codex).Select(a => a.Priority));
        Assert.Equal(second.Id, store.Choose(ProviderKind.Codex)!.Id);
    }

    [Fact]
    public void Disabled_accounts_are_skipped_even_when_pinned_and_all_disabled_returns_none()
    {
        var store = new AccountStore(new InMemorySettingsStore());
        var first = store.ForService(ProviderKind.Codex)[0];
        var second = store.Add(ProviderKind.Codex);
        store.SetEnabled(first.Id, false);
        Assert.Equal(second.Id, store.Choose(ProviderKind.Codex)!.Id);
        Assert.Null(store.Choose(ProviderKind.Codex, first.Id));
        Assert.Null(store.Choose(ProviderKind.Claude, second.Id));
        store.SetEnabled(second.Id, false);
        Assert.Null(store.Choose(ProviderKind.Codex));
        store.SetEnabled(first.Id, true);
        Assert.Equal(first.Id, store.Choose(ProviderKind.Codex)!.Id);
    }

    [Theory]
    [InlineData(ProviderKind.Copilot)]
    [InlineData(ProviderKind.Antigravity)]
    [InlineData(ProviderKind.Commandcode)]
    public void Single_account_services_reject_a_second_identity(ProviderKind service)
    {
        var store = new AccountStore(new InMemorySettingsStore());
        Assert.False(AccountCapabilities.SupportsMultipleAccounts(service));
        Assert.Throws<InvalidOperationException>(() => store.Add(service));
    }

    [Fact]
    public void Defaults_priority_enabled_plan_and_custom_name_roundtrip_independently_per_service()
    {
        var settings = new InMemorySettingsStore();
        var store = new AccountStore(settings);
        var first = store.ForService(ProviderKind.Claude)[0];
        var second = store.Add(ProviderKind.Claude, "Max");
        store.Rename(second.Id, "Research");
        store.SetDefaults(second.Id, new AccountDefaults { Model = "opus", Effort = "max", FastMode = true, HandshakePoint = new(99) });
        store.SetEnabled(first.Id, false);
        store.Reorder(ProviderKind.Claude, [second.Id, first.Id]);
        store.GeneralHandshakePoint = new(80);
        var reopened = new AccountStore(settings);
        Assert.Equal(store.Accounts, reopened.Accounts);
        Assert.Equal(new AccountDefaults(), reopened.Get(first.Id).Defaults);
        Assert.Equal(new AccountDefaults(), reopened.ForService(ProviderKind.Codex)[0].Defaults);
        Assert.Equal(new HandshakePoint(99), reopened.EffectiveHandshakePoint(second.Id));
        Assert.Equal(new HandshakePoint(80), reopened.EffectiveHandshakePoint(first.Id));
        Assert.Equal(ProviderKind.Codex, reopened.Choose(ProviderKind.Codex)!.Service);
        Assert.DoesNotContain("API", Encoding.UTF8.GetString((byte[])settings.Object(AccountStore.SettingsKey)!), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(79)]
    [InlineData(100)]
    public void Handshake_rejects_points_outside_80_to_99(int point) => Assert.Throws<ArgumentOutOfRangeException>(() => new HandshakePoint(point));

    [Fact]
    public void Smart_is_the_general_default_and_an_explicit_account_smart_overrides_a_general_percentage()
    {
        var store = new AccountStore(new InMemorySettingsStore());
        var account = store.ForService(ProviderKind.Claude)[0];
        Assert.Equal(HandshakePoint.Smart, store.EffectiveHandshakePoint(account.Id));
        store.GeneralHandshakePoint = new(95);
        store.SetDefaults(account.Id, new AccountDefaults { HandshakePoint = HandshakePoint.Smart });
        Assert.Equal(HandshakePoint.Smart, store.EffectiveHandshakePoint(account.Id));
    }

    [Fact]
    public void Custom_names_accept_24_characters_including_emoji_and_reject_25_without_saving()
    {
        var settings = new InMemorySettingsStore();
        var store = new AccountStore(settings);
        var account = store.ForService(ProviderKind.Claude)[0];
        var name = string.Concat(Enumerable.Repeat("👩‍💻", 24));
        store.Rename(account.Id, name);
        settings.Writes.Clear();
        Assert.Throws<ArgumentOutOfRangeException>(() => store.Rename(account.Id, name + "a"));
        Assert.Equal(name, store.Get(account.Id).CustomName);
        Assert.Empty(settings.Writes);
    }

    [Fact]
    public void Service_identity_is_saved_by_stable_wire_name()
    {
        var settings = new InMemorySettingsStore();
        _ = new AccountStore(settings);
        Assert.Contains("\"Service\":\"claude\"", Encoding.UTF8.GetString((byte[])settings.Object(AccountStore.SettingsKey)!), StringComparison.Ordinal);
        Assert.Contains("\"Service\":\"openrouter\"", Encoding.UTF8.GetString((byte[])settings.Object(AccountStore.SettingsKey)!), StringComparison.Ordinal);
    }

    [Fact]
    public void Invalid_reorder_never_changes_any_service_or_saved_data()
    {
        var settings = new InMemorySettingsStore();
        var store = new AccountStore(settings);
        var codex = store.ForService(ProviderKind.Codex)[0];
        var claude = store.ForService(ProviderKind.Claude)[0];
        var before = store.Accounts.ToArray();
        settings.Writes.Clear();
        Assert.Throws<ArgumentException>(() => store.Reorder(ProviderKind.Codex, [claude.Id]));
        Assert.Throws<ArgumentException>(() => store.Reorder(ProviderKind.Codex, [codex.Id, codex.Id]));
        Assert.Equal(before, store.Accounts);
        Assert.Empty(settings.Writes);
    }

    [Theory]
    [InlineData("broken")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("{\"Accounts\":[]}")]
    [InlineData("{\"Version\":\"1\",\"Accounts\":[]}")]
    [InlineData("{\"Version\":999,\"Accounts\":[]}")]
    public void Unreadable_or_future_account_settings_are_preserved_and_never_remigrated(string json)
    {
        var settings = new InMemorySettingsStore();
        var data = Encoding.UTF8.GetBytes(json);
        settings.Set(AccountStore.SettingsKey, data);
        settings.Writes.Clear();
        Assert.Throws<InvalidDataException>(() => new AccountStore(settings));
        Assert.Same(data, settings.Object(AccountStore.SettingsKey));
        Assert.Empty(settings.Writes);
    }
}
