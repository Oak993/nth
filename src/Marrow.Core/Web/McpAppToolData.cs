// PORT-ONLY: web surfaces W19 (DESIGN.md 7.6 "sends ui/notifications/tool-input-partial while arguments stream, then
// tool-input and tool-result"; ED-NEW-web-w19-1). What an MCP App is told about the tool call it draws, read from the thread's
// ToolCall: the arguments (a complete JSON object, or the best-effort object of arguments still streaming, closed where they
// stop) and the result as the protocol's CallToolResult (text, images, links, structuredContent and _meta as the thread kept
// them). A call the user declined or that was stopped is cancelled, not a result. No Swift counterpart.
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Marrow.Core.Models;

namespace Marrow.Core.Web;

/// <summary>What an app hears of its call, in the order it may hear it.</summary>
public enum McpAppToolStage
{
    /// <summary>Nothing yet (no arguments that read as an object).</summary>
    None,

    /// <summary>Arguments still streaming: a partial object.</summary>
    Partial,

    /// <summary>The full arguments; the call runs.</summary>
    Input,

    /// <summary>The call finished (also with an error).</summary>
    Result,

    /// <summary>The call was declined or stopped.</summary>
    Cancelled,
}

public static class McpAppToolData
{
    /// <summary>The stage a call is at, with its arguments (null before any).</summary>
    public static (McpAppToolStage Stage, JsonObject? Arguments) Read(ToolCall call)
    {
        ArgumentNullException.ThrowIfNull(call);
        var complete = Arguments(call.Detail);
        var arguments = complete ?? (call.ToolCallStatus == ToolCall.Status.Running ? PartialArguments(call.Detail) : null);
        var stage = call.ToolCallStatus switch
        {
            ToolCall.Status.Completed or ToolCall.Status.Failed => McpAppToolStage.Result,
            ToolCall.Status.Declined => McpAppToolStage.Cancelled,
            _ when complete is not null => McpAppToolStage.Input,
            _ when arguments is not null => McpAppToolStage.Partial,
            _ => McpAppToolStage.None,
        };
        return (stage, arguments);
    }

    /// <summary>The arguments as a JSON object, or null when they are not one (yet).</summary>
    public static JsonObject? Arguments(string? detail)
    {
        if (string.IsNullOrWhiteSpace(detail)) return null;
        try
        {
            return JsonNode.Parse(detail) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// The object an argument stream has said so far: an open string, array or object is closed where the text stops, a
    /// dangling key or separator is dropped. Null when even that is not an object.
    /// </summary>
    public static JsonObject? PartialArguments(string? detail)
    {
        if (string.IsNullOrWhiteSpace(detail)) return null;
        var text = detail.TrimStart();
        if (text.Length == 0 || text[0] != '{') return null;
        var closers = new Stack<char>();
        var inString = false;
        var escaped = false;
        foreach (var c in text)
        {
            if (inString)
            {
                if (escaped) escaped = false;
                else if (c == '\\') escaped = true;
                else if (c == '"') inString = false;
                continue;
            }

            switch (c)
            {
                case '"': inString = true; break;
                case '{': closers.Push('}'); break;
                case '[': closers.Push(']'); break;
                case '}' or ']' when closers.Count > 0: closers.Pop(); break;
            }
        }

        var repaired = new StringBuilder(text);
        if (escaped) repaired.Length--;
        if (inString) repaired.Append('"');
        // Try the text closed as it stands, then without its last unfinished member, a few times at most.
        for (var attempt = 0; attempt < 4; attempt++)
        {
            var candidate = repaired.ToString().TrimEnd().TrimEnd(',', ':').TrimEnd();
            var closed = candidate + new string([.. closers]);
            try
            {
                if (JsonNode.Parse(closed) is JsonObject parsed) return parsed;
            }
            catch (JsonException)
            {
            }

            var cut = candidate.LastIndexOfAny([',', '{', '[']);
            if (cut <= 0) return closers.Count > 0 && candidate.StartsWith('{') ? [] : null;
            if (candidate[cut] is '{' or '[')
            {
                repaired = new StringBuilder(candidate[..(cut + 1)]);
            }
            else repaired = new StringBuilder(candidate[..cut]);

            // Recount what is open in what is left.
            closers.Clear();
            var open = false;
            var esc = false;
            foreach (var c in repaired.ToString())
            {
                if (open)
                {
                    if (esc) esc = false;
                    else if (c == '\\') esc = true;
                    else if (c == '"') open = false;
                    continue;
                }

                if (c == '"') open = true;
                else if (c == '{') closers.Push('}');
                else if (c == '[') closers.Push(']');
                else if (c is '}' or ']' && closers.Count > 0) closers.Pop();
            }
        }

        return null;
    }

    /// <summary>The call's result as an MCP CallToolResult.</summary>
    public static JsonObject Result(ToolCall call)
    {
        ArgumentNullException.ThrowIfNull(call);
        var content = new JsonArray();
        if (call.Output.Length > 0) content.Add(new JsonObject { ["type"] = "text", ["text"] = call.Output });
        if (call.Payload is { } payload)
        {
            foreach (var image in payload.Images)
                if (image.Data is { Length: > 0 } data) content.Add(new JsonObject { ["type"] = "image", ["data"] = data, ["mimeType"] = image.MimeType });
            foreach (var link in payload.ResourceLinks)
            {
                if (link.Embedded && link.Text is { } embedded)
                    content.Add(new JsonObject { ["type"] = "resource", ["resource"] = new JsonObject { ["uri"] = link.Uri, ["mimeType"] = link.MimeType, ["text"] = embedded } });
                else
                    content.Add(new JsonObject { ["type"] = "resource_link", ["uri"] = link.Uri, ["name"] = link.Name ?? link.Uri, ["mimeType"] = link.MimeType });
            }
        }

        var result = new JsonObject { ["content"] = content, ["isError"] = call.ToolCallStatus == ToolCall.Status.Failed };
        if (Object(call.Payload?.StructuredContent) is { } structured) result["structuredContent"] = structured;
        if (Object(call.Payload?.Meta) is { } meta) result["_meta"] = meta;
        return result;
    }

    /// <summary>Why the call ended without a result.</summary>
    public static string CancelReason(ToolCall call) => call.ToolCallStatus == ToolCall.Status.Declined ? "The user declined the call." : "The call was stopped.";

    private static JsonObject? Object(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonNode.Parse(json) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
