using System.Diagnostics;
using System.Text;
using Marrow.Core.Support;

namespace Marrow.Core.Tests.Support;

public sealed class StdioFramerTests
{
    private static byte[] Bytes(string value) => Encoding.UTF8.GetBytes(value);

    [Fact] // PR-PROVACP-149
    public void Lines_split_only_on_LF_preserving_CR_empty_messages_and_arbitrary_bytes()
    {
        var framer = new StdioFramer(StdioFramer.Framing.Lines);
        var messages = framer.Append([0x0A, 0xFF, 0x00, 0x0D, 0x0A, 0x0D, 0x0B, 0x0C]);
        Assert.Equal(2, messages.Count);
        Assert.Empty(messages[0]);
        Assert.Equal(new byte[] { 0xFF, 0x00, 0x0D }, messages[1]);
        Assert.Equal(new byte[] { 0x0D, 0x0B, 0x0C }, framer.Finish());
    }

    [Fact] // PR-PROVACP-149, PR-PROVACP-150
    public void Lines_survive_every_two_chunk_split_without_duplication()
    {
        var wire = Bytes("a\r\nb\n\nlast");
        for (var split = 0; split <= wire.Length; split++)
        {
            var framer = new StdioFramer(StdioFramer.Framing.Lines);
            var messages = framer.Append(wire.AsSpan(0, split))
                .Concat(framer.Append(wire.AsSpan(split))).ToArray();
            Assert.Equal(3, messages.Length);
            Assert.Equal(Bytes("a\r"), messages[0]);
            Assert.Equal(Bytes("b"), messages[1]);
            Assert.Empty(messages[2]);
            Assert.Empty(framer.Append([]));
            Assert.Equal(Bytes("last"), framer.Finish());
        }
    }

    [Theory] // PR-PROVACP-150: test budget is infrastructure, not a product timeout.
    [InlineData(StdioFramer.Framing.Lines)]
    [InlineData(StdioFramer.Framing.ContentLength)]
    public void One_MiB_byte_at_a_time_is_intact_and_does_not_take_quadratic_time(StdioFramer.Framing framing)
    {
        const int size = 1024 * 1024;
        var framer = new StdioFramer(framing);
        if (framing == StdioFramer.Framing.ContentLength)
            Assert.Empty(framer.Append(Bytes($"Content-Length: {size}\r\n\r\n")));
        byte[] oneByte = [0x61];
        var timer = Stopwatch.StartNew();
        var emitted = 0;
        byte[]? body = null;
        for (var i = 0; i < size; i++)
        {
            foreach (var message in framer.Append(oneByte))
            {
                emitted++;
                body = message;
                Assert.Equal(size - 1, i);
            }
        }
        if (framing == StdioFramer.Framing.Lines)
        {
            Assert.Equal(0, emitted);
            body = Assert.Single(framer.Append([0x0A]));
        }
        else Assert.Equal(1, emitted);
        timer.Stop();
        Assert.NotNull(body);
        Assert.Equal(Enumerable.Repeat((byte)0x61, size).ToArray(), body);
        Assert.Null(framer.Finish());
        Assert.True(timer.Elapsed < TimeSpan.FromSeconds(10), $"Byte-at-a-time feed took {timer.Elapsed}.");
    }

    [Fact] // PR-PROVACP-150, PR-PROVACP-151
    public void Content_length_survives_every_pair_of_chunk_boundaries_including_separator_overlap()
    {
        var wire = Bytes("Content-Length: 2\r\n\r\nABContent-Length: 0\r\n\r\nContent-Length: 3\r\n\r\nCDE");
        for (var first = 0; first <= wire.Length; first++)
        for (var second = first; second <= wire.Length; second++)
        {
            var framer = new StdioFramer(StdioFramer.Framing.ContentLength);
            var messages = framer.Append(wire.AsSpan(0, first))
                .Concat(framer.Append(wire.AsSpan(first, second - first)))
                .Concat(framer.Append(wire.AsSpan(second))).ToArray();
            Assert.Equal(3, messages.Length);
            Assert.Equal(Bytes("AB"), messages[0]);
            Assert.Empty(messages[1]);
            Assert.Equal(Bytes("CDE"), messages[2]);
            Assert.Null(framer.Finish());
        }
    }

