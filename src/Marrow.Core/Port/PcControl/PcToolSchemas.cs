// PORT-ONLY: Tactus, PC control (OD-0140, OD-0141, ED-NEW-pcctl-1, ED-NEW-pcctl-6, ED-NEW-pcctl-7; DESIGN.md 2.2). The
// fifteen nth-pc tools (and pc_delegate, listed only with the fast helper on): stable wire names, short titles for the
// timeline, descriptions and JSON input schemas, in nth's own words. Every tool that acts on a window names it, so a model
// can work in several apps in one turn; every action takes an element number or a target locator and an optional expect
// that is checked before the call returns. One place for the MCP server and for the API providers' native tool loop.
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
    public const string Delegate = "pc_delegate";
    public const string Ask = "pc_ask";

    /// <summary>The most steps one pc_batch runs.</summary>
    public const int MaxBatchSteps = 50;

    /// <summary>The tools pc_batch may run as steps.</summary>
    public static IReadOnlySet<string> BatchSteps { get; } = new HashSet<string>(StringComparer.Ordinal) { Click, Type, SetValue, Key, Scroll, Drag, Action, Read, Wait };

    /// <summary>
    /// The verbs a fast helper may use (DESIGN.md 2.2, critique C30): observe, read, click, type, set value, select (pc_action
    /// select only), scroll and wait. Never key chords, drag, launch or close.
    /// </summary>
    public static IReadOnlySet<string> DelegateVerbs { get; } = new HashSet<string>(StringComparer.Ordinal) { Observe, Read, Click, Type, SetValue, Action, Scroll, Wait };

    /// <summary>The most steps one pc_delegate plan holds.</summary>
    public const int MaxDelegateSteps = 40;

    /// <summary>The secondary actions pc_action accepts.</summary>
    public static IReadOnlyList<string> ElementActions { get; } =
        ["expand", "collapse", "toggle", "select", "focus", "scroll_into_view", "minimize", "maximize", "restore", "close"];

    private const string WindowProp = "\"window\":{\"type\":\"string\",\"description\":\"The window, as pc_list_apps gives it (w:...).\"}";
    private const string ObsProp = "\"obs\":{\"type\":\"string\",\"description\":\"The observation the point was read from (o:...). Points are refused for any other.\"}";
    private const string ElementProp = "\"element\":{\"type\":\"integer\",\"minimum\":1,\"description\":\"An element number from the window's tree.\"}";
    private const string TargetProp = "\"target\":{\"type\":\"object\",\"description\":\"The element by what it is, instead of its number: name, type (control type such as button), id (its automation id), within (the name of a container it sits in) and nth (which of equal matches, from 1). Two matches are refused, never guessed.\",\"properties\":{\"name\":{\"type\":\"string\"},\"type\":{\"type\":\"string\"},\"id\":{\"type\":\"string\"},\"within\":{\"type\":\"string\"},\"nth\":{\"type\":\"integer\",\"minimum\":1}},\"additionalProperties\":false}";
    private const string PointProp = "\"point\":{\"type\":\"array\",\"items\":{\"type\":\"number\"},\"minItems\":2,\"maxItems\":2,\"description\":\"[x, y] in the screenshot's pixels, window-relative. Only where the tree has no element.\"}";
    private const string ModifiersProp = "\"modifiers\":{\"type\":\"string\",\"description\":\"Keys held during the action, such as ctrl or ctrl+shift.\"}";
    private const string ExpectProp = "\"expect\":{\"type\":\"object\",\"description\":\"What must be true after the action, checked before the call returns: the target's value, name, selected, checked or focused_in_app; an element that appears; text that is gone; text_in_window; a window that opens (window_opens with title or class, and bind to name it for later batch steps, such as $dlg).\",\"properties\":{\"value\":{\"type\":\"string\"},\"name\":{\"type\":\"string\"},\"appears\":{\"type\":\"string\"},\"gone\":{\"type\":\"string\"},\"selected\":{\"type\":\"boolean\"},\"checked\":{\"type\":\"boolean\"},\"focused_in_app\":{\"type\":\"boolean\"},\"window_opens\":{\"type\":\"object\",\"properties\":{\"title\":{\"type\":\"string\"},\"class\":{\"type\":\"string\"}},\"additionalProperties\":false},\"text_in_window\":{\"type\":\"string\"},\"bind\":{\"type\":\"string\"}},\"additionalProperties\":false}";

    public static IReadOnlyList<PcToolSpec> All { get; } =
    [
        new(ListApps, "List apps",
            "List the running apps that have windows: each app's name, exe, app_id and whether Tactus may use it (approved: none, chat, always or blocked), and each window's id, title, screen and whether it is minimized. With installed, also list apps from the Start menu that can be launched.",
            true, "{\"type\":\"object\",\"properties\":{\"installed\":{\"type\":\"boolean\",\"default\":false,\"description\":\"Also list installed apps.\"}},\"additionalProperties\":false}"),
        new(LaunchApp, "Launch app",
            "Start an app by its app_id from pc_list_apps (installed), or by the full path of an .exe, without taking the user's focus. Returns its new windows. Never runs a shell or a command line. place picks where it opens: desk (the user's screen), workspace (Tactus's separate workspace, when the user turned it on) or auto.",
            false, "{\"type\":\"object\",\"properties\":{\"app_id\":{\"type\":\"string\"},\"path\":{\"type\":\"string\"},\"place\":{\"type\":\"string\",\"enum\":[\"auto\",\"desk\",\"workspace\"],\"default\":\"auto\"}},\"additionalProperties\":false}"),
        new(Observe, "Look at window",
            "Look at one window, in front, behind others or minimized. Returns a new obs id and the window's accessibility tree as numbered lines (a diff against your last look unless full is true), the focused element and the selection. Adds a screenshot when screenshot is yes, or in auto when the tree has little to act on; marks draws each element's number on it, and region crops it at up to full resolution.",
            true, "{\"type\":\"object\",\"properties\":{" + WindowProp + ",\"tree\":{\"type\":\"boolean\",\"default\":true},\"screenshot\":{\"type\":\"string\",\"enum\":[\"auto\",\"yes\",\"no\"],\"default\":\"auto\"},\"marks\":{\"type\":\"boolean\",\"default\":true,\"description\":\"Draw element numbers on the screenshot.\"},\"region\":{\"type\":\"array\",\"items\":{\"type\":\"number\"},\"minItems\":4,\"maxItems\":4,\"description\":\"[x, y, width, height] of the previous screenshot to show at up to full resolution.\"},\"query\":{\"type\":\"string\",\"description\":\"Keep only lines containing this text, with their parents.\"},\"full\":{\"type\":\"boolean\",\"default\":false,\"description\":\"The whole tree, not a diff.\"},\"max_lines\":{\"type\":\"integer\",\"minimum\":20,\"maximum\":2000,\"default\":300}},\"required\":[\"window\"],\"additionalProperties\":false}"),
        new(Click, "Click",
            "Click an element by its number or target (preferred), or a point from the latest screenshot where the tree has nothing. Tactus runs the element's own action (press, toggle, select, expand) in the background, without the user's pointer, and checks that it took effect.",
            false, "{\"type\":\"object\",\"properties\":{" + WindowProp + "," + ElementProp + "," + TargetProp + "," + PointProp + "," + ObsProp + ",\"button\":{\"type\":\"string\",\"enum\":[\"left\",\"right\",\"middle\"],\"default\":\"left\"},\"count\":{\"type\":\"integer\",\"minimum\":1,\"maximum\":3,\"default\":1}," + ModifiersProp + "," + ExpectProp + "},\"required\":[\"window\"],\"additionalProperties\":false}"),
        new(Type, "Type",
            "Type text into an element, or into the window's focused field, without the user's keyboard: the text goes straight into the control. Mode insert adds it at the caret (at the end when the caret is unknown); replace replaces the whole text. Tactus reads the text back to check it. Never for passwords, codes or card numbers. submit presses the field's default button or Enter after.",
            false, "{\"type\":\"object\",\"properties\":{" + WindowProp + ",\"text\":{\"type\":\"string\",\"maxLength\":20000}," + ElementProp + "," + TargetProp + ",\"mode\":{\"type\":\"string\",\"enum\":[\"insert\",\"replace\"],\"default\":\"insert\"},\"submit\":{\"type\":\"boolean\",\"default\":false}," + ExpectProp + "},\"required\":[\"window\",\"text\"],\"additionalProperties\":false}"),
        new(SetValue, "Set value",
            "Replace an element's whole value (a text field, a slider or a spin box) in one step, then read it back. Refused for password and read-only fields.",
            false, "{\"type\":\"object\",\"properties\":{" + WindowProp + "," + ElementProp + "," + TargetProp + ",\"value\":{\"type\":\"string\"}," + ExpectProp + "},\"required\":[\"window\",\"value\"],\"additionalProperties\":false}"),
        new(Key, "Press keys",
            "Run a key or a shortcut in the window, such as enter, escape, tab, f5, ctrl+s or alt+f4, without the user's keyboard: a shortcut runs the command that shows it (keys= in the tree), alt+f4 closes the window, and a plain key goes to the window's focused control or to the element given. The Windows key is never pressed; copy and paste are refused (use pc_read and pc_type).",
            false, "{\"type\":\"object\",\"properties\":{" + WindowProp + ",\"keys\":{\"type\":\"string\"}," + ElementProp + "," + TargetProp + ",\"repeat\":{\"type\":\"integer\",\"minimum\":1,\"maximum\":20,\"default\":1}," + ExpectProp + "},\"required\":[\"window\",\"keys\"],\"additionalProperties\":false}"),
        new(Scroll, "Scroll",
            "Scroll an element by its number or target, or at a point from the latest screenshot, by pages or lines.",
            false, "{\"type\":\"object\",\"properties\":{" + WindowProp + "," + ElementProp + "," + TargetProp + "," + PointProp + "," + ObsProp + ",\"direction\":{\"type\":\"string\",\"enum\":[\"up\",\"down\",\"left\",\"right\"]},\"amount\":{\"type\":\"number\",\"minimum\":0.1,\"maximum\":50,\"default\":1},\"unit\":{\"type\":\"string\",\"enum\":[\"page\",\"line\"],\"default\":\"page\"}," + ExpectProp + "},\"required\":[\"window\",\"direction\"],\"additionalProperties\":false}"),
        new(Drag, "Drag",
            "Press at the first point, move through the others and release at the last, for canvases, sliders and drawing. Points come from the latest screenshot.",
            false, "{\"type\":\"object\",\"properties\":{" + WindowProp + "," + ObsProp + ",\"path\":{\"type\":\"array\",\"minItems\":2,\"maxItems\":50,\"items\":{\"type\":\"array\",\"items\":{\"type\":\"number\"},\"minItems\":2,\"maxItems\":2}}," + ModifiersProp + ",\"hold_ms\":{\"type\":\"integer\",\"minimum\":0,\"maximum\":2000}," + ExpectProp + "},\"required\":[\"window\",\"obs\",\"path\"],\"additionalProperties\":false}"),
        new(Action, "Element action",
            "Run one of the actions an element lists after actions: (expand, collapse, toggle, select), or focus (inside the app only), scroll_into_view, and the window actions minimize, maximize, restore and close. Restore never brings the window to the front.",
            false, "{\"type\":\"object\",\"properties\":{" + WindowProp + "," + ElementProp + "," + TargetProp + ",\"action\":{\"type\":\"string\",\"enum\":[\"expand\",\"collapse\",\"toggle\",\"select\",\"focus\",\"scroll_into_view\",\"minimize\",\"maximize\",\"restore\",\"close\"]}," + ExpectProp + "},\"required\":[\"window\",\"action\"],\"additionalProperties\":false}"),
        new(FocusWindow, "Show window",
            "Show the window to the user (restored and raised, without taking their keyboard), only when the user asked to see it. The user is asked first on the pill. Tactus never needs this to act: it works in background and minimized windows.",
            false, "{\"type\":\"object\",\"properties\":{" + WindowProp + "},\"required\":[\"window\"],\"additionalProperties\":false}"),
        new(Batch, "Run steps",
            "Run 2 to 50 steps in one call (pc_click, pc_type, pc_set_value, pc_key, pc_scroll, pc_drag, pc_action, pc_read, pc_wait). Each step is an object with tool and that tool's arguments; its window comes from the batch unless the step names one (a w:... id, or a name bound by an earlier step's expect). Every step is checked; the batch stops at the first error, mismatch or unverified step and says what it saw. A step a later step depends on needs an expect: every pc_click, pc_key, pc_action other than focus and scroll_into_view, and pc_type with submit that has steps after it in its window. With parallel, the steps of different windows run at the same time and a failure stops only its own window's steps. A pc_read step can bind its text (bind: $total) and a later step can use it as its text or value.",
            false, "{\"type\":\"object\",\"properties\":{" + WindowProp + ",\"actions\":{\"type\":\"array\",\"minItems\":2,\"maxItems\":50,\"items\":{\"type\":\"object\",\"properties\":{\"tool\":{\"type\":\"string\",\"enum\":[\"pc_click\",\"pc_type\",\"pc_set_value\",\"pc_key\",\"pc_scroll\",\"pc_drag\",\"pc_action\",\"pc_read\",\"pc_wait\"]},\"window\":{\"type\":\"string\"},\"bind\":{\"type\":\"string\"}},\"required\":[\"tool\"]}},\"parallel\":{\"type\":\"boolean\",\"default\":false},\"observe\":{\"type\":\"string\",\"enum\":[\"diff\",\"full\",\"screenshot\",\"none\"],\"default\":\"diff\"}},\"required\":[\"window\",\"actions\"],\"additionalProperties\":false}"),
        new(Read, "Read text",
            "Read the full text of a document, text field or list, page by page, so pc_observe stays small. Without element or target, reads the focused element. Use this instead of copying.",
            true, "{\"type\":\"object\",\"properties\":{" + WindowProp + "," + ElementProp + "," + TargetProp + ",\"offset\":{\"type\":\"integer\",\"minimum\":0,\"default\":0},\"length\":{\"type\":\"integer\",\"minimum\":1,\"maximum\":20000,\"default\":8000}},\"required\":[\"window\"],\"additionalProperties\":false}"),
        new(Zoom, "Zoom",
            "A sharper crop of a region of a screenshot, at up to the screen's full resolution, for small text and small controls.",
            true, "{\"type\":\"object\",\"properties\":{" + ObsProp + ",\"region\":{\"type\":\"array\",\"items\":{\"type\":\"number\"},\"minItems\":4,\"maxItems\":4,\"description\":\"[x, y, width, height] in the screenshot's pixels.\"}},\"required\":[\"obs\",\"region\"],\"additionalProperties\":false}"),
        new(Wait, "Wait",
            "Wait until the window is idle, until some text appears, until some text is gone, or until an expect holds, at most 15 seconds. It wakes on the window's own change events. Do not sleep any other way. until step, with the token of a waiting_for_user result, waits up to 25 seconds more for that same step and returns its result.",
            true, "{\"type\":\"object\",\"properties\":{" + WindowProp + ",\"until\":{\"type\":\"string\",\"enum\":[\"idle\",\"text\",\"gone\",\"expect\",\"step\"]},\"text\":{\"type\":\"string\"},\"token\":{\"type\":\"string\",\"description\":\"The token of a waiting_for_user result (until step).\"}," + ElementProp + "," + TargetProp + "," + ExpectProp + ",\"timeout_ms\":{\"type\":\"integer\",\"minimum\":100,\"maximum\":15000,\"default\":5000}},\"required\":[\"window\",\"until\"],\"additionalProperties\":false}"),
    ];

    /// <summary>
    /// pc_delegate (DESIGN.md 2.2, 2.12, critique C30): a plan of steps written by the main model, each with a target and an
    /// expect, that the user's fast helper model runs. Listed only when the user turned the fast helper on.
    /// </summary>
    public static PcToolSpec DelegateSpec { get; } = new(Delegate, "Fast helper",
        "Hand a plan of simple steps to the user's fast helper model. goal is the plan: each step has tool (pc_observe, pc_read, pc_click, pc_type, pc_set_value, pc_action with select, pc_scroll or pc_wait), window, a target and an expect; the helper only fills in details and never adds steps. Any mismatch, ambiguity or guarded action returns to you at once with a report.",
        false, "{\"type\":\"object\",\"properties\":{\"goal\":{\"type\":\"array\",\"minItems\":1,\"maxItems\":40,\"items\":{\"type\":\"object\",\"properties\":{\"tool\":{\"type\":\"string\",\"enum\":[\"pc_observe\",\"pc_read\",\"pc_click\",\"pc_type\",\"pc_set_value\",\"pc_action\",\"pc_scroll\",\"pc_wait\"]},\"window\":{\"type\":\"string\"}," + TargetProp + "," + ExpectProp + "},\"required\":[\"tool\",\"window\",\"target\",\"expect\"]}},\"windows\":{\"type\":\"array\",\"items\":{\"type\":\"string\"}},\"done_when\":{\"type\":\"string\"},\"max_steps\":{\"type\":\"integer\",\"minimum\":1,\"maximum\":40}},\"required\":[\"goal\",\"windows\",\"done_when\"],\"additionalProperties\":false}");

    /// <summary>
    /// pc_ask (DESIGN.md 10.4, 10.9): the model's short question, answered from the pill or the chat. Plain text; a question
    /// that asks for a secret is shown only in the chat.
    /// </summary>
    public static PcToolSpec AskSpec { get; } = new(Ask, "Ask the user",
        "Ask the user a short question while you work in their apps; they answer from Tactus's pill or from the chat. Give up to four short choices, and allow_text for a typed answer (on unless you turn it off). Plain text only. Never ask for passwords, codes, or card or bank numbers: such a question goes only to the chat. Returns the answer, or waiting_for_user with reason question and a token for pc_wait until \"step\".",
        true, "{\"type\":\"object\",\"properties\":{\"question\":{\"type\":\"string\",\"minLength\":1,\"maxLength\":600},\"choices\":{\"type\":\"array\",\"maxItems\":4,\"items\":{\"type\":\"string\",\"minLength\":1,\"maxLength\":40}},\"allow_text\":{\"type\":\"boolean\",\"default\":true},\"window\":{\"type\":\"string\",\"description\":\"The window the question is about (w:...), for the pill's line.\"}},\"required\":[\"question\"],\"additionalProperties\":false}");

    /// <summary>The tools a model is offered: the fifteen, plus pc_delegate when the fast helper is on.</summary>
    public static IReadOnlyList<PcToolSpec> Listed(PcSettings settings) => settings.FastHelper ? [.. All, DelegateSpec] : All;

    public static PcToolSpec? Find(string? name) => name == Delegate ? DelegateSpec : All.FirstOrDefault(t => t.Name == name);
}
