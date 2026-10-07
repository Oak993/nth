namespace Marrow.Core.Port.Local;

// PORT-ONLY: T15-review-rulings.md #5 / MAUI-054. Unknown is the honest default until a server identifies itself.
public enum LocalServerKind
{
    Unknown,
    Ollama,
    LMStudio,
}
