// PORT-ONLY: SK-11f / WU-12, DL-14. The two function-tool definitions Marrow adds for the API providers, in the shape the six built-in ones have.
using Marrow.Core.Port;
using Marrow.Core.Support;

namespace Marrow.Services.Port.Skills.Providers;

internal static class NativeSkillToolDefinitions
{
    public static JSONValue UseSkill() => Definition(
        NativeSkillTools.UseSkillName,
        "Read a skill's full instructions. Pass the skill's name as listed in the system prompt; pass file (a path inside the skill's folder) to read a file its instructions mention.",
        Schema([("name", Field("Skill name")), ("file", Field("Path inside the skill's folder, relative to it; SKILL.md when left out"))], "name"));

    public static JSONValue RequestTool() => Definition(
        NativeSkillTools.RequestToolName,
        "Ask the user to download a tool the task needs and that is not installed. Waits for the answer and returns \"installed at <path>\" or \"declined\".",
        Schema([("name", Field("Program or catalogue item name")), ("reason", Field("Why the task needs it"))], "name", "reason"));

    private static JSONValue Field(string description) => JSONValue.FromDictionaryLiteral(("type", "string"), ("description", description));

    private static JSONValue Schema(IEnumerable<(string Name, JSONValue Field)> properties, params string[] required) => JSONValue.FromDictionaryLiteral(
        ("type", "object"),
        ("properties", new JSONValue.Object(new ValueMap<string, JSONValue>(properties.Select(p => new KeyValuePair<string, JSONValue>(p.Name, p.Field)), SwiftStringComparer.Instance))),
        ("required", JSONValue.FromArrayLiteral([.. required.Select(name => (JSONValue)new JSONValue.String(name))])));

    private static JSONValue Definition(string name, string description, JSONValue parameters) =>
        JSONValue.FromDictionaryLiteral(("type", "function"), ("function", JSONValue.FromDictionaryLiteral(
            ("name", name), ("description", description), ("parameters", parameters))));
}
