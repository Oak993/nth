namespace Marrow.Core.Port.Local;

// PORT-ONLY: T15-review-rulings.md #5 / MAUI-054. Unknown is the honest default until a server identifies itself.
public enum LocalServerKind
{
    Unknown,
    Ollama,
    LMStudio,

    // PORT-ONLY (web surfaces W17, ED-NEW-web-w17-1): a llama.cpp server, which takes a GBNF grammar with a request.
    LlamaCpp,
}
