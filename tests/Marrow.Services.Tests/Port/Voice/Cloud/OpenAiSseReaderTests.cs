using System.Text;
using Marrow.Services.Port.Voice.Cloud;

namespace Marrow.Services.Tests.Port.Voice.Cloud;

// VT-16, voice-SPEC V-18.1: the events of the streaming reply.
[Trait("Spec", "V-T111")]
public sealed class OpenAiSseReaderTests
{
    private static async Task<List<OpenAiEvent>> Read(string text)
    {
        var events = new List<OpenAiEvent>();
        await foreach (var e in OpenAiSseReader.Read(new MemoryStream(Encoding.UTF8.GetBytes(text)), CancellationToken.None)) events.Add(e);
        return events;
    }

    [Fact]
    [Trait("Feature", "VT-16")]
    [Trait("Rule", "R-TEST-4")]
    public async Task V_T111_deltas_and_the_done_event_are_read_in_order_with_either_line_ending()
    {
        var lf = CloudFixtures.Sse(CloudFixtures.Delta("Hel"), CloudFixtures.Delta("lo"), CloudFixtures.Done("Hello"));
        var expected = new[] { new OpenAiEvent(false, "Hel"), new OpenAiEvent(false, "lo"), new OpenAiEvent(true, "Hello") };
        Assert.Equal(expected, await Read(lf));
        Assert.Equal(expected, await Read(lf.Replace("\n", "\r\n", StringComparison.Ordinal)));
    }

    [Fact]
    [Trait("Feature", "VT-16")]
    [Trait("Rule", "R-TEST-4")]
    public async Task V_T111_other_events_comments_and_bad_data_are_skipped_and_text_keeps_its_unicode_and_spaces()
    {
        var text = ": keep-alive\n\n"
            + "event: other\ndata: {\"type\":\"transcript.text.segment\",\"text\":\"x\"}\n\n"
            + "data:not json\n\n"
            + "data:{\"type\":\"transcript.text.delta\",\"delta\":\" Gr\\u00fc\\u00dfe \"}\n\n"
            + "data: {\"type\":\"transcript.text.delta\"}\n\n"
            + "data: {\"type\":\"transcript.text.done\",\"text\":\"Gr\\u00fc\\u00dfe\"}";   // no blank line at the end of the stream
        Assert.Equal([new OpenAiEvent(false, " Grüße "), new OpenAiEvent(true, "Grüße")], await Read(text));
    }
}
