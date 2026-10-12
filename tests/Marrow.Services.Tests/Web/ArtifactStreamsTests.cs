using Marrow.Core.Models;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;
using Marrow.Services.Web;
using Marrow.Testing;

namespace Marrow.Services.Tests.Web;

// PORT-ONLY: web surfaces W14, Tabula's streaming render (DESIGN.md 6.4 "The pane opens the moment artifact_create starts
// streaming ... Code shows the text arriving"; 12 "every 250 ms"). The artifacts being written: a fenced block from the
// moment its opener arrives (with its id, title and kind) until it closes or its turn ends, its text read at most four
// times a second; and an artifact_create tool call, by whichever name its provider gives it.
[Trait("Decision", "ED-NEW-web-w14-1")]
public sealed class ArtifactStreamsTests
{
    private static readonly Guid Chat = Guid.Parse("4c1d9a7e-0b3f-4e21-9d8c-7a6b5c4d3e21");

    [Fact]
    public void A_stream_begins_ends_and_reads_its_text_at_most_every_250_ms()
    {
        var time = new ManualTimeProvider();
        var streams = new ArtifactStreams(time);
        var changed = new List<ArtifactStream>();
        var ended = new List<ArtifactStream>();
        streams.Changed += changed.Add;
        streams.Ended += ended.Add;
        var reads = 0;
        string Content() { reads++; return "text " + reads; }

        streams.Begin(Chat, "k", "pricing", "Pricing", ArtifactKind.Html);
        streams.Update(Chat, "k", Content);
        streams.Update(Chat, "k", Content);
        Assert.Equal(1, reads);
        time.Advance(TimeSpan.FromMilliseconds(249));
        streams.Update(Chat, "k", Content);
        Assert.Equal(1, reads);
        time.Advance(TimeSpan.FromMilliseconds(1));
        streams.Update(Chat, "k", Content);
        Assert.Equal(2, reads);
        streams.Update(Chat, "k", Content, force: true);
        Assert.Equal(3, reads);
        var stream = Assert.Single(streams.Of(Chat));
        Assert.Equal(("pricing", "Pricing", ArtifactKind.Html, "text 3", false), (stream.Id, stream.Title, stream.Kind, stream.Content, stream.IsUpdate));
        Assert.Equal(4, changed.Count);

        // A second begin only adds what it knows.
        streams.Begin(Chat, "k", null, null, null, isUpdate: true);
        Assert.Equal(("pricing", "Pricing", true), (streams.Of(Chat)[0].Id, streams.Of(Chat)[0].Title, streams.Of(Chat)[0].IsUpdate));

        streams.End(Chat, "k");
        streams.End(Chat, "k");
        Assert.Single(ended);
        Assert.Empty(streams.Of(Chat));
        streams.Update(Chat, "k", Content, force: true);
        Assert.Equal(3, reads);
    }

    [Fact]
    public void Ending_all_of_a_chat_ends_only_its_streams_and_only_those_chosen()
    {
        var streams = new ArtifactStreams(new ManualTimeProvider());
        var other = Guid.NewGuid();
        streams.Begin(Chat, "tool:1", null, null, null);
        streams.Begin(Chat, "fence:m1:0", "a", null, null);
        streams.Begin(other, "tool:2", null, null, null);
        var ended = new List<string>();
        streams.Ended += s => ended.Add(s.Key);
        streams.EndAll(Chat, s => s.Key.StartsWith("tool:", StringComparison.Ordinal));
        Assert.Equal(["tool:1"], ended);
        streams.EndAll(Chat);
        Assert.Equal(["tool:1", "fence:m1:0"], ended);
        Assert.Single(streams.Of(other));
    }

    [Theory]
    [InlineData("nth-ui · artifact_create", null, true)]
    [InlineData("mcp__nth-ui__artifact_create", null, true)]
    [InlineData("Call tool", "nth-ui artifact_create", true)]
    [InlineData("nth-ui · artifact_update", null, false)]
    [InlineData("artifact_create", null, false)]
    [InlineData(null, null, false)]
    public void A_create_call_is_known_by_any_provider_s_name_for_it(string? title, string? detail, bool expected) =>
        Assert.Equal(expected, ArtifactStreams.IsCreateCall(title, detail));

    [Fact]
    public void A_fenced_block_streams_from_its_opener_with_its_id_title_and_kind_and_ends_when_it_closes()
    {
        using var scope = new ArtifactHostScope();
        var filter = new ArtifactFenceFilter(Chat, scope.Host, () => (new ArtifactAuthor("claude", "sonnet", null), "Make it"));
        var began = new List<ArtifactStream>();
        var ended = new List<ArtifactStream>();
        scope.Host.Streams.Changed += began.Add;
        scope.Host.Streams.Ended += ended.Add;
        filter.Filter(new ProviderEvent.MessageDelta("m1", "Sure:\n```nth-artifact id=pricing kind=html title=\"Pricing calculator\"\n<h3>Orbit"));
        var open = Assert.Single(scope.Host.Streams.Of(Chat));
        Assert.Equal(("pricing", "Pricing calculator", ArtifactKind.Html, false), (open.Id, open.Title, open.Kind, open.IsUpdate));
        Assert.Equal("<h3>Orbit", open.Content); // the partial line shows as it arrives
        Assert.Empty(ended);

        filter.Filter(new ProviderEvent.MessageDelta("m1", " pricing</h3>\n```\nDone."));
        Assert.Empty(scope.Host.Streams.Of(Chat));
        Assert.Equal("pricing", Assert.Single(ended).Id);
        Assert.Equal(1, scope.Store.Read(Chat, "pricing").Version.Number);

        // A block naming the id it just saved is a new version of it: an update stream.
        filter.Filter(new ProviderEvent.MessageDelta("m2", "```nth-artifact id=pricing\n<h3>"));
        Assert.True(Assert.Single(scope.Host.Streams.Of(Chat)).IsUpdate);
        filter.Filter(new ProviderEvent.Exited(null));
        Assert.Empty(scope.Host.Streams.Of(Chat));
    }
}
