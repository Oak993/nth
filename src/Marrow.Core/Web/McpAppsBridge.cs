// PORT-ONLY: web surfaces W19 (DESIGN.md 7.6 "Bridge", "Display modes"; 13.1 McpAppsBridgeTests; ED-NEW-web-w19-1). nth's side of
// the MCP Apps protocol (io.modelcontextprotocol/ui, spec 2026-01-26) for one mounted view, as a pure state machine over JSON-RPC
// text: the view's handshake (ui/initialize, then ui/notifications/initialized), the tool's input, partial input, result and
// cancellation, the host context, and the teardown. nth sends the view nothing but the answer to its own ui/initialize (and ping)
// until the view says it is initialized; what arrived before waits and goes out in order the moment it does. Requests the view
// makes (tools/call, resources/read, ui/message, ui/open-link, ui/request-display-mode, ui/update-model-context) are handed to the
// host, which answers each one; a request the bridge can refuse alone (before the handshake, a tool the app may not call, a
// method nth does not host) never reaches it. No Swift counterpart.
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Marrow.Core.Web;

/// <summary>Where an MCP App or an inline HTML widget is shown (DESIGN 7.6 "Display modes").</summary>
public enum McpAppDisplayMode
{
    /// <summary>In the tool row of the thread.</summary>
    Inline,

    /// <summary>In the App pane of the workspace; the spec's <c>fullscreen</c> maps here, expanded (DESIGN 7.6, 8.4).</summary>
    Pane,

    /// <summary>In its own small floating window; the spec's <c>pip</c>.</summary>
    PopOut,
}

/// <summary>The protocol's names (MCP Apps, io.modelcontextprotocol/ui, 2026-01-26).</summary>
public static class McpAppProtocol
{
    public const string Version = "2026-01-26";
    public const string Extension = "io.modelcontextprotocol/ui";
    public const string Initialize = "ui/initialize";
    public const string Initialized = "ui/notifications/initialized";
    public const string ToolInput = "ui/notifications/tool-input";
    public const string ToolInputPartial = "ui/notifications/tool-input-partial";
    public const string ToolResult = "ui/notifications/tool-result";
    public const string ToolCancelled = "ui/notifications/tool-cancelled";
    public const string HostContextChanged = "ui/notifications/host-context-changed";
    public const string SizeChanged = "ui/notifications/size-changed";
    public const string ResourceTeardown = "ui/resource-teardown";
    public const string Message = "ui/message";
    public const string OpenLink = "ui/open-link";
    public const string RequestDisplayMode = "ui/request-display-mode";
    public const string UpdateModelContext = "ui/update-model-context";
    public const string ToolsCall = "tools/call";
    public const string ResourcesRead = "resources/read";
    public const string Log = "notifications/message";
    public const string Ping = "ping";

    /// <summary>The requests a view may make that nth hosts (besides ui/initialize and ping, which the bridge answers).</summary>
    public static IReadOnlySet<string> HostedRequests { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        ToolsCall, ResourcesRead, Message, OpenLink, RequestDisplayMode, UpdateModelContext,
    };

    /// <summary>The spec's name of a display mode: inline, fullscreen (the App pane, expanded) or pip (the pop-out window).</summary>
    public static string SpecName(McpAppDisplayMode mode) => mode switch
    {
        McpAppDisplayMode.Pane => "fullscreen",
        McpAppDisplayMode.PopOut => "pip",
        _ => "inline",
    };

    /// <summary>A spec display mode as nth's, or null for a name nth does not know.</summary>
    public static McpAppDisplayMode? ParseMode(string? name) => name switch
    {
        "inline" => McpAppDisplayMode.Inline,
        "fullscreen" => McpAppDisplayMode.Pane,
        "pip" => McpAppDisplayMode.PopOut,
        _ => null,
    };
}

/// <summary>Where the bridge is in a view's life.</summary>
public enum McpAppBridgeState
{
    /// <summary>The page is loading or doing its handshake; nothing is sent to it but answers to ui/initialize and ping.</summary>
    Loading,

    /// <summary>The view said it is initialized; tool data, context changes and the teardown flow.</summary>
    Ready,

    /// <summary>nth asked the view to tear down and waits for its answer.</summary>
    TearingDown,

