// PORT-ONLY: web surfaces W24, live-answer tools (DESIGN.md 7.3: ui_show, ui_update and html_widget; ED-NEW-web-w24-1, -4). The three
// tools every model gets to answer with a live view, in one place for the nth-ui MCP server (CLI providers) and the native tool
// loop (API providers): their names, descriptions and input schemas, how their arguments are read (leniently: a patch sent as
// a JSON string, a view sent as "view"), what they check, and what the model reads back. A view that nth could not read at all
// is a failure the model can fix; a view with a few unreadable lines is shown, and the lines are listed so the model can send
// it again. Our own words; nothing here is copied from another product. No Swift counterpart.
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Marrow.Core.Models;
using Marrow.Core.NthUi;
using Marrow.Core.Support;

namespace Marrow.Services.Web;

public static class LiveAnswerTools
{
    public const string ShowName = NthUiTeaching.ShowTool;
    public const string UpdateName = NthUiTeaching.UpdateTool;
    public const string HtmlName = NthUiTeaching.HtmlTool;

    /// <summary>The row's name in the chat (the feature's plain name, DESIGN.md 15).</summary>
    public const string Title = "Live answers";

    /// <summary>The longest HTML page an inline widget takes, in characters.</summary>
    public const int MaxHtmlLength = 524_288;

    public const int DefaultHeight = 320;
    public const int MinHeight = 120;
    public const int MaxHeight = 900;

    public static IReadOnlyList<string> Names { get; } = [ShowName, UpdateName, HtmlName];

    public static bool IsName(string? name) => name is ShowName or UpdateName or HtmlName;

    /// <summary>The tools a session is offered: all three, but a small local model (lite) has no HTML widget.</summary>
    public static IReadOnlyList<string> Offered(bool lite) => lite ? [ShowName, UpdateName] : Names;

    /// <summary>The tools a session is offered with Settings › Tabula › "Inline HTML widgets" (W10): off leaves out html_widget for everyone.</summary>
    public static IReadOnlyList<string> Offered(bool lite, bool htmlWidgets) => htmlWidgets ? Offered(lite) : [ShowName, UpdateName];

    public static string Description(string name) => name switch
    {
        ShowName => NthUiTeaching.ShowToolDescription,
        UpdateName =>
            "Change a live view you already showed, without writing it again. Give its id and a patch: an object with any of "
            + "state (a state name mapped to its new value; the reader's control jumps to it), "
            + "nodes (a node id mapped to new lines of the view that replace that node and what it holds; an empty text removes it) and "
            + "append (a node id mapped to lines added at the end of that node). A node has an id when its line says id=name. "
            + "To rewrite a whole view, call ui_show again with the same id.",
        HtmlName =>
            "Show one small interactive HTML page inline in the chat, only when no live-view component fits (a game, a simulation, a custom toy). "
            + "html is one complete, self-contained document: its CSS and JavaScript inline, no network, no outside scripts or fonts. "
            + "It runs in a sandbox with no access to the chat, the files or the internet. height is in pixels (120 to 900, default 320). "
            + "Anything the reader will keep and change belongs in an artifact instead.",
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };

    /// <summary>The tool's input schema (a fresh node each call).</summary>
    public static JsonObject InputSchema(string name) => (JsonObject)JsonNode.Parse(SchemaText(name))!;

    /// <summary>The OpenAI-style function definition the API providers' tool loop sends.</summary>
    public static JSONValue FunctionDefinition(string name) => JSONValue.FromDictionaryLiteral(
        ("type", "function"),
        ("function", JSONValue.FromDictionaryLiteral(("name", name), ("description", Description(name)), ("parameters", JSONValue.Parse(SchemaText(name))!))));

