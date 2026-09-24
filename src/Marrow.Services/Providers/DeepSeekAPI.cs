using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;

// Source: DroppyCode/Services/Providers/DeepSeekAPI.swift (B2 1fbf81e), :1-99.

namespace Marrow.Services.Providers;

// :6-99
public static class DeepSeekAPI
{
    // :7
    public static readonly Uri BaseURL = new("https://api.deepseek.com");

    // :8
    public static readonly Uri ChatURL = new("https://api.deepseek.com/chat/completions");

    // :9
    public static readonly Uri ModelsURL = new("https://api.deepseek.com/models");

    // :10
    public static readonly Uri BalanceURL = new("https://api.deepseek.com/user/balance");

    // :13-22
    public static async Task<bool> Validate(string apiKey)
    {
        var key = apiKey.Trimmed();
        if (key.Length == 0) return false;

        var request = new URLRequest(ModelsURL, 15)
            .SetValue("Bearer " + key, "Authorization")
            .SetValue("application/json", "Accept");

        try
        {
            var (_, response) = await URLSession.Shared.Data(request);
            return response is HTTPURLResponse http && http.StatusCode == 200;
        }
        catch (Exception error) when (SwiftTry.IsThrownError(error))
        {
            return false;
        }
    }

    // :24-38
    public static async Task<ValueList<ModelOption>> ListModels(string apiKey)
    {
        var key = apiKey.Trimmed();
        if (key.Length == 0) return ValueList<ModelOption>.Empty;

        var request = new URLRequest(ModelsURL, 15)
            .SetValue("Bearer " + key, "Authorization")
            .SetValue("application/json", "Accept");
        var (data, response) = await URLSession.Shared.Data(request);

        if (response is not HTTPURLResponse http || http.StatusCode != 200 ||
            JSONValue.Parse(data) is not { } json)
            return ValueList<ModelOption>.Empty;

        var ids = (json["data"]?.JSONValueArray ?? [])
            .Select(item => item["id"]?.JSONValueString)
            .Where(id => id is not null)
            .Select(id => id!);

        var options = ids
            .Select(Option)
            .Where(option => option is not null)
            .Select(option => option!)
            .OrderBy(option => option.IsDefault ? 0 : 1)
            .ThenBy(option => option.Id, SwiftStringComparer.Instance);

        return new ValueList<ModelOption>(options);
    }

    // :43-53
    public static async Task<ProviderCredits?> Balance(string apiKey)
    {
        var key = apiKey.Trimmed();
        if (key.Length == 0) return null;

        var request = new URLRequest(BalanceURL, 15)
            .SetValue("Bearer " + key, "Authorization")
            .SetValue("application/json", "Accept");

        try
        {
            var (data, response) = await URLSession.Shared.Data(request);
            if (response is not HTTPURLResponse http || http.StatusCode != 200 ||
                JSONValue.Parse(data) is not { } json)
                return null;

            return Credits(json);
        }
        catch (Exception error) when (SwiftTry.IsThrownError(error))
        {
            return null;
        }
    }

    // :58-68
    public static ProviderCredits? Credits(JSONValue json)
    {
        var infos = json["balance_infos"]?.JSONValueArray;
        if (infos is null || infos.Count == 0) return null;

        var info = infos[0];
        var totalText = info["total_balance"]?.JSONValueString;
        if (totalText is null || SwiftNumber.ParseDouble(totalText) is not { } total) return null;

        double? ParseAmount(string key)
        {
            var text = info[key]?.JSONValueString;
            return text is null ? null : SwiftNumber.ParseDouble(text);
        }

        return new ProviderCredits
        {
            Total = total,
            Currency = info["currency"]?.JSONValueString ?? "",
            ToppedUp = ParseAmount("topped_up_balance"),
            Granted = ParseAmount("granted_balance"),
            IsUsable = json["is_available"]?.JSONValueBool ?? true,
        };
    }

    // :70
    public static readonly ValueList<string> DeepseekEfforts = new(new[] { "low", "high", "max" });

    // :72-73
    public const int ContextWindow = 1_048_576;

    // :79-92
    public static ModelOption? Option(string id) => id switch
    {
        "deepseek-v4-pro" => new ModelOption(
            id,
            "V4 Pro",
            detail: "Best reasoning and coding quality",
            efforts: DeepseekEfforts,
            defaultEffort: "high",
            isDefault: true),
        "deepseek-flash" => new ModelOption(
            id,
            "V4.1 Flash",
            detail: "Newest, fast, reads images",
            efforts: DeepseekEfforts,
            defaultEffort: "high"),
        "deepseek-v4-flash" or "deepseek-v4-flash-vision-exp" or "deepseek-chat" or "deepseek-reasoner" => null,
        _ => new ModelOption(
            id,
            id,
            efforts: DeepseekEfforts,
            defaultEffort: "high"),
    };

    // :96-98
    public static bool IsVisionModel(string? id) =>
        id is "deepseek-flash" or "deepseek-v4-flash" or "deepseek-v4-flash-vision-exp";
}