    [Fact] // PR-PROVACP-151
    public void Body_length_counts_bytes_not_characters_and_preserves_nontext_bytes()
    {
        var framer = new StdioFramer(StdioFramer.Framing.ContentLength);
        Assert.Empty(framer.Append(Bytes("Content-Length: 7\r\n\r\n")));
        Assert.Empty(framer.Append([0xF0, 0x9F, 0x98]));
        Assert.Equal(new byte[] { 0xF0, 0x9F, 0x98, 0x80, 0xFF, 0x00, 0x0A },
            Assert.Single(framer.Append([0x80, 0xFF, 0x00, 0x0A])));
    }

    [Theory] // PR-PROVACP-151
    [InlineData("Content-Length: 1\n\nA")]
    [InlineData("Content-Length: 1\r\rA")]
    [InlineData("Content-Length: 1\r\n\nA")]
    public void Only_the_literal_CRLFCRLF_ends_a_header(string wire)
    {
        var framer = new StdioFramer(StdioFramer.Framing.ContentLength);
        Assert.Empty(framer.Append(Bytes(wire)));
        Assert.Null(framer.Finish());
    }

    [Theory] // PR-PROVACP-151, PR-PROVACP-152
    [InlineData("Ignored: yes")]
    [InlineData("Content-Length: -1")]
    [InlineData("Content-Length: garbage")]
    [InlineData("Content-Length: 9223372036854775808")]
    [InlineData("Content-Length: 1.0")]
    [InlineData("Content-Length: 1e0")]
    [InlineData("Content-Length: 0x1")]
    [InlineData("Content-Length: 1\0")]
    [InlineData("Content-Length: \u0661")]
    [InlineData("Content-Length: + 1")]
    [InlineData("Content-Length: 1:2")]
    [InlineData("Content-Length: bad\r\nContent-Length: 1")]
    [InlineData("Content-Length: \r\nContent-Length: 1")]
    public void Malformed_header_is_discarded_as_a_whole_then_next_header_is_read(string header)
    {
        var framer = new StdioFramer(StdioFramer.Framing.ContentLength);
        var messages = framer.Append(Bytes(header + "\r\n\r\nContent-Length: 1\r\n\r\nZ"));
        Assert.Equal(Bytes("Z"), Assert.Single(messages));
        Assert.Null(framer.Finish());
    }

    [Theory] // PR-PROVACP-152
    [InlineData("content-length:1")]
    [InlineData("CONTENT-LENGTH: 1")]
    [InlineData(" \tContent-Length \t: \t+1 \t")]
    [InlineData("\u00A0Content-Length\u2003: \u30001\u202F")]
    [InlineData("Content-Length: 0001")]
    [InlineData("Ignored: anything\r\nContent-Length: 1")]
    [InlineData("Ignored: anything\vContent-Length: 1")]
    [InlineData("Ignored: anything\fContent-Length: 1")]
    [InlineData("Ignored: anything\u0085Content-Length: 1")]
    [InlineData("Ignored: anything\u2028Content-Length: 1")]
    [InlineData("Ignored: anything\u2029Content-Length: 1")]
    [InlineData("Content-Length: 1\r\nContent-Length: invalid")]
    [InlineData(":Content-Length: 1")]
    [InlineData("::Content-Length: 1")]
    [InlineData("Content-Length:\r\nContent-Length: 1")]
    public void Swift_header_name_trimming_integer_and_first_matching_line_rules_are_preserved(string header)
    {
        var framer = new StdioFramer(StdioFramer.Framing.ContentLength);
        Assert.Equal(Bytes("Z"), Assert.Single(framer.Append(Bytes(header + "\r\n\r\nZ"))));
    }

    [Theory] // PR-PROVACP-152
    [InlineData("0")]
    [InlineData("+0")]
    [InlineData("-0")]
    public void Zero_length_is_a_complete_empty_message(string length)
    {
        var framer = new StdioFramer(StdioFramer.Framing.ContentLength);
        Assert.Empty(Assert.Single(framer.Append(Bytes($"Content-Length: {length}\r\n\r\n"))));
        Assert.Null(framer.Finish());
    }