    private static string SchemaText(string name) => name switch
    {
        ShowName => """
            {"type":"object","properties":{
            "ui":{"type":"string","description":"The view: one component per line, children indented two spaces. No JSON, HTML or fence lines."},
            "id":{"type":"string","description":"Optional short name (letters, digits, hyphens). Showing the same id again replaces that view; ui_update finds it by this id."}},
            "required":["ui"]}
            """,
        UpdateName => """
            {"type":"object","properties":{
            "id":{"type":"string","description":"The id of the view to change."},
            "patch":{"type":"object","description":"What to change.","properties":{
            "state":{"type":"object","description":"State names mapped to their new values."},
            "nodes":{"type":"object","additionalProperties":{"type":"string"},"description":"Node ids mapped to the lines that replace the node (empty to remove it)."},
            "append":{"type":"object","additionalProperties":{"type":"string"},"description":"Node ids mapped to lines added at the end of the node."}}}},
            "required":["id","patch"]}
            """,
        HtmlName => """
            {"type":"object","properties":{
            "html":{"type":"string","description":"One complete HTML document with its CSS and JavaScript inline."},
            "height":{"type":"integer","description":"Height in pixels, 120 to 900 (default 320)."},
            "title":{"type":"string","description":"A short name for the widget."},
            "id":{"type":"string","description":"Optional short name; showing the same id again replaces the widget."}},
            "required":["html"]}
            """,
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };

    /// <summary>The timeline row's title for a call: always the feature's name (the view itself is what the reader sees).</summary>
    public static string RowTitle(string name, JsonNode? arguments) =>
        name == HtmlName && Text(arguments, "title") is { } title && title.Trim().Length > 0 ? Title + " · " + Short(title) : Title;

    /// <summary>Runs one tool for a chat. A refusal is a failed result the model can act on, never a throw.</summary>
    public static async Task<ArtifactToolResult> Run(LiveAnswerHost host, Guid thread, bool lite, string name, JsonNode? arguments)
    {
        try
        {
            if (!host.Enabled) return Failure("Live answers are turned off in Settings.");
            return name switch
            {
                ShowName => await Show(host, thread, lite, arguments).ConfigureAwait(false),
                UpdateName => await Update(host, thread, lite, arguments).ConfigureAwait(false),
                HtmlName => await Html(host, thread, arguments).ConfigureAwait(false),
                _ => Failure($"{name} is not a live-answer tool."),
            };
        }
        catch (InvalidOperationException error) { return Failure(error.Message); }
    }

    // ---- ui_show ----

    private static async Task<ArtifactToolResult> Show(LiveAnswerHost host, Guid thread, bool lite, JsonNode? arguments)
    {
        var text = NthUiPatch.Clean(Text(arguments, "ui") ?? Text(arguments, "view") ?? Text(arguments, "content") ?? "").TrimStart('\n', '\r');
        if (text.Trim().Length == 0) return Failure("ui is required: the view's lines, one component per line. Call ui_reference for the language.");
        if (text.Length > ToolLiveView.MaxTextLength) return Failure($"The view is longer than {ToolLiveView.MaxTextLength.ToString("N0", CultureInfo.InvariantCulture)} characters; show less of it.");
        var profile = lite ? NthUiProfile.Lite : NthUiProfile.Full;
        var document = NthUiParser.Parse(text, profile);
        if (document.Nodes.Count == 0 || document.Nodes.All(node => node.Degraded))
        {
            return Failure("No line of the view could be read as a component, so nothing was shown. " + Problems(document)
                + " Write one component per line (card, text, stat, table, chart, stepper, slider, checklist ...); it is not JSON, HTML or Markdown. Call ui_reference for the language.");
        }
        var id = LiveViewIds.Normalize(Text(arguments, "id")) ?? host.NewId(thread);
        _ = await host.Find(thread, id).ConfigureAwait(false); // a view of this id from an earlier session is replaced at its next version
        var change = host.Show(thread, new LiveViewRecord(id, LiveViewKind.Ui, text, NthUiPatch.TitleOf(document), 1, lite, null));
        var view = change.View;
        var summary = new StringBuilder();
        summary.Append(change.Replaced ? $"Replaced the live view \"{id}\" (now version {view.Version})" : $"Showed the live view \"{id}\"")
            .Append(" in the chat: ").Append(Plural(document.Nodes.Count(node => !node.Degraded), "component")).Append('.');
        summary.Append(" To change it later, call ").Append(UpdateName).Append(" with id \"").Append(id).Append("\"; to rewrite it, call ").Append(ShowName).Append(" again with the same id.");
        var problems = Problems(document);
        if (problems.Length > 0)
            summary.Append("\nSome lines could not be read and are shown as plain text: ").Append(problems).Append(" Send the view again with those lines fixed.");
        return new ArtifactToolResult(summary.ToString(), false, Details(view, document));
    }

