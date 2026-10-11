// PORT-ONLY: PC control (OD-0140, ED-NEW-pcctl-1; DESIGN.md 2.2). The fifteen nth-pc tools: stable names, short titles for
// the timeline, descriptions and JSON input schemas, in nth's own words. One place for the MCP server and for the API
// providers' native tool loop.
using System.Text.Json.Nodes;

namespace Marrow.Core.Port.PcControl;

/// <summary>One tool: wire name, timeline title, description, whether it only reads, and its input schema (JSON text).</summary>
public sealed record PcToolSpec(string Name, string Title, string Description, bool ReadOnly, string InputSchema)
{
    public JsonObject Schema() => (JsonObject)JsonNode.Parse(InputSchema)!;

    /// <summary>The OpenAI-style function definition API providers send.</summary>
    public JsonObject FunctionDefinition() => new()
    {
        ["type"] = "function",
        ["function"] = new JsonObject { ["name"] = Name, ["description"] = Description, ["parameters"] = Schema() },
    };
}

public static class PcToolSchemas
{
    public const string ServerName = "nth-pc";

    public const string ListApps = "pc_list_apps";
    public const string LaunchApp = "pc_launch_app";
    public const string Observe = "pc_observe";
    public const string Click = "pc_click";
    public const string Type = "pc_type";
    public const string SetValue = "pc_set_value";
    public const string Key = "pc_key";
    public const string Scroll = "pc_scroll";
    public const string Drag = "pc_drag";
    public const string Action = "pc_action";
    public const string FocusWindow = "pc_focus_window";
    public const string Batch = "pc_batch";
    public const string Read = "pc_read";
    public const string Zoom = "pc_zoom";
    public const string Wait = "pc_wait";

    /// <summary>The tools pc_batch may run as steps.</summary>
    public static IReadOnlySet<string> BatchSteps { get; } = new HashSet<string>(StringComparer.Ordinal) { Click, Type, SetValue, Key, Scroll, Drag, Action };

    /// <summary>The secondary actions pc_action accepts.</summary>
    public static IReadOnlyList<string> ElementActions { get; } =
        ["expand", "collapse", "toggle", "select", "focus", "scroll_into_view", "minimize", "maximize", "restore", "close"];

    private const string WindowProp = "\"window\":{\"type\":\"string\",\"description\":\"The window, as pc_list_apps gives it (w:...).\"}";
    private const string ObsProp = "\"obs\":{\"type\":\"string\",\"description\":\"The observation the point was read from (o:...). Points are refused for any other.\"}";
    private const string ElementProp = "\"element\":{\"type\":\"integer\",\"minimum\":1,\"description\":\"An element number from the window's latest tree.\"}";
    private const string PointProp = "\"point\":{\"type\":\"array\",\"items\":{\"type\":\"number\"},\"minItems\":2,\"maxItems\":2,\"description\":\"[x, y] in the screenshot's pixels, window-relative.\"}";
    private const string ModifiersProp = "\"modifiers\":{\"type\":\"string\",\"description\":\"Keys held during the action, such as ctrl or ctrl+shift.\"}";

