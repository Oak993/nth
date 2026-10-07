using Marrow.Core.Models;
using Marrow.Core.Port.Accounts;
using Marrow.Services.Port.Accounts;
using Marrow.Testing;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Port.Accounts;

[Trait("Feature", "MA-1")]
[Trait("Rule", "R-TEST-10")]
public sealed class AccountEnvironmentTests
{
    [Theory]
    [InlineData(ProviderKind.Claude, "CLAUDE_CONFIG_DIR")]
    [InlineData(ProviderKind.Codex, "CODEX_HOME")]
    [InlineData(ProviderKind.Cursor, "CURSOR_CONFIG_DIR")]
    [InlineData(ProviderKind.Grok, "GROK_HOME")]
    [InlineData(ProviderKind.Pi, "PI_CODING_AGENT_DIR")]
    public void Dedicated_selectors_use_distinct_opaque_absolute_roots_and_preserve_system_environment(ProviderKind service, string selector)
    {
        using var folder = new AccountTestFolder();
        var builder = new AccountEnvironment(folder.Path);
        var store = new AccountStore(new InMemorySettingsStore());
        var first = store.Add(service);
        var second = store.Add(service);
        var inherited = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [selector] = "wrong", ["HOME"] = "original", ["SystemRoot"] = "system", ["PATH"] = "binary path", ["TEMP"] = "temp" };
        var a = builder.Build(first, inherited);
        var b = builder.Build(second, inherited);
        Assert.Equal(builder.Folder(first), a[selector]);
        Assert.NotEqual(a[selector], b[selector]);
        Assert.True(Path.IsPathRooted(a[selector]));
        Assert.Contains(first.Id.ToString("N"), a[selector], StringComparison.Ordinal);
        Assert.Equal("original", a["HOME"]);
        Assert.Equal("system", a["SystemRoot"]);
        Assert.Equal("binary path", a["PATH"]);
        Assert.Equal("temp", a["TEMP"]);
        Assert.Equal("wrong", inherited[selector]);
    }

    [Theory]
    [InlineData(ProviderKind.Opencode)]
    [InlineData(ProviderKind.OpencodeGo)]
    public void OpenCode_scopes_all_XDG_roots_and_clears_external_config_paths(ProviderKind service)
    {
        using var folder = new AccountTestFolder();
        var builder = new AccountEnvironment(folder.Path);
        var account = new AccountStore(new InMemorySettingsStore()).Add(service);
        var env = builder.Build(account, new Dictionary<string, string> { ["OPENCODE_CONFIG"] = "global.json", ["OPENCODE_CONFIG_DIR"] = "global", ["OPENCODE_CONFIG_CONTENT"] = "MCP inline" });
        foreach (var key in new[] { "XDG_DATA_HOME", "XDG_CONFIG_HOME", "XDG_STATE_HOME", "XDG_CACHE_HOME" })
            Assert.StartsWith(builder.Folder(account) + Path.DirectorySeparatorChar, env[key], StringComparison.Ordinal);
        Assert.False(env.ContainsKey("OPENCODE_CONFIG"));
        Assert.StartsWith(builder.Folder(account), env["OPENCODE_CONFIG_DIR"], StringComparison.Ordinal);
        Assert.Equal("MCP inline", env["OPENCODE_CONFIG_CONTENT"]);
    }

    [Theory]
    [InlineData(ProviderKind.Devin)]
    [InlineData(ProviderKind.Commandcode)]
    [InlineData(ProviderKind.Copilot)]
    [InlineData(ProviderKind.Antigravity)]
    public void Remaining_CLIs_redirect_consistent_home_and_both_app_data_roots(ProviderKind service)
    {
        using var folder = new AccountTestFolder();
        var builder = new AccountEnvironment(folder.Path);
        var account = new Account { Id = Guid.NewGuid(), Service = service };
        var env = builder.Build(account, new Dictionary<string, string>());
        Assert.Equal(env["HOME"], env["USERPROFILE"]);
        foreach (var key in new[] { "HOME", "USERPROFILE", "APPDATA", "LOCALAPPDATA" })
            Assert.StartsWith(builder.Folder(account) + Path.DirectorySeparatorChar, env[key], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ProviderKind.Deepseek)]
    [InlineData(ProviderKind.Meta)]
    [InlineData(ProviderKind.Zai)]
    [InlineData(ProviderKind.OpenRouter)]
    public void API_services_get_private_tool_home_without_inheriting_hosted_keys(ProviderKind service)
    {
        using var folder = new AccountTestFolder();
        var builder = new AccountEnvironment(folder.Path);
        var account = new AccountStore(new InMemorySettingsStore()).Add(service);
        var env = builder.Build(account, new Dictionary<string, string> { ["OPENAI_API_KEY"] = "made-up", ["OPENROUTER_API_KEY"] = "made-up" });
        Assert.StartsWith(builder.Folder(account), env["HOME"], StringComparison.Ordinal);
        Assert.False(env.ContainsKey("OPENAI_API_KEY"));
        Assert.False(env.ContainsKey("OPENROUTER_API_KEY"));
    }

    [Theory]
    [InlineData(ProviderKind.Claude, "ANTHROPIC_API_KEY")]
    [InlineData(ProviderKind.Claude, "CLAUDE_CODE_OAUTH_TOKEN")]
    [InlineData(ProviderKind.Claude, "ANTHROPIC_PROFILE")]
    [InlineData(ProviderKind.Claude, "CLAUDE_CODE_USE_BEDROCK")]
    [InlineData(ProviderKind.Codex, "OPENAI_API_KEY")]
    [InlineData(ProviderKind.Codex, "CODEX_API_KEY")]
    [InlineData(ProviderKind.Cursor, "CURSOR_API_KEY")]
    [InlineData(ProviderKind.Grok, "XAI_API_KEY")]
    [InlineData(ProviderKind.Pi, "ANTHROPIC_API_KEY")]
    [InlineData(ProviderKind.Pi, "OPENAI_API_KEY")]
    [InlineData(ProviderKind.Pi, "GEMINI_API_KEY")]
    [InlineData(ProviderKind.Pi, "OPENROUTER_API_KEY")]
    [InlineData(ProviderKind.Pi, "GROQ_API_KEY")]
    [InlineData(ProviderKind.Pi, "MISTRAL_API_KEY")]
    [InlineData(ProviderKind.Pi, "DEEPSEEK_API_KEY")]
    [InlineData(ProviderKind.Pi, "XAI_API_KEY")]
    [InlineData(ProviderKind.Meta, "MODEL_API_KEY")]
    [InlineData(ProviderKind.Zai, "ZAI_API_KEY")]
    [InlineData(ProviderKind.Devin, "WINDSURF_API_KEY")]
    public void New_accounts_clear_competing_inherited_credentials(ProviderKind service, string key)
    {
        using var folder = new AccountTestFolder();
        var builder = new AccountEnvironment(folder.Path);
        var account = new AccountStore(new InMemorySettingsStore()).Add(service);
        Assert.False(builder.Build(account, new Dictionary<string, string> { [key] = "other identity" }).ContainsKey(key));
    }

    [Fact]
    public void Every_migrated_service_keeps_its_environment_exactly_and_disabled_launches_fail_closed()
    {
        using var folder = new AccountTestFolder();
        var builder = new AccountEnvironment(folder.Path);
        var store = new AccountStore(new InMemorySettingsStore());
        var inherited = new Dictionary<string, string> { ["HOME"] = "original", ["CODEX_HOME"] = "custom", ["OPENAI_API_KEY"] = "original key" };
        foreach (var account in store.Accounts)
        {
            Assert.Equal(inherited, builder.Build(account, inherited));
            Assert.Throws<InvalidOperationException>(() => builder.Build(account with { Enabled = false }, inherited));
        }
    }

    [Fact]
    public void Creating_an_account_folder_writes_nothing_into_the_original_profile()
    {
        using var folder = new AccountTestFolder();
        var root = folder.Path;
        try
        {
            var builder = new AccountEnvironment(root);
            var account = new AccountStore(new InMemorySettingsStore()).Add(ProviderKind.Devin);
            builder.EnsureFolder(account);
            Assert.True(Directory.Exists(builder.Folder(account)));
            Assert.True(Directory.Exists(builder.Build(account, new Dictionary<string, string>())["APPDATA"]));
            Assert.Empty(Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void A_root_given_as_a_function_is_asked_for_only_when_a_folder_is()
    {
        using var folder = new AccountTestFolder();
        var asked = 0;
        var builder = new AccountEnvironment(() => { asked++; return folder.Path; });
        Assert.Equal(0, asked);
        var account = new AccountStore(new InMemorySettingsStore()).Add(ProviderKind.Codex);
        var first = builder.Folder(account);
        Assert.Equal(Path.Combine(Path.GetFullPath(folder.Path), "accounts", account.Service.Id, account.Id.ToString("N")), first);
        Assert.Equal(first, builder.Folder(account));
        Assert.Equal(1, asked);
    }
}
