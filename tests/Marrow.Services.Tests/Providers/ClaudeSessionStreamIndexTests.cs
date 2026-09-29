using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Providers;

namespace Marrow.Services.Tests.Providers;

// Audit fix (svc): ClaudeSession.swift:457-478. Swift Int is 64-bit, so a content_block_start index of 2147483648 is a valid
// blockIDs key and builds the block id "<message>-2147483648"; a 32-bit narrowing would trap the read loop instead.
public sealed class ClaudeSessionStreamIndexTests
{
    [Trait("Req", "PR-PROVCLI-050")] // ClaudeSession.swift:457-478
    [Fact]
    public void A_stream_index_beyond_thirty_two_bits_opens_and_extends_its_block()
    {
        var events = new List<ProviderEvent>();
        var session = new ClaudeSession(new SessionConfiguration
        {
            Provider = ProviderKind.Claude,
            WorkingDirectory = SwiftURL.Create("file:///" + Path.GetTempPath().Replace('/', '\\').Replace('\\', '/'))!,
            Environment = ValueMap<string, string>.Empty,
            RuntimeMode = RuntimeMode.Supervised,
            InteractionMode = InteractionMode.Build,
        });
        var stream = new ClaudeSession.StreamState();
        var index = 2147483648L;

        session.HandleStreamEvent(JSONValue.FromDictionaryLiteral(("type", "message_start"), ("message", JSONValue.FromDictionaryLiteral(("id", "m1")))), ref stream, events.Add);
        session.HandleStreamEvent(JSONValue.FromDictionaryLiteral(("type", "content_block_start"), ("index", index),
            ("content_block", JSONValue.FromDictionaryLiteral(("type", "text"), ("text", "he")))), ref stream, events.Add);
        session.HandleStreamEvent(JSONValue.FromDictionaryLiteral(("type", "content_block_delta"), ("index", index),
            ("delta", JSONValue.FromDictionaryLiteral(("type", "text_delta"), ("text", "llo")))), ref stream, events.Add);

        Assert.Equal(2, events.Count);
        Assert.Equal(new ProviderEvent.MessageDelta("m1-2147483648", "he"), events[0]);
        Assert.Equal(new ProviderEvent.MessageDelta("m1-2147483648", "llo"), events[1]);
    }
}
