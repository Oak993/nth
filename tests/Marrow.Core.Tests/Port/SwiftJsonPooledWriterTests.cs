using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Marrow.Core.Port;
using Marrow.Core.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Core.Tests.Port;

// ED-NEW-perf-memory-5: SwiftJson writes straight to pooled UTF-8 chunks and copies string values from the document's own bytes. The bytes must be
// the ones the old StringBuilder, string and UTF8Encoding route wrote (frozen in Fixtures/SwiftJsonReference), in every style, and a save must
// allocate little more than the finished array.
public sealed class SwiftJsonPooledWriterTests
{
    public static TheoryData<string> StyleNames => new() { "Storage", "PlainEncoder", "PrettySorted", "SerializationCompact", "SerializationPretty" };

    private static SwiftJsonStyle Style(string name) => name switch
    {
        "Storage" => SwiftJsonStyle.Storage,
        "PlainEncoder" => SwiftJsonStyle.PlainEncoder,
        "PrettySorted" => SwiftJsonStyle.PrettySorted,
        "SerializationCompact" => SwiftJsonStyle.SerializationCompact,
        _ => SwiftJsonStyle.SerializationPretty,
    };

    private static JsonElement Parse(string json) => JsonDocument.Parse(json, SwiftJson.DocumentOptions).RootElement.Clone();

    private static void AssertSame(JsonElement element, SwiftJsonStyle style) =>
        Assert.True(SwiftJsonReference.Write(element, style).AsSpan().SequenceEqual(SwiftJson.Write(element, style)),
            "pooled writer differs from the frozen reference for " + element.GetRawText()[..Math.Min(80, element.GetRawText().Length)]);

    // Text with every kind of character the writer treats differently: controls, quotes, backslashes, slashes, DEL, Latin, CJK,
    // separators, a byte-order mark, private use, and supplementary scalars (never a lone surrogate: neither writer accepts one in a document).
    private static string RandomText(Random random, int length)
    {
        var text = new StringBuilder();
        while (text.Length < length)
        {
            switch (random.Next(9))
            {
                case 0: text.Append((char)random.Next(0, 0x20)); break;
                case 1: text.Append("\"\\/"[random.Next(3)]); break;
                case 2: text.Append((char)random.Next(0x20, 0x7F)); break;
                case 3: text.Append((char)random.Next(0x7F, 0x800)); break;
                case 4: text.Append((char)random.Next(0x800, 0xD800)); break;
                case 5: text.Append((char)random.Next(0xE000, 0x10000)); break;
                case 6: text.Append(char.ConvertFromUtf32(random.Next(0x10000, 0x110000))); break;
                case 7: text.Append("\u2028\uFEFF\u007F"[random.Next(3)]); break;
                default: text.Append('a', random.Next(1, 40)); break;
            }
        }
        return text.ToString();
    }