    public static IReadOnlyList<PcToolSpec> All { get; } =
    [
        new(ListApps, "List apps",
            "List the running apps that have windows, with each window's id, title and screen, and whether you may use the app. With installed, also list apps from the Start menu that can be launched.",
            true, "{\"type\":\"object\",\"properties\":{\"installed\":{\"type\":\"boolean\",\"default\":false,\"description\":\"Also list installed apps.\"}},\"additionalProperties\":false}"),
        new(LaunchApp, "Launch app",
            "Start an app by its app_id from pc_list_apps (installed), or by the full path of an .exe. Returns its new windows. Never runs a shell or a command line.",
            false, "{\"type\":\"object\",\"properties\":{\"app_id\":{\"type\":\"string\"},\"path\":{\"type\":\"string\"}},\"additionalProperties\":false}"),
        new(Observe, "Look at window",
            "Look at one window. Returns a new obs id and the window's accessibility tree as numbered lines (a diff against your last look unless full is true), the focused element and the selection. Adds a screenshot when screenshot is yes, or in auto when the tree has little to act on.",
            true, "{\"type\":\"object\",\"properties\":{" + WindowProp + ",\"tree\":{\"type\":\"boolean\",\"default\":true},\"screenshot\":{\"type\":\"string\",\"enum\":[\"auto\",\"yes\",\"no\"],\"default\":\"auto\"},\"query\":{\"type\":\"string\",\"description\":\"Keep only lines containing this text, with their parents.\"},\"full\":{\"type\":\"boolean\",\"default\":false,\"description\":\"The whole tree, not a diff.\"},\"max_lines\":{\"type\":\"integer\",\"minimum\":20,\"maximum\":2000,\"default\":300}},\"required\":[\"window\"],\"additionalProperties\":false}"),
        new(Click, "Click",
            "Click an element by its number (preferred) or a point from the latest screenshot. A plain left click on an element uses its own action (press, toggle, select, expand) when it has one.",
            false, "{\"type\":\"object\",\"properties\":{" + WindowProp + "," + ElementProp + "," + PointProp + "," + ObsProp + ",\"button\":{\"type\":\"string\",\"enum\":[\"left\",\"right\",\"middle\"],\"default\":\"left\"},\"count\":{\"type\":\"integer\",\"minimum\":1,\"maximum\":3,\"default\":1}," + ModifiersProp + "},\"required\":[\"window\"],\"additionalProperties\":false}"),
        new(Type, "Type",
            "Type text into the focused field, or into element after focusing it. Never for passwords, codes or card numbers. mode paste puts the text on the clipboard, pastes it and restores the clipboard. submit presses Enter after.",
            false, "{\"type\":\"object\",\"properties\":{" + WindowProp + ",\"text\":{\"type\":\"string\",\"maxLength\":20000}," + ElementProp + ",\"mode\":{\"type\":\"string\",\"enum\":[\"type\",\"paste\"],\"default\":\"type\"},\"submit\":{\"type\":\"boolean\",\"default\":false}},\"required\":[\"window\",\"text\"],\"additionalProperties\":false}"),
        new(SetValue, "Set value",
            "Replace an element's whole value (a text field, a slider or a spin box) in one step. Refused for password and read-only fields.",
            false, "{\"type\":\"object\",\"properties\":{" + WindowProp + "," + ElementProp + ",\"value\":{\"type\":\"string\"}},\"required\":[\"window\",\"element\",\"value\"],\"additionalProperties\":false}"),
        new(Key, "Press keys",
            "Press a key or a chord in the window, such as enter, ctrl+s, alt+f4, shift+tab, f5 or numpad0. The Windows key is never pressed.",
            false, "{\"type\":\"object\",\"properties\":{" + WindowProp + ",\"keys\":{\"type\":\"string\"},\"repeat\":{\"type\":\"integer\",\"minimum\":1,\"maximum\":20,\"default\":1}},\"required\":[\"window\",\"keys\"],\"additionalProperties\":false}"),
        new(Scroll, "Scroll",
            "Scroll an element by its number, or at a point from the latest screenshot, by pages or lines.",
            false, "{\"type\":\"object\",\"properties\":{" + WindowProp + "," + ElementProp + "," + PointProp + "," + ObsProp + ",\"direction\":{\"type\":\"string\",\"enum\":[\"up\",\"down\",\"left\",\"right\"]},\"amount\":{\"type\":\"number\",\"minimum\":0.1,\"maximum\":50,\"default\":1},\"unit\":{\"type\":\"string\",\"enum\":[\"page\",\"line\"],\"default\":\"page\"}},\"required\":[\"window\",\"direction\"],\"additionalProperties\":false}"),
        new(Drag, "Drag",
            "Press at the first point, move through the others and release at the last, for canvases, sliders and drawing. Points come from the latest screenshot.",
            false, "{\"type\":\"object\",\"properties\":{" + WindowProp + "," + ObsProp + ",\"path\":{\"type\":\"array\",\"minItems\":2,\"maxItems\":50,\"items\":{\"type\":\"array\",\"items\":{\"type\":\"number\"},\"minItems\":2,\"maxItems\":2}}," + ModifiersProp + ",\"hold_ms\":{\"type\":\"integer\",\"minimum\":0,\"maximum\":2000}},\"required\":[\"window\",\"obs\",\"path\"],\"additionalProperties\":false}"),
        new(Action, "Element action",
            "Run one of the actions an element lists after actions: (expand, collapse, toggle, select, set_value through pc_set_value), or focus, scroll_into_view, and the window actions minimize, maximize, restore and close.",
            false, "{\"type\":\"object\",\"properties\":{" + WindowProp + "," + ElementProp + ",\"action\":{\"type\":\"string\",\"enum\":[\"expand\",\"collapse\",\"toggle\",\"select\",\"focus\",\"scroll_into_view\",\"minimize\",\"maximize\",\"restore\",\"close\"]}},\"required\":[\"window\",\"element\",\"action\"],\"additionalProperties\":false}"),
        new(FocusWindow, "Focus window",
            "Bring the window to the front. Input tools do this themselves; use it when a point was reported as covered.",
            false, "{\"type\":\"object\",\"properties\":{" + WindowProp + "},\"required\":[\"window\"],\"additionalProperties\":false}"),
        new(Batch, "Run steps",
            "Run 2 to 20 sure steps in one call (pc_click, pc_type, pc_set_value, pc_key, pc_scroll, pc_drag, pc_action), stopping at the first error, then look once. Each step is an object with tool and that tool's arguments; window comes from the batch.",
            false, "{\"type\":\"object\",\"properties\":{" + WindowProp + ",\"actions\":{\"type\":\"array\",\"minItems\":2,\"maxItems\":20,\"items\":{\"type\":\"object\",\"properties\":{\"tool\":{\"type\":\"string\",\"enum\":[\"pc_click\",\"pc_type\",\"pc_set_value\",\"pc_key\",\"pc_scroll\",\"pc_drag\",\"pc_action\"]}},\"required\":[\"tool\"]}},\"observe\":{\"type\":\"string\",\"enum\":[\"diff\",\"full\",\"screenshot\",\"none\"],\"default\":\"diff\"}},\"required\":[\"window\",\"actions\"],\"additionalProperties\":false}"),
        new(Read, "Read text",
            "Read the full text of a document, text field or list, page by page, so pc_observe stays small. Without element, reads the focused element.",
            true, "{\"type\":\"object\",\"properties\":{" + WindowProp + "," + ElementProp + ",\"offset\":{\"type\":\"integer\",\"minimum\":0,\"default\":0},\"length\":{\"type\":\"integer\",\"minimum\":1,\"maximum\":20000,\"default\":8000}},\"required\":[\"window\"],\"additionalProperties\":false}"),
        new(Zoom, "Zoom",
            "A sharper crop of a region of the latest screenshot, up to twice the size, for small text.",
            true, "{\"type\":\"object\",\"properties\":{" + ObsProp + ",\"region\":{\"type\":\"array\",\"items\":{\"type\":\"number\"},\"minItems\":4,\"maxItems\":4,\"description\":\"[x, y, width, height] in the screenshot's pixels.\"}},\"required\":[\"obs\",\"region\"],\"additionalProperties\":false}"),
        new(Wait, "Wait",
            "Wait until the window is idle, until some text appears, or until some text is gone, at most 15 seconds. Do not sleep any other way.",
            true, "{\"type\":\"object\",\"properties\":{" + WindowProp + ",\"until\":{\"type\":\"string\",\"enum\":[\"idle\",\"text\",\"gone\"]},\"text\":{\"type\":\"string\"},\"timeout_ms\":{\"type\":\"integer\",\"minimum\":100,\"maximum\":15000,\"default\":5000}},\"required\":[\"window\",\"until\"],\"additionalProperties\":false}"),
    ];

    public static PcToolSpec? Find(string? name) => All.FirstOrDefault(t => t.Name == name);
}