    /// <summary>Done: nothing is sent or answered any more.</summary>
    Closed,
}

/// <summary>A request from the view that the host answers with <see cref="McpAppsBridge.Answer"/> or <see cref="McpAppsBridge.Fail"/>.</summary>
public sealed record McpAppRequest(JsonNode Id, string Method, JsonObject Params, int Document);

/// <summary>A notification from the view the host acts on (size changes and log lines).</summary>
public sealed record McpAppNotice(string Method, JsonObject Params);

public sealed class McpAppsBridge
{
    /// <summary>The spec's error code for a refused request (denied, invalid, policy).</summary>
    public const int RefusedCode = -32000;
    public const int MethodNotFoundCode = -32601;
    public const int InvalidParamsCode = -32602;

    /// <summary>The longest message nth reads from a view; longer text is ignored.</summary>
    public const int MaxMessageCharacters = 4 * 1024 * 1024;

    public const string NotInitializedMessage = "The app has not finished its handshake (ui/notifications/initialized).";
    public const string ClosingMessage = "The app is closing.";
    public const string UnknownToolMessage = "Unknown tool";

    private readonly Action<string> post;
    private readonly Func<JsonObject> initializeResult;
    private readonly Func<string, bool> canCallTool;
    private JsonObject? partialInput;
    private JsonObject? input;
    private JsonObject? result;
    private string? cancelled;
    private bool initializeAnswered;
    private bool inputSent;
    private bool resultSent;
    private bool cancelledSent;
    private string? partialSent;
    private int document;
    private int teardowns;
    private string? teardownId;

    /// <param name="post">Sends one JSON-RPC message to the view.</param>
    /// <param name="initializeResult">The result of ui/initialize: protocol version, host info, capabilities and the host context now.</param>
    /// <param name="canCallTool">True for a tool of this view's server the app may call (DESIGN 7.6: only that server's tools,
    /// and only those visible to the app); null allows none.</param>
    public McpAppsBridge(Action<string> post, Func<JsonObject> initializeResult, Func<string, bool>? canCallTool = null)
    {
        this.post = post ?? throw new ArgumentNullException(nameof(post));
        this.initializeResult = initializeResult ?? throw new ArgumentNullException(nameof(initializeResult));
        this.canCallTool = canCallTool ?? (static _ => false);
    }

    public McpAppBridgeState State { get; private set; } = McpAppBridgeState.Loading;

    /// <summary>The current document's number; a request of an earlier document is never answered.</summary>
    public int Document => document;

    /// <summary>The display modes the view said it supports, or null when it did not say (then every mode is fine).</summary>
    public IReadOnlyList<McpAppDisplayMode>? AvailableModes { get; private set; }

    /// <summary>The name the view gave itself in ui/initialize, if any.</summary>
    public string? AppName { get; private set; }

    /// <summary>The view asked for something the host must answer.</summary>
    public event Action<McpAppRequest>? Requested;

    /// <summary>The view told the host something (size-changed, a log line).</summary>
    public event Action<McpAppNotice>? Noticed;

    /// <summary>The view finished its handshake.</summary>
    public event Action? BecameReady;

    /// <summary>The teardown finished (the view answered, or was never ready).</summary>
    public event Action? TornDown;

    /// <summary>True when the host may switch the view to <paramref name="mode"/> (spec: never to a mode the view did not list).</summary>
    public bool Supports(McpAppDisplayMode mode) => AvailableModes is not { } modes || modes.Contains(mode) || mode == McpAppDisplayMode.Inline;

    // ---- from the view ----

    /// <summary>One message the view posted (already origin-checked by the caller).</summary>
    public void Receive(string json)
    {
        if (State == McpAppBridgeState.Closed || string.IsNullOrEmpty(json) || json.Length > MaxMessageCharacters) return;
        JsonObject message;
        try
        {
            if (JsonNode.Parse(json) is not JsonObject parsed) return;
            message = parsed;
        }
        catch (JsonException)
        {
            return;
        }

        if (Text(message["jsonrpc"]) != "2.0") return;
        var method = Text(message["method"]);
        var id = message["id"];
        if (method is null)
        {
            if (id is not null && (message.ContainsKey("result") || message.ContainsKey("error"))) Response(id);
            return;
        }

        var parameters = message["params"] as JsonObject ?? [];
        if (id is null) Notification(method, parameters);
        else if (IsValidId(id)) Request(id.DeepClone(), method, (JsonObject)parameters.DeepClone());
    }

