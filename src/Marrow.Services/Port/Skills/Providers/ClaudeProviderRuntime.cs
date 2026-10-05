// PORT-ONLY: SK-11a async-flow-local offline seams (ED-NEW-f-sk-11a); production uses the existing process and help paths.
using Marrow.Core.Port;
using Marrow.Core.Support;
namespace Marrow.Services.Port.Skills.Providers;

internal static class ClaudeProviderRuntime
{
    internal static Ambient<Func<string, IReadOnlyList<string>, string, IReadOnlyDictionary<string, string>, IClaudeProcess>> Process { get; } =
        new((executable, arguments, directory, environment) => new ClaudeProcess(executable, arguments, directory, environment));
    internal static Ambient<Func<SwiftURL, IReadOnlyDictionary<string, string>, Task<ShellResult>>> Help { get; } =
        new((executable, environment) => Shell.Run(executable.Path, ["--help"], environment: environment, timeout: 20));
}
