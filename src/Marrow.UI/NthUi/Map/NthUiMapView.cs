// PORT-ONLY: web surfaces W16c (DESIGN.md 7.2 "Map: stops and routes on a built-in offline world basemap ... Pan and zoom by pointer and
// keyboard"; 7.4; 10 "a text alternative"; ED-NEW-web-w16c-1). No Swift counterpart. The `map` component of a live answer: the map
// canvas with its three buttons (zoom in, zoom out, show all stops), the title above it, and "Show as list" under it, which swaps
// the map for the stops and routes as a plain list (the text alternative a screen reader gets as well as the summary). The line's
// stop and route parts arrive through Add; each arrival fades in on the map and, while the reader has not moved the map, the camera
// glides to keep every stop in view.
using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Marrow.Core.NthUi;
using Marrow.UI.NthUi.Diagram;

namespace Marrow.UI.NthUi.Map;

/// <summary>Words of the map view (W22 moves them into the resource files).</summary>
public static class NthUiMapStrings
{
    public const string ShowAsList = "Show as list";
    public const string ShowAsMap = "Show as map";
    public const string ZoomIn = "Zoom in";
    public const string ZoomOut = "Zoom out";
    public const string ShowAll = "Show all stops";
    public const string ZoomInTip = "Zoom in (Ctrl and scroll also zooms)";
}

/// <summary>`map`: stops and routes on the offline world.</summary>
public sealed class NthUiMapView : NthUiNodeView
{
    private readonly StackPanel root = new() { Spacing = 8 };
    private readonly Panel host = new();
    private readonly Panel mapLayer = new();
    private readonly NthUiColumn listLayer = new() { Gap = 6 };
    private readonly NthUiMapCanvas canvas;
    private readonly TextBlock? title;
    private readonly ToggleButton toggle;
    private readonly TextBlock toggleText;
    private readonly Button zoomIn;
    private readonly Button zoomOut;
    private readonly Button fit;
    private NthUiMapModel model = new("", [], []);
    private bool listShown;
    private NthSvgFade? fade;

    public NthUiMapView(NthUiContext context, NthUiNode node) : base(context, node)
    {
        canvas = new NthUiMapCanvas(context.Motion);
        canvas.InitialZoom = Runtime.Value(node, "zoom") is { IsNumber: true } z ? Math.Clamp(z.AsNumber, 0, NthUiMapCamera.MaxZoom) : null;
        var caption = Runtime.Text(node);
        if (caption.Length > 0)
        {
            title = NthUiText.Block(caption, NthUiTokens.BodyStrong, NthUiTokens.Text1);
            root.Children.Add(title);
        }

        zoomIn = IconButton("plus", NthUiMapStrings.ZoomIn, () => canvas.ZoomIn());
        zoomOut = IconButton("minus", NthUiMapStrings.ZoomOut, () => canvas.ZoomOut());
        fit = IconButton("scan", NthUiMapStrings.ShowAll, () => canvas.FitAll());
        var buttons = new StackPanel { Spacing = 4, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 10, 10) };
        buttons.Children.Add(zoomIn);
        buttons.Children.Add(zoomOut);
        buttons.Children.Add(fit);
        mapLayer.Children.Add(canvas);
        mapLayer.Children.Add(buttons);
        listLayer.IsVisible = false;
        AutomationProperties.SetControlTypeOverride(listLayer, AutomationControlType.List);
        host.Children.Add(mapLayer);
        host.Children.Add(listLayer);
        root.Children.Add(host);

