using System.Diagnostics;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;

// Source: DroppyCode/Services/MCP/MCPProbe.swift (B2 1fbf81e), :1-431.

namespace Marrow.Services.MCP;

// :4-50. An error enum keeps its cases as a nested record union (R-ERR-3).
public sealed class MCPProbeError(MCPProbeError.Case errorCase) : SwiftError
{
    public abstract record Case
    {
        private Case() { }
        public sealed record CommandMissing(string Name) : Case;       // :5
        public sealed record Exited(int Code, string Stderr) : Case;   // :6
        public sealed record TimedOut : Case;                          // :7
        public sealed record Http(int Status, string Body) : Case;     // :8
        public sealed record UnauthorizedOAuth : Case;                 // :9
        public sealed record NeedsSetup(MCPSetupGuide Guide) : Case;   // :12
        public sealed record Unavailable(string Host) : Case;          // :14
        public sealed record Malformed(string Detail) : Case;          // :15
    }

    public Case ErrorCase { get; } = errorCase;

    // :17-49
    public override string? ErrorDescription => ErrorCase switch
    {
        Case.CommandMissing(var name) => name switch
        {
            // :21
            "npx" => "npx isn't installed. Install Node.js from nodejs.org, then try again.",
            // :22 DEVIATION OD-0008 B5: upstream names `brew install uv`; Windows names winget.
            "uvx" => "uvx isn't installed. Run `winget install astral-sh.uv` in Terminal, then try again.",
            // :23
            _ => $"{name} isn't installed. Install it, then try again.",
        },
        // :25-31
        Case.Exited(var code, var stderr) => LastNonEmptyLine(stderr) is { } last
            ? $"The server quit (exit {code}). {last}"
            : $"The server quit (exit {code}).",
        // :32-33
        Case.TimedOut => "The server didn't answer in time.",
        // :36
        Case.Http(401 or 403, _) => "That key was refused. Press Try again and paste a new one.",
        // :37
        Case.Http(404, _) => "Nothing answered at that address. Check the address and try again.",
        // :38
        Case.Http(var status, _) => $"The server answered {status}.",
        // :41
        Case.UnauthorizedOAuth => "Sign-in was rejected. Sign in again in your browser.",
        // :43
        Case.NeedsSetup(var guide) => guide.Reason,
        // :45
        Case.Unavailable(var host) => $"{host} didn't answer (404). If the address is right, try again in a few minutes.",
        // :47
        Case.Malformed(var detail) => $"The server sent something unexpected: {detail}",
        _ => throw new UnreachableException(),
    };

    // :26-29
    private static string? LastNonEmptyLine(string stderr) =>
        SwiftString.ComponentsSeparatedByNewlines(stderr).Select(line => line.Trimmed()).LastOrDefault(line => line.Length > 0);
}

// :52-379
public static class MCPProbe
{
    // :53-62
    public static async Task<MCPProbeResult> Probe(MCPResolvedServer server)
    {
        if (server.Command is not null) return await ProbeStdio(server);
        if (server.Url is { } url) return await ProbeRemote(server, url);
        throw new MCPProbeError(new MCPProbeError.Case.Malformed("missing command or URL"));
    }

    // MARK: - Shared message builders

    // :66-72. DEVIATION OD-0012 F1: upstream's clientInfo.name is "Droppy Code"; the port sends "Marrow".
    private static JSONValue InitializeParams() => JSONValue.FromDictionaryLiteral(
        ("protocolVersion", "2025-06-18"),
        ("capabilities", new JSONValue.Object(ValueMap<string, JSONValue>.Empty)),
        ("clientInfo", JSONValue.FromDictionaryLiteral(("name", "Marrow"), ("version", "1.0"))));

    // :74-82
    private static JSONValue Request(long id, string method, JSONValue? parameters)
    {
        var fields = new Dictionary<string, JSONValue>
        {
            ["jsonrpc"] = new JSONValue.String("2.0"),
            ["id"] = new JSONValue.Int(id),
            ["method"] = new JSONValue.String(method),
        };
        if (parameters is { } p) fields["params"] = p;
        return new JSONValue.Object(new ValueMap<string, JSONValue>(fields, SwiftStringComparer.Instance));
    }

