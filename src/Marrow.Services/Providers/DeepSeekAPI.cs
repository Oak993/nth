using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;

// Source: DroppyCode/Services/Providers/DeepSeekAPI.swift (B2 1fbf81e), :1-99. Skeleton: the URLs and constants
// (:7-10, :70, :73) are complete; every member with behaviour is left to the implementing unit. URLs are Uri, as
// MetaAPI (the same shape) and the HTTP unit's URLRequest take them.

namespace Marrow.Services.Providers;

// :6-99
public static class DeepSeekAPI
{
    public static readonly Uri BaseURL = new("https://api.deepseek.com");                    // :7
    public static readonly Uri ChatURL = new("https://api.deepseek.com/chat/completions");   // :8
    public static readonly Uri ModelsURL = new("https://api.deepseek.com/models");           // :9
    public static readonly Uri BalanceURL = new("https://api.deepseek.com/user/balance");    // :10

    // :13-22
    public static Task<bool> Validate(string apiKey) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/DeepSeekAPI.swift:13");

    // :24-38
    public static Task<ValueList<ModelOption>> ListModels(string apiKey) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/DeepSeekAPI.swift:24");

    // :43-53
    public static Task<ProviderCredits?> Balance(string apiKey) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/DeepSeekAPI.swift:43");

    // :58-68
    public static ProviderCredits? Credits(JSONValue json) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/DeepSeekAPI.swift:58");

    // :70
    public static readonly ValueList<string> DeepseekEfforts = new(new[] { "low", "high", "max" });

    // :73. int, as OpenAICompatibleSession's configuration takes it.
    public const int ContextWindow = 1_048_576;

    // :79-92
    public static ModelOption? Option(string id) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/DeepSeekAPI.swift:79");

    // :96-98
    public static bool IsVisionModel(string? id) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Providers/DeepSeekAPI.swift:96");
}
