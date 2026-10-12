namespace Marrow.UI.Tests.NthUi.Diagram;

/// <summary>ED-NEW-web-w16c-1: SVG markup the tests share. <see cref="Flowchart"/> has the shape of what the hidden renderer
/// hands over for a Mermaid flowchart (styles already written into the elements, markers for the arrowheads, labels as text
/// with tspans); the hostile samples are what a page or a model could try.</summary>
internal static class SvgSamples
{
    public const string Flowchart = """
        <svg xmlns="http://www.w3.org/2000/svg" width="320" height="180" viewBox="0 0 320 180" role="graphics-document document" aria-roledescription="flowchart-v2">
          <title>Request flow</title>
          <defs>
            <marker id="m_pointEnd" viewBox="0 0 10 10" refX="5" refY="5" markerUnits="userSpaceOnUse" markerWidth="8" markerHeight="8" orient="auto">
              <path d="M 0 0 L 10 5 L 0 10 z" fill="#6f6f72" stroke="#6f6f72" stroke-width="1" stroke-dasharray="1, 0"/>
            </marker>
          </defs>
          <g>
            <path d="M160,50 L160,110" fill="none" stroke="#6f6f72" stroke-width="2" marker-end="url(#m_pointEnd)"/>
            <g transform="translate(160, 30)">
              <rect x="-50" y="-20" width="100" height="40" rx="8" ry="8" fill="#ffffff" stroke="#9a9a9d" stroke-width="1"/>
              <text x="0" y="0" text-anchor="middle" dominant-baseline="central" fill="#1c1c1e" font-family="Segoe UI Variable, Segoe UI, sans-serif" font-size="14px"><tspan x="0" dy="0" xml:space="preserve">Client</tspan></text>
            </g>
            <g transform="translate(160, 130)">
              <rect x="-50" y="-20" width="100" height="40" rx="8" ry="8" fill="#ffffff" stroke="#9a9a9d" stroke-width="1"/>
              <text x="0" y="0" text-anchor="middle" dominant-baseline="central" fill="#1c1c1e" font-family="Segoe UI Variable, Segoe UI, sans-serif" font-size="14px"><tspan x="0" dy="0">Server</tspan></text>
            </g>
          </g>
        </svg>
        """;

    public const string LineArt = """
        <svg viewBox="0 0 100 60" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round">
          <circle cx="30" cy="30" r="20"/>
          <path d="M55 30 H90 M80 20 L90 30 L80 40"/>
          <text x="30" y="34" text-anchor="middle" font-size="10" fill="currentColor" stroke="none">A</text>
        </svg>
        """;

    /// <summary>Everything a page could try, in one document that is still well formed.</summary>
    public const string Hostile = """
        <svg xmlns="http://www.w3.org/2000/svg" xmlns:xlink="http://www.w3.org/1999/xlink" viewBox="0 0 100 100" onload="alert(1)">
          <script>alert(document.cookie)</script>
          <style>@import url(http://evil.example/x.css); rect { fill: url(http://evil.example/a.svg#b) }</style>
          <foreignObject x="0" y="0" width="50" height="50"><div xmlns="http://www.w3.org/1999/xhtml"><iframe src="http://evil.example"></iframe></div></foreignObject>
          <image href="http://evil.example/pixel.png" width="10" height="10"/>
          <a href="javascript:alert(1)"><rect id="r1" x="10" y="10" width="30" height="30" fill="red" onclick="steal()" onmouseover="x()"/></a>
          <rect x="50" y="10" width="30" height="30" style="fill:url(http://evil.example/a.svg#b);stroke:red" fill="blue"/>
          <use href="http://evil.example/x.svg#a"/>
          <use xlink:href="javascript:alert(1)"/>
          <use href="#r1" x="20" y="40"/>
          <animate attributeName="x" from="0" to="100" dur="1s"/>
          <set attributeName="fill" to="red"/>
          <filter id="f"><feImage href="http://evil.example/a.png"/></filter>
          <clipPath id="c"><rect width="5" height="5"/></clipPath>
          <mask id="mk"><rect width="5" height="5"/></mask>
          <pattern id="p" width="5" height="5"><rect width="5" height="5"/></pattern>
          <switch><text>x</text></switch>
          <path d="M0 0 L50 50" stroke="red" marker-end="url(http://evil.example/m.svg#m)" filter="url(#f)" clip-path="url(#c)" mask="url(#mk)"/>
          <text x="10" y="90" font-family="Arial, url(http://evil.example/f.woff)">Hi<tspan onclick="x()">there</tspan><textPath href="#r1">bent</textPath></text>
        </svg>
        """;

    public static string Many(int count)
    {
        var builder = new System.Text.StringBuilder("<svg viewBox=\"0 0 100 100\">");
        for (var i = 0; i < count; i++) builder.Append("<rect width=\"1\" height=\"1\"/>");
        return builder.Append("</svg>").ToString();
    }

    public static string Deep(int depth)
    {
        var builder = new System.Text.StringBuilder("<svg viewBox=\"0 0 100 100\">");
        for (var i = 0; i < depth; i++) builder.Append("<g>");
        builder.Append("<rect width=\"1\" height=\"1\"/>");
        for (var i = 0; i < depth; i++) builder.Append("</g>");
        return builder.Append("</svg>").ToString();
    }

    /// <summary>The markup shapes that must never be written back, whatever text a document carries (a text node may say
    /// "javascript:", which is only text).</summary>
    public static readonly string[] Structural =
    [
        "<script", "<foreignObject", "<style", " onclick=", " onload=", " onmouseover=", "<image", "<iframe", "<animate", "<set ", "<filter",
        "<clipPath", "<mask", "<pattern", "<switch", "<textPath", "href=\"http", "href=\"javascript", "url(http", "DOCTYPE", "<!ENTITY",
    ];

    /// <summary>What must never appear in markup nth writes back from the hostile sample.</summary>
    public static readonly string[] Forbidden =
    [
        "script", "foreignObject", "<style", "onclick", "onload", "onmouseover", "javascript:", "evil.example", "<image", "<iframe",
        "<animate", "<set", "<filter", "clipPath", "<mask", "<pattern", "<switch", "textPath", "DOCTYPE", "<!ENTITY",
    ];
}
