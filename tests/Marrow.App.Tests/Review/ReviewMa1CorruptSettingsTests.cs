using System.Text;
using Marrow.Core.Port;
using Marrow.Core.Port.Accounts;
using Marrow.Services.Providers;
using Marrow.App.Tests.Fixtures;

namespace Marrow.App.Tests.Review;

// Independent review of codex/ma-1 (porting/reports/review-ma-1-2-4.md, finding MA1-1): "migration keeps today's single sign-in untouched" must also hold when the account document
// is unreadable or from a newer app. The store keeps the original value (good) but throws, and AppModel builds the ProviderRegistry from Settings.Accounts, so the app cannot start.
[Collection("AppSettings")]
public sealed class ReviewMa1CorruptSettingsTests
{
    [Theory]
    [InlineData("{ not json")]
    [InlineData("{\"Version\":2,\"Accounts\":[]}")]
    [Trait("Feature", "MA-1")]
    [Trait("Review", "MA-1")]
    [Trait("Rule", "R-TEST-4")]
    public void REVIEW_unreadable_or_newer_account_settings_must_not_stop_the_provider_registry_from_starting(string stored)
    {
        using var scope = AppSettingsScope.Create();
        scope.OnMain(() =>
        {
            UserDefaults.Standard.Current.Set(AccountStore.SettingsKey, Encoding.UTF8.GetBytes(stored));
            var settings = new AppSettings();
            var error = Record.Exception(() => new ProviderRegistry(settings));
            Assert.Null(error);
            // And the stored value is still the user's original.
            Assert.Equal(stored, Encoding.UTF8.GetString((byte[])UserDefaults.Standard.Current.Object(AccountStore.SettingsKey)!));
        });
    }
}
