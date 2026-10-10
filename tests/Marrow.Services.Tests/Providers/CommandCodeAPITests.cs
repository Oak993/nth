using System.Text;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Services.Tests.Providers;

// Source: DroppyCode/Services/Providers/CommandCodeAPI.swift (B3 52bc7926), :1-460.
//
// Every expectation below is derived from the pinned Swift and the records PR-PROVCLI-250 to 265, UI-PROVCLI-019 and
// UI-PROVCLI-024, never from running the port (R-TEST-4). Network tests run against LoopbackHttpServer on 127.0.0.1
// through the PORT-ONLY CommandCodeAPI.FetchBaseURL seam (R-TEST-10); process tests spawn the offline stand-in
// tests/fixtures/command-code-cli/CommandCodeStandIn.cs, never a real Command Code CLI. Every environment passed in
// carries HOME pointing at a fake home folder under MARROW_PORT_DATA_DIR (R-TEST-9), so no test ever reads the user's
// own ~/.commandcode/auth.json or config.json; the HOME-empty fallback to LoginEnvironment.homeDirectory (:34, :44) is
// therefore not exercised here: exercising it would read the real user profile.
public sealed class CommandCodeAPITests
{
    private static readonly TimeSpan Guard = LoopbackHttpServer.Guard;
    private const string Dot = " \U000000B7 "; // U+00B7 MIDDLE DOT, a space on each side
    private const string Times = "\U000000D7"; // U+00D7 MULTIPLICATION SIGN
    private const string Esc = "\U0000001B"; // U+001B ESCAPE

