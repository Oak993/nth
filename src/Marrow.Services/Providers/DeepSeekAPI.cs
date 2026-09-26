using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;

// Source: DroppyCode/Services/Providers/DeepSeekAPI.swift (B3 52bc7926), :1-79.

namespace Marrow.Services.Providers;

// :6-79
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

    // :13-15
    public static async Task<bool> Validate(string apiKey) =>
        // :14
        await OpenAICompatibleAPI.Validate(ModelsURL, apiKey);

    // :17-19
    public static async Task<ValueList<ModelOption>> ListModels(string apiKey) =>
        // :18
        await OpenAICompatibleAPI.ListModels(ModelsURL, apiKey, Option);

    // :24-32
    public static async Task<ProviderCredits?> Balance(string apiKey)
    {
        var key = apiKey.Trimmed(); // :25
        if (key.Length == 0) return null; // :26

        var request = OpenAICompatibleAPI.BearerRequest(BalanceURL, key); // :27

        try
        {
            var (data, response) = await URLSession.Shared.Data(request); // :28
            if (response is not HTTPURLResponse http || http.StatusCode != 200 || // :29
                JSONValue.Parse(data) is not { } json) // :30
                return null;

            return Credits(json); // :31
        }
        catch (Exception error) when (SwiftTry.IsThrownError(error))
        {
            return null;
        }
    }

    // :37-47
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

    // :49
    public static readonly ValueList<string> DeepseekEfforts = new(new[] { "low", "high", "max" });

    // :51-52
    public const int ContextWindow = 1_048_576;

    // :58-71
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

    // :75-77
    public static bool IsVisionModel(string? id) =>
        id is "deepseek-flash" or "deepseek-v4-flash" or "deepseek-v4-flash-vision-exp";
}
