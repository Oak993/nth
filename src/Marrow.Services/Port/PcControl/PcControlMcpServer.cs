// PORT-ONLY: Tactus, PC control (OD-0140, OD-0141, OD-0144, ED-NEW-pcctl-10; DESIGN.md 3.3, 5.2, critique C10). The MCP
// contract of the nth-pc tools: one JSON-RPC line in, one line out, as the advisor's server, except that calls never wait
// on each other. Every tools/call runs as its own task with its own cancellation, a ping is answered during long calls,
// replies carry their own id in completion order, notifications/cancelled cancels the call it names (which then sends no
// reply), and a call that carried a progress token gets notifications/progress while it runs, so a client's own timeout
// sees the call alive. A connection without a registration, or while Tactus is off, lists no tools and runs none.
// PORT-ONLY (web surfaces W11, ED-NEW-web-w11-1): with Web set, the same server also lists and runs the web_* tools while
// Visus model access is on, independent of Tactus's own switch.
using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using Marrow.Core.Models;
using Marrow.Core.Port.PcControl;
using Marrow.Core.Web;
using Marrow.Services.Web;

namespace Marrow.Services.Port.PcControl;

/// <summary>
/// A session Tactus serves: its chat thread, the provider and model it launched with, whether that model takes images,
/// and the Hydra head's name when the chat is a head (the cards and busy_window name it).
/// </summary>
public sealed record PcRegistration(string Thread, ProviderKind Provider, string? Model, bool Vision)
{
    public string? Head { get; init; }
}

public sealed class PcControlMcpServer(PcRegistration? registration, Func<PcRegistration, PcControlEngine?> engines, Func<PcSettings> settings, Func<string, Task>? notify = null)
{
    public const string ServerName = PcToolSchemas.ServerName;

    /// <summary>How often a call that carried a progress token reports that it is still running.</summary>
    public const int ProgressEveryMs = 5_000;

    /// <summary>The progress interval this server uses (<see cref="ProgressEveryMs"/>; tests shorten it).</summary>
    public int ProgressMs { get; init; } = ProgressEveryMs;

    /// <summary>PORT-ONLY (web surfaces W11): the web tools this server also offers; null offers none.</summary>
    public WebMcpTools? Web { get; init; }

    private readonly ConcurrentDictionary<string, CancellationTokenSource> calls = new(StringComparer.Ordinal);

    /// <summary>The calls running now (for tests and the pill's count).</summary>
    public int Running => calls.Count;

    /// <summary>One JSON-RPC line in; the reply line, or null for a notification or a call the client cancelled.</summary>
    public async Task<string?> Handle(string line, CancellationToken cancellationToken)
    {
        JsonNode? request;
        try
        {
            request = JsonNode.Parse(line);
        }
        catch (JsonException)
        {
            return Error(null, -32700, "Invalid JSON.");
        }
        if (request is not JsonObject message) return Error(null, -32600, "Invalid MCP request.");
        var method = Text(message["method"]);
        if (!message.ContainsKey("id"))
        {
            if (method == "notifications/cancelled" && message["params"]?["requestId"] is { } cancelled) Cancel(Key(cancelled));
            return null;
        }
        var id = message["id"]?.DeepClone();
        var parameters = message["params"];
        try
        {
            switch (method)
            {
                case "initialize":
                    return Result(id, new JsonObject
                    {
                        ["protocolVersion"] = ProtocolVersion(parameters),
                        ["capabilities"] = new JsonObject { ["tools"] = new JsonObject() },
                        ["serverInfo"] = new JsonObject { ["name"] = ServerName, ["version"] = "1.0.0" },
                        ["instructions"] = Instructions(),
                    });
                case "ping":
                    return Result(id, new JsonObject());
                case "tools/list":
                    return Result(id, new JsonObject { ["tools"] = Tools() });
                case "tools/call":
                    return await Call(id, parameters, cancellationToken).ConfigureAwait(false);
                default:
                    return Error(id, -32601, "Unsupported MCP method.");
            }
        }
        catch (ArgumentException error)
        {
            return Error(id, -32602, error.Message);
        }
    }

    private bool Offered() => registration is not null && settings().Enabled;

    private bool WebOffered() => Web is not null && Web.Offered(registration);

    private string Instructions() => (Offered(), WebOffered()) switch
    {
        (true, true) => PcInstructions.Text + "\n\n" + WebAgentText.Instructions,
        (false, true) => WebAgentText.Instructions,
        (true, false) => PcInstructions.Text,
        _ => "Tactus (PC control) is off for this chat.",
    };

    private JsonArray Tools()
    {
        var tools = new JsonArray();
        if (WebOffered())
        {
            foreach (var web in WebMcpTools.Tools()) tools.Add(web);
        }
        if (!Offered()) return tools;
        foreach (var spec in PcToolSchemas.Listed(settings()))
        {
            tools.Add(new JsonObject
            {
                ["name"] = spec.Name,
                ["title"] = spec.Title,
                ["description"] = spec.Description,
                ["inputSchema"] = spec.Schema(),
                ["annotations"] = new JsonObject { ["readOnlyHint"] = spec.ReadOnly, ["openWorldHint"] = false },
            });
        }
        return tools;
    }

