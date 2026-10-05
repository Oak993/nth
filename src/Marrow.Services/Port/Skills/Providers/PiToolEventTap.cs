// PORT-ONLY: SK-11e / DL-12 (ONDEMAND 5.2). Pi RPC tool events become CommandHeld / CommandFinished. Pi has no skill-use event in the plan, so no SkillUsed.
using Marrow.Core.Port.Skills.Detect;
using Marrow.Core.Support;
using Marrow.Services.Providers;

namespace Marrow.Services.Port.Skills.Providers;

public sealed class PiToolEventTap(Action<ToolEvent>? sink)
{
    private readonly Dictionary<string, string> commands = new(StringComparer.Ordinal);

    /// <summary>tool_execution_start: remembers a bash call's command line.</summary>
    public void ToolStarted(JSONValue message)
    {
        if (sink is null) return;
        if (message["toolCallId"]?.JSONValueString is not { } id || message["toolName"]?.JSONValueString != "bash") return;
        var args = message["args"] ?? message["input"];
        if (args?["command"]?.JSONValueString is { } command) commands[id] = command;
    }

    /// <summary>tool_execution_end: a bash call that ended.</summary>
    public void ToolEnded(JSONValue message)
    {
        if (sink is null) return;
        if (message["toolCallId"]?.JSONValueString is not { } id || !commands.Remove(id, out var command)) return;
        var result = message["result"] ?? new JSONValue.Null();
        var isError = message["isError"]?.JSONValueBool == true || result["isError"]?.JSONValueBool == true;
        var exit = result["details"]?["exitCode"]?.JSONValueInt;
        sink(new CommandFinished(command, exit is { } e ? (int)e : null, isError, ToolText.Blocks(result["content"])));
    }

    /// <summary>The gate's approval request for a command that is now waiting on the user.</summary>
    public void Held(JSONValue payload)
    {
        if (sink is null || payload["kind"]?.JSONValueString != "command") return;
        if ((payload["input"]?["command"]?.JSONValueString ?? payload["toolName"]?.JSONValueString) is { } command) sink(new Marrow.Core.Port.Skills.Detect.CommandHeld(command));
    }
}
