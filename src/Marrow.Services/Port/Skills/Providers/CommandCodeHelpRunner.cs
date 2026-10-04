// PORT-ONLY: SK-11e. The real `--help` runner for CommandCodeSkillFlagProbe: Shell.Run with a timeout; non-zero exit or timeout gives null.
using Marrow.Core.Support;

namespace Marrow.Services.Port.Skills.Providers;

public static class CommandCodeHelpRunner
{
    public const double TimeoutSeconds = 10;

    public static async Task<string?> Run(string executable, CancellationToken cancel)
    {
        var result = await Shell.Run(executable, ["--help"], timeout: TimeoutSeconds).WaitAsync(cancel).ConfigureAwait(false);
        return result.Succeeded ? result.Output + result.ErrorOutput : null;
    }
}
