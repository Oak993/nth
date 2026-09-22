using System.Text;
using System.Text.Json;
using Marrow.Core.Models;
using Marrow.Core.Support;

namespace Marrow.Core.Tests.Models;

public sealed class MCPTests
{
    private static MCPCatalogEntry Entry(MCPTransport? transport = null) =>
        new("test", "Test", "Vendor", "Summary", MCPCategory.Developer, "asset", 0x123456,
            transport ?? new MCPTransport.OAuth("https://{instance}/api"), "docs");

    [Fact]
    [Trait("Req", "PR-MCP-038")]
    public void Category_order_ids_and_titles_are_verbatim()
    {
        var categories = Enum.GetValues<MCPCategory>();
        Assert.Equal(new[] { "developer", "browser", "search", "work", "data", "cloud", "knowledge" }, categories.Select(c => c.Id()));
        Assert.Equal(new[] { "Developer", "Browser", "Search", "Work", "Data", "Cloud", "Knowledge" }, categories.Select(c => c.Title()));
    }

    [Fact]
    [Trait("Req", "PR-MCP-039")]
    public void Fill_uses_field_order_replaces_all_occurrences_and_preserves_unknown_tokens()
    {
        var entry = Entry() with { Fields = [new("a", "A", "", DefaultValue: "{b}"), new("b", "B", "", DefaultValue: "fallback"), new("c", "C", "")] };
        Assert.Equal("fallback/fallback//{unknown}", entry.Fill("{a}/{a}/{c}/{unknown}", new Dictionary<string,string> { ["a"] = "" }));
        Assert.Equal(" typed / typed //{unknown}", entry.Fill("{a}/{a}/{c}/{unknown}", new Dictionary<string,string> { ["a"] = " typed " }));
        Assert.Equal("fallback", entry.Fill("{a}", new Dictionary<string,string>()));
    }

    [Theory]
    [Trait("Req", "PR-MCP-040")]
    [InlineData("  HTTPS://gitlab.example.com///\r\n", "https://gitlab.example.com/api")]
    [InlineData("https://http://x", "https://x/api")]
    [InlineData("http://https://x", "https://https://x/api")]
    [InlineData("///", "https:///api")]
    [InlineData("\u0085https://x/\u2028", "https://x/api")]
    public void Host_normalization_matches_the_ordered_source_scheme_loop(string input, string expected)
    {
        var entry = Entry() with { Fields = [new("instance", "Instance", "", MCPField.Kind.Text)] };
        var values = new Dictionary<string,string> { ["instance"] = input };
        Assert.Equal(expected, entry.Fill("https://{instance}/api", values));
        Assert.Equal(input, entry.Fill("{instance}", values));
    }

    [Fact]
    [Trait("Req", "PR-MCP-044")]
    [Trait("Req", "PR-MCP-045")]
    [Trait("Req", "PR-MCP-046")]
    public void OAuth_URL_and_input_flags_are_derived_from_the_transport_and_required_fields()
    {
        var entry = Entry();
        Assert.True(entry.IsOAuth); Assert.False(entry.NeedsInput);
        entry = entry with { Fields = [new("instance", "Address", "", MCPField.Kind.Text, DefaultValue: "gitlab.com", IsRequired: false)] };
        Assert.False(entry.NeedsInput); Assert.Equal("https://gitlab.com/api", entry.OAuthUrl());
        Assert.Equal("https://self.host/api", entry.OAuthUrl(new Dictionary<string,string> { ["instance"] = "self.host" }));
        Assert.True((entry with { Fields = [new("key", "Key", "")] }).NeedsInput);
        foreach (var transport in new MCPTransport[] { new MCPTransport.Stdio("cmd", []), new MCPTransport.Http("url", new Dictionary<string,string>()) })
        {
            var other = entry with { Transport = transport };
            Assert.False(other.IsOAuth); Assert.Null(other.OAuthUrl());
        }
        Assert.False(new MCPResolvedServer("id", "name").IsRemote);
        Assert.True((new MCPResolvedServer("id", "name") { Url = "" }).IsRemote);
    }

    [Fact]
    [Trait("Req", "PR-MCP-047")]
    public void Connection_JSON_uses_exact_keys_and_restarts_with_date_tools_and_values()
    {
        var connection = new MCPConnection("github") { IsEnabled = false, Values = new Dictionary<string,string> { ["path"] = "/repo" },
            ConnectedAt = new DateTimeOffset(2026, 9, 22, 1, 2, 3, TimeSpan.Zero), ServerVersion = "1.2", Tools = [new("search", "Find")] };
        const string expected = "{\"catalogID\":\"github\",\"connectedAt\":\"2026-09-22T01:02:03Z\",\"isEnabled\":false,\"serverVersion\":\"1.2\",\"tools\":[{\"name\":\"search\",\"summary\":\"Find\"}],\"values\":{\"path\":\"\\/repo\"}}";
        Assert.Equal(expected, Encoding.UTF8.GetString(Coding.Encode(connection)));
        var restored = Coding.Decode<MCPConnection>(Encoding.UTF8.GetBytes(expected))!;
        Assert.Equal("github", restored.Id); Assert.False(restored.IsEnabled); Assert.Equal(connection.ConnectedAt, restored.ConnectedAt);
        Assert.Equal("/repo", restored.Values["path"]); Assert.Equal("1.2", restored.ServerVersion);
        Assert.Equal(new MCPToolSummary("search", "Find"), Assert.Single(restored.Tools));
        var empty = new MCPConnection("id");
        Assert.True(empty.IsEnabled); Assert.Empty(empty.Values); Assert.Empty(empty.Tools); Assert.Null(empty.ConnectedAt); Assert.Null(empty.ServerVersion);
        Assert.Equal("{\"catalogID\":\"id\",\"isEnabled\":true,\"tools\":[],\"values\":{}}", Encoding.UTF8.GetString(Coding.Encode(empty)));
        Assert.Equal("search", new MCPToolSummary("search", null).Id);
    }

    [Theory] // Swift synthesized Codable requires the nonoptional fields even when construction has defaults.
    [Trait("Req", "PR-MCP-047")]
    [Trait("Rule", "R-CODE-1")]
    [InlineData("{}")]
    [InlineData("{\"catalogID\":\"x\"}")]
    [InlineData("{\"catalogID\":null,\"isEnabled\":true,\"values\":{},\"tools\":[]}")]
    [InlineData("{\"catalogID\":\"x\",\"isEnabled\":1,\"values\":{},\"tools\":[]}")]
    [InlineData("{\"catalogID\":\"x\",\"isEnabled\":true,\"values\":{\"a\":null},\"tools\":[]}")]
    [InlineData("{\"catalogID\":\"x\",\"isEnabled\":true,\"values\":{},\"tools\":[{}]}")]
    public void Malformed_required_connection_fields_fail_decoding(string json) =>
        Assert.Throws<JsonException>(() => Coding.Decode<MCPConnection>(Encoding.UTF8.GetBytes(json)));
}
