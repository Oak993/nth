// PORT-ONLY: web surfaces W19 (DESIGN.md 7.6 "HostContext"; ED-NEW-web-w19-1). What nth tells an MCP App about where it runs:
// theme, the glass tokens as the spec's standard CSS variables, fonts, display mode and the modes nth offers, the room it has,
// locale, time zone, platform "desktop", device capabilities and safe-area insets. A change is sent as the members that
// changed only (ui/notifications/host-context-changed). No Swift counterpart.
using System.Globalization;
using System.Text.Json.Nodes;

namespace Marrow.Core.Web;

/// <summary>The host context of one mounted view.</summary>
public sealed record McpAppHostContext
{
    public bool Dark { get; init; } = true;

    /// <summary>The CSS variables (the spec's standard names), from <see cref="McpAppStyles.Variables"/>.</summary>
    public IReadOnlyDictionary<string, string> Variables { get; init; } = McpAppStyles.Variables(true);

    public McpAppDisplayMode DisplayMode { get; init; } = McpAppDisplayMode.Inline;

    public IReadOnlyList<McpAppDisplayMode> AvailableModes { get; init; } =
        [McpAppDisplayMode.Inline, McpAppDisplayMode.Pane, McpAppDisplayMode.PopOut];

    /// <summary>A fixed width the host gives the view (pane and window), or null.</summary>
    public double? Width { get; init; }

    /// <summary>A fixed height the host gives the view (pane and window), or null.</summary>
    public double? Height { get; init; }

    /// <summary>The most width the view may take when it sizes itself (inline), or null.</summary>
    public double? MaxWidth { get; init; }

    /// <summary>The most height the view may take when it sizes itself (inline: 60 % of the thread), or null.</summary>
    public double? MaxHeight { get; init; }

    public string Locale { get; init; } = CultureInfo.CurrentUICulture.Name is { Length: > 0 } name ? name : "en-US";

    public string TimeZone { get; init; } = McpAppStyles.IanaTimeZone(TimeZoneInfo.Local);

    public bool Touch { get; init; }

    public bool Hover { get; init; } = true;

    /// <summary>The tool whose result the view draws (name, title, description, inputSchema), or null.</summary>
    public JsonObject? Tool { get; init; }

    /// <summary>The JSON-RPC id of the tool call, when nth has one.</summary>
    public string? ToolCallId { get; init; }

    public JsonObject ToJson()
    {
        var variables = new JsonObject();
        foreach (var (name, value) in Variables) variables[name] = value;
        var context = new JsonObject
        {
            ["theme"] = Dark ? "dark" : "light",
            ["styles"] = new JsonObject { ["variables"] = variables, ["css"] = new JsonObject { ["fonts"] = McpAppStyles.Fonts } },
            ["displayMode"] = McpAppProtocol.SpecName(DisplayMode),
            ["availableDisplayModes"] = new JsonArray(AvailableModes.Select(mode => (JsonNode)McpAppProtocol.SpecName(mode)).ToArray()),
            ["containerDimensions"] = Dimensions(),
            ["locale"] = Locale,
            ["timeZone"] = TimeZone,
            ["platform"] = "desktop",
            ["deviceCapabilities"] = new JsonObject { ["touch"] = Touch, ["hover"] = Hover },
            ["safeAreaInsets"] = new JsonObject { ["top"] = 0, ["right"] = 0, ["bottom"] = 0, ["left"] = 0 },
        };
        if (Tool is { } tool)
        {
            var info = new JsonObject { ["tool"] = tool.DeepClone() };
            if (ToolCallId is { } id) info["id"] = id;
            context["toolInfo"] = info;
        }

        return context;
    }

    /// <summary>The members of <paramref name="after"/> that differ from <paramref name="before"/> (the spec's partial context).</summary>
    public static JsonObject Changes(McpAppHostContext before, McpAppHostContext after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        var old = before.ToJson();
        var changes = new JsonObject();
        foreach (var (name, value) in after.ToJson())
        {
            if (!JsonNode.DeepEquals(old[name], value)) changes[name] = value?.DeepClone();
        }

        return changes;
    }

    private JsonObject Dimensions()
    {
        var dimensions = new JsonObject();
        if (Height is { } height) dimensions["height"] = Math.Round(height);
        else if (MaxHeight is { } maxHeight) dimensions["maxHeight"] = Math.Round(maxHeight);
        if (Width is { } width) dimensions["width"] = Math.Round(width);
        else if (MaxWidth is { } maxWidth) dimensions["maxWidth"] = Math.Round(maxWidth);
        return dimensions;
    }
}

/// <summary>nth's glass tokens under the spec's standard variable names (DESIGN 7.6 "styles.variables mapped from the glass tokens").</summary>
public static class McpAppStyles
{
    /// <summary>nth's type ramp: Segoe UI Variable, the system's own; no web font is loaded.</summary>
    public const string Sans = "\"Segoe UI Variable Text\", \"Segoe UI\", system-ui, sans-serif";

    public const string Mono = "\"Cascadia Mono\", Consolas, ui-monospace, monospace";

    /// <summary>The spec's <c>css.fonts</c>: nothing to load, the fonts are the system's.</summary>
    public const string Fonts = "";

