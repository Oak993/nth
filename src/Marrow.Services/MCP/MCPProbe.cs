using System.Diagnostics;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;

// Source: DroppyCode/Services/MCP/MCPProbe.swift (B3 52bc7926), :1-234. B3_PLAN.md N5: probeStdio and probeRemote
// now go through MCPWire.cs and MCPStdioConnection (MCPWire.swift) instead of building requests and driving the
// child process or the HTTP POST here directly; MCPProbeError, IsGitLabMCP and GitLabServerDisabled are unchanged
// apart from their citations.

namespace Marrow.Services.MCP;

// :3-49. An error enum keeps its cases as a nested record union (R-ERR-3).
public sealed class MCPProbeError(MCPProbeError.Case errorCase) : SwiftError
{
    public abstract record Case
    {
        private Case() { }
        public sealed record CommandMissing(string Name) : Case;       // :4
        public sealed record Exited(int Code, string Stderr) : Case;   // :5
        public sealed record TimedOut : Case;                          // :6
        public sealed record Http(int Status, string Body) : Case;     // :7
        public sealed record UnauthorizedOAuth : Case;                 // :8
        public sealed record NeedsSetup(MCPSetupGuide Guide) : Case;   // :11
        public sealed record Unavailable(string Host) : Case;          // :13
        public sealed record Malformed(string Detail) : Case;          // :14
    }

    public Case ErrorCase { get; } = errorCase;

    // :16-48
    public override string? ErrorDescription => ErrorCase switch
    {
        Case.CommandMissing(var name) => name switch
        {
            // :20
            "npx" => "npx isn't installed. Install Node.js from nodejs.org, then try again.",
            // :21 DEVIATION OD-0008 B5: upstream names `brew install uv`; Windows names winget.
            "uvx" => "uvx isn't installed. Run `winget install astral-sh.uv` in Terminal, then try again.",
            // :22
            _ => $"{name} isn't installed. Install it, then try again.",
        },
        // :24-30
        Case.Exited(var code, var stderr) => LastNonEmptyLine(stderr) is { } last
            ? $"The server quit (exit {code}). {last}"
            : $"The server quit (exit {code}).",
        // :31-32
        Case.TimedOut => "The server didn't answer in time.",
        // :35
        Case.Http(401 or 403, _) => "That key was refused. Press Try again and paste a new one.",
        // :36
        Case.Http(404, _) => "Nothing answered at that address. Check the address and try again.",
        // :37
        Case.Http(var status, _) => $"The server answered {status}.",
        // :40
        Case.UnauthorizedOAuth => "Sign-in was rejected. Sign in again in your browser.",
        // :42
        Case.NeedsSetup(var guide) => guide.Reason,
        // :44
        Case.Unavailable(var host) => $"{host} didn't answer (404). If the address is right, try again in a few minutes.",
        // :46
        Case.Malformed(var detail) => $"The server sent something unexpected: {detail}",
        _ => throw new UnreachableException(),
    };

    // :25-28
    private static string? LastNonEmptyLine(string stderr) =>
        SwiftString.ComponentsSeparatedByNewlines(stderr).Select(line => line.Trimmed()).LastOrDefault(line => line.Length > 0);
}

// :51-234
public static class MCPProbe
{
    // :52-61
    public static async Task<MCPProbeResult> Probe(MCPResolvedServer server)
    {
        if (server.Command is not null) return await ProbeStdio(server);
        if (server.Url is { } url) return await ProbeRemote(server, url);
        throw new MCPProbeError(new MCPProbeError.Case.Malformed("missing command or URL"));
    }

    // :63-67
    private static void CheckReply(JSONValue value)
    {
        if (MCPWire.ErrorMessage(value) is { } message)
            throw new MCPProbeError(new MCPProbeError.Case.Malformed(message));
    }

    // :69-74
    private static string? FirstLine(string? text)
    {
        if (text is null) return null;
        var components = SwiftString.ComponentsSeparatedByNewlines(text);
        var line = (components.Count > 0 ? components[0] : "").Trimmed();
        return line.Length == 0 ? null : line;
    }

    // :76-79
    private static MCPToolSummary? ToolSummary(JSONValue tool)
    {
        if (tool["name"]?.JSONValueString is not { } name) return null;
        return new MCPToolSummary(name, FirstLine(tool["description"]?.JSONValueString));
    }

    private static IReadOnlyList<MCPToolSummary> ToolSummaries(IReadOnlyList<JSONValue> items)
    {
        var summaries = new List<MCPToolSummary>();
        foreach (var item in items)
        {
            if (ToolSummary(item) is { } summary) summaries.Add(summary);
        }
        return summaries;
    }

