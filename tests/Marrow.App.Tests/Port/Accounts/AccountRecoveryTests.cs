using System.Text;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port;
using Marrow.Core.Port.Accounts;

namespace Marrow.App.Tests.Port.Accounts;

[Collection("AppSettings")]
[Trait("Rule", "R-TEST-4")]
public sealed class AccountRecoveryTests
{
    [Fact]
    public void Unreadable_accounts_keep_legacy_credentials_read_only_without_overwriting_the_users_document()
    {
        using var scope = AppSettingsScope.Create();
        scope.OnMain(() =>
        {
            var saved = Encoding.UTF8.GetBytes("{ unsupported document");
            UserDefaults.Standard.Current.Set(AccountStore.SettingsKey, saved);
            var settings = new AppSettings();
            Assert.True(settings.Accounts.SaveBlocked);
            Assert.All(settings.Accounts.Accounts, account => Assert.True(account.UsesLegacyCredentials));
            Assert.Throws<InvalidOperationException>(() => settings.Accounts.Rename(settings.Accounts.Accounts[0].Id, "Changed"));
            Assert.Equal(saved, UserDefaults.Standard.Current.Object(AccountStore.SettingsKey));
            var startup = Record.Exception(() => new Marrow.Services.Providers.ProviderRegistry(settings));
            Assert.True(startup is null, startup?.ToString().Replace("\n", " | ", StringComparison.Ordinal));
        });
    }
}
