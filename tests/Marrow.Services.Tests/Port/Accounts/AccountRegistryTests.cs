using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Accounts;
using Marrow.Core.Support;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Services.Tests.Port.Accounts;

[Trait("Feature", "MA-1")]
[Trait("Rule", "R-TEST-10")]
public sealed class AccountRegistryTests
{
    [Fact]
    public void Registry_prepares_a_session_with_the_account_environment_identity_and_defaults()
    {
        using var runtime = new TestRuntime();
        using var executable = ProviderRegistry.SetCLIUpdaterExecutableForTesting(_ => null);
        var accounts = new AccountStore(UserDefaults.Standard.Current);
        var account = accounts.Add(ProviderKind.Claude, "Max");
        accounts.SetDefaults(account.Id, new AccountDefaults { Model = "opus", Effort = "high", FastMode = true });
        using var folder = new AccountTestFolder();
        var registry = new ProviderRegistry(new FakeProviderSettings(), accounts, folder.Path);
        var prepared = registry.PrepareSession(Input(ProviderKind.Claude), account.Id);
        Assert.Equal(account.Id, prepared.AccountId);
        Assert.Equal(registry.Environment(account.Id), prepared.Environment);
        Assert.Contains(account.Id.ToString("N"), prepared.Environment["CLAUDE_CONFIG_DIR"], StringComparison.Ordinal);
        Assert.Equal("opus", prepared.Model);
        Assert.Equal("high", prepared.Effort);
        Assert.True(prepared.FastMode);
        Assert.Equal(HandshakePoint.Smart, prepared.AccountHandshakePoint);
        Assert.Equal(RuntimeMode.Supervised, prepared.RuntimeMode);
        Assert.Null(prepared.ApiKey);
    }

    [Fact]
    public async Task Disabled_API_accounts_cannot_be_probed_using_the_legacy_settings_key()
    {
        using var runtime = new TestRuntime();
        using var folder = new AccountTestFolder();
        var handler = new InProcessHttpHandler { Answer = _ => InProcessHttpHandler.Reply(401, "{}") };
        using var transport = URLSession.Transport.Override(handler);
        var settings = new FakeProviderSettings();
        settings.Keys[ProviderKind.Deepseek] = "synthetic-legacy-key";
        var accounts = new AccountStore(UserDefaults.Standard.Current);
        foreach (var account in accounts.ForService(ProviderKind.Deepseek)) accounts.SetEnabled(account.Id, false);
        var registry = new ProviderRegistry(settings, accounts, folder.Path);
        await Task.Run(() => runtime.Loop.Run(async () =>
        {
            await registry.Refresh(ProviderKind.Deepseek);
            await registry.LoadCatalog(ProviderKind.Deepseek, true);
        }, TimeSpan.FromSeconds(30)));
        runtime.OnMain(() => { registry.RefreshPlanLimits(ProviderKind.Deepseek, true); registry.RefreshCredits(ProviderKind.Deepseek, true); });
        runtime.RunUntilIdle();
        Assert.Empty(handler.Received);
        Assert.False(registry.Status(ProviderKind.Deepseek).IsChecking);
        Assert.False(registry.Status(ProviderKind.Deepseek).ApiKeyConfigured);
    }

    [Fact]
    public void Selected_API_session_uses_only_its_key_with_no_legacy_fallback()
    {
        using var runtime = new TestRuntime();
        using var folder = new AccountTestFolder();
        using var secrets = Marrow.Services.Port.SecretStore.Shared.Override(new Marrow.Services.Port.SecretStore(new InMemoryCredentialBackend()));
        var settings = new FakeProviderSettings();
        settings.Keys[ProviderKind.Deepseek] = "synthetic-legacy-key";
        var accounts = new AccountStore(UserDefaults.Standard.Current);
        var account = accounts.Add(ProviderKind.Deepseek);
        var registry = new ProviderRegistry(settings, accounts, folder.Path);
        Assert.Equal("", registry.PrepareSession(Input(ProviderKind.Deepseek), account.Id).ApiKey);
        Assert.True(APIKeychain.SetKey("synthetic-selected-key", account));
        Assert.Equal("synthetic-selected-key", registry.PrepareSession(Input(ProviderKind.Deepseek), account.Id).ApiKey);
    }

    [Fact]
    public void Preparing_another_account_never_keeps_a_foreign_native_resume_id_or_Pi_path()
    {
        using var runtime = new TestRuntime();
        using var executable = ProviderRegistry.SetCLIUpdaterExecutableForTesting(_ => null);
        using var folder = new AccountTestFolder();
        var accounts = new AccountStore(UserDefaults.Standard.Current);
        var first = accounts.Add(ProviderKind.Pi);
        var second = accounts.Add(ProviderKind.Pi);
        var registry = new ProviderRegistry(new FakeProviderSettings(), accounts, folder.Path);
        var input = Input(ProviderKind.Pi) with { AccountId = first.Id, ResumeID = "/foreign/account/session.jsonl", ResumeAt = "step" };
        var prepared = registry.PrepareSession(input, second.Id);
        Assert.Null(prepared.ResumeID);
        Assert.Null(prepared.ResumeAt);
        var mislabeled = registry.PrepareSession(input with { AccountId = second.Id }, second.Id);
        Assert.Null(mislabeled.ResumeID);
        Assert.Null(mislabeled.ResumeAt);
    }

