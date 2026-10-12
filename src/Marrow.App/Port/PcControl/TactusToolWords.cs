// PORT-ONLY: Tactus, the chat timeline's words for nth-pc tool calls (OD-0140, OD-0145, ED-NEW-pcctl-28; DESIGN.md 4.6
// "The chat timeline shows each action as a tool row: "Clicked Save in Notepad", "Typed 42 characters"", section 6 unit 14).
// No Swift twin. A CLI provider titles an MCP call "nth-pc · pc_click" (kind Mcp, the arguments as its detail); an API
// provider's native call is titled "Tactus · Click" (kind Other, PcNativeTools.Title). Both read as one sentence built from
// the tool, its arguments and the engine's result: the window line names the app ("window: "Untitled - Notepad"
// (notepad.exe)"), the result's "done:" line names the element ("done: clicked button "Save""), else the call's target.
// A call still running reads "Clicking Save in Notepad"; a failed one "Could not click Save in Notepad"; a paused one
// "Paused: click Save in Notepad".
using System.Globalization;
using System.Text.Json.Nodes;
using Marrow.Core.Models;
using Marrow.Core.Port.PcControl;
using Marrow.Services.Port.PcControl;

namespace Marrow.App.Port.PcControl;

public static class TactusToolWords
{
    /// <summary>The API rows' title prefix (PcNativeTools.Title).</summary>
    public const string ApiPrefix = "Tactus";

    private const string Separator = " · ";

    private static readonly IReadOnlyList<PcToolSpec> Specs = [.. PcToolSchemas.All, PcToolSchemas.DelegateSpec, PcToolSchemas.AskSpec];

    /// <summary>The nth-pc tool a call ran ("pc_click"), or null for any other call.</summary>
    public static string? ToolOf(ToolCall call)
    {
        ArgumentNullException.ThrowIfNull(call);
        var at = call.Title.IndexOf(Separator, StringComparison.Ordinal);
        if (at <= 0) return null;
        var head = call.Title[..at].Trim();
        var tail = call.Title[(at + Separator.Length)..].Trim();
        if (call.ToolCallKind == ToolCall.Kind.Mcp && (head.Equals(PcToolSchemas.ServerName, StringComparison.OrdinalIgnoreCase) || head.Equals("nth_pc", StringComparison.OrdinalIgnoreCase)))
            return Specs.FirstOrDefault(s => s.Name == tail)?.Name;
        if (call.ToolCallKind == ToolCall.Kind.Other && head == ApiPrefix)
            return Specs.FirstOrDefault(s => s.Title == tail)?.Name ?? Specs.FirstOrDefault(s => s.Name == tail)?.Name;
        return null;
    }

    /// <summary>True for an nth-pc call: the row reads <see cref="Label"/> and never wears an MCP server's badge.</summary>
    public static bool Owns(ToolCall call) => ToolOf(call) is not null;

    /// <summary>The row's sentence for an nth-pc call, or null for any other call.</summary>
    public static string? Label(ToolCall call)
    {
        if (ToolOf(call) is not { } tool) return null;
        var args = Arguments(call.Detail);
        var output = call.Output;
        var app = AppOf(output) ?? LaunchedApp(tool, args);
        var element = ElementOf(output) ?? TargetOf(args);
        var (running, done, plain) = Words(tool, args, element, app);
        var first = output.Split('\n', 2)[0].Trim();
        return call.ToolCallStatus switch
        {
            ToolCall.Status.Running => running,
            ToolCall.Status.Failed => "Could not " + plain,
            ToolCall.Status.Declined => "Not allowed to " + plain,
            _ when first.StartsWith("error:", StringComparison.Ordinal) => "Could not " + plain,
            _ when first.StartsWith("waiting_for_user: paused", StringComparison.Ordinal) => "Paused: " + plain,
            _ when first.StartsWith("waiting_for_user", StringComparison.Ordinal) => "Waiting for you: " + plain,
            _ when first.StartsWith("unverified:", StringComparison.Ordinal) => done + " (not confirmed)",
            _ => done,
        };
    }