    // :84-86
    private static JSONValue Notification(string method) => new JSONValue.Object(new ValueMap<string, JSONValue>(
        new Dictionary<string, JSONValue>
        {
            ["jsonrpc"] = new JSONValue.String("2.0"),
            ["method"] = new JSONValue.String(method),
        }, SwiftStringComparer.Instance));

    // :88-92
    private static void CheckReply(JSONValue value)
    {
        if (value["error"] is { } error && error.JSONValueObject is not null)
            throw new MCPProbeError(new MCPProbeError.Case.Malformed(error["message"]?.JSONValueString ?? "unknown error"));
    }

    // :94-98
    private static string? FirstLine(string? text)
    {
        if (text is null) return null;
        var components = SwiftString.ComponentsSeparatedByNewlines(text);
        var line = (components.Count > 0 ? components[0] : "").Trimmed();
        return line.Length == 0 ? null : line;
    }

    // :101-107
    private static IReadOnlyList<MCPToolSummary> ToolSummaries(JSONValue? result)
    {
        if (result?["tools"]?.JSONValueArray is not { } tools) return [];
        var summaries = new List<MCPToolSummary>();
        foreach (var tool in tools)
        {
            if (tool["name"]?.JSONValueString is not { } name) continue;
            summaries.Add(new MCPToolSummary(name, FirstLine(tool["description"]?.JSONValueString)));
        }
        return summaries;
    }

    // MARK: - Local servers over stdio

    // :111-217
    private static async Task<MCPProbeResult> ProbeStdio(MCPResolvedServer server)
    {
        // :112-115
        var command = server.Command ?? "";
        if (LoginEnvironment.Which(command, LoginEnvironment.Current) is not { } executable)
            throw new MCPProbeError(new MCPProbeError.Case.CommandMissing(command));

        // :116-117
        var isOAuth = server.Args.Any(a => SwiftString.Equal(a, "mcp-remote"));
        var deadline = SwiftDate.AddingTimeInterval(SwiftDate.Now, isOAuth ? 240 : 90);

        // :119-120. ED-128: environment dictionaries are case-insensitive, first spelling, last value.
        var environment = new Dictionary<string, string>(LoginEnvironment.Current, StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in server.Env) environment[key] = value;

        // :122-132
        var stdinPipe = new ProcessPipe();
        var stdoutPipe = new ProcessPipe();
        var stderrPipe = new ProcessPipe();
        var process = new ProcessLauncher
        {
            ExecutableURL = executable,
            Arguments = server.Args,
            Environment = environment,
            CurrentDirectoryURL = LoginEnvironment.HomeDirectory,
            StandardInput = stdinPipe,
            StandardOutput = stdoutPipe,
            StandardError = stderrPipe,
        };

        // :134-153
        var conversation = new StdioConversation();
        stdoutPipe.ReadabilityHandler = data =>
        {
            if (data.Length == 0) stdoutPipe.ReadabilityHandler = null;
            else conversation.AppendStdout(data);
        };
        stderrPipe.ReadabilityHandler = data =>
        {
            if (data.Length == 0) stderrPipe.ReadabilityHandler = null;
            else conversation.AppendStderr(data);
        };
        process.TerminationHandler = finished => conversation.SetExit(finished.TerminationStatus);

        // :154-161
        try
        {
            process.Run();
        }
        catch (Exception error) when (SwiftTry.IsThrownError(error))
        {
            stdoutPipe.ReadabilityHandler = null;
            stderrPipe.ReadabilityHandler = null;
            Terminate(process);
            throw;
        }

        // :162-167
        try
        {
            // :169-173
            void Send(JSONValue message)
            {
                var line = message.Data();
                var withNewline = new byte[line.Length + 1];
                Array.Copy(line, withNewline, line.Length);
                withNewline[^1] = 0x0A;
                SwiftTry.Ignore(() => stdinPipe.Write(withNewline));
            }

            // :175-191
            async Task<JSONValue> NextReply(long id)
            {
                while (true)
                {
                    foreach (var line in conversation.TakeLines())
                    {
                        if (JSONValue.Parse(line) is not JSONValue.Object value) continue;
                        if (value["id"]?.JSONValueInt != id) continue;
                        CheckReply(value);
                        return value;
                    }
                    if (conversation.ExitCode() is { } code)
                        throw ExitError(code, isOAuth, conversation.StderrText());
                    if (!(SwiftDate.Now < deadline))
                        throw ExitError(null, isOAuth, conversation.StderrText());
                    await SwiftTask.Sleep(TimeSpan.FromMilliseconds(5));
                }
            }

            // :193-198
            Send(Request(1, "initialize", InitializeParams()));
            var initReply = await NextReply(1);
            var initResult = initReply["result"];
            if (initResult?.JSONValueObject is null) throw new MCPProbeError(new MCPProbeError.Case.Malformed("missing result"));
            var serverName = initResult?["serverInfo"]?["name"]?.JSONValueString;
            var serverVersion = initResult?["serverInfo"]?["version"]?.JSONValueString;

            // :200
            Send(Notification("notifications/initialized"));

            // :202-215
            var tools = new List<MCPToolSummary>();
            string? cursor = null;
            long nextID = 2;
            for (var i = 0; i < 10; i++)
            {
                var parameters = new Dictionary<string, JSONValue>();
                if (cursor is { } c) parameters["cursor"] = new JSONValue.String(c);
                Send(Request(nextID, "tools/list", new JSONValue.Object(new ValueMap<string, JSONValue>(parameters, SwiftStringComparer.Instance))));
                var reply = await NextReply(nextID);
                nextID++;
                var list = reply["result"];
                tools.AddRange(ToolSummaries(list));
                if (list?["nextCursor"]?.JSONValueString is not { } next || next.Length == 0) break;
                cursor = next;
            }
            // :216
            return new MCPProbeResult(serverName, serverVersion, new ValueList<MCPToolSummary>(tools));
        }
        finally
        {
            // :163-166
            stdoutPipe.ReadabilityHandler = null;
            stderrPipe.ReadabilityHandler = null;
            Terminate(process);
            SwiftTry.Ignore(stdinPipe.CloseWriting);
        }
    }

