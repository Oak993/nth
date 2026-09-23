// PORT-ONLY: the System.Uri that F.HTTP's URLRequest takes, made from a Foundation URL the Swift code has already parsed (URLRequest(url:), MCPOAuth.swift:252, :721, :750, :774).
using Marrow.Core.Port;

namespace Marrow.Services.Port;

/// <summary>
/// Bridges the two foundation units: Swift decides whether an address is a URL with <c>URL(string:)</c>
/// (<see cref="SwiftURL.Create"/>, R-PATH-1), and F.HTTP's <see cref="URLRequest"/> is keyed by a System.Uri
/// (ED-168). ENGINEERING DECISION: the request goes to the URL's absoluteString; System.Uri may still refuse a string
/// Foundation accepted (Foundation hands such a URL to URLSession, which then fails the request), so a refusal is
/// thrown as <c>URLError(.badURL)</c> at the point where URLSession would fail. Every MCPOAuth call site collapses a
/// URLSession error without reading its code (MCPOAuth.swift:255, :736, :764, :788).
/// </summary>
public static class HttpAddress
{
    public static Uri For(SwiftURL url) =>
        Uri.TryCreate(url.AbsoluteString, UriKind.Absolute, out var uri) ? uri : throw new URLError(URLError.Code.BadURL);
}
