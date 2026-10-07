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
public sealed class ConversationHeadKeyBindingTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_private_head_charges_its_own_key_and_stops_at_the_shared_cap_even_if_the_account_snapshot_is_stale(bool binding)
    {
        using var runtime = new TestRuntime();
        using var folder = new AccountTestFolder();
        var accounts = new AccountStore(runtime.Defaults);
        var id = accounts.ForService(ProviderKind.Deepseek)[0].Id;
        var settings = new FakeProviderSettings(); settings.Keys[ProviderKind.Deepseek] = "offline-key";
        var registry = new ProviderRegistry(settings, accounts, folder.Path);
        var key = id.ToString("D");
        var store = new InMemoryApiKeyStore().Add(new(key, "deepseek", "Opinion key", MonthlyCapUsd: 1), "fake-opinion-key")
            .Add(new(Guid.NewGuid().ToString("D"), "deepseek", "Other key"), "fake-other-key");
        var spend = new InMemoryApiKeySpend();
        var pool = new ApiKeyPool("deepseek", store, new InMemoryApiKeyCredits(), spend, new ManualTimeProvider());
        var target = new ConversationHeadAccount(id, ProviderKind.Deepseek, "DeepSeek", true, true);
        var config = new SessionConfiguration
        {
            Provider = target.Service, WorkingDirectory = SwiftURL.Create("marrow-native-path:" + folder.Path.Replace('\\', '/'))!,
            Environment = ValueMap<string, string>.Empty, RuntimeMode = RuntimeMode.Supervised, InteractionMode = InteractionMode.Build,
        };
        var sessions = new List<OfflineOpinionApiSession>();
        using var factory = AccountSessionFactory.Constructor.Override(prepared => { var session = new OfflineOpinionApiSession(prepared); sessions.Add(session); return session; });
        var consent = new ConversationTransferConsent(runtime.Defaults, new TransferPromptFake());
        var host = new AccountConversationHeadHost(registry, _ => id, _ => 1, _ => [target], (_, _) => Task.FromResult("context"),
            (_, _) => config, consent, (_, _, _, _, _) => Task.CompletedTask,
            bindKeys: binding ? (session, account) =>
            {
                Assert.Equal(id, account);
                var privateId = Guid.NewGuid(); pool.Pin(privateId, account.ToString("D"));
                ((OpenAICompatibleSession)session).KeyRotation = pool.ForThread(privateId, account.ToString("D"));
            } : null);
        runtime.Loop.Run(async () =>
        {
            Task<string> Run() => host.RunAsync(new(Guid.Empty, target, ConversationHeadPurpose.Opinion, "context", "question"), _ => { }, () => true, CancellationToken.None);
            if (!binding)
            {
                await Assert.ThrowsAsync<InvalidOperationException>(Run);
                Assert.Empty(sessions.Single().Payloads);
                Assert.Equal(0, spend.SpendSince(key, DateTimeOffset.MinValue));
                return;
            }
            Assert.Equal("Opinion text", await Run());
            Assert.Equal(1, spend.SpendSince(key, DateTimeOffset.MinValue));
            Assert.False(pool.IsAvailable(key));
            Assert.DoesNotContain("tools", sessions[0].Payloads.Single().Keys);
            await Assert.ThrowsAnyAsync<Exception>(Run);
            Assert.Empty(sessions[1].Payloads);
            Assert.Equal(1, spend.SpendSince(key, DateTimeOffset.MinValue));
        });
    }
}
