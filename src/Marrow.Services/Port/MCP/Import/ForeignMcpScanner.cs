// PORT-ONLY: owner request 2026-10-11 (ED-NEW-mcpimport-1, -3, -4, -9, -10). Reads, and only reads, the MCP configs other AI apps keep on this PC. The places come from a home, an AppData and a LocalAppData folder
// (plus CODEX_HOME and CLAUDE_CONFIG_DIR when set), the contents through a read seam (null: the file is not there) and a list seam (the folders in a folder), so tests hand it text and never touch the real
// profile. Besides each app's own file it looks in the projects those apps know of (Claude Code's .mcp.json, .cursor, .vscode, .codex and .gemini folders of a project) and in Claude Desktop's extensions.
// A file that is there but cannot be read or parsed is reported as a problem, never thrown.
using System.Text.Json;
using Marrow.Core.Models;
using Marrow.Core.Port.MCP.Import;

namespace Marrow.Services.Port.MCP.Import;

public sealed class ForeignMcpScanner
{
    private static readonly JsonDocumentOptions Lenient = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
    private const long MaxBytes = 16L * 1024 * 1024;
    private const int MaxProjects = 300;

    /// <summary>The package of the Windows (MSIX) build of Claude Desktop; its app data is kept under the package, not under %APPDATA%.</summary>
    public const string ClaudeDesktopPackage = "Claude_pzs8sxrjxfjjc";

    private readonly IReadOnlyList<ForeignMcpPlace> places;
    private readonly Func<string, string?> read;
    private readonly Func<string, IReadOnlyList<string>> list;
    private readonly string home;