    // :219-225
    private static MCPProbeError ExitError(int? code, bool isOAuth, string stderr)
    {
        if (isOAuth && (stderr.Contains("Unauthorized", StringComparison.Ordinal) || stderr.Contains("invalid_grant", StringComparison.Ordinal)))
            return new MCPProbeError(new MCPProbeError.Case.UnauthorizedOAuth());
        if (code is { } c) return new MCPProbeError(new MCPProbeError.Case.Exited(c, stderr));
        return new MCPProbeError(new MCPProbeError.Case.TimedOut());
    }

    // :227-236. Identical to Shell.swift:110-118 and MCPHub.swift:286-295: F.ProcessLauncher's ladder.
    private static void Terminate(ProcessLauncher process) => TerminationLadder.Shell(process);

    // MARK: - Remote servers over streamable HTTP

    // :240-301
    private static async Task<MCPProbeResult> ProbeRemote(MCPResolvedServer server, string urlString)
    {
        // :241-243
        if (!Uri.TryCreate(urlString, UriKind.Absolute, out var url))
            throw new MCPProbeError(new MCPProbeError.Case.Malformed("invalid URL"));
        string? sessionID = null;

        // :246-266. DEVIATION OD-0022/ED-152: F.HTTP (Marrow.Core.Port.Http.cs) currently drops only the
        // Authorization header across an origin-changing redirect (ED-152); it has no per-request mechanism to
        // drop an arbitrary named header the same way. Extending it is out of this unit's scope (Marrow.Services
        // only). The headers this probe sends are therefore forwarded to a same-server redirect target exactly
        // as OD-0022 requires, but are not yet withheld from a cross-server redirect target. Tracked, not silently
        // dropped: see this unit's report.
        async Task<JSONValue?> Post(JSONValue payload)
        {
            try
            {
                var posted = await PostJSON(url, server.Headers, sessionID, payload);
                sessionID = posted.SessionID ?? sessionID;
                return posted.Body;
            }
            catch (MCPProbeError e) when (e.ErrorCase is MCPProbeError.Case.Http(401 or 403, var body) && server.OauthUpstream is not null)
            {
                // :252-254
                if (SwiftString.LocalizedCaseInsensitiveContains(body, "MCP server disabled"))
                    throw new MCPProbeError(new MCPProbeError.Case.NeedsSetup(GitLabServerDisabled(server.OauthUpstream ?? urlString)));
                // :255
                throw new MCPProbeError(new MCPProbeError.Case.UnauthorizedOAuth());
            }
            catch (MCPProbeError e) when (e.ErrorCase is MCPProbeError.Case.Http(404, _) && server.OauthUpstream is not null)
            {
                // :257-264
                var upstream = server.OauthUpstream ?? urlString;
                if (IsGitLabMCP(upstream))
                    throw new MCPProbeError(new MCPProbeError.Case.NeedsSetup(GitLabServerDisabled(upstream)));
                throw new MCPProbeError(new MCPProbeError.Case.Unavailable(HostOrServer(upstream)));
            }
        }

        // :268-273
        static JSONValue Nearest(JSONValue? value)
        {
            if (value is null) throw new MCPProbeError(new MCPProbeError.Case.Malformed("empty reply"));
            CheckReply(value);
            if (value.JSONValueObject is null) throw new MCPProbeError(new MCPProbeError.Case.Malformed("invalid reply"));
            return value;
        }

        // :275-279
        var initReply = Nearest(await Post(Request(1, "initialize", InitializeParams())));
        var initResult = initReply["result"];
        if (initResult?.JSONValueObject is null) throw new MCPProbeError(new MCPProbeError.Case.Malformed("missing result"));
        var serverName = initResult?["serverInfo"]?["name"]?.JSONValueString;
        var serverVersion = initResult?["serverInfo"]?["version"]?.JSONValueString;

        // :281-285
        try
        {
            _ = await Post(Notification("notifications/initialized"));
        }
        catch (CancellationError)
        {
            throw;
        }
        catch (Exception error) when (SwiftTry.IsThrownError(error))
        {
            // swallowed
        }

        // :287-299
        var tools = new List<MCPToolSummary>();
        string? cursor = null;
        long nextID = 2;
        for (var i = 0; i < 10; i++)
        {
            var parameters = new Dictionary<string, JSONValue>();
            if (cursor is { } c) parameters["cursor"] = new JSONValue.String(c);
            var reply = Nearest(await Post(Request(nextID, "tools/list", new JSONValue.Object(new ValueMap<string, JSONValue>(parameters, SwiftStringComparer.Instance)))));
            nextID++;
            var list = reply["result"];
            tools.AddRange(ToolSummaries(list));
            if (list?["nextCursor"]?.JSONValueString is not { } next || next.Length == 0) break;
            cursor = next;
        }
        // :300
        return new MCPProbeResult(serverName, serverVersion, new ValueList<MCPToolSummary>(tools));
    }

