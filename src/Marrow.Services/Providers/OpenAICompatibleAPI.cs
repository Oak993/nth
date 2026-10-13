using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;

namespace Marrow.Services.Providers;

// Source: DroppyCode/Services/Providers/OpenAICompatibleAPI.swift (B3 52bc7926), :1-35.
//
// Bearer auth, key validation and the models list, read the same way by DeepSeek, Meta and Z.ai (:3). Extracted
// at B3 (N6) from the near-identical bodies MetaAPI, DeepSeekAPI and ZaiAPI each carried at B2; those three now
// delegate here (see their own headers).
public static class OpenAICompatibleAPI
{
    /// <summary>
    /// A GET-ready bearer request: 15 s timeout, "Bearer &lt;key&gt;" Authorization, "application/json" Accept.
    /// </summary>
    // :5-10
    public static URLRequest BearerRequest(Uri url, string key)
    {
        // :6
        var request = new URLRequest(url, timeoutInterval: 15);
        // :7
        request = request.SetValue("Bearer " + key, forHTTPHeaderField: "Authorization");
        // :8
        request = request.SetValue("application/json", forHTTPHeaderField: "Accept");
        // :9
        return request;
    }

    /// <summary>A key counts as valid when the models endpoint accepts it.</summary>
    // :13-20
    public static async Task<bool> Validate(Uri modelsUrl, string key)
    {
        // :14. Swift shadows the `key` parameter with its trimmed self; C# cannot re-declare a parameter name,
        // so the trimmed value is `trimmedKey` here. No behaviour differs.
        var trimmedKey = key.Trimmed();
        // :15
        if (trimmedKey.Length == 0) return false;
        // :16
        var request = BearerRequest(modelsUrl, trimmedKey);
        // :17-18
        var result = await SwiftTry.OptionalValueAsync(() => URLSession.Shared.Data(request));
        if (result is not { } exchange || exchange.Response is not HTTPURLResponse http) return false;
        // :19
        return http.StatusCode == 200;
    }

    // :22-34
    public static Task<ValueList<ModelOption>> ListModels(Uri modelsUrl, string key, Func<string, ModelOption?> option) =>
        ListModels(modelsUrl, key, (id, _) => option(id));

    // DEVIATION ED-NEW-muse-wonder-2: keep endpoint metadata for chat eligibility; the id-only callers retain their behavior.
    internal static async Task<ValueList<ModelOption>> ListModels(Uri modelsUrl, string key, Func<string, JSONValue, ModelOption?> option)
    {
        // :23 (see the Validate note on the trimmed-key rename)
        var trimmedKey = key.Trimmed();
        // :24
        if (trimmedKey.Length == 0) return ValueList<ModelOption>.Empty;
        // :25
        var request = BearerRequest(modelsUrl, trimmedKey);
        // :26
        var (data, response) = await URLSession.Shared.Data(request);
        // :27-28
        if (response is not HTTPURLResponse http || http.StatusCode != 200 ||
            JSONValue.Parse(data) is not { } json)
        {
            return ValueList<ModelOption>.Empty;
        }
        // :29
        var options = new List<ModelOption>();
        foreach (var item in json["data"]?.JSONValueArray ?? [])
        {
            if (item["id"]?.JSONValueString is { } id && option(id, item) is { } modelOption)
                options.Add(modelOption);
        }
        return new ValueList<ModelOption>(options
            .OrderBy(o => o.IsDefault ? 0 : 1)
            .ThenBy(o => o.Id, SwiftStringComparer.Instance));
    }
}
