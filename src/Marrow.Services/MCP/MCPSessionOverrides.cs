using System.Text;
using System.Text.Json;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Models;

// Source: DroppyCode/Services/MCP/MCPSessionOverrides.swift (B2 1fbf81e), :1-107.

namespace Marrow.Services.MCP;

public static class MCPSessionOverrides
{
    // Testing support: allows overriding the claude.json path
    private static string? testClaudeConfigPath;

    // Testing support: allows overriding the copilot config path
    private static string? testCopilotConfigPath;

    internal static void SetClaudeConfigPathForTesting(string path)
    {
        testClaudeConfigPath = path;
    }

    internal static void ResetClaudeConfigPathForTesting()
    {
        testClaudeConfigPath = null;
    }

    internal static void SetCopilotConfigPathForTesting(string path)
    {
        testCopilotConfigPath = path;
    }

    internal static void ResetCopilotConfigPathForTesting()
    {
        testCopilotConfigPath = null;
    }

    // :4-12. PR-MCP-173. Extract server names from the user's MCP config, sorted.
    public static IReadOnlyList<string> CopilotUserServerNames()
    {
        // :5-6. ~/.copilot/mcp-config.json
        var url = testCopilotConfigPath ?? BuildCopilotConfigPath();

        // :7-10. Parse the JSON and extract mcpServers keys
        var data = SwiftTry.Optional(() => File.ReadAllBytes(url));
        if (data is null)
        {
            return Array.Empty<string>();
        }

        var root = JSONValue.Parse(data);
        if (root is null)
        {
            return Array.Empty<string>();
        }

        // :11. Sort and return the keys
        var servers = root["mcpServers"]?.JSONValueObject;
        if (servers is null || servers.Count == 0)
        {
            return Array.Empty<string>();
        }

        return servers.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
    }

    // :14-30. PR-MCP-173, PR-PROVCLI-136. Build copilot arguments with config inline, disable user servers by name.
    public static IReadOnlyList<string> CopilotArguments()
    {
        var json = MCPProviderConfig.CopilotConfigJSON();
        return CopilotArguments(json);
    }

    // Internal overload for testing
    internal static IReadOnlyList<string> CopilotArguments(string? json)
    {
        // :15. Return empty immediately when copilotConfigJSON is nil
        if (json is null)
        {
            return Array.Empty<string>();
        }

        // :16-21. Extract the ids (keys of mcpServers) from the JSON
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var parsed = JSONValue.Parse(json);
        if (parsed is not null)
        {
            var servers = parsed["mcpServers"]?.JSONValueObject;
            if (servers is not null)
            {
                foreach (var key in servers.Keys)
                {
                    ids.Add(key);
                }
            }
        }

        // :22-29. Build arguments in exact order
        var arguments = new List<string>();

        // :22. First: "--additional-mcp-config" and the JSON as one token
        arguments.Add("--additional-mcp-config");
        arguments.Add(json);

        // :23-24. Then: disable each user server not in the config ids
        var userServerNames = CopilotUserServerNames();
        foreach (var name in userServerNames)
        {
            if (!ids.Contains(name))
            {
                arguments.Add("--disable-mcp-server");
                arguments.Add(name);
            }
        }

        // :26-27. Finally: "--disable-builtin-mcps" only when github is in ids
        if (ids.Contains("github"))
        {
            arguments.Add("--disable-builtin-mcps");
        }

        return arguments;
    }

