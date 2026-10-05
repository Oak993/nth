using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Providers;

namespace Marrow.Services.Port.Usage;

// PORT-ONLY OD-0114: ordinary selected-key reports. OpenRouter GET /key limit_remaining is a USD
// allowance (https://openrouter.ai/docs/api/api-reference/api-keys/get-current-api-key).
// Its nullable allowance and textual reset interval are not a balance or an absolute reset time.
public static class AccountCreditLimitsReader
{
    public static async Task<PlanLimits?> Read(UsageAccountKey account, string apiKey)
    {
        var key = apiKey.Trimmed();
        if (key.Length == 0) return null;
        var url = account.Provider switch
        {
            ProviderKind.OpenRouter => OpenRouterAPI.KeyURL,
            ProviderKind.Deepseek => DeepSeekAPI.Endpoint.Current(DeepSeekAPI.BalanceURL),
            _ => null,
        };
        if (url is null) return null;
        try
        {
            var (data, response) = await URLSession.Shared.Data(OpenAICompatibleAPI.BearerRequest(url, key));
            if (response is not HTTPURLResponse http) return null;
            LimitsPollException.ThrowIfThrottled(http);
            if (http.StatusCode != 200 || JSONValue.Parse(data) is not { } json) return null;
            var credits = account.Provider == ProviderKind.Deepseek ? DeepSeekAPI.Credits(json) : OpenRouterCredits(json);
            if (credits is null || !double.IsFinite(credits.Total)) return null;
            return new PlanLimits { Windows = ValueList<PlanLimits.Window>.Empty, AccountCredits = credits };
        }
        catch (Exception error) when (error is not LimitsPollException && SwiftTry.IsThrownError(error)) { return null; }
    }

    private static ProviderCredits? OpenRouterCredits(JSONValue json) => json["data"]?["limit_remaining"]?.JSONValueDouble is { } remaining && double.IsFinite(remaining)
        ? new() { Total = Math.Max(0, remaining), Currency = "USD", IsUsable = remaining > 0 } : null;
}