    /// <summary>
    /// The pill's line for a step a chat's audit log wrote (ED-NEW-pcctl-32): the timeline's words without the app, which
    /// the pill's title names ("Clicked Save", "Typed 42 characters into Subject", "Could not click Save").
    /// </summary>
    public static string Step(PcAuditEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var element = entry.Element is { Length: > 0 } named ? named : null;
        var (_, done, plain) = Words(entry.Tool, new JsonObject(), element, null);
        if (entry.Tool == PcToolSchemas.Type)
        {
            var typed = " " + Count(entry.TextLength ?? 0, "character") + (element is null ? "" : " into " + element);
            (done, plain) = ("Typed" + typed, "type" + typed);
        }
        return entry.Result switch
        {
            "ok" => done,
            "unverified" => done + " (not confirmed)",
            "waiting_for_user" => "Waiting for you: " + plain,
            _ => "Could not " + plain,
        };
    }

    /// <summary>(running, done, the plain verb phrase) for a tool, its arguments, the element and the app.</summary>
    private static (string Running, string Done, string Plain) Words(string tool, JsonObject args, string? element, string? app)
    {
        var at = app is null ? "" : " in " + app;
        var on = element is null ? "" : " " + element;
        switch (tool)
        {
            case PcToolSchemas.ListApps: return ("Listing apps", "Listed apps", "list apps");
            case PcToolSchemas.LaunchApp: return Three("Opening", "Opened", "open", app is null ? " an app" : " " + app);
            case PcToolSchemas.Observe: return Three("Looking at", "Looked at", "look at", app is null ? " a window" : " " + app);
            case PcToolSchemas.Click: return Three("Clicking", "Clicked", "click", on + at);
            case PcToolSchemas.Type:
                var count = Count(Text(args, "text"), "character");
                return Three("Typing", "Typed", "type", " " + count + (element is null ? at : " into" + on + at));
            case PcToolSchemas.SetValue: return Three("Setting", "Set", "set", element is null ? " a value" + at : on + at);
            case PcToolSchemas.Key:
                var keys = Text(args, "keys") is { Length: > 0 } k ? " " + k : " keys";
                var repeat = args["repeat"] is JsonValue r && r.TryGetValue<int>(out var n) && n > 1 ? " " + n.ToString(CultureInfo.InvariantCulture) + " times" : "";
                return Three("Pressing", "Pressed", "press", keys + repeat + at);
            case PcToolSchemas.Scroll:
                var direction = Text(args, "direction") is { Length: > 0 } d ? " " + d : "";
                return Three("Scrolling", "Scrolled", "scroll", direction + at);
            case PcToolSchemas.Drag: return Three("Dragging", "Dragged", "drag", at);
            case PcToolSchemas.Action: return ActionWords(Text(args, "action"), on, at);
            case PcToolSchemas.FocusWindow: return Three("Showing", "Showed", "show", app is null ? " a window" : " " + app);
            case PcToolSchemas.Batch:
                var steps = args["actions"] is JsonArray list ? list.Count : 0;
                return Three("Running", "Ran", "run", " " + (steps > 0 ? Count(steps, "step") : "steps") + at);
            case PcToolSchemas.Read: return Three("Reading", "Read", "read", " text" + at);
            case PcToolSchemas.Zoom: return ("Zooming in on a screenshot", "Zoomed in on a screenshot", "zoom in on a screenshot");
            case PcToolSchemas.Wait: return Three("Waiting for", "Waited for", "wait for", app is null ? " a window" : " " + app);
            case PcToolSchemas.Delegate: return ("Handing steps to the fast helper", "Handed steps to the fast helper", "hand steps to the fast helper");
            case PcToolSchemas.Ask:
                var question = Text(args, "question") is { Length: > 0 } q ? ": " + Short(q, 80) : "";
                return ("Asking you" + question, "Asked you" + question, "ask you" + question);
            default: return ("Using " + tool, "Used " + tool, "use " + tool);
        }
    }

    private static (string, string, string) Three(string running, string done, string plain, string rest) => (running + rest, done + rest, plain + rest);

    // pc_action's actions (DESIGN.md tool 10): each reads as its own verb
    private static (string, string, string) ActionWords(string? action, string on, string at)
    {
        var (running, done, plain, own) = (action ?? "").ToLowerInvariant() switch
        {
            "expand" => ("Expanding", "Expanded", "expand", false),
            "collapse" => ("Collapsing", "Collapsed", "collapse", false),
            "toggle" => ("Toggling", "Toggled", "toggle", false),
            "select" => ("Selecting", "Selected", "select", false),
            "scroll-into-view" or "scroll_into_view" => ("Scrolling to", "Scrolled to", "scroll to", false),
            "focus" => ("Focusing", "Focused", "focus", false),
            "minimize" => ("Minimizing", "Minimized", "minimize", true),
            "maximize" => ("Maximizing", "Maximized", "maximize", true),
            "restore" => ("Restoring", "Restored", "restore", true),
            "close" => ("Closing", "Closed", "close", true),
            _ => ("Using", "Used", "use", false),
        };
        // a window action names the window's app; an element action names the element
        var rest = own ? (at.Length > 0 ? " " + at[4..] : " a window") : (on.Length > 0 ? on + at : " an element" + at);
        return (running + rest, done + rest, plain + rest);
    }

