// PORT-ONLY: web surfaces W11, the page as the model reads it (E:/Droppy/work/design/web-surfaces/DESIGN.md 5.10 "web_read {tab,
// mode: ax|text|html, ref?, max_tokens?}: compact accessibility tree with element refs (e12)", "web_find {tab, query}: finds
// elements by text or role, returns refs"; ED-NEW-web-w11-1). No Swift counterpart. From the DevTools Protocol's full
// accessibility tree (Accessibility.getFullAXTree) to compact lines: one line per meaningful node with its ref, role, name and
// states, nodes with no role of their own lifted into their parent, text runs as quoted lines, links with their addresses. A ref
// stays the same for the same DOM node for as long as the tab lives (WebRefs), so a ref from an earlier read still means that
// element. Values of password fields never appear.
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Marrow.Core.Web;

/// <summary>One element a ref names: its DOM node (backendDOMNodeId) and, inside an iframe, the frame's CDP session.</summary>
public readonly record struct WebNodeKey(long BackendId, string? Session);

/// <summary>A tab's refs: the same DOM node keeps the same "e12" for as long as the tab lives.</summary>
public sealed class WebRefs
{
    private readonly Dictionary<WebNodeKey, int> numbers = [];
    private readonly Dictionary<int, WebNodeKey> keys = [];

    /// <summary>The ref of a node, made on first sight.</summary>
    public string Ref(WebNodeKey key)
    {
        if (!numbers.TryGetValue(key, out var number))
        {
            number = numbers.Count + 1;
            numbers[key] = number;
            keys[number] = key;
        }
        return "e" + number.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>The node a ref names, or null for a ref this tab never gave.</summary>
    public WebNodeKey? Resolve(string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference)) return null;
        var text = reference.Trim();
        if (text.StartsWith('e') || text.StartsWith('E')) text = text[1..];
        return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var number) && keys.TryGetValue(number, out var key) ? key : null;
    }

    public int Count => numbers.Count;
}

/// <summary>One accessibility node, as read from CDP.</summary>
public sealed record WebAxNode(string NodeId, string Role, string Name, string? Value, long? BackendId, IReadOnlyList<string> ChildIds, bool Ignored)
{
    public IReadOnlyDictionary<string, string> Properties { get; init; } = new Dictionary<string, string>();
}

/// <summary>A read tree: its lines, the addresses its links point to, and how many lines were left out.</summary>
public sealed record WebTreeText(string Text, IReadOnlyList<string> Links, int Omitted, int Lines);

public static class WebAgentTree
{
    /// <summary>Roles that only group: with no name of their own their children are lifted into the parent.</summary>
    private static readonly HashSet<string> transparent = new(StringComparer.OrdinalIgnoreCase)
    {
        "none", "generic", "presentation", "GenericContainer", "group", "Section", "div", "LayoutTable", "LayoutTableRow", "LayoutTableCell",
        "RootWebArea", "WebArea", "Iframe", "IframePresentational", "ScrollArea", "Pane", "LabelText", "Canvas", "Unknown",
    };

    /// <summary>Roles that carry only text.</summary>
    private static readonly HashSet<string> text = new(StringComparer.OrdinalIgnoreCase) { "StaticText", "InlineTextBox", "text" };

    private static readonly HashSet<string> skipped = new(StringComparer.OrdinalIgnoreCase) { "LineBreak", "InlineTextBox", "ListMarker" };

    private static readonly string[] states = ["focused", "disabled", "required", "readonly", "expanded", "selected", "checked", "pressed", "invalid", "modal"];

