// PORT-ONLY: SK-13 / DL-16. What the before-uninstall hook left behind.
namespace Marrow.Services.Port.Skills.Uninstall;

/// <summary>
/// <see cref="Left"/>: the deepest paths the hook could not delete (held open, or still undeleted when the budget ran out). Each is already unlocked, so an ordinary delete
/// removes it once the handle closes. <see cref="OutOfTime"/>: the budget ended before every delete had been tried.
/// </summary>
public sealed record UninstallReport(IReadOnlyList<string> Left, bool OutOfTime);
