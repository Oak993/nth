using Marrow.Core.Models;

namespace Marrow.Core.Tests.Models;

public sealed class MCPValueSemanticsTests
{
    [Fact] // MCP.swift Hashable/Sendable collections have value semantics.
    public void Transport_equality_and_hashing_compare_contents_and_own_input_arrays()
    {
        var args = new List<string> { "a" };
        var first = new MCPTransport.Stdio("command", args);
        args.Add("b");
        var second = new MCPTransport.Stdio("command", ["a"]);
        Assert.Single(first.Args); Assert.Equal(first, second); Assert.Equal(first.GetHashCode(), second.GetHashCode());
        var left = new MCPTransport.Http("url", new Dictionary<string,string> { ["a"] = "1", ["b"] = "2" });
        var right = new MCPTransport.Http("url", new Dictionary<string,string> { ["b"] = "2", ["a"] = "1" });
        Assert.Equal(left, right); Assert.Equal(left.GetHashCode(), right.GetHashCode());
        Assert.NotEqual(first, new MCPTransport.Stdio("command", ["different"]));
    }

    [Fact]
    public void Catalog_entries_copy_collections_and_compare_transport_fields_and_tools()
    {
        var fields = new List<MCPField> { new("key", "Key", "") };
        var first = new MCPCatalogEntry("id", "Name", "Vendor", "Summary", MCPCategory.Work, "asset", 3, new MCPTransport.Stdio("cmd", ["a"]), "docs") { Fields = fields, SampleTools = ["tool"] };
        fields.Clear();
        var second = new MCPCatalogEntry("id", "Name", "Vendor", "Summary", MCPCategory.Work, "asset", 3, new MCPTransport.Stdio("cmd", ["a"]), "docs") { Fields = [new("key", "Key", "")], SampleTools = ["tool"] };
        Assert.Single(first.Fields); Assert.Equal(first, second); Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.NotEqual(first, second with { SampleTools = ["other"] });
        Assert.NotEqual(first, second with { KeysUrl = "changed" });
    }

    [Fact]
    public void Connection_and_resolved_server_compare_collection_contents()
    {
        var values = new Dictionary<string,string> { ["path"] = "one" };
        var first = new MCPConnection("id") { Values = values, Tools = [new("tool", null)] };
        values["path"] = "two";
        var second = new MCPConnection("id") { Values = new Dictionary<string,string> { ["path"] = "one" }, Tools = [new("tool", null)] };
        Assert.Equal("one", first.Values["path"]); Assert.Equal(first, second); Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.NotEqual(first, second with { IsEnabled = false });
        var serverA = new MCPResolvedServer("id", "Name") { Args = ["a"], Env = new Dictionary<string,string> { ["X"] = "x" } };
        var serverB = new MCPResolvedServer("id", "Name") { Args = ["a"], Env = new Dictionary<string,string> { ["X"] = "x" } };
        Assert.Equal(serverA, serverB); Assert.Equal(serverA.GetHashCode(), serverB.GetHashCode());
        Assert.NotEqual(serverA, serverB with { OAuthUpstream = "changed" });
    }
}