    // :303-334
    private static async Task<(string? SessionID, JSONValue? Body)> PostJSON(
        Uri url, IReadOnlyDictionary<string, string> headers, string? sessionID, JSONValue payload)
    {
        // :309-316
        var request = new URLRequest(url, timeoutInterval: 30) { HttpMethod = "POST", HttpBody = payload.Data() };
        foreach (var (field, value) in headers) request = request.SetValue(value, field);
        request = request.SetValue("application/json", "Content-Type");
        request = request.SetValue("application/json, text/event-stream", "Accept");
        request = request.SetValue("2025-06-18", "MCP-Protocol-Version");
        if (sessionID is { } sid) request = request.SetValue(sid, "Mcp-Session-Id");

        // :318-322
        var (data, response) = await URLSession.Shared.Data(request);
        if (response is not HTTPURLResponse http) throw new MCPProbeError(new MCPProbeError.Case.Malformed("invalid reply"));
        var session = http.Value("Mcp-Session-Id");
        // :323-327
        if (http.StatusCode is < 200 or >= 300)
        {
            var body = SwiftString.Prefix(SwiftText.DecodeUtf8Repairing(data), 2000).Trimmed();
            throw new MCPProbeError(new MCPProbeError.Case.Http(http.StatusCode, body));
        }
        // :328-333
        var contentType = http.Value("Content-Type") ?? "";
        if (contentType.Contains("text/event-stream", StringComparison.Ordinal))
            return (session, FirstSSEMessage(data));
        if (data.Length == 0) return (session, null);
        return (session, JSONValue.Parse(data));
    }

