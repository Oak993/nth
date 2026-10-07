// PORT-ONLY: SK-11e / DL-12. ACP tool updates and permission requests (Grok) become CommandHeld / CommandFinished.
// ACP has no skill-use update in the plan, so no SkillUsed.
using Marrow.Core.Port.Skills.Detect;
using Marrow.Core.Support;

namespace Marrow.Services.Port.Skills.Providers;

public sealed class AcpToolEventTap(Action<ToolEvent>? sink)
{
    private readonly Dictionary<string, string> commands = new(StringComparer.Ordinal);

    private static string? CommandOf(JSONValue update) =>
        update["kind"]?.JSONValueString == "execute" ? update["rawInput"]?["command"]?.JSONValueString : null;

    /// <summary>session/update tool_call.</summary>
    public void ToolCall(JSONValue update)
    {
        if (sink is null || update["toolCallId"]?.JSONValueString is not { } id || CommandOf(update) is not { } command) return;
        commands[id] = command;
    }

    /// <summary>session/update tool_call_update: a command that reached completed or failed.</summary>
    public void ToolCallUpdate(JSONValue update)
    {
        if (sink is null || update["toolCallId"]?.JSONValueString is not { } id) return;
        if (update["status"]?.JSONValueString is not ("completed" or "failed") || !commands.TryGetValue(id, out var command)) return;
        commands.Remove(id);
        var output = string.Join("\n", (update["content"]?.JSONValueArray ?? [])
            .Select(b => b["type"]?.JSONValueString == "content" ? b["content"]?["text"]?.JSONValueString : null).OfType<string>());
        long? exit = null;
        if (update["rawOutput"] is { } raw && !raw.IsNull)
        {
            if (output.Length == 0)
                output = string.Join("\n", new[] { raw["stdout"]?.JSONValueString, raw["stderr"]?.JSONValueString, raw["output"]?.JSONValueString }.OfType<string>().Where(s => s.Length > 0));
            exit = raw["exitCode"]?.JSONValueInt ?? raw["metadata"]?["exit"]?.JSONValueInt;
        }
        sink(new CommandFinished(command, exit is { } e ? (int)e : null, update["status"]?.JSONValueString == "failed", output));
    }

    /// <summary>session/request_permission for a command, once it will be shown to the user.</summary>
    public void Held(JSONValue toolCall)
    {
        if (sink is null) return;
        if (CommandOf(toolCall) is { } command) sink(new CommandHeld(command));
    }
}
