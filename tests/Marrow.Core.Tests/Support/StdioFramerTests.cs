using System.Diagnostics;
using System.Reflection;
using System.Text;
using Marrow.Core.Support;

namespace Marrow.Core.Tests.Support;

// Expected values are transcribed from DroppyCode/Core/Support/StdioFramer.swift (Droppy Code 1.6.2, 1fbf81e)
// and the PR-PROVACP-149..153 records, never from the C# implementation.
public sealed class StdioFramerTests
{
    private static byte[] Bytes(string value) => Encoding.UTF8.GetBytes(value);

    // Swift: `private var scanned = 0` (StdioFramer.swift:11). PR-PROVACP-150 specifies this state ("records how
    // much of it has been searched"), and it is the only deterministic witness of the incremental scan, so it is
    // read by its Swift name (R-NAME-2 casing or the private-field spelling).
    private static long Scanned(StdioFramer framer)
    {
        var field = typeof(StdioFramer).GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
            .Single(f => f.Name.TrimStart('_') is "scanned" or "Scanned");
        return field.GetValue(framer) switch
        {
            int value => value,
            long value => value,
            var other => throw new InvalidOperationException($"scanned has an unexpected type: {other?.GetType()}"),
        };
    }

    // Byte storage the framer retains (ED-147). Swift keeps `private var buffer = Data()` (StdioFramer.swift:10),
    // replaces it at :59 with `Data(buffer[start...])`, storage sized to the unconsumed slice, and at :65 with Data().
    // Every non-public instance field must have one of the listed types, so no other store can hide bytes; the
    // result is the total Length of the byte[] fields, one of which must be named buffer.
    private static long RetainedBytes(StdioFramer framer)
    {
        var fields = typeof(StdioFramer).GetFields(BindingFlags.NonPublic | BindingFlags.Instance);
        Type[] allowed = [typeof(byte[]), typeof(int), typeof(long), typeof(int?), typeof(long?), typeof(StdioFramer.Framing)];
        foreach (var field in fields)
        {
            if (!allowed.Contains(field.FieldType))
                throw new InvalidOperationException($"Field {field.Name} has type {field.FieldType}, which is not an allowed framer field type.");
        }
        var bufferFields = fields.Count(f => f.FieldType == typeof(byte[]) && f.Name.TrimStart('_') is "buffer" or "Buffer");
        if (bufferFields != 1)
            throw new InvalidOperationException($"Expected one byte[] field named buffer, found {bufferFields}.");
        long total = 0;
        foreach (var field in fields)
        {
            if (field.FieldType == typeof(byte[])) total += ((byte[]?)field.GetValue(framer))?.Length ?? 0;
        }
        return total;
    }

