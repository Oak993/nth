// PORT-ONLY: web surfaces W17, Teaching (DESIGN.md 7.3 "where the runtime supports it (llama.cpp GBNF ...), grammar-constrained
// output"; ED-NEW-web-w17-1). No Swift counterpart. What a Local request adds so a llama.cpp server can only write a valid
// lite view: the GBNF grammar from NthUiGrammar, applied lazily, so the reply is free text until the model writes the
// opening ```nth-ui fence and a grammar-checked view from there to the closing fence. Only a llama.cpp server takes these
// fields (LocalServerKind.LlamaCpp, found by GET /props); Ollama's and LM Studio's structured output is JSON-schema only
// and would wrap the whole reply, so those servers are taught by the prompt alone. llama.cpp refuses a custom grammar next
// to tools, so the fields are for a request that carries none.
using Marrow.Core.NthUi;
using Marrow.Core.Port.Local;
using Marrow.Core.Support;

namespace Marrow.Services.Port.Local;

public static class LocalViewGrammar
{
    /// <summary>llama.cpp's trigger type for a word (common_grammar_trigger_type COMMON_GRAMMAR_TRIGGER_TYPE_WORD).</summary>
    private const int WordTrigger = 0;

    /// <summary>The request fields for a server of <paramref name="kind"/>, or null when it takes no grammar.</summary>
    public static IReadOnlyDictionary<string, JSONValue>? Fields(LocalServerKind kind)
    {
        if (kind != LocalServerKind.LlamaCpp) return null;
        return new Dictionary<string, JSONValue>
        {
            ["grammar"] = NthUiGrammar.Gbnf,
            ["grammar_lazy"] = true,
            ["grammar_triggers"] = JSONValue.FromArrayLiteral([.. NthUiGrammar.Triggers.Select(word =>
                JSONValue.FromDictionaryLiteral(("type", WordTrigger), ("value", word)))]),
        };
    }

    /// <summary>Adds the fields to a request that carries no tools; anything else is left as it was.</summary>
    public static void Apply(Dictionary<string, JSONValue> payload, LocalServerKind kind)
    {
        if (payload.ContainsKey("tools") || Fields(kind) is not { } fields) return;
        foreach (var (key, value) in fields) payload[key] = value;
    }
}