    // ---- ui_update ----

    private static async Task<ArtifactToolResult> Update(LiveAnswerHost host, Guid thread, bool lite, JsonNode? arguments)
    {
        var id = LiveViewIds.Normalize(Text(arguments, "id"));
        if (id is null) return Failure("id is required: the id of the view to change.");
        var view = await host.Find(thread, id).ConfigureAwait(false);
        if (view is null)
        {
            var known = host.Ids(thread);
            return Failure($"There is no live view \"{id}\" in this chat" + (known.Count == 0 ? "; none has been shown." : $"; its views are {string.Join(", ", known)}.") + " Show it first with ui_show.");
        }
        if (view.Kind == LiveViewKind.Html) return Failure($"\"{id}\" is an HTML widget, which cannot be patched; call html_widget again with the same id to replace it.");
        var patch = NthUiPatch.Read(arguments is JsonObject map && map.TryGetPropertyValue("patch", out var given) ? given : null);
        if (patch is null) return Failure("patch is required: an object with state, nodes or append (it may be sent as JSON text).");
        var profile = lite ? NthUiProfile.Lite : NthUiProfile.Full;
        var before = NthUiParser.Parse(view.Text, profile);
        var result = NthUiPatch.Apply(view.Text, patch, profile);
        if (result.Failed) return Failure(result.Error!);
        var document = NthUiParser.Parse(result.Text!, profile);
        var change = host.Update(thread, id, result.Text!, NthUiPatch.TitleOf(document), result.StateSets);
        var text = new StringBuilder($"Updated the live view \"{id}\" (now version {change.View.Version}): {string.Join("; ", result.Done)}.");
        var introduced = Problems(document, skip: before);
        if (introduced.Length > 0) text.Append("\nSome lines could not be read and are shown as plain text: ").Append(introduced).Append(" Fix them with another ui_update.");
        return new ArtifactToolResult(text.ToString(), false, Details(change.View, document));
    }

    // ---- html_widget ----

    private static async Task<ArtifactToolResult> Html(LiveAnswerHost host, Guid thread, JsonNode? arguments)
    {
        if (!host.HtmlEnabled) return Failure("Inline HTML widgets are turned off in Settings. Answer in text, a live view or an artifact instead.");
        var html = Text(arguments, "html") ?? Text(arguments, "content") ?? "";
        if (html.Trim().Length == 0) return Failure("html is required: one complete HTML page.");
        if (!html.Contains('<', StringComparison.Ordinal)) return Failure("html must be an HTML page (it has no tags).");
        if (html.Length > MaxHtmlLength) return Failure($"The page is longer than {MaxHtmlLength.ToString("N0", CultureInfo.InvariantCulture)} characters; make it smaller.");
        var height = Math.Clamp(Integer(arguments, "height") ?? DefaultHeight, MinHeight, MaxHeight);
        var title = Text(arguments, "title") is { } named && named.Trim().Length > 0 ? Short(named) : HtmlTitle(html);
        var id = LiveViewIds.Normalize(Text(arguments, "id")) ?? LiveViewIds.Normalize(title) ?? host.NewId(thread);
        _ = await host.Find(thread, id).ConfigureAwait(false);
        var change = host.Show(thread, new LiveViewRecord(id, LiveViewKind.Html, html, title, 1, false, height));
        var text = new StringBuilder(change.Replaced ? $"Replaced the HTML widget \"{id}\" (now version {change.View.Version})" : $"Showed the HTML widget \"{id}\"");
        text.Append(" in the chat, ").Append(height.ToString(CultureInfo.InvariantCulture)).Append(" pixels high.");
        if (ReachesOut(html)) text.Append("\nIt names an outside address. The widget has no network and loads nothing from outside, so keep everything inline.");
        return new ArtifactToolResult(text.ToString(), false, Details(change.View, null));
    }

