// PORT-ONLY: web surfaces W16c (DESIGN.md 7.2; ED-NEW-web-w16c-1). No Swift counterpart. The sanitised document written back as
// clean SVG markup, for "Copy as SVG" and for tests: it contains only what the allow-list let through, every number rounded to
// four places, every attribute escaped by an XML writer. Reading what this writes gives the same document (the sanitiser is
// idempotent on its own output), which the tests hold.
using System.Globalization;
using System.Text;
using System.Xml;
using Avalonia;
using Avalonia.Media;

namespace Marrow.UI.NthUi.Diagram;

public static class NthSvgWriter
{
    private const string SvgNamespace = "http://www.w3.org/2000/svg";

    /// <summary>The document as a standalone SVG text.</summary>
    public static string ToMarkup(NthSvgDocument document)
    {
        var text = new StringBuilder();
        var settings = new XmlWriterSettings { OmitXmlDeclaration = true, Indent = false, ConformanceLevel = ConformanceLevel.Fragment, CloseOutput = false };
        using (var w = XmlWriter.Create(text, settings))
        {
            w.WriteStartElement("svg", SvgNamespace);
            w.WriteAttributeString("viewBox", string.Join(' ', N(document.ViewBox.X), N(document.ViewBox.Y), N(document.ViewBox.Width), N(document.ViewBox.Height)));
            if (document.DeclaredSize is { } declared)
            {
                w.WriteAttributeString("width", N(declared.Width));
                w.WriteAttributeString("height", N(declared.Height));
            }
            Style(w, document.Root.Style);
            if (document.Title is { } title) w.WriteElementString("title", title);
            if (document.Description is { } description) w.WriteElementString("desc", description);
            if (document.Gradients.Count > 0 || document.Markers.Count > 0 || document.Defs.Children.Count > 0)
            {
                w.WriteStartElement("defs");
                foreach (var gradient in document.Gradients.Values) Gradient(w, gradient);
                foreach (var marker in document.Markers.Values) Marker(w, marker);
                foreach (var child in document.Defs.Children) Node(w, child);
                w.WriteEndElement();
            }
            foreach (var child in document.Root.Children) Node(w, child);
            w.WriteEndElement();
        }
        return text.ToString();
    }

    private static string N(double value) => value.ToString("0.####", CultureInfo.InvariantCulture);

    private static void Node(XmlWriter w, NthSvgNode node)
    {
        switch (node)
        {
            case NthSvgGroup group:
                w.WriteStartElement(group.Rendered ? "g" : "symbol");
                Common(w, group);
                foreach (var child in group.Children) Node(w, child);
                w.WriteEndElement();
                break;
            case NthSvgRect rect:
                w.WriteStartElement("rect");
                Common(w, rect);
                w.WriteAttributeString("x", N(rect.X));
                w.WriteAttributeString("y", N(rect.Y));
                w.WriteAttributeString("width", N(rect.Width));
                w.WriteAttributeString("height", N(rect.Height));
                if (rect.Rx > 0) w.WriteAttributeString("rx", N(rect.Rx));
                if (rect.Ry > 0) w.WriteAttributeString("ry", N(rect.Ry));
                w.WriteEndElement();
                break;
            case NthSvgEllipse ellipse:
                w.WriteStartElement(ellipse.IsCircle ? "circle" : "ellipse");
                Common(w, ellipse);
                w.WriteAttributeString("cx", N(ellipse.Cx));
                w.WriteAttributeString("cy", N(ellipse.Cy));
                if (ellipse.IsCircle) w.WriteAttributeString("r", N(ellipse.Rx));
                else
                {
                    w.WriteAttributeString("rx", N(ellipse.Rx));
                    w.WriteAttributeString("ry", N(ellipse.Ry));
                }
                w.WriteEndElement();
                break;
            case NthSvgLine line:
                w.WriteStartElement("line");
                Common(w, line);
                w.WriteAttributeString("x1", N(line.X1));
                w.WriteAttributeString("y1", N(line.Y1));
                w.WriteAttributeString("x2", N(line.X2));
                w.WriteAttributeString("y2", N(line.Y2));
                w.WriteEndElement();
                break;
            case NthSvgPoly poly:
                w.WriteStartElement(poly.Closed ? "polygon" : "polyline");
                Common(w, poly);
                w.WriteAttributeString("points", string.Join(' ', poly.Points.Select(p => N(p.X) + "," + N(p.Y))));
                w.WriteEndElement();
                break;
            case NthSvgPathShape path:
                w.WriteStartElement("path");
                Common(w, path);
                w.WriteAttributeString("d", path.Data.ToText());
                w.WriteEndElement();
                break;
            case NthSvgUse use:
                w.WriteStartElement("use");
                Common(w, use);
                w.WriteAttributeString("href", "#" + use.Target);
                if (use.X != 0) w.WriteAttributeString("x", N(use.X));
                if (use.Y != 0) w.WriteAttributeString("y", N(use.Y));
                w.WriteEndElement();
                break;
            case NthSvgText text:
                w.WriteStartElement("text");
                Common(w, text);
                w.WriteAttributeString("x", N(text.X));
                w.WriteAttributeString("y", N(text.Y));
                foreach (var run in text.Runs)
                {
                    w.WriteStartElement("tspan");
                    if (run.X is { } x) w.WriteAttributeString("x", N(x));
                    if (run.Y is { } y) w.WriteAttributeString("y", N(y));
                    if (run.Dx != 0) w.WriteAttributeString("dx", N(run.Dx) + (run.DxEm ? "em" : ""));
                    if (run.Dy != 0) w.WriteAttributeString("dy", N(run.Dy) + (run.DyEm ? "em" : ""));
                    Style(w, run.Style);
                    w.WriteAttributeString("xml", "space", null, "preserve");
                    w.WriteString(run.Text);
                    w.WriteEndElement();
                }
                w.WriteEndElement();
                break;
        }
    }