        toggle = NthUiPress.Plain(new ToggleButton());
        var face = new NthUiFace(toggle, NthUiFaceStyle.Segment, 13) { Height = 26, Padding = new Thickness(9, 0) };
        toggleText = NthUiText.Block(NthUiMapStrings.ShowAsList, NthUiTokens.Small, null, wrap: false);
        toggleText.VerticalAlignment = VerticalAlignment.Center;
        face.Child = toggleText;
        toggle.Content = face;
        toggle.HorizontalAlignment = HorizontalAlignment.Right;
        toggle.Click += (_, _) => ShowList(toggle.IsChecked == true);
        toggle.IsCheckedChanged += (_, _) => toggle.Classes.Set("on", toggle.IsChecked == true);
        AutomationProperties.SetName(toggle, NthUiMapStrings.ShowAsList);
        var footer = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), MinHeight = 26 };
        Grid.SetColumn(toggle, 1);
        footer.Children.Add(toggle);
        root.Children.Add(footer);

        AutomationProperties.SetControlTypeOverride(root, AutomationControlType.Group);
        AutomationProperties.SetControlTypeOverride(canvas, AutomationControlType.Group);
        Rebuild();
        // The basemap is read on the pool as soon as a map is asked for, so the first render does not wait for the file.
        _ = NthUiMapData.PreloadAsync();
    }

    public override Control Control => root;

    /// <summary>The control that draws the map.</summary>
    public NthUiMapCanvas Canvas => canvas;

    /// <summary>The stops and routes on show.</summary>
    public NthUiMapModel Model => model;

    public Button ZoomInButton => zoomIn;

    public Button ZoomOutButton => zoomOut;

    public Button FitButton => fit;

    /// <summary>The "Show as list" button.</summary>
    public ToggleButton ListButton => toggle;

    /// <summary>The list of stops and routes (shown in place of the map).</summary>
    public NthUiColumn List => listLayer;

    public bool IsListShown => listShown;

    /// <summary>The words that describe the map, as a screen reader gets them.</summary>
    public string Summary => model.Summary(Runtime.Culture);

    public override NthUiNodeView? Add(NthUiNode child)
    {
        if (child.Degraded || child.Kind is not ("stop" or "route")) return null;
        Context.Register(child, this);
        Rebuild();
        return null;
    }

    public override void Refresh() => Rebuild();

    private static Button IconButton(string icon, string name, Action pressed)
    {
        var button = NthUiPress.Plain(new Button());
        var face = new NthUiFace(button, NthUiFaceStyle.Plain, 15) { Width = 30, Height = 30 };
        if (NthUiText.Icon(icon, 14) is { } glyph)
        {
            glyph.HorizontalAlignment = HorizontalAlignment.Center;
            glyph.VerticalAlignment = VerticalAlignment.Center;
            face.Child = glyph;
        }
        button.Content = face;
        button.Click += (_, _) => pressed();
        button.Focusable = true;
        AutomationProperties.SetName(button, name);
        ToolTip.SetTip(button, name);
        return button;
    }

    private void Rebuild()
    {
        var culture = Runtime.Culture;
        var stops = new List<NthUiMapStop>();
        var routes = new List<NthUiMapRoute>();
        foreach (var child in Node.Children)
        {
            if (child.Degraded) continue;
            if (child.Kind == "stop")
            {
                var lat = Runtime.Value(child, "lat");
                var lon = Runtime.Value(child, "lon");
                if (!lat.IsNumber || !lon.IsNumber) continue;
                stops.Add(new NthUiMapStop(child.Key, Runtime.Text(child), Math.Clamp(lat.AsNumber, -90, 90), Math.Clamp(lon.AsNumber, -180, 180),
                    child.Arg("detail") is null ? null : Runtime.Display(child, "detail")));
            }
            else if (child.Kind == "route")
            {
                routes.Add(new NthUiMapRoute(child.Key, Runtime.Text(child), [.. Runtime.Values(child, "via").Select(v => v.Display(culture))]));
            }
        }
        model = new NthUiMapModel(Runtime.Text(Node), stops, routes);
        title?.SetCurrentValue(TextBlock.TextProperty, Runtime.Text(Node));
        canvas.Show(model);
        BuildList(culture);
        AutomationProperties.SetName(root, model.Title.Length > 0 ? model.Title : NthUiStrings.Map);
        AutomationProperties.SetName(canvas, model.Title.Length > 0 ? model.Title : NthUiStrings.Map);
        AutomationProperties.SetHelpText(canvas, Summary);
        AutomationProperties.SetHelpText(root, Summary);
    }

    private void BuildList(CultureInfo culture)
    {
        listLayer.Children.Clear();
        foreach (var stop in model.Stops)
        {
            var place = stop.Coordinates(culture);
            var detail = stop.Detail is { Length: > 0 } d ? d + " · " + place : place;
            var marker = (Control?)NthUiText.Icon("map-pin", 14, NthUiTokens.Text2) ?? new Border();
            marker.Margin = new Thickness(0, 2, 8, 0);
            var line = NthUiItems.Line(marker, stop.Label, detail, out _, out _);
            AutomationProperties.SetName(line, stop.Label + ", " + detail);
            listLayer.Children.Add(line);
        }
        foreach (var route in model.Routes)
        {
            var text = route.Label;
            var detail = string.Join(" → ", route.Via);
            if (text.Length == 0) (text, detail) = (detail, "");
            var marker = (Control?)NthUiText.Icon("git-branch", 14, NthUiTokens.Text2) ?? new Border();
            marker.Margin = new Thickness(0, 2, 8, 0);
            var line = NthUiItems.Line(marker, text, detail, out _, out _);
            AutomationProperties.SetName(line, detail.Length > 0 ? text + ", " + detail : text);
            listLayer.Children.Add(line);
        }
    }

    /// <summary>Shows the stops and routes as a list in place of the map (or the map again): the height follows by spring and the
    /// incoming side fades in where it sits.</summary>
    public void ShowList(bool on)
    {
        if (listShown == on) return;
        listShown = on;
        toggle.IsChecked = on;
        toggleText.Text = on ? NthUiMapStrings.ShowAsMap : NthUiMapStrings.ShowAsList;
        AutomationProperties.SetName(toggle, toggleText.Text);
        Slot?.AnimateNextChange();
        mapLayer.IsVisible = !on;
        listLayer.IsVisible = on;
        if (!Context.Motion.CanAnimate) return;
        var incoming = on ? (Control)listLayer : mapLayer;
        fade = new NthSvgFade(incoming, NthUiMotion.Now, Context.IsCalm(incoming) ? NthUiTokens.CalmFadeSeconds : NthUiTokens.FadeSeconds);
        Context.Motion.Start(fade);
    }
}
