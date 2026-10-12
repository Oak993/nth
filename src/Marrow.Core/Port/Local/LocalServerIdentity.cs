using System.Text.Json;

namespace Marrow.Core.Port.Local;

// PORT-ONLY: MAUI-054 / T15-review-rulings.md #5. Parse ONLY successful replies from the probed server.
// Ollama source: https://github.com/ollama/ollama/blob/main/server/routes.go (GET /).
// LM Studio native protocol: https://lmstudio.ai/docs/developer/rest/endpoints (GET /api/v0/models).
// An address, model publisher/name, or generic {"version":...} reply is not a server identity.
public static class LocalServerIdentity
{
    public static LocalServerKind FromRootReply(string? reply) => reply?.Trim() == "Ollama is running"
        ? LocalServerKind.Ollama : LocalServerKind.Unknown;

    public static LocalServerKind FromStudioModelsReply(JsonElement reply)
    {
        if (Text(reply, "object") != "list" || Property(reply, "data") is not { ValueKind: JsonValueKind.Array } rows)
            return LocalServerKind.Unknown;
        // The native response reports runtime format and loaded state, unlike an OpenAI-compatible model list.
        // Require the published shape, including a real model; missing, unfamiliar or empty replies stay Unknown.
        return rows.EnumerateArray().Any(row => Text(row, "object") == "model"
            && Text(row, "id") is { Length: > 0 }
            && Text(row, "type") is "llm" or "vlm" or "embeddings"
            && Text(row, "compatibility_type") is "gguf" or "mlx"
            && Text(row, "state") is "loaded" or "not-loaded"
            && Text(row, "publisher") is { Length: > 0 } && Text(row, "arch") is { Length: > 0 }
            && Text(row, "quantization") is { Length: > 0 }
            && Property(row, "max_context_length") is { ValueKind: JsonValueKind.Number } length
            && length.TryGetInt32(out var context) && context > 0)
            ? LocalServerKind.LMStudio : LocalServerKind.Unknown;
    }

    // PORT-ONLY (web surfaces W17, ED-NEW-web-w17-1): llama.cpp's server answers GET /props with default_generation_settings (holding
    // the slot's n_ctx) and total_slots. An LM Studio or Ollama server has no /props, and an unfamiliar or partial reply stays Unknown.
    public static LocalServerKind FromPropsReply(JsonElement reply) =>
        Property(reply, "default_generation_settings") is { ValueKind: JsonValueKind.Object } settings
        && Property(settings, "n_ctx") is { ValueKind: JsonValueKind.Number } context && context.TryGetInt32(out var window) && window > 0
        && Property(reply, "total_slots") is { ValueKind: JsonValueKind.Number } slots && slots.TryGetInt32(out var count) && count > 0
            ? LocalServerKind.LlamaCpp : LocalServerKind.Unknown;

    private static JsonElement? Property(JsonElement value, string key) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(key, out var property) ? property : null;
    private static string? Text(JsonElement value, string key) =>
        Property(value, key) is { ValueKind: JsonValueKind.String } text ? text.GetString() : null;
}
