// PORT-ONLY: owner request 2026-10-11 (ED-NEW-mcpimport-1). Reads, and only reads, the MCP configs other AI apps keep on this PC. The places come from a home and an AppData folder, the contents
// through a read seam (null: the file is not there), so tests hand it text and never touch the real profile. A file that is there but cannot be read or parsed is reported as a problem, never thrown.
using System.Text.Json;
using Marrow.Core.Models;
using Marrow.Core.Port.MCP.Import;

namespace Marrow.Services.Port.MCP.Import;

public sealed class ForeignMcpScanner
{
    private static readonly JsonDocumentOptions Lenient = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
    private const long MaxBytes = 16L * 1024 * 1024;

    private readonly IReadOnlyList<ForeignMcpPlace> places;
    private readonly Func<string, string?> read;

    public ForeignMcpScanner(IReadOnlyList<ForeignMcpPlace> places, Func<string, string?>? read = null)
    {
        this.places = places;
        this.read = read ?? ReadFile;
    }

    /// <summary>The places the scan looks in, under <paramref name="home"/> (the user's profile folder) and <paramref name="appData"/> (its Roaming AppData).</summary>
    public static IReadOnlyList<ForeignMcpPlace> Places(string home, string appData) =>
    [
        new(ForeignMcpApp.ClaudeCode, Path.Combine(home, ".claude.json"), ForeignMcpFormat.ClaudeJson),
        new(ForeignMcpApp.ClaudeCode, Path.Combine(home, ".claude", "settings.json"), ForeignMcpFormat.McpServersJson, McpImportWords.Settings),
        new(ForeignMcpApp.ClaudeDesktop, Path.Combine(appData, "Claude", "claude_desktop_config.json"), ForeignMcpFormat.McpServersJson),
        new(ForeignMcpApp.Codex, Path.Combine(home, ".codex", "config.toml"), ForeignMcpFormat.CodexToml),
        new(ForeignMcpApp.Cursor, Path.Combine(home, ".cursor", "mcp.json"), ForeignMcpFormat.McpServersJson),
        new(ForeignMcpApp.VSCode, Path.Combine(appData, "Code", "User", "mcp.json"), ForeignMcpFormat.VSCodeMcp, McpImportWords.User),
        new(ForeignMcpApp.VSCode, Path.Combine(appData, "Code", "User", "settings.json"), ForeignMcpFormat.VSCodeSettings, McpImportWords.Settings),
        new(ForeignMcpApp.Windsurf, Path.Combine(home, ".codeium", "windsurf", "mcp_config.json"), ForeignMcpFormat.McpServersJson),
        // Antigravity's CLI reads the user's servers from the same file (ProviderRegistry: GEMINI_CLI_SYSTEM_SETTINGS_PATH sits beside ~/.gemini/settings.json).
        new(ForeignMcpApp.Gemini, Path.Combine(home, ".gemini", "settings.json"), ForeignMcpFormat.McpServersJson),
    ];

    /// <summary>The default read: the file's text, or null when it is not there. Anything else (a locked file, no access, too big) throws and is reported.</summary>
    public static string? ReadFile(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists) return null;
        if (info.Length > MaxBytes) throw new IOException("The file is too big.");
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    public ForeignMcpScan Scan()
    {
        var servers = new List<ForeignMcpServer>();
        var problems = new List<ForeignMcpProblem>();
        foreach (var place in places)
        {
            string? text;
            try { text = read(place.Path); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException or NotSupportedException)
            {
                problems.Add(new ForeignMcpProblem(place.App, place.Path, e.Message));
                continue;
            }
            if (text is null) continue;
            try { servers.AddRange(Parse(place, text)); }
            catch (Exception e) when (e is JsonException or FormatException or InvalidOperationException or ArgumentException)
            {
                problems.Add(new ForeignMcpProblem(place.App, place.Path, e.Message));
            }
        }
        return new ForeignMcpScan(servers, problems);
    }

    /// <summary>The servers in one file's text. Throws on a file that is not the format it should be; the scan turns that into a problem.</summary>
    public static IReadOnlyList<ForeignMcpServer> Parse(ForeignMcpPlace place, string text)
    {
        if (place.Format == ForeignMcpFormat.CodexToml) return Codex(place, text);
        using var document = JsonDocument.Parse(text.Length > 0 && text[0] == '﻿' ? text[1..] : text, Lenient);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new FormatException("The file is not a JSON object.");
        var found = new List<ForeignMcpServer>();
        switch (place.Format)
        {
            case ForeignMcpFormat.ClaudeJson:
                if (root.TryGetProperty("mcpServers", out var top)) found.AddRange(Servers(place, top, McpImportWords.User));
                if (root.TryGetProperty("projects", out var projects) && projects.ValueKind == JsonValueKind.Object)
                    foreach (var project in projects.EnumerateObject())
                        if (project.Value.ValueKind == JsonValueKind.Object && project.Value.TryGetProperty("mcpServers", out var own))
                            found.AddRange(Servers(place, own, ProjectName(project.Name)));
                break;
            case ForeignMcpFormat.McpServersJson:
                if (root.TryGetProperty("mcpServers", out var servers)) found.AddRange(Servers(place, servers, place.Source));
                break;
            case ForeignMcpFormat.VSCodeMcp:
                if (root.TryGetProperty("servers", out var vscode)) found.AddRange(Servers(place, vscode, place.Source));
                break;
            case ForeignMcpFormat.VSCodeSettings:
                if (root.TryGetProperty("mcp", out var mcp) && mcp.ValueKind == JsonValueKind.Object && mcp.TryGetProperty("servers", out var nested)) found.AddRange(Servers(place, nested, place.Source));
                if (root.TryGetProperty("mcp.servers", out var flat)) found.AddRange(Servers(place, flat, place.Source));
                break;
        }
        return found;
    }