    private static JsonObject Arguments(string? detail)
    {
        if (string.IsNullOrWhiteSpace(detail)) return [];
        try
        {
            return JsonNode.Parse(detail) as JsonObject ?? [];
        }
        catch (System.Text.Json.JsonException)
        {
            return [];
        }
    }

    private static string? Text(JsonObject args, string key) =>
        args[key] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static string Count(string? text, string noun) => Count(text is null ? 0 : new StringInfo(text).LengthInTextElements, noun);

    private static string Count(int count, string noun) => count.ToString(CultureInfo.InvariantCulture) + " " + noun + (count == 1 ? "" : "s");

    private static string Short(string text, int max)
    {
        var line = text.ReplaceLineEndings(" ").Trim();
        return line.Length <= max ? line : line[..(max - 1)].TrimEnd() + "…";
    }

    /// <summary>The app of the result's window line: "window: "Untitled - Notepad" (notepad.exe) ..." reads "Notepad".</summary>
    public static string? AppOf(string output)
    {
        foreach (var raw in output.Split('\n'))
        {
            var line = raw.Trim();
            if (!line.StartsWith("window: \"", StringComparison.Ordinal)) continue;
            if (Quoted(line, "window: ".Length) is not { } title) continue;
            var open = line.IndexOf(" (", title.End, StringComparison.Ordinal);
            var close = open < 0 ? -1 : line.IndexOf(')', open);
            if (open < 0 || close <= open + 2) return title.Text.Length > 0 ? title.Text : null;
            var exe = line[(open + 2)..close];
            // the title's last part names the app as the app itself does ("Book1 - Excel", "Page - Google Chrome") when it
            // holds the exe's name; else the exe's own name ("EXCEL.EXE" alone reads "EXCEL")
            var last = title.Text.Split(new[] { " - ", " – ", " — " }, StringSplitOptions.None)[^1].Trim();
            var bare = Path.GetFileNameWithoutExtension(exe);
            if (bare.Length > 0 && last.Length is > 0 and <= 40 && last.Contains(bare, StringComparison.OrdinalIgnoreCase)) return last;
            return PcChatMirror.NameOf("exe:" + exe);
        }
        return null;
    }

    /// <summary>The element the result's "done:" line names: "done: clicked button "Save"" reads "Save".</summary>
    public static string? ElementOf(string output)
    {
        foreach (var raw in output.Split('\n'))
        {
            var line = raw.Trim();
            if (!line.StartsWith("done: ", StringComparison.Ordinal)) continue;
            var quote = line.IndexOf('"', StringComparison.Ordinal);
            return quote < 0 ? null : Quoted(line, quote)?.Text is { Length: > 0 } name ? name : null;
        }
        return null;
    }

    // the call's own locator: target {name, role}
    private static string? TargetOf(JsonObject args) =>
        args["target"] is JsonObject target && Text(target, "name") is { Length: > 0 } name ? Short(name, 60) : null;

    private static string? LaunchedApp(string tool, JsonObject args)
    {
        if (tool != PcToolSchemas.LaunchApp) return null;
        if (Text(args, "path") is { Length: > 0 } path) return PcChatMirror.NameOf("exe:" + path);
        return Text(args, "app_id") is { Length: > 0 } id ? PcChatMirror.NameOf(id.Contains('!', StringComparison.Ordinal) ? "aumid:" + id : "exe:" + id) : null;
    }

    // a quoted string starting at index (the opening quote), with \" and \\ unescaped (PcWindowLine.Quote)
    private static (string Text, int End)? Quoted(string line, int index)
    {
        if (index >= line.Length || line[index] != '"') return null;
        var text = new System.Text.StringBuilder();
        for (var i = index + 1; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '\\' && i + 1 < line.Length)
            {
                text.Append(line[++i]);
                continue;
            }
            if (c == '"') return (text.ToString(), i + 1);
            text.Append(c);
        }
        return null;
    }
}
