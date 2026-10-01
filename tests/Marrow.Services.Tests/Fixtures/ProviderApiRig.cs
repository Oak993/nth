using System.Text.Json;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;
using Marrow.Testing;

namespace Marrow.Services.Tests.Fixtures;

// PORT-ONLY test rig (R-TEST-3, R-TEST-10): one ProviderRegistry over in-memory settings, a deterministic
// main loop and clock, and a loopback answerer standing in for api.deepseek.com, api.meta.ai and api.z.ai.
// The three Endpoint boundaries are scoped to the creating test's async flow, so parallel tests never share one,
// and no request leaves the loopback interface.

/// <summary>A registry whose API-key providers talk to a loopback server only.</summary>
public sealed class ProviderApiRig : IAsyncDisposable
{
    public const string DeepSeekModels = "/models";
    public const string DeepSeekBalance = "/user/balance";
    public const string ZaiModels = "/api/coding/paas/v4/models";
    public const string ZaiQuota = "/api/monitor/usage/quota/limit";
    public const string ZaiResetList = "/api/biz/customer-package-reset/list";
    public const string ZaiResetUse = "/api/biz/customer-package-reset/use";
    public const string MetaModels = "/v1/models";

    private readonly IDisposable deepSeekRoute;
    private readonly IDisposable metaRoute;
    private readonly IDisposable zaiRoute;

    public ProviderApiRig()
    {
        Runtime = new TestRuntime();
        Settings = new FakeProviderSettings();
        Loopback = new ProviderApiLoopback();
        deepSeekRoute = DeepSeekAPI.Endpoint.Override(Loopback.Redirect);
        metaRoute = MetaAPI.Endpoint.Override(Loopback.Redirect);
        zaiRoute = ZaiAPI.Endpoint.Override(Loopback.Redirect);
        // The constructor seeds an empty catalog from each provider's own table; a persisted row stands in so
        // a test sees only what the call under test does.
        var payload = new Dictionary<string, object[]>
        {
            ["deepseek"] = [new { id = "seed-ds", name = "Seed", efforts = Array.Empty<string>() }],
            ["zai"] = [new { id = "seed-zai", name = "Seed", efforts = Array.Empty<string>() }],
            ["commandcode"] = [new { id = "seed-cc", name = "Seed", efforts = Array.Empty<string>() }],
        };
        Runtime.Defaults.Set("providerModelCatalogs", System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload)));
        Registry = new ProviderRegistry(Settings);
    }

    public TestRuntime Runtime { get; }

    public FakeProviderSettings Settings { get; }

    public ProviderApiLoopback Loopback { get; }

    public ProviderRegistry Registry { get; }

    /// <summary>Runs one registry call on the main loop and pumps until it completes (background thread, so real I/O can finish).</summary>
    public Task Run(Func<Task> body) => Task.Run(() => Runtime.Loop.Run(body, TimeSpan.FromSeconds(30)));

    /// <summary>Runs a synchronous registry call on the main loop, then everything it posted.</summary>
    public void OnMain(Action body) => Runtime.Loop.Run(() => MainActor.Run(body));

    /// <summary>Pumps the main loop on a background thread until the condition holds (the work spawned on main needs a pump).</summary>
    public Task Until(Func<bool> condition) => Task.Run(() => Runtime.Loop.RunUntil(condition, TimeSpan.FromSeconds(20)));

    public async ValueTask DisposeAsync()
    {
        zaiRoute.Dispose();
        metaRoute.Dispose();
        deepSeekRoute.Dispose();
        await Loopback.DisposeAsync();
        Runtime.Dispose();
    }

    /// <summary>A Z.ai quota body with one 5-hour window at the given percentage.</summary>
    public static string ZaiQuotaBody(double percentage) =>
        "{\"code\":200,\"success\":true,\"data\":{\"level\":\"pro\",\"limits\":[{\"type\":\"TOKENS_LIMIT\",\"unit\":3,\"number\":5,\"percentage\":" +
        percentage.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}]}}";

    /// <summary>A DeepSeek balance body with one USD balance.</summary>
    public static string DeepSeekBalanceBody(string total) =>
        "{\"is_available\":true,\"balance_infos\":[{\"currency\":\"USD\",\"total_balance\":\"" + total +
        "\",\"granted_balance\":\"0.00\",\"topped_up_balance\":\"" + total + "\"}]}";
}
