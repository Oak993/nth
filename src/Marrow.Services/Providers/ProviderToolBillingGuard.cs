using Marrow.Core.Models;
using Marrow.Core.Support;

namespace Marrow.Services.Providers;

// PORT-ONLY: RR-50 round-4 lead ruling / ED-NEW-ma-prices-3. Optional provider tool fees
// are excluded only while the exact request shapes remain local-function-only. Unknown
// options stop dispatch; a future native tool needs its own sourced billing implementation.
internal static class ProviderToolBillingGuard
{
    internal static void Validate(ProviderKind provider, Dictionary<string, JSONValue> payload)
    {
        if (provider is not (ProviderKind.Meta or ProviderKind.Zai)) return;
        var allowed = new HashSet<string>(StringComparer.Ordinal)
        {
            "model", "messages", "stream", "tools", "tool_choice", "stream_options", "reasoning_effort", "max_tokens",
        };
        if (provider == ProviderKind.Zai) allowed.UnionWith(["thinking", "tool_stream"]);
        var invalid = payload.Keys.Any(key => !allowed.Contains(key));
        if (payload.TryGetValue("tools", out var tools))
            invalid |= tools.JSONValueArray is not { } definitions || definitions.Any(tool =>
                tool.JSONValueObject is not { } fields || fields.Keys.Any(key => key is not ("type" or "function"))
                || tool["type"]?.JSONValueString != "function" || tool["function"]?.JSONValueObject is null);
        if (payload.TryGetValue("tool_choice", out var choice)) invalid |= choice.JSONValueString != "auto";
        if (payload.TryGetValue("stream_options", out var stream))
            invalid |= stream.JSONValueObject is not { } options || options.Keys.Any(key => key != "include_usage");
        if (payload.TryGetValue("thinking", out var thinking))
            invalid |= thinking.JSONValueObject is not { } options || options.Keys.Any(key => key is not ("type" or "clear_thinking"));
        if (invalid) throw new ProviderError(new ProviderError.Case.Failed("This request enables an unpriced provider option or tool. No request was sent."));
    }
}
