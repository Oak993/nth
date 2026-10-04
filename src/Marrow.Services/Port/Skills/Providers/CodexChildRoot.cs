// PORT-ONLY: SK-11b / DL-11. Where the Marrow Code data root is, for the DL-6 PATH rule on Codex's app-server child; null means "not known", and then nothing changes.
namespace Marrow.Services.Port.Skills.Providers;

public sealed class CodexChildRoot(Func<string?> resolve)
{
    public string? Resolve() => resolve();
}
