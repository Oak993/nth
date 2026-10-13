// PORT-ONLY: owner model-discovery ruling, 2026-10-13; ED-NEW-muse-wonder-2.
using Marrow.Core.Support;

namespace Marrow.Services.Port;

internal static class ChatModelCatalog
{
    // Output/task metadata describes eligibility. Input images, audio or video alone do not disqualify chat.
    internal static bool CanChat(string id, JSONValue item)
    {
        if (item["capabilities"]?["chat"]?.JSONValueBool is { } chat) return chat;
        if (item["supported_endpoints"]?.JSONValueArray is { Count: > 0 } endpoints)
            return endpoints.Any(endpoint => endpoint.JSONValueString is { } value &&
                value.Trim('/').EndsWith("chat/completions", StringComparison.OrdinalIgnoreCase));
        var kind = item["type"]?.JSONValueString ?? item["task"]?.JSONValueString;
        if (kind is not null)
        {
            var lowered = kind.ToLowerInvariant();
            if (lowered is "chat" or "text" or "text-generation" or "chat-completion") return true;
            if (NonChatWords(lowered)) return false;
        }
        if (item["architecture"]?["output_modalities"]?.JSONValueArray is { Count: > 0 } outputs)
            return outputs.Any(output => output.JSONValueString == "text");
        return !NonChatWords(id.ToLowerInvariant());
    }

    private static bool NonChatWords(string value) =>
        value.Split(['-', '_', '/', '.', ' '], StringSplitOptions.RemoveEmptyEntries)
            .Any(word => word is "image" or "video" or "voice" or "audio" or "transcribe" or "transcription"
                or "embedding" or "embeddings" or "rerank" or "reranker" or "segmentation" or "sam" or "tts" or "asr");

    internal static bool? TakesImages(JSONValue item)
    {
        if (item["capabilities"]?["vision"]?.JSONValueBool is { } vision) return vision;
        if (item["architecture"]?["input_modalities"]?.JSONValueArray is { } modalities)
            return modalities.Any(modality => modality.JSONValueString == "image");
        return null;
    }
}