    /// <summary>Parses the result of <c>Accessibility.getFullAXTree</c> (or getPartialAXTree): its <c>nodes</c> array.</summary>
    public static IReadOnlyList<WebAxNode> Parse(JsonElement result)
    {
        var nodes = new List<WebAxNode>();
        if (result.ValueKind != JsonValueKind.Object || !result.TryGetProperty("nodes", out var array) || array.ValueKind != JsonValueKind.Array) return nodes;
        foreach (var node in array.EnumerateArray())
        {
            var id = Str(node, "nodeId") ?? "";
            var ignored = node.TryGetProperty("ignored", out var ig) && ig.ValueKind == JsonValueKind.True;
            var role = Inner(node, "role") ?? "";
            var name = Inner(node, "name") ?? "";
            var value = Inner(node, "value");
            long? backend = node.TryGetProperty("backendDOMNodeId", out var b) && b.TryGetInt64(out var n) ? n : null;
            var children = new List<string>();
            if (node.TryGetProperty("childIds", out var kids) && kids.ValueKind == JsonValueKind.Array)
                foreach (var kid in kids.EnumerateArray())
                    if (kid.ValueKind == JsonValueKind.String) children.Add(kid.GetString()!);
            var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (node.TryGetProperty("properties", out var props) && props.ValueKind == JsonValueKind.Array)
            {
                foreach (var prop in props.EnumerateArray())
                {
                    if (Str(prop, "name") is not { } key || !prop.TryGetProperty("value", out var holder)) continue;
                    if (holder.TryGetProperty("value", out var v))
                    {
                        properties[key] = v.ValueKind switch
                        {
                            JsonValueKind.String => v.GetString() ?? "",
                            JsonValueKind.True => "true",
                            JsonValueKind.False => "false",
                            JsonValueKind.Number => v.GetRawText(),
                            _ => v.GetRawText(),
                        };
                    }
                }
            }
            nodes.Add(new WebAxNode(id, role, name, value, backend, children, ignored) { Properties = properties });
        }
        return nodes;
    }

