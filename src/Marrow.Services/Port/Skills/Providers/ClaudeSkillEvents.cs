// PORT-ONLY: SK-11a / DL-12. Complete stream-json tool fields only; never infer tools from prose or invent exit codes.
using Marrow.Core.Port.Skills.Detect;
using Marrow.Core.Support;
using Marrow.Services.Providers;
namespace Marrow.Services.Port.Skills.Providers;

internal sealed class ClaudeSkillEvents
{
    private readonly Dictionary<(string? Parent, string Id), string> commands = new();
    private readonly HashSet<(string? Parent, string Id)> skills = new();
    public Exception? LastFailure { get; private set; }
    private void Emit(ToolEvent value, Action<ToolEvent>? sink)
    {
        try { sink?.Invoke(value); }
        catch (Exception error) { LastFailure = error; } // An observer cannot approve a request or interrupt existing protocol handling.
    }
    public void Handle(JSONValue message, Action<ToolEvent>? sink)
    {
        var parent = message["parent_tool_use_id"]?.JSONValueString;
        switch (message["type"]?.JSONValueString)
        {
            case "control_request":
                if (message["request_id"]?.JSONValueString is null || message["request"] is not { } request || request["subtype"]?.JSONValueString != "can_use_tool") return;
                if (request["tool_name"]?.JSONValueString is ("Bash" or "PowerShell") && request["input"]?["command"]?.JSONValueString is { } held)
                    Emit(new CommandHeld(held), sink);
                break;
            case "assistant":
                // A content_block_start has only a partial input. Use the completed assistant tool_use, once.
                foreach (var block in message["message"]?["content"]?.JSONValueArray ?? [])
                {
                    if (block["type"]?.JSONValueString != "tool_use" || block["id"]?.JSONValueString is not { } id || block["name"]?.JSONValueString is not { } name) continue;
                    var input = block["input"];
                    if (name is "Bash" or "PowerShell")
                    {
                        if (input?["command"]?.JSONValueString is { } command) commands[(parent, id)] = command;
                    }
                    else if (name == "Skill" && (input?["skill"]?.JSONValueString ?? input?["command"]?.JSONValueString) is { } skill)
                    {
                        var folder = skill[(skill.LastIndexOf(':') + 1)..];
                        if (folder.Length != 0 && skills.Add((parent, id))) Emit(new SkillUsed(folder), sink);
                    }
                }
                break;
            case "user":
                foreach (var block in message["message"]?["content"]?.JSONValueArray ?? [])
                {
                    if (block["type"]?.JSONValueString != "tool_result" || block["tool_use_id"]?.JSONValueString is not { } id || !commands.Remove((parent, id), out var command)) continue;
                    Emit(new CommandFinished(command, null, block["is_error"]?.JSONValueBool ?? false, ToolText.Blocks(block["content"])), sink);
                }
                break;
        }
    }
    public void Clear() { commands.Clear(); skills.Clear(); }
}
