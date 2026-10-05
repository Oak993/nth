// PORT-ONLY: SK-11e / DL-12. Command Code's per-turn JSON events become SkillUsed (activate_skill), CommandHeld and CommandFinished.
using Marrow.Core.Port.Skills.Detect;
using Marrow.Core.Support;
using Marrow.Services.Providers;

namespace Marrow.Services.Port.Skills.Providers;

public sealed class CommandCodeToolEventTap(Action<ToolEvent>? sink)
{
    private readonly Dictionary<string, string> commands = new(StringComparer.Ordinal);

    /// <summary>tool_queued.</summary>
    public void Queued(string callId, string name, JSONValue input)
    {
        if (sink is null) return;
        if (name == "activate_skill") { if (input["name"]?.JSONValueString is { Length: > 0 } skill) sink(new SkillUsed(skill)); return; }
        if (ToolEventSink.ShellTools.Contains(name) && input["command"]?.JSONValueString is { } command) commands[callId] = command;
    }

    /// <summary>tool_completed.</summary>
    public void Completed(string callId, JSONValue @event)
    {
        if (sink is null || !commands.Remove(callId, out var command)) return;
        sink(new CommandFinished(command, null, false, Text(@event["result"])));
    }

    /// <summary>tool_errored.</summary>
    public void Errored(string callId, JSONValue @event)
    {
        if (sink is null || !commands.Remove(callId, out var command)) return;
        sink(new CommandFinished(command, null, true, Text(@event["error"])));
    }

    /// <summary>The gate's request for a shell tool that now waits on the user.</summary>
    public void Held(string name, JSONValue input)
    {
        if (sink is null || !ToolEventSink.ShellTools.Contains(name)) return;
        if (input["command"]?.JSONValueString is { } command) sink(new CommandHeld(command));
    }

    private static string Text(JSONValue? value)
    {
        if (value is null || value.IsNull) return "";
        if (value.JSONValueString is { } text) return text;
        var blocks = ToolText.Blocks(value);
        return blocks.Length != 0 ? blocks : value["message"]?.JSONValueString ?? "";
    }
}