    /// <summary>
    /// The compact lines of a tree. <paramref name="root"/> limits it to one DOM node's subtree; <paramref name="secrets"/> are DOM
    /// nodes whose values are never shown (password fields); <paramref name="maxChars"/> caps the text, the rest counted.
    /// </summary>
    public static WebTreeText Format(IReadOnlyList<WebAxNode> nodes, WebRefs refs, string? session = null, long? root = null,
        IReadOnlySet<long>? secrets = null, int maxChars = 16_000, string? pageUrl = null)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(refs);
        var byId = new Dictionary<string, WebAxNode>(StringComparer.Ordinal);
        foreach (var node in nodes) byId.TryAdd(node.NodeId, node);
        var childOf = new HashSet<string>(nodes.SelectMany(n => n.ChildIds), StringComparer.Ordinal);
        IEnumerable<WebAxNode> starts = root is { } r
            ? nodes.Where(n => n.BackendId == r).Take(1)
            : nodes.Where(n => !childOf.Contains(n.NodeId)).Take(1);
        var lines = new List<string>();
        var links = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var start in starts) Walk(start, 0, null);
        var output = new StringBuilder();
        var used = 0;
        var omitted = 0;
        foreach (var line in lines)
        {
            if (used + line.Length + 1 > maxChars)
            {
                omitted++;
                continue;
            }
            output.Append(line).Append('\n');
            used += line.Length + 1;
        }
        if (omitted > 0)
            output.Append("… ").Append(omitted.ToString(CultureInfo.InvariantCulture)).Append(" more lines left out (use web_find, or web_read with a ref)\n");
        return new WebTreeText(output.ToString().TrimEnd('\n'), links, omitted, lines.Count);

        void Walk(WebAxNode node, int depth, string? parentName)
        {
            if (!seen.Add(node.NodeId)) return;
            var role = node.Role;
            var name = Clean(node.Name);
            var kept = !node.Ignored && !skipped.Contains(role) && !(transparent.Contains(role) && name.Length == 0);
            if (!node.Ignored && text.Contains(role))
            {
                // A text run that only repeats its parent's name adds nothing.
                if (name.Length > 0 && !(parentName is { Length: > 0 } && parentName.Contains(name, StringComparison.Ordinal)))
                    lines.Add(Indent(depth) + Quote(name));
                return;
            }
            var childDepth = depth;
            var childParent = parentName;
            if (kept)
            {
                var line = new StringBuilder(Indent(depth));
                if (node.BackendId is { } backend) line.Append(refs.Ref(new WebNodeKey(backend, session))).Append(' ');
                line.Append(RoleWord(role));
                if (name.Length > 0) line.Append(' ').Append(Quote(name));
                foreach (var state in states)
                {
                    if (!node.Properties.TryGetValue(state, out var value)) continue;
                    if (value == "true") line.Append(' ').Append(state);
                    else if (value == "mixed") line.Append(' ').Append(state).Append("=mixed");
                    else if (state is "checked" or "pressed" or "expanded" && value == "false") line.Append(" not-").Append(state);
                }
                if (node.Properties.TryGetValue("level", out var level)) line.Append(" level=").Append(level);
                if (node.Value is { Length: > 0 } shown && !(node.BackendId is { } id && secrets?.Contains(id) == true) && !IsMasked(shown) && shown != node.Name)
                    line.Append(" value=").Append(Quote(Clean(shown)));
                if (node.Properties.TryGetValue("url", out var url) && url.Length > 0)
                {
                    links.Add(url);
                    line.Append(" -> ").Append(Short(url, pageUrl));
                }
                lines.Add(line.ToString());
                childDepth = depth + 1;
                childParent = name;
            }
            foreach (var child in node.ChildIds)
                if (byId.TryGetValue(child, out var next)) Walk(next, childDepth, childParent);
        }
    }

    /// <summary>
    /// DESIGN 5.10 web_find: the nodes whose name, value or role contains <paramref name="query"/> (any case), as lines with refs,
    /// at most <paramref name="limit"/>.
    /// </summary>
    public static IReadOnlyList<string> Find(IReadOnlyList<WebAxNode> nodes, WebRefs refs, string query, string? session = null, IReadOnlySet<long>? secrets = null, int limit = 30)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        var q = query.Trim();
        var found = new List<string>();
        if (q.Length == 0) return found;
        foreach (var node in nodes)
        {
            if (node.Ignored || node.BackendId is not { } backend || skipped.Contains(node.Role) || text.Contains(node.Role)) continue;
            var name = Clean(node.Name);
            var value = node.Value is { } v && secrets?.Contains(backend) != true && !IsMasked(v) ? Clean(v) : "";
            var roleMatch = RoleWord(node.Role).Equals(q, StringComparison.OrdinalIgnoreCase);
            if (!roleMatch && !name.Contains(q, StringComparison.OrdinalIgnoreCase) && !value.Contains(q, StringComparison.OrdinalIgnoreCase)) continue;
            if (transparent.Contains(node.Role) && name.Length == 0) continue;
            var line = refs.Ref(new WebNodeKey(backend, session)) + " " + RoleWord(node.Role) + (name.Length > 0 ? " " + Quote(name) : "");
            if (value.Length > 0 && value != name) line += " value=" + Quote(value);
            found.Add(line);
            if (found.Count >= limit) break;
        }
        return found;
    }

    /// <summary>The role as the lines show it (CDP's own role names, lower case: "button", "textbox", "link").</summary>
    public static string RoleWord(string role) => role.Length == 0 ? "node" : role.ToLowerInvariant() switch
    {
        "rootwebarea" or "webarea" => "page",
        "statictext" => "text",
        var other => other,
    };

    private static bool IsMasked(string value) => value.Length > 0 && value.All(c => c is '•' or '●' or '*' or '∙');

    private static string Indent(int depth) => new(' ', Math.Min(depth, 20) * 2);

    private static string Clean(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        var builder = new StringBuilder(value.Length);
        var space = false;
        foreach (var c in value)
        {
            if (char.IsWhiteSpace(c) || char.IsControl(c))
            {
                if (!space && builder.Length > 0) builder.Append(' ');
                space = true;
                continue;
            }
            builder.Append(c);
            space = false;
        }
        var cleaned = builder.ToString().Trim();
        return cleaned.Length <= 300 ? cleaned : cleaned[..297] + "…";
    }

    private static string Quote(string value) => "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

    // A link on the page's own site is shown by its path; another site's in full.
    private static string Short(string url, string? pageUrl)
    {
        if (pageUrl is not null && WebPolicy.OriginOf(url) is { } origin && string.Equals(origin, WebPolicy.OriginOf(pageUrl), StringComparison.OrdinalIgnoreCase)
            && Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return uri.PathAndQuery + uri.Fragment;
        return url;
    }

    private static string? Str(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    // An AXValue's value ({type, value}) as text.
    private static string? Inner(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var holder) || holder.ValueKind != JsonValueKind.Object) return null;
        if (!holder.TryGetProperty("value", out var value)) return null;
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => value.GetRawText(),
            _ => null,
        };
    }
}