    private void Request(JsonNode id, string method, JsonObject parameters)
    {
        if (State is McpAppBridgeState.TearingDown)
        {
            SendError(id, RefusedCode, ClosingMessage);
            return;
        }

        switch (method)
        {
            case McpAppProtocol.Initialize:
                ReadCapabilities(parameters);
                initializeAnswered = true;
                SendResult(id, initializeResult());
                return;
            case McpAppProtocol.Ping:
                SendResult(id, []);
                return;
        }

        if (!McpAppProtocol.HostedRequests.Contains(method))
        {
            SendError(id, MethodNotFoundCode, "Method not found: " + method);
            return;
        }

        if (State != McpAppBridgeState.Ready)
        {
            SendError(id, RefusedCode, NotInitializedMessage);
            return;
        }

        if (method == McpAppProtocol.ToolsCall && !(Text(parameters["name"]) is { Length: > 0 } name && canCallTool(name)))
        {
            SendError(id, InvalidParamsCode, UnknownToolMessage);
            return;
        }

        Requested?.Invoke(new McpAppRequest(id, method, parameters, document));
    }

    private void Notification(string method, JsonObject parameters)
    {
        switch (method)
        {
            case McpAppProtocol.Initialized:
                if (!initializeAnswered || State != McpAppBridgeState.Loading) return;
                State = McpAppBridgeState.Ready;
                Flush();
                BecameReady?.Invoke();
                return;
            case McpAppProtocol.SizeChanged:
            case McpAppProtocol.Log:
                if (State is McpAppBridgeState.Loading or McpAppBridgeState.Ready) Noticed?.Invoke(new McpAppNotice(method, (JsonObject)parameters.DeepClone()));
                return;
        }
    }

    private void Response(JsonNode id)
    {
        if (State == McpAppBridgeState.TearingDown && teardownId is { } wanted && Text(id) == wanted) Finish();
    }

    private void ReadCapabilities(JsonObject parameters)
    {
        AppName = Text(parameters["clientInfo"]?["name"]) ?? Text(parameters["appInfo"]?["name"]) ?? AppName;
        if (parameters["appCapabilities"]?["availableDisplayModes"] is JsonArray modes)
        {
            AvailableModes = modes.Select(node => McpAppProtocol.ParseMode(Text(node))).OfType<McpAppDisplayMode>().Distinct().ToList();
        }
    }

    // ---- from nth ----

    /// <summary>The tool's arguments as far as they have arrived (best-effort JSON). Sent only until the full input goes out.</summary>
    public void SetToolInputPartial(JsonObject arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        if (input is not null) return;
        partialInput = (JsonObject)arguments.DeepClone();
        Flush();
    }

    /// <summary>The tool's complete arguments; sent once per document, after the handshake.</summary>
    public void SetToolInput(JsonObject arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        input ??= (JsonObject)arguments.DeepClone();
        Flush();
    }

    /// <summary>The tool's result (a CallToolResult); sent after the input.</summary>
    public void SetToolResult(JsonObject callToolResult)
    {
        ArgumentNullException.ThrowIfNull(callToolResult);
        result ??= (JsonObject)callToolResult.DeepClone();
        input ??= (JsonObject)(partialInput?.DeepClone() ?? new JsonObject());
        Flush();
    }

    /// <summary>The call was stopped or declined before it had a result.</summary>
    public void SetToolCancelled(string reason)
    {
        if (result is not null) return;
        cancelled ??= reason ?? "";
        Flush();
    }

    /// <summary>Tells a ready view what changed in the host context (theme, display mode, size). Ignored before the handshake:
    /// the answer to ui/initialize carries the context of that moment.</summary>
    public void ChangeHostContext(JsonObject changes)
    {
        ArgumentNullException.ThrowIfNull(changes);
        if (State != McpAppBridgeState.Ready || changes.Count == 0) return;
        SendNotification(McpAppProtocol.HostContextChanged, (JsonObject)changes.DeepClone());
    }

