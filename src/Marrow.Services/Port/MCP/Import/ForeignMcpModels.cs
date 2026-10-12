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

    /// <summary>Claude Desktop's "Claude Extensions" folder: one folder per installed extension, each with a manifest.json whose server.mcp_config says how its MCP server starts.</summary>
    ClaudeExtensions,
}

/// <summary>One file (or, for <see cref="ForeignMcpFormat.ClaudeExtensions"/>, one folder) another app keeps its servers in. <see cref="Scope"/> says whose they are when that is more than the file: a project's full path ("project:" and the full path of the folder).</summary>
public sealed record ForeignMcpPlace(ForeignMcpApp App, string Path, ForeignMcpFormat Format, string Source = "", string Scope = "");

/// <summary>An environment variable (stdio) or a header (remote) with its value as the other app has it.</summary>
public sealed record ForeignMcpVariable(string Key, string Value);

/// <summary>
/// One server another app has. <see cref="Source"/> says where in that app when it has more than one place ("User", a project folder, "Settings") and is what the row shows; <see cref="Path"/> is the file.
/// <see cref="Scope"/> is the part of the row key that tells two servers apart when <see cref="Source"/> cannot (two projects with the same folder name); empty means <see cref="Source"/>.
/// <see cref="LegacySse"/>: the other app talks to it over the older SSE connection, which nth does not speak. <see cref="Ordinal"/> is 0 for the first server with a given key and counts up for any other.
/// </summary>
public sealed record ForeignMcpServer(
    ForeignMcpApp App,
    string Source,
    string Name,
    MCPCustomServer.Kind Kind,
    string Command,
    IReadOnlyList<string> Args,
    string Url,
    IReadOnlyList<ForeignMcpVariable> Variables,
    string Path,
    string Scope = "",
    bool LegacySse = false,
    int Ordinal = 0)
{
    /// <summary>One per found server: the app, the file, the owner (scope, else source) and the name; a server that would still share it (the same name twice in one object) is told apart by its place in the file.</summary>
    public string Key => App + "|" + Path + "|" + (Scope.Length > 0 ? Scope : Source) + "|" + Name + (Ordinal > 0 ? "#" + (Ordinal + 1) : "");

    /// <summary>The project's full path when this is a project's server (its scope says so), else empty.</summary>
    public string ProjectPath => Scope.StartsWith(ProjectScope, StringComparison.Ordinal) ? Scope[ProjectScope.Length..] : "";

    public const string ProjectScope = "project:";

    /// <summary>The scope of a Claude Desktop extension's server ("extension:" and the extension's folder name).</summary>
    public const string ExtensionScope = "extension:";

    /// <summary>
    /// Whether the app this server comes from fills in ${...} placeholders itself, so that a placeholder it could not fill in is a value missing from the config. Claude Desktop and Codex do not: what their config says is
    /// handed to the program as written (a program may expand it itself, as mcp-remote does with ${NAME} in a --header argument), so it is imported as written. A Claude Desktop extension's manifest does have placeholders.
    /// </summary>
    public bool AppExpandsPlaceholders => App is not (ForeignMcpApp.ClaudeDesktop or ForeignMcpApp.Codex) || Scope.StartsWith(ExtensionScope, StringComparison.Ordinal);

    /// <summary>What the row shows: the command line, or the address.</summary>
    public string Detail => Kind == MCPCustomServer.Kind.Stdio ? string.Join(" ", new[] { Command }.Concat(Args)) : Url;
}

/// <summary>A file that is there but could not be read or parsed. It is reported, never thrown.</summary>
public sealed record ForeignMcpProblem(ForeignMcpApp App, string Path, string Reason);

public sealed record ForeignMcpScan(IReadOnlyList<ForeignMcpServer> Servers, IReadOnlyList<ForeignMcpProblem> Problems)
{
    public static ForeignMcpScan Empty { get; } = new([], []);
}
