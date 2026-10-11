// PORT-ONLY: owner request 2026-10-11 (ED-NEW-mcpimport-1). What the read-only scan of other AI apps' MCP configs finds: servers, and files it could not read.
using Marrow.Core.Models;

namespace Marrow.Services.Port.MCP.Import;

public enum ForeignMcpApp
{
    ClaudeCode,
    ClaudeDesktop,
    Codex,
    Cursor,
    VSCode,
    Windsurf,
    Gemini,
}

/// <summary>How a place's file is laid out.</summary>
public enum ForeignMcpFormat
{
    /// <summary>~/.claude.json: top-level "mcpServers" and "projects.*.mcpServers".</summary>
    ClaudeJson,

    /// <summary>A JSON file with a top-level "mcpServers" object (Claude Desktop, Cursor, Windsurf, Gemini CLI, Claude Code settings).</summary>
    McpServersJson,

    /// <summary>VS Code's mcp.json: a top-level "servers" object (JSON with comments).</summary>
    VSCodeMcp,

    /// <summary>VS Code's settings.json: "mcp": { "servers": ... } or the flat key "mcp.servers" (JSON with comments).</summary>
    VSCodeSettings,

    /// <summary>~/.codex/config.toml: [mcp_servers.NAME] tables.</summary>
    CodexToml,
}

/// <summary>One file another app keeps its servers in.</summary>
public sealed record ForeignMcpPlace(ForeignMcpApp App, string Path, ForeignMcpFormat Format, string Source = "");

/// <summary>An environment variable (stdio) or a header (remote) with its value as the other app has it.</summary>
public sealed record ForeignMcpVariable(string Key, string Value);

/// <summary>One server another app has. <see cref="Source"/> says where in that app when it has more than one place ("User", a project folder, "Settings"); <see cref="Path"/> is the file.</summary>
public sealed record ForeignMcpServer(
    ForeignMcpApp App,
    string Source,
    string Name,
    MCPCustomServer.Kind Kind,
    string Command,
    IReadOnlyList<string> Args,
    string Url,
    IReadOnlyList<ForeignMcpVariable> Variables,
    string Path)
{
    public string Key => App + "|" + Path + "|" + Source + "|" + Name;

    /// <summary>What the row shows: the command line, or the address.</summary>
    public string Detail => Kind == MCPCustomServer.Kind.Stdio ? string.Join(" ", new[] { Command }.Concat(Args)) : Url;
}

/// <summary>A file that is there but could not be read or parsed. It is reported, never thrown.</summary>
public sealed record ForeignMcpProblem(ForeignMcpApp App, string Path, string Reason);

public sealed record ForeignMcpScan(IReadOnlyList<ForeignMcpServer> Servers, IReadOnlyList<ForeignMcpProblem> Problems)
{
    public static ForeignMcpScan Empty { get; } = new([], []);
}