    public ForeignMcpScanner(IReadOnlyList<ForeignMcpPlace> places, Func<string, string?>? read = null, Func<string, IReadOnlyList<string>>? list = null, string? home = null)
    {
        this.places = places;
        this.read = read ?? ReadFile;
        this.list = list ?? ListDirectories;
        this.home = home ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    /// <summary>
    /// The places the scan looks in, under <paramref name="home"/> (the user's profile folder) and <paramref name="appData"/> (its Roaming AppData). With <paramref name="localAppData"/> it also looks where the
    /// packaged (MSIX) Claude Desktop keeps its data: <c>Packages\Claude_pzs8sxrjxfjjc\LocalCache\Roaming\Claude</c>, and any other <c>Claude_*</c> package (<paramref name="packageFolders"/> lists the folders
    /// of Packages). <paramref name="codexHome"/> and <paramref name="claudeConfigDir"/> are CODEX_HOME and CLAUDE_CONFIG_DIR: where those apps keep their files when the variable is set, looked in before
    /// the usual place. The packaged copy comes first because it is the one the packaged app reads.
    /// </summary>
    public static IReadOnlyList<ForeignMcpPlace> Places(string home, string appData, string? localAppData = null, string? codexHome = null, string? claudeConfigDir = null, Func<string, IReadOnlyList<string>>? packageFolders = null)
    {
        var found = new List<ForeignMcpPlace>();
        if (Rooted(claudeConfigDir))
        {
            found.Add(new(ForeignMcpApp.ClaudeCode, Path.Combine(claudeConfigDir!, ".claude.json"), ForeignMcpFormat.ClaudeJson));
            found.Add(new(ForeignMcpApp.ClaudeCode, Path.Combine(claudeConfigDir!, "settings.json"), ForeignMcpFormat.McpServersJson, McpImportWords.Settings));
        }
        found.Add(new(ForeignMcpApp.ClaudeCode, Path.Combine(home, ".claude.json"), ForeignMcpFormat.ClaudeJson));
        found.Add(new(ForeignMcpApp.ClaudeCode, Path.Combine(home, ".claude", "settings.json"), ForeignMcpFormat.McpServersJson, McpImportWords.Settings));
        foreach (var data in ClaudeDesktopFolders(appData, localAppData, packageFolders))
        {
            found.Add(new(ForeignMcpApp.ClaudeDesktop, Path.Combine(data, "claude_desktop_config.json"), ForeignMcpFormat.McpServersJson));
            found.Add(new(ForeignMcpApp.ClaudeDesktop, Path.Combine(data, "Claude Extensions"), ForeignMcpFormat.ClaudeExtensions, McpImportWords.Extension));
        }
        if (Rooted(codexHome)) found.Add(new(ForeignMcpApp.Codex, Path.Combine(codexHome!, "config.toml"), ForeignMcpFormat.CodexToml));
        found.Add(new(ForeignMcpApp.Codex, Path.Combine(home, ".codex", "config.toml"), ForeignMcpFormat.CodexToml));
        found.Add(new(ForeignMcpApp.Cursor, Path.Combine(home, ".cursor", "mcp.json"), ForeignMcpFormat.McpServersJson));
        found.Add(new(ForeignMcpApp.VSCode, Path.Combine(appData, "Code", "User", "mcp.json"), ForeignMcpFormat.VSCodeMcp, McpImportWords.User));
        found.Add(new(ForeignMcpApp.VSCode, Path.Combine(appData, "Code", "User", "settings.json"), ForeignMcpFormat.VSCodeSettings, McpImportWords.Settings));
        found.Add(new(ForeignMcpApp.Windsurf, Path.Combine(home, ".codeium", "windsurf", "mcp_config.json"), ForeignMcpFormat.McpServersJson));
        // Antigravity's CLI reads the user's servers from the same file (ProviderRegistry: GEMINI_CLI_SYSTEM_SETTINGS_PATH sits beside ~/.gemini/settings.json).
        found.Add(new(ForeignMcpApp.Gemini, Path.Combine(home, ".gemini", "settings.json"), ForeignMcpFormat.McpServersJson));
        return Distinct(found);
    }

    /// <summary>Claude Desktop's data folders: the packaged ones first (the known package, then any other Claude_* package), then the classic Roaming one.</summary>
    private static IEnumerable<string> ClaudeDesktopFolders(string appData, string? localAppData, Func<string, IReadOnlyList<string>>? packageFolders)
    {
        var folders = new List<string>();
        if (Rooted(localAppData))
        {
            var packages = Path.Combine(localAppData!, "Packages");
            var names = new List<string> { Path.Combine(packages, ClaudeDesktopPackage) };
            foreach (var folder in (packageFolders ?? ListPackages)(packages))
                if (Path.GetFileName(folder.TrimEnd('\\', '/')).StartsWith("Claude_", StringComparison.OrdinalIgnoreCase)) names.Add(folder);
            folders.AddRange(names.Select(n => Path.Combine(n, "LocalCache", "Roaming", "Claude")));
        }
        folders.Add(Path.Combine(appData, "Claude"));
        return folders;
    }

    private static IReadOnlyList<string> ListPackages(string packages)
    {
        try { return Directory.Exists(packages) ? [.. Directory.EnumerateDirectories(packages, "Claude_*")] : []; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException) { return []; }
    }

    private static bool Rooted(string? path) => !string.IsNullOrWhiteSpace(path) && Path.IsPathFullyQualified(path);

    private static List<ForeignMcpPlace> Distinct(IEnumerable<ForeignMcpPlace> all)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return [.. all.Where(p => seen.Add(p.Format + "|" + p.Path.TrimEnd('\\', '/')))];
    }

    /// <summary>The places a project folder can keep servers in (the project's own files, not the user's).</summary>
    public static IReadOnlyList<ForeignMcpPlace> ProjectPlaces(string project)
    {
        var root = ProjectRoot(project);
        var name = ProjectName(root);
        var scope = ForeignMcpServer.ProjectScope + root;
        return
        [
            new(ForeignMcpApp.ClaudeCode, Path.Combine(root, ".mcp.json"), ForeignMcpFormat.McpServersJson, name, scope),
            new(ForeignMcpApp.Cursor, Path.Combine(root, ".cursor", "mcp.json"), ForeignMcpFormat.McpServersJson, name, scope),
            new(ForeignMcpApp.VSCode, Path.Combine(root, ".vscode", "mcp.json"), ForeignMcpFormat.VSCodeMcp, name, scope),
            new(ForeignMcpApp.Codex, Path.Combine(root, ".codex", "config.toml"), ForeignMcpFormat.CodexToml, name, scope),
            new(ForeignMcpApp.Gemini, Path.Combine(root, ".gemini", "settings.json"), ForeignMcpFormat.McpServersJson, name, scope),
        ];
    }

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

