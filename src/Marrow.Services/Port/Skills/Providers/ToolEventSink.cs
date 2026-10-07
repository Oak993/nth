// PORT-ONLY: SK-11e / DL-12. The shell tool names Command Code reports (CommandCodeSession.cs AutoApproves / MakeToolCall).
using Marrow.Core.Port.Skills.Detect;

namespace Marrow.Services.Port.Skills.Providers;

public static class ToolEventSink
{
    internal static readonly string[] ShellTools = ["shell_command", "powershell", "monitor_command", "run_command"];
}