    // :336-348
    private static JSONValue? FirstSSEMessage(byte[] data)
    {
        var text = SwiftText.DecodeUtf8Repairing(data);
        foreach (var rawLine in SwiftString.ComponentsSeparatedByNewlines(text))
        {
            var trimmedLine = FoundationText.Trim(rawLine);
            if (!SwiftString.HasPrefix(trimmedLine, "data:")) continue;
            var payload = FoundationText.Trim(SwiftString.DropFirst(trimmedLine, 5));
            if (JSONValue.Parse(payload) is not JSONValue.Object value) continue;
            if (value["id"] is null) continue;
            return value;
        }
        return null;
    }

    // MARK: - Setup guides

    // :354-357
    public static bool IsGitLabMCP(string upstream)
    {
        if (SwiftURL.Create(upstream) is not { } url) return false;
        return SwiftString.HasSuffix(SwiftString.Lowercased(url.Path), "/api/v4/mcp");
    }

    // :359-378
    public static MCPSetupGuide GitLabServerDisabled(string upstream)
    {
        // :361-364
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

    // :264 URL(string: upstream)?.host ?? "The server"
    private static string HostOrServer(string upstream) =>
        SwiftURL.Create(upstream)?.Host is { Length: > 0 } host ? host : "The server";
}

/// <summary>
/// :381-430. Lock-guarded buffers fed by the stdio readability handlers: stdout bytes are split on newlines into
/// complete lines, stderr and the exit status are kept for failure reporting.
/// </summary>
file sealed class StdioConversation
{
    // :385-390
    private struct State
    {
        public List<byte> Buffer;
        public List<byte[]> Pending;
        public List<byte> Stderr;
        public int? ExitStatus;
    }

    // :392
    private readonly Mutex<State> state = new(new State { Buffer = [], Pending = [], Stderr = [] });

    // :394-402
    public void AppendStdout(byte[] data)
    {
        state.WithLock((ref State current) =>
        {
            current.Buffer.AddRange(data);
            int newline;
            while ((newline = current.Buffer.IndexOf((byte)0x0A)) >= 0)
            {
                current.Pending.Add(current.Buffer.Take(newline).ToArray());
                current.Buffer.RemoveRange(0, newline + 1);
            }
        });
    }

    // :404-409
    public void AppendStderr(byte[] data)
    {
        state.WithLock((ref State current) =>
        {
            current.Stderr.AddRange(data);
            if (current.Stderr.Count > 64_000) current.Stderr = current.Stderr.Skip(current.Stderr.Count - 16_000).ToList();
        });
    }

    // :411-413
    public void SetExit(int status) => state.WithLock((ref State current) => current.ExitStatus = status);

    // :415-421
    public IReadOnlyList<byte[]> TakeLines() => state.WithLock((ref State current) =>
    {
        var lines = current.Pending;
        current.Pending = [];
        return (IReadOnlyList<byte[]>)lines;
    });

    // :423-425
    public string StderrText() => state.WithLock((ref State current) => SwiftText.DecodeUtf8Repairing(current.Stderr.ToArray()));

    // :427-429
    public int? ExitCode() => state.WithLock((ref State current) => current.ExitStatus);
}
