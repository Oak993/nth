// PORT-ONLY: DL-12 adapter for the ACP fields already parsed by ACPSession; ONDEMAND 5.1-5.2.
using Marrow.Core.Models;
using Marrow.Core.Port.Skills.Detect;
using Marrow.Core.Support;
using Marrow.Services.Providers;

namespace Marrow.Services.Port.Skills.Providers;

public sealed class ACPSkillEvents
{
    private readonly Dictionary<string, string> commands = new(StringComparer.Ordinal);
    public void Observe(JSONValue update)
    {
        if (update["toolCallId"]?.JSONValueString is not { } id) return;
        if (update["kind"]?.JSONValueString is { } kind && kind != "execute") { commands.Remove(id); return; }
        if ((update["kind"]?.JSONValueString == "execute" || commands.ContainsKey(id)) &&
            update["rawInput"]?["command"]?.JSONValueString is { Length: > 0 } command) commands[id] = command;
    }
    public CommandHeld? Held(JSONValue call)
    {
        Observe(call);
        return call["toolCallId"]?.JSONValueString is { } id && commands.TryGetValue(id, out var command) ? new(command) : null;
    }
    public CommandFinished? Finished(JSONValue update, ToolUpdate result)
    {
        Observe(update);
        if (update["toolCallId"]?.JSONValueString is not { } id || result.Status is not (ToolCall.Status.Completed or ToolCall.Status.Failed) ||
            !commands.Remove(id, out var command)) return null;
        // Core's normalized event uses int?. Refuse an out-of-range code instead of narrowing/wrapping it.
        if (result.ExitCode is < int.MinValue or > int.MaxValue) return null;
        return new(command, result.ExitCode is { } exit ? (int)exit : null, result.Status == ToolCall.Status.Failed, result.Output ?? "");
    }
    public void Clear() => commands.Clear();
}
