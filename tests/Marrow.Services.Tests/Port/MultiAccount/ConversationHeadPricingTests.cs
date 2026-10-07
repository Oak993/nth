using Marrow.Core.Accounts.Keys;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Accounts;
using Marrow.Core.Port.MultiAccount;
using Marrow.Services.Port.MultiAccount;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;
using Marrow.Testing.MultiAccountSettings;

namespace Marrow.Services.Tests.Port.MultiAccount;

[Trait("Feature", "MA-10")]
[Trait("Rule", "R-TEST-4")]
public sealed class ConversationHeadPricingTests
{
    [Theory]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void A_capped_head_without_reported_USD_requires_authoritative_pricing_before_any_send(bool capped, bool priced)
    {
        using var runtime = new TestRuntime();
        using var folder = new AccountTestFolder();
        var accounts = new AccountStore(runtime.Defaults);
        var id = accounts.ForService(ProviderKind.Deepseek)[0].Id;
        var settings = new FakeProviderSettings(); settings.Keys[ProviderKind.Deepseek] = "offline-key";
        var registry = new ProviderRegistry(settings, accounts, folder.Path);
        var target = new ConversationHeadAccount(id, ProviderKind.Deepseek, "DeepSeek", true, true) { MonthlyCapUsd = capped ? 1 : null };
        var store = new InMemoryApiKeyStore().Add(new(id.ToString("D"), "deepseek", "Opinion key", MonthlyCapUsd: target.MonthlyCapUsd), "fake-key");
        var spend = new InMemoryApiKeySpend();
        var pool = new ApiKeyPool("deepseek", store, new InMemoryApiKeyCredits(), spend, new ManualTimeProvider());
        var config = new SessionConfiguration
        {
            Provider = target.Service, WorkingDirectory = SwiftURL.Create("marrow-native-path:" + folder.Path.Replace('\\', '/'))!,
            Environment = ValueMap<string, string>.Empty, RuntimeMode = RuntimeMode.Supervised, InteractionMode = InteractionMode.Build,
        };
        OfflineOpinionApiSession? session = null;
        using var factory = AccountSessionFactory.Constructor.Override(prepared => session = new(prepared, reportUsd: false));
        var host = new AccountConversationHeadHost(registry, _ => id, _ => 1, _ => [target], (_, _) => Task.FromResult("context"),
            (_, _) => config, new ConversationTransferConsent(runtime.Defaults, new TransferPromptFake()), (_, _, _, _, _) => Task.CompletedTask,
            bindKeys: (provider, account) =>
            {
                var privateId = Guid.NewGuid(); pool.Pin(privateId, account.ToString("D"));
                var api = (OpenAICompatibleSession)provider;
                api.KeyRotation = pool.ForThread(privateId, account.ToString("D"));
                if (priced) api.KeySpendEstimator = _ => 0.25;
            });
        runtime.Loop.Run(async () =>
        {
            Task<string> Run() => host.RunAsync(new(Guid.Empty, target, ConversationHeadPurpose.Opinion, "context", "Q"), _ => { }, () => true, CancellationToken.None);
            if (capped && !priced)
            {
                await Assert.ThrowsAsync<InvalidOperationException>(Run);
                Assert.NotNull(session); Assert.Empty(session.Payloads);
                Assert.Equal(0, spend.SpendSince(id.ToString("D"), DateTimeOffset.MinValue));
            }
            else
            {
                Assert.Equal("Opinion text", await Run());
                Assert.Equal(priced ? 0.25 : 0, spend.SpendSince(id.ToString("D"), DateTimeOffset.MinValue));
            }
        });
    }
}