    [Fact]
    public void Disabled_and_wrong_service_accounts_are_refused_at_every_launch_boundary()
    {
        using var runtime = new TestRuntime();
        using var executable = ProviderRegistry.SetCLIUpdaterExecutableForTesting(_ => null);
        var accounts = new AccountStore(UserDefaults.Standard.Current);
        var account = accounts.Add(ProviderKind.Codex);
        using var folder = new AccountTestFolder();
        var registry = new ProviderRegistry(new FakeProviderSettings(), accounts, folder.Path);
        Assert.Throws<InvalidOperationException>(() => registry.PrepareSession(Input(ProviderKind.Claude), account.Id));
        foreach (var row in accounts.ForService(ProviderKind.Codex)) accounts.SetEnabled(row.Id, false);
        Assert.Throws<InvalidOperationException>(() => registry.Environment(account.Id));
        Assert.Throws<InvalidOperationException>(() => registry.Environment(ProviderKind.Codex));
        Assert.Throws<InvalidOperationException>(() => registry.PrepareSession(Input(ProviderKind.Codex), account.Id));
    }

    [Fact]
    public async Task Sign_in_is_single_flight_per_account_persisted_on_success_and_runs_in_the_account_folder()
    {
        using var runtime = new TestRuntime();
        using var executable = ProviderRegistry.SetCLIUpdaterExecutableForTesting(_ => SwiftURL.FileURLWithPath("/fake-never-executed"));
        var accounts = new AccountStore(UserDefaults.Standard.Current);
        var first = accounts.Add(ProviderKind.Claude);
        var second = accounts.Add(ProviderKind.Claude);
        var root = Path.Combine(Environment.GetEnvironmentVariable("MARROW_PORT_DATA_DIR")!, "ma1-signin-" + Guid.NewGuid().ToString("N"));
        try
        {
            var registry = new ProviderRegistry(new FakeProviderSettings(), accounts, root);
            registry.AccountLoginLoad = () => Task.CompletedTask;
            var calls = new List<ShellInvocation>();
            var finish = new TaskCompletionSource<ShellResult>();
            registry.AccountLoginRunner = call => { calls.Add(call); return finish.Task; };
            var a = registry.SignIn(first.Id);
            var again = registry.SignIn(first.Id);
            var b = registry.SignIn(second.Id);
            finish.SetResult(new ShellResult(0, "signed in"u8.ToArray(), []));
            Assert.Null(await a);
            Assert.Null(await again);
            Assert.Null(await b);
            Assert.Equal(2, calls.Count);
            Assert.Equal(["auth", "login"], calls[0].Arguments);
            Assert.Equal(300, calls[0].Timeout);
            Assert.Equal(calls[0].Environment!["CLAUDE_CONFIG_DIR"], calls[0].Directory);
            Assert.NotEqual(calls[0].Directory, calls[1].Directory);
            Assert.True(Directory.Exists(calls[0].Directory));
            Assert.Null(await registry.SignIn(first.Id));
            Assert.Equal(2, calls.Count);
            Assert.True(new AccountStore(UserDefaults.Standard.Current).Get(first.Id).HasSignedIn);
            accounts.SetEnabled(first.Id, false);
            await Assert.ThrowsAsync<InvalidOperationException>(() => registry.SignIn(first.Id));
            Assert.Equal(2, calls.Count);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Failed_sign_in_is_retryable_and_does_not_mark_the_account_signed_in()
    {
        using var runtime = new TestRuntime();
        using var executable = ProviderRegistry.SetCLIUpdaterExecutableForTesting(_ => SwiftURL.FileURLWithPath("/fake-never-executed"));
        var accounts = new AccountStore(UserDefaults.Standard.Current);
        var account = accounts.Add(ProviderKind.Codex);
        var root = Path.Combine(Environment.GetEnvironmentVariable("MARROW_PORT_DATA_DIR")!, "ma1-retry-" + Guid.NewGuid().ToString("N"));
        try
        {
            var registry = new ProviderRegistry(new FakeProviderSettings(), accounts, root);
            registry.AccountLoginLoad = () => Task.CompletedTask;
            var calls = 0;
            registry.AccountLoginRunner = _ => { calls++; return Task.FromResult(new ShellResult(1, [], "try again"u8.ToArray())); };
            Assert.Equal("try again", await registry.SignIn(account.Id));
            Assert.False(accounts.Get(account.Id).HasSignedIn);
            Assert.Equal("try again", await registry.SignIn(account.Id));
            Assert.Equal(2, calls);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private static SessionConfiguration Input(ProviderKind service) => new()
    {
        Provider = service, WorkingDirectory = SwiftURL.FileURLWithPath("/project"), Environment = ValueMap<string, string>.Empty,
        RuntimeMode = RuntimeMode.Supervised, InteractionMode = InteractionMode.Build,
    };
}