    /// <summary>Answers a request of the current document.</summary>
    public void Answer(McpAppRequest request, JsonObject result)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Document != document || State is McpAppBridgeState.Closed) return;
        SendResult(request.Id, result ?? []);
    }

    /// <summary>Refuses a request of the current document.</summary>
    public void Fail(McpAppRequest request, string message, int code = RefusedCode)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Document != document || State is McpAppBridgeState.Closed) return;
        SendError(request.Id, code, message);
    }

    /// <summary>
    /// The view is going away (its row left, its thread closed): a ready view is asked to tear down and the bridge closes when
    /// it answers (or when the host gives up and calls <see cref="Close"/>); a view that never finished its handshake is sent
    /// nothing and closes at once.
    /// </summary>
    public void Teardown(string reason)
    {
        if (State is McpAppBridgeState.TearingDown or McpAppBridgeState.Closed) return;
        if (State != McpAppBridgeState.Ready)
        {
            Finish();
            return;
        }

        State = McpAppBridgeState.TearingDown;
        teardownId = "nth-teardown-" + ++teardowns;
        Send(new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = teardownId,
            ["method"] = McpAppProtocol.ResourceTeardown,
            ["params"] = new JsonObject { ["reason"] = reason ?? "" },
        });
    }

    /// <summary>A new document is loading in the view (a reload, a move between places, a new CSP): its handshake starts again,
    /// and the tool's input and result go to it again once it is initialized.</summary>
    public void NewDocument()
    {
        if (State is McpAppBridgeState.Closed or McpAppBridgeState.TearingDown) return;
        document++;
        State = McpAppBridgeState.Loading;
        initializeAnswered = false;
        inputSent = resultSent = cancelledSent = false;
        partialSent = null;
    }

    /// <summary>Stops for good: nothing more is sent or answered.</summary>
    public void Close()
    {
        if (State == McpAppBridgeState.Closed) return;
        Finish();
    }

    private void Finish()
    {
        State = McpAppBridgeState.Closed;
        teardownId = null;
        TornDown?.Invoke();
    }

    // The tool's data, in the spec's order: partial input until the full input, the input once, then the result or the
    // cancellation. Nothing goes out before the view is ready.
    private void Flush()
    {
        if (State != McpAppBridgeState.Ready) return;
        if (!inputSent && input is not null)
        {
            inputSent = true;
            SendNotification(McpAppProtocol.ToolInput, new JsonObject { ["arguments"] = input.DeepClone() });
        }
        else if (!inputSent && partialInput is not null)
        {
            var text = partialInput.ToJsonString();
            if (text != partialSent)
            {
                partialSent = text;
                SendNotification(McpAppProtocol.ToolInputPartial, new JsonObject { ["arguments"] = partialInput.DeepClone() });
            }
        }

        if (inputSent && result is not null && !resultSent)
        {
            resultSent = true;
            SendNotification(McpAppProtocol.ToolResult, (JsonObject)result.DeepClone());
        }
        else if (cancelled is not null && result is null && !cancelledSent)
        {
            cancelledSent = true;
            SendNotification(McpAppProtocol.ToolCancelled, new JsonObject { ["reason"] = cancelled });
        }
    }

    private void SendNotification(string method, JsonObject parameters) =>
        Send(new JsonObject { ["jsonrpc"] = "2.0", ["method"] = method, ["params"] = parameters });

    private void SendResult(JsonNode id, JsonObject value) =>
        Send(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id.DeepClone(), ["result"] = value.DeepClone() });

    private void SendError(JsonNode id, int code, string message) =>
        Send(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id.DeepClone(), ["error"] = new JsonObject { ["code"] = code, ["message"] = message } });

    private void Send(JsonObject message) => post(message.ToJsonString());

    private static bool IsValidId(JsonNode id) =>
        id is JsonValue value && (value.TryGetValue<string>(out var text) ? text.Length is > 0 and <= 256 : value.TryGetValue<double>(out var number) && double.IsFinite(number));

    internal static string? Text(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) ? text
        : node is JsonValue number && number.TryGetValue<double>(out var n) ? n.ToString(System.Globalization.CultureInfo.InvariantCulture) : null;
}
