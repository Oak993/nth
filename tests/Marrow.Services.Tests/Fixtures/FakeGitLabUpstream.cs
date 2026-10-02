using System.Text.Json;
using Marrow.Testing;

// PORT-ONLY (OD-0057): a stand-in for a GitLab instance's MCP server on https, answered in-process by the HTTP seam
// (URLSession.Transport, InProcessHttpHandler): no socket, no network, no certificate. The address is
// "https://gitlab.example.test/api/v4/mcp" (the catalog's template with the instance a test fills in). It answers:
//  - the sign-in's discovery and exchange the way a server that supports dynamic registration does (MCPOAuth.swift:
//    the 401 challenge naming the resource metadata, that metadata, the authorization server's metadata, the
//    registration, the token endpoint), handing out Token;
//  - the MCP endpoint itself (POST /api/v4/mcp) through McpAnswer, which a test replaces to make the server answer
//    403 "MCP server disabled" or 404; the default answers initialize, the initialized notification and tools/list
//    for a request bearing Token and 401 for any other.
internal sealed class FakeGitLabUpstream
{
    public const string Host = "gitlab.example.test";
    public const string Base = "https://" + Host;
    public const string Token = "at-new";

    /// <summary>The answer of POST /api/v4/mcp; the default is a working server.</summary>
    public Func<InProcessRequest, HttpResponseMessage> McpAnswer { get; set; }

    public FakeGitLabUpstream() => McpAnswer = Working;

    /// <summary>Puts this server behind <paramref name="handler"/> for every address that is not loopback.</summary>
    public void Install(InProcessHttpHandler handler) => handler.Answer = Handle;

    /// <summary>The answer of a server that is on: 401 without the right token, then the three MCP calls of a probe.</summary>
    public static HttpResponseMessage Working(InProcessRequest request)
    {
        if (request.Header("Authorization") != "Bearer " + Token) return InProcessHttpHandler.Reply(401, "{}");
        using var document = JsonDocument.Parse(request.Body);
        var root = document.RootElement;
        var method = root.GetProperty("method").GetString();
        if (!root.TryGetProperty("id", out var id)) return InProcessHttpHandler.Reply(202, "", "text/plain");
        var result = method switch
        {
            "initialize" => """{"serverInfo":{"name":"gitlab","version":"19.5"}}""",
            "tools/list" => """{"tools":[{"name":"get_issue","description":"Reads an issue."},{"name":"create_issue"}]}""",
            _ => "{}",
        };
        return InProcessHttpHandler.Reply(200, $$"""{"jsonrpc":"2.0","id":{{id.GetRawText()}},"result":{{result}}}""");
    }

    private HttpResponseMessage Handle(InProcessRequest request)
    {
        var path = request.Url.AbsolutePath;
        if (path == "/api/v4/mcp")
        {
            // The sign-in's first request is a GET without a token: the 401 that names where the metadata is.
            return request.Method == "GET"
                ? InProcessHttpHandler.Reply(401, "", "text/plain", ("WWW-Authenticate", $"Bearer resource_metadata=\"{Base}/prm\""))
                : McpAnswer(request);
        }
        return path switch
        {
            "/prm" => InProcessHttpHandler.Reply(200, $$"""{"resource":"{{Base}}/api/v4/mcp","authorization_servers":["{{Base}}/auth"],"scopes_supported":["mcp"]}"""),
            "/.well-known/oauth-authorization-server/auth" => InProcessHttpHandler.Reply(200,
                $$"""{"issuer":"{{Base}}/auth","authorization_endpoint":"{{Base}}/authorize","token_endpoint":"{{Base}}/token","registration_endpoint":"{{Base}}/register"}"""),
            "/register" => InProcessHttpHandler.Reply(201, """{"client_id":"cid-1"}"""),
            "/token" => InProcessHttpHandler.Reply(200, $$"""{"access_token":"{{Token}}","refresh_token":"rt-1","expires_in":3600,"token_type":"Bearer"}"""),
            _ => InProcessHttpHandler.Reply(404, "not found", "text/plain"),
        };
    }
}