    /// <summary>The default list: the folders in a folder (empty when it is not there).</summary>
    public static IReadOnlyList<string> ListDirectories(string path) => Directory.Exists(path) ? [.. Directory.EnumerateDirectories(path)] : [];

    public ForeignMcpScan Scan()
    {
        extensionsSeen.Clear();
        var servers = new List<ForeignMcpServer>();
        var problems = new List<ForeignMcpProblem>();
        var projects = new List<string>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var place in places) Visit(place, servers, problems, projects, visited);
        var seenProjects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var homeRoot = Rooted(home) ? ProjectRoot(home) : "";
        foreach (var project in projects)
        {
            var root = ProjectRoot(project);
            if (!Rooted(root) || root.StartsWith(@"\\", StringComparison.Ordinal) || string.Equals(root, homeRoot, StringComparison.OrdinalIgnoreCase) || !seenProjects.Add(root)) continue;
            if (seenProjects.Count > MaxProjects) break;
            foreach (var place in ProjectPlaces(root)) Visit(place, servers, problems, projects: null, visited);
        }
        return new ForeignMcpScan(Unique(servers), problems);
    }

    private void Visit(ForeignMcpPlace place, List<ForeignMcpServer> servers, List<ForeignMcpProblem> problems, List<string>? projects, HashSet<string> visited)
    {
        if (!visited.Add(place.Format + "|" + place.Path.TrimEnd('\\', '/'))) return;
        if (place.Format == ForeignMcpFormat.ClaudeExtensions) { Extensions(place, servers, problems); return; }
        string? text;
        try { text = read(place.Path); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException or NotSupportedException)
        {
            problems.Add(new ForeignMcpProblem(place.App, place.Path, e.Message));
            return;
        }
        if (text is null) return;
        try { servers.AddRange(Parse(place, text, projects)); }
        catch (Exception e) when (e is JsonException or FormatException or InvalidOperationException or ArgumentException)
        {
            problems.Add(new ForeignMcpProblem(place.App, place.Path, e.Message));
        }
    }

    // One folder per installed extension, each with a manifest.json; the packaged copy is read first and an extension the classic copy has again is left out (it is the same extension).
    private readonly HashSet<string> extensionsSeen = new(StringComparer.OrdinalIgnoreCase);

    private void Extensions(ForeignMcpPlace place, List<ForeignMcpServer> servers, List<ForeignMcpProblem> problems)
    {
        IReadOnlyList<string> folders;
        try { folders = list(place.Path); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException or NotSupportedException)
        {
            problems.Add(new ForeignMcpProblem(place.App, place.Path, e.Message));
            return;
        }
        var settingsFolder = Path.Combine(Path.GetDirectoryName(place.Path.TrimEnd('\\', '/')) ?? "", "Claude Extensions Settings");
        foreach (var folder in folders.Order(StringComparer.OrdinalIgnoreCase))
        {
            var id = Path.GetFileName(folder.TrimEnd('\\', '/'));
            var manifestPath = Path.Combine(folder, "manifest.json");
            string? manifest, settings = null;
            try
            {
                manifest = read(manifestPath);
                if (manifest is null) continue;
                settings = read(Path.Combine(settingsFolder, id + ".json"));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException or NotSupportedException)
            {
                problems.Add(new ForeignMcpProblem(place.App, manifestPath, e.Message));
                continue;
            }
            if (!extensionsSeen.Add(id)) continue;
            try { servers.AddRange(Extension(place, folder, manifest, settings, home)); }
            catch (Exception e) when (e is JsonException or FormatException or InvalidOperationException or ArgumentException)
            {
                problems.Add(new ForeignMcpProblem(place.App, manifestPath, e.Message));
            }
        }
    }

    /// <summary>The servers in one file's text. Throws on a file that is not the format it should be; the scan turns that into a problem.</summary>
    public static IReadOnlyList<ForeignMcpServer> Parse(ForeignMcpPlace place, string text) => Parse(place, text, null);

    // projects: where the project folders this file knows of are added (Claude Code's projects, Codex's [projects]), so the scan can look in them too.
    private static IReadOnlyList<ForeignMcpServer> Parse(ForeignMcpPlace place, string text, List<string>? projects)
    {
        if (place.Format == ForeignMcpFormat.CodexToml) return Codex(place, text, projects);
        using var document = JsonDocument.Parse(text.Length > 0 && text[0] == '﻿' ? text[1..] : text, Lenient);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new FormatException("The file is not a JSON object.");
        var found = new List<ForeignMcpServer>();
        switch (place.Format)
        {
            case ForeignMcpFormat.ClaudeJson:
                if (root.TryGetProperty("mcpServers", out var top)) found.AddRange(Servers(place, top, McpImportWords.User, place.Scope));
                if (root.TryGetProperty("projects", out var all) && all.ValueKind == JsonValueKind.Object)
                    foreach (var project in all.EnumerateObject())
                    {
                        projects?.Add(project.Name);
                        if (project.Value.ValueKind == JsonValueKind.Object && project.Value.TryGetProperty("mcpServers", out var own))
                            found.AddRange(Servers(place, own, ProjectName(project.Name), ForeignMcpServer.ProjectScope + ProjectRoot(project.Name)));
                    }
                break;
            case ForeignMcpFormat.McpServersJson:
                if (root.TryGetProperty("mcpServers", out var servers)) found.AddRange(Servers(place, servers, place.Source, place.Scope));
                break;
            case ForeignMcpFormat.VSCodeMcp:
                if (root.TryGetProperty("servers", out var vscode)) found.AddRange(Servers(place, vscode, place.Source, place.Scope));
                break;
            case ForeignMcpFormat.VSCodeSettings:
                // the nested object and the flat key describe the same setting; a name in both is one server (the nested one is kept)
                if (root.TryGetProperty("mcp", out var mcp) && mcp.ValueKind == JsonValueKind.Object && mcp.TryGetProperty("servers", out var nested)) found.AddRange(Servers(place, nested, place.Source, place.Scope));
                if (root.TryGetProperty("mcp.servers", out var flat))
                {
                    var names = found.Select(s => s.Name).ToHashSet(StringComparer.Ordinal);
                    found.AddRange(Servers(place, flat, place.Source, place.Scope).Where(s => !names.Contains(s.Name)));
                }
                break;
        }
        return found;
    }

    private static string ProjectRoot(string path)
    {
        var slashed = path.Trim().Replace('/', '\\');
        return slashed.Length > 3 ? slashed.TrimEnd('\\') : slashed;
    }

    private static string ProjectName(string path)
    {
        var trimmed = path.TrimEnd('/', '\\');
        var cut = trimmed.LastIndexOfAny(['/', '\\']);
        return cut >= 0 && cut < trimmed.Length - 1 ? trimmed[(cut + 1)..] : trimmed;
    }

    private static IEnumerable<ForeignMcpServer> Servers(ForeignMcpPlace place, JsonElement servers, string source, string scope)
    {
        if (servers.ValueKind != JsonValueKind.Object) yield break;
        foreach (var server in servers.EnumerateObject())
            if (server.Value.ValueKind == JsonValueKind.Object && Server(place, source, scope, server.Name, server.Value) is { } found)
                yield return found;
    }

    // stdio: "command" (+ "args", "env"); remote: "url" (Claude, Cursor, VS Code, Gemini's SSE), "serverUrl" (Windsurf) or "httpUrl" (Gemini), with "headers". "type" (or "transport") picks when both are there.
    // "sse" (and Gemini's "url", which is its SSE address) is the older connection nth does not speak: the server is found, marked, and not imported.
    private static ForeignMcpServer? Server(ForeignMcpPlace place, string source, string scope, string name, JsonElement e)
    {
        var type = (Text(e, "type") ?? Text(e, "transport") ?? "").Trim().ToLowerInvariant();
        var command = (Text(e, "command") ?? "").Trim();
        var httpUrl = (Text(e, "httpUrl") ?? "").Trim();
        var plainUrl = (Text(e, "url") ?? "").Trim();
        var url = httpUrl.Length > 0 ? httpUrl : plainUrl.Length > 0 ? plainUrl : (Text(e, "serverUrl") ?? "").Trim();
        var remote = type is "http" or "sse" or "streamable-http" or "streamablehttp" || (command.Length == 0 && url.Length > 0);
        if (remote && url.Length == 0 || !remote && command.Length == 0) return null; // nothing to run or reach
        if (remote)
        {
            var legacy = type == "sse" || (place.App == ForeignMcpApp.Gemini && httpUrl.Length == 0 && plainUrl.Length > 0 && type is not ("http" or "streamable-http" or "streamablehttp"));
            return new ForeignMcpServer(place.App, source, name, MCPCustomServer.Kind.Http, "", [], url, Map(e, "headers"), place.Path, scope, legacy);
        }
        var args = e.TryGetProperty("args", out var list) && list.ValueKind == JsonValueKind.Array
            ? list.EnumerateArray().Select(a => a.ValueKind == JsonValueKind.String ? a.GetString()! : a.GetRawText()).ToArray()
            : [];
        return new ForeignMcpServer(place.App, source, name, MCPCustomServer.Kind.Stdio, command, args, "", Map(e, "env"), place.Path, scope);
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

    // [mcp_servers.NAME]: command, args, env (an inline table or [mcp_servers.NAME.env]); or url with http_headers. [projects.PATH] tables name the projects Codex knows of.
    private static IReadOnlyList<ForeignMcpServer> Codex(ForeignMcpPlace place, string text, List<string>? projects)
    {
        var root = TomlLite.Parse(text);
        if (projects is not null && root.TryGetValue("projects", out var known) && known is Dictionary<string, object> known2) projects.AddRange(known2.Keys);
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
                found.Add(new ForeignMcpServer(place.App, place.Source, name, MCPCustomServer.Kind.Stdio, command, args, "", Variables(table.GetValueOrDefault("env")), place.Path, place.Scope));
            }
            else if (url.Length > 0)
                found.Add(new ForeignMcpServer(place.App, place.Source, name, MCPCustomServer.Kind.Http, "", [], url, Variables(table.GetValueOrDefault("http_headers")), place.Path, place.Scope));
        }
        return found;
    }

    private static IReadOnlyList<ForeignMcpVariable> Variables(object? value) =>
        value is Dictionary<string, object> map ? [.. map.Where(p => p.Value is string).Select(p => new ForeignMcpVariable(p.Key, (string)p.Value))] : [];

    /// <summary>
    /// The server a Claude Desktop extension (a folder with a manifest.json) defines in <c>server.mcp_config</c> (with the win32 override applied). Its own placeholders are filled in as Claude Desktop does:
    /// <c>${__dirname}</c> is the extension's folder, <c>${HOME}</c>, <c>${DESKTOP}</c>, <c>${DOCUMENTS}</c>, <c>${DOWNLOADS}</c> the user's folders, <c>${user_config.KEY}</c> the value the user set (or the
    /// manifest's default). An optional setting nobody set leaves its variable or argument out; a required one stays as written, which the import then reports as a value it needs. A disabled extension has none.
    /// </summary>
    public static IReadOnlyList<ForeignMcpServer> Extension(ForeignMcpPlace place, string folder, string manifest, string? settings, string home)
    {
        using var document = JsonDocument.Parse(manifest.Length > 0 && manifest[0] == '﻿' ? manifest[1..] : manifest, Lenient);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new FormatException("The manifest is not a JSON object.");
        JsonDocument? settingsDocument = null;
        try
        {
            JsonElement user = default;
            if (!string.IsNullOrWhiteSpace(settings))
            {
                settingsDocument = JsonDocument.Parse(settings, Lenient);
                var s = settingsDocument.RootElement;
                if (s.ValueKind == JsonValueKind.Object)
                {
                    if (s.TryGetProperty("isEnabled", out var enabled) && enabled.ValueKind == JsonValueKind.False) return [];
                    if (s.TryGetProperty("userConfig", out var u) && u.ValueKind == JsonValueKind.Object) user = u;
                }
            }
            if (!root.TryGetProperty("server", out var server) || server.ValueKind != JsonValueKind.Object || !server.TryGetProperty("mcp_config", out var config) || config.ValueKind != JsonValueKind.Object) return [];
            var id = Path.GetFileName(folder.TrimEnd('\\', '/'));
            var name = (Text(root, "name") ?? id).Trim();
            var label = (Text(root, "display_name") ?? name).Trim();
            if (name.Length == 0) name = id;
            var definitions = root.TryGetProperty("user_config", out var d) && d.ValueKind == JsonValueKind.Object ? d : default;
            var context = new ExtensionContext(folder, home, user, definitions);

            var command = Text(config, "command");
            JsonElement args = config.TryGetProperty("args", out var a) ? a : default, env = config.TryGetProperty("env", out var v) ? v : default;
            if (config.TryGetProperty("platform_overrides", out var overrides) && overrides.ValueKind == JsonValueKind.Object && overrides.TryGetProperty("win32", out var win) && win.ValueKind == JsonValueKind.Object)
            {
                command = Text(win, "command") ?? command;
                if (win.TryGetProperty("args", out var wa)) args = wa;
                if (win.TryGetProperty("env", out var we)) env = we;
            }
            command = command is null ? null : context.Text(command)?.Trim();
            if (string.IsNullOrEmpty(command)) return [];
            var argList = new List<string>();
            if (args.ValueKind == JsonValueKind.Array)
                foreach (var arg in args.EnumerateArray())
                    if (arg.ValueKind == JsonValueKind.String) argList.AddRange(context.Args(arg.GetString()!));
                    else argList.Add(arg.GetRawText());
            var variables = new List<ForeignMcpVariable>();
            if (env.ValueKind == JsonValueKind.Object)
                foreach (var pair in env.EnumerateObject())
                    if (pair.Value.ValueKind != JsonValueKind.String) variables.Add(new ForeignMcpVariable(pair.Name, pair.Value.ValueKind == JsonValueKind.Null ? "" : pair.Value.GetRawText()));
                    else if (context.Text(pair.Value.GetString()!) is { } text) variables.Add(new ForeignMcpVariable(pair.Name, text));
            return [new ForeignMcpServer(place.App, label, name, MCPCustomServer.Kind.Stdio, command, argList, "", variables, Path.Combine(folder, "manifest.json"), ForeignMcpServer.ExtensionScope + id)];
        }
        finally { settingsDocument?.Dispose(); }
    }

    // The placeholders of an extension's manifest. The documents stay alive for the whole call (the elements point into them).
    private sealed class ExtensionContext(string folder, string home, JsonElement user, JsonElement definitions)
    {
        private static readonly System.Text.RegularExpressions.Regex Braces = new(@"\$\{([^{}]*)\}", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

        // null: an optional setting nobody set, so the value is left out. A required one nobody set stays as written.
        public string? Text(string text)
        {
            var dropped = false;
            var result = Braces.Replace(text, match =>
            {
                var value = Value(match.Groups[1].Value.Trim(), out var drop);
                if (drop) dropped = true;
                return value ?? match.Value;
            });
            return dropped ? null : result;
        }

        // an argument: a list setting that is the whole argument becomes several; an optional setting nobody set leaves the argument out
        public IEnumerable<string> Args(string text)
        {
            var whole = Braces.Match(text);
            if (whole.Success && whole.Length == text.Length && whole.Groups[1].Value.Trim() is var body && body.StartsWith("user_config.", StringComparison.Ordinal)
                && Setting(body["user_config.".Length..]) is { ValueKind: JsonValueKind.Array } list)
                return list.EnumerateArray().Select(item => item.ValueKind == JsonValueKind.String ? Braces.Replace(item.GetString()!, match => Builtin(match.Groups[1].Value.Trim()) ?? match.Value) : item.GetRawText());
            return Text(text) is { } one ? [one] : [];
        }

        private string? Value(string name, out bool drop)
        {
            drop = false;
            if (Builtin(name) is { } known) return known;
            if (!name.StartsWith("user_config.", StringComparison.Ordinal)) return null;
            var key = name["user_config.".Length..];
            if (Setting(key) is { } set) return Plain(set);
            if (definitions.ValueKind == JsonValueKind.Object && definitions.TryGetProperty(key, out var definition) && definition.ValueKind == JsonValueKind.Object)
            {
                var required = definition.TryGetProperty("required", out var r) && r.ValueKind == JsonValueKind.True;
                if (!required) drop = true;
            }
            return null;
        }

        private string? Builtin(string name) => name switch
        {
            "__dirname" => folder,
            "HOME" => home,
            "DESKTOP" => Path.Combine(home, "Desktop"),
            "DOCUMENTS" => Path.Combine(home, "Documents"),
            "DOWNLOADS" => Path.Combine(home, "Downloads"),
            "pathSeparator" or "/" => @"\",
            _ => null,
        };

        // a setting as text; the manifest's own words in it (a default of "${DOCUMENTS}/notes") are filled in, but a setting never refers to another
        private string Plain(JsonElement set) => set.ValueKind switch
        {
            JsonValueKind.String => Braces.Replace(set.GetString()!, match => Builtin(match.Groups[1].Value.Trim()) ?? match.Value),
            JsonValueKind.Array => string.Join(",", set.EnumerateArray().Select(i => i.ValueKind == JsonValueKind.String ? Braces.Replace(i.GetString()!, match => Builtin(match.Groups[1].Value.Trim()) ?? match.Value) : i.GetRawText())),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => set.GetRawText(),
        };

        // what the user set, else the manifest's default
        private JsonElement? Setting(string key)
        {
            if (user.ValueKind == JsonValueKind.Object && user.TryGetProperty(key, out var set) && set.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined)) return set;
            if (definitions.ValueKind == JsonValueKind.Object && definitions.TryGetProperty(key, out var definition) && definition.ValueKind == JsonValueKind.Object
                && definition.TryGetProperty("default", out var fallback) && fallback.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined)) return fallback;
            return null;
        }
    }

    // The same server twice (an identical definition under the same app and owner, found in two files: the packaged and the classic Claude Desktop config, a CLAUDE_CONFIG_DIR that is also the default)
    // is one row; every row's key is then unique, so ticking one never ticks another.
    private static List<ForeignMcpServer> Unique(List<ForeignMcpServer> servers)
    {
        var definitions = new HashSet<string>(StringComparer.Ordinal);
        var keys = new Dictionary<string, int>(StringComparer.Ordinal);
        var result = new List<ForeignMcpServer>(servers.Count);
        foreach (var server in servers)
        {
            var definition = string.Join("\u001f", server.App, server.Scope.Length > 0 ? server.Scope.ToLowerInvariant() : server.Source, server.Name, server.Kind, server.Command, string.Join("\u001e", server.Args), server.Url, server.LegacySse,
                string.Join("\u001e", server.Variables.Select(v => v.Key + "=" + v.Value)));
            if (!definitions.Add(definition)) continue;
            var count = keys.GetValueOrDefault(server.Key);
            keys[server.Key] = count + 1;
            result.Add(count == 0 ? server : server with { Ordinal = count });
        }
        return result;
    }
}
