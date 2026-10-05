// PORT-ONLY: SK-11b / DL-12 Codex taps (ONDEMAND-PLAN.md 5.1 and 5.2). Provider JSON in, the normalized SK-10 events out; the detector never sees the JSON.
// Formats read: the item and approval shapes CodexSession already handles (item/commandExecution/requestApproval; commandExecution items with command, exitCode, aggregatedOutput, status;
// userMessage content parts of type skill with name and path, the shape the port itself sends in CodexInput).
using Marrow.Core.Support;
using Marrow.Core.Port.Skills.Detect;
using Marrow.Services.Port.Skills.Generations;
using Marrow.Services.Providers;

namespace Marrow.Services.Port.Skills.Providers;

public static class CodexToolTaps
{
    // S2: a command waiting for approval. The command is shown the way the approval card shows it (the shell wrapper removed).
    public static ToolEvent? Request(string method, JSONValue parameters)
    {
        if (method != "item/commandExecution/requestApproval") return null;
        var command = Command(parameters);
        return command is null ? null : new CommandHeld(command);
    }

    // S1: the user's input carried a skill part ({type:"skill", name, path}); ItemId is the set the path belongs to when it is a Marrow generation's path.
    public static IReadOnlyList<ToolEvent> ItemStarted(JSONValue item, IReadOnlyList<PublishedSkill>? published)
    {
        if (item["type"]?.JSONValueString != "userMessage") return [];
        var events = new List<ToolEvent>();
        foreach (var part in item["content"]?.JSONValueArray ?? [])
        {
            if (part["type"]?.JSONValueString != "skill" || part["name"]?.JSONValueString is not { Length: > 0 } name) continue;
            events.Add(new SkillUsed(name, ItemOf(part["path"]?.JSONValueString, published)));
        }
        return events;
    }

    // S3: a command that ended. A declined command never ran, so it is not a finished command.
    public static IReadOnlyList<ToolEvent> ItemCompleted(JSONValue item)
    {
        if (item["type"]?.JSONValueString != "commandExecution") return [];
        var status = item["status"]?.JSONValueString;
        if (status == "declined") return [];
        if (Command(item) is not { } command) return [];
        var exit = item["exitCode"]?.JSONValueInt is { } code ? unchecked((int)code) : (int?)null;
        return [new CommandFinished(command, exit, status is "failed" or "interrupted", item["aggregatedOutput"]?.JSONValueString ?? "")];
    }

    private static string? Command(JSONValue value)
    {
        var raw = value["command"]?.JSONValueString;
        if (raw is null) return null;
        var command = ToolTitles.UnwrapShell(raw);
        return command.Length == 0 ? null : command;
    }

    private static string? ItemOf(string? path, IReadOnlyList<PublishedSkill>? published)
    {
        if (path is null || published is null) return null;
        var wanted = path.Replace('\\', '/');
        var skill = published.FirstOrDefault(s => s.File.Replace('\\', '/').Equals(wanted, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal));
        return skill is null ? null : skill.Id[..skill.Id.IndexOf('/', StringComparison.Ordinal)];
    }
}
