// PORT-ONLY: WU-8 / DL-11, skills-PLAN.md 2.2; copilot-sdk 9b28c94 client.ts:1854-1857, 2135-2138.
using System.Collections.Concurrent;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Port.Skills.Generations;

namespace Marrow.Services.Port.Skills.Providers;

public static class CopilotSkillWiring
{
    private static readonly ConcurrentDictionary<CopilotExecutableIdentity, byte> rejected = new();

    public static void AddDirectories(Dictionary<string, JSONValue> parameters, Generation? generation, string? executable)
    {
        if (generation is null || generation.Skills.Count == 0) return;
        var identity = CopilotExecutableIdentity.Read(executable);
        if (identity is null || rejected.ContainsKey(identity) || !generation.Verify()) return;
        parameters["skillDirectories"] = JSONValue.FromArrayLiteral(generation.Folder);
    }

    public static async Task<JSONValue> Open(string method, Dictionary<string, JSONValue> parameters, string? executable,
        Func<string, JSONValue, Task<JSONValue>> request)
    {
        ArgumentNullException.ThrowIfNull(request); // Explicit seam only; never silently run a CLI.
        var identity = parameters.ContainsKey("skillDirectories") ? CopilotExecutableIdentity.Read(executable) : null;
        var sent = new Dictionary<string, JSONValue>(parameters, SwiftStringComparer.Instance);
        // Recheck the cache for a session composed before a concurrent session learned rejection.
        if (identity is not null && rejected.ContainsKey(identity)) sent.Remove("skillDirectories");
        try { return await request(method, Object(sent)); }
        catch (RPCError error) when (sent.ContainsKey("skillDirectories") &&
            (error.Code == -32602 || error.Message.Contains("skillDirectories", StringComparison.Ordinal)))
        {
            if (identity is not null) rejected.TryAdd(identity, 0);
            sent.Remove("skillDirectories");
            return await request(method, Object(sent)); // Exactly one retry; its error propagates.
        }
    }

    private static JSONValue Object(Dictionary<string, JSONValue> parameters) =>
        new JSONValue.Object(new ValueMap<string, JSONValue>(parameters, SwiftStringComparer.Instance));
}
