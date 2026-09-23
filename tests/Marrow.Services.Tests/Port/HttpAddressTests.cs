using Marrow.Core.Port;
using Marrow.Services.Port;

namespace Marrow.Services.Tests.Port;

// Tests for src/Marrow.Services/Port/HttpAddress.cs, the port-only bridge from a URL that URL(string:) accepted to the
// System.Uri F.HTTP's URLRequest takes (MCPOAuth.swift:252, :721, :750, :774).
public sealed class HttpAddressTests
{
    [Fact]
    [Trait("Rule", "R-PATH-1")]
    public void An_absolute_http_address_becomes_the_request_address_it_names()
    {
        var url = SwiftURL.Create("http://127.0.0.1:8123/.well-known/oauth-authorization-server/tenant")!;
        var uri = HttpAddress.For(url);
        Assert.Equal("http", uri.Scheme);
        Assert.Equal("127.0.0.1", uri.Host);
        Assert.Equal(8123, uri.Port);
        Assert.Equal("/.well-known/oauth-authorization-server/tenant", uri.AbsolutePath);
    }

    [Fact]
    [Trait("Rule", "R-PATH-1")]
    public void A_relative_reference_that_URL_accepts_fails_as_a_bad_URL_the_way_URLSession_fails_the_request()
    {
        // URL(string:) accepts a relative reference (R-PATH-1); URLSession cannot send it, so the request fails.
        var url = SwiftURL.Create("not a url")!;
        var error = Assert.Throws<URLError>(() => HttpAddress.For(url));
        Assert.Equal(URLError.Code.BadURL, error.URLErrorCode);
    }
}
