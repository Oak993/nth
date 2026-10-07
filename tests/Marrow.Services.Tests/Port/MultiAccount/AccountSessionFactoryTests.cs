using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Accounts;
using Marrow.Services.Port.MultiAccount;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Services.Tests.Port.MultiAccount;

[Trait("Rule", "R-TEST-4")]
public sealed class AccountSessionFactoryTests
{
    [Fact]
    public void A_detached_destination_uses_its_own_account_and_never_inherits_foreign_resume_paths_or_transcript()
    {
        using var runtime = new TestRuntime();
        using var folder = new AccountTestFolder();
        var settings = new FakeProviderSettings();
        settings.Keys[ProviderKind.Deepseek] = "offline-key";
        var accounts = new AccountStore(UserDefaults.Standard.Current);
        var account = accounts.ForService(ProviderKind.Deepseek)[0];
        var registry = new ProviderRegistry(settings, accounts, folder.Path);
        var configuration = new SessionConfiguration
        {
            Provider = ProviderKind.Deepseek, AccountId = Guid.NewGuid(), ResumeID = "foreign-session", ResumeAt = "foreign-anchor", ResumePath = "foreign-path",
            WorkingDirectory = SwiftURL.Create("marrow-native-path:" + folder.Path.Replace('\\', '/'))!, Environment = ValueMap<string, string>.Empty,
            Model = "exact-model", Effort = "high", FastMode = true, RuntimeMode = RuntimeMode.Supervised, InteractionMode = InteractionMode.Build,
            Transcript = new ValueList<TranscriptMessage>([new() { TranscriptMessageRole = TranscriptMessage.Role.User, Text = "foreign-context" }]),
        };
        var launch = AccountSessionFactory.Create(registry, configuration, account.Id, freshContext: true);
        Assert.IsType<DeepSeekSession>(launch.Session);
        Assert.Equal(account.Id, launch.Configuration.AccountId);
        Assert.Equal("offline-key", launch.Configuration.ApiKey);
        Assert.Null(launch.Configuration.ResumeID);
        Assert.Null(launch.Configuration.ResumeAt);
        Assert.Null(launch.Configuration.ResumePath);
        Assert.Empty(launch.Configuration.Transcript);
        Assert.Equal("exact-model", launch.Configuration.Model);
        Assert.Equal("high", launch.Configuration.Effort);
        Assert.True(launch.Configuration.FastMode);
        Assert.False(launch.Session.IsRunning);
        accounts.SetEnabled(account.Id, false);
        Assert.Throws<InvalidOperationException>(() => AccountSessionFactory.Create(registry, configuration, account.Id, freshContext: true));
    }

    [Fact]
    public void PrepareSession_also_clears_a_foreign_provider_resume_path_when_called_outside_the_detached_factory()
    {
        using var runtime = new TestRuntime();
        using var folder = new AccountTestFolder();
        var accounts = new AccountStore(UserDefaults.Standard.Current);
        var account = accounts.Add(ProviderKind.Codex);
        var registry = new ProviderRegistry(new FakeProviderSettings(), accounts, folder.Path);
        var configuration = new SessionConfiguration
        {
            Provider = ProviderKind.Codex, AccountId = Guid.NewGuid(), ResumeID = "foreign", ResumePath = "foreign-path",
            WorkingDirectory = SwiftURL.Create("marrow-native-path:" + folder.Path.Replace('\\', '/'))!, Environment = ValueMap<string, string>.Empty,
            RuntimeMode = RuntimeMode.Supervised, InteractionMode = InteractionMode.Build,
        };
        Assert.Null(registry.PrepareSession(configuration, account.Id).ResumePath);
    }
}