    private static byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text);

    private static Dictionary<string, string> Env(string home, params (string Key, string Value)[] extra)
    {
        var environment = new Dictionary<string, string> { ["HOME"] = home };
        foreach (var (key, value) in extra) environment[key] = value;
        return environment;
    }

    private static readonly string[] Full = ["low", "medium", "high", "xhigh", "max"];
    private static readonly string[] ToXHigh = ["low", "medium", "high", "xhigh"];
    private static readonly string[] ToHigh = ["low", "medium", "high"];
    private static readonly string[] HighMax = ["high", "max"];
    private static readonly string[] LowHighMax = ["low", "high", "max"];

    // ---- :13-14 -------------------------------------------------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-PROVCLI-258")] // Services/Providers/CommandCodeAPI.swift:13-14
    public void BaseURL_is_api_commandcode_ai_and_the_billing_page_is_commandcode_ai_billing()
    {
        Assert.Equal("https://api.commandcode.ai", CommandCodeAPI.BaseURL.OriginalString);
        Assert.Equal("https://commandcode.ai/billing", CommandCodeAPI.BillingURL.OriginalString);
        Assert.Same(CommandCodeAPI.BaseURL, CommandCodeAPI.FetchBaseURL.Current);
    }

    // ---- PR-PROVCLI-255: apiKey and storedLogin ---------------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-PROVCLI-255")] // Services/Providers/CommandCodeAPI.swift:19-22
    public void ApiKey_takes_the_environment_key_trimmed_of_whitespace_and_newlines_over_the_stored_login()
    {
        var home = CommandCodeScratch.NewHome(authJson: """{"apiKey":"stored-key","userName":"alice"}""");

        var key = CommandCodeAPI.ApiKey(Env(home, ("COMMAND_CODE_API_KEY", " \t env-key \r\n")));

        Assert.Equal("env-key", key);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-255")] // Services/Providers/CommandCodeAPI.swift:20-23
    public void ApiKey_falls_back_to_the_stored_login_when_the_environment_key_is_blank_or_missing()
    {
        var home = CommandCodeScratch.NewHome(authJson: """{"apiKey":"  stored-key\n","userName":"alice"}""");

        Assert.Equal("stored-key", CommandCodeAPI.ApiKey(Env(home, ("COMMAND_CODE_API_KEY", " \n "))));
        Assert.Equal("stored-key", CommandCodeAPI.ApiKey(Env(home)));
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-255")] // Services/Providers/CommandCodeAPI.swift:23, :36-37
    public void ApiKey_is_nil_with_no_environment_key_and_no_auth_json()
    {
        var home = CommandCodeScratch.NewHome();

        Assert.Null(CommandCodeAPI.ApiKey(Env(home)));
        Assert.Null(CommandCodeAPI.CommandCodeAPIStoredLogin(Env(home)));
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-255")] // Services/Providers/CommandCodeAPI.swift:33-38
    public void StoredLogin_reads_home_commandcode_auth_json_with_a_trimmed_key_and_the_userName()
    {
        var home = CommandCodeScratch.NewHome(authJson:
            """{"apiKey":" k-123 ","userId":"u1","userName":"alice","keyName":"laptop","authenticatedAt":"2026-01-01"}""");

        var login = CommandCodeAPI.CommandCodeAPIStoredLogin(Env(home));

        Assert.Equal(new CommandCodeAPI.StoredLogin("k-123", "alice"), login);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-255")] // Services/Providers/CommandCodeAPI.swift:38
    public void StoredLogin_user_name_is_userName_else_name_else_nil()
    {
        var withName = CommandCodeScratch.NewHome(authJson: """{"apiKey":"k","name":"bob"}""");
        var withBoth = CommandCodeScratch.NewHome(authJson: """{"apiKey":"k","userName":"alice","name":"bob"}""");
        var withNeither = CommandCodeScratch.NewHome(authJson: """{"apiKey":"k"}""");
        var nonString = CommandCodeScratch.NewHome(authJson: """{"apiKey":"k","userName":7,"name":"bob"}""");

        Assert.Equal("bob", CommandCodeAPI.CommandCodeAPIStoredLogin(Env(withName))!.UserName);
        Assert.Equal("alice", CommandCodeAPI.CommandCodeAPIStoredLogin(Env(withBoth))!.UserName);
        Assert.Null(CommandCodeAPI.CommandCodeAPIStoredLogin(Env(withNeither))!.UserName);
        Assert.Equal("bob", CommandCodeAPI.CommandCodeAPIStoredLogin(Env(nonString))!.UserName);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-255")] // Services/Providers/CommandCodeAPI.swift:36-37
    public void StoredLogin_is_nil_for_unparseable_json_an_empty_or_blank_key_or_a_key_that_is_not_a_string()
    {
        Assert.Null(CommandCodeAPI.CommandCodeAPIStoredLogin(Env(CommandCodeScratch.NewHome(authJson: "{not json"))));
        Assert.Null(CommandCodeAPI.CommandCodeAPIStoredLogin(Env(CommandCodeScratch.NewHome(authJson: """{"apiKey":""}"""))));
        Assert.Null(CommandCodeAPI.CommandCodeAPIStoredLogin(Env(CommandCodeScratch.NewHome(authJson: """{"apiKey":" \n "}"""))));
        Assert.Null(CommandCodeAPI.CommandCodeAPIStoredLogin(Env(CommandCodeScratch.NewHome(authJson: """{"apiKey":42}"""))));
        Assert.Null(CommandCodeAPI.CommandCodeAPIStoredLogin(Env(CommandCodeScratch.NewHome(authJson: """{"userName":"alice"}"""))));
    }

    // ---- PR-PROVCLI-260: userConfig ---------------------------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-PROVCLI-260")] // Services/Providers/CommandCodeAPI.swift:43-47
    public void UserConfig_parses_home_commandcode_config_json_and_is_nil_when_missing_or_unparseable()
    {
        var home = CommandCodeScratch.NewHome(configJson: """{"model":"kimi-k3","reasoningEffort":{"moonshotai/kimi-k3":"high"}}""");

        var config = CommandCodeAPI.UserConfig(Env(home));

        Assert.NotNull(config);
        Assert.Equal("kimi-k3", config!["model"]?.JSONValueString);
        Assert.Equal("high", config["reasoningEffort"]?["moonshotai/kimi-k3"]?.JSONValueString);
        Assert.Null(CommandCodeAPI.UserConfig(Env(CommandCodeScratch.NewHome())));
        Assert.Null(CommandCodeAPI.UserConfig(Env(CommandCodeScratch.NewHome(configJson: "{oops"))));
    }

    // ---- PR-PROVCLI-256: authStatus ---------------------------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-PROVCLI-256")] // Services/Providers/CommandCodeAPI.swift:53
    public async Task AuthStatus_is_signed_out_without_asking_the_CLI_when_there_is_no_key()
    {
        var script = new CommandCodeScript("no-key");
        script.SetStdout("Authenticated as alice\n");
        var home = CommandCodeScratch.NewHome();

        var status = await CommandCodeAPI.AuthStatus(script.Executable, Env(home, (script.EnvironmentEntry.Key, script.EnvironmentEntry.Value))).WaitAsync(Guard);

        Assert.IsType<ProviderStatus.Auth.SignedOut>(status);
        Assert.Empty(script.Calls());
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-256")] // Services/Providers/CommandCodeAPI.swift:54, :57-59
    public async Task AuthStatus_runs_status_no_auto_update_and_reads_the_name_after_Authenticated_as_through_ANSI()
    {
        var script = new CommandCodeScript("signed-in");
        script.SetStdout(Esc + "[32mAuthenticated as " + Esc + "[1malice@example.com" + Esc + "[0m (Pro)\n");
        var home = CommandCodeScratch.NewHome();

        var status = await CommandCodeAPI.AuthStatus(script.Executable,
            Env(home, ("COMMAND_CODE_API_KEY", "k"), (script.EnvironmentEntry.Key, script.EnvironmentEntry.Value))).WaitAsync(Guard);

        Assert.Equal(new ProviderStatus.Auth.SignedIn("alice@example.com"), status);
        var call = Assert.Single(script.Calls());
        Assert.Equal(["status", "--no-auto-update"], call.Arguments);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-256")] // Services/Providers/CommandCodeAPI.swift:57-59
    public async Task AuthStatus_reads_the_name_from_stderr_too_and_it_wins_over_exit_status_3()
    {
        var script = new CommandCodeScript("stderr-name");
        script.SetStderr("Authenticated as bob\n");
        script.SetExitStatus(3);
        var home = CommandCodeScratch.NewHome();

        var status = await CommandCodeAPI.AuthStatus(script.Executable,
            Env(home, ("COMMAND_CODE_API_KEY", "k"), (script.EnvironmentEntry.Key, script.EnvironmentEntry.Value))).WaitAsync(Guard);

        Assert.Equal(new ProviderStatus.Auth.SignedIn("bob"), status);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-256")] // Services/Providers/CommandCodeAPI.swift:61-63
    public async Task AuthStatus_is_signed_out_for_exit_status_3_or_not_authenticated_or_not_logged_in_in_any_case()
    {
        var home = CommandCodeScratch.NewHome();
        var exit3 = new CommandCodeScript("exit-3");
        exit3.SetStdout("Checking...\n");
        exit3.SetExitStatus(3);
        var notAuthenticated = new CommandCodeScript("not-authenticated");
        notAuthenticated.SetStdout("NOT AUTHENTICATED. Run cmd login.\n");
        var notLoggedIn = new CommandCodeScript("not-logged-in");
        notLoggedIn.SetStderr("You are Not Logged In\n");
        notLoggedIn.SetExitStatus(1);

        foreach (var script in new[] { exit3, notAuthenticated, notLoggedIn })
        {
            var status = await CommandCodeAPI.AuthStatus(script.Executable,
                Env(home, ("COMMAND_CODE_API_KEY", "k"), (script.EnvironmentEntry.Key, script.EnvironmentEntry.Value))).WaitAsync(Guard);
            Assert.IsType<ProviderStatus.Auth.SignedOut>(status);
        }
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-256")] // Services/Providers/CommandCodeAPI.swift:65-67
    public async Task AuthStatus_authentication_verified_is_signed_in_with_the_stored_login_user_name_or_nil()
    {
        var withLogin = CommandCodeScratch.NewHome(authJson: """{"apiKey":"k","userName":"carol"}""");
        var withoutLogin = CommandCodeScratch.NewHome();
        var script = new CommandCodeScript("verified");
        script.SetStdout("Authentication Verified.\n");

        var fromStored = await CommandCodeAPI.AuthStatus(script.Executable,
            Env(withLogin, (script.EnvironmentEntry.Key, script.EnvironmentEntry.Value))).WaitAsync(Guard);
        var fromEnvironment = await CommandCodeAPI.AuthStatus(script.Executable,
            Env(withoutLogin, ("COMMAND_CODE_API_KEY", "k"), (script.EnvironmentEntry.Key, script.EnvironmentEntry.Value))).WaitAsync(Guard);

        Assert.Equal(new ProviderStatus.Auth.SignedIn("carol"), fromStored);
        Assert.Equal(new ProviderStatus.Auth.SignedIn(null), fromEnvironment);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-256")] // Services/Providers/CommandCodeAPI.swift:68
    public async Task AuthStatus_is_unknown_for_any_other_output()
    {
        var script = new CommandCodeScript("other");
        script.SetStdout("Something else happened\n");
        script.SetExitStatus(1);
        var home = CommandCodeScratch.NewHome();

        var status = await CommandCodeAPI.AuthStatus(script.Executable,
            Env(home, ("COMMAND_CODE_API_KEY", "k"), (script.EnvironmentEntry.Key, script.EnvironmentEntry.Value))).WaitAsync(Guard);

        Assert.IsType<ProviderStatus.Auth.Unknown>(status);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-256")] // Services/Providers/CommandCodeAPI.swift:54-56
    public async Task AuthStatus_is_unknown_never_signed_out_when_the_CLI_cannot_be_run()
    {
        var home = CommandCodeScratch.NewHome();
        var missing = CommandCodeScratch.NewFolder("missing") + "\\cmd.exe";
        var executable = SwiftURL.Create("marrow-native-path:" + missing.Replace('\\', '/'))!;

        var status = await CommandCodeAPI.AuthStatus(executable, Env(home, ("COMMAND_CODE_API_KEY", "k"))).WaitAsync(Guard);

        Assert.IsType<ProviderStatus.Auth.Unknown>(status);
    }

    // ---- PR-PROVCLI-257, UI-PROVCLI-024: planLimits ------------------------------------------------------------

    // Test double for the PORT-ONLY MoneyFormatter seam (CommandCodeAPI.swift:93). Services.Providers.ProviderCredits
    // is a sibling unit in this dependency cycle and ProviderCredits.Money is MARROW_STUB at this commit; this mirrors
    // ProviderCredits.swift:38-45's en_US currency formatting only for the round two-decimal USD amounts these tests
    // use, so PR-PROVCLI-257's own window-building logic (title concatenation, percent clamp, resetAt) can be proven
    // now. It is not a claim about ProviderCredits.Money's general behaviour (non-USD codes, non-round amounts).
    private static string TestMoney(double value, string currency) =>
        currency == "USD" ? value.ToString("$0.00", System.Globalization.CultureInfo.GetCultureInfo("en-US")) : throw new NotSupportedException();

    private static async Task<PlanLimits?> PlanLimitsAgainst(LoopbackHttpServer server, Dictionary<string, string> environment,
        (int Status, string Body) credits, (int Status, string Body) subscription)
    {
        using var route = CommandCodeAPI.FetchBaseURL.Override(server.Url("/"));
        using var money = CommandCodeAPI.MoneyFormatter.Override(TestMoney);
        var pending = CommandCodeAPI.PlanLimits(environment);
        var first = await server.NextExchange();
        var second = await server.NextExchange();
        foreach (var exchange in new[] { first, second })
        {
            var (status, body) = exchange.Target == "/alpha/billing/credits" ? credits : subscription;
            await exchange.Respond(status, status == 200 ? "OK" : "Error", Utf8(body), ("Content-Type", "application/json"));
        }
        return await pending.WaitAsync(Guard);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-257")] // Services/Providers/CommandCodeAPI.swift:79
    public async Task PlanLimits_is_nil_with_no_request_when_there_is_no_key()
    {
        using var runtime = new TestRuntime();
        await using var server = new LoopbackHttpServer();
        using var route = CommandCodeAPI.FetchBaseURL.Override(server.Url("/"));

        var limits = await CommandCodeAPI.PlanLimits(Env(CommandCodeScratch.NewHome())).WaitAsync(Guard);

        Assert.Null(limits);
        Assert.Equal(0, server.ConnectionCount);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-257")] // Services/Providers/CommandCodeAPI.swift:80-83, :168-173
    public async Task PlanLimits_fetches_credits_and_subscriptions_concurrently_with_bearer_auth_and_a_json_accept()
    {
        using var runtime = new TestRuntime();
        await using var server = new LoopbackHttpServer();
        using var route = CommandCodeAPI.FetchBaseURL.Override(server.Url("/"));
        var home = CommandCodeScratch.NewHome();

        var pending = CommandCodeAPI.PlanLimits(Env(home, ("COMMAND_CODE_API_KEY", " key-1 ")));
        // Both requests are in flight before either is answered: the two fetches run concurrently (async let).
        var first = await server.NextExchange();
        var second = await server.NextExchange();

        Assert.Equal(["/alpha/billing/credits", "/alpha/billing/subscriptions"], new[] { first.Target, second.Target }.Order(StringComparer.Ordinal));
        foreach (var exchange in new[] { first, second })
        {
            Assert.Equal("GET", exchange.Method);
            Assert.Equal(["Bearer key-1"], exchange.HeaderValues("Authorization"));
            Assert.Equal(["application/json"], exchange.HeaderValues("Accept"));
        }
        foreach (var exchange in new[] { first, second })
        {
            var body = exchange.Target == "/alpha/billing/credits" ? "{}" : """{"data":{"planId":"individual-go"}}""";
            await exchange.Respond(200, "OK", Utf8(body));
        }
        var limits = await pending.WaitAsync(Guard);
        Assert.Equal(new PlanLimits { PlanName = "Go", Windows = ValueList<PlanLimits.Window>.Empty }, limits);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-257")] // Services/Providers/CommandCodeAPI.swift:86-100
    [Trait("Req", "UI-PROVCLI-024")] // Services/Providers/CommandCodeAPI.swift:87-100
    public async Task PlanLimits_builds_the_fiveHour_then_weekly_windows_titled_with_the_USD_cap()
    {
        // NEEDS ProviderCredits.money (ProviderCredits.swift:38-45): en_US currency "$3.00" and "$16.00".
        using var runtime = new TestRuntime();
        await using var server = new LoopbackHttpServer();
        var home = CommandCodeScratch.NewHome();
        const string credits = """
            {"credits":{"monthlyCredits":16},
             "windowLimits":{"weekly":{"used":4,"cap":16,"resetAt":1767225600000},
                             "fiveHour":{"used":1.5,"cap":3,"resetAt":1767222000500}}}
            """;

        var limits = await PlanLimitsAgainst(server, Env(home, ("COMMAND_CODE_API_KEY", "k")),
            (200, credits), (200, """{"data":{"planId":"individual-pro"}}"""));

        var expected = new PlanLimits
        {
            PlanName = "Pro",
            Windows = new ValueList<PlanLimits.Window>(
            [
                new PlanLimits.Window { Id = "fiveHour", Title = "5-hour limit" + Dot + "$3.00", Percent = 50, ResetsAt = SwiftDate.From1970Seconds(1767222000.5) },
                new PlanLimits.Window { Id = "weekly", Title = "Weekly" + Dot + "$16.00", Percent = 25, ResetsAt = SwiftDate.From1970Seconds(1767225600) },
            ]),
        };
        Assert.Equal(expected, limits);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-257")] // Services/Providers/CommandCodeAPI.swift:88-94
    [Trait("Req", "UI-PROVCLI-024")] // Services/Providers/CommandCodeAPI.swift:88
    public async Task PlanLimits_skips_a_window_that_is_missing_null_or_has_no_positive_cap_and_clamps_only_at_100()
    {
        // NEEDS ProviderCredits.money (ProviderCredits.swift:38-45): en_US currency "$3.00".
        using var runtime = new TestRuntime();
        await using var server = new LoopbackHttpServer();
        var home = CommandCodeScratch.NewHome();
        const string credits = """
            {"windowLimits":{"fiveHour":{"used":7,"cap":3,"resetAt":0},"weekly":{"used":1,"cap":0}}}
            """;

        var limits = await PlanLimitsAgainst(server, Env(home, ("COMMAND_CODE_API_KEY", "k")), (200, credits), (404, "{}"));

        var window = Assert.Single(limits!.Windows);
        Assert.Equal(new PlanLimits.Window { Id = "fiveHour", Title = "5-hour limit" + Dot + "$3.00", Percent = 100, ResetsAt = null }, window);
        Assert.Null(limits.PlanName);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-257")] // Services/Providers/CommandCodeAPI.swift:88-90
    public async Task PlanLimits_used_defaults_to_zero_and_a_negative_resetAt_is_absent()
    {
        // NEEDS ProviderCredits.money (ProviderCredits.swift:38-45): en_US currency "$16.00".
        using var runtime = new TestRuntime();
        await using var server = new LoopbackHttpServer();
        var home = CommandCodeScratch.NewHome();
        const string credits = """{"windowLimits":{"fiveHour":null,"weekly":{"cap":16,"resetAt":-5}}}""";

        var limits = await PlanLimitsAgainst(server, Env(home, ("COMMAND_CODE_API_KEY", "k")), (200, credits), (200, "{}"));

        Assert.Equal(new PlanLimits
        {
            PlanName = null,
            Windows = new ValueList<PlanLimits.Window>([new PlanLimits.Window { Id = "weekly", Title = "Weekly" + Dot + "$16.00", Percent = 0, ResetsAt = null }]),
        }, limits);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-257")] // Services/Providers/CommandCodeAPI.swift:84
    public async Task PlanLimits_is_nil_when_the_credits_route_fails_whatever_the_subscription_says()
    {
        using var runtime = new TestRuntime();
        await using var server = new LoopbackHttpServer();
        var home = CommandCodeScratch.NewHome();

        var limits = await PlanLimitsAgainst(server, Env(home, ("COMMAND_CODE_API_KEY", "k")),
            (500, """{"windowLimits":{"fiveHour":{"used":1,"cap":3}}}"""), (200, """{"data":{"planId":"individual-max"}}"""));

        Assert.Null(limits);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-257")] // Services/Providers/CommandCodeAPI.swift:84, :172-174
    public async Task PlanLimits_is_nil_when_the_credits_body_is_not_json()
    {
        using var runtime = new TestRuntime();
        await using var server = new LoopbackHttpServer();
        var home = CommandCodeScratch.NewHome();

        var limits = await PlanLimitsAgainst(server, Env(home, ("COMMAND_CODE_API_KEY", "k")),
            (200, "<html>"), (200, """{"data":{"planId":"individual-max"}}"""));

        Assert.Null(limits);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-257")] // Services/Providers/CommandCodeAPI.swift:86, :99
    [Trait("Req", "UI-PROVCLI-024")] // Services/Providers/CommandCodeAPI.swift:99
    public async Task PlanLimits_is_nil_with_no_windows_and_no_plan_name()
    {
        using var runtime = new TestRuntime();
        await using var server = new LoopbackHttpServer();
        var home = CommandCodeScratch.NewHome();

        var limits = await PlanLimitsAgainst(server, Env(home, ("COMMAND_CODE_API_KEY", "k")),
            (200, """{"credits":{"monthlyCredits":3}}"""), (200, """{"data":{"planId":""}}"""));

        Assert.Null(limits);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-257")] // Services/Providers/CommandCodeAPI.swift:98-100
    public async Task PlanLimits_with_only_a_plan_name_reads_it_from_data_planId()
    {
        using var runtime = new TestRuntime();
        await using var server = new LoopbackHttpServer();
        var home = CommandCodeScratch.NewHome();

        var limits = await PlanLimitsAgainst(server, Env(home, ("COMMAND_CODE_API_KEY", "k")),
            (200, """{"windowLimits":null}"""), (200, """{"data":{"planId":"individual-ultra"}}"""));

        Assert.Equal(new PlanLimits { PlanName = "Max 20" + Times, Windows = ValueList<PlanLimits.Window>.Empty }, limits);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-257")] // Services/Providers/CommandCodeAPI.swift:169 (timeoutInterval: 15)
    public async Task PlanLimits_requests_time_out_after_15_seconds_and_read_as_nil()
    {
        using var runtime = new TestRuntime();
        await using var server = new LoopbackHttpServer();
        using var route = CommandCodeAPI.FetchBaseURL.Override(server.Url("/"));
        var home = CommandCodeScratch.NewHome();

        var pending = CommandCodeAPI.PlanLimits(Env(home, ("COMMAND_CODE_API_KEY", "k")));
        await server.NextExchange();
        await server.NextExchange();
        runtime.Advance(TimeSpan.FromMilliseconds(14_999));
        Assert.False(pending.IsCompleted);
        runtime.Advance(TimeSpan.FromMilliseconds(1));

        Assert.Null(await pending.WaitAsync(Guard));
    }

    // ---- PR-PROVCLI-258: credits ------------------------------------------------------------------------------

    private static async Task<ProviderCredits?> CreditsAgainst(LoopbackHttpServer server, int status, string body)
    {
        using var route = CommandCodeAPI.FetchBaseURL.Override(server.Url("/"));
        var pending = CommandCodeAPI.Credits("key-2");
        var exchange = await server.NextExchange();
        Assert.Equal("/alpha/billing/credits", exchange.Target);
        Assert.Equal(["Bearer key-2"], exchange.HeaderValues("Authorization"));
        Assert.Equal(["application/json"], exchange.HeaderValues("Accept"));
        await exchange.Respond(status, status == 200 ? "OK" : "Error", Utf8(body));
        return await pending.WaitAsync(Guard);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-258")] // Services/Providers/CommandCodeAPI.swift:111-124
    public async Task Credits_sum_monthly_purchased_and_free_dollars_in_USD()
    {
        using var runtime = new TestRuntime();
        await using var server = new LoopbackHttpServer();

        var credits = await CreditsAgainst(server, 200,
            """{"credits":{"monthlyCredits":10.5,"purchasedCredits":2,"freeCredits":0.25,"belowThreshold":false}}""");

        Assert.Equal(new ProviderCredits { Total = 12.75, Currency = "USD", ToppedUp = 2, Granted = 0.25, Monthly = 10.5, IsUsable = true }, credits);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-258")] // Services/Providers/CommandCodeAPI.swift:114-116, :123
    public async Task Credits_missing_amounts_count_as_zero_and_a_missing_belowThreshold_is_usable()
    {
        using var runtime = new TestRuntime();
        await using var server = new LoopbackHttpServer();

        var credits = await CreditsAgainst(server, 200, """{"credits":{"purchasedCredits":"5"}}""");

        Assert.Equal(new ProviderCredits { Total = 0, Currency = "USD", ToppedUp = 0, Granted = 0, Monthly = 0, IsUsable = true }, credits);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-258")] // Services/Providers/CommandCodeAPI.swift:123
    public async Task Credits_are_unusable_only_when_belowThreshold_is_exactly_true()
    {
        using var runtime = new TestRuntime();
        await using var server = new LoopbackHttpServer();

        var below = await CreditsAgainst(server, 200, """{"credits":{"monthlyCredits":1,"belowThreshold":true}}""");
        var notBool = await CreditsAgainst(server, 200, """{"credits":{"monthlyCredits":1,"belowThreshold":"true"}}""");

        Assert.False(below!.IsUsable);
        Assert.True(notBool!.IsUsable);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-258")] // Services/Providers/CommandCodeAPI.swift:112-113, :172-173
    public async Task Credits_are_nil_for_a_missing_or_null_credits_object_a_non_200_or_a_body_that_is_not_json()
    {
        using var runtime = new TestRuntime();
        await using var server = new LoopbackHttpServer();

        Assert.Null(await CreditsAgainst(server, 200, """{"windowLimits":{}}"""));
        Assert.Null(await CreditsAgainst(server, 200, """{"credits":null}"""));
        Assert.Null(await CreditsAgainst(server, 401, """{"credits":{"monthlyCredits":1}}"""));
        Assert.Null(await CreditsAgainst(server, 200, "not json"));
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-258")] // Services/Providers/CommandCodeAPI.swift:106-108
    public async Task Credits_for_an_environment_are_nil_with_no_request_when_there_is_no_key()
    {
        using var runtime = new TestRuntime();
        await using var server = new LoopbackHttpServer();
        using var route = CommandCodeAPI.FetchBaseURL.Override(server.Url("/"));

        var credits = await CommandCodeAPI.Credits(Env(CommandCodeScratch.NewHome())).WaitAsync(Guard);

        Assert.Null(credits);
        Assert.Equal(0, server.ConnectionCount);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-258")] // Services/Providers/CommandCodeAPI.swift:106-108
    public async Task Credits_for_an_environment_use_the_resolved_key()
    {
        using var runtime = new TestRuntime();
        await using var server = new LoopbackHttpServer();
        using var route = CommandCodeAPI.FetchBaseURL.Override(server.Url("/"));
        var home = CommandCodeScratch.NewHome(authJson: """{"apiKey":"stored-9"}""");

        var pending = CommandCodeAPI.Credits(Env(home));
        var exchange = await server.NextExchange();
        Assert.Equal(["Bearer stored-9"], exchange.HeaderValues("Authorization"));
        await exchange.Respond(200, "OK", Utf8("""{"credits":{"freeCredits":4}}"""));

        Assert.Equal(new ProviderCredits { Total = 4, Currency = "USD", ToppedUp = 0, Granted = 4, Monthly = 0, IsUsable = true },
            await pending.WaitAsync(Guard));
    }

    // ---- PR-PROVCLI-259: planName -----------------------------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-PROVCLI-259")] // Services/Providers/CommandCodeAPI.swift:132-141, :148-149
    public void PlanName_maps_the_eight_known_ids_case_insensitively()
    {
        Assert.Equal("Go", CommandCodeAPI.PlanName("individual-go"));
        Assert.Equal("GOAT", CommandCodeAPI.PlanName("individual-goat"));
        Assert.Equal("Pro", CommandCodeAPI.PlanName("individual-pro"));
        Assert.Equal("Pro", CommandCodeAPI.PlanName("individual-pro-v1"));
        Assert.Equal("Provider", CommandCodeAPI.PlanName("individual-provider"));
        Assert.Equal("Max 10" + Times, CommandCodeAPI.PlanName("individual-max"));
        Assert.Equal("Max 20" + Times, CommandCodeAPI.PlanName("individual-ultra"));
        Assert.Equal("Teams Pro", CommandCodeAPI.PlanName("teams-pro"));
        Assert.Equal("GOAT", CommandCodeAPI.PlanName("INDIVIDUAL-GOAT"));
        Assert.Equal("Max 20" + Times, CommandCodeAPI.PlanName("Individual-Ultra"));
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-259")] // Services/Providers/CommandCodeAPI.swift:148, :152
    public void PlanName_is_nil_for_nil_empty_or_a_bare_individual()
    {
        Assert.Null(CommandCodeAPI.PlanName(null));
        Assert.Null(CommandCodeAPI.PlanName(""));
        Assert.Null(CommandCodeAPI.PlanName("individual"));
        Assert.Null(CommandCodeAPI.PlanName("Individual-"));
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-259")] // Services/Providers/CommandCodeAPI.swift:150-165
    public void PlanName_reads_an_unfamiliar_id_out_of_its_own_words()
    {
        Assert.Equal("Max 20" + Times, CommandCodeAPI.PlanName("individual-max-20x"));
        Assert.Equal("Teams Ultra", CommandCodeAPI.PlanName("teams-ultra"));
        Assert.Equal("Teams Enterprise", CommandCodeAPI.PlanName("team-enterprise"));
        Assert.Equal("Starter", CommandCodeAPI.PlanName("individual-starter"));
        Assert.Equal("Pro Plus", CommandCodeAPI.PlanName("Individual-Pro-Plus"));
        Assert.Equal("Go GOAT", CommandCodeAPI.PlanName("go-goat"));
        Assert.Equal("Enterprise", CommandCodeAPI.PlanName("enterprise"));
        Assert.Equal("Max 5" + Times + " Beta", CommandCodeAPI.PlanName("max-5x-beta"));
        // The multiplier pattern is matched on the part as written (:162), so an upper-case X is not a multiplier.
        Assert.Equal("Max 20X", CommandCodeAPI.PlanName("individual-max-20X"));
        Assert.Equal("X20", CommandCodeAPI.PlanName("x20"));
        Assert.Equal("Pro Pro", CommandCodeAPI.PlanName("pro--PRO"));
    }

    // ---- PR-PROVCLI-260: listModels and parseModels -----------------------------------------------------------

    private const string Listing = """
        Available models:

        Anthropic
          claude-sonnet-5              best combo of speed & intelligence (recommended)
          claude-opus-5                most intelligent Opus for agents and coding
        Open Source
          deepseek/deepseek-v4-flash   fast hybrid-attention reasoning (default)
          moonshotai/kimi-k3           long-horizon coding
        Pass the full id with --model to pick one.
        cmd --model <id>
        Usage: cmd [options]
        Docs: https://commandcode.ai/docs
        """;

    [Fact]
    [Trait("Req", "PR-PROVCLI-260")] // Services/Providers/CommandCodeAPI.swift:194-224, :230-232
    public void ParseModels_reads_rows_under_their_section_and_keeps_the_first_default_only()
    {
        var models = CommandCodeAPI.ParseModels(Listing, configuredModel: null, configuredEfforts: null);

        Assert.Equal(new ValueList<ModelOption>(
        [
            new ModelOption("claude-sonnet-5", "Claude Sonnet 5", detail: "Anthropic" + Dot + "best combo of speed & intelligence", efforts: Full, isDefault: true),
            new ModelOption("claude-opus-5", "Claude Opus 5", detail: "Anthropic" + Dot + "most intelligent Opus for agents and coding", efforts: Full),
            new ModelOption("deepseek/deepseek-v4-flash", "Deepseek V4 Flash", detail: "Open Source" + Dot + "fast hybrid-attention reasoning", efforts: HighMax),
            new ModelOption("moonshotai/kimi-k3", "Kimi K3", detail: "Open Source" + Dot + "long-horizon coding", efforts: LowHighMax),
        ]), models);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-260")] // Services/Providers/CommandCodeAPI.swift:219, :222, :226-229
    public void ParseModels_moves_the_configured_model_first_as_the_only_default_with_its_configured_effort()
    {
        var efforts = JSONValue.Parse("""{"moonshotai/kimi-k3":"high","kimi-k3":"low","claude-opus-5":7}""");

        var models = CommandCodeAPI.ParseModels(Listing, configuredModel: "KIMI-K3", configuredEfforts: efforts);

        Assert.Equal(new ValueList<ModelOption>(
        [
            new ModelOption("moonshotai/kimi-k3", "Kimi K3", detail: "Open Source" + Dot + "long-horizon coding", efforts: LowHighMax, defaultEffort: "high", isDefault: true),
            new ModelOption("claude-sonnet-5", "Claude Sonnet 5", detail: "Anthropic" + Dot + "best combo of speed & intelligence", efforts: Full),
            new ModelOption("claude-opus-5", "Claude Opus 5", detail: "Anthropic" + Dot + "most intelligent Opus for agents and coding", efforts: Full),
            new ModelOption("deepseek/deepseek-v4-flash", "Deepseek V4 Flash", detail: "Open Source" + Dot + "fast hybrid-attention reasoning", efforts: HighMax),
        ]), models);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-260")] // Services/Providers/CommandCodeAPI.swift:230-232
    public void ParseModels_with_a_configured_model_that_matches_nothing_keeps_the_order_and_the_first_default()
    {
        var models = CommandCodeAPI.ParseModels(Listing, configuredModel: "gpt-9", configuredEfforts: null);

        Assert.Equal(["claude-sonnet-5", "claude-opus-5", "deepseek/deepseek-v4-flash", "moonshotai/kimi-k3"], models.Select(m => m.Id));
        Assert.Equal([true, false, false, false], models.Select(m => m.IsDefault));
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-260")] // Services/Providers/CommandCodeAPI.swift:212-213, :220, :230-232
    public void ParseModels_strips_recommended_and_default_and_either_one_marks_the_row()
    {
        const string text = """
            Mixed
              alpha-1  plain row
              beta-2   both tags (recommended) (default)
              gamma-3  cli default (default) here
            """;

        var models = CommandCodeAPI.ParseModels(text, configuredModel: null, configuredEfforts: null);

        Assert.Equal(["Mixed" + Dot + "plain row", "Mixed" + Dot + "both tags", "Mixed" + Dot + "cli default here"], models.Select(m => m.Detail));
        Assert.Equal([false, true, false], models.Select(m => m.IsDefault));
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-260")] // Services/Providers/CommandCodeAPI.swift:209-213, :217
    public void ParseModels_a_tag_with_no_space_before_it_stays_in_the_detail_and_no_section_leaves_the_detail_alone()
    {
        const string text = """
              solo-1  (recommended)
              solo-2  lone detail
            """;

        var models = CommandCodeAPI.ParseModels(text, configuredModel: null, configuredEfforts: null);

        Assert.Equal(["(recommended)", "lone detail"], models.Select(m => m.Detail));
        Assert.Equal([false, false], models.Select(m => m.IsDefault));
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-260")] // Services/Providers/CommandCodeAPI.swift:202-208
    public void ParseModels_needs_two_spaces_and_an_id_with_a_hyphen_or_slash()
    {
        const string text = "Header\n  one-space-only row with a long detail\n  gemini  no hyphen or slash\n  vendor/model  slash is enough\n  tabbed-id\t\tdetail after tabs\n";

        var models = CommandCodeAPI.ParseModels(text, configuredModel: null, configuredEfforts: null);

        Assert.Equal(["vendor/model", "tabbed-id"], models.Select(m => m.Id));
        Assert.Equal(["Header" + Dot + "slash is enough", "Header" + Dot + "detail after tabs"], models.Select(m => m.Detail));
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-260")] // Services/Providers/CommandCodeAPI.swift:202-205
    public void ParseModels_a_non_row_line_is_a_section_when_it_is_one_capitalised_word_or_shorter_than_24_characters()
    {
        const string text = """
            ThisIsOneLongCapitalisedHeaderWord
              a-1  first
            thisisonelonglowercaseheaderword
              b-2  second
            short lower header
              c-3  third
            this lowercase header has spaces and is long
              d-4  fourth
            """;

        var models = CommandCodeAPI.ParseModels(text, configuredModel: null, configuredEfforts: null);

        Assert.Equal(
        [
            "ThisIsOneLongCapitalisedHeaderWord" + Dot + "first",
            "ThisIsOneLongCapitalisedHeaderWord" + Dot + "second",
            "short lower header" + Dot + "third",
            "short lower header" + Dot + "fourth",
        ], models.Select(m => m.Detail));
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-260")] // Services/Providers/CommandCodeAPI.swift:197-201
    public void ParseModels_skips_blank_and_boilerplate_lines_and_splits_on_every_newline_kind()
    {
        const string text = "Available models (3)\r\nPass the full id\r\ncmd --list-models\r\nDocs: x\r\nUsage cmd\r\n\r\nA\r\n  x-1   one\r\n  y-2   two\n";

        var models = CommandCodeAPI.ParseModels(text, configuredModel: null, configuredEfforts: null);

        Assert.Equal(["x-1", "y-2"], models.Select(m => m.Id));
        Assert.Equal(["A" + Dot + "one", "A" + Dot + "two"], models.Select(m => m.Detail));
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-260")] // Services/Providers/CommandCodeAPI.swift:194-233
    public void ParseModels_of_nothing_is_empty()
    {
        Assert.Empty(CommandCodeAPI.ParseModels("", configuredModel: null, configuredEfforts: null));
        Assert.Empty(CommandCodeAPI.ParseModels("Available models:\nAnthropic\n", configuredModel: "claude-sonnet-5", configuredEfforts: null));
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-260")] // Services/Providers/CommandCodeAPI.swift:184-191
    public async Task ListModels_runs_list_models_no_auto_update_strips_ANSI_and_reads_the_CLI_config()
    {
        var script = new CommandCodeScript("list");
        script.SetStdout("Anthropic\n  " + Esc + "[36mclaude-opus-5" + Esc + "[0m  most intelligent\n  claude-sonnet-5  best (recommended)\n");
        script.SetStderr("warning  ignored-row  never parsed\n");
        var home = CommandCodeScratch.NewHome(configJson: """{"model":"claude-opus-5","reasoningEffort":{"claude-opus-5":"max"}}""");

        var models = await CommandCodeAPI.ListModels(script.Executable,
            Env(home, (script.EnvironmentEntry.Key, script.EnvironmentEntry.Value))).WaitAsync(Guard);

        Assert.Equal(new ValueList<ModelOption>(
        [
            new ModelOption("claude-opus-5", "Claude Opus 5", detail: "Anthropic" + Dot + "most intelligent", efforts: Full, defaultEffort: "max", isDefault: true),
            new ModelOption("claude-sonnet-5", "Claude Sonnet 5", detail: "Anthropic" + Dot + "best", efforts: Full),
        ]), models);
        var call = Assert.Single(script.Calls());
        Assert.Equal(["--list-models", "--no-auto-update"], call.Arguments);
    }

    // The listing as the Swift doc comment (CommandCodeAPI.swift:171-174) and the grammar of :197-221 describe it: section headers, then
    // `<id>  <description>` rows, two tagged. OD-0098 (PR-PROVCLI-260): no frame or capture of a Command Code listing exists, so the
    // grammar the Swift states is the proof, and what a Windows console adds to that text (CRLF, colour codes, padding, a byte-order
    // mark, a leading blank line) must leave the parsed options as they are.
    private static ValueList<ModelOption> ListingOptions() => new(
    [
        new ModelOption("claude-sonnet-5", "Claude Sonnet 5", detail: "Anthropic" + Dot + "best combo of speed & intelligence", efforts: Full, isDefault: true),
        new ModelOption("claude-opus-5", "Claude Opus 5", detail: "Anthropic" + Dot + "most intelligent Opus for agents and coding", efforts: Full),
        new ModelOption("deepseek/deepseek-v4-flash", "Deepseek V4 Flash", detail: "Open Source" + Dot + "fast hybrid-attention reasoning", efforts: HighMax),
        new ModelOption("moonshotai/kimi-k3", "Kimi K3", detail: "Open Source" + Dot + "long-horizon coding", efforts: LowHighMax),
    ]);

    private static async Task<ValueList<ModelOption>> ListFrom(string stdout, string label)
    {
        var script = new CommandCodeScript(label);
        script.SetStdout(stdout);
        var home = CommandCodeScratch.NewHome();
        return await CommandCodeAPI.ListModels(script.Executable,
            Env(home, (script.EnvironmentEntry.Key, script.EnvironmentEntry.Value))).WaitAsync(Guard);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-260")] // CommandCodeAPI.swift:197 text.components(separatedBy: .newlines): a CRLF listing parses to the same options; OD-0098
    public async Task ListModels_reads_the_same_options_from_a_listing_with_CRLF_line_endings()
    {
        var lf = await ListFrom(Listing, "list-lf");
        var crlf = await ListFrom(Listing.Replace("\n", "\r\n", StringComparison.Ordinal), "list-crlf");

        Assert.Equal(ListingOptions(), lf);
        Assert.Equal(lf, crlf);
        Assert.Single(crlf, option => option.IsDefault); // one default only (:225-232)
        Assert.Equal(["Anthropic", "Anthropic", "Open Source", "Open Source"], crlf.Select(option => option.Detail!.Split(Dot)[0]));
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-260")] // CommandCodeAPI.swift:185 TextCleanup.stripANSI(result.output): colour codes around ids and headers leave the options as they are; OD-0098
    public async Task ListModels_reads_the_same_options_from_a_listing_with_ANSI_colour_codes_around_ids_and_headers()
    {
        var colored = string.Join("\n",
            "Available models:",
            "",
            Esc + "[1m" + Esc + "[34mAnthropic" + Esc + "[0m",
            "  " + Esc + "[36mclaude-sonnet-5" + Esc + "[0m              best combo of speed & intelligence " + Esc + "[2m(recommended)" + Esc + "[0m",
            "  " + Esc + "[36mclaude-opus-5" + Esc + "[0m                most intelligent Opus for agents and coding",
            Esc + "[1m" + Esc + "[34mOpen Source" + Esc + "[0m",
            "  " + Esc + "[36mdeepseek/deepseek-v4-flash" + Esc + "[0m   fast hybrid-attention reasoning " + Esc + "[2m(default)" + Esc + "[0m",
            "  " + Esc + "[36mmoonshotai/kimi-k3" + Esc + "[0m           long-horizon coding",
            "Pass the full id with --model to pick one.",
            "cmd --model <id>",
            "Usage: cmd [options]",
            "Docs: https://commandcode.ai/docs") + "\n";

        Assert.Equal(ListingOptions(), await ListFrom(colored, "list-ansi"));
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-260")] // CommandCodeAPI.swift:198 trimmingCharacters(in: .whitespaces), :202 the two-space column split: console padding and a leading UTF-8 byte-order mark leave the options as they are; OD-0098
    public async Task ListModels_reads_the_same_options_from_a_listing_with_console_trailing_spaces_and_a_leading_byte_order_mark()
    {
        // A Windows console pads every line to its width with spaces, and a UTF-8 file or pipe can start with U+FEFF.
        var padded = "﻿" + string.Join("\r\n", Listing.Split('\n').Select(line => line + new string(' ', 120 - line.Length))) + "\r\n";

        Assert.Equal(ListingOptions(), await ListFrom(padded, "list-padded"));
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-260")] // CommandCodeAPI.swift:199 guard !line.isEmpty: leading blank lines are skipped; OD-0098
    public async Task ListModels_reads_the_same_options_from_a_listing_with_a_leading_blank_line()
    {
        Assert.Equal(ListingOptions(), await ListFrom("\n\r\n" + Listing, "list-blank"));
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-260")] // Services/Providers/CommandCodeAPI.swift:188-190
    public async Task ListModels_with_no_rows_throws_failed_with_the_last_lines_of_stderr_then_stdout()
    {
        var script = new CommandCodeScript("empty");
        script.SetStdout("Available models:\n");
        script.SetStderr("error: " + Esc + "[31mnot signed in" + Esc + "[0m\n");
        var home = CommandCodeScratch.NewHome();

        var error = await Assert.ThrowsAsync<ProviderError>(() => CommandCodeAPI.ListModels(script.Executable,
            Env(home, (script.EnvironmentEntry.Key, script.EnvironmentEntry.Value))).WaitAsync(Guard));

        Assert.Equal(new ProviderError.Case.Failed("error: not signed in\nAvailable models:"), error.ErrorCase);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-260")] // Services/Providers/CommandCodeAPI.swift:189
    public async Task ListModels_with_no_output_at_all_throws_Command_Code_listed_no_models()
    {
        var script = new CommandCodeScript("silent");
        var home = CommandCodeScratch.NewHome();

        var error = await Assert.ThrowsAsync<ProviderError>(() => CommandCodeAPI.ListModels(script.Executable,
            Env(home, (script.EnvironmentEntry.Key, script.EnvironmentEntry.Value))).WaitAsync(Guard));

        Assert.Equal(new ProviderError.Case.Failed("Command Code listed no models."), error.ErrorCase);
    }

    // ---- PR-PROVCLI-261: matches ------------------------------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-PROVCLI-261")] // Services/Providers/CommandCodeAPI.swift:237-243
    public void Matches_compares_lowercased_full_ids_then_the_names_after_the_last_slash()
    {
        Assert.True(CommandCodeAPI.Matches("moonshotai/kimi-k3", "kimi-k3"));
        Assert.True(CommandCodeAPI.Matches("kimi-k3", "moonshotai/kimi-k3"));
        Assert.True(CommandCodeAPI.Matches("MoonshotAI/Kimi-K3", "moonshotai/kimi-k3"));
        Assert.True(CommandCodeAPI.Matches("a/b/model-x", "other/MODEL-X"));
        Assert.True(CommandCodeAPI.Matches("vendor/model-y/", "model-y"));
        Assert.False(CommandCodeAPI.Matches("moonshotai/kimi-k3", "kimi-k2"));
        Assert.False(CommandCodeAPI.Matches("claude-opus-5", "claude-opus"));
    }

    // ---- PR-PROVCLI-262: displayName --------------------------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-PROVCLI-262")] // Services/Providers/CommandCodeAPI.swift:249-279
    public void DisplayName_drops_the_vendor_merges_version_digits_and_special_cases_known_tokens()
    {
        Assert.Equal("Claude Sonnet 5", CommandCodeAPI.DisplayName("claude-sonnet-5"));
        Assert.Equal("Claude Fable 5.1", CommandCodeAPI.DisplayName("claude-fable-5-1"));
        Assert.Equal("Kimi K2.7 Code", CommandCodeAPI.DisplayName("moonshotai/kimi-k2.7-code"));
        Assert.Equal("GPT-5.6 Terra", CommandCodeAPI.DisplayName("gpt-5.6-terra"));
        Assert.Equal("GPT-6 Astra", CommandCodeAPI.DisplayName("gpt-6-astra"));
        Assert.Equal("GPT", CommandCodeAPI.DisplayName("gpt"));
        Assert.Equal("GLM 5.3", CommandCodeAPI.DisplayName("zai-org/glm-5.3"));
        Assert.Equal("HY4 Preview", CommandCodeAPI.DisplayName("tencent/hy4-preview"));
        Assert.Equal("HY3 Paid", CommandCodeAPI.DisplayName("tencent/hy3-paid"));
        Assert.Equal("Deepseek V4.1 Flash", CommandCodeAPI.DisplayName("deepseek/deepseek-v4.1-flash"));
        Assert.Equal("Mimo V2.5 Pro", CommandCodeAPI.DisplayName("xiaomi/mimo-v2.5-pro"));
        Assert.Equal("Nemotron 3 Ultra 550b A55b", CommandCodeAPI.DisplayName("nvidia/nemotron-3-ultra-550b-a55b"));
        Assert.Equal("Qwen3.8 Max 0902", CommandCodeAPI.DisplayName("qwen/qwen3.8-max-0902"));
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-262")] // Services/Providers/CommandCodeAPI.swift:250-252, :278
    public void DisplayName_cuts_at_the_first_colon_and_a_free_tag_adds_Free()
    {
        Assert.Equal("Longcat 2.0 Free", CommandCodeAPI.DisplayName("meituan/longcat-2.0:free"));
        Assert.Equal("Ling 3.0 Flash Sante Free", CommandCodeAPI.DisplayName("inclusionai/ling-3.0-flash-sante:free"));
        Assert.Equal("GPT-5 Free", CommandCodeAPI.DisplayName("gpt-5:beta:free"));
        Assert.Equal("Model X", CommandCodeAPI.DisplayName("vendor/model-x:paid"));
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-262")] // Services/Providers/CommandCodeAPI.swift:255-263
    public void DisplayName_merges_each_adjacent_all_digit_pair_left_to_right()
    {
        Assert.Equal("Claude Haiku 4.5 20251001", CommandCodeAPI.DisplayName("claude-haiku-4-5-20251001"));
        Assert.Equal("A 1.2 3", CommandCodeAPI.DisplayName("a-1-2-3"));
        Assert.Equal("A 1.2 X 3.4", CommandCodeAPI.DisplayName("a-1-2-x-3-4"));
    }

    // ---- PR-PROVCLI-263: efforts ------------------------------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-PROVCLI-263")] // Services/Providers/CommandCodeAPI.swift:283-309
    public void Efforts_transcribe_the_CLI_table_and_every_other_id_takes_none()
    {
        var table = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["claude-sonnet-5"] = Full, ["claude-sonnet-4-6"] = Full, ["claude-fable-5-1"] = Full, ["claude-fable-5"] = Full,
            ["claude-opus-5-5"] = Full, ["claude-opus-5"] = Full, ["claude-opus-4-8"] = Full, ["claude-opus-4-7"] = Full,
            ["gpt-6-astra"] = Full, ["gpt-5.6-sol"] = Full, ["gpt-5.6-terra"] = Full, ["gpt-5.6-luna"] = Full,
            ["gpt-5.5"] = ToXHigh, ["gpt-5.4"] = ToXHigh, ["gpt-5.3-codex"] = ToXHigh, ["gpt-5.4-mini"] = ToHigh,
            ["deepseek/deepseek-v4-pro"] = HighMax, ["deepseek/deepseek-v4-flash"] = HighMax,
            ["deepseek/deepseek-v4-flash-vision-exp"] = HighMax, ["deepseek/deepseek-v4-flash-fast"] = LowHighMax,
            ["deepseek/deepseek-v4.1-flash"] = LowHighMax,
            ["moonshotai/kimi-k3"] = LowHighMax, ["zai-org/glm-5.3"] = LowHighMax, ["z-ai/glm-5.3-flash"] = LowHighMax,
            ["zai-org/glm-5.2"] = HighMax,
            ["google/gemini-3.8-flash"] = ToHigh, ["google/gemini-3.7-flash"] = ToHigh, ["google/gemini-3.6-flash"] = ToHigh,
            ["google/gemini-3.5-flash"] = ToHigh, ["google/gemini-3.5-flash-lite"] = ToHigh, ["google/gemini-3.1-flash-lite"] = ToHigh,
            ["tencent/hy4-preview"] = ToHigh, ["sakana/fugu-ultra"] = ["high", "xhigh"],
            ["xai/grok-4.5"] = ToHigh, ["xai/grok-4.6"] = ToXHigh,
            ["qwen/qwen3.8-max-0902"] = ["low", "medium", "xhigh"], ["qwen/qwen3.8-max"] = ["low", "medium", "xhigh"],
            ["qwen/qwen3.8-27b"] = ["low", "medium", "xhigh"], ["qwen/qwen3.8-flash"] = ["low", "medium", "xhigh"],
            ["meta/muse-spark-1.1"] = ToXHigh, ["meta/muse-spark-1.2"] = ToXHigh, ["meta/muse-spark-1.2-contributor"] = ToXHigh,
            ["meta/muse-spark-1.3"] = Full, ["meta/muse-spark-1.3-contributor"] = ToXHigh,
            ["minimaxai/minimax-m3"] = ToHigh, ["minimax/minimax-m3-free"] = ToHigh,
        };
        Assert.Equal(46, table.Count);

        foreach (var (id, efforts) in table)
        {
            Assert.Equal(efforts, CommandCodeAPI.Efforts(id));
        }
        Assert.Equal(Full, CommandCodeAPI.Efforts("CLAUDE-OPUS-5-5"));
        Assert.Equal(Full, CommandCodeAPI.Efforts("CLAUDE-SONNET-5"));
        Assert.Equal(LowHighMax, CommandCodeAPI.Efforts("MoonshotAI/Kimi-K3"));
        Assert.Empty(CommandCodeAPI.Efforts("claude-haiku-4-5"));
        Assert.Empty(CommandCodeAPI.Efforts("kimi-k3"));
        Assert.Empty(CommandCodeAPI.Efforts(""));
    }

    // ---- PR-PROVCLI-264: contextWindow ------------------------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-PROVCLI-264")] // Services/Providers/CommandCodeAPI.swift:313-343
    public void ContextWindow_transcribes_the_CLI_table_with_a_lowercased_lookup()
    {
        var table = new Dictionary<string, long>(StringComparer.Ordinal)
        {
            ["claude-sonnet-5"] = 1_000_000, ["claude-sonnet-4-6"] = 1_000_000, ["claude-fable-5-1"] = 1_000_000, ["claude-fable-5"] = 1_000_000,
            ["claude-opus-5-5"] = 1_000_000, ["claude-opus-5"] = 1_000_000, ["claude-opus-4-8"] = 1_000_000, ["claude-opus-4-7"] = 1_000_000, ["claude-haiku-4-5"] = 200_000,
            ["claude-haiku-4-5-20251001"] = 200_000,
            ["gpt-6-astra"] = 1_050_000, ["gpt-5.6-sol"] = 1_050_000, ["gpt-5.6-terra"] = 1_050_000, ["gpt-5.6-luna"] = 1_050_000,
            ["gpt-5.5"] = 400_000, ["gpt-5.4"] = 400_000, ["gpt-5.3-codex"] = 400_000, ["gpt-5.4-mini"] = 400_000,
            ["deepseek/deepseek-v4-pro"] = 1_000_000, ["deepseek/deepseek-v4-flash"] = 1_000_000, ["deepseek/deepseek-v4-flash-vision-exp"] = 1_000_000,
            ["deepseek/deepseek-v4-flash-fast"] = 1_000_000, ["deepseek/deepseek-v4.1-flash"] = 1_000_000,
            ["moonshotai/kimi-k3"] = 1_000_000, ["moonshotai/kimi-k2.7-code"] = 256_000, ["moonshotai/kimi-k2.7-code-highspeed"] = 262_000,
            ["moonshotai/kimi-k2.6"] = 256_000, ["moonshotai/kimi-k2.5"] = 256_000,
            ["zai-org/glm-5.3"] = 1_000_000, ["z-ai/glm-5.3-flash"] = 1_048_576, ["zai-org/glm-5.2"] = 1_000_000, ["zai-org/glm-5.2-fast"] = 1_000_000,
            ["zai-org/glm-5"] = 200_000, ["zai-org/glm-5.1"] = 200_000,
            ["minimaxai/minimax-m3"] = 1_000_000, ["minimaxai/minimax-m2.7"] = 197_000, ["minimaxai/minimax-m2.5"] = 200_000,
            ["xiaomi/mimo-v2.5-pro"] = 1_000_000, ["xiaomi/mimo-v2.5"] = 1_000_000,
            ["qwen/qwen3.7-max"] = 1_000_000, ["qwen/qwen3.7-plus"] = 1_000_000, ["qwen/qwen3.8-max-0902"] = 1_000_000, ["qwen/qwen3.8-max"] = 1_000_000,
            ["qwen/qwen3.8-27b"] = 262_144, ["qwen/qwen3.8-flash"] = 1_000_000, ["qwen/qwen3.7-flash"] = 1_000_000,
            ["meituan/longcat-2.0:free"] = 1_048_576, ["stepfun/step-3.7-flash"] = 256_000, ["stepfun/step-3.5-flash"] = 1_000_000,
            ["tencent/hy4-preview"] = 1_048_576, ["tencent/hy3-paid"] = 262_144,
            ["google/gemini-3.5-flash"] = 1_000_000, ["google/gemini-3.8-flash"] = 1_000_000, ["google/gemini-3.7-flash"] = 1_048_576,
            ["google/gemini-3.6-flash"] = 1_000_000, ["google/gemini-3.5-flash-lite"] = 1_000_000, ["google/gemini-3.1-flash-lite"] = 1_000_000,
            ["thinkingmachines/inkling"] = 256_000, ["thinkingmachines/inkling-small"] = 1_000_000,
            ["poolside/laguna-s-2.1-free"] = 256_000, ["inclusionai/ling-3.0-flash-sante:free"] = 262_144,
            ["sakana/fugu-ultra"] = 1_000_000, ["xai/grok-4.5"] = 500_000, ["xai/grok-4.6"] = 500_000,
            ["meta/muse-spark-1.1"] = 1_048_576, ["meta/muse-spark-1.2"] = 1_048_576, ["meta/muse-spark-1.2-contributor"] = 1_048_576,
            ["meta/muse-spark-1.3"] = 1_048_576, ["meta/muse-spark-1.3-contributor"] = 1_048_576,
            ["nvidia/nemotron-3-ultra-550b-a55b"] = 1_000_000,
        };
        Assert.Equal(70, table.Count);

        foreach (var (id, window) in table)
        {
            Assert.Equal(window, CommandCodeAPI.ContextWindow(id));
        }
        Assert.Equal(1_000_000, CommandCodeAPI.ContextWindow("Claude-Opus-5-5"));
        Assert.Equal(200_000, CommandCodeAPI.ContextWindow("Claude-Haiku-4-5"));
        Assert.Equal(262_144, CommandCodeAPI.ContextWindow("TENCENT/HY3-PAID"));
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-264")] // Services/Providers/CommandCodeAPI.swift:314, :342-344
    public void ContextWindow_is_nil_for_nil_and_otherwise_falls_back_to_a_short_name_match()
    {
        // Every key's name after its last slash is unique in the table, so each fallback below has exactly one
        // matching key and the unordered iteration at :344 cannot change the answer.
        Assert.Null(CommandCodeAPI.ContextWindow(null));
        Assert.Equal(1_000_000, CommandCodeAPI.ContextWindow("kimi-k3"));
        Assert.Equal(1_048_576, CommandCodeAPI.ContextWindow("Longcat-2.0:free"));
        Assert.Equal(200_000, CommandCodeAPI.ContextWindow("someone/claude-haiku-4-5"));
        Assert.Equal(262_000, CommandCodeAPI.ContextWindow("other/kimi-k2.7-code-highspeed"));
        Assert.Null(CommandCodeAPI.ContextWindow("unknown/model-z"));
        Assert.Null(CommandCodeAPI.ContextWindow(""));
    }

    // ---- PR-PROVCLI-265, UI-PROVCLI-019: seed -----------------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-PROVCLI-265")] // Services/Providers/CommandCodeAPI.swift:349-361
    [Trait("Req", "UI-PROVCLI-019")] // Services/Providers/CommandCodeAPI.swift:349-361
    public void Seed_is_the_eleven_rows_in_order_byte_for_byte_with_only_the_first_default()
    {
        var expected = new ValueList<ModelOption>(
        [
            new ModelOption("claude-sonnet-5", "Claude Sonnet 5", detail: "Anthropic" + Dot + "best combo of speed & intelligence", efforts: Full, isDefault: true),
            new ModelOption("claude-fable-5-1", "Claude Fable 5.1", detail: "Anthropic" + Dot + "most capable for demanding reasoning & long-horizon agents", efforts: Full),
            new ModelOption("claude-opus-5-5", "Claude Opus 5.5", detail: "Anthropic" + Dot + "the newest Opus, for everyday complex work and coding", efforts: Full),
            new ModelOption("claude-opus-5", "Claude Opus 5", detail: "Anthropic" + Dot + "most intelligent Opus for agents and coding", efforts: Full),
            new ModelOption("gpt-6-astra", "GPT-6 Astra", detail: "OpenAI" + Dot + "most capable OpenAI model for demanding reasoning & agents", efforts: Full),
            new ModelOption("gpt-5.6-sol", "GPT-5.6 Sol", detail: "OpenAI" + Dot + "latest frontier model for general complex work", efforts: Full),
            new ModelOption("gpt-5.6-luna", "GPT-5.6 Luna", detail: "OpenAI" + Dot + "fast and efficient model", efforts: Full),
            new ModelOption("google/gemini-3.8-flash", "Gemini 3.8 Flash", detail: "Google" + Dot + "newest Gemini Flash, improved core reasoning", efforts: ToHigh),
            new ModelOption("deepseek/deepseek-v4-flash", "DeepSeek V4 Flash", detail: "Open Source" + Dot + "fast hybrid-attention reasoning", efforts: HighMax),
            new ModelOption("moonshotai/kimi-k3", "Kimi K3", detail: "Open Source" + Dot + "long-horizon coding & knowledge work with 1M context", efforts: LowHighMax),
            new ModelOption("zai-org/glm-5.3", "GLM 5.3", detail: "Open Source" + Dot + "frontier coding with emergent cyber capabilities", efforts: LowHighMax),
        ]);

        Assert.Equal(expected, CommandCodeAPI.Seed);
        Assert.Equal(new byte[] { 0x20, 0xC2, 0xB7, 0x20 }, Utf8(CommandCodeAPI.Seed[0].Detail!)[9..13]);
        Assert.Equal([true, false, false, false, false, false, false, false, false, false, false], CommandCodeAPI.Seed.Select(m => m.IsDefault));
    }

    // ---- PR-PROVCLI-250 to 254: the approval mod --------------------------------------------------------------

    // The Swift literal :375-438 as it evaluates (the four-space indentation of the closing delimiter stripped, no final
    // newline), with the two OD-0012 F1 wordings of PR-PROVCLI-253 and 254, cut around the orphan predicate at :430
    // whose Windows form is not specified (PR-PROVCLI-254 open_question; STOP #1 in this unit's report).
    private const string ModBeforeOrphanPredicate = """
        import fs from 'node:fs'
        import path from 'node:path'

        // Tools that only look: they never wait on the user.
        const READ_ONLY = new Set([
          'read_file', 'read_directory', 'glob', 'grep', 'shell_output', 'shell_tasks',
          'todo_write', 'task_create', 'task_update', 'task_list', 'task_get', 'sleep',
          'web_search', 'web_fetch', 'agent', 'agent_output', 'activate_skill', 'taste',
          'get_diagnostics', 'ask_user_question', 'plan_review', 'enter_plan_mode', 'exit_plan_mode',
          'cron_list', 'run_command',
        ])

        const sleep = (ms: number) => new Promise(resolve => setTimeout(resolve, ms))

        export default function (cmd: any) {
          const bridgePath = process.env.DROPPY_CODE_MCP_BRIDGE
          if (bridgePath) {
            import(bridgePath).then(async (bridge: any) => {
              for (const tool of await bridge.loadMCPTools()) {
                cmd.addTool({
                  schema: { name: 'mcp__' + tool.server + '__' + tool.name, description: tool.description, input_schema: tool.inputSchema },
                  run: async ({input}: any) => {
                    const text = await tool.call(input || {})
                    return { ok: true, content: [{ type: 'text', text }] }
                  },
                })
              }
            }).catch((error: any) => console.error('droppy mcp', error))
          }
          const dir = process.env.DROPPY_CODE_APPROVALS
          if (!dir) return
          cmd.hooks({
            beforeToolCall: async ({toolCallId, toolName, input}: any, ctx: any) => {
              if (READ_ONLY.has(toolName)) return
              const id = String(toolCallId || `${Date.now()}-${Math.random().toString(36).slice(2)}`).replace(/[^A-Za-z0-9_.-]/g, '_')
              const request = path.join(dir, `${id}.request.json`)
              const reply = path.join(dir, `${id}.reply.json`)
              try { fs.unlinkSync(reply) } catch {}
              fs.writeFileSync(`${request}.tmp`, JSON.stringify({toolCallId: id, toolName, input}))
              fs.renameSync(`${request}.tmp`, request)
              const started = Date.now()
              for (;;) {
                let raw: string | undefined
                try { raw = fs.readFileSync(reply, 'utf8') } catch {}
                if (raw !== undefined) {
                  try { fs.unlinkSync(reply) } catch {}
                  let answer: any = {}
                  try { answer = JSON.parse(raw) } catch {}
                  if (answer.decision === 'allow') return
                  return {block: true, additionalContext: answer.reason || 'The user declined this in nth.'}
                }
                if (ctx && ctx.signal && ctx.signal.aborted) {
                  try { fs.unlinkSync(request) } catch {}
                  return {block: true, additionalContext: 'The turn was stopped.'}
                }
                if (
        """;

    private const string ModAfterOrphanPredicate = """
         || Date.now() - started > 6 * 60 * 60 * 1000) {
                  try { fs.unlinkSync(request) } catch {}
                  return {block: true, additionalContext: 'nth did not answer.'}
                }
                await sleep(40)
              }
            },
          })
        }
        """;

    [Fact]
    [Trait("Req", "PR-PROVCLI-251")] // Services/Providers/CommandCodeAPI.swift:378-385, :408
    [Trait("Req", "PR-PROVCLI-252")] // Services/Providers/CommandCodeAPI.swift:390-403
    [Trait("Req", "PR-PROVCLI-253")] // Services/Providers/CommandCodeAPI.swift:404-425
    [Trait("Req", "PR-PROVCLI-254")] // Services/Providers/CommandCodeAPI.swift:426-438
    public void ApprovalMod_is_the_Swift_literal_byte_for_byte_with_the_two_Marrow_wordings_around_the_orphan_predicate()
    {
        var mod = Utf8(CommandCodeAPI.ApprovalMod);
        var before = Utf8(ModBeforeOrphanPredicate);
        var after = Utf8(ModAfterOrphanPredicate);

        Assert.True(mod.Length > before.Length + after.Length);
        Assert.Equal(before, mod[..before.Length]);
        Assert.Equal(after, mod[^after.Length..]);
        Assert.DoesNotContain("Droppy Code", CommandCodeAPI.ApprovalMod, StringComparison.Ordinal);
        Assert.False(CommandCodeAPI.ApprovalMod.EndsWith('\n'));
    }

    [Fact]
    [Trait("Decision", "ED-021")]
    public void ApprovalMod_orphan_predicate_is_the_literal_ppid_test_or_ED_021_s_signal_0_probe()
    {
        // Not a Req test: PR-PROVCLI-254 leaves open which of these two the port ships (its open_question); this pins
        // that it is one of them and nothing else, so the settled answer only has to drop one alternative.
        var mod = CommandCodeAPI.ApprovalMod;
        var predicate = mod[ModBeforeOrphanPredicate.Length..^ModAfterOrphanPredicate.Length];

        Assert.Contains(predicate, new[]
        {
            "process.ppid === 1",
            "(() => { try { process.kill(process.ppid, 0); return false } catch (e) { return e.code === 'ESRCH' } })()",
        });
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-251")] // Services/Providers/CommandCodeAPI.swift:379-385
    public void ApprovalMod_read_only_set_is_the_twenty_five_shipped_tool_names_run_command_included()
    {
        var mod = CommandCodeAPI.ApprovalMod;
        var start = mod.IndexOf("new Set([", StringComparison.Ordinal);
        var end = mod.IndexOf("])", start, StringComparison.Ordinal);
        var names = mod[(start + "new Set([".Length)..end]
            .Split(',').Select(part => FoundationText.Trim(part, includeNewlines: true)).Where(part => part.Length > 0).ToArray();

        Assert.Equal(
        [
            "'read_file'", "'read_directory'", "'glob'", "'grep'", "'shell_output'", "'shell_tasks'",
            "'todo_write'", "'task_create'", "'task_update'", "'task_list'", "'task_get'", "'sleep'",
            "'web_search'", "'web_fetch'", "'agent'", "'agent_output'", "'activate_skill'", "'taste'",
            "'get_diagnostics'", "'ask_user_question'", "'plan_review'", "'enter_plan_mode'", "'exit_plan_mode'",
            "'cron_list'", "'run_command'",
        ], names);
        Assert.Contains("      if (READ_ONLY.has(toolName)) return\n", mod, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-253")] // Services/Providers/CommandCodeAPI.swift:424
    [Trait("Req", "PR-PROVCLI-254")] // Services/Providers/CommandCodeAPI.swift:428, :432
    public void ApprovalMod_decline_texts_are_the_OD_0012_wordings_and_the_stop_text_is_unchanged()
    {
        var mod = CommandCodeAPI.ApprovalMod;

        Assert.Contains("additionalContext: answer.reason || 'The user declined this in nth.'}", mod, StringComparison.Ordinal);
        Assert.Contains("additionalContext: 'The turn was stopped.'}", mod, StringComparison.Ordinal);
        Assert.Contains("additionalContext: 'nth did not answer.'}", mod, StringComparison.Ordinal);
        Assert.Contains("Date.now() - started > 6 * 60 * 60 * 1000", mod, StringComparison.Ordinal);
        Assert.Contains("await sleep(40)", mod, StringComparison.Ordinal);
        Assert.Contains(".replace(/[^A-Za-z0-9_.-]/g, '_')", mod, StringComparison.Ordinal);
    }

    // ---- PR-PROVCLI-250: supportDirectory and installMod ------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-PROVCLI-250")] // Services/Providers/CommandCodeAPI.swift:442-446
    [Trait("Decision", "ED-023")]
    public void SupportDirectory_is_LocalAppData_Nth_CommandCode_as_a_directory_URL()
    {
        var localAppData = CommandCodeScratch.NewFolder("local app data");

        var directory = CommandCodeAPI.SupportDirectoryUnder(localAppData);

        Assert.Equal("file", directory.Scheme);
        Assert.Contains("/local%20app%20data-", directory.AbsoluteString, StringComparison.Ordinal);
        Assert.EndsWith("/nth/CommandCode/", directory.AbsoluteString, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(localAppData, "nth")));
        // The parameterless CommandCodeAPI.SupportDirectory property additionally resolves through
        // DevDataRoot.LocalApplicationData() (ED-212), which reads the process-wide MARROW_DEV_DATA_ROOT variable in
        // a Debug build; that case is covered, in the one collection that serializes every test touching that
        // variable in this assembly, by
        // DevDataRootProductionSitesTests.CommandCodeAPI_SupportDirectory_is_the_real_special_folder_when_not_redirected
        // (review fix, R-TEST-5) rather than here.
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-250")] // Services/Providers/CommandCodeAPI.swift:450-458
    public void InstallMod_creates_the_directory_and_writes_marrow_approvals_ts_as_UTF8()
    {
        var localAppData = CommandCodeScratch.NewFolder("install");

        var url = CommandCodeAPI.InstallMod(localAppData);

        var file = Path.Combine(localAppData, "nth", "CommandCode", "marrow-approvals.ts");
        Assert.Equal(Utf8(CommandCodeAPI.ApprovalMod), File.ReadAllBytes(file));
        Assert.Equal("marrow-approvals.ts", SwiftPath.LastPathComponent(url.Path));
        Assert.EndsWith("/nth/CommandCode/marrow-approvals.ts", url.AbsoluteString, StringComparison.Ordinal);
        Assert.Equal(new[] { "marrow-approvals.ts" }, Directory.GetFileSystemEntries(Path.GetDirectoryName(file)!).Select(Path.GetFileName));
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-250")] // Services/Providers/CommandCodeAPI.swift:455
    public void InstallMod_leaves_an_identical_file_untouched()
    {
        var localAppData = CommandCodeScratch.NewFolder("unchanged");
        CommandCodeAPI.InstallMod(localAppData);
        var file = Path.Combine(localAppData, "nth", "CommandCode", "marrow-approvals.ts");
        var stamp = new DateTime(2001, 2, 3, 4, 5, 6, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(file, stamp);

        CommandCodeAPI.InstallMod(localAppData);

        Assert.Equal(stamp, File.GetLastWriteTimeUtc(file));
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-250")] // Services/Providers/CommandCodeAPI.swift:455-457
    public void InstallMod_rewrites_a_file_whose_bytes_differ()
    {
        var localAppData = CommandCodeScratch.NewFolder("changed");
        var folder = Path.Combine(localAppData, "nth", "CommandCode");
        Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, "marrow-approvals.ts");
        File.WriteAllBytes(file, Utf8(CommandCodeAPI.ApprovalMod + "\n"));

        CommandCodeAPI.InstallMod(localAppData);

        Assert.Equal(Utf8(CommandCodeAPI.ApprovalMod), File.ReadAllBytes(file));
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-250")] // Services/Providers/CommandCodeAPI.swift:452, :456
    public void InstallMod_propagates_a_write_failure()
    {
        var localAppData = CommandCodeScratch.NewFolder("fails");
        // A directory where the file must go: reading it fails (so the bytes "differ") and the atomic write fails.
        Directory.CreateDirectory(Path.Combine(localAppData, "nth", "CommandCode", "marrow-approvals.ts"));

        var error = Assert.ThrowsAny<Exception>(() => CommandCodeAPI.InstallMod(localAppData));
        Assert.IsNotType<NotImplementedException>(error);
    }
}
