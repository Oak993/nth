using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Port.Usage;
using Marrow.Services.Providers;
using Marrow.Testing;

namespace Marrow.Services.Tests.Port.Usage;

public sealed class AccountPlanLimitsTests
{
    [Fact]
    [Trait("Decision", "ED-406")]
    public async Task Explicit_Claude_roots_use_only_their_own_credentials_and_never_global_login()
    {
        var root = Path.Combine(Environment.GetEnvironmentVariable("MARROW_PORT_DATA_DIR")!, Guid.NewGuid().ToString("N"));
        var home = Path.Combine(root, "home");
        Directory.CreateDirectory(Path.Combine(home, ".claude"));
        File.WriteAllText(Path.Combine(home, ".claude", ".credentials.json"), Login("global"));
        foreach (var name in new[] { "A", "B" })
        {
            var folder = Path.Combine(root, name);
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, ".credentials.json"), Login(name));
            var env = new Dictionary<string, string> { ["CLAUDE_CONFIG_DIR"] = folder };
            Assert.Equal(name, (await PlanLimitsReader.ReaderClaudeLogin(env, home, allowHomeFallback: false))!.AccessToken);
            File.WriteAllText(Path.Combine(folder, ".credentials.json"), "{}");
            Assert.Null(await PlanLimitsReader.ReaderClaudeLogin(env, home, allowHomeFallback: false));
            File.Delete(Path.Combine(folder, ".credentials.json"));
            Assert.Null(await PlanLimitsReader.ReaderClaudeLogin(env, home, allowHomeFallback: false));
            Assert.Equal("global", (await PlanLimitsReader.ReaderClaudeLogin(env, home))!.AccessToken);
        }
        Assert.Null(await PlanLimitsReader.ReaderClaudeLogin(new Dictionary<string, string>(), home, allowHomeFallback: false));
    }

    [Fact]
    [Trait("Decision", "ED-406")]
    public async Task Explicit_accounts_without_scoped_credentials_return_unshared_without_a_global_read()
    {
        var executable = SwiftURL.Create("marrow-native-path:/missing-ma2-cli")!;
        var empty = new Dictionary<string, string>();
        foreach (var provider in new[] { ProviderKind.Claude, ProviderKind.Codex, ProviderKind.Pi, ProviderKind.Commandcode, ProviderKind.Copilot, ProviderKind.Antigravity })
            Assert.Null(await PlanLimitsReader.Read(new UsageAccountKey(provider, "A"), executable, empty));
        Assert.Null(await PlanLimitsReader.Read(new UsageAccountKey(ProviderKind.Zai, "A"), ""));
    }

    [Fact]
    [Trait("Decision", "ED-406")]
    public void Service_windows_and_credits_are_preserved_without_inventing_plan_windows()
    {
        var account = new UsageAccountKey(ProviderKind.Claude, "A");
        var limits = PlanLimitsReader.ParseClaudeUsage(JSONValue.Parse("{\"seven_day\":{\"utilization\":27},\"extra_usage\":{\"is_enabled\":true,\"utilization\":8}}")!, [], "Future 20x");
        var snapshot = new AccountPlanLimitsSnapshot(account, limits, DateTimeOffset.UnixEpoch);
        Assert.Equal("no 5-hour window", snapshot.FiveHourStatus);
        Assert.Equal(2, snapshot.Limits!.Windows.Count);
        Assert.Equal(27, snapshot.Limits.Windows[0].Percent);
        Assert.Equal(10080, snapshot.Limits.Windows[0].DurationMinutes);
        Assert.Null(snapshot.Limits.Windows[1].DurationMinutes);
        var five = PlanLimitsReader.ParseClaudeUsage(JSONValue.Parse("{\"five_hour\":{\"utilization\":4}}")!, [], "Unknown plan");
        Assert.Equal("5-hour limit", new AccountPlanLimitsSnapshot(account, five, DateTimeOffset.UnixEpoch).FiveHourStatus);
        Assert.Equal(300, Assert.Single(five!.Windows).DurationMinutes);
        Assert.Equal("limits not shared", new AccountPlanLimitsSnapshot(account, null, DateTimeOffset.UnixEpoch).FiveHourStatus);
        Assert.Equal("limits not shared", new AccountPlanLimitsSnapshot(account, new PlanLimits { PlanName = "20x", Windows = ValueList<PlanLimits.Window>.Empty }, DateTimeOffset.UnixEpoch).FiveHourStatus);
    }

    [Fact]
    [Trait("Decision", "ED-406")]
    public void Usage_limit_signals_keep_the_account_and_existing_reset_parser()
    {
        var account = new UsageAccountKey(ProviderKind.Claude, "B");
        var signal = UsageLimitSignal.Detect("usage limit | 1800000000", account, DateTimeOffset.UnixEpoch);
        Assert.Equal(account, signal!.Account);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1800000000), signal.ResetsAt);
        Assert.Null(UsageLimitSignal.Detect("network timeout", account, DateTimeOffset.UnixEpoch));
    }

    [Fact]
    [Trait("Decision", "ED-406")]
    public async Task Valid_account_reads_use_selected_tokens_and_keys_in_process_with_distinct_service_reports()
    {
        using var runtime = new TestRuntime();
        using var handler = new InProcessHttpHandler { Answer = request => InProcessHttpHandler.Reply(200,
            request.Url.AbsolutePath.Contains("reset", StringComparison.Ordinal) ? "{\"code\":200,\"data\":[]}" :
            request.Url.Host == "api.anthropic.com" ?
                request.Header("Authorization") == "Bearer A" ? "{\"five_hour\":{\"utilization\":12}}" : "{\"seven_day\":{\"utilization\":73}}" :
                "{\"code\":200,\"data\":{\"limits\":[{\"type\":\"CREDIT_LIMIT\",\"unit\":3,\"number\":5,\"percentage\":22}]}}") };
        using var transport = URLSession.Transport.Override(handler);
        var executable = SwiftURL.Create("marrow-native-path:/missing-ma2-cli")!;
        foreach (var name in new[] { "A", "B" })
        {
            var root = Path.Combine(Environment.GetEnvironmentVariable("MARROW_PORT_DATA_DIR")!, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, ".credentials.json"), Login(name));
            var limits = await PlanLimitsReader.Read(new UsageAccountKey(ProviderKind.Claude, name), executable,
                new Dictionary<string, string> { ["CLAUDE_CONFIG_DIR"] = root });
            Assert.Equal(name == "A" ? 12 : 73, Assert.Single(limits!.Windows).Percent);
            Assert.Equal(name == "A" ? 300 : 10080, limits.Windows[0].DurationMinutes);
        }
        await PlanLimitsReader.Read(new UsageAccountKey(ProviderKind.Zai, "A"), "key-A");
        await PlanLimitsReader.Read(new UsageAccountKey(ProviderKind.Zai, "B"), "key-B");
        Assert.Contains(handler.Received, request => request.Header("Authorization") == "Bearer key-A");
        Assert.Contains(handler.Received, request => request.Header("Authorization") == "Bearer key-B");
    }

    [Fact]
    [Trait("Decision", "ED-406")]
    public async Task Account_API_throttle_reaches_the_scheduler_and_unknown_durations_are_not_guessed()
    {
        using var runtime = new TestRuntime();
        using var handler = new InProcessHttpHandler { Answer = _ => InProcessHttpHandler.Reply(429, "{}", headers: [("Retry-After", "600")]) };
        using var transport = URLSession.Transport.Override(handler);
        var error = await Assert.ThrowsAsync<LimitsPollException>(() => PlanLimitsReader.Read(new UsageAccountKey(ProviderKind.Zai, "A"), "key-A"));
        Assert.Equal(TimeSpan.FromMinutes(10), error.RetryAfter);
        var json = JSONValue.Parse("{\"data\":{\"limits\":[{\"type\":\"CREDIT_LIMIT\",\"percentage\":20}]}}")!;
        var limits = ZaiAPI.Limits(json, reportedWindowsOnly: true);
        Assert.Equal("Usage limit", Assert.Single(limits!.Windows).Title);
        Assert.Null(limits.Windows[0].DurationMinutes);
        Assert.Equal("no 5-hour window", new AccountPlanLimitsSnapshot(new(ProviderKind.Zai, "A"), limits, DateTimeOffset.UnixEpoch).FiveHourStatus);
        Assert.Equal("5-hour limit", Assert.Single(ZaiAPI.Limits(json)!.Windows).Title);
    }

    [Fact]
    [Trait("Decision", "ED-406")]
    public async Task Command_code_account_metadata_does_not_change_the_legacy_single_account_result()
    {
        using var runtime = new TestRuntime();
        using var handler = new InProcessHttpHandler { Answer = request => InProcessHttpHandler.Reply(200,
            request.Url.AbsolutePath.EndsWith("credits", StringComparison.Ordinal)
                ? "{\"windowLimits\":{\"fiveHour\":{\"cap\":10,\"used\":2}}}"
                : "{\"data\":{}}") };
        using var transport = URLSession.Transport.Override(handler);
        using var money = CommandCodeAPI.MoneyFormatter.Override((_, _) => "USD 10");
        var env = new Dictionary<string, string> { ["COMMAND_CODE_API_KEY"] = "synthetic-key" };
        var legacy = await CommandCodeAPI.PlanLimits(env);
        Assert.Null(Assert.Single(legacy!.Windows).DurationMinutes);
        var account = await PlanLimitsReader.Read(new UsageAccountKey(ProviderKind.Commandcode, "A"), SwiftURL.Create("marrow-native-path:/missing-ma2-cli")!, env);
        var window = Assert.Single(account!.Windows);
        Assert.Equal(300, window.DurationMinutes);
        Assert.Equal(legacy.Windows[0].Title, window.Title);
        Assert.Equal(legacy.Windows[0].Percent, window.Percent);
    }

    private static string Login(string token) => "{\"claudeAiOauth\":{\"accessToken\":\"" + token + "\"}}";
}