    // ---- helpers ----

    private static ArtifactToolResult Failure(string message) => new("Error: " + message, true, null);

    private static JsonObject Details(LiveViewRecord view, NthUiDocument? document)
    {
        var details = new JsonObject
        {
            ["id"] = view.Id,
            ["kind"] = view.Kind == LiveViewKind.Html ? "html" : "ui",
            ["version"] = view.Version,
        };
        if (view.Title is { } title) details["title"] = title;
        if (document is not null)
        {
            details["components"] = document.Nodes.Count(node => !node.Degraded);
            var unread = new JsonArray();
            foreach (var note in document.Diagnostics.Where(note => note.Severity == NthUiSeverity.Error).Take(10)) unread.Add(note.ToString());
            if (unread.Count > 0) details["unread"] = unread;
        }
        return details;
    }

    /// <summary>The lines that did not read, as one sentence ("line 4: gauge is not a component; shown as text. line 9: ..."), at most five; those already in <paramref name="skip"/> are left out.</summary>
    private static string Problems(NthUiDocument document, NthUiDocument? skip = null)
    {
        var already = skip?.Diagnostics.Where(note => note.Severity == NthUiSeverity.Error).Select(note => note.Message).ToHashSet(StringComparer.Ordinal) ?? [];
        var notes = document.Diagnostics.Where(note => note.Severity == NthUiSeverity.Error && !already.Contains(note.Message)).ToList();
        if (notes.Count == 0) return "";
        var shown = string.Join(" ", notes.Take(5).Select(note => note.ToString().TrimEnd('.') + "."));
        return notes.Count > 5 ? shown + $" And {notes.Count - 5} more." : shown;
    }

    private static string Plural(int count, string noun) => count.ToString(CultureInfo.InvariantCulture) + " " + noun + (count == 1 ? "" : "s");

    private static string Short(string text)
    {
        var one = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return one.Length <= 60 ? one : one[..59].TrimEnd() + "…";
    }

    private static string? HtmlTitle(string html)
    {
        var open = html.IndexOf("<title", StringComparison.OrdinalIgnoreCase);
        if (open < 0) return null;
        var start = html.IndexOf('>', open);
        var end = start < 0 ? -1 : html.IndexOf("</title", start, StringComparison.OrdinalIgnoreCase);
        return start < 0 || end < 0 || end - start <= 1 ? null : Short(System.Net.WebUtility.HtmlDecode(html[(start + 1)..end]));
    }

    private static bool ReachesOut(string html) =>
        html.Contains("src=\"http", StringComparison.OrdinalIgnoreCase) || html.Contains("src='http", StringComparison.OrdinalIgnoreCase)
        || html.Contains("href=\"http", StringComparison.OrdinalIgnoreCase) || html.Contains("@import", StringComparison.OrdinalIgnoreCase)
        || html.Contains("fetch(", StringComparison.Ordinal) || html.Contains("XMLHttpRequest", StringComparison.Ordinal);

    private static string? Text(JsonNode? arguments, string key) => arguments is JsonObject map && map.TryGetPropertyValue(key, out var value) ? Scalar(value) : null;

    private static string? Scalar(JsonNode? value) => value is JsonValue scalar
        ? scalar.GetValueKind() switch
        {
            JsonValueKind.String => scalar.GetValue<string>(),
            JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => scalar.ToJsonString(),
            _ => null,
        }
        : null;

    private static int? Integer(JsonNode? arguments, string key) =>
        Text(arguments, key) is { } text && int.TryParse(text.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var number) ? number : null;
}