    // The same string as a document element, written by a writer that escapes non-ASCII (\uXXXX) or one that keeps it raw.
    private static JsonElement StringElement(string value, bool relaxed)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Encoder = relaxed ? JavaScriptEncoder.UnsafeRelaxedJsonEscaping : JavaScriptEncoder.Default }))
            writer.WriteStringValue(value);
        return JsonDocument.Parse(stream.ToArray()).RootElement.Clone();
    }

    [Theory]
    [MemberData(nameof(StyleNames))]
    [Trait("Rule", "R-CODE-3")]
    [Trait("Decision", "ED-072")]
    public void Random_strings_match_the_reference_whether_the_document_escaped_them_or_not(string styleName)
    {
        var random = new Random(20261013);
        var style = Style(styleName);
        for (var round = 0; round < 400; round++)
        {
            var text = RandomText(random, random.Next(1, 300));
            AssertSame(StringElement(text, relaxed: false), style);
            AssertSame(StringElement(text, relaxed: true), style);
        }
    }

    [Theory]
    [MemberData(nameof(StyleNames))]
    [Trait("Rule", "R-CODE-3")]
    [Trait("Decision", "ED-072")]
    public void Escapes_written_in_the_JSON_text_decode_to_the_same_bytes(string styleName)
    {
        var style = Style(styleName);
        foreach (var json in new[]
        {
            "\"\\u00e9\\u00E9\\ud83d\\ude00\\uD83D\\uDE00\\/\\\\\\\"\\b\\f\\n\\r\\t\\u0000\\u001f\\u007f\\u2028\\ufeff\\u0041\"",
            "\"a\\/b/c\"",
            "\"\"",
            "\"\\u0022\\u005c\\u002f\"",
            "{\"k\\u00e9y\":\"v\",\"\\ud83d\\ude00\":1,\"a\\/b\":[\"\\u0000\"]}",
        })
            AssertSame(Parse(json), style);
    }

    [Theory]
    [MemberData(nameof(StyleNames))]
    [Trait("Rule", "R-CODE-8")]
    [Trait("Oracle", "O-09")]
    public void Objects_match_the_reference_with_duplicate_keys_many_members_and_every_value_kind(string styleName)
    {
        var style = Style(styleName);
        var many = string.Join(",", Enumerable.Range(0, 40).Select(i => "\"key" + (i * 7 % 40) + "\":" + i)); // 40 members, out of order
        var manyWithRepeat = many + ",\"key3\":\"again\",\"key39\":null";
        foreach (var json in new[]
        {
            "{}", "[]", "{\"a\":{},\"b\":[],\"c\":[{}],\"d\":[[]]}",
            "{\"a\":1,\"b\":2,\"a\":3}", "{\"b\":1,\"a\":2,\"b\":3,\"a\":4}",
            "{\"z\":1,\"Z\":2,\"a\":3,\"\u00e9\":4,\"\U0001F600\":5,\"\uFFFD\":6,\"\uE000\":7}",
            "{" + many + "}", "{" + manyWithRepeat + "}",
            "{\"t\":true,\"f\":false,\"n\":null,\"s\":\"x/y\",\"i\":-12,\"big\":12345678901234567890}",
            "[1,1.5,-0,-0.0,1e5,1E-05,0.00001,1.0,2.5e-7,123456789.123456789,\"\",null]",
            "{\"a\":[{\"b\":{\"c\":[1,{\"d\":\"e\"}]}}]}",
        })
            AssertSame(Parse(json), style);
    }

    [Fact]
    [Trait("Rule", "R-CODE-3")]
    [Trait("Decision", "ED-072")]
    public void A_string_longer_than_one_chunk_is_written_whole()
    {
        var text = new string('x', 200_000) + "\"/\\\U0001F600" + new string('y', 70_000);
        var element = StringElement(text, relaxed: false);
        AssertSame(element, SwiftJsonStyle.Storage);
        var expected = text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal).Replace("/", "\\/", StringComparison.Ordinal);
        Assert.Equal(expected, Encoding.UTF8.GetString(SwiftJson.Write(element, SwiftJsonStyle.Storage))[1..^1]);
    }

    private static JsonElement ThreadLikeDocument()
    {
        var random = new Random(7);
        var entries = Enumerable.Range(0, 1500).Select(index => new Dictionary<string, object?>
        {
            ["id"] = Guid.NewGuid().ToString(),
            ["kind"] = "assistant",
            ["text"] = RandomText(random, 2000),
            ["tokens"] = random.NextDouble() * 1000,
            ["tags"] = new[] { "a", "b" },
        });
        return JsonSerializer.SerializeToElement(new { entries });
    }

    [Fact]
    [Trait("Rule", "R-TEST-5")]
    [Trait("Decision", "ED-072")]
    public void A_save_allocates_little_more_than_the_finished_array_where_the_old_writer_allocated_several_times_it()
    {
        var element = ThreadLikeDocument();
        var size = SwiftJson.Write(element, SwiftJsonStyle.Storage).Length;

        // Two warm-up writes: the pool of rented buffers needs a second pass to hold every size the writer uses.
        var pooled = AllocationProbe.Measure(() => { SwiftJson.Write(element, SwiftJsonStyle.Storage); SwiftJson.Write(element, SwiftJsonStyle.Storage); }, () => SwiftJson.Write(element, SwiftJsonStyle.Storage));
        var reference = AllocationProbe.Measure(() => SwiftJsonReference.Write(element, SwiftJsonStyle.Storage), () => SwiftJsonReference.Write(element, SwiftJsonStyle.Storage));

        Assert.True(size > 3_000_000, "the probe document is a few megabytes");
        Assert.True(pooled < size * 1.3, $"pooled writer allocated {pooled} bytes for {size} bytes of output");
        Assert.True(reference > size * 4, $"control: the frozen reference allocated {reference} bytes for {size} bytes of output");
    }

    [Fact]
    [Trait("Rule", "R-TEST-5")]
    [Trait("Decision", "ED-072")]
    public void Strings_without_escapes_make_no_allocation_beyond_the_output_per_value()
    {
        var element = Parse("[" + string.Join(",", Enumerable.Range(0, 4000).Select(i => "\"plain text value number " + i + " \u00e9\U0001F600\"")) + "]");

        var allocated = AllocationProbe.Measure(() => { SwiftJson.Write(element, SwiftJsonStyle.Storage); SwiftJson.Write(element, SwiftJsonStyle.Storage); }, () => SwiftJson.Write(element, SwiftJsonStyle.Storage));

        var size = SwiftJson.Write(element, SwiftJsonStyle.Storage).Length;
        Assert.True(allocated < size + 4096, $"allocated {allocated} bytes for {size} bytes of output (no string per value)");
    }
}
