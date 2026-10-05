// PORT-ONLY: SK-11b / WU-7, skills-PLAN.md 2.2 Codex row and research-loading.md 3.2: after initialize, skills/list then skills/extraRoots/set { extraRoots: [<absolute generation folder>] }, resent on every connection.
using Marrow.Core.Port;
using Marrow.Core.Support;

namespace Marrow.Services.Port.Skills.Providers;

public static class CodexSkillRoots
{
    public static JSONValue ListParameters(string directory) => new JSONValue.Object(new ValueMap<string, JSONValue>(new Dictionary<string, JSONValue>
    {
        ["cwds"] = new JSONValue.Array(new ValueList<JSONValue>([new JSONValue.String(directory)])),
        ["forceReload"] = true,
    }));

    // A relative path is refused by Codex (JSON-RPC -32600, research-loading.md 3.2), so it is refused here before anything is sent.
    public static JSONValue ExtraRootsParameters(string folder)
    {
        if (string.IsNullOrEmpty(folder) || !Path.IsPathFullyQualified(folder))
            throw new ArgumentException("Codex needs the absolute path of the skills folder.", nameof(folder));
        return new JSONValue.Object(new ValueMap<string, JSONValue>(new Dictionary<string, JSONValue>
        {
            ["extraRoots"] = new JSONValue.Array(new ValueList<JSONValue>([new JSONValue.String(folder)])),
        }));
    }

    // An error reply (Codex before 0.136.0 has no skills/extraRoots/set) is ignored and the session carries on without the bundle. Only a reply is tolerated: a failed transport is not.
    public static async Task Register(Func<string, JSONValue, Task<JSONValue>> request, string directory, string folder, bool listFirst)
    {
        var roots = ExtraRootsParameters(folder);
        if (listFirst) await Tolerate(() => request("skills/list", ListParameters(directory)));
        await Tolerate(() => request("skills/extraRoots/set", roots));
    }

    private static async Task Tolerate(Func<Task<JSONValue>> send)
    {
        try { _ = await send(); }
        catch (RPCError) { }
    }
}
