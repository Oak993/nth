using System.Text.Json;
using Marrow.Services.Web;

namespace Marrow.Services.Tests.Web;

// DESIGN 5.8, 8.1: the console and network views, the model's page tools and the logs never see cookie or
// authorization values; every other byte of the CDP message is kept.
[Trait("Decision", "ED-NEW-web-w7-4")]
[Trait("Feature", "W-7")]
public sealed class CdpRedactorTests
{
    [Fact]
    public void Header_objects_lose_cookie_and_authorization_values_in_any_case()
    {
        var json = """{"response":{"headers":{"Set-Cookie":"a=1","content-type":"text/html"},"requestHeaders":{"COOKIE":"b=2","Proxy-Authorization":"Basic x","authorization":"Bearer y"}}}""";
        using var document = JsonDocument.Parse(CdpRedactor.Redact(json));
        var response = document.RootElement.GetProperty("response");
        Assert.Equal(CdpRedactor.Mask, response.GetProperty("headers").GetProperty("Set-Cookie").GetString());
        Assert.Equal("text/html", response.GetProperty("headers").GetProperty("content-type").GetString());
        var request = response.GetProperty("requestHeaders");
        Assert.Equal(CdpRedactor.Mask, request.GetProperty("COOKIE").GetString());
        Assert.Equal(CdpRedactor.Mask, request.GetProperty("Proxy-Authorization").GetString());
        Assert.Equal(CdpRedactor.Mask, request.GetProperty("authorization").GetString());
    }

    [Fact]
    public void A_redacted_message_keeps_every_other_character_as_it_was()
    {
        var json = """{"expression":"1+1 < 2 && 'é' != \"ü\"","headers":{"Cookie":"x=1"}}""";
        Assert.Equal("""{"expression":"1+1 < 2 && 'é' != \"ü\"","headers":{"Cookie":"[redacted]"}}""", CdpRedactor.Redact(json));
    }

    [Fact]
    public void Header_lists_and_raw_header_text_are_redacted()
    {
        var json = """{"headers":[{"name":"Cookie","value":"c=3"},{"name":"Accept","value":"*/*"}],"headersText":"GET / HTTP/1.1\r\nCookie: c=3\r\nHost: a.test\r\nAuthorization:Bearer z\r\n"}""";
        using var document = JsonDocument.Parse(CdpRedactor.Redact(json));
        var headers = document.RootElement.GetProperty("headers");
        Assert.Equal(CdpRedactor.Mask, headers[0].GetProperty("value").GetString());
        Assert.Equal("*/*", headers[1].GetProperty("value").GetString());
        Assert.Equal("GET / HTTP/1.1\r\nCookie: [redacted]\r\nHost: a.test\r\nAuthorization:[redacted]\r\n", document.RootElement.GetProperty("headersText").GetString());
    }

    [Fact]
    public void Cookie_lists_keep_names_and_lose_values_and_lines()
    {
        var json = """{"associatedCookies":[{"cookie":{"name":"sid","value":"v1"},"blockedReasons":[]}],"blockedCookies":[{"cookieLine":"x=1; Path=/","blockedReasons":["SameSiteLax"]}],"cookie":{"name":"one","value":"v2"}}""";
        var redacted = CdpRedactor.Redact(json);
        Assert.DoesNotContain("v1", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("v2", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("x=1", redacted, StringComparison.Ordinal);
        Assert.Contains("\"sid\"", redacted, StringComparison.Ordinal);
        Assert.Contains("SameSiteLax", redacted, StringComparison.Ordinal);
    }

    [Fact]
    public void Messages_without_secrets_and_text_that_is_not_JSON_come_back_unchanged()
    {
        const string plain = """{"frameId":"F1","url":"https://a.test/?q=cookie"}""";
        Assert.Same(plain, CdpRedactor.Redact(plain));
        Assert.Equal("not json", CdpRedactor.Redact("not json"));
        Assert.Equal(string.Empty, CdpRedactor.Redact(string.Empty));
    }

    [Theory]
    [InlineData("cookie", true)]
    [InlineData("Set-Cookie", true)]
    [InlineData("AUTHORIZATION", true)]
    [InlineData("proxy-authorization", true)]
    [InlineData("x-cookie-hint", false)]
    [InlineData("accept", false)]
    public void Sensitive_headers_are_matched_by_exact_name(string name, bool sensitive)
    {
        Assert.Equal(sensitive, CdpRedactor.IsSensitiveHeader(name));
    }
}