    // MARK: - Local servers over stdio

    // :83-132
    private static async Task<MCPProbeResult> ProbeStdio(MCPResolvedServer server)
    {
        // :84-87
        var command = server.Command ?? "";
        if (LoginEnvironment.Which(command, LoginEnvironment.Current) is not { } executable)
            throw new MCPProbeError(new MCPProbeError.Case.CommandMissing(command));
        // :88
        var isOAuth = server.Args.Any(a => SwiftString.Equal(a, "mcp-remote"));
        // :89
        var deadline = SwiftDate.AddingTimeInterval(SwiftDate.Now, isOAuth ? 240 : 90);

        // :91-92. ED-128: environment dictionaries are case-insensitive, first spelling, last value.
        var environment = new Dictionary<string, string>(LoginEnvironment.Current, StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in server.Env) environment[key] = value;

        // :94-95. ED-194: termination is MCPStdioConnection.Stop's pid-only ladder (3 s), no job-object kill here.
        var connection = await MCPStdioConnection.LaunchAsync(executable, server.Args, environment); // ED-NEW-perf-ui-thread-7: the child is created on the pool
        try
        {
            // :97-114
            async Task<JSONValue> NextReply(string method, JSONValue? parameters)
            {
                // :98-101
                var remaining = (deadline - SwiftDate.Now).TotalSeconds;
                if (!(remaining > 0)) throw ExitError(null, isOAuth, connection.ErrorTail);
                // :102-113
                try
                {
                    var reply = await connection.Request(method, parameters, remaining);
                    CheckReply(reply);
                    return reply;
                }
                catch (MCPWireError error)
                {
                    switch (error.ErrorCase)
                    {
                        case MCPWireError.Case.Exited(var code):
                            throw ExitError(code, isOAuth, connection.ErrorTail);
                        case MCPWireError.Case.TimedOut:
                            throw ExitError(null, isOAuth, connection.ErrorTail);
                        default:
                            throw new UnreachableException();
                    }
                }
            }

            // :116-120
            var initReply = await NextReply("initialize", MCPWire.InitializeParams());
            var initResult = initReply["result"];
            if (initResult?.JSONValueObject is null) throw new MCPProbeError(new MCPProbeError.Case.Malformed("missing result"));
            var serverName = initResult?["serverInfo"]?["name"]?.JSONValueString;
            var serverVersion = initResult?["serverInfo"]?["version"]?.JSONValueString;

            // :122
            connection.Notify("notifications/initialized");

            // :124-131
            var items = await MCPWire.ListTools(parameters => NextReply("tools/list", parameters));
            return new MCPProbeResult(serverName, serverVersion, new ValueList<MCPToolSummary>(ToolSummaries(items)));
        }
        finally
        {
            // :95 defer { connection.stop() }
            connection.Stop();
        }
    }

    // :134-140
    private static MCPProbeError ExitError(int? code, bool isOAuth, string stderr)
    {
        if (isOAuth && (stderr.Contains("Unauthorized", StringComparison.Ordinal) || stderr.Contains("invalid_grant", StringComparison.Ordinal)))
            return new MCPProbeError(new MCPProbeError.Case.UnauthorizedOAuth());
        if (code is { } c) return new MCPProbeError(new MCPProbeError.Case.Exited(c, stderr));
        return new MCPProbeError(new MCPProbeError.Case.TimedOut());
    }

    // MARK: - Remote servers over streamable HTTP

