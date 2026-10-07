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
public sealed class AccountConversationHeadHostTests
{
    [Fact]
    public void The_concrete_host_uses_a_fresh_account_bound_session_and_never_resumes_the_working_thread()
    {
        using var runtime = new TestRuntime();
        using var folder = new AccountTestFolder();
        var accounts = new AccountStore(runtime.Defaults);
        var id = accounts.ForService(ProviderKind.Deepseek)[0].Id;
        var settings = new FakeProviderSettings(); settings.Keys[ProviderKind.Deepseek] = "offline-key";
        var registry = new ProviderRegistry(settings, accounts, folder.Path);
        var source = new ConversationHeadAccount(Guid.NewGuid(), ProviderKind.Claude, "Claude Max 1", true, true);
        var target = new ConversationHeadAccount(id, ProviderKind.Deepseek, "DeepSeek", true, true);
        var prompt = new TransferPromptFake { Answer = new(true, false) };
        var consent = new ConversationTransferConsent(runtime.Defaults, prompt);
        var session = new HandoffSession();
        SessionConfiguration? prepared = null;
        using var factory = AccountSessionFactory.Constructor.Override(config => { prepared = config; return session; });
        var host = new AccountConversationHeadHost(registry, _ => source.Id, _ => 1, _ => [source, target],
            (_, _) => Task.FromResult("FULL RECORDED CONTEXT"),
            (_, _) => new SessionConfiguration
            {
                Provider = target.Service, AccountId = source.Id, ResumeID = "working-session", ResumePath = "working-rollout",
                WorkingDirectory = SwiftURL.Create("marrow-native-path:" + folder.Path.Replace('\\', '/'))!, Environment = ValueMap<string, string>.Empty,
                RuntimeMode = RuntimeMode.Supervised, InteractionMode = InteractionMode.Build,
                Transcript = new([new() { TranscriptMessageRole = TranscriptMessage.Role.User, Text = "foreign transcript" }]),
                IsHydraHead = true,
            }, consent, (_, _, _, _, _) => Task.CompletedTask);
        runtime.Loop.Run(async () =>
        {
            Assert.True(await host.ConfirmTransferAsync(Guid.Empty, source, [target], CancellationToken.None));
            var run = host.RunAsync(new(Guid.Empty, target, ConversationHeadPurpose.Opinion, "FULL RECORDED CONTEXT", "USER QUESTION"), _ => { }, () => true, CancellationToken.None);
            Assert.NotNull(prepared);
            Assert.Equal(id, prepared.AccountId);
            Assert.Null(prepared.ResumeID); Assert.Null(prepared.ResumePath); Assert.Empty(prepared.Transcript);
            Assert.False(prepared.IsHydraHead); Assert.Null(prepared.Hydra); Assert.Null(prepared.SkillGeneration);
            Assert.Contains("FULL RECORDED CONTEXT", session.Inputs.Single().Text, StringComparison.Ordinal);
            Assert.Contains("USER QUESTION", session.Inputs.Single().Text, StringComparison.Ordinal);
            Assert.True(session.Inputs.Single().IsFinalReport);
            session.OnEvent!(new ProviderEvent.MessageCompleted("a", "Actual opinion"));
            session.OnEvent!(new ProviderEvent.TurnCompleted(TurnStatus.Completed, null));
            Assert.Equal("Actual opinion", await run);
            Assert.True(session.Stopped);
        });
    }
}
