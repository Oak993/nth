using System.Reflection;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Accounts;
using Marrow.Core.Support;
using Marrow.Services.Port.Accounts;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Services.Tests.Port.Accounts;

[Trait("Feature", "MA-1")]
[Trait("Rule", "R-TEST-10")]
public sealed class AccountReaderTests
{
    [Fact]
    public async Task Explicit_Claude_account_never_reads_the_original_OAuth_file()
    {
        using var folder = new AccountTestFolder();
        var legacy = Path.Combine(folder.Path, "legacy-profile");
        Directory.CreateDirectory(Path.Combine(legacy, ".claude"));
        var original = Path.Combine(legacy, ".claude", ".credentials.json");
        var bytes = "{\"claudeAiOauth\":{\"accessToken\":\"synthetic-never-send\"}}"u8.ToArray();
        File.WriteAllBytes(original, bytes);
        var store = new AccountStore(new InMemorySettingsStore());
        var account = store.Add(ProviderKind.Claude);
        var builder = new AccountEnvironment(folder.Path);
        builder.EnsureFolder(account);
        var env = builder.Build(account, new Dictionary<string, string> { ["HOME"] = legacy });
        Assert.Null(await PlanLimitsReader.ReaderClaudeLogin(env, legacy));
        Assert.Equal(bytes, File.ReadAllBytes(original));
        Assert.Empty(Directory.EnumerateFiles(builder.Folder(account), "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Grok_status_uses_the_selected_root_without_reading_the_original_account()
    {
        using var folder = new AccountTestFolder();
        var legacy = Path.Combine(folder.Path, "legacy-auth.json");
        Directory.CreateDirectory(folder.Path);
        File.WriteAllText(legacy, "{\"test\":{\"email\":\"wrong@example.invalid\"}}");
        var store = new AccountStore(new InMemorySettingsStore());
        var account = store.Add(ProviderKind.Grok);
        var builder = new AccountEnvironment(folder.Path);
        builder.EnsureFolder(account);
        var env = builder.Build(account, new Dictionary<string, string>());
        ProviderRegistry.SetGrokAuthPathForTesting(legacy);
        try
        {
            Assert.IsType<ProviderStatus.Auth.Unknown>(await ProviderRegistry.Auth(ProviderKind.Grok, SwiftURL.FileURLWithPath("/fake-never-executed"), env));
            File.WriteAllText(Path.Combine(builder.Folder(account), "auth.json"), "{\"test\":{\"email\":\"right@example.invalid\"}}");
            Assert.Equal(new ProviderStatus.Auth.SignedIn("right@example.invalid"), await ProviderRegistry.Auth(ProviderKind.Grok, SwiftURL.FileURLWithPath("/fake-never-executed"), env));
        }
        finally { ProviderRegistry.ResetGrokAuthPathForTesting(); }
    }

    [Fact]
    public async Task API_tool_launch_uses_the_prepared_account_environment_without_starting_a_shell()
    {
        using var folder = new AccountTestFolder();
        using var runtime = new TestRuntime();
        var store = new AccountStore(UserDefaults.Standard.Current);
        var account = store.Add(ProviderKind.Deepseek);
        var env = new AccountEnvironment(folder.Path).Build(account, new Dictionary<string, string> { ["OPENAI_API_KEY"] = "synthetic" });
        var configuration = new SessionConfiguration
        {
            Provider = ProviderKind.Deepseek, AccountId = account.Id, Environment = env,
            WorkingDirectory = SwiftURL.Create("marrow-native-path:" + folder.Path.Replace('\\', '/'))!, RuntimeMode = RuntimeMode.Supervised, InteractionMode = InteractionMode.Build,
        };
        var session = new DeepSeekSession(configuration);
        ShellInvocation? seen = null;
        using var observer = ShellObserver.Observe(call => { seen = call; throw new InvalidOperationException("No process is permitted in this test."); });
        var method = typeof(OpenAICompatibleSession).GetMethod("RunCommand", BindingFlags.Instance | BindingFlags.NonPublic)!;
        await (Task)method.Invoke(session, ["synthetic command never executed"])!;
        Assert.NotNull(seen);
        Assert.Equal(env, seen.Environment);
        Assert.False(seen.Environment!.ContainsKey("OPENAI_API_KEY"));
        Assert.Equal(env["HOME"], seen.Environment["HOME"]);
    }
}