    private static string ProjectName(string path)
    {
        var trimmed = path.TrimEnd('/', '\\');
        var cut = trimmed.LastIndexOfAny(['/', '\\']);
        return cut >= 0 && cut < trimmed.Length - 1 ? trimmed[(cut + 1)..] : trimmed;
    }

    private static IEnumerable<ForeignMcpServer> Servers(ForeignMcpPlace place, JsonElement servers, string source)
    {
        if (servers.ValueKind != JsonValueKind.Object) yield break;
        foreach (var server in servers.EnumerateObject())
            if (server.Value.ValueKind == JsonValueKind.Object && Server(place, source, server.Name, server.Value) is { } found)
                yield return found;
    }

    // stdio: "command" (+ "args", "env"); remote: "url" (Claude, Cursor, VS Code, Gemini's SSE), "serverUrl" (Windsurf) or "httpUrl" (Gemini), with "headers". "type" (or "transport") picks when both are there.
    private static ForeignMcpServer? Server(ForeignMcpPlace place, string source, string name, JsonElement e)
    {
        var type = (Text(e, "type") ?? Text(e, "transport") ?? "").Trim().ToLowerInvariant();
        var command = (Text(e, "command") ?? "").Trim();
        var url = (Text(e, "url") ?? Text(e, "serverUrl") ?? Text(e, "httpUrl") ?? "").Trim();
        var remote = type is "http" or "sse" or "streamable-http" or "streamablehttp" || (command.Length == 0 && url.Length > 0);
        if (remote && url.Length == 0 || !remote && command.Length == 0) return null; // nothing to run or reach
        if (remote) return new ForeignMcpServer(place.App, source, name, MCPCustomServer.Kind.Http, "", [], url, Map(e, "headers"), place.Path);
        var args = e.TryGetProperty("args", out var list) && list.ValueKind == JsonValueKind.Array
            ? list.EnumerateArray().Select(a => a.ValueKind == JsonValueKind.String ? a.GetString()! : a.GetRawText()).ToArray()
            : [];
        return new ForeignMcpServer(place.App, source, name, MCPCustomServer.Kind.Stdio, command, args, "", Map(e, "env"), place.Path);
    }

    private static string? Text(JsonElement e, string key) => e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static IReadOnlyList<ForeignMcpVariable> Map(JsonElement e, string key)
    {
        if (!e.TryGetProperty(key, out var map) || map.ValueKind != JsonValueKind.Object) return [];
        return [.. map.EnumerateObject().Select(p => new ForeignMcpVariable(p.Name, p.Value.ValueKind switch
        {
            JsonValueKind.String => p.Value.GetString()!,
            JsonValueKind.Null => "",
            _ => p.Value.GetRawText(),
        }))];
    }

    // [mcp_servers.NAME]: command, args, env (an inline table or [mcp_servers.NAME.env]); or url with http_headers.
    private static IReadOnlyList<ForeignMcpServer> Codex(ForeignMcpPlace place, string text)
    {
        var root = TomlLite.Parse(text);
        if (!root.TryGetValue("mcp_servers", out var all) || all is not Dictionary<string, object> servers) return [];
        var found = new List<ForeignMcpServer>();
        foreach (var (name, value) in servers)
        {
            if (value is not Dictionary<string, object> table) continue;
            var command = (table.GetValueOrDefault("command") as string ?? "").Trim();
            var url = (table.GetValueOrDefault("url") as string ?? "").Trim();
            if (command.Length > 0)
            {
                var args = table.GetValueOrDefault("args") is List<object> list ? list.Select(a => a as string ?? a.ToString() ?? "").ToArray() : [];
                found.Add(new ForeignMcpServer(place.App, place.Source, name, MCPCustomServer.Kind.Stdio, command, args, "", Variables(table.GetValueOrDefault("env")), place.Path));
            }
            else if (url.Length > 0)
                found.Add(new ForeignMcpServer(place.App, place.Source, name, MCPCustomServer.Kind.Http, "", [], url, Variables(table.GetValueOrDefault("http_headers")), place.Path));
        }
        return found;
    }

    private static IReadOnlyList<ForeignMcpVariable> Variables(object? value) =>
        value is Dictionary<string, object> map ? [.. map.Where(p => p.Value is string).Select(p => new ForeignMcpVariable(p.Key, (string)p.Value))] : [];
}