    // ED-147. Every expected value, derived from StdioFramer.swift:
    // Lines, each append of A = 1,048,576 x 0x61, 0x0A, "xyz" (the same array every time):
    //   :20 buffer = remainder + A. :23 search = start + scanned. :27 finds the 0x0A after the 0x61 run; :31 emits
    //   buffer[start..<newline] (1 MiB of 'a' the first time, then "xyz" + 1 MiB of 'a'); :32-33 start = search =
    //   newline + 1. :27-28 finds no further 0x0A, so search = endIndex. :58 scanned = endIndex - start = 3 ("xyz").
    //   :59 buffer = Data(buffer[start...]), storage sized to the 3-byte slice, so at most 3 bytes are retained.
    //   The next append resumes the search at :23 start + 3, past "xyz", so it emits exactly one message again.
    //   :64 finish() returns "xyz" (line framing, non-empty buffer) and :65 buffer = Data() retains nothing.
    // ContentLength:
    //   Append("Content-Le"), 10 bytes: :36 bodyLength is nil; :37 finds no separator, so :38 search =
    //   max(0, 10 - 3) = 7; :58 scanned = 7 - 0 = 7; :59 start == startIndex, nothing consumed and no reallocation,
    //   so no storage assertion is made here.
    //   Each append of "ngth: 1048576\r\n\r\n" + 1 MiB of 'a' + "Content-Le": :23 search = 0 + 7; :37 finds the
    //   separator after "Content-Length: 1048576"; :41-45 the header parses to 1048576 and start moves past the
    //   separator; :47-53 the body is complete, is emitted, and start = its end; :36-38 "Content-Le" holds no
    //   separator, so search = max(start, endIndex - 3) = start + 7; :58 scanned = 7; :59 buffer = the 10-byte
    //   slice "Content-Le", so at most 10 bytes are retained.
    //   Append("ngth: 1\r\n\r\nZ"): :37-45 header "Content-Length: 1", length 1; :47-53 emits "Z", start = endIndex;
    //   :36-38 no separator, search = max(start, endIndex - 3) = start; :58 scanned = 0; :59 buffer =
    //   Data(buffer[endIndex...]), empty, so nothing is retained.
    [Theory]
    [Trait("Req", "PR-PROVACP-150")] // StdioFramer.swift:10, :58-59, :65
    [InlineData(StdioFramer.Framing.Lines)]
    [InlineData(StdioFramer.Framing.ContentLength)]
    public void Each_consuming_append_keeps_only_the_unconsumed_remainder_as_storage(StdioFramer.Framing framing)
    {
        const int mib = 1024 * 1024;
        var framer = new StdioFramer(framing);
        if (framing == StdioFramer.Framing.Lines)
        {
            var chunk = new byte[mib + 4];
            Array.Fill(chunk, (byte)0x61, 0, mib);
            chunk[mib] = 0x0A;
            Bytes("xyz").CopyTo(chunk, mib + 1);
            for (var i = 0; i < 64; i++)
            {
                var message = Assert.Single(framer.Append(chunk));
                var prefix = i == 0 ? 0 : 3;
                Assert.Equal(prefix + mib, message.Length);
                if (i != 0) Assert.Equal(Bytes("xyz"), message[..3]);
                Assert.True(message.AsSpan(prefix).IndexOfAnyExcept((byte)0x61) < 0, $"Append {i}: the message is not 'a' after its prefix.");
                var retained = RetainedBytes(framer);
                Assert.True(retained <= 3, $"Append {i}: {retained} bytes retained.");
                Assert.Equal(3, Scanned(framer));
            }
            Assert.Equal(Bytes("xyz"), framer.Finish());
            Assert.Equal(0, RetainedBytes(framer));
        }
        else
        {
            Assert.Empty(framer.Append(Bytes("Content-Le")));
            Assert.Equal(7, Scanned(framer));
            var head = Bytes("ngth: 1048576\r\n\r\n");
            var tail = Bytes("Content-Le");
            var chunk = new byte[head.Length + mib + tail.Length];
            head.CopyTo(chunk, 0);
            Array.Fill(chunk, (byte)0x61, head.Length, mib);
            tail.CopyTo(chunk, head.Length + mib);
            for (var i = 0; i < 64; i++)
            {
                var message = Assert.Single(framer.Append(chunk));
                Assert.Equal(mib, message.Length);
                Assert.True(message.AsSpan().IndexOfAnyExcept((byte)0x61) < 0, $"Append {i}: the body is not all 'a'.");
                var retained = RetainedBytes(framer);
                Assert.True(retained <= 10, $"Append {i}: {retained} bytes retained.");
                Assert.Equal(7, Scanned(framer));
            }
            Assert.Equal(Bytes("Z"), Assert.Single(framer.Append(Bytes("ngth: 1\r\n\r\nZ"))));
            Assert.Equal(0, RetainedBytes(framer));
            Assert.Equal(0, Scanned(framer));
        }
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-149")] // StdioFramer.swift:26-34, :64
    public void Lines_split_only_on_LF_preserving_CR_empty_messages_and_arbitrary_bytes()
    {
        var framer = new StdioFramer(StdioFramer.Framing.Lines);
        var messages = framer.Append([0x0A, 0xFF, 0x00, 0x0D, 0x0A, 0x0D, 0x0B, 0x0C]);
        Assert.Equal(2, messages.Count);
        Assert.Empty(messages[0]);
        Assert.Equal(new byte[] { 0xFF, 0x00, 0x0D }, messages[1]);
        Assert.Equal(new byte[] { 0x0D, 0x0B, 0x0C }, framer.Finish());
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-149")] // StdioFramer.swift:27-33
    public void Lines_from_a_CRLF_writer_keep_the_CR_and_lines_from_an_LF_writer_do_not_gain_one()
    {
        var crlf = new StdioFramer(StdioFramer.Framing.Lines);
        var messages = crlf.Append(Bytes("{\"id\":1}\r\n{\"id\":2}\r\n"));
        Assert.Equal(2, messages.Count);
        Assert.Equal(Bytes("{\"id\":1}\r"), messages[0]);
        Assert.Equal(Bytes("{\"id\":2}\r"), messages[1]);
        Assert.Null(crlf.Finish());

        var lf = new StdioFramer(StdioFramer.Framing.Lines);
        messages = lf.Append(Bytes("{\"id\":1}\n{\"id\":2}\n"));
        Assert.Equal(2, messages.Count);
        Assert.Equal(Bytes("{\"id\":1}"), messages[0]);
        Assert.Equal(Bytes("{\"id\":2}"), messages[1]);
        Assert.Null(lf.Finish());
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-149")] // StdioFramer.swift:26-34
    [Trait("Req", "PR-PROVACP-150")] // StdioFramer.swift:19-23, :58-59
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

    [Theory]
    [Trait("Req", "PR-PROVACP-150")] // StdioFramer.swift:19-23, :27-28, :36-39, :47-53, :58-59
    [InlineData(StdioFramer.Framing.Lines)]
    [InlineData(StdioFramer.Framing.ContentLength)]
    public void One_MiB_byte_at_a_time_is_delivered_exactly_once_and_intact(StdioFramer.Framing framing)
    {
        const int size = 1024 * 1024;
        var framer = new StdioFramer(framing);
        if (framing == StdioFramer.Framing.ContentLength)
            Assert.Empty(framer.Append(Bytes("Content-Length: 1048576\r\n\r\n")));
        byte[] oneByte = [0x61];
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
        Assert.NotNull(body);
        Assert.Equal(Enumerable.Repeat((byte)0x61, size).ToArray(), body);
        Assert.Null(framer.Finish());
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-150")] // StdioFramer.swift:23, :27-28, :31-33, :58, :66
    public void Lines_scanned_offset_is_the_already_searched_length_of_the_remainder()
    {
        var framer = new StdioFramer(StdioFramer.Framing.Lines);
        Assert.Equal(0, Scanned(framer));
        // "abc": no LF, search = endIndex = 3, start = 0.
        Assert.Empty(framer.Append(Bytes("abc")));
        Assert.Equal(3, Scanned(framer));
        // "abcde\nfg": search resumes at 3, LF at 5, start = 6, no LF in "fg" so search = 8; 8 - 6 = 2.
        Assert.Equal(Bytes("abcde"), Assert.Single(framer.Append(Bytes("de\nfg"))));
        Assert.Equal(2, Scanned(framer));
        // "fg\n": LF at 2, start = search = 3 = endIndex.
        Assert.Equal(Bytes("fg"), Assert.Single(framer.Append(Bytes("\n"))));
        Assert.Equal(0, Scanned(framer));
        Assert.Empty(framer.Append(Bytes("xyz")));
        Assert.Equal(3, Scanned(framer));
        Assert.Equal(Bytes("xyz"), framer.Finish());
        Assert.Equal(0, Scanned(framer));
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-150")] // StdioFramer.swift:23, :37-39, :42-43, :51-52, :58
    [Trait("Req", "PR-PROVACP-151")] // StdioFramer.swift:38
    public void Content_length_scanned_offset_backs_off_three_bytes_but_never_before_start()
    {
        var framer = new StdioFramer(StdioFramer.Framing.ContentLength);
        // 2 bytes: max(start 0, endIndex 2 - 3) = 0.
        Assert.Empty(framer.Append(Bytes("Co")));
        Assert.Equal(0, Scanned(framer));
        // 11 bytes "Content-Len": max(0, 11 - 3) = 8.
        Assert.Empty(framer.Append(Bytes("ntent-Len")));
        Assert.Equal(8, Scanned(framer));
        // 20 bytes ending "\r\n\r": no separator yet, max(0, 20 - 3) = 17.
        Assert.Empty(framer.Append(Bytes("gth: 1\r\n\r")));
        Assert.Equal(17, Scanned(framer));
        // The separator straddling the chunks is found; after the body start = 22 = endIndex, max(22, 19) = 22.
        Assert.Equal(Bytes("Z"), Assert.Single(framer.Append(Bytes("\nZ"))));
        Assert.Equal(0, Scanned(framer));
        // Surplus "tail" after the body: start = 22, endIndex = 26, max(22, 23) = 23; 23 - 22 = 1.
        Assert.Equal(Bytes("Y"), Assert.Single(framer.Append(Bytes("Content-Length: 1\r\n\r\nYtail"))));
        Assert.Equal(1, Scanned(framer));
        Assert.Null(framer.Finish());
        Assert.Equal(0, Scanned(framer));
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-150")] // StdioFramer.swift:42-43, :47, :56-58
    [Trait("Req", "PR-PROVACP-151")] // StdioFramer.swift:47-53
    public void A_pending_body_records_nothing_searched_and_is_emitted_when_complete()
    {
        var framer = new StdioFramer(StdioFramer.Framing.ContentLength);
        // Header ends at 21, bodyLength = 5, only 2 bytes buffered: search = start = 21.
        Assert.Empty(framer.Append(Bytes("Content-Length: 5\r\n\r\nab")));
        Assert.Equal(0, Scanned(framer));
        Assert.Empty(framer.Append(Bytes("cd")));
        Assert.Equal(0, Scanned(framer));
        Assert.Equal(Bytes("abcde"), Assert.Single(framer.Append(Bytes("e"))));
        Assert.Equal(0, Scanned(framer));
        Assert.Null(framer.Finish());
    }

    // ED-148: this test is the one sanctioned R-TEST-5 exception (it keeps its Stopwatch); no other test uses one.
    // Performance guard named by the record's verification_method. The budget is relative to a baseline measured
    // in the same run (a run of complete lines, whose buffer is emptied on every append), not a product timeout.
    // A framer that rescans its whole remainder on every append (search from 0 or from start, instead of
    // start + scanned) searches 16 MiB * 2 Mi calls / 2 = 1.76e13 bytes here and blows the budget many times over;
    // the incremental scan searches 8 (or 8 + 3) bytes per call.
    [Theory]
    [Trait("Req", "PR-PROVACP-150")] // StdioFramer.swift:23, :27-28, :37-38, :58
    [Trait("Decision", "ED-148")]
    [InlineData(StdioFramer.Framing.Lines)]
    [InlineData(StdioFramer.Framing.ContentLength)]
    public void Sixteen_MiB_unterminated_in_8_byte_chunks_costs_about_as_much_as_complete_lines(StdioFramer.Framing framing)
    {
        const int calls = 2 * 1024 * 1024;
        var line = Bytes("aaaaaaa\n");
        var partial = Bytes("aaaaaaaa");

        var baseline = TimeSpan.MaxValue;
        for (var run = 0; run < 2; run++)
        {
            var reference = new StdioFramer(StdioFramer.Framing.Lines);
            var emitted = 0;
            var baselineTimer = Stopwatch.StartNew();
            for (var i = 0; i < calls; i++) emitted += reference.Append(line).Count;
            baselineTimer.Stop();
            Assert.Equal(calls, emitted);
            if (baselineTimer.Elapsed < baseline) baseline = baselineTimer.Elapsed;
        }
        var budget = baseline * 10 + TimeSpan.FromSeconds(2);

        var framer = new StdioFramer(framing);
        var timer = Stopwatch.StartNew();
        for (var i = 0; i < calls; i++)
        {
            if (framer.Append(partial).Count != 0) Assert.Fail($"A message was emitted at append {i}.");
            if ((i & 0x3FF) == 0 && timer.Elapsed > budget)
                Assert.Fail($"Append {i} of {calls}: {timer.Elapsed} exceeds the budget {budget} (baseline {baseline}); the remainder is being rescanned.");
        }
        timer.Stop();
        Assert.True(timer.Elapsed <= budget, $"Unterminated feed took {timer.Elapsed}, budget {budget} (baseline {baseline}).");

        if (framing == StdioFramer.Framing.Lines)
        {
            Assert.Equal(8L * calls, Scanned(framer));
            var message = Assert.Single(framer.Append([0x0A]));
            Assert.Equal(8 * calls, message.Length);
            Assert.True(message.All(b => b == 0x61));
        }
        else
        {
            Assert.Equal(8L * calls - 3, Scanned(framer));
            Assert.Null(framer.Finish());
            Assert.Equal(Bytes("Z"), Assert.Single(framer.Append(Bytes("Content-Length: 1\r\n\r\nZ"))));
        }
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-150")] // StdioFramer.swift:23, :37-39, :58
    [Trait("Req", "PR-PROVACP-151")] // StdioFramer.swift:37-44
    public void One_MiB_header_line_byte_at_a_time_is_read_once_its_separator_arrives()
    {
        var framer = new StdioFramer(StdioFramer.Framing.ContentLength);
        byte[] oneByte = [0x61];
        for (var i = 0; i < 1024 * 1024; i++)
            Assert.Empty(framer.Append(oneByte));
        Assert.Equal(1024 * 1024 - 3, Scanned(framer));
        Assert.Equal(Bytes("Z"), Assert.Single(framer.Append(Bytes("\r\nContent-Length: 1\r\n\r\nZ"))));
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-150")] // StdioFramer.swift:19-23, :58-59
    [Trait("Req", "PR-PROVACP-151")] // StdioFramer.swift:13, :37-38
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

    [Fact]
    [Trait("Req", "PR-PROVACP-151")] // StdioFramer.swift:47-53
    public void Surplus_after_a_body_stays_buffered_for_the_next_message()
    {
        var framer = new StdioFramer(StdioFramer.Framing.ContentLength);
        Assert.Equal(Bytes("Z"), Assert.Single(framer.Append(Bytes("Content-Length: 1\r\n\r\nZContent-Length: 2\r\n\r\nA"))));
        Assert.Equal(Bytes("AB"), Assert.Single(framer.Append(Bytes("B"))));
        Assert.Null(framer.Finish());
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-151")] // StdioFramer.swift:47-49
    public void Body_length_counts_bytes_not_characters_and_preserves_nontext_bytes()
    {
        var framer = new StdioFramer(StdioFramer.Framing.ContentLength);
        Assert.Empty(framer.Append(Bytes("Content-Length: 7\r\n\r\n")));
        Assert.Empty(framer.Append([0xF0, 0x9F, 0x98]));
        Assert.Equal(new byte[] { 0xF0, 0x9F, 0x98, 0x80, 0xFF, 0x00, 0x0A },
            Assert.Single(framer.Append([0x80, 0xFF, 0x00, 0x0A])));
    }

    // Once bodyLength is set the body is taken purely by count: the separator search is gated on
    // `if bodyLength == nil {` (StdioFramer.swift:35-36) and the body is `buffer[start..<start + length]` (:47-49).
    // Each body below is made of separator bytes or header text; a port that searches for "\r\n\r\n" or re-parses
    // headers before checking a pending body splits it wrongly. Header "Content-Length: 4\r\n\r\n" is bytes 0-20,
    // the body 21-24, the next header 25-45 and "Z" byte 46; header "Content-Length: 19\r\n\r\n" is bytes 0-21, the
    // body 22-40, the next header 41-61 and "Z" byte 62.
    [Theory]
    [Trait("Req", "PR-PROVACP-151")] // StdioFramer.swift:35-36, :47-53
    [Trait("Req", "PR-PROVACP-150")] // StdioFramer.swift:19-23, :58-59 (split across any number of chunks)
    [InlineData("Content-Length: 4\r\n\r\n", "\r\n\r\n", 24)]
    [InlineData("Content-Length: 19\r\n\r\n", "Content-Length: 9\r\n", 40)]
    public void A_pending_body_is_taken_by_count_even_when_it_holds_separators_or_header_text(
        string header, string body, int lastBodyByte)
    {
        var wire = Bytes(header + body + "Content-Length: 1\r\n\r\nZ");
        Assert.Equal(lastBodyByte + 22, wire.Length - 1);

        // One append.
        var framer = new StdioFramer(StdioFramer.Framing.ContentLength);
        var messages = framer.Append(wire);
        Assert.Equal(2, messages.Count);
        Assert.Equal(Bytes(body), messages[0]);
        Assert.Equal(Bytes("Z"), messages[1]);
        Assert.Null(framer.Finish());

        // One byte at a time: the body is emitted by the append of its last byte, "Z" by the append of the last byte.
        framer = new StdioFramer(StdioFramer.Framing.ContentLength);
        var collected = new List<byte[]>();
        for (var i = 0; i < wire.Length; i++)
        {
            var emitted = framer.Append(wire.AsSpan(i, 1));
            if (i == lastBodyByte) Assert.Equal(Bytes(body), Assert.Single(emitted));
            else if (i == wire.Length - 1) Assert.Equal(Bytes("Z"), Assert.Single(emitted));
            else Assert.Empty(emitted);
            collected.AddRange(emitted);
        }
        Assert.Equal(2, collected.Count);
        Assert.Null(framer.Finish());

        // Every two-chunk split.
        for (var split = 0; split <= wire.Length; split++)
        {
            framer = new StdioFramer(StdioFramer.Framing.ContentLength);
            var parts = framer.Append(wire.AsSpan(0, split)).Concat(framer.Append(wire.AsSpan(split))).ToArray();
            Assert.Equal(2, parts.Length);
            Assert.Equal(Bytes(body), parts[0]);
            Assert.Equal(Bytes("Z"), parts[1]);
            Assert.Null(framer.Finish());
        }
    }

    [Theory]
    [Trait("Req", "PR-PROVACP-151")] // StdioFramer.swift:13, :37-39, :64
    [InlineData("Content-Length: 1\n\nA")]
    [InlineData("Content-Length: 1\r\rA")]
    [InlineData("Content-Length: 1\r\n\nA")]
    [InlineData("Content-Length: 1\n\r\n\rA")]
    public void Only_the_literal_CRLFCRLF_ends_a_header(string wire)
    {
        var framer = new StdioFramer(StdioFramer.Framing.ContentLength);
        Assert.Empty(framer.Append(Bytes(wire)));
        Assert.Null(framer.Finish());
    }

    [Theory]
    [Trait("Req", "PR-PROVACP-151")] // StdioFramer.swift:42-44
    [Trait("Req", "PR-PROVACP-152")] // StdioFramer.swift:72-79; the single split and its count check: :73-76
    [InlineData("Ignored: yes")]
    [InlineData("")]
    [InlineData("Content-Length: -1")]
    [InlineData("Content-Length: garbage")]
    [InlineData("Content-Length: 9223372036854775808")]
    [InlineData("Content-Length: 99999999999999999999")]
    [InlineData("Content-Length: 1.0")]
    [InlineData("Content-Length: 1e0")]
    [InlineData("Content-Length: 0x1")]
    [InlineData("Content-Length: 1_0")]
    [InlineData("Content-Length: 1\0")]
    [InlineData("Content-Length: \U00000661")] // U+0661 ARABIC-INDIC DIGIT ONE
    [InlineData("Content-Length: \U0000FF11")] // U+FF11 FULLWIDTH DIGIT ONE
    [InlineData("Content-Length: + 1")]
    [InlineData("Content-Length: ++1")]
    [InlineData("Content-Length: -")]
    [InlineData("Content-Length: 1:2")]
    // StdioFramer.swift:73-76: `split(separator: ":", maxSplits: 1)` gives ["Content-Length", " 1:2"], so the count
    // check passes, Int("1:2") is nil and `else { return nil }` ends the whole block; the second line is never read.
    [InlineData("Content-Length: 1:2\r\nContent-Length: 1")]
    // StdioFramer.swift:73-76: one split gives ["Content-Length", ":1"]; Int(":1") is nil, so the block is nil.
    [InlineData("Content-Length::1")]
    [InlineData("Content-Length: bad\r\nContent-Length: 1")]
    [InlineData("Content-Length: \r\nContent-Length: 1")]
    [InlineData("Content-Length: \t\r\nContent-Length: 1")]
    public void Malformed_header_is_discarded_as_a_whole_then_next_header_is_read(string header)
    {
        var framer = new StdioFramer(StdioFramer.Framing.ContentLength);
        var messages = framer.Append(Bytes(header + "\r\n\r\nContent-Length: 1\r\n\r\nZ"));
        Assert.Equal(Bytes("Z"), Assert.Single(messages));
        Assert.Null(framer.Finish());
    }

    [Theory]
    [Trait("Req", "PR-PROVACP-152")] // StdioFramer.swift:72-77
    [InlineData("content-length:1")]
    [InlineData("CONTENT-LENGTH: 1")]
    [InlineData("cOnTeNt-LeNgTh: 1")]
    [InlineData(" \tContent-Length \t: \t+1 \t")]
    [InlineData("\U000000A0Content-Length\U00002003: \U000030001\U0000202F")] // U+00A0 NO-BREAK SPACE, U+2003 EM SPACE, U+3000 IDEOGRAPHIC SPACE, U+202F NARROW NO-BREAK SPACE
    [InlineData("Content-Length: 0001")]
    [InlineData("Content-Length: +0001")]
    [InlineData("Ignored: anything\r\nContent-Length: 1")]
    [InlineData("Ignored: anything\nContent-Length: 1")]
    [InlineData("Ignored: anything\rContent-Length: 1")]
    [InlineData("Ignored: anything\vContent-Length: 1")]
    [InlineData("Ignored: anything\fContent-Length: 1")]
    [InlineData("Ignored: anything\U00000085Content-Length: 1")] // U+0085 NEXT LINE (NEL)
    [InlineData("Ignored: anything\U00002028Content-Length: 1")] // U+2028 LINE SEPARATOR
    [InlineData("Ignored: anything\U00002029Content-Length: 1")] // U+2029 PARAGRAPH SEPARATOR
    [InlineData("\r\n\nContent-Length: 1")]
    [InlineData("No colon here\r\nContent-Length: 1")]
    [InlineData("Content-Length: 1\r\nContent-Length: invalid")]
    [InlineData("Content-Length: 1\r\nContent-Length: 5")]
    [InlineData(":Content-Length: 1")]
    [InlineData("::Content-Length: 1")]
    [InlineData("Content-Length:\r\nContent-Length: 1")]
    [InlineData("Content-Length:\r\nContent-Length: 1\r\nContent-Length: 5")]
    public void Swift_header_name_trimming_integer_and_first_matching_line_rules_are_preserved(string header)
    {
        var framer = new StdioFramer(StdioFramer.Framing.ContentLength);
        Assert.Equal(Bytes("Z"), Assert.Single(framer.Append(Bytes(header + "\r\n\r\nZ"))));
        Assert.Null(framer.Finish());
    }

    [Theory]
    [Trait("Req", "PR-PROVACP-152")] // StdioFramer.swift:76-77, :47-49
    [InlineData("0")]
    [InlineData("+0")]
    [InlineData("-0")]
    [InlineData("000")]
    public void Zero_length_is_a_complete_empty_message(string length)
    {
        var framer = new StdioFramer(StdioFramer.Framing.ContentLength);
        Assert.Empty(Assert.Single(framer.Append(Bytes($"Content-Length: {length}\r\n\r\n"))));
        Assert.Null(framer.Finish());
    }

    [Theory]
    [Trait("Req", "PR-PROVACP-152")] // StdioFramer.swift:76 (Int is 64-bit)
    [Trait("Req", "PR-PROVACP-151")] // StdioFramer.swift:45-47
    [InlineData("2147483648")]
    [InlineData("9223372036854775807")]
    public void A_valid_64_bit_length_waits_for_its_body_instead_of_being_rejected_or_allocated_up_front(string length)
    {
        var framer = new StdioFramer(StdioFramer.Framing.ContentLength);
        Assert.Empty(framer.Append(Bytes($"Content-Length: {length}\r\n\r\nContent-Length: 1\r\n\r\nZ")));
        Assert.Null(framer.Finish());
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-151")] // StdioFramer.swift:42-44
    public void Invalid_header_does_not_consume_a_body_and_garbage_becomes_part_of_the_next_header()
    {
        var framer = new StdioFramer(StdioFramer.Framing.ContentLength);
        var wire = "Content-Length: bad\r\n\r\nXContent-Length: 1\r\n\r\nContent-Length: 1\r\n\r\nZ";
        Assert.Equal(Bytes("Z"), Assert.Single(framer.Append(Bytes(wire))));
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-151")] // StdioFramer.swift:42-44, :79
    public void A_skipped_block_leaves_its_intended_body_to_start_the_next_header_and_the_next_message_is_lost()
    {
        var framer = new StdioFramer(StdioFramer.Framing.ContentLength);
        // Block 1 has no Content-Length line; block 2 is "{}Content-Length: 1", whose name is "{}Content-Length".
        Assert.Empty(framer.Append(Bytes("Ignored: yes\r\n\r\n{}Content-Length: 1\r\n\r\nZ")));
        // "Z" is not a body: it starts the next header block, which has no Content-Length line either.
        Assert.Empty(framer.Append(Bytes("\r\n\r\n")));
        Assert.Null(framer.Finish());
    }

    [Theory]
    [Trait("Req", "PR-PROVACP-151")] // StdioFramer.swift:42-44
    [Trait("Req", "PR-PROVACP-152")] // StdioFramer.swift:72-77
    [InlineData("Ignored: yes\r\n\r\n{\"a\":1}\nContent-Length: 1\r\n\r\nZ")]
    [InlineData("Content-Length: nope\r\n\r\nbody text\r\nContent-Length: 1\r\n\r\nZ")]
    public void Intended_body_lines_are_read_as_header_lines_of_the_next_block(string wire)
    {
        // The skipped body joins the next header block: its line `{"a":1}` splits into the name `{"a"`, which does
        // not match, and the next line "Content-Length: 1" decides the block.
        var framer = new StdioFramer(StdioFramer.Framing.ContentLength);
        Assert.Equal(Bytes("Z"), Assert.Single(framer.Append(Bytes(wire))));
        Assert.Null(framer.Finish());
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-151")] // StdioFramer.swift:41 String(decoding:as: UTF8.self) repairs to U+FFFD
    [Trait("Req", "PR-PROVACP-152")] // StdioFramer.swift:75-76
    public void Invalid_UTF8_in_a_header_is_repaired_not_dropped_or_thrown()
    {
        // 0xFF before the name: the name becomes U+FFFD "Content-Length", so the line is skipped and the next decides.
        var framer = new StdioFramer(StdioFramer.Framing.ContentLength);
        var wire = new byte[] { 0xFF }.Concat(Bytes("Content-Length: 2\r\nContent-Length: 1\r\n\r\nZ")).ToArray();
        Assert.Equal(Bytes("Z"), Assert.Single(framer.Append(wire)));
        Assert.Null(framer.Finish());

        // A truncated sequence after the value: "1" plus U+FFFD is not an Int, so the whole block is nil.
        framer = new StdioFramer(StdioFramer.Framing.ContentLength);
        wire = Bytes("Content-Length: 1").Concat(new byte[] { 0xE2, 0x80 })
            .Concat(Bytes("\r\n\r\nContent-Length: 1\r\n\r\nZ")).ToArray();
        Assert.Equal(Bytes("Z"), Assert.Single(framer.Append(wire)));
        Assert.Null(framer.Finish());
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-153")] // StdioFramer.swift:63-69
    public void Finish_flushes_only_nonempty_line_remainder_and_resets_scanning()
    {
        var framer = new StdioFramer(StdioFramer.Framing.Lines);
        Assert.Null(framer.Finish());
        Assert.Empty(framer.Append(Bytes("old\r")));
        Assert.Equal(Bytes("old\r"), framer.Finish());
        Assert.Equal(0, Scanned(framer));
        Assert.Null(framer.Finish());
        Assert.Equal(Bytes("new"), Assert.Single(framer.Append(Bytes("new\n"))));
        Assert.Null(framer.Finish());
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-153")] // StdioFramer.swift:64
    public void Finish_returns_a_final_json_line_written_without_a_newline()
    {
        var framer = new StdioFramer(StdioFramer.Framing.Lines);
        Assert.Equal(Bytes("{\"a\":1}"), Assert.Single(framer.Append(Bytes("{\"a\":1}\n{\"b\":"))));
        Assert.Empty(framer.Append(Bytes("2}")));
        Assert.Equal(Bytes("{\"b\":2}"), framer.Finish());
        Assert.Null(framer.Finish());
    }

    [Theory]
    [Trait("Req", "PR-PROVACP-153")] // StdioFramer.swift:63-69
    [InlineData("Content-Len")]
    [InlineData("Content-Length: 8\r\n\r\nshort")]
    [InlineData("Content-Length: 8\r\n\r\n")]
    public void Finish_discards_partial_header_or_body_and_resets_all_content_length_state(string partial)
    {
        var framer = new StdioFramer(StdioFramer.Framing.ContentLength);
        Assert.Empty(framer.Append(Bytes(partial)));
        Assert.Null(framer.Finish());
        Assert.Equal(0, Scanned(framer));
        Assert.Null(framer.Finish());
        Assert.Equal(Bytes("Z"), Assert.Single(framer.Append(Bytes("Content-Length: 1\r\n\r\nZ"))));
    }

    [Fact]
    [Trait("Req", "PR-PROVACP-149")] // StdioFramer.swift:31 Data(buffer[start..<newline]) is a copy
    [Trait("Req", "PR-PROVACP-150")] // StdioFramer.swift:20, :59
    [Trait("Req", "PR-PROVACP-153")] // StdioFramer.swift:64-65
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

    // Swift's separator is a Character, not a UTF-16 code unit: ":" followed by an Extend scalar or ZWJ (UAX29 GB9)
    // or a SpacingMark (GB9a) is one Character, so the line has no ":" separator and is skipped, and the next line
    // decides. The supplementary-plane Extend case guards against a port that inspects only the next UTF-16 unit.
    [Theory]
    [Trait("Req", "PR-PROVACP-152")] // StdioFramer.swift:73-74
    [InlineData("\U00000301")] // U+0301 COMBINING ACUTE ACCENT
    [InlineData("\U0000200D")] // U+200D ZERO WIDTH JOINER
    [InlineData("\U00000903")] // U+0903 DEVANAGARI SIGN VISARGA (Mc, SpacingMark, GB9a)
    [InlineData("\U000E0100")] // U+E0100 VARIATION SELECTOR-17 (Mn, Extend, GB9, supplementary plane)
    public void A_colon_joined_to_the_next_scalar_is_not_a_colon_separator(string joiner)
    {
        var framer = new StdioFramer(StdioFramer.Framing.ContentLength);
        Assert.Equal(Bytes("Z"), Assert.Single(framer.Append(
            Bytes("Content-Length:" + joiner + "7\r\nContent-Length: 1\r\n\r\nZ"))));
        Assert.Null(framer.Finish());
    }
}
