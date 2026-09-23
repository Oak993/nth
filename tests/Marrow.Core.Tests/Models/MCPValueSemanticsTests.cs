using Marrow.Core.Models;
using Marrow.Core.Port;

namespace Marrow.Core.Tests.Models;

// Source: DroppyCode/Core/Models/MCP.swift (B2 1fbf81e). Every struct and enum there is Sendable and Hashable, so
// its collections are values: a copy is independent and == compares contents (R-VAL-1, R-VAL-2, ED-077). The
// collections are the foundation ValueList and ValueMap (IMPLEMENTING 4.4 repair of the internal MCPValues wrappers).
public sealed class MCPValueSemanticsTests
{
    private static ValueList<T> L<T>(params T[] items) => new(items);

    private static ValueMap<string, string> M(params (string Key, string Value)[] pairs) =>
        new(pairs.Select(p => KeyValuePair.Create(p.Key, p.Value)), SwiftStringComparer.Instance);

    private static MCPCatalogEntry Entry(MCPTransport transport) =>
        new MCPCatalogEntry(Id: "id", Name: "Name", Vendor: "Vendor", Summary: "Summary", Category: MCPCategory.Work,
            Asset: "mcp-id", ColorValue: 3, Transport: transport, DocsURL: "docs");

    [Fact] // MCP.swift:5-15 MCPTransport: Hashable
    [Trait("Rule", "R-VAL-2")]
    public void Transport_equality_and_hashing_compare_contents_and_a_copy_is_independent()
    {
        var args = new List<string> { "a" };
        var first = new MCPTransport.Stdio("command", new ValueList<string>(args));
        args.Add("b");
        var second = new MCPTransport.Stdio("command", L("a"));
        Assert.Single(first.Args);
        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.IsType<ValueList<string>>(first.Args);
        var left = new MCPTransport.Http("url", M(("a", "1"), ("b", "2")));
        var right = new MCPTransport.Http("url", M(("b", "2"), ("a", "1")));
        Assert.Equal(left, right);
        Assert.Equal(left.GetHashCode(), right.GetHashCode());
        Assert.IsType<ValueMap<string, string>>(left.Headers);
        Assert.NotEqual<MCPTransport>(first, new MCPTransport.Stdio("command", L("different")));
        Assert.NotEqual<MCPTransport>(new MCPTransport.Oauth("u"), new MCPTransport.Http("u", M()));
        Assert.Equal(new MCPTransport.Oauth("u"), new MCPTransport.Oauth("u"));
    }

    [Fact] // MCP.swift:79-90 the declared defaults of MCPCatalogEntry
    [Trait("Rule", "R-VAL-1")]
    public void Catalog_entry_defaults_are_the_declared_ones()
    {
        var entry = Entry(new MCPTransport.Oauth("https://o"));
        Assert.False(entry.IsMonochrome);   // :79
        Assert.Empty(entry.Fields);         // :81
        Assert.Empty(entry.Env);            // :83
        Assert.Empty(entry.SampleTools);    // :86
        Assert.Null(entry.KeysURL);         // :90
        Assert.Equal("docs", entry.DocsURL);
        Assert.Equal(3u, entry.ColorValue);
        Assert.IsType<ValueList<MCPField>>(entry.Fields);
        Assert.IsType<ValueMap<string, string>>(entry.Env);
        Assert.IsType<ValueList<string>>(entry.SampleTools);
    }

    [Fact] // MCP.swift:65 MCPCatalogEntry: Hashable
    [Trait("Rule", "R-VAL-2")]
    public void Catalog_entries_compare_collections_by_content()
    {
        var fields = new List<MCPField> { new(Key: "key", Label: "Key", Placeholder: "") };
        var first = Entry(new MCPTransport.Stdio("cmd", L("a"))) with { Fields = new ValueList<MCPField>(fields), SampleTools = L("tool") };
        fields.Clear();
        var second = Entry(new MCPTransport.Stdio("cmd", L("a"))) with { Fields = L(new MCPField(Key: "key", Label: "Key", Placeholder: "")), SampleTools = L("tool") };
        Assert.Single(first.Fields);
        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.NotEqual(first, second with { SampleTools = L("other") });
        Assert.NotEqual(first, second with { KeysURL = "changed" });
        Assert.NotEqual(first, second with { Env = M(("A", "1")) });
        Assert.NotEqual(first, second with { IsMonochrome = true });
    }

    [Fact] // MCP.swift:104-119 MCPResolvedServer and :188-199 MCPConnection: Hashable
    [Trait("Rule", "R-VAL-2")]
    public void Connection_and_resolved_server_compare_collection_contents()
    {
        var values = new Dictionary<string, string> { ["path"] = "one" };
        var first = new MCPConnection(CatalogID: "id") { Values = new ValueMap<string, string>(values, SwiftStringComparer.Instance), Tools = L(new MCPToolSummary(Name: "tool", Summary: null)) };
        values["path"] = "two";
        var second = new MCPConnection(CatalogID: "id") { Values = M(("path", "one")), Tools = L(new MCPToolSummary(Name: "tool", Summary: null)) };
        Assert.Equal("one", first.Values["path"]);
        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.NotEqual(first, second with { IsEnabled = false });
        Assert.IsType<ValueMap<string, string>>(first.Values);
        Assert.IsType<ValueList<MCPToolSummary>>(first.Tools);
        var serverA = new MCPResolvedServer(Id: "id", Name: "Name") { Args = L("a"), Env = M(("X", "x")) };
        var serverB = new MCPResolvedServer(Id: "id", Name: "Name") { Args = L("a"), Env = M(("X", "x")) };
        Assert.Equal(serverA, serverB);
        Assert.Equal(serverA.GetHashCode(), serverB.GetHashCode());
        Assert.NotEqual(serverA, serverB with { OauthUpstream = "changed" });
        Assert.NotEqual(serverA, serverB with { Headers = M(("H", "h")) });
        var empty = new MCPResolvedServer(Id: "id", Name: "Name");
        Assert.Null(empty.Command);
        Assert.Empty(empty.Args);
        Assert.Empty(empty.Env);
        Assert.Null(empty.Url);
        Assert.Empty(empty.Headers);
        Assert.Null(empty.OauthUpstream);
        Assert.IsType<ValueList<string>>(empty.Args);
        Assert.IsType<ValueMap<string, string>>(empty.Env);
        Assert.IsType<ValueMap<string, string>>(empty.Headers);
    }

    [Fact] // MCP.swift:180-184 MCPProbeResult: Hashable, three stored fields
    [Trait("Rule", "R-VAL-2")]
    public void Probe_result_compares_its_tools_by_content()
    {
        var a = new MCPProbeResult(ServerName: "srv", ServerVersion: "1.0", Tools: L(new MCPToolSummary(Name: "t", Summary: "s")));
        var b = new MCPProbeResult(ServerName: "srv", ServerVersion: "1.0", Tools: L(new MCPToolSummary(Name: "t", Summary: "s")));
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.NotEqual(a, b with { Tools = L<MCPToolSummary>() });
        Assert.NotEqual(a, b with { ServerName = null });
        Assert.IsType<ValueList<MCPToolSummary>>(a.Tools);
    }
}