    private async Task<string?> Call(JsonNode? id, JsonNode? parameters, CancellationToken cancellationToken)
    {
        var name = Text(parameters?["name"]);
        var web = WebToolSchemas.IsWebTool(name) && WebOffered();
        if (!web && (!Offered() || registration is null || name is null || !PcToolSchemas.Listed(settings()).Any(spec => spec.Name == name)))
            throw new ArgumentException("Unknown nth-pc tool.");
        if (parameters?["arguments"] is { } given && given is not JsonObject) throw new ArgumentException("Tool arguments must be an object.");
        var arguments = (parameters?["arguments"] as JsonObject)?.DeepClone() as JsonObject ?? [];
        var engine = web ? null : engines(registration!) ?? throw new ArgumentException("Tactus is not available here.");

        var key = Key(id);
        using var call = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        // A second call reusing a running id is refused rather than replacing the first one's cancellation.
        if (key is not null && !calls.TryAdd(key, call)) return Error(id, -32600, "A call with this id is already running.");
        using var progress = CancellationTokenSource.CreateLinkedTokenSource(call.Token);
        var reporting = parameters?["_meta"]?["progressToken"] is { } token && notify is not null
            ? Report(token.DeepClone(), web ? "Visus is still working on this step." : "Tactus is still working on this step.", progress.Token)
            : Task.CompletedTask;
        try
        {
            JsonObject content;
            try
            {
                content = web
                    ? await Web!.Call(registration!, name!, arguments, call.Token).ConfigureAwait(false)
                    : Content(await engine!.Call(name!, arguments, call.Token).ConfigureAwait(false), registration!.Vision);
            }
            catch (OperationCanceledException) when (call.IsCancellationRequested)
            {
                return null;
            }
            // A call the client cancelled sends nothing, whatever the engine answered on its way out.
            if (call.IsCancellationRequested) return null;
            return Result(id, content);
        }
        finally
        {
            await progress.CancelAsync().ConfigureAwait(false);
            await reporting.ConfigureAwait(false);
            if (key is not null) calls.TryRemove(new KeyValuePair<string, CancellationTokenSource>(key, call));
        }
    }

    /// <summary>notifications/progress every <see cref="ProgressMs"/> until the call ends.</summary>
    private async Task Report(JsonNode token, string message, CancellationToken cancellationToken)
    {
        var count = 0;
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(ProgressMs));
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                count++;
                var note = new JsonObject
                {
                    ["jsonrpc"] = "2.0",
                    ["method"] = "notifications/progress",
                    ["params"] = new JsonObject
                    {
                        ["progressToken"] = token.DeepClone(),
                        ["progress"] = count,
                        ["message"] = message,
                    },
                };
                await notify!(note.ToJsonString()).ConfigureAwait(false);
            }
        }
        catch (Exception error) when (error is OperationCanceledException or IOException or ObjectDisposedException)
        {
        }
    }

    private void Cancel(string? key)
    {
        if (key is not null && calls.TryGetValue(key, out var call))
        {
            try
            {
                call.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }
    }

    /// <summary>
    /// The tool result: the text part (status, window line, body), then the screenshot and its full-resolution crops as
    /// image parts for a model that takes images. An error is a tool error (isError), never a protocol error, so the model
    /// reads the recovery hint.
    /// </summary>
    public static JsonObject Content(PcResult result, bool vision)
    {
        var parts = new JsonArray { new JsonObject { ["type"] = "text", ["text"] = result.Render() } };
        if (vision)
        {
            if (result.Image is { } image) parts.Add(Image(image));
            foreach (var detail in result.Details) parts.Add(Image(detail));
        }
        return new JsonObject { ["content"] = parts, ["isError"] = result.Error is not null };
    }

    private static JsonObject Image(PcImage image) => new()
    {
        ["type"] = "image",
        ["data"] = Convert.ToBase64String(image.Jpeg),
        ["mimeType"] = "image/jpeg",
    };

    /// <summary>A JSON-RPC id as a dictionary key: numbers and strings never collide ("1" and 1 differ).</summary>
    private static string? Key(JsonNode? id) => id switch
    {
        null => null,
        JsonValue value when value.TryGetValue<string>(out var text) => "s:" + text,
        _ => "n:" + id.ToJsonString(),
    };

    private static string? Text(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static string Result(JsonNode? id, JsonObject result) =>
        new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id?.DeepClone(), ["result"] = result }.ToJsonString();

    private static string Error(JsonNode? id, int code, string message) =>
        new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id?.DeepClone(), ["error"] = new JsonObject { ["code"] = code, ["message"] = message } }.ToJsonString();

    private static string ProtocolVersion(JsonNode? parameters)
    {
        var version = Text(parameters?["protocolVersion"]);
        return version is "2024-11-05" or "2025-03-26" or "2025-06-18" or "2025-11-25" ? version : "2025-11-25";
    }
}
