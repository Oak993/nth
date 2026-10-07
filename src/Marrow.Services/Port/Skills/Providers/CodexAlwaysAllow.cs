// PORT-ONLY: SK-11b / WU-7, skills-PLAN.md 2.1 "Codex Always allow for skill scripts" (r3): an execpolicy amendment that names a path under Marrow's skills\views root is not offered, because Codex would append it to the user's default.rules and the path dies with the chat.
using Marrow.Core.Support;

namespace Marrow.Services.Port.Skills.Providers;

public static class CodexAlwaysAllow
{
    public static bool Withholds(IReadOnlyList<JSONValue>? availableDecisions, string viewsRoot)
    {
        if (availableDecisions is null) return false;
        var root = Normalize(viewsRoot).TrimEnd('/');
        if (root.Length == 0) return false;
        return availableDecisions.Any(d => d["acceptWithExecpolicyAmendment"] is { } amendment && Strings(amendment).Any(s => Under(Normalize(s), root)));
    }

    // Either slash, any letter case; the root must end at a name boundary, so "views-sibling" is not under "views".
    private static bool Under(string text, string root)
    {
        for (var at = text.IndexOf(root, StringComparison.OrdinalIgnoreCase); at >= 0; at = text.IndexOf(root, at + 1, StringComparison.OrdinalIgnoreCase))
        {
            var end = at + root.Length;
            if (end == text.Length || text[end] == '/') return true;
        }
        return false;
    }

    private static string Normalize(string path) => path.Replace('\\', '/');

    private static IEnumerable<string> Strings(JSONValue value)
    {
        if (value.JSONValueString is { } text) yield return text;
        if (value.JSONValueArray is { } items) foreach (var item in items) foreach (var s in Strings(item)) yield return s;
        if (value.JSONValueObject is { } fields) foreach (var field in fields.Values) foreach (var s in Strings(field)) yield return s;
    }
}