    /// <summary>The variables for a dark or light thread. Colours are the pane tokens (PaneTokens, NthUiTokens): text at
    /// 100, 68 and 48 % on dark, ink at 86, 62 and 56 % on light, hairlines at 8 %, the accent and the status colours.</summary>
    public static IReadOnlyDictionary<string, string> Variables(bool dark)
    {
        var (ink, paper) = dark ? ("236,236,238", "255,255,255") : ("20,20,24", "0,0,0");
        string Text(double alpha) => $"rgba({ink},{alpha.ToString("0.##", CultureInfo.InvariantCulture)})";
        string Wash(double alpha) => $"rgba({paper},{alpha.ToString("0.###", CultureInfo.InvariantCulture)})";
        var accent = dark ? "#4B9FFC" : "#1F6FD6";
        var ok = dark ? "#5BD18B" : "#1E8E4E";
        var warn = dark ? "#F2B84B" : "#A86B00";
        var danger = dark ? "#FF7A7A" : "#C4372F";
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["--color-background-primary"] = dark ? "#1C1C1F" : "#FBFBFA",
            ["--color-background-secondary"] = Wash(dark ? .06 : .04),
            ["--color-background-tertiary"] = Wash(dark ? .10 : .07),
            ["--color-background-inverse"] = dark ? "#ECECEE" : "#1C1C1F",
            ["--color-background-ghost"] = "transparent",
            ["--color-background-info"] = dark ? "rgba(75,159,252,.16)" : "rgba(31,111,214,.10)",
            ["--color-background-danger"] = dark ? "rgba(255,122,122,.16)" : "rgba(196,55,47,.10)",
            ["--color-background-success"] = dark ? "rgba(91,209,139,.16)" : "rgba(30,142,78,.10)",
            ["--color-background-warning"] = dark ? "rgba(242,184,75,.16)" : "rgba(168,107,0,.10)",
            ["--color-background-disabled"] = Wash(dark ? .04 : .03),
            ["--color-text-primary"] = Text(dark ? 1 : .86),
            ["--color-text-secondary"] = Text(dark ? .68 : .62),
            ["--color-text-tertiary"] = Text(dark ? .48 : .56),
            ["--color-text-inverse"] = dark ? "#1C1C1F" : "#ECECEE",
            ["--color-text-info"] = accent,
            ["--color-text-danger"] = danger,
            ["--color-text-success"] = ok,
            ["--color-text-warning"] = warn,
            ["--color-text-disabled"] = Text(dark ? .32 : .38),
            ["--color-text-ghost"] = Text(dark ? .48 : .56),
            ["--color-border-primary"] = Wash(dark ? .14 : .12),
            ["--color-border-secondary"] = Wash(.08),
            ["--color-border-tertiary"] = Wash(dark ? .05 : .05),
            ["--color-border-inverse"] = dark ? "rgba(0,0,0,.2)" : "rgba(255,255,255,.3)",
            ["--color-border-ghost"] = "transparent",
            ["--color-border-info"] = accent,
            ["--color-border-danger"] = danger,
            ["--color-border-success"] = ok,
            ["--color-border-warning"] = warn,
            ["--color-border-disabled"] = Wash(.06),
            ["--color-ring-primary"] = accent,
            ["--color-ring-secondary"] = Wash(dark ? .3 : .25),
            ["--color-ring-inverse"] = dark ? "#1C1C1F" : "#FFFFFF",
            ["--color-ring-info"] = accent,
            ["--color-ring-danger"] = danger,
            ["--color-ring-success"] = ok,
            ["--color-ring-warning"] = warn,
            ["--font-sans"] = Sans,
            ["--font-mono"] = Mono,
            ["--font-weight-normal"] = "400",
            ["--font-weight-medium"] = "500",
            ["--font-weight-semibold"] = "600",
            ["--font-weight-bold"] = "700",
            ["--font-text-xs-size"] = "11px",
            ["--font-text-sm-size"] = "12.5px",
            ["--font-text-md-size"] = "14px",
            ["--font-text-lg-size"] = "16px",
            ["--font-heading-xs-size"] = "13px",
            ["--font-heading-sm-size"] = "15px",
            ["--font-heading-md-size"] = "17px",
            ["--font-heading-lg-size"] = "20px",
            ["--font-heading-xl-size"] = "24px",
            ["--font-heading-2xl-size"] = "28px",
            ["--font-heading-3xl-size"] = "34px",
            ["--font-text-xs-line-height"] = "1.4",
            ["--font-text-sm-line-height"] = "1.45",
            ["--font-text-md-line-height"] = "1.5",
            ["--font-text-lg-line-height"] = "1.5",
            ["--font-heading-xs-line-height"] = "1.3",
            ["--font-heading-sm-line-height"] = "1.3",
            ["--font-heading-md-line-height"] = "1.25",
            ["--font-heading-lg-line-height"] = "1.2",
            ["--font-heading-xl-line-height"] = "1.2",
            ["--font-heading-2xl-line-height"] = "1.15",
            ["--font-heading-3xl-line-height"] = "1.1",
            ["--border-radius-xs"] = "4px",
            ["--border-radius-sm"] = "7px",
            ["--border-radius-md"] = "9px",
            ["--border-radius-lg"] = "12px",
            ["--border-radius-xl"] = "14px",
            ["--border-radius-full"] = "999px",
            ["--border-width-regular"] = "0.5px",
            ["--shadow-hairline"] = "inset 0 0 0 0.5px " + Wash(dark ? .1 : .1),
            ["--shadow-sm"] = dark ? "0 1px 2px rgba(0,0,0,.4)" : "0 1px 2px rgba(0,0,0,.08)",
            ["--shadow-md"] = dark ? "0 4px 14px rgba(0,0,0,.35)" : "0 4px 14px rgba(0,0,0,.10)",
            ["--shadow-lg"] = dark ? "0 12px 36px rgba(0,0,0,.45)" : "0 12px 36px rgba(0,0,0,.14)",
        };
    }

    /// <summary>The IANA name of a time zone (Windows ids are converted), "UTC" when there is none.</summary>
    public static string IanaTimeZone(TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        if (zone.HasIanaId) return zone.Id;
        return TimeZoneInfo.TryConvertWindowsIdToIanaId(zone.Id, out var iana) ? iana : "UTC";
    }
}