    private static void Common(XmlWriter w, NthSvgNode node)
    {
        if (node.Id is { } id) w.WriteAttributeString("id", id);
        if (node.HasTransform)
        {
            var m = node.Transform;
            w.WriteAttributeString("transform", $"matrix({N(m.M11)} {N(m.M12)} {N(m.M21)} {N(m.M22)} {N(m.M31)} {N(m.M32)})");
        }
        Style(w, node.Style);
    }

    private static string Paint(NthSvgPaint paint) => paint.Kind switch
    {
        NthSvgPaintKind.None => "none",
        NthSvgPaintKind.CurrentColor => "currentColor",
        NthSvgPaintKind.Reference => "url(#" + paint.Reference + ")" + (paint.Color.A > 0 ? " " + NthSvgColor.ToText(paint.Color) : ""),
        _ => NthSvgColor.ToText(paint.Color),
    };

    private static void Style(XmlWriter w, NthSvgStyle s)
    {
        if (s.IsEmpty) return;
        if (s.Fill is { } fill) w.WriteAttributeString("fill", Paint(fill));
        if (s.Stroke is { } stroke) w.WriteAttributeString("stroke", Paint(stroke));
        if (s.FillOpacity is { } fo) w.WriteAttributeString("fill-opacity", N(fo));
        if (s.StrokeOpacity is { } so) w.WriteAttributeString("stroke-opacity", N(so));
        if (s.Opacity is { } op) w.WriteAttributeString("opacity", N(op));
        if (s.StrokeWidth is { } sw) w.WriteAttributeString("stroke-width", N(sw));
        if (s.MiterLimit is { } ml) w.WriteAttributeString("stroke-miterlimit", N(ml));
        if (s.EvenOdd is { } eo) w.WriteAttributeString("fill-rule", eo ? "evenodd" : "nonzero");
        if (s.LineCap is { } cap) w.WriteAttributeString("stroke-linecap", cap == PenLineCap.Round ? "round" : cap == PenLineCap.Square ? "square" : "butt");
        if (s.LineJoin is { } join) w.WriteAttributeString("stroke-linejoin", join == PenLineJoin.Round ? "round" : join == PenLineJoin.Bevel ? "bevel" : "miter");
        if (s.DashArray is { } dashes) w.WriteAttributeString("stroke-dasharray", dashes.Length == 0 ? "none" : string.Join(' ', dashes.Select(N)));
        if (s.DashOffset is { } offset) w.WriteAttributeString("stroke-dashoffset", N(offset));
        if (s.FontFamily is { } family) w.WriteAttributeString("font-family", family);
        if (s.FontSize is { } size) w.WriteAttributeString("font-size", N(size));
        if (s.FontWeight is { } weight) w.WriteAttributeString("font-weight", weight.ToString(CultureInfo.InvariantCulture));
        if (s.Italic is { } italic) w.WriteAttributeString("font-style", italic ? "italic" : "normal");
        if (s.Anchor is { } anchor) w.WriteAttributeString("text-anchor", anchor == NthSvgAnchor.Middle ? "middle" : anchor == NthSvgAnchor.End ? "end" : "start");
        if (s.Baseline is { } baseline) w.WriteAttributeString("dominant-baseline", baseline switch { NthSvgBaseline.Central => "central", NthSvgBaseline.Hanging => "hanging", NthSvgBaseline.TextBottom => "text-after-edge", _ => "alphabetic" });
        if (s.LetterSpacing is { } spacing) w.WriteAttributeString("letter-spacing", N(spacing));
        if (s.Underline == true || s.LineThrough == true)
            w.WriteAttributeString("text-decoration", string.Join(' ', new[] { s.Underline == true ? "underline" : null, s.LineThrough == true ? "line-through" : null }.Where(x => x is not null)));
        if (s.Visible is { } visible) w.WriteAttributeString("visibility", visible ? "visible" : "hidden");
        if (s.MarkerStart is { } ms) w.WriteAttributeString("marker-start", "url(#" + ms + ")");
        if (s.MarkerMid is { } mm) w.WriteAttributeString("marker-mid", "url(#" + mm + ")");
        if (s.MarkerEnd is { } me) w.WriteAttributeString("marker-end", "url(#" + me + ")");
    }