    // :32-71. PR-MCP-174. Build OpenCode environment from claude.json, with command as array.
    public static IReadOnlyDictionary<string, string> OpencodeEnvironment()
    {
        var claudeConfigPath = testClaudeConfigPath ?? MCPPaths.ClaudeConfigURL;

        // :33-36. Guard against missing, unparseable, or empty mcpServers
        var data = SwiftTry.Optional(() => File.ReadAllBytes(claudeConfigPath));
        if (data is null)
        {
            return new Dictionary<string, string>();
        }

        var root = JSONValue.Parse(data);
        if (root is null)
        {
            return new Dictionary<string, string>();
        }

        var servers = root["mcpServers"]?.JSONValueObject;
        if (servers is null || servers.Count == 0)
        {
            return new Dictionary<string, string>();
        }

        // :37-82. Build the mcp object
        var mcp = new Dictionary<string, JSONValue>();

        foreach (var (id, entry) in servers)
        {
            if (entry is not JSONValue.Object obj)
            {
                continue;
            }

            // :40-48. HTTP type
            if (entry["type"]?.JSONValueString == "http")
            {
                var remote = BuildHttpEntry(entry);
                if (remote is not null)
                {
                    mcp[id] = remote;
                }
            }
            // :50-61. Local type (no "type" key, or type != "http")
            else
            {
                var local = BuildLocalEntry(entry);
                if (local is not null)
                {
                    mcp[id] = local;
                }
            }
        }

        // :62. Return empty if nothing was built
        if (mcp.Count == 0)
        {
            return new Dictionary<string, string>();
        }

        // :63-68. Build root object with sorted keys
        var rootObject = new Dictionary<string, JSONValue>
        {
            ["$schema"] = new JSONValue.String("https://opencode.ai/config.json"),
            ["mcp"] = new JSONValue.Object(
                new ValueMap<string, JSONValue>(
                    mcp.OrderBy(k => k.Key, StringComparer.Ordinal),
                    SwiftStringComparer.Instance))
        };

        var rootValue = new JSONValue.Object(
            new ValueMap<string, JSONValue>(rootObject, SwiftStringComparer.Instance));

        var jsonBytes = rootValue.Data(pretty: false);
        var json = Encoding.UTF8.GetString(jsonBytes);

        // :62. Return empty if building failed (all nulls become "null" string)
        if (json == "null")
        {
            return new Dictionary<string, string>();
        }

        return new Dictionary<string, string>
        {
            ["OPENCODE_CONFIG_CONTENT"] = json
        };
    }

    // :40-48. Build HTTP remote entry
    private static JSONValue? BuildHttpEntry(JSONValue entry)
    {
        // :43. url - empty string if missing
        var url = entry["url"]?.JSONValueString ?? "";

        var headers = entry["headers"]?.JSONValueObject;
        var headerDict = new Dictionary<string, JSONValue>();
        if (headers is not null)
        {
            foreach (var (key, value) in headers)
            {
                if (value is JSONValue.String strValue)
                {
                    headerDict[key] = strValue;
                }
            }
        }

        var entryDict = new Dictionary<string, JSONValue>
        {
            ["type"] = new JSONValue.String("remote"),
            ["url"] = new JSONValue.String(url),
            ["enabled"] = new JSONValue.Bool(true),
        };

        // :46-47. headers only when non-empty
        if (headerDict.Count > 0)
        {
            entryDict["headers"] = new JSONValue.Object(
                new ValueMap<string, JSONValue>(
                    headerDict.OrderBy(k => k.Key, StringComparer.Ordinal),
                    SwiftStringComparer.Instance));
        }

        return new JSONValue.Object(
            new ValueMap<string, JSONValue>(entryDict, SwiftStringComparer.Instance));
    }

    // :50-61. Build local entry with command as array
    private static JSONValue? BuildLocalEntry(JSONValue entry)
    {
        // :50. Require non-empty command
        var command = entry["command"]?.JSONValueString;
        if (string.IsNullOrEmpty(command))
        {
            return null;
        }

        // :54. command as array: [executable] + args
        var commandArray = new List<JSONValue> { new JSONValue.String(command) };

        // :51. args
        if (entry["args"] is JSONValue.Array argsValue)
        {
            foreach (var arg in argsValue.Value)
            {
                if (arg is JSONValue.String strArg)
                {
                    commandArray.Add(strArg);
                }
            }
        }

        var env = entry["env"]?.JSONValueObject;
        var envDict = new Dictionary<string, JSONValue>();
        if (env is not null)
        {
            foreach (var (key, value) in env)
            {
                if (value is JSONValue.String strValue)
                {
                    envDict[key] = strValue;
                }
            }
        }

        var entryDict = new Dictionary<string, JSONValue>
        {
            ["type"] = new JSONValue.String("local"),
            ["command"] = new JSONValue.Array(new ValueList<JSONValue>(commandArray)),
            ["enabled"] = new JSONValue.Bool(true),
        };

        // :57-58. environment (not env) only when non-empty
        if (envDict.Count > 0)
        {
            entryDict["environment"] = new JSONValue.Object(
                new ValueMap<string, JSONValue>(
                    envDict.OrderBy(k => k.Key, StringComparer.Ordinal),
                    SwiftStringComparer.Instance));
        }

        return new JSONValue.Object(
            new ValueMap<string, JSONValue>(entryDict, SwiftStringComparer.Instance));
    }

    // Helper to build the copilot config path
    private static string BuildCopilotConfigPath()
    {
        var homeDir = LoginEnvironment.HomeDirectory;
        return SwiftPath.AppendingPathComponent(
            SwiftPath.AppendingPathComponent(homeDir, ".copilot"),
            "mcp-config.json");
    }
}
