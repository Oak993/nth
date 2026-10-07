using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Port.Usage;
using Marrow.Services.Providers;
using Marrow.Testing;

namespace Marrow.Services.Tests.Port.Usage;

[Trait("Rule", "R-TEST-4")]
public sealed class AccountCreditLimitsTests
{
    [Theory]
    [InlineData(12.5)]
    [InlineData(0)]
    public async Task An_OpenRouter_account_reads_only_its_ordinary_keys_reported_USD_allowance(double remaining)
    {
        using var runtime = new TestRuntime();
        using var http = new InProcessHttpHandler { Answer = _ => InProcessHttpHandler.Reply(200,
            "{\"data\":{\"limit_remaining\":" + remaining.ToString(System.Globalization.CultureInfo.InvariantCulture) + ",\"limit_reset\":\"monthly\"}}") };
        using var transport = URLSession.Transport.Override(http);
        var limits = await PlanLimitsReader.Read(new UsageAccountKey(ProviderKind.OpenRouter, "B"), "  offline-key-B  ");
        Assert.NotNull(limits);
        var credits = Report(limits);
        Assert.Equal(remaining, credits.Total);
        Assert.Equal("USD", credits.Currency);
        Assert.Equal(remaining > 0, credits.IsUsable);
        Assert.Empty(limits.Windows);
        Assert.Null(limits.PlanLimitsResetCredits);
        var request = Assert.Single(http.Received);
        Assert.Equal("GET", request.Method);
        Assert.Equal(OpenRouterAPI.KeyURL, request.Url);
        Assert.Equal("Bearer offline-key-B", request.Header("Authorization"));
        Assert.Empty(request.Body);
        Assert.True(new AccountPlanLimitsSnapshot(new(ProviderKind.OpenRouter, "B"), limits, DateTimeOffset.UnixEpoch).LimitsShared);
    }

    [Theory]
    [InlineData("USD", true)]
    [InlineData("CNY", false)]
    public async Task A_Deepseek_account_preserves_the_reported_currency_and_availability_without_inventing_a_window(string currency, bool usable)
    {
        using var runtime = new TestRuntime();
        using var http = new InProcessHttpHandler { Answer = _ => InProcessHttpHandler.Reply(200,
            "{\"is_available\":" + (usable ? "true" : "false") + ",\"balance_infos\":[{\"total_balance\":\"8.75\",\"currency\":\"" + currency + "\"}]}") };
        using var transport = URLSession.Transport.Override(http);
        var limits = await PlanLimitsReader.Read(new UsageAccountKey(ProviderKind.Deepseek, "A"), "offline-key-A");
        Assert.NotNull(limits);
        var credits = Report(limits);
        Assert.Equal(8.75, credits.Total);
        Assert.Equal(currency, credits.Currency);
        Assert.Equal(usable, credits.IsUsable);
        Assert.Empty(limits.Windows);
        Assert.Null(limits.PlanName);
        var request = Assert.Single(http.Received);
        Assert.Equal(DeepSeekAPI.BalanceURL, request.Url);
        Assert.Equal("Bearer offline-key-A", request.Header("Authorization"));
    }

    [Theory]
    [InlineData(ProviderKind.OpenRouter, 429)]
    [InlineData(ProviderKind.OpenRouter, 503)]
    [InlineData(ProviderKind.Deepseek, 429)]
    [InlineData(ProviderKind.Deepseek, 503)]
    public async Task A_credit_read_honors_service_throttling_through_the_shared_scheduler(ProviderKind service, int status)
    {
        using var runtime = new TestRuntime();
        using var http = new InProcessHttpHandler { Answer = _ => InProcessHttpHandler.Reply(status, "{}", headers: [("Retry-After", "600")]) };
        using var transport = URLSession.Transport.Override(http);
        var error = await Assert.ThrowsAsync<LimitsPollException>(() => PlanLimitsReader.Read(new UsageAccountKey(service, "A"), "offline-key-A"));
        Assert.Equal(TimeSpan.FromMinutes(10), error.RetryAfter);
    }

    [Theory]
    [InlineData("{\"data\":{\"limit_remaining\":null,\"usage\":10}}")]
    [InlineData("{\"data\":{\"limit\":20,\"usage\":10}}")]
    [InlineData("{\"data\":{\"limit_remaining\":\"unknown\"}}")]
    public async Task An_unreported_OpenRouter_allowance_stays_unknown_after_the_actual_read(string body)
    {
        using var runtime = new TestRuntime();
        using var http = new InProcessHttpHandler { Answer = _ => InProcessHttpHandler.Reply(200, body) };
        using var transport = URLSession.Transport.Override(http);
        Assert.Null(await PlanLimitsReader.Read(new UsageAccountKey(ProviderKind.OpenRouter, "A"), "offline-key-A"));
        Assert.Single(http.Received);
    }

    private static ProviderCredits Report(PlanLimits limits)
    {
        var property = typeof(PlanLimits).GetProperty("AccountCredits");
        Assert.NotNull(property);
        return Assert.IsType<ProviderCredits>(property.GetValue(limits));
    }
}