    // :144-203
    private static async Task<MCPProbeResult> ProbeRemote(MCPResolvedServer server, string urlString)
    {
        // :145-147
        if (!Uri.TryCreate(urlString, UriKind.Absolute, out var url))
            throw new MCPProbeError(new MCPProbeError.Case.Malformed("invalid URL"));
        // :148
        string? sessionID = null;

        // :150-174. OD-0022/ED-152, enforced by ED-209 (B3 batch 4, MCPWire.cs:77-84): MCPWire.Post marks each
        // of a server's custom headers as origin-bound before sending, so F.HTTP's Redirected drops one across
        // a redirect that changes scheme, host or port (e.g. X-Api-Key) the same way it drops Authorization,
        // and keeps it on a same-server redirect. This Post delegates straight to MCPWire.Post with no inline
        // copy of the header logic, so that marking applies here with no separate fix needed.
        async Task<JSONValue?> Post(JSONValue payload, int? id)
        {
            try
            {
                // :152-154
                var posted = await MCPWire.Post(url, server.Headers, sessionID, payload, id, 30);
                sessionID = posted.SessionID ?? sessionID;
                return posted.Body;
            }
            catch (MCPHTTPError error) when ((error.Status == 401 || error.Status == 403) && server.OauthUpstream is not null)
            {
                // :156-157
                var status = error.Status;
                var body = error.Body;
                if (status == 403 && SwiftString.LocalizedCaseInsensitiveContains(body, "MCP server disabled"))
                    throw new MCPProbeError(new MCPProbeError.Case.NeedsSetup(GitLabServerDisabled(server.OauthUpstream ?? urlString)));
                // :160
                throw new MCPProbeError(new MCPProbeError.Case.UnauthorizedOAuth());
            }
            catch (MCPHTTPError error) when (error.Status == 404 && server.OauthUpstream is not null)
            {
                // :162-169. GitLab 19.4 and earlier answer a namespace that does not allow its MCP server with 404
                // instead of 403 'MCP server disabled'; both statuses share every cause, so a 404 from its MCP
                // endpoint is the setup case rather than a hiccup on GitLab's side.
                var upstream = server.OauthUpstream ?? urlString;
                if (IsGitLabMCP(upstream))
                    throw new MCPProbeError(new MCPProbeError.Case.NeedsSetup(GitLabServerDisabled(upstream)));
                throw new MCPProbeError(new MCPProbeError.Case.Unavailable(HostOrServer(upstream)));
            }
            catch (MCPHTTPError error)
            {
                // :170-172
                if (error.Status is not { } status) throw new MCPProbeError(new MCPProbeError.Case.Malformed("invalid reply"));
                throw new MCPProbeError(new MCPProbeError.Case.Http(status, error.Body));
            }
        }

        // :176-181
        static JSONValue Nearest(JSONValue? value)
        {
            if (value is null) throw new MCPProbeError(new MCPProbeError.Case.Malformed("empty reply"));
            CheckReply(value);
            if (value.JSONValueObject is null) throw new MCPProbeError(new MCPProbeError.Case.Malformed("invalid reply"));
            return value;
        }

        // :183-187
        var initReply = Nearest(await Post(MCPWire.Request(1, "initialize", MCPWire.InitializeParams()), 1));
        var initResult = initReply["result"];
        if (initResult?.JSONValueObject is null) throw new MCPProbeError(new MCPProbeError.Case.Malformed("missing result"));
        var serverName = initResult?["serverInfo"]?["name"]?.JSONValueString;
        var serverVersion = initResult?["serverInfo"]?["version"]?.JSONValueString;

        // :189-193
        try
        {
            _ = await Post(MCPWire.Notification("notifications/initialized"), null);
        }
        catch (CancellationError)
        {
            throw;
        }
        catch (Exception error) when (SwiftTry.IsThrownError(error))
        {
            // swallowed
        }

        // :195-202. Every tools/list page is sent and matched as id 2, never an incrementing id (B3 change).
        async Task<JSONValue> Page(JSONValue parameters) => Nearest(await Post(MCPWire.Request(2, "tools/list", parameters), 2));
        var items = await MCPWire.ListTools(Page);
        return new MCPProbeResult(serverName, serverVersion, new ValueList<MCPToolSummary>(ToolSummaries(items)));
    }

    // MARK: - Setup guides

    // :209-212
    public static bool IsGitLabMCP(string upstream)
    {
        if (SwiftURL.Create(upstream) is not { } url) return false;
        return SwiftString.HasSuffix(SwiftString.Lowercased(url.Path), "/api/v4/mcp");
    }

    // :215-233
    public static MCPSetupGuide GitLabServerDisabled(string upstream)
    {
        // :216-219
        var origin = SwiftURL.Create(upstream) is { Host: { } host } url
            ? (url.Scheme ?? "https") + "://" + host
            : "https://gitlab.com";

        return new MCPSetupGuide(
            Title: "Turn on GitLab's MCP server",
            Reason: "You're signed in, but GitLab keeps its MCP server off until a group owner turns it on. Until then, the address answers not found.",
            Steps: new ValueList<string>([
                "Open your groups and pick the top-level group your projects live in",
                "Go to Settings \U0000203A General and expand Permissions and group features", // U+203A SINGLE RIGHT-POINTING ANGLE QUOTATION MARK
                "Under MCP client access, tick Allow connection to GitLab and save",
                "Come back here and press Check again",
            ]),
            PageLabel: "Open my groups",
            PageURL: origin + "/dashboard/groups",
            Note: "GitLab can take a minute to apply the change.");
    }

    // :169 URL(string: upstream)?.host ?? "The server"
    private static string HostOrServer(string upstream) =>
        SwiftURL.Create(upstream)?.Host is { Length: > 0 } host ? host : "The server";
}
