// PORT-ONLY: DL-12 adapter for CopilotSession's existing SDK fields, ONDEMAND 5.1-5.2.
using Marrow.Core.Port.Skills.Detect;
using Marrow.Core.Support;

namespace Marrow.Services.Port.Skills.Providers;

public sealed class CopilotSkillEvents
{
    private readonly Dictionary<string, string> commands = new(StringComparer.Ordinal);
    public ToolEvent? Started(string id, string name, JSONValue arguments, string? server)
    {
        if (server is not null) return null;
        if (name is "bash" or "powershell" && arguments["command"]?.JSONValueString is { Length: > 0 } command)
            commands[id] = command;
        return name == "skill" && arguments["skill"]?.JSONValueString is { Length: > 0 } skill ? new SkillUsed(skill) : null;
    }
    public CommandHeld? Held(JSONValue request) => request["kind"]?.JSONValueString == "shell" &&
        request["fullCommandText"]?.JSONValueString is { Length: > 0 } command ? new(command) : null;
    public CommandFinished? Finished(string id, JSONValue data, bool declined)
    {
        if (!commands.Remove(id, out var command) || declined) return null;
        var succeeded = data["success"]?.JSONValueBool ?? true;
        var output = !succeeded && data["error"]?["message"]?.JSONValueString is { } error ? error :
            data["result"]?["detailedContent"]?.JSONValueString ?? data["result"]?["content"]?.JSONValueString ?? "";
        // CopilotSession's format has no exitCode. Never manufacture one from success/error text.
        return new(command, null, !succeeded, output);
    }
    public void Clear() => commands.Clear();
}