    [Theory] // PR-PROVACP-152
    [InlineData("2147483648")]
    [InlineData("9223372036854775807")]
    public void A_valid_64_bit_length_waits_for_its_body_instead_of_being_rejected_or_allocated_up_front(string length)
    {
        var framer = new StdioFramer(StdioFramer.Framing.ContentLength);
        Assert.Empty(framer.Append(Bytes($"Content-Length: {length}\r\n\r\nContent-Length: 1\r\n\r\nZ")));
        Assert.Null(framer.Finish());
    }

    [Fact] // PR-PROVACP-151
    public void Invalid_header_does_not_consume_a_body_and_garbage_becomes_part_of_the_next_header()
    {
        var framer = new StdioFramer(StdioFramer.Framing.ContentLength);
        var wire = "Content-Length: bad\r\n\r\nXContent-Length: 1\r\n\r\nContent-Length: 1\r\n\r\nZ";
        Assert.Equal(Bytes("Z"), Assert.Single(framer.Append(Bytes(wire))));
    }

    [Fact] // PR-PROVACP-153
    public void Finish_flushes_only_nonempty_line_remainder_and_resets_scanning()
    {
        var framer = new StdioFramer(StdioFramer.Framing.Lines);
        Assert.Null(framer.Finish());
        Assert.Empty(framer.Append(Bytes("old\r")));
        Assert.Equal(Bytes("old\r"), framer.Finish());
        Assert.Null(framer.Finish());
        Assert.Equal(Bytes("new"), Assert.Single(framer.Append(Bytes("new\n"))));
        Assert.Null(framer.Finish());
    }

    [Theory] // PR-PROVACP-153
    [InlineData("Content-Len")]
    [InlineData("Content-Length: 8\r\n\r\nshort")]
    public void Finish_discards_partial_header_or_body_and_resets_all_content_length_state(string partial)
    {
        var framer = new StdioFramer(StdioFramer.Framing.ContentLength);
        Assert.Empty(framer.Append(Bytes(partial)));
        Assert.Null(framer.Finish());
        Assert.Null(framer.Finish());
        Assert.Equal(Bytes("Z"), Assert.Single(framer.Append(Bytes("Content-Length: 1\r\n\r\nZ"))));
    }

    [Fact] // PR-PROVACP-149, PR-PROVACP-150, PR-PROVACP-153
    public void Input_and_returned_messages_do_not_alias_the_mutable_remainder()
    {
        var framer = new StdioFramer(StdioFramer.Framing.Lines);
        var input = Bytes("first\nlast");
        var first = Assert.Single(framer.Append(input));
        Array.Fill(input, (byte)0);
        Assert.Equal(Bytes("last"), Assert.Single(framer.Append([0x0A])));
        Assert.Equal(Bytes("first"), first);
        Assert.Empty(framer.Append(Bytes("tail")));
        var tail = framer.Finish();
        framer.Append(Bytes("replacement\n"));
        Assert.Equal(Bytes("tail"), tail);
    }

    [Fact] // PR-PROVACP-150, PR-PROVACP-151
    public void One_MiB_header_byte_at_a_time_does_not_rescan_its_entire_prefix()
    {
        var framer = new StdioFramer(StdioFramer.Framing.ContentLength);
        byte[] oneByte = [0x61];
        var timer = Stopwatch.StartNew();
        for (var i = 0; i < 1024 * 1024; i++)
            Assert.Empty(framer.Append(oneByte));
        Assert.Equal(Bytes("Z"), Assert.Single(framer.Append(Bytes("\r\nContent-Length: 1\r\n\r\nZ"))));
        timer.Stop();
        Assert.True(timer.Elapsed < TimeSpan.FromSeconds(10), $"Byte-at-a-time header took {timer.Elapsed}.");
    }

    [Fact] // PR-PROVACP-152: Swift's separator is a Character, not a UTF-16 code unit.
    public void A_colon_with_a_combining_mark_is_not_a_colon_separator()
    {
        var framer = new StdioFramer(StdioFramer.Framing.ContentLength);
        Assert.Equal(Bytes("Z"), Assert.Single(framer.Append(
            Bytes("Content-Length:\u0301bad\r\nContent-Length: 1\r\n\r\nZ"))));
    }
}