    private static void Gradient(XmlWriter w, NthSvgGradient gradient)
    {
        w.WriteStartElement(gradient.Radial ? "radialGradient" : "linearGradient");
        w.WriteAttributeString("id", gradient.Id);
        w.WriteAttributeString("gradientUnits", gradient.UserSpace ? "userSpaceOnUse" : "objectBoundingBox");
        if (gradient.Radial)
        {
            w.WriteAttributeString("cx", N(gradient.Cx));
            w.WriteAttributeString("cy", N(gradient.Cy));
            w.WriteAttributeString("r", N(gradient.R));
            w.WriteAttributeString("fx", N(gradient.Fx));
            w.WriteAttributeString("fy", N(gradient.Fy));
        }
        else
        {
            w.WriteAttributeString("x1", N(gradient.X1));
            w.WriteAttributeString("y1", N(gradient.Y1));
            w.WriteAttributeString("x2", N(gradient.X2));
            w.WriteAttributeString("y2", N(gradient.Y2));
        }
        if (gradient.HasTransform)
        {
            var m = gradient.Transform;
            w.WriteAttributeString("gradientTransform", $"matrix({N(m.M11)} {N(m.M12)} {N(m.M21)} {N(m.M22)} {N(m.M31)} {N(m.M32)})");
        }
        foreach (var stop in gradient.Stops)
        {
            w.WriteStartElement("stop");
            w.WriteAttributeString("offset", N(stop.Offset));
            w.WriteAttributeString("stop-color", NthSvgColor.ToText(Color.FromRgb(stop.Color.R, stop.Color.G, stop.Color.B)));
            if (stop.Color.A < 255) w.WriteAttributeString("stop-opacity", N(stop.Color.A / 255.0));
            w.WriteEndElement();
        }
        w.WriteEndElement();
    }

    private static void Marker(XmlWriter w, NthSvgMarker marker)
    {
        w.WriteStartElement("marker");
        w.WriteAttributeString("id", marker.Id);
        w.WriteAttributeString("refX", N(marker.RefX));
        w.WriteAttributeString("refY", N(marker.RefY));
        w.WriteAttributeString("markerWidth", N(marker.Width));
        w.WriteAttributeString("markerHeight", N(marker.Height));
        w.WriteAttributeString("markerUnits", marker.UserSpace ? "userSpaceOnUse" : "strokeWidth");
        w.WriteAttributeString("orient", marker.Auto ? "auto" : N(marker.Angle));
        if (marker.ViewBox is { } vb) w.WriteAttributeString("viewBox", string.Join(' ', N(vb.X), N(vb.Y), N(vb.Width), N(vb.Height)));
        foreach (var child in marker.Content.Children) Node(w, child);
        w.WriteEndElement();
    }
}
